using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ScrollerCapture;

internal sealed class CaptureEditorForm : Form
{
    private readonly EditorCanvasControl _canvas;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _statusTool;
    private readonly ToolStripStatusLabel _statusZoom;
    private readonly ToolStripStatusLabel _statusSize;
    private readonly ToolStripStatusLabel _statusHint;
    private readonly ToolStrip _toolbar;
    private readonly MenuStrip _mainMenu;

    private readonly AppSettings? _settings;

    public Bitmap? ResultImage { get; private set; }
    public string? ExternalSavePath { get; private set; }

    public CaptureEditorForm(Bitmap capturedImage, AppSettings? settings = null)
    {
        if (capturedImage == null) throw new ArgumentNullException(nameof(capturedImage));
        _settings = settings;

        Text = AppBranding.DisplayName + " — Editor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 480);
        KeyPreview = true;
        Icon = TryGetAppIcon();

        // Normal (restored) size if the user leaves full screen; starts maximized.
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Size = new Size(Math.Min(1280, work.Width - 40), Math.Min(900, work.Height - 40));
        WindowState = FormWindowState.Maximized;

        _toolbar = BuildToolbar();

        _mainMenu = BuildMainMenu();

        _canvas = new EditorCanvasControl(capturedImage)
        {
            Dock = DockStyle.Fill,
        };
        _canvas.StateChanged += (_, _) => UpdateStatus();
        _canvas.ViewportChanged += (_, _) => UpdateStatus();
        _canvas.SpeechBalloonRequested += OnSpeechBalloonRequested;
        _canvas.SpeechBalloonEditRequested += OnSpeechBalloonEditRequested;
        _canvas.TextRequested += OnTextRequested;

        _status = new StatusStrip { Dock = DockStyle.Bottom };
        _statusTool = new ToolStripStatusLabel("Tool: None") { AutoSize = true };
        _statusZoom = new ToolStripStatusLabel("Zoom: 100%") { AutoSize = true };
        _statusSize = new ToolStripStatusLabel($"Size: {capturedImage.Width} x {capturedImage.Height}") { AutoSize = true };
        _statusHint = new ToolStripStatusLabel(
            "Enter=Save+Copy  …  Ctrl+Shift+O=OCR  V=Select  R=Rect  B=Blur  A=Arrow  H=Highlight  S=Balloon  T=Text  N=Step  M=Magnify  O=Spot  X=Crop  C=Strip  Del=Delete  Ctrl+Wheel=Zoom")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = EditorTheme.TextDim,
        };
        _status.Items.AddRange(new ToolStripItem[] { _statusTool, _statusZoom, _statusSize, _statusHint });

        // Z-order: add menu strip last so both dock Top stacks with menu at the
        // very top; canvas Fill; status Bottom.
        Controls.Add(_canvas);
        Controls.Add(_status);
        Controls.Add(_toolbar);
        Controls.Add(_mainMenu);
        MainMenuStrip = _mainMenu;

        EditorTheme.Apply(this);

        Shown += (_, _) =>
        {
            // Layout runs before maximized bounds are final; center after show.
            BeginInvoke(() =>
            {
                _canvas.InitializeViewportOnShow();
                _canvas.Focus();
                UpdateStatus();
            });
        };
        FormClosing += OnFormClosing;
    }

    private ToolStrip BuildToolbar()
    {
        var ts = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            ImageScalingSize = new Size(20, 20),
        };
        ts.Items.Add(MakeToolButton("Select (V)", EditorTool.Select));
        ts.Items.Add(MakeToolButton("Cutout (X)", EditorTool.Cutout));
        ts.Items.Add(MakeToolButton("Strip Cut (C)", EditorTool.StripCutout));
        ts.Items.Add(MakeToolButton("Rectangle (R)", EditorTool.Rectangle));
        ts.Items.Add(MakeToolButton("Blur (B)", EditorTool.Blur));
        ts.Items.Add(MakeToolButton("Arrow (A)", EditorTool.Arrow));
        ts.Items.Add(MakeToolButton("Highlight (H)", EditorTool.Highlight));
        ts.Items.Add(MakeToolButton("Balloon (S)", EditorTool.SpeechBalloon));
        ts.Items.Add(MakeToolButton("Text (T)", EditorTool.Text));
        ts.Items.Add(MakeToolButton("Step (N)", EditorTool.StepMarker));
        ts.Items.Add(MakeToolButton("Magnifier (M)", EditorTool.Magnifier));
        ts.Items.Add(MakeToolButton("Spotlight (O)", EditorTool.Spotlight));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripButton("Undo (Ctrl+Z)", null, (_, _) => _canvas.Undo()));
        ts.Items.Add(new ToolStripButton("Redo (Ctrl+Y)", null, (_, _) => _canvas.Redo()));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripButton("Fit (F)", null, (_, _) => _canvas.FitToWindow()));
        ts.Items.Add(new ToolStripButton("100% (1)", null, (_, _) => _canvas.ZoomActualSize()));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripButton("Save+Copy (Enter)", null, (_, _) => AcceptAndClose()));
        ts.Items.Add(new ToolStripButton("Save As (Ctrl+S)", null, (_, _) => SaveAs()));
        ts.Items.Add(new ToolStripButton("Copy (Ctrl+C)", null, (_, _) => CopyToClipboard()));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripButton("OCR / Search…", null, (_, _) => ShowOcrSearch()) { ToolTipText = "Extract text and search in image (Ctrl+Shift+O)" });
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripButton("Export PDF...", null, (_, _) => ExportPdf()));
        ts.Items.Add(BuildShareDropdown());
        return ts;
    }

    private MenuStrip BuildMainMenu()
    {
        var menu = new MenuStrip();
        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add(new ToolStripMenuItem("&OCR / Search…", null, (_, _) => ShowOcrSearch())
        {
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.O,
            ShowShortcutKeys = true,
        });
        menu.Items.Add(tools);
        return menu;
    }

    private ToolStripDropDownButton BuildShareDropdown()
    {
        var dd = new ToolStripDropDownButton("Share");
        foreach (var t in ShareTargetService.BuiltIns())
        {
            var captured = t;
            dd.DropDownItems.Add(new ToolStripMenuItem(t.Name, null, (_, _) => ShareWith(captured)));
        }
        if (_settings != null && _settings.Output.ShareTargets.Count > 0)
        {
            dd.DropDownItems.Add(new ToolStripSeparator());
            foreach (var t in _settings.Output.ShareTargets)
            {
                if (!t.Enabled) continue;
                var captured = t;
                dd.DropDownItems.Add(new ToolStripMenuItem(t.Name, null, (_, _) => ShareWith(captured)));
            }
        }
        return dd;
    }

    private void ShareWith(ShareTarget target)
    {
        // Save to a temp file (PNG) and pass that path to the share command.
        try
        {
            using var flat = _canvas.FlattenForOutput();
            string folder = Path.Combine(Path.GetTempPath(), AppBranding.ShortName + "-Share");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"share-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            flat.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            var ctx = new ShareContext
            {
                Path = path,
                Width = flat.Width,
                Height = flat.Height,
            };
            ShareTargetService.Run(target, ctx);
            FlashStatus($"Shared via {target.Name}");
        }
        catch (Exception ex)
        {
            FlashStatus("Share failed: " + ex.Message);
        }
    }

    private void ExportPdf()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PDF document (*.pdf)|*.pdf",
            FileName = $"scroll-capture-{DateTime.Now:yyyyMMdd-HHmmss}.pdf",
            InitialDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                AppBranding.DisplayName),
            OverwritePrompt = true,
            AddExtension = true,
        };
        try { Directory.CreateDirectory(dlg.InitialDirectory); } catch { /* ignore */ }
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        using var flat = _canvas.FlattenForOutput();
        try
        {
            // Choose page size based on aspect ratio: very tall captures use FitWidth.
            var pageSize = flat.Height > flat.Width * 1.5 ? PdfPageSize.FitWidth : PdfPageSize.A4;
            PdfExportService.Export(flat, dlg.FileName, pageSize);
            FlashStatus("Exported PDF: " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "PDF export failed:\n\n" + ex.Message,
                AppBranding.DisplayName + " — Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowOcrSearch()
    {
        using var dlg = new OcrSearchForm(this, _canvas);
        dlg.ShowDialog(this);
    }

    private ToolStripButton MakeToolButton(string text, EditorTool tool)
    {
        var btn = new ToolStripButton(text)
        {
            CheckOnClick = false,
            DisplayStyle = ToolStripItemDisplayStyle.Text,
        };
        btn.Click += (_, _) => SetActiveTool(tool);
        btn.Tag = tool;
        return btn;
    }

    private void SetActiveTool(EditorTool tool)
    {
        _canvas.SetTool(tool);
        foreach (ToolStripItem item in _toolbar.Items)
        {
            if (item is ToolStripButton tb && tb.Tag is EditorTool t)
            {
                tb.Checked = t == tool;
            }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter)
        {
            AcceptAndClose();
            return true;
        }
        if (keyData == Keys.Escape)
        {
            if (_canvas.HasSelection)
            {
                _canvas.ClearSelection();
                return true;
            }
            CancelAndClose();
            return true;
        }
        if (keyData == Keys.Delete)
        {
            if (_canvas.HasSelection)
            {
                _canvas.DeleteSelected();
                return true;
            }
        }

        if ((keyData & Keys.Alt) == Keys.Alt && _canvas.HasSelection)
        {
            var k = keyData & ~Keys.Alt;
            switch (k)
            {
                case Keys.Left: _canvas.SetSelectedTailDirection(TailDirection.Left); return true;
                case Keys.Right: _canvas.SetSelectedTailDirection(TailDirection.Right); return true;
                case Keys.Up: _canvas.SetSelectedTailDirection(TailDirection.Up); return true;
                case Keys.Down: _canvas.SetSelectedTailDirection(TailDirection.Down); return true;
            }
        }

        if ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.Shift) == Keys.Shift)
        {
            if ((keyData & Keys.KeyCode) == Keys.O)
            {
                ShowOcrSearch();
                return true;
            }
        }
        if ((keyData & Keys.Control) == Keys.Control)
        {
            var kd = keyData & ~Keys.Control;
            switch (kd)
            {
                case Keys.S: SaveAs(); return true;
                case Keys.C: CopyToClipboard(); return true;
                case Keys.Z: _canvas.Undo(); return true;
                case Keys.Y: _canvas.Redo(); return true;
            }
        }
        switch (keyData)
        {
            case Keys.V: SetActiveTool(EditorTool.Select); return true;
            case Keys.X: SetActiveTool(EditorTool.Cutout); return true;
            case Keys.C: SetActiveTool(EditorTool.StripCutout); return true;
            case Keys.R: SetActiveTool(EditorTool.Rectangle); return true;
            case Keys.B: SetActiveTool(EditorTool.Blur); return true;
            case Keys.A: SetActiveTool(EditorTool.Arrow); return true;
            case Keys.H: SetActiveTool(EditorTool.Highlight); return true;
            case Keys.S: SetActiveTool(EditorTool.SpeechBalloon); return true;
            case Keys.T: SetActiveTool(EditorTool.Text); return true;
            case Keys.N: SetActiveTool(EditorTool.StepMarker); return true;
            case Keys.M: SetActiveTool(EditorTool.Magnifier); return true;
            case Keys.O: SetActiveTool(EditorTool.Spotlight); return true;
            case Keys.F: _canvas.FitToWindow(); return true;
            case Keys.D1:
            case Keys.NumPad1: _canvas.ZoomActualSize(); return true;
            case Keys.Space: _canvas.HandleSpaceKey(true); return true;
            case Keys.OemCloseBrackets: _canvas.BringForward(); return true;
            case Keys.OemOpenBrackets: _canvas.SendBackward(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnTextRequested(object? sender, TextRequestEventArgs e)
    {
        using var input = new TextInputForm("Text", "Text:", initial: e.Text ?? string.Empty);
        var dr = input.ShowDialog(this);
        if (dr != DialogResult.OK)
        {
            e.Cancelled = true;
            return;
        }
        e.Text = input.Value;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space) _canvas.HandleSpaceKey(false);
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    private void AcceptAndClose()
    {
        var flat = _canvas.FlattenForOutput();
        // Copy to clipboard automatically. Use a clone so the clipboard owns
        // a stable bitmap regardless of when the caller disposes ResultImage.
        try
        {
            var clipboardCopy = new Bitmap(flat);
            Clipboard.SetImage(clipboardCopy);
        }
        catch
        {
            // ignore clipboard failures - the save will still succeed.
        }
        ResultImage = flat;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelAndClose()
    {
        if (_canvas.IsDirty)
        {
            var ok = MessageBox.Show(this,
                "Discard edits and close without saving?",
                AppBranding.DisplayName + " — Editor",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (ok != DialogResult.OK) return;
        }
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void SaveAs()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
            FileName = $"scroll-capture-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            InitialDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                AppBranding.DisplayName),
            OverwritePrompt = true,
            AddExtension = true,
        };
        try { Directory.CreateDirectory(dlg.InitialDirectory); } catch { /* ignore */ }
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        using var flat = _canvas.FlattenForOutput();
        try
        {
            var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            var fmt = ext switch
            {
                ".jpg" or ".jpeg" => System.Drawing.Imaging.ImageFormat.Jpeg,
                ".bmp" => System.Drawing.Imaging.ImageFormat.Bmp,
                _ => System.Drawing.Imaging.ImageFormat.Png,
            };
            flat.Save(dlg.FileName, fmt);
            try
            {
                Clipboard.SetImage(new Bitmap(flat));
            }
            catch { /* ignore */ }
            ExternalSavePath = dlg.FileName;
            ResultImage = null;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to save:\n\n" + ex.Message,
                AppBranding.DisplayName + " — Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopyToClipboard()
    {
        try
        {
            using var flat = _canvas.FlattenForOutput();
            var clone = new Bitmap(flat);
            Clipboard.SetImage(clone);
            FlashStatus("Copied to clipboard");
        }
        catch (Exception ex)
        {
            FlashStatus("Clipboard failed: " + ex.Message);
        }
    }

    private void OnSpeechBalloonRequested(object? sender, SpeechBalloonRequestEventArgs e)
    {
        using var input = new TextInputForm("Speech balloon", "Text:", initial: string.Empty);
        var dr = input.ShowDialog(this);
        if (dr != DialogResult.OK)
        {
            e.Cancelled = true;
            return;
        }
        e.Text = input.Value;
    }

    private void OnSpeechBalloonEditRequested(object? sender, SpeechBalloonEditEventArgs e)
    {
        using var input = new TextInputForm("Edit speech balloon", "Text:", initial: e.Text ?? string.Empty);
        var dr = input.ShowDialog(this);
        if (dr != DialogResult.OK)
        {
            e.Cancelled = true;
            return;
        }
        e.Text = input.Value;
    }

    private void UpdateStatus()
    {
        _statusTool.Text = $"Tool: {_canvas.ActiveTool}";
        _statusZoom.Text = $"Zoom: {Math.Round(_canvas.Zoom * 100)}%";
        _statusSize.Text = $"Size: {_canvas.ImageWidth} x {_canvas.ImageHeight}";
        Text = _canvas.IsDirty
            ? AppBranding.DisplayName + " — Editor *"
            : AppBranding.DisplayName + " — Editor";
    }

    private void FlashStatus(string message)
    {
        var previous = _statusHint.Text;
        _statusHint.Text = message;
        var timer = new System.Windows.Forms.Timer { Interval = 1800 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (!IsDisposed) _statusHint.Text = previous;
        };
        timer.Start();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.None)
        {
            if (_canvas.IsDirty)
            {
                var ok = MessageBox.Show(this,
                    "Discard edits and close without saving?",
                    AppBranding.DisplayName + " — Editor",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (ok != DialogResult.OK)
                {
                    e.Cancel = true;
                    return;
                }
            }
            DialogResult = DialogResult.Cancel;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _canvas.DisposeOwnedBitmaps();
        }
        base.Dispose(disposing);
    }

    private static Icon? TryGetAppIcon() => AppIconFactory.GetApplicationIcon();
}

internal sealed class TextInputForm : Form
{
    private readonly TextBox _text;
    public string Value => _text.Text;

    public TextInputForm(string title, string label, string initial = "")
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 150);
        KeyPreview = true;

        var lbl = new Label
        {
            Text = label,
            Location = new Point(12, 12),
            AutoSize = true,
        };
        _text = new TextBox
        {
            Location = new Point(12, 36),
            Size = new Size(356, 60),
            Multiline = true,
            AcceptsReturn = false,
            Text = initial,
        };
        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(212, 110),
            Size = new Size(75, 28),
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(293, 110),
            Size = new Size(75, 28),
        };

        Controls.AddRange(new Control[] { lbl, _text, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
        EditorTheme.Apply(this);
        Shown += (_, _) =>
        {
            _text.SelectAll();
            _text.Focus();
        };
    }
}
