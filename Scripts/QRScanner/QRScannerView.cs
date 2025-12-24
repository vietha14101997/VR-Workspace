using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// QR Scanner View - hiển thị camera passthrough và quét mã QR
/// </summary>
public class QRScannerView : MonoBehaviour
{
    [Header("Theme")]
    public Color themeColor = new Color(0.0f, 0.9f, 1.0f);
    public Color accentColor = new Color(0.8f, 0.4f, 1.0f);
    public TMP_FontAsset customFont;

    [Header("Scan Settings")]
    public float scanInterval = 0.2f;  // 5 scans per second

    // Events
    public event Action<QRScannerConfig> OnQRScanned;

    // UI References
    private RawImage _cameraPreview;
    private Material _previewMaterial;
    private RectTransform _scanFrame;
    private TextMeshProUGUI _statusText;
    private ScanLineAnimator _scanLineAnimator;
    private CanvasGroup _canvasGroup;

    // Camera
    private WebCamTexture _webCamTexture;
    private bool _isScanning;
    private Coroutine _scanCoroutine;

    // Container size
    private float _containerWidth;
    private float _containerHeight;

    // ZXing (will be used when library is added)
    // private IBarcodeReader _barcodeReader;

    public void BuildUI(Transform parent, float containerWidth, float containerHeight)
    {
        _containerWidth = containerWidth;
        _containerHeight = containerHeight;

        // Add CanvasGroup for fade animation
        _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Create camera container (full size - fill toàn bộ BodyContainer)
        CreateCameraContainer(parent, containerWidth, containerHeight);

        // Start camera
        StartCoroutine(StartCamera());
    }

    void CreateCameraContainer(Transform parent, float width, float height)
    {
        // Camera Container
        GameObject containerObj = new GameObject("CameraContainer");
        containerObj.transform.SetParent(parent, false);

        RectTransform containerRT = containerObj.AddComponent<RectTransform>();
        containerRT.anchorMin = Vector2.zero;
        containerRT.anchorMax = Vector2.one;
        containerRT.offsetMin = Vector2.zero;
        containerRT.offsetMax = Vector2.zero;

        // Tạo mask với rounded corners sprite
        GameObject maskObj = new GameObject("RoundedMask");
        maskObj.transform.SetParent(containerObj.transform, false);

        RectTransform maskRT = maskObj.AddComponent<RectTransform>();
        maskRT.anchorMin = Vector2.zero;
        maskRT.anchorMax = Vector2.one;
        maskRT.offsetMin = Vector2.zero;
        maskRT.offsetMax = Vector2.zero;

        // Tạo sprite rounded rect runtime
        Image maskImg = maskObj.AddComponent<Image>();
        maskImg.sprite = CreateRoundedRectSprite((int)width, (int)height, 0.1f);
        maskImg.type = Image.Type.Simple;
        maskImg.color = Color.white;  // Mask cần màu trắng để alpha hoạt động đúng
        maskImg.raycastTarget = false;

        // Thêm Mask để clip children theo alpha của sprite
        Mask mask = maskObj.AddComponent<Mask>();
        mask.showMaskGraphic = false;  // Ẩn mask graphic, chỉ dùng để clip

        // Camera Preview (RawImage) - nằm trong mask
        GameObject previewObj = new GameObject("CameraPreview");
        previewObj.transform.SetParent(maskObj.transform, false);

        RectTransform previewRT = previewObj.AddComponent<RectTransform>();
        previewRT.anchorMin = Vector2.zero;
        previewRT.anchorMax = Vector2.one;
        previewRT.offsetMin = Vector2.zero;
        previewRT.offsetMax = Vector2.zero;

        _cameraPreview = previewObj.AddComponent<RawImage>();
        _cameraPreview.color = Color.white;

        // Scan Overlay
        CreateScanOverlay(maskObj.transform, width, height);

        // Status Text
        // CreateStatusText(maskObj.transform, width);
    }

    /// <summary>
    /// Tạo sprite hình chữ nhật bo góc runtime với anti-aliasing
    /// </summary>
    Sprite CreateRoundedRectSprite(int width, int height, float radiusRatio)
    {
        // Giữ nguyên tỷ lệ aspect ratio của vùng hiển thị
        float aspect = (float)width / height;
        int texWidth, texHeight;

        if (aspect >= 1f)
        {
            // Wider than tall
            texWidth = 1024;
            texHeight = Mathf.RoundToInt(1024f / aspect);
        }
        else
        {
            // Taller than wide
            texHeight = 1024;
            texWidth = Mathf.RoundToInt(1024f * aspect);
        }

        // Đảm bảo kích thước tối thiểu
        texWidth = Mathf.Max(texWidth, 256);
        texHeight = Mathf.Max(texHeight, 256);

        Texture2D tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        // Tính radius - dùng chiều nhỏ hơn để bo góc đều và tròn
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

    /// <summary>
    /// Tính alpha sử dụng Signed Distance Function (SDF) cho rounded rectangle
    /// Đảm bảo chuyển tiếp mượt giữa góc cong và cạnh thẳng
    /// </summary>
    float GetRoundedRectAlphaSmooth(int x, int y, int width, int height, float radius)
    {
        // Chuyển về tọa độ centered (tâm = 0,0)
        float px = x - width * 0.5f;
        float py = y - height * 0.5f;

        // Half size trừ radius (vùng không bo góc)
        float hx = width * 0.5f - radius;
        float hy = height * 0.5f - radius;

        // Tính khoảng cách đến cạnh rounded rect (SDF)
        float dx = Mathf.Max(Mathf.Abs(px) - hx, 0f);
        float dy = Mathf.Max(Mathf.Abs(py) - hy, 0f);
        float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;

        // Anti-aliasing với smoothstep (transition ~2 pixel)
        float edge0 = -1.5f;
        float edge1 = 0.5f;

        if (dist <= edge0) return 1f;  // Fully inside
        if (dist >= edge1) return 0f;  // Fully outside

        // Smoothstep interpolation
        float t = (dist - edge0) / (edge1 - edge0);
        return 1f - (t * t * (3f - 2f * t));
    }

    void CreateScanOverlay(Transform parent, float width, float height)
    {
        // Overlay container
        GameObject overlayObj = new GameObject("ScanOverlay");
        overlayObj.transform.SetParent(parent, false);

        RectTransform overlayRT = overlayObj.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;

        // Dark overlay image
        Image overlayImg = overlayObj.AddComponent<Image>();
        overlayImg.color = new Color(0, 0, 0, 0.5f);
        overlayImg.raycastTarget = false;

        // Scan Frame (center)
        float frameSize = Mathf.Min(width, height) * 0.65f;
        CreateScanFrame(overlayObj.transform, frameSize);
    }

    void CreateScanFrame(Transform parent, float size)
    {
        // Frame container
        GameObject frameObj = new GameObject("ScanFrame");
        frameObj.transform.SetParent(parent, false);

        _scanFrame = frameObj.AddComponent<RectTransform>();
        _scanFrame.anchorMin = new Vector2(0.5f, 0.5f);
        _scanFrame.anchorMax = new Vector2(0.5f, 0.5f);
        _scanFrame.pivot = new Vector2(0.5f, 0.5f);
        _scanFrame.sizeDelta = new Vector2(size, size);

        // Frame background (transparent center)
        Image frameImg = frameObj.AddComponent<Image>();
        frameImg.color = new Color(0, 0, 0, 0);  // Fully transparent
        frameImg.raycastTarget = false;

        // Create 4 corner brackets
        CreateCornerBrackets(frameObj.transform, size);

        // Create scan line
        CreateScanLine(frameObj.transform, size);
    }

    void CreateCornerBrackets(Transform parent, float frameSize)
    {
        float bracketLength = frameSize * 0.15f;
        float bracketWidth = 6f;
        Color bracketColor = Color.white;

        // Helper to create a bracket line
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

            // Add glow shadow
            Shadow shadow = lineObj.AddComponent<Shadow>();
            shadow.effectColor = new Color(bracketColor.r, bracketColor.g, bracketColor.b, 0.6f);
            shadow.effectDistance = new Vector2(0, 2);
        }

        float halfSize = frameSize / 2f;
        float offset = 4f;

        // Top-Left corner
        CreateBracketLine("TL_H", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(bracketLength, bracketWidth), new Vector2(offset, -offset));
        CreateBracketLine("TL_V", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(bracketWidth, bracketLength), new Vector2(offset, -offset));

        // Top-Right corner
        CreateBracketLine("TR_H", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(bracketLength, bracketWidth), new Vector2(-offset, -offset));
        CreateBracketLine("TR_V", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(bracketWidth, bracketLength), new Vector2(-offset, -offset));

        // Bottom-Left corner
        CreateBracketLine("BL_H", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(bracketLength, bracketWidth), new Vector2(offset, offset));
        CreateBracketLine("BL_V", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(bracketWidth, bracketLength), new Vector2(offset, offset));

        // Bottom-Right corner
        CreateBracketLine("BR_H", new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(bracketLength, bracketWidth), new Vector2(-offset, offset));
        CreateBracketLine("BR_V", new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
            new Vector2(bracketWidth, bracketLength), new Vector2(-offset, offset));
    }

    void CreateScanLine(Transform parent, float frameSize)
    {
        GameObject lineObj = new GameObject("ScanLine");
        lineObj.transform.SetParent(parent, false);

        RectTransform lineRT = lineObj.AddComponent<RectTransform>();
        lineRT.anchorMin = new Vector2(0.1f, 0.5f);
        lineRT.anchorMax = new Vector2(0.9f, 0.5f);
        lineRT.pivot = new Vector2(0.5f, 0.5f);
        lineRT.sizeDelta = new Vector2(0, 4f);

        Image lineImg = lineObj.AddComponent<Image>();
        lineImg.color = Color.white;
        lineImg.raycastTarget = false;

        // Add glow
        Shadow glow = lineObj.AddComponent<Shadow>();
        glow.effectColor = new Color(1f, 1f, 1f, 0.7f);
        glow.effectDistance = new Vector2(0, 3);

        // Add animator
        _scanLineAnimator = lineObj.AddComponent<ScanLineAnimator>();
        _scanLineAnimator.duration = 2f;
        _scanLineAnimator.startY = 0.9f;
        _scanLineAnimator.endY = 0.1f;
    }

    void CreateStatusText(Transform parent, float width)
    {
        GameObject textObj = new GameObject("StatusText");
        textObj.transform.SetParent(parent, false);

        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0.5f, 0);
        textRT.anchorMax = new Vector2(0.5f, 0);
        textRT.pivot = new Vector2(0.5f, 0);
        textRT.sizeDelta = new Vector2(width * 0.8f, 60f);
        textRT.anchoredPosition = new Vector2(0, 30f);

        _statusText = textObj.AddComponent<TextMeshProUGUI>();
        _statusText.text = "Đang khởi động camera...";
        _statusText.fontSize = 36;
        _statusText.color = Color.white;
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.fontStyle = FontStyles.Bold;
        _statusText.raycastTarget = false;
        if (customFont != null) _statusText.font = customFont;
    }

    IEnumerator StartCamera()
    {
        // Request camera permission on Android
        #if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            yield return new WaitForSeconds(1f);

            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                _statusText.text = "Cần quyền truy cập camera";
                _statusText.color = Color.yellow;
                yield break;
            }
        }
        #endif

        // Get available cameras
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            _statusText.text = "Không tìm thấy camera";
            _statusText.color = Color.red;
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

        // Create and start WebCamTexture
        _webCamTexture = new WebCamTexture(devices[deviceIndex].name, 1280, 720, 30);
        _cameraPreview.texture = _webCamTexture;
        _webCamTexture.Play();

        // Wait for camera to initialize
        float timeout = 5f;
        float elapsed = 0f;
        while (!_webCamTexture.didUpdateThisFrame && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!_webCamTexture.didUpdateThisFrame)
        {
            _statusText.text = "Không thể khởi động camera";
            _statusText.color = Color.red;
            yield break;
        }

        // Adjust preview aspect ratio
        AdjustPreviewAspect();

        // Update status and start scanning
        _statusText.text = "Đưa mã QR vào khung để quét";
        _statusText.color = Color.white;

        // Start scan routine
        _scanCoroutine = StartCoroutine(ScanRoutine());
    }

    void AdjustPreviewAspect()
    {
        if (_webCamTexture == null || _cameraPreview == null) return;

        float videoAspect = (float)_webCamTexture.width / _webCamTexture.height;
        float previewAspect = _containerWidth / _containerHeight;

        RectTransform rt = _cameraPreview.GetComponent<RectTransform>();

        if (videoAspect > previewAspect)
        {
            // Video wider than preview - fit height, crop width
            float scale = previewAspect / videoAspect;
            rt.localScale = new Vector3(1f / scale, 1f, 1f);
        }
        else
        {
            // Video taller than preview - fit width, crop height
            float scale = videoAspect / previewAspect;
            rt.localScale = new Vector3(1f, 1f / scale, 1f);
        }

        // Handle rotation (some cameras may be rotated)
        int angle = _webCamTexture.videoRotationAngle;
        rt.localRotation = Quaternion.Euler(0, 0, -angle);

        // Handle mirroring
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
                // Try to decode QR
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

    /// <summary>
    /// Try to decode QR from current camera frame
    /// Override this method when ZXing is added
    /// </summary>
    string TryDecodeQR()
    {
        // TODO: Implement ZXing decode when library is added
        // Example implementation:
        /*
        try
        {
            Color32[] pixels = _webCamTexture.GetPixels32();
            int width = _webCamTexture.width;
            int height = _webCamTexture.height;

            var reader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
                }
            };

            var result = reader.Decode(pixels, width, height);
            return result?.Text;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"QR decode error: {e.Message}");
            return null;
        }
        */

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
                // Success!
                _statusText.text = "Tìm thấy mã QR!";
                _statusText.color = Color.green;

                // Stop scan line
                if (_scanLineAnimator != null) _scanLineAnimator.Stop();

                // Haptic feedback
                #if UNITY_ANDROID && !UNITY_EDITOR
                Handheld.Vibrate();
                #endif

                // Success animation then callback
                StartCoroutine(SuccessAnimation(() =>
                {
                    OnQRScanned?.Invoke(config);
                }));
            }
            else
            {
                _statusText.text = "Mã QR không hợp lệ";
                _statusText.color = Color.yellow;

                // Resume scanning after delay
                StartCoroutine(ResumeScanning(1.5f));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"QR parse error: {e.Message}");
            _statusText.text = "Không đọc được dữ liệu";
            _statusText.color = Color.yellow;

            // Resume scanning after delay
            StartCoroutine(ResumeScanning(1.5f));
        }
    }

    IEnumerator ResumeScanning(float delay)
    {
        yield return new WaitForSeconds(delay);

        _statusText.text = "Đưa mã QR vào khung để quét";
        _statusText.color = Color.white;

        _isScanning = true;
        _scanCoroutine = StartCoroutine(ScanRoutine());
    }

    IEnumerator SuccessAnimation(Action onComplete)
    {
        // Flash frame green
        var brackets = _scanFrame.GetComponentsInChildren<Image>();
        Color originalColor = accentColor;
        Color successColor = Color.green;

        foreach (var img in brackets)
        {
            img.color = successColor;
        }

        // Scale pulse animation
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

        // Wait a moment
        yield return new WaitForSeconds(0.4f);

        onComplete?.Invoke();
    }

    public void StopScanning()
    {
        _isScanning = false;

        if (_scanCoroutine != null)
        {
            StopCoroutine(_scanCoroutine);
            _scanCoroutine = null;
        }

        if (_webCamTexture != null)
        {
            _webCamTexture.Stop();
            Destroy(_webCamTexture);
            _webCamTexture = null;
        }
    }

    void OnDestroy()
    {
        StopScanning();
    }

    // ==================== DEBUG / TESTING ====================

    /// <summary>
    /// Simulate QR scan result (for testing without camera)
    /// </summary>
    public void SimulateQRScan(string jsonData)
    {
        ProcessQRResult(jsonData);
    }

    /// <summary>
    /// Simulate with default test data
    /// </summary>
    [ContextMenu("Test QR Scan")]
    public void TestQRScan()
    {
        string testJson = "{\"host\":\"192.168.1.100\",\"port\":\"9000\",\"resolution\":\"1920x1080\",\"bitrate\":\"20 Mbps\",\"fps\":\"60 FPS\",\"monitors\":2}";
        SimulateQRScan(testJson);
    }
}
