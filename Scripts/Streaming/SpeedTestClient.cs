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

        // Improved bandwidth measurement - track actual data transfer time
        private Stopwatch _dataTransferStopwatch;
        private bool _firstByteReceived;
        private bool _speedTestEndReceived;
        private TaskCompletionSource<bool> _speedTestEndTcs;

        // Results
        public double BandwidthMbps { get; private set; } // Download speed only (upload test removed)
        public double PingMs { get; private set; }
        public double JitterMs { get; private set; }

        // Ping measurement state
        private System.Collections.Generic.List<double> _pingTimes = new System.Collections.Generic.List<double>();
        private TaskCompletionSource<bool> _pongReceived;

        /// <summary>
        /// Event fired when speed test progress updates.
        /// Parameters: direction, currentMbps, progressPercent
        /// </summary>
        public event Action<string, double, int> OnSpeedProgress;

        /// <summary>
        /// Legacy event for backward compatibility.
        /// </summary>
        public event Action<string, double> OnProgress; // direction, progressPercent

        public SpeedTestClient(ClientWebSocket ws, CancellationToken ct)
        {
            _ws = ws;
            _ct = ct;
        }

        #region Client-Initiated Speed Test (NEW)

        /// <summary>
        /// Client chủ động thực hiện speed test.
        /// Trả về SpeedTestResult với các giá trị đo được.
        /// </summary>
        public async Task<SpeedTestResult> RunSpeedTestAsync()
        {
            Debug.Log("[SpeedTest] Client-initiated speed test starting...");

            var result = new SpeedTestResult();

            // 1. Ping test (5 samples)
            Debug.Log("[SpeedTest] Running ping test...");
            await MeasurePingAsync();
            result.PingMs = PingMs;
            result.JitterMs = JitterMs;
            Debug.Log($"[SpeedTest] Ping: {PingMs:F1}ms, Jitter: {JitterMs:F1}ms");

            // 2. Bandwidth test (download only) - Request Server to send data
            Debug.Log("[SpeedTest] Running bandwidth test...");
            await SendTextAsync("{\"type\":\"speedtest_request\",\"direction\":\"download\",\"durationMs\":2000}");
            result.BandwidthMbps = await MeasureDownloadAsync(2000);
            Debug.Log($"[SpeedTest] Bandwidth: {result.BandwidthMbps:F1} Mbps");

            // 3. Send final results to Server (no upload test)
            // Use InvariantCulture with explicit ToString() to ensure decimal point (not comma) in JSON
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var bandwidthStr = result.BandwidthMbps.ToString("F2", culture);
            var pingStr = result.PingMs.ToString("F2", culture);
            var jitterStr = result.JitterMs.ToString("F2", culture);
            var resultMsg = "{\"type\":\"speedtest_result\",\"bandwidthMbps\":" + bandwidthStr + ",\"pingMs\":" + pingStr + ",\"jitterMs\":" + jitterStr + "}";
            await SendTextAsync(resultMsg);
            Debug.Log($"[SpeedTest] Sent speedtest_result to server: {resultMsg}");

            BandwidthMbps = result.BandwidthMbps;

            return result;
        }

        /// <summary>
        /// Measure ping with 5 samples.
        /// </summary>
        private async Task MeasurePingAsync()
        {
            _pingTimes.Clear();
            const int samples = 5;

            for (int i = 0; i < samples; i++)
            {
                try
                {
                    _pongReceived = new TaskCompletionSource<bool>();
                    var sw = Stopwatch.StartNew();

                    await SendTextAsync("ping");

                    // Wait for pong with timeout
                    using var cts = new CancellationTokenSource(2000);
                    cts.Token.Register(() => _pongReceived?.TrySetResult(false));

                    await _pongReceived.Task;
                    sw.Stop();

                    if (_pongReceived.Task.Result)
                    {
                        _pingTimes.Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SpeedTest] Ping sample {i + 1} failed: {ex.Message}");
                }

                await Task.Delay(100, _ct);
            }

            if (_pingTimes.Count > 0)
            {
                double sum = 0;
                foreach (var t in _pingTimes) sum += t;
                PingMs = sum / _pingTimes.Count;

                // Calculate jitter (average of consecutive differences)
                if (_pingTimes.Count > 1)
                {
                    double jitterSum = 0;
                    for (int i = 1; i < _pingTimes.Count; i++)
                    {
                        jitterSum += Math.Abs(_pingTimes[i] - _pingTimes[i - 1]);
                    }
                    JitterMs = jitterSum / (_pingTimes.Count - 1);
                }
                else
                {
                    JitterMs = 0;
                }
            }
        }

        /// <summary>
        /// Called when pong message is received.
        /// </summary>
        public void HandlePong()
        {
            _pongReceived?.TrySetResult(true);
        }

        /// <summary>
        /// Measure bandwidth (download speed). Server sends binary data after speedtest_request.
        /// This runs in parallel with ReceiveLoop - binary data is collected via HandleBinaryData.
        /// </summary>
        private async Task<double> MeasureDownloadAsync(int durationMs)
        {
            _bytesReceived = 0;
            _durationMs = durationMs;
            _stopwatch = Stopwatch.StartNew();
            _isRunning = true;
            _currentDirection = "bandwidth";

            // Reset improved tracking state
            _firstByteReceived = false;
            _speedTestEndReceived = false;
            _dataTransferStopwatch = null;
            _speedTestEndTcs = new TaskCompletionSource<bool>();

            OnSpeedProgress?.Invoke("bandwidth", 0, 0);
            OnProgress?.Invoke("bandwidth", 0);

            // Wait for either speedtest_end message or timeout
            // This ensures we measure actual data transfer time, not including startup latency
            var maxWaitMs = durationMs + 2000; // Allow extra time for network latency
            var startTime = DateTime.UtcNow;

            while ((DateTime.UtcNow - startTime).TotalMilliseconds < maxWaitMs && !_speedTestEndReceived)
            {
                // Use a shorter timeout to check for completion more frequently
                var remainingMs = maxWaitMs - (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                var waitMs = Math.Min(100, Math.Max(10, remainingMs));

                try
                {
                    await Task.WhenAny(_speedTestEndTcs.Task, Task.Delay(waitMs, _ct));
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Calculate current speed and update UI using actual data transfer time
                if (_firstByteReceived && _dataTransferStopwatch != null)
                {
                    double elapsedSec = _dataTransferStopwatch.Elapsed.TotalSeconds;
                    if (elapsedSec > 0.1) // Only calculate after at least 100ms of data transfer
                    {
                        double currentMbps = (_bytesReceived * 8.0) / (elapsedSec * 1_000_000);
                        int progress = (int)Math.Min(100, (elapsedSec * 1000 * 100) / durationMs);

                        OnSpeedProgress?.Invoke("bandwidth", currentMbps, progress);
                        OnProgress?.Invoke("bandwidth", progress);
                    }
                }
            }

            _isRunning = false;

            // Calculate final speed using actual data transfer time (from first byte to end)
            double seconds;
            if (_firstByteReceived && _dataTransferStopwatch != null)
            {
                _dataTransferStopwatch.Stop();
                seconds = _dataTransferStopwatch.Elapsed.TotalSeconds;
                Debug.Log($"[SpeedTest] Actual data transfer time: {seconds:F2}s (total elapsed: {_stopwatch.Elapsed.TotalSeconds:F2}s)");
            }
            else
            {
                // Fallback to total elapsed time if no data received
                _stopwatch.Stop();
                seconds = _stopwatch.Elapsed.TotalSeconds;
            }

            double mbps = seconds > 0 ? (_bytesReceived * 8.0) / (seconds * 1_000_000) : 0;

            OnSpeedProgress?.Invoke("bandwidth", mbps, 100);
            OnProgress?.Invoke("bandwidth", 100);

            Debug.Log($"[SpeedTest] Final: {_bytesReceived} bytes in {seconds:F2}s = {mbps:F1} Mbps");

            return mbps;
        }

        /// <summary>
        /// Measure upload speed by sending binary data to server.
        /// </summary>
        private async Task<double> MeasureUploadAsync(int durationMs)
        {
            _isRunning = true;
            _currentDirection = "upload";

            OnSpeedProgress?.Invoke("upload", 0, 0);
            OnProgress?.Invoke("upload", 0);

            // Wait for server ready signal
            await Task.Delay(100, _ct);

            var chunk = new byte[64 * 1024]; // 64KB chunks
            new System.Random().NextBytes(chunk);

            var stopwatch = Stopwatch.StartNew();
            long bytesSent = 0;

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

                    // Calculate current speed and update UI
                    double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                    double currentMbps = elapsedSec > 0 ? (bytesSent * 8.0) / (elapsedSec * 1_000_000) : 0;
                    int progress = (int)Math.Min(100, (stopwatch.ElapsedMilliseconds * 100) / durationMs);

                    OnSpeedProgress?.Invoke("upload", currentMbps, progress);
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

            double seconds = stopwatch.Elapsed.TotalSeconds;
            double mbps = seconds > 0 ? (bytesSent * 8.0) / (seconds * 1_000_000) : 0;

            OnSpeedProgress?.Invoke("upload", mbps, 100);
            OnProgress?.Invoke("upload", 100);

            return mbps;
        }

        #endregion

        #region Server-Initiated Speed Test (Legacy)

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
        /// Handle binary data received during bandwidth test.
        /// </summary>
        public void HandleBinaryData(byte[] data, int length)
        {
            if (!_isRunning || _currentDirection != "bandwidth") return;

            // Start timing from when first byte is received
            // This excludes network latency for the request message
            if (!_firstByteReceived)
            {
                _firstByteReceived = true;
                _dataTransferStopwatch = Stopwatch.StartNew();
                Debug.Log($"[SpeedTest] First byte received after {_stopwatch.ElapsedMilliseconds}ms");
            }

            _bytesReceived += length;

            // Calculate progress using actual data transfer time
            if (_dataTransferStopwatch != null)
            {
                double elapsed = _dataTransferStopwatch.ElapsedMilliseconds;
                double progress = Math.Min(100, (elapsed / _durationMs) * 100);
                OnProgress?.Invoke("bandwidth", progress);
            }
        }

        /// <summary>
        /// Called when speedtest_end message is received from server.
        /// This signals that all data has been sent.
        /// </summary>
        public void HandleSpeedTestEnd()
        {
            _speedTestEndReceived = true;
            _speedTestEndTcs?.TrySetResult(true);
            Debug.Log($"[SpeedTest] speedtest_end received, total bytes: {_bytesReceived}");
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
                BandwidthMbps = mbps;

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

            // Calculate speed (legacy - upload test no longer used in new flow)
            double seconds = stopwatch.Elapsed.TotalSeconds;
            double uploadMbps = (bytesSent * 8.0) / (seconds * 1_000_000);

            Debug.Log($"[SpeedTest] Upload complete: {bytesSent} bytes in {seconds:F2}s = {uploadMbps:F1} Mbps");
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
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var mbpsStr = clientMbps.ToString("F2", culture);
            var ack = "{\"type\":\"speedtest_ack\",\"clientMbps\":" + mbpsStr + "}";
            await SendTextAsync(ack);
        }

        private async Task SendTextAsync(string text)
        {
            if (_ws.State != WebSocketState.Open) return;

            // Debug: Log what we're sending
            Debug.Log($"[SpeedTest] SendTextAsync: \"{text}\" (len={text.Length})");

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

        #endregion
    }

    /// <summary>
    /// Speed test result data.
    /// </summary>
    public class SpeedTestResult
    {
        public double BandwidthMbps { get; set; } // Download speed only (upload test removed)
        public double PingMs { get; set; }
        public double JitterMs { get; set; }
    }
}
