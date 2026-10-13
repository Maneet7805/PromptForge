using static PromptForge.App.Native.Win32;

namespace PromptForge.App.Native;

/// <summary>Synthetic keystrokes. Only ever sends select-all, copy and paste chords; never Enter.</summary>
internal static class Keyboard
{
    private static readonly ushort[] Modifiers =
        [VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU, VK_LSHIFT, VK_RSHIFT, VK_LWIN, VK_RWIN];

    public static void SendCtrlChord(char key) => SendCtrlChord((ushort)char.ToUpperInvariant(key));

    public static void SendCtrlChord(ushort vk) =>
        Send([Key(VK_CONTROL, down: true), Key(vk, down: true), Key(vk, down: false), Key(VK_CONTROL, down: false)]);

    /// <summary>Ctrl+End: moves the caret to the end and clears a select-all, so the next keystroke can't overwrite the text.</summary>
    public static void CollapseSelectionToEnd() => SendCtrlChord(VK_END);

    private const ushort VK_END = 0x23;

    /// <summary>
    /// Hotkeys containing Alt leave the target app seeing "Alt pressed and released alone", which opens
    /// menu bars (and in browsers moves focus off the text box). A dummy key in between prevents that.
    /// </summary>
    public static void MaskAltRelease() =>
        Send([Key(VK_MASK, down: true), Key(VK_MASK, down: false)]);

    public static bool AnyModifierDown() =>
        Modifiers.Any(vk => IsKeyDown(vk)) || IsKeyDown(VK_CONTROL) || IsKeyDown(VK_MENU) || IsKeyDown(VK_SHIFT);

    /// <summary>Waits for the user to let go of the hotkey so held modifiers don't combine with our keystrokes.</summary>
    public static async Task<bool> WaitForModifiersReleasedAsync(int mainVk, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!AnyModifierDown() && (mainVk == 0 || !IsKeyDown(mainVk))) return true;
            await Task.Delay(15);
        }
        return false;
    }

    /// <summary>Releases modifiers that are still reported as down (used only after the wait times out).</summary>
    public static void ReleaseStuckModifiers()
    {
        var ups = Modifiers.Where(vk => IsKeyDown(vk)).Select(vk => Key(vk, down: false)).ToList();
        if (ups.Count > 0)
        {
            if (IsKeyDown(VK_MENU)) ups.Insert(0, Key(VK_MASK, down: true));
            Send([.. ups]);
        }
    }

    private static INPUT Key(ushort vk, bool down)
    {
        var flags = down ? 0u : KEYEVENTF_KEYUP;
        if (vk is VK_RCONTROL or VK_RMENU or VK_LWIN or VK_RWIN or VK_END) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKeyW(vk, 0),
                    dwFlags = flags,
                    dwExtraInfo = InputSignature,
                },
            },
        };
    }

    private static void Send(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, INPUT.Size);
        if (sent != inputs.Length)
            Log.Warn($"SendInput sent {sent}/{inputs.Length} events (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
    }
}
