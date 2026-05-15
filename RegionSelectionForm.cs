using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScrollerCapture;

internal enum RegionSelectionSource { None, ManualDrag, UiPreselect }

internal sealed class RegionSelectionResult
{
    public Rectangle Region;
    public RegionSelectionSource Source;
    public UiCandidate? Candidate;
    public bool Cancelled;
}

/// <summary>
/// Fullscreen drag selection overlay with a deep black dim over the virtual screen.
/// Supports UIA preselection on hover. Drag-to-select overrides preselection.
/// </summary>
internal sealed class RegionSelectionForm : Form
{
    /// <summary>Form-level opacity for the dim veil (higher = darker, less gray haze).</summary>
    private const double OverlayDimOpacity = 0.68;

    private const int DirtyPadding = 12;
    private const int HintBottomMargin = 28;
    private const int HintStripHeight = 36;

    private Point _start;
    private Point _current;
    private bool _dragging;
    private bool _draggingMoved;

    private readonly System.Windows.Forms.Timer _uiaThrottle;
    private Point _lastMouseScreen;
    private bool _uiaQueryPending;
    private List<UiCandidate> _candidates = new();
    private int _candidateIndex = -1;
    private Rectangle _preselectRect = Rectangle.Empty;

    private Rectangle _lastDirtyBounds = Rectangle.Empty;

    private static readonly Font s_frameFont = new(SystemFonts.MessageBoxFont!.FontFamily, 9.5f, FontStyle.Bold);
    private static readonly Font s_hintFont = new(SystemFonts.MessageBoxFont!.FontFamily, 9.5f, FontStyle.Regular);

    public RegionSelectionResult Result { get; } = new() { Cancelled = true };

    /// <summary>Backward compat: rectangle of the selection in virtual screen coords.</summary>
    public Rectangle SelectedRegion => Result.Region;

    public RegionSelectionForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;

        Bounds = SystemInformation.VirtualScreen;

        BackColor = Color.Black;
        Opacity = OverlayDimOpacity;

        KeyPreview = true;
        KeyDown += OnKeyDown;

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        Paint += OnPaint;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);

        _uiaThrottle = new System.Windows.Forms.Timer { Interval = 90 };
        _uiaThrottle.Tick += (_, _) => RunUiaQuery();
    }

    protected override bool ShowWithoutActivation => false;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Suppress default erase; OnPaint fills the clip region.
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _lastDirtyBounds = Rectangle.Empty;
        Invalidate(true);
        _uiaThrottle.Start();
        if (NativeMethods.GetCursorPos(out var pt))
        {
            _lastMouseScreen = new Point(pt.X, pt.Y);
            _uiaQueryPending = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiaThrottle.Stop();
            _uiaThrottle.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Invalidate only regions that changed; never call Update().</summary>
    private void InvalidateDirty(Rectangle newBounds)
    {
        if (newBounds.Width <= 0 || newBounds.Height <= 0)
        {
            newBounds = Rectangle.Empty;
        }

        Rectangle dirty;
        if (_lastDirtyBounds.Width > 0 && newBounds.Width > 0)
        {
            dirty = Rectangle.Union(_lastDirtyBounds, newBounds);
        }
        else if (newBounds.Width > 0)
        {
            dirty = newBounds;
        }
        else if (_lastDirtyBounds.Width > 0)
        {
            dirty = _lastDirtyBounds;
        }
        else
        {
            dirty = ClientRectangle;
        }

        dirty = InflateClamped(dirty, DirtyPadding);
        _lastDirtyBounds = newBounds.Width > 0 ? InflateClamped(newBounds, DirtyPadding) : Rectangle.Empty;
        Invalidate(dirty);
    }

    private Rectangle InflateClamped(Rectangle r, int pad)
    {
        var inflated = Rectangle.Inflate(r, pad, pad);
        return Rectangle.Intersect(inflated, ClientRectangle);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Capture = false;
            Result.Cancelled = true;
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.KeyCode == Keys.Tab && !_dragging && _candidates.Count > 0)
        {
            var oldBounds = ComputeOverlayBounds();
            int delta = (e.Modifiers & Keys.Shift) == Keys.Shift ? -1 : 1;
            _candidateIndex = Math.Max(0, Math.Min(_candidates.Count - 1, _candidateIndex + delta));
            _preselectRect = _candidates[_candidateIndex].Bounds;
            var newBounds = ComputeOverlayBounds();
            InvalidateDirty(Rectangle.Union(oldBounds, newBounds));
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Enter && !_dragging && _candidateIndex >= 0 && _candidateIndex < _candidates.Count)
        {
            AcceptPreselection();
            e.Handled = true;
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var oldBounds = ComputeOverlayBounds();
        if (!_dragging)
        {
            oldBounds = Rectangle.Union(oldBounds, GetBottomHintStripBounds());
        }

        Capture = true;
        _dragging = true;
        _draggingMoved = false;
        _start = e.Location;
        _current = e.Location;

        var newBounds = ComputeOverlayBounds();
        InvalidateDirty(Rectangle.Union(oldBounds, newBounds));
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            if (e.Location == _current) return;
            _draggingMoved = true;
            var oldBounds = ComputeOverlayBounds();
            _current = e.Location;
            var newBounds = ComputeOverlayBounds();
            InvalidateDirty(Rectangle.Union(oldBounds, newBounds));
            return;
        }

        var screen = PointToScreen(e.Location);
        if (screen != _lastMouseScreen)
        {
            _lastMouseScreen = screen;
            _uiaQueryPending = true;
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging) return;
        Capture = false;
        _dragging = false;

        if (!_draggingMoved)
        {
            if (_candidateIndex >= 0 && _candidateIndex < _candidates.Count)
            {
                AcceptPreselection();
                return;
            }
            Result.Cancelled = true;
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        var rectLocal = MakeRect(_start, _current);
        if (rectLocal.Width < 8 || rectLocal.Height < 8)
        {
            Result.Cancelled = true;
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        var virt = SystemInformation.VirtualScreen;
        Result.Region = new Rectangle(
            virt.X + rectLocal.X,
            virt.Y + rectLocal.Y,
            rectLocal.Width,
            rectLocal.Height);
        Result.Source = RegionSelectionSource.ManualDrag;
        Result.Cancelled = false;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AcceptPreselection()
    {
        var c = _candidates[_candidateIndex];
        Result.Region = c.Bounds;
        Result.Candidate = c;
        Result.Source = RegionSelectionSource.UiPreselect;
        Result.Cancelled = false;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RunUiaQuery()
    {
        if (!_uiaQueryPending || _dragging) return;
        _uiaQueryPending = false;

        var oldBounds = ComputeOverlayBounds();
        var pt = _lastMouseScreen;
        var list = UiElementDetector.FindCandidatesAt(pt);
        _candidates = list;
        _candidateIndex = UiElementDetector.FindDefaultIndex(list);
        _preselectRect = _candidateIndex >= 0 ? list[_candidateIndex].Bounds : Rectangle.Empty;
        var newBounds = ComputeOverlayBounds();
        InvalidateDirty(Rectangle.Union(oldBounds, newBounds));
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        PaintOverlay(e.Graphics, e.ClipRectangle);
    }

    private void PaintOverlay(Graphics g, Rectangle clip)
    {
        g.SetClip(clip);

        // Redraw dim in the dirty region only (form Opacity applies to the window).
        using (var dim = new SolidBrush(BackColor))
        {
            g.FillRectangle(dim, clip);
        }

        if (_dragging)
        {
            var rect = MakeRect(_start, _current);
            if (rect.Width > 0 && rect.Height > 0)
            {
                DrawSelectionFrame(g, rect);
                DrawSizePill(g, rect);
            }
            return;
        }

        if (_preselectRect.Width > 0 && _preselectRect.Height > 0)
        {
            var rect = ToClient(_preselectRect);
            DrawSelectionFrame(g, rect);
            DrawSizePill(g, rect);
            DrawBottomHint(g, "Click to capture · Tab to cycle · Esc to cancel");
        }
        else
        {
            DrawBottomHint(g, "Drag to select · Esc to cancel");
        }
    }

    /// <summary>Bounds of selection chrome + bottom hint (for dirty invalidation).</summary>
    private Rectangle ComputeOverlayBounds()
    {
        Rectangle bounds = Rectangle.Empty;

        if (_dragging)
        {
            var rect = MakeRect(_start, _current);
            if (rect.Width > 0 && rect.Height > 0)
            {
                bounds = Rectangle.Union(bounds, GetSelectionChromeBounds(rect));
            }
            return bounds;
        }

        if (_preselectRect.Width > 0 && _preselectRect.Height > 0)
        {
            bounds = Rectangle.Union(bounds, GetSelectionChromeBounds(ToClient(_preselectRect)));
        }

        bounds = Rectangle.Union(bounds, GetBottomHintStripBounds());
        return bounds;
    }

    private Rectangle GetSelectionChromeBounds(Rectangle selection)
    {
        if (selection.Width <= 0 || selection.Height <= 0) return Rectangle.Empty;
        var label = $"{selection.Width} × {selection.Height}";
        var size = TextRenderer.MeasureText(label, s_frameFont);
        int pillH = size.Height + 6;
        int pillW = size.Width + 12;
        int lx = selection.X;
        int ly = Math.Max(0, selection.Y - pillH - 4);
        var pill = new Rectangle(lx, ly, pillW, pillH);
        return Rectangle.Union(selection, pill);
    }

    private Rectangle GetBottomHintStripBounds()
    {
        if (_dragging || ClientSize.Height <= 0) return Rectangle.Empty;
        int y = Math.Max(0, ClientSize.Height - HintBottomMargin - HintStripHeight);
        return new Rectangle(0, y, ClientSize.Width, HintStripHeight + HintBottomMargin);
    }

    private Rectangle ToClient(Rectangle screenRect)
    {
        var virt = SystemInformation.VirtualScreen;
        return new Rectangle(screenRect.X - virt.X, screenRect.Y - virt.Y, screenRect.Width, screenRect.Height);
    }

    private static void DrawSelectionFrame(Graphics g, Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // Soft outer shadow, then crisp white frame.
        var shadow = rect;
        shadow.Offset(1, 1);
        using (var shadowPen = new Pen(Color.FromArgb(160, 0, 0, 0), 2f))
        {
            g.DrawRectangle(shadowPen, shadow);
        }
        using (var white = new Pen(Color.White, 2f) { LineJoin = LineJoin.Miter })
        {
            g.DrawRectangle(white, rect);
        }
    }

    private static void DrawSizePill(Graphics g, Rectangle rect)
    {
        var label = $"{rect.Width} × {rect.Height}";
        var size = TextRenderer.MeasureText(label, s_frameFont);
        int pillH = size.Height + 6;
        int pillW = size.Width + 12;
        int lx = rect.X;
        int ly = Math.Max(0, rect.Y - pillH - 4);
        var pillRect = new Rectangle(lx, ly, pillW, pillH);

        using var path = RoundedRect(pillRect, 4);
        using (var bg = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
        {
            g.FillPath(bg, path);
        }
        TextRenderer.DrawText(
            g,
            label,
            s_frameFont,
            pillRect,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawBottomHint(Graphics g, string text)
    {
        if (ClientSize.Height <= 0 || ClientSize.Width <= 0) return;

        int stripH = HintStripHeight;
        int y = Math.Max(0, ClientSize.Height - HintBottomMargin - stripH);

        var textSize = TextRenderer.MeasureText(text, s_hintFont);
        int pillW = textSize.Width + 20;
        int pillH = textSize.Height + 8;
        int px = (ClientSize.Width - pillW) / 2;
        int py = y + (stripH - pillH) / 2;
        var pill = new Rectangle(px, py, pillW, pillH);

        using var path = RoundedRect(pill, 6);
        using (var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
        {
            g.FillPath(bg, path);
        }
        using (var accent = new Pen(EditorTheme.Accent, 1f))
        {
            g.DrawPath(accent, path);
        }
        TextRenderer.DrawText(
            g,
            text,
            s_hintFont,
            pill,
            EditorTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Capture = false;
        base.OnFormClosed(e);
    }

    private static Rectangle MakeRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(a.X - b.X);
        var h = Math.Abs(a.Y - b.Y);
        return new Rectangle(x, y, w, h);
    }
}
