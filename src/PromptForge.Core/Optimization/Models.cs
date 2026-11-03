namespace PromptForge.Core.Optimization;

/// <summary>How much the optimiser is allowed to change the draft.</summary>
public enum OptimizeMode
{
    Light,
    Standard,
    Detailed,
}

/// <summary>Task categories the optimiser tailors its edits to. Auto lets the model decide.</summary>
/// <remarks>Append new values at the end: the review window maps its dropdown index straight onto this enum.</remarks>
public enum TaskType
{
    Auto,
    Coding,
    Debugging,
    Research,
    Study,
    Writing,
    Planning,
    General,
    Image,
}

/// <summary>What the model judged it needed to do to the draft.</summary>
public enum Verdict
{
    Unchanged,
    LightEdit,
    Rewritten,
}

public sealed record OptimizeRequest
{
    public required string Text { get; init; }
    public OptimizeMode Mode { get; init; } = OptimizeMode.Standard;
    public TaskType TaskOverride { get; init; } = TaskType.Auto;

    /// <summary>Optional standing context about the user, used only where relevant.</summary>
    public string? PersonalContext { get; init; }

    /// <summary>Coarse label for where the draft was written, e.g. "ChatGPT (browser)". Optional.</summary>
    public string? WrittenIn { get; init; }

    /// <summary>Questions from earlier runs on this draft that <see cref="Answers"/> may refer to.</summary>
    public IReadOnlyList<string> PreviousQuestions { get; init; } = [];

    /// <summary>What the user typed in reply on re-run: answers or extra details. Sent beside the draft, never as part of it.</summary>
    public string? Answers { get; init; }
}

/// <summary>What the repair step (re-asking for details the rewrite dropped) did on one run.</summary>
public enum RepairOutcome
{
    /// <summary>Nothing was missing, or the rewrite was left unchanged.</summary>
    NotNeeded,
    /// <summary>The setting is off, so dropped details were not even checked.</summary>
    Disabled,
    /// <summary>The repaired rewrite was used: it kept more of the draft's details.</summary>
    Restored,
    /// <summary>The repair call worked but did not bring details back, so the first rewrite was kept.</summary>
    NoImprovement,
    /// <summary>The repair call failed (for example too large) and the first rewrite was kept.</summary>
    Failed,
}

public sealed record OptimizeResult
{
    public required string Original { get; init; }
    public required string Prompt { get; init; }
    public required TaskType TaskType { get; init; }
    public required Verdict Verdict { get; init; }
    public IReadOnlyList<string> Changes { get; init; } = [];
    public IReadOnlyList<string> Questions { get; init; } = [];

    /// <summary>Problems found by local validation, e.g. details that may have been dropped.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public required string Model { get; init; }
    public TimeSpan Elapsed { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int CachedTokens { get; init; }
    public int Attempts { get; init; } = 1;

    /// <summary>What the repair step did. Diagnostic only; counts and error kinds, never text from the draft.</summary>
    public RepairOutcome Repair { get; init; } = RepairOutcome.NotNeeded;

    /// <summary>Error kind and the provider's limit and requested size when the repair call failed. Digits and kinds only.</summary>
    public string? RepairFailure { get; init; }

    /// <summary>How many exact details were missing from the first rewrite, and after repair.</summary>
    public int MissingBeforeRepair { get; init; }
    public int MissingAfterRepair { get; init; }

    /// <summary>One entry per failed call that led to a retry or the next model ("model llmError=RateLimit"). Never the provider's message.</summary>
    public IReadOnlyList<string> FailedCalls { get; init; } = [];

    public bool IsUnchanged => Verdict == Verdict.Unchanged || Prompt == Original;
}
