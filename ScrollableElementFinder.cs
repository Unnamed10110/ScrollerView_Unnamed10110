using System;
using System.Drawing;
using System.Windows;
using System.Windows.Automation;

namespace ScrollerCapture;

/// <summary>
/// Locates a UI Automation element under a screen region that supports
/// scrolling on the requested axis via the <see cref="ScrollPattern"/>.
/// </summary>
internal sealed class ScrollabilityInfo
{
    public bool HorizontalScrollable;
    public bool VerticalScrollable;
    public double HorizontalViewSize;
    public double VerticalViewSize;
    public AutomationElement? Element;
    public Rectangle ElementBounds;
}

internal static class ScrollableElementFinder
{
    public static ScrollableTarget? Find(Rectangle screenRegion, CaptureDirection direction)
    {
        var center = new System.Windows.Point(
            screenRegion.X + screenRegion.Width / 2.0,
            screenRegion.Y + screenRegion.Height / 2.0);

        AutomationElement? hit = null;
        try
        {
            hit = AutomationElement.FromPoint(center);
        }
        catch
        {
            // ignore - some elements throw
        }

        if (hit != null)
        {
            var match = WalkUpForScroll(hit, screenRegion, direction);
            if (match != null) return match;
        }

        // Fallback: search the foreground window's tree top-down.
        try
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                var root = AutomationElement.FromHandle(fg);
                if (root != null)
                {
                    var match = SearchTreeForScroll(root, screenRegion, direction);
                    if (match != null) return match;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Inspects which directions are scrollable inside <paramref name="region"/>.
    /// Always returns a non-null info object, but its flags may both be false
    /// if nothing usable was found.
    /// </summary>
    public static ScrollabilityInfo Inspect(Rectangle region)
    {
        var info = new ScrollabilityInfo();
        AutomationElement? hit = null;
        try
        {
            var center = new System.Windows.Point(
                region.X + region.Width / 2.0,
                region.Y + region.Height / 2.0);
            hit = AutomationElement.FromPoint(center);
        }
        catch { /* ignore */ }

        if (hit != null)
        {
            FillFromAncestor(hit, region, info);
        }

        if (!info.HorizontalScrollable && !info.VerticalScrollable)
        {
            try
            {
                var fg = NativeMethods.GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    var root = AutomationElement.FromHandle(fg);
                    if (root != null)
                    {
                        SearchTreeForAny(root, region, info, depth: 0);
                    }
                }
            }
            catch { /* ignore */ }
        }

        return info;
    }

    private static void FillFromAncestor(AutomationElement element, Rectangle region, ScrollabilityInfo info)
    {
        var current = element;
        for (int i = 0; i < 30 && current != null; i++)
        {
            TryFillScroll(current, region, info);
            if (info.HorizontalScrollable && info.VerticalScrollable) return;
            try { current = TreeWalker.ControlViewWalker.GetParent(current); }
            catch { break; }
        }
    }

    private static void SearchTreeForAny(AutomationElement root, Rectangle region, ScrollabilityInfo info, int depth)
    {
        if (depth > 12) return;
        TryFillScroll(root, region, info);
        if (info.HorizontalScrollable && info.VerticalScrollable) return;
        AutomationElement? child;
        try { child = TreeWalker.ControlViewWalker.GetFirstChild(root); }
        catch { child = null; }
        while (child != null)
        {
            SearchTreeForAny(child, region, info, depth + 1);
            if (info.HorizontalScrollable && info.VerticalScrollable) return;
            try { child = TreeWalker.ControlViewWalker.GetNextSibling(child); }
            catch { break; }
        }
    }

    private static void TryFillScroll(AutomationElement element, Rectangle region, ScrollabilityInfo info)
    {
        try
        {
            var b = element.Current.BoundingRectangle;
            if (b.IsEmpty) return;
            var r = new Rectangle((int)Math.Round(b.X), (int)Math.Round(b.Y),
                (int)Math.Round(b.Width), (int)Math.Round(b.Height));
            if (!r.IntersectsWith(region)) return;
            if (!element.TryGetCurrentPattern(ScrollPattern.Pattern, out var patternObj)) return;
            var pattern = (ScrollPattern)patternObj;

            bool changed = false;
            if (pattern.Current.HorizontallyScrollable && !info.HorizontalScrollable)
            {
                info.HorizontalScrollable = true;
                info.HorizontalViewSize = pattern.Current.HorizontalViewSize;
                changed = true;
            }
            if (pattern.Current.VerticallyScrollable && !info.VerticalScrollable)
            {
                info.VerticalScrollable = true;
                info.VerticalViewSize = pattern.Current.VerticalViewSize;
                changed = true;
            }
            if (changed && info.Element == null)
            {
                info.Element = element;
                info.ElementBounds = r;
            }
        }
        catch { /* ignore */ }
    }

    private static ScrollableTarget? WalkUpForScroll(AutomationElement element, Rectangle region, CaptureDirection direction)
    {
        var current = element;
        for (int i = 0; i < 30 && current != null; i++)
        {
            if (TryGetScroll(current, region, direction, out var target) && target != null)
            {
                return target;
            }
            try
            {
                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch
            {
                break;
            }
        }
        return null;
    }

    private static ScrollableTarget? SearchTreeForScroll(AutomationElement root, Rectangle region, CaptureDirection direction)
    {
        if (TryGetScroll(root, region, direction, out var here) && here != null)
        {
            return here;
        }

        AutomationElement? child;
        try
        {
            child = TreeWalker.ControlViewWalker.GetFirstChild(root);
        }
        catch
        {
            child = null;
        }

        while (child != null)
        {
            var found = SearchTreeForScroll(child, region, direction);
            if (found != null) return found;
            try
            {
                child = TreeWalker.ControlViewWalker.GetNextSibling(child);
            }
            catch
            {
                break;
            }
        }
        return null;
    }

    private static bool TryGetScroll(AutomationElement element, Rectangle region, CaptureDirection direction, out ScrollableTarget? target)
    {
        target = null;
        try
        {
            var bounds = element.Current.BoundingRectangle;
            if (bounds.IsEmpty) return false;

            var elemRect = new Rectangle(
                (int)Math.Round(bounds.X),
                (int)Math.Round(bounds.Y),
                (int)Math.Round(bounds.Width),
                (int)Math.Round(bounds.Height));

            if (!elemRect.IntersectsWith(region)) return false;

            if (!element.TryGetCurrentPattern(ScrollPattern.Pattern, out var patternObj))
            {
                return false;
            }

            var pattern = (ScrollPattern)patternObj;
            bool scrollable = direction == CaptureDirection.Horizontal
                ? pattern.Current.HorizontallyScrollable
                : pattern.Current.VerticallyScrollable;
            if (!scrollable) return false;

            target = new ScrollableTarget(element, pattern, elemRect, direction);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class ScrollableTarget
{
    public ScrollableTarget(AutomationElement element, ScrollPattern pattern, Rectangle bounds, CaptureDirection direction)
    {
        Element = element;
        Pattern = pattern;
        Bounds = bounds;
        Direction = direction;
    }

    public AutomationElement Element { get; }
    public ScrollPattern Pattern { get; }
    public Rectangle Bounds { get; }
    public CaptureDirection Direction { get; }

    public double ScrollPercent
    {
        get
        {
            try
            {
                return Direction == CaptureDirection.Horizontal
                    ? Pattern.Current.HorizontalScrollPercent
                    : Pattern.Current.VerticalScrollPercent;
            }
            catch { return double.NaN; }
        }
    }

    public double ViewSize
    {
        get
        {
            try
            {
                return Direction == CaptureDirection.Horizontal
                    ? Pattern.Current.HorizontalViewSize
                    : Pattern.Current.VerticalViewSize;
            }
            catch { return double.NaN; }
        }
    }

    public bool IsAtEnd
    {
        get
        {
            var p = ScrollPercent;
            if (double.IsNaN(p)) return false;
            return p >= 99.5;
        }
    }

    public bool TrySetScrollPercent(double percent)
    {
        try
        {
            percent = Math.Max(0, Math.Min(100, percent));
            if (Direction == CaptureDirection.Horizontal)
            {
                Pattern.SetScrollPercent(percent, ScrollPattern.NoScroll);
            }
            else
            {
                Pattern.SetScrollPercent(ScrollPattern.NoScroll, percent);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
