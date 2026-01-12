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

    [Header("Glowing Border")]
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorA = new Color(0f, 1.5f, 2f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorB = new Color(1.2f, 0.3f, 2f, 1f);

    [Header("Position")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private float gapAboveTaskbar = 0.0025f; // Reduced to half
    #endregion

    #region Types
    public enum ExpansionType
    {
        None,
        Bitrate,
        Fps
    }
    #endregion

    #region Events
    public event Action<int> OnBitrateSelected;
    public event Action<int> OnFpsSelected;
    public event Action OnDismissed;
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

    // Colors
    private readonly Color _cyanColor = new Color(0f, 0.9f, 1f);
    private readonly Color _purpleColor = new Color(0.9f, 0.3f, 1f);

    // State
    private bool _isVisible = false;

    // Trigger button world position (for X-axis alignment)
    private Vector3 _triggerButtonWorldPos;
    private bool _hasTriggerPosition = false;
    #endregion

    #region Properties
    public bool IsVisible => _isVisible;
    public ExpansionType CurrentType => _currentType;
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
        // Start hidden
        Hide();
    }

    protected override void OnDestroy()
    {
        // Clear static reference if this is the open one
        if (CurrentlyOpenExpansion == this)
        {
            CurrentlyOpenExpansion = null;
        }

        if (_glassMaterial != null) Destroy(_glassMaterial);
        if (_borderMaterial != null) Destroy(_borderMaterial);
        base.OnDestroy();
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        UpdatePositionTracking();
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
    /// <param name="triggerButtonWorldPos">World position of the trigger button (optional, for X-axis alignment)</param>
    public void ShowBitrateOptions(int currentBitrateMbps, Vector3? triggerButtonWorldPos = null)
    {
        _selectedBitrateMbps = currentBitrateMbps;
        if (triggerButtonWorldPos.HasValue)
        {
            _triggerButtonWorldPos = triggerButtonWorldPos.Value;
            _hasTriggerPosition = true;
        }
        ShowExpansion(ExpansionType.Bitrate);
    }

    /// <summary>
    /// Show expansion panel with FPS options.
    /// </summary>
    /// <param name="currentFps">Current selected FPS</param>
    /// <param name="triggerButtonWorldPos">World position of the trigger button (optional, for X-axis alignment)</param>
    public void ShowFpsOptions(int currentFps, Vector3? triggerButtonWorldPos = null)
    {
        _selectedFps = currentFps;
        if (triggerButtonWorldPos.HasValue)
        {
            _triggerButtonWorldPos = triggerButtonWorldPos.Value;
            _hasTriggerPosition = true;
        }
        ShowExpansion(ExpansionType.Fps);
    }

    /// <summary>
    /// Hide the expansion panel.
    /// </summary>
    public void Hide()
    {
        _isVisible = false;
        _currentType = ExpansionType.None;
        _hasTriggerPosition = false;

        // Clear static reference
        if (CurrentlyOpenExpansion == this)
        {
            CurrentlyOpenExpansion = null;
        }

        if (gameObject.activeInHierarchy)
        {
            gameObject.SetActive(false);
        }

        OnDismissed?.Invoke();
    }

    /// <summary>
    /// Set the follow target (typically the taskbar).
    /// </summary>
    public void SetFollowTarget(Transform target)
    {
        followTarget = target;
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
    #endregion

    #region Private Methods
    private void ShowExpansion(ExpansionType type)
    {
        // Close any other open expansion first
        if (CurrentlyOpenExpansion != null && CurrentlyOpenExpansion != this)
        {
            CurrentlyOpenExpansion.Hide();
        }

        _currentType = type;

        // Recalculate size based on type
        RecalculateSize(type);

        // Update world size
        worldWidth = _totalWidth * PixelToMeter;
        worldHeight = frameHeight * PixelToMeter;

        // Show
        gameObject.SetActive(true);
        _isVisible = true;

        // Set static reference
        CurrentlyOpenExpansion = this;

        // Rebuild content
        RebuildContent();

        MarkDirty();

        Debug.Log($"[RTTTaskbarExpansion] Showing {type} options, width={_totalWidth}px");
    }

    private void RecalculateSize(ExpansionType type)
    {
        int buttonCount = type == ExpansionType.Bitrate ? _bitrateOptions.Length : _fpsOptions.Length;
        _totalWidth = contentPadding * 2 + (buttonCount * buttonSize) + ((buttonCount - 1) * buttonSpacing);
    }

    private void RebuildUI()
    {
        // Destroy old visuals
        if (_canvas != null)
        {
            foreach (Transform child in _canvas.transform)
            {
                Destroy(child.gameObject);
            }
        }

        // Clear references
        _optionButtons.Clear();
        _contentContainer = null;

        // Rebuild
        BuildUI();
        RebuildContent();
        MarkDirty();
    }

    private void RebuildContent()
    {
        if (_contentContainer == null) return;

        // Clear existing buttons
        foreach (var btn in _optionButtons)
        {
            if (btn != null) Destroy(btn);
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

    #region Position Tracking
    private void UpdatePositionTracking()
    {
        if (followTarget == null || !_isVisible) return;

        // Get taskbar bounds
        var taskbarRTT = followTarget.GetComponent<RTTCanvasBase>();
        float taskbarHalfHeight = 0f;

        if (taskbarRTT != null)
        {
            taskbarHalfHeight = taskbarRTT.GetWorldSize().y / 2f;
        }
        else
        {
            var taskbarMiniFrame = followTarget.GetComponent<RTTMiniFrame>();
            if (taskbarMiniFrame != null)
            {
                taskbarHalfHeight = taskbarMiniFrame.GetWorldSize().y / 2f;
            }
        }

        float myHalfHeight = (frameHeight * PixelToMeter) / 2f;

        // Position above taskbar
        float totalOffset = taskbarHalfHeight + myHalfHeight + gapAboveTaskbar;

        Vector3 localUp = followTarget.TransformDirection(Vector3.up);

        // Calculate base position (centered above taskbar)
        Vector3 basePosition = followTarget.position + localUp * totalOffset;

        // If we have a trigger button position, align X-axis to it
        if (_hasTriggerPosition)
        {
            // Get the local right direction of the taskbar
            Vector3 localRight = followTarget.TransformDirection(Vector3.right);

            // Calculate the offset from taskbar center to trigger button (along local right)
            Vector3 taskbarToTrigger = _triggerButtonWorldPos - followTarget.position;
            float xOffset = Vector3.Dot(taskbarToTrigger, localRight);

            // Apply the offset to center expansion panel on trigger button
            transform.position = basePosition + localRight * xOffset;
        }
        else
        {
            transform.position = basePosition;
        }

        // Face camera
        FaceCamera();
    }

    private void FaceCamera()
    {
        if (followTarget == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        // Copy Y rotation from taskbar (followTarget)
        // Only adjust X rotation (tilt) based on camera position

        // Get taskbar's forward direction (Y rotation)
        Vector3 taskbarForward = followTarget.forward;
        Vector3 taskbarUp = followTarget.up;

        // Calculate the direction to camera in taskbar's local space
        Vector3 toCamera = cam.transform.position - transform.position;
        if (toCamera.sqrMagnitude < 0.001f) return;

        // Project toCamera onto the plane defined by taskbar's right axis
        // This gives us the tilt angle (X rotation) while keeping Y rotation from taskbar
        Vector3 taskbarRight = followTarget.right;

        // Calculate the angle between taskbar forward and direction to camera (projected onto forward-up plane)
        Vector3 toCameraProjected = toCamera - Vector3.Project(toCamera, taskbarRight);
        if (toCameraProjected.sqrMagnitude < 0.001f)
        {
            toCameraProjected = -taskbarForward;
        }

        // Create rotation that faces the projected direction to camera
        Quaternion lookRotation = Quaternion.LookRotation(-toCameraProjected.normalized, taskbarUp);

        // Apply rotation
        transform.rotation = lookRotation;
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

        Debug.Log($"[RTTTaskbarExpansion] Icons loaded - Bitrate: {_bitrateIcons.Count}/{_bitrateOptions.Length}, FPS: {_fpsIcons.Count}/{_fpsOptions.Length}");
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
