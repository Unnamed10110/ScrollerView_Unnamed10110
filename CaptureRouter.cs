using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScrollerCapture;

internal sealed class CaptureRequest
{
    public CaptureMode Mode { get; set; }
    /// <summary>Optional pre-selected region (skip overlay).</summary>
    public Rectangle? Region { get; set; }
    /// <summary>Optional override for delay seconds (else AppSettings.Capture.DelaySeconds).</summary>
    public int? DelaySecondsOverride { get; set; }
}

/// <summary>
/// Coordinates region selection, optional countdown, direction decisions,
/// and dispatch to the right capture implementation (region/horizontal/
/// vertical/auto/manual/browser). Always returns a non-null result; check
/// <see cref="CaptureResult.Success"/>.
/// </summary>
internal sealed class CaptureRouter
{
    private readonly SynchronizationContext _ui;
    private readonly AppSettings _settings;

    public CaptureRouter(SynchronizationContext ui, AppSettings settings)
    {
        _ui = ui;
        _settings = settings;
    }

    public async Task<CaptureResult> RunAsync(CaptureRequest request)
    {
        // Browser full-page capture bypasses the region selector entirely.
        if (request.Mode == CaptureMode.BrowserFullPage)
        {
            return await BrowserCaptureService.CaptureAsync(_settings.Browser).ConfigureAwait(false);
        }

        var selection = request.Region.HasValue
            ? new RegionSelectionResult
            {
                Region = request.Region.Value,
                Source = RegionSelectionSource.ManualDrag,
                Cancelled = false,
            }
            : await SelectRegionAsync().ConfigureAwait(false);

        if (selection == null || selection.Cancelled || selection.Region.Width < 8 || selection.Region.Height < 8)
        {
            return new CaptureResult
            {
                Success = false,
                Mode = request.Mode,
                Message = "Region selection cancelled.",
            };
        }

        int delay = request.DelaySecondsOverride ?? _settings.Capture.DelaySeconds;
        if (delay > 0)
        {
            await RunCountdownAsync(delay, new Point(
                selection.Region.X + selection.Region.Width / 2,
                selection.Region.Y + selection.Region.Height / 2)).ConfigureAwait(false);
        }

        return request.Mode switch
        {
            CaptureMode.Region => await Task.Run(() => DoRegion(selection.Region)).ConfigureAwait(false),
            CaptureMode.Horizontal => await Task.Run(() => DoScroll(selection.Region, CaptureDirection.Horizontal)).ConfigureAwait(false),
            CaptureMode.Vertical => await Task.Run(() => DoScroll(selection.Region, CaptureDirection.Vertical)).ConfigureAwait(false),
            CaptureMode.Auto => await DoAutoAsync(selection.Region).ConfigureAwait(false),
            CaptureMode.Manual => await DoManualAsync(selection.Region).ConfigureAwait(false),
            _ => new CaptureResult { Success = false, Mode = request.Mode, Message = "Unsupported mode." },
        };
    }

    // ------------------------------------------------------------------

    private CaptureResult DoRegion(Rectangle region)
    {
        try
        {
            var bmp = ScreenCapture.CaptureScreenRegion(region);
            return new CaptureResult
            {
                Success = true,
                Image = bmp,
                PartCount = 1,
                Mode = CaptureMode.Region,
                Message = "Region capture complete.",
            };
        }
        catch (Exception ex)
        {
            return new CaptureResult
            {
                Success = false,
                Mode = CaptureMode.Region,
                Message = "Region capture failed: " + ex.Message,
            };
        }
    }

    private CaptureResult DoScroll(Rectangle region, CaptureDirection dir)
    {
        var svc = new ScrollCaptureService { StickyTrim = _settings.Capture.StickyTrim };
        return svc.Capture(region, dir);
    }

    private async Task<CaptureResult> DoAutoAsync(Rectangle region)
    {
        // Inspect on a background thread to keep UI responsive.
        var info = await Task.Run(() => ScrollableElementFinder.Inspect(region)).ConfigureAwait(false);

        if (info.HorizontalScrollable && info.VerticalScrollable)
        {
            var picked = await PickModeAsync(true, true, offerRegion: true, offerManual: true,
                subtitle: "Both axes look scrollable. What should we capture?").ConfigureAwait(false);
            if (picked == null) return Cancelled(CaptureMode.Auto);
            return await DispatchPickedAsync(picked.Value, region).ConfigureAwait(false);
        }
        if (info.HorizontalScrollable)
        {
            return await Task.Run(() => DoScroll(region, CaptureDirection.Horizontal)).ConfigureAwait(false);
        }
        if (info.VerticalScrollable)
        {
            return await Task.Run(() => DoScroll(region, CaptureDirection.Vertical)).ConfigureAwait(false);
        }

        // Nothing detected as scrollable. Ask whether to do a plain region
        // capture or fall back to manual sampling.
        var fallback = await PickModeAsync(false, false, offerRegion: true, offerManual: true,
            subtitle: "No scrollable container detected in the selected region.").ConfigureAwait(false);
        if (fallback == null) return Cancelled(CaptureMode.Auto);
        return await DispatchPickedAsync(fallback.Value, region).ConfigureAwait(false);
    }

    private async Task<CaptureResult> DispatchPickedAsync(CaptureMode mode, Rectangle region)
    {
        return mode switch
        {
            CaptureMode.Horizontal => await Task.Run(() => DoScroll(region, CaptureDirection.Horizontal)).ConfigureAwait(false),
            CaptureMode.Vertical => await Task.Run(() => DoScroll(region, CaptureDirection.Vertical)).ConfigureAwait(false),
            CaptureMode.Region => await Task.Run(() => DoRegion(region)).ConfigureAwait(false),
            CaptureMode.Manual => await DoManualAsync(region).ConfigureAwait(false),
            _ => Cancelled(mode),
        };
    }

    private Task<CaptureResult> DoManualAsync(Rectangle region)
    {
        var tcs = new TaskCompletionSource<CaptureResult>();
        _ui.Post(_ =>
        {
            try
            {
                var res = ManualCaptureService.Run(region, _settings.Capture);
                tcs.SetResult(res);
            }
            catch (Exception ex)
            {
                tcs.SetResult(new CaptureResult
                {
                    Success = false,
                    Mode = CaptureMode.Manual,
                    Message = "Manual capture failed: " + ex.Message,
                });
            }
        }, null);
        return tcs.Task;
    }

    private static CaptureResult Cancelled(CaptureMode mode) => new()
    {
        Success = false,
        Mode = mode,
        Message = "Cancelled by user.",
    };

    // ------------------------------------------------------------------
    // UI thread helpers
    // ------------------------------------------------------------------

    private Task<RegionSelectionResult?> SelectRegionAsync()
    {
        var tcs = new TaskCompletionSource<RegionSelectionResult?>();
        _ui.Post(_ =>
        {
            try
            {
                using var overlay = new RegionSelectionForm();
                overlay.ShowDialog();
                if (overlay.Result.Cancelled) tcs.SetResult(null);
                else tcs.SetResult(overlay.Result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    private Task RunCountdownAsync(int seconds, Point anchor)
    {
        var tcs = new TaskCompletionSource<bool>();
        _ui.Post(_ =>
        {
            try
            {
                var c = new CountdownOverlayForm(seconds, anchor);
                c.Finished += (_, _) => tcs.TrySetResult(true);
                c.Show();
            }
            catch
            {
                tcs.TrySetResult(true);
            }
        }, null);
        return tcs.Task;
    }

    private Task<CaptureMode?> PickModeAsync(bool hasH, bool hasV, bool offerRegion, bool offerManual, string subtitle)
    {
        var tcs = new TaskCompletionSource<CaptureMode?>();
        _ui.Post(_ =>
        {
            try
            {
                using var chooser = new CaptureDirectionChooserForm(hasH, hasV, offerRegion, offerManual, subtitle);
                var dr = chooser.ShowDialog();
                tcs.SetResult(dr == DialogResult.OK ? chooser.ChosenMode : null);
            }
            catch
            {
                tcs.SetResult(null);
            }
        }, null);
        return tcs.Task;
    }
}
