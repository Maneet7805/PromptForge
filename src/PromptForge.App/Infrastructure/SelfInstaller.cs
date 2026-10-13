using System.Diagnostics;

namespace PromptForge.App.Infrastructure;

/// <summary>
/// Double-clicking a downloaded PromptForge.exe installs (or updates) it for the current user:
/// copies itself to %LOCALAPPDATA%\Programs\PromptForge, adds a Start menu shortcut, optionally
/// registers start-with-Windows, asks any running copy to exit, and launches the installed copy.
/// No admin rights, no installer framework.
/// </summary>
internal static class SelfInstaller
{
    public const string ExitEventName = @"Local\PromptForge.Exit";

    public static string InstallDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PromptForge");
    public static string InstalledExe => Path.Combine(InstallDir, "PromptForge.exe");
    private static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PromptForge.lnk");

    public static bool IsRunningFromInstallDir =>
        string.Equals(Path.GetFullPath(AppPaths.ExePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true when the caller should exit (installed and launched, or cancelled).</summary>
    public static bool OfferInstall()
    {
        var installed = File.Exists(InstalledExe);
        var version = Application.ProductVersion;
        var installedVersion = installed ? FileVersionInfo.GetVersionInfo(InstalledExe).ProductVersion ?? "unknown" : null;
        var question = installed
            ? $"Update the installed PromptForge ({installedVersion}) to this version ({version})?\n\nYour settings and API keys are kept. A running PromptForge will restart."
            : $"Install PromptForge {version} for {Environment.UserName}?\n\nIt goes to {InstallDir}, gets a Start menu shortcut, and runs in the system tray. No admin rights needed.";
        var answer = MessageBox.Show(question + "\n\nChoose No to run this copy without installing.", "PromptForge setup",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
        if (answer == DialogResult.Cancel) return true;
        if (answer == DialogResult.No) return false;

        try
        {
            StopRunningInstance();
            CopyWithRetry(AppPaths.ExePath, InstalledExe);
            CreateShortcut();
            if (!installed)
            {
                var autostart = MessageBox.Show("Start PromptForge automatically when Windows starts?", "PromptForge setup",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                SetAutostart(autostart);
            }
            else if (StartupRegistration.IsEnabledFor(InstalledExe))
            {
                SetAutostart(true); // refresh the command line in case it changed
            }
            Log.Info($"{(installed ? "updated" : "installed")} version={version}");
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("install failed", ex);
            MessageBox.Show("Couldn't install PromptForge: " + ex.Message, "PromptForge setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return true;
        }
    }

    /// <summary>Asks a running PromptForge to exit and waits for it; kills it only as a last resort.</summary>
    private static void StopRunningInstance()
    {
        if (EventWaitHandle.TryOpenExisting(ExitEventName, out var exitEvent))
        {
            using (exitEvent) exitEvent.Set();
        }
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (!OtherInstances().Any()) return;
            Thread.Sleep(150);
        }
        foreach (var p in OtherInstances())
        {
            try { p.Kill(); p.WaitForExit(3000); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    private static IEnumerable<Process> OtherInstances() =>
        Process.GetProcessesByName("PromptForge").Where(p => p.Id != Environment.ProcessId);

    private static void CopyWithRetry(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 15)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static void CreateShortcut()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(ShortcutPath);
        link.TargetPath = InstalledExe;
        link.WorkingDirectory = InstallDir;
        link.Description = "Rewrite rough prompts into clear ones";
        link.Save();
    }

    private static void SetAutostart(bool enabled) => StartupRegistration.SetFor(InstalledExe, enabled);
}
