using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScrollerCapture;

/// <summary>
/// Small AMOLED modal shown when auto-capture finds both axes scrollable
/// (or when the user must pick a fallback). Returns the chosen mode via
/// <see cref="ChosenMode"/> when DialogResult is OK.
/// </summary>
internal sealed class CaptureDirectionChooserForm : Form
{
    public CaptureMode? ChosenMode { get; private set; }

    public CaptureDirectionChooserForm(bool hasHorizontal, bool hasVertical, bool offerRegion, bool offerManual, string? subtitle)
    {
        Text = "Choose capture";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size(360, 200);

        var title = new Label
        {
            Text = "How should we capture this region?",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(16, 12, 320, 24),
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10, FontStyle.Bold),
        };
        Controls.Add(title);

        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = new Label
            {
                Text = subtitle,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(16, 36, 320, 36),
                ForeColor = EditorTheme.TextDim,
            };
            Controls.Add(sub);
        }

        int y = string.IsNullOrEmpty(subtitle) ? 48 : 80;
        int btnW = 320, btnH = 28;

        if (hasHorizontal)
        {
            AddChoice("Horizontal scroll capture (Shift+Alt+D)", CaptureMode.Horizontal, ref y, btnW, btnH);
        }
        if (hasVertical)
        {
            AddChoice("Vertical scroll capture (Shift+Alt+S)", CaptureMode.Vertical, ref y, btnW, btnH);
        }
        if (offerRegion)
        {
            AddChoice("Region only (no scrolling)", CaptureMode.Region, ref y, btnW, btnH);
        }
        if (offerManual)
        {
            AddChoice("Manual scroll fallback", CaptureMode.Manual, ref y, btnW, btnH);
        }

        var cancel = new Button
        {
            Text = "Cancel (Esc)",
            Bounds = new Rectangle(16, y + 4, btnW, btnH),
            DialogResult = DialogResult.Cancel,
        };
        Controls.Add(cancel);
        CancelButton = cancel;

        ClientSize = new Size(360, y + btnH + 16);
        EditorTheme.Apply(this);
    }

    private void AddChoice(string text, CaptureMode mode, ref int y, int btnW, int btnH)
    {
        var b = new Button
        {
            Text = text,
            Bounds = new Rectangle(16, y, btnW, btnH),
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
        };
        b.Click += (_, _) =>
        {
            ChosenMode = mode;
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(b);
        y += btnH + 6;
    }
}
