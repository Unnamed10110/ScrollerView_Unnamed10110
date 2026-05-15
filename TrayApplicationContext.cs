using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScrollerCapture;

internal sealed class TrayApplicationContext : ApplicationContext
{
    // Hotkey IDs (one per slot).
    private const int HotkeyIdRegion = 0xB001;
    private const int HotkeyIdVertical = 0xB002;
    private const int HotkeyIdHorizontal = 0xB003;
    private const int HotkeyIdAuto = 0xB004;

    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly SynchronizationContext _uiContext;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _regionMenuItem;
    private readonly ToolStripMenuItem _verticalMenuItem;
    private readonly ToolStripMenuItem _horizontalMenuItem;
    private readonly ToolStripMenuItem _autoMenuItem;
    private readonly ToolStripMenuItem _browserMenuItem;
    private readonly ToolStripMenuItem _manualMenuItem;
    private readonly ToolStripMenuItem _recentMenu;
    private readonly CaptureHistoryService _history;

    private AppSettings _settings;
    private HotkeySettings _registered = new();
    private int _busy;
    private bool _settingsFormOpen;

    public TrayApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = AppSettings.LoadOrDefault();
        _history = new CaptureHistoryService(_settings.Output.RecentMax);

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;

        _regionMenuItem = new ToolStripMenuItem("Capture region", null,
            (_, _) => TriggerCapture(CaptureMode.Region));
        _verticalMenuItem = new ToolStripMenuItem("Capture vertical", null,
            (_, _) => TriggerCapture(CaptureMode.Vertical));
        _horizontalMenuItem = new ToolStripMenuItem("Capture horizontal", null,
            (_, _) => TriggerCapture(CaptureMode.Horizontal));
        _autoMenuItem = new ToolStripMenuItem("Capture auto", null,
            (_, _) => TriggerCapture(CaptureMode.Auto));
        _browserMenuItem = new ToolStripMenuItem("Full-page browser capture", null,
            (_, _) => TriggerCapture(CaptureMode.BrowserFullPage));
        _manualMenuItem = new ToolStripMenuItem("Manual scroll capture", null,
            (_, _) => TriggerCapture(CaptureMode.Manual));
        _recentMenu = new ToolStripMenuItem("Recent captures");

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_regionMenuItem);
        _menu.Items.Add(_verticalMenuItem);
        _menu.Items.Add(_horizontalMenuItem);
        _menu.Items.Add(_autoMenuItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_browserMenuItem);
        _menu.Items.Add(_manualMenuItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_recentMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        _menu.Items.Add("Open output folder", null, (_, _) => OpenOutputFolder());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitThread());
        EditorTheme.ApplyToMenu(_menu);

        _trayIcon = new NotifyIcon
        {
            Icon = BuildTrayIcon(),
            Text = BuildTrayTooltip(_settings),
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _trayIcon.DoubleClick += (_, _) => TriggerCapture(CaptureMode.Region);

        UpdateMenuLabels();
        RebuildRecentMenu();

        var (ok, failures) = ApplyHotkeys(_settings, persistOnSuccess: false);
        if (!ok)
        {
            _trayIcon.ShowBalloonTip(
                4000,
                "ScrollerCapture",
                "Some hotkeys could not be registered: " + failures + ". Adjust them in Settings...",
                ToolTipIcon.Warning);
        }
        else
        {
            _trayIcon.ShowBalloonTip(
                3000,
                "ScrollerCapture",
                $"Running. Region: {_settings.Hotkeys.Region.Display}.  Vertical: {_settings.Hotkeys.Vertical.Display}.  Horizontal: {_settings.Hotkeys.Horizontal.Display}.",
                ToolTipIcon.Info);
        }
    }

    // ------------------------------------------------------------------
    // Hotkey management
    // ------------------------------------------------------------------

    private (bool ok, string? failures) ApplyHotkeys(AppSettings newSettings, bool persistOnSuccess)
    {
        TryUnregister(HotkeyIdRegion);
        TryUnregister(HotkeyIdVertical);
        TryUnregister(HotkeyIdHorizontal);
        TryUnregister(HotkeyIdAuto);
        _registered = new HotkeySettings
        {
            Region = new HotkeyBinding(),
            Vertical = new HotkeyBinding(),
            Horizontal = new HotkeyBinding(),
            Auto = new HotkeyBinding(),
        };

        string? failures = null;
        void RecordFailure(string label)
        {
            failures = failures == null ? label : failures + ", " + label;
        }

        bool TryOne(int id, HotkeyBinding b, string label, Action<HotkeyBinding> onOk)
        {
            if (!b.IsValid) { onOk(new HotkeyBinding()); return true; }
            if (TryRegister(id, b)) { onOk(b.Clone()); return true; }
            RecordFailure($"{label} ({b.Display})");
            return false;
        }

        bool r = TryOne(HotkeyIdRegion, newSettings.Hotkeys.Region, "Region", b => _registered.Region = b);
        bool v = TryOne(HotkeyIdVertical, newSettings.Hotkeys.Vertical, "Vertical", b => _registered.Vertical = b);
        bool h = TryOne(HotkeyIdHorizontal, newSettings.Hotkeys.Horizontal, "Horizontal", b => _registered.Horizontal = b);
        bool a = TryOne(HotkeyIdAuto, newSettings.Hotkeys.Auto, "Auto", b => _registered.Auto = b);

        _settings = newSettings;
        UpdateMenuLabels();
        _trayIcon.Text = BuildTrayTooltip(_settings);

        bool fullyOk = r && v && h && a;
        if (fullyOk && persistOnSuccess) _settings.TrySave(out _);
        return (fullyOk, failures);
    }

    private bool TryRegister(int id, HotkeyBinding binding)
    {
        if (!binding.IsValid) return false;
        try
        {
            return NativeMethods.RegisterHotKey(
                _hotkeyWindow.Handle,
                id,
                binding.Modifiers | NativeMethods.MOD_NOREPEAT,
                binding.VirtualKey);
        }
        catch
        {
            return false;
        }
    }

    private void TryUnregister(int id)
    {
        try { NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, id); }
        catch { /* best effort */ }
    }

    private void OpenSettings()
    {
        if (_settingsFormOpen) return;
        _settingsFormOpen = true;
        _uiContext.Post(_ =>
        {
            try
            {
                using var form = new SettingsForm(_settings, candidate =>
                {
                    var (ok, failures) = ApplyHotkeys(candidate, persistOnSuccess: true);
                    if (!ok)
                    {
                        var rollback = _settings.Clone();
                        rollback.Hotkeys = new HotkeySettings
                        {
                            Region = _registered.Region.IsValid ? _registered.Region : HotkeySettings.Default().Region,
                            Vertical = _registered.Vertical.IsValid ? _registered.Vertical : HotkeySettings.Default().Vertical,
                            Horizontal = _registered.Horizontal.IsValid ? _registered.Horizontal : HotkeySettings.Default().Horizontal,
                            Auto = _registered.Auto, // unassigned ok
                        };
                        ApplyHotkeys(rollback, persistOnSuccess: false);
                        return (false, "Could not register: " + failures + ". Another app may already use it.");
                    }
                    return (true, null);
                });
                form.ShowDialog();
            }
            finally
            {
                _settingsFormOpen = false;
            }
        }, null);
    }

    private void UpdateMenuLabels()
    {
        _regionMenuItem.Text = $"Capture region ({_settings.Hotkeys.Region.Display})";
        _verticalMenuItem.Text = $"Capture vertical ({_settings.Hotkeys.Vertical.Display})";
        _horizontalMenuItem.Text = $"Capture horizontal ({_settings.Hotkeys.Horizontal.Display})";
        _autoMenuItem.Text = _settings.Hotkeys.Auto.IsValid
            ? $"Capture auto ({_settings.Hotkeys.Auto.Display})"
            : "Capture auto (unassigned)";
    }

    private static string BuildTrayTooltip(AppSettings s) =>
        "ScrollerCapture - " +
        $"{s.Hotkeys.Region.Display}=region, " +
        $"{s.Hotkeys.Vertical.Display}=vertical, " +
        $"{s.Hotkeys.Horizontal.Display}=horizontal";

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        var mode = e.HotkeyId switch
        {
            HotkeyIdRegion => CaptureMode.Region,
            HotkeyIdVertical => CaptureMode.Vertical,
            HotkeyIdHorizontal => CaptureMode.Horizontal,
            HotkeyIdAuto => CaptureMode.Auto,
            _ => CaptureMode.Region,
        };
        TriggerCapture(mode);
    }

    // ------------------------------------------------------------------
    // Capture flow
    // ------------------------------------------------------------------

    private void TriggerCapture(CaptureMode mode)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await RunCaptureAsync(mode).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ShowError("ScrollerCapture failed", ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private async Task RunCaptureAsync(CaptureMode mode)
    {
        var fg = ForegroundInfo.Capture();

        var router = new CaptureRouter(_uiContext, _settings);
        var result = await router.RunAsync(new CaptureRequest { Mode = mode }).ConfigureAwait(false);

        result.SourceApp = fg.ProcessName;
        result.SourceTitle = fg.Title;

        if (!result.Success || result.Image == null)
        {
            if (!string.IsNullOrEmpty(result.Message)
                && !result.Message!.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("Capture aborted", result.Message);
            }
            return;
        }

        var editorOutcome = await ShowEditorOnUiThreadAsync(result.Image).ConfigureAwait(false);
        result.Image.Dispose();

        if (editorOutcome == null) return;

        if (editorOutcome.ExternalSavePath != null)
        {
            RecordHistory(editorOutcome.ExternalSavePath, result);
            Bitmap? preview = TryCreatePreviewThumbnailFromFile(editorOutcome.ExternalSavePath);
            ShowInfo("Saved and copied",
                $"Saved {Path.GetFileName(editorOutcome.ExternalSavePath)} ({result.PartCount} parts). Copied to clipboard.",
                editorOutcome.ExternalSavePath,
                preview);
            return;
        }

        if (editorOutcome.FinalImage != null)
        {
            int w = editorOutcome.FinalImage.Width;
            int h = editorOutcome.FinalImage.Height;
            string outputPath;
            Bitmap? preview;
            try
            {
                outputPath = SaveImage(editorOutcome.FinalImage, result);
                preview = CreatePreviewThumbnail(editorOutcome.FinalImage);
            }
            finally
            {
                editorOutcome.FinalImage.Dispose();
            }
            RecordHistory(outputPath, result, w, h);
            ShowInfo("Saved and copied",
                $"Saved {Path.GetFileName(outputPath)} ({result.PartCount} parts). Copied to clipboard.",
                outputPath,
                preview);
        }
    }

    private void RecordHistory(string path, CaptureResult result, int width = 0, int height = 0)
    {
        if (width <= 0 || height <= 0)
            TryGetImageFileDimensions(path, out width, out height);

        var entry = new CaptureHistoryEntry
        {
            Path = path,
            Timestamp = DateTime.Now,
            Mode = result.Mode.ToString(),
            SourceApp = result.SourceApp,
            SourceTitle = result.SourceTitle,
            Width = width,
            Height = height,
        };
        _history.Add(entry);
        _uiContext.Post(_ => RebuildRecentMenu(), null);
    }

    private static void TryGetImageFileDimensions(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            if (!File.Exists(path)) return;
            using var img = Image.FromFile(path);
            width = img.Width;
            height = img.Height;
        }
        catch
        {
            /* ignore */
        }
    }

    private void RebuildRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        var entries = _history.Snapshot();
        if (entries.Count == 0)
        {
            var empty = new ToolStripMenuItem("(none yet)") { Enabled = false };
            _recentMenu.DropDownItems.Add(empty);
            EditorTheme.ApplyToMenu(_menu);
            return;
        }
        foreach (var entry in entries)
        {
            var item = new ToolStripMenuItem(entry.Display);
            var path = entry.Path;
            var open = new ToolStripMenuItem("Open", null, (_, _) => SafeStart(path));
            var copy = new ToolStripMenuItem("Copy to clipboard", null, (_, _) => CopyImageToClipboard(path));
            var folder = new ToolStripMenuItem("Open containing folder", null, (_, _) =>
            {
                try
                {
                    if (File.Exists(path))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{path}\"",
                            UseShellExecute = true,
                        });
                    }
                }
                catch { /* ignore */ }
            });
            item.DropDownItems.Add(open);
            item.DropDownItems.Add(copy);
            item.DropDownItems.Add(folder);
            _recentMenu.DropDownItems.Add(item);
        }
        EditorTheme.ApplyToMenu(_menu);
    }

    private static void CopyImageToClipboard(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var bmp = new Bitmap(path);
            using var copy = new Bitmap(bmp);
            Clipboard.SetImage(copy);
        }
        catch { /* ignore */ }
    }

    private static void SafeStart(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch { /* ignore */ }
    }

    private sealed class EditorOutcome
    {
        public Bitmap? FinalImage;
        public string? ExternalSavePath;
    }

    private Task<EditorOutcome?> ShowEditorOnUiThreadAsync(Bitmap capturedImage)
    {
        var tcs = new TaskCompletionSource<EditorOutcome?>();
        _uiContext.Post(_ =>
        {
            try
            {
                using var editor = new CaptureEditorForm(new Bitmap(capturedImage), _settings);
                var dr = editor.ShowDialog();
                if (dr != DialogResult.OK)
                {
                    tcs.SetResult(null);
                    return;
                }
                tcs.SetResult(new EditorOutcome
                {
                    FinalImage = editor.ResultImage,
                    ExternalSavePath = editor.ExternalSavePath,
                });
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    private string SaveImage(Bitmap image, CaptureResult result)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "ScrollerCapture");
        Directory.CreateDirectory(folder);
        var name = FilenameTemplateService.Build(
            _settings.Output.FilenameTemplate,
            result,
            image.Width,
            image.Height);
        var file = Path.Combine(folder, name);
        image.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        return file;
    }

    private void OpenOutputFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "ScrollerCapture");
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    private void ShowError(string title, string message)
    {
        _uiContext.Post(_ =>
        {
            _trayIcon.ShowBalloonTip(5000, title, message, ToolTipIcon.Error);
        }, null);
    }

    private void ShowInfo(string title, string message, string? path, Bitmap? previewThumbnail = null)
    {
        _uiContext.Post(_ =>
        {
            if (previewThumbnail != null)
            {
                try
                {
                    var toast = new CapturePreviewToastForm(title, message, path, previewThumbnail);
                    toast.Show();
                }
                catch
                {
                    previewThumbnail.Dispose();
                    ShowBalloonInfo(title, message, path);
                }
            }
            else
            {
                ShowBalloonInfo(title, message, path);
            }
        }, null);
    }

    private void ShowBalloonInfo(string title, string message, string? path)
    {
        _trayIcon.BalloonTipClicked -= OnBalloonClicked;
        if (path != null)
        {
            _trayIcon.Tag = path;
            _trayIcon.BalloonTipClicked += OnBalloonClicked;
        }
        else
        {
            _trayIcon.Tag = null;
        }
        _trayIcon.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
    }

    /// <summary>Downscale for preview toast (max edge ~240 px).</summary>
    private static Bitmap? CreatePreviewThumbnail(Bitmap source)
    {
        if (source.Width <= 0 || source.Height <= 0) return null;
        const int maxEdge = 240;
        double scale = Math.Min(maxEdge / (double)source.Width, maxEdge / (double)source.Height);
        if (scale > 1.0) scale = 1.0;
        int tw = Math.Max(1, (int)Math.Round(source.Width * scale));
        int th = Math.Max(1, (int)Math.Round(source.Height * scale));
        var bmp = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, tw, th));
        }
        return bmp;
    }

    private static Bitmap? TryCreatePreviewThumbnailFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var img = Image.FromFile(path);
            using var full = new Bitmap(img);
            return CreatePreviewThumbnail(full);
        }
        catch
        {
            return null;
        }
    }

    private void OnBalloonClicked(object? sender, EventArgs e)
    {
        if (_trayIcon.Tag is string path && File.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
    }

    private static Icon BuildTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(32, 110, 200));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 2.5f);
            g.DrawLine(pen, 7, 16, 25, 16);
            g.DrawLine(pen, 7, 16, 11, 12);
            g.DrawLine(pen, 7, 16, 11, 20);
            g.DrawLine(pen, 25, 16, 21, 12);
            g.DrawLine(pen, 25, 16, 21, 20);
        }
        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TryUnregister(HotkeyIdRegion);
            TryUnregister(HotkeyIdVertical);
            TryUnregister(HotkeyIdHorizontal);
            TryUnregister(HotkeyIdAuto);
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _hotkeyWindow.DestroyHandle();
        }
        base.Dispose(disposing);
    }

    private sealed class HotkeyEventArgs : EventArgs
    {
        public int HotkeyId { get; }
        public HotkeyEventArgs(int id) { HotkeyId = id; }
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                HotkeyPressed?.Invoke(this, new HotkeyEventArgs(m.WParam.ToInt32()));
            }
            base.WndProc(ref m);
        }
    }
}
