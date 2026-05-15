using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ScrollerCapture;

/// <summary>
/// Manual scroll fallback. Shows a small topmost controller overlay, samples
/// the selected region periodically while the user scrolls, and stitches
/// the collected frames once the user presses Enter.
/// </summary>
internal static class ManualCaptureService
{
    public static CaptureResult Run(Rectangle region, CaptureOptions options)
    {
        using var controller = new ManualController(region, options);
        var dr = controller.ShowDialog();
        var frames = controller.TakeFrames();
        try
        {
            if (dr != DialogResult.OK || frames.Count == 0)
            {
                foreach (var f in frames) f.Dispose();
                return new CaptureResult
                {
                    Success = false,
                    Mode = CaptureMode.Manual,
                    Message = "Manual capture cancelled.",
                };
            }

            if (frames.Count == 1)
            {
                var only = frames[0];
                frames.Clear();
                return new CaptureResult
                {
                    Success = true,
                    Image = only,
                    PartCount = 1,
                    Mode = CaptureMode.Manual,
                    Message = "Only one frame captured.",
                };
            }

            // Decide dominant direction by comparing horizontal vs vertical
            // pixel-shift hints across consecutive frames.
            var direction = ManualHeuristics.GuessDirection(frames);
            var stitched = ImageStitcher.Stitch(frames, direction, options.StickyTrim, out var msg);
            return new CaptureResult
            {
                Success = stitched != null,
                Image = stitched,
                PartCount = frames.Count,
                Mode = CaptureMode.Manual,
                Direction = direction,
                Message = stitched != null ? msg : "Manual stitching failed: " + (msg ?? "unknown error"),
            };
        }
        finally
        {
            foreach (var f in frames) f.Dispose();
        }
    }

    private sealed class ManualController : Form
    {
        private readonly Rectangle _region;
        private readonly CaptureOptions _options;
        private readonly System.Windows.Forms.Timer _sampler;
        private readonly List<Bitmap> _frames = new();
        private Bitmap? _lastFrame;

        public ManualController(Rectangle region, CaptureOptions options)
        {
            _region = region;
            _options = options;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = Color.Black;
            Size = new Size(360, 60);
            KeyPreview = true;
            KeyDown += OnKeyDown;

            _sampler = new System.Windows.Forms.Timer
            {
                Interval = Math.Max(80, options.ManualSampleIntervalMs),
            };
            _sampler.Tick += (_, _) => SampleOnce();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // toolwindow
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle screen;
            try { screen = Screen.FromRectangle(_region).Bounds; }
            catch { screen = SystemInformation.VirtualScreen; }
            Location = new Point(
                screen.Left + (screen.Width - Width) / 2,
                screen.Top + 24);
            SampleOnce();
            _sampler.Start();
        }

        private void SampleOnce()
        {
            try
            {
                var snap = ScreenCapture.CaptureScreenRegion(_region);
                if (_lastFrame == null || !ManualHeuristics.FramesIdentical(_lastFrame, snap, _options.ManualMinDiff))
                {
                    _frames.Add(snap);
                    _lastFrame = snap;
                    if (_frames.Count >= _options.MaxFrames)
                    {
                        _sampler.Stop();
                        FinishOk();
                    }
                }
                else
                {
                    snap.Dispose();
                }
            }
            catch
            {
                // ignore sampling errors
            }
            Invalidate();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                _sampler.Stop();
                FinishOk();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _sampler.Stop();
                DialogResult = DialogResult.Cancel;
                Close();
                e.Handled = true;
            }
        }

        private void FinishOk()
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        public List<Bitmap> TakeFrames()
        {
            var list = new List<Bitmap>(_frames);
            _frames.Clear();
            _lastFrame = null;
            return list;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var bg = new SolidBrush(EditorTheme.Background);
            g.FillRectangle(bg, rect);
            using var border = new Pen(EditorTheme.Accent, 1.5f);
            g.DrawRectangle(border, rect);

            using var font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 9, FontStyle.Regular);
            using var fg = new SolidBrush(EditorTheme.Text);
            var msg = $"Scroll manually. Frames: {_frames.Count}. Enter = finish, Esc = cancel.";
            g.DrawString(msg, font, fg, 12, 12);
            using var sub = new SolidBrush(EditorTheme.TextDim);
            g.DrawString("Stitches automatically.", font, sub, 12, 32);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sampler.Dispose();
                foreach (var f in _frames) f.Dispose();
                _frames.Clear();
            }
            base.Dispose(disposing);
        }
    }
}

internal static class ManualHeuristics
{
    public static CaptureDirection GuessDirection(List<Bitmap> frames)
    {
        // Compare a left strip vs right strip & top vs bottom of consecutive
        // pairs to see which axis "moved" more on average.
        long hScore = 0;
        long vScore = 0;
        int compares = 0;
        for (int i = 1; i < frames.Count; i++)
        {
            var a = frames[i - 1];
            var b = frames[i];
            if (a.Width != b.Width || a.Height != b.Height) continue;
            int w = a.Width, h = a.Height;
            int sw = Math.Max(8, w / 8);
            int sh = Math.Max(8, h / 8);
            long left = StripSad(a, b, new Rectangle(0, 0, sw, h));
            long right = StripSad(a, b, new Rectangle(w - sw, 0, sw, h));
            long top = StripSad(a, b, new Rectangle(0, 0, w, sh));
            long bottom = StripSad(a, b, new Rectangle(0, h - sh, w, sh));
            hScore += Math.Abs(left - right);
            vScore += Math.Abs(top - bottom);
            compares++;
        }
        if (compares == 0) return CaptureDirection.Vertical;
        return hScore > vScore ? CaptureDirection.Horizontal : CaptureDirection.Vertical;
    }

    public static bool FramesIdentical(Bitmap a, Bitmap b, double minDiffPerChannel)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        int w = a.Width, h = a.Height;
        int bandH = Math.Max(8, Math.Min(40, h / 20));
        int[] yCenters = { h / 4, h / 2, 3 * h / 4 };
        long totalSad = 0;
        long totalPixels = 0;
        foreach (var yc in yCenters)
        {
            int y0 = Math.Max(0, Math.Min(h - bandH, yc - bandH / 2));
            var r = new Rectangle(0, y0, w, bandH);
            totalSad += StripSad(a, b, r);
            totalPixels += (long)r.Width * r.Height;
        }
        if (totalPixels == 0) return false;
        double mean = (double)totalSad / (totalPixels * 3);
        return mean < minDiffPerChannel;
    }

    private static long StripSad(Bitmap a, Bitmap b, Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return 0;
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
