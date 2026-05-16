using System;
using System.Drawing;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ScrollerCapture;

internal enum PdfPageSize { A4, Letter, FitWidth }

/// <summary>
/// Paginates a tall capture into a multi-page PDF. The image width is
/// mapped to the page's drawable width and the image is sliced vertically
/// so each page shows a contiguous strip without overlap or distortion.
/// </summary>
internal static class PdfExportService
{
    public static void Export(Bitmap image, string filePath, PdfPageSize size, float marginInches = 0.4f)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path required.", nameof(filePath));

        using var doc = new PdfDocument();
        doc.Info.Title = AppBranding.DisplayName;
        doc.Info.Creator = AppBranding.DisplayName;

        double pageW, pageH; // in points (1/72 inch)
        switch (size)
        {
            case PdfPageSize.Letter:
                pageW = 8.5 * 72;
                pageH = 11 * 72;
                break;
            case PdfPageSize.FitWidth:
                // Pick a width that matches the image aspect ratio at A4 height.
                pageW = image.Width / (double)image.Height * (11.69 * 72);
                pageH = 11.69 * 72;
                if (pageW < 4 * 72) pageW = 4 * 72;
                if (pageW > 14 * 72) pageW = 14 * 72;
                break;
            case PdfPageSize.A4:
            default:
                pageW = 8.27 * 72;
                pageH = 11.69 * 72;
                break;
        }
        double margin = marginInches * 72;
        double drawW = pageW - margin * 2;
        double drawH = pageH - margin * 2;
        if (drawW <= 10 || drawH <= 10) throw new InvalidOperationException("Page too small after margins.");

        double scale = drawW / image.Width;
        double sliceHeightPx = drawH / scale;

        int total = image.Height;
        int y = 0;
        while (y < total)
        {
            int sliceH = (int)Math.Min(sliceHeightPx, total - y);
            if (sliceH <= 0) break;

            var page = doc.AddPage();
            page.Width = new XUnit(pageW);
            page.Height = new XUnit(pageH);
            using var g = XGraphics.FromPdfPage(page);
            using var slice = new Bitmap(image.Width, sliceH);
            using (var sg = Graphics.FromImage(slice))
            {
                sg.DrawImage(image, new Rectangle(0, 0, image.Width, sliceH),
                    new Rectangle(0, y, image.Width, sliceH),
                    GraphicsUnit.Pixel);
            }
            using var ms = new MemoryStream();
            slice.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            using var xi = XImage.FromStream(ms);
            g.DrawImage(xi, margin, margin, drawW, sliceH * scale);

            y += sliceH;
        }

        doc.Save(filePath);
    }
}
