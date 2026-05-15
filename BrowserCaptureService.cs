using System.Threading.Tasks;

namespace ScrollerCapture;

/// <summary>
/// Stub for the Chrome/Edge DevTools full-page capture pipeline. The real
/// DevTools client implementation lives in
/// <see cref="DevToolsClient"/> and is wired in during the browser-devtools
/// phase. For now this returns a failure result with a clear message so
/// the router/UI can fall back gracefully.
/// </summary>
internal static class BrowserCaptureService
{
    public static async Task<CaptureResult> CaptureAsync(BrowserOptions options)
    {
        var result = await BrowserDevTools.CaptureFullPageAsync(options).ConfigureAwait(false);
        return result;
    }
}
