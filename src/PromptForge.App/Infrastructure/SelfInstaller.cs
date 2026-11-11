using System.Diagnostics;
using PromptForge.App.UI;
using PromptForge.Core.Install;

namespace PromptForge.App.Infrastructure;

/// <summary>
/// Double-clicking a downloaded PromptForge.exe installs (or updates) it for the current user:
/// copies itself to %LOCALAPPDATA%\Programs\PromptForge, adds a Start menu shortcut and an "Installed apps"
/// entry, optionally registers start-with-Windows, asks any running copy to exit, and launches the installed copy.
/// "--uninstall" (from Settings → Apps) undoes all of it. No admin rights, no installer framework.
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
    public static bool OfferInstall(bool fromUpdater)
    {
        var installed = File.Exists(InstalledExe);
        var version = Application.ProductVersion;
        if (!InstallPaths.ShouldSkipInstallPrompt(fromUpdater ? [InstallPaths.FromUpdaterFlag] : [], installed))
        {
            var installedVersion = installed ? FileVersionInfo.GetVersionInfo(InstalledExe).ProductVersion ?? "unknown" : null;
            var question = installed
                ? $"Update the installed PromptForge ({installedVersion}) to this version ({version})?\n\nYour settings and API keys are kept. A running PromptForge will restart."
                : $"Install PromptForge {version} for {Environment.UserName}?\n\nIt goes to {InstallDir}, gets a Start menu shortcut and an entry in Settings → Apps, and runs in the system tray. No admin rights needed.";
            var answer = MessageBox.Show(question + "\n\nChoose No to run this copy without installing.", "PromptForge setup",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
            if (answer == DialogResult.Cancel) return true;
            if (answer == DialogResult.No) return false;
        }

        // Asked before the progress window opens, so no dialog sits on top of it.
        var autostart = !installed && MessageBox.Show("Start PromptForge automatically when Windows starts?", "PromptForge setup",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        try
        {
            InstallingForm.Run(version, () => Install(installed, version, autostart));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Error("install failed", ex);
            MessageBox.Show("Couldn't install PromptForge: " + ex.Message, "PromptForge setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return true;
        }
    }

    /// <summary>Runs on a background thread while the Installing window shows.</summary>
    private static void Install(bool installed, string version, bool autostart)
    {
        StopRunningInstance();
        CopyWithRetry(AppPaths.ExePath, InstalledExe);
        CreateShortcut();
        WriteInstalledAppsEntry(version);
        if (!installed)
            SetAutostart(autostart);
        else if (StartupRegistration.IsEnabledFor(InstalledExe))
            SetAutostart(true); // refresh the command line in case it changed
        Log.Info($"{(installed ? "updated" : "installed")} version={version}");
        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
    }

    /// <summary>Best effort: a missing list entry must not fail an install.</summary>
    private static void WriteInstalledAppsEntry(string version)
    {
        try
        {
            UninstallRegistration.Write(InstallDir, InstalledExe, version.Split('+')[0]);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn("installed apps entry failed: " + ex.GetType().Name);
        }
    }

    /// <summary>Handles "--uninstall" from Settings → Apps. Returns the process exit code.</summary>
    public static int Uninstall()
    {
        const string title = "PromptForge uninstall";
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!IsRunningFromInstallDir || !InstallPaths.IsSafeToDelete(InstallDir, localAppData))
        {
            MessageBox.Show("This copy of PromptForge isn't the installed one, so there is nothing to uninstall.\n\nA copy run from another folder can simply be deleted.",
                title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
        if (MessageBox.Show("Uninstall PromptForge?\n\nThe app is removed from this computer. Your settings and API keys are kept unless you choose to delete them next.",
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
        var deleteData = MessageBox.Show("Also delete your settings and API keys?\n\nChoose No to keep them, for example to reinstall later.",
            title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        try
        {
            StopRunningInstance();
            StartupRegistration.SetFor(InstalledExe, false);
            File.Delete(ShortcutPath);
            UninstallRegistration.Remove();
            Log.Info($"uninstalled keepData={!deleteData}"); // before the data folder goes: logging recreates it
            if (deleteData && Directory.Exists(AppPaths.DataDir)) Directory.Delete(AppPaths.DataDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            if (!deleteData) Log.Error("uninstall failed", ex);
            MessageBox.Show("Couldn't finish uninstalling: " + ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        MessageBox.Show("PromptForge was uninstalled.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        RemoveInstallFolderAfterExit(localAppData);
        return 0;
    }

    /// <summary>A running exe can't delete itself: a hidden command waits for this process to exit, then removes the folder.</summary>
    private static void RemoveInstallFolderAfterExit(string localAppData)
    {
        if (!InstallPaths.IsSafeToDelete(InstallDir, localAppData)) return;
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(InstallDir));
        const string wait = "ping -n 4 127.0.0.1 >nul";
        var command = $"/c {wait} & rmdir /s /q \"{folder}\" & {wait} & rmdir /s /q \"{folder}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", command) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
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
