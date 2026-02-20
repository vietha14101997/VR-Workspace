using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using VRWorkspace.UI.Config;

/// <summary>
/// RTTProgressPopup - A modal popup showing file operation progress.
/// Features:
/// - Progress bar with percentage
/// - Status text showing current file
/// - Cancel button to abort operation
/// - Dark overlay blocking all UI interactions
/// - World-space mode for VR
/// </summary>
public class RTTProgressPopup : MonoBehaviour
{
    #region Nested Classes

    [System.Serializable]
    public class PopupConfig
    {
        public float width = 500f;
        public float padding = 24f;
        public float titleFontSize = 28f;
        public float statusFontSize = 22f;
        public float percentFontSize = 24f;
        public float buttonFontSize = 22f;
        public float titleHeight = 40f;
        public float statusHeight = 30f;
        public float progressBarHeight = 24f;
        public float buttonHeight = 50f;
        public float spacing = 16f;
        public Color primaryColor = new Color(0f, 0.9f, 1f);
        public Color accentColor = new Color(0.76f, 0.36f, 1f);
        public Color progressBarBgColor = new Color(0.2f, 0.2f, 0.25f, 0.8f);
        public Color overlayColor = new Color(0f, 0f, 0f, 0.5f);
        public TMP_FontAsset font;
        public string layerName = "VirtualObjects";

        // Border settings (synchronized with other popups)
        public float borderWidth = 0.05f;
        public float buttonWidth = 450f;
        public float buttonBorderWidth = 0.04f;
        public float buttonGlowWidth = 0.08f;
        public float buttonGlowIntensity = 4f;
        public float buttonCornerRadius = 0.12f;
    }

    #endregion

    #region Private Fields

    private PopupConfig _config;
    private GameObject _popupObject;
    private RectTransform _popupRT;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _statusText;
    private TextMeshProUGUI _percentText;
    private RectTransform _progressBarFillRT;
    private Action _onCancel;
    private Action<bool> _onPauseToggle;
    private Material _bgMaterial;
    private Material _borderMaterial;
    private TextMeshProUGUI _pauseButtonText;
    private bool _isPaused = false;

    // External overlays
    private List<GameObject> _externalOverlays = new List<GameObject>();

    // World-space mode
    private bool _isWorldSpaceMode = false;
    private Canvas _worldCanvas;
    private Camera _mainCamera;
    private Transform _referenceTransform;
    private const float WORLD_SPACE_SCALE = 0.001f;
    private const float WORLD_SPACE_OFFSET = 0.05f;

    private static Sprite _pixelSprite;

    #endregion

    #region Public Properties

    public bool IsVisible => gameObject.activeSelf;
    public static RTTProgressPopup CurrentlyOpenPopup { get; private set; }
    public event Action OnHide;

    #endregion

    #region Factory Methods

    /// <summary>
    /// Create a world-space progress popup
    /// </summary>
    public static RTTProgressPopup CreateWorldSpace(PopupConfig config, Transform referenceFrame)
    {
        config = config ?? new PopupConfig();

        GameObject rootObj = new GameObject("RTTProgressPopup_WorldSpace");

        Camera mainCam = FindMainCamera();

        float totalHeight = CalculateTotalHeight(config);

        // World-space Canvas
        Canvas canvas = rootObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = mainCam;
        canvas.sortingOrder = 100;

        RectTransform canvasRT = rootObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(config.width + 40f, totalHeight + 40f);
        canvasRT.localScale = Vector3.one * WORLD_SPACE_SCALE;

        rootObj.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = rootObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        int layer = LayerMask.NameToLayer(config.layerName);
        if (layer != -1) rootObj.layer = layer;

        RTTProgressPopup popup = rootObj.AddComponent<RTTProgressPopup>();
        popup._config = config;
        popup._isWorldSpaceMode = true;
        popup._worldCanvas = canvas;
        popup._mainCamera = mainCam;
        popup._referenceTransform = referenceFrame;

        popup.BuildWorldSpacePopup();

        rootObj.SetActive(false);

        Debug.Log($"[RTTProgressPopup] Created world-space popup: {config.width}x{totalHeight}");

        return popup;
    }

    private static float CalculateTotalHeight(PopupConfig config)
    {
        return config.padding * 2
            + config.titleHeight
            + config.spacing
            + config.statusHeight
            + config.spacing
            + config.progressBarHeight
            + config.spacing * 2
            + config.buttonHeight;
    }

    private static Camera FindMainCamera()
    {
        string[] cameraNames = { "CenterEyeAnchor", "Main Camera", "PlayerCamera", "Camera" };
        foreach (var name in cameraNames)
        {
            GameObject camObj = GameObject.Find(name);
            if (camObj != null)
            {
                Camera cam = camObj.GetComponent<Camera>();
                if (cam != null && cam.gameObject.activeInHierarchy) return cam;
            }
        }

        if (Camera.main != null) return Camera.main;

        Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var cam in allCameras)
        {
            if (!cam.name.Contains("UI") && cam.gameObject.activeInHierarchy)
                return cam;
        }

        return null;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets whether the operation is currently paused
    /// </summary>
    public bool IsPaused => _isPaused;

    #endregion

    #region Public API

    /// <summary>
    /// Show the progress popup
    /// </summary>
    public void Show(string title, Action onCancel = null, Action<bool> onPauseToggle = null)
    {
        _onCancel = onCancel;
        _onPauseToggle = onPauseToggle;
        _isPaused = false;

        if (CurrentlyOpenPopup != null && CurrentlyOpenPopup != this)
        {
            CurrentlyOpenPopup.Hide();
        }

        // Reset progress and pause state
        UpdateProgress(new FileOperationService.FileOperationProgress());
        if (_pauseButtonText != null)
            _pauseButtonText.text = "Pause";

        if (_titleText != null)
            _titleText.text = title;

        if (_isWorldSpaceMode)
        {
            PositionInFrontOfReference();
            gameObject.SetActive(true);
            CreateExternalOverlays();
        }
        else
        {
            gameObject.SetActive(true);
        }

        CurrentlyOpenPopup = this;
    }

    /// <summary>
    /// Update progress display
    /// </summary>
    public void UpdateProgress(FileOperationService.FileOperationProgress progress)
    {
        if (progress == null) return;

        // Status text
        if (_statusText != null)
        {
            if (progress.TotalFiles > 0)
            {
                string fileName = progress.CurrentFileName ?? "";
                if (fileName.Length > 35)
                    fileName = fileName.Substring(0, 32) + "...";

                _statusText.text = $"File {progress.CompletedFiles + 1} of {progress.TotalFiles}: {fileName}";
            }
            else
            {
                _statusText.text = "Preparing...";
            }
        }

        // Progress bar
        if (_progressBarFillRT != null)
        {
            float fillPercent = Mathf.Clamp01(progress.OverallProgress);
            _progressBarFillRT.anchorMax = new Vector2(fillPercent, 1f);
        }

        // Percentage text
        if (_percentText != null)
        {
            int percent = Mathf.RoundToInt(progress.OverallProgress * 100f);
            _percentText.text = $"{percent}%";
        }
    }

    /// <summary>
    /// Hide the popup
    /// </summary>
    public void Hide()
    {
        DestroyExternalOverlays();

        gameObject.SetActive(false);

        if (CurrentlyOpenPopup == this)
        {
            CurrentlyOpenPopup = null;
        }

        OnHide?.Invoke();
    }

    #endregion

    #region Private - Build Methods

    private void BuildWorldSpacePopup()
    {
        int layer = LayerMask.NameToLayer(_config.layerName);
        float totalHeight = CalculateTotalHeight(_config);

        _popupObject = new GameObject("PopupPanel");
        _popupObject.transform.SetParent(transform, false);

        _popupRT = _popupObject.AddComponent<RectTransform>();
        _popupRT.anchorMin = new Vector2(0.5f, 0.5f);
        _popupRT.anchorMax = new Vector2(0.5f, 0.5f);
        _popupRT.pivot = new Vector2(0.5f, 0.5f);
        _popupRT.sizeDelta = new Vector2(_config.width, totalHeight);
        _popupRT.anchoredPosition = Vector2.zero;

        CreateBackground();
        CreateGlowBorder(totalHeight);

        // Update aspect ratio with actual height (like RTTPopupMenu)
        UpdateBackgroundAspect(totalHeight);

        CreateContent(totalHeight);

        // BoxCollider for VR raycast
        BoxCollider popupCol = _popupObject.AddComponent<BoxCollider>();
        popupCol.size = new Vector3(_config.width, totalHeight, 0.1f);
        popupCol.center = Vector3.zero;

        // Block dwell behavior
        Selectable blockingSelectable = _popupObject.AddComponent<Selectable>();
        blockingSelectable.interactable = false;
        blockingSelectable.transition = Selectable.Transition.None;

        if (layer != -1) SetLayerRecursively(gameObject, layer);
    }

    private void CreateBackground()
    {
        Image bgImg = _popupObject.AddComponent<Image>();
        bgImg.sprite = GetPixelSprite();

        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            _bgMaterial = new Material(glassShader);
            _bgMaterial.SetFloat("_CornerRadius", UIConstants.PopupCornerRadius + 0.01f); // Slightly larger than border
            _bgMaterial.SetFloat("_EdgePadding", UIConstants.PopupEdgePadding);
            _bgMaterial.SetFloat("_Aspect", _config.width / 100f);

            _bgMaterial.SetColor("_ColorA", UIConstants.PopupGlassColorA);
            _bgMaterial.SetColor("_ColorB", UIConstants.PopupGlassColorB);
            _bgMaterial.SetFloat("_GlassAlpha", UIConstants.PopupGlassAlpha);
            _bgMaterial.SetFloat("_GradientOffset", 0f);
            _bgMaterial.SetFloat("_GradientAngle", -10f);
            _bgMaterial.SetFloat("_CyanRatio", 0.7f);
            _bgMaterial.SetFloat("_FresnelPower", 2.2f);
            _bgMaterial.SetFloat("_FresnelStrength", 0.12f);

            bgImg.material = _bgMaterial;
            bgImg.color = Color.white;
        }
        else
        {
            bgImg.color = new Color(0.12f, 0.12f, 0.16f, 0.85f);
        }
    }

    private void CreateGlowBorder(float height)
    {
        GameObject borderObj = new GameObject("GlowBorder");
        borderObj.transform.SetParent(_popupObject.transform, false);

        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.sprite = GetPixelSprite();
        borderImg.raycastTarget = false;

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            _borderMaterial = new Material(glowShader);
            _borderMaterial.SetFloat("_StrokeEnabled", 0);
            _borderMaterial.SetFloat("_BorderWidth", UIConstants.PopupBorderWidth);
            _borderMaterial.SetFloat("_CornerRadius", UIConstants.PopupCornerRadius);
            _borderMaterial.SetFloat("_EdgePadding", UIConstants.PopupEdgePadding);
            _borderMaterial.SetFloat("_Aspect", _config.width / 100f);

            // Glow layers from UIConstants
            _borderMaterial.SetFloat("_Layer1Width", UIConstants.PopupGlowLayer1Width);
            _borderMaterial.SetFloat("_Layer1Alpha", UIConstants.PopupGlowLayer1Alpha);
            _borderMaterial.SetFloat("_Layer2Width", UIConstants.PopupGlowLayer2Width);
            _borderMaterial.SetFloat("_Layer2Alpha", UIConstants.PopupGlowLayer2Alpha);
            _borderMaterial.SetFloat("_Layer3Width", UIConstants.PopupGlowLayer3Width);
            _borderMaterial.SetFloat("_Layer3Alpha", UIConstants.PopupGlowLayer3Alpha);
            _borderMaterial.SetFloat("_Layer4Width", UIConstants.PopupGlowLayer4Width);
            _borderMaterial.SetFloat("_Layer4Alpha", UIConstants.PopupGlowLayer4Alpha);

            // Glow colors from UIConstants
            _borderMaterial.SetColor("_ColorA", UIConstants.PopupGlowColorA);
            _borderMaterial.SetColor("_ColorB", UIConstants.PopupGlowColorB);
            _borderMaterial.SetFloat("_GradientMode", 2f);
            _borderMaterial.SetFloat("_GradientAngle", -10f);
            _borderMaterial.SetFloat("_GlassAlpha", 0.02f);
            _borderMaterial.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
            _borderMaterial.SetFloat("_ShimmerSpeed", 0.4f);
            _borderMaterial.SetFloat("_ShimmerIntensity", 0.2f);
            _borderMaterial.SetFloat("_LightSize", 0.008f);
            _borderMaterial.SetFloat("_LightGlow", 0.008f);

            borderImg.material = _borderMaterial;
        }
    }

    /// <summary>
    /// Update aspect ratio for background and border materials with actual popup height.
    /// Matches RTTPopupMenu behavior for consistent border rendering.
    /// </summary>
    private void UpdateBackgroundAspect(float height)
    {
        if (_bgMaterial != null)
        {
            _bgMaterial.SetFloat("_Aspect", _config.width / height);
        }
        if (_borderMaterial != null)
        {
            _borderMaterial.SetFloat("_Aspect", _config.width / height);
        }
    }

    private void CreateContent(float totalHeight)
    {
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(_popupObject.transform, false);

        RectTransform contentRT = contentObj.AddComponent<RectTransform>();
        contentRT.anchorMin = Vector2.zero;
        contentRT.anchorMax = Vector2.one;
        contentRT.offsetMin = new Vector2(_config.padding, _config.padding);
        contentRT.offsetMax = new Vector2(-_config.padding, -_config.padding);

        float yOffset = 0;

        // Title
        yOffset = CreateTitle(contentObj.transform, yOffset);
        yOffset += _config.spacing;

        // Status text
        yOffset = CreateStatusText(contentObj.transform, yOffset);
        yOffset += _config.spacing;

        // Progress bar with percentage
        yOffset = CreateProgressBar(contentObj.transform, yOffset);
        yOffset += _config.spacing * 2; // Double spacing before buttons

        // Action buttons (Pause/Resume and Cancel)
        CreateActionButtons(contentObj.transform, yOffset);
    }

    private float CreateTitle(Transform parent, float yOffset)
    {
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(parent, false);

        RectTransform titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1);
        titleRT.sizeDelta = new Vector2(0, _config.titleHeight);
        titleRT.anchoredPosition = new Vector2(0, -yOffset);

        _titleText = titleObj.AddComponent<TextMeshProUGUI>();
        _titleText.text = "Processing...";
        _titleText.font = _config.font;
        _titleText.fontSize = _config.titleFontSize;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = Color.white;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.raycastTarget = false;

        return yOffset + _config.titleHeight;
    }

    private float CreateStatusText(Transform parent, float yOffset)
    {
        GameObject statusObj = new GameObject("StatusText");
        statusObj.transform.SetParent(parent, false);

        RectTransform statusRT = statusObj.AddComponent<RectTransform>();
        statusRT.anchorMin = new Vector2(0, 1);
        statusRT.anchorMax = new Vector2(1, 1);
        statusRT.pivot = new Vector2(0.5f, 1);
        statusRT.sizeDelta = new Vector2(0, _config.statusHeight);
        statusRT.anchoredPosition = new Vector2(0, -yOffset);

        _statusText = statusObj.AddComponent<TextMeshProUGUI>();
        _statusText.text = "Preparing...";
        _statusText.font = _config.font;
        _statusText.fontSize = _config.statusFontSize;
        _statusText.color = new Color(0.8f, 0.8f, 0.85f);
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.raycastTarget = false;
        _statusText.textWrappingMode = TextWrappingModes.NoWrap;
        _statusText.overflowMode = TextOverflowModes.Ellipsis;

        return yOffset + _config.statusHeight;
    }

    private float CreateProgressBar(Transform parent, float yOffset)
    {
        // Container for progress bar + percentage
        GameObject containerObj = new GameObject("ProgressContainer");
        containerObj.transform.SetParent(parent, false);

        RectTransform containerRT = containerObj.AddComponent<RectTransform>();
        containerRT.anchorMin = new Vector2(0, 1);
        containerRT.anchorMax = new Vector2(1, 1);
        containerRT.pivot = new Vector2(0.5f, 1);
        containerRT.sizeDelta = new Vector2(0, _config.progressBarHeight);
        containerRT.anchoredPosition = new Vector2(0, -yOffset);

        // Progress bar background
        GameObject bgObj = new GameObject("ProgressBarBg");
        bgObj.transform.SetParent(containerObj.transform, false);

        RectTransform bgRT = bgObj.AddComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0);
        bgRT.anchorMax = new Vector2(0.85f, 1); // Leave room for percentage
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.sprite = GetPixelSprite();
        bgImg.color = _config.progressBarBgColor;
        bgImg.raycastTarget = false;

        // Progress bar fill
        GameObject fillObj = new GameObject("ProgressBarFill");
        fillObj.transform.SetParent(bgObj.transform, false);

        _progressBarFillRT = fillObj.AddComponent<RectTransform>();
        _progressBarFillRT.anchorMin = Vector2.zero;
        _progressBarFillRT.anchorMax = new Vector2(0f, 1f); // Start at 0%
        _progressBarFillRT.offsetMin = Vector2.zero;
        _progressBarFillRT.offsetMax = Vector2.zero;

        Image fillImg = fillObj.AddComponent<Image>();
        fillImg.sprite = GetPixelSprite();
        fillImg.raycastTarget = false;

        // Gradient fill color
        Shader gradientShader = Shader.Find("Custom/HorizontalGradient");
        if (gradientShader != null)
        {
            Material fillMat = new Material(gradientShader);
            fillMat.SetColor("_ColorLeft", _config.primaryColor);
            fillMat.SetColor("_ColorRight", _config.accentColor);
            fillImg.material = fillMat;
            fillImg.color = Color.white;
        }
        else
        {
            fillImg.color = _config.primaryColor;
        }

        // Percentage text
        GameObject percentObj = new GameObject("PercentText");
        percentObj.transform.SetParent(containerObj.transform, false);

        RectTransform percentRT = percentObj.AddComponent<RectTransform>();
        percentRT.anchorMin = new Vector2(0.80f, 0);
        percentRT.anchorMax = new Vector2(1, 1);
        percentRT.offsetMin = Vector2.zero;
        percentRT.offsetMax = Vector2.zero;

        _percentText = percentObj.AddComponent<TextMeshProUGUI>();
        _percentText.text = "0%";
        _percentText.font = _config.font;
        _percentText.fontSize = _config.percentFontSize;
        _percentText.fontStyle = FontStyles.Bold;
        _percentText.color = Color.white;
        _percentText.alignment = TextAlignmentOptions.MidlineRight;
        _percentText.raycastTarget = false;

        return yOffset + _config.progressBarHeight;
    }

    private void CreateActionButtons(Transform parent, float yOffset)
    {
        // Calculate button width for 2-column layout (same as RTTPopupMenu)
        float availableWidth = _config.width - (_config.padding * 2);
        float buttonWidth = (availableWidth - _config.spacing) / 2f;

        // Create button container with HorizontalLayoutGroup
        GameObject containerObj = new GameObject("ButtonContainer");
        containerObj.transform.SetParent(parent, false);

        RectTransform containerRT = containerObj.AddComponent<RectTransform>();
        containerRT.anchorMin = new Vector2(0.5f, 1);
        containerRT.anchorMax = new Vector2(0.5f, 1);
        containerRT.pivot = new Vector2(0.5f, 1);
        float totalWidth = (buttonWidth * 2) + _config.spacing;
        containerRT.sizeDelta = new Vector2(totalWidth, _config.buttonHeight);
        containerRT.anchoredPosition = new Vector2(0, -yOffset);

        // Add HorizontalLayoutGroup for proper button distribution
        HorizontalLayoutGroup hlg = containerObj.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = _config.spacing;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Pause button (left)
        var pauseBtnConfig = new VRButtonFactory.ButtonConfig
        {
            label = "Pause",
            themeColor = _config.primaryColor,
            width = buttonWidth,
            height = _config.buttonHeight,
            fontSize = (int)_config.buttonFontSize,
            font = _config.font,
            textOnly = true,
            borderWidth = _config.buttonBorderWidth,
            glowWidth = _config.buttonGlowWidth,
            glowIntensity = _config.buttonGlowIntensity,
            cornerRadius = _config.buttonCornerRadius,
            popAmount = 0.05f,
            layerName = _config.layerName
        };

        GameObject pauseBtn = VRButtonFactory.CreateButton(
            containerRT,
            pauseBtnConfig,
            OnPauseClicked
        );

        RectTransform pauseBtnRT = pauseBtn.GetComponent<RectTransform>();
        pauseBtnRT.sizeDelta = new Vector2(buttonWidth, _config.buttonHeight);

        // Add LayoutElement to control size in HorizontalLayoutGroup
        LayoutElement pauseLE = pauseBtn.AddComponent<LayoutElement>();
        pauseLE.preferredWidth = buttonWidth;
        pauseLE.preferredHeight = _config.buttonHeight;

        // Get pause button text for later updates
        _pauseButtonText = pauseBtn.GetComponentInChildren<TextMeshProUGUI>();

        // Cancel button (right)
        var cancelBtnConfig = new VRButtonFactory.ButtonConfig
        {
            label = "Cancel",
            themeColor = _config.accentColor,
            width = buttonWidth,
            height = _config.buttonHeight,
            fontSize = (int)_config.buttonFontSize,
            font = _config.font,
            textOnly = true,
            borderWidth = _config.buttonBorderWidth,
            glowWidth = _config.buttonGlowWidth,
            glowIntensity = _config.buttonGlowIntensity,
            cornerRadius = _config.buttonCornerRadius,
            popAmount = 0.05f,
            layerName = _config.layerName
        };

        GameObject cancelBtn = VRButtonFactory.CreateButton(
            containerRT,
            cancelBtnConfig,
            OnCancelClicked
        );

        RectTransform cancelBtnRT = cancelBtn.GetComponent<RectTransform>();
        cancelBtnRT.sizeDelta = new Vector2(buttonWidth, _config.buttonHeight);

        // Add LayoutElement to control size in HorizontalLayoutGroup
        LayoutElement cancelLE = cancelBtn.AddComponent<LayoutElement>();
        cancelLE.preferredWidth = buttonWidth;
        cancelLE.preferredHeight = _config.buttonHeight;
    }

    private void OnPauseClicked()
    {
        _isPaused = !_isPaused;
        Debug.Log($"[RTTProgressPopup] Pause toggled: {_isPaused}");

        if (_pauseButtonText != null)
            _pauseButtonText.text = _isPaused ? "Resume" : "Pause";

        _onPauseToggle?.Invoke(_isPaused);
    }

    private void OnCancelClicked()
    {
        Debug.Log("[RTTProgressPopup] Cancel clicked");
        _onCancel?.Invoke();
    }

    #endregion

    #region Private - Overlay Methods

    private void PositionInFrontOfReference()
    {
        if (_referenceTransform == null)
        {
            Debug.LogWarning("[RTTProgressPopup] No reference transform set");
            return;
        }

        Vector3 refPosition = _referenceTransform.position;
        Vector3 refForward = _referenceTransform.forward;
        Quaternion refRotation = _referenceTransform.rotation;

        Vector3 popupPosition = refPosition - refForward * WORLD_SPACE_OFFSET;

        transform.position = popupPosition;
        transform.rotation = refRotation;
    }

    private void CreateExternalOverlays()
    {
        CreateWorldSpaceVisualOverlay();
        CreateCanvasBlockingOverlays();
    }

    private void CreateWorldSpaceVisualOverlay()
    {
        Camera mainCam = _mainCamera ?? FindMainCamera();
        if (mainCam == null) return;

        GameObject overlayObj = new GameObject("WorldSpaceProgressOverlay");

        MeshFilter meshFilter = overlayObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = overlayObj.AddComponent<MeshRenderer>();

        Mesh mesh = CreateFlatQuadMesh(20f, 20f);
        meshFilter.mesh = mesh;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("UI/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        mat.color = _config.overlayColor;
        mat.renderQueue = 3500;
        meshRenderer.material = mat;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        float distance = 2.5f;
        overlayObj.transform.position = mainCam.transform.position + mainCam.transform.forward * distance;
        overlayObj.transform.rotation = mainCam.transform.rotation;
        overlayObj.transform.SetParent(mainCam.transform);

        overlayObj.layer = LayerMask.NameToLayer("Ignore Raycast");

        _externalOverlays.Add(overlayObj);
    }

    private void CreateCanvasBlockingOverlays()
    {
        GameObject virtualObjects = GameObject.Find("VirtualObjects");
        if (virtualObjects == null) return;

        RTTCanvasBase[] allFrames = virtualObjects.GetComponentsInChildren<RTTCanvasBase>(true);

        foreach (var frame in allFrames)
        {
            Canvas frameCanvas = frame.GetCanvas();
            if (frameCanvas == null) continue;

            if (frame is RTTMobileKeyboard) continue;

            GameObject overlay = CreateInvisibleCanvasOverlay(frameCanvas, frame);
            if (overlay != null)
            {
                _externalOverlays.Add(overlay);
            }
        }
    }

    private GameObject CreateInvisibleCanvasOverlay(Canvas canvas, RTTCanvasBase frame)
    {
        int layer = LayerMask.NameToLayer(_config.layerName);

        GameObject overlayObj = new GameObject("ProgressBlockingOverlay");
        overlayObj.transform.SetParent(canvas.transform, false);
        overlayObj.transform.SetAsLastSibling();

        if (layer != -1) overlayObj.layer = layer;

        RectTransform overlayRT = overlayObj.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;

        Image overlayImg = overlayObj.AddComponent<Image>();
        overlayImg.sprite = GetPixelSprite();
        overlayImg.raycastTarget = true;
        overlayImg.color = new Color(0f, 0f, 0f, 0f);

        // No click-to-close for progress popup - user must use Cancel button

        Vector2 size = frame.GetWorldSize();
        BoxCollider col = overlayObj.AddComponent<BoxCollider>();
        col.size = new Vector3(size.x * 1000f, size.y * 1000f, 0.1f);
        col.center = Vector3.zero;

        return overlayObj;
    }

    private Mesh CreateFlatQuadMesh(float width, float height)
    {
        Mesh mesh = new Mesh();

        float halfW = width / 2f;
        float halfH = height / 2f;

        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-halfW, -halfH, 0),
            new Vector3(halfW, -halfH, 0),
            new Vector3(-halfW, halfH, 0),
            new Vector3(halfW, halfH, 0)
        };

        Vector2[] uvs = new Vector2[4]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };

        int[] triangles = new int[6] { 0, 2, 1, 2, 3, 1 };

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        return mesh;
    }

    private void DestroyExternalOverlays()
    {
        foreach (var overlay in _externalOverlays)
        {
            if (overlay != null)
            {
                MeshRenderer renderer = overlay.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material != null)
                {
                    Destroy(renderer.material);
                }
                MeshFilter filter = overlay.GetComponent<MeshFilter>();
                if (filter != null && filter.mesh != null)
                {
                    Destroy(filter.mesh);
                }
                Destroy(overlay);
            }
        }
        _externalOverlays.Clear();
    }

    #endregion

    #region Utility Methods

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;

        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }

    #endregion

    #region Unity Lifecycle

    private void OnDestroy()
    {
        if (CurrentlyOpenPopup == this)
        {
            CurrentlyOpenPopup = null;
        }

        DestroyExternalOverlays();

        if (_bgMaterial != null) Destroy(_bgMaterial);
        if (_borderMaterial != null) Destroy(_borderMaterial);
    }

    #endregion
}
