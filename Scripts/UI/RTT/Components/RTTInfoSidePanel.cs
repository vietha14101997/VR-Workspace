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

    // UI Pooling - reuse objects instead of destroy/recreate to reduce GC
    private List<GameObject> _rowPool = new List<GameObject>();
    private int _activeRowCount = 0;
    private GameObject _titleObj;
    private TextMeshProUGUI _titleText;
    private GameObject _separatorObj;
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
        Debug.Log($"[RTTInfoSidePanel] BuildUI started for {panelType}, container={container?.name ?? "NULL"}");

        if (container == null)
        {
            Debug.LogError($"[RTTInfoSidePanel] BuildUI failed: container is NULL for {panelType}!");
            return;
        }

        _container = container;

        // Setup RectTransform
        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        // Check if already parented correctly (parent might be set before BuildUI)
        if (transform.parent != container)
        {
            Debug.Log($"[RTTInfoSidePanel] Setting parent from {transform.parent?.name ?? "NULL"} to {container.name}");
            rt.SetParent(container, false);
        }
        else
        {
            Debug.Log($"[RTTInfoSidePanel] Already parented to {container.name}");
        }

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
        Debug.Log($"[RTTInfoSidePanel] Built for type: {panelType}, _isBuilt={_isBuilt}, parent={transform.parent?.name}");
    }

    /// <summary>
    /// Show loading state before data arrives.
    /// </summary>
    public void ShowLoadingState()
    {
        Debug.Log($"[RTTInfoSidePanel] ShowLoadingState called for {panelType}, _isBuilt={_isBuilt}");

        if (!_isBuilt)
        {
            Debug.LogWarning($"[RTTInfoSidePanel] ShowLoadingState skipped - not built yet for {panelType}");
            return;
        }

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
        Debug.Log($"[RTTInfoSidePanel] ShowSpeedTestLoadingState called for {panelType}, _isBuilt={_isBuilt}");

        if (panelType != PanelType.NetworkInfo)
        {
            Debug.LogWarning($"[RTTInfoSidePanel] ShowSpeedTestLoadingState called on wrong panel type: {panelType}");
            return;
        }

        if (!_isBuilt)
        {
            Debug.LogWarning($"[RTTInfoSidePanel] ShowSpeedTestLoadingState skipped - not built yet");
            return;
        }

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
    /// <summary>
    /// Reset content for reuse - disables pooled objects instead of destroying.
    /// This reduces GC allocation and improves performance.
    /// </summary>
    private void ClearContent()
    {
        _valueTexts.Clear();
        _activeRowCount = 0;

        // Disable pooled rows instead of destroying
        foreach (var row in _rowPool)
        {
            if (row != null) row.SetActive(false);
        }

        // Hide title and separator (will be reactivated when needed)
        if (_titleObj != null) _titleObj.SetActive(false);
        if (_separatorObj != null) _separatorObj.SetActive(false);
    }

    private void AddTitle(string title)
    {
        // Reuse existing title object if available
        if (_titleObj != null)
        {
            _titleObj.SetActive(true);
            _titleObj.transform.SetAsLastSibling(); // Ensure correct order
            _titleText.text = title;
            _titleText.color = themeColor;
        }
        else
        {
            // Create new title object (first time only)
            _titleObj = new GameObject("Title");
            _titleObj.transform.SetParent(transform, false);

            RectTransform rt = _titleObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, titleFontSize + 20);

            _titleText = _titleObj.AddComponent<TextMeshProUGUI>();
            _titleText.text = title;
            _titleText.fontSize = titleFontSize;
            _titleText.color = themeColor;
            _titleText.alignment = TextAlignmentOptions.Center;
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.raycastTarget = false;
            if (customFont != null) _titleText.font = customFont;
        }

        AddSeparator();
    }

    private void AddSeparator()
    {
        // Reuse existing separator if available
        if (_separatorObj != null)
        {
            _separatorObj.SetActive(true);
            _separatorObj.transform.SetAsLastSibling(); // Ensure correct order
            var img = _separatorObj.GetComponent<Image>();
            if (img != null) img.color = new Color(themeColor.r, themeColor.g, themeColor.b, 0.5f);
        }
        else
        {
            // Create new separator (first time only)
            _separatorObj = new GameObject("Separator");
            _separatorObj.transform.SetParent(transform, false);

            RectTransform rt = _separatorObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 4);

            Image img = _separatorObj.AddComponent<Image>();
            img.color = new Color(themeColor.r, themeColor.g, themeColor.b, 0.5f);
            img.raycastTarget = false;

            var layoutElem = _separatorObj.AddComponent<LayoutElement>();
            layoutElem.preferredHeight = 4;
            layoutElem.flexibleWidth = 1;
        }
    }

    private void AddInfoRow(string label, string value, Color? valueColor = null)
    {
        GameObject rowObj;
        TextMeshProUGUI labelTxt;
        TextMeshProUGUI valueTxt;

        // Try to reuse pooled row
        if (_activeRowCount < _rowPool.Count)
        {
            rowObj = _rowPool[_activeRowCount];
            rowObj.SetActive(true);
            rowObj.transform.SetAsLastSibling(); // Ensure correct order

            // Get cached text components
            labelTxt = rowObj.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            valueTxt = rowObj.transform.Find("Value")?.GetComponent<TextMeshProUGUI>();

            if (labelTxt != null && valueTxt != null)
            {
                labelTxt.text = label;
                valueTxt.text = value;
                valueTxt.color = valueColor ?? Color.white;
                _valueTexts[label] = valueTxt;
                _activeRowCount++;
                return;
            }
        }

        // Create new row (pool miss or first time)
        rowObj = CreateInfoRowObject(label, value, valueColor, out valueTxt);
        _rowPool.Add(rowObj);
        _valueTexts[label] = valueTxt;
        _activeRowCount++;
    }

    /// <summary>
    /// Create a new info row object. Called when pool is empty.
    /// </summary>
    private GameObject CreateInfoRowObject(string label, string value, Color? valueColor, out TextMeshProUGUI valueTxt)
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

        valueTxt = valueObj.AddComponent<TextMeshProUGUI>();
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

        return rowObj;
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
