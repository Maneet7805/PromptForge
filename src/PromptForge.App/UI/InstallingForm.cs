using System.Runtime.ExceptionServices;
using PromptForge.App.Native;

namespace PromptForge.App.UI;

/// <summary>
/// A small "Installing PromptForge X..." window with a moving bar, shown while setup copies the files and restarts
/// the app. The copy is one file and takes about a second, so the bar is honest motion, not a percentage.
/// It never takes keyboard focus, so typing continues in the user's app.
/// </summary>
internal sealed class InstallingForm : Form
{
    private const int FormWidth = 320, FormHeight = 84, Pad = 18, ScreenMargin = 24, BarHeight = 8, MarqueeMs = 30;

    private InstallingForm(string version)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 24, 31);
        ForeColor = Color.FromArgb(245, 245, 250);
        ClientSize = new Size(FormWidth, FormHeight);
        AccessibleRole = AccessibleRole.Alert;
        AccessibleName = $"Installing PromptForge {version}";

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Right - FormWidth - ScreenMargin, area.Bottom - FormHeight - ScreenMargin);

        Controls.Add(new Label
        {
            Text = $"Installing PromptForge {version}…",
            Font = new Font("Segoe UI Semibold", 11f),
            AutoSize = false,
            Location = new Point(Pad, Pad),
            Size = new Size(FormWidth - 2 * Pad, 24),
        });
        Controls.Add(new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = MarqueeMs,
            Location = new Point(Pad, FormHeight - Pad - BarHeight),
            Size = new Size(FormWidth - 2 * Pad, BarHeight),
        });
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
            return cp;
        }
    }

    /// <summary>
    /// Shows the window, runs <paramref name="work"/> on a background thread so the bar keeps moving, and closes the
    /// window when it ends. An exception from the work is rethrown here, after the window is gone.
    /// </summary>
    public static void Run(string version, Action work)
    {
        using var form = new InstallingForm(version);
        Exception? error = null;
        form.Shown += async (_, _) =>
        {
            try { await Task.Run(work); }
            catch (Exception ex) { error = ex; } // rethrown below, once the window is closed
            form.Close();
        };
        form.ShowDialog();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
