using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Displays connection progress UI on a WorldPanelPlus panel.
/// Shows black background, progress bar, percentage text, and status messages.
/// Progress: 0-50% = Server Setup (shared), 50-100% = ICE/PC (per panel)
/// </summary>
public class PanelProgressOverlay : MonoBehaviour
{
    #region Private Fields
    private WorldPanelPlus _panel;
    private GameObject _overlayRoot;
    private Canvas _canvas;
    private RectTransform _canvasRT;

    // UI Elements
    private Image _background;
    private Image _progressBarBg;
    private Image _progressBarFill;
    private TextMeshProUGUI _statusText;
    private TextMeshProUGUI _percentText;

    // State
    private int _serverProgress;    // 0-100 from server
    private int _iceProgress;       // 0-100 from ICE
    private int _displayProgress;   // Combined 0-100
    private bool _isReady;
    private bool _isInitialized;
    #endregion

    #region Events
    public event Action OnReady;
    #endregion

    #region Properties
    public int Progress => _displayProgress;
    public bool IsReady => _isReady;
    #endregion

    #region Public API

    /// <summary>
    /// Initialize the overlay for a specific panel.
    /// </summary>
    public void Initialize(WorldPanelPlus panel)
    {
        if (_isInitialized) return;

        _panel = panel;
        CreateOverlayUI();
        _isInitialized = true;

        // Start with 0% progress
        UpdateDisplay(0, "Initializing...");
    }

    /// <summary>
    /// Set server setup progress (0-100 maps to display 0-50%)
    /// </summary>
    public void SetServerProgress(int progress)
    {
        _serverProgress = Mathf.Clamp(progress, 0, 100);

        // Server progress maps to 0-50% of display
        int serverContribution = _serverProgress / 2;

        // If server is done (100%), ICE progress starts contributing
        if (_serverProgress >= 100)
        {
            _displayProgress = 50 + (_iceProgress / 2);
        }
        else
        {
            _displayProgress = serverContribution;
        }

        string status = _serverProgress < 100 ? "Server Setup..." : "Server Ready";
        UpdateDisplay(_displayProgress, status);
    }

    /// <summary>
    /// Set ICE negotiation progress (0-100 maps to display 50-100%)
    /// </summary>
    public void SetIceProgress(int progress)
    {
        _iceProgress = Mathf.Clamp(progress, 0, 100);

        // ICE progress maps to 50-100% of display (only after server is done)
        if (_serverProgress >= 100)
        {
            _displayProgress = 50 + (_iceProgress / 2);
        }

        string status;
        if (_iceProgress >= 100)
        {
            status = "Ready to connect";
            if (!_isReady)
            {
                _isReady = true;
                OnReady?.Invoke();
            }
        }
        else
        {
            status = "Connecting...";
        }

        UpdateDisplay(_displayProgress, status);
    }

    /// <summary>
    /// Show "Connecting..." state before auto-start
    /// </summary>
    public void ShowConnecting()
    {
        UpdateDisplay(100, "Connecting...");
    }

    /// <summary>
    /// Hide overlay and show video content
    /// </summary>
    public void Hide()
    {
        if (_overlayRoot != null)
        {
            _overlayRoot.SetActive(false);
        }
    }

    /// <summary>
    /// Show overlay
    /// </summary>
    public void Show()
    {
        if (_overlayRoot != null)
        {
            _overlayRoot.SetActive(true);
        }
    }

    #endregion

    #region Private Methods

    private void CreateOverlayUI()
    {
        if (_panel == null || _panel.board == null) return;

        // Create overlay root as child of panel
        _overlayRoot = new GameObject("ProgressOverlay");
        _overlayRoot.transform.SetParent(_panel.board, false);

        // Position slightly in front of board
        // Board has localScale = (width, height, 1), so we need to counteract that
        _overlayRoot.transform.localPosition = new Vector3(0, 0, -0.005f / _panel.width); // Adjust Z for board scale
        _overlayRoot.transform.localRotation = Quaternion.identity;

        // Create World Space Canvas
        _canvas = _overlayRoot.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 100;

        // Size canvas to match panel
        _canvasRT = _overlayRoot.GetComponent<RectTransform>();
        // Board is a unit quad scaled to (width, height, 1)
        // We want the canvas to cover exactly (-0.5 to 0.5) in local space = full quad
        // Use pixels for UI resolution (1000 px/m)
        float pixelWidth = _panel.width * 1000f;
        float pixelHeight = _panel.height * 1000f;
        _canvasRT.sizeDelta = new Vector2(pixelWidth, pixelHeight);
        // Scale: convert pixels to meters, then counteract board's scale
        // Final world size should be (width, height) meters
        // Board scale = (width, height, 1), so we need localScale = (1/width, 1/height, 1) * pixelToMeters
        float scaleX = 1f / _panel.width * 0.001f * _panel.width;   // = 0.001
        float scaleY = 1f / _panel.height * 0.001f * _panel.height; // = 0.001
        // Simplified: canvas in board's local space should be (1, 1) to match quad
        _canvasRT.localScale = new Vector3(1f / pixelWidth, 1f / pixelHeight, 1f);

        // Add CanvasScaler for consistent sizing
        var scaler = _overlayRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100f;

        // Add GraphicRaycaster (optional, for interaction)
        _overlayRoot.AddComponent<GraphicRaycaster>();

        // Create UI elements
        CreateBackground(pixelWidth, pixelHeight);
        CreateProgressBar(pixelWidth, pixelHeight);
        CreateStatusText(pixelWidth, pixelHeight);
        CreatePercentText(pixelWidth, pixelHeight);
    }

    private void CreateBackground(float w, float h)
    {
        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(_canvasRT, false);

        _background = bgGO.AddComponent<Image>();
        _background.color = Color.black;
        _background.raycastTarget = false;

        var rt = bgGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void CreateProgressBar(float w, float h)
    {
        float barWidth = w * 0.6f;
        float barHeight = h * 0.05f;

        // Progress bar background (dark gray)
        var bgGO = new GameObject("ProgressBarBg");
        bgGO.transform.SetParent(_canvasRT, false);

        _progressBarBg = bgGO.AddComponent<Image>();
        _progressBarBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        _progressBarBg.raycastTarget = false;

        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0.5f, 0.5f);
        bgRT.anchorMax = new Vector2(0.5f, 0.5f);
        bgRT.pivot = new Vector2(0.5f, 0.5f);
        bgRT.sizeDelta = new Vector2(barWidth, barHeight);
        bgRT.anchoredPosition = Vector2.zero; // Center of canvas

        // Progress bar fill (white) - using anchor-based scaling instead of Filled type
        // This works without requiring a sprite
        var fillGO = new GameObject("ProgressBarFill");
        fillGO.transform.SetParent(bgGO.transform, false);

        _progressBarFill = fillGO.AddComponent<Image>();
        _progressBarFill.color = Color.white;
        _progressBarFill.raycastTarget = false;
        // Use Simple type (default) - works without sprite

        var fillRT = fillGO.GetComponent<RectTransform>();
        // Anchor to left edge, scale width via anchorMax.x
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = new Vector2(0f, 1f); // Start with 0 width (0% progress)
        fillRT.pivot = new Vector2(0f, 0.5f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
    }

    private void CreateStatusText(float w, float h)
    {
        var textGO = new GameObject("StatusText");
        textGO.transform.SetParent(_canvasRT, false);

        _statusText = textGO.AddComponent<TextMeshProUGUI>();
        _statusText.text = "Initializing...";
        _statusText.color = Color.white;
        _statusText.fontSize = h * 0.06f;
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.raycastTarget = false;

        var rt = textGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w * 0.8f, h * 0.1f);
        rt.anchoredPosition = new Vector2(0, h * 0.12f); // Above progress bar
    }

    private void CreatePercentText(float w, float h)
    {
        var textGO = new GameObject("PercentText");
        textGO.transform.SetParent(_canvasRT, false);

        _percentText = textGO.AddComponent<TextMeshProUGUI>();
        _percentText.text = "0%";
        _percentText.color = Color.white;
        _percentText.fontSize = h * 0.05f;
        _percentText.alignment = TextAlignmentOptions.Center;
        _percentText.raycastTarget = false;

        var rt = textGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w * 0.3f, h * 0.08f);
        rt.anchoredPosition = new Vector2(0, -h * 0.08f); // Below progress bar
    }

    private void UpdateDisplay(int progress, string status)
    {
        _displayProgress = Mathf.Clamp(progress, 0, 100);

        if (_progressBarFill != null)
        {
            // Use anchor-based scaling: anchorMax.x controls width percentage
            var rt = _progressBarFill.GetComponent<RectTransform>();
            if (rt != null)
            {
                float fillPercent = _displayProgress / 100f;
                rt.anchorMax = new Vector2(fillPercent, 1f);
            }
        }

        if (_percentText != null)
        {
            _percentText.text = $"{_displayProgress}%";
        }

        if (_statusText != null)
        {
            _statusText.text = status;
        }
    }

    #endregion

    #region Cleanup

    private void OnDestroy()
    {
        if (_overlayRoot != null)
        {
            Destroy(_overlayRoot);
        }
    }

    #endregion
}
