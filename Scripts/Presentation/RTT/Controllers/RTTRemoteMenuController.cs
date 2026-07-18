using System;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;
using System.Collections.Generic;
using Unity.WebRTC;
using VRWorkspace.Core;
using VRWorkspace.ViewModels;
using VRWorkspace.Streaming;
using VRWorkspace.Panel;
using VRWorkspace.Services.RemoteDesktop;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.UI.RTT.Controllers
{
    /// <summary>
    /// Controls the remote menu lifecycle including creation, event handling,
    /// and coordination with RemoteConnectionPipeline.
    /// </summary>
    public class RTTRemoteMenuController : MonoBehaviour
    {
        #region Configuration
        [Header("Connection Pipeline")]
        [SerializeField] private RemoteConnectionPipeline connectionPipeline;
        #endregion

        #region Private Fields
        private RTTRemoteMenu _remoteMenuInstance;
        private GameObject _menuObject;

        // Progress flow fields
        private ConnectionViewModel _viewModel;
        private WorldPanelClusterRig _clusterRig;
        private List<PanelProgressOverlay> _progressOverlays = new List<PanelProgressOverlay>();
        private bool _isStreamingActive = false;
        private Coroutine _webrtcUpdateCoroutine;

        // Delayed ClusterRig creation - store config until streaming is ready
        private StreamingConfig _pendingConfig;

        // Remote taskbar (follows ClusterRig during streaming)
        private RTTRemoteTaskbar _remoteTaskbar;

        // Remote audio playback (auto-created when streaming starts)
        private RemoteAudioPlayer _audioPlayer;
        private DataChannelAudioPlayer _dcAudioPlayer; // Fallback: Opus via DataChannel when Audio PC ICE fails
        private bool _mediaRelayActive; // True when media relay (WebSocket fallback) is active

        // Cursor tracking
        private int _activeCursorPanelIndex = -1;

        // Remote input forwarding (BT mouse/keyboard → server)
        private VRWorkspace.VRInput.RemoteInputBridge _inputBridge;

        // Mipmap textures for anti-aliasing at distance
        private RenderTexture[] _mipmapTextures;
        private float _lastMipmapDebugTime;
        private const float MIPMAP_DEBUG_INTERVAL = 2f;

        // Saved zoom distance before remote mode depth changes (restored when exiting remote)
        private float _savedZoomDistance = -1f;
        #endregion

        #region Events
        public event System.Action OnBackClicked;
        public event System.Action OnConnectClicked;
        public event System.Action OnStartClicked;
        #endregion

        #region Properties
        public RTTRemoteMenu RemoteMenuInstance => _remoteMenuInstance;
        public RemoteConnectionPipeline ConnectionPipeline => connectionPipeline;
        public RTTRemoteTaskbar RemoteTaskbar => _remoteTaskbar;
        #endregion

        #region Public API
        /// <summary>
        /// Create the remote menu in the specified container.
        /// </summary>
        public GameObject CreateMenu(RectTransform container, float containerW, float containerH,
            TMP_FontAsset font, Color themeColor, Color accentColor)
        {
            if (container == null)
            {
                Debug.LogWarning("[RTTRemoteMenuController] Container is null");
                return null;
            }

            // Auto-create connection pipeline if not assigned
            EnsureConnectionPipeline();

            // Create menu container
            _menuObject = new GameObject("RemoteMenu");
            _menuObject.transform.SetParent(container, false);

            RectTransform remoteRT = _menuObject.AddComponent<RectTransform>();
            remoteRT.anchorMin = Vector2.zero;
            remoteRT.anchorMax = Vector2.one;
            remoteRT.offsetMin = Vector2.zero;
            remoteRT.offsetMax = Vector2.zero;

            // Add RTTRemoteMenu component
            _remoteMenuInstance = _menuObject.AddComponent<RTTRemoteMenu>();
            _remoteMenuInstance.themeColor = themeColor;
            _remoteMenuInstance.accentColor = accentColor;
            _remoteMenuInstance.customFont = font;

            // Subscribe to events
            _remoteMenuInstance.OnBackClicked += HandleBackClicked;
            _remoteMenuInstance.OnConnectClicked += HandleConnectClicked;
            _remoteMenuInstance.OnStartClicked += HandleStartClicked;
            _remoteMenuInstance.OnDisconnectClicked += HandleDisconnectClicked;
            _remoteMenuInstance.OnResumeClicked += HandleResumeClicked;

            // Build the remote menu UI
            // NOTE: BuildUI() calls BindToViewModel() which registers ConnectionViewModel with ServiceLocator
            _remoteMenuInstance.BuildUI(_menuObject.transform, containerW, containerH);

            // Get ViewModel AFTER BuildUI() - it's now guaranteed to be registered
            // TODO: Replace with VContainer [Inject] when LifetimeScopes are wired in scene
#pragma warning disable CS0618
            _viewModel = ServiceLocator.Get<ConnectionViewModel>();
#pragma warning restore CS0618
            if (_viewModel != null)
            {
                _viewModel.OnStartWithProgress += HandleStartWithProgress;
                _viewModel.OnAllMonitorsReady += HandleAllMonitorsReady;
                _viewModel.ServerSetupProgress.OnChanged += HandleServerSetupProgress;
                _viewModel.MonitorIceProgress.OnChanged += HandleMonitorIceProgress;
                _viewModel.IsStreaming.OnChanged += HandleStreamingStateChanged;
                _viewModel.OnCursorPositionChanged += HandleCursorPosition;
                _viewModel.OnCursorImageReceived += HandleCursorImageReceived;
                _viewModel.OnConnectionLost += HandleConnectionLost;
            }
            else
            {
                Debug.LogError("[RTTRemoteMenuController] ConnectionViewModel is NULL after BuildUI!");
            }

            return _menuObject;
        }

        /// <summary>
        /// Ensure connection pipeline exists, create if needed.
        /// </summary>
        private void EnsureConnectionPipeline()
        {
            if (connectionPipeline != null) return;

            // Try to find existing
            connectionPipeline = FindAnyObjectByType<RemoteConnectionPipeline>();

            // Create if not found
            if (connectionPipeline == null)
            {
                connectionPipeline = gameObject.AddComponent<RemoteConnectionPipeline>();
            }
        }

        /// <summary>
        /// Cleanup resources and unsubscribe from events.
        /// Fully disconnects and resets connection state so reopening starts fresh.
        /// </summary>
        public void Cleanup()
        {
            if (_remoteMenuInstance != null)
            {
                _remoteMenuInstance.OnBackClicked -= HandleBackClicked;
                _remoteMenuInstance.OnConnectClicked -= HandleConnectClicked;
                _remoteMenuInstance.OnStartClicked -= HandleStartClicked;
                _remoteMenuInstance.OnDisconnectClicked -= HandleDisconnectClicked;
                _remoteMenuInstance.OnResumeClicked -= HandleResumeClicked;
                _remoteMenuInstance = null;
            }

            // Stop streaming and disconnect from server
            if (connectionPipeline != null)
            {
                connectionPipeline.StopStreaming();
            }

            // Reset ViewModel state synchronously FIRST (so next menu opens clean)
            // Then fire-and-forget the actual server disconnect (without state reset)
            if (_viewModel != null)
            {
                _viewModel.ResetState();
                _ = _viewModel.StopClientAsync();

                // Unsubscribe from ViewModel events
                _viewModel.OnStartWithProgress -= HandleStartWithProgress;
                _viewModel.OnAllMonitorsReady -= HandleAllMonitorsReady;
                _viewModel.ServerSetupProgress.OnChanged -= HandleServerSetupProgress;
                _viewModel.MonitorIceProgress.OnChanged -= HandleMonitorIceProgress;
                _viewModel.IsStreaming.OnChanged -= HandleStreamingStateChanged;
                _viewModel.OnCursorPositionChanged -= HandleCursorPosition;
                _viewModel.OnCursorImageReceived -= HandleCursorImageReceived;
                _viewModel.OnConnectionLost -= HandleConnectionLost;
            }

            // Hide cursors before cleanup and clear cache
            HideAllCursors();
            _activeCursorPanelIndex = -1;
            WorldPanelCursor.ClearCache();

            // Cleanup progress overlays and pending config
            CleanupProgressOverlays();
            _isStreamingActive = false;
            _pendingConfig = null;

            // Cleanup remote audio player
            CleanupRemoteAudioPlayer();

            // Cleanup remote taskbar
            CleanupRemoteTaskbar();

            // Cleanup mipmap textures
            CleanupMipmapTextures();

            // Stop WebRTC update coroutine
            if (_webrtcUpdateCoroutine != null)
            {
                StopCoroutine(_webrtcUpdateCoroutine);
                _webrtcUpdateCoroutine = null;
            }

            // Clear ClusterRig from RTTMenu before destroying
            RTTMenu menu = RTTMenu.Instance;
            if (menu != null)
            {
                menu.ClearClusterRig();
            }

            // Destroy ClusterRig
            if (_clusterRig != null)
            {
                Destroy(_clusterRig.gameObject);
                _clusterRig = null;
            }

            // Restore zoom distance to pre-remote value so main menu panels are unaffected
            RestoreZoomDistance();

            // Show main menu and taskbar after cleanup
            ShowMainMenuAndTaskbar();

            _menuObject = null;
        }

        /// <summary>
        /// Poll textures from ViewModel and apply to panels when streaming.
        /// Uses mipmap textures to eliminate aliasing artifacts at distance.
        /// </summary>
        void Update()
        {
            if (_viewModel == null || _clusterRig == null) return;

            // Poll when ICE is connected or later
            var phase = _viewModel.Phase.Value;
            bool shouldPoll = _isStreamingActive ||
                phase == ConnectionPhase.ICENegotiating ||
                phase == ConnectionPhase.ReadyToStream ||
                phase == ConnectionPhase.StartingStream ||
                phase == ConnectionPhase.Streaming;

            if (!shouldPoll) return;

            // Poll textures from WebRTC
            _viewModel.PollTextures();

            // Apply textures to panels
            var panels = _clusterRig.panels;
            if (panels == null || panels.Count == 0) return;

            // Debug log disabled to reduce RAM usage
            // Set PhaseProtocolClient.VerboseLogging = true to enable
            bool shouldLog = false; // Time.time - _lastMipmapDebugTime > MIPMAP_DEBUG_INTERVAL;

            for (int i = 0; i < panels.Count; i++)
            {
                var panel = panels[i];
                if (panel == null) continue;

                var tex = _viewModel.GetTexture(i);
                if (tex == null) continue;

                // Generate mipmap texture for anti-aliasing at distance
                var mipmapTex = GetMipmapTexture(i, tex, panel.mipSharpness);
                var finalTex = mipmapTex != null ? (Texture)mipmapTex : tex;

                if (shouldLog)
                {
                    Debug.Log($"[RTTRemote-MIPMAP] Panel {i}: src={tex.GetType().Name} {tex.width}x{tex.height}, mipmap={(mipmapTex != null ? "OK" : "null")}");
                }

                if (panel.contentTexture != finalTex)
                {
                    panel.contentTexture = finalTex;
                    panel.Apply();

                    // Hide progress overlay when first texture arrives
                    if (i < _progressOverlays.Count && _progressOverlays[i] != null && _progressOverlays[i].gameObject.activeSelf)
                    {
                        _progressOverlays[i].Hide();
                    }
                }
            }
        }

        /// <summary>
        /// Get or create a RenderTexture with mipmaps for anti-aliasing.
        /// This eliminates moire/aliasing artifacts when viewing panels at distance.
        /// </summary>
        private RenderTexture GetMipmapTexture(int index, Texture source, float mipSharpness = 0.1f)
        {
            if (source == null || source.width <= 0 || source.height <= 0)
            {
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
                rt = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                rt.useMipMap = true;
                rt.autoGenerateMips = false; // Manual generation for reliability
                rt.filterMode = FilterMode.Trilinear;
                rt.anisoLevel = 16; // Maximum anisotropic filtering for VR
                rt.Create();
                rt.mipMapBias = 0f; // Bias handled by shader _MipMapBias property to avoid double-bias

                _mipmapTextures[index] = rt;
                Debug.Log($"[RTTRemote-MIPMAP] Created mipmap RT for panel {index}: {source.width}x{source.height}, sourceType={source.GetType().Name}");
            }

            // Copy source to mipmap texture and generate sharp mipmaps
            try
            {
                var prevRT = RenderTexture.active;
                RenderTexture.active = rt;
                Graphics.Blit(source, rt);
                RenderTexture.active = prevRT;

                // Generate sharp mipmaps using Lanczos-2 kernel instead of blurry box filter.
                // This preserves edge detail (text sharpness) while still anti-aliasing (no shimmer).
                SharpMipGenerator.Generate(rt, sharpness: mipSharpness, maxMipLevels: 4);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RTTRemote-MIPMAP] Failed to copy texture for panel {index}: {ex.Message}");
                return null;
            }

            return rt;
        }

        /// <summary>
        /// Set the connection pipeline.
        /// </summary>
        public void SetConnectionPipeline(RemoteConnectionPipeline pipeline)
        {
            connectionPipeline = pipeline;
        }

        /// <summary>
        /// Get connection settings from the remote menu.
        /// </summary>
        public (string host, string port, int monitors, string mode, string resolution, string fps, string monitorType) GetConnectionSettings()
        {
            if (_remoteMenuInstance == null)
                return ("localhost", "8080", 1, "Classic", "1080p", "60 FPS", "standard");

            return (
                _remoteMenuInstance.Host,
                _remoteMenuInstance.Port,
                _remoteMenuInstance.IsUltrawide ? 1 : _remoteMenuInstance.MonitorIndex + 1,
                _remoteMenuInstance.Mode,
                _remoteMenuInstance.Resolution,
                _remoteMenuInstance.FPS,
                _remoteMenuInstance.MonitorType
            );
        }


        #endregion

        #region Private Methods
        private void HandleBackClicked()
        {
            OnBackClicked?.Invoke();
        }

        private void HandleConnectClicked()
        {
            // Streaming is handled by RTTRemoteMenu → ConnectionViewModel → PhaseProtocolClient
            OnConnectClicked?.Invoke();
        }

        private void HandleStartClicked()
        {
            OnStartClicked?.Invoke();
        }

        /// <summary>
        /// Handle DISCONNECT button click.
        /// Cleanup ClusterRig and remote taskbar, reset to initial state.
        /// </summary>
        /// <summary>
        /// Called when connection is lost unexpectedly (ICE failed, reconnect exhausted, weak network).
        /// Performs the same cleanup as user-initiated disconnect, returning to menu.
        /// </summary>
        private void HandleConnectionLost(string reason)
        {
            Debug.LogWarning($"[RTTRemoteMenuController] Connection lost: {reason} — returning to menu");
            HandleDisconnectClicked();
        }

        private void HandleDisconnectClicked()
        {
            Debug.Log("[RTTRemoteMenuController] DISCONNECT clicked - cleaning up streaming resources");

            // Hide cursors before cleanup
            HideAllCursors();
            _activeCursorPanelIndex = -1;

            // Clear ClusterRig from RTTMenu before destroying
            RTTMenu menu = RTTMenu.Instance;
            if (menu != null)
            {
                menu.ClearClusterRig();
            }

            // Cleanup ClusterRig
            if (_clusterRig != null)
            {
                Destroy(_clusterRig.gameObject);
                _clusterRig = null;
            }

            // Cleanup remote audio player
            CleanupRemoteAudioPlayer();

            // Cleanup remote taskbar
            CleanupRemoteTaskbar();

            // Cleanup mipmap textures
            CleanupMipmapTextures();

            // Reset streaming state
            _isStreamingActive = false;
            _pendingConfig = null;

            // Stop WebRTC update coroutine
            if (_webrtcUpdateCoroutine != null)
            {
                StopCoroutine(_webrtcUpdateCoroutine);
                _webrtcUpdateCoroutine = null;
            }

            // Restore zoom distance to pre-remote value so main menu panels are unaffected
            RestoreZoomDistance();

            // Show main menu and taskbar again
            ShowMainMenuAndTaskbar();

            Debug.Log("[RTTRemoteMenuController] Streaming resources cleaned up, menu reset");
        }

        /// <summary>
        /// Handle RESUME button click.
        /// Hide menu and return to ClusterRig/streaming view.
        /// Sends resume_streaming to server to restart capture/encode.
        /// </summary>
        private async void HandleResumeClicked()
        {
            Debug.Log("[RTTRemoteMenuController] RESUME clicked - returning to streaming view");

            // Resume streaming on server (restart capture/encode)
            if (_viewModel != null)
            {
                await _viewModel.ResumeStreamingAsync();
            }

            // Hide menu and taskbar
            HideMainMenuAndTaskbar();

            // Re-apply remote depth setting (zoom was restored for menu display)
            ReapplyRemoteDepth();

            // ClusterRig should already be visible
            if (_clusterRig != null)
            {
                _clusterRig.gameObject.SetActive(true);
            }

            // Show remote taskbar if it exists
            if (_remoteTaskbar != null)
            {
                _remoteTaskbar.Show();
                _remoteTaskbar.OnMenuDismissed(); // Reset back button state
            }
        }

        /// <summary>
        /// Handle new flow: Store config and start WebRTC update loop.
        /// ClusterRig will be created when streaming actually starts.
        /// Progress is shown on RTTRemoteMenu button text instead of panel overlays.
        /// </summary>
        private void HandleStartWithProgress(StreamingConfig config)
        {
            Debug.Log($"[RTTRemoteMenuController] HandleStartWithProgress: {config.monitors} monitors - waiting for streaming ready");

            // Clear cursor cache to ensure fresh cursors for new session
            WorldPanelCursor.ClearCache();

            // Store config for later ClusterRig creation
            _pendingConfig = config;

            // Ensure connection pipeline exists
            EnsureConnectionPipeline();

            // Start WebRTC update loop if not already running
            if (_webrtcUpdateCoroutine == null)
            {
                _webrtcUpdateCoroutine = StartCoroutine(WebRTC.Update());
            }

            // NOTE: ClusterRig is NOT created yet
            // Menu stays visible with button showing progress text
            // ClusterRig will be created when IsStreaming becomes true
        }

        /// <summary>
        /// Hide RTTMenuFrame and RTTTaskbar when entering streaming mode.
        /// </summary>
        private void HideMainMenuAndTaskbar()
        {
            // Hide RTTMenuFrame (primary instance)
            RTTMenuFrame menuFrame = RTTMenuFrame.PrimaryInstance;
            if (menuFrame != null)
            {
                menuFrame.Hide();
                Debug.Log("[RTTRemoteMenuController] Hidden RTTMenuFrame");
            }

            // Hide RTTTaskbar
            RTTTaskbar taskbar = RTTTaskbar.Instance;
            if (taskbar != null)
            {
                taskbar.Hide();
                Debug.Log("[RTTRemoteMenuController] Hidden RTTTaskbar");
            }
        }

        /// <summary>
        /// Restore zoom distance to what it was before remote mode changed it.
        /// </summary>
        private void RestoreZoomDistance()
        {
            if (_savedZoomDistance < 0f) return;

            var zoom = VirtualObjectsZoomController.Instance;
            if (zoom != null)
            {
                Debug.Log($"[RTTRemoteMenuController] Restoring zoom distance: {_savedZoomDistance:F2}m");
                zoom.SetZoomDistance(_savedZoomDistance);
            }
            _savedZoomDistance = -1f;
        }

        /// <summary>
        /// Re-apply remote depth setting (after restoring zoom for menu display).
        /// </summary>
        private void ReapplyRemoteDepth()
        {
            var zoom = VirtualObjectsZoomController.Instance;
            if (zoom == null) return;

            // Save current zoom as base again
            _savedZoomDistance = zoom.CurrentDistance;

            float depth = PlayerPrefs.GetFloat("RemoteDesktop_ScreenDepth", 0.5f);
            float distance = Mathf.Lerp(zoom.MinDistance, zoom.MaxDistance, depth);
            zoom.SetZoomDistance(distance);
        }

        /// <summary>
        /// Show RTTMenuFrame and RTTTaskbar when exiting streaming mode.
        /// </summary>
        private void ShowMainMenuAndTaskbar()
        {
            // Show RTTMenuFrame
            RTTMenuFrame menuFrame = RTTMenuFrame.PrimaryInstance;
            if (menuFrame != null)
            {
                menuFrame.Show();
                Debug.Log("[RTTRemoteMenuController] Shown RTTMenuFrame");
            }

            // Show RTTTaskbar
            RTTTaskbar taskbar = RTTTaskbar.Instance;
            if (taskbar != null)
            {
                taskbar.Show();
                Debug.Log("[RTTRemoteMenuController] Shown RTTTaskbar");
            }
        }

        /// <summary>
        /// Create RTTRemoteTaskbar that follows RTTMenu (same position as RTTTaskbar).
        /// </summary>
        private void CreateRemoteTaskbar()
        {
            if (_clusterRig == null) return;

            // Cleanup existing taskbar
            CleanupRemoteTaskbar();

            // Get or create RTTToolbar
            RTTToolbar toolbar = RTTToolbar.Instance;
            if (toolbar == null)
            {
                toolbar = RTTToolbar.Create();
            }

            // Create taskbar object in RTTToolbar
            GameObject taskbarObj = new GameObject("RTTRemoteTaskbar");
            taskbarObj.transform.SetParent(toolbar.transform, false);

            // Add RTTMiniFrame first (required component)
            RTTMiniFrame frame = taskbarObj.AddComponent<RTTMiniFrame>();
            frame.Configure(
                sec1Capacity: 6,    // Back, Bitrate, FPS, Zoom, Passthrough, Recenter
                sec2Capacity: 3,    // 3 monitor slots
                btnSize: 90f,
                btnSpacing: 12f,
                height: 128f
            );

            // Add RTTRemoteTaskbar controller
            _remoteTaskbar = taskbarObj.AddComponent<RTTRemoteTaskbar>();

            // Follow RTTMenu (same target as RTTTaskbar) for consistent positioning
            RTTMenu menu = RTTMenu.Instance;
            if (menu != null)
            {
                _remoteTaskbar.SetFollowTarget(menu.transform);
            }
            else
            {
                // Fallback to ClusterRig if RTTMenu not available
                _remoteTaskbar.SetFollowTarget(_clusterRig.transform);
            }

            // Set ClusterRig reference separately for panel enable/disable
            _remoteTaskbar.SetClusterRig(_clusterRig);

            // Register with RTTToolbar for sphere positioning
            toolbar.SetActiveTaskbar(frame);

            // Subscribe to taskbar events
            _remoteTaskbar.OnMenuRequested += HandleTaskbarMenuRequest;

            Debug.Log("[RTTRemoteMenuController] Created RTTRemoteTaskbar in RTTToolbar, following RTTMenu");
        }

        /// <summary>
        /// Handle taskbar menu request (first back press).
        /// Hides ClusterRig and RTTRemoteTaskbar, shows RTTMenuFrame and RTTTaskbar.
        /// Sends pause_streaming to server to stop capture/encode but keep connection.
        /// Does NOT fire OnBackClicked to avoid closing the app.
        /// </summary>
        private async void HandleTaskbarMenuRequest()
        {
            Debug.Log("[RTTRemoteMenuController] Taskbar menu requested - returning to RemoteMenu");

            // Pause streaming on server (stop capture/encode, keep connection)
            if (_viewModel != null)
            {
                await _viewModel.PauseStreamingAsync();
            }

            // Hide RTTRemoteTaskbar
            if (_remoteTaskbar != null)
            {
                _remoteTaskbar.Hide();
            }

            // Hide ClusterRig
            if (_clusterRig != null)
            {
                _clusterRig.gameObject.SetActive(false);
            }

            // Restore zoom distance to pre-remote value so main menu panels are unaffected
            RestoreZoomDistance();

            // Show RTTMenuFrame and RTTTaskbar
            ShowMainMenuAndTaskbar();

            // Note: Do NOT fire OnBackClicked here - it would close the app
            // OnBackClicked is only for when user actually wants to exit RemoteDesktop
        }



        /// <summary>
        /// Resume streaming from RTTRemoteMenu overlay.
        /// Hides menu and taskbar, shows ClusterRig and RTTRemoteTaskbar again.
        /// </summary>
        public void ResumeStreaming()
        {
            Debug.Log("[RTTRemoteMenuController] Resuming streaming");

            // Hide RTTMenuFrame
            RTTMenuFrame menuFrame = RTTMenuFrame.PrimaryInstance;
            if (menuFrame != null)
            {
                menuFrame.Hide();
            }

            // Hide RTTTaskbar
            RTTTaskbar taskbar = RTTTaskbar.Instance;
            if (taskbar != null)
            {
                taskbar.Hide();
            }

            // Re-apply remote depth setting (zoom was restored for menu display)
            ReapplyRemoteDepth();

            // Show ClusterRig
            if (_clusterRig != null)
            {
                _clusterRig.gameObject.SetActive(true);
            }

            // Show RTTRemoteTaskbar
            if (_remoteTaskbar != null)
            {
                _remoteTaskbar.Show();
                _remoteTaskbar.OnMenuDismissed(); // Reset back button state
            }
        }

        /// <summary>
        /// Create RemoteAudioPlayer dynamically for desktop audio playback.
        /// Auto-subscribes to OnRemoteAudioTrackReceived from ViewModel.
        ///
        /// Audio routing on the wire:
        ///   - Host primary path is raw PCM16 over the audio DataChannel (SIPSorceryStreamer.Audio.cs)
        ///   - Opus RTP is negotiated in SDP for fallback only when the DC is unavailable
        ///   - Media relay (WebSocket fallback) also delivers raw PCM16 via the 0xF2 channel
        /// Therefore the DC player is the actual primary sink; the RTP player is wired up so
        /// the Unity WebRTC decoder is pre-allocated, but muted because no Opus RTP traffic
        /// arrives in the normal case.
        /// </summary>
        private void CreateRemoteAudioPlayer()
        {
            CleanupRemoteAudioPlayer();

            // 1. DC Player (Primary) - receives raw PCM16 from the host DataChannel.
            // Bypasses Opus encode/decode + libwebrtc NetEQ jitter for ~20-40ms lower latency.
            var dcAudioObj = new GameObject("DataChannelAudioPlayer");
            dcAudioObj.transform.SetParent(transform, false);
            dcAudioObj.AddComponent<AudioSource>();
            _dcAudioPlayer = dcAudioObj.AddComponent<DataChannelAudioPlayer>();
            _dcAudioPlayer.StartPlayback();
            _dcAudioPlayer.SetMute(false); // Primary: unmuted

            // 2. RTP Player (Fallback) - kept allocated in case the host falls back to Opus RTP,
            // but muted by default because the host sends nothing via RTP while DC is open.
            var rtpAudioObj = new GameObject("RemoteAudioPlayer");
            rtpAudioObj.transform.SetParent(transform, false);
            rtpAudioObj.AddComponent<AudioSource>();
            _audioPlayer = rtpAudioObj.AddComponent<RemoteAudioPlayer>();
            _audioPlayer.SetMute(true); // Muted: only unmuted if host actually sends Opus RTP

            if (_viewModel != null)
            {
                _viewModel.OnDCAudioData += HandleDCAudioData;
                _viewModel.OnRemoteAudioTrackReceived += _audioPlayer.SetTrack;
                _viewModel.OnMediaRelayStateChanged += HandleMediaRelayStateChanged;

                var cachedTrack = _viewModel.CachedAudioTrack;
                if (cachedTrack != null)
                {
                    Debug.Log("[RTTRemoteMenuController] Cached audio track wired to RTP player (kept muted; DC is primary)");
                    _audioPlayer.SetTrack(cachedTrack);
                }
            }

            Debug.Log("[RTTRemoteMenuController] Created audio players (DC primary unmuted, RTP fallback muted)");
        }

        private void HandleDCAudioData(byte[] data)
        {
            if (_dcAudioPlayer == null) return;

            // Both code paths deliver raw PCM16:
            //   - Normal WebRTC: host sends 48kHz/2ch/PCM16 over the audio DataChannel
            //   - Media relay:   host sends raw PCM16 framed as 0xF2 binary WebSocket messages
            // In neither case does the host send Opus over the DataChannel, so the Opus
            // decoder path is dead code that produced the "cách cách" artifacts when it
            // tried to decode PCM16 bytes as Opus.
            _dcAudioPlayer.OnPCMFrame(data, 0, data.Length);
        }

        private void HandleMediaRelayStateChanged(bool active)
        {
            _mediaRelayActive = active;
            // DC is primary in both modes; nothing to toggle here. Kept as a no-op so existing
            // event subscribers don't break and the diagnostic log still records relay state.
            Debug.Log($"[RTTRemoteMenuController] Media relay {(active ? "active" : "stopped")}: DC audio remains primary");
        }

        /// <summary>
        /// Cleanup remote audio player.
        /// </summary>
        private void CleanupRemoteAudioPlayer()
        {
            // Cleanup RTP audio player (via dedicated Audio PC)
            if (_audioPlayer != null)
            {
                if (_viewModel != null)
                {
                    _viewModel.OnRemoteAudioTrackReceived -= _audioPlayer.SetTrack;
                }

                _audioPlayer.StopAudio();
                Destroy(_audioPlayer.gameObject);
                _audioPlayer = null;
            }

            // Cleanup DataChannel audio player (fallback, muted)
            if (_dcAudioPlayer != null)
            {
                if (_viewModel != null)
                {
                    _viewModel.OnDCAudioData -= HandleDCAudioData;
                    _viewModel.OnMediaRelayStateChanged -= HandleMediaRelayStateChanged;
                }

                _dcAudioPlayer.StopAudio();
                Destroy(_dcAudioPlayer.gameObject);
                _dcAudioPlayer = null;
            }
        }

        /// <summary>
        /// Cleanup remote taskbar.
        /// </summary>
        private void CleanupRemoteTaskbar()
        {
            if (_remoteTaskbar != null)
            {
                _remoteTaskbar.OnMenuRequested -= HandleTaskbarMenuRequest;

                Destroy(_remoteTaskbar.gameObject);
                _remoteTaskbar = null;
            }
        }

        /// <summary>
        /// Handle server setup progress updates.
        /// NOTE: Progress is now shown on RTTRemoteMenu button text, not overlays.
        /// This handler is kept for future use if needed.
        /// </summary>
        private void HandleServerSetupProgress(int progress)
        {
            // Progress is handled by RTTRemoteMenu button text
            // Overlays are no longer used in the new flow
        }

        /// <summary>
        /// Handle per-monitor ICE progress updates.
        /// NOTE: Progress is now shown on RTTRemoteMenu button text, not overlays.
        /// This handler is kept for future use if needed.
        /// </summary>
        private void HandleMonitorIceProgress(Dictionary<int, int> progressDict)
        {
            // Progress is handled by RTTRemoteMenu button text
            // Overlays are no longer used in the new flow
        }

        /// <summary>
        /// Handle all monitors ready.
        /// NOTE: In the new flow, streaming auto-starts and ClusterRig is created
        /// when HandleStreamingStateChanged is called.
        /// </summary>
        private void HandleAllMonitorsReady()
        {
            // Streaming will auto-start via ConnectionViewModel
            // ClusterRig will be created when IsStreaming becomes true
            Debug.Log("[RTTRemoteMenuController] All monitors ready - waiting for streaming to start");
        }

        /// <summary>
        /// Handle streaming state change - create ClusterRig when streaming starts.
        /// This is the NEW flow: ClusterRig is created only when streaming is ready,
        /// not when START is clicked. This avoids showing default/sample textures.
        /// </summary>
        private void HandleStreamingStateChanged(bool isStreaming)
        {
            _isStreamingActive = isStreaming;

            if (isStreaming && _pendingConfig != null)
            {
                // Create ClusterRig now that streaming is ready
                CreateClusterRigForStreaming(_pendingConfig);
                _pendingConfig = null;
            }

            // Activate/deactivate remote input forwarding (BT mouse/keyboard)
            if (isStreaming)
            {
                if (_inputBridge == null)
                    _inputBridge = gameObject.AddComponent<VRWorkspace.VRInput.RemoteInputBridge>();
                _inputBridge.SetActive(true);
            }
            else
            {
                if (_inputBridge != null)
                    _inputBridge.SetActive(false);
            }
        }

        /// <summary>
        /// Create ClusterRig when streaming is ready.
        /// No progress overlays or sample textures - video will be applied directly.
        /// ClusterRig is parented inside RTTMenu for synchronized positioning.
        /// </summary>
        private void CreateClusterRigForStreaming(StreamingConfig config)
        {
            Debug.Log($"[RTTRemoteMenuController] Creating ClusterRig for streaming: {config.monitors} monitors");

            // Get or create ClusterRig via RemoteConnectionPipeline
            _clusterRig = connectionPipeline.GetOrCreateClusterRig();
            if (_clusterRig == null)
            {
                Debug.LogError("[RTTRemoteMenuController] Failed to create ClusterRig!");
                return;
            }

            // Parent ClusterRig into RTTMenu BEFORE building
            // This enables parent-origin positioning for synchronized distance with menus
            RTTMenu menu = RTTMenu.Instance;
            if (menu != null)
            {
                menu.SetClusterRig(_clusterRig);
            }
            else
            {
                Debug.LogWarning("[RTTRemoteMenuController] RTTMenu.Instance is null, ClusterRig not parented");
            }

            // Build cluster with the specified monitor count (skip sample textures)
            _clusterRig.BuildWithPanelCount(config.monitors, skipSampleTextures: true);

            // Auto-apply style based on monitor selection:
            // Ultrawide/Super Ultrawide → Curved Surround, Standard (1/2/3) → Flat Planar
            if (_remoteMenuInstance != null)
            {
                bool isCurvedSurround = _remoteMenuInstance.IsUltrawide;
                _clusterRig.SetStyle(isCurvedSurround);
                Debug.Log($"[RTTRemoteMenuController] Applied style: {(isCurvedSurround ? "Curved Surround (Ultrawide)" : "Flat Planar (Standard)")}");

                // Set ultrawide panel aspect ratio if needed
                if (isCurvedSurround)
                {
                    _clusterRig.SetUltrawideAspect(config.monitorType);
                }
            }

            // Create remote audio player for desktop audio streaming
            CreateRemoteAudioPlayer();

            // Save current zoom distance before remote mode changes it
            var zoom = VirtualObjectsZoomController.Instance;
            if (zoom != null && _savedZoomDistance < 0f)
            {
                _savedZoomDistance = zoom.CurrentDistance;
                Debug.Log($"[RTTRemoteMenuController] Saved zoom distance: {_savedZoomDistance:F2}m");
            }

            // Create Remote Taskbar that follows ClusterRig
            CreateRemoteTaskbar();

            // Don't hide menu yet — wait until ALL monitors have their first frame decoded.
            // This ensures the user sees desktop content on all panels before the menu disappears.
            // Timeout after 5s to avoid stuck menu if a monitor fails to decode.
            if (_viewModel != null)
            {
                bool firstFrameReceived = false;
                _viewModel.OnAllMonitorsFirstFrame += () =>
                {
                    if (firstFrameReceived) return;
                    firstFrameReceived = true;
                    Debug.Log("[RTTRemoteMenuController] All monitors have first frame — hiding menu");
                    HideMainMenuAndTaskbar();
                };
                // Safety timeout: hide menu after 5s even if some monitors haven't decoded
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    if (!firstFrameReceived)
                    {
                        firstFrameReceived = true;
                        VRWorkspace.Core.MainThreadDispatcher.Enqueue(() =>
                        {
                            Debug.LogWarning("[RTTRemoteMenuController] Timeout waiting for all first frames — hiding menu anyway");
                            HideMainMenuAndTaskbar();
                        });
                    }
                });
            }
            else
            {
                // Fallback: hide immediately if no ViewModel
                HideMainMenuAndTaskbar();
            }

            Debug.Log("[RTTRemoteMenuController] ClusterRig created inside RTTMenu - waiting for first frames before hiding menu");
        }

        /// <summary>
        /// Cleanup all progress overlays.
        /// </summary>
        private void CleanupProgressOverlays()
        {
            foreach (var overlay in _progressOverlays)
            {
                if (overlay != null)
                {
                    Destroy(overlay);
                }
            }
            _progressOverlays.Clear();
        }

        /// <summary>
        /// Cleanup mipmap RenderTextures.
        /// </summary>
        private void CleanupMipmapTextures()
        {
            if (_mipmapTextures == null) return;

            foreach (var rt in _mipmapTextures)
            {
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
            }
            _mipmapTextures = null;
        }

        /// <summary>
        /// Handle cursor position updates from server.
        /// </summary>
        private void HandleCursorPosition(int monitorIndex, float u, float v, bool visible, VRWorkspace.Streaming.CursorType cursorType, long cursorId)
        {
            if (_clusterRig == null || _clusterRig.panels == null) return;

            if (!visible || monitorIndex < 0 || monitorIndex >= _clusterRig.panels.Count)
            {
                HideAllCursors();
                _activeCursorPanelIndex = -1;
                return;
            }

            // Hide cursor on previous panel if switching monitors
            if (_activeCursorPanelIndex != monitorIndex && _activeCursorPanelIndex >= 0 && _activeCursorPanelIndex < _clusterRig.panels.Count)
            {
                var oldPanel = _clusterRig.panels[_activeCursorPanelIndex];
                if (oldPanel?.cursor != null)
                    oldPanel.cursor.SetVisible(false);
            }

            var panel = _clusterRig.panels[monitorIndex];
            if (panel == null) return;

            panel.EnsureCursor();
            if (panel.cursor == null) return;

            // Set cursor from cache (if available)
            panel.cursor.SetCursorById(cursorId);

            // Unity V is inverted (0 at bottom, 1 at top)
            float unityV = 1f - v;
            panel.cursor.SetUV(u, unityV, silent: true);
            panel.cursor.SetVisible(true);

            _activeCursorPanelIndex = monitorIndex;
        }

        /// <summary>
        /// Handle cursor image received from server.
        /// Caches the texture for use by cursor rendering.
        /// </summary>
        private void HandleCursorImageReceived(long cursorId, VRWorkspace.Streaming.CursorType cursorType, Texture2D texture, int hotspotX, int hotspotY)
        {
            // Cache the cursor texture
            WorldPanelCursor.CacheCursor(cursorId, texture, hotspotX, hotspotY);

            // If this is the cursor currently being displayed, update it immediately
            if (_activeCursorPanelIndex >= 0 && _activeCursorPanelIndex < _clusterRig?.panels?.Count)
            {
                var panel = _clusterRig.panels[_activeCursorPanelIndex];
                if (panel?.cursor != null && panel.cursor.currentCursorId == cursorId)
                {
                    panel.cursor.SetCursorById(cursorId);
                }
            }
        }

        /// <summary>
        /// Hide cursors on all panels.
        /// </summary>
        private void HideAllCursors()
        {
            if (_clusterRig == null || _clusterRig.panels == null) return;
            foreach (var panel in _clusterRig.panels)
            {
                if (panel?.cursor != null)
                    panel.cursor.SetVisible(false);
            }
        }
        #endregion
    }

}
