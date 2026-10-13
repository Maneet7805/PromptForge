using Microsoft.Win32;

namespace PromptForge.App.Infrastructure;

/// <summary>Start-with-Windows via the per-user Run key (no admin rights, no scheduled task).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PromptForge";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Contains(AppPaths.ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{AppPaths.ExePath}\" --background");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
