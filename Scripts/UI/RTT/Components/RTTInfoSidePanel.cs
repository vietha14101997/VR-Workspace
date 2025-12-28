using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRWorkspace.Streaming;
using System.Collections.Generic;

/// <summary>
/// Content component for displaying hardware or network info.
/// Only creates child UI elements (titles, rows, separators).
/// Must be placed inside a container (like RTTMenuFrame.ContentContainer).
/// Similar pattern to RTTMainMenu - content-only, no frame creation.
/// </summary>
public class RTTInfoSidePanel : MonoBehaviour
{
    public enum PanelType
    {
        HardwareInfo,
        NetworkInfo
    }

    #region Configuration
    [Header("Panel Type")]
    [SerializeField] private PanelType panelType = PanelType.HardwareInfo;

    [Header("Visual Settings")]
    [SerializeField] private Color themeColor = new Color(0f, 0.9f, 1f);
    [SerializeField] private TMP_FontAsset customFont;

    [Header("Typography")]
    [SerializeField] private int titleFontSize = 48;
    [SerializeField] private int labelFontSize = 36;
    [SerializeField] private int valueFontSize = 40;
    [SerializeField] private float lineSpacing = 80f;
    #endregion

    #region Private Fields
    private RectTransform _container;
    private Dictionary<string, TextMeshProUGUI> _valueTexts = new Dictionary<string, TextMeshProUGUI>();
    private bool _isBuilt = false;
    #endregion

    #region Properties
    public PanelType Type => panelType;
    public TMP_FontAsset CustomFont { get => customFont; set => customFont = value; }
    public Color ThemeColor { get => themeColor; set => themeColor = value; }
    #endregion

    #region Events
    /// <summary>
    /// Called when content needs re-rendering (for RTT frames).
    /// </summary>
    public event System.Action OnContentChanged;
    #endregion

    #region Public API
    /// <summary>
    /// Build the info panel UI inside the given container.
    /// </summary>
    public void BuildUI(RectTransform container, PanelType type, Color theme, TMP_FontAsset font = null)
    {
        panelType = type;
        themeColor = theme;
        if (font != null) customFont = font;

        BuildUI(container);
    }

    /// <summary>
    /// Build the info panel UI inside the given container.
    /// </summary>
    public void BuildUI(RectTransform container)
    {
        _container = container;

        // Setup RectTransform
        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        rt.SetParent(container, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        // Add vertical layout
        var layout = gameObject.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = gameObject.AddComponent<VerticalLayoutGroup>();

        layout.spacing = lineSpacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(20, 20, 40, 40);

        _isBuilt = true;
        Debug.Log($"[RTTInfoSidePanel] Built for type: {panelType}");
    }

    /// <summary>
    /// Show loading state before data arrives.
    /// </summary>
    public void ShowLoadingState()
    {
        if (!_isBuilt) return;

        ClearContent();

        if (panelType == PanelType.HardwareInfo)
        {
            AddTitle("SERVER INFO");
            AddInfoRow("Device", "Loading...");
            AddInfoRow("CPU", "...");
            AddInfoRow("VGA", "...");
            AddInfoRow("RAM", "...");
            AddInfoRow("OS", "...");
        }
        else
        {
            AddTitle("NETWORK INFO");
            AddInfoRow("Ping", "...");
            AddInfoRow("Jitter", "...");
            AddInfoRow("Bandwidth", "...");
            AddInfoRow("Type", "...");
            AddInfoRow("Quality", "...");
        }

        OnContentChanged?.Invoke();
    }

    /// <summary>
    /// Set hardware info data.
    /// </summary>
    public void SetHardwareInfo(ServerHardwareInfo info)
    {
        if (info == null || panelType != PanelType.HardwareInfo) return;

        ClearContent();

        AddTitle("SERVER INFO");
        AddInfoRow("Device", info.deviceName ?? "Unknown");
        AddInfoRow("CPU", info.processor ?? "Unknown");
        AddInfoRow("VGA", $"{info.gpu ?? "Unknown"} - {info.gpuVramGB}GB");
        AddInfoRow("RAM", $"{info.ramGB} GB");
        AddInfoRow("OS", info.os ?? "Unknown");

        OnContentChanged?.Invoke();
        Debug.Log($"[RTTInfoSidePanel] SetHardwareInfo: {info.deviceName}");
    }

    /// <summary>
    /// Set network info data.
    /// </summary>
    public void SetNetworkInfo(NetworkTestResult info)
    {
        if (info == null || panelType != PanelType.NetworkInfo) return;

        ClearContent();

        AddTitle("NETWORK INFO");
        AddInfoRow("Ping", $"{info.pingMs:F1} ms");
        AddInfoRow("Jitter", $"{info.jitterMs:F1} ms");
        AddInfoRow("Bandwidth", $"{info.bandwidthMbps:F0} Mbps");
        AddInfoRow("Type", info.connectionType ?? "Unknown");

        string quality = GetNetworkQuality(info);
        AddInfoRow("Quality", quality, GetQualityColor(quality));

        OnContentChanged?.Invoke();
        Debug.Log($"[RTTInfoSidePanel] SetNetworkInfo: {info.pingMs:F1}ms");
    }

    /// <summary>
    /// Show speed test loading state.
    /// </summary>
    public void ShowSpeedTestLoadingState()
    {
        if (panelType != PanelType.NetworkInfo) return;

        ClearContent();
        _valueTexts.Clear();

        AddTitle("NETWORK INFO");
        AddInfoRow("Ping", "Measuring...");
        AddInfoRow("Jitter", "Waiting...");
        AddInfoRow("Bandwidth", "Waiting...");
        AddInfoRow("Quality", "Testing...");

        OnContentChanged?.Invoke();
    }

    /// <summary>
    /// Update ping values during speed test.
    /// </summary>
    public void UpdatePingValues(double pingMs, double jitterMs)
    {
        if (panelType != PanelType.NetworkInfo) return;

        if (_valueTexts.TryGetValue("Ping", out var pingTxt))
            pingTxt.text = $"{pingMs:F1} ms";

        if (_valueTexts.TryGetValue("Jitter", out var jitterTxt))
            jitterTxt.text = $"{jitterMs:F1} ms";

        OnContentChanged?.Invoke();
    }

    /// <summary>
    /// Update bandwidth progress during speed test.
    /// </summary>
    public void UpdateSpeedTestProgress(string direction, double currentMbps, int progress)
    {
        if (panelType != PanelType.NetworkInfo) return;
        if (direction != "bandwidth") return;

        string value = progress < 100
            ? $"{currentMbps:F1} Mbps ({progress}%)"
            : $"{currentMbps:F1} Mbps";

        if (_valueTexts.TryGetValue("Bandwidth", out var txt))
        {
            txt.text = value;
            OnContentChanged?.Invoke();
        }
    }
    #endregion

    #region Content Building
    private void ClearContent()
    {
        _valueTexts.Clear();

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(transform.GetChild(i).gameObject);
            else
                DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }

    private void AddTitle(string title)
    {
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(transform, false);

        RectTransform rt = titleObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, titleFontSize + 20);

        TextMeshProUGUI txt = titleObj.AddComponent<TextMeshProUGUI>();
        txt.text = title;
        txt.fontSize = titleFontSize;
        txt.color = themeColor;
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = FontStyles.Bold;
        txt.raycastTarget = false;
        if (customFont != null) txt.font = customFont;

        AddSeparator();
    }

    private void AddSeparator()
    {
        GameObject sepObj = new GameObject("Separator");
        sepObj.transform.SetParent(transform, false);

        RectTransform rt = sepObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 4);

        Image img = sepObj.AddComponent<Image>();
        img.color = new Color(themeColor.r, themeColor.g, themeColor.b, 0.5f);
        img.raycastTarget = false;

        var layoutElem = sepObj.AddComponent<LayoutElement>();
        layoutElem.preferredHeight = 4;
        layoutElem.flexibleWidth = 1;
    }

    private void AddInfoRow(string label, string value, Color? valueColor = null)
    {
        GameObject rowObj = new GameObject($"Row_{label}");
        rowObj.transform.SetParent(transform, false);

        RectTransform rt = rowObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, valueFontSize + 16);

        HorizontalLayoutGroup hlg = rowObj.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowObj.transform, false);

        RectTransform labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.sizeDelta = new Vector2(200, 0);

        TextMeshProUGUI labelTxt = labelObj.AddComponent<TextMeshProUGUI>();
        labelTxt.text = label;
        labelTxt.fontSize = labelFontSize;
        labelTxt.color = Color.white;
        labelTxt.alignment = TextAlignmentOptions.Left;
        labelTxt.raycastTarget = false;
        if (customFont != null) labelTxt.font = customFont;

        var labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 200;

        // Value
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(rowObj.transform, false);

        RectTransform valueRT = valueObj.AddComponent<RectTransform>();
        valueRT.sizeDelta = new Vector2(350, 0);

        TextMeshProUGUI valueTxt = valueObj.AddComponent<TextMeshProUGUI>();
        valueTxt.text = value;
        valueTxt.fontSize = valueFontSize;
        valueTxt.color = valueColor ?? Color.white;
        valueTxt.alignment = TextAlignmentOptions.Left;
        valueTxt.fontStyle = FontStyles.Bold;
        valueTxt.raycastTarget = false;
        valueTxt.enableWordWrapping = true;
        valueTxt.overflowMode = TextOverflowModes.Overflow;
        if (customFont != null) valueTxt.font = customFont;

        var valueLayout = valueObj.AddComponent<LayoutElement>();
        valueLayout.flexibleWidth = 1;

        _valueTexts[label] = valueTxt;
    }
    #endregion

    #region Helpers
    private string GetNetworkQuality(NetworkTestResult info)
    {
        if (info.pingMs < 20 && info.bandwidthMbps > 100) return "Excellent";
        if (info.pingMs < 50 && info.bandwidthMbps > 50) return "Good";
        if (info.pingMs < 100 && info.bandwidthMbps > 20) return "Fair";
        return "Poor";
    }

    private Color GetQualityColor(string quality)
    {
        return quality switch
        {
            "Excellent" => new Color(0.2f, 1f, 0.4f),
            "Good" => new Color(0.5f, 1f, 0.3f),
            "Fair" => new Color(1f, 0.8f, 0.2f),
            _ => new Color(1f, 0.4f, 0.3f)
        };
    }
    #endregion
}
