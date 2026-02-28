using UnityEngine;
using TMPro;
using System;
using System.Collections;

namespace VRWorkspace.UI.RTT.Components
{
    #if UNITY_ANDROID
    using UnityEngine.Android;
    #endif

    // ZXing for QR decoding - install via: https://github.com/micjahn/ZXing.Net
    // Download zxing.unity.dll and place in Assets/Plugins/
    using VRWorkspace.QRScanner;
    #if ZXING_AVAILABLE
    using ZXing;
    using ZXing.Common;
    #endif

    /// <summary>
    /// RTT-based QR Scanner with camera passthrough using shader-based rendering.
    /// Uses a Quad with CameraPassthroughRounded shader for camera display,
    /// scan frame overlay, corner brackets, and animated scan line - all rendered via shader.
    /// </summary>
    public class RTTQRScanner : MonoBehaviour
    {
        #region Configuration
        [Header("Panel Size (meters)")]
        [Tooltip("Physical width in meters - matches RTTMenuFrame")]
        [SerializeField] private float panelWidth = 1.6f;

        [Tooltip("Physical height in meters - matches RTTMenuFrame")]
        [SerializeField] private float panelHeight = 0.9f;

        [Header("Theme")]
        [SerializeField] private Color themeColor = new Color(0.0f, 0.9f, 1.0f);
        [SerializeField] private Color accentColor = new Color(0.8f, 0.4f, 1.0f);
        [SerializeField] private TMP_FontAsset customFont;

        [Header("Scan Settings")]
        [SerializeField] private float scanInterval = 0.2f;
        #endregion

        #region Events
        public event Action<QRScannerConfig> OnQRScanned;
        public event Action OnCancelled;
        #endregion

        #region Private Fields
        // Camera Quad
        private GameObject _cameraQuad;
        private MeshRenderer _cameraRenderer;
        private Material _cameraMaterial;
        private WebCamTexture _webCamTexture;

        // Status text (World Space Canvas for text only)
        private Canvas _textCanvas;
        private TextMeshProUGUI _statusText;

        // Scan line animation
        private Coroutine _scanLineCoroutine;

        // Scanning state
        private bool _isScanning;
        private Coroutine _scanCoroutine;

        // Calculated values
        private float Aspect => panelWidth / panelHeight;
        #endregion

        #region Properties
        public float PanelWidth => panelWidth;
        public float PanelHeight => panelHeight;
        #endregion

        #region Lifecycle
        void Awake()
        {
            BuildUI();
        }

        void OnDestroy()
        {
            StopScanning();

            if (_cameraMaterial != null)
                Destroy(_cameraMaterial);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Start the QR scanning process
        /// </summary>
        public void StartScanning()
        {
            if (_isScanning) return;
            StartCoroutine(StartCamera());
        }

        /// <summary>
        /// Stop QR scanning
        /// </summary>
        public void StopScanning()
        {
            _isScanning = false;

            if (_scanCoroutine != null)
            {
                StopCoroutine(_scanCoroutine);
                _scanCoroutine = null;
            }

            StopScanLineAnimation();

            if (_webCamTexture != null)
            {
                _webCamTexture.Stop();
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }
        }

        /// <summary>
        /// Cancel scanning and notify listeners
        /// </summary>
        public void Cancel()
        {
            StopScanning();
            OnCancelled?.Invoke();
        }
        #endregion

        #region UI Building
        private void BuildUI()
        {
            // 1. Create Camera Quad with rounded corners shader (includes scan frame)
            CreateCameraQuad();

            // 2. Create simple text canvas for status messages
            CreateTextCanvas();

            Debug.Log($"[RTTQRScanner] UI built: {panelWidth}m x {panelHeight}m");
        }

        private void CreateCameraQuad()
        {
            _cameraQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _cameraQuad.name = "CameraQuad";
            _cameraQuad.transform.SetParent(transform, false);
            _cameraQuad.transform.localPosition = Vector3.zero;
            _cameraQuad.transform.localRotation = Quaternion.identity;
            _cameraQuad.transform.localScale = new Vector3(panelWidth, panelHeight, 1f);

            // Remove default collider
            var collider = _cameraQuad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            // Add BoxCollider for VR interaction
            BoxCollider boxCol = _cameraQuad.AddComponent<BoxCollider>();
            boxCol.size = new Vector3(1f, 1f, 0.01f);
            boxCol.center = Vector3.zero;

            // Set layer for Reticle
            int vrLayer = LayerMask.NameToLayer("VirtualObjects");
            if (vrLayer != -1) _cameraQuad.layer = vrLayer;

            // Setup material with CameraPassthroughRounded shader
            _cameraRenderer = _cameraQuad.GetComponent<MeshRenderer>();
            Shader passShader = Shader.Find("Custom/CameraPassthroughRounded");

            if (passShader != null)
            {
                _cameraMaterial = new Material(passShader);

                // Shape settings - exact RTTMenuFrame values
                _cameraMaterial.SetFloat("_CornerRadius", 0.04f);
                _cameraMaterial.SetFloat("_EdgePadding", 0.025f);
                _cameraMaterial.SetFloat("_Aspect", Aspect);

                // Border settings - exact RTTMenuFrame values
                _cameraMaterial.SetFloat("_BorderEnabled", 1);
                _cameraMaterial.SetFloat("_BorderWidth", 0.125f);

                // Glow layers - exact RTTMenuFrame values
                _cameraMaterial.SetFloat("_Layer1Width", 0.015f);
                _cameraMaterial.SetFloat("_Layer1Alpha", 1.5f);
                _cameraMaterial.SetFloat("_Layer2Width", 0.03f);
                _cameraMaterial.SetFloat("_Layer2Alpha", 1.0f);
                _cameraMaterial.SetFloat("_Layer3Width", 0.06f);
                _cameraMaterial.SetFloat("_Layer3Alpha", 0.6f);
                _cameraMaterial.SetFloat("_Layer4Width", 0.12f);
                _cameraMaterial.SetFloat("_Layer4Alpha", 0.3f);

                // Colors
                Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
                Color purpleColor = new Color(1f, 0.4f, 1f, 1f);
                _cameraMaterial.SetColor("_ColorA", cyanColor);
                _cameraMaterial.SetColor("_ColorB", purpleColor);
                _cameraMaterial.SetFloat("_GradientAngle", -10f);

                // Animation
                _cameraMaterial.SetFloat("_ShimmerSpeed", 0.1f);
                _cameraMaterial.SetFloat("_ShimmerIntensity", 0.2f);

                // Scan frame settings (drawn by shader)
                _cameraMaterial.SetFloat("_ScanFrameEnabled", 1);
                _cameraMaterial.SetFloat("_ScanFrameSize", 0.65f);
                _cameraMaterial.SetColor("_ScanFrameColor", Color.white);
                _cameraMaterial.SetFloat("_BracketLength", 0.15f);
                _cameraMaterial.SetFloat("_BracketWidth", 0.008f);
                _cameraMaterial.SetFloat("_ScanLineY", 0.9f);
                _cameraMaterial.SetColor("_ScanLineColor", themeColor);
                _cameraMaterial.SetFloat("_OverlayAlpha", 0.4f);

                _cameraRenderer.material = _cameraMaterial;
            }
            else
            {
                Debug.LogWarning("[RTTQRScanner] CameraPassthroughRounded shader not found, using default");
                _cameraMaterial = new Material(Shader.Find("Unlit/Texture"));
                _cameraRenderer.material = _cameraMaterial;
            }
        }

        private void CreateTextCanvas()
        {
            // Create minimal Canvas for status text only
            GameObject canvasObj = new GameObject("TextCanvas");
            canvasObj.transform.SetParent(transform, false);
            canvasObj.transform.localPosition = new Vector3(0, 0, -0.002f);
            canvasObj.transform.localRotation = Quaternion.identity;

            _textCanvas = canvasObj.AddComponent<Canvas>();
            _textCanvas.renderMode = RenderMode.WorldSpace;
            _textCanvas.sortingOrder = 10;

            RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
            float logicalWidth = 1920f;
            float logicalHeight = logicalWidth / Aspect;
            canvasRT.sizeDelta = new Vector2(logicalWidth, logicalHeight);
            float scaleFactor = panelWidth / logicalWidth;
            canvasRT.localScale = new Vector3(scaleFactor, scaleFactor, 1f);

            // Status text
            GameObject textObj = new GameObject("StatusText");
            textObj.transform.SetParent(canvasRT, false);

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

            // Start scan line animation via shader
            _scanLineCoroutine = StartCoroutine(ShaderScanLineAnimation());
        }

        private void UpdateStatusText(string message, bool show = true)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
                _statusText.gameObject.SetActive(show);
            }
        }

        private IEnumerator ShaderScanLineAnimation()
        {
            float duration = 2f;

            while (true)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / duration;
                    // Animate from top (0.9) to bottom (0.1)
                    float y = Mathf.Lerp(0.9f, 0.1f, t);
                    if (_cameraMaterial != null)
                    {
                        _cameraMaterial.SetFloat("_ScanLineY", y);
                    }
                    yield return null;
                }
            }
        }

        private void StopScanLineAnimation()
        {
            if (_scanLineCoroutine != null)
            {
                StopCoroutine(_scanLineCoroutine);
                _scanLineCoroutine = null;
            }
        }
        #endregion

        #region Camera & Scanning
        private IEnumerator StartCamera()
        {
            #if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);
                yield return new WaitForSeconds(1f);

                if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    Debug.LogWarning("[RTTQRScanner] Camera permission denied");
                    UpdateStatusText("Camera permission required");
                    yield break;
                }
            }
            #endif

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogWarning("[RTTQRScanner] No camera found");
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
            Debug.Log($"[RTTQRScanner] Using camera: {cameraName}");
            UpdateStatusText($"Connecting to camera...\n{cameraName}");

            _webCamTexture = new WebCamTexture(cameraName, 1280, 720, 30);

            // Apply texture to material
            if (_cameraMaterial != null)
            {
                _cameraMaterial.SetTexture("_MainTex", _webCamTexture);
            }

            _webCamTexture.Play();

            // Wait for camera to initialize
            float timeout = 5f;
            float elapsed = 0f;
            while (!_webCamTexture.didUpdateThisFrame && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (_webCamTexture.didUpdateThisFrame && _webCamTexture.width > 16 && _webCamTexture.height > 16)
            {
                Debug.Log($"[RTTQRScanner] Camera initialized: {_webCamTexture.width}x{_webCamTexture.height}");
                UpdateStatusText("", false);

                // Update aspect ratio in shader if camera aspect differs
                float videoAspect = (float)_webCamTexture.width / _webCamTexture.height;
                // Note: We keep panel aspect for rounded corners, camera fills the space

                _scanCoroutine = StartCoroutine(ScanRoutine());
            }
            else
            {
                Debug.LogWarning($"[RTTQRScanner] Camera failed to initialize");
                UpdateStatusText($"Camera not available.\n{cameraName}");
            }
        }

        private IEnumerator ScanRoutine()
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

        private string TryDecodeQR()
        {
    #if ZXING_AVAILABLE
            try
            {
                // Get pixels from webcam texture
                Color32[] pixels = _webCamTexture.GetPixels32();
                int width = _webCamTexture.width;
                int height = _webCamTexture.height;

                // Create ZXing barcode reader
                var barcodeReader = new BarcodeReader
                {
                    AutoRotate = true,
                    Options = new DecodingOptions
                    {
                        TryHarder = true,
                        PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                    }
                };

                // Decode
                var result = barcodeReader.Decode(pixels, width, height);
                if (result != null)
                {
                    Debug.Log($"[RTTQRScanner] QR Decoded: {result.Text}");
                    return result.Text;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RTTQRScanner] Decode error: {e.Message}");
            }
    #else
            // ZXing not available - log warning once
            if (_webCamTexture.didUpdateThisFrame && Time.frameCount % 300 == 0)
            {
                Debug.LogWarning("[RTTQRScanner] ZXing library not installed. Add ZXING_AVAILABLE to Scripting Define Symbols after installing ZXing.Net");
            }
    #endif
            return null;
        }

        private void ProcessQRResult(string qrText)
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

        private IEnumerator ResumeScanning(float delay)
        {
            yield return new WaitForSeconds(delay);
            _isScanning = true;
            _scanCoroutine = StartCoroutine(ScanRoutine());
        }

        private IEnumerator SuccessAnimation(Action onComplete)
        {
            if (_cameraMaterial == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            // Change scan frame color to green via shader
            Color successColor = Color.green;
            _cameraMaterial.SetColor("_ScanFrameColor", successColor);
            _cameraMaterial.SetColor("_ScanLineColor", successColor);

            // Pulse effect via scan frame size
            float originalSize = 0.65f;
            float duration = 0.3f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float scale = originalSize * (1f + 0.08f * Mathf.Sin(t * Mathf.PI));
                _cameraMaterial.SetFloat("_ScanFrameSize", scale);
                yield return null;
            }

            _cameraMaterial.SetFloat("_ScanFrameSize", originalSize);
            yield return new WaitForSeconds(0.4f);

            onComplete?.Invoke();
        }
        #endregion

        #region Debug
        /// <summary>
        /// Simulate QR scan result (for testing without camera)
        /// </summary>
        public void SimulateQRScan(string jsonData)
        {
            ProcessQRResult(jsonData);
        }

        [ContextMenu("Test QR Scan")]
        public void TestQRScan()
        {
            string testJson = "{\"host\":\"192.168.1.100\",\"port\":\"9000\",\"resolution\":\"1920x1080\",\"bitrate\":\"20 Mbps\",\"fps\":\"60 FPS\",\"monitors\":2}";
            SimulateQRScan(testJson);
        }
        #endregion
    }

}
