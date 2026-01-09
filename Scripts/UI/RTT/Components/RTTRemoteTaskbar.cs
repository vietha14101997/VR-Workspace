using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.Core;
using VRWorkspace.ViewModels;
using VRWorkspace.Streaming;

/// <summary>
/// RTTRemoteTaskbar - Taskbar for Remote Desktop streaming mode.
/// Follows WorldPanelClusterRig and displays streaming metrics.
///
/// Section 1: Back, Bitrate (display), FPS (display), Passthrough, Recenter
/// Section 2: Zoom (display), Monitor 1-3 (dynamic display)
/// Section 3: Latency text (replaces WiFi icon)
/// </summary>
[RequireComponent(typeof(RTTMiniFrame))]
public class RTTRemoteTaskbar : MonoBehaviour
{
    #region Static Instance
    private static RTTRemoteTaskbar _instance;
    public static RTTRemoteTaskbar Instance => _instance;
    #endregion

    #region Configuration
    [Header("Icons")]
    [SerializeField] private Sprite iconBack;
    [SerializeField] private Sprite iconPassthrough;
    [SerializeField] private Sprite iconRecenter;
    [SerializeField] private Sprite iconZoom;
    [SerializeField] private Sprite iconMonitor;
    #endregion

    #region Events
    public event Action OnMenuRequested;
    public event Action OnDisconnectRequested;
    #endregion

    #region Private Fields
    private RTTMiniFrame _miniFrame;
    private ConnectionViewModel _viewModel;

    // Section 1 references
    private GameObject _backButton;
    private GameObject _bitrateButton;
    private GameObject _fpsButton;
    private GameObject _passthroughButton;
    private TextMeshProUGUI _bitrateText;
    private TextMeshProUGUI _fpsText;

    // Section 2 references
    private GameObject _zoomButton;
    private List<GameObject> _monitorSlots = new List<GameObject>();

    // Section 3 reference
    private TextMeshProUGUI _latencyText;

    // State
    private bool _isPassthroughOn = false;

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

        // Bind to ViewModel
        BindToViewModel();

        // Sync passthrough state
        SyncPassthroughWithModeController();

        _miniFrame.MarkDirty();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        UnbindFromViewModel();
    }

    private void LateUpdate()
    {
        UpdateMetricsDisplay();
    }
    #endregion

    #region ViewModel Binding
    private void BindToViewModel()
    {
        _viewModel = ServiceLocator.Get<ConnectionViewModel>();
        if (_viewModel == null)
        {
            Debug.LogWarning("[RTTRemoteTaskbar] ConnectionViewModel not found in ServiceLocator");
            return;
        }

        // Subscribe to config changes
        _viewModel.AppliedConfig.OnChanged += OnConfigChanged;

        // Initial update
        if (_viewModel.AppliedConfig.Value != null)
        {
            OnConfigChanged(_viewModel.AppliedConfig.Value);
        }
    }

    private void UnbindFromViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.AppliedConfig.OnChanged -= OnConfigChanged;
        }
    }

    private void OnConfigChanged(StreamingConfig config)
    {
        if (config == null) return;

        UpdateBitrateDisplay(config.bitrateKbps);
        UpdateFpsDisplay(config.fps);
        UpdateMonitorSlotVisibility(config.monitors);

        _miniFrame.MarkDirty();
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

        // 2. Bitrate display button
        _bitrateButton = CreateDisplayButton(section1, "Bitrate", buttonSize);
        _bitrateText = GetOrCreateValueText(_bitrateButton, "-- Mbps");

        // 3. FPS display button
        _fpsButton = CreateDisplayButton(section1, "FPS", buttonSize);
        _fpsText = GetOrCreateValueText(_fpsButton, "--");

        // 4. Passthrough toggle
        _passthroughButton = CreateIconButton(section1, iconPassthrough, "Passthrough", _cyanColor, buttonSize, TogglePassthrough);

        // 5. Recenter
        CreateIconButton(section1, iconRecenter, "Recenter", _cyanColor, buttonSize, RecenterObject);
    }

    private void OnBackClicked()
    {
        Debug.Log("[RTTRemoteTaskbar] Back pressed - showing menu");
        OnMenuRequested?.Invoke();
        _miniFrame.MarkDirty();
    }

    /// <summary>
    /// Call this when menu is dismissed to reset any state.
    /// </summary>
    public void OnMenuDismissed()
    {
        // Reserved for future use
    }
    #endregion

    #region Section 2 - Zoom & Monitor Slots
    private void AddSection2Slots()
    {
        var section2 = _miniFrame.GetSection2Container();
        if (section2 == null) return;

        float buttonSize = _miniFrame.ButtonSize;

        // Slot 0: Zoom (always visible)
        _zoomButton = CreateDisplayButton(section2, "Zoom", buttonSize);
        var zoomIcon = CreateIconInButton(_zoomButton, iconZoom, buttonSize);
        GetOrCreateValueText(_zoomButton, "1.0x");

        // Slots 1-3: Monitor indicators (dynamic visibility)
        _monitorSlots.Clear();
        for (int i = 0; i < 3; i++)
        {
            var slot = CreateMonitorSlot(section2, i + 1, buttonSize);
            _monitorSlots.Add(slot);

            // Initially hidden
            var cg = slot.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 0f;
        }
    }

    private GameObject CreateMonitorSlot(Transform parent, int monitorNumber, float buttonSize)
    {
        GameObject slot = new GameObject($"MonitorSlot_{monitorNumber}");
        slot.transform.SetParent(parent, false);

        RectTransform rt = slot.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(buttonSize, buttonSize);

        CanvasGroup cg = slot.AddComponent<CanvasGroup>();

        // Background with glow effect
        CreateIconInButton(slot, iconMonitor, buttonSize);

        // Monitor number text
        var numberText = GetOrCreateValueText(slot, monitorNumber.ToString());
        numberText.fontSize = 32f;
        numberText.fontStyle = FontStyles.Bold;

        SetLayerRecursively(slot, LayerMask.NameToLayer("UI"));
        return slot;
    }

    private void UpdateMonitorSlotVisibility(int monitorCount)
    {
        for (int i = 0; i < _monitorSlots.Count; i++)
        {
            var slot = _monitorSlots[i];
            if (slot == null) continue;

            var cg = slot.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = (i < monitorCount) ? 1f : 0f;
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

            // Update FPS from effective FPS if available
            if (metrics.EffectiveFps > 0)
            {
                if (_fpsText != null)
                {
                    _fpsText.text = $"{Mathf.RoundToInt(metrics.EffectiveFps)}";
                }
            }
        }
    }

    private void UpdateBitrateDisplay(int bitrateKbps)
    {
        if (_bitrateText == null) return;

        int mbps = bitrateKbps / 1000;
        _bitrateText.text = $"{mbps} Mbps";
    }

    private void UpdateFpsDisplay(int fps)
    {
        if (_fpsText == null) return;
        _fpsText.text = $"{fps}";
    }
    #endregion

    #region Passthrough
    private void TogglePassthrough()
    {
        _isPassthroughOn = !_isPassthroughOn;
        Debug.Log($"[RTTRemoteTaskbar] Passthrough: {(_isPassthroughOn ? "ON" : "OFF")}");

        UpdatePassthroughButtonColor();

        var modeController = FindObjectOfType<ModeController>();
        if (modeController != null)
        {
            modeController.SetMode(_isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace);
        }

        _miniFrame.MarkDirty();
    }

    private void SyncPassthroughWithModeController()
    {
        var modeController = FindObjectOfType<ModeController>();
        if (modeController != null)
        {
            _isPassthroughOn = modeController.mode == ViewMode.RealWorld;
            UpdatePassthroughButtonColor();
        }
    }

    private void UpdatePassthroughButtonColor()
    {
        if (_passthroughButton == null) return;

        Color targetColor = _isPassthroughOn ? _purpleColor : _cyanColor;
        SetBareIconButtonColor(_passthroughButton, targetColor);
    }

    public void SetPassthrough(bool isOn)
    {
        if (_isPassthroughOn != isOn)
        {
            _isPassthroughOn = isOn;
            UpdatePassthroughButtonColor();

            var modeController = FindObjectOfType<ModeController>();
            if (modeController != null)
            {
                modeController.SetMode(_isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace);
            }

            _miniFrame.MarkDirty();
        }
    }
    #endregion

    #region Recenter
    private void RecenterObject()
    {
        Debug.Log("[RTTRemoteTaskbar] Recenter clicked");
        StartCoroutine(RecenterRoutine());
    }

    private IEnumerator RecenterRoutine()
    {
        VRGazeReticle reticle = VRGazeReticle.Instance;
        if (reticle == null) reticle = FindObjectOfType<VRGazeReticle>();

        if (reticle != null)
        {
            reticle.EnterRecenterMode(iconRecenter);
        }

        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            if (reticle != null)
            {
                reticle.UpdateRecenterProgress(progress);
            }

            yield return null;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            RecenterAllVirtualObjects(cam);
        }

        if (reticle != null)
        {
            reticle.ExitRecenterMode();
        }

        _miniFrame.MarkDirty();
        Debug.Log("[RTTRemoteTaskbar] Recenter complete.");
    }

    private void RecenterAllVirtualObjects(Camera cam)
    {
        GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");
        if (virtualObjectsParent == null)
        {
            Debug.LogWarning("[RTTRemoteTaskbar] VirtualObjects parent not found");
            RecenterFollowTarget(cam);
            return;
        }

        // Find primary menu frame as pivot
        RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
        if (primary == null)
        {
            RecenterFollowTarget(cam);
            return;
        }

        Vector3 pivotPos = primary.transform.position;
        Quaternion pivotRot = primary.transform.rotation;

        List<Transform> children = new List<Transform>();
        List<Vector3> relativePositions = new List<Vector3>();
        List<Quaternion> relativeRotations = new List<Quaternion>();

        foreach (Transform child in virtualObjectsParent.transform)
        {
            children.Add(child);
            Vector3 relPos = Quaternion.Inverse(pivotRot) * (child.position - pivotPos);
            relativePositions.Add(relPos);
            Quaternion relRot = Quaternion.Inverse(pivotRot) * child.rotation;
            relativeRotations.Add(relRot);
        }

        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(
            new Vector2(pivotPos.x, pivotPos.z),
            new Vector2(camPos.x, camPos.z)
        );

        Vector3 newPivotPos = camPos + camForward * hDist;
        newPivotPos.y = pivotPos.y;
        Quaternion newPivotRot = Quaternion.LookRotation(camForward);

        for (int i = 0; i < children.Count; i++)
        {
            Transform child = children[i];
            child.position = newPivotPos + newPivotRot * relativePositions[i];
            child.rotation = newPivotRot * relativeRotations[i];
        }
    }

    private void RecenterFollowTarget(Camera cam)
    {
        var followTarget = _miniFrame.GetFollowTarget();
        if (followTarget != null)
        {
            RecenterTransform(followTarget, cam);
        }
    }

    private void RecenterTransform(Transform target, Camera cam)
    {
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 currentPos = target.position;
        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(new Vector2(currentPos.x, currentPos.z), new Vector2(camPos.x, camPos.z));

        Vector3 newPos = camPos + camForward * hDist;
        newPos.y = currentPos.y;

        target.position = newPos;
        target.rotation = Quaternion.LookRotation(camForward);
    }
    #endregion

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

    private GameObject CreateDisplayButton(Transform parent, string name, float buttonSize)
    {
        GameObject btn = new GameObject($"Display_{name}");
        btn.transform.SetParent(parent, false);

        RectTransform rt = btn.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(buttonSize, buttonSize);

        // Add background for visibility
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(btn.transform, false);

        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0.1f, 0.1f, 0.15f, 0.5f);
        bgImg.raycastTarget = false;

        RectTransform bgRt = bgObj.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

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
        Transform iconTransform = null;

        // Find icon in VRButtonFactory structure
        foreach (Transform child in container.transform)
        {
            var hitArea = child.Find("HitArea");
            if (hitArea != null)
            {
                var visuals = hitArea.Find("Visuals");
                if (visuals != null)
                {
                    var content = visuals.Find("Content");
                    if (content != null)
                    {
                        iconTransform = content.Find("Icon");
                        break;
                    }
                }
            }
        }

        if (iconTransform == null) return;

        Image iconImg = iconTransform.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.color = Color.Lerp(color, Color.white, 0.9f);

            Shadow[] shadows = iconTransform.GetComponents<Shadow>();
            if (shadows.Length >= 4)
            {
                Color glowCol = Color.Lerp(color, Color.white, 0.7f);
                glowCol.a = 0.4f;
                shadows[0].effectColor = glowCol;
                shadows[1].effectColor = glowCol;

                Color bloomCol = Color.Lerp(color, Color.white, 0.8f);
                bloomCol.a = 0.15f;
                shadows[2].effectColor = bloomCol;
                shadows[3].effectColor = bloomCol;
            }
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
    public bool IsPassthroughOn => _isPassthroughOn;

    /// <summary>
    /// Set the follow target (typically WorldPanelClusterRig).
    /// </summary>
    public void SetFollowTarget(Transform target)
    {
        if (_miniFrame != null && target != null)
        {
            _miniFrame.SetFollowTarget(target);
        }
    }

    /// <summary>
    /// Set the follow target (WorldPanelClusterRig overload).
    /// </summary>
    public void SetFollowTarget(WorldPanelClusterRig clusterRig)
    {
        if (clusterRig != null)
        {
            SetFollowTarget(clusterRig.transform);
        }
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
            _miniFrame.Show();
    }
    #endregion

    #region Icons
    private void LoadIcons()
    {
        if (iconBack == null) iconBack = LoadIcon("back");
        if (iconPassthrough == null) iconPassthrough = LoadIcon("passthrough");
        if (iconRecenter == null) iconRecenter = LoadIcon("recenter");
        if (iconZoom == null) iconZoom = LoadIcon("zoom");
        if (iconMonitor == null) iconMonitor = LoadIcon("monitor");

        Debug.Log($"[RTTRemoteTaskbar] Icons loaded - Back:{iconBack != null}, Passthrough:{iconPassthrough != null}, Recenter:{iconRecenter != null}, Zoom:{iconZoom != null}, Monitor:{iconMonitor != null}");
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
