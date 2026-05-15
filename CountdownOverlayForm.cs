using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScrollerCapture;

/// <summary>
/// Small AMOLED-themed topmost overlay showing a 3-2-1 countdown before the
/// real capture starts. Positioned at the top center of the monitor that
/// contains the supplied anchor point so it does not cover the target area.
/// </summary>
internal sealed class CountdownOverlayForm : Form
{
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private int _remaining;
    private readonly Point _anchor;
    public event EventHandler? Finished;

    public CountdownOverlayForm(int seconds, Point anchor)
    {
        _remaining = Math.Max(0, seconds);
        _anchor = anchor;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Color.Black;
        TransparencyKey = Color.FromArgb(1, 1, 1);
        Size = new Size(160, 90);
        _tick.Tick += OnTick;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080 | 0x00000020; // toolwindow + transparent (click-through)
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Rectangle bounds;
        try { bounds = Screen.FromPoint(_anchor).Bounds; }
        catch { bounds = SystemInformation.PrimaryMonitorSize.IsEmpty ? new Rectangle(0,0,800,600) : new Rectangle(Point.Empty, SystemInformation.PrimaryMonitorSize); }
        Location = new Point(bounds.Left + (bounds.Width - Width) / 2, bounds.Top + 80);
        if (_remaining <= 0)
        {
            Close();
            Finished?.Invoke(this, EventArgs.Empty);
            return;
        }
        _tick.Start();
        Invalidate();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        if (_remaining <= 0)
        {
            _tick.Stop();
            Close();
            Finished?.Invoke(this, EventArgs.Empty);
            return;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var bg = new SolidBrush(EditorTheme.Background);
        g.FillRectangle(bg, rect);
        using var border = new Pen(EditorTheme.Accent, 2f);
        g.DrawRectangle(border, rect);

        string text = _remaining.ToString();
        using var font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 32, FontStyle.Bold);
        var size = g.MeasureString(text, font);
        using var fg = new SolidBrush(EditorTheme.Accent);
        g.DrawString(text, font, fg,
            (Width - size.Width) / 2f,
            (Height - size.Height) / 2f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tick.Dispose();
        base.Dispose(disposing);
    }
}
