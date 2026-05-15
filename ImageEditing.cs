using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScrollerCapture;

/// <summary>
/// Bitmap-level operations used by the editor: crop, mosaic blur, strip
/// removal, and final flattening of vector annotations onto the image.
/// </summary>
internal static class ImageEditing
{
    public const int BlurBlockSize = 12;

    public static Bitmap Crop(Bitmap source, Rectangle imageRect)
    {
        var clamped = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), imageRect);
        if (clamped.Width <= 0 || clamped.Height <= 0)
        {
            throw new ArgumentException("Crop rectangle is empty.", nameof(imageRect));
        }

        var result = new Bitmap(clamped.Width, clamped.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(
            source,
            new Rectangle(0, 0, clamped.Width, clamped.Height),
            clamped,
            GraphicsUnit.Pixel);
        return result;
    }

    /// <summary>
    /// Returns a fresh bitmap containing only the blurred region from
    /// <paramref name="source"/> in the dimensions of <paramref name="imageRect"/>.
    /// Used by editable BlurAnnotations during paint.
    /// </summary>
    public static Bitmap BlurRegionStandalone(Bitmap source, Rectangle imageRect, int blockSize = BlurBlockSize)
    {
        var clamped = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), imageRect);
        if (clamped.Width <= 0 || clamped.Height <= 0)
        {
            return new Bitmap(Math.Max(1, imageRect.Width), Math.Max(1, imageRect.Height), PixelFormat.Format32bppArgb);
        }
        blockSize = Math.Max(2, blockSize);
        int smallW = Math.Max(1, clamped.Width / blockSize);
        int smallH = Math.Max(1, clamped.Height / blockSize);
        using var small = new Bitmap(smallW, smallH, PixelFormat.Format32bppArgb);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            sg.CompositingMode = CompositingMode.SourceCopy;
            sg.DrawImage(source, new Rectangle(0, 0, smallW, smallH), clamped, GraphicsUnit.Pixel);
        }
        var result = new Bitmap(clamped.Width, clamped.Height, PixelFormat.Format32bppArgb);
        using var rg = Graphics.FromImage(result);
        rg.InterpolationMode = InterpolationMode.NearestNeighbor;
        rg.PixelOffsetMode = PixelOffsetMode.Half;
        rg.CompositingMode = CompositingMode.SourceCopy;
        rg.DrawImage(small, new Rectangle(0, 0, clamped.Width, clamped.Height), new Rectangle(0, 0, smallW, smallH), GraphicsUnit.Pixel);
        return result;
    }

    public static Bitmap BlurRegion(Bitmap source, Rectangle imageRect, int blockSize = BlurBlockSize)
    {
        var clamped = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), imageRect);
        if (clamped.Width <= 0 || clamped.Height <= 0)
        {
            return new Bitmap(source);
        }

        blockSize = Math.Max(2, blockSize);

        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        }

        int smallW = Math.Max(1, clamped.Width / blockSize);
        int smallH = Math.Max(1, clamped.Height / blockSize);
        using var small = new Bitmap(smallW, smallH, PixelFormat.Format32bppArgb);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            sg.CompositingMode = CompositingMode.SourceCopy;
            sg.DrawImage(source, new Rectangle(0, 0, smallW, smallH), clamped, GraphicsUnit.Pixel);
        }
        using (var rg = Graphics.FromImage(result))
        {
            rg.InterpolationMode = InterpolationMode.NearestNeighbor;
            rg.PixelOffsetMode = PixelOffsetMode.Half;
            rg.CompositingMode = CompositingMode.SourceCopy;
            rg.DrawImage(small, clamped, new Rectangle(0, 0, smallW, smallH), GraphicsUnit.Pixel);
        }
        return result;
    }

    /// <summary>
    /// Removes a full-width horizontal strip or full-height vertical strip
    /// from <paramref name="source"/> and joins the surrounding parts.
    /// The choice of orientation is based on the aspect ratio of
    /// <paramref name="dragRect"/>: wider-than-tall drags remove a
    /// horizontal strip, taller-than-wide drags remove a vertical strip.
    /// </summary>
    /// <returns>
    /// The shortened bitmap, the removed strip rectangle in source
    /// coordinates, and whether the strip was horizontal.
    /// </returns>
    public static Bitmap StripCutout(
        Bitmap source,
        Rectangle dragRect,
        out Rectangle stripRect,
        out bool horizontalStrip)
    {
        horizontalStrip = dragRect.Width >= dragRect.Height;
        stripRect = Rectangle.Empty;

        if (horizontalStrip)
        {
            int y = Math.Max(0, dragRect.Y);
            int height = Math.Min(source.Height - y, dragRect.Height);
            if (height <= 0 || height >= source.Height)
            {
                return new Bitmap(source);
            }
            stripRect = new Rectangle(0, y, source.Width, height);
            int newH = source.Height - height;
            var result = new Bitmap(source.Width, newH, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceCopy;
            if (y > 0)
            {
                g.DrawImage(
                    source,
                    new Rectangle(0, 0, source.Width, y),
                    new Rectangle(0, 0, source.Width, y),
                    GraphicsUnit.Pixel);
            }
            int bottomY = y + height;
            int bottomH = source.Height - bottomY;
            if (bottomH > 0)
            {
                g.DrawImage(
                    source,
                    new Rectangle(0, y, source.Width, bottomH),
                    new Rectangle(0, bottomY, source.Width, bottomH),
                    GraphicsUnit.Pixel);
            }
            return result;
        }
        else
        {
            int x = Math.Max(0, dragRect.X);
            int width = Math.Min(source.Width - x, dragRect.Width);
            if (width <= 0 || width >= source.Width)
            {
                return new Bitmap(source);
            }
            stripRect = new Rectangle(x, 0, width, source.Height);
            int newW = source.Width - width;
            var result = new Bitmap(newW, source.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceCopy;
            if (x > 0)
            {
                g.DrawImage(
                    source,
                    new Rectangle(0, 0, x, source.Height),
                    new Rectangle(0, 0, x, source.Height),
                    GraphicsUnit.Pixel);
            }
            int rightX = x + width;
            int rightW = source.Width - rightX;
            if (rightW > 0)
            {
                g.DrawImage(
                    source,
                    new Rectangle(x, 0, rightW, source.Height),
                    new Rectangle(rightX, 0, rightW, source.Height),
                    GraphicsUnit.Pixel);
            }
            return result;
        }
    }

    public static Bitmap Flatten(Bitmap baseBitmap, IReadOnlyList<EditorAnnotation> annotations)
    {
        var result = new Bitmap(baseBitmap.Width, baseBitmap.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(baseBitmap, new Rectangle(0, 0, result.Width, result.Height));

        g.CompositingMode = CompositingMode.SourceOver;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // Annotations that sample the base bitmap (Blur, Spotlight, Magnifier)
        // read it through BlurContext.CurrentBase.
        var prevContext = BlurContext.CurrentBase;
        BlurContext.CurrentBase = baseBitmap;
        try
        {
            foreach (var a in annotations)
            {
                a.Render(g);
            }
        }
        finally
        {
            BlurContext.CurrentBase = prevContext;
        }
        return result;
    }

    public static Rectangle NormalizeRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(a.X - b.X);
        var h = Math.Abs(a.Y - b.Y);
        return new Rectangle(x, y, w, h);
    }
}
