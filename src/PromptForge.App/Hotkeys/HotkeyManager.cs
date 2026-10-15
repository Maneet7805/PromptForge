using System.Runtime.InteropServices;
using PromptForge.App.Native;
using PromptForge.Core.Settings;

namespace PromptForge.App.Hotkeys;

internal enum HotkeyAction
{
    Optimize = 1,
    OptimizeSelection = 2,
    Voice = 3,
}

/// <summary>System-wide hotkeys via RegisterHotKey. Windows delivers WM_HOTKEY to a hidden message window.</summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private readonly Dictionary<HotkeyAction, HotkeyChord> _registered = [];

    public HotkeyManager() => CreateHandle(new CreateParams { Caption = "PromptForge.Hotkeys" });

    public event Action<HotkeyAction>? Pressed;

    public IReadOnlyDictionary<HotkeyAction, HotkeyChord> Registered => _registered;

    /// <summary>Registers a hotkey. Returns an error message, or null on success.</summary>
    public string? Register(HotkeyAction action, string text)
    {
        Unregister(action);
        if (!HotkeyChord.TryParse(text, out var chord)) return $"\"{text}\" isn't a valid shortcut.";
        if (chord.Problem() is { } problem) return problem;
        if (!TryGetVirtualKey(chord, out var vk)) return $"\"{chord.Key}\" isn't a key PromptForge recognises.";

        var modifiers = Win32.MOD_NOREPEAT
                        | (chord.Ctrl ? Win32.MOD_CONTROL : 0)
                        | (chord.Alt ? Win32.MOD_ALT : 0)
                        | (chord.Shift ? Win32.MOD_SHIFT : 0)
                        | (chord.Win ? Win32.MOD_WIN : 0);
        if (!Win32.RegisterHotKey(Handle, (int)action, modifiers, (uint)vk))
        {
            var error = Marshal.GetLastWin32Error();
            return error == Win32.ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"{chord} is already used by another app. Pick a different shortcut in Settings."
                : $"Couldn't register {chord} (error {error}).";
        }
        _registered[action] = chord;
        return null;
    }

    public void Unregister(HotkeyAction action)
    {
        if (_registered.Remove(action)) Win32.UnregisterHotKey(Handle, (int)action);
    }

    public void UnregisterAll()
    {
        foreach (var action in _registered.Keys.ToList()) Unregister(action);
    }

    /// <summary>Checks whether a shortcut is free by registering it briefly under a scratch id.</summary>
    public string? Probe(string text)
    {
        if (!HotkeyChord.TryParse(text, out var chord)) return $"\"{text}\" isn't a valid shortcut.";
        if (chord.Problem() is { } problem) return problem;
        if (_registered.Values.Contains(chord)) return null; // already ours
        if (!TryGetVirtualKey(chord, out var vk)) return $"\"{chord.Key}\" isn't a key PromptForge recognises.";
        const int probeId = 99;
        var modifiers = (chord.Ctrl ? Win32.MOD_CONTROL : 0) | (chord.Alt ? Win32.MOD_ALT : 0)
                        | (chord.Shift ? Win32.MOD_SHIFT : 0) | (chord.Win ? Win32.MOD_WIN : 0);
        if (!Win32.RegisterHotKey(Handle, probeId, modifiers, (uint)vk))
            return $"{chord} is already used by another app.";
        Win32.UnregisterHotKey(Handle, probeId);
        return null;
    }

    /// <summary>
    /// Windows treats Ctrl+Alt as AltGr, so on many keyboard layouts (German, Polish, French, English (India)…)
    /// Ctrl+Alt+key types a character. A shortcut on that chord blocks the character while PromptForge runs,
    /// and if the shortcut isn't registered the character gets typed instead. Returns a warning naming the
    /// character for any installed layout, or null.
    /// </summary>
    public static string? AltGrConflict(HotkeyChord chord)
    {
        if (!(chord.Ctrl && chord.Alt) || chord.Win || !TryGetVirtualKey(chord, out var key)) return null;
        var count = Win32.GetKeyboardLayoutList(0, null);
        if (count <= 0) return null;
        var layouts = new IntPtr[count];
        count = Win32.GetKeyboardLayoutList(count, layouts);

        var state = new byte[256];
        foreach (var vk in new[] { Keys.ControlKey, Keys.LControlKey, Keys.Menu, Keys.RMenu }) state[(int)vk] = 0x80;
        if (chord.Shift) state[(int)Keys.ShiftKey] = state[(int)Keys.LShiftKey] = 0x80;

        var buffer = new System.Text.StringBuilder(8);
        foreach (var layout in layouts.Take(count))
        {
            buffer.Clear();
            var scan = Win32.MapVirtualKeyExW((uint)key, 0, layout);
            var n = Win32.ToUnicodeEx((uint)key, scan, state, buffer, buffer.Capacity, Win32.TOUNICODE_NO_STATE_CHANGE, layout);
            if (n == 0 || buffer.Length == 0 || char.IsControl(buffer[0])) continue;
            var typed = n > 0 ? buffer.ToString(0, Math.Min(n, buffer.Length)) : buffer.ToString(0, 1); // n < 0: dead key
            return $"{chord} types \"{typed}\" on one of your keyboard layouts, so you can't type that character while PromptForge uses it. If you need it, pick a shortcut without Ctrl+Alt in Settings → Shortcuts.";
        }
        return null;
    }

    public static bool TryGetVirtualKey(HotkeyChord chord, out Keys key) =>
        Enum.TryParse(chord.Key, ignoreCase: true, out key) && key is not (Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY)
        {
            var action = (HotkeyAction)m.WParam.ToInt32();
            if (_registered.TryGetValue(action, out var chord) && chord.Alt)
                Native.Keyboard.MaskAltRelease();
            Pressed?.Invoke(action);
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
