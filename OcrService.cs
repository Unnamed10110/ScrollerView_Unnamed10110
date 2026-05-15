using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ScrollerCapture;

internal sealed class OcrWord
{
    public string Text { get; set; } = "";
    public Rectangle Bounds;
}

internal sealed class OcrLine
{
    public string Text { get; set; } = "";
    public List<OcrWord> Words { get; } = new();
    public Rectangle Bounds;
}

internal sealed class OcrResult
{
    public string PlainText { get; set; } = "";
    public List<OcrLine> Lines { get; } = new();
    public Size ImageSize;
}

/// <summary>
/// Windows built-in OCR (Windows.Media.Ocr). Available on Windows 10 1809+
/// with a recognized OCR language pack. Results are returned in image
/// coordinates so the editor can highlight matches directly.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class OcrService
{
    public static bool IsAvailable
    {
        get
        {
            try
            {
                var langs = OcrEngine.AvailableRecognizerLanguages;
                return langs != null && langs.Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public static async Task<OcrResult?> RecognizeAsync(Bitmap source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!IsAvailable) return null;

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
            var langs = OcrEngine.AvailableRecognizerLanguages;
            if (langs != null && langs.Count > 0)
            {
                engine = OcrEngine.TryCreateFromLanguage(langs[0]);
            }
        }
        if (engine == null) return null;

        SoftwareBitmap? soft;
        using (var ms = new MemoryStream())
        {
            source.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            using var ras = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(ras))
            {
                writer.WriteBytes(ms.ToArray());
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            ras.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(ras);
            soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }

        var result = await engine.RecognizeAsync(soft);
        var output = new OcrResult
        {
            PlainText = result.Text ?? string.Empty,
            ImageSize = new Size(source.Width, source.Height),
        };
        foreach (var line in result.Lines)
        {
            var ol = new OcrLine { Text = line.Text ?? "" };
            int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
            foreach (var word in line.Words)
            {
                var b = word.BoundingRect;
                var rect = new Rectangle(
                    (int)Math.Round(b.X),
                    (int)Math.Round(b.Y),
                    (int)Math.Round(b.Width),
                    (int)Math.Round(b.Height));
                ol.Words.Add(new OcrWord { Text = word.Text ?? "", Bounds = rect });
                if (rect.X < minX) minX = rect.X;
                if (rect.Y < minY) minY = rect.Y;
                if (rect.Right > maxX) maxX = rect.Right;
                if (rect.Bottom > maxY) maxY = rect.Bottom;
            }
            if (ol.Words.Count > 0)
            {
                ol.Bounds = Rectangle.FromLTRB(minX, minY, maxX, maxY);
            }
            output.Lines.Add(ol);
        }
        return output;
    }
}
