using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScrollerCapture;

/// <summary>
/// Chrome / Edge full-page capture via the DevTools Protocol. Tries to find
/// an existing remote-debugging endpoint, otherwise offers to launch a
/// dedicated browser instance with debugging enabled.
/// </summary>
internal static class BrowserDevTools
{
    public static async Task<CaptureResult> CaptureFullPageAsync(BrowserOptions options)
    {
        int port = await BrowserDiscovery.FindEndpointAsync(options.Port).ConfigureAwait(false);
        if (port == 0)
        {
            var launched = await PromptAndLaunchDedicatedAsync(options).ConfigureAwait(false);
            if (launched == 0)
            {
                return new CaptureResult
                {
                    Success = false,
                    Mode = CaptureMode.BrowserFullPage,
                    Message = "No DevTools endpoint found. Cancelled by user or failed to launch.",
                };
            }
            port = launched;
        }

        var tabs = await BrowserDiscovery.ListTabsAsync(port).ConfigureAwait(false);
        if (tabs.Count == 0)
        {
            return new CaptureResult
            {
                Success = false,
                Mode = CaptureMode.BrowserFullPage,
                Message = $"DevTools found on port {port} but no pages are open.",
            };
        }

        BrowserTab? chosen = tabs.Count == 1 ? tabs[0] : await PickTabAsync(tabs).ConfigureAwait(false);
        if (chosen == null)
        {
            return new CaptureResult
            {
                Success = false,
                Mode = CaptureMode.BrowserFullPage,
                Message = "Tab selection cancelled.",
            };
        }

        try
        {
            using var client = new DevToolsClient();
            await client.ConnectAsync(chosen.WebSocketDebuggerUrl).ConfigureAwait(false);
            await client.SendAsync("Page.enable").ConfigureAwait(false);

            // Send Page.captureScreenshot with captureBeyondViewport so the
            // full scrollable page is rendered to a single PNG (Chromium >= 89).
            var resp = await client.SendAsync("Page.captureScreenshot", new
            {
                format = "png",
                captureBeyondViewport = true,
                fromSurface = true,
            }).ConfigureAwait(false);

            if (!resp.TryGetProperty("data", out var dataEl))
            {
                return new CaptureResult
                {
                    Success = false,
                    Mode = CaptureMode.BrowserFullPage,
                    Message = "DevTools did not return image data.",
                };
            }
            var base64 = dataEl.GetString() ?? "";
            var bytes = Convert.FromBase64String(base64);
            using var ms = new MemoryStream(bytes);
            using var loaded = new Bitmap(ms);
            var bmp = new Bitmap(loaded);
            return new CaptureResult
            {
                Success = true,
                Image = bmp,
                Mode = CaptureMode.BrowserFullPage,
                PartCount = 1,
                SourceApp = "browser",
                SourceTitle = chosen.Title,
                Message = $"Captured full page: {chosen.Title}",
            };
        }
        catch (Exception ex)
        {
            return new CaptureResult
            {
                Success = false,
                Mode = CaptureMode.BrowserFullPage,
                Message = "DevTools capture failed: " + ex.Message,
            };
        }
    }

    private static async Task<int> PromptAndLaunchDedicatedAsync(BrowserOptions options)
    {
        // Synchronously prompt the user via MessageBox; the synchronization
        // context handles dispatch.
        bool wantLaunch = false;
        var tcs = new TaskCompletionSource<bool>();
        var ui = SynchronizationContext.Current;
        Action prompt = () =>
        {
            var exe = !string.IsNullOrEmpty(options.BrowserPath) && File.Exists(options.BrowserPath)
                ? options.BrowserPath
                : BrowserDiscovery.FindBrowserExecutable();
            var msg = exe == null
                ? "No running browser with DevTools enabled. No Chrome/Edge installation was found either.\n\n" +
                  "Set BrowserPath in settings or start Chrome/Edge with --remote-debugging-port=9222."
                : "No running browser with DevTools enabled.\n\nLaunch a dedicated browser instance now?\n\n" + exe;
            var icon = exe == null ? MessageBoxIcon.Error : MessageBoxIcon.Question;
            var buttons = exe == null ? MessageBoxButtons.OK : MessageBoxButtons.YesNo;
            var dr = MessageBox.Show(msg, "Full-page browser capture", buttons, icon);
            wantLaunch = exe != null && dr == DialogResult.Yes;
            tcs.SetResult(true);
        };
        if (ui != null) ui.Post(_ => prompt(), null);
        else prompt();
        await tcs.Task.ConfigureAwait(false);

        if (!wantLaunch) return 0;

        var exePath = !string.IsNullOrEmpty(options.BrowserPath) && File.Exists(options.BrowserPath)
            ? options.BrowserPath
            : BrowserDiscovery.FindBrowserExecutable();
        if (exePath == null) return 0;

        int port = options.Port > 0 ? options.Port : 9223;
        var profile = !string.IsNullOrEmpty(options.DedicatedProfilePath)
            ? options.DedicatedProfilePath
            : Path.Combine(Path.GetTempPath(), "ScrollerCapture-Browser-Profile");
        try { Directory.CreateDirectory(profile); } catch { /* ignore */ }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add($"--remote-debugging-port={port}");
            psi.ArgumentList.Add($"--user-data-dir={profile}");
            psi.ArgumentList.Add("--no-first-run");
            psi.ArgumentList.Add("--no-default-browser-check");
            Process.Start(psi);
        }
        catch
        {
            return 0;
        }

        // Poll for the endpoint to come up. Give the browser a few seconds.
        for (int i = 0; i < 40; i++)
        {
            if (await BrowserDiscovery.PingAsync(port).ConfigureAwait(false)) return port;
            await Task.Delay(250).ConfigureAwait(false);
        }
        return 0;
    }

    private static Task<BrowserTab?> PickTabAsync(System.Collections.Generic.List<BrowserTab> tabs)
    {
        var tcs = new TaskCompletionSource<BrowserTab?>();
        var ui = SynchronizationContext.Current;
        Action show = () =>
        {
            using var form = new BrowserTabPickerForm(tabs);
            var dr = form.ShowDialog();
            tcs.SetResult(dr == DialogResult.OK ? form.Chosen : null);
        };
        if (ui != null) ui.Post(_ => show(), null);
        else show();
        return tcs.Task;
    }
}

internal sealed class BrowserTabPickerForm : Form
{
    private readonly ListBox _list;
    public BrowserTab? Chosen { get; private set; }

    public BrowserTabPickerForm(System.Collections.Generic.List<BrowserTab> tabs)
    {
        Text = "Pick a browser tab";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 360);
        ShowInTaskbar = false;

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = EditorTheme.SurfaceAlt,
            ForeColor = EditorTheme.Text,
            BorderStyle = BorderStyle.None,
        };
        foreach (var t in tabs)
        {
            _list.Items.Add(new Entry(t));
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.DoubleClick += (_, _) => Accept();

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        var ok = new Button { Text = "Capture", Dock = DockStyle.Right, Width = 100 };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Text = "Cancel", Dock = DockStyle.Right, Width = 100, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        Controls.Add(_list);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        EditorTheme.Apply(this);
    }

    private void Accept()
    {
        if (_list.SelectedItem is Entry e)
        {
            Chosen = e.Tab;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private sealed class Entry
    {
        public BrowserTab Tab { get; }
        public Entry(BrowserTab t) { Tab = t; }
        public override string ToString()
        {
            var title = string.IsNullOrWhiteSpace(Tab.Title) ? "(untitled)" : Tab.Title;
            return $"{title}    [{Tab.Url}]";
        }
    }
}
