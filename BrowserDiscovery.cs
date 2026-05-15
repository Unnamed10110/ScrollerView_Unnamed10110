using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScrollerCapture;

internal sealed class BrowserTab
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string WebSocketDebuggerUrl { get; set; } = string.Empty;
    public int Port;
}

internal static class BrowserDiscovery
{
    public static readonly int[] DefaultPorts = { 9222, 9223, 9224, 21222 };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(750) };

    public static async Task<int> FindEndpointAsync(int preferredPort = 9222, CancellationToken cancel = default)
    {
        foreach (var port in EnumeratePorts(preferredPort))
        {
            if (await PingAsync(port, cancel).ConfigureAwait(false)) return port;
        }
        return 0;
    }

    public static async Task<List<BrowserTab>> ListTabsAsync(int port, CancellationToken cancel = default)
    {
        var list = new List<BrowserTab>();
        try
        {
            var url = $"http://127.0.0.1:{port}/json/list";
            using var resp = await Http.GetAsync(url, cancel).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return list;
            var json = await resp.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            var arr = JsonDocument.Parse(json).RootElement;
            foreach (var el in arr.EnumerateArray())
            {
                var t = new BrowserTab
                {
                    Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Title = el.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                    Url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                    Type = el.TryGetProperty("type", out var ty) ? ty.GetString() ?? "" : "",
                    WebSocketDebuggerUrl = el.TryGetProperty("webSocketDebuggerUrl", out var ws)
                        ? ws.GetString() ?? "" : "",
                    Port = port,
                };
                if (t.Type == "page" && !string.IsNullOrEmpty(t.WebSocketDebuggerUrl))
                {
                    list.Add(t);
                }
            }
        }
        catch
        {
            // ignore
        }
        return list;
    }

    public static async Task<bool> PingAsync(int port, CancellationToken cancel = default)
    {
        try
        {
            var url = $"http://127.0.0.1:{port}/json/version";
            using var resp = await Http.GetAsync(url, cancel).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<int> EnumeratePorts(int preferred)
    {
        if (preferred > 0) yield return preferred;
        foreach (var p in DefaultPorts)
        {
            if (p != preferred) yield return p;
        }
    }

    /// <summary>
    /// Tries to find a Chrome or Edge executable path under common install locations.
    /// </summary>
    public static string? FindBrowserExecutable()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(pf86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(local, "Microsoft", "Edge", "Application", "msedge.exe"),
        };
        foreach (var c in candidates)
        {
            try { if (File.Exists(c)) return c; }
            catch { /* ignore */ }
        }
        return null;
    }
}
