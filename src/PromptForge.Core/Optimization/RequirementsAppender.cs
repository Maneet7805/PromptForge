namespace PromptForge.Core.Optimization;

/// <summary>
/// Adds the user's own standing requirements (Settings → Requirements) to a finished rewrite.
/// This runs after the model call, never inside it, so the text stays exactly as the user wrote it
/// and the rewriting rules don't change.
/// </summary>
public static class RequirementsAppender
{
    /// <summary>Longest requirements text kept per task type.</summary>
    public const int MaxLength = 2000;

    /// <summary>
    /// Returns the result with the requirements for its task type added at the end, word for word.
    /// A rewrite the model left unchanged gets nothing: it asked for nothing, so there is nothing to attach to.
    /// </summary>
    public static OptimizeResult Apply(OptimizeResult result, IReadOnlyDictionary<string, string>? requirements)
    {
        if (result.IsUnchanged || requirements is null) return result;

        var text = RequirementsFor(result.TaskType, requirements);
        if (text.Length == 0) return result;

        var prompt = result.Prompt.Replace("\r\n", "\n").TrimEnd();
        if (prompt.EndsWith(text, StringComparison.Ordinal)) return result;

        return result with
        {
            Prompt = prompt + "\n\n" + text,
            Changes = [.. result.Changes, $"Added your standing requirements for {result.TaskType.ToString().ToLowerInvariant()}"],
        };
    }

    /// <summary>The user's text for a task type, trimmed with LF newlines; empty when there is none.</summary>
    public static string RequirementsFor(TaskType task, IReadOnlyDictionary<string, string> requirements)
    {
        var entry = requirements.FirstOrDefault(kv => string.Equals(kv.Key, task.ToString(), StringComparison.OrdinalIgnoreCase));
        return (entry.Value ?? "").Replace("\r\n", "\n").Trim();
    }
}
