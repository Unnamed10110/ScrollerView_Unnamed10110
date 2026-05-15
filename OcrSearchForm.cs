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
        MinimumSize = new Size(340, 360);
        ClientSize = new Size(380, 420);
        KeyPreview = true;

        _ocrButton = new Button
        {
            Text = "Run OCR",
            Dock = DockStyle.Top,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
        };
        _ocrButton.Click += async (_, _) => await RunOcrAsync();

        _ocrStatus = new Label
        {
            Text = (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && OcrService.IsAvailable)
                ? "Ready."
                : "OCR not available on this system.",
            ForeColor = EditorTheme.TextDim,
            Dock = DockStyle.Top,
            Height = 36,
            AutoSize = false,
        };

        _searchBox = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 26,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Search OCR results...",
        };
        _searchBox.TextChanged += (_, _) => RunSearch();

        _searchResults = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
        };
        _searchResults.SelectedIndexChanged += (_, _) => FocusSelectedSearchResult();

        Controls.Add(_searchResults);
        Controls.Add(_searchBox);
        Controls.Add(_ocrStatus);
        Controls.Add(_ocrButton);

        FormClosed += (_, _) => _canvas.SetSearchHighlights(Array.Empty<Rectangle>());
        EditorTheme.Apply(this);
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
        try
        {
            using var flat = _canvas.FlattenForOutput();
            _ocrResult = await OcrService.RecognizeAsync(flat);
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
