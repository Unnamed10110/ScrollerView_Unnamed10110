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
/// ShareX-style element detection: the UI element directly under the cursor is
/// highlighted as you hover; mouse wheel or Tab widens/narrows the selection
/// through the element's ancestor chain. Drag-to-select overrides detection.
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

    // Marching-ants animation for the element highlight border.
    private readonly System.Windows.Forms.Timer _antsTimer;
    private float _dashPhase;
    private static readonly float[] s_dashPattern = { 5f, 4f };
    private const int FrameThickness = 2;

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
        MouseWheel += OnMouseWheel;
        Paint += OnPaint;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);

        _uiaThrottle = new System.Windows.Forms.Timer { Interval = 90 };
        _uiaThrottle.Tick += (_, _) => RunUiaQuery();

        _antsTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _antsTimer.Tick += (_, _) => AnimateAnts();
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
        _antsTimer.Start();
        if (NativeMethods.GetCursorPos(out var pt))
        {
            _lastMouseScreen = new Point(pt.X, pt.Y);
            _uiaQueryPending = true;
        }
    }

    /// <summary>
    /// Advances the dashed border phase and repaints only the element's border
    /// edges (cheap), producing a marching-ants effect.
    /// </summary>
    private void AnimateAnts()
    {
        if (_dragging) return;
        if (_preselectRect.Width <= 0 || _preselectRect.Height <= 0) return;

        _dashPhase -= 1f;
        if (_dashPhase <= -1000f) _dashPhase = 0f;

        var rect = ToClient(_preselectRect);
        foreach (var strip in GetFrameEdgeStrips(rect))
        {
            Invalidate(strip);
        }
    }

    /// <summary>Four thin rectangles covering the element's border edges.</summary>
    private static IEnumerable<Rectangle> GetFrameEdgeStrips(Rectangle rect)
    {
        int t = FrameThickness + 3;
        yield return new Rectangle(rect.X - t, rect.Y - t, rect.Width + 2 * t, t * 2);            // top
        yield return new Rectangle(rect.X - t, rect.Bottom - t, rect.Width + 2 * t, t * 2);       // bottom
        yield return new Rectangle(rect.X - t, rect.Y - t, t * 2, rect.Height + 2 * t);           // left
        yield return new Rectangle(rect.Right - t, rect.Y - t, t * 2, rect.Height + 2 * t);       // right
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiaThrottle.Stop();
            _uiaThrottle.Dispose();
            _antsTimer.Stop();
            _antsTimer.Dispose();
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
            int delta = (e.Modifiers & Keys.Shift) == Keys.Shift ? -1 : 1;
            CycleCandidate(delta);
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

    /// <summary>
    /// ShareX-style depth control: wheel up widens the selection to the parent
    /// element, wheel down narrows it back to the deeper child.
    /// </summary>
    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        if (_dragging || _candidates.Count == 0) return;
        CycleCandidate(e.Delta > 0 ? 1 : -1);
    }

    private void CycleCandidate(int delta)
    {
        if (_candidates.Count == 0) return;
        var oldBounds = ComputeOverlayBounds();
        _candidateIndex = Math.Max(0, Math.Min(_candidates.Count - 1, _candidateIndex + delta));
        _preselectRect = _candidates[_candidateIndex].Bounds;
        var newBounds = ComputeOverlayBounds();
        InvalidateDirty(Rectangle.Union(oldBounds, newBounds));
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

        // Make the overlay click-through for the duration of the hit test so
        // AutomationElement.FromPoint resolves to the window underneath instead
        // of our own fullscreen overlay. Restore immediately afterwards.
        List<UiCandidate> list;
        SetClickThrough(true);
        try
        {
            list = UiElementDetector.FindCandidatesAt(pt);
        }
        finally
        {
            SetClickThrough(false);
        }

        _candidates = list;
        _candidateIndex = UiElementDetector.FindDefaultIndex(list);
        _preselectRect = _candidateIndex >= 0 ? list[_candidateIndex].Bounds : Rectangle.Empty;
        var newBounds = ComputeOverlayBounds();
        InvalidateDirty(Rectangle.Union(oldBounds, newBounds));
    }

    /// <summary>
    /// Toggles WS_EX_TRANSPARENT so the overlay does not intercept hit testing
    /// while we resolve the UI element under the cursor. The form is already a
    /// layered window (Opacity &lt; 1), so the transparent bit makes it
    /// click-through for the brief query window.
    /// </summary>
    private void SetClickThrough(bool enable)
    {
        if (!IsHandleCreated) return;
        try
        {
            long ex = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            long updated = enable
                ? ex | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                : ex & ~(long)NativeMethods.WS_EX_TRANSPARENT;
            if (updated != ex)
            {
                NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE, (IntPtr)updated);
            }
        }
        catch
        {
            // ignore — detection just falls back to whatever FromPoint returns
        }
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

        if (_candidateIndex >= 0 && _candidateIndex < _candidates.Count
            && _preselectRect.Width > 0 && _preselectRect.Height > 0)
        {
            DrawElementHighlight(g);
            var c = _candidates[_candidateIndex];
            DrawBottomHint(g,
                $"{c.Display} [{_candidateIndex + 1}/{_candidates.Count}] · Click = capture · Wheel/Tab = widen/narrow · Drag = manual · Esc = cancel");
        }
        else
        {
            DrawBottomHint(g, "Drag to select · Esc to cancel");
        }
    }

    /// <summary>
    /// ShareX-style highlight: the element under the cursor gets a faint
    /// translucent fill plus an animated marching-ants dashed border.
    /// </summary>
    private void DrawElementHighlight(Graphics g)
    {
        var rect = ToClient(_preselectRect);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // Subtle fill so the element reads as "lit" without washing out content.
        using (var fill = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
        {
            g.FillRectangle(fill, rect);
        }

        DrawDashedFrame(g, rect);
        DrawSizePill(g, rect);
    }

    /// <summary>
    /// Animated marching-ants frame: a solid dark backing line for contrast on
    /// any background, with a moving white dashed line on top.
    /// </summary>
    private void DrawDashedFrame(Graphics g, Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var prevMode = g.SmoothingMode;
        // Crisp 1px-aligned lines look better for thin dashes than AA.
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // Dark backing line so the dashes are visible over light content too.
        using (var backing = new Pen(Color.FromArgb(150, 0, 0, 0), FrameThickness + 1f))
        {
            g.DrawRectangle(backing, rect);
        }

        using (var ants = new Pen(Color.White, FrameThickness)
        {
            DashStyle = DashStyle.Custom,
            DashPattern = s_dashPattern,
            DashOffset = _dashPhase,
            LineJoin = LineJoin.Miter,
        })
        {
            g.DrawRectangle(ants, rect);
        }

        g.SmoothingMode = prevMode;
    }

    /// <summary>Bounds of selection chrome + active element highlight + bottom hint (for dirty invalidation).</summary>
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
            bounds = GetSelectionChromeBounds(ToClient(_preselectRect));
        }

        bounds = bounds.IsEmpty
            ? GetBottomHintStripBounds()
            : Rectangle.Union(bounds, GetBottomHintStripBounds());
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
        // Inflate so the frame/backing pen (which straddles the edge) is fully covered.
        var frame = Rectangle.Inflate(selection, FrameThickness + 2, FrameThickness + 2);
        return Rectangle.Union(frame, pill);
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
