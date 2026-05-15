using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace ScrollerCapture;

/// <summary>
/// GDI-based screen capture that copies a rectangle of the virtual desktop
/// in physical pixels (PerMonitorV2 aware).
/// </summary>
internal static class ScreenCapture
{
    public static Bitmap CaptureScreenRegion(Rectangle screenRegion)
    {
        if (screenRegion.Width <= 0 || screenRegion.Height <= 0)
        {
            throw new ArgumentException("Empty screen region", nameof(screenRegion));
        }

        var bmp = new Bitmap(screenRegion.Width, screenRegion.Height, PixelFormat.Format32bppArgb);

        IntPtr screenDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr hBmp = IntPtr.Zero;
        IntPtr hBmpOld = IntPtr.Zero;
        try
        {
            screenDc = NativeMethods.GetDC(IntPtr.Zero);
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            hBmp = NativeMethods.CreateCompatibleBitmap(screenDc, screenRegion.Width, screenRegion.Height);
            hBmpOld = NativeMethods.SelectObject(memDc, hBmp);

            NativeMethods.BitBlt(
                memDc, 0, 0, screenRegion.Width, screenRegion.Height,
                screenDc, screenRegion.X, screenRegion.Y,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

            using var captured = Image.FromHbitmap(hBmp);
            using var g = Graphics.FromImage(bmp);
            g.DrawImageUnscaled(captured, 0, 0);
        }
        finally
        {
            if (hBmpOld != IntPtr.Zero) NativeMethods.SelectObject(memDc, hBmpOld);
            if (hBmp != IntPtr.Zero) NativeMethods.DeleteObject(hBmp);
            if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }

        return bmp;
    }
}
