using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// RTT-based Mobile Virtual Keyboard.
/// Migrated from VRMobileKeyboard to use Render-to-Texture approach.
/// </summary>
public class RTTMobileKeyboard : RTTCanvasBase
{
    public enum KeyboardLayout { Letters, Symbols, MoreSymbols }

    #region Static Instance
    private static RTTMobileKeyboard _instance;
    public static RTTMobileKeyboard Instance => _instance;
    public static RTTMobileKeyboard CurrentlyOpenKeyboard { get; private set; }
    #endregion

    #region Configuration
    [Header("Theme")]
    [SerializeField] private Color themeColor = new Color(0.0f, 0.9f, 1.0f);
    [SerializeField] private Color keyColor = new Color(0.25f, 0.27f, 0.32f);
    [SerializeField] private Color specialKeyColor = new Color(0.35f, 0.38f, 0.45f);
    [SerializeField] private TMP_FontAsset customFont;

    [Header("Layout")]
    [SerializeField] private float marginLeft = 40f;
    [SerializeField] private float marginRight = 40f;
    [SerializeField] private float marginTop = 30f;
    [SerializeField] private float marginBottom = 50f;
    [SerializeField] [Range(0.05f, 0.2f)] private float keySpacingRatio = 0.1f;
    [SerializeField] [Range(0.8f, 1.5f)] private float keyHeightRatio = 1.2f;
    [SerializeField] private int keyFontSize = 36;

    [Header("Position")]
    [SerializeField] private bool followTaskbar = true;
    [SerializeField] private float spacingMultiplier = 1.5f;
    [SerializeField] private float widthRatioToFrame = 0.667f;
    #endregion

    #region Constants
    private const float PixelToMeter = 1.6f / 1920f;

    private static readonly string[] LETTERS_ROW_0 = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
    private static readonly string[] LETTERS_ROW_1 = { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" };
    private static readonly string[] LETTERS_ROW_2 = { "a", "s", "d", "f", "g", "h", "j", "k", "l" };
    private static readonly string[] LETTERS_ROW_3 = { "z", "x", "c", "v", "b", "n", "m" };

    private static readonly string[] SYMBOLS_ROW_0 = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
    private static readonly string[] SYMBOLS_ROW_1 = { "@", "#", "$", "_", "&", "-", "+", "(", ")", "/" };
    private static readonly string[] SYMBOLS_ROW_2 = { "*", "\"", "'", ":", ";", "!", "?" };

    private static readonly string[] MORE_SYMBOLS_ROW_0 = { "~", "`", "|", "•", "√", "π", "÷", "×", "§", "△" };
    private static readonly string[] MORE_SYMBOLS_ROW_1 = { "£", "€", "$", "¢", "^", "°", "=", "{", "}", "\\" };
    private static readonly string[] MORE_SYMBOLS_ROW_2 = { "%", "©", "®", "™", "✓", "[", "]" };
    #endregion

    #region Events
    public event Action<string> OnKeyPressed;
    public event Action OnBackspacePressed;
    public event Action OnEnterPressed;
    public event Action OnClosePressed;
    #endregion

    #region Private Fields
    private TMP_InputField _targetInputField;
    private bool _isShiftActive = false;
    private bool _isCapsLock = false;
    private KeyboardLayout _currentLayout = KeyboardLayout.Letters;

    private RectTransform _contentContainer;
    private TextMeshProUGUI _previewText;
    private List<GameObject> _allKeys = new List<GameObject>();
    private Dictionary<string, GameObject> _keyMap = new Dictionary<string, GameObject>();

    private float _logicalWidth;
    private float _logicalHeight;
    private float _keyWidth;
    private float _keyHeight;
    private float _keySpacing;

    // 4-row layout dimensions (calculated from 5-row layout)
    private float _increasedSpacing;  // 3x normal spacing for 4-row layouts
    private float _topRowKeyHeight;   // Taller keys for top 3 rows in 4-row layout

    private Sprite _pixelSprite;
    private Material _glassMaterial;
    private Material _borderMaterial;

    // Track if Show() was called before Start() completes
    private bool _showRequested = false;
    private bool _startCompleted = false;
    #endregion

    #region Private Fields
    private float _cornerRadius = 0.12f;
    private float _edgePadding = 0.06f;
    #endregion

    #region Lifecycle
    protected override void Awake()
    {
        // Set singleton instance early so it's available after AddComponent
        if (_instance == null)
        {
            _instance = this;
        }

        CalculateDimensions();
        worldWidth = _logicalWidth * PixelToMeter;
        worldHeight = _logicalHeight * PixelToMeter;

        base.Awake();
    }

    protected override void Start()
    {
        base.Start();

        // Ensure instance is set (in case Awake wasn't called due to inactive)
        if (_instance == null)
        {
            _instance = this;
        }

        // NOTE: Do NOT call SetLayerRecursive for VirtualObjects here!
        // RTTCanvasBase already handles layers correctly:
        // - DisplayQuad is on VirtualObjects layer (for world raycast)
        // - Canvas and children are on UI layer (for RTT camera to render)
        // Changing Canvas children to VirtualObjects would make them invisible to RTT camera.

        _startCompleted = true;

        // Only hide if Show() wasn't already called
        if (!_showRequested)
        {
            gameObject.SetActive(false); // Hidden by default
        }
    }

    protected override void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (CurrentlyOpenKeyboard == this) CurrentlyOpenKeyboard = null;

        if (_glassMaterial != null) Destroy(_glassMaterial);
        if (_borderMaterial != null) Destroy(_borderMaterial);

        base.OnDestroy();
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();

        if (_targetInputField != null && gameObject.activeSelf)
        {
            UpdatePositionRelativeToTaskbar();
        }
    }
    #endregion

    #region RTTCanvasBase Overrides
    protected override Vector2Int GetResolution()
    {
        CalculateDimensions();
        return new Vector2Int(Mathf.RoundToInt(_logicalWidth), Mathf.RoundToInt(_logicalHeight));
    }

    protected override int GetCameraDepth()
    {
        return -48; // Render after taskbar
    }

    protected override void BuildUI()
    {
        if (_canvas == null) return;

        var canvasRect = _canvas.GetComponent<RectTransform>();

        // Glass background
        CreateGlassBackground(canvasRect);

        // Content container
        CreateContentContainer(canvasRect);

        // Preview row
        CreatePreviewRow();

        // Key rows
        CreateKeyRows();

        Debug.Log($"[RTTMobileKeyboard] UI built: {_logicalWidth}x{_logicalHeight}");
    }
    #endregion

    #region Dimension Calculation
    private void CalculateDimensions()
    {
        // Base on VRMenuFrame width
        RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
        float menuLogicalWidth = primary != null ? primary.LogicalWidthValue : 1920f;

        _logicalWidth = menuLogicalWidth * widthRatioToFrame;

        // Calculate key dimensions
        float contentWidth = _logicalWidth - marginLeft - marginRight;
        int maxKeysPerRow = 10;
        _keySpacing = contentWidth * keySpacingRatio / maxKeysPerRow;
        _keyWidth = (contentWidth - _keySpacing * (maxKeysPerRow - 1)) / maxKeysPerRow;
        _keyHeight = _keyWidth * keyHeightRatio;

        // Calculate 4-row layout dimensions (same logic as VRMobileKeyboard)
        // For bottom row to stay at same position when switching layouts:
        // 5-row: preview + 4*keyHeight + 5*keySpacing above bottom row (5 gaps in VLG)
        // 4-row: preview + 3*topRowKeyHeight + 4*increasedSpacing above bottom row (4 gaps in VLG)
        // So: 4*keyHeight + 5*keySpacing = 3*topRowKeyHeight + 4*(3*keySpacing)
        //     4*keyHeight + 5*keySpacing = 3*topRowKeyHeight + 12*keySpacing
        //     3*topRowKeyHeight = 4*keyHeight - 7*keySpacing
        _increasedSpacing = _keySpacing * 3f;
        _topRowKeyHeight = (4f * _keyHeight - 7f * _keySpacing) / 3f;

        // Calculate total height (preview row + 5 key rows)
        // Row 1: Numbers, Row 2: QWERTY, Row 3: ASDF, Row 4: Shift+letters, Row 5: Bottom row
        int numRows = 5;
        float previewHeight = 60f;
        float rowSpacing = _keySpacing;
        float totalRowsHeight = previewHeight + (_keyHeight * numRows) + (rowSpacing * numRows);
        _logicalHeight = marginTop + totalRowsHeight + marginBottom;
    }
    #endregion

    #region UI Building
    private void CreateGlassBackground(RectTransform parent)
    {
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);

        Image img = bgObj.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = true;

        float aspect = _logicalWidth / _logicalHeight;
        float expansion = 0.07f;  // 7% expansion (2% original + 5% extra)
        float edgePad = expansion / (1f + 2f * expansion);

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            _glassMaterial = new Material(glassShader);
            _glassMaterial.SetFloat("_CornerRadius", 0.08f);
            _glassMaterial.SetFloat("_EdgePadding", edgePad);
            _glassMaterial.SetFloat("_Aspect", aspect);
            _glassMaterial.SetColor("_ColorA", new Color(0.2f, 0.22f, 0.28f, 0.95f));
            _glassMaterial.SetColor("_ColorB", new Color(0.15f, 0.17f, 0.22f, 0.95f));
            _glassMaterial.SetFloat("_GlassAlpha", 0.95f);
            img.material = _glassMaterial;
            img.color = Color.white;
        }
        else
        {
            img.color = new Color(0.12f, 0.13f, 0.16f, 0.98f);
        }

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(-expansion, -expansion);
        rt.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        rt.sizeDelta = Vector2.zero;
        rt.SetAsFirstSibling();

        // Border
        CreateGlowingBorder(bgObj.transform, edgePad, aspect);
    }

    private void CreateGlowingBorder(Transform parent, float edgePad, float aspect)
    {
        GameObject borderObj = new GameObject("GlowingBorder");
        borderObj.transform.SetParent(parent, false);

        RectTransform rt = borderObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;
        borderImg.sprite = GetPixelSprite();

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            _borderMaterial = new Material(glowShader);
            _borderMaterial.SetFloat("_CornerRadius", 0.08f);
            _borderMaterial.SetFloat("_EdgePadding", edgePad);
            _borderMaterial.SetFloat("_Aspect", aspect);
            _borderMaterial.SetColor("_ColorA", themeColor);
            _borderMaterial.SetColor("_ColorB", new Color(0.9f, 0.3f, 1f));
            _borderMaterial.SetFloat("_Layer1Width", 0.006f);
            _borderMaterial.SetFloat("_Layer1Alpha", 1.2f);
            borderImg.material = _borderMaterial;
        }
    }

    private void CreateContentContainer(RectTransform parent)
    {
        GameObject contentObj = new GameObject("ContentContainer");
        contentObj.transform.SetParent(parent, false);

        _contentContainer = contentObj.AddComponent<RectTransform>();
        _contentContainer.anchorMin = Vector2.zero;
        _contentContainer.anchorMax = Vector2.one;
        _contentContainer.offsetMin = new Vector2(marginLeft, marginBottom);
        _contentContainer.offsetMax = new Vector2(-marginRight, -marginTop);

        VerticalLayoutGroup layout = contentObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = _keySpacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private void CreatePreviewRow()
    {
        float previewHeight = 60f;
        float buttonSize = _keyHeight * 0.5f;
        float buttonPadding = 10f;

        GameObject previewRow = new GameObject("PreviewRow");
        previewRow.transform.SetParent(_contentContainer, false);

        RectTransform rt = previewRow.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, previewHeight);

        LayoutElement le = previewRow.AddComponent<LayoutElement>();
        le.preferredHeight = previewHeight;

        // Preview text - shortened on both sides for close and clear buttons
        GameObject textObj = new GameObject("PreviewText");
        textObj.transform.SetParent(previewRow.transform, false);

        _previewText = textObj.AddComponent<TextMeshProUGUI>();
        _previewText.text = "";
        _previewText.fontSize = 32f;
        _previewText.alignment = TextAlignmentOptions.Center;
        _previewText.color = Color.white;
        _previewText.overflowMode = TextOverflowModes.Ellipsis;
        if (customFont != null) _previewText.font = customFont;

        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(buttonSize + buttonPadding * 2f, 8f);
        textRT.offsetMax = new Vector2(-(buttonSize + buttonPadding * 2f), 0);

        // Glowing underline
        CreatePreviewUnderline(previewRow.transform);

        // Close button (left side) - using BareIconButton
        Sprite closeIcon = VRTaskbar.LoadIcon("close");
        GameObject closeBtn = VRButtonFactory.CreateBareIconButton(
            previewRow.transform,
            buttonSize,
            closeIcon,
            new Color(0.8f, 0.4f, 1.0f),
            () => OnClose(),
            0.01f,
            0.6f
        );
        RectTransform closeRT = closeBtn.GetComponent<RectTransform>();
        closeRT.anchorMin = new Vector2(0f, 0.5f);
        closeRT.anchorMax = new Vector2(0f, 0.5f);
        closeRT.pivot = new Vector2(0f, 0.5f);
        closeRT.anchoredPosition = new Vector2(buttonPadding, 4f);

        // Clear button (right side) - using BareIconButton
        Sprite clearIcon = VRTaskbar.LoadIcon("clear");
        GameObject clearBtn = VRButtonFactory.CreateBareIconButton(
            previewRow.transform,
            buttonSize,
            clearIcon,
            new Color(0.8f, 0.4f, 1.0f),
            () => OnClear(),
            0.01f,
            0.6f
        );
        RectTransform clearRT = clearBtn.GetComponent<RectTransform>();
        clearRT.anchorMin = new Vector2(1f, 0.5f);
        clearRT.anchorMax = new Vector2(1f, 0.5f);
        clearRT.pivot = new Vector2(1f, 0.5f);
        clearRT.anchoredPosition = new Vector2(-buttonPadding, 4f);
    }

    private void CreatePreviewUnderline(Transform parent)
    {
        float contentWidth = _logicalWidth - marginLeft - marginRight;

        GameObject underlineObj = new GameObject("Underline");
        underlineObj.transform.SetParent(parent, false);

        RectTransform rt = underlineObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0, 0);
        rt.sizeDelta = new Vector2(0, _keyHeight * 0.4f);

        Image lineImg = underlineObj.AddComponent<Image>();
        lineImg.raycastTarget = false;
        lineImg.sprite = GetPixelSprite();
        lineImg.color = Color.white;

        Shader glowShader = Shader.Find("Custom/GlowingHorizontalLine");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);
            glowMat.SetFloat("_LineWidth", 0.12f);
            glowMat.SetFloat("_GlowWidth", 0.4f);
            glowMat.SetColor("_ColorA", new Color(0.0f, 0.8f, 1f, 1f));   // Deep Sea Blue
            glowMat.SetColor("_ColorB", new Color(0.8f, 0.2f, 1f, 1f));   // Purple
            glowMat.SetFloat("_Layer1Alpha", 1f);
            glowMat.SetFloat("_Layer2Alpha", 0.6f);
            glowMat.SetFloat("_EdgeFade", 1f);
            lineImg.material = glowMat;
        }
        else
        {
            lineImg.color = new Color(0.3f, 0.9f, 1f, 0.5f);
        }
    }

    private void OnClose()
    {
        Hide();
    }

    private void OnClear()
    {
        if (_targetInputField != null)
        {
            _targetInputField.text = "";
            UpdatePreview();
            MarkDirty();
        }
    }

    private void CreateKeyRows()
    {
        _allKeys.Clear();
        _keyMap.Clear();

        // Update VerticalLayoutGroup spacing based on layout type
        VerticalLayoutGroup vlg = _contentContainer?.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            // 5-row layout uses normal spacing, 4-row layouts use increased spacing
            vlg.spacing = (_currentLayout == KeyboardLayout.Letters) ? _keySpacing : _increasedSpacing;
        }

        if (_currentLayout == KeyboardLayout.Letters)
        {
            CreateLettersLayout();
        }
        else if (_currentLayout == KeyboardLayout.Symbols)
        {
            CreateSymbolsLayout();
        }
        else
        {
            CreateMoreSymbolsLayout();
        }
    }

    private void CreateLettersLayout()
    {
        // Row 0 - Numbers
        CreateKeyRow(LETTERS_ROW_0, 0);

        // Row 1 - QWERTY
        CreateKeyRow(LETTERS_ROW_1, 1);

        // Row 2 - ASDF (with offset)
        CreateKeyRow(LETTERS_ROW_2, 2, _keyWidth * 0.5f);

        // Row 3 - ZXCV (with shift and backspace)
        CreateBottomLetterRow();

        // Row 4 - Space row
        CreateSpaceRow();
    }

    private void CreateSymbolsLayout()
    {
        // 4-row symbol layout like VRMobileKeyboard
        // Top 3 rows use _topRowKeyHeight, bottom row uses normal _keyHeight

        // Row 0 - Numbers (taller)
        CreateKeyRowWithHeight(SYMBOLS_ROW_0, 0, _topRowKeyHeight);

        // Row 1 - Symbols @#$... (taller)
        CreateKeyRowWithHeight(SYMBOLS_ROW_1, 1, _topRowKeyHeight);

        // Row 2 - Symbols *"'... with =\< and Back (taller)
        CreateSymbolRow2();

        // Row 3 - Bottom row: ABC , spacebar . Enter (normal height)
        CreateSymbolBottomRow();
    }

    private void CreateMoreSymbolsLayout()
    {
        // 4-row more symbols layout like VRMobileKeyboard
        // Top 3 rows use _topRowKeyHeight, bottom row uses normal _keyHeight

        // Row 0 - More symbols ~`|... (taller)
        CreateKeyRowWithHeight(MORE_SYMBOLS_ROW_0, 0, _topRowKeyHeight);

        // Row 1 - More symbols £€$... (taller)
        CreateKeyRowWithHeight(MORE_SYMBOLS_ROW_1, 1, _topRowKeyHeight);

        // Row 2 - More symbols with ?123 and Back (taller)
        CreateMoreSymbolRow2();

        // Row 3 - Bottom row: ABC < spacebar > Enter (normal height)
        CreateMoreSymbolBottomRow();
    }

    private void CreateKeyRowWithHeight(string[] keys, int rowIndex, float height)
    {
        GameObject row = new GameObject($"Row_{rowIndex}");
        row.transform.SetParent(_contentContainer, false);

        RectTransform rowRT = row.AddComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, height);

        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = height;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = _keySpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        foreach (string key in keys)
        {
            CreateKey(row.transform, key, _keyWidth, height, false);
        }
    }

    private void CreateSymbolRow2()
    {
        // Row 2 uses taller height for 4-row layout
        float rowHeight = _topRowKeyHeight;

        GameObject row = new GameObject("Row_2");
        row.transform.SetParent(_contentContainer, false);

        RectTransform rowRT = row.AddComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, rowHeight);

        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = rowHeight;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = _keySpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        // =\< key (switch to more symbols)
        CreateKey(row.transform, "=\\<", _keyWidth * 1.5f, rowHeight, true, () => {
            _currentLayout = KeyboardLayout.MoreSymbols;
            RebuildKeyboard();
        });

        // Symbol keys
        foreach (string key in SYMBOLS_ROW_2)
        {
            CreateKey(row.transform, key, _keyWidth, rowHeight, false);
        }

        // Backspace
        CreateKey(row.transform, "Back", _keyWidth * 1.5f, rowHeight, true, OnBackspace);
    }

    private void CreateSymbolBottomRow()
    {
        float contentWidth = _logicalWidth - marginLeft - marginRight;
        float specialKeyWidth = _keyWidth * 1.5f;

        GameObject row = new GameObject("Row_3");
        row.transform.SetParent(_contentContainer, false);

        RectTransform rowRT = row.AddComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, _keyHeight);

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = _keyHeight;

        // Left group: ABC, comma
        float leftGroupWidth = specialKeyWidth + _keySpacing + _keyWidth;
        GameObject leftGroup = new GameObject("LeftGroup");
        leftGroup.transform.SetParent(row.transform, false);

        RectTransform leftRT = leftGroup.AddComponent<RectTransform>();
        leftRT.anchorMin = new Vector2(0, 0.5f);
        leftRT.anchorMax = new Vector2(0, 0.5f);
        leftRT.pivot = new Vector2(0, 0.5f);
        leftRT.anchoredPosition = Vector2.zero;
        leftRT.sizeDelta = new Vector2(leftGroupWidth, _keyHeight);

        HorizontalLayoutGroup leftHLG = leftGroup.AddComponent<HorizontalLayoutGroup>();
        leftHLG.spacing = _keySpacing;
        leftHLG.childAlignment = TextAnchor.MiddleLeft;
        leftHLG.childControlWidth = false;
        leftHLG.childControlHeight = false;

        CreateKey(leftGroup.transform, "ABC", specialKeyWidth, _keyHeight, true, () => {
            _currentLayout = KeyboardLayout.Letters;
            RebuildKeyboard();
        });
        CreateKey(leftGroup.transform, ",", _keyWidth, _keyHeight, true);

        // Right group: dot, Enter
        float rightGroupWidth = _keyWidth + _keySpacing + specialKeyWidth;
        GameObject rightGroup = new GameObject("RightGroup");
        rightGroup.transform.SetParent(row.transform, false);

        RectTransform rightRT = rightGroup.AddComponent<RectTransform>();
        rightRT.anchorMin = new Vector2(1, 0.5f);
        rightRT.anchorMax = new Vector2(1, 0.5f);
        rightRT.pivot = new Vector2(1, 0.5f);
        rightRT.anchoredPosition = Vector2.zero;
        rightRT.sizeDelta = new Vector2(rightGroupWidth, _keyHeight);

        HorizontalLayoutGroup rightHLG = rightGroup.AddComponent<HorizontalLayoutGroup>();
        rightHLG.spacing = _keySpacing;
        rightHLG.childAlignment = TextAnchor.MiddleRight;
        rightHLG.childControlWidth = false;
        rightHLG.childControlHeight = false;

        CreateKey(rightGroup.transform, ".", _keyWidth, _keyHeight, true);
        CreateKey(rightGroup.transform, "Enter", specialKeyWidth, _keyHeight, true, OnEnter);

        // Space bar - centered between left and right groups with padding expansion
        float gap = _keySpacing + _keyWidth * _edgePadding;
        float baseWidth = contentWidth - leftGroupWidth - rightGroupWidth - 2 * gap;
        float spaceWidth = baseWidth * (1 + 2 * _edgePadding);
        float spaceLeft = leftGroupWidth + gap - baseWidth * _edgePadding;

        GameObject spaceKey = new GameObject("Key_Space");
        spaceKey.transform.SetParent(row.transform, false);

        RectTransform spaceRT = spaceKey.AddComponent<RectTransform>();
        spaceRT.anchorMin = new Vector2(0, 0.5f);
        spaceRT.anchorMax = new Vector2(0, 0.5f);
        spaceRT.pivot = new Vector2(0, 0.5f);
        spaceRT.anchoredPosition = new Vector2(spaceLeft, 0);
        spaceRT.sizeDelta = new Vector2(spaceWidth, _keyHeight);

        CreateKeyVisuals(spaceKey, " ", spaceWidth, _keyHeight, false, () => OnKeyPress(" "));
    }

    private void CreateMoreSymbolRow2()
    {
        // Row 2 uses taller height for 4-row layout
        float rowHeight = _topRowKeyHeight;

        GameObject row = new GameObject("Row_2");
        row.transform.SetParent(_contentContainer, false);

        RectTransform rowRT = row.AddComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, rowHeight);

        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = rowHeight;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = _keySpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        // ?123 key (back to symbols)
        CreateKey(row.transform, "?123", _keyWidth * 1.5f, rowHeight, true, () => {
            _currentLayout = KeyboardLayout.Symbols;
            RebuildKeyboard();
        });

        // Symbol keys
        foreach (string key in MORE_SYMBOLS_ROW_2)
        {
            CreateKey(row.transform, key, _keyWidth, rowHeight, false);
        }

        // Backspace
        CreateKey(row.transform, "Back", _keyWidth * 1.5f, rowHeight, true, OnBackspace);
    }

    private void CreateMoreSymbolBottomRow()
    {
        float contentWidth = _logicalWidth - marginLeft - marginRight;
        float specialKeyWidth = _keyWidth * 1.5f;

        GameObject row = new GameObject("Row_3");
        row.transform.SetParent(_contentContainer, false);

        RectTransform rowRT = row.AddComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, _keyHeight);

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = _keyHeight;

        // Left group: ABC, <
        float leftGroupWidth = specialKeyWidth + _keySpacing + _keyWidth;
        GameObject leftGroup = new GameObject("LeftGroup");
        leftGroup.transform.SetParent(row.transform, false);

        RectTransform leftRT = leftGroup.AddComponent<RectTransform>();
        leftRT.anchorMin = new Vector2(0, 0.5f);
        leftRT.anchorMax = new Vector2(0, 0.5f);
        leftRT.pivot = new Vector2(0, 0.5f);
        leftRT.anchoredPosition = Vector2.zero;
        leftRT.sizeDelta = new Vector2(leftGroupWidth, _keyHeight);

        HorizontalLayoutGroup leftHLG = leftGroup.AddComponent<HorizontalLayoutGroup>();
        leftHLG.spacing = _keySpacing;
        leftHLG.childAlignment = TextAnchor.MiddleLeft;
        leftHLG.childControlWidth = false;
        leftHLG.childControlHeight = false;

        CreateKey(leftGroup.transform, "ABC", specialKeyWidth, _keyHeight, true, () => {
            _currentLayout = KeyboardLayout.Letters;
            RebuildKeyboard();
        });
        CreateKey(leftGroup.transform, "<", _keyWidth, _keyHeight, true);

        // Right group: >, Enter
        float rightGroupWidth = _keyWidth + _keySpacing + specialKeyWidth;
        GameObject rightGroup = new GameObject("RightGroup");
        rightGroup.transform.SetParent(row.transform, false);

        RectTransform rightRT = rightGroup.AddComponent<RectTransform>();
        rightRT.anchorMin = new Vector2(1, 0.5f);
        rightRT.anchorMax = new Vector2(1, 0.5f);
        rightRT.pivot = new Vector2(1, 0.5f);
        rightRT.anchoredPosition = Vector2.zero;
        rightRT.sizeDelta = new Vector2(rightGroupWidth, _keyHeight);

        HorizontalLayoutGroup rightHLG = rightGroup.AddComponent<HorizontalLayoutGroup>();
        rightHLG.spacing = _keySpacing;
        rightHLG.childAlignment = TextAnchor.MiddleRight;
        rightHLG.childControlWidth = false;
        rightHLG.childControlHeight = false;

        CreateKey(rightGroup.transform, ">", _keyWidth, _keyHeight, true);
        CreateKey(rightGroup.transform, "Enter", specialKeyWidth, _keyHeight, true, OnEnter);

        // Space bar - centered between left and right groups with padding expansion
        float gap = _keySpacing + _keyWidth * _edgePadding;
        float baseWidth = contentWidth - leftGroupWidth - rightGroupWidth - 2 * gap;
        float spaceWidth = baseWidth * (1 + 2 * _edgePadding);
        float spaceLeft = leftGroupWidth + gap - baseWidth * _edgePadding;

        GameObject spaceKey = new GameObject("Key_Space");
        spaceKey.transform.SetParent(row.transform, false);

        RectTransform spaceRT = spaceKey.AddComponent<RectTransform>();
        spaceRT.anchorMin = new Vector2(0, 0.5f);
        spaceRT.anchorMax = new Vector2(0, 0.5f);
        spaceRT.pivot = new Vector2(0, 0.5f);
        spaceRT.anchoredPosition = new Vector2(spaceLeft, 0);
        spaceRT.sizeDelta = new Vector2(spaceWidth, _keyHeight);

        CreateKeyVisuals(spaceKey, " ", spaceWidth, _keyHeight, false, () => OnKeyPress(" "));
    }

    private void CreateKeyRow(string[] keys, int rowIndex, float offset = 0)
    {
        GameObject row = new GameObject($"Row_{rowIndex}");
        row.transform.SetParent(_contentContainer, false);

        RectTransform rowRT = row.AddComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, _keyHeight);

        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = _keyHeight;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = _keySpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        if (offset > 0)
        {
            layout.padding = new RectOffset(Mathf.RoundToInt(offset), 0, 0, 0);
        }

        foreach (string key in keys)
        {
            CreateKey(row.transform, key, _keyWidth, _keyHeight, false);
        }
    }

    private void CreateBottomLetterRow()
    {
        GameObject row = new GameObject("Row_3");
        row.transform.SetParent(_contentContainer, false);

        RectTransform rowRT = row.AddComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, _keyHeight);

        LayoutElement le = row.AddComponent<LayoutElement>();
        le.preferredHeight = _keyHeight;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = _keySpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        // Shift key - use text label like VRMobileKeyboard
        string shiftLabel = _isCapsLock ? "SHIFT" : "Shift";
        CreateKey(row.transform, shiftLabel, _keyWidth * 1.5f, _keyHeight, true, OnShiftPress);

        // Letter keys
        foreach (string key in LETTERS_ROW_3)
        {
            CreateKey(row.transform, key, _keyWidth, _keyHeight, false);
        }

        // Backspace - use text label like VRMobileKeyboard
        CreateKey(row.transform, "Back", _keyWidth * 1.5f, _keyHeight, true, OnBackspace);
    }

    private void CreateSpaceRow()
    {
        float contentWidth = _logicalWidth - marginLeft - marginRight;
        float specialKeyWidth = _keyWidth * 1.5f;

        GameObject row = new GameObject("Row_4");
        row.transform.SetParent(_contentContainer, false);

        RectTransform rowRT = row.AddComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, _keyHeight);

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = _keyHeight;

        // NO HLG on row - we position 3 groups manually

        // Left group: ?123, /
        float leftGroupWidth = specialKeyWidth + _keySpacing + _keyWidth;
        GameObject leftGroup = new GameObject("LeftGroup");
        leftGroup.transform.SetParent(row.transform, false);

        RectTransform leftRT = leftGroup.AddComponent<RectTransform>();
        leftRT.anchorMin = new Vector2(0, 0.5f);
        leftRT.anchorMax = new Vector2(0, 0.5f);
        leftRT.pivot = new Vector2(0, 0.5f);
        leftRT.anchoredPosition = Vector2.zero;
        leftRT.sizeDelta = new Vector2(leftGroupWidth, _keyHeight);

        HorizontalLayoutGroup leftHLG = leftGroup.AddComponent<HorizontalLayoutGroup>();
        leftHLG.spacing = _keySpacing;
        leftHLG.childAlignment = TextAnchor.MiddleLeft;
        leftHLG.childControlWidth = false;
        leftHLG.childControlHeight = false;

        CreateKey(leftGroup.transform, "?123", specialKeyWidth, _keyHeight, true, OnLayoutToggle);
        CreateKey(leftGroup.transform, "/", _keyWidth, _keyHeight, true);

        // Right group: ., Enter
        float rightGroupWidth = _keyWidth + _keySpacing + specialKeyWidth;
        GameObject rightGroup = new GameObject("RightGroup");
        rightGroup.transform.SetParent(row.transform, false);

        RectTransform rightRT = rightGroup.AddComponent<RectTransform>();
        rightRT.anchorMin = new Vector2(1, 0.5f);
        rightRT.anchorMax = new Vector2(1, 0.5f);
        rightRT.pivot = new Vector2(1, 0.5f);
        rightRT.anchoredPosition = Vector2.zero;
        rightRT.sizeDelta = new Vector2(rightGroupWidth, _keyHeight);

        HorizontalLayoutGroup rightHLG = rightGroup.AddComponent<HorizontalLayoutGroup>();
        rightHLG.spacing = _keySpacing;
        rightHLG.childAlignment = TextAnchor.MiddleRight;
        rightHLG.childControlWidth = false;
        rightHLG.childControlHeight = false;

        CreateKey(rightGroup.transform, ".", _keyWidth, _keyHeight, true);
        CreateKey(rightGroup.transform, "Enter", specialKeyWidth, _keyHeight, true, OnEnter);

        // Space bar - centered between left and right groups with padding expansion
        float gap = _keySpacing + _keyWidth * _edgePadding;
        float baseWidth = contentWidth - leftGroupWidth - rightGroupWidth - 2 * gap;
        float spaceWidth = baseWidth * (1 + 2 * _edgePadding);
        float spaceLeft = leftGroupWidth + gap - baseWidth * _edgePadding;

        GameObject spaceKey = new GameObject("Key_Space");
        spaceKey.transform.SetParent(row.transform, false);

        RectTransform spaceRT = spaceKey.AddComponent<RectTransform>();
        spaceRT.anchorMin = new Vector2(0, 0.5f);
        spaceRT.anchorMax = new Vector2(0, 0.5f);
        spaceRT.pivot = new Vector2(0, 0.5f);
        spaceRT.anchoredPosition = new Vector2(spaceLeft, 0);
        spaceRT.sizeDelta = new Vector2(spaceWidth, _keyHeight);

        // Create space key visuals
        CreateKeyVisuals(spaceKey, " ", spaceWidth, _keyHeight, false, () => OnKeyPress(" "));
    }

    /// <summary>
    /// Creates key visuals without LayoutElement (for manual positioned keys).
    /// </summary>
    private void CreateKeyVisuals(GameObject keyObj, string label, float width, float height, bool isSpecial, Action onClick)
    {
        Color baseColor = isSpecial ? specialKeyColor : keyColor;
        float aspect = width / height;

        // Background with rounded corners and glass effect
        Image bg = keyObj.AddComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.raycastTarget = true;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", _cornerRadius);
            glassMat.SetFloat("_EdgePadding", _edgePadding);
            glassMat.SetFloat("_Aspect", aspect);

            Color colorA = new Color(baseColor.r * 1.2f, baseColor.g * 1.2f, baseColor.b * 1.2f, 0.95f);
            Color colorB = new Color(baseColor.r * 0.8f, baseColor.g * 0.8f, baseColor.b * 0.8f, 0.95f);
            glassMat.SetColor("_ColorA", colorA);
            glassMat.SetColor("_ColorB", colorB);
            glassMat.SetFloat("_GradientOffset", 0f);
            glassMat.SetFloat("_GradientAngle", -15f);
            glassMat.SetFloat("_GlassAlpha", 0.95f);
            glassMat.SetFloat("_BlurEnabled", 0f);

            bg.material = glassMat;
            bg.color = Color.white;
        }
        else
        {
            bg.color = baseColor;
        }

        // Border with glow effect
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(keyObj.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.sprite = GetPixelSprite();
        borderImg.raycastTarget = false;

        Shader borderShader = Shader.Find("Custom/GlowingElementBorder");
        if (borderShader != null)
        {
            Material borderMat = new Material(borderShader);
            borderMat.SetFloat("_Aspect", aspect);
            borderMat.SetFloat("_EdgePadding", _edgePadding);
            borderMat.SetFloat("_CornerRadius", _cornerRadius);

            Color borderGlowCol = Color.Lerp(baseColor, Color.white, 0.5f);
            borderMat.SetColor("_GlowColor", borderGlowCol);

            borderMat.SetFloat("_BorderWidth", 0.02f);
            borderMat.SetFloat("_GlowWidth", 0.03f);
            borderMat.SetFloat("_GlowIntensity", 1.5f);
            borderMat.SetFloat("_PulseEnabled", 0f);

            borderImg.material = borderMat;
        }

        // Button
        Button btn = keyObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = colors;

        btn.onClick.AddListener(() =>
        {
            onClick?.Invoke();
            MarkDirty();
        });

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(keyObj.transform, false);

        TextMeshProUGUI tmp = labelObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label == " " ? "Space" : label;
        tmp.fontSize = keyFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (customFont != null) tmp.font = customFont;

        RectTransform labelRT = labelObj.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        _allKeys.Add(keyObj);

        // Add hover effect
        AddHoverEffect(keyObj, baseColor);
    }

    /// <summary>
    /// Creates a key with manual positioning (no HLG).
    /// </summary>
    private void CreateKeyManual(Transform parent, string label, float xPos, float width, float height, bool isSpecial, Action onClick = null)
    {
        GameObject keyObj = new GameObject($"Key_{label}");
        keyObj.transform.SetParent(parent, false);

        RectTransform rt = keyObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(0, 0.5f);
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(xPos, 0);
        rt.sizeDelta = new Vector2(width, height);

        // No LayoutElement needed for manual positioning

        Color baseColor = isSpecial ? specialKeyColor : keyColor;
        float aspect = width / height;

        // Background with rounded corners and glass effect
        Image bg = keyObj.AddComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.raycastTarget = true;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", _cornerRadius);
            glassMat.SetFloat("_EdgePadding", _edgePadding);
            glassMat.SetFloat("_Aspect", aspect);

            Color colorA = new Color(baseColor.r * 1.2f, baseColor.g * 1.2f, baseColor.b * 1.2f, 0.95f);
            Color colorB = new Color(baseColor.r * 0.8f, baseColor.g * 0.8f, baseColor.b * 0.8f, 0.95f);
            glassMat.SetColor("_ColorA", colorA);
            glassMat.SetColor("_ColorB", colorB);
            glassMat.SetFloat("_GradientOffset", 0f);
            glassMat.SetFloat("_GradientAngle", -15f);
            glassMat.SetFloat("_GlassAlpha", 0.95f);
            glassMat.SetFloat("_BlurEnabled", 0f);

            bg.material = glassMat;
            bg.color = Color.white;
        }
        else
        {
            bg.color = baseColor;
        }

        // Border with glow effect
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(keyObj.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.sprite = GetPixelSprite();
        borderImg.raycastTarget = false;

        Shader borderShader = Shader.Find("Custom/GlowingElementBorder");
        if (borderShader != null)
        {
            Material borderMat = new Material(borderShader);
            borderMat.SetFloat("_Aspect", aspect);
            borderMat.SetFloat("_EdgePadding", _edgePadding);
            borderMat.SetFloat("_CornerRadius", _cornerRadius);

            Color borderGlowCol = Color.Lerp(baseColor, Color.white, 0.5f);
            borderMat.SetColor("_GlowColor", borderGlowCol);

            // Keep same border width for all keys for visual consistency
            borderMat.SetFloat("_BorderWidth", 0.02f);
            borderMat.SetFloat("_GlowWidth", 0.03f);
            borderMat.SetFloat("_GlowIntensity", 1.5f);
            borderMat.SetFloat("_PulseEnabled", 0f);

            borderImg.material = borderMat;
        }

        // Button
        Button btn = keyObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = colors;

        string capturedLabel = label;
        btn.onClick.AddListener(() =>
        {
            if (onClick != null)
                onClick.Invoke();
            else
                OnKeyPress(capturedLabel);
            MarkDirty();
        });

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(keyObj.transform, false);

        TextMeshProUGUI tmp = labelObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label == " " ? "Space" : label;
        tmp.fontSize = keyFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (customFont != null) tmp.font = customFont;

        RectTransform labelRT = labelObj.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        _allKeys.Add(keyObj);
        if (!string.IsNullOrEmpty(label) && label != " ")
        {
            _keyMap[label.ToLower()] = keyObj;
        }

        // Add hover effect
        AddHoverEffect(keyObj, baseColor);
    }

    private void CreateKey(Transform parent, string label, float width, float height, bool isSpecial, Action onClick = null)
    {
        GameObject keyObj = new GameObject($"Key_{label}");
        keyObj.transform.SetParent(parent, false);

        RectTransform rt = keyObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        LayoutElement le = keyObj.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;

        Color baseColor = isSpecial ? specialKeyColor : keyColor;
        float aspect = width / height;

        // Background with rounded corners and glass effect
        Image bg = keyObj.AddComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.raycastTarget = true;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", _cornerRadius);
            glassMat.SetFloat("_EdgePadding", _edgePadding);
            glassMat.SetFloat("_Aspect", aspect);

            // Gradient colors based on key color
            Color colorA = new Color(baseColor.r * 1.2f, baseColor.g * 1.2f, baseColor.b * 1.2f, 0.95f);
            Color colorB = new Color(baseColor.r * 0.8f, baseColor.g * 0.8f, baseColor.b * 0.8f, 0.95f);
            glassMat.SetColor("_ColorA", colorA);
            glassMat.SetColor("_ColorB", colorB);
            glassMat.SetFloat("_GradientOffset", 0f);
            glassMat.SetFloat("_GradientAngle", -15f);
            glassMat.SetFloat("_GlassAlpha", 0.95f);
            glassMat.SetFloat("_BlurEnabled", 0f);

            bg.material = glassMat;
            bg.color = Color.white;
        }
        else
        {
            bg.color = baseColor;
        }

        // Border with glow effect
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(keyObj.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.sprite = GetPixelSprite();
        borderImg.raycastTarget = false;

        Shader borderShader = Shader.Find("Custom/GlowingElementBorder");
        if (borderShader != null)
        {
            Material borderMat = new Material(borderShader);
            borderMat.SetFloat("_Aspect", aspect);
            borderMat.SetFloat("_EdgePadding", _edgePadding);
            borderMat.SetFloat("_CornerRadius", _cornerRadius);

            Color borderGlowCol = Color.Lerp(baseColor, Color.white, 0.5f);
            borderMat.SetColor("_GlowColor", borderGlowCol);

            borderMat.SetFloat("_BorderWidth", 0.02f);
            borderMat.SetFloat("_GlowWidth", 0.03f);
            borderMat.SetFloat("_GlowIntensity", 1.5f);
            borderMat.SetFloat("_PulseEnabled", 0f);

            borderImg.material = borderMat;
        }

        // Button
        Button btn = keyObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = colors;

        string capturedLabel = label;
        btn.onClick.AddListener(() =>
        {
            if (onClick != null)
                onClick.Invoke();
            else
                OnKeyPress(capturedLabel);
            MarkDirty();
        });

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(keyObj.transform, false);

        TextMeshProUGUI tmp = labelObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label == " " ? "space" : label;
        tmp.fontSize = keyFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (customFont != null) tmp.font = customFont;

        RectTransform labelRT = labelObj.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        _allKeys.Add(keyObj);
        if (!string.IsNullOrEmpty(label) && label != " ")
        {
            _keyMap[label.ToLower()] = keyObj;
        }

        // Add hover effect
        AddHoverEffect(keyObj, baseColor);
    }
    #endregion

    #region Key Actions
    private void OnKeyPress(string key)
    {
        if (_targetInputField == null) return;

        string insertKey = key;
        if (_isShiftActive || _isCapsLock)
        {
            insertKey = key.ToUpper();
            if (_isShiftActive && !_isCapsLock)
            {
                _isShiftActive = false;
            }
        }

        int caretPos = _targetInputField.caretPosition;
        _targetInputField.text = _targetInputField.text.Insert(caretPos, insertKey);
        _targetInputField.caretPosition = caretPos + insertKey.Length;

        OnKeyPressed?.Invoke(insertKey);
        UpdatePreview();
        MarkDirty();
    }

    private void OnBackspace()
    {
        if (_targetInputField == null) return;

        int caretPos = _targetInputField.caretPosition;
        if (caretPos > 0)
        {
            _targetInputField.text = _targetInputField.text.Remove(caretPos - 1, 1);
            _targetInputField.caretPosition = caretPos - 1;
        }

        OnBackspacePressed?.Invoke();
        UpdatePreview();
        MarkDirty();
    }

    private void OnEnter()
    {
        OnEnterPressed?.Invoke();
        Hide();
    }

    private void OnShiftPress()
    {
        _isShiftActive = !_isShiftActive;
        MarkDirty();
    }

    private void OnLayoutToggle()
    {
        _currentLayout = _currentLayout == KeyboardLayout.Letters
            ? KeyboardLayout.Symbols
            : KeyboardLayout.Letters;
        RebuildKeyboard();
    }

    private void RebuildKeyboard()
    {
        // Clear existing keys
        foreach (var key in _allKeys)
        {
            if (key != null) Destroy(key);
        }
        _allKeys.Clear();
        _keyMap.Clear();

        // Destroy all row containers (Row_0, Row_1, etc.)
        if (_contentContainer != null)
        {
            // Collect children to destroy (can't destroy during iteration)
            var rowsToDestroy = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in _contentContainer)
            {
                if (child.name.StartsWith("Row_"))
                {
                    rowsToDestroy.Add(child.gameObject);
                }
            }
            foreach (var row in rowsToDestroy)
            {
                Destroy(row);
            }
        }

        // Rebuild based on layout
        CreateKeyRows();
        MarkDirty();
    }
    #endregion

    #region Preview
    private void UpdatePreview()
    {
        if (_previewText == null || _targetInputField == null) return;

        // Just show text without blinking cursor
        _previewText.text = _targetInputField.text;

        // Always keep caret at end
        _targetInputField.caretPosition = _targetInputField.text.Length;
    }
    #endregion

    #region Position
    [Header("Camera Proximity")]
    [Tooltip("How much closer to camera (0 = same as menu, 1 = at camera)")]
    [SerializeField] [Range(0f, 0.5f)] private float cameraProximity = 0.15f;

    /// <summary>
    /// Updates keyboard position to overlap RTTMenuFrame bottom and be closer to camera.
    /// Similar to VRMobileKeyboard's UpdatePositionRelativeToPrimary.
    /// </summary>
    private void UpdatePositionRelativeToTaskbar()
    {
        if (!followTaskbar) return;

        RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
        if (primary == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        RTTTaskbar taskbar = RTTTaskbar.Instance;
        Vector3 cameraPos = cam.transform.position;
        float keyboardHalfHeight = (_logicalHeight * PixelToMeter) / 2f;

        Vector3 targetPos;
        Quaternion targetRotation;

        // Get primary frame bottom position
        Vector3 primaryPos = primary.transform.position;
        float primaryHalfHeight = primary.PanelHeight / 2f;
        float frameBottomY = primaryPos.y - primaryHalfHeight;

        // Keyboard top edge should slightly overlap frame bottom
        float overlapAmount = keyboardHalfHeight * 0.1f;
        float marginOffset = (marginBottom - marginTop) * 1.5f * PixelToMeter / 2f;
        float keyboardCenterY = frameBottomY - keyboardHalfHeight + overlapAmount - marginOffset;

        if (taskbar != null && taskbar.gameObject.activeInHierarchy)
        {
            Vector3 taskbarPos = taskbar.transform.position;

            // Use taskbar X and Z, but calculated Y for overlap
            Vector3 targetPlanePoint = new Vector3(taskbarPos.x, keyboardCenterY, taskbarPos.z);

            // Move closer to camera
            Vector3 dirToTarget = targetPlanePoint - cameraPos;
            float distToTarget = dirToTarget.magnitude;
            targetPos = cameraPos + dirToTarget.normalized * (distToTarget * (1f - cameraProximity));

            targetRotation = taskbar.transform.rotation;
        }
        else
        {
            Vector3 targetPlanePoint = new Vector3(primaryPos.x, keyboardCenterY, primaryPos.z);

            Vector3 dirToTarget = targetPlanePoint - cameraPos;
            float distToTarget = dirToTarget.magnitude;
            targetPos = cameraPos + dirToTarget.normalized * (distToTarget * (1f - cameraProximity));

            targetRotation = primary.transform.rotation;
        }

        transform.position = targetPos;
        transform.rotation = targetRotation;
    }
    #endregion

    #region Show/Hide
    public void Show(TMP_InputField inputField)
    {
        if (inputField == null) return;

        _showRequested = true;
        _targetInputField = inputField;
        gameObject.SetActive(true);
        CurrentlyOpenKeyboard = this;

        // NOTE: Do NOT call SetLayerRecursive here!
        // RTTCanvasBase handles layers: DisplayQuad = VirtualObjects, Canvas = UI

        // Position relative to RTTMenuFrame
        UpdatePositionRelativeToTaskbar();

        UpdatePreview();
        MarkDirty();

        Debug.Log("[RTTMobileKeyboard] Shown");
    }

    public new void Hide()
    {
        base.Hide(); // Call base to properly set visibility
        _targetInputField = null;

        if (CurrentlyOpenKeyboard == this)
            CurrentlyOpenKeyboard = null;

        OnClosePressed?.Invoke();

        Debug.Log("[RTTMobileKeyboard] Hidden");
    }

    public void SwitchToInputField(TMP_InputField newInputField)
    {
        _targetInputField = newInputField;
        UpdatePreview();
        MarkDirty();
    }

    public bool IsPartOfKeyboard(GameObject obj)
    {
        if (obj == null) return false;
        return obj.transform.IsChildOf(transform);
    }
    #endregion

    #region Helpers
    private Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;

        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }

    private void AddHoverEffect(GameObject keyObj, Color baseColor)
    {
        // Use simple KeyHoverEffect - VRButtonAnimation breaks layout by modifying transform
        var hover = keyObj.AddComponent<KeyHoverEffect>();
        hover.Initialize(themeColor, this);
    }
    #endregion
}

/// <summary>
/// Simple hover effect for keyboard keys. Only changes border glow, no transform modifications.
/// RTTRaycastManager will call OnPointerEnter/Exit through ExecuteEvents.
/// </summary>
public class KeyHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Material _borderMaterial;
    private Color _hoverGlowColor;
    private RTTMobileKeyboard _keyboard;
    private Shader _originalShader;
    private Shader _hoverShader;

    // Saved properties
    private float _savedAspect;
    private float _savedCornerRadius;
    private float _savedEdgePadding;
    private float _savedBorderWidth;
    private float _savedGlowWidth;
    private float _savedGlowIntensity;
    private Color _savedGlowColor;

    public void Initialize(Color themeColor, RTTMobileKeyboard keyboard)
    {
        _keyboard = keyboard;
        _hoverGlowColor = themeColor;
        _hoverShader = Shader.Find("Custom/GlowingGlassBorder");

        // Find border child
        Transform borderTransform = transform.Find("Border");
        if (borderTransform != null)
        {
            var borderImage = borderTransform.GetComponent<Image>();
            if (borderImage != null && borderImage.material != null)
            {
                // Create material instance
                _borderMaterial = new Material(borderImage.material);
                borderImage.material = _borderMaterial;
                _originalShader = _borderMaterial.shader;

                // Save ALL original properties (including geometry)
                if (_borderMaterial.HasProperty("_Aspect"))
                    _savedAspect = _borderMaterial.GetFloat("_Aspect");
                if (_borderMaterial.HasProperty("_CornerRadius"))
                    _savedCornerRadius = _borderMaterial.GetFloat("_CornerRadius");
                if (_borderMaterial.HasProperty("_EdgePadding"))
                    _savedEdgePadding = _borderMaterial.GetFloat("_EdgePadding");
                if (_borderMaterial.HasProperty("_BorderWidth"))
                    _savedBorderWidth = _borderMaterial.GetFloat("_BorderWidth");
                if (_borderMaterial.HasProperty("_GlowWidth"))
                    _savedGlowWidth = _borderMaterial.GetFloat("_GlowWidth");
                if (_borderMaterial.HasProperty("_GlowIntensity"))
                    _savedGlowIntensity = _borderMaterial.GetFloat("_GlowIntensity");
                if (_borderMaterial.HasProperty("_GlowColor"))
                    _savedGlowColor = _borderMaterial.GetColor("_GlowColor");
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_borderMaterial == null) return;

        // Switch to hover shader for better glow effect
        if (_hoverShader != null)
            _borderMaterial.shader = _hoverShader;

        // Restore geometry properties after shader change
        _borderMaterial.SetFloat("_Aspect", _savedAspect);
        _borderMaterial.SetFloat("_CornerRadius", _savedCornerRadius);
        _borderMaterial.SetFloat("_EdgePadding", _savedEdgePadding);

        // Apply hover effect
        _borderMaterial.SetColor("_GlowColor", _hoverGlowColor);
        _borderMaterial.SetFloat("_GlowIntensity", _savedGlowIntensity * 2f);
        _borderMaterial.SetFloat("_GlowWidth", _savedGlowWidth * 2f);
        _borderMaterial.SetFloat("_BorderWidth", _savedBorderWidth * 1.5f);

        _keyboard?.MarkDirty();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_borderMaterial == null) return;

        // Restore original shader
        if (_originalShader != null)
            _borderMaterial.shader = _originalShader;

        // Restore ALL properties
        _borderMaterial.SetFloat("_Aspect", _savedAspect);
        _borderMaterial.SetFloat("_CornerRadius", _savedCornerRadius);
        _borderMaterial.SetFloat("_EdgePadding", _savedEdgePadding);
        _borderMaterial.SetColor("_GlowColor", _savedGlowColor);
        _borderMaterial.SetFloat("_GlowIntensity", _savedGlowIntensity);
        _borderMaterial.SetFloat("_GlowWidth", _savedGlowWidth);
        _borderMaterial.SetFloat("_BorderWidth", _savedBorderWidth);

        _keyboard?.MarkDirty();
    }

    private void OnDestroy()
    {
        if (_borderMaterial != null)
            Destroy(_borderMaterial);
    }
}
