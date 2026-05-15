using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ScrollerCapture;

/// <summary>
/// Minimal Chrome DevTools Protocol client over WebSocket. Implements just
/// enough of the request/response correlation to send a handful of CDP
/// commands and await their results. Designed to be created per-capture
/// and disposed quickly.
/// </summary>
internal sealed class DevToolsClient : IDisposable
{
    private readonly ClientWebSocket _ws = new();
    private int _id;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly object _lock = new();
    private Task? _receiveLoop;
    private CancellationTokenSource _cts = new();

    public async Task ConnectAsync(string wsUrl, CancellationToken cancel = default)
    {
        await _ws.ConnectAsync(new Uri(wsUrl), cancel).ConfigureAwait(false);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public async Task<JsonElement> SendAsync(string method, object? parameters = null, CancellationToken cancel = default)
    {
        int id = Interlocked.Increment(ref _id);
        var msg = new
        {
            id,
            method,
            @params = parameters,
        };
        var json = JsonSerializer.Serialize(msg);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _pending[id] = tcs;
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancel).ConfigureAwait(false);
        using var reg = cancel.Register(() => tcs.TrySetCanceled());
        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancel)
    {
        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (!cancel.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);
                var text = sb.ToString();
                try
                {
                    var node = JsonNode.Parse(text);
                    if (node is JsonObject obj && obj.TryGetPropertyValue("id", out var idVal) && idVal != null)
                    {
                        int id = idVal.GetValue<int>();
                        TaskCompletionSource<JsonElement>? tcs;
                        lock (_lock)
                        {
                            _pending.TryGetValue(id, out tcs);
                            _pending.Remove(id);
                        }
                        if (tcs != null)
                        {
                            if (obj.TryGetPropertyValue("error", out var err) && err != null)
                            {
                                tcs.TrySetException(new InvalidOperationException(err.ToJsonString()));
                            }
                            else if (obj.TryGetPropertyValue("result", out var res) && res != null)
                            {
                                var doc = JsonDocument.Parse(res.ToJsonString());
                                tcs.TrySetResult(doc.RootElement.Clone());
                            }
                            else
                            {
                                tcs.TrySetResult(default);
                            }
                        }
                    }
                    // Events (no id) are ignored for now.
                }
                catch
                {
                    // ignore non-JSON frames
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
        catch
        {
            // surface to pending tasks
            lock (_lock)
            {
                foreach (var kv in _pending)
                {
                    kv.Value.TrySetException(new InvalidOperationException("DevTools connection lost."));
                }
                _pending.Clear();
            }
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* ignore */ }
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(500);
            }
        }
        catch { /* ignore */ }
        _ws.Dispose();
        _cts.Dispose();
    }
}
