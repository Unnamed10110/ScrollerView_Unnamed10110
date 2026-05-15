using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ScrollerCapture;

/// <summary>
/// Short-lived toast near the taskbar with a thumbnail preview. Tray balloon
/// APIs cannot show images; this mimics a modern notification with a picture.
/// </summary>
internal sealed class CapturePreviewToastForm : Form
{
    private readonly string? _openPath;
    private readonly System.Windows.Forms.Timer _autoClose;

    public CapturePreviewToastForm(string title, string subtitle, string? openPath, Bitmap thumbnail)
    {
        _openPath = openPath;
        ArgumentNullException.ThrowIfNull(thumbnail);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = EditorTheme.Background;
        ForeColor = EditorTheme.Text;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;

        const int pad = 10;
        int titleH = 26;
        int subH = 40;
        int picW = thumbnail.Width;
        int picH = thumbnail.Height;
        int clientW = Math.Max(240, picW + pad * 2);
        int clientH = pad + titleH + subH + picH + pad + 4;

        ClientSize = new Size(clientW, clientH);

        var wa = Screen.GetWorkingArea(Cursor.Position);
        Location = new Point(
            wa.Right - Width - 12,
            wa.Bottom - Height - 12);

        var titleLbl = new Label
        {
            Text = title,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 11, FontStyle.Bold),
            ForeColor = EditorTheme.Accent,
            Bounds = new Rectangle(pad, pad, clientW - pad * 2, titleH),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var subLbl = new Label
        {
            Text = subtitle,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 9, FontStyle.Regular),
            ForeColor = EditorTheme.TextDim,
            Bounds = new Rectangle(pad, pad + titleH, clientW - pad * 2, subH),
        };

        var pic = new PictureBox
        {
            Image = thumbnail,
            SizeMode = PictureBoxSizeMode.Zoom,
            Bounds = new Rectangle(pad, pad + titleH + subH, clientW - pad * 2, picH),
            BackColor = EditorTheme.SurfaceAlt,
            BorderStyle = BorderStyle.FixedSingle,
        };

        Controls.Add(titleLbl);
        Controls.Add(subLbl);
        Controls.Add(pic);

        Paint += OnChromePaint;
        Click += (_, _) => TryOpen();
        titleLbl.Click += (_, _) => TryOpen();
        subLbl.Click += (_, _) => TryOpen();
        pic.Click += (_, _) => TryOpen();

        _autoClose = new System.Windows.Forms.Timer { Interval = 5500 };
        _autoClose.Tick += (_, _) =>
        {
            _autoClose.Stop();
            Close();
        };

        FormClosed += (_, _) =>
        {
            _autoClose.Dispose();
            pic.Image?.Dispose();
            pic.Image = null;
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _autoClose.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE — do not steal focus from editor
            return cp;
        }
    }

    private void OnChromePaint(object? sender, PaintEventArgs e)
    {
        using var border = new Pen(EditorTheme.BorderAccent, 2f) { LineJoin = LineJoin.Miter };
        e.Graphics.DrawRectangle(border, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
    }

    private void TryOpen()
    {
        if (string.IsNullOrEmpty(_openPath) || !File.Exists(_openPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _openPath,
                UseShellExecute = true,
            });
        }
        catch { /* ignore */ }
        Close();
    }
}
