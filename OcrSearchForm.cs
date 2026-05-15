using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ScrollerCapture;

/// <summary>
/// Standalone dialog for OCR and in-capture text search (highlights on the canvas).
/// </summary>
internal sealed class OcrSearchForm : Form
{
    private readonly EditorCanvasControl _canvas;
    private readonly Button _ocrButton;
    private readonly Label _ocrStatus;
    private readonly TextBox _ocrTextBox;
    private readonly TextBox _searchBox;
    private readonly ListBox _searchResults;
    private OcrResult? _ocrResult;

    public OcrSearchForm(Form owner, EditorCanvasControl canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        Owner = owner;
        Text = "OCR / Search";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimumSize = new Size(340, 480);
        ClientSize = new Size(380, 520);
        KeyPreview = true;

        _ocrButton = new Button
        {
            Text = "Run OCR",
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Fill,
        };
        _ocrButton.Click += async (_, _) => await RunOcrAsync();

        _ocrStatus = new Label
        {
            Text = (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && OcrService.IsAvailable)
                ? "Ready. Run OCR to extract text."
                : "OCR not available on this system.",
            ForeColor = EditorTheme.TextDim,
            Height = 36,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _searchBox = new TextBox
        {
            Height = 26,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Search OCR results...",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 0),
        };
        _searchBox.TextChanged += (_, _) => RunSearch();

        _ocrTextBox = new TextBox
        {
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.None,
            AcceptsReturn = true,
            ReadOnly = false,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            Font = SystemFonts.MessageBoxFont,
            Margin = new Padding(0, 10, 0, 6),
        };

        _searchResults = new ListBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Dock = DockStyle.Fill,
        };
        _searchResults.SelectedIndexChanged += (_, _) => FocusSelectedSearchResult();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108f));

        layout.Controls.Add(_ocrButton, 0, 0);
        layout.Controls.Add(_ocrStatus, 0, 1);
        layout.Controls.Add(_searchBox, 0, 2);
        layout.Controls.Add(_ocrTextBox, 0, 3);
        layout.Controls.Add(_searchResults, 0, 4);

        Controls.Add(layout);

        var cancelEsc = new Button
        {
            DialogResult = DialogResult.Cancel,
            Visible = false,
            TabStop = false,
            Size = Size.Empty,
        };
        Controls.Add(cancelEsc);
        CancelButton = cancelEsc;

        FormClosed += (_, _) => _canvas.SetSearchHighlights(Array.Empty<Rectangle>());
        EditorTheme.Apply(this);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async System.Threading.Tasks.Task RunOcrAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) || !OcrService.IsAvailable)
        {
            _ocrStatus.Text = "OCR not available. Install a Windows language pack with OCR.";
            return;
        }

        _ocrButton.Enabled = false;
        _ocrStatus.Text = "Running OCR...";
        _ocrTextBox.Clear();
        _ocrTextBox.ReadOnly = true;
        try
        {
            using var flat = _canvas.FlattenForOutput();
            _ocrResult = await OcrService.RecognizeAsync(flat);
            _ocrTextBox.Text = _ocrResult?.PlainText ?? "";
            int lines = _ocrResult?.Lines.Count ?? 0;
            int chars = _ocrResult?.PlainText.Length ?? 0;
            _ocrStatus.Text = $"OCR done: {lines} lines, {chars} chars.";
            try
            {
                if (_ocrResult != null && !string.IsNullOrEmpty(_ocrResult.PlainText))
                {
                    Clipboard.SetText(_ocrResult.PlainText);
                    _ocrStatus.Text += " Text copied to clipboard.";
                }
            }
            catch { /* ignore */ }

            RunSearch();
        }
        catch (Exception ex)
        {
            _ocrStatus.Text = "OCR failed: " + ex.Message;
        }
        finally
        {
            _ocrTextBox.ReadOnly = false;
            _ocrButton.Enabled = true;
        }
    }

    private void RunSearch()
    {
        _searchResults.BeginUpdate();
        _searchResults.Items.Clear();
        if (_ocrResult == null)
        {
            _canvas.SetSearchHighlights(Array.Empty<Rectangle>());
            _searchResults.EndUpdate();
            return;
        }

        var query = (_searchBox.Text ?? "").Trim();
        var rects = new List<Rectangle>();
        if (query.Length == 0)
        {
            _canvas.SetSearchHighlights(rects);
            _searchResults.EndUpdate();
            return;
        }

        var matches = new List<MatchEntry>();
        foreach (var line in _ocrResult.Lines)
        {
            if (line.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            bool anyWord = false;
            foreach (var w in line.Words)
            {
                if (w.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rects.Add(w.Bounds);
                    anyWord = true;
                }
            }

            if (!anyWord) rects.Add(line.Bounds);
            matches.Add(new MatchEntry
            {
                Text = line.Text,
                Bounds = anyWord ? rects[^1] : line.Bounds,
            });
        }

        foreach (var m in matches) _searchResults.Items.Add(m);
        _searchResults.EndUpdate();
        _canvas.SetSearchHighlights(rects);
    }

    private void FocusSelectedSearchResult()
    {
        if (_searchResults.SelectedItem is MatchEntry m)
            _canvas.ScrollIntoView(m.Bounds);
    }

    private sealed class MatchEntry
    {
        public string Text { get; set; } = "";
        public Rectangle Bounds { get; set; }

        public override string ToString()
        {
            var t = Text.Length > 64 ? Text[..64] + "..." : Text;
            return t;
        }
    }
}
