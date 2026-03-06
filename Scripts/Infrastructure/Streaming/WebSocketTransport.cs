using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Manages a ClientWebSocket lifecycle: connect, send, receive loop, keepalive.
    /// Fires events for incoming messages, disconnection, and errors.
    /// Owned and disposed by PhaseProtocolClient.
    /// </summary>
    internal sealed class WebSocketTransport : IDisposable
    {
        // ── Public events ────────────────────────────────────────────────────────
        public event Action<string> OnMessageReceived;
        public event Action<byte[]> OnBinaryDataReceived;    // binary data (audio frames, etc.)
        public event Action<int>    OnBinaryBytesReceived;   // byte count only – no copy (speed test)
        public event Action         OnDisconnected;
        public event Action<string> OnError;
        public event Action<double> OnPingRtt;               // RTT in ms when pong arrives

        // ── Configuration ────────────────────────────────────────────────────────
        private const int RECEIVE_TIMEOUT_MS  = 30_000;   // 30 s receive timeout
        private const int PING_INTERVAL_MS    = 2_000;    // aggressive 2 s ping
        private const int PONG_TIMEOUT_MS     = 6_000;    // pong must arrive within 6 s
        private const int MAX_MISSED_PONGS    = 3;

        // ── State ────────────────────────────────────────────────────────────────
        private ClientWebSocket         _ws;
        private CancellationTokenSource _cts;
        private long                    _lastPingSentMs;
        private long                    _lastPongReceivedMs;
        private int                     _missedPongCount;

        public bool IsOpen => _ws?.State == WebSocketState.Open;

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Connect to the given URI and start the receive + keepalive loops.
        /// Throws if connection fails.
        /// </summary>
        public async Task ConnectAsync(Uri uri, CancellationToken externalCt = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            var ct = _cts.Token;

            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            await _ws.ConnectAsync(uri, ct);

            _lastPongReceivedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            _ = ReceiveLoopAsync(ct);
            _ = KeepaliveLoopAsync(ct);
        }

        /// <summary>
        /// Send a UTF-8 text frame. Fire-and-forget friendly (no throw on closed socket).
        /// </summary>
        public async Task SendAsync(string text)
        {
            if (_ws?.State != WebSocketState.Open) return;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await _ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    _cts?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WSTransport] Send error: {ex.Message}");
            }
        }

        /// <summary>
        /// Gracefully close the WebSocket and stop loops.
        /// </summary>
        public async Task DisconnectAsync()
        {
            try { _cts?.Cancel(); } catch { }

            if (_ws?.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Disconnect",
                        CancellationToken.None);
                }
                catch { }
            }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); }  catch { }
            try { _ws?.Abort(); }    catch { }
            try { _ws?.Dispose(); }  catch { }
            _ws  = null;
            _cts = null;
        }

        // ── Receive loop ─────────────────────────────────────────────────────────

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            // 256 KB – large enough for speed-test chunks
            var buffer = new byte[256 * 1024];

            try
            {
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult first;

                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(RECEIVE_TIMEOUT_MS);
                        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                        first = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), linked.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        Debug.LogWarning("[WSTransport] Receive timeout – checking connection health");
                        if (_missedPongCount > 0)
                            OnError?.Invoke("WebSocket receive timeout with missed pongs");
                        continue;
                    }

                    if (first.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[WSTransport] Remote closed WebSocket");
                        OnDisconnected?.Invoke();
                        return;
                    }

                    // ── Binary message ──
                    if (first.MessageType == WebSocketMessageType.Binary)
                    {
                        // Audio frames (small, type byte 0x01): forward data to handler
                        if (OnBinaryDataReceived != null && first.EndOfMessage)
                        {
                            var copy = new byte[first.Count];
                            Buffer.BlockCopy(buffer, 0, copy, 0, first.Count);
                            OnBinaryDataReceived.Invoke(copy);
                            continue;
                        }

                        // Multi-fragment binary or speed-test: count bytes only
                        int total = first.Count;
                        while (!first.EndOfMessage)
                        {
                            first = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                            total += first.Count;
                        }
                        if (OnBinaryDataReceived != null)
                        {
                            // Multi-fragment binary with data handler — reassemble
                            // (unlikely for audio, but handle gracefully)
                            OnBinaryBytesReceived?.Invoke(total);
                        }
                        else
                        {
                            OnBinaryBytesReceived?.Invoke(total);
                        }
                        continue;
                    }

                    // ── Text message ─────────────────────────────────────────────
                    var ms = new System.IO.MemoryStream();
                    ms.Write(buffer, 0, first.Count);

                    while (!first.EndOfMessage)
                    {
                        first = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        ms.Write(buffer, 0, first.Count);
                    }

                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    if (!string.IsNullOrWhiteSpace(text))
                        OnMessageReceived?.Invoke(text);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[WSTransport] Receive loop cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WSTransport] Receive error: {ex.Message}");
                OnError?.Invoke(ex.Message);
            }
        }

        // ── Keepalive loop ───────────────────────────────────────────────────────

        private async Task KeepaliveLoopAsync(CancellationToken ct)
        {
            await Task.Delay(1_000, ct);     // initial delay

            while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    var now             = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var timeSinceLastPong = now - _lastPongReceivedMs;

                    if (_lastPongReceivedMs > 0 && timeSinceLastPong > PONG_TIMEOUT_MS)
                    {
                        _missedPongCount++;
                        Debug.LogWarning(
                            $"[WSTransport] Pong timeout! {timeSinceLastPong} ms since last pong, missed={_missedPongCount}");

                        if (_missedPongCount >= MAX_MISSED_PONGS)
                        {
                            Debug.LogError("[WSTransport] Server not responding – triggering reconnect");
                            OnError?.Invoke("Server not responding (pong timeout)");
                        }
                    }

                    _lastPingSentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    await SendAsync($"ping:{_lastPingSentMs}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WSTransport] Keepalive error: {ex.Message}");
                }

                await Task.Delay(PING_INTERVAL_MS, ct);
            }
        }

        // ── Called by PhaseProtocolClient when a pong arrives ───────────────────

        /// <summary>
        /// Notify the transport that a pong was received.
        /// Resets the missed-pong counter and fires OnPingRtt if timestamp is available.
        /// </summary>
        public void NotifyPongReceived(string rawText)
        {
            var pongTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _lastPongReceivedMs = pongTime;
            _missedPongCount    = 0;

            // "pong:timestamp" format → calculate RTT
            if (rawText.Contains(":"))
            {
                var parts = rawText.Split(':');
                if (parts.Length >= 2 && long.TryParse(parts[1].Trim(), out long sentMs))
                {
                    double rtt = pongTime - sentMs;
                    if (rtt > 0 && rtt < 10_000)
                        OnPingRtt?.Invoke(rtt);
                    return;
                }
            }

            // Simple "pong" without timestamp – use last sent time
            if (_lastPingSentMs > 0)
            {
                double rtt = pongTime - _lastPingSentMs;
                if (rtt > 0 && rtt < 10_000)
                    OnPingRtt?.Invoke(rtt);
            }
        }
    }
}
