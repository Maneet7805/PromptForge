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
