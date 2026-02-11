using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// RTTTaskbarExpansion - Expansion panel that appears above the taskbar.
/// Shows options for Bitrate or FPS selection when triggered.
/// Centers horizontally on the trigger button.
/// Click-outside to dismiss (like VRDropdown).
/// </summary>
public class RTTTaskbarExpansion : RTTCanvasBase
{
    #region Static
    /// <summary>
    /// Currently open expansion panel (for VRGazeReticle to check click-outside).
    /// </summary>
    public static RTTTaskbarExpansion CurrentlyOpenExpansion { get; private set; }
    #endregion

    #region Configuration
    [Header("Layout")]
    [SerializeField] private float buttonSize = 90f;
    [SerializeField] private float buttonSpacing = 12f;
    [SerializeField] private float frameHeight = 128f;
    [SerializeField] private float contentPadding = 16f;

    [Header("App Expansion")]
    [SerializeField] private int appExpansionCapacity = 4; // Fixed 4 slots for app expansion

    [Header("Glowing Border")]
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorA = new Color(0f, 1.5f, 2f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorB = new Color(1.2f, 0.3f, 2f, 1f);

    // Note: Positioning is handled by RTTToolbar parent
    #endregion

    #region Types
    public enum ExpansionType
    {
        None,
        Bitrate,
        Fps,
        Eye,
        Apps
    }
    #endregion

    #region Events
    public event Action<int> OnBitrateSelected;
    public event Action<int> OnFpsSelected;
    public event Action<bool> OnPassthroughToggled;
    public event Action<bool> OnLightToggled;
    public event Action OnDismissed;
    public event Action<int> OnAppSlotClicked;
    #endregion

    #region Private Fields
    private const float PixelToMeter = 1.6f / 1920f;

    private ExpansionType _currentType = ExpansionType.None;
    private float _totalWidth;

    private Material _glassMaterial;
    private Material _borderMaterial;
    private Sprite _pixelSprite;

    private RectTransform _contentContainer;
    private List<GameObject> _optionButtons = new List<GameObject>();

    // Current selection state
    private int _selectedBitrateMbps = 20;
    private int _selectedFps = 60;

    // Available options
    private readonly int[] _bitrateOptions = { 10, 15, 20, 25, 30 };
    private readonly int[] _fpsOptions = { 30, 45, 60 };

    // Icons
    private Dictionary<int, Sprite> _bitrateIcons = new Dictionary<int, Sprite>();
    private Dictionary<int, Sprite> _fpsIcons = new Dictionary<int, Sprite>();
    private Sprite _iconPassthrough;
    private Sprite _iconLightOn;
    private Sprite _iconLightOff;

    // Eye expansion state
    private bool _isPassthroughOn = false;
    private bool _isLightOn = true; // Default ON

    // App expansion state
    private List<AppSlotData> _appSlots = new List<AppSlotData>();
    private int _activeAppSlotIndex = -1;

    private class AppSlotData
    {
        public int SlotIndex;
        public Sprite Icon;
        public Action OnClick;
        public GameObject Button;
    }

    // Colors
    private readonly Color _cyanColor = new Color(0f, 0.9f, 1f);
    private readonly Color _purpleColor = new Color(0.9f, 0.3f, 1f);

    // State
    private new bool _isVisible = false;

    // Local X offset to align with trigger button
    private float _localXOffset = 0f;
    #endregion

    #region Properties
    public new bool IsVisible => _isVisible;
    public ExpansionType CurrentType => _currentType;
    public int AppSlotCount => _appSlots.Count;
    #endregion

    #region Lifecycle
    protected override void Awake()
    {
        LoadIcons();
        RecalculateSize(ExpansionType.Bitrate); // Default to bitrate for initial size

        worldWidth = _totalWidth * PixelToMeter;
        worldHeight = frameHeight * PixelToMeter;

        base.Awake();
    }

    protected override void Start()
    {
        base.Start();
        // Start hidden - but only if not already shown by creator
        // (ShowExpansion may have been called between Awake and Start)
        if (!_isVisible)
        {
            Hide();
        }
    }

    protected override void OnDestroy()
    {
        if (_glassMaterial != null) Destroy(_glassMaterial);
        if (_borderMaterial != null) Destroy(_borderMaterial);
        base.OnDestroy();
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        // Note: Positioning is handled by RTTToolbar parent
    }
    #endregion

    #region RTTCanvasBase Overrides
    protected override Vector2Int GetResolution()
    {
        return new Vector2Int(
            Mathf.RoundToInt(_totalWidth),
            Mathf.RoundToInt(frameHeight)
        );
    }

    protected override int GetCameraDepth()
    {
        return -48; // Render after taskbar
    }

    protected override void BuildUI()
    {
        if (_canvas == null) return;

        var canvasRect = _canvas.GetComponent<RectTransform>();

        // 1. Glass Background
        CreateGlassPanel(canvasRect);

        // 2. Content Container
        CreateContentContainer(canvasRect);

        Debug.Log($"[RTTTaskbarExpansion] UI built: {_totalWidth}x{frameHeight} pixels");
    }
    #endregion

    #region Public API
    /// <summary>
    /// Show expansion panel with bitrate options.
    /// </summary>
    /// <param name="currentBitrateMbps">Current selected bitrate</param>
    /// <param name="triggerButtonWorldPos">World position of the trigger button (for X-axis alignment)</param>
    public void ShowBitrateOptions(int currentBitrateMbps, Vector3? triggerButtonWorldPos = null)
    {
        _selectedBitrateMbps = currentBitrateMbps;
        CalculateLocalXOffset(triggerButtonWorldPos);
        ShowExpansion(ExpansionType.Bitrate);
    }

    /// <summary>
    /// Show expansion panel with FPS options.
    /// </summary>
    /// <param name="currentFps">Current selected FPS</param>
    /// <param name="triggerButtonWorldPos">World position of the trigger button (for X-axis alignment)</param>
    public void ShowFpsOptions(int currentFps, Vector3? triggerButtonWorldPos = null)
    {
        _selectedFps = currentFps;
        CalculateLocalXOffset(triggerButtonWorldPos);
        ShowExpansion(ExpansionType.Fps);
    }

    /// <summary>
    /// Show expansion panel with Eye options (Passthrough and Light).
    /// </summary>
    /// <param name="isPassthroughOn">Current passthrough state</param>
    /// <param name="isLightOn">Current light state</param>
    /// <param name="triggerButtonWorldPos">World position of the trigger button (for X-axis alignment)</param>
    public void ShowEyeOptions(bool isPassthroughOn, bool isLightOn, Vector3? triggerButtonWorldPos = null)
    {
        _isPassthroughOn = isPassthroughOn;
        _isLightOn = isLightOn;
        CalculateLocalXOffset(triggerButtonWorldPos);
        ShowExpansion(ExpansionType.Eye);
    }

    /// <summary>
    /// Calculate local X offset to align expansion center with trigger button.
    /// </summary>
    private void CalculateLocalXOffset(Vector3? triggerButtonWorldPos)
    {
        _localXOffset = 0f;

        if (!triggerButtonWorldPos.HasValue) return;

        // Get RTTToolbar to convert world position to local
        RTTToolbar toolbar = RTTToolbar.Instance;
        if (toolbar == null) return;

        // Convert button world position to toolbar local position
        Vector3 localPos = toolbar.transform.InverseTransformPoint(triggerButtonWorldPos.Value);

        // Use the local X as offset (expansion panel center aligns with button center)
        _localXOffset = localPos.x;
    }

    /// <summary>
    /// Hide the expansion panel.
    /// </summary>
    public new void Hide()
    {
        // Don't hide Apps expansion if there are still apps - only UnregisterAppSlot can hide it
        if (_currentType == ExpansionType.Apps && _appSlots.Count > 0)
        {
            Debug.Log("[RTTTaskbarExpansion] Cannot hide Apps expansion while apps are registered");
            return;
        }

        ForceHide();
    }

    /// <summary>
    /// Force hide the expansion panel (used internally when all apps are unregistered).
    /// </summary>
    private void ForceHide()
    {
        _isVisible = false;
        _currentType = ExpansionType.None;

        if (gameObject.activeInHierarchy)
        {
            gameObject.SetActive(false);
        }

        OnDismissed?.Invoke();
    }

    /// <summary>
    /// Set current bitrate selection (for visual update without triggering event).
    /// </summary>
    public void SetCurrentBitrate(int mbps)
    {
        _selectedBitrateMbps = mbps;
        if (_currentType == ExpansionType.Bitrate)
        {
            UpdateButtonStates();
        }
    }

    /// <summary>
    /// Set current FPS selection (for visual update without triggering event).
    /// </summary>
    public void SetCurrentFps(int fps)
    {
        _selectedFps = fps;
        if (_currentType == ExpansionType.Fps)
        {
            UpdateButtonStates();
        }
    }

    /// <summary>
    /// Set passthrough state from external (used when light toggles).
    /// </summary>
    public void SetPassthroughState(bool isOn)
    {
        if (_isPassthroughOn == isOn) return;

        _isPassthroughOn = isOn;
        if (_currentType == ExpansionType.Eye)
        {
            UpdateEyeButtonStates();
            MarkDirty();
        }
    }

    /// <summary>
    /// Set light state from external source (e.g., Video Player).
    /// </summary>
    public void SetLightState(bool isOn)
    {
        if (_isLightOn == isOn) return;

        _isLightOn = isOn;
        if (_currentType == ExpansionType.Eye)
        {
            UpdateEyeButtonStates();
            SetPassthroughInteractable(isOn);
            MarkDirty();
        }
    }

    /// <summary>
    /// Set whether passthrough button is interactable (disabled when light is OFF).
    /// </summary>
    public void SetPassthroughInteractable(bool interactable)
    {
        if (_currentType != ExpansionType.Eye || _optionButtons.Count < 1) return;

        var passthroughBtn = _optionButtons[0];
        var button = passthroughBtn.GetComponentInChildren<UnityEngine.UI.Button>();
        if (button != null)
        {
            button.interactable = interactable;
        }

        // Disable hover effect when locked
        var hoverController = passthroughBtn.GetComponentInChildren<VRWorkspace.UI.HoverEffects.HoverEffectController>();
        if (hoverController != null)
        {
            hoverController.enabled = interactable;
        }

        // Dim the icon when disabled
        var canvasGroup = passthroughBtn.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = passthroughBtn.AddComponent<CanvasGroup>();
        }
        canvasGroup.alpha = interactable ? 1f : 0.5f;

        MarkDirty();
    }

    /// <summary>
    /// Check if a GameObject is part of this expansion panel.
    /// Used by VRGazeReticle to determine click-outside behavior.
    /// </summary>
    public bool IsPartOfExpansionPanel(GameObject obj)
    {
        if (obj == null) return false;

        // Check if obj is this panel or a child
        Transform t = obj.transform;
        while (t != null)
        {
            if (t == transform) return true;
            t = t.parent;
        }

        // Also check against canvas and its children (for RTT raycast results)
        if (_canvas != null)
        {
            Transform canvasT = _canvas.transform;
            t = obj.transform;
            while (t != null)
            {
                if (t == canvasT) return true;
                t = t.parent;
            }
        }

        return false;
    }

    /// <summary>
    /// Register an app in the expansion panel.
    /// </summary>
    public void RegisterAppSlot(int slotIndex, Sprite icon, Action onClick)
    {
        // Check if slot already exists
        var existing = _appSlots.Find(s => s.SlotIndex == slotIndex);
        if (existing != null)
        {
            existing.Icon = icon;
            existing.OnClick = onClick;
            RebuildAppButtons();
            return;
        }

        _appSlots.Add(new AppSlotData
        {
            SlotIndex = slotIndex,
            Icon = icon,
            OnClick = onClick,
            Button = null
        });

        // Show expansion if not already visible
        if (!_isVisible || _currentType != ExpansionType.Apps)
        {
            ShowAppExpansion();
        }
        else
        {
            RebuildAppButtons();
        }

        Debug.Log($"[RTTTaskbarExpansion] Registered app slot {slotIndex}, total slots: {_appSlots.Count}");
    }

    /// <summary>
    /// Unregister an app from the expansion panel.
    /// </summary>
    public void UnregisterAppSlot(int slotIndex)
    {
        var slot = _appSlots.Find(s => s.SlotIndex == slotIndex);
        if (slot == null) return;

        if (slot.Button != null)
        {
            Destroy(slot.Button);
        }
        _appSlots.Remove(slot);

        // If active slot was removed, reset to -1
        if (_activeAppSlotIndex == slotIndex)
        {
            _activeAppSlotIndex = -1;
        }

        // Force hide expansion if no more apps
        if (_appSlots.Count == 0)
        {
            ForceHide();
        }
        else
        {
            RebuildAppButtons();
        }

        Debug.Log($"[RTTTaskbarExpansion] Unregistered app slot {slotIndex}, remaining slots: {_appSlots.Count}");
    }

    /// <summary>
    /// Clear all app slots from the expansion panel.
    /// </summary>
    public void ClearAllAppSlots()
    {
        foreach (var slot in _appSlots)
        {
            if (slot.Button != null)
            {
                Destroy(slot.Button);
            }
        }
        _appSlots.Clear();
        _activeAppSlotIndex = -1;

        if (_currentType == ExpansionType.Apps)
        {
            ForceHide();
        }

        Debug.Log("[RTTTaskbarExpansion] Cleared all app slots");
    }

    /// <summary>
    /// Select an app slot in the expansion (set it as active).
    /// </summary>
    public void SelectAppSlot(int slotIndex)
    {
        if (_activeAppSlotIndex == slotIndex) return;

        _activeAppSlotIndex = slotIndex;
        UpdateAppButtonStates();
        MarkDirty();
    }

    /// <summary>
    /// Clear active selection in expansion (when main taskbar slot is selected).
    /// </summary>
    public void ClearAppSlotSelection()
    {
        if (_activeAppSlotIndex == -1) return;

        _activeAppSlotIndex = -1;
        UpdateAppButtonStates();
        MarkDirty();
    }

    /// <summary>
    /// Set the position offset for app expansion (call before registering slots).
    /// </summary>
    /// <param name="section2CenterWorldPos">World position of Section 2 center for alignment</param>
    public void SetAppExpansionPosition(Vector3? section2CenterWorldPos)
    {
        CalculateLocalXOffset(section2CenterWorldPos);
    }

    /// <summary>
    /// Show expansion panel for app overflow.
    /// </summary>
    public void ShowAppExpansion()
    {
        ShowExpansion(ExpansionType.Apps);
    }
    #endregion

    #region Private Methods
    private void ShowExpansion(ExpansionType type)
    {
        _currentType = type;

        // Recalculate size based on type
        RecalculateSize(type);

        // Update world size
        worldWidth = _totalWidth * PixelToMeter;
        worldHeight = frameHeight * PixelToMeter;

        // Resize quad and canvas to match new size
        ResizeQuadAndCanvas();

        // Update local position with X offset to align with trigger button
        UpdateLocalPosition();

        // Show
        gameObject.SetActive(true);
        _isVisible = true;

        // NOTE: Not setting CurrentlyOpenExpansion - click-outside-to-close is disabled for all expansion types

        // Rebuild UI completely (glass panel aspect ratio needs updating)
        RebuildUI();

        MarkDirty();

        Debug.Log($"[RTTTaskbarExpansion] Showing {type} options, width={_totalWidth}px, worldWidth={worldWidth}m, xOffset={_localXOffset:F3}");
    }

    /// <summary>
    /// Update local position within RTTToolbar, applying X offset to align with trigger button.
    /// </summary>
    private void UpdateLocalPosition()
    {
        RTTToolbar toolbar = RTTToolbar.Instance;
        if (toolbar == null) return;

        // Get base Y position from toolbar
        Vector3 basePos = toolbar.GetExpansionLocalPosition();

        // Apply X offset to align with trigger button
        transform.localPosition = new Vector3(_localXOffset, basePos.y, basePos.z);
    }

    /// <summary>
    /// Resize the display quad and canvas to match current worldWidth/worldHeight.
    /// </summary>
    private void ResizeQuadAndCanvas()
    {
        Vector2Int resolution = GetResolution();

        // Resize render texture (this also updates camera, material, canvas scaler, camera ortho size)
        ResizeRenderTexture(resolution.x, resolution.y);

        // Resize display quad
        if (_displayQuad != null)
        {
            _displayQuad.transform.localScale = new Vector3(worldWidth, worldHeight, 1f);
        }

        // Resize quad collider
        if (_quadCollider != null)
        {
            _quadCollider.size = new Vector3(1f, 1f, 0.01f);
        }
    }

    private void RecalculateSize(ExpansionType type)
    {
        int buttonCount = type switch
        {
            ExpansionType.Bitrate => _bitrateOptions.Length,
            ExpansionType.Fps => _fpsOptions.Length,
            ExpansionType.Eye => 2, // Passthrough + Light
            ExpansionType.Apps => appExpansionCapacity, // Fixed 4 slots
            _ => 3
        };
        _totalWidth = contentPadding * 2 + (buttonCount * buttonSize) + ((buttonCount - 1) * buttonSpacing);
    }

    private void RebuildUI()
    {
        // Destroy old visuals - use DestroyImmediate to avoid timing issues
        if (_canvas != null)
        {
            for (int i = _canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = _canvas.transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }

        // Cleanup old materials to avoid memory leak
        if (_glassMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_glassMaterial);
            else
                DestroyImmediate(_glassMaterial);
            _glassMaterial = null;
        }
        if (_borderMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_borderMaterial);
            else
                DestroyImmediate(_borderMaterial);
            _borderMaterial = null;
        }

        // Clear references
        _optionButtons.Clear();
        _contentContainer = null;

        // Clear app slot button references
        foreach (var slot in _appSlots)
        {
            slot.Button = null;
        }

        // Rebuild
        BuildUI();
        RebuildContent();
        MarkDirty();
    }

    private void RebuildContent()
    {
        if (_contentContainer == null) return;

        // Clear existing buttons - destroy immediately to avoid overlap issues
        for (int i = _optionButtons.Count - 1; i >= 0; i--)
        {
            var btn = _optionButtons[i];
            if (btn != null)
            {
                if (Application.isPlaying)
                    Destroy(btn);
                else
                    DestroyImmediate(btn);
            }
        }
        _optionButtons.Clear();

        // Create new buttons based on type
        if (_currentType == ExpansionType.Bitrate)
        {
            CreateBitrateButtons();
        }
        else if (_currentType == ExpansionType.Fps)
        {
            CreateFpsButtons();
        }
        else if (_currentType == ExpansionType.Eye)
        {
            CreateEyeButtons();
        }
        else if (_currentType == ExpansionType.Apps)
        {
            CreateAppButtons();
        }
    }

    private void CreateBitrateButtons()
    {
        foreach (int mbps in _bitrateOptions)
        {
            bool isSelected = (mbps == _selectedBitrateMbps);
            Color color = isSelected ? _purpleColor : _cyanColor;
            Sprite icon = _bitrateIcons.TryGetValue(mbps, out var s) ? s : null;

            int capturedMbps = mbps;
            var btn = VRButtonFactory.CreateBareIconButton(
                _contentContainer, buttonSize, icon, color,
                () => OnBitrateButtonClicked(capturedMbps),
                0.05f, 0.6f
            );

            btn.name = $"Btn_Bitrate_{mbps}";
            SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));
            _optionButtons.Add(btn);
        }
    }

    private void CreateFpsButtons()
    {
        foreach (int fps in _fpsOptions)
        {
            bool isSelected = (fps == _selectedFps);
            Color color = isSelected ? _purpleColor : _cyanColor;
            Sprite icon = _fpsIcons.TryGetValue(fps, out var s) ? s : null;

            int capturedFps = fps;
            var btn = VRButtonFactory.CreateBareIconButton(
                _contentContainer, buttonSize, icon, color,
                () => OnFpsButtonClicked(capturedFps),
                0.05f, 0.6f
            );

            btn.name = $"Btn_Fps_{fps}";
            SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));
            _optionButtons.Add(btn);
        }
    }

    private void CreateEyeButtons()
    {
        // Passthrough button
        Color passthroughColor = _isPassthroughOn ? _purpleColor : _cyanColor;
        var passthroughBtn = VRButtonFactory.CreateBareIconButton(
            _contentContainer, buttonSize, _iconPassthrough, passthroughColor,
            OnPassthroughButtonClicked,
            0.05f, 0.6f
        );
        passthroughBtn.name = "Btn_Passthrough";
        SetLayerRecursively(passthroughBtn, LayerMask.NameToLayer("UI"));
        _optionButtons.Add(passthroughBtn);

        // Light button
        Color lightColor = _isLightOn ? _purpleColor : _cyanColor;
        Sprite lightIcon = _isLightOn ? _iconLightOn : _iconLightOff;
        var lightBtn = VRButtonFactory.CreateBareIconButton(
            _contentContainer, buttonSize, lightIcon, lightColor,
            OnLightButtonClicked,
            0.05f, 0.6f
        );
        lightBtn.name = "Btn_Light";
        SetLayerRecursively(lightBtn, LayerMask.NameToLayer("UI"));
        _optionButtons.Add(lightBtn);
    }

    private void CreateAppButtons()
    {
        // Create fixed number of slots (4 buttons)
        for (int visualIndex = 0; visualIndex < appExpansionCapacity; visualIndex++)
        {
            // Find app for this visual index (apps are ordered left to right)
            AppSlotData slotData = (visualIndex < _appSlots.Count) ? _appSlots[visualIndex] : null;

            if (slotData != null)
            {
                // Create actual app button
                bool isActive = (slotData.SlotIndex == _activeAppSlotIndex);
                Color color = isActive ? _purpleColor : _cyanColor;

                int capturedIndex = slotData.SlotIndex;
                var btn = VRButtonFactory.CreateBareIconButton(
                    _contentContainer, buttonSize, slotData.Icon, color,
                    () => OnAppButtonClicked(capturedIndex),
                    0.05f, 0.6f
                );

                btn.name = $"Btn_App_{slotData.SlotIndex}";
                SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));
                _optionButtons.Add(btn);
                slotData.Button = btn;

                // Set interactable and force hover for active button
                var button = btn.GetComponentInChildren<UnityEngine.UI.Button>();
                if (button != null)
                {
                    button.interactable = !isActive;
                }

                var hoverController = btn.GetComponentInChildren<VRWorkspace.UI.HoverEffects.HoverEffectController>();
                if (hoverController != null)
                {
                    hoverController.SetForceHover(isActive);
                }
            }
            else
            {
                // Create empty placeholder slot
                var placeholder = CreateEmptySlotPlaceholder();
                _optionButtons.Add(placeholder);
            }
        }
    }

    private GameObject CreateEmptySlotPlaceholder()
    {
        GameObject placeholder = new GameObject("EmptySlot");
        placeholder.transform.SetParent(_contentContainer, false);

        RectTransform rt = placeholder.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(buttonSize, buttonSize);

        // Make it invisible but take up space
        CanvasGroup cg = placeholder.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        SetLayerRecursively(placeholder, LayerMask.NameToLayer("UI"));
        return placeholder;
    }

    private void RebuildAppButtons()
    {
        if (_currentType != ExpansionType.Apps) return;

        // Just rebuild content, size is fixed
        RebuildContent();
        MarkDirty();
    }

    private void OnAppButtonClicked(int slotIndex)
    {
        // Don't do anything if already active
        if (slotIndex == _activeAppSlotIndex) return;

        _activeAppSlotIndex = slotIndex;
        UpdateAppButtonStates();

        // Find the slot and invoke its callback
        var slot = _appSlots.Find(s => s.SlotIndex == slotIndex);
        slot?.OnClick?.Invoke();

        OnAppSlotClicked?.Invoke(slotIndex);
        MarkDirty();
    }

    private void UpdateAppButtonStates()
    {
        foreach (var slot in _appSlots)
        {
            if (slot.Button == null) continue;

            bool isActive = (slot.SlotIndex == _activeAppSlotIndex);
            Color color = isActive ? _purpleColor : _cyanColor;
            VRButtonFactory.SetBareIconButtonGlowColor(slot.Button, color);

            // Update interactable and force hover
            var button = slot.Button.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.interactable = !isActive;
            }

            var hoverController = slot.Button.GetComponentInChildren<VRWorkspace.UI.HoverEffects.HoverEffectController>();
            if (hoverController != null)
            {
                hoverController.SetForceHover(isActive);
            }
        }
    }

    private void OnPassthroughButtonClicked()
    {
        _isPassthroughOn = !_isPassthroughOn;
        UpdateEyeButtonStates();
        OnPassthroughToggled?.Invoke(_isPassthroughOn);
        MarkDirty();

        // Hide after selection (except Apps expansion)
        Hide();
    }

    private void OnLightButtonClicked()
    {
        _isLightOn = !_isLightOn;
        UpdateEyeButtonStates();
        OnLightToggled?.Invoke(_isLightOn);
        MarkDirty();

        // Hide after selection (except Apps expansion)
        Hide();
    }

    private void UpdateEyeButtonStates()
    {
        if (_optionButtons.Count < 2) return;

        // Passthrough button (index 0)
        Color passthroughColor = _isPassthroughOn ? _purpleColor : _cyanColor;
        VRButtonFactory.SetBareIconButtonGlowColor(_optionButtons[0], passthroughColor);

        // Light button (index 1) - also update icon
        Color lightColor = _isLightOn ? _purpleColor : _cyanColor;
        VRButtonFactory.SetBareIconButtonGlowColor(_optionButtons[1], lightColor);

        // Update light icon
        Sprite lightIcon = _isLightOn ? _iconLightOn : _iconLightOff;
        VRButtonFactory.SetBareIconButtonSprite(_optionButtons[1], lightIcon);
    }

    private void OnBitrateButtonClicked(int mbps)
    {
        _selectedBitrateMbps = mbps;
        UpdateButtonStates();
        OnBitrateSelected?.Invoke(mbps);
        MarkDirty();

        // Hide after selection
        Hide();
    }

    private void OnFpsButtonClicked(int fps)
    {
        _selectedFps = fps;
        UpdateButtonStates();
        OnFpsSelected?.Invoke(fps);
        MarkDirty();

        // Hide after selection
        Hide();
    }

    private void UpdateButtonStates()
    {
        if (_currentType == ExpansionType.Bitrate)
        {
            for (int i = 0; i < _bitrateOptions.Length && i < _optionButtons.Count; i++)
            {
                bool isSelected = (_bitrateOptions[i] == _selectedBitrateMbps);
                Color color = isSelected ? _purpleColor : _cyanColor;
                VRButtonFactory.SetBareIconButtonGlowColor(_optionButtons[i], color);
            }
        }
        else if (_currentType == ExpansionType.Fps)
        {
            for (int i = 0; i < _fpsOptions.Length && i < _optionButtons.Count; i++)
            {
                bool isSelected = (_fpsOptions[i] == _selectedFps);
                Color color = isSelected ? _purpleColor : _cyanColor;
                VRButtonFactory.SetBareIconButtonGlowColor(_optionButtons[i], color);
            }
        }
    }
    #endregion

    #region UI Building
    private void CreateGlassPanel(RectTransform parent)
    {
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);

        Image img = bgObj.AddComponent<Image>();
        img.type = Image.Type.Simple;
        img.sprite = GetPixelSprite();
        img.raycastTarget = true;

        float edgePad = 0.06f;
        float aspect = _totalWidth / frameHeight;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            _glassMaterial = new Material(glassShader);

            _glassMaterial.SetFloat("_CornerRadius", 0.12f);
            _glassMaterial.SetFloat("_EdgePadding", edgePad);
            _glassMaterial.SetFloat("_Aspect", aspect);

            _glassMaterial.SetColor("_ColorA", GetGlassColorA());
            _glassMaterial.SetColor("_ColorB", GetGlassColorB());
            _glassMaterial.SetFloat("_GradientOffset", 0f);
            _glassMaterial.SetFloat("_GradientAngle", -10f);
            _glassMaterial.SetFloat("_CyanRatio", 0.7f);
            _glassMaterial.SetFloat("_GlassAlpha", 0.65f);
            _glassMaterial.SetFloat("_FresnelPower", 2.2f);
            _glassMaterial.SetFloat("_FresnelStrength", 0.12f);

            img.material = _glassMaterial;
            img.color = Color.white;
        }

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.SetAsFirstSibling();

        CreateGlowingBorder(bgObj.transform, edgePad);
    }

    private void CreateGlowingBorder(Transform parent, float edgePad)
    {
        GameObject borderObj = new GameObject("GlowingBorder");
        borderObj.transform.SetParent(parent, false);

        RectTransform rt = borderObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;
        borderImg.sprite = GetPixelSprite();

        float aspect = _totalWidth / frameHeight;

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            _borderMaterial = new Material(glowShader);

            _borderMaterial.SetFloat("_StrokeEnabled", 0);
            _borderMaterial.SetFloat("_BorderWidth", 0.06f);
            _borderMaterial.SetFloat("_CornerRadius", 0.12f);
            _borderMaterial.SetFloat("_EdgePadding", edgePad);
            _borderMaterial.SetFloat("_Aspect", aspect);

            // Glow layers
            _borderMaterial.SetFloat("_Layer1Width", 0.03f);
            _borderMaterial.SetFloat("_Layer1Alpha", 1.5f);
            _borderMaterial.SetFloat("_Layer2Width", 0.06f);
            _borderMaterial.SetFloat("_Layer2Alpha", 1.0f);
            _borderMaterial.SetFloat("_Layer3Width", 0.1275f);
            _borderMaterial.SetFloat("_Layer3Alpha", 0.6f);
            _borderMaterial.SetFloat("_Layer4Width", 0.27f);
            _borderMaterial.SetFloat("_Layer4Alpha", 0.3f);

            _borderMaterial.SetColor("_ColorA", GetGlowColorA());
            _borderMaterial.SetColor("_ColorB", GetGlowColorB());
            _borderMaterial.SetFloat("_GradientMode", 2f);
            _borderMaterial.SetFloat("_GradientAngle", -10f);

            _borderMaterial.SetFloat("_GlassAlpha", 0.02f);
            _borderMaterial.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));

            _borderMaterial.SetFloat("_ShimmerSpeed", 0.1f);
            _borderMaterial.SetFloat("_ShimmerIntensity", 0.2f);
            _borderMaterial.SetFloat("_LightSize", 0.008f);
            _borderMaterial.SetFloat("_LightGlow", 0.008f);

            // No separators for expansion panel
            _borderMaterial.SetFloat("_SeparatorCount", 0);

            borderImg.material = _borderMaterial;
        }

        borderObj.transform.SetAsLastSibling();
    }

    private void CreateContentContainer(RectTransform parent)
    {
        GameObject contentObj = new GameObject("ContentContainer");
        contentObj.transform.SetParent(parent, false);

        _contentContainer = contentObj.AddComponent<RectTransform>();
        _contentContainer.anchorMin = Vector2.zero;
        _contentContainer.anchorMax = Vector2.one;
        _contentContainer.offsetMin = new Vector2(contentPadding, contentPadding);
        _contentContainer.offsetMax = new Vector2(-contentPadding, -contentPadding);

        HorizontalLayoutGroup layout = contentObj.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = buttonSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }
    #endregion

    #region Theme Support
    protected override void ApplyCurrentTheme()
    {
        var theme = GetTheme();
        if (theme == null) return;

        if (_glassMaterial != null)
        {
            _glassMaterial.SetColor("_ColorA", theme.glassColorA);
            _glassMaterial.SetColor("_ColorB", theme.glassColorB);
            _glassMaterial.SetFloat("_GlassAlpha", theme.glassAlpha);
        }

        if (_borderMaterial != null)
        {
            _borderMaterial.SetColor("_ColorA", theme.glowColorA);
            _borderMaterial.SetColor("_ColorB", theme.glowColorB);
        }

        MarkDirty();
    }

    private Color GetGlassColorA()
    {
        var theme = GetTheme();
        return theme?.glassColorA ?? new Color(0.0f, 0.55f, 0.65f, 0.35f);
    }

    private Color GetGlassColorB()
    {
        var theme = GetTheme();
        return theme?.glassColorB ?? new Color(0.30f, 0.12f, 0.50f, 0.32f);
    }

    private Color GetGlowColorA()
    {
        var theme = GetTheme();
        return theme?.glowColorA ?? glowColorA;
    }

    private Color GetGlowColorB()
    {
        var theme = GetTheme();
        return theme?.glowColorB ?? glowColorB;
    }
    #endregion

    #region Icons
    private void LoadIcons()
    {
        // Dynamic bitrate icons (10, 15, 20, 25, 30 Mbps)
        foreach (int mbps in _bitrateOptions)
        {
            var icon = Resources.Load<Sprite>($"icon_{mbps}_mbps");
            if (icon != null)
            {
                _bitrateIcons[mbps] = icon;
            }
        }

        // Dynamic FPS icons (30, 45, 60)
        foreach (int fps in _fpsOptions)
        {
            var icon = Resources.Load<Sprite>($"icon_{fps}_fps");
            if (icon != null)
            {
                _fpsIcons[fps] = icon;
            }
        }

        // Eye expansion icons
        _iconPassthrough = Resources.Load<Sprite>("icon_passthrough");
        _iconLightOn = Resources.Load<Sprite>("icon_light_on");
        _iconLightOff = Resources.Load<Sprite>("icon_light_off");

        Debug.Log($"[RTTTaskbarExpansion] Icons loaded - Bitrate: {_bitrateIcons.Count}/{_bitrateOptions.Length}, " +
            $"FPS: {_fpsIcons.Count}/{_fpsOptions.Length}, " +
            $"Passthrough: {_iconPassthrough != null}, LightOn: {_iconLightOn != null}, LightOff: {_iconLightOff != null}");
    }
    #endregion

    #region Helpers
    private Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;

        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
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
}
