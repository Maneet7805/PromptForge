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

    /// <summary>Settings key for the text added to every rewrite, whatever its task type.</summary>
    public const string AllTasksKey = "All";

    /// <summary>
    /// Returns the result with the requirements for its task type, then the all-tasks requirements, added at the
    /// end word for word. A rewrite the model left unchanged gets nothing: it asked for nothing, so there is
    /// nothing to attach to. The same text is never added twice.
    /// </summary>
    public static OptimizeResult Apply(OptimizeResult result, IReadOnlyDictionary<string, string>? requirements)
    {
        if (result.IsUnchanged || requirements is null) return result;

        var taskText = RequirementsFor(result.TaskType, requirements);
        var allText = Normalise(requirements.FirstOrDefault(kv => string.Equals(kv.Key, AllTasksKey, StringComparison.OrdinalIgnoreCase)).Value);
        if (string.Equals(allText, taskText, StringComparison.Ordinal)) allText = "";

        var prompt = result.Prompt.Replace("\r\n", "\n").TrimEnd();
        var notes = new List<string>();
        var added = "";
        if (taskText.Length > 0 && !prompt.EndsWith(taskText, StringComparison.Ordinal))
        {
            added += "\n\n" + taskText;
            notes.Add($"Added your standing requirements for {result.TaskType.ToString().ToLowerInvariant()}");
        }
        if (allText.Length > 0 && !(prompt + added).EndsWith(allText, StringComparison.Ordinal))
        {
            added += "\n\n" + allText;
            notes.Add("Added your standing requirements for all tasks");
        }
        if (added.Length == 0) return result;

        return result with { Prompt = prompt + added, Changes = [.. result.Changes, .. notes] };
    }

    /// <summary>The user's text for a task type, trimmed with LF newlines; empty when there is none.</summary>
    public static string RequirementsFor(TaskType task, IReadOnlyDictionary<string, string> requirements)
    {
        var entry = requirements.FirstOrDefault(kv => string.Equals(kv.Key, task.ToString(), StringComparison.OrdinalIgnoreCase));
        return Normalise(entry.Value);
    }

    private static string Normalise(string? text) => (text ?? "").Replace("\r\n", "\n").Trim();
}
