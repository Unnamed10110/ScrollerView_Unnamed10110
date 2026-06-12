using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScrollerCapture;

/// <summary>
/// Logical handle slot exposed by an annotation. Most rectangular shapes
/// expose the 8 box handles; arrows expose start/end; balloons add a tail
/// handle. Handles are mainly hit-test and cursor hints; the actual
/// manipulation math lives inside each annotation's <c>UpdateManipulation</c>.
/// </summary>
internal enum HandleKind
{
    None,
    TopLeft, Top, TopRight,
    Right,
    BottomRight, Bottom, BottomLeft,
    Left,
    Body, // whole-shape move
    ArrowStart, ArrowEnd,
    BalloonTail,
}

internal readonly struct AnnotationHandle
{
    public AnnotationHandle(HandleKind kind, Point imagePoint)
    {
        Kind = kind;
        ImagePoint = imagePoint;
    }
    public HandleKind Kind { get; }
    public Point ImagePoint { get; }
}

/// <summary>
/// Visual style shared by most annotations. Each subclass picks which
/// fields it actually uses; the others are ignored. Keeping them on one
/// class lets the side-panel show a consistent set of controls.
/// </summary>
internal sealed class AnnotationStyle
{
    public Color StrokeColor { get; set; } = Color.FromArgb(255, 230, 30, 30);
    public Color FillColor { get; set; } = Color.FromArgb(50, 255, 0, 0);
    public Color TextColor { get; set; } = Color.Black;
    public float StrokeWidth { get; set; } = 3f;
    public float FontSize { get; set; } = 14f;
    public string FontFamily { get; set; } = "Segoe UI";
    public int BlurRadius { get; set; } = 12;

    public AnnotationStyle Clone() => (AnnotationStyle)MemberwiseClone();
}

internal abstract class EditorAnnotation
{
    public AnnotationStyle Style { get; set; } = new();

    public abstract string DisplayName { get; }
    public abstract Rectangle Bounds { get; }

    // ---- Style capability flags ---------------------------------------
    // Used by the properties side panel to decide which controls to show
    // for the active tool / selected annotation.
    public virtual bool UsesStroke => false;
    public virtual bool UsesFill => false;
    public virtual bool UsesTextColor => false;
    public virtual bool UsesFontSize => false;
    public virtual bool UsesBlur => false;
    public abstract void Render(Graphics g);
    public abstract bool HitTest(Point imagePoint, int tolerance);
    public abstract void Move(int dx, int dy);
    public abstract IEnumerable<AnnotationHandle> GetHandles();

    /// <summary>Snapshot internal state at the start of a drag.</summary>
    public abstract void BeginManipulation(HandleKind kind);

    /// <summary>Apply a manipulation given the cumulative drag delta from BeginManipulation.</summary>
    public abstract void UpdateManipulation(HandleKind kind, int dx, int dy);

    public virtual EditorAnnotation Clone()
    {
        var copy = (EditorAnnotation)MemberwiseClone();
        copy.Style = Style.Clone();
        return copy;
    }

    // ---- Shared helpers ----------------------------------------------

    protected static IEnumerable<AnnotationHandle> RectHandles(Rectangle r)
    {
        yield return new AnnotationHandle(HandleKind.TopLeft, new Point(r.Left, r.Top));
        yield return new AnnotationHandle(HandleKind.Top, new Point(r.Left + r.Width / 2, r.Top));
        yield return new AnnotationHandle(HandleKind.TopRight, new Point(r.Right, r.Top));
        yield return new AnnotationHandle(HandleKind.Right, new Point(r.Right, r.Top + r.Height / 2));
        yield return new AnnotationHandle(HandleKind.BottomRight, new Point(r.Right, r.Bottom));
        yield return new AnnotationHandle(HandleKind.Bottom, new Point(r.Left + r.Width / 2, r.Bottom));
        yield return new AnnotationHandle(HandleKind.BottomLeft, new Point(r.Left, r.Bottom));
        yield return new AnnotationHandle(HandleKind.Left, new Point(r.Left, r.Top + r.Height / 2));
    }

    protected static Rectangle ResizeRect(Rectangle start, HandleKind handle, int dx, int dy, int minDim = 10)
    {
        int x = start.X, y = start.Y, right = start.Right, bottom = start.Bottom;
        switch (handle)
        {
            case HandleKind.TopLeft: x += dx; y += dy; break;
            case HandleKind.Top: y += dy; break;
            case HandleKind.TopRight: y += dy; right += dx; break;
            case HandleKind.Right: right += dx; break;
            case HandleKind.BottomRight: right += dx; bottom += dy; break;
            case HandleKind.Bottom: bottom += dy; break;
            case HandleKind.BottomLeft: x += dx; bottom += dy; break;
            case HandleKind.Left: x += dx; break;
        }
        if (right - x < minDim)
        {
            if (handle is HandleKind.TopLeft or HandleKind.BottomLeft or HandleKind.Left)
                x = right - minDim;
            else
                right = x + minDim;
        }
        if (bottom - y < minDim)
        {
            if (handle is HandleKind.TopLeft or HandleKind.TopRight or HandleKind.Top)
                y = bottom - minDim;
            else
                bottom = y + minDim;
        }
        return new Rectangle(x, y, right - x, bottom - y);
    }
}

// ----------------------------------------------------------------------
// Concrete annotations
// ----------------------------------------------------------------------

internal abstract class RectAnnotation : EditorAnnotation
{
    public Rectangle Box;

    public override Rectangle Bounds => Box;
    protected Rectangle StartBox;

    public override bool HitTest(Point p, int tol)
    {
        var r = Rectangle.Inflate(Box, tol, tol);
        return r.Contains(p);
    }

    public override void Move(int dx, int dy) =>
        Box = new Rectangle(Box.X + dx, Box.Y + dy, Box.Width, Box.Height);

    public override IEnumerable<AnnotationHandle> GetHandles() => RectHandles(Box);

    public override void BeginManipulation(HandleKind kind) => StartBox = Box;

    public override void UpdateManipulation(HandleKind kind, int dx, int dy)
    {
        if (kind == HandleKind.Body)
            Box = new Rectangle(StartBox.X + dx, StartBox.Y + dy, StartBox.Width, StartBox.Height);
        else
            Box = ResizeRect(StartBox, kind, dx, dy);
    }
}

internal sealed class RectangleAnnotation : RectAnnotation
{
    public override string DisplayName => "Rectangle";
    public override bool UsesStroke => true;
    public override bool UsesFill => true;

    public RectangleAnnotation()
    {
        Style.StrokeColor = Color.FromArgb(255, 230, 30, 30);
        Style.FillColor = Color.FromArgb(50, 255, 0, 0);
        Style.StrokeWidth = 3f;
    }

    public override void Render(Graphics g)
    {
        if (Box.Width == 0 || Box.Height == 0) return;
        using var fill = new SolidBrush(Style.FillColor);
        g.FillRectangle(fill, Box);
        using var pen = new Pen(Style.StrokeColor, Style.StrokeWidth);
        g.DrawRectangle(pen, Box);
    }
}

internal sealed class EllipseAnnotation : RectAnnotation
{
    public override string DisplayName => "Ellipse";
    public override bool UsesStroke => true;
    public override bool UsesFill => true;

    public EllipseAnnotation()
    {
        Style.StrokeColor = Color.FromArgb(255, 230, 30, 30);
        Style.FillColor = Color.FromArgb(40, 255, 0, 0);
        Style.StrokeWidth = 3f;
    }

    public override void Render(Graphics g)
    {
        if (Box.Width <= 0 || Box.Height <= 0) return;
        var prev = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Style.FillColor.A > 0)
        {
            using var fill = new SolidBrush(Style.FillColor);
            g.FillEllipse(fill, Box);
        }
        if (Style.StrokeWidth > 0 && Style.StrokeColor.A > 0)
        {
            using var pen = new Pen(Style.StrokeColor, Style.StrokeWidth);
            g.DrawEllipse(pen, Box);
        }
        g.SmoothingMode = prev;
    }

    public override bool HitTest(Point p, int tol)
    {
        // Normalized ellipse equation with a tolerance band so the outline is
        // grabbable even when the ellipse has no fill.
        float rx = Box.Width / 2f;
        float ry = Box.Height / 2f;
        if (rx <= 0 || ry <= 0) return Rectangle.Inflate(Box, tol, tol).Contains(p);
        float cx = Box.X + rx;
        float cy = Box.Y + ry;
        float nx = (p.X - cx) / (rx + tol);
        float ny = (p.Y - cy) / (ry + tol);
        if (nx * nx + ny * ny <= 1f) return true;
        return false;
    }
}

internal sealed class HighlightAnnotation : RectAnnotation
{
    public override string DisplayName => "Highlight";
    public override bool UsesFill => true;

    public HighlightAnnotation()
    {
        Style.FillColor = Color.FromArgb(110, 255, 235, 60);
        Style.StrokeWidth = 0f;
    }

    public override void Render(Graphics g)
    {
        if (Box.Width == 0 || Box.Height == 0) return;
        using var brush = new SolidBrush(Style.FillColor);
        var prev = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceOver;
        g.FillRectangle(brush, Box);
        g.CompositingMode = prev;
    }
}

/// <summary>
/// Editable mosaic blur over a rectangular area. Rendered every paint by
/// recomputing the blur from the live base bitmap region under the box,
/// so it stays in sync with size/position changes. The base bitmap pointer
/// is provided through <see cref="BlurContext"/> set by the canvas before
/// painting; this avoids each annotation holding a stale bitmap reference.
/// </summary>
internal sealed class BlurAnnotation : RectAnnotation
{
    public override string DisplayName => "Blur";
    public override bool UsesStroke => false;
    public override bool UsesBlur => true;

    public BlurAnnotation()
    {
        Style.BlurRadius = 12;
    }

    public override void Render(Graphics g)
    {
        if (Box.Width < 2 || Box.Height < 2) return;
        var bmp = BlurContext.CurrentBase;
        if (bmp == null) return;
        var clamp = Rectangle.Intersect(Box, new Rectangle(0, 0, bmp.Width, bmp.Height));
        if (clamp.Width <= 0 || clamp.Height <= 0) return;
        try
        {
            using var mosaic = ImageEditing.BlurRegionStandalone(bmp, clamp, Math.Max(2, Style.BlurRadius));
            g.DrawImage(mosaic, clamp);
        }
        catch
        {
            // ignore -- fall back to no blur visual on failure
        }
    }
}

/// <summary>
/// Context passed down to <see cref="BlurAnnotation"/> during paint. The
/// canvas sets <see cref="CurrentBase"/> before iterating annotations so
/// blur can sample the up-to-date bitmap without holding a reference.
/// </summary>
internal static class BlurContext
{
    [ThreadStatic] public static Bitmap? CurrentBase;
}

internal sealed class ArrowAnnotation : EditorAnnotation
{
    public Point Start;
    public Point End;
    private Point _startStart;
    private Point _startEnd;

    public override string DisplayName => "Arrow";
    public override bool UsesStroke => true;

    public ArrowAnnotation()
    {
        Style.StrokeColor = Color.FromArgb(255, 230, 30, 30);
        Style.StrokeWidth = 4f;
    }

    public override Rectangle Bounds => Rectangle.FromLTRB(
        Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y),
        Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y));

    public override void Render(Graphics g)
    {
        var prevSmooth = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Style.StrokeColor, Style.StrokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        try { pen.CustomEndCap = new AdjustableArrowCap(4.5f, 5.5f, isFilled: true); }
        catch { /* ignore */ }
        g.DrawLine(pen, Start, End);
        g.SmoothingMode = prevSmooth;
    }

    public override bool HitTest(Point p, int tol)
    {
        return DistancePointToSegment(p, Start, End) <= Math.Max(tol, Style.StrokeWidth + 2);
    }

    public override void Move(int dx, int dy)
    {
        Start = new Point(Start.X + dx, Start.Y + dy);
        End = new Point(End.X + dx, End.Y + dy);
    }

    public override IEnumerable<AnnotationHandle> GetHandles()
    {
        yield return new AnnotationHandle(HandleKind.ArrowStart, Start);
        yield return new AnnotationHandle(HandleKind.ArrowEnd, End);
    }

    public override void BeginManipulation(HandleKind kind)
    {
        _startStart = Start;
        _startEnd = End;
    }

    public override void UpdateManipulation(HandleKind kind, int dx, int dy)
    {
        switch (kind)
        {
            case HandleKind.ArrowStart: Start = new Point(_startStart.X + dx, _startStart.Y + dy); break;
            case HandleKind.ArrowEnd: End = new Point(_startEnd.X + dx, _startEnd.Y + dy); break;
            case HandleKind.Body:
                Start = new Point(_startStart.X + dx, _startStart.Y + dy);
                End = new Point(_startEnd.X + dx, _startEnd.Y + dy);
                break;
        }
    }

    private static double DistancePointToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Max(0, Math.Min(1, t));
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
    }
}

internal sealed class LineAnnotation : EditorAnnotation
{
    public Point Start;
    public Point End;
    private Point _startStart;
    private Point _startEnd;

    public override string DisplayName => "Line";
    public override bool UsesStroke => true;

    public LineAnnotation()
    {
        Style.StrokeColor = Color.FromArgb(255, 230, 30, 30);
        Style.StrokeWidth = 4f;
    }

    public override Rectangle Bounds => Rectangle.FromLTRB(
        Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y),
        Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y));

    public override void Render(Graphics g)
    {
        var prevSmooth = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Style.StrokeColor, Style.StrokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawLine(pen, Start, End);
        g.SmoothingMode = prevSmooth;
    }

    public override bool HitTest(Point p, int tol)
        => DistancePointToSegment(p, Start, End) <= Math.Max(tol, Style.StrokeWidth + 2);

    public override void Move(int dx, int dy)
    {
        Start = new Point(Start.X + dx, Start.Y + dy);
        End = new Point(End.X + dx, End.Y + dy);
    }

    public override IEnumerable<AnnotationHandle> GetHandles()
    {
        yield return new AnnotationHandle(HandleKind.ArrowStart, Start);
        yield return new AnnotationHandle(HandleKind.ArrowEnd, End);
    }

    public override void BeginManipulation(HandleKind kind)
    {
        _startStart = Start;
        _startEnd = End;
    }

    public override void UpdateManipulation(HandleKind kind, int dx, int dy)
    {
        switch (kind)
        {
            case HandleKind.ArrowStart: Start = new Point(_startStart.X + dx, _startStart.Y + dy); break;
            case HandleKind.ArrowEnd: End = new Point(_startEnd.X + dx, _startEnd.Y + dy); break;
            case HandleKind.Body:
                Start = new Point(_startStart.X + dx, _startStart.Y + dy);
                End = new Point(_startEnd.X + dx, _startEnd.Y + dy);
                break;
        }
    }

    private static double DistancePointToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Max(0, Math.Min(1, t));
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
    }
}

/// <summary>
/// Freehand pen stroke: an open polyline captured while dragging. Supports
/// whole-shape move plus proportional resize via the bounding-box handles.
/// </summary>
internal sealed class FreehandAnnotation : EditorAnnotation
{
    public List<Point> Points = new();
    private List<Point> _startPoints = new();
    private Rectangle _startBox;

    public override string DisplayName => "Pen";
    public override bool UsesStroke => true;

    public FreehandAnnotation()
    {
        Style.StrokeColor = Color.FromArgb(255, 230, 30, 30);
        Style.StrokeWidth = 3f;
    }

    private Rectangle TightBounds()
    {
        if (Points.Count == 0) return Rectangle.Empty;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in Points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    public override Rectangle Bounds
    {
        get
        {
            var b = TightBounds();
            if (b.IsEmpty) return b;
            int pad = (int)Math.Ceiling(Style.StrokeWidth) + 1;
            return Rectangle.Inflate(b, pad, pad);
        }
    }

    public override void Render(Graphics g)
    {
        if (Points.Count == 0) return;
        var prev = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Style.StrokeColor, Style.StrokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        if (Points.Count == 1)
        {
            float r = Math.Max(1f, Style.StrokeWidth / 2f);
            using var b = new SolidBrush(Style.StrokeColor);
            g.FillEllipse(b, Points[0].X - r, Points[0].Y - r, r * 2, r * 2);
        }
        else
        {
            g.DrawLines(pen, Points.ToArray());
        }
        g.SmoothingMode = prev;
    }

    public override bool HitTest(Point p, int tol)
    {
        double thresh = Math.Max(tol, Style.StrokeWidth + 2);
        for (int i = 1; i < Points.Count; i++)
        {
            if (DistancePointToSegment(p, Points[i - 1], Points[i]) <= thresh) return true;
        }
        if (Points.Count == 1)
        {
            var d = Points[0];
            return Math.Abs(p.X - d.X) <= thresh && Math.Abs(p.Y - d.Y) <= thresh;
        }
        return false;
    }

    public override void Move(int dx, int dy)
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i] = new Point(Points[i].X + dx, Points[i].Y + dy);
    }

    public override IEnumerable<AnnotationHandle> GetHandles() => RectHandles(TightBounds());

    public override void BeginManipulation(HandleKind kind)
    {
        _startPoints = new List<Point>(Points);
        _startBox = TightBounds();
    }

    public override void UpdateManipulation(HandleKind kind, int dx, int dy)
    {
        if (kind == HandleKind.Body)
        {
            for (int i = 0; i < _startPoints.Count; i++)
                Points[i] = new Point(_startPoints[i].X + dx, _startPoints[i].Y + dy);
            return;
        }
        if (_startBox.Width <= 0 || _startBox.Height <= 0) return;
        var newBox = ResizeRect(_startBox, kind, dx, dy, minDim: 4);
        float sx = newBox.Width / (float)_startBox.Width;
        float sy = newBox.Height / (float)_startBox.Height;
        for (int i = 0; i < _startPoints.Count; i++)
        {
            var sp = _startPoints[i];
            Points[i] = new Point(
                newBox.X + (int)Math.Round((sp.X - _startBox.X) * sx),
                newBox.Y + (int)Math.Round((sp.Y - _startBox.Y) * sy));
        }
    }

    public override EditorAnnotation Clone()
    {
        var copy = (FreehandAnnotation)base.Clone();
        copy.Points = new List<Point>(Points);
        copy._startPoints = new List<Point>(_startPoints);
        return copy;
    }

    private static double DistancePointToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Max(0, Math.Min(1, t));
        double cx = a.X + t * dx, cy = a.Y + t * dy;
        return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
    }
}

internal sealed class TextAnnotation : RectAnnotation
{
    public string Text = string.Empty;

    public override string DisplayName => "Text";
    public override bool UsesStroke => true;
    public override bool UsesFill => true;
    public override bool UsesTextColor => true;
    public override bool UsesFontSize => true;

    public TextAnnotation()
    {
        Style.TextColor = Color.White;
        Style.FillColor = Color.FromArgb(150, 0, 0, 0);
        Style.StrokeColor = Color.FromArgb(255, 230, 30, 30);
        Style.StrokeWidth = 1f;
        Style.FontSize = 14f;
    }

    public override void Render(Graphics g)
    {
        if (Box.Width < 4 || Box.Height < 4) return;
        if (Style.FillColor.A > 0)
        {
            using var fill = new SolidBrush(Style.FillColor);
            g.FillRectangle(fill, Box);
        }
        if (Style.StrokeWidth > 0 && Style.StrokeColor.A > 0)
        {
            using var pen = new Pen(Style.StrokeColor, Style.StrokeWidth);
            g.DrawRectangle(pen, Box);
        }
        if (string.IsNullOrEmpty(Text)) return;

        using var font = new Font(Style.FontFamily, Style.FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Style.TextColor);
        var pad = 6;
        var rect = new RectangleF(Box.X + pad, Box.Y + pad,
            Math.Max(1, Box.Width - pad * 2), Math.Max(1, Box.Height - pad * 2));
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.LineLimit,
        };
        g.DrawString(Text, font, brush, rect, fmt);
    }
}

/// <summary>
/// Number badge for step-by-step screenshots. Renders as a filled circle
/// with a centered number. Size is controlled by <see cref="Bounds"/>; the
/// number itself is taken from <see cref="Number"/>.
/// </summary>
internal sealed class StepMarkerAnnotation : EditorAnnotation
{
    public Point Center;
    public int Radius = 18;
    public int Number = 1;
    private Point _startCenter;
    private int _startRadius;

    public override string DisplayName => "Step marker";
    public override bool UsesStroke => true;
    public override bool UsesFill => true;
    public override bool UsesTextColor => true;
    public override bool UsesFontSize => true;

    public StepMarkerAnnotation()
    {
        Style.FillColor = Color.FromArgb(255, 230, 30, 30);
        Style.TextColor = Color.White;
        Style.StrokeColor = Color.White;
        Style.StrokeWidth = 2f;
        Style.FontSize = 16f;
    }

    public override Rectangle Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);

    public override void Render(Graphics g)
    {
        var prev = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = Bounds;
        using var fill = new SolidBrush(Style.FillColor);
        g.FillEllipse(fill, r);
        if (Style.StrokeWidth > 0)
        {
            using var pen = new Pen(Style.StrokeColor, Style.StrokeWidth);
            g.DrawEllipse(pen, r);
        }
        using var font = new Font(Style.FontFamily, Style.FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Style.TextColor);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(Number.ToString(), font, textBrush, r, fmt);
        g.SmoothingMode = prev;
    }

    public override bool HitTest(Point p, int tol)
    {
        var dx = p.X - Center.X;
        var dy = p.Y - Center.Y;
        var rr = Radius + tol;
        return dx * dx + dy * dy <= rr * rr;
    }

    public override void Move(int dx, int dy) => Center = new Point(Center.X + dx, Center.Y + dy);

    public override IEnumerable<AnnotationHandle> GetHandles()
    {
        // Treat as a square for resize handles. Only diagonals to keep it round.
        var b = Bounds;
        yield return new AnnotationHandle(HandleKind.TopLeft, new Point(b.Left, b.Top));
        yield return new AnnotationHandle(HandleKind.BottomRight, new Point(b.Right, b.Bottom));
    }

    public override void BeginManipulation(HandleKind kind)
    {
        _startCenter = Center;
        _startRadius = Radius;
    }

    public override void UpdateManipulation(HandleKind kind, int dx, int dy)
    {
        if (kind == HandleKind.Body)
        {
            Center = new Point(_startCenter.X + dx, _startCenter.Y + dy);
            return;
        }
        int delta;
        switch (kind)
        {
            case HandleKind.TopLeft: delta = -(dx + dy) / 2; break;
            case HandleKind.BottomRight: delta = (dx + dy) / 2; break;
            default: delta = 0; break;
        }
        Radius = Math.Max(8, _startRadius + delta);
    }
}

/// <summary>
/// Spotlight effect: darkens everything except an elliptical area.
/// Useful for highlighting a single UI element on a busy capture.
/// </summary>
internal sealed class SpotlightAnnotation : RectAnnotation
{
    public override string DisplayName => "Spotlight";
    public override bool UsesStroke => true;
    public override bool UsesFill => true;

    public SpotlightAnnotation()
    {
        Style.FillColor = Color.FromArgb(180, 0, 0, 0);
        Style.StrokeColor = Color.FromArgb(255, 230, 30, 30);
        Style.StrokeWidth = 1.5f;
    }

    public override void Render(Graphics g)
    {
        if (Box.Width < 4 || Box.Height < 4) return;
        var bmp = BlurContext.CurrentBase;
        if (bmp == null) return;

        var prev = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var imageRect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        using var outsidePath = new GraphicsPath();
        outsidePath.AddRectangle(imageRect);
        outsidePath.AddEllipse(Box);
        outsidePath.FillMode = FillMode.Alternate;
        using var dim = new SolidBrush(Style.FillColor);
        g.FillPath(dim, outsidePath);
        if (Style.StrokeWidth > 0)
        {
            using var pen = new Pen(Style.StrokeColor, Style.StrokeWidth);
            g.DrawEllipse(pen, Box);
        }
        g.SmoothingMode = prev;
    }
}

/// <summary>
/// Magnifier: a circle that shows a zoomed-in copy of the base bitmap
/// region under a different image-space rectangle. The user picks the
/// source area via <see cref="Source"/>; the magnifier displays it inside
/// <see cref="Box"/>.
/// </summary>
internal sealed class MagnifierAnnotation : RectAnnotation
{
    public Rectangle Source;
    public float ZoomFactor = 2.0f;
    private Rectangle _startSource;

    public override string DisplayName => "Magnifier";
    public override bool UsesStroke => true;

    public MagnifierAnnotation()
    {
        Style.StrokeColor = Color.FromArgb(255, 230, 30, 30);
        Style.StrokeWidth = 2f;
    }

    public override void Render(Graphics g)
    {
        if (Box.Width < 8 || Box.Height < 8) return;
        var bmp = BlurContext.CurrentBase;
        if (bmp == null) return;
        var src = Rectangle.Intersect(Source, new Rectangle(0, 0, bmp.Width, bmp.Height));
        if (src.Width <= 0 || src.Height <= 0) return;

        var prev = g.SmoothingMode;
        var prevInterp = g.InterpolationMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using var clip = new GraphicsPath();
        clip.AddEllipse(Box);
        var prevClip = g.Clip;
        g.SetClip(clip);
        g.DrawImage(bmp, Box, src, GraphicsUnit.Pixel);
        g.Clip = prevClip;
        if (Style.StrokeWidth > 0)
        {
            using var pen = new Pen(Style.StrokeColor, Style.StrokeWidth);
            g.DrawEllipse(pen, Box);
        }
        g.SmoothingMode = prev;
        g.InterpolationMode = prevInterp;
    }

    public override void BeginManipulation(HandleKind kind)
    {
        base.BeginManipulation(kind);
        _startSource = Source;
    }

    public override void UpdateManipulation(HandleKind kind, int dx, int dy)
    {
        var prevBox = Box;
        base.UpdateManipulation(kind, dx, dy);
        // Keep source roughly aligned: when Box moves, source moves with it; on resize we don't change source.
        if (kind == HandleKind.Body)
        {
            Source = new Rectangle(_startSource.X + dx, _startSource.Y + dy, _startSource.Width, _startSource.Height);
        }
    }
}

internal sealed class SpeechBalloonAnnotation : EditorAnnotation
{
    public Rectangle Box;
    public Point TailTip;
    public string Text = string.Empty;
    private Rectangle _startBox;
    private Point _startTail;

    public override string DisplayName => "Balloon";
    public override bool UsesStroke => true;
    public override bool UsesFill => true;
    public override bool UsesTextColor => true;
    public override bool UsesFontSize => true;

    public SpeechBalloonAnnotation()
    {
        Style.FillColor = Color.White;
        Style.StrokeColor = Color.Black;
        Style.TextColor = Color.Black;
        Style.StrokeWidth = 2f;
        Style.FontSize = 14f;
    }

    public override Rectangle Bounds => Box;

    public override void Render(Graphics g)
    {
        if (Box.Width < 6 || Box.Height < 6) return;

        var prevSmooth = g.SmoothingMode;
        var prevText = g.TextRenderingHint;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        int radius = Math.Min(28, Math.Min(Box.Width, Box.Height) / 4);
        using var bodyPath = BuildRoundedRect(Box, radius);
        using var fillBrush = new SolidBrush(Style.FillColor);
        using var strokePen = new Pen(Style.StrokeColor, Style.StrokeWidth)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.FillPath(fillBrush, bodyPath);
        g.DrawPath(strokePen, bodyPath);

        var tail = BuildTail(Box, TailTip);
        if (tail.Length == 3)
        {
            g.FillPolygon(fillBrush, tail);
            g.DrawLine(strokePen, tail[0], tail[2]);
            g.DrawLine(strokePen, tail[1], tail[2]);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            using var font = new Font(Style.FontFamily, Style.FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Style.TextColor);
            var padding = 10;
            var textRect = new RectangleF(Box.X + padding, Box.Y + padding,
                Math.Max(1, Box.Width - padding * 2), Math.Max(1, Box.Height - padding * 2));
            using var fmt = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisWord,
                FormatFlags = StringFormatFlags.LineLimit,
            };
            g.DrawString(Text, font, textBrush, textRect, fmt);
        }

        g.SmoothingMode = prevSmooth;
        g.TextRenderingHint = prevText;
    }

    public override bool HitTest(Point p, int tol)
    {
        var r = Rectangle.Inflate(Box, tol, tol);
        return r.Contains(p);
    }

    public override void Move(int dx, int dy)
    {
        Box = new Rectangle(Box.X + dx, Box.Y + dy, Box.Width, Box.Height);
        TailTip = new Point(TailTip.X + dx, TailTip.Y + dy);
    }

    public override IEnumerable<AnnotationHandle> GetHandles()
    {
        foreach (var h in RectHandles(Box)) yield return h;
        yield return new AnnotationHandle(HandleKind.BalloonTail, TailTip);
    }

    public override void BeginManipulation(HandleKind kind)
    {
        _startBox = Box;
        _startTail = TailTip;
    }

    public override void UpdateManipulation(HandleKind kind, int dx, int dy)
    {
        switch (kind)
        {
            case HandleKind.Body:
                Box = new Rectangle(_startBox.X + dx, _startBox.Y + dy, _startBox.Width, _startBox.Height);
                TailTip = new Point(_startTail.X + dx, _startTail.Y + dy);
                break;
            case HandleKind.BalloonTail:
                TailTip = new Point(_startTail.X + dx, _startTail.Y + dy);
                break;
            default:
                Box = ResizeRect(_startBox, kind, dx, dy);
                break;
        }
    }

    public static GraphicsPath BuildRoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        int d = Math.Max(2, radius * 2);
        d = Math.Min(d, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Point[] BuildTail(Rectangle body, Point tip)
    {
        if (body.Width < 4 || body.Height < 4) return Array.Empty<Point>();

        int dxL = body.Left - tip.X;
        int dxR = tip.X - body.Right;
        int dyT = body.Top - tip.Y;
        int dyB = tip.Y - body.Bottom;
        int max = Math.Max(Math.Max(dxL, dxR), Math.Max(dyT, dyB));
        if (max <= 0) return Array.Empty<Point>();

        int baseWidth = Math.Max(14, Math.Min(body.Width, body.Height) / 4);
        int maxBase = Math.Min(body.Width, body.Height) - 6;
        if (maxBase < 6) return Array.Empty<Point>();
        if (baseWidth > maxBase) baseWidth = maxBase;
        int half = baseWidth / 2;

        if (max == dyB)
        {
            int cx = Math.Clamp(tip.X, body.Left + half + 1, body.Right - half - 1);
            return new[]
            {
                new Point(cx - half, body.Bottom),
                new Point(cx + half, body.Bottom),
                tip,
            };
        }
        if (max == dyT)
        {
            int cx = Math.Clamp(tip.X, body.Left + half + 1, body.Right - half - 1);
            return new[]
            {
                new Point(cx + half, body.Top),
                new Point(cx - half, body.Top),
                tip,
            };
        }
        if (max == dxR)
        {
            int cy = Math.Clamp(tip.Y, body.Top + half + 1, body.Bottom - half - 1);
            return new[]
            {
                new Point(body.Right, cy - half),
                new Point(body.Right, cy + half),
                tip,
            };
        }
        {
            int cy = Math.Clamp(tip.Y, body.Top + half + 1, body.Bottom - half - 1);
            return new[]
            {
                new Point(body.Left, cy + half),
                new Point(body.Left, cy - half),
                tip,
            };
        }
    }

    public static Point DefaultTailTipFor(Rectangle bounds)
    {
        int dx = Math.Min(80, Math.Max(20, bounds.Width / 3));
        int dy = Math.Min(80, Math.Max(20, bounds.Height / 3));
        return new Point(bounds.X + dx, bounds.Bottom + dy);
    }
}
