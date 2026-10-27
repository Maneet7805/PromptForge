using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PromptForge.Core.Optimization;
using PromptForge.Core.Voice;

namespace PromptForge.Core.Llm;

public sealed record LlmEndpoint(ProviderPreset Preset, string BaseUrl, string Model, string? ApiKey)
{
    public string Label => $"{Model} ({Preset.Id})";
}

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatOptions
{
    public OptimizeMode Mode { get; init; } = OptimizeMode.Standard;
    public int MaxTokens { get; init; } = 2048;
    public bool JsonMode { get; init; } = true;
    public double Temperature { get; init; } = 0.3;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);
}

public sealed record ChatResponse(string Content, string? FinishReason, int PromptTokens, int CompletionTokens, int CachedTokens, string Model);

public interface ILlmClient
{
    Task<ChatResponse> CompleteAsync(LlmEndpoint endpoint, IReadOnlyList<ChatMessage> messages, ChatOptions options, CancellationToken ct);

    Task<string> TranscribeAsync(LlmEndpoint endpoint, byte[] audio, string fileName, string? language, TimeSpan timeout, CancellationToken ct);
}

/// <summary>Minimal client for OpenAI-compatible chat and transcription endpoints.</summary>
public sealed partial class OpenAiCompatibleClient(HttpClient http) : ILlmClient
{
    public async Task<ChatResponse> CompleteAsync(LlmEndpoint endpoint, IReadOnlyList<ChatMessage> messages, ChatOptions options, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = endpoint.Model,
            ["messages"] = new JsonArray(messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }).ToArray()),
            ["temperature"] = options.Temperature,
            ["max_tokens"] = options.MaxTokens,
            ["stream"] = false,
        };
        if (options.JsonMode && endpoint.Preset.SupportsJsonMode)
            body["response_format"] = new JsonObject { ["type"] = "json_object" };
        endpoint.Preset.ApplyExtras?.Invoke(body, options.Mode);

        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(endpoint.BaseUrl, "chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        AddAuth(request, endpoint);

        using var response = await SendAsync(request, options.Timeout, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw MapError(response, text);

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var choice = root.GetProperty("choices")[0];
            var content = choice.GetProperty("message").TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";
            var finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            if (finish == "length")
                throw new LlmException(LlmErrorKind.Truncated, "The reply was cut off by the output token limit.");

            int promptTokens = 0, completionTokens = 0, cached = 0;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                promptTokens = GetInt(usage, "prompt_tokens");
                completionTokens = GetInt(usage, "completion_tokens");
                if (usage.TryGetProperty("prompt_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object)
                    cached = GetInt(details, "cached_tokens");
            }
            var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : endpoint.Model;
            return new ChatResponse(content, finish, promptTokens, completionTokens, cached, model);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new LlmException(LlmErrorKind.Server, "The provider returned a response in an unexpected format.", inner: ex);
        }
    }

    public async Task<string> TranscribeAsync(LlmEndpoint endpoint, byte[] audio, string fileName, string? language, TimeSpan timeout, CancellationToken ct)
    {
        // verbose_json carries the model's confidence per segment, which TranscriptFilter uses to drop invented text.
        // A provider that rejects it gets the plain json format, as before.
        try
        {
            return await RequestTranscriptionAsync(endpoint, audio, fileName, language, timeout, "verbose_json", ct).ConfigureAwait(false);
        }
        catch (LlmException ex) when (ex.Kind == LlmErrorKind.JsonModeUnsupported)
        {
            return await RequestTranscriptionAsync(endpoint, audio, fileName, language, timeout, "json", ct).ConfigureAwait(false);
        }
    }

    private async Task<string> RequestTranscriptionAsync(LlmEndpoint endpoint, byte[] audio, string fileName, string? language, TimeSpan timeout, string responseFormat, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(endpoint.Model), "model");
        form.Add(new StringContent(responseFormat), "response_format");
        form.Add(new StringContent("0"), "temperature");
        if (!string.IsNullOrWhiteSpace(language)) form.Add(new StringContent(language.Trim()), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(endpoint.BaseUrl, "audio/transcriptions")) { Content = form };
        AddAuth(request, endpoint);

        using var response = await SendAsync(request, timeout, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw MapError(response, text);
        try
        {
            using var doc = JsonDocument.Parse(text);
            return TranscriptFilter.Read(doc.RootElement);
        }
        catch (JsonException ex)
        {
            throw new LlmException(LlmErrorKind.Server, "The transcription response was not valid JSON.", inner: ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new LlmException(LlmErrorKind.Cancelled, "Cancelled.");
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmException(LlmErrorKind.Timeout, $"No response within {timeout.TotalSeconds:0} seconds.", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(LlmErrorKind.Network, $"Network error: {ex.Message}", inner: ex);
        }
    }

    private static void AddAuth(HttpRequestMessage request, LlmEndpoint endpoint)
    {
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey.Trim());
        request.Headers.UserAgent.ParseAdd("PromptForge/0.1");
    }

    private static string Combine(string baseUrl, string path) => baseUrl.TrimEnd('/') + "/" + path;

    private static int GetInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    internal static LlmException MapError(HttpResponseMessage response, string body)
    {
        var message = ExtractErrorMessage(body) ?? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        var lower = message.ToLowerInvariant();
        var retryAfter = ParseRetryAfter(response, message);

        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => LlmErrorKind.Auth,
            HttpStatusCode.RequestEntityTooLarge => LlmErrorKind.TooLarge,
            HttpStatusCode.TooManyRequests when lower.Contains("request too large") || RequestedExceedsLimit(message) => LlmErrorKind.TooLarge,
            HttpStatusCode.TooManyRequests => LlmErrorKind.RateLimit,
            HttpStatusCode.NotFound => LlmErrorKind.ModelNotFound,
            HttpStatusCode.BadRequest when lower.Contains("response_format") || lower.Contains("json mode") || lower.Contains("json_object") => LlmErrorKind.JsonModeUnsupported,
            HttpStatusCode.BadRequest when lower.Contains("model") && (lower.Contains("not found") || lower.Contains("does not exist") || lower.Contains("decommissioned") || lower.Contains("not supported")) => LlmErrorKind.ModelNotFound,
            HttpStatusCode.BadRequest when lower.Contains("api key") || lower.Contains("api_key") => LlmErrorKind.Auth,
            HttpStatusCode.BadRequest when lower.Contains("context length") || lower.Contains("too long") || lower.Contains("maximum context") => LlmErrorKind.TooLarge,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => LlmErrorKind.BadRequest,
            _ when (int)response.StatusCode >= 500 => LlmErrorKind.Server,
            _ => LlmErrorKind.BadRequest,
        };
        return new LlmException(kind, message, retryAfter);
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0]; // Gemini sometimes wraps errors in an array
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString();
                if (error.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String) return msg.GetString();
            }
            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) return m.GetString();
        }
        catch (JsonException)
        {
            // Not JSON: fall through to a trimmed raw body.
        }
        return body.Length > 300 ? body[..300] : body;
    }

    internal static TimeSpan? ParseRetryAfter(HttpResponseMessage response, string message)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return delta;
        if (response.Headers.TryGetValues("retry-after", out var values) &&
            double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
            return TimeSpan.FromSeconds(secs);

        // Groq: "Please try again in 7.5s" or "in 1m3.2s" or "in 450ms"
        var match = TryAgainRegex().Match(message);
        if (!match.Success) return null;
        double total = 0;
        if (match.Groups["m"].Success) total += double.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) * 60;
        if (match.Groups["s"].Success) total += double.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        if (match.Groups["ms"].Success) total += double.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture) / 1000;
        return TimeSpan.FromSeconds(total);
    }

    private static bool RequestedExceedsLimit(string message)
    {
        var m = LimitRequestedRegex().Match(message);
        return m.Success
            && int.TryParse(m.Groups["limit"].Value, out var limit)
            && int.TryParse(m.Groups["req"].Value, out var requested)
            && requested > limit;
    }

    [GeneratedRegex(@"try again in (?:(?<m>\d+(?:\.\d+)?)m(?!s))?(?:(?<s>\d+(?:\.\d+)?)s)?(?:(?<ms>\d+(?:\.\d+)?)ms)?", RegexOptions.IgnoreCase)]
    private static partial Regex TryAgainRegex();

    [GeneratedRegex(@"Limit\s+(?<limit>\d+).*?Requested\s+(?<req>\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LimitRequestedRegex();
}
