namespace PromptForge.Core.Settings;

/// <summary>A key combination such as "Ctrl+Alt+P". The key part is a Windows Forms Keys name (P, F8, Space, D1...).</summary>
public sealed record HotkeyChord(bool Ctrl, bool Alt, bool Shift, bool Win, string Key)
{
    private static readonly HashSet<string> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl", "Control", "Alt", "Shift", "Win", "Windows", "ControlKey", "ShiftKey", "Menu", "LWin", "RWin",
        "LControlKey", "RControlKey", "LShiftKey", "RShiftKey", "LMenu", "RMenu",
    };

    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = new HotkeyChord(false, false, false, false, "");
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool ctrl = false, alt = false, shift = false, win = false;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win" or "windows": win = true; break;
                default:
                    if (key is not null || ModifierNames.Contains(raw)) return false;
                    key = NormalizeKey(raw);
                    break;
            }
        }
        if (key is null) return false;
        chord = new HotkeyChord(ctrl, alt, shift, win, key);
        return true;
    }

    /// <summary>Why this chord can't be used as a global hotkey, or null if it can.</summary>
    public string? Problem()
    {
        if (string.IsNullOrEmpty(Key)) return "Press a key together with Ctrl or Alt.";
        if (!Ctrl && !Alt) return "Include Ctrl or Alt so normal typing can't trigger it.";
        if (Ctrl && Alt && !Shift && Key is "Delete") return "Ctrl+Alt+Delete is reserved by Windows.";
        return null;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(DisplayKey(Key));
        return string.Join("+", parts);
    }

    private static string NormalizeKey(string key) => key.Length == 1 && char.IsDigit(key[0]) ? "D" + key : key.Length == 1 ? key.ToUpperInvariant() : key;

    private static string DisplayKey(string key) => key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]) ? key[1..] : key;
}
