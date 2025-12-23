using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// Mobile-style Virtual Keyboard for VR environment.
/// Displays a compact mobile keyboard with multiple layouts (letters, symbols).
/// </summary>
public class VRMobileKeyboard : MonoBehaviour
{
    public enum KeyboardLayout
    {
        Letters,        // qwerty with numbers on top
        Symbols,        // @#$_&-+()/ etc
        MoreSymbols     // ~`|•√π÷×§△ etc
    }

    [Header("Theme")]
    public Color themeColor = new Color(0.0f, 0.9f, 1.0f);
    public Color accentColor = new Color(0.4f, 0.5f, 0.7f);
    public Color specialKeyColor = new Color(0.35f, 0.38f, 0.45f);
    public Color keyColor = new Color(0.25f, 0.27f, 0.32f);
    public Color backgroundColor = new Color(0.12f, 0.13f, 0.16f, 0.98f);
    public TMP_FontAsset customFont;

    [Header("Layout")]
    public float keyWidth = 90f;
    public float keyHeight = 90f;
    public float keySpacing = 8f;

    [Header("Settings")]
    public bool showPreview = true;
    public float keyboardScale = 1f;

    // Events
    public event Action<string> OnKeyPressed;
    public event Action OnBackspacePressed;
    public event Action OnEnterPressed;
    public event Action OnClosePressed;

    // Current state
    private TMP_InputField _targetInputField;
    private bool _isShiftActive = false;
    private bool _isCapsLock = false;
    private KeyboardLayout _currentLayout = KeyboardLayout.Letters;

    // UI References
    private GameObject _keyboardContainer;
    private GameObject _previewText;
    private TextMeshProUGUI _previewTMP;
    private List<GameObject> _allKeys = new List<GameObject>();
    private GameObject _shiftKey;
    private GameObject _layoutSwitchKey;
    private GameObject _symbolSwitchKey;

    // Cursor blinking
    private bool _cursorVisible = true;
    private float _cursorBlinkTimer = 0f;
    private const float CURSOR_BLINK_RATE = 0.5f;

    // Mobile keyboard layouts
    // Letters layout (lowercase) - row 0 is numbers
    private static readonly string[] LETTERS_ROW_0 = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
    private static readonly string[] LETTERS_ROW_1 = { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" };
    private static readonly string[] LETTERS_ROW_2 = { "a", "s", "d", "f", "g", "h", "j", "k", "l" };
    private static readonly string[] LETTERS_ROW_3 = { "z", "x", "c", "v", "b", "n", "m" };

    // Shifted numbers (symbols above numbers)
    private static readonly string[] SHIFTED_ROW_0 = { "!", "@", "#", "$", "%", "^", "&", "*", "(", ")" };

    // Symbols layout
    private static readonly string[] SYMBOLS_ROW_0 = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
    private static readonly string[] SYMBOLS_ROW_1 = { "@", "#", "₫", "_", "&", "-", "+", "(", ")", "/" };
    private static readonly string[] SYMBOLS_ROW_2 = { "*", "\"", "'", ":", ";", "!", "?" };

    // More symbols layout
    private static readonly string[] MORE_SYMBOLS_ROW_0 = { "~", "`", "|", "•", "√", "π", "÷", "×", "§", "△" };
    private static readonly string[] MORE_SYMBOLS_ROW_1 = { "£", "€", "$", "¢", "^", "°", "=", "{", "}", "\\" };
    private static readonly string[] MORE_SYMBOLS_ROW_2 = { "%", "©", "®", "™", "✓", "[", "]" };

    private static VRMobileKeyboard _instance;
    public static VRMobileKeyboard Instance => _instance;

    private bool _isBuilt = false;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    void Start()
    {
        EnsureBuilt();
        if (!_isKeyboardVisibleOnStart)
        {
            gameObject.SetActive(false);
        }
    }

    private bool _isKeyboardVisibleOnStart = false;

    private void EnsureBuilt()
    {
        if (_isBuilt) return;
        _isBuilt = true;
        BuildKeyboard();
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    void Update()
    {
        if (_targetInputField != null && gameObject.activeSelf)
        {
            _cursorBlinkTimer += Time.deltaTime;
            if (_cursorBlinkTimer >= CURSOR_BLINK_RATE)
            {
                _cursorBlinkTimer = 0f;
                _cursorVisible = !_cursorVisible;
                UpdatePreview();
            }
        }
    }

    public void Show(TMP_InputField inputField)
    {
        _targetInputField = inputField;
        _isKeyboardVisibleOnStart = true;

        EnsureBuilt();

        gameObject.SetActive(true);

        if (_targetInputField != null)
        {
            _targetInputField.ActivateInputField();
            _targetInputField.Select();
            _targetInputField.caretPosition = _targetInputField.text.Length;
            _targetInputField.selectionAnchorPosition = _targetInputField.text.Length;
            _targetInputField.selectionFocusPosition = _targetInputField.text.Length;
        }

        ResetCursorBlink();
        UpdatePreview();
        PositionKeyboardNearInput(inputField);
    }

    public void Hide()
    {
        _targetInputField = null;
        gameObject.SetActive(false);
        ResetShift();
        _currentLayout = KeyboardLayout.Letters;
    }

    public bool IsVisible => gameObject.activeSelf;
    public TMP_InputField TargetInputField => _targetInputField;

    void PositionKeyboardNearInput(TMP_InputField inputField)
    {
        if (inputField == null) return;

        RectTransform inputRT = inputField.GetComponent<RectTransform>();
        RectTransform keyboardRT = GetComponent<RectTransform>();

        if (inputRT == null || keyboardRT == null) return;

        Canvas inputCanvas = inputField.GetComponentInParent<Canvas>();
        if (inputCanvas == null) return;

        Vector3 inputWorldPos = inputRT.position;
        float keyboardHeight = keyboardRT.rect.height * keyboardRT.lossyScale.y;

        Vector3 keyboardPos = inputWorldPos;
        keyboardPos.y -= inputRT.rect.height * inputRT.lossyScale.y / 2f + keyboardHeight / 2f + 50f;

        transform.position = keyboardPos;
    }

    void BuildKeyboard()
    {
        // Calculate total keyboard size - Mobile layout (10 keys wide)
        float unit = keyWidth + keySpacing;

        // 10 keys wide for main rows
        float totalWidth = 10f * unit + keySpacing;

        // 5 rows: numbers, qwerty, asdf, shift row, bottom row
        float totalHeight = 5 * (keyHeight + keySpacing) + keySpacing;

        if (showPreview)
        {
            totalHeight += keyHeight * 0.6f + keySpacing;
        }

        // Setup RectTransform
        RectTransform rt = gameObject.GetComponent<RectTransform>();
        if (rt == null)
            rt = gameObject.AddComponent<RectTransform>();

        rt.sizeDelta = new Vector2(totalWidth, totalHeight) * keyboardScale;

        // Add background
        CreateBackground(totalWidth, totalHeight);

        // Create container for keys
        _keyboardContainer = new GameObject("KeyContainer");
        _keyboardContainer.transform.SetParent(transform, false);
        RectTransform containerRT = _keyboardContainer.AddComponent<RectTransform>();
        containerRT.anchorMin = Vector2.zero;
        containerRT.anchorMax = Vector2.one;
        containerRT.offsetMin = Vector2.zero;
        containerRT.offsetMax = Vector2.zero;

        float currentY = totalHeight / 2f - keySpacing - keyHeight / 2f;

        // Preview text row (optional)
        if (showPreview)
        {
            CreatePreviewRow(_keyboardContainer.transform, currentY, totalWidth);
            currentY -= keyHeight * 0.6f + keySpacing;
        }

        // Build initial layout
        RebuildKeys();
    }

    void ClearKeys()
    {
        foreach (var key in _allKeys)
        {
            if (key != null)
                Destroy(key);
        }
        _allKeys.Clear();
        _shiftKey = null;
        _layoutSwitchKey = null;
        _symbolSwitchKey = null;
    }

    void RebuildKeys()
    {
        ClearKeys();

        float unit = keyWidth + keySpacing;
        float totalWidth = 10f * unit + keySpacing;
        float totalHeight = 5 * (keyHeight + keySpacing) + keySpacing;

        if (showPreview)
        {
            totalHeight += keyHeight * 0.6f + keySpacing;
        }

        float startY = totalHeight / 2f - keySpacing - keyHeight / 2f;

        if (showPreview)
        {
            startY -= keyHeight * 0.6f + keySpacing;
        }

        float currentY = startY;

        if (_currentLayout == KeyboardLayout.Letters)
        {
            BuildLettersLayout(_keyboardContainer.transform, currentY, unit, totalWidth);
        }
        else if (_currentLayout == KeyboardLayout.Symbols)
        {
            BuildSymbolsLayout(_keyboardContainer.transform, currentY, unit, totalWidth);
        }
        else
        {
            BuildMoreSymbolsLayout(_keyboardContainer.transform, currentY, unit, totalWidth);
        }
    }

    void BuildLettersLayout(Transform parent, float startY, float unit, float totalWidth)
    {
        float currentY = startY;
        float startX = -totalWidth / 2f + keySpacing + keyWidth / 2f;

        // Row 0: Numbers (or shifted symbols)
        string[] row0 = _isShiftActive ? SHIFTED_ROW_0 : LETTERS_ROW_0;
        CreateKeyRow(parent, row0, startX, currentY, unit, false);
        currentY -= keyHeight + keySpacing;

        // Row 1: QWERTY
        CreateKeyRow(parent, LETTERS_ROW_1, startX, currentY, unit, true);
        currentY -= keyHeight + keySpacing;

        // Row 2: ASDF (9 keys, slightly offset)
        float row2Offset = unit * 0.5f;
        CreateKeyRow(parent, LETTERS_ROW_2, startX + row2Offset, currentY, unit, true);
        currentY -= keyHeight + keySpacing;

        // Row 3: Shift + ZXCV + Backspace
        float shiftWidth = keyWidth * 1.4f;
        float x = startX;

        // Shift key
        _shiftKey = CreateSpecialKey(parent, "⇧", x + (shiftWidth - keyWidth) / 2f, currentY, shiftWidth, keyHeight,
            _isShiftActive ? accentColor : specialKeyColor, () => ToggleShift());
        _allKeys.Add(_shiftKey);
        x += shiftWidth + keySpacing;

        // Letter keys
        foreach (string key in LETTERS_ROW_3)
        {
            string displayKey = (_isShiftActive || _isCapsLock) ? key.ToUpper() : key;
            var keyObj = CreateKey(parent, displayKey, x, currentY, keyWidth, keyHeight, keyColor, key);
            _allKeys.Add(keyObj);
            x += unit;
        }

        // Backspace
        float backspaceWidth = keyWidth * 1.4f;
        var backKey = CreateSpecialKey(parent, "⌫", x + (backspaceWidth - keyWidth) / 2f, currentY, backspaceWidth, keyHeight,
            specialKeyColor, () => OnBackspace());
        _allKeys.Add(backKey);

        currentY -= keyHeight + keySpacing;

        // Bottom row: ?123 / emoji [spacebar] . Enter
        x = startX;
        float modKeyWidth = keyWidth * 1.2f;

        // ?123 key (switch to symbols)
        _layoutSwitchKey = CreateSpecialKey(parent, "?123", x + (modKeyWidth - keyWidth) / 2f, currentY, modKeyWidth, keyHeight,
            specialKeyColor, () => SwitchToLayout(KeyboardLayout.Symbols));
        _allKeys.Add(_layoutSwitchKey);
        x += modKeyWidth + keySpacing;

        // Slash key
        var slashKey = CreateKey(parent, "/", x, currentY, keyWidth, keyHeight, specialKeyColor, "/");
        _allKeys.Add(slashKey);
        x += unit;

        // Emoji key (placeholder - shows smiley)
        var emojiKey = CreateSpecialKey(parent, "☺", x, currentY, keyWidth, keyHeight, specialKeyColor, null);
        _allKeys.Add(emojiKey);
        x += unit;

        // Spacebar
        float spaceWidth = keyWidth * 4f;
        var spaceKey = CreateSpecialKey(parent, "", x + (spaceWidth - keyWidth) / 2f, currentY, spaceWidth, keyHeight,
            keyColor, () => OnKeyPress(" "));
        _allKeys.Add(spaceKey);
        x += spaceWidth + keySpacing;

        // Period key
        var periodKey = CreateKey(parent, ".", x, currentY, keyWidth, keyHeight, specialKeyColor, ".");
        _allKeys.Add(periodKey);
        x += unit;

        // Enter key
        float enterWidth = keyWidth * 1.3f;
        var enterKey = CreateSpecialKey(parent, "→", x + (enterWidth - keyWidth) / 2f, currentY, enterWidth, keyHeight,
            accentColor, () => OnEnter());
        _allKeys.Add(enterKey);
    }

    void BuildSymbolsLayout(Transform parent, float startY, float unit, float totalWidth)
    {
        float currentY = startY;
        float startX = -totalWidth / 2f + keySpacing + keyWidth / 2f;

        // 4 rows instead of 5, so make keys taller (5/4 = 1.25)
        float tallKeyHeight = keyHeight * 1.25f;
        float tallUnit = tallKeyHeight + keySpacing;

        // Row 0: Numbers
        CreateKeyRowWithHeight(parent, SYMBOLS_ROW_0, startX, currentY, unit, tallKeyHeight);
        currentY -= tallUnit;

        // Row 1: Symbols @#₫_&-+()/
        CreateKeyRowWithHeight(parent, SYMBOLS_ROW_1, startX, currentY, unit, tallKeyHeight);
        currentY -= tallUnit;

        // Row 2: =\< + symbols + backspace
        float switchWidth = keyWidth * 1.4f;
        float x = startX;

        // =\< key (switch to more symbols)
        _symbolSwitchKey = CreateSpecialKey(parent, "=\\<", x + (switchWidth - keyWidth) / 2f, currentY, switchWidth, tallKeyHeight,
            specialKeyColor, () => SwitchToLayout(KeyboardLayout.MoreSymbols));
        _allKeys.Add(_symbolSwitchKey);
        x += switchWidth + keySpacing;

        // Symbol keys
        foreach (string key in SYMBOLS_ROW_2)
        {
            var keyObj = CreateKeyWithHeight(parent, key, x, currentY, keyWidth, tallKeyHeight, keyColor, key);
            _allKeys.Add(keyObj);
            x += unit;
        }

        // Backspace
        float backspaceWidth = keyWidth * 1.4f;
        var backKey = CreateSpecialKey(parent, "⌫", x + (backspaceWidth - keyWidth) / 2f, currentY, backspaceWidth, tallKeyHeight,
            specialKeyColor, () => OnBackspace());
        _allKeys.Add(backKey);

        currentY -= tallUnit;

        // Bottom row: ABC , [spacebar] . Enter
        x = startX;
        float modKeyWidth = keyWidth * 1.2f;

        // ABC key (switch to letters)
        _layoutSwitchKey = CreateSpecialKey(parent, "ABC", x + (modKeyWidth - keyWidth) / 2f, currentY, modKeyWidth, tallKeyHeight,
            specialKeyColor, () => SwitchToLayout(KeyboardLayout.Letters));
        _allKeys.Add(_layoutSwitchKey);
        x += modKeyWidth + keySpacing;

        // Comma key
        var commaKey = CreateKeyWithHeight(parent, ",", x, currentY, keyWidth, tallKeyHeight, specialKeyColor, ",");
        _allKeys.Add(commaKey);
        x += unit;

        // Spacebar (extended - 5 units instead of 4)
        float spaceWidth = keyWidth * 5f;
        var spaceKey = CreateSpecialKey(parent, "", x + (spaceWidth - keyWidth) / 2f, currentY, spaceWidth, tallKeyHeight,
            keyColor, () => OnKeyPress(" "));
        _allKeys.Add(spaceKey);
        x += spaceWidth + keySpacing;

        // Period key
        var periodKey = CreateKeyWithHeight(parent, ".", x, currentY, keyWidth, tallKeyHeight, specialKeyColor, ".");
        _allKeys.Add(periodKey);
        x += unit;

        // Enter key
        float enterWidth = keyWidth * 1.3f;
        var enterKey = CreateSpecialKey(parent, "→", x + (enterWidth - keyWidth) / 2f, currentY, enterWidth, tallKeyHeight,
            accentColor, () => OnEnter());
        _allKeys.Add(enterKey);
    }

    void BuildMoreSymbolsLayout(Transform parent, float startY, float unit, float totalWidth)
    {
        float currentY = startY;
        float startX = -totalWidth / 2f + keySpacing + keyWidth / 2f;

        // 4 rows instead of 5, so make keys taller (5/4 = 1.25)
        float tallKeyHeight = keyHeight * 1.25f;
        float tallUnit = tallKeyHeight + keySpacing;

        // Row 0: More symbols ~`|•√π÷×§△
        CreateKeyRowWithHeight(parent, MORE_SYMBOLS_ROW_0, startX, currentY, unit, tallKeyHeight);
        currentY -= tallUnit;

        // Row 1: Currency and symbols £€$¢^°={}\
        CreateKeyRowWithHeight(parent, MORE_SYMBOLS_ROW_1, startX, currentY, unit, tallKeyHeight);
        currentY -= tallUnit;

        // Row 2: ?123 + more symbols + backspace
        float switchWidth = keyWidth * 1.4f;
        float x = startX;

        // ?123 key (switch to symbols)
        _symbolSwitchKey = CreateSpecialKey(parent, "?123", x + (switchWidth - keyWidth) / 2f, currentY, switchWidth, tallKeyHeight,
            specialKeyColor, () => SwitchToLayout(KeyboardLayout.Symbols));
        _allKeys.Add(_symbolSwitchKey);
        x += switchWidth + keySpacing;

        // Symbol keys
        foreach (string key in MORE_SYMBOLS_ROW_2)
        {
            var keyObj = CreateKeyWithHeight(parent, key, x, currentY, keyWidth, tallKeyHeight, keyColor, key);
            _allKeys.Add(keyObj);
            x += unit;
        }

        // Backspace
        float backspaceWidth = keyWidth * 1.4f;
        var backKey = CreateSpecialKey(parent, "⌫", x + (backspaceWidth - keyWidth) / 2f, currentY, backspaceWidth, tallKeyHeight,
            specialKeyColor, () => OnBackspace());
        _allKeys.Add(backKey);

        currentY -= tallUnit;

        // Bottom row: ABC < [spacebar] > Enter
        x = startX;
        float modKeyWidth = keyWidth * 1.2f;

        // ABC key (switch to letters)
        _layoutSwitchKey = CreateSpecialKey(parent, "ABC", x + (modKeyWidth - keyWidth) / 2f, currentY, modKeyWidth, tallKeyHeight,
            specialKeyColor, () => SwitchToLayout(KeyboardLayout.Letters));
        _allKeys.Add(_layoutSwitchKey);
        x += modKeyWidth + keySpacing;

        // < key
        var ltKey = CreateKeyWithHeight(parent, "<", x, currentY, keyWidth, tallKeyHeight, specialKeyColor, "<");
        _allKeys.Add(ltKey);
        x += unit;

        // Spacebar (extended - 5 units instead of 4)
        float spaceWidth = keyWidth * 5f;
        var spaceKey = CreateSpecialKey(parent, "", x + (spaceWidth - keyWidth) / 2f, currentY, spaceWidth, tallKeyHeight,
            keyColor, () => OnKeyPress(" "));
        _allKeys.Add(spaceKey);
        x += spaceWidth + keySpacing;

        // > key
        var gtKey = CreateKeyWithHeight(parent, ">", x, currentY, keyWidth, tallKeyHeight, specialKeyColor, ">");
        _allKeys.Add(gtKey);
        x += unit;

        // Enter key
        float enterWidth = keyWidth * 1.3f;
        var enterKey = CreateSpecialKey(parent, "→", x + (enterWidth - keyWidth) / 2f, currentY, enterWidth, tallKeyHeight,
            accentColor, () => OnEnter());
        _allKeys.Add(enterKey);
    }

    void CreateKeyRow(Transform parent, string[] keys, float startX, float y, float unit, bool isLetter)
    {
        float x = startX;
        foreach (string key in keys)
        {
            string displayKey = key;
            if (isLetter && (_isShiftActive || _isCapsLock))
            {
                displayKey = key.ToUpper();
            }
            var keyObj = CreateKey(parent, displayKey, x, y, keyWidth, keyHeight, keyColor, key);
            _allKeys.Add(keyObj);
            x += unit;
        }
    }

    void CreateKeyRowWithHeight(Transform parent, string[] keys, float startX, float y, float unit, float height)
    {
        float x = startX;
        foreach (string key in keys)
        {
            var keyObj = CreateKeyWithHeight(parent, key, x, y, keyWidth, height, keyColor, key);
            _allKeys.Add(keyObj);
            x += unit;
        }
    }

    GameObject CreateKeyWithHeight(Transform parent, string displayKey, float x, float y, float width, float height, Color color, string outputKey)
    {
        var config = new VRButtonFactory.ButtonConfig
        {
            label = displayKey,
            themeColor = color,
            width = width,
            height = height,
            fontSize = 32,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.9f,
            cornerRadius = 0.12f,
            borderWidth = 0.0f,
            popAmount = 0.015f
        };

        string keyValue = outputKey;
        GameObject btn = VRButtonFactory.CreateButton(parent, config, () => OnKeyPress(keyValue));

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);

        return btn;
    }

    void CreateBackground(float width, float height)
    {
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(transform, false);
        RectTransform rt = bg.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-15, -15);
        rt.offsetMax = new Vector2(15, 15);

        Image img = bg.AddComponent<Image>();
        img.raycastTarget = true;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            float aspect = width / height;
            mat.SetFloat("_CornerRadius", 0.04f);
            mat.SetFloat("_EdgePadding", 0.02f);
            mat.SetFloat("_Aspect", aspect);
            mat.SetColor("_ColorA", backgroundColor);
            mat.SetColor("_ColorB", new Color(backgroundColor.r * 0.7f, backgroundColor.g * 0.7f, backgroundColor.b * 0.7f, backgroundColor.a));
            mat.SetFloat("_GlassAlpha", 0.98f);
            img.material = mat;
            img.color = Color.white;
        }
        else
        {
            img.color = backgroundColor;
        }

        BoxCollider col = bg.AddComponent<BoxCollider>();
        col.size = new Vector3(width + 30, height + 30, 0.1f);
        col.center = new Vector3(0, 0, -0.05f);

        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) bg.layer = vrLayer;
    }

    void CreatePreviewRow(Transform parent, float y, float totalWidth)
    {
        _previewText = new GameObject("PreviewText");
        _previewText.transform.SetParent(parent, false);

        RectTransform rt = _previewText.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, y);
        rt.sizeDelta = new Vector2(totalWidth - 30, keyHeight * 0.5f);

        Image bgImg = _previewText.AddComponent<Image>();
        bgImg.color = new Color(0.18f, 0.19f, 0.22f, 0.6f);
        bgImg.raycastTarget = false;

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(_previewText.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(15, 0);
        textRT.offsetMax = new Vector2(-15, 0);

        _previewTMP = textObj.AddComponent<TextMeshProUGUI>();
        _previewTMP.fontSize = 28;
        _previewTMP.color = Color.white;
        _previewTMP.alignment = TextAlignmentOptions.Left;
        _previewTMP.verticalAlignment = VerticalAlignmentOptions.Middle;
        _previewTMP.raycastTarget = false;
        _previewTMP.overflowMode = TextOverflowModes.Ellipsis;
        if (customFont != null) _previewTMP.font = customFont;
    }

    GameObject CreateKey(Transform parent, string displayKey, float x, float y, float width, float height, Color color, string outputKey)
    {
        var config = new VRButtonFactory.ButtonConfig
        {
            label = displayKey,
            themeColor = color,
            width = width,
            height = height,
            fontSize = 32,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.9f,
            cornerRadius = 0.12f,
            borderWidth = 0.0f,
            popAmount = 0.015f
        };

        string keyValue = outputKey;
        GameObject btn = VRButtonFactory.CreateButton(parent, config, () => OnKeyPress(keyValue));

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);

        return btn;
    }

    GameObject CreateSpecialKey(Transform parent, string label, float x, float y, float width, float height,
        Color color, Action onClick)
    {
        var config = new VRButtonFactory.ButtonConfig
        {
            label = label,
            themeColor = color,
            width = width,
            height = height,
            fontSize = label.Contains("\n") ? 20 : 26,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.9f,
            cornerRadius = 0.12f,
            borderWidth = 0.0f,
            popAmount = 0.015f
        };

        GameObject btn = VRButtonFactory.CreateButton(parent, config, () => onClick?.Invoke());

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);

        return btn;
    }

    void OnKeyPress(string key)
    {
        string outputKey = key;

        // Handle shift/caps for letters
        if (key.Length == 1 && char.IsLetter(key[0]))
        {
            if (_isShiftActive || _isCapsLock)
            {
                outputKey = key.ToUpper();
            }
        }

        // Send to input field
        if (_targetInputField != null)
        {
            int caretPos = _targetInputField.caretPosition;
            string currentText = _targetInputField.text;

            _targetInputField.text = currentText.Insert(caretPos, outputKey);
            _targetInputField.caretPosition = caretPos + outputKey.Length;

            ResetCursorBlink();
            UpdatePreview();
        }

        OnKeyPressed?.Invoke(outputKey);

        // Auto-release shift (but not caps lock)
        if (_isShiftActive && !_isCapsLock && _currentLayout == KeyboardLayout.Letters)
        {
            _isShiftActive = false;
            RebuildKeys();
        }
    }

    void OnBackspace()
    {
        if (_targetInputField != null && _targetInputField.text.Length > 0)
        {
            int caretPos = _targetInputField.caretPosition;
            if (caretPos > 0)
            {
                string currentText = _targetInputField.text;
                _targetInputField.text = currentText.Remove(caretPos - 1, 1);
                _targetInputField.caretPosition = caretPos - 1;
                ResetCursorBlink();
                UpdatePreview();
            }
        }

        OnBackspacePressed?.Invoke();
    }

    void OnEnter()
    {
        if (_targetInputField != null)
        {
            _targetInputField.onEndEdit?.Invoke(_targetInputField.text);
        }

        OnEnterPressed?.Invoke();
        Hide();
    }

    void ToggleShift()
    {
        // Double-tap for caps lock
        if (_isShiftActive && !_isCapsLock)
        {
            _isCapsLock = true;
        }
        else if (_isCapsLock)
        {
            _isCapsLock = false;
            _isShiftActive = false;
        }
        else
        {
            _isShiftActive = true;
        }

        RebuildKeys();
    }

    void SwitchToLayout(KeyboardLayout layout)
    {
        _currentLayout = layout;
        _isShiftActive = false;
        _isCapsLock = false;
        RebuildKeys();
    }

    void ResetShift()
    {
        _isShiftActive = false;
        _isCapsLock = false;
    }

    void UpdatePreview()
    {
        if (_previewTMP != null && _targetInputField != null)
        {
            string text = _targetInputField.text;
            int caretPos = _targetInputField.caretPosition;

            string cursor = _cursorVisible ? "|" : " ";

            if (caretPos >= text.Length)
            {
                _previewTMP.text = text + cursor;
            }
            else
            {
                _previewTMP.text = text.Substring(0, caretPos) + cursor + text.Substring(caretPos);
            }
        }
    }

    void ResetCursorBlink()
    {
        _cursorVisible = true;
        _cursorBlinkTimer = 0f;
    }

    /// <summary>
    /// Static method to show mobile keyboard for an input field
    /// </summary>
    public static void ShowForInput(TMP_InputField inputField, Transform keyboardParent = null)
    {
        if (_instance == null)
        {
            GameObject keyboardObj = new GameObject("VRMobileKeyboard");

            if (keyboardParent != null)
            {
                keyboardObj.transform.SetParent(keyboardParent, false);
            }

            Canvas canvas = keyboardObj.GetComponentInParent<Canvas>();
            if (canvas == null && keyboardParent != null)
            {
                canvas = keyboardParent.GetComponentInParent<Canvas>();
            }

            if (canvas != null)
            {
                keyboardObj.transform.SetParent(canvas.transform, false);
            }

            _instance = keyboardObj.AddComponent<VRMobileKeyboard>();

            var remoteMenu = inputField.GetComponentInParent<VRRemoteMenu>();
            if (remoteMenu != null)
            {
                _instance.themeColor = remoteMenu.themeColor;
                _instance.accentColor = remoteMenu.accentColor;
                _instance.customFont = remoteMenu.customFont;
            }
        }

        _instance.Show(inputField);
    }

    /// <summary>
    /// Static method to hide mobile keyboard
    /// </summary>
    public static void HideKeyboard()
    {
        if (_instance != null)
        {
            _instance.Hide();
        }
    }
}
