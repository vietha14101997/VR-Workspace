using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;
using VRWorkspace.Streaming;
using VRWorkspace.Native;

/// <summary>
/// Decoder mode for video stream.
/// </summary>
public enum DecoderMode
{
    /// <summary>WebRTC internal decoder (H.264)</summary>
    WebRTC,
    /// <summary>Native HEVC decoder via MediaCodec</summary>
    NativeHevc
}

/// <summary>
/// Multi-PC WebRTC stream client using V2 protocol (3-phase connection).
/// Creates N PeerConnections (one per monitor) via PhaseProtocolClient.
///
/// Decoder modes:
/// - WebRTC: Use WebRTC internal decoder (H.264)
/// - NativeHevc: Use native HEVC decoder via HevcDecoderPlugin (requires RTP depacketization)
/// </summary>
[DisallowMultipleComponent]
public class MultiPCStreamClient : MonoBehaviour
{
    [Header("Signal")]
    public string signalUrl = "ws://192.168.1.9:8288/signal?mode=multitrack&monitors=2&resW=1920&resH=1080&kbps=4000&fps=30";

    [Header("Panels")]
    public WorldPanelPlus[] panels;

    [Header("ICE")]
    public bool skipTcpIceCandidates = true;

    [Header("LAN Optimization")]
    [Tooltip("Auto-detect LAN connection and add &lan=1 parameter")]
    public bool autoDetectLAN = true;

    [Header("Protocol")]
    [Tooltip("Deprecated: V2 protocol is now always used")]
    [System.Obsolete("V2 protocol is now always used. This field is kept for API compatibility.")]
    public bool useV2Protocol = true;

    [Tooltip("Auto-accept suggested config in V2 mode (skip user review)")]
    public bool autoAcceptSuggestedConfig = true;

    [Tooltip("If true, automatically start connection in Start(). Set to false for manual control via ConnectV2Async().")]
    public bool autoStartConnection = true;

    [Header("Decoder")]
    [Tooltip("Force H.264 even if server supports H.265")]
    public bool forceH264 = false;

    // V2 Protocol client
    private PhaseProtocolClient _v2Client;

    // V2 Protocol events (for external UI integration)
    public event Action<ServerHardwareInfo> OnHardwareInfoReceived;
    public event Action<NetworkTestResult> OnNetworkInfoReceived;
    public event Action<SuggestedStreamConfig> OnSuggestedConfigReceived;
    public event Action<string, int, string> OnConfigProgress;
    public event Action<string> OnConnectionError;
    public event Action OnStreamingStarted;
    public event Action<string, double, int> OnSpeedTestProgress; // direction, currentMbps, progress%
    public event Action<int, float, float, bool> OnCursorPosition; // monitorIndex, u, v, visible
    public event Action<VideoCodec> OnCodecSelected; // Fires when codec is negotiated

    // V2 Protocol state (read-only)
    public ConnectionStateMachine StateMachine => _v2Client?.StateMachine;
    public ServerHardwareInfo HardwareInfo => _v2Client?.HardwareInfo;
    public NetworkTestResult NetworkInfo => _v2Client?.NetworkInfo;
    public SuggestedStreamConfig SuggestedConfig => _v2Client?.SuggestedConfig;

    // Decoder state
    private DecoderMode _decoderMode = DecoderMode.WebRTC;
    private VideoCodec _selectedCodec = VideoCodec.H264;

    /// <summary>
    /// Current decoder mode (WebRTC or NativeHevc).
    /// </summary>
    public DecoderMode CurrentDecoderMode => _decoderMode;

    /// <summary>
    /// Selected video codec for this session.
    /// </summary>
    public VideoCodec SelectedCodec => _v2Client?.SelectedCodec ?? VideoCodec.H264;

    /// <summary>
    /// Check if using native HEVC decoder.
    /// </summary>
    public bool IsUsingNativeHevc => _decoderMode == DecoderMode.NativeHevc;

    private CancellationTokenSource _cts;

    // Mipmap RenderTextures for anti-aliasing at distance
    private RenderTexture[] _mipmapTextures;

    public int MonitorCount => _v2Client?.MonitorCount ?? 0;
    public Texture GetTexture(int index) => _v2Client?.GetTexture(index);

    /// <summary>
    /// Check if currently streaming (V2 only).
    /// </summary>
    public bool IsStreaming => _v2Client?.IsStreaming ?? false;

    /// <summary>
    /// Check if connected.
    /// </summary>
    public bool IsConnected => _v2Client?.IsConnected ?? false;

    static bool IsPrivateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(host.Trim(), @"^(\d+)\.(\d+)\.(\d+)\.(\d+)$"))
        {
            var parts = host.Trim().Split('.');
            if (parts.Length == 4 && int.TryParse(parts[0], out int first) && int.TryParse(parts[1], out int second))
            {
                if (first == 10) return true;
                if (first == 192 && second == 168) return true;
                if (first == 172 && second >= 16 && second <= 31) return true;
            }
        }
        return false;
    }

    string BuildOptimizedSignalUrl()
    {
        string url = signalUrl;
        if (autoDetectLAN && !string.IsNullOrEmpty(url))
        {
            try
            {
                var uri = new Uri(url);
                if (IsPrivateHost(uri.Host) && !url.Contains("lan="))
                {
                    url += url.Contains("?") ? "&lan=1" : "?lan=1";
                    Debug.Log($"[MultiPC] LAN optimization: added lan=1 for {uri.Host}");
                }
            }
            catch { }
        }
        return url;
    }

    int GetExpectedMonitors()
    {
        try
        {
            var uri = new Uri(signalUrl);
            var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (int.TryParse(qs.Get("monitors"), out int m)) return Math.Max(1, m);
        }
        catch { }
        return panels?.Length > 0 ? panels.Length : 2;
    }

    /// <summary>
    /// Get or create a RenderTexture with mipmaps for the given source texture.
    /// This eliminates moire patterns when viewing panels at distance.
    /// Supports ExternalTexture from WebRTC by using CommandBuffer for reliable copy.
    /// </summary>
    private RenderTexture GetMipmapTexture(int index, Texture source)
    {
        if (source == null || source.width <= 0 || source.height <= 0)
        {
            Debug.LogWarning($"[MultiPC-MIPMAP] Source invalid for monitor {index}: source={(source != null ? $"{source.width}x{source.height}" : "null")}");
            return null;
        }

        // Lazy init array
        if (_mipmapTextures == null)
            _mipmapTextures = new RenderTexture[16]; // Max 16 monitors

        if (index < 0 || index >= _mipmapTextures.Length)
            return null;

        // Check if we need to create or resize
        var rt = _mipmapTextures[index];
        if (rt == null || rt.width != source.width || rt.height != source.height)
        {
            // Cleanup old
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }

            // Create new RenderTexture with mipmaps
            // Use BGRA32 format for better compatibility with WebRTC textures
            rt = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            rt.useMipMap = true;
            rt.autoGenerateMips = false; // We'll manually generate for reliability
            rt.filterMode = FilterMode.Trilinear;
            rt.anisoLevel = 8;
            rt.Create();

            _mipmapTextures[index] = rt;
            Debug.Log($"[MultiPC-MIPMAP] Created RenderTexture for monitor {index}: {source.width}x{source.height}, useMipMap={rt.useMipMap}, filterMode={rt.filterMode}, sourceType={source.GetType().Name}");
        }

        // Copy source to mipmap texture using the most reliable method available
        // WebRTC textures are ExternalTextures which may not work with standard Blit
        try
        {
            // Method 1: Try Graphics.Blit (works for most textures)
            // Set active RT to ensure Blit works correctly
            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            Graphics.Blit(source, rt);
            RenderTexture.active = prevRT;

            // Manually generate mipmaps
            rt.GenerateMips();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MultiPC-MIPMAP] Failed to copy texture for monitor {index}: {ex.Message}");
            return null;
        }

        return rt;
    }

    async void Start()
    {
        Application.runInBackground = true; // Prevent throttling when not focused (critical for same-machine testing)
        StartCoroutine(WebRTC.Update());
        // IMPORTANT: Enable VSync to prevent screen tearing (diagonal stripe artifacts)
        // VSync=1 synchronizes frame presentation with display refresh
        // This eliminates mid-frame texture update artifacts
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = 60; // Lock to 60fps for smooth streaming
        _cts = new CancellationTokenSource();

        // Wait for manual connection if autoStartConnection is false
        if (!autoStartConnection)
        {
            Debug.Log("[MultiPC] Waiting for manual ConnectV2Async() call");
            return;
        }
        Debug.Log("[MultiPC] Starting V2 protocol (3-phase connection)");
        await StartV2ProtocolAsync();
    }

    // Debug: Log interval control
    private float _lastMipmapDebugTime;
    private const float MIPMAP_DEBUG_INTERVAL = 2f; // Log every 2 seconds

    void Update()
    {
        if (_v2Client == null) return;

        // Poll textures
        _v2Client.PollTextures();

        // Apply textures to panels
        int count = _v2Client.MonitorCount;

        // Debug log (throttled)
        bool shouldLog = Time.time - _lastMipmapDebugTime > MIPMAP_DEBUG_INTERVAL;
        if (shouldLog)
        {
            _lastMipmapDebugTime = Time.time;
            Debug.Log($"[MultiPC-MIPMAP-DEBUG] MonitorCount={count}, panels={(panels != null ? panels.Length.ToString() : "null")}");
        }

        for (int i = 0; i < count; i++)
        {
            var tex = _v2Client.GetTexture(i);

            if (shouldLog)
            {
                Debug.Log($"[MultiPC-MIPMAP-DEBUG] Monitor {i}: tex={(tex != null ? $"{tex.GetType().Name} {tex.width}x{tex.height}" : "null")}");
            }

            if (tex == null) continue;

            // Generate mipmap texture for anti-aliasing at distance
            var mipmapTex = GetMipmapTexture(i, tex);

            if (shouldLog)
            {
                Debug.Log($"[MultiPC-MIPMAP-DEBUG] Monitor {i}: mipmapTex={(mipmapTex != null ? $"{mipmapTex.width}x{mipmapTex.height} mip={mipmapTex.useMipMap}" : "null")}");
            }

            // Apply to panel (use mipmap texture if available)
            if (panels != null && i < panels.Length && panels[i] != null)
            {
                panels[i].contentTexture = mipmapTex != null ? mipmapTex : tex;
                panels[i].Apply();
            }
        }
    }

    /// <summary>
    /// Connect using V2 protocol (for manual connection control).
    /// Call this after setting signalUrl and subscribing to events when autoStartConnection is false.
    /// </summary>
    public async Task ConnectV2Async()
    {
        if (_v2Client != null && _v2Client.IsConnected)
        {
            Debug.LogWarning("[MultiPC-V2] Already connected");
            return;
        }

        Debug.Log("[MultiPC] Manually starting V2 protocol connection");
        await StartV2ProtocolAsync();
    }

    /// <summary>
    /// Start V2 protocol connection flow.
    /// </summary>
    async Task StartV2ProtocolAsync()
    {
        _v2Client = new PhaseProtocolClient();

        // Subscribe to events
        _v2Client.OnHardwareInfoReceived += hw =>
        {
            Debug.Log($"[MultiPC-V2] Hardware: {hw.deviceName}, GPU: {hw.gpu} ({hw.gpuVramGB}GB)");
            OnHardwareInfoReceived?.Invoke(hw);
        };

        _v2Client.OnNetworkInfoReceived += net =>
        {
            Debug.Log($"[MultiPC-V2] Network: {net.connectionType}, Ping: {net.pingMs:F1}ms, BW: {net.bandwidthMbps:F0}Mbps");
            OnNetworkInfoReceived?.Invoke(net);
        };

        _v2Client.OnSpeedTestProgress += (direction, currentMbps, progress) =>
        {
            OnSpeedTestProgress?.Invoke(direction, currentMbps, progress);
        };

        _v2Client.OnSuggestedConfigReceived += cfg =>
        {
            Debug.Log($"[MultiPC-V2] Suggested: {cfg.monitors}mon @ {cfg.resolutionWidth}x{cfg.resolutionHeight}, {cfg.fps}fps, codec: {cfg.selectedCodec}");
            OnSuggestedConfigReceived?.Invoke(cfg);

            // Determine decoder mode based on selected codec
            SetupDecoderMode(_v2Client.SelectedCodec);

            // Auto-accept if enabled
            if (autoAcceptSuggestedConfig)
            {
                Debug.Log("[MultiPC-V2] Auto-accepting suggested config");
                _ = AcceptSuggestedConfigAsync();
            }
        };

        _v2Client.OnConfigProgress += (step, progress, message) =>
        {
            Debug.Log($"[MultiPC-V2] Config: {step} {progress}% - {message}");
            OnConfigProgress?.Invoke(step, progress, message);
        };

        _v2Client.OnVideoTextureReceived += (idx, tex) =>
        {
            Debug.Log($"[MultiPC-V2] Monitor {idx} texture received: {tex.width}x{tex.height}");
        };

        _v2Client.OnStreamingStarted += () =>
        {
            Debug.Log("[MultiPC-V2] Streaming started!");
            OnStreamingStarted?.Invoke();
        };

        _v2Client.OnError += err =>
        {
            Debug.LogError($"[MultiPC-V2] Error: {err}");
            OnConnectionError?.Invoke(err);
        };

        _v2Client.OnDisconnected += () =>
        {
            Debug.Log("[MultiPC-V2] Disconnected");
        };

        _v2Client.OnCursorPosition += (monitorIndex, u, v, visible) =>
        {
            OnCursorPosition?.Invoke(monitorIndex, u, v, visible);
        };

        // Connect
        string url = BuildOptimizedSignalUrl();
        await _v2Client.ConnectAsync(url);
    }

    /// <summary>
    /// Accept suggested config and proceed to Phase 2 (V2 protocol).
    /// </summary>
    public async Task AcceptSuggestedConfigAsync()
    {
        if (_v2Client == null || _v2Client.SuggestedConfig == null)
        {
            Debug.LogWarning("[MultiPC-V2] No suggested config available");
            return;
        }

        var config = StreamingConfig.FromSuggested(_v2Client.SuggestedConfig);
        await _v2Client.ProceedToPhase2Async(config);

        // Wait for ICE to complete and start streaming
        while (_v2Client.StateMachine.CurrentPhase != ConnectionPhase.ReadyToStream &&
               _v2Client.StateMachine.CurrentPhase != ConnectionPhase.Error)
        {
            await Task.Delay(100);
        }

        if (_v2Client.StateMachine.CurrentPhase == ConnectionPhase.ReadyToStream)
        {
            await _v2Client.StartStreamingAsync();
        }
    }

    /// <summary>
    /// Apply custom config and proceed to Phase 2 (V2 protocol).
    /// </summary>
    public async Task ApplyConfigAsync(StreamingConfig config)
    {
        Debug.Log("[MultiPC-V2] ApplyConfigAsync called");

        if (_v2Client == null)
        {
            Debug.LogWarning("[MultiPC-V2] Not connected (_v2Client is null)");
            return;
        }

        Debug.Log("[MultiPC-V2] Calling _v2Client.ProceedToPhase2Async...");
        await _v2Client.ProceedToPhase2Async(config);
        Debug.Log("[MultiPC-V2] ProceedToPhase2Async returned");
    }

    /// <summary>
    /// Start streaming after ICE is complete (V2 protocol).
    /// </summary>
    public async Task StartStreamingV2Async()
    {
        if (_v2Client == null)
        {
            Debug.LogWarning("[MultiPC-V2] Not connected");
            return;
        }

        await _v2Client.StartStreamingAsync();
    }

    /// <summary>
    /// Stop V2 connection.
    /// </summary>
    public async Task StopV2Async()
    {
        if (_v2Client != null)
        {
            await _v2Client.StopAsync();
            _v2Client.Dispose();
            _v2Client = null;
        }
    }

    /// <summary>
    /// Setup decoder mode based on selected codec.
    /// </summary>
    private void SetupDecoderMode(VideoCodec codec)
    {
        // Check if forceH264 is enabled
        if (forceH264)
        {
            Debug.Log("[MultiPC] forceH264 enabled, using WebRTC decoder");
            _decoderMode = DecoderMode.WebRTC;
            _selectedCodec = VideoCodec.H264;
            OnCodecSelected?.Invoke(VideoCodec.H264);
            return;
        }

        _selectedCodec = codec;

        if (codec == VideoCodec.H265)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Check if native HEVC decoder is available
            if (HevcDecoderPlugin.IsAvailable())
            {
                _decoderMode = DecoderMode.NativeHevc;
                Debug.Log("[MultiPC] Using Native HEVC decoder (MediaCodec)");
            }
            else
            {
                // Fallback to WebRTC (will likely fail for HEVC)
                _decoderMode = DecoderMode.WebRTC;
                Debug.LogWarning("[MultiPC] HEVC selected but native decoder not available, falling back to WebRTC");
            }
#else
            // Non-Android: WebRTC only
            _decoderMode = DecoderMode.WebRTC;
            Debug.Log("[MultiPC] Non-Android platform, using WebRTC decoder for HEVC (may not work)");
#endif
        }
        else
        {
            // H.264: Use WebRTC decoder
            _decoderMode = DecoderMode.WebRTC;
            Debug.Log("[MultiPC] Using WebRTC decoder for H.264");
        }

        OnCodecSelected?.Invoke(codec);
        Debug.Log($"[MultiPC] Decoder mode: {_decoderMode}, Codec: {codec}");
    }

    /// <summary>
    /// Check if HEVC hardware decoder is available on this device.
    /// </summary>
    public static bool IsHevcDecoderAvailable()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return HevcDecoderPlugin.IsAvailable();
#else
        return false;
#endif
    }

    void OnDisable() => Cleanup();
    void OnDestroy() => Cleanup();

    void Cleanup()
    {
        // V2 Protocol cleanup
        if (_v2Client != null)
        {
            try { _v2Client.Dispose(); } catch { }
            _v2Client = null;
        }

        // Mipmap textures cleanup
        if (_mipmapTextures != null)
        {
            foreach (var rt in _mipmapTextures)
            {
                if (rt != null)
                {
                    try { rt.Release(); Destroy(rt); } catch { }
                }
            }
            _mipmapTextures = null;
        }

        try { _cts?.Cancel(); } catch { }
    }
}
