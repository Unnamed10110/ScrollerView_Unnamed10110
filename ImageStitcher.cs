using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace ScrollerCapture;

/// <summary>
/// Aligns and concatenates a sequence of equally-sized captures of a region
/// that scrolls on one axis between frames. For each pair, finds the pixel
/// shift along that axis by minimizing SAD on a central band perpendicular
/// to the scroll axis, then appends only the newly revealed pixels so the
/// final image has no overlap and no cut.
/// </summary>
internal static class ImageStitcher
{
    private const int AnchorPixels = 96;
    private const int MinShift = 2;
    private const double BandStart = 0.10;
    private const double BandEnd = 0.90;
    private const int MaxAcceptableErrorPerPixel = 28;

    public static Bitmap? Stitch(IReadOnlyList<Bitmap> captures, CaptureDirection direction, out string? message)
        => Stitch(captures, direction, StickyTrimMode.Auto, out message);

    public static Bitmap? Stitch(
        IReadOnlyList<Bitmap> captures,
        CaptureDirection direction,
        StickyTrimMode sticky,
        out string? message)
    {
        message = null;

        if (captures == null || captures.Count == 0)
        {
            message = "No captures.";
            return null;
        }

        int w = captures[0].Width;
        int h = captures[0].Height;
        for (int i = 1; i < captures.Count; i++)
        {
            if (captures[i].Width != w || captures[i].Height != h)
            {
                message = "Captures have inconsistent sizes.";
                return null;
            }
        }

        if (captures.Count == 1)
        {
            return new Bitmap(captures[0]);
        }

        return direction == CaptureDirection.Horizontal
            ? StitchHorizontal(captures, w, h, out message)
            : StitchVertical(captures, w, h, sticky, out message);
    }

    // ------------------------------------------------------------------
    // Horizontal stitching
    // ------------------------------------------------------------------

    private static Bitmap? StitchHorizontal(IReadOnlyList<Bitmap> captures, int w, int h, out string? message)
    {
        message = null;

        int bandTop = (int)Math.Round(h * BandStart);
        int bandBottom = (int)Math.Round(h * BandEnd);
        if (bandBottom - bandTop < 8)
        {
            bandTop = 0;
            bandBottom = h;
        }
        int bandHeight = bandBottom - bandTop;

        var bands = new byte[captures.Count][];
        for (int i = 0; i < captures.Count; i++)
        {
            bands[i] = ExtractGrayscaleBand(captures[i], new Rectangle(0, bandTop, w, bandHeight));
        }

        var shifts = new int[captures.Count];
        for (int i = 1; i < captures.Count; i++)
        {
            int shift = FindHorizontalShift(bands[i - 1], bands[i], w, bandHeight, out var quality);
            if (shift < MinShift || quality < 0)
            {
                message = $"Could not align horizontal capture {i + 1} of {captures.Count} (best shift={shift}, quality={quality}).";
                return null;
            }
            shifts[i] = shift;
        }

        var cumShift = new int[captures.Count];
        for (int i = 1; i < captures.Count; i++)
        {
            cumShift[i] = cumShift[i - 1] + shifts[i];
        }
        int totalWidth = w + cumShift[captures.Count - 1];

        var result = new Bitmap(totalWidth, h, PixelFormat.Format32bppArgb);
        try
        {
            CopyRect(captures[0], srcX: 0, srcY: 0, copyW: w, copyH: h, result, dstX: 0, dstY: 0);
            for (int i = 1; i < captures.Count; i++)
            {
                int s = shifts[i];
                int srcX = w - s;
                int dstX = w + cumShift[i - 1];
                CopyRect(captures[i], srcX: srcX, srcY: 0, copyW: s, copyH: h, result, dstX: dstX, dstY: 0);
            }
        }
        catch
        {
            result.Dispose();
            throw;
        }
        return result;
    }

    private static int FindHorizontalShift(byte[] prev, byte[] cur, int w, int h, out int quality)
    {
        int anchorWidth = Math.Min(AnchorPixels, Math.Max(8, w / 6));
        int maxShift = w - anchorWidth - 1;
        if (maxShift < MinShift)
        {
            quality = -1;
            return 0;
        }

        int anchorXInPrev = w - anchorWidth;

        long bestErr = long.MaxValue;
        int bestShift = 0;
        for (int s = MinShift; s <= maxShift; s++)
        {
            int anchorXInCur = anchorXInPrev - s;
            if (anchorXInCur < 0) break;

            long err = 0;
            for (int y = 0; y < h; y++)
            {
                int rowP = y * w + anchorXInPrev;
                int rowC = y * w + anchorXInCur;
                for (int x = 0; x < anchorWidth; x++)
                {
                    int d = prev[rowP + x] - cur[rowC + x];
                    err += d >= 0 ? d : -d;
                }
                if (err >= bestErr) break;
            }
            if (err < bestErr)
            {
                bestErr = err;
                bestShift = s;
            }
        }

        long pixels = (long)anchorWidth * h;
        double meanErr = pixels > 0 ? (double)bestErr / pixels : double.MaxValue;
        quality = meanErr <= MaxAcceptableErrorPerPixel ? (int)(1000 - meanErr * 10) : -1;
        return bestShift;
    }

    // ------------------------------------------------------------------
    // Vertical stitching
    // ------------------------------------------------------------------

    private static Bitmap? StitchVertical(
        IReadOnlyList<Bitmap> captures, int w, int h, StickyTrimMode sticky, out string? message)
    {
        message = null;

        int bandLeft = (int)Math.Round(w * BandStart);
        int bandRight = (int)Math.Round(w * BandEnd);
        if (bandRight - bandLeft < 8)
        {
            bandLeft = 0;
            bandRight = w;
        }
        int bandWidth = bandRight - bandLeft;

        var bands = new byte[captures.Count][];
        for (int i = 0; i < captures.Count; i++)
        {
            bands[i] = ExtractGrayscaleBand(captures[i], new Rectangle(bandLeft, 0, bandWidth, h));
        }

        // Detect sticky header (top rows that don't change across frames) and
        // sticky footer (bottom rows that don't change). Skip in Off mode.
        int topH = 0, bottomH = 0;
        if (sticky != StickyTrimMode.Off)
        {
            int maxBand = sticky == StickyTrimMode.Aggressive ? h / 3 : h / 5;
            int threshold = sticky == StickyTrimMode.Aggressive ? 8 : 3;
            topH = DetectStickyTop(bands, bandWidth, h, maxBand, threshold);
            bottomH = DetectStickyBottom(bands, bandWidth, h, maxBand, threshold);

            // Refuse silly results (sticky bands swallowing the whole viewport).
            if (topH + bottomH > h / 2)
            {
                topH = 0;
                bottomH = 0;
            }
        }

        int anchorAreaTop = topH;
        int anchorAreaBottom = h - bottomH; // exclusive
        if (anchorAreaBottom - anchorAreaTop < 16)
        {
            // Fall back: ignore sticky detection if it leaves no usable area.
            anchorAreaTop = 0;
            anchorAreaBottom = h;
            topH = 0;
            bottomH = 0;
        }

        var shifts = new int[captures.Count];
        for (int i = 1; i < captures.Count; i++)
        {
            int shift = FindVerticalShift(
                bands[i - 1], bands[i], bandWidth, h,
                anchorAreaTop, anchorAreaBottom, out var quality);
            if (shift < MinShift || quality < 0)
            {
                message = $"Could not align vertical capture {i + 1} of {captures.Count} (best shift={shift}, quality={quality}).";
                return null;
            }
            shifts[i] = shift;
        }

        // Final image height: first frame contributes h rows. Each subsequent
        // frame contributes only its newly revealed rows, with the sticky
        // footer band excluded (except for the very last frame so the page's
        // real footer is preserved once at the end).
        int totalHeight = h;
        for (int i = 1; i < captures.Count; i++)
        {
            int s = shifts[i];
            bool isLast = i == captures.Count - 1;
            int contribute = isLast ? s : Math.Max(0, s - bottomH);
            totalHeight += contribute;
        }

        var result = new Bitmap(w, totalHeight, PixelFormat.Format32bppArgb);
        try
        {
            // First frame: copy in full. Includes any header AND footer present.
            CopyRect(captures[0], srcX: 0, srcY: 0, copyW: w, copyH: h, result, dstX: 0, dstY: 0);

            int dstYNext = h;
            // If we have a sticky footer, the first frame already wrote the
            // footer at rows [h-bottomH..h]. When we want subsequent newly
            // revealed pixels to land in the FINAL image, they must overwrite
            // the previous frame's footer area, because that area is sticky
            // and would otherwise leak duplicates. Track the current "write
            // position" right where new content goes.
            // dstYNext starts at h (after first full frame).

            for (int i = 1; i < captures.Count; i++)
            {
                int s = shifts[i];
                bool isLast = i == captures.Count - 1;

                // Bottom edge of source frame contains newly revealed rows
                // [h-s..h]. Exclude footer rows [h-bottomH..h] except on last.
                int srcBottom = isLast ? h : h - bottomH;
                int srcTop = h - s;
                if (srcBottom > h) srcBottom = h;
                if (srcTop < 0) srcTop = 0;
                int copyH = Math.Max(0, srcBottom - srcTop);
                if (copyH <= 0) continue;

                if (bottomH > 0 && i == 1)
                {
                    // For frame 1, the very first appended segment starts
                    // exactly where the previous frame's footer began. Slide
                    // the write position back so the new content overwrites
                    // the duplicated footer that came from frame 0.
                    dstYNext = h - bottomH;
                    // Adjust total height: we already counted h+contribute_1+...
                    // but allocated using same formula. We allocate with same
                    // total either way; the difference is purely the starting
                    // write offset.
                }

                CopyRect(
                    captures[i],
                    srcX: 0, srcY: srcTop, copyW: w, copyH: copyH,
                    result, dstX: 0, dstY: dstYNext);
                dstYNext += copyH;
            }

            // The exact final pixel count may differ slightly from the
            // pre-allocated height when bottomH > 0 because of the overwrite
            // trick above. Crop to the actually-used range to keep the image
            // tidy.
            if (dstYNext != totalHeight && dstYNext > 0 && dstYNext <= result.Height)
            {
                using (result)
                {
                    var trimmed = new Bitmap(w, dstYNext, PixelFormat.Format32bppArgb);
                    CopyRect(result, 0, 0, w, dstYNext, trimmed, 0, 0);
                    if (topH > 0 || bottomH > 0)
                    {
                        message = $"Stitched with sticky trim: top {topH}px, bottom {bottomH}px.";
                    }
                    return trimmed;
                }
            }
        }
        catch
        {
            result.Dispose();
            throw;
        }
        if (topH > 0 || bottomH > 0)
        {
            message = $"Stitched with sticky trim: top {topH}px, bottom {bottomH}px.";
        }
        return result;
    }

    private static int FindVerticalShift(
        byte[] prev, byte[] cur, int w, int h,
        int anchorAreaTop, int anchorAreaBottom,
        out int quality)
    {
        int usableHeight = anchorAreaBottom - anchorAreaTop;
        int anchorHeight = Math.Min(AnchorPixels, Math.Max(8, usableHeight / 6));
        // The anchor is the bottom slice of prev (just above sticky footer).
        // Its position inside prev is [anchorYInPrev .. anchorYInPrev+anchorHeight].
        int anchorYInPrev = anchorAreaBottom - anchorHeight;
        if (anchorYInPrev < anchorAreaTop)
        {
            quality = -1;
            return 0;
        }
        int maxShift = anchorYInPrev - anchorAreaTop;
        if (maxShift < MinShift)
        {
            quality = -1;
            return 0;
        }

        long bestErr = long.MaxValue;
        int bestShift = 0;
        for (int s = MinShift; s <= maxShift; s++)
        {
            int anchorYInCur = anchorYInPrev - s;
            if (anchorYInCur < anchorAreaTop) break;

            long err = 0;
            for (int y = 0; y < anchorHeight; y++)
            {
                int rowP = (anchorYInPrev + y) * w;
                int rowC = (anchorYInCur + y) * w;
                for (int x = 0; x < w; x++)
                {
                    int d = prev[rowP + x] - cur[rowC + x];
                    err += d >= 0 ? d : -d;
                }
                if (err >= bestErr) break;
            }
            if (err < bestErr)
            {
                bestErr = err;
                bestShift = s;
            }
        }

        long pixels = (long)anchorHeight * w;
        double meanErr = pixels > 0 ? (double)bestErr / pixels : double.MaxValue;
        quality = meanErr <= MaxAcceptableErrorPerPixel ? (int)(1000 - meanErr * 10) : -1;
        return bestShift;
    }

    private static int DetectStickyTop(byte[][] bands, int w, int h, int maxBand, int threshold)
    {
        int n = bands.Length;
        if (n < 2 || maxBand <= 0) return 0;
        int upper = Math.Min(maxBand, h);
        int stickyRows = 0;
        for (int row = 0; row < upper; row++)
        {
            if (!RowMatchesAcrossFrames(bands, w, row, threshold)) break;
            stickyRows = row + 1;
        }
        // Require some minimum so single-row matches don't trigger.
        return stickyRows >= 4 ? stickyRows : 0;
    }

    private static int DetectStickyBottom(byte[][] bands, int w, int h, int maxBand, int threshold)
    {
        int n = bands.Length;
        if (n < 2 || maxBand <= 0) return 0;
        int upper = Math.Min(maxBand, h);
        int stickyRows = 0;
        for (int k = 0; k < upper; k++)
        {
            int row = h - 1 - k;
            if (!RowMatchesAcrossFrames(bands, w, row, threshold)) break;
            stickyRows = k + 1;
        }
        return stickyRows >= 4 ? stickyRows : 0;
    }

    private static bool RowMatchesAcrossFrames(byte[][] bands, int w, int row, int threshold)
    {
        int n = bands.Length;
        long totalAbs = 0;
        long totalPixels = 0;
        for (int i = 1; i < n; i++)
        {
            long abs = 0;
            int rowP = row * w;
            int rowC = row * w;
            var prev = bands[0];
            var cur = bands[i];
            for (int x = 0; x < w; x++)
            {
                int d = prev[rowP + x] - cur[rowC + x];
                abs += d >= 0 ? d : -d;
            }
            totalAbs += abs;
            totalPixels += w;
        }
        if (totalPixels == 0) return false;
        double mean = (double)totalAbs / totalPixels;
        return mean <= threshold;
    }

    // ------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns a row-major grayscale buffer covering <paramref name="rect"/>
    /// inside <paramref name="src"/>. Width of the buffer equals
    /// <c>rect.Width</c>, height equals <c>rect.Height</c>.
    /// </summary>
    private static byte[] ExtractGrayscaleBand(Bitmap src, Rectangle rect)
    {
        var data = src.LockBits(
            rect,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            int w = rect.Width;
            int h = rect.Height;
            var gray = new byte[w * h];
            unsafe
            {
                byte* basePtr = (byte*)data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* p = basePtr + y * stride;
                    int gOffset = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        byte b = p[x * 4 + 0];
                        byte g = p[x * 4 + 1];
                        byte r = p[x * 4 + 2];
                        gray[gOffset + x] = (byte)((r * 299 + g * 587 + b * 114) / 1000);
                    }
                }
            }
            return gray;
        }
        finally
        {
            src.UnlockBits(data);
        }
    }

    private static void CopyRect(Bitmap src, int srcX, int srcY, int copyW, int copyH, Bitmap dst, int dstX, int dstY)
    {
        if (copyW <= 0 || copyH <= 0) return;

        var sData = src.LockBits(
            new Rectangle(srcX, srcY, copyW, copyH),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        var dData = dst.LockBits(
            new Rectangle(dstX, dstY, copyW, copyH),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* sp = (byte*)sData.Scan0;
                byte* dp = (byte*)dData.Scan0;
                int rowBytes = copyW * 4;
                for (int y = 0; y < copyH; y++)
                {
                    Buffer.MemoryCopy(sp + y * sData.Stride, dp + y * dData.Stride, rowBytes, rowBytes);
                }
            }
        }
        finally
        {
            src.UnlockBits(sData);
            dst.UnlockBits(dData);
        }
    }
}
