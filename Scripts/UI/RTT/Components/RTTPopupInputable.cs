using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using System;
using System.Collections.Generic;
using VRWorkspace.UI.Config;

/// <summary>
/// RTTPopupInputable - A modal popup with input field for user input.
/// Features:
/// - Dark overlay blocking all UI interactions until closed
/// - Title bar with close button
/// - Label + Input field
/// - Action button (e.g., "Create")
/// - Glass background with glow border (RTTMenuFrame style)
/// </summary>
public class RTTPopupInputable : MonoBehaviour
{
    #region Nested Classes

    /// <summary>
    /// Configuration for the popup
    /// </summary>
    [System.Serializable]
    public class PopupConfig
    {
        public string title = "CREATE FOLDER";
        public string inputLabel = "Name";
        public string inputPlaceholder = "Enter folder name";
        public string buttonText = "Create";
        public float width = 500f;
        public float padding = 20f;
        public float titleFontSize = 32;
        public float labelFontSize = 36;
        public float inputFontSize = 26;
        public float buttonFontSize = 24;
        public float buttonHeight = 60f;
        public float inputHeight = 60f;
        public float titleHeight = 50f;
        public float closeButtonSize = 75f;
        public float spacing = 10f;
        public Color primaryColor = new Color(0f, 0.9f, 1f);
        public Color accentColor = new Color(0.76f, 0.36f, 1f);
        public Color overlayColor = new Color(0f, 0f, 0f, 0.7f);
        public TMP_FontAsset font;
        public string layerName = "UI";
    }

    #endregion

    #region Private Fields

    private PopupConfig _config;
    private GameObject _overlayObject;
    private GameObject _popupObject;
    private RectTransform _popupRT;
    private TMP_InputField _inputField;
    private Action<string> _onConfirm;
    private Action _onCancel;
    private Material _bgMaterial;
    private Material _borderMaterial;
    private Material _overlayMaterial;

    // External overlays for other frames
    private List<GameObject> _externalOverlays = new List<GameObject>();

    // World-space mode fields
    private bool _isWorldSpaceMode = false;
    private Canvas _worldCanvas;
    private Camera _mainCamera;
    private Transform _referenceTransform; // Reference frame for positioning
    private const float WORLD_SPACE_OFFSET = 0.05f; // Distance in front of reference frame (meters)
    private const float WORLD_SPACE_SCALE = 0.001f; // Scale for world-space canvas (1 pixel = 0.001m)

    private static Sprite _pixelSprite;
    private const float kBorderInset = 6f;

    #endregion

    #region Public Properties

    public bool IsVisible => _popupObject != null && _popupObject.activeSelf;
    public string InputValue => _inputField != null ? _inputField.text : "";

    /// <summary>
    /// Set the default value for the input field (will be shown when popup opens)
    /// </summary>
    public void SetDefaultValue(string value)
    {
        _defaultInputValue = value;
    }
    private string _defaultInputValue = "";

    /// <summary>
    /// Static reference to currently open popup
    /// </summary>
    public static RTTPopupInputable CurrentlyOpenPopup { get; private set; }

    /// <summary>
    /// Event fired when popup is hidden
    /// </summary>
    public event Action OnHide;

    #endregion

    #region Factory Method

    /// <summary>
    /// Create a new RTTPopupInputable
    /// </summary>
    /// <param name="parent">Parent transform (should be a full-screen canvas or large container)</param>
    /// <param name="config">Popup configuration</param>
    /// <returns>RTTPopupInputable instance</returns>
    public static RTTPopupInputable Create(Transform parent, PopupConfig config)
    {
        GameObject popupObj = new GameObject("RTTPopupInputable");
        popupObj.transform.SetParent(parent, false);

        // Make the container fill the parent
        RectTransform rt = popupObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        RTTPopupInputable popup = popupObj.AddComponent<RTTPopupInputable>();
        popup._config = config ?? new PopupConfig();
        popup.BuildPopup();

        return popup;
    }

    /// <summary>
    /// Create with default config
    /// </summary>
    public static RTTPopupInputable Create(Transform parent, string title, Color primaryColor, TMP_FontAsset font = null)
    {
        var config = new PopupConfig
        {
            title = title,
            primaryColor = primaryColor,
            font = font
        };
        return Create(parent, config);
    }

    /// <summary>
    /// Create a world-space RTTPopupInputable that floats in front of a reference frame.
    /// This mode positions the popup independently in 3D space like RTTMobileKeyboard,
    /// allowing it to appear in front of overlay effects.
    /// The popup is fixed in world space (does not follow camera).
    /// </summary>
    /// <param name="config">Popup configuration</param>
    /// <param name="referenceFrame">Reference transform to position in front of (e.g., RTTMenuFrame)</param>
    /// <returns>RTTPopupInputable instance in world-space mode</returns>
    public static RTTPopupInputable CreateWorldSpace(PopupConfig config, Transform referenceFrame = null)
    {
        config = config ?? new PopupConfig();

        // Create root object
        GameObject rootObj = new GameObject("RTTPopupInputable_WorldSpace");

        // Find main camera for canvas worldCamera
        Camera mainCam = FindMainCamera();

        // Calculate popup dimensions for canvas sizing
        float totalHeight = config.padding * 2
            + config.titleHeight
            + config.spacing * 0.5f
            + config.labelFontSize * 1.5f
            + config.spacing * 0.5f
            + config.inputHeight
            + config.spacing
            + config.buttonHeight;

        // Create world-space Canvas
        Canvas canvas = rootObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = mainCam;
        canvas.sortingOrder = 100; // Higher sorting order to render above overlay

        // Set canvas size to match popup dimensions (with some margin)
        RectTransform canvasRT = rootObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(config.width + 40f, totalHeight + 40f);
        canvasRT.localScale = Vector3.one * WORLD_SPACE_SCALE;

        // Add GraphicRaycaster for UI interaction
        rootObj.AddComponent<GraphicRaycaster>();

        // Add CanvasScaler for consistent sizing
        CanvasScaler scaler = rootObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // Set layer for VR interaction
        int layer = LayerMask.NameToLayer(config.layerName);
        if (layer != -1)
        {
            rootObj.layer = layer;
        }

        // Create popup component
        RTTPopupInputable popup = rootObj.AddComponent<RTTPopupInputable>();
        popup._config = config;
        popup._isWorldSpaceMode = true;
        popup._worldCanvas = canvas;
        popup._mainCamera = mainCam;
        popup._referenceTransform = referenceFrame;

        popup.BuildWorldSpacePopup();

        // Position will be set when Show() is called (needs reference frame to be active)
        // Hidden by default
        rootObj.SetActive(false);

        Debug.Log($"[RTTPopupInputable] Created world-space popup: {config.width}x{totalHeight}");

        return popup;
    }

    /// <summary>
    /// Find the main camera (VR center eye or main camera)
    /// </summary>
    private static Camera FindMainCamera()
    {
        // Try to find camera by specific names used in VR apps
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

        // Fallback to Camera.main
        if (Camera.main != null) return Camera.main;

        // Last resort: find any camera that's not UICamera
        Camera[] allCameras = GameObject.FindObjectsOfType<Camera>();
        foreach (var cam in allCameras)
        {
            if (!cam.name.Contains("UI") && cam.gameObject.activeInHierarchy)
            {
                return cam;
            }
        }

        return null;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Show the popup with callbacks
    /// </summary>
    /// <param name="onConfirm">Called with input value when confirmed</param>
    /// <param name="onCancel">Called when cancelled or closed</param>
    public void Show(Action<string> onConfirm = null, Action onCancel = null)
    {
        _onConfirm = onConfirm;
        _onCancel = onCancel;

        // Close any other open popup first
        if (CurrentlyOpenPopup != null && CurrentlyOpenPopup != this)
        {
            CurrentlyOpenPopup.Hide();
        }

        // Set input value (use default value if set, otherwise clear)
        if (_inputField != null)
        {
            _inputField.text = _defaultInputValue;
            // Clear default value after use
            _defaultInputValue = "";
        }

        if (_isWorldSpaceMode)
        {
            // Position popup in front of reference frame (fixed in world space)
            PositionInFrontOfReference();

            // World-space mode: activate the entire GameObject
            gameObject.SetActive(true);

            // Create overlays (world-space visual + canvas blocking)
            CreateExternalOverlays();

            Debug.Log($"[RTTPopupInputable] Shown in world-space mode, fixed position in front of reference frame");
        }
        else
        {
            // RTT canvas mode: activate overlay and popup within canvas
            transform.SetAsLastSibling();

            // Create overlays on all other frames in VirtualObjects
            CreateExternalOverlays();

            // Show overlay and popup
            if (_overlayObject != null) _overlayObject.SetActive(true);
            if (_popupObject != null) _popupObject.SetActive(true);
        }

        CurrentlyOpenPopup = this;

        // Focus input field
        if (_inputField != null)
        {
            _inputField.Select();
            _inputField.ActivateInputField();
        }
    }

    /// <summary>
    /// Hide the popup
    /// </summary>
    public void Hide()
    {
        // Destroy external overlays on other frames
        DestroyExternalOverlays();

        if (_isWorldSpaceMode)
        {
            // World-space mode: deactivate the entire GameObject
            gameObject.SetActive(false);
        }
        else
        {
            // RTT canvas mode: hide overlay and popup
            if (_overlayObject != null) _overlayObject.SetActive(false);
            if (_popupObject != null) _popupObject.SetActive(false);
        }

        if (CurrentlyOpenPopup == this)
        {
            CurrentlyOpenPopup = null;
        }

        OnHide?.Invoke();
    }

    /// <summary>
    /// Set input field placeholder text
    /// </summary>
    public void SetPlaceholder(string placeholder)
    {
        if (_inputField != null && _inputField.placeholder is TMP_Text placeholderText)
        {
            placeholderText.text = placeholder;
        }
    }

    /// <summary>
    /// Set title text
    /// </summary>
    public void SetTitle(string title)
    {
        var titleText = _popupObject?.transform.Find("TitleBar/TitleText")?.GetComponent<TextMeshProUGUI>();
        if (titleText != null)
        {
            titleText.text = title;
        }
    }

    #endregion

    #region Private - Build Methods

    private void BuildPopup()
    {
        int layer = LayerMask.NameToLayer(_config.layerName);

        // 1. Create dark overlay (covers entire parent)
        CreateOverlay(layer);

        // 2. Create popup panel (centered)
        CreatePopupPanel(layer);

        // Hidden by default
        _overlayObject.SetActive(false);
        _popupObject.SetActive(false);
    }

    /// <summary>
    /// Build popup UI for world-space mode.
    /// In this mode, no local overlay is needed - popup floats in world space.
    /// </summary>
    private void BuildWorldSpacePopup()
    {
        int layer = LayerMask.NameToLayer(_config.layerName);

        // In world-space mode, we don't need a local overlay
        // The popup panel is created directly on the canvas
        _overlayObject = null;

        // Create popup panel centered on canvas
        CreatePopupPanelWorldSpace(layer);

        // Add BoxCollider for VR raycast on the root canvas
        BoxCollider canvasCollider = gameObject.AddComponent<BoxCollider>();
        // Size based on popup dimensions
        float totalHeight = CalculateTotalHeight();
        canvasCollider.size = new Vector3(_config.width, totalHeight, 0.1f);
        canvasCollider.center = Vector3.zero;

        // Apply layer to entire hierarchy including root canvas
        if (layer != -1) SetLayerRecursively(gameObject, layer);
    }

    /// <summary>
    /// Create popup panel for world-space mode (no anchor filling, just centered)
    /// </summary>
    private void CreatePopupPanelWorldSpace(int layer)
    {
        float totalHeight = CalculateTotalHeight();

        _popupObject = new GameObject("PopupPanel");
        _popupObject.transform.SetParent(transform, false);

        _popupRT = _popupObject.AddComponent<RectTransform>();
        // Center in canvas
        _popupRT.anchorMin = new Vector2(0.5f, 0.5f);
        _popupRT.anchorMax = new Vector2(0.5f, 0.5f);
        _popupRT.pivot = new Vector2(0.5f, 0.5f);
        _popupRT.sizeDelta = new Vector2(_config.width, totalHeight);
        _popupRT.anchoredPosition = Vector2.zero;

        // Background
        CreateBackground();

        // Glow Border
        CreateGlowBorder(totalHeight);

        // Content
        CreateContent(totalHeight);

        // BoxCollider for VR raycast
        BoxCollider popupCol = _popupObject.AddComponent<BoxCollider>();
        popupCol.size = new Vector3(_config.width, totalHeight, 0.1f);
        popupCol.center = Vector3.zero;

        // Apply layer AFTER all content is created
        if (layer != -1) SetLayerRecursively(_popupObject, layer);
    }

    /// <summary>
    /// Calculate total popup height based on config
    /// </summary>
    private float CalculateTotalHeight()
    {
        return _config.padding * 2
            + _config.titleHeight
            + _config.spacing * 0.5f
            + _config.labelFontSize * 1.5f
            + _config.spacing * 0.5f
            + _config.inputHeight
            + _config.spacing
            + _config.buttonHeight;
    }

    private void CreateOverlay(int layer)
    {
        // Local overlay is now invisible - world-space overlay handles visual darkening
        // This overlay only provides click-to-close functionality and interaction blocking on this canvas
        _overlayObject = new GameObject("LocalOverlay");
        _overlayObject.transform.SetParent(transform, false);

        if (layer != -1) _overlayObject.layer = layer;

        // Fill entire parent
        RectTransform overlayRT = _overlayObject.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;

        // Invisible image for raycast target only (no visual)
        Image overlayImg = _overlayObject.AddComponent<Image>();
        overlayImg.sprite = GetPixelSprite();
        overlayImg.raycastTarget = true;
        overlayImg.color = new Color(0f, 0f, 0f, 0f); // Fully transparent

        // Click overlay to close
        Button overlayBtn = _overlayObject.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlayBtn.onClick.AddListener(OnOverlayClicked);

        // BoxCollider for VR raycast blocking on this canvas
        BoxCollider overlayCol = _overlayObject.AddComponent<BoxCollider>();
        overlayCol.size = new Vector3(3000f, 2000f, 0.1f);
        overlayCol.center = Vector3.zero;
    }

    /// <summary>
    /// Create overlays:
    /// 1. World-space visual overlay (darkens entire view)
    /// 2. Invisible collider overlays on other RTT canvases (blocks interactions)
    /// </summary>
    private void CreateExternalOverlays()
    {
        // 1. Create world-space visual overlay
        CreateWorldSpaceVisualOverlay();

        // 2. Create invisible collider overlays on other RTT canvases
        CreateCanvasBlockingOverlays();
    }

    private void CreateWorldSpaceVisualOverlay()
    {
        // Find the correct main camera (not UICamera used for RTT)
        Camera mainCam = null;

        // Try to find camera by specific names used in VR apps
        string[] cameraNames = { "CenterEyeAnchor", "Main Camera", "PlayerCamera", "Camera" };
        foreach (var name in cameraNames)
        {
            GameObject camObj = GameObject.Find(name);
            if (camObj != null)
            {
                mainCam = camObj.GetComponent<Camera>();
                if (mainCam != null && mainCam.gameObject.activeInHierarchy) break;
            }
        }

        // Fallback to Camera.main
        if (mainCam == null)
        {
            mainCam = Camera.main;
        }

        // Last resort: find any camera that's not UICamera
        if (mainCam == null)
        {
            Camera[] allCameras = GameObject.FindObjectsOfType<Camera>();
            foreach (var cam in allCameras)
            {
                if (!cam.name.Contains("UI") && cam.gameObject.activeInHierarchy)
                {
                    mainCam = cam;
                    break;
                }
            }
        }

        if (mainCam == null)
        {
            Debug.LogWarning("[RTTPopupInputable] Could not find main camera for world overlay");
            return;
        }

        Debug.Log($"[RTTPopupInputable] Creating world overlay using camera: {mainCam.name}");

        // Create world-space overlay quad (visual only)
        GameObject overlayObj = CreateWorldSpaceOverlay(mainCam);
        if (overlayObj != null)
        {
            _externalOverlays.Add(overlayObj);
        }
    }

    private void CreateCanvasBlockingOverlays()
    {
        // Find VirtualObjects container
        GameObject virtualObjects = GameObject.Find("VirtualObjects");
        if (virtualObjects == null) return;

        // Find all RTTCanvasBase (includes RTTMenuFrame and RTTMiniFrame)
        RTTCanvasBase[] allFrames = virtualObjects.GetComponentsInChildren<RTTCanvasBase>(true);

        foreach (var frame in allFrames)
        {
            // Skip if this is the frame containing our popup
            Canvas frameCanvas = frame.GetCanvas();
            if (frameCanvas == null) continue;
            if (frameCanvas.transform == transform.parent) continue;

            // Skip RTTMobileKeyboard - it should remain interactive when popup is shown
            if (frame is RTTMobileKeyboard) continue;

            // Create invisible overlay with collider on this frame's canvas
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

        GameObject overlayObj = new GameObject("PopupBlockingOverlay");
        overlayObj.transform.SetParent(canvas.transform, false);
        overlayObj.transform.SetAsLastSibling(); // Render on top

        if (layer != -1) overlayObj.layer = layer;

        // Fill entire canvas
        RectTransform overlayRT = overlayObj.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;

        // Invisible image for raycast target
        Image overlayImg = overlayObj.AddComponent<Image>();
        overlayImg.sprite = GetPixelSprite();
        overlayImg.raycastTarget = true;
        overlayImg.color = new Color(0f, 0f, 0f, 0f); // Fully transparent

        // BoxCollider for VR raycast blocking
        Vector2 size = frame.GetWorldSize();
        BoxCollider col = overlayObj.AddComponent<BoxCollider>();
        col.size = new Vector3(size.x * 1000f, size.y * 1000f, 0.1f); // Scale to logical pixels
        col.center = Vector3.zero;

        return overlayObj;
    }

    private GameObject CreateWorldSpaceOverlay(Camera camera)
    {
        GameObject overlayObj = new GameObject("WorldSpacePopupOverlay");

        MeshFilter meshFilter = overlayObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = overlayObj.AddComponent<MeshRenderer>();

        // Create flat quad mesh
        Mesh mesh = CreateFlatQuadMesh(20f, 20f); // Large flat quad
        meshFilter.mesh = mesh;

        // Create semi-transparent dark material
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("UI/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        mat.color = _config.overlayColor;
        // Render above most objects but below popup/inputable/keyboard canvases
        mat.renderQueue = 3500;
        meshRenderer.material = mat;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        // Position flat quad in front of camera
        float distance = 2.5f;
        overlayObj.transform.position = camera.transform.position + camera.transform.forward * distance;
        overlayObj.transform.rotation = camera.transform.rotation;

        // Parent to camera so it follows head movement
        overlayObj.transform.SetParent(camera.transform);

        // Set to Ignore Raycast layer so it doesn't block reticle
        overlayObj.layer = LayerMask.NameToLayer("Ignore Raycast");

        Debug.Log($"[RTTPopupInputable] Flat world overlay created at distance {distance}");

        return overlayObj;
    }

    private Mesh CreateFlatQuadMesh(float width, float height)
    {
        Mesh mesh = new Mesh();

        float halfW = width / 2f;
        float halfH = height / 2f;

        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-halfW, -halfH, 0), // bottom-left
            new Vector3(halfW, -halfH, 0),  // bottom-right
            new Vector3(-halfW, halfH, 0),  // top-left
            new Vector3(halfW, halfH, 0)    // top-right
        };

        Vector2[] uvs = new Vector2[4]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };

        int[] triangles = new int[6]
        {
            0, 2, 1, // first triangle
            2, 3, 1  // second triangle
        };

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
                // Destroy material to prevent memory leak
                MeshRenderer renderer = overlay.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material != null)
                {
                    Destroy(renderer.material);
                }
                // Destroy mesh
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

    private void CreatePopupPanel(int layer)
    {
        // Calculate total height
        float totalHeight = _config.padding * 2 // Top and bottom padding
            + _config.titleHeight // Title bar
            + _config.spacing * 0.5f // After title
            + _config.labelFontSize * 1.5f // Label
            + _config.spacing * 0.5f // After label
            + _config.inputHeight // Input field
            + _config.spacing // After input
            + _config.buttonHeight; // Button

        _popupObject = new GameObject("PopupPanel");
        _popupObject.transform.SetParent(transform, false);

        if (layer != -1) SetLayerRecursively(_popupObject, layer);

        _popupRT = _popupObject.AddComponent<RectTransform>();
        // Center in parent
        _popupRT.anchorMin = new Vector2(0.5f, 0.5f);
        _popupRT.anchorMax = new Vector2(0.5f, 0.5f);
        _popupRT.pivot = new Vector2(0.5f, 0.5f);
        _popupRT.sizeDelta = new Vector2(_config.width, totalHeight);
        _popupRT.anchoredPosition = Vector2.zero;

        // No nested Canvas - rely on sibling order within parent Canvas
        // Popup is created after overlay, so it renders on top

        // Background
        CreateBackground();

        // Glow Border
        CreateGlowBorder(totalHeight);

        // Content
        CreateContent(totalHeight);

        // BoxCollider for VR raycast (block rays from passing through)
        BoxCollider popupCol = _popupObject.AddComponent<BoxCollider>();
        popupCol.size = new Vector3(_config.width, totalHeight, 0.1f);
        popupCol.center = Vector3.zero;
    }

    private void CreateBackground()
    {
        Image bgImg = _popupObject.AddComponent<Image>();
        bgImg.sprite = GetPixelSprite();

        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            _bgMaterial = new Material(glassShader);
            _bgMaterial.SetFloat("_CornerRadius", UIConstants.PopupCornerRadius);
            _bgMaterial.SetFloat("_EdgePadding", UIConstants.PopupEdgePadding);
            _bgMaterial.SetFloat("_Aspect", _config.width / 300f);

            // Glass colors from UIConstants
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
            bgImg.color = new Color(0.12f, 0.12f, 0.16f, 0.7f); // Lighter fallback
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
            _borderMaterial.SetFloat("_Aspect", _config.width / height);

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

            borderImg.material = _borderMaterial;
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

        // Title Bar (Title + Close Button)
        yOffset = CreateTitleBar(contentObj.transform, yOffset);

        // Spacing
        yOffset += _config.spacing * 0.5f;

        // Label
        yOffset = CreateLabel(contentObj.transform, yOffset);

        // Small spacing
        yOffset += _config.spacing * 0.5f;

        // Input Field
        yOffset = CreateInputField(contentObj.transform, yOffset);

        // Spacing
        yOffset += _config.spacing;

        // Action Button
        CreateActionButton(contentObj.transform, yOffset);
    }

    private float CreateTitleBar(Transform parent, float yOffset)
    {
        GameObject titleBar = new GameObject("TitleBar");
        titleBar.transform.SetParent(parent, false);

        RectTransform titleBarRT = titleBar.AddComponent<RectTransform>();
        titleBarRT.anchorMin = new Vector2(0, 1);
        titleBarRT.anchorMax = new Vector2(1, 1);
        titleBarRT.pivot = new Vector2(0.5f, 1);
        titleBarRT.sizeDelta = new Vector2(0, _config.titleHeight);
        titleBarRT.anchoredPosition = new Vector2(0, -yOffset);

        // Title Text
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(titleBar.transform, false);

        RectTransform titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = Vector2.zero;
        titleRT.offsetMax = Vector2.zero; // Full width - title centered across popup, not affected by close button

        TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = _config.title;
        titleText.font = _config.font;
        titleText.fontSize = _config.titleFontSize;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = Color.white;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.raycastTarget = false;

        // Close Button (X)
        CreateCloseButton(titleBar.transform);

        return yOffset + _config.titleHeight;
    }

    private void CreateCloseButton(Transform parent)
    {
        Sprite closeIcon = Resources.Load<Sprite>("icon_close");

        GameObject closeBtn = VRButtonFactory.CreateBareIconButton(
            parent,
            _config.closeButtonSize,
            closeIcon,
            _config.accentColor,
            OnCloseClicked
        );

        RectTransform closeRT = closeBtn.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(1, 0.5f);
        closeRT.anchorMax = new Vector2(1, 0.5f);
        closeRT.pivot = new Vector2(1, 0.5f);
        closeRT.anchoredPosition = new Vector2(0, 0);
    }

    private float CreateLabel(Transform parent, float yOffset)
    {
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(parent, false);

        float labelHeight = _config.labelFontSize * 1.5f;

        RectTransform labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 1);
        labelRT.anchorMax = new Vector2(1, 1);
        labelRT.pivot = new Vector2(0.5f, 1);
        labelRT.sizeDelta = new Vector2(0, labelHeight);
        labelRT.anchoredPosition = new Vector2(0, -yOffset);

        TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text = _config.inputLabel;
        labelText.font = _config.font;
        labelText.fontSize = _config.labelFontSize;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = new Color(1f, 1f, 1f, 1f);
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.raycastTarget = false;

        return yOffset + labelHeight;
    }

    private float CreateInputField(Transform parent, float yOffset)
    {
        var inputConfig = new VRInputFieldFactory.InputFieldConfig
        {
            label = "",
            placeholder = _config.inputPlaceholder,
            width = _config.width - _config.padding * 2,
            themeColor = _config.primaryColor,
            inputFontSize = (int)_config.inputFontSize,
            font = _config.font,
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            layerName = _config.layerName
        };

        GameObject inputObj = VRInputFieldFactory.CreateInputField(
            parent as RectTransform,
            inputConfig,
            null,
            OnInputEndEdit
        );

        RectTransform inputRT = inputObj.GetComponent<RectTransform>();
        inputRT.anchorMin = new Vector2(0, 1);
        inputRT.anchorMax = new Vector2(1, 1);
        inputRT.pivot = new Vector2(0.5f, 1);
        inputRT.sizeDelta = new Vector2(0, _config.inputHeight);
        inputRT.anchoredPosition = new Vector2(0, -yOffset);

        _inputField = inputObj.GetComponentInChildren<TMP_InputField>();

        return yOffset + _config.inputHeight;
    }

    private void CreateActionButton(Transform parent, float yOffset)
    {
        var btnConfig = new VRButtonFactory.ButtonConfig
        {
            label = _config.buttonText,
            themeColor = _config.accentColor,
            width = _config.width - _config.padding * 2,
            height = _config.buttonHeight,
            fontSize = (int)_config.buttonFontSize,
            font = _config.font,
            textOnly = true,
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };

        GameObject actionBtn = VRButtonFactory.CreateButton(
            parent as RectTransform,
            btnConfig,
            OnConfirmClicked
        );

        RectTransform btnRT = actionBtn.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0, 1);
        btnRT.anchorMax = new Vector2(1, 1);
        btnRT.pivot = new Vector2(0.5f, 1);
        btnRT.sizeDelta = new Vector2(0, _config.buttonHeight);
        btnRT.anchoredPosition = new Vector2(0, -yOffset);
    }

    #endregion

    #region Event Handlers

    private void OnCloseClicked()
    {
        _onCancel?.Invoke();
        Hide();
    }

    private void OnOverlayClicked()
    {
        // Optional: close on overlay click
        // Comment out the next two lines if you don't want this behavior
        _onCancel?.Invoke();
        Hide();
    }

    private void OnConfirmClicked()
    {
        string value = _inputField != null ? _inputField.text : "";

        // Don't confirm if input is empty
        if (string.IsNullOrWhiteSpace(value))
        {
            // Optionally show error or shake animation
            return;
        }

        _onConfirm?.Invoke(value);
        Hide();
    }

    private void OnInputEndEdit(string value)
    {
        // Submit on Enter key
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            OnConfirmClicked();
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Position the popup in front of the reference frame (fixed in world space).
    /// The popup maintains the same orientation as the reference frame.
    /// </summary>
    private void PositionInFrontOfReference()
    {
        if (_referenceTransform == null)
        {
            Debug.LogWarning("[RTTPopupInputable] No reference transform set, popup will stay at origin");
            return;
        }

        // Get reference frame's world position and forward direction
        Vector3 refPosition = _referenceTransform.position;
        Vector3 refForward = _referenceTransform.forward;
        Quaternion refRotation = _referenceTransform.rotation;

        // Position popup slightly in front of reference frame (toward camera/viewer)
        // Reference frame's forward points away from viewer, so we subtract to move toward viewer
        Vector3 popupPosition = refPosition - refForward * WORLD_SPACE_OFFSET;

        // Apply position and rotation (same orientation as reference)
        transform.position = popupPosition;
        transform.rotation = refRotation;

        Debug.Log($"[RTTPopupInputable] Positioned at {popupPosition}, offset {WORLD_SPACE_OFFSET}m in front of {_referenceTransform.name}");
    }

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

        // Clean up external overlays
        DestroyExternalOverlays();

        // Clean up materials
        if (_bgMaterial != null) Destroy(_bgMaterial);
        if (_borderMaterial != null) Destroy(_borderMaterial);
        if (_overlayMaterial != null) Destroy(_overlayMaterial);
    }

    #endregion
}
