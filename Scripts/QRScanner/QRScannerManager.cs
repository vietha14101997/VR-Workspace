using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// QR Scanner Manager - Manages full-screen QR scanning mode
/// - Hides VRMenuFrame and VRTaskbar when active
/// - Shows RTT-based passthrough frame with glowing border
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

    [Header("Position Settings")]
    [Tooltip("Distance from camera for passthrough frame")]
    public float frameDistance = 1.5f;

    // Events
    public event Action<QRScannerConfig> OnQRScanned;
    public event Action OnCancelled;

    // References to hide/show
    private RTTMenuFrame _rttMenuFrame;
    private RTTTaskbar _rttTaskbar;

    // RTT-based QR Scanner (replaces Canvas-based passthrough)
    private RTTQRScanner _rttQRScanner;

    // Cancel button
    private GameObject _cancelButtonFrame;
    private Canvas _cancelCanvas;

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

        // Create RTT-based passthrough frame (parented to camera)
        CreatePassthroughFrame();

        // Create cancel button (parented to camera)
        CreateCancelButton();

        // Start scanning on RTTQRScanner
        if (_rttQRScanner != null)
        {
            _rttQRScanner.StartScanning();
        }
    }

    /// <summary>
    /// Stop QR scanning mode and restore original UI
    /// </summary>
    public void StopScanning()
    {
        // Stop and destroy RTTQRScanner
        if (_rttQRScanner != null)
        {
            _rttQRScanner.StopScanning();
            Destroy(_rttQRScanner.gameObject);
            _rttQRScanner = null;
        }

        // Destroy cancel button
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

        // Create RTTQRScanner - RTT-based QR scanner with glowing border
        GameObject scannerObj = new GameObject("RTTQRScanner");
        scannerObj.transform.SetParent(_mainCamera.transform, false);

        // Set local position in front of camera, centered on reticle (camera forward)
        scannerObj.transform.localPosition = new Vector3(0, 0, frameDistance);
        scannerObj.transform.localRotation = Quaternion.identity;

        // Add RTTQRScanner component
        _rttQRScanner = scannerObj.AddComponent<RTTQRScanner>();

        // Subscribe to events
        _rttQRScanner.OnQRScanned += HandleQRScanned;
        _rttQRScanner.OnCancelled += HandleCancelled;

        // Set VirtualObjects layer for Reticle interaction
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) scannerObj.layer = vrLayer;

        Debug.Log($"[QRScannerManager] Created RTTQRScanner at distance {frameDistance}m");
    }

    void HandleQRScanned(QRScannerConfig config)
    {
        StopScanning();
        OnQRScanned?.Invoke(config);
    }

    void HandleCancelled()
    {
        StopScanning();
        OnCancelled?.Invoke();
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

    void OnDestroy()
    {
        StopScanning();
    }

    /// <summary>
    /// Test QR scan with RTTQRScanner
    /// </summary>
    [ContextMenu("Test QR Scan")]
    public void TestQRScan()
    {
        if (_rttQRScanner != null)
        {
            _rttQRScanner.TestQRScan();
        }
    }
}
