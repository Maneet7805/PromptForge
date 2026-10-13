using System.Drawing.Drawing2D;
using PromptForge.App.Native;

namespace PromptForge.App.UI;

/// <summary>
/// Small always-on-top status pill ("Listening…", "Transcribing…") near the bottom of the screen.
/// It never takes focus, so typing continues in the user's app. Clicking it raises Clicked (used to cancel).
/// </summary>
internal sealed class StatusPill : Form
{
    private string _message = "";
    private Color _dot = Color.FromArgb(220, 50, 50);

    public StatusPill()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 32, 36);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        Size = new Size(320, 40);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    public event Action? Clicked;

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

    public void ShowMessage(string message, Color dot)
    {
        _message = message;
        _dot = dot;
        using (var g = CreateGraphics())
        {
            var width = (int)g.MeasureString(message, Font).Width + 56;
            Size = new Size(Math.Clamp(width, 200, 560), 40);
        }
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 48);
        Region = RoundedRegion(Size, 20);
        if (!Visible) Show();
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        Clicked?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var dot = new SolidBrush(_dot);
        e.Graphics.FillEllipse(dot, 16, Height / 2 - 6, 12, 12);
        TextRenderer.DrawText(e.Graphics, _message, Font, new Point(38, (Height - Font.Height) / 2), ForeColor);
    }

    private static Region RoundedRegion(Size size, int radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
        path.AddArc(size.Width - radius * 2, 0, radius * 2, radius * 2, 270, 90);
        path.AddArc(size.Width - radius * 2, size.Height - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(0, size.Height - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }
}
