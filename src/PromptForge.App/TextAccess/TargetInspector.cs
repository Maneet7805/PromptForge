using System.Diagnostics;
using System.Windows.Automation;
using PromptForge.App.Native;

namespace PromptForge.App.TextAccess;

/// <summary>The window the user was typing in when they pressed a hotkey.</summary>
internal sealed record TargetInfo(
    IntPtr Hwnd,
    string ProcessName,
    string WindowClass,
    string? AppLabel,
    bool IsTerminal,
    bool IsPasswordField,
    bool IsOwnWindow)
{
    public bool IsAlive => Win32.IsWindow(Hwnd);
}

internal static class TargetInspector
{
    private static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh", "wt", "wezterm-gui", "alacritty",
        "mintty", "Hyper", "Tabby", "kitty", "ConEmu64", "ConEmu", "Cmder", "putty", "ubuntu", "wsl", "bash",
    };

    private static readonly HashSet<string> TerminalClasses = new(StringComparer.Ordinal)
    {
        "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "PseudoConsoleWindow", "mintty", "PuTTY", "VirtualConsoleClass",
    };

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "zen", "librewolf", "thorium",
    };

    private static readonly HashSet<string> CodeEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "Code - Insiders", "Cursor", "Windsurf", "Antigravity", "VSCodium", "zed", "devenv", "rider64", "idea64", "pycharm64", "webstorm64",
    };

    public static TargetInfo Inspect(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        var processName = "";
        try
        {
            using var process = Process.GetProcessById((int)pid);
            processName = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        var windowClass = Win32.GetWindowClass(hwnd);
        var title = Win32.GetWindowTitle(hwnd);
        var own = pid == (uint)Environment.ProcessId;

        var focus = own ? default : InspectFocusedElement(TimeSpan.FromMilliseconds(400));
        var isTerminal = TerminalProcesses.Contains(processName) || TerminalClasses.Contains(windowClass) || focus.LooksLikeTerminal;

        return new TargetInfo(hwnd, processName, windowClass, Label(processName, title, isTerminal), isTerminal, focus.IsPassword, own);
    }

    /// <summary>
    /// UI Automation look at the focused control: password fields are refused, and terminals embedded in
    /// editors (VS Code's xterm.js) are treated as terminals. Bounded by a timeout because UIA can hang.
    /// </summary>
    private static FocusFacts InspectFocusedElement(TimeSpan timeout)
    {
        var task = Task.Run(() =>
        {
            try
            {
                var element = AutomationElement.FocusedElement;
                if (element is null) return default;
                var current = element.Current;
                var cls = current.ClassName ?? "";
                var name = current.Name ?? "";
                var looksLikeTerminal = cls.Contains("xterm", StringComparison.OrdinalIgnoreCase)
                                        || cls.Contains("terminal", StringComparison.OrdinalIgnoreCase)
                                        || name.StartsWith("Terminal ", StringComparison.Ordinal);
                return new FocusFacts(current.IsPassword, looksLikeTerminal);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or TimeoutException)
            {
                return default;
            }
        });
        return task.Wait(timeout) ? task.Result : default;
    }

    private static string? Label(string process, string title, bool isTerminal)
    {
        if (isTerminal) return "a terminal (possibly a coding agent CLI)";
        if (Browsers.Contains(process))
        {
            if (Has(title, "ChatGPT")) return "ChatGPT (browser)";
            if (Has(title, "Claude")) return "Claude (browser)";
            if (Has(title, "Gemini")) return "Gemini (browser)";
            if (Has(title, "Perplexity")) return "Perplexity (browser)";
            if (Has(title, "Copilot")) return "Microsoft Copilot (browser)";
            if (Has(title, "Gmail") || Has(title, "Outlook")) return "an email client (browser)";
            if (Has(title, "Google Docs") || Has(title, "Notion")) return "a document editor (browser)";
            return "a web page";
        }
        if (CodeEditors.Contains(process)) return "a code editor (likely its AI chat panel)";
        return process.ToLowerInvariant() switch
        {
            "claude" => "the Claude desktop app",
            "chatgpt" => "the ChatGPT desktop app",
            "outlook" or "olk" or "thunderbird" => "an email client",
            "winword" or "notion" or "obsidian" => "a document editor",
            "notepad" or "notepad++" => "a plain text editor",
            _ => null,
        };
    }

    private static bool Has(string title, string word) => title.Contains(word, StringComparison.OrdinalIgnoreCase);

    private readonly record struct FocusFacts(bool IsPassword, bool LooksLikeTerminal);
}
