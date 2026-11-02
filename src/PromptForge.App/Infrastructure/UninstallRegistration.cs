using Microsoft.Win32;

namespace PromptForge.App.Infrastructure;

/// <summary>The per-user "Installed apps" entry (Settings → Apps), so PromptForge can be uninstalled like any app. No admin rights.</summary>
internal static class UninstallRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PromptForge";
    private const string Publisher = "Maneet7805";

    public static void Write(string installDir, string exePath, string version)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("DisplayName", "PromptForge");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", Publisher);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void Remove() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
}
