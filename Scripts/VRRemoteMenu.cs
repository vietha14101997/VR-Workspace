using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

public class VRRemoteMenu : MonoBehaviour
{
    public Color themeColor = new Color(0.0f, 0.9f, 1.0f);
    public Color accentColor = new Color(0.8f, 0.4f, 1.0f);
    public TMP_FontAsset customFont;

    // Event callbacks
    public event Action OnBackClicked;
    public event Action OnConnectClicked;
    public event Action OnQRClicked;

    private VRMainMenu _mainMenu;
    private VRMenuFrame _menuFrame;
    private float _containerWidth;
    private float _containerHeight;

    // UI References
    private GameObject _hostInput;
    private GameObject _portInput;
    private GameObject _monitorsDropdown;
    private GameObject _resolutionDropdown;
    private GameObject _bitrateDropdown;
    private GameObject _fpsDropdown;
    private GameObject _bodyContainer;
    private QRScannerView _qrScannerView;

    // Font sizes cho InputField và Dropdown
    private const int LABEL_FONT_SIZE = 40;
    private const int INPUT_FONT_SIZE = 42;
    private const int DROPDOWN_LABEL_FONT_SIZE = 40;
    private const int DROPDOWN_VALUE_FONT_SIZE = 42;

    // ==================== PUBLIC ACCESSORS ====================

    public string Host => VRInputFieldFactory.GetValue(_hostInput);
    public string Port => VRInputFieldFactory.GetValue(_portInput);
    public int MonitorIndex => VRDropdownFactory.GetSelectedIndex(_monitorsDropdown);
    public string Resolution => VRDropdownFactory.GetSelectedValue(_resolutionDropdown);
    public string Bitrate => VRDropdownFactory.GetSelectedValue(_bitrateDropdown);
    public string FPS => VRDropdownFactory.GetSelectedValue(_fpsDropdown);

    public void BuildUI(Transform parent, VRMainMenu mainMenu, float containerWidth, float containerHeight)
    {
        _mainMenu = mainMenu;
        _menuFrame = GetComponentInParent<VRMenuFrame>();
        _containerWidth = containerWidth;
        _containerHeight = containerHeight;
        OnBackClicked += ReturnToMainMenu;
        OnQRClicked += ShowQRScanner;
        BuildUI(parent, containerWidth, containerHeight);
    }

    public void BuildUI(Transform parent)
    {
        BuildUI(parent, 1920f, 1000f);
    }

    public void BuildUI(Transform parent, float containerWidth, float containerHeight)
    {
        float contentW = containerWidth * 0.875f;

        // Tính chiều cao tự động từ font size
        float inputH = VRInputFieldFactory.CalculateHeight(INPUT_FONT_SIZE, true);
        float dropdownH = VRDropdownFactory.CalculateHeight(DROPDOWN_VALUE_FONT_SIZE);

        // Layout heights
        float headerH = containerHeight * 0.1f;
        float connectButtonH = containerHeight * 0.125f;
        float gap = containerHeight * 0.04f;
        float gridH = dropdownH * 2 + gap;

        float y = containerHeight;

        // Header
        y -= headerH;
        CreateHeader(parent, 0, y, containerWidth, headerH);

        // Separator 1 position (between Header and InputRow)
        float sep1Y = y - gap * 1.25f;

        float bodyContainerHeight = containerHeight - 2f * headerH;
        y -= 2.25f * gap + bodyContainerHeight;
        _bodyContainer = CreateContainer(parent, "BodyContainer", 0, y, containerWidth, bodyContainerHeight);

        // Input Row (Host / Port) - chiều cao tự động
        float bodyY = bodyContainerHeight;
        bodyY -= inputH;
        CreateInputRow(_bodyContainer.transform, 0, bodyY, contentW, inputH, gap * 1.5f);

        // Separator 2 position (between InputRow and Grid)
        float sep2Y = y + bodyY - gap * 1.5f;

        // Grid 2x2 (Monitors, Resolution, Bitrate, FPS)
        bodyY -= gap * 2.5f + gridH;
        CreateGrid(_bodyContainer.transform, 0, bodyY, contentW, 0.04f * contentW, dropdownH, gap * 1.5f, gap);

        // Button Connect - lồng vào trong BodyContainer
        CreateConnectButton(_bodyContainer.transform, contentW * 0.625f, connectButtonH);

        // Configure horizontal separators via shader (using VRMenuFrame)
        // Separator 2 length = contentW / containerWidth (content area ratio)
        float separatorLength = contentW / containerWidth;
        ConfigureHorizontalSeparators(containerHeight, sep1Y, sep2Y, separatorLength);
    }

    void ConfigureHorizontalSeparators(float containerHeight, float sep1Y, float sep2Y, float sep2Length = 1f)
    {
        if (_menuFrame == null) return;

        // Convert content-area Y positions to frame UV coordinates
        // UV.y = 0 is bottom, UV.y = 1 is top
        // Need to account for content margins

        float frameLogicalHeight = _menuFrame.logicalWidth * (_menuFrame.panelHeight / _menuFrame.panelWidth);
        float marginBottom = _menuFrame.contentMarginBottom;
        float marginTop = _menuFrame.contentMarginTop;
        float contentHeight = frameLogicalHeight - marginTop - marginBottom;

        // Calculate UV positions (frame coordinates)
        // Content area starts at marginBottom from frame bottom
        float sep1FrameY = marginBottom + sep1Y;
        float sep2FrameY = marginBottom + sep2Y;

        float sep1UV = sep1FrameY / frameLogicalHeight;
        float sep2UV = sep2FrameY / frameLogicalHeight;

        // Set horizontal separators in shader with different lengths
        // Separator 1: full width (1f)
        // Separator 2: custom length (sep2Length)
        Vector4 positions = new Vector4(sep1UV, sep2UV, 0, 0);
        Vector4 lengths = new Vector4(1f, sep2Length, 1f, 1f);
        _menuFrame.SetHorizontalSeparators(2, positions, 0.003f, 0.012f, 0.9f, lengths);
    }

    void DisableHorizontalSeparators()
    {
        if (_menuFrame == null) return;
        _menuFrame.SetHorizontalSeparators(0, Vector4.zero);
    }

    void ReturnToMainMenu()
    {
        if (_menuFrame == null) return;

        // Disable horizontal separators when leaving this menu
        DisableHorizontalSeparators();

        Transform contentContainer = _menuFrame.ContentContainer;
        if (contentContainer == null) return;

        GameObject mainMenuObj = new GameObject("VRMainMenu");
        mainMenuObj.transform.SetParent(contentContainer, false);

        RectTransform mainMenuRT = mainMenuObj.AddComponent<RectTransform>();
        mainMenuRT.anchorMin = Vector2.zero;
        mainMenuRT.anchorMax = Vector2.one;
        mainMenuRT.offsetMin = Vector2.zero;
        mainMenuRT.offsetMax = Vector2.zero;

        VRMainMenu mainMenu = mainMenuObj.AddComponent<VRMainMenu>();
        mainMenu.customFont = customFont;

        Destroy(gameObject);
    }

    void CreateHeader(Transform parent, float x, float y, float w, float h)
    {
        var header = CreateContainer(parent, "Header", x, y, w, h);

        // Back button
        float backW = 280f;
        CreateButton(header.transform, 0, 0, backW, h, "Back", LoadIcon("back"), themeColor,
            () => OnBackClicked?.Invoke());

        // Title - căn giữa tuyệt đối trong header
        CreateLabel(header.transform, 0, 0, w, h, "Remote Desktop", 48, Color.white, true, TextAlignmentOptions.Center);

        // QR button
        float qrSize = 100f;
        CreateButton(header.transform, w - qrSize, (h - qrSize) / 2f, qrSize, qrSize, "", LoadIcon("qr"), accentColor,
            () => OnQRClicked?.Invoke());
    }

    void CreateInputRow(Transform parent, float x, float y, float w, float h, float gapX)
    {
        var row = CreateContainer(parent, "InputRow", x, y, w, h);

        // Concept: Host ~60%, gap ~5%, Port ~35%
        float hostW = (w - gapX) * 0.5f;
        float portW = (w - gapX) * 0.5f;

        // Host Input - chiều cao tự động từ font size
        _hostInput = VRInputFieldFactory.CreateLabeledInputField(
            row.transform, hostW,
            "Host", "192.168.1.10", accentColor,
            onEndEdit: (value) => Debug.Log("Host: " + value),
            labelFontSize: LABEL_FONT_SIZE, inputFontSize: INPUT_FONT_SIZE, font: customFont);
        PositionElement(_hostInput, 0, 0);

        // Port Input - chiều cao tự động từ font size
        _portInput = VRInputFieldFactory.CreateLabeledInputField(
            row.transform, portW,
            "Port", "9000", themeColor,
            onEndEdit: (value) => Debug.Log("Port: " + value),
            labelFontSize: LABEL_FONT_SIZE, inputFontSize: INPUT_FONT_SIZE, font: customFont,
            contentType: TMPro.TMP_InputField.ContentType.IntegerNumber);
        PositionElement(_portInput, hostW + gapX, 0);
    }

    void CreateGrid(Transform parent, float x, float y, float w, float h, float dropdownH, float gapX, float gapY)
    {
        var grid = CreateContainer(parent, "Grid", x, y, w, h);

        // 2x2 grid with equal cells
        float cellW = (w - gapX) / 2f;

        // Dropdown options
        var monitorOptions = new List<string> {"1 Monitor", "2 Monitors", "3 Monitors"};
        var monitorIcons = new List<Sprite> { LoadIcon("1_monitor"), LoadIcon("2_monitors"), LoadIcon("3_monitors")};
        var monitorIconMultipliers = new List<float> { 1f, 1.5f, 2.25f }; // 1x, 2x, 3x size for each option
        var resolutionOptions = new List<string> {"1920 x 1080", "1600 x 900", "1366 x 768", "1280 x 720"};
        var bitrateOptions = new List<string> {"5 Mbps", "10 Mbps", "20 Mbps", "30 Mbps", "50 Mbps"};
        var fpsOptions = new List<string> {"30 FPS", "45 FPS", "60 FPS"};

        // Row 1 (top) - Monitors và Resolution
        float row1Y = dropdownH + gapY;

        _monitorsDropdown = VRDropdownFactory.CreateIconDropdown( // CreateIconDropdownWithOptionIcons
            grid.transform, cellW,
            "Monitors", LoadIcon("monitor"), themeColor,
            monitorOptions, 0,
            onValueChanged: (index, value) => Debug.Log("Monitor: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_monitorsDropdown, 0, row1Y);

        _resolutionDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "Resolution", LoadIcon("resolution"), accentColor,
            resolutionOptions, 0,
            onValueChanged: (index, value) => Debug.Log("Resolution: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_resolutionDropdown, cellW + gapX, row1Y);

        // Row 2 (bottom) - Bitrate và FPS
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

    // ==================== UI COMPONENTS ====================

    GameObject CreateContainer(Transform parent, string name, float x, float y, float w, float h)
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

    void PositionElement(GameObject element, float x, float y)
    {
        RectTransform rt = element.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
    }

    void CreateButton(Transform parent, float x, float y, float w, float h,
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

    void CreateConnectButton(Transform parent, float w, float h)
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
            // Sử dụng shader đặc biệt cho Connect Button
            // Gradient 3 màu: Cyan -> Deep Sea Blue -> Purple
            useConnectButtonShader = true,
            connectColorA = themeColor,  // Cyan
            connectColorB = new Color(0.1f, 0.5f, 0.85f), // Deep Sea Blue
            connectColorC = accentColor  // Purple
        };

        var btn = VRButtonFactory.CreateButton(parent, config, () => OnConnectClicked?.Invoke());

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = Vector2.zero;
    }

    // ==================== PRIMITIVES ====================

    GameObject CreateLabel(Transform parent, float x, float y, float w, float h,
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

    // ==================== SPRITES ====================

    /// <summary>
    /// Load icon from Resources folder by name (delegates to VRTaskbar.LoadIcon)
    /// </summary>
    Sprite LoadIcon(string name) => VRTaskbar.LoadIcon(name);

    // ==================== QR SCANNER ====================

    /// <summary>
    /// Hiển thị QR Scanner view, ẩn BodyContainer
    /// </summary>
    void ShowQRScanner()
    {
        if (_bodyContainer == null) return;

        // Ẩn BodyContainer (giữ data)
        _bodyContainer.SetActive(false);

        // Disable horizontal separators
        DisableHorizontalSeparators();

        // Tạo QRScannerView
        GameObject scannerObj = new GameObject("QRScannerView");
        scannerObj.transform.SetParent(transform, false);

        RectTransform scannerRT = scannerObj.AddComponent<RectTransform>();
        scannerRT.anchorMin = Vector2.zero;
        scannerRT.anchorMax = Vector2.one;
        scannerRT.offsetMin = Vector2.zero;
        scannerRT.offsetMax = Vector2.zero;

        _qrScannerView = scannerObj.AddComponent<QRScannerView>();
        _qrScannerView.themeColor = themeColor;
        _qrScannerView.accentColor = accentColor;
        _qrScannerView.customFont = customFont;

        // Subscribe events
        _qrScannerView.OnQRScanned += OnQRCodeScanned;
        _qrScannerView.OnCancelClicked += OnQRScannerCancelled;

        // Build UI
        RectTransform bodyRT = _bodyContainer.GetComponent<RectTransform>();
        float bodyWidth = bodyRT.sizeDelta.x;
        float bodyHeight = bodyRT.sizeDelta.y;
        _qrScannerView.BuildUI(scannerObj.transform, bodyWidth, bodyHeight);
    }

    /// <summary>
    /// Xử lý khi quét QR thành công
    /// </summary>
    void OnQRCodeScanned(QRScannerConfig config)
    {
        Debug.Log($"QR Scanned: {config}");

        // Destroy QRScannerView
        if (_qrScannerView != null)
        {
            Destroy(_qrScannerView.gameObject);
            _qrScannerView = null;
        }

        // Show lại BodyContainer
        if (_bodyContainer != null)
        {
            _bodyContainer.SetActive(true);
        }

        // Re-enable horizontal separators
        float contentW = _containerWidth * 0.875f;
        float separatorLength = contentW / _containerWidth;
        float headerH = _containerHeight * 0.1f;
        float gap = _containerHeight * 0.04f;
        float inputH = VRInputFieldFactory.CalculateHeight(INPUT_FONT_SIZE, true);
        float dropdownH = VRDropdownFactory.CalculateHeight(DROPDOWN_VALUE_FONT_SIZE);
        float gridH = dropdownH * 2 + gap;
        float bodyContainerHeight = _containerHeight - 2f * headerH;

        float y = _containerHeight - headerH;
        float sep1Y = y - gap * 1.25f;
        y -= 2.25f * gap + bodyContainerHeight;
        float bodyY = bodyContainerHeight - inputH;
        float sep2Y = y + bodyY - gap * 1.5f;

        ConfigureHorizontalSeparators(_containerHeight, sep1Y, sep2Y, separatorLength);

        // Fill data vào các input fields
        FillConfigData(config);
    }

    /// <summary>
    /// Xử lý khi người dùng hủy QR Scanner
    /// </summary>
    void OnQRScannerCancelled()
    {
        // Destroy QRScannerView
        if (_qrScannerView != null)
        {
            Destroy(_qrScannerView.gameObject);
            _qrScannerView = null;
        }

        // Show lại BodyContainer
        if (_bodyContainer != null)
        {
            _bodyContainer.SetActive(true);
        }

        // Re-enable horizontal separators
        float contentW = _containerWidth * 0.875f;
        float separatorLength = contentW / _containerWidth;
        float headerH = _containerHeight * 0.1f;
        float gap = _containerHeight * 0.04f;
        float inputH = VRInputFieldFactory.CalculateHeight(INPUT_FONT_SIZE, true);
        float dropdownH = VRDropdownFactory.CalculateHeight(DROPDOWN_VALUE_FONT_SIZE);
        float gridH = dropdownH * 2 + gap;
        float bodyContainerHeight = _containerHeight - 2f * headerH;

        float y = _containerHeight - headerH;
        float sep1Y = y - gap * 1.25f;
        y -= 2.25f * gap + bodyContainerHeight;
        float bodyY = bodyContainerHeight - inputH;
        float sep2Y = y + bodyY - gap * 1.5f;

        ConfigureHorizontalSeparators(_containerHeight, sep1Y, sep2Y, separatorLength);
    }

    /// <summary>
    /// Điền dữ liệu từ QR config vào các input fields và dropdowns
    /// </summary>
    void FillConfigData(QRScannerConfig config)
    {
        if (config == null) return;

        // Host & Port
        if (!string.IsNullOrEmpty(config.host))
            VRInputFieldFactory.SetValue(_hostInput, config.host);

        if (!string.IsNullOrEmpty(config.port))
            VRInputFieldFactory.SetValue(_portInput, config.port);

        // Resolution dropdown - tìm index phù hợp
        if (!string.IsNullOrEmpty(config.resolution))
        {
            int resIndex = FindResolutionIndex(config.resolution);
            if (resIndex >= 0)
                VRDropdownFactory.SetSelectedIndex(_resolutionDropdown, resIndex);
        }

        // Bitrate dropdown
        if (!string.IsNullOrEmpty(config.bitrate))
        {
            int bitrateIndex = FindBitrateIndex(config.bitrate);
            if (bitrateIndex >= 0)
                VRDropdownFactory.SetSelectedIndex(_bitrateDropdown, bitrateIndex);
        }

        // FPS dropdown
        if (!string.IsNullOrEmpty(config.fps))
        {
            int fpsIndex = FindFPSIndex(config.fps);
            if (fpsIndex >= 0)
                VRDropdownFactory.SetSelectedIndex(_fpsDropdown, fpsIndex);
        }

        // Monitors dropdown (index = monitors - 1)
        if (config.monitors >= 1 && config.monitors <= 3)
        {
            VRDropdownFactory.SetSelectedIndex(_monitorsDropdown, config.monitors - 1);
        }
    }

    /// <summary>
    /// Tìm index của resolution trong dropdown
    /// </summary>
    int FindResolutionIndex(string resolution)
    {
        // Chuẩn hóa format: "1920x1080" -> "1920 x 1080"
        string normalized = resolution.Replace("x", " x ").Replace("  ", " ");

        var options = new List<string> { "1920 x 1080", "1600 x 900", "1366 x 768", "1280 x 720" };
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                options[i].Replace(" ", "").Equals(resolution.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Tìm index của bitrate trong dropdown
    /// </summary>
    int FindBitrateIndex(string bitrate)
    {
        var options = new List<string> { "5 Mbps", "10 Mbps", "20 Mbps", "30 Mbps", "50 Mbps" };
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].Equals(bitrate, StringComparison.OrdinalIgnoreCase) ||
                options[i].Replace(" ", "").Equals(bitrate.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Tìm index của FPS trong dropdown
    /// </summary>
    int FindFPSIndex(string fps)
    {
        var options = new List<string> { "30 FPS", "45 FPS", "60 FPS" };
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].Equals(fps, StringComparison.OrdinalIgnoreCase) ||
                options[i].Replace(" ", "").Equals(fps.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
}
