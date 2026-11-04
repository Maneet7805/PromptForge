using System.Text.RegularExpressions;

namespace PromptForge.Core.Llm;

public enum LlmErrorKind
{
    /// <summary>Missing, invalid or revoked API key.</summary>
    Auth,
    /// <summary>Requests-per-minute/day or tokens-per-minute/day limit hit (HTTP 429).</summary>
    RateLimit,
    /// <summary>Request larger than the model or plan allows (HTTP 413, or a 429 that says so).</summary>
    TooLarge,
    /// <summary>The model name doesn't exist for this provider.</summary>
    ModelNotFound,
    /// <summary>The provider rejected the JSON response_format parameter.</summary>
    JsonModeUnsupported,
    /// <summary>The reply was cut off by the output token limit.</summary>
    Truncated,
    BadRequest,
    Server,
    Timeout,
    Network,
    Cancelled,
    /// <summary>The provider refused a reply because the model's JSON was invalid (Groq: "Failed to validate JSON"). Often passes on a second try.</summary>
    InvalidJsonOutput,
}

public sealed partial class LlmException : Exception
{
    public LlmException(LlmErrorKind kind, string message, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        RetryAfter = retryAfter;
    }

    public LlmErrorKind Kind { get; }
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Kind plus, when the provider reported them, its token limit, the tokens already used in the window, the size
    /// requested ("Limit 8000, Used 7462, Requested 6913") and how long it said to wait. Digits and the error kind
    /// only, so it is safe to log: it never contains the provider's message or any prompt text.
    /// </summary>
    public string LogSummary
    {
        get
        {
            var text = $"llmError={Kind}";
            var m = LimitRequestedRegex().Match(Message);
            if (m.Success)
            {
                text += $" limit={m.Groups["limit"].Value}";
                var used = UsedRegex().Match(Message);
                if (used.Success) text += $" used={used.Groups["used"].Value}";
                text += $" requested={m.Groups["req"].Value}";
            }
            if (RetryAfter is { } wait) text += $" retryAfter={Math.Ceiling(wait.TotalSeconds):0}s";
            return text;
        }
    }

    /// <summary>Whether trying another model or provider might succeed.</summary>
    public bool WorthFallingBack => Kind is LlmErrorKind.RateLimit or LlmErrorKind.TooLarge or LlmErrorKind.Server
        or LlmErrorKind.Timeout or LlmErrorKind.Network or LlmErrorKind.ModelNotFound or LlmErrorKind.Truncated
        or LlmErrorKind.Auth
        or LlmErrorKind.InvalidJsonOutput;

    /// <summary>A message suitable for showing to the user.</summary>
    public string UserMessage => Kind switch
    {
        LlmErrorKind.Auth => "The API key was rejected. Check it in Settings → Model.",
        LlmErrorKind.RateLimit => RetryAfter is { } wait
            ? $"Free-tier rate limit reached. Try again in about {Math.Ceiling(wait.TotalSeconds)} s."
            : "Free-tier rate limit reached. Wait a minute and try again.",
        LlmErrorKind.TooLarge => "This prompt is too long for the model's free-tier limit. Try Light mode, optimise a selection, or shorten the pasted material.",
        LlmErrorKind.ModelNotFound => "The model name wasn't recognised by the provider. Check it in Settings → Model.",
        LlmErrorKind.Truncated => "The model ran out of output space. Try again, or use Light mode.",
        LlmErrorKind.Timeout => "The model took too long to respond. Try again.",
        LlmErrorKind.Network => "Couldn't reach the model provider. Check your internet connection.",
        LlmErrorKind.Server => "The model provider had an error. Try again shortly.",
        LlmErrorKind.InvalidJsonOutput => "The model's reply couldn't be understood. Try again.",
        LlmErrorKind.Cancelled => "Cancelled.",
        _ => Message,
    };

    [GeneratedRegex(@"Limit\s+(?<limit>\d+).*?Requested\s+(?<req>\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LimitRequestedRegex();

    [GeneratedRegex(@"\bUsed\s+(?<used>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex UsedRegex();
}
