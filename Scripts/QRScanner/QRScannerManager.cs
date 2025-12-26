using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// QR Scanner Manager - Manages full-screen QR scanning mode
/// - Hides VRMenuFrame and VRTaskbar when active
/// - Shows passthrough frame with VRMenuFrame-style border
/// - Shows cancel button at taskbar position
/// - Passthrough frame follows camera
/// - Cancel button follows camera horizontally only (fixed vertical)
/// </summary>
public class QRScannerManager : MonoBehaviour
{
    [Header("Theme")]
    public Color themeColor = new Color(0.0f, 0.9f, 1.0f);
    public Color accentColor = new Color(0.8f, 0.4f, 1.0f);
    public TMP_FontAsset customFont;

    [Header("Scan Settings")]
    public float scanInterval = 0.2f;

    [Header("Position Settings")]
    [Tooltip("Distance from camera for passthrough frame")]
    public float frameDistance = 1.5f;

    // Events
    public event Action<QRScannerConfig> OnQRScanned;
    public event Action OnCancelled;

    // References to hide/show
    private RTTMenuFrame _rttMenuFrame;
    private RTTTaskbar _rttTaskbar;

    // Created objects
    private GameObject _passthroughFrame;
    private GameObject _cancelButtonFrame;
    private Canvas _passthroughCanvas;
    private Canvas _cancelCanvas;

    // Camera reference
    private RawImage _cameraPreview;
    private WebCamTexture _webCamTexture;
    private bool _isScanning;
    private Coroutine _scanCoroutine;

    // Scan UI elements
    private RectTransform _scanFrame;
    private RectTransform _scanLineRT;
    private Coroutine _scanLineCoroutine;
    private TextMeshProUGUI _statusText;

    // Position tracking
    private Vector3 _initialFramePosition;
    private Quaternion _initialFrameRotation;
    private float _cancelButtonWorldY; // Fixed world Y position
    private float _cancelButtonDistance; // Horizontal distance from camera
    private Camera _mainCamera;

    // Frame dimensions (matching VRMenuFrame)
    private float _frameWidth = 1.6f;
    private float _frameHeight = 0.9f;
    private float _logicalWidth = 1920f;

    // Cancel button dimensions (same as QR button in VRRemoteMenu)
    private float _cancelButtonSize = 100f;

    // Scale factor (matching VRMenuFrame)
    private float ScaleFactor => _frameWidth / _logicalWidth;

    /// <summary>
    /// Start QR scanning mode with RTTMenuFrame and RTTTaskbar
    /// </summary>
    public void StartScanning(RTTMenuFrame rttMenuFrame, RTTTaskbar rttTaskbar)
    {
        _rttMenuFrame = rttMenuFrame;
        _rttTaskbar = rttTaskbar;
        _mainCamera = Camera.main;

        // Store initial positions and dimensions from RTTMenuFrame
        if (_rttMenuFrame != null)
        {
            _initialFramePosition = _rttMenuFrame.transform.position;
            _initialFrameRotation = _rttMenuFrame.transform.rotation;
            _frameWidth = _rttMenuFrame.PanelWidth;
            _frameHeight = _rttMenuFrame.PanelHeight;
            _logicalWidth = _rttMenuFrame.LogicalWidthValue;

            // Calculate frameDistance from actual RTTMenuFrame position
            if (_mainCamera != null)
            {
                Vector3 toFrame = _initialFramePosition - _mainCamera.transform.position;
                frameDistance = toFrame.magnitude;
            }
        }

        StartScanningInternal();
    }

    private void StartScanningInternal()
    {
        // Find RTTTaskbar if not set
        if (_rttTaskbar == null)
            _rttTaskbar = FindObjectOfType<RTTTaskbar>();

        // Calculate cancel button position
        _cancelButtonDistance = frameDistance;

        // Use RTTTaskbar Y position if available, otherwise calculate
        if (_rttTaskbar != null)
        {
            _cancelButtonWorldY = _rttTaskbar.transform.position.y;
        }
        else
        {
            // Fallback: position below the QR frame
            Vector3 cancelWorldPos = _mainCamera.transform.position + _mainCamera.transform.forward * frameDistance
                                     - _mainCamera.transform.up * (_frameHeight / 2f + 0.15f);
            _cancelButtonWorldY = cancelWorldPos.y;
        }

        // Hide original UI
        HideOriginalUI();

        // Create passthrough frame (parented to camera)
        CreatePassthroughFrame();

        // Create cancel button (parented to camera)
        CreateCancelButton();

        // Start camera
        StartCoroutine(StartCamera());
    }

    /// <summary>
    /// Stop QR scanning mode and restore original UI
    /// </summary>
    public void StopScanning()
    {
        _isScanning = false;

        if (_scanCoroutine != null)
        {
            StopCoroutine(_scanCoroutine);
            _scanCoroutine = null;
        }

        // Stop camera
        if (_webCamTexture != null)
        {
            _webCamTexture.Stop();
            Destroy(_webCamTexture);
            _webCamTexture = null;
        }

        // Destroy created objects
        if (_passthroughFrame != null)
        {
            Destroy(_passthroughFrame);
            _passthroughFrame = null;
        }

        if (_cancelButtonFrame != null)
        {
            Destroy(_cancelButtonFrame);
            _cancelButtonFrame = null;
        }

        // Show original UI
        ShowOriginalUI();
    }

    void HideOriginalUI()
    {
        if (_rttMenuFrame != null)
            _rttMenuFrame.Hide();

        if (_rttTaskbar != null)
            _rttTaskbar.Hide();
    }

    void ShowOriginalUI()
    {
        if (_rttMenuFrame != null)
            _rttMenuFrame.Show();

        if (_rttTaskbar != null)
            _rttTaskbar.Show();
    }

    void CreatePassthroughFrame()
    {
        if (_mainCamera == null) _mainCamera = Camera.main;

        // Create root object - PARENTED TO CAMERA like Reticle
        _passthroughFrame = new GameObject("QRPassthroughFrame");
        _passthroughFrame.transform.SetParent(_mainCamera.transform, false);

        // Set local position in front of camera, centered on reticle (camera forward)
        _passthroughFrame.transform.localPosition = new Vector3(0, 0, frameDistance);
        _passthroughFrame.transform.localRotation = Quaternion.identity;

        // Add Canvas - lower sorting order so Reticle shows on top
        _passthroughCanvas = _passthroughFrame.AddComponent<Canvas>();
        _passthroughCanvas.renderMode = RenderMode.WorldSpace;
        _passthroughCanvas.sortingOrder = 100; // Lower than Reticle (30000)

        _passthroughFrame.AddComponent<GraphicRaycaster>();

        // Setup RectTransform - MATCH VRMenuFrame SIZE EXACTLY
        RectTransform canvasRT = _passthroughFrame.GetComponent<RectTransform>();
        float logicalHeight = (_logicalWidth / _frameWidth) * _frameHeight;
        canvasRT.sizeDelta = new Vector2(_logicalWidth, logicalHeight);
        canvasRT.localScale = new Vector3(ScaleFactor, ScaleFactor, 1f);

        // Create camera preview container FIRST (so it's behind)
        CreateCameraContainer(_passthroughFrame.transform, _logicalWidth, logicalHeight);

        // Create border LAST (on top of camera container) - no background
        CreateBorderOnly(_passthroughFrame.transform, _logicalWidth, logicalHeight);

        // Add BoxCollider for Reticle collision
        BoxCollider frameCol = _passthroughFrame.AddComponent<BoxCollider>();
        frameCol.size = new Vector3(_logicalWidth, logicalHeight, 0.01f);
        frameCol.center = Vector3.zero;

        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) _passthroughFrame.layer = vrLayer;
    }

    /// <summary>
    /// Create only the glowing border (no background) - displayed on top of camera container
    /// </summary>
    void CreateBorderOnly(Transform parent, float w, float h)
    {
        float glowExpansion = 0.02f;
        float edgePad = glowExpansion / (1f + 2f * glowExpansion);
        float aspect = w / h;

        GameObject borderObj = new GameObject("GlowingBorder");
        borderObj.transform.SetParent(parent, false);

        RectTransform rt = borderObj.AddComponent<RectTransform>();
        // Expand to cover glow area
        rt.anchorMin = new Vector2(-glowExpansion, -glowExpansion);
        rt.anchorMax = new Vector2(1f + glowExpansion, 1f + glowExpansion);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;
        borderImg.sprite = CreatePixelSprite();

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);

            glowMat.SetFloat("_StrokeEnabled", 0);
            glowMat.SetFloat("_BorderWidth", 0.02f);
            glowMat.SetFloat("_CornerRadius", 0.12f);
            glowMat.SetFloat("_EdgePadding", edgePad);
            glowMat.SetFloat("_Aspect", aspect);

            glowMat.SetFloat("_Layer1Width", 0.008f);
            glowMat.SetFloat("_Layer1Alpha", 1.5f);
            glowMat.SetFloat("_Layer2Width", 0.018f);
            glowMat.SetFloat("_Layer2Alpha", 1.0f);
            glowMat.SetFloat("_Layer3Width", 0.04f);
            glowMat.SetFloat("_Layer3Alpha", 0.6f);
            glowMat.SetFloat("_Layer4Width", 0.08f);
            glowMat.SetFloat("_Layer4Alpha", 0.3f);

            Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
            Color purpleColor = new Color(1f, 0.4f, 1f, 1f);
            glowMat.SetColor("_ColorA", cyanColor);
            glowMat.SetColor("_ColorB", purpleColor);
            glowMat.SetFloat("_GradientMode", 2f);
            glowMat.SetFloat("_GradientAngle", -10f);
            glowMat.SetFloat("_GlassAlpha", 0f); // No glass fill
            glowMat.SetFloat("_ShimmerSpeed", 0.1f);
            glowMat.SetFloat("_ShimmerIntensity", 0.2f);

            borderImg.material = glowMat;
        }

        // Ensure border is on top
        borderObj.transform.SetAsLastSibling();
    }

    void CreateGlassPanel(Transform parent, float w, float h)
    {
        float glowExpansion = 0.02f;
        float edgePad = glowExpansion / (1f + 2f * glowExpansion);
        float aspect = w / h;

        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);
        Image img = bgObj.AddComponent<Image>();
        img.type = Image.Type.Simple;
        img.sprite = CreatePixelSprite();

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", 0.12f);
            glassMat.SetFloat("_EdgePadding", edgePad);
            glassMat.SetFloat("_Aspect", aspect);

            Color cyanGlass = new Color(0.35f, 0.9f, 1f, 0.05f);
            Color purpleGlass = new Color(0.75f, 0.45f, 1f, 0.08f);
            glassMat.SetColor("_ColorA", cyanGlass);
            glassMat.SetColor("_ColorB", purpleGlass);
            glassMat.SetFloat("_GradientOffset", 0f);
            glassMat.SetFloat("_GradientAngle", -10f);
            glassMat.SetFloat("_CyanRatio", 0.7f);
            glassMat.SetFloat("_GlassAlpha", 0.02f);

            img.material = glassMat;
            img.color = Color.white;
        }

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(-glowExpansion, -glowExpansion);
        rt.anchorMax = new Vector2(1f + glowExpansion, 1f + glowExpansion);
        rt.sizeDelta = Vector2.zero;
        rt.SetAsFirstSibling();

        // Add BoxCollider for VR interaction
        float expandedW = w * (1f + 2f * glowExpansion);
        float expandedH = h * (1f + 2f * glowExpansion);
        BoxCollider bgCol = bgObj.AddComponent<BoxCollider>();
        bgCol.size = new Vector3(expandedW, expandedH, 0.01f);
        bgCol.center = Vector3.zero;

        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) bgObj.layer = vrLayer;

        // Create glowing border
        CreateGlowingBorder(bgObj.transform, w, h, edgePad);
    }

    void CreateGlowingBorder(Transform parent, float w, float h, float edgePad)
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
        borderImg.sprite = CreatePixelSprite();

        float aspect = w / h;

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);

            glowMat.SetFloat("_StrokeEnabled", 0);
            glowMat.SetFloat("_BorderWidth", 0.02f);
            glowMat.SetFloat("_CornerRadius", 0.12f);
            glowMat.SetFloat("_EdgePadding", edgePad);
            glowMat.SetFloat("_Aspect", aspect);

            glowMat.SetFloat("_Layer1Width", 0.008f);
            glowMat.SetFloat("_Layer1Alpha", 1.5f);
            glowMat.SetFloat("_Layer2Width", 0.018f);
            glowMat.SetFloat("_Layer2Alpha", 1.0f);
            glowMat.SetFloat("_Layer3Width", 0.04f);
            glowMat.SetFloat("_Layer3Alpha", 0.6f);
            glowMat.SetFloat("_Layer4Width", 0.08f);
            glowMat.SetFloat("_Layer4Alpha", 0.3f);

            Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
            Color purpleColor = new Color(1f, 0.4f, 1f, 1f);
            glowMat.SetColor("_ColorA", cyanColor);
            glowMat.SetColor("_ColorB", purpleColor);
            glowMat.SetFloat("_GradientMode", 2f);
            glowMat.SetFloat("_GradientAngle", -10f);
            glowMat.SetFloat("_ShimmerSpeed", 0.1f);
            glowMat.SetFloat("_ShimmerIntensity", 0.2f);

            borderImg.material = glowMat;
        }

        borderObj.transform.SetAsLastSibling();
    }

    void CreateCameraContainer(Transform parent, float w, float h)
    {
        // No margins - passthrough fills entire frame
        float contentW = w;
        float contentH = h;

        // Container - full size
        GameObject containerObj = new GameObject("CameraContainer");
        containerObj.transform.SetParent(parent, false);

        RectTransform containerRT = containerObj.AddComponent<RectTransform>();
        containerRT.anchorMin = Vector2.zero;
        containerRT.anchorMax = Vector2.one;
        containerRT.offsetMin = Vector2.zero;
        containerRT.offsetMax = Vector2.zero;

        // Rounded mask
        GameObject maskObj = new GameObject("RoundedMask");
        maskObj.transform.SetParent(containerObj.transform, false);

        RectTransform maskRT = maskObj.AddComponent<RectTransform>();
        maskRT.anchorMin = Vector2.zero;
        maskRT.anchorMax = Vector2.one;
        maskRT.offsetMin = Vector2.zero;
        maskRT.offsetMax = Vector2.zero;

        Image maskImg = maskObj.AddComponent<Image>();
        // Corner radius matches border (0.12f) so passthrough aligns with border
        maskImg.sprite = CreateRoundedRectSprite((int)contentW, (int)contentH, 0.12f);
        maskImg.color = Color.white;
        maskImg.raycastTarget = false;

        Mask mask = maskObj.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // Camera preview
        GameObject previewObj = new GameObject("CameraPreview");
        previewObj.transform.SetParent(maskObj.transform, false);

        RectTransform previewRT = previewObj.AddComponent<RectTransform>();
        previewRT.anchorMin = Vector2.zero;
        previewRT.anchorMax = Vector2.one;
        previewRT.offsetMin = Vector2.zero;
        previewRT.offsetMax = Vector2.zero;

        _cameraPreview = previewObj.AddComponent<RawImage>();
        _cameraPreview.color = Color.white; // White so texture displays correctly

        // Status text (shown when camera not available)
        CreateStatusText(previewObj.transform);

        // Scan overlay
        CreateScanOverlay(maskObj.transform, contentW, contentH);
    }

    void CreateStatusText(Transform parent)
    {
        GameObject textObj = new GameObject("StatusText");
        textObj.transform.SetParent(parent, false);

        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        _statusText = textObj.AddComponent<TextMeshProUGUI>();
        _statusText.text = "Initializing camera...";
        _statusText.fontSize = 48;
        _statusText.color = Color.white;
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.raycastTarget = false;
        if (customFont != null) _statusText.font = customFont;
    }

    void UpdateStatusText(string message, bool show = true)
    {
        if (_statusText != null)
        {
            _statusText.text = message;
            _statusText.gameObject.SetActive(show);
        }
    }

    void CreateScanOverlay(Transform parent, float width, float height)
    {
        // Dark overlay
        GameObject overlayObj = new GameObject("ScanOverlay");
        overlayObj.transform.SetParent(parent, false);

        RectTransform overlayRT = overlayObj.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;

        Image overlayImg = overlayObj.AddComponent<Image>();
        overlayImg.color = new Color(0, 0, 0, 0.4f);
        overlayImg.raycastTarget = false;

        // Scan frame
        float frameSize = Mathf.Min(width, height) * 0.65f;
        CreateScanFrame(overlayObj.transform, frameSize);
    }

    void CreateScanFrame(Transform parent, float size)
    {
        GameObject frameObj = new GameObject("ScanFrame");
        frameObj.transform.SetParent(parent, false);

        _scanFrame = frameObj.AddComponent<RectTransform>();
        _scanFrame.anchorMin = new Vector2(0.5f, 0.5f);
        _scanFrame.anchorMax = new Vector2(0.5f, 0.5f);
        _scanFrame.pivot = new Vector2(0.5f, 0.5f);
        _scanFrame.sizeDelta = new Vector2(size, size);

        Image frameImg = frameObj.AddComponent<Image>();
        frameImg.color = new Color(0, 0, 0, 0);
        frameImg.raycastTarget = false;

        CreateCornerBrackets(frameObj.transform, size);
        CreateScanLine(frameObj.transform, size);
    }

    void CreateCornerBrackets(Transform parent, float frameSize)
    {
        float bracketLength = frameSize * 0.15f;
        float bracketWidth = 6f;
        Color bracketColor = Color.white;

        void CreateBracketLine(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPos)
        {
            GameObject lineObj = new GameObject(name);
            lineObj.transform.SetParent(parent, false);

            RectTransform rt = lineObj.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;

            Image img = lineObj.AddComponent<Image>();
            img.color = bracketColor;
            img.raycastTarget = false;

            Shadow shadow = lineObj.AddComponent<Shadow>();
            shadow.effectColor = new Color(1f, 1f, 1f, 0.6f);
            shadow.effectDistance = new Vector2(0, 2);
        }

        float offset = 4f;

        // Top-Left
        CreateBracketLine("TL_H", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(bracketLength, bracketWidth), new Vector2(offset, -offset));
        CreateBracketLine("TL_V", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(bracketWidth, bracketLength), new Vector2(offset, -offset));

        // Top-Right
        CreateBracketLine("TR_H", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(bracketLength, bracketWidth), new Vector2(-offset, -offset));
        CreateBracketLine("TR_V", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(bracketWidth, bracketLength), new Vector2(-offset, -offset));

        // Bottom-Left
        CreateBracketLine("BL_H", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(bracketLength, bracketWidth), new Vector2(offset, offset));
        CreateBracketLine("BL_V", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(bracketWidth, bracketLength), new Vector2(offset, offset));

        // Bottom-Right
        CreateBracketLine("BR_H", new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(bracketLength, bracketWidth), new Vector2(-offset, offset));
        CreateBracketLine("BR_V", new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(bracketWidth, bracketLength), new Vector2(-offset, offset));
    }

    void CreateScanLine(Transform parent, float frameSize)
    {
        GameObject lineObj = new GameObject("ScanLine");
        lineObj.transform.SetParent(parent, false);

        _scanLineRT = lineObj.AddComponent<RectTransform>();
        _scanLineRT.anchorMin = new Vector2(0.1f, 0.9f);
        _scanLineRT.anchorMax = new Vector2(0.9f, 0.9f);
        _scanLineRT.pivot = new Vector2(0.5f, 0.5f);
        _scanLineRT.sizeDelta = new Vector2(0, 4f);

        Image lineImg = lineObj.AddComponent<Image>();
        lineImg.color = themeColor;
        lineImg.raycastTarget = false;

        Shadow glow = lineObj.AddComponent<Shadow>();
        glow.effectColor = new Color(themeColor.r, themeColor.g, themeColor.b, 0.7f);
        glow.effectDistance = new Vector2(0, 3);

        // Start scan line animation
        _scanLineCoroutine = StartCoroutine(ScanLineAnimation());
    }

    IEnumerator ScanLineAnimation()
    {
        float duration = 2f;
        float startY = 0.9f;
        float endY = 0.1f;

        while (true)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float y = Mathf.Lerp(startY, endY, t);
                if (_scanLineRT != null)
                {
                    _scanLineRT.anchorMin = new Vector2(0.1f, y);
                    _scanLineRT.anchorMax = new Vector2(0.9f, y);
                }
                yield return null;
            }
        }
    }

    void StopScanLineAnimation()
    {
        if (_scanLineCoroutine != null)
        {
            StopCoroutine(_scanLineCoroutine);
            _scanLineCoroutine = null;
        }
    }

    void CreateCancelButton()
    {
        if (_mainCamera == null) _mainCamera = Camera.main;

        // Create root object - NOT parented to camera (follows horizontally only)
        _cancelButtonFrame = new GameObject("QRCancelButtonFrame");
        _cancelButtonFrame.transform.SetParent(transform, false);

        // Set initial position in front of camera at fixed world Y
        Vector3 camForward = _mainCamera.transform.forward;
        camForward.y = 0;
        camForward.Normalize();
        Vector3 initialPos = _mainCamera.transform.position + camForward * _cancelButtonDistance;
        initialPos.y = _cancelButtonWorldY;
        _cancelButtonFrame.transform.position = initialPos;

        // Face camera with pitch - SAME as VRTaskbar's OrientTowardsCamera()
        Vector3 toCamera = _mainCamera.transform.position - initialPos;
        if (toCamera.sqrMagnitude > 1e-6f)
        {
            _cancelButtonFrame.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        }

        // Add Canvas - higher sorting order to render on top of passthrough frame
        _cancelCanvas = _cancelButtonFrame.AddComponent<Canvas>();
        _cancelCanvas.renderMode = RenderMode.WorldSpace;
        _cancelCanvas.sortingOrder = 200; // Higher than passthrough frame (100)

        _cancelButtonFrame.AddComponent<GraphicRaycaster>();

        // Frame size - mini taskbar style (like VRTaskbar but just for 1 button)
        // Button size + padding on each side
        float buttonPadding = 19f; // Same as VRTaskbar padding
        float frameWidth = _cancelButtonSize + buttonPadding * 2;
        float frameHeight = _cancelButtonSize + buttonPadding * 2;

        RectTransform canvasRT = _cancelButtonFrame.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(frameWidth, frameHeight);
        canvasRT.localScale = new Vector3(ScaleFactor, ScaleFactor, 1f);

        // Create mini taskbar-style glass panel
        CreateMiniTaskbarPanel(_cancelButtonFrame.transform, frameWidth, frameHeight);
    }

    /// <summary>
    /// Create cancel button only (no frame/border) - same style as QR button in VRRemoteMenu
    /// </summary>
    void CreateMiniTaskbarPanel(Transform parent, float width, float height)
    {
        // Create button with same config as QR button in VRRemoteMenu
        Sprite closeIcon = RTTTaskbar.LoadIcon("close") ?? RTTTaskbar.LoadIcon("clear") ?? RTTTaskbar.LoadIcon("quit");

        var config = new VRButtonFactory.ButtonConfig
        {
            label = closeIcon != null ? closeIcon.name : "Cancel",
            icon = closeIcon,
            themeColor = accentColor,
            width = _cancelButtonSize,
            height = _cancelButtonSize,
            iconOnly = true,
            iconSize = 44f,           // Same as QR button
            backgroundAlpha = 0.08f,  // Same as QR button
            borderWidth = 0.04f,      // Same as QR button
            popAmount = 0.0125f       // Same as QR button
        };

        var btn = VRButtonFactory.CreateButton(parent, config, OnCancelClicked);

        RectTransform btnRT = btn.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 0.5f);
        btnRT.anchorMax = new Vector2(0.5f, 0.5f);
        btnRT.pivot = new Vector2(0.5f, 0.5f);
        btnRT.anchoredPosition = Vector2.zero;

        // Add collider for VR interaction
        BoxCollider btnCol = btn.AddComponent<BoxCollider>();
        btnCol.size = new Vector3(_cancelButtonSize, _cancelButtonSize, 0.01f);
        btnCol.center = Vector3.zero;

        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) btn.layer = vrLayer;
    }

    void CreateMiniGlassPanel(Transform parent, float size)
    {
        float glowExpansion = 0.04f;
        float edgePad = glowExpansion / (1f + 2f * glowExpansion);

        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);
        Image img = bgObj.AddComponent<Image>();
        img.type = Image.Type.Simple;
        img.sprite = CreatePixelSprite();

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", 0.25f);
            glassMat.SetFloat("_EdgePadding", edgePad);
            glassMat.SetFloat("_Aspect", 1f);

            Color cyanGlass = new Color(0.35f, 0.9f, 1f, 0.12f);
            Color purpleGlass = new Color(0.75f, 0.45f, 1f, 0.15f);
            glassMat.SetColor("_ColorA", cyanGlass);
            glassMat.SetColor("_ColorB", purpleGlass);
            glassMat.SetFloat("_GlassAlpha", 0.06f);

            img.material = glassMat;
            img.color = Color.white;
        }

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(-glowExpansion, -glowExpansion);
        rt.anchorMax = new Vector2(1f + glowExpansion, 1f + glowExpansion);
        rt.sizeDelta = Vector2.zero;
        rt.SetAsFirstSibling();

        // Collider
        float expandedSize = size * (1f + 2f * glowExpansion);
        BoxCollider bgCol = bgObj.AddComponent<BoxCollider>();
        bgCol.size = new Vector3(expandedSize, expandedSize, 0.01f);
        bgCol.center = Vector3.zero;

        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) bgObj.layer = vrLayer;

        // Mini glowing border
        CreateMiniGlowingBorder(bgObj.transform, size, edgePad);
    }

    void CreateMiniGlowingBorder(Transform parent, float size, float edgePad)
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
        borderImg.sprite = CreatePixelSprite();

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);

            glowMat.SetFloat("_StrokeEnabled", 0);
            glowMat.SetFloat("_BorderWidth", 0.04f);
            glowMat.SetFloat("_CornerRadius", 0.25f);
            glowMat.SetFloat("_EdgePadding", edgePad);
            glowMat.SetFloat("_Aspect", 1f);

            glowMat.SetFloat("_Layer1Width", 0.02f);
            glowMat.SetFloat("_Layer1Alpha", 1.5f);
            glowMat.SetFloat("_Layer2Width", 0.04f);
            glowMat.SetFloat("_Layer2Alpha", 1.0f);
            glowMat.SetFloat("_Layer3Width", 0.08f);
            glowMat.SetFloat("_Layer3Alpha", 0.6f);
            glowMat.SetFloat("_Layer4Width", 0.16f);
            glowMat.SetFloat("_Layer4Alpha", 0.3f);

            // Purple accent for cancel
            Color purpleColor = new Color(1f, 0.3f, 0.8f, 1f);
            Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
            glowMat.SetColor("_ColorA", purpleColor);
            glowMat.SetColor("_ColorB", cyanColor);
            glowMat.SetFloat("_GradientMode", 2f);
            glowMat.SetFloat("_ShimmerSpeed", 0.15f);
            glowMat.SetFloat("_ShimmerIntensity", 0.3f);

            borderImg.material = glowMat;
        }

        borderObj.transform.SetAsLastSibling();
    }

    void OnCancelClicked()
    {
        StopScanning();
        OnCancelled?.Invoke();
    }

    void LateUpdate()
    {
        // Update cancel button position - follows camera horizontally, fixed Y
        if (_cancelButtonFrame != null && _mainCamera != null)
        {
            // Get camera forward (horizontal only for position)
            Vector3 camForward = _mainCamera.transform.forward;
            camForward.y = 0;
            if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
            camForward.Normalize();

            // Position in front of camera at fixed distance and fixed world Y
            Vector3 targetPos = _mainCamera.transform.position + camForward * _cancelButtonDistance;
            targetPos.y = _cancelButtonWorldY; // Fixed world Y

            // Rotation faces camera with pitch - SAME as VRTaskbar's OrientTowardsCamera()
            Vector3 toCamera = _mainCamera.transform.position - targetPos;
            Quaternion targetRot;
            if (toCamera.sqrMagnitude > 1e-6f)
            {
                targetRot = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
            else
            {
                targetRot = Quaternion.LookRotation(-camForward, Vector3.up);
            }

            // Apply directly (no smoothing for immediate response)
            _cancelButtonFrame.transform.position = targetPos;
            _cancelButtonFrame.transform.rotation = targetRot;
        }
    }

    IEnumerator StartCamera()
    {
        #if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            yield return new WaitForSeconds(1f);

            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Debug.LogWarning("[QRScannerManager] Camera permission denied");
                yield break;
            }
        }
        #endif

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogWarning("[QRScannerManager] No camera found");
            UpdateStatusText("No camera found.\nPlease connect a camera.");
            yield break;
        }

        // Prefer back-facing camera
        int deviceIndex = 0;
        for (int i = 0; i < devices.Length; i++)
        {
            if (!devices[i].isFrontFacing)
            {
                deviceIndex = i;
                break;
            }
        }

        string cameraName = devices[deviceIndex].name;
        Debug.Log($"[QRScannerManager] Using camera: {cameraName}");
        UpdateStatusText($"Connecting to camera...\n{cameraName}");

        _webCamTexture = new WebCamTexture(cameraName, 1280, 720, 30);
        _cameraPreview.texture = _webCamTexture;
        _webCamTexture.Play();

        // Wait for camera to initialize
        float timeout = 5f;
        float elapsed = 0f;
        int frameCount = 0;
        while (!_webCamTexture.didUpdateThisFrame && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            frameCount++;
            yield return null;
        }

        if (_webCamTexture.didUpdateThisFrame && _webCamTexture.width > 16 && _webCamTexture.height > 16)
        {
            Debug.Log($"[QRScannerManager] Camera initialized successfully after {frameCount} frames. " +
                      $"Size: {_webCamTexture.width}x{_webCamTexture.height}, " +
                      $"IsPlaying: {_webCamTexture.isPlaying}");
            UpdateStatusText("", false); // Hide status text
            AdjustPreviewAspect();
            _scanCoroutine = StartCoroutine(ScanRoutine());
        }
        else
        {
            Debug.LogWarning($"[QRScannerManager] Camera failed to initialize within {timeout}s. " +
                             $"Size: {_webCamTexture.width}x{_webCamTexture.height}, " +
                             $"IsPlaying: {_webCamTexture.isPlaying}");
            UpdateStatusText($"Camera not available.\n{cameraName}\nSize: {_webCamTexture.width}x{_webCamTexture.height}");
        }
    }

    void AdjustPreviewAspect()
    {
        if (_webCamTexture == null || _cameraPreview == null) return;

        float videoAspect = (float)_webCamTexture.width / _webCamTexture.height;
        RectTransform rt = _cameraPreview.GetComponent<RectTransform>();
        float previewAspect = rt.rect.width / rt.rect.height;

        if (videoAspect > previewAspect)
        {
            float scale = previewAspect / videoAspect;
            rt.localScale = new Vector3(1f / scale, 1f, 1f);
        }
        else
        {
            float scale = videoAspect / previewAspect;
            rt.localScale = new Vector3(1f, 1f / scale, 1f);
        }

        int angle = _webCamTexture.videoRotationAngle;
        rt.localRotation = Quaternion.Euler(0, 0, -angle);

        if (_webCamTexture.videoVerticallyMirrored)
        {
            rt.localScale = new Vector3(rt.localScale.x, -rt.localScale.y, rt.localScale.z);
        }
    }

    IEnumerator ScanRoutine()
    {
        _isScanning = true;

        while (_isScanning && _webCamTexture != null && _webCamTexture.isPlaying)
        {
            if (_webCamTexture.didUpdateThisFrame)
            {
                string result = TryDecodeQR();
                if (!string.IsNullOrEmpty(result))
                {
                    ProcessQRResult(result);
                    yield break;
                }
            }

            yield return new WaitForSeconds(scanInterval);
        }
    }

    string TryDecodeQR()
    {
        // TODO: Implement ZXing decode when library is added
        return null;
    }

    void ProcessQRResult(string qrText)
    {
        _isScanning = false;

        try
        {
            QRScannerConfig config = QRScannerConfig.FromJson(qrText);

            if (config != null && config.IsValid())
            {
                StopScanLineAnimation();

                #if UNITY_ANDROID && !UNITY_EDITOR
                Handheld.Vibrate();
                #endif

                StartCoroutine(SuccessAnimation(() =>
                {
                    StopScanning();
                    OnQRScanned?.Invoke(config);
                }));
            }
            else
            {
                StartCoroutine(ResumeScanning(1.5f));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"QR parse error: {e.Message}");
            StartCoroutine(ResumeScanning(1.5f));
        }
    }

    IEnumerator ResumeScanning(float delay)
    {
        yield return new WaitForSeconds(delay);
        _isScanning = true;
        _scanCoroutine = StartCoroutine(ScanRoutine());
    }

    IEnumerator SuccessAnimation(Action onComplete)
    {
        if (_scanFrame == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        var brackets = _scanFrame.GetComponentsInChildren<Image>();
        Color successColor = Color.green;

        foreach (var img in brackets)
        {
            img.color = successColor;
        }

        Vector3 originalScale = _scanFrame.localScale;
        float duration = 0.3f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float scale = 1f + 0.08f * Mathf.Sin(t * Mathf.PI);
            _scanFrame.localScale = originalScale * scale;
            yield return null;
        }

        _scanFrame.localScale = originalScale;
        yield return new WaitForSeconds(0.4f);

        onComplete?.Invoke();
    }

    // Sprite creation helpers
    Sprite _pixelSprite;
    Sprite CreatePixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }

    Sprite CreateRoundedRectSprite(int width, int height, float radiusRatio)
    {
        float aspect = (float)width / height;
        int texWidth, texHeight;

        if (aspect >= 1f)
        {
            texWidth = 512;
            texHeight = Mathf.RoundToInt(512f / aspect);
        }
        else
        {
            texHeight = 512;
            texWidth = Mathf.RoundToInt(512f * aspect);
        }

        texWidth = Mathf.Max(texWidth, 128);
        texHeight = Mathf.Max(texHeight, 128);

        Texture2D tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float minDim = Mathf.Min(texWidth, texHeight);
        float radius = minDim * radiusRatio;

        Color32[] pixels = new Color32[texWidth * texHeight];

        for (int y = 0; y < texHeight; y++)
        {
            for (int x = 0; x < texWidth; x++)
            {
                float alpha = GetRoundedRectAlphaSmooth(x, y, texWidth, texHeight, radius);
                byte a = (byte)(alpha * 255);
                pixels[y * texWidth + x] = new Color32(255, 255, 255, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, texWidth, texHeight), new Vector2(0.5f, 0.5f), 100f);
    }

    float GetRoundedRectAlphaSmooth(int x, int y, int width, int height, float radius)
    {
        float px = x - width * 0.5f;
        float py = y - height * 0.5f;

        float hx = width * 0.5f - radius;
        float hy = height * 0.5f - radius;

        float dx = Mathf.Max(Mathf.Abs(px) - hx, 0f);
        float dy = Mathf.Max(Mathf.Abs(py) - hy, 0f);
        float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;

        float edge0 = -1.5f;
        float edge1 = 0.5f;

        if (dist <= edge0) return 1f;
        if (dist >= edge1) return 0f;

        float t = (dist - edge0) / (edge1 - edge0);
        return 1f - (t * t * (3f - 2f * t));
    }

    void OnDestroy()
    {
        StopScanning();
    }

    // Debug methods
    [ContextMenu("Test QR Scan")]
    public void TestQRScan()
    {
        string testJson = "{\"host\":\"192.168.1.100\",\"port\":\"9000\",\"resolution\":\"1920x1080\",\"bitrate\":\"20 Mbps\",\"fps\":\"60 FPS\",\"monitors\":2}";
        ProcessQRResult(testJson);
    }
}
