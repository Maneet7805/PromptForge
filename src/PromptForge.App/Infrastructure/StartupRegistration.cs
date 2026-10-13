using Microsoft.Win32;

namespace PromptForge.App.Infrastructure;

/// <summary>Start-with-Windows via the per-user Run key (no admin rights, no scheduled task).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PromptForge";

    public static bool IsEnabled() => IsEnabledFor(AppPaths.ExePath);

    public static bool IsEnabledFor(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled) => SetFor(AppPaths.ExePath, enabled);

    public static void SetFor(string exePath, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{exePath}\" --background");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
