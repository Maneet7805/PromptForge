namespace PromptForge.Core.Diffing;

/// <summary>Tells whether the user changed text the optimiser produced.</summary>
public static class EditCheck
{
    /// <summary>The text with Windows line breaks (\r\n) turned into \n, so the two conventions compare equal.</summary>
    public static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n");

    /// <summary>True when <paramref name="current"/> differs from <paramref name="produced"/>, ignoring only the line-break convention.</summary>
    public static bool HasEdits(string current, string produced) =>
        NormalizeNewlines(current) != NormalizeNewlines(produced);
}
