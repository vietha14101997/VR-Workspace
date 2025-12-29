using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

namespace VRWorkspace.UI
{
    /// <summary>
    /// Embedded WebView component for RTTMenuFrame.
    /// Renders WebView content to a RawImage for display in VR UI.
    /// Uses SimpleUnity3DWebView on Android, placeholder on Editor.
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

        [Header("WebView Settings")]
        [Tooltip("Texture width in pixels (height calculated from aspect ratio)")]
        public int textureWidth = 1920;
        [Tooltip("Texture update interval in milliseconds")]
        public int updateIntervalMs = 33; // ~30fps
        #endregion

        #region Private Fields
        private RawImage _webViewDisplay;
        private GameObject _headerBar;
        private TMP_InputField _urlInput;
        private TextMeshProUGUI _statusText;
        private string _currentUrl;
        private bool _isLoading;

        // WebViewManager reference (stored as Component to avoid type dependency)
        private Component _webViewManagerComponent;

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

            // Use explicit sizeDelta instead of anchors for WebViewManager compatibility
            // WebViewManager.Start() reads sizeDelta directly to calculate texture dimensions
            rt.anchorMin = new Vector2(0.5f, 0);
            rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 0);

            // Set explicit size (WebViewManager needs this for texture calculation)
            float displayHeight = height - headerHeight;
            rt.sizeDelta = new Vector2(width, displayHeight);

            // RawImage to display WebView content
            _webViewDisplay = displayObj.AddComponent<RawImage>();
            _webViewDisplay.color = new Color(0.08f, 0.1f, 0.12f, 1f); // Dark background until loaded

            _webViewDisplayObj = displayObj;
            Debug.Log($"[RTTBrowserView] WebView display created with size: {width}x{displayHeight}");
        }

        private GameObject _webViewDisplayObj;
        #endregion

        #region WebView Bridge
        private void InitializeWebViewBridge()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            InitializeSimpleWebView();
#else
            // Editor: show placeholder
            ShowEditorPlaceholder();
            Debug.Log("[RTTBrowserView] Editor mode - showing placeholder");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private GameObject _webViewPrefabInstance;

        /// <summary>
        /// Initialize SimpleUnity3DWebView by instantiating the pre-configured prefab.
        /// This avoids reflection issues with internal PointerEventSource class.
        /// </summary>
        private void InitializeSimpleWebView()
        {
            try
            {
                // Load the pre-configured WebView prefab from Resources
                var prefab = Resources.Load<GameObject>("WebViewSample");
                if (prefab == null)
                {
                    Debug.LogError("[RTTBrowserView] WebViewSample.prefab not found in Resources!");
                    return;
                }

                Debug.Log("[RTTBrowserView] Loaded WebViewSample prefab");

                // Instantiate as child of our display container
                _webViewPrefabInstance = Instantiate(prefab, _webViewDisplayObj.transform);
                _webViewPrefabInstance.name = "WebViewInstance";

                // Find the WebViewManager component in the prefab hierarchy
                foreach (var comp in _webViewPrefabInstance.GetComponentsInChildren<Component>(true))
                {
                    if (comp != null && comp.GetType().Name == "WebViewManager")
                    {
                        _webViewManagerComponent = comp;
                        Debug.Log($"[RTTBrowserView] Found WebViewManager: {comp}");
                        break;
                    }
                }

                if (_webViewManagerComponent == null)
                {
                    Debug.LogError("[RTTBrowserView] WebViewManager not found in prefab!");
                    return;
                }

                // Find and resize the RawImage in the prefab to fill our container
                var prefabRawImage = _webViewPrefabInstance.GetComponentInChildren<RawImage>();
                if (prefabRawImage != null)
                {
                    var rt = prefabRawImage.GetComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    rt.localScale = Vector3.one;
                    Debug.Log($"[RTTBrowserView] Configured prefab RawImage to fill container");
                }

                // Position the prefab instance to fill our WebViewDisplay area
                var instanceRT = _webViewPrefabInstance.GetComponent<RectTransform>();
                if (instanceRT == null)
                {
                    instanceRT = _webViewPrefabInstance.AddComponent<RectTransform>();
                }
                instanceRT.anchorMin = Vector2.zero;
                instanceRT.anchorMax = Vector2.one;
                instanceRT.offsetMin = Vector2.zero;
                instanceRT.offsetMax = Vector2.zero;
                instanceRT.localScale = Vector3.one;
                instanceRT.localPosition = Vector3.zero;

                // Hide the sample prefab's UI elements (we use our own header bar)
                HideSamplePrefabUI();

                // Hide our placeholder RawImage (use prefab's instead)
                if (_webViewDisplay != null)
                {
                    _webViewDisplay.enabled = false;
                }

                Debug.Log("[RTTBrowserView] SimpleUnity3DWebView initialized from prefab");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RTTBrowserView] Failed to initialize WebView: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Hide the sample prefab's built-in UI elements (we use our own header bar).
        /// </summary>
        private void HideSamplePrefabUI()
        {
            if (_webViewPrefabInstance == null) return;

            foreach (Transform child in _webViewPrefabInstance.GetComponentsInChildren<Transform>(true))
            {
                if (child == null) continue;
                string name = child.name.ToLower();

                // Hide address bar, navigation buttons, and Canvas from sample prefab
                if (name.Contains("address") || name.Contains("urlfield") ||
                    name.Contains("back") || name.Contains("forward") ||
                    name.Contains("reload") || name.Contains("navigation") ||
                    name.Contains("canvas"))
                {
                    // Don't hide the main Canvas that contains the RawImage
                    if (child.GetComponentInChildren<RawImage>() != null &&
                        child.GetComponent<Canvas>() != null)
                    {
                        continue;
                    }

                    child.gameObject.SetActive(false);
                    Debug.Log($"[RTTBrowserView] Hidden sample UI: {child.name}");
                }
            }
        }
#endif

        private void ShowEditorPlaceholder()
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
                       "<size=32>SimpleUnity3DWebView</size>\n" +
                       "WebView will display here on Android device";
            tmp.fontSize = 36;
            tmp.color = new Color(0.7f, 0.75f, 0.8f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.font = customFont;
            tmp.enableWordWrapping = true;

            // Add "Open in Chrome" button for testing in Editor
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
            tmp.text = "Test: Open in Chrome";
            tmp.fontSize = 36;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.font = customFont;
            tmp.fontStyle = FontStyles.Bold;
        }

        private void OpenInChrome()
        {
            if (!string.IsNullOrEmpty(_pendingServerIp))
            {
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
        #endregion

        #region Public API
        /// <summary>
        /// Load a URL in the WebView.
        /// </summary>
        public void LoadUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            // Add protocol if missing
            if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("file://"))
                url = "http://" + url;

            _currentUrl = url;
            if (_urlInput != null) _urlInput.text = url;

            Debug.Log($"[RTTBrowserView] Loading URL: {url}");
            OnUrlChanged?.Invoke(url);

#if UNITY_ANDROID && !UNITY_EDITOR
            InvokeWebViewMethod("LoadUrl", url);
#else
            if (_statusText != null) _statusText.text = "Editor Mode";
#endif
        }

        /// <summary>
        /// Load webrtc_protocolv2.html from Resources with connection parameters.
        /// Extracts HTML to persistentDataPath and loads via file:// URL.
        /// </summary>
        public void LoadLocalWebRTCProtocol(string serverIp, int port)
        {
            _pendingServerIp = serverIp;
            _pendingPort = port;

            if (_urlInput != null) _urlInput.text = $"{serverIp}:{port}";

#if UNITY_ANDROID && !UNITY_EDITOR
            // Extract HTML from Resources and load it
            string htmlPath = ExtractWebRTCHtml(serverIp, port);
            if (!string.IsNullOrEmpty(htmlPath))
            {
                string fileUrl = "file://" + htmlPath;
                Debug.Log($"[RTTBrowserView] Loading local WebRTC: {fileUrl}");

                // Wait for WebViewManager to initialize, then load
                StartCoroutine(LoadUrlAfterInit(fileUrl));
            }
#else
            Debug.Log($"[RTTBrowserView] Editor mode - WebView would load with server: {serverIp}:{port}");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private System.Collections.IEnumerator LoadUrlAfterInit(string url)
        {
            Debug.Log($"[RTTBrowserView] LoadUrlAfterInit started, waiting for WebView...");

            // Wait longer for WebViewManager to fully initialize
            // Android WebView needs time to create the native view
            yield return new WaitForSeconds(0.5f);

            if (_webViewManagerComponent != null)
            {
                Debug.Log($"[RTTBrowserView] WebViewManager exists, loading URL...");
                InvokeWebViewMethod("LoadUrl", url);
                Debug.Log($"[RTTBrowserView] LoadUrl called: {url}");

                // Also try calling Init if available
                InvokeWebViewMethod("Init");
            }
            else
            {
                Debug.LogError("[RTTBrowserView] WebViewManager is null after wait");
            }
        }

        /// <summary>
        /// Invoke a method on WebViewManager via reflection.
        /// </summary>
        private void InvokeWebViewMethod(string methodName, params object[] args)
        {
            if (_webViewManagerComponent == null) return;

            try
            {
                var method = _webViewManagerComponent.GetType().GetMethod(methodName);
                method?.Invoke(_webViewManagerComponent, args);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RTTBrowserView] Failed to invoke {methodName}: {ex.Message}");
            }
        }
#endif

        /// <summary>
        /// Extract webrtc_protocolv2.html from Resources to persistentDataPath.
        /// Injects server connection parameters into the HTML.
        /// Returns the file path if successful, null otherwise.
        /// </summary>
        private string ExtractWebRTCHtml(string serverIp, int port)
        {
            string destPath = System.IO.Path.Combine(Application.persistentDataPath, "webrtc_protocolv2.html");

            // Load from Resources (file is stored as .txt TextAsset)
            TextAsset htmlAsset = Resources.Load<TextAsset>("webrtc_protocolv2");
            if (htmlAsset == null)
            {
                Debug.LogError("[RTTBrowserView] webrtc_protocolv2.txt not found in Resources!");
                return null;
            }

            try
            {
                // Inject connection parameters into HTML
                string html = htmlAsset.text;

                // Replace default values with actual server params
                // The HTML should have placeholders or we inject via script
                string injectionScript = $@"
<script>
    // Auto-injected by Unity RTTBrowserView
    window.UNITY_WS_HOST = '{serverIp}';
    window.UNITY_WS_PORT = {port};
    window.UNITY_AUTO_CONNECT = true;

    // Wait for page load then set values
    document.addEventListener('DOMContentLoaded', function() {{
        var hostInput = document.getElementById('host');
        var portInput = document.getElementById('port');
        if (hostInput) hostInput.value = '{serverIp}';
        if (portInput) portInput.value = '{port}';
        console.log('[Unity] Injected connection: {serverIp}:{port}');
    }});
</script>
";
                // Inject before </head>
                if (html.Contains("</head>"))
                {
                    html = html.Replace("</head>", injectionScript + "</head>");
                }
                else
                {
                    // Fallback: prepend
                    html = injectionScript + html;
                }

                // Write to persistentDataPath
                System.IO.File.WriteAllText(destPath, html);
                Debug.Log($"[RTTBrowserView] Extracted with params to: {destPath}");
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

        /// <summary>
        /// Refresh the current page.
        /// </summary>
        public void Refresh()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            InvokeWebViewMethod("Reload");
#else
            if (!string.IsNullOrEmpty(_currentUrl))
            {
                LoadUrl(_currentUrl);
            }
#endif
        }

        /// <summary>
        /// Go back in browser history.
        /// </summary>
        public void GoBack()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            InvokeWebViewMethod("GoBack");
#endif
        }

        /// <summary>
        /// Execute JavaScript in the WebView.
        /// </summary>
        public void ExecuteJS(string script)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            InvokeWebViewMethod("EvaluateJavascript", script);
#endif
        }
        #endregion

        #region Lifecycle
        private void OnDestroy()
        {
            // WebViewManager handles its own cleanup
            Debug.Log("[RTTBrowserView] Destroyed");
        }
        #endregion
    }
}
