using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

/// <summary>
/// Mobile-style Virtual Keyboard for VR environment.
/// Displays a compact mobile keyboard with multiple layouts (letters, symbols).
/// Positions relative to primary VRMenuFrame with VRMenuFrame-style glass background.
/// </summary>
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(GraphicRaycaster))]
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
    [Tooltip("Content margins inside keyboard frame (pixels)")]
    public float marginLeft = 40f;
    public float marginRight = 40f;
    public float marginTop = 30f;
    public float marginBottom = 50f;
    [Tooltip("Spacing between keys as ratio of key width (0.1 = 10%)")]
    [Range(0.05f, 0.2f)]
    public float keySpacingRatio = 0.1f;
    [Tooltip("Key height to width ratio (1.2 = 20% taller than wide)")]
    [Range(0.8f, 1.5f)]
    public float keyHeightRatio = 1.2f;
    [Tooltip("Font size for all keyboard keys")]
    public int keyFontSize = 36;

    [Header("Key Animation")]
    [Tooltip("Scale when key is pressed down")]
    [Range(0.8f, 1.0f)]
    public float keyDownScale = 0.92f;
    [Tooltip("Animation duration in seconds")]
    [Range(0.01f, 0.2f)]
    public float keyAnimDuration = 0.08f;

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
    [Tooltip("How much closer to camera (0 = same as taskbar, 1 = at camera)")]
    [Range(0f, 0.5f)]
    public float cameraProximity = 0.15f;

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
    private VRButtonAnimation _shiftKeyAnimation;
    private GameObject _layoutSwitchKey;
    private GameObject _symbolSwitchKey;

    // Key mapping for physical keyboard visual feedback
    private Dictionary<string, GameObject> _keyMap = new Dictionary<string, GameObject>();

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

    // Static reference to currently open keyboard (for VRGazeReticle to check)
    public static VRMobileKeyboard CurrentlyOpenKeyboard { get; private set; }

    private bool _isBuilt = false;
    private bool _isKeyboardVisibleOnStart = false;
    private bool _hasInitializedOrientation = false;

    // Calculated logical dimensions
    private float _logicalWidth;
    private float _logicalHeight;

    // Calculated key dimensions (based on container size)
    private float keyWidth;
    private float keyHeight;
    private float keySpacing;

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

        // Set lower sortingOrder so DropdownPanel (sortingOrder=100) renders in front
        Canvas.overrideSorting = true;
        Canvas.sortingOrder = 150;

        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        EnsureBuilt();

        // Set VirtualObjects layer for keyboard and all children (after building)
        SetLayerRecursive(gameObject, "VirtualObjects");

        if (!_isKeyboardVisibleOnStart)
        {
            gameObject.SetActive(false);
        }
    }

    void SetLayerRecursive(GameObject obj, string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer == -1) return;

        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layerName);
        }
    }

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
            // Cursor blinking
            _cursorBlinkTimer += Time.deltaTime;
            if (_cursorBlinkTimer >= CURSOR_BLINK_RATE)
            {
                _cursorBlinkTimer = 0f;
                _cursorVisible = !_cursorVisible;
                UpdatePreview();
            }

            // Handle physical keyboard input
            HandlePhysicalKeyboardInput();
        }
    }

    /// <summary>
    /// Process input from physical keyboard
    /// </summary>
    void HandlePhysicalKeyboardInput()
    {
        // Handle character input
        if (!string.IsNullOrEmpty(Input.inputString))
        {
            foreach (char c in Input.inputString)
            {
                if (c == '\b') // Backspace
                {
                    TriggerKeyVisualFeedback("Back");
                    OnBackspace();
                }
                else if (c == '\n' || c == '\r') // Enter
                {
                    TriggerKeyVisualFeedback("Enter");
                    OnEnter();
                }
                else if (!char.IsControl(c)) // Regular character
                {
                    TriggerKeyVisualFeedback(c.ToString());
                    OnKeyPress(c.ToString());
                }
            }
        }

        // Handle special keys not in inputString
        if (Input.GetKeyDown(KeyCode.Delete))
        {
            // Delete character after cursor
            if (_targetInputField != null)
            {
                int caretPos = _targetInputField.caretPosition;
                string text = _targetInputField.text;
                if (caretPos < text.Length)
                {
                    _targetInputField.text = text.Remove(caretPos, 1);
                    ResetCursorBlink();
                    UpdatePreview();
                }
            }
        }

        // Arrow keys for cursor movement
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            if (_targetInputField != null && _targetInputField.caretPosition > 0)
            {
                _targetInputField.caretPosition--;
                ResetCursorBlink();
                UpdatePreview();
            }
        }
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (_targetInputField != null && _targetInputField.caretPosition < _targetInputField.text.Length)
            {
                _targetInputField.caretPosition++;
                ResetCursorBlink();
                UpdatePreview();
            }
        }
        if (Input.GetKeyDown(KeyCode.Home))
        {
            if (_targetInputField != null)
            {
                _targetInputField.caretPosition = 0;
                ResetCursorBlink();
                UpdatePreview();
            }
        }
        if (Input.GetKeyDown(KeyCode.End))
        {
            if (_targetInputField != null)
            {
                _targetInputField.caretPosition = _targetInputField.text.Length;
                ResetCursorBlink();
                UpdatePreview();
            }
        }

        // Escape to close keyboard
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Hide();
        }
    }

    /// <summary>
    /// Trigger visual feedback (press animation) on a virtual key when physical key is pressed
    /// </summary>
    void TriggerKeyVisualFeedback(string key)
    {
        GameObject keyObj = null;

        // Try exact match first
        if (_keyMap.TryGetValue(key, out keyObj))
        {
            TriggerKeyAnimation(keyObj);
            return;
        }

        // Try lowercase/uppercase match
        if (_keyMap.TryGetValue(key.ToLower(), out keyObj))
        {
            TriggerKeyAnimation(keyObj);
            return;
        }

        // Handle space bar
        if (key == " " && _keyMap.TryGetValue("Space", out keyObj))
        {
            TriggerKeyAnimation(keyObj);
        }
    }

    /// <summary>
    /// Trigger press animation on a key GameObject
    /// </summary>
    void TriggerKeyAnimation(GameObject keyWrapper)
    {
        if (keyWrapper == null) return;

        // Find HitArea and Visuals like in AddKeyPressAnimation
        Transform hitArea = keyWrapper.transform.Find("HitArea");
        if (hitArea == null)
        {
            Button button = keyWrapper.GetComponentInChildren<Button>();
            if (button != null)
                hitArea = button.transform;
            else
                hitArea = keyWrapper.transform;
        }

        Transform visuals = hitArea.Find("Visuals");
        Transform animTarget = visuals != null ? visuals : hitArea;

        // Trigger down then up animation
        StartCoroutine(AnimateKeyPressFromPhysical(animTarget));
    }

    /// <summary>
    /// Animate key press triggered by physical keyboard
    /// </summary>
    IEnumerator AnimateKeyPressFromPhysical(Transform keyTransform)
    {
        // Animate down
        Vector3 startScale = Vector3.one;
        Vector3 downScale = Vector3.one * keyDownScale;
        float elapsed = 0f;

        while (elapsed < keyAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / keyAnimDuration;
            t = t * t * (3f - 2f * t); // Smoothstep
            keyTransform.localScale = Vector3.Lerp(startScale, downScale, t);
            yield return null;
        }
        keyTransform.localScale = downScale;

        // Brief hold
        yield return new WaitForSeconds(0.03f);

        // Animate up
        elapsed = 0f;
        while (elapsed < keyAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / keyAnimDuration;
            t = t * t * (3f - 2f * t); // Smoothstep
            keyTransform.localScale = Vector3.Lerp(downScale, startScale, t);
            yield return null;
        }
        keyTransform.localScale = startScale;
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
        _hasInitializedOrientation = true;
    }

    /// <summary>
    /// Updates keyboard position to overlap VRTaskbar and slightly overlap VRMenuFrame bottom.
    /// Keyboard is parallel to taskbar/frame but closer to camera.
    /// </summary>
    void UpdatePositionRelativeToPrimary()
    {
        if (!followPrimaryFrame) return;

        VRMenuFrame primary = VRMenuFrame.PrimaryInstance;
        if (primary == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        VRTaskbar taskbar = FindObjectOfType<VRTaskbar>();

        Vector3 cameraPos = cam.transform.position;
        float keyboardHalfHeight = (_logicalHeight * PixelToMeter) / 2f;

        Vector3 targetPos;
        Quaternion targetRotation;

        // Get primary frame bottom position
        Vector3 primaryPos = primary.transform.position;
        float primaryHalfHeight = primary.panelHeight / 2f;
        float frameBottomY = primaryPos.y - primaryHalfHeight;

        // Keyboard top edge should slightly overlap frame bottom (by ~5% of keyboard height)
        float overlapAmount = keyboardHalfHeight * 0.1f;
        // Offset to compensate for asymmetric margins (shift down by marginBottom - marginTop)
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

    public void Show(TMP_InputField inputField)
    {
        _targetInputField = inputField;
        _isKeyboardVisibleOnStart = true;

        EnsureBuilt();

        gameObject.SetActive(true);

        if (_targetInputField != null)
        {
            // Don't activate input field - we handle all input ourselves
            // This prevents TMP_InputField from intercepting keyboard input
            _targetInputField.DeactivateInputField();
            _targetInputField.caretPosition = _targetInputField.text.Length;
        }

        ResetCursorBlink();
        UpdatePreview();

        // Position relative to VRMenuFrame/VRTaskbar (also sets rotation parallel to taskbar)
        UpdatePositionRelativeToPrimary();

        // Set static reference
        CurrentlyOpenKeyboard = this;
    }

    public void Hide()
    {
        _targetInputField = null;
        gameObject.SetActive(false);
        ResetShift();
        _currentLayout = KeyboardLayout.Letters;

        // Clear static reference
        if (CurrentlyOpenKeyboard == this)
        {
            CurrentlyOpenKeyboard = null;
        }
    }

    /// <summary>
    /// Switch keyboard target to a different InputField without closing
    /// </summary>
    public void SwitchToInputField(TMP_InputField newInputField)
    {
        if (newInputField == null || newInputField == _targetInputField) return;

        _targetInputField = newInputField;

        if (_targetInputField != null)
        {
            _targetInputField.DeactivateInputField();
            _targetInputField.caretPosition = _targetInputField.text.Length;
        }

        ResetCursorBlink();
        UpdatePreview();
    }

    /// <summary>
    /// Check if a GameObject is part of this keyboard (for click-outside detection)
    /// </summary>
    public bool IsPartOfKeyboard(GameObject obj)
    {
        if (obj == null) return false;

        // Check if obj is this keyboard or a child of it
        Transform current = obj.transform;
        while (current != null)
        {
            if (current.gameObject == gameObject)
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    public bool IsVisible => gameObject.activeSelf;
    public TMP_InputField TargetInputField => _targetInputField;

    void BuildKeyboard()
    {
        // Calculate dimensions based on VRMenuFrame (2/3 width)
        VRMenuFrame primary = VRMenuFrame.PrimaryInstance;
        float frameLogicalWidth = primary != null ? primary.logicalWidth : 1920f;

        // Keyboard logical width = 2/3 of VRMenuFrame
        _logicalWidth = frameLogicalWidth * widthRatioToFrame;

        // Calculate content area (inside margins)
        float contentWidth = _logicalWidth - marginLeft - marginRight;

        // Calculate key dimensions based on content area
        // 10 keys + 9 gaps in a row: contentWidth = 10*keyWidth + 9*keySpacing
        // keySpacing = keyWidth * keySpacingRatio
        // contentWidth = 10*keyWidth + 9*keyWidth*keySpacingRatio = keyWidth * (10 + 9*keySpacingRatio)
        keyWidth = contentWidth / (10f + 9f * keySpacingRatio);
        keySpacing = keyWidth * keySpacingRatio;
        keyHeight = keyWidth * keyHeightRatio;

        // Calculate number of rows and total content height
        int numRows = 5; // numbers, qwerty, asdf, shift, bottom
        float previewHeight = showPreview ? keyHeight * 0.6f + keySpacing : 0f;
        float contentHeight = numRows * keyHeight + (numRows - 1) * keySpacing + previewHeight;

        // Total keyboard dimensions (content + margins)
        _logicalHeight = contentHeight + marginTop + marginBottom;

        // Setup RectTransform with VRMenuFrame-style scale
        CanvasRect = GetComponent<RectTransform>();
        if (CanvasRect == null)
            CanvasRect = gameObject.AddComponent<RectTransform>();

        CanvasRect.sizeDelta = new Vector2(_logicalWidth, _logicalHeight);
        CanvasRect.localScale = new Vector3(PixelToMeter, PixelToMeter, 1f);
        CanvasRect.localPosition = Vector3.zero;

        // Add VRMenuFrame-style glass background
        CreateGlassPanel(transform, _logicalWidth, _logicalHeight);

        // Create container for keys with margins
        _keyboardContainer = new GameObject("KeyContainer");
        _keyboardContainer.transform.SetParent(transform, false);
        RectTransform containerRT = _keyboardContainer.AddComponent<RectTransform>();
        containerRT.anchorMin = Vector2.zero;
        containerRT.anchorMax = Vector2.one;
        containerRT.offsetMin = new Vector2(marginLeft, marginBottom);
        containerRT.offsetMax = new Vector2(-marginRight, -marginTop);

        // Add nested Canvas with higher sortingOrder so keys render AFTER glassmorphism GrabPass
        Canvas keyCanvas = _keyboardContainer.AddComponent<Canvas>();
        keyCanvas.overrideSorting = true;
        keyCanvas.sortingOrder = 160; // Higher than keyboard's 150, after Overlay shader Queue
        _keyboardContainer.AddComponent<GraphicRaycaster>();

        // Calculate starting Y position for keys (top of content area)
        float currentY = contentHeight / 2f - keyHeight / 2f;

        // Preview text row (optional)
        if (showPreview)
        {
            CreatePreviewRow(_keyboardContainer.transform, currentY, contentWidth);
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
        _keyMap.Clear();
        _shiftKey = null;
        _shiftKeyAnimation = null;
        _layoutSwitchKey = null;
        _symbolSwitchKey = null;
    }

    void RebuildKeys()
    {
        ClearKeys();

        float unit = keyWidth + keySpacing;
        // Content width = 10 keys + 9 gaps
        float contentWidth = 10f * keyWidth + 9f * keySpacing;

        // Content height = 5 rows + gaps + preview (this is the fixed reference)
        int numRows5 = 5;
        float previewHeight = showPreview ? keyHeight * 0.6f + keySpacing : 0f;
        float contentHeight5Rows = numRows5 * keyHeight + (numRows5 - 1) * keySpacing;

        // Start Y at top of content, accounting for preview
        float startY = (contentHeight5Rows + previewHeight) / 2f - keyHeight / 2f;
        if (showPreview)
        {
            startY -= keyHeight * 0.6f + keySpacing;
        }

        // Fixed bottom row Y position (same for all layouts - based on 5-row layout)
        float bottomRowY = startY - 4 * (keyHeight + keySpacing);

        if (_currentLayout == KeyboardLayout.Letters)
        {
            // 5-row layout uses normal spacing and key heights
            BuildLettersLayout(_keyboardContainer.transform, startY, unit, contentWidth, bottomRowY);
        }
        else
        {
            // 4-row layout calculations:
            // - Top of row 1 and bottom of bottom row are fixed reference points
            // - Increase spacing by 3x between rows
            // - Remaining space goes to top 3 rows (bottom row stays same height)

            float increasedSpacing = keySpacing * 3f;
            // Total vertical space for keys (excluding preview): 5*keyHeight + 4*keySpacing
            // For 4 rows: bottomRowHeight + 3*topRowHeight + 3*increasedSpacing = totalSpace
            // bottomRowHeight = keyHeight (fixed)
            // 3*topRowHeight = totalSpace - keyHeight - 9*keySpacing
            // topRowHeight = (4*keyHeight - 5*keySpacing) / 3
            float topRowsHeight = (4f * keyHeight - 5f * keySpacing) / 3f;

            if (_currentLayout == KeyboardLayout.Symbols)
            {
                BuildSymbolsLayout(_keyboardContainer.transform, startY, unit, contentWidth, bottomRowY, increasedSpacing, topRowsHeight);
            }
            else
            {
                BuildMoreSymbolsLayout(_keyboardContainer.transform, startY, unit, contentWidth, bottomRowY, increasedSpacing, topRowsHeight);
            }
        }

        // Ensure all newly created keys are in VirtualObjects layer
        SetLayerRecursive(_keyboardContainer, "VirtualObjects");
    }

    void BuildLettersLayout(Transform parent, float startY, float unit, float contentWidth, float bottomRowY)
    {
        // startX = center of first key (no extra margin, margins handled by container)
        float startX = -contentWidth / 2f + keyWidth / 2f;

        // Build from bottom up - calculate positions
        // 5 rows: bottom, shift, asdf, qwerty, numbers
        // bottomRowY is passed in to ensure consistent position across layouts
        float shiftRowY = bottomRowY + (keyHeight + keySpacing);
        float asdfRowY = shiftRowY + (keyHeight + keySpacing);
        float qwertyRowY = asdfRowY + (keyHeight + keySpacing);
        float numbersRowY = qwertyRowY + (keyHeight + keySpacing);

        // === ROW 5 (Bottom): ?123 / [spacebar] . Enter ===
        // Mirroring shift row: ?123=Shift(1.5), /=Z(1), spacebar=xcvbn(5), .=M(1), Enter=Back(1.5)
        float sideKeyWidth = keyWidth * 1.5f;  // Same as Shift/Back

        float x = startX;

        // ?123 key (same position as Shift)
        _layoutSwitchKey = CreatePillKey(parent, "?123", x + (sideKeyWidth - keyWidth) / 2f, bottomRowY, sideKeyWidth, keyHeight,
            specialKeyColor, () => SwitchToLayout(KeyboardLayout.Symbols));
        _allKeys.Add(_layoutSwitchKey);
        x += sideKeyWidth + keySpacing;

        // Slash key (aligned with Z - position 0 of letter keys)
        var slashKey = CreateKey(parent, "/", x, bottomRowY, keyWidth, keyHeight, specialKeyColor, "/");
        _allKeys.Add(slashKey);
        x += unit;

        // Spacebar center (spans positions 1-5: x,c,v,b,n)
        // Spacebar width = 5 keys + 4 gaps
        float spaceWidth = 5 * keyWidth + 4 * keySpacing;
        float spaceCenterX = x + 2 * unit; // Center of 5 keys
        var spaceKey = CreateSpecialKey(parent, "", spaceCenterX, bottomRowY, spaceWidth, keyHeight,
            keyColor, () => OnKeyPress(" "));
        _allKeys.Add(spaceKey);
        x += 5 * unit;

        // Period key (aligned with M - position 6 of letter keys)
        var periodKey = CreateKey(parent, ".", x, bottomRowY, keyWidth, keyHeight, specialKeyColor, ".");
        _allKeys.Add(periodKey);
        x += unit;

        // Enter key (same position as Back)
        var enterKey = CreatePillKey(parent, "Enter", x + (sideKeyWidth - keyWidth) / 2f, bottomRowY, sideKeyWidth, keyHeight,
            keyColor, () => OnEnter());
        _allKeys.Add(enterKey);

        // === ROW 4 (Shift row): Shift zxcvbnm Back ===
        x = startX;
        float shiftWidth = keyWidth * 1.5f;

        // Shift key (wider, rounded square)
        // Show "SHIFT" in caps lock state, "Shift" otherwise
        string shiftLabel = _isCapsLock ? "SHIFT" : "Shift";
        _shiftKey = CreateSpecialKey(parent, shiftLabel, x + (shiftWidth - keyWidth) / 2f, shiftRowY, shiftWidth, keyHeight,
            specialKeyColor, () => ToggleShift());
        _allKeys.Add(_shiftKey);

        // Get VRButtonAnimation and set force hover when shift is active (state 2 or 3)
        _shiftKeyAnimation = _shiftKey.GetComponentInChildren<VRButtonAnimation>();
        if (_shiftKeyAnimation != null && (_isShiftActive || _isCapsLock))
        {
            _shiftKeyAnimation.SetForceHover(true);
        }
        x += shiftWidth + keySpacing;

        // Letter keys z-m
        foreach (string key in LETTERS_ROW_3)
        {
            string displayKey = (_isShiftActive || _isCapsLock) ? key.ToUpper() : key;
            var keyObj = CreateKey(parent, displayKey, x, shiftRowY, keyWidth, keyHeight, keyColor, key);
            _allKeys.Add(keyObj);
            x += unit;
        }

        // Backspace (wider, rounded square)
        float backspaceWidth = keyWidth * 1.5f;
        var backKey = CreateSpecialKey(parent, "Back", x + (backspaceWidth - keyWidth) / 2f, shiftRowY, backspaceWidth, keyHeight,
            specialKeyColor, () => OnBackspace());
        _allKeys.Add(backKey);

        // === ROW 3 (ASDF): asdfghjkl (9 keys, centered) ===
        float row3Offset = unit * 0.5f;
        CreateKeyRow(parent, LETTERS_ROW_2, startX + row3Offset, asdfRowY, unit, true);

        // === ROW 2 (QWERTY): qwertyuiop ===
        CreateKeyRow(parent, LETTERS_ROW_1, startX, qwertyRowY, unit, true);

        // === ROW 1 (Numbers): 1234567890 ===
        string[] row0 = _isShiftActive ? SHIFTED_ROW_0 : LETTERS_ROW_0;
        CreateKeyRow(parent, row0, startX, numbersRowY, unit, false);
    }

    void BuildSymbolsLayout(Transform parent, float startY, float unit, float contentWidth, float bottomRowY, float rowSpacing, float topRowKeyHeight)
    {
        // startX = center of first key (no extra margin, margins handled by container)
        float startX = -contentWidth / 2f + keyWidth / 2f;

        // Build from bottom up - 4 rows
        // Bottom row uses standard keyHeight, top 3 rows use topRowKeyHeight
        // rowSpacing is doubled compared to 5-row layout
        // Calculate Y positions from bottom up
        float symbolsRowY = bottomRowY + keyHeight / 2f + rowSpacing + topRowKeyHeight / 2f;
        float row1Y = symbolsRowY + topRowKeyHeight / 2f + rowSpacing + topRowKeyHeight / 2f;
        float topRowY = row1Y + topRowKeyHeight / 2f + rowSpacing + topRowKeyHeight / 2f;

        // === ROW 4 (Bottom): ABC , [spacebar] . Enter ===
        // Mirroring shift row: ABC=Shift(1.5), ,=Z(1), spacebar=xcvbn(5), .=M(1), Enter=Back(1.5)
        float sideKeyWidth = keyWidth * 1.5f;  // Same as Shift/Back

        float x = startX;

        // ABC key (same position as Shift)
        _layoutSwitchKey = CreatePillKey(parent, "ABC", x + (sideKeyWidth - keyWidth) / 2f, bottomRowY, sideKeyWidth, keyHeight,
            specialKeyColor, () => SwitchToLayout(KeyboardLayout.Letters));
        _allKeys.Add(_layoutSwitchKey);
        x += sideKeyWidth + keySpacing;

        // Comma key (aligned with Z - position 0)
        var commaKey = CreateKey(parent, ",", x, bottomRowY, keyWidth, keyHeight, specialKeyColor, ",");
        _allKeys.Add(commaKey);
        x += unit;

        // Spacebar (spans positions 1-5: x,c,v,b,n)
        float spaceWidth = 5 * keyWidth + 4 * keySpacing;
        float spaceCenterX = x + 2 * unit;
        var spaceKey = CreateSpecialKey(parent, "", spaceCenterX, bottomRowY, spaceWidth, keyHeight,
            keyColor, () => OnKeyPress(" "));
        _allKeys.Add(spaceKey);
        x += 5 * unit;

        // Period key (aligned with M - position 6)
        var periodKey = CreateKey(parent, ".", x, bottomRowY, keyWidth, keyHeight, specialKeyColor, ".");
        _allKeys.Add(periodKey);
        x += unit;

        // Enter key (same position as Back)
        var enterKey = CreatePillKey(parent, "Enter", x + (sideKeyWidth - keyWidth) / 2f, bottomRowY, sideKeyWidth, keyHeight,
            keyColor, () => OnEnter());
        _allKeys.Add(enterKey);

        // === ROW 3: =\< *"':;!? Back ===
        // Same structure as shift row: =\<=Shift(1.5), symbols(7), Back(1.5)
        x = startX;
        float sideKeyHeightTop = topRowKeyHeight;  // Side keys in top rows also use topRowKeyHeight

        // =\< key (same width as Shift/ABC)
        _symbolSwitchKey = CreatePillKey(parent, "=\\<", x + (sideKeyWidth - keyWidth) / 2f, symbolsRowY, sideKeyWidth, sideKeyHeightTop,
            specialKeyColor, () => SwitchToLayout(KeyboardLayout.MoreSymbols));
        _allKeys.Add(_symbolSwitchKey);
        x += sideKeyWidth + keySpacing;

        // Symbol keys
        foreach (string key in SYMBOLS_ROW_2)
        {
            var keyObj = CreateKey(parent, key, x, symbolsRowY, keyWidth, topRowKeyHeight, keyColor, key);
            _allKeys.Add(keyObj);
            x += unit;
        }

        // Backspace (same width as Back/Enter)
        var backKey = CreateSpecialKey(parent, "Back", x + (sideKeyWidth - keyWidth) / 2f, symbolsRowY, sideKeyWidth, sideKeyHeightTop,
            specialKeyColor, () => OnBackspace());
        _allKeys.Add(backKey);

        // === ROW 2: @#₫_&-+()/ ===
        CreateKeyRowWithHeight(parent, SYMBOLS_ROW_1, startX, row1Y, unit, false, topRowKeyHeight);

        // === ROW 1 (Top): 1234567890 ===
        CreateKeyRowWithHeight(parent, SYMBOLS_ROW_0, startX, topRowY, unit, false, topRowKeyHeight);
    }

    void BuildMoreSymbolsLayout(Transform parent, float startY, float unit, float contentWidth, float bottomRowY, float rowSpacing, float topRowKeyHeight)
    {
        // startX = center of first key (no extra margin, margins handled by container)
        float startX = -contentWidth / 2f + keyWidth / 2f;

        // Build from bottom up - 4 rows
        // Bottom row uses standard keyHeight, top 3 rows use topRowKeyHeight
        // rowSpacing is doubled compared to 5-row layout
        float symbolsRowY = bottomRowY + keyHeight / 2f + rowSpacing + topRowKeyHeight / 2f;
        float currencyRowY = symbolsRowY + topRowKeyHeight / 2f + rowSpacing + topRowKeyHeight / 2f;
        float topRowY = currencyRowY + topRowKeyHeight / 2f + rowSpacing + topRowKeyHeight / 2f;

        // === ROW 4 (Bottom): ABC < [spacebar] > Enter ===
        // Mirroring shift row: ABC=Shift(1.5), <=Z(1), spacebar=xcvbn(5), >=M(1), Enter=Back(1.5)
        float sideKeyWidth = keyWidth * 1.5f;  // Same as Shift/Back

        float x = startX;

        // ABC key (same position as Shift)
        _layoutSwitchKey = CreatePillKey(parent, "ABC", x + (sideKeyWidth - keyWidth) / 2f, bottomRowY, sideKeyWidth, keyHeight,
            specialKeyColor, () => SwitchToLayout(KeyboardLayout.Letters));
        _allKeys.Add(_layoutSwitchKey);
        x += sideKeyWidth + keySpacing;

        // < key (aligned with Z - position 0)
        var ltKey = CreateKey(parent, "<", x, bottomRowY, keyWidth, keyHeight, specialKeyColor, "<");
        _allKeys.Add(ltKey);
        x += unit;

        // Spacebar (spans positions 1-5: x,c,v,b,n)
        float spaceWidth = 5 * keyWidth + 4 * keySpacing;
        float spaceCenterX = x + 2 * unit;
        var spaceKey = CreateSpecialKey(parent, "", spaceCenterX, bottomRowY, spaceWidth, keyHeight,
            keyColor, () => OnKeyPress(" "));
        _allKeys.Add(spaceKey);
        x += 5 * unit;

        // > key (aligned with M - position 6)
        var gtKey = CreateKey(parent, ">", x, bottomRowY, keyWidth, keyHeight, specialKeyColor, ">");
        _allKeys.Add(gtKey);
        x += unit;

        // Enter key (same position as Back)
        var enterKey = CreatePillKey(parent, "Enter", x + (sideKeyWidth - keyWidth) / 2f, bottomRowY, sideKeyWidth, keyHeight,
            keyColor, () => OnEnter());
        _allKeys.Add(enterKey);

        // === ROW 3: ?123 % © ® ™ ✓ [ ] Back ===
        // Same structure as shift row: ?123=Shift(1.5), symbols(7), Back(1.5)
        x = startX;
        float sideKeyHeightTop = topRowKeyHeight;  // Side keys in top rows also use topRowKeyHeight

        // ?123 key (same width as Shift/ABC)
        _symbolSwitchKey = CreatePillKey(parent, "?123", x + (sideKeyWidth - keyWidth) / 2f, symbolsRowY, sideKeyWidth, sideKeyHeightTop,
            specialKeyColor, () => SwitchToLayout(KeyboardLayout.Symbols));
        _allKeys.Add(_symbolSwitchKey);
        x += sideKeyWidth + keySpacing;

        // Symbol keys: % © ® ™ ✓ [ ]
        foreach (string key in MORE_SYMBOLS_ROW_2)
        {
            var keyObj = CreateKey(parent, key, x, symbolsRowY, keyWidth, topRowKeyHeight, keyColor, key);
            _allKeys.Add(keyObj);
            x += unit;
        }

        // Backspace (same width as Back/Enter)
        var backKey = CreateSpecialKey(parent, "Back", x + (sideKeyWidth - keyWidth) / 2f, symbolsRowY, sideKeyWidth, sideKeyHeightTop,
            specialKeyColor, () => OnBackspace());
        _allKeys.Add(backKey);

        // === ROW 2: £€$¢^°={} \ ===
        CreateKeyRowWithHeight(parent, MORE_SYMBOLS_ROW_1, startX, currencyRowY, unit, false, topRowKeyHeight);

        // === ROW 1 (Top): ~`|•√π÷×§△ ===
        CreateKeyRowWithHeight(parent, MORE_SYMBOLS_ROW_0, startX, topRowY, unit, false, topRowKeyHeight);
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

    void CreateKeyRowWithHeight(Transform parent, string[] keys, float startX, float y, float unit, bool isLetter, float height)
    {
        float x = startX;
        foreach (string key in keys)
        {
            string displayKey = key;
            if (isLetter && (_isShiftActive || _isCapsLock))
            {
                displayKey = key.ToUpper();
            }
            var keyObj = CreateKey(parent, displayKey, x, y, keyWidth, height, keyColor, key);
            _allKeys.Add(keyObj);
            x += unit;
        }
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

        // Use GlassGradientBackgroundOverlay for GrabPass to capture content behind keyboard
        Shader overlayShader = Shader.Find("Custom/GlassGradientBackgroundOverlay");
        if (overlayShader != null)
        {
            Material glassMat = new Material(overlayShader);

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

            // Glassmorphism settings - enable blur to see through
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
            // Fallback to regular shader
            Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
            if (glassShader != null)
            {
                Material glassMat = new Material(glassShader);
                glassMat.SetFloat("_CornerRadius", 0.12f);
                glassMat.SetFloat("_EdgePadding", edgePad);
                glassMat.SetFloat("_Aspect", aspect);
                glassMat.SetColor("_ColorA", new Color(0.35f, 0.9f, 1f, 0.15f));
                glassMat.SetColor("_ColorB", new Color(0.75f, 0.45f, 1f, 0.22f));
                glassMat.SetFloat("_GlassAlpha", 0.08f);
                glassMat.SetFloat("_BlurEnabled", 0f);
                img.material = glassMat;
                img.color = Color.white;
            }
            else
            {
                img.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
                expansion = 0;
            }
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

    void CreatePreviewRow(Transform parent, float y, float contentWidth)
    {
        // Calculate clear button size
        float clearButtonSize = keyHeight * 0.65f;
        float buttonPadding = 10f;

        _previewText = new GameObject("PreviewText");
        _previewText.transform.SetParent(parent, false);

        RectTransform rt = _previewText.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, y + keyHeight * 0.235f);
        rt.sizeDelta = new Vector2(contentWidth, keyHeight * 0.5f);

        // Text object - shortened on the right to make room for clear button
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(_previewText.transform, false);
        RectTransform textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(15, 0);
        textRT.offsetMax = new Vector2(-(clearButtonSize + buttonPadding), 0);

        _previewTMP = textObj.AddComponent<TextMeshProUGUI>();
        _previewTMP.fontSize = 42;
        _previewTMP.color = Color.white;
        _previewTMP.alignment = TextAlignmentOptions.Center;
        _previewTMP.verticalAlignment = VerticalAlignmentOptions.Middle;
        _previewTMP.raycastTarget = false;
        _previewTMP.overflowMode = TextOverflowModes.Ellipsis;
        if (customFont != null) _previewTMP.font = customFont;

        // Set Text Style to "Title" from TMP Style Sheet
        if (TMP_Settings.defaultStyleSheet != null)
        {
            TMP_Style titleStyle = TMP_Settings.defaultStyleSheet.GetStyle("Title");
            if (titleStyle != null)
            {
                _previewTMP.textStyle = titleStyle;
            }
        }

        // Glowing underline (full width)
        CreatePreviewUnderline(_previewText.transform, contentWidth);

        // Create Clear button (BareIconButton) inside PreviewText, on the right, above underline
        Sprite clearIcon = VRTaskbar.LoadIcon("clear");
        GameObject clearBtn = VRButtonFactory.CreateBareIconButton(
            _previewText.transform,
            clearButtonSize,
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
        clearRT.anchoredPosition = new Vector2(-buttonPadding * 1.5f, keyHeight * 0.04f); // Slightly above underline
    }

    void CreatePreviewUnderline(Transform parent, float width)
    {
        GameObject underlineObj = new GameObject("Underline");
        underlineObj.transform.SetParent(parent, false);

        RectTransform rt = underlineObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(15, 0);
        rt.offsetMax = new Vector2(-15, 0);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, 0);
        rt.sizeDelta = new Vector2(width * 0.975f, keyHeight * 0.5f); // Height for glow effect

        Image lineImg = underlineObj.AddComponent<Image>();
        lineImg.raycastTarget = false;
        lineImg.color = Color.white; // Important for shader to work

        Shader glowShader = Shader.Find("Custom/GlowingHorizontalLine");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);

            // Line settings
            glowMat.SetFloat("_LineWidth", 0.12f);
            glowMat.SetFloat("_GlowWidth", 0.4f);

            // Gradient colors: Deep Sea Blue to Purple
            glowMat.SetColor("_ColorA", new Color(0.0f, 0.8f, 1f, 1f));   // Deep Sea Blue
            glowMat.SetColor("_ColorB", new Color(0.8f, 0.2f, 1f, 1f));   // Purple

            // Intensity
            glowMat.SetFloat("_Layer1Alpha", 1f);
            glowMat.SetFloat("_Layer2Alpha", 0.6f);

            // Edge fade enabled (alpha fades to 30% at last 20% of both ends)
            glowMat.SetFloat("_EdgeFade", 1f);

            lineImg.material = glowMat;
            lineImg.sprite = GetPixelSprite();
        }
        else
        {
            // Fallback: simple gradient line
            lineImg.color = new Color(0.3f, 0.9f, 1f, 0.5f);
        }
    }

    GameObject CreateKey(Transform parent, string displayKey, float x, float y, float width, float height, Color color, string outputKey)
    {
        var config = new VRButtonFactory.ButtonConfig
        {
            label = displayKey,
            themeColor = color,
            width = width,
            height = height,
            fontSize = keyFontSize,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.9f,
            cornerRadius = 0.12f,
            borderWidth = 0.015f,
            popAmount = 0.015f
        };

        string keyValue = outputKey;
        GameObject btn = VRButtonFactory.CreateButton(parent, config, () => OnKeyPress(keyValue));

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);

        AddKeyPressAnimation(btn);

        // Register key in map for physical keyboard visual feedback
        // Register both lowercase and uppercase for letter keys
        string keyLower = outputKey.ToLower();
        string keyUpper = outputKey.ToUpper();
        if (!_keyMap.ContainsKey(keyLower)) _keyMap[keyLower] = btn;
        if (!_keyMap.ContainsKey(keyUpper)) _keyMap[keyUpper] = btn;

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
            fontSize = keyFontSize,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.9f,
            cornerRadius = 0.12f,
            borderWidth = 0.015f,
            popAmount = 0.015f
        };

        GameObject btn = VRButtonFactory.CreateButton(parent, config, () => onClick?.Invoke());

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);

        AddKeyPressAnimation(btn);

        // Register special keys in keyMap for physical keyboard visual feedback
        if (!string.IsNullOrEmpty(label) && !_keyMap.ContainsKey(label))
        {
            _keyMap[label] = btn;
        }
        // Register space bar with " " key
        if (string.IsNullOrEmpty(label))
        {
            _keyMap[" "] = btn;
            _keyMap["Space"] = btn;
        }

        return btn;
    }

    /// <summary>
    /// Create horizontal rectangular key (was pill-shaped, now rectangle)
    /// Uses exact width/height passed in (no multipliers)
    /// </summary>
    GameObject CreatePillKey(Transform parent, string label, float x, float y, float width, float height,
        Color color, Action onClick)
    {
        var config = new VRButtonFactory.ButtonConfig
        {
            label = label,
            themeColor = color,
            width = width,
            height = height,
            fontSize = keyFontSize,
            font = customFont,
            textOnly = true,
            backgroundAlpha = 0.9f,
            cornerRadius = 0.15f, // Rectangular with slight rounding
            borderWidth = 0.015f,
            popAmount = 0.015f
        };

        GameObject btn = VRButtonFactory.CreateButton(parent, config, () => onClick?.Invoke());

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);

        AddKeyPressAnimation(btn);

        // Register pill keys (Enter, layout switches) for physical keyboard visual feedback
        if (!string.IsNullOrEmpty(label) && !_keyMap.ContainsKey(label))
        {
            _keyMap[label] = btn;
        }

        return btn;
    }

    /// <summary>
    /// Add KeyDown/KeyUp animation events to a button
    /// VRButtonFactory structure: Wrapper -> HitArea (has Button) -> Visuals
    /// EventTrigger needs to be on HitArea, animation targets Visuals
    /// </summary>
    void AddKeyPressAnimation(GameObject btnWrapper)
    {
        // Find HitArea (child of wrapper, has Button component)
        Transform hitArea = btnWrapper.transform.Find("HitArea");
        if (hitArea == null)
        {
            // Fallback: try to find Button component in children
            Button button = btnWrapper.GetComponentInChildren<Button>();
            if (button != null)
                hitArea = button.transform;
            else
                hitArea = btnWrapper.transform;
        }

        // Find Visuals (the transform to animate)
        Transform visuals = hitArea.Find("Visuals");
        Transform animTarget = visuals != null ? visuals : hitArea;

        // Get VRButtonAnimation to check force hover state
        VRButtonAnimation btnAnim = hitArea.GetComponent<VRButtonAnimation>();

        EventTrigger trigger = hitArea.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = hitArea.gameObject.AddComponent<EventTrigger>();

        // PointerDown - scale down
        EventTrigger.Entry pointerDown = new EventTrigger.Entry();
        pointerDown.eventID = EventTriggerType.PointerDown;
        pointerDown.callback.AddListener((data) => StartCoroutine(AnimateKeyDown(animTarget)));
        trigger.triggers.Add(pointerDown);

        // PointerUp - scale back (only if not in force hover state)
        EventTrigger.Entry pointerUp = new EventTrigger.Entry();
        pointerUp.eventID = EventTriggerType.PointerUp;
        pointerUp.callback.AddListener((data) => {
            if (btnAnim == null || !btnAnim.IsForceHover)
                StartCoroutine(AnimateKeyUp(animTarget));
        });
        trigger.triggers.Add(pointerUp);

        // PointerExit - also scale back (only if not in force hover state)
        EventTrigger.Entry pointerExit = new EventTrigger.Entry();
        pointerExit.eventID = EventTriggerType.PointerExit;
        pointerExit.callback.AddListener((data) => {
            if (btnAnim == null || !btnAnim.IsForceHover)
                StartCoroutine(AnimateKeyUp(animTarget));
        });
        trigger.triggers.Add(pointerExit);
    }

    IEnumerator AnimateKeyDown(Transform keyTransform)
    {
        Vector3 startScale = Vector3.one;
        Vector3 targetScale = Vector3.one * keyDownScale;
        float elapsed = 0f;

        while (elapsed < keyAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / keyAnimDuration;
            t = t * t * (3f - 2f * t); // Smoothstep easing
            keyTransform.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }
        keyTransform.localScale = targetScale;
    }

    IEnumerator AnimateKeyUp(Transform keyTransform)
    {
        Vector3 startScale = keyTransform.localScale;
        Vector3 targetScale = Vector3.one;
        float elapsed = 0f;

        while (elapsed < keyAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / keyAnimDuration;
            t = t * t * (3f - 2f * t); // Smoothstep easing
            keyTransform.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }
        keyTransform.localScale = targetScale;
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

    void OnClear()
    {
        if (_targetInputField != null)
        {
            _targetInputField.text = "";
            _targetInputField.caretPosition = 0;
            ResetCursorBlink();
            UpdatePreview();
        }
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
        // 3 states:
        // State 1: default (_isShiftActive=false, _isCapsLock=false)
        // State 2: shift active, label "Shift", auto-release after pressing another key
        // State 3: caps lock, label "SHIFT", stays until clicked again

        if (!_isShiftActive && !_isCapsLock)
        {
            // State 1 -> State 2: Enable shift
            _isShiftActive = true;
            _isCapsLock = false;
        }
        else if (_isShiftActive && !_isCapsLock)
        {
            // State 2 -> State 3: Enable caps lock
            _isShiftActive = true;
            _isCapsLock = true;
        }
        else
        {
            // State 3 -> State 1: Disable all
            _isShiftActive = false;
            _isCapsLock = false;
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
            _previewTMP.text = _targetInputField.text;
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
            // Create keyboard under VirtualObjects parent for organization
            GameObject keyboardObj = new GameObject("VRMobileKeyboard");

            // Find VirtualObjects parent in scene
            GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");
            if (virtualObjectsParent != null)
            {
                keyboardObj.transform.SetParent(virtualObjectsParent.transform, false);
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
