using System.Diagnostics;
using PromptForge.Core.Llm;

namespace PromptForge.Core.Optimization;

public enum OptimizeFailure
{
    Empty,
    TooLong,
    NoEndpoint,
    Model,
    BadResponse,
}

public sealed class OptimizeException(OptimizeFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public OptimizeFailure Failure { get; } = failure;
}

public sealed record OptimizerOptions
{
    /// <summary>Hard ceiling on draft size. Free tiers cap tokens per minute well below this.</summary>
    public int MaxInputChars { get; init; } = 40_000;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>One extra call when exact details were dropped. Costs tokens, so it can be turned off.</summary>
    public bool RepairDroppedDetails { get; init; } = true;

    /// <summary>Longest wait honoured for a rate-limit "retry after" before giving up on an endpoint.</summary>
    public TimeSpan MaxRateLimitWait { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// The system prompt. Only the quality check changes it, to compare another version of the
    /// instructions (an older release, or a candidate fix) without building a new exe.
    /// </summary>
    public string Instructions { get; init; } = SystemPrompt.Text;
}

/// <summary>
/// The optimisation pipeline: one model call does classification, gap analysis and the rewrite;
/// everything around it is deterministic (masking, parsing, preservation checks, fallback).
/// </summary>
public sealed class PromptOptimizer(ILlmClient client, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<OptimizeResult> OptimizeAsync(
        OptimizeRequest request,
        IReadOnlyList<LlmEndpoint> endpoints,
        OptimizerOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new OptimizerOptions();
        var original = request.Text ?? "";
        if (string.IsNullOrWhiteSpace(original))
            throw new OptimizeException(OptimizeFailure.Empty, "There's no text to optimise.");
        if (original.Length > options.MaxInputChars)
            throw new OptimizeException(OptimizeFailure.TooLong,
                $"The text is {original.Length:N0} characters; the limit is {options.MaxInputChars:N0}. Select just the prompt and use 'Optimise selection'.");
        if (endpoints.Count == 0)
            throw new OptimizeException(OptimizeFailure.NoEndpoint, "No model is configured. Open Settings → Model.");

        var stopwatch = Stopwatch.StartNew();
        var masked = BlockMasker.Mask(original);
        var messages = new List<ChatMessage>
        {
            new("system", options.Instructions),
            new("user", UserMessageBuilder.Build(request, masked)),
        };
        var chatOptions = new ChatOptions
        {
            Mode = request.Mode,
            MaxTokens = EstimateMaxTokens(masked.Text, request.Mode),
            Timeout = options.Timeout,
        };

        var (response, parsed, endpoint, attempts) = await CallWithFallbackAsync(endpoints, messages, chatOptions, options, ct).ConfigureAwait(false);
        var promptTokens = response.PromptTokens;
        var completionTokens = response.CompletionTokens;
        var cachedTokens = response.CachedTokens;
        var warnings = new List<string>();

        // Repair pass: exact details (paths, numbers, identifiers...) that the rewrite dropped.
        if (parsed.Verdict != Verdict.Unchanged && options.RepairDroppedDetails)
        {
            var missing = PreservationChecker.FindMissing(masked.Text, parsed.Prompt);
            if (missing.Count > 0)
            {
                try
                {
                    var repairMessages = new List<ChatMessage>(messages)
                    {
                        new("assistant", response.Content),
                        new("user", UserMessageBuilder.BuildRepairMessage(missing)),
                    };
                    var repairResponse = await client.CompleteAsync(endpoint, repairMessages, chatOptions, ct).ConfigureAwait(false);
                    attempts++;
                    promptTokens += repairResponse.PromptTokens;
                    completionTokens += repairResponse.CompletionTokens;
                    cachedTokens += repairResponse.CachedTokens;
                    var repaired = ResponseParser.Parse(repairResponse.Content);
                    if (repaired.Verdict != Verdict.Unchanged &&
                        PreservationChecker.FindMissing(masked.Text, repaired.Prompt).Count < missing.Count)
                    {
                        parsed = repaired with { Changes = parsed.Changes, Questions = parsed.Questions };
                    }
                }
                catch (Exception ex) when (ex is LlmException { Kind: not LlmErrorKind.Cancelled } or ResponseFormatException)
                {
                    // Keep the first rewrite; the warning below tells the user what to check.
                }

                var stillMissing = PreservationChecker.FindMissing(masked.Text, parsed.Prompt);
                if (stillMissing.Count > 0)
                    warnings.Add("Check these details from your draft; they may have been dropped: " + string.Join(", ", stillMissing.Take(8)));
            }
        }

        string finalPrompt;
        var verdict = parsed.Verdict;
        if (verdict == Verdict.Unchanged)
        {
            finalPrompt = original;
        }
        else
        {
            var unmasked = BlockMasker.Unmask(parsed.Prompt, masked.Blocks);
            finalPrompt = unmasked.Text.Trim();
            if (unmasked.MissingMarkers.Count > 0)
                warnings.Add("Some pasted material wasn't placed by the model, so it was added at the end unchanged.");
            if (Normalize(finalPrompt) == Normalize(original))
            {
                finalPrompt = original;
                verdict = Verdict.Unchanged;
            }
        }

        return new OptimizeResult
        {
            Original = original,
            Prompt = finalPrompt,
            TaskType = request.TaskOverride != TaskType.Auto ? request.TaskOverride : parsed.TaskType,
            Verdict = verdict,
            Changes = verdict == Verdict.Unchanged ? [] : parsed.Changes,
            Questions = parsed.Questions,
            Warnings = warnings,
            Model = endpoint.Label,
            Elapsed = stopwatch.Elapsed,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CachedTokens = cachedTokens,
            Attempts = attempts,
        };
    }

    private async Task<(ChatResponse Response, ParsedResponse Parsed, LlmEndpoint Endpoint, int Attempts)> CallWithFallbackAsync(
        IReadOnlyList<LlmEndpoint> endpoints,
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        OptimizerOptions options,
        CancellationToken ct)
    {
        Exception? last = null;
        var attempts = 0;
        for (var i = 0; i < endpoints.Count; i++)
        {
            var endpoint = endpoints[i];
            var isLast = i == endpoints.Count - 1;
            var callOptions = chatOptions;
            var waitedForRateLimit = false;
            var retriedFormat = false;
            var retriedTruncation = false;
            var currentMessages = messages;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                attempts++;
                try
                {
                    var response = await client.CompleteAsync(endpoint, currentMessages, callOptions, ct).ConfigureAwait(false);
                    var parsed = ResponseParser.Parse(response.Content);
                    return (response, parsed, endpoint, attempts);
                }
                catch (LlmException ex) when (ex.Kind == LlmErrorKind.Cancelled)
                {
                    throw new OperationCanceledException("Cancelled.", ex, ct);
                }
                catch (LlmException ex) when (ex.Kind == LlmErrorKind.JsonModeUnsupported && callOptions.JsonMode)
                {
                    callOptions = callOptions with { JsonMode = false };
                }
                catch (LlmException ex) when (ex.Kind == LlmErrorKind.Truncated && !retriedTruncation)
                {
                    retriedTruncation = true;
                    callOptions = callOptions with { MaxTokens = Math.Min(callOptions.MaxTokens * 2, 12_000) };
                }
                catch (LlmException ex) when (ex.Kind == LlmErrorKind.RateLimit && isLast && !waitedForRateLimit
                                              && ex.RetryAfter is { } wait && wait <= options.MaxRateLimitWait)
                {
                    waitedForRateLimit = true;
                    last = ex;
                    await _delay(wait + TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
                }
                catch (ResponseFormatException ex) when (!retriedFormat)
                {
                    retriedFormat = true;
                    last = ex;
                    currentMessages = [.. messages, new ChatMessage("user", "Respond with only the JSON object described in your instructions.")];
                }
                catch (LlmException ex) when (ex.WorthFallingBack)
                {
                    last = ex;
                    break; // next endpoint
                }
                catch (ResponseFormatException ex)
                {
                    last = ex;
                    break;
                }
                catch (LlmException ex)
                {
                    throw new OptimizeException(OptimizeFailure.Model, ex.UserMessage, ex);
                }
            }
        }

        throw last switch
        {
            LlmException llm => new OptimizeException(OptimizeFailure.Model, llm.UserMessage, llm),
            ResponseFormatException fmt => new OptimizeException(OptimizeFailure.BadResponse, "The model's reply couldn't be understood. Try again.", fmt),
            _ => new OptimizeException(OptimizeFailure.Model, "The model call failed.", last),
        };
    }

    /// <summary>Output budget: rewrite plus short reasoning, scaled to input size.</summary>
    internal static int EstimateMaxTokens(string maskedText, OptimizeMode mode)
    {
        var inputTokens = (int)Math.Ceiling(maskedText.Length / 3.5);
        var budget = inputTokens * 2 + (mode == OptimizeMode.Detailed ? 2000 : 1200);
        return Math.Clamp(budget, 1500, 8000);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();
}
