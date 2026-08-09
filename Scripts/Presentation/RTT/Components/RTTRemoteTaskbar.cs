using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.Core;
using VRWorkspace.ViewModels;
using VRWorkspace.Streaming;
using VRWorkspace.VRInput;
using VRWorkspace.Media.Core;
using VRWorkspace.Media.UI;
using VRWorkspace.Panel;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// RTTRemoteTaskbar - Taskbar for Remote Desktop streaming mode.
    /// Follows WorldPanelClusterRig and displays streaming metrics.
    ///
    /// Section 1: Back, resolution, FPS, screen settings, light, recenter.
    /// Section 2: Monitor 1-3 (dynamic display)
    /// Section 3: Latency text (replaces WiFi icon)
    /// </summary>
    [RequireComponent(typeof(RTTMiniFrame))]
    public class RTTRemoteTaskbar : MonoBehaviour
    {
        #region Static Instance
        private static RTTRemoteTaskbar _instance;
        public static RTTRemoteTaskbar Instance => _instance;

        /// <summary>
        /// Reset static singleton at the start of each Play session.
        /// RTTRemoteTaskbar assigns _instance in Start() (not Awake), which means a ghost
        /// reference from the previous Play session would survive and block the
        /// duplicate-destroy guard during the 2nd Play onwards.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticInstance()
        {
            _instance = null;
        }
        #endregion

        #region Configuration
        [Header("Icons")]
        [SerializeField] private Sprite iconBack;
        [SerializeField] private Sprite iconLightOn;
        [SerializeField] private Sprite iconLightOff;
        [SerializeField] private Sprite iconRecenter;
        [SerializeField] private Sprite iconZoom;
        [SerializeField] private Sprite iconMonitor;
        [SerializeField] private Sprite iconEnviroment;

        // Dynamic icons for resolution (720, 1080, 1440 p)
        private Dictionary<int, Sprite> _resolutionIcons = new Dictionary<int, Sprite>();

        // Screen icons (screen_1, screen_2, screen_3)
        private Dictionary<int, Sprite> _screenIcons = new Dictionary<int, Sprite>();

        // Screen button states (all ON by default)
        private Dictionary<int, bool> _screenStates = new Dictionary<int, bool>();
        // Dynamic icons for FPS (30, 45, 60)
        private Dictionary<int, Sprite> _fpsIcons = new Dictionary<int, Sprite>();
        #endregion

        #region Events
        public event Action OnMenuRequested;

        #endregion

        #region Private Fields
        private RTTMiniFrame _miniFrame;
        private ConnectionViewModel _viewModel;

        // Section 1 references
        private GameObject _backButton;
        private GameObject _resolutionButton;
        private GameObject _fpsButton;
        private GameObject _lightButton;
        private int _currentResolutionHeight = 1080;
        private int _currentFps = 60;

        // Section 2 references
        private GameObject _screenSettingsButton;
        private List<GameObject> _monitorSlots = new List<GameObject>();

        // Screen settings popup (like Video Player UI Settings)
        private GameObject _screenSettingsFrame;
        private RTTMediaUISettingsPopup _screenSettingsPopup;

        // Section 3 reference
        private TextMeshProUGUI _latencyText;

        // Expansion panel
        private RTTTaskbarExpansion _expansionPanel;

        // ClusterRig reference for panel enable/disable
        private WorldPanelClusterRig _clusterRig;

        private bool _isLightOn = true; // Default ON
        private MediaEnvironmentController _environmentController;

        // Colors
        private readonly Color _cyanColor = new Color(0f, 0.9f, 1f);
        private readonly Color _purpleColor = new Color(0.9f, 0.3f, 1f);
        #endregion

        #region Lifecycle
        private void Awake()
        {
            _miniFrame = GetComponent<RTTMiniFrame>();
            LoadIcons();
        }

        private void Start()
        {
            _instance = this;
            StartCoroutine(InitializeAfterFrame());
        }

        private IEnumerator InitializeAfterFrame()
        {
            // Wait for RTTMiniFrame to build UI
            yield return null;
            yield return null;

            // Configure Section 3 to show text instead of WiFi icon
            _miniFrame.SetWifiMode(WifiDisplayMode.Text);

            // Build sections
            AddSection1Buttons();
            AddSection2Slots();
            SetupLatencyDisplay();

            // Initialize environment controller
            _environmentController = MediaEnvironmentController.Instance;
            if (_environmentController != null)
            {
                _environmentController.Initialize();
                _environmentController.OnLightsChanged += HandleLightsChanged;
                _isLightOn = _environmentController.LightsEnabled;
                UpdateLightButtonVisual();
            }

            // Create expansion panel
            CreateExpansionPanel();

            // Create screen settings popup
            CreateScreenSettingsPopup();

            // Bind to ViewModel
            BindToViewModel();

            _miniFrame.MarkDirty();
        }

        private void OnDestroy()
        {
            if (_environmentController != null)
            {
                _environmentController.OnLightsChanged -= HandleLightsChanged;
            }

            if (_instance == this) _instance = null;
            UnbindFromViewModel();
            CleanupExpansionPanel();
            CleanupScreenSettingsPopup();
        }

        private void LateUpdate()
        {
            UpdateMetricsDisplay();
        }
        #endregion

        #region ViewModel Binding
        private void BindToViewModel()
        {
            // TODO: Replace with VContainer [Inject] when LifetimeScopes are wired in scene
#pragma warning disable CS0618
            _viewModel = ServiceLocator.Get<ConnectionViewModel>();
#pragma warning restore CS0618
            if (_viewModel == null)
            {
                Debug.LogWarning("[RTTRemoteTaskbar] ConnectionViewModel not found in ServiceLocator");
                return;
            }

            // Subscribe to current resolution/fps changes (these update when user changes settings)
            _viewModel.CurrentResolutionHeight.OnChanged += OnResolutionChanged;
            _viewModel.CurrentFps.OnChanged += OnFpsChanged;

            // Subscribe to config changes for monitor count
            _viewModel.AppliedConfig.OnChanged += OnConfigChanged;

            // Initial update
            UpdateResolutionIcon(_viewModel.CurrentResolutionHeight.Value);
            UpdateFpsIcon(_viewModel.CurrentFps.Value);
            if (_viewModel.AppliedConfig.Value != null)
            {
                UpdateMonitorSlotVisibility(_viewModel.AppliedConfig.Value.monitors);
            }
        }

        private void UnbindFromViewModel()
        {
            if (_viewModel != null)
            {
                _viewModel.CurrentResolutionHeight.OnChanged -= OnResolutionChanged;
                _viewModel.CurrentFps.OnChanged -= OnFpsChanged;
                _viewModel.AppliedConfig.OnChanged -= OnConfigChanged;
            }
        }

        private void OnResolutionChanged(int resolutionHeight)
        {
            UpdateResolutionIcon(resolutionHeight);

            // Sync expansion panel
            if (_expansionPanel != null)
            {
                _expansionPanel.SetCurrentResolution(resolutionHeight);
            }

            _miniFrame?.MarkDirty();
        }

        private void OnFpsChanged(int fps)
        {
            UpdateFpsIcon(fps);

            // Sync expansion panel
            if (_expansionPanel != null)
            {
                _expansionPanel.SetCurrentFps(fps);
            }

            _miniFrame?.MarkDirty();
        }

        private void OnConfigChanged(StreamingConfig config)
        {
            if (config == null) return;

            // Only update monitor count from config (bitrate/fps come from CurrentBitrateKbps/CurrentFps)
            UpdateMonitorSlotVisibility(config.monitors);
            _miniFrame?.MarkDirty();
        }
        #endregion

        #region Section 1 - Control Buttons
        private void AddSection1Buttons()
        {
            var section1 = _miniFrame.GetSection1Container();
            if (section1 == null) return;

            float buttonSize = _miniFrame.ButtonSize;

            // 1. Back button
            _backButton = CreateIconButton(section1, iconBack, "Back", _cyanColor, buttonSize, OnBackClicked);

            // 2. Resolution button (clickable to show expansion panel)
            var defaultResolutionIcon = GetResolutionIcon(_currentResolutionHeight);
            _resolutionButton = CreateIconButton(section1, defaultResolutionIcon, "Resolution", _cyanColor, buttonSize, OnResolutionClicked);

            // 3. FPS button (clickable to show expansion panel)
            var defaultFpsIcon = GetFpsIcon(_currentFps);
            _fpsButton = CreateIconButton(section1, defaultFpsIcon, "FPS", _cyanColor, buttonSize, OnFpsClicked);

            // 4. Screen Settings button (opens screen settings popup)
            _screenSettingsButton = CreateIconButton(section1, iconEnviroment, "ScreenSettings", _cyanColor, buttonSize, OnScreenSettingsClicked);

            // 5. Light toggle
            _lightButton = CreateIconButton(section1, iconLightOn, "Light", _cyanColor, buttonSize, ToggleLights);

            // 6. Recenter
            CreateIconButton(section1, iconRecenter, "Recenter", _cyanColor, buttonSize, RecenterObject);
        }

        private void OnBackClicked()
        {
            Debug.Log("[RTTRemoteTaskbar] Back pressed - showing menu");
            HideExpansionPanel();
            HideScreenSettingsPopup();
            OnMenuRequested?.Invoke();
            _miniFrame.MarkDirty();
        }

        private void OnResolutionClicked()
        {
            if (_expansionPanel == null) return;

            HideScreenSettingsPopup();

            // Toggle behavior: if already showing Resolution options, hide it
            if (_expansionPanel.IsVisible && _expansionPanel.CurrentType == RTTTaskbarExpansion.ExpansionType.Resolution)
            {
                Debug.Log("[RTTRemoteTaskbar] Resolution clicked - hiding expansion panel (toggle)");
                _expansionPanel.Hide();
                UpdateExpansionTriggerButtonColors(null);
            }
            else
            {
                // Show resolution options (will auto-close if showing FPS options)
                Debug.Log("[RTTRemoteTaskbar] Resolution clicked - showing expansion panel");
                Vector3? buttonWorldPos = GetButtonWorldPosition(_resolutionButton);
                _expansionPanel.ShowResolutionOptions(_currentResolutionHeight, buttonWorldPos);
                UpdateExpansionTriggerButtonColors(RTTTaskbarExpansion.ExpansionType.Resolution);
            }
            _miniFrame.MarkDirty();
        }

        private void OnFpsClicked()
        {
            if (_expansionPanel == null) return;

            HideScreenSettingsPopup();

            // Toggle behavior: if already showing FPS options, hide it
            if (_expansionPanel.IsVisible && _expansionPanel.CurrentType == RTTTaskbarExpansion.ExpansionType.Fps)
            {
                Debug.Log("[RTTRemoteTaskbar] FPS clicked - hiding expansion panel (toggle)");
                _expansionPanel.Hide();
                UpdateExpansionTriggerButtonColors(null);
            }
            else
            {
                // Show FPS options (will auto-close if showing Resolution options)
                Debug.Log("[RTTRemoteTaskbar] FPS clicked - showing expansion panel");
                Vector3? buttonWorldPos = GetButtonWorldPosition(_fpsButton);
                _expansionPanel.ShowFpsOptions(_currentFps, buttonWorldPos);
                UpdateExpansionTriggerButtonColors(RTTTaskbarExpansion.ExpansionType.Fps);
            }
            _miniFrame.MarkDirty();
        }

        private void OnScreenSettingsClicked()
        {
            if (_screenSettingsFrame == null) return;

            // Hide expansion panel if visible
            HideExpansionPanel();

            // Toggle popup visibility
            bool isVisible = _screenSettingsFrame.activeSelf;
            if (!isVisible)
            {
                PositionPopupAtPrimaryFrame();
            }
            _screenSettingsFrame.SetActive(!isVisible);
            UpdateScreenSettingsButtonAppearance(!isVisible);

            Debug.Log($"[RTTRemoteTaskbar] Screen Settings {(!isVisible ? "opened" : "closed")}");
            _miniFrame.MarkDirty();
        }

        /// <summary>
        /// Position popup centered on and parallel to the primary frame, at default zoom distance.
        /// Uses direction from camera to primary frame (not camera forward) so the popup
        /// stays aligned with the primary frame regardless of where the camera is looking.
        /// </summary>
        private void PositionPopupAtPrimaryFrame()
        {
            if (_screenSettingsFrame == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
            if (primary == null) return;

            // Use default zoom distance for stable positioning regardless of current depth
            var zoom = VirtualObjectsZoomController.Instance;
            float distance = zoom != null ? zoom.DefaultDistance : 2.0f;

            Vector3 camPos = cam.transform.position;
            // Use direction from camera to primary frame (not camera forward)
            // This ensures popup is centered on and parallel to the primary frame
            Vector3 toPrimary = primary.transform.position - camPos;
            toPrimary.y = 0;
            if (toPrimary.sqrMagnitude < 0.001f) toPrimary = cam.transform.forward;
            toPrimary.Normalize();

            Vector3 position = camPos + toPrimary * distance;
            position.y = primary.transform.position.y; // Match primary frame height

            _screenSettingsFrame.transform.position = position;
            _screenSettingsFrame.transform.rotation = Quaternion.LookRotation(toPrimary, Vector3.up);
        }

        /// <summary>
        /// Get world position of a button in the RTT canvas.
        /// Uses RectTransformUtility to get accurate bounds even with layout groups.
        /// </summary>
        private Vector3? GetButtonWorldPosition(GameObject button)
        {
            if (button == null || _miniFrame == null) return null;

            var buttonRT = button.GetComponent<RectTransform>();
            if (buttonRT == null) return null;

            // Get miniframe's canvas to calculate relative bounds
            var miniFrameCanvas = _miniFrame.GetCanvas();
            if (miniFrameCanvas == null) return null;

            var canvasRT = miniFrameCanvas.GetComponent<RectTransform>();
            if (canvasRT == null) return null;

            // Get bounds of button relative to canvas center
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRT, buttonRT);

            // Get miniframe's world size and resolution
            Vector2 worldSize = _miniFrame.GetWorldSize();
            Vector2 resolution = new Vector2(_miniFrame.TotalWidth, _miniFrame.TotalHeight);

            // Convert pixels to meters (relative to miniframe center)
            float pixelToMeter = worldSize.x / resolution.x;
            float localX = bounds.center.x * pixelToMeter;
            float localY = bounds.center.y * pixelToMeter;

            // Transform to world space
            Vector3 localRight = _miniFrame.transform.TransformDirection(Vector3.right);
            Vector3 localUp = _miniFrame.transform.TransformDirection(Vector3.up);

            Vector3 worldPos = _miniFrame.transform.position + localRight * localX + localUp * localY;
            return worldPos;
        }

        /// <summary>
        /// Call this when menu is dismissed to reset any state.
        /// </summary>
        public void OnMenuDismissed()
        {
            // Reserved for future use
        }
        #endregion

        #region Expansion Panel
        private void CreateExpansionPanel()
        {
            // Get RTTToolbar to parent expansion into
            RTTToolbar toolbar = RTTToolbar.Instance;
            if (toolbar == null)
            {
                Debug.LogWarning("[RTTRemoteTaskbar] RTTToolbar not found, expansion positioning may be incorrect");
                // Fallback: create toolbar
                toolbar = RTTToolbar.Create();
            }

            // Create expansion panel inside RTTToolbar
            GameObject expansionObj = new GameObject("RTTTaskbarExpansion");
            expansionObj.transform.SetParent(toolbar.transform, false);

            // Set local position (above taskbar area)
            expansionObj.transform.localPosition = toolbar.GetExpansionLocalPosition();
            expansionObj.transform.localRotation = Quaternion.identity;

            _expansionPanel = expansionObj.AddComponent<RTTTaskbarExpansion>();

            // Subscribe to events
            _expansionPanel.OnResolutionSelected += OnExpansionResolutionSelected;
            _expansionPanel.OnFpsSelected += OnExpansionFpsSelected;

            Debug.Log("[RTTRemoteTaskbar] Expansion panel created in RTTToolbar");
        }

        private void CleanupExpansionPanel()
        {
            if (_expansionPanel != null)
            {
                _expansionPanel.OnResolutionSelected -= OnExpansionResolutionSelected;
                _expansionPanel.OnFpsSelected -= OnExpansionFpsSelected;

                if (Application.isPlaying)
                    Destroy(_expansionPanel.gameObject);
                else
                    DestroyImmediate(_expansionPanel.gameObject);

                _expansionPanel = null;
            }
        }

        private void OnExpansionResolutionSelected(int resH)
        {
            Debug.Log($"[RTTRemoteTaskbar] Resolution selected from expansion: {resH}p");

            // Update ViewModel via UpdateConfigAsync
            if (_viewModel != null)
            {
                // Fire and forget - the async update will trigger OnResolutionChanged
                _ = _viewModel.UpdateConfigAsync(null, resH);
            }

            // Reset trigger button colors (expansion auto-hides after selection)
            UpdateExpansionTriggerButtonColors(null);
            _miniFrame.MarkDirty();
        }

        private void OnExpansionFpsSelected(int fps)
        {
            Debug.Log($"[RTTRemoteTaskbar] FPS selected from expansion: {fps}");

            // Update ViewModel via UpdateConfigAsync
            if (_viewModel != null)
            {
                // Fire and forget - the async update will trigger OnFpsChanged
                _ = _viewModel.UpdateConfigAsync(fps, null);
            }

            // Reset trigger button colors (expansion auto-hides after selection)
            UpdateExpansionTriggerButtonColors(null);
            _miniFrame.MarkDirty();
        }

        /// <summary>
        /// Get the expansion panel reference.
        /// </summary>
        public RTTTaskbarExpansion GetExpansionPanel() => _expansionPanel;

        /// <summary>
        /// Hide the expansion panel if visible.
        /// </summary>
        public void HideExpansionPanel()
        {
            if (_expansionPanel != null && _expansionPanel.IsVisible)
            {
                _expansionPanel.Hide();
                UpdateExpansionTriggerButtonColors(null);
            }
        }

        /// <summary>
        /// Update trigger button colors based on which expansion type is active.
        /// Pass null to reset all buttons to default cyan color.
        /// </summary>
        private void UpdateExpansionTriggerButtonColors(RTTTaskbarExpansion.ExpansionType? activeType)
        {
            // Resolution button
            if (_resolutionButton != null)
            {
                Color color = (activeType == RTTTaskbarExpansion.ExpansionType.Resolution) ? _purpleColor : _cyanColor;
                VRButtonFactory.SetBareIconButtonGlowColor(_resolutionButton, color);
            }

            // FPS button
            if (_fpsButton != null)
            {
                Color color = (activeType == RTTTaskbarExpansion.ExpansionType.Fps) ? _purpleColor : _cyanColor;
                VRButtonFactory.SetBareIconButtonGlowColor(_fpsButton, color);
            }

        }
        #endregion

        #region Screen Settings Popup
        // Match Video Player sizing: density=1200, aspect=1.065
        private const float POPUP_DENSITY = 1200f;
        private const float POPUP_ASPECT = 1.065f;
        private const float POPUP_PHYS_W = 0.7f; // 0.7m physical width (matches Video Player scale)

        private const float DEFAULT_SCREEN_DEPTH = 0.5f;
        private const float DEFAULT_SCREEN_HEIGHT = 0.5f;
        private const float DEFAULT_SCREEN_SCALE = 0.5f;

        private void CreateScreenSettingsPopup()
        {
            float physW = POPUP_PHYS_W;
            float physH = physW / POPUP_ASPECT;
            float logicalW = Mathf.Round(physW * POPUP_DENSITY);
            float logicalH = Mathf.Round(logicalW / POPUP_ASPECT);

            _screenSettingsFrame = new GameObject("ScreenSettingsPopupFrame");
            // Parent at scene root - immune to VirtualObjects zoom and Toolbar rotation
            _screenSettingsFrame.transform.SetParent(null, false);
            _screenSettingsFrame.layer = LayerMask.NameToLayer("UI");

            var menuFrame = _screenSettingsFrame.AddComponent<RTTMenuFrame>();
            menuFrame.Configure(physW, physH, logicalW);
            menuFrame.SetGlassBackgroundEnabled(false);
            menuFrame.SetFloatingDataEnabled(false);
            menuFrame.SetContentMargins(0, 0, 0, 0);
            menuFrame.ForceInitialize();

            // Set render queue above other panels
            var quad = menuFrame.GetDisplayQuad();
            if (quad?.material != null)
                quad.material.renderQueue = 3200;

            var container = menuFrame.ContentContainer;
            if (container != null)
            {
                // Get font from RTTManager
                var font = RTTManager.Instance?.Font;
                Color primaryColor = new Color(0f, 0.9f, 1f, 1f); // Cyan theme for remote desktop

                _screenSettingsPopup = _screenSettingsFrame.AddComponent<RTTMediaUISettingsPopup>();
                _screenSettingsPopup.Initialize(container, logicalW, logicalH, font, primaryColor,
                    DEFAULT_SCREEN_DEPTH, DEFAULT_SCREEN_HEIGHT, DEFAULT_SCREEN_SCALE, 0.1f);

                WireScreenSettingsEvents();
                LoadScreenSettings();
            }

            _screenSettingsFrame.SetActive(false);
            Debug.Log("[RTTRemoteTaskbar] Screen Settings popup created");
        }

        private void WireScreenSettingsEvents()
        {
            if (_screenSettingsPopup == null) return;

            _screenSettingsPopup.OnCloseRequested += () =>
            {
                _screenSettingsFrame?.SetActive(false);
                UpdateScreenSettingsButtonAppearance(false);
            };

            _screenSettingsPopup.OnUIDepthChanged += (v) =>
            {
                ApplyScreenDepth(v);
                PlayerPrefs.SetFloat("RemoteDesktop_ScreenDepth", v);
                PlayerPrefs.Save();
            };

            _screenSettingsPopup.OnUIHeightChanged += (v) =>
            {
                ApplyScreenHeight(v);
                PlayerPrefs.SetFloat("RemoteDesktop_ScreenHeight", v);
                PlayerPrefs.Save();
            };

            _screenSettingsPopup.OnUIScaleChanged += (v) =>
            {
                ApplyScreenScale(v);
                PlayerPrefs.SetFloat("RemoteDesktop_ScreenScale", v);
                PlayerPrefs.Save();
            };

            _screenSettingsPopup.OnUISettingsReset += () =>
            {
                ApplyScreenDepth(DEFAULT_SCREEN_DEPTH);
                ApplyScreenHeight(DEFAULT_SCREEN_HEIGHT);
                ApplyScreenScale(DEFAULT_SCREEN_SCALE);
                _screenSettingsPopup.SetValues(DEFAULT_SCREEN_DEPTH, DEFAULT_SCREEN_HEIGHT, DEFAULT_SCREEN_SCALE);

                PlayerPrefs.SetFloat("RemoteDesktop_ScreenDepth", DEFAULT_SCREEN_DEPTH);
                PlayerPrefs.SetFloat("RemoteDesktop_ScreenHeight", DEFAULT_SCREEN_HEIGHT);
                PlayerPrefs.SetFloat("RemoteDesktop_ScreenScale", DEFAULT_SCREEN_SCALE);
                PlayerPrefs.Save();

                Debug.Log("[RTTRemoteTaskbar] Screen settings reset to defaults");
            };
        }

        private void ApplyScreenDepth(float v)
        {
            var zoom = VirtualObjectsZoomController.Instance;
            if (zoom == null) return;

            float distance = Mathf.Lerp(zoom.MinDistance, zoom.MaxDistance, v);
            zoom.SetZoomDistance(distance);
        }

        private void ApplyScreenHeight(float v)
        {
            if (_clusterRig != null)
            {
                float heightOffset = (v - 0.5f) * 1.0f; // 0.1 slider = 0.1m
                _clusterRig.verticalOffset = heightOffset;
                _clusterRig.RequestLayout();

                // Move toolbar up/down together with the screen
                RTTToolbar.Instance?.SetHeightOffset(heightOffset);
            }
        }

        private void ApplyScreenScale(float v)
        {
            if (_clusterRig != null)
            {
                float scale = Mathf.Max(0.1f, 0.5f + v);
                _clusterRig.transform.localScale = Vector3.one * scale;

                // Notify toolbar about scale change so it can reposition
                // using the cluster rig's scaled bounds
                RTTToolbar.Instance?.SetActiveClusterRig(_clusterRig);
            }
        }

        private void LoadScreenSettings()
        {
            float depth = PlayerPrefs.GetFloat("RemoteDesktop_ScreenDepth", DEFAULT_SCREEN_DEPTH);
            float height = PlayerPrefs.GetFloat("RemoteDesktop_ScreenHeight", DEFAULT_SCREEN_HEIGHT);
            float scale = PlayerPrefs.GetFloat("RemoteDesktop_ScreenScale", DEFAULT_SCREEN_SCALE);

            _screenSettingsPopup?.SetValues(depth, height, scale);

            // Apply loaded values (deferred to allow ClusterRig to initialize)
            StartCoroutine(ApplyScreenSettingsDeferred(depth, height, scale));
        }

        private IEnumerator ApplyScreenSettingsDeferred(float depth, float height, float scale)
        {
            // Wait for ClusterRig and ZoomController to be ready
            yield return null;
            yield return null;

            ApplyScreenDepth(depth);
            ApplyScreenHeight(height);
            ApplyScreenScale(scale);
        }

        private void CleanupScreenSettingsPopup()
        {
            if (_screenSettingsFrame != null)
            {
                if (Application.isPlaying)
                    Destroy(_screenSettingsFrame);
                else
                    DestroyImmediate(_screenSettingsFrame);

                _screenSettingsFrame = null;
                _screenSettingsPopup = null;
            }
        }

        /// <summary>
        /// Hide the screen settings popup if visible.
        /// </summary>
        public void HideScreenSettingsPopup()
        {
            if (_screenSettingsFrame != null && _screenSettingsFrame.activeSelf)
            {
                _screenSettingsFrame.SetActive(false);
                UpdateScreenSettingsButtonAppearance(false);
            }
        }

        private void UpdateScreenSettingsButtonAppearance(bool isActive)
        {
            if (_screenSettingsButton == null) return;
            Color targetColor = isActive ? _purpleColor : _cyanColor;
            VRButtonFactory.SetBareIconButtonGlowColor(_screenSettingsButton, targetColor);
        }
        #endregion

        #region Section 2 - Screen Toggle Buttons
        private void AddSection2Slots()
        {
            var section2 = _miniFrame.GetSection2Container();
            if (section2 == null) return;

            float buttonSize = _miniFrame.ButtonSize;

            // Screen toggle buttons (dynamic visibility based on monitor count)
            _monitorSlots.Clear();
            _screenStates.Clear();

            for (int i = 0; i < 3; i++)
            {
                int screenNumber = i + 1;
                _screenStates[screenNumber] = true; // All ON by default

                var btn = CreateScreenToggleButton(section2, screenNumber, buttonSize);
                _monitorSlots.Add(btn);

                // Initially hidden (will be shown based on monitor count)
                var cg = btn.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 0f;
            }
        }

        private GameObject CreateScreenToggleButton(Transform parent, int screenNumber, float buttonSize)
        {
            // Get screen icon for this number
            Sprite screenIcon = _screenIcons.TryGetValue(screenNumber, out var icon) ? icon : iconMonitor;

            // Default state is ON (purple color)
            bool isOn = _screenStates.TryGetValue(screenNumber, out var state) ? state : true;
            Color btnColor = isOn ? _purpleColor : _cyanColor;

            int capturedNumber = screenNumber;

            // Create toggle button using VRButtonFactory
            var btn = VRButtonFactory.CreateBareIconButton(
                parent, buttonSize, screenIcon, btnColor,
                () => OnScreenToggleClicked(capturedNumber),
                0.05f, 0.6f
            );

            btn.name = $"ScreenBtn_{screenNumber}";

            // Add CanvasGroup for visibility control
            var cg = btn.GetComponent<CanvasGroup>();
            if (cg == null) cg = btn.AddComponent<CanvasGroup>();

            SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));
            return btn;
        }

        private void OnScreenToggleClicked(int screenNumber)
        {
            bool currentState = IsScreenOn(screenNumber);
            bool newState = !currentState;

            // Restriction Check: Only applied when trying to turn a screen OFF
            if (!newState)
            {
                // Calculate configured and enabled counts
                int configuredCount = 0;
                int enabledCount = 0;

                for (int i = 0; i < _monitorSlots.Count; i++)
                {
                    var slot = _monitorSlots[i];
                    if (slot == null) continue;

                    // Check visibility to determine if configured/active in current mode
                    var cg = slot.GetComponent<CanvasGroup>();
                    bool isConfigured = cg != null && cg.alpha > 0.5f;

                    if (isConfigured)
                    {
                        configuredCount++;
                        if (IsScreenOn(i + 1)) enabledCount++;
                    }
                }

                // Rule 1: Cannot turn off the last monitor
                if (enabledCount <= 1)
                {
                    Debug.Log($"[RTTRemoteTaskbar] Cannot turn off Screen {screenNumber}: It is the last active monitor.");
                    return;
                }

                // Rule 2: Cannot turn off Center Monitor (2) when using 3 Monitors
                if (configuredCount == 3 && screenNumber == 2)
                {
                    Debug.Log($"[RTTRemoteTaskbar] Cannot turn off Screen {screenNumber}: Center monitor is locked in 3-monitor mode.");
                    return;
                }
            }

            // Apply state change
            SetScreenState(screenNumber, newState);
        }

        private void UpdateScreenButtonAppearance(int screenNumber)
        {
            int index = screenNumber - 1;
            if (index < 0 || index >= _monitorSlots.Count) return;

            var btn = _monitorSlots[index];
            if (btn == null) return;

            bool isOn = _screenStates.TryGetValue(screenNumber, out var state) ? state : true;
            Color targetColor = isOn ? _purpleColor : _cyanColor;

            // Update glow color
            VRButtonFactory.SetBareIconButtonGlowColor(btn, targetColor);
        }

        private void UpdateMonitorSlotVisibility(int monitorCount)
        {
            for (int i = 0; i < _monitorSlots.Count; i++)
            {
                var slot = _monitorSlots[i];
                if (slot == null) continue;

                int screenNumber = i + 1;
                bool isVisible = (i < monitorCount);

                var cg = slot.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = isVisible ? 1f : 0f;
                }

                // Reset state to ON when becoming visible
                if (isVisible && !_screenStates.ContainsKey(screenNumber))
                {
                    _screenStates[screenNumber] = true;
                    UpdateScreenButtonAppearance(screenNumber);
                }
            }
        }

        /// <summary>
        /// Get the current state of a screen button.
        /// </summary>
        public bool IsScreenOn(int screenNumber)
        {
            return _screenStates.TryGetValue(screenNumber, out var state) ? state : true;
        }

        /// <summary>
        /// Set the state of a screen button and update panel visibility.
        /// </summary>
        public void SetScreenState(int screenNumber, bool isOn)
        {
            _screenStates[screenNumber] = isOn;
            UpdateScreenButtonAppearance(screenNumber);

            // screenNumber is 1-based, panelIndex is 0-based
            int panelIndex = screenNumber - 1;

            // Update ClusterRig panel visibility
            if (_clusterRig != null)
            {
                _clusterRig.SetPanelEnabled(panelIndex, isOn);
            }

            // Notify server to pause/resume this monitor's stream
            if (_viewModel != null)
            {
                if (isOn)
                {
                    _ = _viewModel.ResumeMonitorAsync(panelIndex);
                }
                else
                {
                    _ = _viewModel.PauseMonitorAsync(panelIndex);
                }
            }
        }
        #endregion

        #region Section 3 - Latency Display
        private void SetupLatencyDisplay()
        {
            // Get network text reference from RTTMiniFrame
            _latencyText = _miniFrame.GetNetworkText();

            if (_latencyText != null)
            {
                _latencyText.text = "-- ms";
                _latencyText.fontSize = 24f;
                _latencyText.textWrappingMode = TextWrappingModes.NoWrap;  // Single line
                _latencyText.overflowMode = TMPro.TextOverflowModes.Overflow;
            }
        }

        private void UpdateLatencyDisplay(double pingMs)
        {
            if (_latencyText == null) return;

            if (pingMs > 0)
            {
                _latencyText.text = $"{Mathf.RoundToInt((float)pingMs)} ms";
                _latencyText.color = GetLatencyColor(pingMs);
            }
            else
            {
                _latencyText.text = "-- ms";
                _latencyText.color = Color.gray;
            }
        }

        private Color GetLatencyColor(double pingMs)
        {
            if (pingMs < 20) return new Color(0.3f, 1f, 0.3f);   // Green - Excellent
            if (pingMs < 50) return new Color(1f, 1f, 0.3f);    // Yellow - Good
            if (pingMs < 100) return new Color(1f, 0.6f, 0.2f); // Orange - Fair
            return new Color(1f, 0.3f, 0.3f);                    // Red - Poor
        }
        #endregion

        #region Metrics Update
        private void UpdateMetricsDisplay()
        {
            if (_viewModel == null) return;

            // Update latency from streaming metrics via ViewModel
            var metrics = _viewModel.GetMetrics();
            if (metrics != null)
            {
                UpdateLatencyDisplay(metrics.CurrentPingMs);
            }
        }

        private void UpdateResolutionIcon(int resH)
        {
            if (_resolutionButton == null) return;

            Sprite icon = GetResolutionIcon(resH);
            SetBareIconButtonIcon(_resolutionButton, icon);

            // Turn off glow since it's just a display (turns purple only when expansion is open)
            VRButtonFactory.SetBareIconButtonGlowColor(_resolutionButton, _cyanColor);

            _currentResolutionHeight = resH;
        }



        private void UpdateFpsIcon(int fps)
        {
            if (fps == _currentFps) return;

            _currentFps = fps;
            var icon = GetFpsIcon(fps);
            if (icon != null)
            {
                SetBareIconButtonIcon(_fpsButton, icon);
            }
        }



        private Sprite GetResolutionIcon(int resH)
        {
            // Try specific icon
            if (_resolutionIcons.TryGetValue(resH, out var icon))
            {
                return icon;
            }

            // Fallback strategy: return closest available icon, or default if none
            return iconMonitor;
        }

        private Sprite GetFpsIcon(int fps)
        {
            // Snap to nearest valid option: 30, 60, 120
            int[] validOptions = { 30, 60, 120 };
            int nearest = validOptions[0];
            int minDiff = Mathf.Abs(fps - nearest);

            foreach (int opt in validOptions)
            {
                int diff = Mathf.Abs(fps - opt);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    nearest = opt;
                }
            }

            if (_fpsIcons.TryGetValue(nearest, out Sprite icon))
            {
                return icon;
            }
            return _fpsIcons.ContainsKey(60) ? _fpsIcons[60] : null;
        }
        #endregion

        #region Light Button
        private void HandleLightsChanged(bool isOn)
        {
            if (_isLightOn == isOn) return;

            _isLightOn = isOn;
            UpdateLightButtonVisual();

            _miniFrame.MarkDirty();
            Debug.Log($"[RTTRemoteTaskbar] Light state synchronized to: {(isOn ? "ON" : "OFF")}");
        }

        private void ToggleLights()
        {
            HideScreenSettingsPopup();
            HideExpansionPanel();
            _environmentController?.ToggleLights();
        }

        private void UpdateLightButtonVisual()
        {
            if (_lightButton == null) return;

            bool isNonDefault = !_isLightOn;
            Color targetColor = isNonDefault ? _purpleColor : _cyanColor;
            Sprite targetIcon = _isLightOn ? iconLightOn : iconLightOff;

            Transform iconTransform = _lightButton.transform.Find("HitArea/Visuals/Content/Icon");
            if (iconTransform != null)
            {
                Image iconImg = iconTransform.GetComponent<Image>();
                if (iconImg != null)
                {
                    if (targetIcon != null)
                    {
                        iconImg.sprite = targetIcon;
                    }

                    iconImg.color = Color.Lerp(targetColor, Color.white, 0.9f);

                    Shadow[] shadows = iconTransform.GetComponents<Shadow>();
                    if (shadows.Length >= 2)
                    {
                        Color glowCol = Color.Lerp(targetColor, Color.white, 0.7f);
                        glowCol.a = 0.4f;
                        shadows[0].effectColor = glowCol;
                        shadows[1].effectColor = glowCol;
                    }
                }
            }

            var hoverController = _lightButton.GetComponentInChildren<VRWorkspace.UI.HoverEffects.HoverEffectController>();
            if (hoverController != null)
            {
                hoverController.SetForceHover(isNonDefault);
            }
        }

        public void SetLight(bool isOn)
        {
            _environmentController?.SetLightsEnabled(isOn);
        }
        #endregion

        private void RecenterObject()
        {
            Debug.Log("[RTTRemoteTaskbar] Recenter clicked");
            Transform fallback = _miniFrame != null ? _miniFrame.GetFollowTarget() : transform;
            StartCoroutine(VirtualObjectsRecenter.RunWithReticleProgress(
                this, fallback, iconRecenter, () => _miniFrame?.MarkDirty()));
        }

        #region Button Factory
        private GameObject CreateIconButton(Transform parent, Sprite icon, string name, Color glowColor, float buttonSize, Action onClick)
        {
            var btn = VRButtonFactory.CreateBareIconButton(
                parent, buttonSize, icon, glowColor,
                () =>
                {
                    onClick?.Invoke();
                    _miniFrame.MarkDirty();
                },
                0.05f, 0.6f
            );

            btn.name = $"Btn_{name}";
            SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));

            return btn;
        }

        /// <summary>
        /// Create a BareIconButton for display-only purposes (no click action).
        /// Icon is positioned at top, value text will be added below.
        /// </summary>
        private GameObject CreateDisplayIconButton(Transform parent, Sprite icon, string name, Color glowColor, float buttonSize)
        {
            var btn = VRButtonFactory.CreateBareIconButton(
                parent, buttonSize, icon, glowColor,
                null,  // No click action - display only
                0.05f, 0.6f
            );

            btn.name = $"Display_{name}";
            SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));

            return btn;
        }

        private GameObject CreateIconInButton(GameObject button, Sprite icon, float buttonSize)
        {
            if (icon == null) return null;

            float iconSize = buttonSize * 0.5f;

            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(button.transform, false);

            RectTransform rt = iconObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(iconSize, iconSize);
            rt.anchorMin = new Vector2(0.5f, 0.6f);
            rt.anchorMax = new Vector2(0.5f, 0.6f);
            rt.anchoredPosition = Vector2.zero;

            Image img = iconObj.AddComponent<Image>();
            img.sprite = icon;
            img.color = Color.Lerp(_cyanColor, Color.white, 0.9f);
            img.raycastTarget = false;

            SetLayerRecursively(iconObj, LayerMask.NameToLayer("UI"));
            return iconObj;
        }

        private TextMeshProUGUI GetOrCreateValueText(GameObject button, string initialText)
        {
            // Check if text already exists
            var existingText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (existingText != null)
            {
                existingText.text = initialText;
                return existingText;
            }

            GameObject textObj = new GameObject("ValueText");
            textObj.transform.SetParent(button.transform, false);

            RectTransform rt = textObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0.45f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = initialText;
            tmp.fontSize = 20f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            SetLayerRecursively(textObj, LayerMask.NameToLayer("UI"));
            return tmp;
        }

        private void SetBareIconButtonColor(GameObject container, Color color)
        {
            if (container == null) return;

            // Find icon in VRButtonFactory structure: container > HitArea > Visuals > Content > Icon
            Transform iconTransform = container.transform.Find("HitArea/Visuals/Content/Icon");
            if (iconTransform == null) return;

            Image iconImg = iconTransform.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.color = Color.Lerp(color, Color.white, 0.9f);

                Shadow[] shadows = iconTransform.GetComponents<Shadow>();
                if (shadows.Length >= 2)
                {
                    Color glowCol = Color.Lerp(color, Color.white, 0.7f);
                    glowCol.a = 0.4f;
                    shadows[0].effectColor = glowCol;
                    shadows[1].effectColor = glowCol;
                }
            }
        }

        /// <summary>
        /// Update the icon sprite of a BareIconButton.
        /// Structure: Wrapper > HitArea > Visuals > Content > Icon
        /// </summary>
        private void SetBareIconButtonIcon(GameObject container, Sprite newIcon)
        {
            if (container == null || newIcon == null) return;

            // Find icon in VRButtonFactory structure: container > HitArea > Visuals > Content > Icon
            Transform iconTransform = container.transform.Find("HitArea/Visuals/Content/Icon");

            if (iconTransform == null) return;

            Image iconImg = iconTransform.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.sprite = newIcon;
            }
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            if (layer == -1) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// Set the follow target (typically WorldPanelClusterRig).
        /// </summary>
        public void SetFollowTarget(Transform target)
        {
            if (_miniFrame != null && target != null)
            {
                _miniFrame.SetFollowTarget(target);

                // Try to get ClusterRig reference for panel enable/disable
                if (_clusterRig == null && target != null)
                {
                    _clusterRig = target.GetComponent<WorldPanelClusterRig>();
                }
            }
        }

        /// <summary>
        /// Set the follow target (WorldPanelClusterRig overload).
        /// Also stores reference for panel enable/disable functionality.
        /// </summary>
        public void SetFollowTarget(WorldPanelClusterRig clusterRig)
        {
            _clusterRig = clusterRig;
            if (clusterRig != null)
            {
                SetFollowTarget(clusterRig.transform);
            }
        }

        /// <summary>
        /// Set the ClusterRig reference for panel enable/disable functionality,
        /// without changing the follow target.
        /// Also notifies RTTToolbar for dynamic scaled target height calculation.
        /// </summary>
        public void SetClusterRig(WorldPanelClusterRig clusterRig)
        {
            _clusterRig = clusterRig;
            RTTToolbar.Instance?.SetActiveClusterRig(clusterRig);
        }

        /// <summary>
        /// Hide the taskbar.
        /// </summary>
        public void Hide()
        {
            if (_miniFrame != null)
                _miniFrame.Hide();
        }

        /// <summary>
        /// Show the taskbar.
        /// </summary>
        public void Show()
        {
            if (_miniFrame != null)
            {
                _miniFrame.Show();

                // Re-register with RTTToolbar as the active taskbar
                if (RTTToolbar.Instance != null)
                {
                    RTTToolbar.Instance.SetActiveTaskbar(_miniFrame);
                    RTTToolbar.Instance.SetActiveClusterRig(_clusterRig);

                    // Restore height offset from saved settings
                    float height = PlayerPrefs.GetFloat("RemoteDesktop_ScreenHeight", DEFAULT_SCREEN_HEIGHT);
                    RTTToolbar.Instance.SetHeightOffset((height - 0.5f) * 1.0f);
                }
            }
        }
        #endregion

        #region Icons
        private void LoadIcons()
        {
            // Static icons
            if (iconBack == null) iconBack = LoadIcon("back");
            if (iconLightOn == null) iconLightOn = LoadIcon("light_on");
            if (iconLightOff == null) iconLightOff = LoadIcon("light_off");
            if (iconRecenter == null) iconRecenter = LoadIcon("recenter");
            if (iconZoom == null) iconZoom = LoadIcon("zoom");
            if (iconMonitor == null) iconMonitor = LoadIcon("resolution");
            if (iconEnviroment == null) iconEnviroment = LoadIcon("enviroment");

            // Dynamic resolution icons (720, 1080, 1440 p)
            int[] resOptions = { 720, 1080, 1440 };
            foreach (int res in resOptions)
            {
                var icon = Resources.Load<Sprite>($"icon_{res}p");
                if (icon != null)
                {
                    _resolutionIcons[res] = icon;
                }
            }



            // Dynamic FPS icons (30, 45, 60, 120)
            // Naming: icon_{fps}_fps (e.g., icon_30_fps, icon_60_fps)
            int[] fpsOptions = { 30, 60, 120 };
            foreach (int fps in fpsOptions)
            {
                var icon = LoadIcon($"{fps}_fps");
                if (icon != null)
                {
                    _fpsIcons[fps] = icon;
                }
            }

            // Screen icons (screen_1, screen_2, screen_3)
            for (int i = 1; i <= 3; i++)
            {
                var icon = Resources.Load<Sprite>($"icon_screen_{i}");
                if (icon != null)
                {
                    _screenIcons[i] = icon;
                }
            }

            Debug.Log($"[RTTRemoteTaskbar] Icons loaded - Back:{iconBack != null}, LightOn:{iconLightOn != null}, LightOff:{iconLightOff != null}, Recenter:{iconRecenter != null}, Zoom:{iconZoom != null}, Monitor:{iconMonitor != null}");
            Debug.Log($"[RTTRemoteTaskbar] Loaded icons - Resolution: {_resolutionIcons.Count}, FPS: {_fpsIcons.Count}/3, Screens: {_screenIcons.Count}/3");
        }

        private static Sprite LoadIcon(string name)
        {
            var sprite = Resources.Load<Sprite>($"icon_{name}");
            if (sprite == null)
            {
                Debug.LogWarning($"[RTTRemoteTaskbar] Failed to load icon: icon_{name} from Resources");
            }
            return sprite;
        }
        #endregion
    }

}
