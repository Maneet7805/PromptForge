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
}

public sealed class LlmException : Exception
{
    public LlmException(LlmErrorKind kind, string message, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        RetryAfter = retryAfter;
    }

    public LlmErrorKind Kind { get; }
    public TimeSpan? RetryAfter { get; }

    /// <summary>Whether trying another model or provider might succeed.</summary>
    public bool WorthFallingBack => Kind is LlmErrorKind.RateLimit or LlmErrorKind.TooLarge or LlmErrorKind.Server
        or LlmErrorKind.Timeout or LlmErrorKind.Network or LlmErrorKind.ModelNotFound or LlmErrorKind.Truncated
        or LlmErrorKind.Auth;

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
        LlmErrorKind.Cancelled => "Cancelled.",
        _ => Message,
    };
}
