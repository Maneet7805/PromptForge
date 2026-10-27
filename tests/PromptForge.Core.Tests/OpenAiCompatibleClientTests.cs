using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PromptForge.Core.Llm;

namespace PromptForge.Core.Tests;

public class OpenAiCompatibleClientTests
{
    private static readonly LlmEndpoint Endpoint = new(ProviderPresets.Groq, "https://example.test/v1/", "test-model", " key-123 ");
    private static readonly IReadOnlyList<ChatMessage> Messages = [new("system", "rules"), new("user", "hello")];

    private sealed class FakeHandler(Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request, Body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (OpenAiCompatibleClient Client, FakeHandler Handler) ClientReturning(HttpResponseMessage response)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(response));
        return (new OpenAiCompatibleClient(new HttpClient(handler)), handler);
    }

    private const string GoodReply = """
        {"model":"served-model","choices":[{"message":{"content":"{\"ok\":true}"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":11,"completion_tokens":7,"prompt_tokens_details":{"cached_tokens":5}}}
        """;

    [Fact]
    public async Task Complete_parses_content_usage_and_model()
    {
        var (client, _) = ClientReturning(Json(GoodReply));
        var r = await client.CompleteAsync(Endpoint, Messages, new ChatOptions(), CancellationToken.None);
        Assert.Equal("{\"ok\":true}", r.Content);
        Assert.Equal("stop", r.FinishReason);
        Assert.Equal(11, r.PromptTokens);
        Assert.Equal(7, r.CompletionTokens);
        Assert.Equal(5, r.CachedTokens);
        Assert.Equal("served-model", r.Model);
    }

    [Fact]
    public async Task Complete_sends_trimmed_bearer_key_and_url_without_double_slash()
    {
        var (client, handler) = ClientReturning(Json(GoodReply));
        await client.CompleteAsync(Endpoint, Messages, new ChatOptions(), CancellationToken.None);
        Assert.Equal("https://example.test/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("key-123", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Complete_omits_authorization_when_there_is_no_key()
    {
        var (client, handler) = ClientReturning(Json(GoodReply));
        await client.CompleteAsync(Endpoint with { ApiKey = "  " }, Messages, new ChatOptions(), CancellationToken.None);
        Assert.Null(handler.Request!.Headers.Authorization);
    }

    [Fact]
    public async Task Complete_request_body_has_model_messages_and_json_mode()
    {
        var (client, handler) = ClientReturning(Json(GoodReply));
        await client.CompleteAsync(Endpoint, Messages, new ChatOptions { MaxTokens = 321, Temperature = 0.1 }, CancellationToken.None);
        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("test-model", (string?)body["model"]);
        Assert.Equal(321, (int?)body["max_tokens"]);
        Assert.False((bool?)body["stream"]);
        Assert.Equal(2, body["messages"]!.AsArray().Count);
        Assert.Equal("system", (string?)body["messages"]![0]!["role"]);
        Assert.Equal("hello", (string?)body["messages"]![1]!["content"]);
        Assert.Equal("json_object", (string?)body["response_format"]!["type"]);
    }

    [Fact]
    public async Task Complete_leaves_out_json_mode_when_not_requested()
    {
        var (client, handler) = ClientReturning(Json(GoodReply));
        await client.CompleteAsync(Endpoint, Messages, new ChatOptions { JsonMode = false }, CancellationToken.None);
        Assert.Null(JsonNode.Parse(handler.Body!)!["response_format"]);
    }

    [Fact]
    public async Task Complete_leaves_out_json_mode_when_the_provider_does_not_support_it()
    {
        var (client, handler) = ClientReturning(Json(GoodReply));
        var endpoint = Endpoint with { Preset = ProviderPresets.Groq with { SupportsJsonMode = false } };
        await client.CompleteAsync(endpoint, Messages, new ChatOptions(), CancellationToken.None);
        Assert.Null(JsonNode.Parse(handler.Body!)!["response_format"]);
    }

    [Fact]
    public async Task Complete_without_usage_or_model_falls_back_to_zero_and_the_requested_model()
    {
        var (client, _) = ClientReturning(Json("""{"choices":[{"message":{"content":"hi"},"finish_reason":"stop"}]}"""));
        var r = await client.CompleteAsync(Endpoint, Messages, new ChatOptions(), CancellationToken.None);
        Assert.Equal(0, r.PromptTokens);
        Assert.Equal(0, r.CompletionTokens);
        Assert.Equal(0, r.CachedTokens);
        Assert.Equal("test-model", r.Model);
    }

    [Fact]
    public async Task Complete_with_null_content_returns_empty_string()
    {
        var (client, _) = ClientReturning(Json("""{"choices":[{"message":{"content":null},"finish_reason":"stop"}]}"""));
        var r = await client.CompleteAsync(Endpoint, Messages, new ChatOptions(), CancellationToken.None);
        Assert.Equal("", r.Content);
    }

    [Fact]
    public async Task Complete_finish_reason_length_is_a_truncation_error()
    {
        var (client, _) = ClientReturning(Json("""{"choices":[{"message":{"content":"par"},"finish_reason":"length"}]}"""));
        var ex = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Endpoint, Messages, new ChatOptions(), CancellationToken.None));
        Assert.Equal(LlmErrorKind.Truncated, ex.Kind);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"choices":[]}""")]
    public async Task Complete_malformed_success_body_is_a_server_error(string body)
    {
        var (client, _) = ClientReturning(Json(body));
        var ex = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Endpoint, Messages, new ChatOptions(), CancellationToken.None));
        Assert.Equal(LlmErrorKind.Server, ex.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LlmErrorKind.Auth)]
    [InlineData(HttpStatusCode.TooManyRequests, LlmErrorKind.RateLimit)]
    [InlineData(HttpStatusCode.InternalServerError, LlmErrorKind.Server)]
    public async Task Complete_http_errors_are_mapped(HttpStatusCode status, LlmErrorKind kind)
    {
        var (client, _) = ClientReturning(Json("""{"error":{"message":"nope"}}""", status));
        var ex = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Endpoint, Messages, new ChatOptions(), CancellationToken.None));
        Assert.Equal(kind, ex.Kind);
        Assert.Equal("nope", ex.Message);
    }

    [Fact]
    public async Task Complete_network_failure_is_a_network_error()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("offline"));
        var client = new OpenAiCompatibleClient(new HttpClient(handler));
        var ex = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Endpoint, Messages, new ChatOptions(), CancellationToken.None));
        Assert.Equal(LlmErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task Complete_that_exceeds_its_timeout_is_a_timeout_error()
    {
        var honouring = new TokenHonouringHandler();
        var client = new OpenAiCompatibleClient(new HttpClient(honouring));
        var ex = await Assert.ThrowsAsync<LlmException>(() =>
            client.CompleteAsync(Endpoint, Messages, new ChatOptions { Timeout = TimeSpan.FromMilliseconds(50) }, CancellationToken.None));
        Assert.Equal(LlmErrorKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task Complete_cancelled_by_the_caller_is_a_cancelled_error()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = new OpenAiCompatibleClient(new HttpClient(new TokenHonouringHandler()));
        var ex = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(Endpoint, Messages, new ChatOptions(), cts.Token));
        Assert.Equal(LlmErrorKind.Cancelled, ex.Kind);
    }

    [Fact]
    public async Task Transcribe_returns_trimmed_text_and_posts_to_the_transcription_url()
    {
        var (client, handler) = ClientReturning(Json("""{"text":"  hello world \n"}"""));
        var text = await client.TranscribeAsync(Endpoint, [1, 2, 3], "a.wav", "en", TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal("hello world", text);
        Assert.Equal("https://example.test/v1/audio/transcriptions", handler.Request!.RequestUri!.ToString());
        Assert.Contains("name=language", handler.Body!);
        Assert.Contains("test-model", handler.Body!);
    }

    [Fact]
    public async Task Transcribe_asks_for_the_verbose_format_and_drops_text_the_model_thinks_is_not_speech()
    {
        var (client, handler) = ClientReturning(Json("""{"text":" Thank you.","segments":[{"text":" Thank you.","no_speech_prob":0.9,"avg_logprob":-1.3}]}"""));

        var text = await client.TranscribeAsync(Endpoint, [1], "a.wav", null, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal("", text);
        Assert.Contains("verbose_json", handler.Body!);
    }

    [Fact]
    public async Task Transcribe_falls_back_to_plain_json_when_the_provider_rejects_the_verbose_format()
    {
        var bodies = new List<string>();
        var handler = new FakeHandler((_, body) =>
        {
            bodies.Add(body ?? "");
            return Task.FromResult(bodies.Count == 1
                ? Json("""{"error":{"message":"response_format verbose_json is not supported for this model"}}""", HttpStatusCode.BadRequest)
                : Json("""{"text":"hello"}"""));
        });
        var client = new OpenAiCompatibleClient(new HttpClient(handler));

        var text = await client.TranscribeAsync(Endpoint, [1], "a.wav", null, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal("hello", text);
        Assert.Equal(2, bodies.Count);
        Assert.Contains("verbose_json", bodies[0]);
        Assert.DoesNotContain("verbose_json", bodies[1]);
    }

    [Fact]
    public async Task Transcribe_without_a_language_does_not_send_one()
    {
        var (client, handler) = ClientReturning(Json("""{"text":"hi"}"""));
        await client.TranscribeAsync(Endpoint, [1], "a.wav", "  ", TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.DoesNotContain("name=language", handler.Body!);
    }

    [Fact]
    public async Task Transcribe_missing_text_field_gives_empty_string()
    {
        var (client, _) = ClientReturning(Json("{}"));
        Assert.Equal("", await client.TranscribeAsync(Endpoint, [1], "a.wav", null, TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task Transcribe_invalid_json_is_a_server_error()
    {
        var (client, _) = ClientReturning(Json("<html>"));
        var ex = await Assert.ThrowsAsync<LlmException>(() => client.TranscribeAsync(Endpoint, [1], "a.wav", null, TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.Equal(LlmErrorKind.Server, ex.Kind);
    }

    [Fact]
    public async Task Transcribe_http_error_is_mapped()
    {
        var (client, _) = ClientReturning(Json("""{"error":{"message":"bad key"}}""", HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<LlmException>(() => client.TranscribeAsync(Endpoint, [1], "a.wav", null, TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.Equal(LlmErrorKind.Auth, ex.Kind);
    }

    /// <summary>Waits until its token is cancelled, like a real HttpClient handler does.</summary>
    private sealed class TokenHonouringHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
