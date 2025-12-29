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
    /// REWRITTEN to match browser performance (340 Mbps vs previous 65 Mbps).
    /// Key changes:
    /// - Increased duration to 4 seconds (was 2s)
    /// - High-resolution Stopwatch timing throughout
    /// - Event-driven with TaskCompletionSource (no polling)
    /// - 500ms warmup period before measuring
    /// </summary>
    public class SpeedTestClient
    {
        private readonly ClientWebSocket _ws;
        private readonly CancellationToken _ct;

        // High-resolution timing (like browser's performance.now())
        private readonly Stopwatch _masterTimer = Stopwatch.StartNew();

        // Speed test state
        private volatile bool _isRunning;
        private string _currentDirection;
        private int _durationMs;
        private long _bytesReceived;

        // Improved measurement - event-driven instead of polling
        private long _measurementStartTicks;
        private bool _warmupComplete;
        private TaskCompletionSource<double> _bandwidthComplete;

        // Constants - tuned for accurate measurement
        private const int SPEED_TEST_DURATION_MS = 4000;  // 4 seconds (was 2s)
        private const int WARMUP_PERIOD_MS = 500;         // First 500ms = warmup
        private const int PING_SAMPLES = 5;
        private const int PING_TIMEOUT_MS = 2000;

        // Results
        public double BandwidthMbps { get; private set; }
        public double PingMs { get; private set; }
        public double JitterMs { get; private set; }

        // Ping measurement
        private System.Collections.Generic.List<double> _pingTimes = new System.Collections.Generic.List<double>();
        private TaskCompletionSource<bool> _pongReceived;
        private long _pingStartTicks;

        /// <summary>
        /// Event fired when speed test progress updates.
        /// Parameters: direction, currentMbps, progressPercent
        /// </summary>
        public event Action<string, double, int> OnSpeedProgress;

        /// <summary>
        /// Legacy event for backward compatibility.
        /// </summary>
        public event Action<string, double> OnProgress;

        public SpeedTestClient(ClientWebSocket ws, CancellationToken ct)
        {
            _ws = ws;
            _ct = ct;
        }

        #region Client-Initiated Speed Test (Rewritten)

        /// <summary>
        /// Run complete speed test. Returns result with bandwidth, ping, jitter.
        /// Rewritten to match browser's accuracy.
        /// </summary>
        public async Task<SpeedTestResult> RunSpeedTestAsync()
        {
            Debug.Log("[SpeedTest] Client-initiated speed test starting (browser-matched version)...");

            var result = new SpeedTestResult();

            // 1. Ping test (5 samples with high-resolution timing)
            Debug.Log("[SpeedTest] Running ping test...");
            await MeasurePingAsync();
            result.PingMs = PingMs;
            result.JitterMs = JitterMs;
            Debug.Log($"[SpeedTest] Ping: {PingMs:F1}ms, Jitter: {JitterMs:F1}ms");

            // 2. Bandwidth test (4 seconds with warmup)
            Debug.Log($"[SpeedTest] Running bandwidth test ({SPEED_TEST_DURATION_MS}ms with {WARMUP_PERIOD_MS}ms warmup)...");
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var requestJson = "{\"type\":\"speedtest_request\",\"direction\":\"download\",\"durationMs\":" + SPEED_TEST_DURATION_MS + "}";
            await SendTextAsync(requestJson);
            result.BandwidthMbps = await MeasureDownloadAsync();
            Debug.Log($"[SpeedTest] Bandwidth: {result.BandwidthMbps:F1} Mbps");

            // 3. Send final results to server
            var bandwidthStr = result.BandwidthMbps.ToString("F2", culture);
            var pingStr = result.PingMs.ToString("F2", culture);
            var jitterStr = result.JitterMs.ToString("F2", culture);
            var resultMsg = "{\"type\":\"speedtest_result\",\"bandwidthMbps\":" + bandwidthStr + ",\"pingMs\":" + pingStr + ",\"jitterMs\":" + jitterStr + "}";
            await SendTextAsync(resultMsg);
            Debug.Log($"[SpeedTest] Sent speedtest_result to server");

            BandwidthMbps = result.BandwidthMbps;

            return result;
        }

        /// <summary>
        /// Measure ping with high-resolution Stopwatch timing.
        /// </summary>
        private async Task MeasurePingAsync()
        {
            _pingTimes.Clear();

            for (int i = 0; i < PING_SAMPLES; i++)
            {
                try
                {
                    _pongReceived = new TaskCompletionSource<bool>();
                    _pingStartTicks = _masterTimer.ElapsedTicks;

                    // Fire-and-forget send (like browser)
                    _ = SendTextAsync("ping");

                    // Wait for pong with timeout
                    using var cts = new CancellationTokenSource(PING_TIMEOUT_MS);
                    cts.Token.Register(() => _pongReceived?.TrySetResult(false));

                    var success = await _pongReceived.Task;

                    if (success)
                    {
                        // Already calculated in HandlePong() with high-res timing
                        // _pingTimes was already updated
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SpeedTest] Ping sample {i + 1} failed: {ex.Message}");
                }

                // Small delay between samples (but don't block)
                await Task.Delay(50, _ct);
            }

            if (_pingTimes.Count > 0)
            {
                // Calculate average
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
            }
        }

        /// <summary>
        /// Called when pong message is received - high-resolution timing.
        /// </summary>
        public void HandlePong()
        {
            var now = _masterTimer.ElapsedTicks;
            if (_pingStartTicks > 0)
            {
                // Calculate RTT with microsecond precision
                double rttMs = (now - _pingStartTicks) * 1000.0 / Stopwatch.Frequency;
                _pingTimes.Add(rttMs);
                _pingStartTicks = 0;
            }
            _pongReceived?.TrySetResult(true);
        }

        /// <summary>
        /// Measure bandwidth - event-driven, no polling.
        /// </summary>
        private async Task<double> MeasureDownloadAsync()
        {
            // Reset state
            _bytesReceived = 0;
            _warmupComplete = false;
            _measurementStartTicks = 0;
            _isRunning = true;
            _currentDirection = "bandwidth";
            _durationMs = SPEED_TEST_DURATION_MS;

            // Create completion source for event-driven pattern
            _bandwidthComplete = new TaskCompletionSource<double>();

            OnSpeedProgress?.Invoke("bandwidth", 0, 0);
            OnProgress?.Invoke("bandwidth", 0);

            // Wait for completion (triggered by HandleSpeedTestEnd) or timeout
            var totalTimeoutMs = SPEED_TEST_DURATION_MS + 3000; // Extra 3s for network latency

            var completed = await Task.WhenAny(
                _bandwidthComplete.Task,
                Task.Delay(totalTimeoutMs, _ct)
            );

            _isRunning = false;

            // Get result
            double mbps;
            if (_bandwidthComplete.Task.IsCompleted && !_bandwidthComplete.Task.IsFaulted)
            {
                mbps = _bandwidthComplete.Task.Result;
            }
            else
            {
                // Timeout - calculate from what we received
                mbps = CalculateBandwidth();
                Debug.LogWarning($"[SpeedTest] Timeout waiting for speedtest_end, calculated: {mbps:F1} Mbps");
            }

            OnSpeedProgress?.Invoke("bandwidth", mbps, 100);
            OnProgress?.Invoke("bandwidth", 100);

            return mbps;
        }

        /// <summary>
        /// Calculate bandwidth from collected data.
        /// </summary>
        private double CalculateBandwidth()
        {
            if (_measurementStartTicks == 0 || _bytesReceived == 0)
                return 0;

            var elapsedTicks = _masterTimer.ElapsedTicks - _measurementStartTicks;
            var elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency;

            if (elapsedSeconds <= 0)
                return 0;

            return (_bytesReceived * 8.0) / (elapsedSeconds * 1_000_000);
        }

        /// <summary>
        /// Handle binary data received during bandwidth test.
        /// Uses warmup period for accurate measurement.
        /// </summary>
        public void HandleBinaryData(byte[] data, int length)
        {
            // Delegate to RecordBytesReceived - data is not used
            RecordBytesReceived(length);
        }

        /// <summary>
        /// Record bytes received during bandwidth test - OPTIMIZED version.
        /// No memory allocation, just counts bytes.
        /// </summary>
        public void RecordBytesReceived(int byteCount)
        {
            if (!_isRunning || _currentDirection != "bandwidth") return;

            var now = _masterTimer.ElapsedTicks;

            // First chunk - start warmup timer
            if (_measurementStartTicks == 0)
            {
                _measurementStartTicks = now;
                Debug.Log("[SpeedTest] First byte received - starting warmup...");
            }

            var elapsedMs = (now - _measurementStartTicks) * 1000.0 / Stopwatch.Frequency;

            // Only count bytes after warmup period
            if (elapsedMs >= WARMUP_PERIOD_MS)
            {
                if (!_warmupComplete)
                {
                    _warmupComplete = true;
                    _measurementStartTicks = now; // Reset timing for actual measurement
                    _bytesReceived = 0;           // Reset byte count
                    Debug.Log("[SpeedTest] Warmup complete - starting measurement");
                }
                _bytesReceived += byteCount;

                // Calculate progress and current speed
                var measureElapsedMs = (now - _measurementStartTicks) * 1000.0 / Stopwatch.Frequency;
                var measurementDuration = _durationMs - WARMUP_PERIOD_MS;
                int progress = (int)Math.Min(100, (measureElapsedMs / measurementDuration) * 100);

                if (measureElapsedMs > 100) // Only after 100ms of actual measurement
                {
                    var currentMbps = CalculateBandwidth();
                    OnSpeedProgress?.Invoke("bandwidth", currentMbps, progress);
                    OnProgress?.Invoke("bandwidth", progress);
                }
            }
        }

        /// <summary>
        /// Called when speedtest_end message is received from server.
        /// Triggers completion of bandwidth measurement.
        /// </summary>
        public void HandleSpeedTestEnd()
        {
            if (!_isRunning) return;

            var mbps = CalculateBandwidth();
            Debug.Log($"[SpeedTest] speedtest_end received - Final: {_bytesReceived} bytes = {mbps:F1} Mbps");

            _bandwidthComplete?.TrySetResult(mbps);
        }

        #endregion

        #region Legacy Support

        /// <summary>
        /// Handle speedtest_start message from server (legacy).
        /// </summary>
        public async Task HandleSpeedTestStartAsync(string direction, int chunkSize, int durationMs)
        {
            Debug.Log($"[SpeedTest] Legacy speedtest_start: {direction} (chunk={chunkSize}, duration={durationMs}ms)");

            _currentDirection = direction;
            _durationMs = durationMs;
            _bytesReceived = 0;
            _measurementStartTicks = 0;
            _warmupComplete = false;
            _isRunning = true;

            OnProgress?.Invoke(direction, 0);

            if (direction == "upload")
            {
                _ = RunUploadTestAsync(chunkSize, durationMs);
            }
        }

        /// <summary>
        /// Handle speedtest_start message from server (sync version).
        /// </summary>
        public void HandleSpeedTestStart(string direction, int chunkSize, int durationMs)
        {
            Debug.Log($"[SpeedTest] Legacy speedtest_start (sync): {direction}");

            _currentDirection = direction;
            _durationMs = durationMs;
            _bytesReceived = 0;
            _measurementStartTicks = 0;
            _warmupComplete = false;
            _isRunning = true;

            OnProgress?.Invoke(direction, 0);
        }

        /// <summary>
        /// Handle speedtest_end message from server (legacy async).
        /// </summary>
        public async Task<double> HandleSpeedTestEndAsync(string direction, long serverBytes, long serverDurationMs)
        {
            if (!_isRunning) return 0;

            _isRunning = false;
            var mbps = CalculateBandwidth();

            Debug.Log($"[SpeedTest] Legacy {direction} complete: {_bytesReceived} bytes = {mbps:F1} Mbps");
            Debug.Log($"[SpeedTest] Server reported: {serverBytes} bytes in {serverDurationMs}ms");

            if (direction == "download")
            {
                BandwidthMbps = mbps;
                await SendAckAsync(mbps);
            }

            OnProgress?.Invoke(direction, 100);
            return mbps;
        }

        /// <summary>
        /// Handle upload test (legacy).
        /// </summary>
        public async Task RunUploadTestAsync(int chunkSize, int durationMs)
        {
            Debug.Log($"[SpeedTest] Starting upload test (chunk={chunkSize}, duration={durationMs}ms)");

            _currentDirection = "upload";
            _isRunning = true;
            OnProgress?.Invoke("upload", 0);

            var chunk = new byte[chunkSize];
            new System.Random().NextBytes(chunk);

            var startTicks = _masterTimer.ElapsedTicks;
            long bytesSent = 0;

            while (!_ct.IsCancellationRequested)
            {
                var elapsedMs = (_masterTimer.ElapsedTicks - startTicks) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs >= durationMs) break;

                try
                {
                    await _ws.SendAsync(
                        new ArraySegment<byte>(chunk),
                        WebSocketMessageType.Binary,
                        true,
                        _ct);
                    bytesSent += chunk.Length;

                    int progress = (int)Math.Min(100, (elapsedMs / durationMs) * 100);
                    OnProgress?.Invoke("upload", progress);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SpeedTest] Upload error: {ex.Message}");
                    break;
                }
            }

            _isRunning = false;

            var totalElapsedMs = (_masterTimer.ElapsedTicks - startTicks) * 1000.0 / Stopwatch.Frequency;
            var endMsg = $"{{\"type\":\"speedtest_end\",\"direction\":\"upload\",\"totalBytes\":{bytesSent},\"durationMs\":{(long)totalElapsedMs}}}";
            await SendTextAsync(endMsg);

            var seconds = totalElapsedMs / 1000.0;
            var uploadMbps = seconds > 0 ? (bytesSent * 8.0) / (seconds * 1_000_000) : 0;

            Debug.Log($"[SpeedTest] Upload complete: {bytesSent} bytes in {seconds:F2}s = {uploadMbps:F1} Mbps");
            OnProgress?.Invoke("upload", 100);
        }

        /// <summary>
        /// Send pong response (legacy).
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
        /// Send speed test acknowledgment (legacy).
        /// </summary>
        private async Task SendAckAsync(double clientMbps)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var mbpsStr = clientMbps.ToString("F2", culture);
            var ack = "{\"type\":\"speedtest_ack\",\"clientMbps\":" + mbpsStr + "}";
            await SendTextAsync(ack);
        }

        #endregion

        #region Utilities

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

        #endregion
    }

    /// <summary>
    /// Speed test result data.
    /// </summary>
    public class SpeedTestResult
    {
        public double BandwidthMbps { get; set; }
        public double PingMs { get; set; }
        public double JitterMs { get; set; }
    }
}
