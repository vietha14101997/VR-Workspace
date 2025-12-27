using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Handles client-side speed test protocol.
    /// Receives binary chunks from server and measures bandwidth.
    /// </summary>
    public class SpeedTestClient
    {
        private readonly ClientWebSocket _ws;
        private readonly CancellationToken _ct;

        // Speed test state
        private bool _isRunning;
        private string _currentDirection;
        private int _chunkSize;
        private int _durationMs;
        private long _bytesReceived;
        private Stopwatch _stopwatch;

        // Results
        public double DownloadMbps { get; private set; }
        public double UploadMbps { get; private set; }

        /// <summary>
        /// Event fired when speed test progress updates.
        /// </summary>
        public event Action<string, double> OnProgress; // direction, progressPercent

        public SpeedTestClient(ClientWebSocket ws, CancellationToken ct)
        {
            _ws = ws;
            _ct = ct;
        }

        /// <summary>
        /// Handle speedtest_start message from server.
        /// Returns a Task that completes when upload test is done (for upload direction).
        /// </summary>
        public async Task HandleSpeedTestStartAsync(string direction, int chunkSize, int durationMs)
        {
            Debug.Log($"[SpeedTest] Starting {direction} test (chunk={chunkSize}, duration={durationMs}ms)");

            _currentDirection = direction;
            _chunkSize = chunkSize;
            _durationMs = durationMs;
            _bytesReceived = 0;
            _stopwatch = Stopwatch.StartNew();
            _isRunning = true;

            OnProgress?.Invoke(direction, 0);

            // For upload direction, we need to start sending data
            // Run in background to not block the receive loop
            if (direction == "upload")
            {
                _ = RunUploadTestAsync(chunkSize, durationMs);
            }
        }

        /// <summary>
        /// Handle speedtest_start message from server (sync version for download).
        /// </summary>
        public void HandleSpeedTestStart(string direction, int chunkSize, int durationMs)
        {
            Debug.Log($"[SpeedTest] Starting {direction} test (chunk={chunkSize}, duration={durationMs}ms)");

            _currentDirection = direction;
            _chunkSize = chunkSize;
            _durationMs = durationMs;
            _bytesReceived = 0;
            _stopwatch = Stopwatch.StartNew();
            _isRunning = true;

            OnProgress?.Invoke(direction, 0);
        }

        /// <summary>
        /// Handle binary data received during download test.
        /// </summary>
        public void HandleBinaryData(byte[] data, int length)
        {
            if (!_isRunning || _currentDirection != "download") return;

            _bytesReceived += length;

            // Calculate progress
            double elapsed = _stopwatch.ElapsedMilliseconds;
            double progress = Math.Min(100, (elapsed / _durationMs) * 100);
            OnProgress?.Invoke("download", progress);
        }

        /// <summary>
        /// Handle speedtest_end message from server.
        /// Returns the measured speed in Mbps.
        /// </summary>
        public async Task<double> HandleSpeedTestEndAsync(string direction, long serverBytes, long serverDurationMs)
        {
            if (!_isRunning) return 0;

            _stopwatch.Stop();
            _isRunning = false;

            double seconds = _stopwatch.Elapsed.TotalSeconds;
            double mbps = (_bytesReceived * 8.0) / (seconds * 1_000_000);

            Debug.Log($"[SpeedTest] {direction} complete: {_bytesReceived} bytes in {seconds:F2}s = {mbps:F1} Mbps");
            Debug.Log($"[SpeedTest] Server reported: {serverBytes} bytes in {serverDurationMs}ms");

            if (direction == "download")
            {
                DownloadMbps = mbps;

                // Send acknowledgment with client-measured speed
                await SendAckAsync(mbps);
            }

            OnProgress?.Invoke(direction, 100);
            return mbps;
        }

        /// <summary>
        /// Handle upload test - send binary chunks to server.
        /// </summary>
        public async Task RunUploadTestAsync(int chunkSize, int durationMs)
        {
            Debug.Log($"[SpeedTest] Starting upload test (chunk={chunkSize}, duration={durationMs}ms)");

            _currentDirection = "upload";
            _isRunning = true;
            OnProgress?.Invoke("upload", 0);

            // Generate random chunk
            var chunk = new byte[chunkSize];
            new System.Random().NextBytes(chunk);

            var stopwatch = Stopwatch.StartNew();
            long bytesSent = 0;

            // Send chunks for durationMs
            while (stopwatch.ElapsedMilliseconds < durationMs && !_ct.IsCancellationRequested)
            {
                try
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(chunk),
                        WebSocketMessageType.Binary,
                        true,
                        _ct);
                    bytesSent += chunk.Length;

                    // Update progress
                    double progress = Math.Min(100, (stopwatch.ElapsedMilliseconds / (double)durationMs) * 100);
                    OnProgress?.Invoke("upload", progress);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SpeedTest] Upload error: {ex.Message}");
                    break;
                }
            }

            stopwatch.Stop();
            _isRunning = false;

            // Send end marker
            var endMsg = $"{{\"type\":\"speedtest_end\",\"direction\":\"upload\",\"totalBytes\":{bytesSent},\"durationMs\":{stopwatch.ElapsedMilliseconds}}}";
            await SendTextAsync(endMsg);

            // Calculate speed
            double seconds = stopwatch.Elapsed.TotalSeconds;
            UploadMbps = (bytesSent * 8.0) / (seconds * 1_000_000);

            Debug.Log($"[SpeedTest] Upload complete: {bytesSent} bytes in {seconds:F2}s = {UploadMbps:F1} Mbps");
            OnProgress?.Invoke("upload", 100);
        }

        /// <summary>
        /// Send pong response for ping measurement.
        /// </summary>
        public async Task SendPongAsync()
        {
            try
            {
                await SendTextAsync("pong");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpeedTest] Failed to send pong: {ex.Message}");
            }
        }

        /// <summary>
        /// Send speed test acknowledgment.
        /// </summary>
        private async Task SendAckAsync(double clientMbps)
        {
            var ack = $"{{\"type\":\"speedtest_ack\",\"clientMbps\":{clientMbps:F2}}}";
            await SendTextAsync(ack);
        }

        private async Task SendTextAsync(string text)
        {
            if (_ws.State != WebSocketState.Open) return;

            var bytes = Encoding.UTF8.GetBytes(text);
            await _ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                _ct);
        }

        /// <summary>
        /// Check if currently running a speed test.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Get current test direction.
        /// </summary>
        public string CurrentDirection => _currentDirection;
    }
}
