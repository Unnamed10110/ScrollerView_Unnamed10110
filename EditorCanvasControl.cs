using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScrollerCapture;

internal sealed class EditorCanvasControl : Control
{
    public event EventHandler? StateChanged;
    public event EventHandler? ViewportChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler<SpeechBalloonRequestEventArgs>? SpeechBalloonRequested;
    public event EventHandler<SpeechBalloonEditEventArgs>? SpeechBalloonEditRequested;
    public event EventHandler<TextRequestEventArgs>? TextRequested;

    private Bitmap _baseBitmap;
    private readonly List<EditorAnnotation> _annotations = new();
    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();
    private readonly HashSet<Bitmap> _ownedBitmaps = new();
    private int _nextStepNumber = 1;

    private float _zoom = 1.0f;
    private PointF _origin;
    private bool _viewportInitialized;

    private EditorTool _tool = EditorTool.None;
    private bool _toolDragging;
    private Point _dragStartImage;
    private Point _dragCurrentImage;

    private bool _panning;
    private Point _panAnchorScreen;
    private PointF _panAnchorOrigin;
    private bool _spaceDown;

    private EditorAnnotation? _selected;
    private HandleKind _manipHandle = HandleKind.None;
    private Point _manipStartImage;
    private bool _manipMoved;
    private HistoryEntry? _manipPreEntry;

    private const float MinZoom = 0.05f;
    private const float MaxZoom = 16.0f;
    private const int HandleScreenSize = 10;
    private const int TailHandleScreenRadius = 7;
    private const int HitToleranceImage = 6;

    private List<Rectangle> _searchHighlights = new();
    public void SetSearchHighlights(IEnumerable<Rectangle> rects)
    {
        _searchHighlights = new List<Rectangle>(rects);
        Invalidate();
    }
    public void ScrollIntoView(Rectangle imageRect)
    {
        if (imageRect.Width <= 0 || imageRect.Height <= 0) return;
        var center = new PointF(imageRect.X + imageRect.Width / 2f, imageRect.Y + imageRect.Height / 2f);
        _origin = new PointF(
            ClientSize.Width / 2f - center.X * _zoom,
            ClientSize.Height / 2f - center.Y * _zoom);
        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    public EditorCanvasControl(Bitmap baseBitmap)
    {
        _baseBitmap = baseBitmap ?? throw new ArgumentNullException(nameof(baseBitmap));
        _ownedBitmaps.Add(_baseBitmap);

        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        BackColor = EditorTheme.CanvasBackground;
        TabStop = true;
        Cursor = Cursors.Default;

        MouseEnter += (_, _) =>
        {
            if (CanFocus && !Focused) Focus();
        };
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public Bitmap BaseBitmap => _baseBitmap;
    public IReadOnlyList<EditorAnnotation> Annotations => _annotations;
    public float Zoom => _zoom;
    public int ImageWidth => _baseBitmap.Width;
    public int ImageHeight => _baseBitmap.Height;
    public bool IsDirty => _annotations.Count > 0 || _undo.Count > 0;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public EditorTool ActiveTool => _tool;
    public EditorAnnotation? Selection => _selected;
    public bool HasSelection => _selected != null;

    public IReadOnlyList<HistoryEntry> UndoHistory => _undo;
    public IReadOnlyList<HistoryEntry> RedoHistory => _redo;

    public void SetTool(EditorTool tool)
    {
        _tool = tool;
        if (tool != EditorTool.None && tool != EditorTool.Select)
        {
            SetSelected(null);
        }
        UpdateCursor();
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection() => SetSelected(null);

    public void DeleteSelected()
    {
        if (_selected == null) return;
        PushUndo("Delete " + _selected.DisplayName);
        _annotations.Remove(_selected);
        SetSelected(null);
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyStyleChanged(string label)
    {
        // Called by side-panel after mutating Selection.Style. We push an undo
        // BEFORE the mutation in the panel; for simplicity we also re-render
        // and notify state changes here.
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RecordStyleEdit(string label)
    {
        PushUndo(label);
    }

    public void SetSelectedTailDirection(TailDirection direction)
    {
        if (_selected is not SpeechBalloonAnnotation balloon) return;
        PushUndo("Move tail");
        var b = balloon.Box;
        int dist = Math.Max(40, Math.Min(b.Width, b.Height) / 2);
        balloon.TailTip = direction switch
        {
            TailDirection.Up => new Point(b.X + b.Width / 4, b.Y - dist),
            TailDirection.Down => new Point(b.X + b.Width / 4, b.Bottom + dist),
            TailDirection.Left => new Point(b.X - dist, b.Y + b.Height / 2),
            TailDirection.Right => new Point(b.Right + dist, b.Y + b.Height / 2),
            _ => balloon.TailTip,
        };
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryEditSelectedText()
    {
        switch (_selected)
        {
            case SpeechBalloonAnnotation balloon:
                {
                    var args = new SpeechBalloonEditEventArgs { Text = balloon.Text };
                    SpeechBalloonEditRequested?.Invoke(this, args);
                    if (args.Cancelled) return false;
                    PushUndo("Edit balloon text");
                    balloon.Text = args.Text ?? string.Empty;
                    Invalidate();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            case TextAnnotation text:
                {
                    var args = new TextRequestEventArgs { Text = text.Text };
                    TextRequested?.Invoke(this, args);
                    if (args.Cancelled) return false;
                    PushUndo("Edit text");
                    text.Text = args.Text ?? string.Empty;
                    Invalidate();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }
        }
        return false;
    }

    public Bitmap FlattenForOutput()
    {
        return ImageEditing.Flatten(_baseBitmap, _annotations);
    }

    public void FitToWindow()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        float zx = (ClientSize.Width - 24f) / _baseBitmap.Width;
        float zy = (ClientSize.Height - 24f) / _baseBitmap.Height;
        _zoom = Math.Max(MinZoom, Math.Min(MaxZoom, Math.Min(zx, zy)));
        CenterImage();
        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomActualSize()
    {
        _zoom = 1.0f;
        CenterImage();
        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var current = CaptureEntry("(redo)");
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        ApplySnapshot(entry.Snapshot);
        _redo.Add(current);
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var current = CaptureEntry("(undo)");
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        ApplySnapshot(entry.Snapshot);
        _undo.Add(current);
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DisposeOwnedBitmaps()
    {
        foreach (var b in _ownedBitmaps)
        {
            try { b.Dispose(); } catch { /* ignore */ }
        }
        _ownedBitmaps.Clear();
    }

    public void BringForward()
    {
        if (_selected == null) return;
        int idx = _annotations.IndexOf(_selected);
        if (idx < 0 || idx >= _annotations.Count - 1) return;
        PushUndo("Bring forward");
        _annotations.RemoveAt(idx);
        _annotations.Insert(idx + 1, _selected);
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SendBackward()
    {
        if (_selected == null) return;
        int idx = _annotations.IndexOf(_selected);
        if (idx <= 0) return;
        PushUndo("Send backward");
        _annotations.RemoveAt(idx);
        _annotations.Insert(idx - 1, _selected);
        Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------
    // Viewport
    // ------------------------------------------------------------------

    /// <summary>
    /// Centers the image at 100% zoom. Call after the host form has its final
    /// layout (e.g. from Shown + BeginInvoke) so ClientSize is correct.
    /// </summary>
    public void InitializeViewportOnShow()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        _viewportInitialized = true;
        ZoomActualSize();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        // Defer initial centering until InitializeViewportOnShow — OnLayout often
        // runs before a maximized form has its final client size.
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_viewportInitialized && ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            CenterImage();
            Invalidate();
        }
    }

    private void CenterImage()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        float sw = _baseBitmap.Width * _zoom;
        float sh = _baseBitmap.Height * _zoom;
        _origin = new PointF(
            (ClientSize.Width - sw) / 2f,
            (ClientSize.Height - sh) / 2f);
    }

    private void ZoomAround(Point screenPoint, float newZoom)
    {
        newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));
        if (Math.Abs(newZoom - _zoom) < 0.0001f) return;
        var imgPt = ScreenToImage(screenPoint);
        _zoom = newZoom;
        _origin = new PointF(
            screenPoint.X - imgPt.X * _zoom,
            screenPoint.Y - imgPt.Y * _zoom);
        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private PointF ScreenToImage(Point screen) =>
        new((screen.X - _origin.X) / _zoom, (screen.Y - _origin.Y) / _zoom);

    private Point ScreenToImageInt(Point screen)
    {
        var f = ScreenToImage(screen);
        return new Point((int)Math.Round(f.X), (int)Math.Round(f.Y));
    }

    private PointF ImageToScreen(Point image) =>
        new(image.X * _zoom + _origin.X, image.Y * _zoom + _origin.Y);

    // ------------------------------------------------------------------
    // Mouse + keyboard
    // ------------------------------------------------------------------

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            ZoomAround(e.Location, _zoom * factor);
        }
        else if ((ModifierKeys & Keys.Shift) == Keys.Shift)
        {
            _origin = new PointF(_origin.X + e.Delta * 0.5f, _origin.Y);
            Invalidate();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _origin = new PointF(_origin.X, _origin.Y + e.Delta * 0.5f);
            Invalidate();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void HandleSpaceKey(bool down)
    {
        if (_spaceDown == down) return;
        _spaceDown = down;
        UpdateCursor();
    }

    private void UpdateCursor()
    {
        if (_panning || _spaceDown)
        {
            Cursor = Cursors.SizeAll;
            return;
        }
        Cursor = _tool switch
        {
            EditorTool.None => Cursors.Default,
            EditorTool.Select => Cursors.Default,
            EditorTool.StepMarker => Cursors.Cross,
            _ => Cursors.Cross,
        };
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        bool wantPan = e.Button == MouseButtons.Middle ||
                       (e.Button == MouseButtons.Left && _spaceDown);
        if (wantPan)
        {
            _panning = true;
            _panAnchorScreen = e.Location;
            _panAnchorOrigin = _origin;
            UpdateCursor();
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        // If something is selected and the user clicked a handle, start
        // manipulation regardless of the active tool.
        if (_selected != null)
        {
            var handle = HitTestSelectedHandles(e.Location);
            if (handle == HandleKind.None && IsInsideSelectedBody(e.Location))
            {
                handle = HandleKind.Body;
            }
            if (handle != HandleKind.None)
            {
                BeginManipulation(handle, e.Location);
                return;
            }
        }

        if (_tool == EditorTool.None || _tool == EditorTool.Select)
        {
            var hit = HitTestAnnotations(e.Location);
            if (hit != null)
            {
                SetSelected(hit);
                BeginManipulation(HandleKind.Body, e.Location);
                return;
            }
            if (_selected != null)
            {
                SetSelected(null);
            }
            return;
        }

        if (_tool == EditorTool.StepMarker)
        {
            // Single-click placement.
            var p = ScreenToImageInt(e.Location);
            PushUndo("Step marker");
            var marker = new StepMarkerAnnotation
            {
                Center = ClampPoint(p),
                Number = _nextStepNumber++,
            };
            _annotations.Add(marker);
            SetSelected(marker);
            SetTool(EditorTool.Select);
            Invalidate();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _toolDragging = true;
        _dragStartImage = ScreenToImageInt(e.Location);
        _dragCurrentImage = _dragStartImage;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            _origin = new PointF(
                _panAnchorOrigin.X + (e.X - _panAnchorScreen.X),
                _panAnchorOrigin.Y + (e.Y - _panAnchorScreen.Y));
            Invalidate();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (_manipHandle != HandleKind.None)
        {
            UpdateManipulation(e.Location);
            return;
        }
        if (_toolDragging)
        {
            _dragCurrentImage = ScreenToImageInt(e.Location);
            Invalidate();
            return;
        }
        UpdateHoverCursor(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning)
        {
            _panning = false;
            UpdateCursor();
            return;
        }
        if (_manipHandle != HandleKind.None)
        {
            EndManipulation();
            return;
        }
        if (_toolDragging && e.Button == MouseButtons.Left)
        {
            _toolDragging = false;
            var rect = ImageEditing.NormalizeRect(_dragStartImage, _dragCurrentImage);
            CommitDrag(rect, _dragStartImage, _dragCurrentImage);
            Invalidate();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        var hit = HitTestAnnotations(e.Location);
        if (hit != null)
        {
            SetSelected(hit);
            TryEditSelectedText();
        }
    }

    private void UpdateHoverCursor(Point screenPoint)
    {
        if (_selected != null)
        {
            var handle = HitTestSelectedHandles(screenPoint);
            if (handle != HandleKind.None)
            {
                Cursor = CursorForHandle(handle);
                return;
            }
            if (IsInsideSelectedBody(screenPoint))
            {
                Cursor = Cursors.SizeAll;
                return;
            }
        }
        UpdateCursor();
    }

    private static Cursor CursorForHandle(HandleKind h) => h switch
    {
        HandleKind.Body => Cursors.SizeAll,
        HandleKind.BalloonTail => Cursors.Hand,
        HandleKind.ArrowStart => Cursors.Hand,
        HandleKind.ArrowEnd => Cursors.Hand,
        HandleKind.TopLeft => Cursors.SizeNWSE,
        HandleKind.BottomRight => Cursors.SizeNWSE,
        HandleKind.TopRight => Cursors.SizeNESW,
        HandleKind.BottomLeft => Cursors.SizeNESW,
        HandleKind.Top => Cursors.SizeNS,
        HandleKind.Bottom => Cursors.SizeNS,
        HandleKind.Right => Cursors.SizeWE,
        HandleKind.Left => Cursors.SizeWE,
        _ => Cursors.Default,
    };

    // ------------------------------------------------------------------
    // Selection helpers
    // ------------------------------------------------------------------

    private EditorAnnotation? HitTestAnnotations(Point screenPoint)
    {
        var img = ScreenToImageInt(screenPoint);
        int tol = Math.Max(2, (int)Math.Round(HitToleranceImage / Math.Max(0.001f, _zoom)));
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            if (_annotations[i].HitTest(img, tol)) return _annotations[i];
        }
        return null;
    }

    private bool IsInsideSelectedBody(Point screenPoint)
    {
        if (_selected == null) return false;
        var img = ScreenToImageInt(screenPoint);
        return _selected.HitTest(img, 0);
    }

    private HandleKind HitTestSelectedHandles(Point screenPoint)
    {
        if (_selected == null) return HandleKind.None;
        int half = HandleScreenSize / 2 + 2;
        foreach (var h in _selected.GetHandles())
        {
            var screen = ImageToScreen(h.ImagePoint);
            if (h.Kind == HandleKind.BalloonTail)
            {
                var dx = screenPoint.X - screen.X;
                var dy = screenPoint.Y - screen.Y;
                if (dx * dx + dy * dy <= TailHandleScreenRadius * TailHandleScreenRadius)
                    return h.Kind;
                continue;
            }
            if (Math.Abs(screenPoint.X - screen.X) <= half && Math.Abs(screenPoint.Y - screen.Y) <= half)
                return h.Kind;
        }
        return HandleKind.None;
    }

    private void SetSelected(EditorAnnotation? a)
    {
        if (_selected == a) return;
        _selected = a;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------
    // Manipulation lifecycle
    // ------------------------------------------------------------------

    private void BeginManipulation(HandleKind handle, Point screenPoint)
    {
        if (_selected == null) return;
        _manipHandle = handle;
        _manipStartImage = ScreenToImageInt(screenPoint);
        _manipMoved = false;
        _manipPreEntry = CaptureEntry(handle == HandleKind.Body
            ? "Move " + _selected.DisplayName
            : "Resize " + _selected.DisplayName);
        _selected.BeginManipulation(handle);
        Cursor = CursorForHandle(handle);
    }

    private void UpdateManipulation(Point screenPoint)
    {
        if (_selected == null || _manipHandle == HandleKind.None) return;
        var img = ScreenToImageInt(screenPoint);
        int dx = img.X - _manipStartImage.X;
        int dy = img.Y - _manipStartImage.Y;
        if (dx != 0 || dy != 0) _manipMoved = true;
        _selected.UpdateManipulation(_manipHandle, dx, dy);
        Invalidate();
    }

    private void EndManipulation()
    {
        if (_manipMoved && _manipPreEntry != null)
        {
            _undo.Add(_manipPreEntry);
            _redo.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        _manipHandle = HandleKind.None;
        _manipPreEntry = null;
        UpdateCursor();
    }

    // ------------------------------------------------------------------
    // Drawing tool commit
    // ------------------------------------------------------------------

    private void CommitDrag(Rectangle imgRect, Point start, Point end)
    {
        if (imgRect.Width < 2 && imgRect.Height < 2 && _tool != EditorTool.Arrow)
        {
            return;
        }

        switch (_tool)
        {
            case EditorTool.Rectangle:
                PushUndo("Rectangle");
                _annotations.Add(new RectangleAnnotation { Box = ClampToImage(imgRect) });
                break;
            case EditorTool.Highlight:
                PushUndo("Highlight");
                _annotations.Add(new HighlightAnnotation { Box = ClampToImage(imgRect) });
                break;
            case EditorTool.Arrow:
                if (Distance(start, end) < 3) return;
                PushUndo("Arrow");
                _annotations.Add(new ArrowAnnotation
                {
                    Start = ClampPoint(start),
                    End = ClampPoint(end),
                });
                break;
            case EditorTool.Text:
                {
                    var args = new TextRequestEventArgs();
                    TextRequested?.Invoke(this, args);
                    if (args.Cancelled) return;
                    PushUndo("Text");
                    var t = new TextAnnotation
                    {
                        Box = ClampToImage(imgRect),
                        Text = args.Text ?? string.Empty,
                    };
                    _annotations.Add(t);
                    SetSelected(t);
                    SetTool(EditorTool.Select);
                    break;
                }
            case EditorTool.SpeechBalloon:
                {
                    var args = new SpeechBalloonRequestEventArgs();
                    SpeechBalloonRequested?.Invoke(this, args);
                    if (args.Cancelled) return;
                    PushUndo("Balloon");
                    var b = new SpeechBalloonAnnotation
                    {
                        Box = ClampToImage(imgRect),
                        Text = args.Text ?? string.Empty,
                    };
                    b.TailTip = SpeechBalloonAnnotation.DefaultTailTipFor(b.Box);
                    _annotations.Add(b);
                    SetSelected(b);
                    SetTool(EditorTool.Select);
                    break;
                }
            case EditorTool.Cutout:
                PerformCutout(imgRect);
                return;
            case EditorTool.StripCutout:
                PerformStripCutout(imgRect);
                return;
            case EditorTool.Blur:
                {
                    PushUndo("Blur");
                    var blur = new BlurAnnotation { Box = ClampToImage(imgRect) };
                    _annotations.Add(blur);
                    SetSelected(blur);
                    SetTool(EditorTool.Select);
                    break;
                }
            case EditorTool.Magnifier:
                {
                    var clamped = ClampToImage(imgRect);
                    PushUndo("Magnifier");
                    var mag = new MagnifierAnnotation
                    {
                        Box = clamped,
                        Source = new Rectangle(
                            Math.Max(0, clamped.X - clamped.Width),
                            Math.Max(0, clamped.Y - clamped.Height),
                            clamped.Width / 2,
                            clamped.Height / 2),
                    };
                    _annotations.Add(mag);
                    SetSelected(mag);
                    SetTool(EditorTool.Select);
                    break;
                }
            case EditorTool.Spotlight:
                {
                    PushUndo("Spotlight");
                    var sp = new SpotlightAnnotation { Box = ClampToImage(imgRect) };
                    _annotations.Add(sp);
                    SetSelected(sp);
                    SetTool(EditorTool.Select);
                    break;
                }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (int)Math.Sqrt(dx * dx + dy * dy);
    }

    private Point ClampPoint(Point p) => new(
        Math.Max(0, Math.Min(_baseBitmap.Width - 1, p.X)),
        Math.Max(0, Math.Min(_baseBitmap.Height - 1, p.Y)));

    private Rectangle ClampToImage(Rectangle r)
    {
        var img = new Rectangle(0, 0, _baseBitmap.Width, _baseBitmap.Height);
        return Rectangle.Intersect(img, r);
    }

    private void PerformCutout(Rectangle imgRect)
    {
        var clamped = ClampToImage(imgRect);
        if (clamped.Width < 4 || clamped.Height < 4) return;

        PushUndo("Crop");

        var cropped = ImageEditing.Crop(_baseBitmap, clamped);
        _ownedBitmaps.Add(cropped);

        var dx = -clamped.X;
        var dy = -clamped.Y;
        var newAnns = new List<EditorAnnotation>();
        foreach (var a in _annotations)
        {
            var clone = a.Clone();
            clone.Move(dx, dy);
            if (clone.Bounds.IntersectsWith(new Rectangle(0, 0, cropped.Width, cropped.Height)))
            {
                newAnns.Add(clone);
            }
        }
        _annotations.Clear();
        _annotations.AddRange(newAnns);
        SetSelected(null);

        _baseBitmap = cropped;
        _viewportInitialized = false;
        OnLayout(new LayoutEventArgs(this, null));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PerformStripCutout(Rectangle imgRect)
    {
        var clamped = ClampToImage(imgRect);
        if (clamped.Width < 4 && clamped.Height < 4) return;

        PushUndo("Strip cutout");

        var newBmp = ImageEditing.StripCutout(_baseBitmap, clamped, out var strip, out var horizontal);
        if (strip.IsEmpty)
        {
            _undo.RemoveAt(_undo.Count - 1);
            newBmp.Dispose();
            return;
        }
        _ownedBitmaps.Add(newBmp);

        var newAnns = new List<EditorAnnotation>();
        foreach (var a in _annotations)
        {
            var clone = a.Clone();
            if (TryShiftAnnotationAfterStrip(clone, strip, horizontal))
            {
                newAnns.Add(clone);
            }
        }
        _annotations.Clear();
        _annotations.AddRange(newAnns);
        if (_selected != null && !_annotations.Contains(_selected))
        {
            SetSelected(null);
        }

        _baseBitmap = newBmp;
        _viewportInitialized = false;
        OnLayout(new LayoutEventArgs(this, null));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryShiftAnnotationAfterStrip(EditorAnnotation a, Rectangle strip, bool horizontal)
    {
        var b = a.Bounds;
        if (horizontal)
        {
            int top = strip.Y;
            int bottom = strip.Bottom;
            if (b.Bottom <= top) return true;
            if (b.Top >= bottom) { a.Move(0, -strip.Height); return true; }
            return false;
        }
        else
        {
            int left = strip.X;
            int right = strip.Right;
            if (b.Right <= left) return true;
            if (b.Left >= right) { a.Move(-strip.Width, 0); return true; }
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Undo/redo
    // ------------------------------------------------------------------

    /// <summary>Public history entry that the side panel can display.</summary>
    internal sealed class HistoryEntry
    {
        public string Label = string.Empty;
        public Snapshot Snapshot = new();
    }

    internal sealed class Snapshot
    {
        public Bitmap Bitmap = null!;
        public List<EditorAnnotation> Annotations = new();
        public int SelectedIndex = -1;
        public int NextStepNumber;
    }

    private HistoryEntry CaptureEntry(string label)
    {
        var snap = CaptureSnapshot();
        return new HistoryEntry { Label = label, Snapshot = snap };
    }

    private Snapshot CaptureSnapshot()
    {
        var anns = CloneAnnotations(_annotations);
        int sel = _selected != null ? _annotations.IndexOf(_selected) : -1;
        return new Snapshot
        {
            Bitmap = _baseBitmap,
            Annotations = anns,
            SelectedIndex = sel,
            NextStepNumber = _nextStepNumber,
        };
    }

    private static List<EditorAnnotation> CloneAnnotations(IEnumerable<EditorAnnotation> source)
    {
        var list = new List<EditorAnnotation>();
        foreach (var a in source) list.Add(a.Clone());
        return list;
    }

    private void PushUndo(string label)
    {
        _undo.Add(CaptureEntry(label));
        _redo.Clear();
    }

    private void ApplySnapshot(Snapshot snap)
    {
        _baseBitmap = snap.Bitmap;
        _annotations.Clear();
        _annotations.AddRange(snap.Annotations);
        _nextStepNumber = snap.NextStepNumber == 0 ? 1 : snap.NextStepNumber;
        if (snap.SelectedIndex >= 0 && snap.SelectedIndex < _annotations.Count)
        {
            _selected = _annotations[snap.SelectedIndex];
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _selected = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        if (ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            ZoomActualSize();
            _viewportInitialized = true;
        }
        else
        {
            _viewportInitialized = false;
        }
        Invalidate();
    }

    // ------------------------------------------------------------------
    // Painting
    // ------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        float sw = _baseBitmap.Width * _zoom;
        float sh = _baseBitmap.Height * _zoom;
        var imgRect = new RectangleF(_origin.X, _origin.Y, sw, sh);

        using (var shadow = new SolidBrush(EditorTheme.CanvasImageShadow))
        {
            g.FillRectangle(shadow, imgRect.X + 4, imgRect.Y + 4, imgRect.Width, imgRect.Height);
        }

        g.InterpolationMode = _zoom >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_baseBitmap, imgRect);

        var prevTransform = g.Transform;
        g.TranslateTransform(_origin.X, _origin.Y);
        g.ScaleTransform(_zoom, _zoom);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Expose base bitmap to annotations that sample it (Blur, etc.).
        var prevContext = BlurContext.CurrentBase;
        BlurContext.CurrentBase = _baseBitmap;
        try
        {
            foreach (var a in _annotations) a.Render(g);
            if (_toolDragging)
            {
                var rect = ImageEditing.NormalizeRect(_dragStartImage, _dragCurrentImage);
                DrawDragPreview(g, rect, _dragStartImage, _dragCurrentImage);
            }
            if (_searchHighlights.Count > 0)
            {
                using var hl = new SolidBrush(Color.FromArgb(120, 255, 230, 80));
                using var border = new Pen(Color.FromArgb(255, 230, 30, 30), 1.5f);
                foreach (var r in _searchHighlights)
                {
                    g.FillRectangle(hl, r);
                    g.DrawRectangle(border, r);
                }
            }
        }
        finally
        {
            BlurContext.CurrentBase = prevContext;
        }

        g.Transform = prevTransform;

        if (_selected != null)
        {
            DrawSelectionOverlay(g, _selected);
        }

        using (var pen = new Pen(EditorTheme.CanvasImageBorder))
        {
            g.DrawRectangle(pen, imgRect.X, imgRect.Y, imgRect.Width, imgRect.Height);
        }
    }

    private void DrawSelectionOverlay(Graphics g, EditorAnnotation a)
    {
        var bounds = a.Bounds;
        var tl = ImageToScreen(new Point(bounds.Left, bounds.Top));
        var br = ImageToScreen(new Point(bounds.Right, bounds.Bottom));
        var sel = RectangleF.FromLTRB(tl.X, tl.Y, br.X, br.Y);

        using var dashPen = new Pen(EditorTheme.SelectionLine, 1.6f) { DashStyle = DashStyle.Dash };
        if (sel.Width > 0 && sel.Height > 0)
        {
            g.DrawRectangle(dashPen, sel.X, sel.Y, sel.Width, sel.Height);
        }

        using var handleFill = new SolidBrush(EditorTheme.SelectionHandleFill);
        using var handlePen = new Pen(EditorTheme.SelectionHandleBorder, 1.5f);
        using var tailFill = new SolidBrush(EditorTheme.TailHandleFill);
        using var tailPen = new Pen(EditorTheme.TailHandleBorder, 1.5f);

        foreach (var h in a.GetHandles())
        {
            var screen = ImageToScreen(h.ImagePoint);
            if (h.Kind == HandleKind.BalloonTail)
            {
                int tr = TailHandleScreenRadius;
                g.FillEllipse(tailFill, screen.X - tr, screen.Y - tr, tr * 2, tr * 2);
                g.DrawEllipse(tailPen, screen.X - tr, screen.Y - tr, tr * 2, tr * 2);
            }
            else
            {
                int hs = HandleScreenSize;
                var r = new RectangleF(screen.X - hs / 2f, screen.Y - hs / 2f, hs, hs);
                g.FillRectangle(handleFill, r);
                g.DrawRectangle(handlePen, r.X, r.Y, r.Width, r.Height);
            }
        }
    }

    private void DrawDragPreview(Graphics g, Rectangle rect, Point start, Point end)
    {
        switch (_tool)
        {
            case EditorTool.Rectangle:
                using (var fill = new SolidBrush(Color.FromArgb(50, 255, 0, 0)))
                using (var pen = new Pen(Color.Red, 3f))
                {
                    g.FillRectangle(fill, rect);
                    g.DrawRectangle(pen, rect);
                }
                break;
            case EditorTool.Highlight:
                using (var fill = new SolidBrush(Color.FromArgb(110, 255, 235, 60)))
                {
                    g.FillRectangle(fill, rect);
                }
                break;
            case EditorTool.Arrow:
                using (var pen = new Pen(Color.Red, 4f) { EndCap = LineCap.ArrowAnchor })
                {
                    try { pen.CustomEndCap = new AdjustableArrowCap(4.5f, 5.5f, true); } catch { }
                    g.DrawLine(pen, start, end);
                }
                break;
            case EditorTool.SpeechBalloon:
            case EditorTool.Text:
                using (var pen = new Pen(Color.White, 2f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawRectangle(pen, rect);
                }
                break;
            case EditorTool.Cutout:
                using (var pen = new Pen(Color.LimeGreen, 2f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawRectangle(pen, rect);
                }
                break;
            case EditorTool.StripCutout:
                DrawStripCutoutPreview(g, rect);
                break;
            case EditorTool.Blur:
                using (var pen = new Pen(Color.DeepSkyBlue, 2f) { DashStyle = DashStyle.Dash })
                using (var fill = new SolidBrush(Color.FromArgb(40, 0, 150, 255)))
                {
                    g.FillRectangle(fill, rect);
                    g.DrawRectangle(pen, rect);
                }
                break;
            case EditorTool.Spotlight:
                using (var pen = new Pen(Color.Orange, 2f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawEllipse(pen, rect);
                }
                break;
            case EditorTool.Magnifier:
                using (var pen = new Pen(Color.MediumPurple, 2f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawEllipse(pen, rect);
                }
                break;
        }
    }

    private void DrawStripCutoutPreview(Graphics g, Rectangle rect)
    {
        if (rect.Width <= 0 && rect.Height <= 0) return;
        bool horizontal = rect.Width >= rect.Height;
        Rectangle strip;
        if (horizontal)
        {
            strip = new Rectangle(0, rect.Y, _baseBitmap.Width, rect.Height);
        }
        else
        {
            strip = new Rectangle(rect.X, 0, rect.Width, _baseBitmap.Height);
        }
        using var fill = new SolidBrush(Color.FromArgb(120, 230, 90, 90));
        using var pen = new Pen(Color.Crimson, 2f) { DashStyle = DashStyle.Dash };
        g.FillRectangle(fill, strip);
        g.DrawRectangle(pen, strip);
        using var dragPen = new Pen(Color.White, 1.4f) { DashStyle = DashStyle.Dot };
        g.DrawRectangle(dragPen, rect);
    }
}

internal enum TailDirection { Up, Down, Left, Right }

internal sealed class SpeechBalloonRequestEventArgs : EventArgs
{
    public bool Cancelled { get; set; }
    public string? Text { get; set; }
}

internal sealed class SpeechBalloonEditEventArgs : EventArgs
{
    public string? Text { get; set; }
    public bool Cancelled { get; set; }
}

internal sealed class TextRequestEventArgs : EventArgs
{
    public string? Text { get; set; }
    public bool Cancelled { get; set; }
}
