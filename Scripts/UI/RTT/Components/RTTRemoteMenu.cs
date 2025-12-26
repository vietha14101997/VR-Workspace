using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// RTT-based Remote Desktop connection menu.
/// Features: Host/Port inputs, Monitors/Resolution/Bitrate/FPS dropdowns, QR scanner.
/// Easy to use: Set themeColor, subscribe to events, call BuildUI().
/// </summary>
public class RTTRemoteMenu : MonoBehaviour
{
    #region Configuration
    [Header("Theme")]
    public Color themeColor = new Color(0f, 0.9f, 1f);
    public Color accentColor = new Color(0.8f, 0.4f, 1f);
    public TMP_FontAsset customFont;
    #endregion

    #region Events
    public event Action OnBackClicked;
    public event Action OnConnectClicked;
    public event Action OnQRClicked;
    #endregion

    #region Private Fields
    // UI References
    private GameObject _hostInput;
    private GameObject _portInput;
    private GameObject _monitorsDropdown;
    private GameObject _resolutionDropdown;
    private GameObject _bitrateDropdown;
    private GameObject _fpsDropdown;
    private GameObject _bodyContainer;

    // QR Scanner
    private QRScannerManager _qrScannerManager;

    // Parent references
    private RTTMenuFrame _menuFrame;
    private float _containerWidth;
    private float _containerHeight;

    // Font sizes
    private const int LABEL_FONT_SIZE = 40;
    private const int INPUT_FONT_SIZE = 42;
    private const int DROPDOWN_LABEL_FONT_SIZE = 40;
    private const int DROPDOWN_VALUE_FONT_SIZE = 42;
    #endregion

    #region Public Accessors (Easy Form Data Access)
    public string Host => VRInputFieldFactory.GetValue(_hostInput);
    public string Port => VRInputFieldFactory.GetValue(_portInput);
    public int MonitorIndex => VRDropdownFactory.GetSelectedIndex(_monitorsDropdown);
    public string Resolution => VRDropdownFactory.GetSelectedValue(_resolutionDropdown);
    public string Bitrate => VRDropdownFactory.GetSelectedValue(_bitrateDropdown);
    public string FPS => VRDropdownFactory.GetSelectedValue(_fpsDropdown);
    #endregion

    #region Build UI
    /// <summary>
    /// Build the Remote Menu UI inside the given container.
    /// </summary>
    public void BuildUI(Transform parent, float containerWidth, float containerHeight)
    {
        _containerWidth = containerWidth;
        _containerHeight = containerHeight;

        // Try to find RTTMenuFrame parent
        _menuFrame = GetComponentInParent<RTTMenuFrame>();

        // Setup RectTransform
        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        // Build layout
        float contentW = containerWidth * 0.875f;

        // Calculate heights from font sizes
        float inputH = VRInputFieldFactory.CalculateHeight(INPUT_FONT_SIZE, true);
        float dropdownH = VRDropdownFactory.CalculateHeight(DROPDOWN_VALUE_FONT_SIZE);

        // Layout heights
        float headerH = containerHeight * 0.1f;
        float connectButtonH = containerHeight * 0.125f;
        float gap = containerHeight * 0.04f;
        float gridH = dropdownH * 2 + gap;

        float y = containerHeight;

        // Header (Back button, Title, QR button)
        y -= headerH;
        CreateHeader(transform, 0, y, containerWidth, headerH);

        // Separator 1 position
        float sep1Y = y - gap * 1.25f;

        // Body container
        float bodyContainerHeight = containerHeight - 2f * headerH;
        y -= 2.25f * gap + bodyContainerHeight;
        _bodyContainer = CreateContainer(transform, "BodyContainer", 0, y, containerWidth, bodyContainerHeight);

        // Input Row (Host / Port)
        float bodyY = bodyContainerHeight;
        bodyY -= inputH;
        CreateInputRow(_bodyContainer.transform, 0, bodyY, contentW, inputH, gap * 1.5f);

        // Separator 2 position
        float sep2Y = y + bodyY - gap * 1.5f;

        // Grid 2x2 (Monitors, Resolution, Bitrate, FPS)
        bodyY -= gap * 2.5f + gridH;
        CreateGrid(_bodyContainer.transform, 0, bodyY, contentW, 0.04f * contentW, dropdownH, gap * 1.5f, gap);

        // Connect Button
        CreateConnectButton(_bodyContainer.transform, contentW * 0.625f, connectButtonH);

        // Configure horizontal separators if RTTMenuFrame available
        if (_menuFrame != null)
        {
            float separatorLength = contentW / containerWidth;
            ConfigureHorizontalSeparators(containerHeight, sep1Y, sep2Y, separatorLength);
        }

        Debug.Log("[RTTRemoteMenu] UI built");
    }
    #endregion

    #region Header
    private void CreateHeader(Transform parent, float x, float y, float w, float h)
    {
        var header = CreateContainer(parent, "Header", x, y, w, h);

        // Back button
        float backW = 280f;
        CreateButton(header.transform, 0, 0, backW, h, "Back", LoadIcon("back"), themeColor,
            () => OnBackClicked?.Invoke());

        // Title - centered
        CreateLabel(header.transform, 0, 0, w, h, "Remote Desktop", 48, Color.white, true, TextAlignmentOptions.Center);

        // QR button
        float qrSize = 100f;
        CreateButton(header.transform, w - qrSize, (h - qrSize) / 2f, qrSize, qrSize, "", LoadIcon("qr"), accentColor,
            () => ShowQRScanner());
    }
    #endregion

    #region Input Row
    private void CreateInputRow(Transform parent, float x, float y, float w, float h, float gapX)
    {
        var row = CreateContainer(parent, "InputRow", x, y, w, h);

        float hostW = (w - gapX) * 0.5f;
        float portW = (w - gapX) * 0.5f;

        // Host Input
        _hostInput = VRInputFieldFactory.CreateLabeledInputField(
            row.transform, hostW,
            "Host", "192.168.1.10", accentColor,
            onEndEdit: (value) => Debug.Log("Host: " + value),
            labelFontSize: LABEL_FONT_SIZE, inputFontSize: INPUT_FONT_SIZE, font: customFont);
        PositionElement(_hostInput, 0, 0);

        // Port Input
        _portInput = VRInputFieldFactory.CreateLabeledInputField(
            row.transform, portW,
            "Port", "9000", themeColor,
            onEndEdit: (value) => Debug.Log("Port: " + value),
            labelFontSize: LABEL_FONT_SIZE, inputFontSize: INPUT_FONT_SIZE, font: customFont,
            contentType: TMP_InputField.ContentType.IntegerNumber);
        PositionElement(_portInput, hostW + gapX, 0);
    }
    #endregion

    #region Grid (Dropdowns)
    private void CreateGrid(Transform parent, float x, float y, float w, float h, float dropdownH, float gapX, float gapY)
    {
        var grid = CreateContainer(parent, "Grid", x, y, w, h);

        float cellW = (w - gapX) / 2f;

        // Dropdown options
        var monitorOptions = new List<string> { "1 Monitor", "2 Monitors", "3 Monitors" };
        var resolutionOptions = new List<string> { "1280 x 720", "1366 x 768", "1600 x 900", "1920 x 1080" };
        var bitrateOptions = new List<string> { "5 Mbps", "10 Mbps", "20 Mbps", "30 Mbps", "50 Mbps" };
        var fpsOptions = new List<string> { "30 FPS", "45 FPS", "60 FPS" };

        // Row 1 (top)
        float row1Y = dropdownH + gapY;

        _monitorsDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "Monitors", LoadIcon("monitor"), themeColor,
            monitorOptions, 1,
            onValueChanged: (index, value) => Debug.Log("Monitor: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_monitorsDropdown, 0, row1Y);

        _resolutionDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "Resolution", LoadIcon("resolution"), accentColor,
            resolutionOptions, 3,
            onValueChanged: (index, value) => Debug.Log("Resolution: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_resolutionDropdown, cellW + gapX, row1Y);

        // Row 2 (bottom)
        _bitrateDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "Bitrate", LoadIcon("bitrate"), accentColor,
            bitrateOptions, 2,
            onValueChanged: (index, value) => Debug.Log("Bitrate: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_bitrateDropdown, 0, 0);

        _fpsDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "FPS", LoadIcon("fps"), themeColor,
            fpsOptions, 2,
            onValueChanged: (index, value) => Debug.Log("FPS: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_fpsDropdown, cellW + gapX, 0);
    }
    #endregion

    #region Connect Button
    private void CreateConnectButton(Transform parent, float w, float h)
    {
        var config = new VRButtonFactory.ButtonConfig
        {
            label = "CONNECT",
            themeColor = themeColor,
            width = w,
            height = h,
            fontSize = 48,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.85f,
            cornerRadius = 0.15f,
            edgePadding = 0.08f,
            enablePulse = true,
            pulseSpeed = 1.8f,
            popAmount = 0.05f,
            useConnectButtonShader = true,
            connectColorA = themeColor,
            connectColorB = new Color(0.1f, 0.5f, 0.85f),
            connectColorC = accentColor
        };

        var btn = VRButtonFactory.CreateButton(parent, config, () => OnConnectClicked?.Invoke());

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = Vector2.zero;
    }
    #endregion

    #region UI Helpers
    private GameObject CreateContainer(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(-w / 2f, y);
        rt.sizeDelta = new Vector2(w, h);
        return go;
    }

    private void PositionElement(GameObject element, float x, float y)
    {
        RectTransform rt = element.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
    }

    private void CreateButton(Transform parent, float x, float y, float w, float h,
        string text, Sprite icon, Color col, UnityEngine.Events.UnityAction onClick)
    {
        bool hasText = !string.IsNullOrEmpty(text);
        bool hasIcon = icon != null;

        var config = new VRButtonFactory.ButtonConfig
        {
            label = hasText ? text : (icon != null ? icon.name : "Button"),
            icon = icon,
            themeColor = col,
            width = w,
            height = h,
            fontSize = 40,
            font = customFont,
            iconOnly = hasIcon && !hasText,
            textOnly = hasText && !hasIcon,
            horizontalLayout = hasIcon && hasText,
            iconSize = hasIcon && !hasText ? 44f : 44f,
            iconPadding = hasIcon && hasText ? 64f : 28f,
            backgroundAlpha = 0.08f,
            borderWidth = 0.04f,
            popAmount = 0.0125f
        };

        var btn = VRButtonFactory.CreateButton(parent, config, onClick);

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
    }

    private GameObject CreateLabel(Transform parent, float x, float y, float w, float h,
        string text, int fontSize, Color col, bool bold,
        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject("Label_" + text);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.color = col;
        txt.alignment = align;
        txt.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false;
        txt.overflowMode = TextOverflowModes.Ellipsis;
        if (customFont) txt.font = customFont;

        return go;
    }

    private Sprite LoadIcon(string name)
    {
        Sprite icon = Resources.Load<Sprite>($"icon_{name}");
        if (icon == null) icon = Resources.Load<Sprite>($"Icons/{name}");
        if (icon == null) icon = Resources.Load<Sprite>(name);
        return icon;
    }
    #endregion

    #region Horizontal Separators
    private void ConfigureHorizontalSeparators(float containerHeight, float sep1Y, float sep2Y, float sep2Length)
    {
        if (_menuFrame == null) return;

        float frameLogicalHeight = _menuFrame.LogicalWidthValue * (_menuFrame.PanelHeight / _menuFrame.PanelWidth);
        float marginBottom = 50f; // Default content margin
        float marginTop = 50f;
        float contentHeight = frameLogicalHeight - marginTop - marginBottom;

        // Calculate UV positions
        float sep1FrameY = marginBottom + sep1Y;
        float sep2FrameY = marginBottom + sep2Y;

        float sep1UV = sep1FrameY / frameLogicalHeight;
        float sep2UV = sep2FrameY / frameLogicalHeight;

        // Set horizontal separators
        Vector4 positions = new Vector4(sep1UV, sep2UV, 0, 0);
        Vector4 lengths = new Vector4(1f, sep2Length, 1f, 1f);
        _menuFrame.SetHorizontalSeparators(2, positions, 0.003f, 0.012f, 0.9f, lengths);
    }

    private void DisableHorizontalSeparators()
    {
        if (_menuFrame == null) return;
        _menuFrame.SetHorizontalSeparators(0, Vector4.zero);
    }
    #endregion

    #region QR Scanner
    /// <summary>
    /// Show QR Scanner view.
    /// </summary>
    public void ShowQRScanner()
    {
        OnQRClicked?.Invoke();

        // If scanner already active, close it
        if (_qrScannerManager != null)
        {
            CloseQRScanner();
            return;
        }

        // Find RTTTaskbar
        RTTTaskbar taskbar = FindObjectOfType<RTTTaskbar>();

        // Create QRScannerManager
        GameObject managerObj = new GameObject("QRScannerManager");
        _qrScannerManager = managerObj.AddComponent<QRScannerManager>();
        _qrScannerManager.themeColor = themeColor;
        _qrScannerManager.accentColor = accentColor;
        _qrScannerManager.customFont = customFont;

        // Subscribe events
        _qrScannerManager.OnQRScanned += OnQRCodeScanned;
        _qrScannerManager.OnCancelled += OnQRScanCancelled;

        // Start scanning with RTTMenuFrame
        if (_menuFrame != null)
        {
            _qrScannerManager.StartScanning(_menuFrame, taskbar);
        }
    }

    private void CloseQRScanner()
    {
        if (_qrScannerManager != null)
        {
            _qrScannerManager.StopScanning(); // This also calls ShowOriginalUI()
            Destroy(_qrScannerManager.gameObject);
            _qrScannerManager = null;
        }
    }

    private void OnQRScanCancelled()
    {
        _qrScannerManager = null;
    }

    private void OnQRCodeScanned(QRScannerConfig config)
    {
        Debug.Log($"[RTTRemoteMenu] QR Scanned: {config}");
        _qrScannerManager = null;
        SetConfig(config);
    }
    #endregion

    #region Set Config (Fill Form)
    /// <summary>
    /// Fill the form with values from a QRScannerConfig.
    /// </summary>
    public void SetConfig(QRScannerConfig config)
    {
        if (config == null) return;

        // Host & Port
        if (!string.IsNullOrEmpty(config.host))
            VRInputFieldFactory.SetValue(_hostInput, config.host);

        if (!string.IsNullOrEmpty(config.port))
            VRInputFieldFactory.SetValue(_portInput, config.port);

        // Resolution
        if (!string.IsNullOrEmpty(config.resolution))
        {
            int index = FindOptionIndex(
                new[] { "1920 x 1080", "1600 x 900", "1366 x 768", "1280 x 720" },
                config.resolution);
            if (index >= 0)
                VRDropdownFactory.SetSelectedIndex(_resolutionDropdown, index);
        }

        // Bitrate
        if (!string.IsNullOrEmpty(config.bitrate))
        {
            int index = FindOptionIndex(
                new[] { "5 Mbps", "10 Mbps", "20 Mbps", "30 Mbps", "50 Mbps" },
                config.bitrate);
            if (index >= 0)
                VRDropdownFactory.SetSelectedIndex(_bitrateDropdown, index);
        }

        // FPS
        if (!string.IsNullOrEmpty(config.fps))
        {
            int index = FindOptionIndex(
                new[] { "30 FPS", "45 FPS", "60 FPS" },
                config.fps);
            if (index >= 0)
                VRDropdownFactory.SetSelectedIndex(_fpsDropdown, index);
        }

        // Monitors
        if (config.monitors >= 1 && config.monitors <= 3)
        {
            VRDropdownFactory.SetSelectedIndex(_monitorsDropdown, config.monitors - 1);
        }
    }

    private int FindOptionIndex(string[] options, string value)
    {
        string normalized = value.Replace("x", " x ").Replace("  ", " ").Trim();
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                options[i].Replace(" ", "").Equals(value.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
    #endregion

    #region Cleanup
    private void OnDestroy()
    {
        DisableHorizontalSeparators();

        if (_qrScannerManager != null)
        {
            _qrScannerManager.OnQRScanned -= OnQRCodeScanned;
            _qrScannerManager.OnCancelled -= OnQRScanCancelled;
        }
    }
    #endregion
}
