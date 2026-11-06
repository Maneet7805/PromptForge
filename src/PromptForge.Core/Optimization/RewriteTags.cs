namespace PromptForge.Core.Optimization;

/// <summary>
/// The repair request wraps the rewrite in <c>&lt;rewrite&gt;</c> tags, and a model sometimes echoes the wrapper
/// inside its reply. This removes a wrapper that encloses the whole prompt so it never reaches the user.
/// </summary>
internal static class RewriteTags
{
    private const string Open = "<rewrite>";
    private const string Close = "</rewrite>";

    /// <summary>
    /// Returns <paramref name="prompt"/> without a <c>&lt;rewrite&gt;…&lt;/rewrite&gt;</c> pair around all of it.
    /// Left alone when the user's own <paramref name="draft"/> contains either tag: then they are the user's text.
    /// </summary>
    public static string StripWrapper(string prompt, string draft)
    {
        if (draft.Contains(Open, StringComparison.OrdinalIgnoreCase) ||
            draft.Contains(Close, StringComparison.OrdinalIgnoreCase))
            return prompt;

        var trimmed = prompt.Trim();
        if (trimmed.StartsWith(Open, StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[Open.Length..];
        if (trimmed.EndsWith(Close, StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^Close.Length];
        return trimmed.Trim();
    }
}
