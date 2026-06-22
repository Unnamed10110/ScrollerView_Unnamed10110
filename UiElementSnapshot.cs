using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Automation;

namespace ScrollerCapture;

/// <summary>
/// A precomputed model of every detectable UI element on screen, built once
/// when the selection overlay opens. Phase 1 enumerates all visible top-level
/// windows (cheap, instant) so hovering works immediately at window
/// granularity. Phase 2 deep-walks each window's UI Automation tree on a
/// background thread (budgeted) to add fine-grained child/sibling elements.
///
/// Hover detection then becomes an in-memory point lookup
/// (<see cref="StackAt"/>) instead of a per-move cross-process UIA call, which
/// removes lag and exposes every element rather than only the ancestor chain.
/// </summary>
internal sealed class UiElementSnapshot
{
    private const int MinSize = 8;
    private const int DepthCap = 25;
    private const int PerWindowCap = 1500;
    private const int GlobalCap = 6000;
    private const int TimeBudgetMs = 600;

    private static readonly HashSet<int> InterestingTypes = new()
    {
        ControlType.Window.Id,
        ControlType.Pane.Id,
        ControlType.Document.Id,
        ControlType.DataGrid.Id,
        ControlType.Table.Id,
        ControlType.List.Id,
        ControlType.Tree.Id,
        ControlType.Edit.Id,
        ControlType.Group.Id,
        ControlType.Custom.Id,
    };

    private readonly object _sync = new();
    private readonly List<TopWindow> _windows = new();
    private readonly List<UiCandidate> _candidates = new();
    private readonly HashSet<long> _seen = new();
    private int _globalCount;

    private readonly int _selfPid = Environment.ProcessId;
    private IntPtr _excludeHwnd;

    private readonly struct TopWindow
    {
        public TopWindow(IntPtr hwnd, Rectangle rect, int z, string title)
        {
            Hwnd = hwnd;
            Rect = rect;
            Z = z;
            Title = title;
        }
        public IntPtr Hwnd { get; }
        public Rectangle Rect { get; }
        public int Z { get; }
        public string Title { get; }
    }

    /// <summary>
    /// Phase 1: enumerate visible top-level windows and seed a window-level
    /// candidate for each so hovering is useful before the deep walk finishes.
    /// Cheap and synchronous; safe to call on the UI thread.
    /// </summary>
    /// <param name="excludeHwnd">The overlay window handle to skip.</param>
    public void BuildWindows(IntPtr excludeHwnd)
    {
        _excludeHwnd = excludeHwnd;
        var virt = System.Windows.Forms.SystemInformation.VirtualScreen;

        var found = new List<TopWindow>();
        int z = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == _excludeHwnd) return true;
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (NativeMethods.IsIconic(hwnd)) return true;

            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == _selfPid) return true;
            }
            catch { /* keep window if pid lookup fails */ }

            if (IsCloaked(hwnd)) return true;

            var rect = GetFrameBounds(hwnd);
            rect = Rectangle.Intersect(rect, virt);
            if (rect.Width < MinSize || rect.Height < MinSize) return true;

            string title = GetTitle(hwnd);
            found.Add(new TopWindow(hwnd, rect, z++, title));
            return true;
        }, IntPtr.Zero);

        lock (_sync)
        {
            _windows.Clear();
            _windows.AddRange(found);
            foreach (var w in found)
            {
                AddLocked(new UiCandidate(
                    w.Rect,
                    w.Title,
                    "window",
                    interesting: true,
                    hwnd: w.Hwnd,
                    windowZ: w.Z,
                    depth: 0));
            }
        }
    }

    /// <summary>
    /// Phase 2: deep-walk each window's UIA tree (topmost first) and append
    /// fine-grained elements. Honors a global time budget and element caps so
    /// huge trees (browsers, IDEs) can never hang the overlay. Intended to run
    /// on a background thread; <paramref name="onProgress"/> is raised after
    /// each window so the UI can refresh the current hover.
    /// </summary>
    public void RunDeepWalk(CancellationToken ct, Action? onProgress)
    {
        NativeMethods.SetThreadDpiAwarenessContext(
            (IntPtr)NativeMethods.DPI_AWARENESS_CONTEXT.PER_MONITOR_AWARE_V2);

        TopWindow[] windows;
        lock (_sync) windows = _windows.ToArray();

        var cache = new CacheRequest();
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.ControlTypeProperty);

        var walker = TreeWalker.ControlViewWalker;
        var virt = System.Windows.Forms.SystemInformation.VirtualScreen;
        var sw = Stopwatch.StartNew();

        foreach (var win in windows)
        {
            if (ct.IsCancellationRequested) return;
            if (sw.ElapsedMilliseconds > TimeBudgetMs) return;
            if (Volatile.Read(ref _globalCount) >= GlobalCap) return;

            WalkWindow(win, walker, cache, virt, sw, ct);
            onProgress?.Invoke();
        }
    }

    private void WalkWindow(
        TopWindow win, TreeWalker walker, CacheRequest cache,
        Rectangle virt, Stopwatch sw, CancellationToken ct)
    {
        AutomationElement? root;
        try { root = AutomationElement.FromHandle(win.Hwnd); }
        catch { return; }
        if (root == null) return;

        var batch = new List<UiCandidate>();
        int perWindow = 0;
        var stack = new Stack<(AutomationElement el, int depth)>();
        PushChildren(walker, cache, root, 1, stack);

        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) break;
            if (sw.ElapsedMilliseconds > TimeBudgetMs) break;
            if (perWindow >= PerWindowCap) break;
            if (Volatile.Read(ref _globalCount) >= GlobalCap) break;

            var (el, depth) = stack.Pop();
            try
            {
                var b = el.Cached.BoundingRectangle;
                if (!b.IsEmpty)
                {
                    var rect = Rectangle.Intersect(Round(b), virt);
                    if (rect.Width >= MinSize && rect.Height >= MinSize)
                    {
                        var ct2 = el.Cached.ControlType;
                        bool interesting = ct2 != null && InterestingTypes.Contains(ct2.Id);
                        string ctName = ct2?.LocalizedControlType ?? "element";
                        string name = el.Cached.Name ?? string.Empty;
                        batch.Add(new UiCandidate(rect, name, ctName, interesting, win.Hwnd, win.Z, depth));
                        perWindow++;
                    }
                }
            }
            catch { /* ignore individual element failures */ }

            if (depth < DepthCap)
            {
                PushChildren(walker, cache, el, depth + 1, stack);
            }
        }

        if (batch.Count > 0)
        {
            lock (_sync)
            {
                foreach (var c in batch) AddLocked(c);
            }
        }
    }

    private static void PushChildren(
        TreeWalker walker, CacheRequest cache, AutomationElement el, int depth,
        Stack<(AutomationElement, int)> stack)
    {
        AutomationElement? child;
        try { child = walker.GetFirstChild(el, cache); }
        catch { return; }
        while (child != null)
        {
            stack.Push((child, depth));
            try { child = walker.GetNextSibling(child, cache); }
            catch { break; }
        }
    }

    /// <summary>
    /// Returns the elements containing <paramref name="screen"/> within the
    /// topmost window covering that point, ordered smallest-area first (deepest
    /// child) so index 0 is the default pick and wheel/Tab widens to ancestors.
    /// Restricting to the topmost window avoids selecting occluded elements in
    /// windows that are visually behind another.
    /// </summary>
    public IReadOnlyList<UiCandidate> StackAt(Point screen)
    {
        var stack = new List<UiCandidate>();
        lock (_sync)
        {
            IntPtr topHwnd = IntPtr.Zero;
            foreach (var w in _windows)
            {
                // _windows is in Z order (topmost first); first hit is frontmost.
                if (w.Rect.Contains(screen))
                {
                    topHwnd = w.Hwnd;
                    break;
                }
            }
            if (topHwnd == IntPtr.Zero) return Array.Empty<UiCandidate>();

            foreach (var c in _candidates)
            {
                if (c.Hwnd == topHwnd && c.Bounds.Contains(screen)) stack.Add(c);
            }
        }

        stack.Sort((a, b) =>
        {
            int cmp = a.Area.CompareTo(b.Area);
            return cmp != 0 ? cmp : b.Depth.CompareTo(a.Depth);
        });

        // Drop adjacent entries with identical bounds (nested wrappers).
        var result = new List<UiCandidate>(stack.Count);
        foreach (var c in stack)
        {
            if (result.Count > 0 && result[^1].Bounds == c.Bounds) continue;
            result.Add(c);
        }
        return result;
    }

    public bool HasWindows
    {
        get { lock (_sync) return _windows.Count > 0; }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Adds a candidate, de-duplicating exact (hwnd + bounds) repeats. Caller holds _sync.</summary>
    private void AddLocked(UiCandidate c)
    {
        long key = RectKey(c.Hwnd, c.Bounds);
        if (!_seen.Add(key)) return;
        _candidates.Add(c);
        Interlocked.Increment(ref _globalCount);
    }

    private static long RectKey(IntPtr hwnd, Rectangle r)
    {
        // Combine the low bits of the hwnd with a packed rectangle hash.
        long h = hwnd.ToInt64() & 0xFFFFF;
        long rectHash = ((long)(r.X & 0x7FFF) << 45)
            ^ ((long)(r.Y & 0x7FFF) << 30)
            ^ ((long)(r.Width & 0x7FFF) << 15)
            ^ (r.Height & 0x7FFF);
        return h ^ rectHash;
    }

    private static Rectangle Round(System.Windows.Rect b) => new(
        (int)Math.Round(b.X),
        (int)Math.Round(b.Y),
        (int)Math.Round(b.Width),
        (int)Math.Round(b.Height));

    private static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            int hr = NativeMethods.DwmGetWindowAttribute(
                hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }
        catch { return false; }
    }

    private static Rectangle GetFrameBounds(IntPtr hwnd)
    {
        try
        {
            int hr = NativeMethods.DwmGetWindowAttribute(
                hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out NativeMethods.RECT r, Marshal_SizeOfRect);
            if (hr == 0)
            {
                var rect = r.ToRectangle();
                if (rect.Width > 0 && rect.Height > 0) return rect;
            }
        }
        catch { /* fall through */ }

        return NativeMethods.GetWindowRect(hwnd, out var wr)
            ? wr.ToRectangle()
            : Rectangle.Empty;
    }

    private static readonly int Marshal_SizeOfRect =
        System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>();

    private static string GetTitle(IntPtr hwnd)
    {
        try
        {
            var sb = new System.Text.StringBuilder(256);
            int len = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString() : string.Empty;
        }
        catch { return string.Empty; }
    }
}
