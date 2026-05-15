using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ScrollerCapture;

internal sealed class ScrollCaptureService
{
    public StickyTrimMode StickyTrim { get; set; } = StickyTrimMode.Auto;

    /// <summary>Time we wait for a scroll to settle (smooth-scroll animation, layout reflow).</summary>
    private const int SettleAfterScrollMs = 220;
    /// <summary>Time we wait after parking the cursor at the safe location, to let hover state fade.</summary>
    private const int CursorSafeSettleMs = 180;
    /// <summary>Hard upper bound on capture frames, to protect against infinite scrolls.</summary>
    private const int MaxIterations = 200;
    /// <summary>Wheel notches sent per fallback step (each notch = WHEEL_DELTA = 120 units).</summary>
    private const int WheelNotchesPerStep = 3;

    public CaptureResult Capture(Rectangle screenRegion, CaptureDirection direction)
    {
        NativeMethods.SetThreadDpiAwarenessContext(
            (IntPtr)NativeMethods.DPI_AWARENESS_CONTEXT.PER_MONITOR_AWARE_V2);

        var target = ScrollableElementFinder.Find(screenRegion, direction);
        if (target == null)
        {
            return new CaptureResult
            {
                Success = false,
                Mode = direction == CaptureDirection.Horizontal ? CaptureMode.Horizontal : CaptureMode.Vertical,
                Direction = direction,
                Message = direction == CaptureDirection.Horizontal
                    ? "Selected region is not a horizontally scrollable UI element."
                    : "Selected region is not a vertically scrollable UI element.",
            };
        }

        // Remember where the user's cursor was so we can restore it after we
        // finish manipulating it for scroll/wheel events.
        bool hadCursor = NativeMethods.GetCursorPos(out var originalPt);
        var safeCursor = ComputeSafeCursorPos(screenRegion);

        double originalPercent = target.ScrollPercent;
        bool stoppedEarly = false;

        // Reset to the start of the scrollable element.
        target.TrySetScrollPercent(0.0);
        SleepPollEscape(SettleAfterScrollMs, ref stoppedEarly);

        // Park the cursor outside the capture region and give any hover state
        // (tooltips, button highlight, smooth-scroll indicators) time to fade.
        NativeMethods.SetCursorPos(safeCursor.X, safeCursor.Y);
        SleepPollEscape(CursorSafeSettleMs, ref stoppedEarly);

        var captures = new List<Bitmap>();
        try
        {
            captures.Add(ScreenCapture.CaptureScreenRegion(screenRegion));

            if (!stoppedEarly)
            {
                double viewSize = target.ViewSize;
                if (double.IsNaN(viewSize) || viewSize <= 0 || viewSize > 100) viewSize = 25;
                double step = Math.Max(viewSize * 0.6, 3.0);

                int stagnantCount = 0;
                for (int i = 0; i < MaxIterations; i++)
                {
                    if (CheckEscape(ref stoppedEarly)) break;
                    if (target.IsAtEnd) break;

                    double currentPercent = target.ScrollPercent;

                    // 1) Try UI Automation first.
                    bool uiaMoved = false;
                    if (!double.IsNaN(currentPercent))
                    {
                        double nextPercent = Math.Min(100.0, currentPercent + step);
                        uiaMoved = target.TrySetScrollPercent(nextPercent);
                    }
                    if (uiaMoved && SleepPollEscape(SettleAfterScrollMs, ref stoppedEarly))
                        break;

                    double afterPercent = target.ScrollPercent;
                    bool uiaAdvanced = uiaMoved &&
                                       !double.IsNaN(currentPercent) &&
                                       !double.IsNaN(afterPercent) &&
                                       (afterPercent - currentPercent) > 0.05;

                    // 2) If UIA didn't actually move the content (common in browsers
                    //    that don't keep ScrollPercent in sync after wheel), fall
                    //    back to a synthetic mouse-wheel event. We always send the
                    //    wheel even when UIA reports success but didn't advance,
                    //    because the UIA percent is sometimes stale rather than
                    //    failed.
                    if (!uiaAdvanced)
                    {
                        if (!ScrollByMouseWheel(target, screenRegion, direction, WheelNotchesPerStep))
                        {
                            break;
                        }
                        if (SleepPollEscape(SettleAfterScrollMs, ref stoppedEarly))
                            break;
                    }

                    // Re-park the cursor before capturing so hover state cannot
                    // poison the frame and the cursor itself can't be captured.
                    NativeMethods.SetCursorPos(safeCursor.X, safeCursor.Y);
                    if (SleepPollEscape(CursorSafeSettleMs, ref stoppedEarly))
                        break;

                    var snap = ScreenCapture.CaptureScreenRegion(screenRegion);

                    // Pixel-based end detection: if multiple sample bands are
                    // essentially identical to the previous frame, the page did
                    // not move so we have reached the bottom (or scrolling is no
                    // longer possible). Two such frames in a row stops the loop.
                    if (FramesLookIdentical(captures[^1], snap))
                    {
                        snap.Dispose();
                        stagnantCount++;
                        if (stagnantCount >= 2) break;
                        continue;
                    }
                    stagnantCount = 0;
                    captures.Add(snap);

                    if (!double.IsNaN(afterPercent) && afterPercent >= 99.95) break;
                }
            }

            if (stoppedEarly && captures.Count == 0)
                captures.Add(ScreenCapture.CaptureScreenRegion(screenRegion));

            if (!double.IsNaN(originalPercent))
            {
                target.TrySetScrollPercent(originalPercent);
            }

            var mode = direction == CaptureDirection.Horizontal ? CaptureMode.Horizontal : CaptureMode.Vertical;

            if (captures.Count == 1)
            {
                var only = captures[0];
                captures.Clear();
                return new CaptureResult
                {
                    Success = true,
                    Image = only,
                    PartCount = 1,
                    Mode = mode,
                    Direction = direction,
                    Message = stoppedEarly
                        ? "Stopped early (Esc). Single frame captured."
                        : "Content was not scrollable beyond the visible viewport.",
                };
            }

            var stitched = ImageStitcher.Stitch(captures, direction, StickyTrim, out var stitchMsg);
            string message = stitchMsg ?? string.Empty;
            if (stoppedEarly)
            {
                message = $"Stopped early (Esc). Stitched {captures.Count} frame(s)."
                    + (string.IsNullOrWhiteSpace(stitchMsg) ? "" : " " + stitchMsg);
            }

            return new CaptureResult
            {
                Success = stitched != null,
                Image = stitched,
                PartCount = captures.Count,
                Mode = mode,
                Direction = direction,
                Message = message,
            };
        }
        finally
        {
            foreach (var b in captures) b.Dispose();
            // Always restore the user's cursor, even on exceptions.
            if (hadCursor) NativeMethods.SetCursorPos(originalPt.X, originalPt.Y);
        }
    }

    private static bool CheckEscape(ref bool stoppedEarly)
    {
        if (!NativeMethods.IsEscapeDown()) return false;
        stoppedEarly = true;
        return true;
    }

    /// <summary>
    /// Sleeps in short slices so Escape can stop capture during settle delays.
    /// Returns true when Escape was pressed.
    /// </summary>
    private static bool SleepPollEscape(int ms, ref bool stoppedEarly)
    {
        const int slice = 50;
        int elapsed = 0;
        while (elapsed < ms)
        {
            if (CheckEscape(ref stoppedEarly)) return true;
            int step = Math.Min(slice, ms - elapsed);
            Thread.Sleep(step);
            elapsed += step;
        }
        return false;
    }

    /// <summary>
    /// Picks a screen point well outside <paramref name="region"/> where we
    /// can park the cursor between scroll attempts. Prefers the corner of the
    /// monitor containing the region that is farthest from the region's
    /// center, so we don't sit on top of interactive content that may render
    /// hover styles into the captured pixels.
    /// </summary>
    private static Point ComputeSafeCursorPos(Rectangle region)
    {
        Rectangle screen;
        try { screen = Screen.FromRectangle(region).Bounds; }
        catch { screen = SystemInformation.VirtualScreen; }

        int cx = region.Left + region.Width / 2;
        int cy = region.Top + region.Height / 2;

        var corners = new[]
        {
            new Point(screen.Left + 2, screen.Top + 2),
            new Point(screen.Right - 2, screen.Top + 2),
            new Point(screen.Left + 2, screen.Bottom - 2),
            new Point(screen.Right - 2, screen.Bottom - 2),
        };

        Point best = corners[0];
        long bestDist = -1;
        foreach (var p in corners)
        {
            if (region.Contains(p)) continue;
            long d = (long)(p.X - cx) * (p.X - cx) + (long)(p.Y - cy) * (p.Y - cy);
            if (d > bestDist) { bestDist = d; best = p; }
        }
        if (bestDist >= 0) return best;

        // All monitor corners are inside the region. Try edges of the monitor.
        if (region.Right < screen.Right - 4) return new Point(region.Right + 4, region.Top + region.Height / 2);
        if (region.Left > screen.Left + 4) return new Point(region.Left - 4, region.Top + region.Height / 2);
        if (region.Bottom < screen.Bottom - 4) return new Point(region.Left + region.Width / 2, region.Bottom + 4);
        if (region.Top > screen.Top + 4) return new Point(region.Left + region.Width / 2, region.Top - 4);

        // Region fills the entire monitor. Use a virtual-screen corner.
        var v = SystemInformation.VirtualScreen;
        return new Point(v.Left + 1, v.Top + 1);
    }

    /// <summary>
    /// Sends N wheel notches in the appropriate axis after temporarily moving
    /// the cursor over the scrollable element. The cursor is NOT restored
    /// here; the caller is expected to re-park it before capturing.
    /// </summary>
    private static bool ScrollByMouseWheel(
        ScrollableTarget target,
        Rectangle screenRegion,
        CaptureDirection direction,
        int notches)
    {
        var intersect = Rectangle.Intersect(target.Bounds, screenRegion);
        if (intersect.IsEmpty) intersect = screenRegion;
        int cx = intersect.X + intersect.Width / 2;
        int cy = intersect.Y + intersect.Height / 2;

        NativeMethods.SetCursorPos(cx, cy);
        Thread.Sleep(15);

        uint flags;
        int delta;
        if (direction == CaptureDirection.Horizontal)
        {
            // Positive HWHEEL = scroll right (advance horizontal position).
            flags = NativeMethods.MOUSEEVENTF_HWHEEL;
            delta = NativeMethods.WHEEL_DELTA * notches;
        }
        else
        {
            // Negative WHEEL = content scrolls down (advance vertical position).
            flags = NativeMethods.MOUSEEVENTF_WHEEL;
            delta = -NativeMethods.WHEEL_DELTA * notches;
        }

        var inputs = new NativeMethods.INPUT[1];
        inputs[0].type = NativeMethods.INPUT_MOUSE;
        inputs[0].union.mi = new NativeMethods.MOUSEINPUT
        {
            dx = 0,
            dy = 0,
            mouseData = unchecked((uint)delta),
            dwFlags = flags,
            time = 0,
            dwExtraInfo = IntPtr.Zero,
        };
        uint sent = NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        return sent > 0;
    }

    /// <summary>
    /// Detects "the page didn't actually move" by checking multiple
    /// horizontal sample bands. If the per-channel mean absolute difference
    /// across all bands is tiny, the two frames are considered identical.
    /// The bands are spread across the height so a small localized animation
    /// (clock, status indicator) cannot make a stuck page look like it moved.
    /// </summary>
    private static bool FramesLookIdentical(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        int w = a.Width, h = a.Height;
        int bandH = Math.Max(8, Math.Min(40, h / 20));
        if (bandH > h) bandH = h;

        int[] yCenters = { h / 4, h / 2, 3 * h / 4 };
        long totalSad = 0;
        long totalPixels = 0;
        foreach (var yc in yCenters)
        {
            int y0 = Math.Max(0, Math.Min(h - bandH, yc - bandH / 2));
            var rect = new Rectangle(0, y0, w, bandH);
            totalSad += ComputeSad(a, b, rect);
            totalPixels += (long)rect.Width * rect.Height;
        }
        if (totalPixels == 0) return false;
        double meanErrPerChannel = (double)totalSad / (totalPixels * 3);
        return meanErrPerChannel < 1.0;
    }

    private static long ComputeSad(Bitmap a, Bitmap b, Rectangle rect)
    {
        var ad = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bd = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        long sad = 0;
        try
        {
            unsafe
            {
                byte* ap = (byte*)ad.Scan0;
                byte* bp = (byte*)bd.Scan0;
                int rowBytes = rect.Width * 4;
                for (int y = 0; y < rect.Height; y++)
                {
                    byte* rowA = ap + y * ad.Stride;
                    byte* rowB = bp + y * bd.Stride;
                    for (int x = 0; x < rowBytes; x++)
                    {
                        int d = rowA[x] - rowB[x];
                        sad += d >= 0 ? d : -d;
                    }
                }
            }
        }
        finally
        {
            a.UnlockBits(ad);
            b.UnlockBits(bd);
        }
        return sad;
    }
}
