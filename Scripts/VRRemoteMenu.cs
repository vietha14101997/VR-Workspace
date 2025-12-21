using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System;

public class VRRemoteMenu : MonoBehaviour
{
    public Color themeColor = new Color(0.0f, 0.9f, 1.0f);
    public Color accentColor = new Color(0.8f, 0.4f, 1.0f);
    public TMP_FontAsset customFont;

    private Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>();

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

    // Font sizes cho InputField và Dropdown
    private const int LABEL_FONT_SIZE = 26;
    private const int INPUT_FONT_SIZE = 38;
    private const int DROPDOWN_LABEL_FONT_SIZE = 30;   // Tăng từ 26
    private const int DROPDOWN_VALUE_FONT_SIZE = 40;   // Tăng từ 38

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
        BuildUI(parent, containerWidth, containerHeight);
    }

    public void BuildUI(Transform parent)
    {
        BuildUI(parent, 1920f, 1000f);
    }

    public void BuildUI(Transform parent, float containerWidth, float containerHeight)
    {
        float W = containerWidth;
        float H = containerHeight;
        float contentW = W;

        // Tính chiều cao tự động từ font size
        float inputH = VRInputFieldFactory.CalculateHeight(INPUT_FONT_SIZE, true);
        float dropdownH = VRDropdownFactory.CalculateHeight(DROPDOWN_VALUE_FONT_SIZE);

        // Layout heights
        float headerH = 100f;
        float gridH = dropdownH * 2 + 25f; // 2 rows dropdown + gap
        float footerH = 100f;
        float gap = 25f;

        float y = H;

        // Header
        y -= headerH;
        CreateHeader(parent, 0, y, contentW, headerH);

        // Input Row (Host / Port) - chiều cao tự động
        y -= gap + inputH;
        CreateInputRow(parent, 0, y, contentW, inputH);

        // Grid 2x2 (Monitors, Resolution, Bitrate, FPS)
        y -= gap + gridH;
        CreateGrid(parent, 0, y, contentW, gridH, dropdownH);

        // Footer (Connect button)
        y -= gap + footerH;
        CreateFooter(parent, 0, y, contentW, footerH);
    }

    void ReturnToMainMenu()
    {
        if (_menuFrame == null) return;

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

        // Title
        float titleX = backW + 35f;
        CreateLabel(header.transform, titleX, 0, w - titleX - 110f, h, "VR Remote Menu", 48, Color.white, true, TextAlignmentOptions.Left);

        // QR button
        float qrSize = 100f;
        CreateButton(header.transform, w - qrSize, (h - qrSize) / 2f, qrSize, qrSize, "", LoadIcon("qr"), accentColor,
            () => OnQRClicked?.Invoke());
    }

    void CreateInputRow(Transform parent, float x, float y, float w, float h)
    {
        var row = CreateContainer(parent, "InputRow", x, y, w, h);

        // Concept: Host ~60%, gap ~5%, Port ~35%
        float gapX = 40f;
        float hostW = (w - gapX) * 0.62f;
        float portW = (w - gapX) * 0.38f;

        // Host Input - chiều cao tự động từ font size
        _hostInput = VRInputFieldFactory.CreateLabeledInputField(
            row.transform, hostW,
            "Host", "192.168.1.10", themeColor,
            onEndEdit: (value) => Debug.Log("Host: " + value),
            labelFontSize: LABEL_FONT_SIZE, inputFontSize: INPUT_FONT_SIZE, font: customFont);
        PositionElement(_hostInput, 0, 0);

        // Port Input - chiều cao tự động từ font size
        _portInput = VRInputFieldFactory.CreateLabeledInputField(
            row.transform, portW,
            "Port", "9000", accentColor,
            onEndEdit: (value) => Debug.Log("Port: " + value),
            labelFontSize: LABEL_FONT_SIZE, inputFontSize: INPUT_FONT_SIZE, font: customFont,
            contentType: TMPro.TMP_InputField.ContentType.IntegerNumber);
        PositionElement(_portInput, hostW + gapX, 0);
    }

    void CreateGrid(Transform parent, float x, float y, float w, float h, float dropdownH)
    {
        var grid = CreateContainer(parent, "Grid", x, y, w, h);

        // 2x2 grid with equal cells
        float gapX = 40f;
        float gapY = 25f;
        float cellW = (w - gapX) / 2f;

        // Dropdown options
        var monitorOptions = new List<string> { "Monitor 1", "Monitor 2", "All" };
        var resolutionOptions = new List<string> { "1920 x 1080", "2560 x 1440", "3840 x 2160", "1280 x 720" };
        var bitrateOptions = new List<string> { "5 Mbps", "10 Mbps", "20 Mbps", "30 Mbps", "50 Mbps" };
        var fpsOptions = new List<string> { "30 FPS", "60 FPS", "90 FPS", "120 FPS" };

        // Row 1 (top) - Monitors và Resolution
        float row1Y = dropdownH + gapY;

        _monitorsDropdown = VRDropdownFactory.CreateIconDropdown(
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
            "Bitrate", LoadIcon("bitrate"), themeColor,
            bitrateOptions, 1,
            onValueChanged: (index, value) => Debug.Log("Bitrate: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_bitrateDropdown, 0, 0);

        _fpsDropdown = VRDropdownFactory.CreateIconDropdown(
            grid.transform, cellW,
            "FPS", LoadIcon("fps"), accentColor,
            fpsOptions, 1,
            onValueChanged: (index, value) => Debug.Log("FPS: " + value),
            labelFontSize: DROPDOWN_LABEL_FONT_SIZE, valueFontSize: DROPDOWN_VALUE_FONT_SIZE, font: customFont);
        PositionElement(_fpsDropdown, cellW + gapX, 0);
    }

    void CreateFooter(Transform parent, float x, float y, float w, float h)
    {
        var footer = CreateContainer(parent, "Footer", x, y, w, h);

        float btnW = 650f;
        CreateConnectButton(footer.transform, (w - btnW) / 2f, 0, btnW, h);
    }

    // ==================== UI COMPONENTS ====================

    GameObject CreateContainer(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
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
            backgroundAlpha = 0.28f,
            borderWidth = 0.04f,
            popAmount = 0.0125f
        };

        var btn = VRButtonFactory.CreateButton(parent, config, onClick);

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
    }

    void CreateConnectButton(Transform parent, float x, float y, float w, float h)
    {
        Color gradCol = Color.Lerp(themeColor, accentColor, 0.45f);

        var config = new VRButtonFactory.ButtonConfig
        {
            label = "CONNECT",
            themeColor = gradCol,
            width = w,
            height = h,
            fontSize = 58,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.50f,
            glowIntensity = 5f,
            enablePulse = true,
            pulseSpeed = 1.8f,
            popAmount = 0.05f
        };

        var btn = VRButtonFactory.CreateButton(parent, config, () => OnConnectClicked?.Invoke());

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
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

    Sprite LoadIcon(string name)
    {
        if (_iconCache.ContainsKey(name)) return _iconCache[name];

        var sprite = Resources.Load<Sprite>($"RemoteMenu/icon_{name}");
        if (sprite != null)
        {
            _iconCache[name] = sprite;
            return sprite;
        }

        return null;
    }
}
