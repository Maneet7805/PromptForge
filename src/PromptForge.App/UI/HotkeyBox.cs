using PromptForge.Core.Settings;

namespace PromptForge.App.UI;

/// <summary>A text box that records the next key combination pressed in it, e.g. "Ctrl+Alt+P".</summary>
internal sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        Cursor = Cursors.Hand;
        ShortcutsEnabled = false;
        PlaceholderText = "Click, then press a shortcut";
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Capture combinations (including Alt ones that would otherwise go to menus) instead of acting on them.
        var key = keyData & Keys.KeyCode;
        var mods = keyData & Keys.Modifiers;
        if (key is Keys.Tab && mods == Keys.None) return base.ProcessCmdKey(ref msg, keyData);
        if (key is Keys.Back or Keys.Delete && mods == Keys.None)
        {
            Text = "";
            return true;
        }
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None)
            return true;

        var chord = new HotkeyChord(
            mods.HasFlag(Keys.Control),
            mods.HasFlag(Keys.Alt),
            mods.HasFlag(Keys.Shift),
            false,
            key.ToString());
        Text = chord.ToString();
        return true;
    }
}
