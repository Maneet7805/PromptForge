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

    /// <summary>
    /// Longest wait honoured for a rate-limit "retry after" before moving on to the next model. Groq's free tier
    /// usually asks for under 20 seconds when a second long draft lands within a minute of the first; waiting
    /// keeps the stronger model instead of silently answering with the backup.
    /// </summary>
    public TimeSpan MaxRateLimitWait { get; init; } = TimeSpan.FromSeconds(25);

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

        var failedCalls = new List<string>();
        var (response, parsed, endpoint, attempts) = await CallWithFallbackAsync(endpoints, messages, chatOptions, options, failedCalls, ct).ConfigureAwait(false);
        var promptTokens = response.PromptTokens;
        var completionTokens = response.CompletionTokens;
        var cachedTokens = response.CachedTokens;
        var warnings = new List<string>();

        // Repair pass: exact details (paths, numbers, identifiers...) that the rewrite dropped.
        var repairOutcome = RepairOutcome.NotNeeded;
        string? repairFailure = null;
        var missingBefore = 0;
        var missingAfter = 0;
        if (parsed.Verdict != Verdict.Unchanged && !options.RepairDroppedDetails)
        {
            repairOutcome = RepairOutcome.Disabled;
        }
        else if (parsed.Verdict != Verdict.Unchanged)
        {
            var missing = PreservationChecker.FindMissing(masked.Text, parsed.Prompt);
            if (missing.Count > 0)
            {
                missingBefore = missing.Count;
                repairOutcome = RepairOutcome.NoImprovement;
                // Compact request (the rewrite plus excerpts, not the whole draft again), sent to the model that
                // answered first and then to the next ones. A long draft leaves the first model with almost no
                // tokens-per-minute budget for a second call, but each model has its own budget.
                var repairMessages = new List<ChatMessage>
                {
                    new("system", UserMessageBuilder.RepairInstructions),
                    new("user", UserMessageBuilder.BuildRepairRequest(parsed.Prompt, missing, masked.Text)),
                };
                var repairOptions = chatOptions with { MaxTokens = Math.Min(chatOptions.MaxTokens, EstimateMaxTokens(parsed.Prompt, request.Mode)) };
                for (var i = IndexOfEndpoint(endpoints, endpoint); i < endpoints.Count; i++)
                {
                    var repairEndpoint = endpoints[i];
                    try
                    {
                        var repairResponse = await client.CompleteAsync(repairEndpoint, repairMessages, repairOptions, ct).ConfigureAwait(false);
                        attempts++;
                        promptTokens += repairResponse.PromptTokens;
                        completionTokens += repairResponse.CompletionTokens;
                        cachedTokens += repairResponse.CachedTokens;
                        repairOutcome = RepairOutcome.NoImprovement;
                        repairFailure = null;
                        var repaired = ResponseParser.Parse(repairResponse.Content);
                        // Accept it only if it restored details without shortening the rewrite: a weaker model
                        // asked to "put it back" sometimes rewrites the whole thing shorter.
                        if (repaired.Verdict != Verdict.Unchanged &&
                            repaired.Prompt.Length >= parsed.Prompt.Length * MinRepairLengthRatio &&
                            PreservationChecker.FindMissing(masked.Text, repaired.Prompt).Count < missing.Count)
                        {
                            parsed = parsed with { Prompt = repaired.Prompt };
                            repairOutcome = RepairOutcome.Restored;
                        }
                        break;
                    }
                    catch (Exception ex) when (ex is LlmException { Kind: not LlmErrorKind.Cancelled } or ResponseFormatException)
                    {
                        // Keep the first rewrite; the warning below tells the user what to check.
                        repairOutcome = RepairOutcome.Failed;
                        repairFailure = Describe(ex);
                        failedCalls.Add($"{repairEndpoint.Model} repair {Describe(ex)}");
                        if (ex is LlmException { WorthFallingBack: false }) break;
                    }
                }

                var stillMissing = PreservationChecker.FindMissing(masked.Text, parsed.Prompt);
                missingAfter = stillMissing.Count;
                if (stillMissing.Count > 0)
                    warnings.Add("Check these details from your draft; they may have been dropped: " + string.Join(", ", stillMissing.Take(8)));
            }
        }

        // Tell the user when the backup model answered: its rewrites of long drafts can be weaker, and a rate limit
        // usually clears within a minute, so re-running may get the main model.
        if (IndexOfEndpoint(endpoints, endpoint) > 0)
            warnings.Add($"Answered by the backup model ({endpoint.Label}) because {endpoints[0].Label} couldn't answer. Press Ctrl+R after a minute to try the main model again.");

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
            Repair = repairOutcome,
            RepairFailure = repairFailure,
            MissingBeforeRepair = missingBefore,
            MissingAfterRepair = missingAfter,
            FailedCalls = failedCalls,
        };
    }

    /// <summary>A repair shorter than this share of the first rewrite is treated as a bad repair and dropped.</summary>
    private const double MinRepairLengthRatio = 0.9;

    private static int IndexOfEndpoint(IReadOnlyList<LlmEndpoint> endpoints, LlmEndpoint endpoint)
    {
        for (var i = 0; i < endpoints.Count; i++)
            if (endpoints[i].Equals(endpoint)) return i;
        return 0;
    }

    private const string JsonReminder = "Respond with only the JSON object described in your instructions.";

    private async Task<(ChatResponse Response, ParsedResponse Parsed, LlmEndpoint Endpoint, int Attempts)> CallWithFallbackAsync(
        IReadOnlyList<LlmEndpoint> endpoints,
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        OptimizerOptions options,
        List<string> failedCalls,
        CancellationToken ct)
    {
        Exception? last = null;
        var attempts = 0;
        for (var i = 0; i < endpoints.Count; i++)
        {
            var endpoint = endpoints[i];
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
                    failedCalls.Add($"{endpoint.Model} {Describe(ex)}");
                    callOptions = callOptions with { JsonMode = false };
                }
                catch (LlmException ex) when (ex.Kind == LlmErrorKind.Truncated && !retriedTruncation)
                {
                    failedCalls.Add($"{endpoint.Model} {Describe(ex)}");
                    retriedTruncation = true;
                    callOptions = callOptions with { MaxTokens = Math.Min(callOptions.MaxTokens * 2, 12_000) };
                }
                catch (LlmException ex) when (ex.Kind == LlmErrorKind.RateLimit && !waitedForRateLimit
                                              && ex.RetryAfter is { } wait && wait <= options.MaxRateLimitWait)
                {
                    failedCalls.Add($"{endpoint.Model} {Describe(ex)}");
                    waitedForRateLimit = true;
                    last = ex;
                    await _delay(wait + TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
                }
                catch (ResponseFormatException ex) when (!retriedFormat)
                {
                    failedCalls.Add($"{endpoint.Model} {Describe(ex)}");
                    retriedFormat = true;
                    last = ex;
                    currentMessages = [.. messages, new ChatMessage("user", JsonReminder)];
                }
                catch (LlmException ex) when (ex.Kind == LlmErrorKind.InvalidJsonOutput && !retriedFormat)
                {
                    failedCalls.Add($"{endpoint.Model} {Describe(ex)}");
                    retriedFormat = true;
                    last = ex;
                    currentMessages = [.. messages, new ChatMessage("user", JsonReminder)];
                }
                catch (LlmException ex) when (ex.WorthFallingBack)
                {
                    failedCalls.Add($"{endpoint.Model} {Describe(ex)}");
                    last = ex;
                    break; // next endpoint
                }
                catch (ResponseFormatException ex)
                {
                    failedCalls.Add($"{endpoint.Model} {Describe(ex)}");
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

    /// <summary>Error kind (plus the provider's limit and requested size) for the log. Never the provider's message or any draft text.</summary>
    private static string Describe(Exception ex) => ex is LlmException llm ? llm.LogSummary : "badFormat";

    /// <summary>Output budget: rewrite plus short reasoning, scaled to input size.</summary>
    internal static int EstimateMaxTokens(string maskedText, OptimizeMode mode)
    {
        var inputTokens = (int)Math.Ceiling(maskedText.Length / 3.5);
        var budget = inputTokens * 2 + (mode == OptimizeMode.Detailed ? 2000 : 1200);
        return Math.Clamp(budget, 1500, 8000);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();
}
