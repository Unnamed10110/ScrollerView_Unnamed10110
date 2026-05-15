using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ScrollerCapture;

/// <summary>
/// Shared application icon (blue circle with horizontal scroll arrows).
/// Used for the tray, WinForms windows, and the embedded .exe icon file.
/// </summary>
internal static class AppIconFactory
{
    private static readonly Color CircleColor = Color.FromArgb(32, 110, 200);

    public static Bitmap RenderBitmap(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        Draw(g, size);
        return bmp;
    }

    public static Icon CreateIcon(int size = 32)
    {
        using var bmp = RenderBitmap(size);
        var hIcon = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>Icon from the built executable when available; otherwise the rendered icon.</summary>
    public static Icon GetApplicationIcon()
    {
        try
        {
            var exePath = Path.Combine(AppContext.BaseDirectory, "ScrollerCapture.exe");
            if (File.Exists(exePath))
            {
                var extracted = Icon.ExtractAssociatedIcon(exePath);
                if (extracted != null) return extracted;
            }
        }
        catch { /* fall through */ }

        return CreateIcon(32);
    }

    /// <summary>Writes a multi-size .ico file (PNG payloads) for ApplicationIcon and the installer.</summary>
    public static void WriteIcoFile(string path, params int[] sizes)
    {
        if (sizes == null || sizes.Length == 0)
            sizes = [16, 32, 48, 256];

        var pngs = new List<byte[]>(sizes.Length);
        var dims = new List<(int w, int h)>(sizes.Length);
        foreach (var size in sizes)
        {
            using var bmp = RenderBitmap(size);
            dims.Add((bmp.Width, bmp.Height));
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0);
        bw.Write((short)1);
        bw.Write((short)pngs.Count);

        int offset = 6 + 16 * pngs.Count;
        for (int i = 0; i < pngs.Count; i++)
        {
            int w = dims[i].w;
            int h = dims[i].h;
            bw.Write((byte)(w >= 256 ? 0 : w));
            bw.Write((byte)(h >= 256 ? 0 : h));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((short)1);
            bw.Write((short)32);
            bw.Write(pngs[i].Length);
            bw.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (var png in pngs)
            bw.Write(png);
    }

    private static void Draw(Graphics g, int size)
    {
        float scale = size / 32f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        int margin = Math.Max(1, (int)Math.Round(scale));
        int diameter = size - margin * 2;
        using (var bg = new SolidBrush(CircleColor))
        {
            g.FillEllipse(bg, margin, margin, diameter, diameter);
        }

        float penWidth = Math.Max(1f, 2.5f * scale);
        using var pen = new Pen(Color.White, penWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        int cy = size / 2;
        int xL = Scale(7, scale);
        int xR = Scale(25, scale);
        int xLa = Scale(11, scale);
        int xRa = Scale(21, scale);
        int yTop = Scale(12, scale);
        int yBot = Scale(20, scale);

        g.DrawLine(pen, xL, cy, xR, cy);
        g.DrawLine(pen, xL, cy, xLa, yTop);
        g.DrawLine(pen, xL, cy, xLa, yBot);
        g.DrawLine(pen, xR, cy, xRa, yTop);
        g.DrawLine(pen, xR, cy, xRa, yBot);
    }

    private static int Scale(int valueAt32, float scale) =>
        Math.Max(0, (int)Math.Round(valueAt32 * scale));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
