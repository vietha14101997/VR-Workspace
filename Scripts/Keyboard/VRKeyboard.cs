using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using Random = UnityEngine.Random;

/// <summary>
/// Virtual Keyboard for VR environment.
/// Displays a floating keyboard that can be used to input text into TMP_InputField.
/// Uses VRButtonFactory for consistent styling with other VR UI components.
/// Positions relative to primary VRMenuFrame with VRMenuFrame-style glass background.
/// </summary>
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(GraphicRaycaster))]
public class VRKeyboard : MonoBehaviour
{
    [Header("Theme")]
    public Color themeColor = new Color(0.0f, 0.9f, 1.0f);
    public Color accentColor = new Color(0.8f, 0.4f, 1.0f);
    public Color specialKeyColor = new Color(0.5f, 0.5f, 0.6f);
    public TMP_FontAsset customFont;

    [Header("Layout")]
    public float keyWidth = 80f;
    public float keyHeight = 80f;
    public float keySpacing = 8f;
    public float specialKeyWidthMultiplier = 1.5f;
    public float spaceBarWidthMultiplier = 5f;

    [Header("Settings")]
    public bool showPreview = true;
    public float keyboardScale = 1f;

    [Header("Position Tracking")]
    [Tooltip("If true, keyboard will auto-position relative to the primary VRMenuFrame")]
    public bool followPrimaryFrame = true;
    [Tooltip("Multiplier for spacing below taskbar (1.5 = 150% of keyboard height)")]
    public float spacingMultiplier = 1.5f;
    [Tooltip("Width ratio relative to VRMenuFrame (0.667 = 2/3)")]
    public float widthRatioToFrame = 0.667f;

    [Header("Initial Orientation")]
    [Tooltip("If true, keyboard will face the camera on show (like VRTaskbar)")]
    public bool faceOnInit = true;

    [Header("Glassmorphism")]
    [Tooltip("Enable glassmorphism blur effect")]
    public bool enableGlassmorphism = true;
    [Range(0, 40)]
    public float blurIntensity = 2f;
    [Range(1, 8)]
    public int blurQuality = 3;
    [Range(0, 1)]
    public float glassOpacity = 0f;
    [Range(0, 1)]
    public float tintStrength = 0.1f;
    [Range(0, 0.5f)]
    public float innerGlow = 0f;
    [Range(0.9f, 1.3f)]
    public float brightness = 1f;
    [Range(0.5f, 1f)]
    public float saturation = 1f;
    [Range(0f, 0.1f)]
    public float glowExpansion = 0.02f;

    // Scale factor: matches VRMenuFrame pixel density (1.6m / 1920px)
    private const float PixelToMeter = 1.6f / 1920f;

    // Internal sprites
    private Sprite _pixelSprite;
    private Sprite _roundedMaskSprite;

    // Canvas components
    public Canvas Canvas { get; private set; }
    public RectTransform CanvasRect { get; private set; }

    // Events
    public event Action<string> OnKeyPressed;
    public event Action OnBackspacePressed;
    public event Action OnEnterPressed;
    public event Action OnClosePressed;

    // Current target input field
    private TMP_InputField _targetInputField;
    private bool _isShiftActive = false;
    private bool _isCapsLock = false;

    // UI References
    private GameObject _keyboardContainer;
    private GameObject _previewText;
    private TextMeshProUGUI _previewTMP;
    private List<GameObject> _letterKeys = new List<GameObject>();
    private GameObject _shiftKey;
    private GameObject _capsKey;

    // Cursor blinking
    private bool _cursorVisible = true;
    private float _cursorBlinkTimer = 0f;
    private const float CURSOR_BLINK_RATE = 0.5f;

    // Keyboard layouts - Full keyboard style
    private static readonly string[] ROW_FUNCTION = { "Esc", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" };
    private static readonly string[] ROW_NUMBERS = { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };
    private static readonly string[] ROW_1 = { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p", "[", "]", "\\" };
    private static readonly string[] ROW_2 = { "a", "s", "d", "f", "g", "h", "j", "k", "l", ";", "'" };
    private static readonly string[] ROW_3 = { "z", "x", "c", "v", "b", "n", "m", ",", ".", "/" };

    // Shift symbols mapping
    private static readonly Dictionary<string, string> SHIFT_SYMBOLS = new Dictionary<string, string>
    {
        {"`", "~"}, {"1", "!"}, {"2", "@"}, {"3", "#"}, {"4", "$"}, {"5", "%"},
        {"6", "^"}, {"7", "&"}, {"8", "*"}, {"9", "("}, {"0", ")"},
        {"-", "_"}, {"=", "+"}, {"[", "{"}, {"]", "}"}, {"\\", "|"},
        {";", ":"}, {"'", "\""}, {",", "<"}, {".", ">"}, {"/", "?"}
    };

    private static VRKeyboard _instance;
    public static VRKeyboard Instance => _instance;

    private bool _isBuilt = false;
    private bool _isKeyboardVisibleOnStart = false;

    // Calculated logical dimensions
    private float _logicalWidth;
    private float _logicalHeight;

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
        // Setup Canvas
        Canvas = GetComponent<Canvas>();
        CanvasRect = GetComponent<RectTransform>();

        if (Canvas == null)
        {
            Canvas = gameObject.AddComponent<Canvas>();
        }
        Canvas.renderMode = RenderMode.WorldSpace;

        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        EnsureBuilt();
        if (!_isKeyboardVisibleOnStart)
        {
            gameObject.SetActive(false); // Hidden by default
        }
    }

    /// <summary>
    /// Ensure keyboard is built (can be called before Start)
    /// </summary>
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
        // Cursor blinking
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

    void LateUpdate()
    {
        if (gameObject.activeSelf)
        {
            UpdatePositionRelativeToPrimary();
        }
    }

    /// <summary>
    /// Orient keyboard to face the camera (like VRTaskbar).
    /// </summary>
    void OrientTowardsCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;

        Vector3 toCamera = cam.transform.position - transform.position;
        if (toCamera.sqrMagnitude < 1e-6f) return;

        transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
    }

    /// <summary>
    /// Updates keyboard position to stay below VRTaskbar (which is below VRMenuFrame).
    /// posX = VRMenuFrame.posX (centered)
    /// </summary>
    void UpdatePositionRelativeToPrimary()
    {
        if (!followPrimaryFrame) return;

        VRMenuFrame primary = VRMenuFrame.PrimaryInstance;
        if (primary == null) return;

        // Find VRTaskbar to position below it
        VRTaskbar taskbar = FindObjectOfType<VRTaskbar>();

        Vector3 primaryPos = primary.transform.position;
        float primaryHalfHeight = primary.panelHeight / 2f;
        float keyboardHalfHeight = (_logicalHeight * PixelToMeter) / 2f;

        Vector3 newPos = transform.position;
        newPos.x = primaryPos.x; // Center X with primary frame

        if (taskbar != null && taskbar.gameObject.activeInHierarchy)
        {
            // Use taskbar's actual world position
            Vector3 taskbarPos = taskbar.transform.position;
            float taskbarHalfHeight = (taskbar.logicalHeight * PixelToMeter) / 2f;
            float taskbarBottomY = taskbarPos.y - taskbarHalfHeight;

            // Position keyboard below taskbar with small gap
            float gap = spacingMultiplier * 0.02f; // spacingMultiplier as gap in meters (1.5 = 3cm gap)
            newPos.y = taskbarBottomY - keyboardHalfHeight - gap;
            newPos.z = taskbarPos.z;
        }
        else
        {
            // Position below primary frame directly with small gap
            float gap = spacingMultiplier * 0.02f;
            newPos.y = primaryPos.y - primaryHalfHeight - keyboardHalfHeight - gap;
            newPos.z = primaryPos.z;
        }

        transform.position = newPos;

        // Match rotation with primary if faceOnInit is disabled
        if (!faceOnInit)
        {
            transform.rotation = primary.transform.rotation;
        }
    }

    /// <summary>
    /// Show keyboard and attach to an input field
    /// </summary>
    public void Show(TMP_InputField inputField)
    {
        _targetInputField = inputField;
        _isKeyboardVisibleOnStart = true; // Prevent hiding in Start()

        // Ensure keyboard is built before showing
        EnsureBuilt();

        gameObject.SetActive(true);

        // Activate the input field to show caret
        if (_targetInputField != null)
        {
            _targetInputField.ActivateInputField();
            _targetInputField.Select();

            // Move caret to end of text
            _targetInputField.caretPosition = _targetInputField.text.Length;
            _targetInputField.selectionAnchorPosition = _targetInputField.text.Length;
            _targetInputField.selectionFocusPosition = _targetInputField.text.Length;
        }

        ResetCursorBlink();
        UpdatePreview();

        // Position relative to VRMenuFrame/VRTaskbar
        UpdatePositionRelativeToPrimary();

        // Orient towards camera on show (like VRTaskbar)
        if (faceOnInit)
        {
            OrientTowardsCamera();
        }
    }

    /// <summary>
    /// Hide keyboard
    /// </summary>
    public void Hide()
    {
        _targetInputField = null;
        gameObject.SetActive(false);
        ResetShift();
    }

    /// <summary>
    /// Check if keyboard is currently visible
    /// </summary>
    public bool IsVisible => gameObject.activeSelf;

    /// <summary>
    /// Get current target input field
    /// </summary>
    public TMP_InputField TargetInputField => _targetInputField;

    void BuildKeyboard()
    {
        // Calculate dimensions based on VRMenuFrame (2/3 width)
        VRMenuFrame primary = VRMenuFrame.PrimaryInstance;
        float frameLogicalWidth = primary != null ? primary.logicalWidth : 1920f;

        // Keyboard logical width = 2/3 of VRMenuFrame
        _logicalWidth = frameLogicalWidth * widthRatioToFrame;

        // Calculate total keyboard size - Full keyboard layout
        // Standard key unit = keyWidth
        float unit = keyWidth + keySpacing;
        float functionKeyHeight = keyHeight * 0.7f;

        // Main keyboard: 15 units wide (including backspace 2u)
        // Nav column: 1.2 units
        float mainKeysWidth = 15f * unit;
        float navGap = keySpacing * 2;
        float navWidth = keyWidth * 1.2f;

        float totalWidth = mainKeysWidth + navGap + navWidth;
        float totalHeight = functionKeyHeight + keySpacing + 5 * (keyHeight + keySpacing);

        if (showPreview)
        {
            totalHeight += keyHeight * 0.6f + keySpacing;
        }

        // Scale keys to fit within logical width
        float scaleToFit = _logicalWidth / totalWidth;
        keyWidth *= scaleToFit;
        keyHeight *= scaleToFit;
        keySpacing *= scaleToFit;

        // Recalculate with scaled dimensions
        unit = keyWidth + keySpacing;
        functionKeyHeight = keyHeight * 0.7f;
        mainKeysWidth = 15f * unit;
        navGap = keySpacing * 2;
        navWidth = keyWidth * 1.2f;
        totalWidth = mainKeysWidth + navGap + navWidth;
        totalHeight = functionKeyHeight + keySpacing + 5 * (keyHeight + keySpacing);
        if (showPreview)
        {
            totalHeight += keyHeight * 0.6f + keySpacing;
        }

        _logicalWidth = totalWidth;
        _logicalHeight = totalHeight;

        // Setup RectTransform with VRMenuFrame-style scale
        CanvasRect = GetComponent<RectTransform>();
        if (CanvasRect == null)
            CanvasRect = gameObject.AddComponent<RectTransform>();

        CanvasRect.sizeDelta = new Vector2(_logicalWidth, _logicalHeight);
        CanvasRect.localScale = new Vector3(PixelToMeter, PixelToMeter, 1f);
        CanvasRect.localPosition = Vector3.zero;

        // Add VRMenuFrame-style glass background
        CreateGlassPanel(transform, _logicalWidth, _logicalHeight);

        // Create container for keys
        _keyboardContainer = new GameObject("KeyContainer");
        _keyboardContainer.transform.SetParent(transform, false);
        RectTransform containerRT = _keyboardContainer.AddComponent<RectTransform>();
        containerRT.anchorMin = Vector2.zero;
        containerRT.anchorMax = Vector2.one;
        containerRT.offsetMin = Vector2.zero;
        containerRT.offsetMax = Vector2.zero;

        // Starting positions
        float leftX = -totalWidth / 2f + keySpacing + keyWidth / 2f;
        float navX = totalWidth / 2f - navWidth / 2f - keySpacing;
        float currentY = totalHeight - keySpacing;

        // Preview text row (optional)
        if (showPreview)
        {
            currentY -= keyHeight * 0.6f;
            CreatePreviewRow(_keyboardContainer.transform, currentY, totalWidth);
            currentY -= keySpacing;
        }

        // Function key row (Esc, F1-F12)
        currentY -= functionKeyHeight;
        CreateFunctionRow(_keyboardContainer.transform, currentY, leftX, functionKeyHeight);
        currentY -= keySpacing;

        // Number row with Backspace + Home
        currentY -= keyHeight;
        CreateNumberRow(_keyboardContainer.transform, currentY, leftX);
        CreateNavKey(_keyboardContainer.transform, "Home", navX, currentY, navWidth);
        currentY -= keySpacing;

        // QWERTY row + PgUp
        currentY -= keyHeight;
        CreateQwertyRow(_keyboardContainer.transform, currentY, leftX);
        CreateNavKey(_keyboardContainer.transform, "PgUp", navX, currentY, navWidth);
        currentY -= keySpacing;

        // Home row (ASDF) + PgDn
        currentY -= keyHeight;
        CreateHomeRow(_keyboardContainer.transform, currentY, leftX);
        CreateNavKey(_keyboardContainer.transform, "PgDn", navX, currentY, navWidth);
        currentY -= keySpacing;

        // Shift row + End + Arrow Up
        currentY -= keyHeight;
        CreateShiftRow(_keyboardContainer.transform, currentY, leftX);
        CreateNavKey(_keyboardContainer.transform, "End", navX, currentY, navWidth);
        // Arrow Up - positioned above arrow cluster
        float arrowX = mainKeysWidth - 2f * unit + keyWidth / 2f - totalWidth / 2f;
        CreateArrowKey(_keyboardContainer.transform, "^", arrowX, currentY, () => MoveCursor(0, -1));
        currentY -= keySpacing;

        // Bottom row + Arrow keys (Left, Down, Right)
        currentY -= keyHeight;
        CreateBottomRow(_keyboardContainer.transform, currentY, leftX);
        // Arrow cluster - inverted T shape
        // Position: right side of keyboard, aligned with shift row arrow
        CreateArrowKey(_keyboardContainer.transform, "<", arrowX - unit, currentY, () => MoveCursor(-1, 0));
        CreateArrowKey(_keyboardContainer.transform, "v", arrowX, currentY, () => MoveCursor(0, 1));
        CreateArrowKey(_keyboardContainer.transform, ">", arrowX + unit, currentY, () => MoveCursor(1, 0));
    }

    void CreateNavKey(Transform parent, string label, float x, float y, float width)
    {
        CreateSpecialKey(parent, label, x, y, width, keyHeight, specialKeyColor, null);
    }

    void CreateArrowKey(Transform parent, string label, float x, float y, System.Action onClick)
    {
        CreateSpecialKey(parent, label, x, y, keyWidth, keyHeight, accentColor, onClick);
    }

    /// <summary>
    /// Create VRMenuFrame-style glass panel with glowing border
    /// </summary>
    void CreateGlassPanel(Transform parent, float w, float h)
    {
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);
        Image img = bgObj.AddComponent<Image>();

        img.type = Image.Type.Simple;
        img.sprite = GetPixelSprite();

        float expansion = glowExpansion;
        float edgePad = glowExpansion > 0 ? glowExpansion / (1f + 2f * glowExpansion) : 0f;
        float aspect = w / h;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);

            glassMat.SetFloat("_CornerRadius", 0.12f);
            glassMat.SetFloat("_EdgePadding", edgePad);
            glassMat.SetFloat("_Aspect", aspect);

            Color cyanGlass = new Color(0.35f, 0.9f, 1f, 0.15f);
            Color purpleGlass = new Color(0.75f, 0.45f, 1f, 0.22f);
            glassMat.SetColor("_ColorA", cyanGlass);
            glassMat.SetColor("_ColorB", purpleGlass);
            glassMat.SetFloat("_GradientOffset", 0f);
            glassMat.SetFloat("_GradientAngle", -10f);
            glassMat.SetFloat("_CyanRatio", 0.7f);
            glassMat.SetFloat("_GlassAlpha", 0.08f);
            glassMat.SetFloat("_FresnelPower", 2.2f);
            glassMat.SetFloat("_FresnelStrength", 0.12f);

            // Glassmorphism settings
            glassMat.SetFloat("_BlurEnabled", enableGlassmorphism ? 1f : 0f);
            glassMat.SetFloat("_BlurRadius", blurIntensity);
            glassMat.SetFloat("_BlurIterations", blurQuality);
            glassMat.SetFloat("_GlassOpacity", glassOpacity);
            glassMat.SetFloat("_TintStrength", tintStrength);
            glassMat.SetFloat("_InnerGlow", innerGlow);
            glassMat.SetFloat("_Brightness", brightness);
            glassMat.SetFloat("_Saturation", saturation);

            img.material = glassMat;
            img.color = Color.white;
        }
        else
        {
            img.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
            expansion = 0;
        }

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(-expansion, -expansion);
        rt.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localPosition = Vector3.zero;
        rt.SetAsFirstSibling();

        // Collider
        float expandedW = w * (1f + 2f * expansion);
        float expandedH = h * (1f + 2f * expansion);
        BoxCollider bgCol = bgObj.AddComponent<BoxCollider>();
        bgCol.size = new Vector3(expandedW, expandedH, 0.01f);
        bgCol.center = new Vector3(0, 0, 0.05f);

        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) bgObj.layer = vrLayer;

        CreateGlowingBorder(bgObj.transform, w, h, edgePad);
        CreateFloatingDataEffects(bgObj.transform, w, h);
    }

    /// <summary>
    /// Create VRMenuFrame-style glowing border
    /// </summary>
    void CreateGlowingBorder(Transform parent, float w, float h, float edgePad)
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

        float aspect = w / h;

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);

            glowMat.SetFloat("_StrokeEnabled", 0);
            glowMat.SetFloat("_BorderWidth", 0.02f);
            glowMat.SetFloat("_CornerRadius", 0.12f);
            glowMat.SetFloat("_EdgePadding", edgePad);
            glowMat.SetFloat("_Aspect", aspect);

            glowMat.SetFloat("_Layer1Width", 0.008f);
            glowMat.SetFloat("_Layer1Alpha", 1.5f);
            glowMat.SetFloat("_Layer2Width", 0.018f);
            glowMat.SetFloat("_Layer2Alpha", 1.0f);
            glowMat.SetFloat("_Layer3Width", 0.04f);
            glowMat.SetFloat("_Layer3Alpha", 0.6f);
            glowMat.SetFloat("_Layer4Width", 0.08f);
            glowMat.SetFloat("_Layer4Alpha", 0.3f);

            Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
            Color purpleColor = new Color(1f, 0.4f, 1f, 1f);
            glowMat.SetColor("_ColorA", cyanColor);
            glowMat.SetColor("_ColorB", purpleColor);
            glowMat.SetFloat("_GradientMode", 2f);
            glowMat.SetFloat("_GradientAngle", -10f);
            glowMat.SetFloat("_GlassAlpha", 0.02f);
            glowMat.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
            glowMat.SetFloat("_ShimmerSpeed", 0.1f);
            glowMat.SetFloat("_ShimmerIntensity", 0.2f);
            glowMat.SetFloat("_LightSize", 0.008f);
            glowMat.SetFloat("_LightGlow", 0.008f);

            borderImg.material = glowMat;
            borderImg.sprite = GetPixelSprite();
        }

        borderObj.transform.SetAsLastSibling();
    }

    /// <summary>
    /// Create floating data effects (like VRMenuFrame)
    /// </summary>
    void CreateFloatingDataEffects(Transform parent, float w, float h)
    {
        GameObject fxContainer = new GameObject("FX_DataStream");
        fxContainer.transform.SetParent(parent, false);
        RectTransform rt = fxContainer.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;

        float expansionPxW = w * glowExpansion;
        float expansionPxH = h * glowExpansion;
        float margin = 20f;

        rt.offsetMin = new Vector2(expansionPxW + margin, expansionPxH + margin);
        rt.offsetMax = new Vector2(-(expansionPxW + margin), -(expansionPxH + margin));

        Image maskImage = fxContainer.AddComponent<Image>();
        maskImage.sprite = GetRoundedMaskSprite();
        maskImage.type = Image.Type.Sliced;
        maskImage.color = Color.white;
        maskImage.raycastTarget = false;

        Mask mask = fxContainer.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        int particleCount = 15;
        for (int i = 0; i < particleCount; i++)
        {
            GameObject p = new GameObject($"Bit_{i}");
            p.transform.SetParent(fxContainer.transform, false);

            Image pImg = p.AddComponent<Image>();
            pImg.sprite = GetPixelSprite();

            bool cyanOrPurple = Random.value > 0.5f;
            Color baseCol = cyanOrPurple ? Color.cyan : new Color(0.8f, 0f, 1f);
            pImg.color = new Color(baseCol.r, baseCol.g, baseCol.b, Random.Range(0.1f, 0.4f));

            RectTransform pRT = p.GetComponent<RectTransform>();
            float size = Random.Range(8f, 50f);
            pRT.sizeDelta = new Vector2(size, size * Random.Range(0.2f, 1.0f));

            float startX = Random.Range(-w / 2f, w / 2f);
            float startY = Random.Range(-h / 2f, h / 2f);
            pRT.anchoredPosition = new Vector2(startX, startY);

            var anim = p.AddComponent<FloatingDataAnim>();
            anim.speed = Random.Range(8f, 30f);
            anim.range = new Vector2(w, h);
        }
    }

    Sprite GetPixelSprite()
    {
        if (_pixelSprite) return _pixelSprite;
        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }

    Sprite GetRoundedMaskSprite()
    {
        if (_roundedMaskSprite != null) return _roundedMaskSprite;

        int size = 128;
        int radius = 24;
        int border = radius;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float alpha = 1f;

                int cornerX = -1, cornerY = -1;
                if (x < radius && y < radius) { cornerX = radius; cornerY = radius; }
                else if (x >= size - radius && y < radius) { cornerX = size - radius - 1; cornerY = radius; }
                else if (x < radius && y >= size - radius) { cornerX = radius; cornerY = size - radius - 1; }
                else if (x >= size - radius && y >= size - radius) { cornerX = size - radius - 1; cornerY = size - radius - 1; }

                if (cornerX >= 0)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cornerX, cornerY));
                    alpha = Mathf.Clamp01(radius + 0.5f - dist);
                }

                colors[y * size + x] = new Color(1, 1, 1, alpha);
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        _roundedMaskSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        return _roundedMaskSprite;
    }

    void CreatePreviewRow(Transform parent, float y, float totalWidth)
    {
        _previewText = new GameObject("PreviewText");
        _previewText.transform.SetParent(parent, false);

        RectTransform rt = _previewText.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(0, y);
        rt.sizeDelta = new Vector2(totalWidth - 40, keyHeight * 0.6f);

        // Background for preview
        Image bgImg = _previewText.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.25f, 0.5f);
        bgImg.raycastTarget = false;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(_previewText.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(20, 0);
        textRT.offsetMax = new Vector2(-20, 0);

        _previewTMP = textObj.AddComponent<TextMeshProUGUI>();
        _previewTMP.fontSize = 32;
        _previewTMP.color = Color.white;
        _previewTMP.alignment = TextAlignmentOptions.Left;
        _previewTMP.verticalAlignment = VerticalAlignmentOptions.Middle;
        _previewTMP.raycastTarget = false;
        _previewTMP.overflowMode = TextOverflowModes.Ellipsis;
        if (customFont != null) _previewTMP.font = customFont;
    }

    void CreateFunctionRow(Transform parent, float y, float startX, float height)
    {
        float unit = keyWidth + keySpacing;
        float funcKeyWidth = keyWidth * 0.9f;
        float x = startX;

        // Esc key - closes keyboard
        CreateSpecialKey(parent, "Esc", x, y, funcKeyWidth, height, new Color(1f, 0.3f, 0.3f), () => OnClose());
        x += funcKeyWidth + keySpacing * 2;

        // F1-F12 in groups of 4
        for (int i = 1; i <= 12; i++)
        {
            CreateSpecialKey(parent, "F" + i, x, y, funcKeyWidth, height, specialKeyColor, null);
            x += funcKeyWidth + keySpacing;

            // Extra gap after F4 and F8
            if (i == 4 || i == 8) x += keySpacing * 2;
        }
    }

    void CreateNumberRow(Transform parent, float y, float startX)
    {
        float unit = keyWidth + keySpacing;
        float x = startX;

        // Number keys (` 1 2 3 4 5 6 7 8 9 0 - =)
        foreach (string key in ROW_NUMBERS)
        {
            GameObject keyObj = CreateKey(parent, key, x, y, keyWidth, keyHeight, themeColor);
            _letterKeys.Add(keyObj);
            x += unit;
        }

        // Backspace (2 units wide)
        float backspaceWidth = keyWidth * 2f;
        CreateSpecialKey(parent, "Back", x + backspaceWidth / 2f - keyWidth / 2f, y, backspaceWidth, keyHeight, accentColor,
            () => OnBackspace());
    }

    void CreateQwertyRow(Transform parent, float y, float startX)
    {
        float unit = keyWidth + keySpacing;
        float tabWidth = keyWidth * 1.5f;
        float x = startX;

        // Tab key (1.5 units)
        CreateSpecialKey(parent, "Tab", x + (tabWidth - keyWidth) / 2f, y, tabWidth, keyHeight, specialKeyColor,
            () => OnKeyPress("\t"));
        x += tabWidth + keySpacing;

        // QWERTY row keys
        foreach (string key in ROW_1)
        {
            GameObject keyObj = CreateKey(parent, key, x, y, keyWidth, keyHeight, themeColor);
            _letterKeys.Add(keyObj);
            x += unit;
        }
    }

    void CreateHomeRow(Transform parent, float y, float startX)
    {
        float unit = keyWidth + keySpacing;
        float capsWidth = keyWidth * 1.75f;
        float x = startX;

        // Caps Lock (1.75 units)
        _capsKey = CreateSpecialKey(parent, "Caps", x + (capsWidth - keyWidth) / 2f, y, capsWidth, keyHeight, specialKeyColor,
            () => ToggleCapsLock());
        x += capsWidth + keySpacing;

        // ASDF row keys
        foreach (string key in ROW_2)
        {
            GameObject keyObj = CreateKey(parent, key, x, y, keyWidth, keyHeight, themeColor);
            _letterKeys.Add(keyObj);
            x += unit;
        }

        // Enter key (2.25 units)
        float enterWidth = keyWidth * 2.25f;
        CreateSpecialKey(parent, "Enter", x + (enterWidth - keyWidth) / 2f, y, enterWidth, keyHeight, accentColor,
            () => OnEnter());
    }

    void CreateShiftRow(Transform parent, float y, float startX)
    {
        float unit = keyWidth + keySpacing;
        float shiftWidth = keyWidth * 2.25f;
        float x = startX;

        // Left Shift (2.25 units)
        _shiftKey = CreateSpecialKey(parent, "Shift", x + (shiftWidth - keyWidth) / 2f, y, shiftWidth, keyHeight, specialKeyColor,
            () => ToggleShift());
        UpdateShiftKeyVisual();
        x += shiftWidth + keySpacing;

        // ZXCV row keys
        foreach (string key in ROW_3)
        {
            GameObject keyObj = CreateKey(parent, key, x, y, keyWidth, keyHeight, themeColor);
            _letterKeys.Add(keyObj);
            x += unit;
        }
        // Note: Arrow Up is placed in BuildKeyboard now
    }

    void CreateBottomRow(Transform parent, float y, float startX)
    {
        float unit = keyWidth + keySpacing;
        float modWidth = keyWidth * 1.25f;
        float spaceWidth = keyWidth * 6.25f;
        float x = startX;

        // Ctrl (1.25 units)
        CreateSpecialKey(parent, "Ctrl", x + (modWidth - keyWidth) / 2f, y, modWidth, keyHeight, specialKeyColor, null);
        x += modWidth + keySpacing;

        // Win (1.25 units)
        CreateSpecialKey(parent, "Win", x + (modWidth - keyWidth) / 2f, y, modWidth, keyHeight, specialKeyColor, null);
        x += modWidth + keySpacing;

        // Alt (1.25 units)
        CreateSpecialKey(parent, "Alt", x + (modWidth - keyWidth) / 2f, y, modWidth, keyHeight, specialKeyColor, null);
        x += modWidth + keySpacing;

        // Space bar (6.25 units)
        CreateSpecialKey(parent, "", x + (spaceWidth - keyWidth) / 2f, y, spaceWidth, keyHeight, specialKeyColor,
            () => OnKeyPress(" "));
        // Note: Arrow keys are placed in BuildKeyboard now
    }

    GameObject CreateKey(Transform parent, string key, float x, float y, float width, float height, Color color)
    {
        var config = new VRButtonFactory.ButtonConfig
        {
            label = key,
            themeColor = color,
            width = width,
            height = height,
            fontSize = 36,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.15f,
            cornerRadius = 0.15f,
            borderWidth = 0.02f,
            popAmount = 0.02f
        };

        string keyValue = key;
        GameObject btn = VRButtonFactory.CreateButton(parent, config, () => OnKeyPress(keyValue));

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
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
            fontSize = 28,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.25f,
            cornerRadius = 0.12f,
            borderWidth = 0.03f,
            popAmount = 0.02f
        };

        GameObject btn = VRButtonFactory.CreateButton(parent, config, () => onClick?.Invoke());

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
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
        // Handle shift for symbols
        else if (_isShiftActive && SHIFT_SYMBOLS.ContainsKey(key))
        {
            outputKey = SHIFT_SYMBOLS[key];
        }

        // Send to input field
        if (_targetInputField != null)
        {
            int caretPos = _targetInputField.caretPosition;
            string currentText = _targetInputField.text;

            // Insert at caret position
            _targetInputField.text = currentText.Insert(caretPos, outputKey);
            _targetInputField.caretPosition = caretPos + outputKey.Length;

            ResetCursorBlink();
            UpdatePreview();
        }

        OnKeyPressed?.Invoke(outputKey);

        // Auto-release shift (but not caps lock)
        if (_isShiftActive && !_isCapsLock)
        {
            _isShiftActive = false;
            UpdateKeyLabels();
            UpdateShiftKeyVisual();
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
        // Trigger end edit on input field
        if (_targetInputField != null)
        {
            _targetInputField.onEndEdit?.Invoke(_targetInputField.text);
        }

        OnEnterPressed?.Invoke();
        Hide();
    }

    void OnClose()
    {
        OnClosePressed?.Invoke();
        Hide();
    }

    void MoveCursor(int horizontal, int vertical)
    {
        if (_targetInputField == null) return;

        int currentPos = _targetInputField.caretPosition;
        int textLength = _targetInputField.text.Length;

        // Horizontal movement: -1 = left, +1 = right
        if (horizontal != 0)
        {
            int newPos = currentPos + horizontal;
            newPos = Mathf.Clamp(newPos, 0, textLength);
            _targetInputField.caretPosition = newPos;
            _targetInputField.selectionAnchorPosition = newPos;
            _targetInputField.selectionFocusPosition = newPos;
        }

        // Vertical movement (for multi-line input fields)
        // For single-line, move to start/end
        if (vertical != 0)
        {
            if (vertical < 0) // Up = move to start
            {
                _targetInputField.caretPosition = 0;
                _targetInputField.selectionAnchorPosition = 0;
                _targetInputField.selectionFocusPosition = 0;
            }
            else // Down = move to end
            {
                _targetInputField.caretPosition = textLength;
                _targetInputField.selectionAnchorPosition = textLength;
                _targetInputField.selectionFocusPosition = textLength;
            }
        }

        ResetCursorBlink();
        UpdatePreview();
    }

    void ToggleShift()
    {
        _isShiftActive = !_isShiftActive;
        UpdateKeyLabels();
        UpdateShiftKeyVisual();
    }

    void ToggleCapsLock()
    {
        _isCapsLock = !_isCapsLock;
        _isShiftActive = _isCapsLock;
        UpdateKeyLabels();
        UpdateShiftKeyVisual();
        UpdateCapsKeyVisual();
    }

    void ResetShift()
    {
        _isShiftActive = false;
        _isCapsLock = false;
        UpdateKeyLabels();
        UpdateShiftKeyVisual();
        UpdateCapsKeyVisual();
    }

    void UpdateKeyLabels()
    {
        foreach (var keyObj in _letterKeys)
        {
            var tmp = keyObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                string currentLabel = tmp.text.ToLower();

                // Handle letters
                if (currentLabel.Length == 1 && char.IsLetter(currentLabel[0]))
                {
                    tmp.text = (_isShiftActive || _isCapsLock) ? currentLabel.ToUpper() : currentLabel;
                }
                // Handle shift symbols
                else if (_isShiftActive)
                {
                    if (SHIFT_SYMBOLS.ContainsKey(currentLabel))
                    {
                        tmp.text = SHIFT_SYMBOLS[currentLabel];
                    }
                }
                else
                {
                    // Reverse lookup for shift symbols
                    foreach (var pair in SHIFT_SYMBOLS)
                    {
                        if (pair.Value == currentLabel)
                        {
                            tmp.text = pair.Key;
                            break;
                        }
                    }
                }
            }
        }
    }

    void UpdateShiftKeyVisual()
    {
        if (_shiftKey == null) return;

        var border = _shiftKey.GetComponentInChildren<VRButtonRipple>();
        if (border != null && border.GetComponent<Image>()?.material != null)
        {
            var mat = border.GetComponent<Image>().material;
            mat.SetFloat("_BorderWidth", _isShiftActive ? 0.06f : 0.03f);
        }
    }

    void UpdateCapsKeyVisual()
    {
        if (_capsKey == null) return;

        var tmp = _capsKey.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.color = _isCapsLock ? accentColor : Color.white;
        }
    }

    void UpdatePreview()
    {
        if (_previewTMP != null && _targetInputField != null)
        {
            string text = _targetInputField.text;
            int caretPos = _targetInputField.caretPosition;

            // Insert cursor character at caret position
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
    /// Static method to show keyboard for an input field
    /// Creates keyboard instance if needed
    /// </summary>
    public static void ShowForInput(TMP_InputField inputField, Transform keyboardParent = null)
    {
        if (_instance == null)
        {
            // Create keyboard at root level - it's a World Space Canvas
            // and must not be parented to another Canvas
            GameObject keyboardObj = new GameObject("VRKeyboard");
            // No SetParent - keyboard stays at root for correct world positioning

            _instance = keyboardObj.AddComponent<VRKeyboard>();

            // Copy theme from input field's parent if available
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
    /// Static method to hide keyboard
    /// </summary>
    public static void HideKeyboard()
    {
        if (_instance != null)
        {
            _instance.Hide();
        }
    }
}
