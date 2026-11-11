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

    /// <summary>
    /// False when the exe is the launcher from a plain <c>dotnet build</c>: it needs <c>PromptForge.dll</c> and the
    /// other build files beside it, so copying only the exe into the install folder would break the installed copy.
    /// The release exe is a single file with nothing beside it.
    /// </summary>
    public static bool CanInstallFrom(string? exePath)
    {
        var dir = string.IsNullOrWhiteSpace(exePath) ? null : Path.GetDirectoryName(exePath);
        return dir is null || !File.Exists(Path.Combine(dir, "PromptForge.dll"));
    }

    /// <summary>Passed by the updater when it starts the exe it has already downloaded and checked.</summary>
    public const string FromUpdaterFlag = "--from-updater";

    /// <summary>
    /// True when setup may skip its "Update the installed PromptForge?" question: the user already chose
    /// "Update now" in the update card, the flag says so, and there is an installed copy to update.
    /// A first install always asks. The caller still applies <see cref="CanInstallFrom"/> before any offer.
    /// </summary>
    public static bool ShouldSkipInstallPrompt(IEnumerable<string> args, bool alreadyInstalled) =>
        alreadyInstalled && args.Contains(FromUpdaterFlag);
}
