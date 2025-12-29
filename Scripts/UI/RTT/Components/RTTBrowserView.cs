using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

namespace VRWorkspace.UI
{
    /// <summary>
    /// Embedded WebView component for RTTMenuFrame.
    /// Renders WebView content to a RawImage for display in VR UI.
    /// Uses Android System WebView on Android, placeholder on Editor.
    /// </summary>
    public class RTTBrowserView : MonoBehaviour
    {
        #region Events
        public event Action OnBackClicked;
        public event Action<string> OnUrlChanged;
        public event Action OnLoadStarted;
        public event Action OnLoadFinished;
        public event Action<string> OnError;
        #endregion

        #region Configuration
        [Header("Theme")]
        public Color themeColor = new Color(0f, 0.9f, 1f);
        public Color accentColor = new Color(0.8f, 0.4f, 1f);
        public TMP_FontAsset customFont;
        #endregion

        #region Private Fields
        private RawImage _webViewDisplay;
        private RenderTexture _renderTexture;
        private GameObject _headerBar;
        private TMP_InputField _urlInput;
        private TextMeshProUGUI _statusText;
        private string _currentUrl;
        private bool _isLoading;

        // Android WebView bridge
        private AndroidWebViewBridge _androidBridge;

        // Container references
        private float _containerWidth;
        private float _containerHeight;
        #endregion

        #region Public Properties
        public string CurrentUrl => _currentUrl;
        public bool IsLoading => _isLoading;
        #endregion

        #region Build UI
        /// <summary>
        /// Build the browser view UI inside the given container.
        /// </summary>
        public void BuildUI(Transform parent, float containerWidth, float containerHeight)
        {
            _containerWidth = containerWidth;
            _containerHeight = containerHeight;

            // Setup RectTransform
            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();

            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;

            // Layout heights
            float headerHeight = 80f;
            float webViewHeight = containerHeight - headerHeight;

            // Create header bar (Back, URL input, Refresh)
            CreateHeaderBar(transform, containerWidth, headerHeight);

            // Create WebView display area
            CreateWebViewDisplay(transform, containerWidth, webViewHeight, headerHeight);

            // Initialize Android WebView bridge
            InitializeWebViewBridge();

            Debug.Log($"[RTTBrowserView] UI built ({containerWidth}x{containerHeight})");
        }

        private void CreateHeaderBar(Transform parent, float width, float height)
        {
            _headerBar = new GameObject("HeaderBar");
            _headerBar.transform.SetParent(parent, false);

            var rt = _headerBar.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, height);

            // Background
            var bg = _headerBar.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.12f, 0.15f, 0.95f);

            // Horizontal layout
            var layout = _headerBar.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 10, 10);
            layout.spacing = 15f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            // Back button
            CreateHeaderButton(_headerBar.transform, "◀ Back", 120f, () => OnBackClicked?.Invoke());

            // URL input field
            CreateUrlInput(_headerBar.transform, width - 350f);

            // Refresh button
            CreateHeaderButton(_headerBar.transform, "↻", 60f, () => Refresh());

            // Status text (loading indicator)
            CreateStatusText(_headerBar.transform);
        }

        private void CreateHeaderButton(Transform parent, string label, float width, Action onClick)
        {
            var btnObj = new GameObject($"Btn_{label}");
            btnObj.transform.SetParent(parent, false);

            var rt = btnObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 60f);

            var bg = btnObj.AddComponent<Image>();
            bg.color = new Color(themeColor.r, themeColor.g, themeColor.b, 0.2f);

            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick?.Invoke());

            // Button text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);

            var textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 32;
            tmp.color = themeColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.font = customFont;
        }

        private void CreateUrlInput(Transform parent, float width)
        {
            var inputObj = new GameObject("UrlInput");
            inputObj.transform.SetParent(parent, false);

            var rt = inputObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 60f);

            var bg = inputObj.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.17f, 0.2f, 1f);

            _urlInput = inputObj.AddComponent<TMP_InputField>();

            // Text area
            var textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputObj.transform, false);
            var textAreaRt = textArea.AddComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.offsetMin = new Vector2(15, 5);
            textAreaRt.offsetMax = new Vector2(-15, -5);
            textArea.AddComponent<RectMask2D>();

            // Text component
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(textArea.transform, false);
            var textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 28;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.font = customFont;

            _urlInput.textComponent = tmp;
            _urlInput.textViewport = textAreaRt;
            _urlInput.onEndEdit.AddListener(url => LoadUrl(url));
        }

        private void CreateStatusText(Transform parent)
        {
            var statusObj = new GameObject("Status");
            statusObj.transform.SetParent(parent, false);

            var rt = statusObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 60f);

            _statusText = statusObj.AddComponent<TextMeshProUGUI>();
            _statusText.fontSize = 24;
            _statusText.color = accentColor;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.font = customFont;
            _statusText.text = "";
        }

        private void CreateWebViewDisplay(Transform parent, float width, float height, float headerHeight)
        {
            var displayObj = new GameObject("WebViewDisplay");
            displayObj.transform.SetParent(parent, false);

            var rt = displayObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0, -headerHeight);

            // Background first (will be behind everything)
            var bg = displayObj.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.1f, 0.12f, 1f);
            bg.raycastTarget = false;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Create RenderTexture for WebView (Android only)
            int texWidth = Mathf.RoundToInt(width * 2);
            int texHeight = Mathf.RoundToInt(height * 2);
            _renderTexture = new RenderTexture(texWidth, texHeight, 0, RenderTextureFormat.ARGB32);
            _renderTexture.Create();

            // RawImage to display WebView
            _webViewDisplay = displayObj.AddComponent<RawImage>();
            _webViewDisplay.texture = _renderTexture;
            _webViewDisplay.color = Color.white;
            Debug.Log($"[RTTBrowserView] Created RenderTexture {texWidth}x{texHeight}");
#endif

            _webViewDisplayObj = displayObj;
            Debug.Log($"[RTTBrowserView] WebView display created");
        }

        private GameObject _webViewDisplayObj;
        #endregion

        #region WebView Bridge
        private void InitializeWebViewBridge()
        {
            // Show placeholder with "Open in Chrome" button
            // WebView texture capture requires native plugin - not implemented yet
            ShowOpenInChromeUI();
            Debug.Log("[RTTBrowserView] Showing Open in Chrome UI (WebView texture capture not implemented)");
        }

        private void ShowOpenInChromeUI()
        {
            if (_webViewDisplayObj == null) return;

            // Add instruction text
            var textObj = new GameObject("InstructionText");
            textObj.transform.SetParent(_webViewDisplayObj.transform, false);

            var textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0, 0.5f);
            textRt.anchorMax = new Vector2(1, 1);
            textRt.offsetMin = new Vector2(50, 0);
            textRt.offsetMax = new Vector2(-50, -50);

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "<size=48><color=#00E5FF>WebRTC Browser</color></size>\n\n" +
                       "Tap the button below to open Chrome\n" +
                       "with full WebRTC support (340 Mbps)";
            tmp.fontSize = 36;
            tmp.color = new Color(0.7f, 0.75f, 0.8f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.font = customFont;
            tmp.enableWordWrapping = true;

            // Add "Open in Chrome" button
            CreateOpenInChromeButton(_webViewDisplayObj.transform);
        }

        private void CreateOpenInChromeButton(Transform parent)
        {
            var btnObj = new GameObject("OpenInChromeBtn");
            btnObj.transform.SetParent(parent, false);

            var rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.25f);
            rt.anchorMax = new Vector2(0.5f, 0.25f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(500f, 100f);

            var bg = btnObj.AddComponent<Image>();
            bg.color = new Color(0f, 0.8f, 0.4f, 0.8f); // Green button

            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => OpenInChrome());

            // Button text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);

            var textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "Open in Chrome";
            tmp.fontSize = 40;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.font = customFont;
            tmp.fontStyle = FontStyles.Bold;
        }

        private void OpenInChrome()
        {
            if (!string.IsNullOrEmpty(_pendingServerIp))
            {
                // Use AndroidWebViewHelper to open in Chrome
                VRWorkspace.Utils.AndroidWebViewHelper.OpenWebRTCProtocolWithParams(
                    _pendingServerIp,
                    _pendingPort,
                    _pendingPort,
                    autoConnect: true
                );
                Debug.Log($"[RTTBrowserView] Opening Chrome: {_pendingServerIp}:{_pendingPort}");
            }
            else
            {
                Debug.LogWarning("[RTTBrowserView] No server IP set");
            }
        }

        private void ShowEditorPlaceholder()
        {
            ShowOpenInChromeUI();
        }

        private void CreateOpenInBrowserButton(Transform parent)
        {
            var btnObj = new GameObject("OpenInBrowserBtn");
            btnObj.transform.SetParent(parent, false);

            var rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.3f);
            rt.anchorMax = new Vector2(0.5f, 0.3f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(400f, 80f);

            var bg = btnObj.AddComponent<Image>();
            bg.color = new Color(themeColor.r, themeColor.g, themeColor.b, 0.3f);

            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => OpenInExternalBrowser());

            // Button text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);

            var textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "🌐 Open in Browser";
            tmp.fontSize = 32;
            tmp.color = themeColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.font = customFont;
        }

        private void OpenInExternalBrowser()
        {
            if (!string.IsNullOrEmpty(_currentUrl))
            {
                Application.OpenURL(_currentUrl);
                Debug.Log($"[RTTBrowserView] Opened in external browser: {_currentUrl}");
            }
            else
            {
                Debug.LogWarning("[RTTBrowserView] No URL to open");
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// Load a URL in the WebView.
        /// </summary>
        public void LoadUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            // Add protocol if missing
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "http://" + url;

            _currentUrl = url;
            if (_urlInput != null) _urlInput.text = url;

            Debug.Log($"[RTTBrowserView] Loading URL: {url}");
            OnUrlChanged?.Invoke(url);

#if UNITY_ANDROID && !UNITY_EDITOR
            _androidBridge?.LoadUrl(url);
#else
            _statusText.text = "Editor Mode";
#endif
        }

        /// <summary>
        /// Set connection parameters for Chrome browser.
        /// </summary>
        public void LoadLocalWebRTCProtocol(string serverIp, int port)
        {
            _pendingServerIp = serverIp;
            _pendingPort = port;

            if (_urlInput != null) _urlInput.text = $"{serverIp}:{port}";

            Debug.Log($"[RTTBrowserView] Ready to open Chrome with server: {serverIp}:{port}");
        }

        /// <summary>
        /// Extract webrtc_protocolv2.html from Resources to persistentDataPath.
        /// Returns the file path if successful, null otherwise.
        /// </summary>
        private string ExtractWebRTCHtml()
        {
            string destPath = System.IO.Path.Combine(Application.persistentDataPath, "webrtc_protocolv2.html");

            // Check if already extracted (skip re-extraction for performance)
            if (System.IO.File.Exists(destPath))
            {
                Debug.Log($"[RTTBrowserView] Using cached: {destPath}");
                return destPath;
            }

            // Load from Resources (file is stored as .txt TextAsset)
            TextAsset htmlAsset = Resources.Load<TextAsset>("webrtc_protocolv2");
            if (htmlAsset == null)
            {
                Debug.LogError("[RTTBrowserView] webrtc_protocolv2.txt not found in Resources!");
                return null;
            }

            try
            {
                // Write to persistentDataPath
                System.IO.File.WriteAllText(destPath, htmlAsset.text);
                Debug.Log($"[RTTBrowserView] Extracted to: {destPath}");
                return destPath;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RTTBrowserView] Failed to write HTML: {ex.Message}");
                return null;
            }
        }

        private string _pendingServerIp;
        private int _pendingPort;

        private void OnLocalPageLoaded(string url)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Unsubscribe to avoid multiple calls
            _androidBridge.OnPageFinished -= OnLocalPageLoaded;

            // Inject connection params via JavaScript
            if (!string.IsNullOrEmpty(_pendingServerIp))
            {
                string js = $@"
                    if (document.getElementById('host')) {{
                        document.getElementById('host').value = '{_pendingServerIp}';
                    }}
                    if (document.getElementById('port')) {{
                        document.getElementById('port').value = '{_pendingPort}';
                    }}
                    console.log('Unity injected: {_pendingServerIp}:{_pendingPort}');
                ";
                ExecuteJS(js);
                Debug.Log($"[RTTBrowserView] Injected connection params: {_pendingServerIp}:{_pendingPort}");
            }
#endif
        }

        /// <summary>
        /// Refresh the current page.
        /// </summary>
        public void Refresh()
        {
            if (!string.IsNullOrEmpty(_currentUrl))
            {
                LoadUrl(_currentUrl);
            }
        }

        /// <summary>
        /// Go back in browser history.
        /// </summary>
        public void GoBack()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _androidBridge?.GoBack();
#endif
        }

        /// <summary>
        /// Execute JavaScript in the WebView.
        /// </summary>
        public void ExecuteJS(string script)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _androidBridge?.ExecuteJS(script);
#endif
        }
        #endregion

        #region Lifecycle
        private void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Update WebView texture
            _androidBridge?.UpdateTexture();
#endif
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _androidBridge?.Destroy();
#endif
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
        }
        #endregion
    }

    /// <summary>
    /// Android WebView bridge using native Android WebView.
    /// Captures WebView content to RenderTexture for Unity display.
    /// </summary>
    public class AndroidWebViewBridge
    {
        public event Action<string> OnPageStarted;
        public event Action<string> OnPageFinished;
        public event Action<string> OnError;

        private RenderTexture _targetTexture;
        private AndroidJavaObject _webView;
        private AndroidJavaObject _activity;
        private bool _isInitialized;

        public AndroidWebViewBridge(RenderTexture targetTexture)
        {
            _targetTexture = targetTexture;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                InitializeAndroidWebView();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AndroidWebViewBridge] Init failed: {ex.Message}");
            }
#endif
        }

        private void InitializeAndroidWebView()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                // Create WebView on UI thread
                _activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        // Create WebView
                        _webView = new AndroidJavaObject("android.webkit.WebView", _activity);

                        // Configure WebView settings
                        var settings = _webView.Call<AndroidJavaObject>("getSettings");
                        settings.Call("setJavaScriptEnabled", true);
                        settings.Call("setDomStorageEnabled", true);
                        settings.Call("setMediaPlaybackRequiresUserGesture", false);
                        settings.Call("setAllowFileAccess", true);
                        settings.Call("setAllowContentAccess", true);

                        // Enable WebRTC
                        settings.Call("setMediaPlaybackRequiresUserGesture", false);

                        // Set size
                        int width = _targetTexture.width;
                        int height = _targetTexture.height;
                        _webView.Call("layout", 0, 0, width, height);

                        _isInitialized = true;
                        Debug.Log($"[AndroidWebViewBridge] WebView created {width}x{height}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AndroidWebViewBridge] WebView creation failed: {ex.Message}");
                        OnError?.Invoke(ex.Message);
                    }
                }));
            }
#endif
        }

        public void LoadUrl(string url)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_isInitialized || _webView == null) return;

            _activity?.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                try
                {
                    _webView.Call("loadUrl", url);
                    OnPageStarted?.Invoke(url);

                    // Since we don't have WebViewClient callbacks set up yet,
                    // fire OnPageFinished after a delay for local file loading
                    // This is a workaround - proper implementation needs WebViewClient
                    if (url.StartsWith("file://"))
                    {
                        // Local files load fast, fire after short delay
                        var handler = new AndroidJavaObject("android.os.Handler",
                            new AndroidJavaClass("android.os.Looper").CallStatic<AndroidJavaObject>("getMainLooper"));
                        handler.Call<bool>("postDelayed", new AndroidJavaRunnable(() =>
                        {
                            OnPageFinished?.Invoke(url);
                        }), 500L); // 500ms delay for local file
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AndroidWebViewBridge] LoadUrl failed: {ex.Message}");
                    OnError?.Invoke(ex.Message);
                }
            }));
#endif
        }

        public void GoBack()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_isInitialized || _webView == null) return;

            _activity?.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                if (_webView.Call<bool>("canGoBack"))
                    _webView.Call("goBack");
            }));
#endif
        }

        public void ExecuteJS(string script)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_isInitialized || _webView == null) return;

            _activity?.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                _webView.Call("evaluateJavascript", script, null);
            }));
#endif
        }

        public void UpdateTexture()
        {
            // Note: Capturing Android WebView to texture requires either:
            // 1. Drawing to a Bitmap and copying to Unity texture (slow)
            // 2. Using Surface/SurfaceTexture with OpenGL (complex)
            // 3. Using a native plugin (recommended for production)
            //
            // For now, this is a placeholder. Full implementation would require
            // a native Android plugin for efficient texture capture.
        }

        public void Destroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_webView != null)
            {
                _activity?.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    _webView.Call("destroy");
                    _webView.Dispose();
                    _webView = null;
                }));
            }
#endif
        }
    }
}
