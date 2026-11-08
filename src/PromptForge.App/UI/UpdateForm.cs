using System.Drawing.Drawing2D;
using System.Drawing.Text;
using PromptForge.App.Native;
using PromptForge.Core.Updates;

namespace PromptForge.App.UI;

/// <summary>
/// The update card: offers a new version, then shows download progress, then any failure.
/// A small dark card in the bottom-right corner that never takes keyboard focus, so typing continues in the
/// user's app; the buttons still work with the mouse. It only reports clicks: UpdateController does the work.
/// </summary>
internal sealed class UpdateForm : Form
{
    private enum Mode { Offer, Working, Starting, Failed }

    private sealed record Button(string Text, Action? Click, bool Primary, Rectangle Bounds = default);

    private static readonly Color Surface = Color.FromArgb(24, 24, 31);
    private static readonly Color Edge = Color.FromArgb(52, 52, 66);
    private static readonly Color Track = Color.FromArgb(46, 46, 58);
    private static readonly Color TextMain = Color.FromArgb(245, 245, 250);
    private static readonly Color TextSubtle = Color.FromArgb(158, 158, 176);
    private static readonly Color AccentFrom = Color.FromArgb(124, 92, 255);
    private static readonly Color AccentTo = Color.FromArgb(34, 211, 238);
    private static readonly Color ErrorColor = Color.FromArgb(255, 107, 107);

    private const int CardWidth = 360, Pad = 20, Radius = 14, BarHeight = 6, ButtonHeight = 32, TickMs = 16;
    private const double FadeStep = 0.12, EaseFactor = 0.22;

    private readonly System.Windows.Forms.Timer _tick = new() { Interval = TickMs };
    private readonly Font _titleFont = new("Segoe UI Semibold", 11.5f);
    private readonly Font _bodyFont = new("Segoe UI", 9.5f);
    private readonly Font _buttonFont = new("Segoe UI Semibold", 9.5f);

    private Mode _mode = Mode.Offer;
    private string _title = "", _detail = "", _percentText = "";
    private bool _barVisible, _indeterminate, _isError;
    private double _target, _shown, _sweep;
    private List<Button> _buttons = [];
    private int _hover = -1;
    private Rectangle _barRect, _titleRect, _detailRect;

    public UpdateForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Surface;
        ForeColor = TextMain;
        DoubleBuffered = true;
        Opacity = 0;
        AccessibleRole = AccessibleRole.Alert;
        _tick.Tick += (_, _) => Animate();
    }

    /// <summary>The user chose to update, or to try again after a failure.</summary>
    public event Action? UpdateClicked;
    /// <summary>The user dismissed the offer ("Later").</summary>
    public event Action? LaterClicked;
    /// <summary>The user cancelled the download, or closed a failure.</summary>
    public event Action? CancelClicked;
    public event Action? ReleasePageClicked;

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

    public void ShowOffer(string version, string current)
    {
        Set(Mode.Offer, $"PromptForge {version} is available", $"You have {current}. Your settings and API keys are kept.", bar: false);
        _buttons = [new("Update now", () => UpdateClicked?.Invoke(), true), new("Later", () => LaterClicked?.Invoke(), false)];
        Present();
    }

    public void ShowWorking(string version)
    {
        _target = 0;
        _shown = 0;
        Set(Mode.Working, $"Downloading {version}", "Getting ready…", bar: true, indeterminate: true);
        _buttons = [new("Cancel", () => CancelClicked?.Invoke(), false)];
        Present();
    }

    public void SetProgress(string version, UpdateProgress progress)
    {
        if (_mode != Mode.Working) return;
        switch (progress.Stage)
        {
            case UpdateStage.Preparing:
                Set(Mode.Working, $"Downloading {version}", "Getting ready…", bar: true, indeterminate: true);
                break;
            case UpdateStage.Downloading when progress.Percent is { } percent:
                _target = percent;
                _percentText = $"{percent}%";
                Set(Mode.Working, $"Downloading {version}", progress.Describe(), bar: true, indeterminate: false);
                break;
            case UpdateStage.Downloading:
                Set(Mode.Working, $"Downloading {version}", progress.Describe(), bar: true, indeterminate: true);
                break;
            default:
                Set(Mode.Working, "Checking the download", "Making sure the file is exactly what was published…", bar: true, indeterminate: true);
                break;
        }
        _buttons = [new("Cancel", () => CancelClicked?.Invoke(), false)];
        Present();
    }

    public void ShowStarting()
    {
        _target = 100;
        _percentText = "100%";
        Set(Mode.Starting, "Verified", "Opening setup…", bar: true, indeterminate: false);
        _buttons = [];
        Present();
    }

    public void ShowFailed(string message, bool canOpenReleasePage)
    {
        Set(Mode.Failed, "Couldn't update", message, bar: false, error: true);
        _buttons = [new("Try again", () => UpdateClicked?.Invoke(), true), new("Close", () => CancelClicked?.Invoke(), false)];
        if (canOpenReleasePage) _buttons.Insert(1, new("Release page", () => ReleasePageClicked?.Invoke(), false));
        Present();
    }

    private void Set(Mode mode, string title, string detail, bool bar, bool indeterminate = false, bool error = false)
    {
        _mode = mode;
        _title = title;
        _detail = detail;
        _barVisible = bar;
        _indeterminate = indeterminate;
        _isError = error;
        if (indeterminate || !bar) _percentText = "";
        AccessibleName = $"{title}. {detail}";
    }

    private int S(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    /// <summary>Sizes the card to its text, keeps it in the bottom-right corner, and starts animating.</summary>
    private void Present()
    {
        var width = S(CardWidth);
        var textWidth = width - S(Pad) * 2;
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Left;
        var detailHeight = TextRenderer.MeasureText(_detail, _bodyFont, new Size(textWidth, 0), flags).Height;

        var y = S(Pad - 2);
        _titleRect = new Rectangle(S(Pad), y, textWidth, _titleFont.Height);
        y = _titleRect.Bottom + S(4);
        _detailRect = new Rectangle(S(Pad), y, textWidth, detailHeight);
        y = _detailRect.Bottom + S(14);
        if (_barVisible)
        {
            _barRect = new Rectangle(S(Pad), y, textWidth, S(BarHeight));
            y = _barRect.Bottom + S(16);
        }

        if (_buttons.Count > 0)
        {
            var x = width - S(Pad);
            var layout = new List<Button>();
            for (var i = _buttons.Count - 1; i >= 0; i--)
            {
                var b = _buttons[i];
                var w = TextRenderer.MeasureText(b.Text, _buttonFont).Width + S(28);
                x -= w;
                layout.Insert(0, b with { Bounds = new Rectangle(x, y, w, S(ButtonHeight)) });
                x -= S(8);
            }
            _buttons = layout;
            y += S(ButtonHeight);
        }

        var height = y + S(Pad - 2);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var size = new Size(width, height);
        if (Size != size || !Visible)
        {
            Size = size;
            Location = new Point(area.Right - width - S(16), area.Bottom - height - S(16));
            Region?.Dispose();
            Region = RoundedRegion(size, S(Radius));
        }

        if (!Visible) Show();
        _tick.Start();
        Invalidate();
    }

    private void Animate()
    {
        var busy = false;
        if (Opacity < 1)
        {
            Opacity = Math.Min(1, Opacity + FadeStep);
            busy = true;
        }
        if (_barVisible)
        {
            if (_indeterminate)
            {
                _sweep = (_sweep + 0.012) % 1.0;
                busy = true;
            }
            else if (Math.Abs(_target - _shown) > 0.05)
            {
                _shown += (_target - _shown) * EaseFactor;
                busy = true;
            }
            else
            {
                _shown = _target;
            }
            Invalidate(Rectangle.Inflate(_barRect, S(2), S(2)));
        }
        if (!busy) _tick.Stop();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var border = new Pen(Edge))
        using (var path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), S(Radius)))
            g.DrawPath(border, path);

        var flags = TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.WordBreak;
        TextRenderer.DrawText(g, _title, _titleFont, _titleRect, _isError ? ErrorColor : TextMain, flags | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _detail, _bodyFont, _detailRect, TextSubtle, flags);
        if (_percentText.Length > 0)
            TextRenderer.DrawText(g, _percentText, _titleFont, _titleRect, AccentTo, TextFormatFlags.NoPadding | TextFormatFlags.Right);

        if (_barVisible) PaintBar(g);
        for (var i = 0; i < _buttons.Count; i++) PaintButton(g, _buttons[i], i == _hover);
    }

    private void PaintBar(Graphics g)
    {
        var radius = _barRect.Height;
        using (var track = new SolidBrush(Track))
        using (var trackPath = RoundedPath(_barRect, radius))
            g.FillPath(track, trackPath);

        Rectangle fill;
        if (_indeterminate)
        {
            // A segment that glides across the track and fades in and out at its ends.
            var segment = _barRect.Width * 2 / 5;
            var travel = _barRect.Width + segment;
            var left = _barRect.Left - segment + (int)(_sweep * travel);
            var clipped = Rectangle.Intersect(new Rectangle(left, _barRect.Top, segment, _barRect.Height), _barRect);
            if (clipped.Width < 2) return;
            fill = clipped;
        }
        else
        {
            var width = (int)(_barRect.Width * Math.Clamp(_shown, 0, 100) / 100.0);
            if (width < 2) return;
            fill = new Rectangle(_barRect.Left, _barRect.Top, width, _barRect.Height);
        }

        using var brush = new LinearGradientBrush(_barRect, AccentFrom, AccentTo, LinearGradientMode.Horizontal);
        using var path = RoundedPath(fill, radius);
        g.FillPath(brush, path);
    }

    private void PaintButton(Graphics g, Button b, bool hover)
    {
        using var path = RoundedPath(b.Bounds, S(8));
        if (b.Primary)
        {
            using var brush = new LinearGradientBrush(b.Bounds, hover ? Lighten(AccentFrom) : AccentFrom, hover ? Lighten(AccentTo) : AccentTo, LinearGradientMode.Horizontal);
            g.FillPath(brush, path);
        }
        else
        {
            using var fill = new SolidBrush(hover ? Color.FromArgb(44, 44, 56) : Color.FromArgb(34, 34, 44));
            using var pen = new Pen(Edge);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, b.Text, _buttonFont, b.Bounds, b.Primary ? Color.White : TextMain,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = _buttons.FindIndex(b => b.Bounds.Contains(e.Location));
        if (index == _hover) return;
        _hover = index;
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover < 0) return;
        _hover = -1;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var button = _buttons.Find(b => b.Bounds.Contains(e.Location));
        button?.Click?.Invoke();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Visible) Present();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Dispose();
            _titleFont.Dispose();
            _bodyFont.Dispose();
            _buttonFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Color Lighten(Color c) => Color.FromArgb(Math.Min(255, c.R + 24), Math.Min(255, c.G + 24), Math.Min(255, c.B + 24));

    private static Region RoundedRegion(Size size, int radius)
    {
        using var path = RoundedPath(new Rectangle(0, 0, size.Width, size.Height), radius);
        return new Region(path);
    }

    private static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        var d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
