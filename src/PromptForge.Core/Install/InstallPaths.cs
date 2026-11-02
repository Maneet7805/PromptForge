namespace PromptForge.Core.Install;

/// <summary>Where the self-installer puts PromptForge, and the check that guards deleting it.</summary>
public static class InstallPaths
{
    /// <summary>The per-user install folder: <c>%LOCALAPPDATA%\Programs\PromptForge</c>.</summary>
    public static string InstallDir(string localAppData) => Path.Combine(localAppData, "Programs", "PromptForge");

    /// <summary>
    /// True only when <paramref name="candidate"/> is exactly the install folder after the path is normalised.
    /// Uninstall deletes a folder recursively, so a root, a parent, a sibling with a similar name or a
    /// <c>..</c> path must never pass.
    /// </summary>
    public static bool IsSafeToDelete(string? candidate, string localAppData)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(localAppData)) return false;
        try
        {
            var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(InstallDir(localAppData)));
            var actual = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
