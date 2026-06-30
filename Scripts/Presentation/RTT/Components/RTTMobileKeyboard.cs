using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.VRInput;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Input;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// RTT-based Mobile Virtual Keyboard.
    /// Migrated from VRMobileKeyboard to use Render-to-Texture approach.
    /// </summary>
    public partial class RTTMobileKeyboard : RTTCanvasBase
    {
        public enum KeyboardLayout { Letters, Symbols, MoreSymbols }

        #region Static Instance
        private static RTTMobileKeyboard _instance;
        public static RTTMobileKeyboard Instance => _instance;
        public static RTTMobileKeyboard CurrentlyOpenKeyboard { get; private set; }

        /// <summary>
        /// Reset static singletons at the start of each Play session.
        /// Without this, _instance and CurrentlyOpenKeyboard keep ghost references to
        /// destroyed keyboards on the 2nd Play onwards (Unity doesn't reset static fields on Play exit).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticInstance()
        {
            _instance = null;
            CurrentlyOpenKeyboard = null;
        }
        #endregion

        #region Configuration
        [Header("Theme")]
        [SerializeField] private Color themeColor = new Color(0.0f, 0.9f, 1.0f);
        [SerializeField] private Color keyColor = new Color(0.25f, 0.27f, 0.32f);
        [SerializeField] private Color specialKeyColor = new Color(0.35f, 0.38f, 0.45f);
        [SerializeField] private TMP_FontAsset customFont;

        [Header("Layout")]
        [SerializeField] private float marginLeft = 60f;
        [SerializeField] private float marginRight = 60f;
        [SerializeField] private float marginTop = 20f;
        [SerializeField] private float marginBottom = 60f;
        [SerializeField] [Range(0.05f, 0.2f)] private float keySpacingRatio = 0.1f;
        [SerializeField] [Range(0.8f, 1.5f)] private float keyHeightRatio = 1.2f;
        [SerializeField] private int keyFontSize = 36;

        [Header("Position")]
        [SerializeField] private bool followTaskbar = true;
    #pragma warning disable 0414 // Reserved for future use
        [SerializeField] private float spacingMultiplier = 1.5f;
    #pragma warning restore 0414
        [SerializeField] private float widthRatioToFrame = 0.7f;
        [SerializeField] private float verticalOffset = -0.105f; // Offset to move keyboard down (negative = lower)
        #endregion

        #region Constants
        private const float PixelToMeter = 1.6f / 1920f;

        // Key press animation constants
        private const float KEY_PRESS_SCALE = 0.9f;        // Scale down to 90% when pressed
        private const float KEY_PRESS_DURATION = 0.08f;    // Total animation duration (down + up)

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
        private Dictionary<string, TextMeshProUGUI> _letterLabels = new Dictionary<string, TextMeshProUGUI>();
        private GameObject _shiftKey;

        // Caret (blinking cursor) support
        private int _caretPosition = 0;
        private bool _caretVisible = true;
        private float _caretBlinkTimer = 0f;
        private const float CARET_BLINK_RATE = 0.5f;
        private RectTransform _caretRect;
        private Image _caretImage;

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
    #pragma warning disable 0414 // Reserved for future use
        private bool _startCompleted = false;
    #pragma warning restore 0414

        // Store original text for cancel/restore functionality
        private string _originalText = "";
        #endregion

        #region Private Fields
        private float _cornerRadius = 0.12f;
        private float _edgePadding = 0.06f;

        // Reference aspect ratio for normal keys (used to calculate aspect-adjusted parameters for wide keys)
        private float _normalKeyAspect => _keyWidth / _keyHeight;
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
            // IMPORTANT: Do NOT call base.Start() here!
            // base.Start() calls Initialize() which creates RenderTexture, Camera, etc.
            // We want to defer initialization until Show() is called to avoid resource contention
            // with other RTT panels (like side panels in RemoteMenu).
            // Initialize() will be called in Show() via EnsureInitialized check.

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

        /// <summary>
        /// Override to use ZWrite shader so keyboard occludes other RTT panels behind it.
        /// This prevents seeing Taskbar/TaskbarExpansion/Pagination through the keyboard.
        /// </summary>
        protected override Material CreateQuadMaterial()
        {
            var shader = Shader.Find("Custom/RTTQuadZWrite");
            if (shader == null)
            {
                Debug.LogWarning("[RTTMobileKeyboard] RTTQuadZWrite shader not found, falling back to default");
                return base.CreateQuadMaterial();
            }

            var mat = new Material(shader);
            mat.name = "RTTQuadMaterial_Keyboard_ZWrite";
            mat.renderQueue = 2990; // Render BEFORE other RTT panels (3000) so depth pre-pass blocks them
            return mat;
        }

        public override void MarkDirty()
        {
            base.MarkDirty();
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();

            if (_targetInputField != null && gameObject.activeSelf)
            {
                UpdatePositionRelativeToTaskbar();
                UpdateCaretBlink();
            }
        }

        private void UpdateCaretBlink()
        {
            _caretBlinkTimer += Time.deltaTime;
            if (_caretBlinkTimer >= CARET_BLINK_RATE)
            {
                _caretBlinkTimer = 0f;
                _caretVisible = !_caretVisible;
                UpdatePreviewDisplay();
                MarkDirty();
            }
        }
        #endregion

        #region Theme Support
        /// <summary>
        /// Apply current theme to keyboard materials.
        /// Called when theme changes at runtime.
        /// </summary>
        protected override void ApplyCurrentTheme()
        {
            var theme = GetTheme();
            if (theme == null) return;

            // Update glass material
            if (_glassMaterial != null)
            {
                _glassMaterial.SetColor("_ColorA", theme.glassColorA);
                _glassMaterial.SetColor("_ColorB", theme.glassColorB);
                _glassMaterial.SetFloat("_GlassAlpha", theme.glassAlpha);
            }

            // Update border material
            // Layer 3 and 4 disabled to prevent glow extending beyond depth mask
            if (_borderMaterial != null)
            {
                _borderMaterial.SetColor("_ColorA", theme.glowColorA);
                _borderMaterial.SetColor("_ColorB", theme.glowColorB);
                _borderMaterial.SetFloat("_Layer1Width", theme.glowLayer1Width);
                _borderMaterial.SetFloat("_Layer1Alpha", theme.glowLayer1Alpha);
                _borderMaterial.SetFloat("_Layer2Width", theme.glowLayer2Width);
                _borderMaterial.SetFloat("_Layer2Alpha", theme.glowLayer2Alpha);
                _borderMaterial.SetFloat("_Layer3Width", 0f);
                _borderMaterial.SetFloat("_Layer3Alpha", 0f);
                _borderMaterial.SetFloat("_Layer4Width", 0f);
                _borderMaterial.SetFloat("_Layer4Alpha", 0f);
            }

            // Update theme color for keys
            themeColor = theme.primaryColor;

            MarkDirty();
        }

        private Color GetGlassColorA()
        {
            return GetTheme()?.glassColorA ?? new Color(0.0f, 0.55f, 0.65f, 0.35f);
        }

        private Color GetGlassColorB()
        {
            return GetTheme()?.glassColorB ?? new Color(0.30f, 0.12f, 0.50f, 0.32f);
        }

        private float GetGlassAlpha()
        {
            return GetTheme()?.glassAlpha ?? 0.65f;
        }

        private Color GetGlowColorA()
        {
            return GetTheme()?.glowColorA ?? themeColor;
        }

        private Color GetGlowColorB()
        {
            return GetTheme()?.glowColorB ?? new Color(0.9f, 0.3f, 1f);
        }

        private Color GetPrimaryColor()
        {
            return GetTheme()?.primaryColor ?? themeColor;
        }

        private Color GetAccentColor()
        {
            return GetTheme()?.accentColor ?? new Color(0.76f, 0.36f, 1f);
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
            float previewHeight = 120f;
            float rowSpacing = _keySpacing;
            float totalRowsHeight = previewHeight + (_keyHeight * numRows) + (rowSpacing * numRows);
            _logicalHeight = marginTop + totalRowsHeight + marginBottom;
        }
        #endregion

        #region UI Building (Background / Container / Preview)
        private void CreateGlassBackground(RectTransform parent)
        {
            GameObject bgObj = new GameObject("GlassBackground");
            bgObj.transform.SetParent(parent, false);

            Image img = bgObj.AddComponent<Image>();
            img.sprite = GetPixelSprite();
            img.raycastTarget = true;

            float aspect = _logicalWidth / _logicalHeight;
            // Edge padding - matches MenuFrame for consistent UI
            float edgePad = 0.0075f;
            // Corner radius - matches MenuFrame for consistent UI
            float bgCornerRadius = 0.04f;
            float borderCornerRadius = 0.04f;

            // Use Wide shader for better Android compatibility (Space key uses this and works)
            Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (glassShader != null)
            {
                _glassMaterial = new Material(glassShader);
                _glassMaterial.SetFloat("_CornerRadius", bgCornerRadius);
                _glassMaterial.SetFloat("_EdgePadding", edgePad);
                _glassMaterial.SetFloat("_Aspect", aspect);
                // Use theme colors for glass gradient
                _glassMaterial.SetColor("_ColorA", GetGlassColorA());
                _glassMaterial.SetColor("_ColorB", GetGlassColorB());
                _glassMaterial.SetFloat("_GlassAlpha", GetGlassAlpha());

                img.material = _glassMaterial;
                img.color = Color.white;
            }
            else
            {
                img.color = new Color(0.0f, 0.4f, 0.5f, 0.35f);
            }

            RectTransform rt = bgObj.GetComponent<RectTransform>();
            // Background stays within canvas bounds - border handles the glow expansion
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.SetAsFirstSibling();

            // Border
            CreateGlowingBorder(bgObj.transform, edgePad, aspect, borderCornerRadius);
        }

        private void CreateGlowingBorder(Transform parent, float edgePad, float aspect, float cornerRadius)
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
                _borderMaterial.SetFloat("_StrokeEnabled", 0);  // Disable stroke to avoid square corners
                _borderMaterial.SetFloat("_BorderWidth", 0.025f);  // Match MenuFrame
                _borderMaterial.SetFloat("_CornerRadius", cornerRadius);
                _borderMaterial.SetFloat("_EdgePadding", edgePad);
                _borderMaterial.SetFloat("_Aspect", aspect);
                // Use theme colors for glow
                _borderMaterial.SetColor("_ColorA", GetGlowColorA());
                _borderMaterial.SetColor("_ColorB", GetGlowColorB());
                // Glow layers - use theme settings or defaults
                // Layer 3 and 4 disabled to prevent glow extending beyond depth mask
                var theme = GetTheme();
                _borderMaterial.SetFloat("_Layer1Width", theme?.glowLayer1Width ?? 0.01f);
                _borderMaterial.SetFloat("_Layer1Alpha", theme?.glowLayer1Alpha ?? 1.5f);
                _borderMaterial.SetFloat("_Layer2Width", theme?.glowLayer2Width ?? 0.02f);
                _borderMaterial.SetFloat("_Layer2Alpha", theme?.glowLayer2Alpha ?? 1.0f);
                _borderMaterial.SetFloat("_Layer3Width", 0f);
                _borderMaterial.SetFloat("_Layer3Alpha", 0f);
                _borderMaterial.SetFloat("_Layer4Width", 0f);
                _borderMaterial.SetFloat("_Layer4Alpha", 0f);
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
            float previewHeight = 120f;
            float buttonSize = _keyHeight * 0.75f;
            float buttonPadding = 25;

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
            _previewText.fontSize = keyFontSize;
            _previewText.alignment = TextAlignmentOptions.Center;
            _previewText.color = Color.white;
            _previewText.overflowMode = TextOverflowModes.Ellipsis;
            if (customFont != null) _previewText.font = customFont;

            // Set Text Style to "Title" from TMP Style Sheet
            if (TMP_Settings.defaultStyleSheet != null)
            {
                TMP_Style titleStyle = TMP_Settings.defaultStyleSheet.GetStyle("Title");
                if (titleStyle != null)
                {
                    _previewText.textStyle = titleStyle;
                }
            }

            RectTransform textRT = textObj.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(buttonSize + buttonPadding * 2f, 8f);
            textRT.offsetMax = new Vector2(-(buttonSize + buttonPadding * 2f), 0);

            // Create caret visual element (thin vertical line)
            GameObject caretObj = new GameObject("Caret");
            caretObj.transform.SetParent(textObj.transform, false);

            _caretRect = caretObj.AddComponent<RectTransform>();
            // Use center anchor (0.5) to match TextMeshPro's coordinate system (centered text)
            _caretRect.anchorMin = new Vector2(0.5f, 0.5f);
            _caretRect.anchorMax = new Vector2(0.5f, 0.5f);
            _caretRect.pivot = new Vector2(0.5f, 0.5f);
            float caretHeight = keyFontSize * 1.5f;
            float caretWidth = keyFontSize * 0.15f;
            _caretRect.sizeDelta = new Vector2(caretWidth, caretHeight);

            _caretImage = caretObj.AddComponent<Image>();
            _caretImage.color = Color.white;
            _caretImage.raycastTarget = false;

            // Create invisible overlay for raycast (separate from text to avoid conflict)
            GameObject overlayObj = new GameObject("PreviewOverlay");
            overlayObj.transform.SetParent(previewRow.transform, false);

            RectTransform overlayRT = overlayObj.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = new Vector2(buttonSize + buttonPadding * 2f, 8f);
            overlayRT.offsetMax = new Vector2(-(buttonSize + buttonPadding * 2f), 0);

            // Add invisible image for raycast
            Image overlayImage = overlayObj.AddComponent<Image>();
            overlayImage.color = Color.clear;
            overlayImage.raycastTarget = true;

            // Add interaction component
            var interaction = overlayObj.AddComponent<PreviewTextInteraction>();
            interaction.Initialize(this, _previewText);

            // Glowing underline
            CreatePreviewUnderline(previewRow.transform);

            // Close button (left side) - using BareIconButton
            Sprite closeIcon = RTTTaskbar.LoadIcon("close");
            GameObject closeBtn = VRButtonFactory.CreateBareIconButton(
                previewRow.transform,
                buttonSize * 0.75f,
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
            Sprite clearIcon = RTTTaskbar.LoadIcon("clear");
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
            // Configure horizontal separator on the border instead of creating a separate object
            if (_borderMaterial == null) return;

            // Calculate the Y position of the separator
            float previewHeight = 100f;
            float underlineOffset = 9f;

            // Preview row bottom edge in canvas space
            float previewRowBottom = (_logicalHeight - marginTop) - previewHeight;
            // Underline Y position
            float underlineY = previewRowBottom + underlineOffset;
            // Convert to UV (0 = bottom, 1 = top)
            float separatorUV = underlineY / _logicalHeight;

            // Calculate separator length (content width ratio)
            float contentWidth = _logicalWidth - marginLeft - marginRight;
            float separatorLength = contentWidth / _logicalWidth;

            // Set horizontal separator on border material
            _borderMaterial.SetFloat("_HSeparatorCount", 1);
            _borderMaterial.SetVector("_HSeparatorPositions", new Vector4(separatorUV, 0, 0, 0));
            _borderMaterial.SetFloat("_HSeparatorWidth", 0.004f);
            _borderMaterial.SetFloat("_HSeparatorGlowWidth", 0.015f);
            _borderMaterial.SetFloat("_HSeparatorAlpha", 1f);
            _borderMaterial.SetVector("_HSeparatorLengths", new Vector4(separatorLength, 1f, 1f, 1f));
        }

        private void OnClose()
        {
            // Restore original text when closing via Close button (cancel operation)
            if (_targetInputField != null)
            {
                _targetInputField.text = _originalText;
            }
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
        #endregion

        #region Preview
        private void UpdatePreview()
        {
            if (_previewText == null || _targetInputField == null) return;

            // Sync caret position from input field
            _caretPosition = _targetInputField.caretPosition;

            // Reset blink timer when typing
            _caretBlinkTimer = 0f;
            _caretVisible = true;

            UpdatePreviewDisplay();
        }

        private void UpdatePreviewDisplay()
        {
            if (_previewText == null || _targetInputField == null) return;

            string text = _targetInputField.text;
            _previewText.text = text; // Always show original text without caret character

            // Update caret visual position and visibility
            if (_caretImage != null)
            {
                _caretImage.enabled = _caretVisible;
            }

            if (_caretRect != null)
            {
                UpdateCaretPosition();
            }
        }

        private void UpdateCaretPosition()
        {
            if (_previewText == null || _caretRect == null) return;

            string text = _previewText.text;
            int pos = Mathf.Clamp(_caretPosition, 0, text.Length);

            // Force mesh update to get accurate character positions
            _previewText.ForceMeshUpdate();

            float caretX = 0f;
            var textInfo = _previewText.textInfo;

            if (textInfo.characterCount > 0 && text.Length > 0)
            {
                if (pos == 0)
                {
                    // Caret at beginning - use left edge of first character
                    var firstChar = textInfo.characterInfo[0];
                    caretX = firstChar.bottomLeft.x;
                }
                else if (pos >= text.Length)
                {
                    // Caret at end - use right edge of last character
                    int lastIndex = Mathf.Min(textInfo.characterCount - 1, text.Length - 1);
                    var lastChar = textInfo.characterInfo[lastIndex];
                    caretX = lastChar.bottomRight.x;
                }
                else
                {
                    // Caret in middle - use left edge of character at position
                    int charIndex = Mathf.Min(pos, textInfo.characterCount - 1);
                    var charAtPos = textInfo.characterInfo[charIndex];
                    caretX = charAtPos.bottomLeft.x;
                }
            }

            // Position caret relative to text's local space
            _caretRect.anchoredPosition = new Vector2(caretX, 0);
        }

        /// <summary>
        /// Move caret to specific position in text
        /// </summary>
        public void SetCaretPosition(int position)
        {
            if (_targetInputField == null) return;

            _caretPosition = Mathf.Clamp(position, 0, _targetInputField.text.Length);
            _targetInputField.caretPosition = _caretPosition;

            // Reset blink to show caret immediately
            _caretBlinkTimer = 0f;
            _caretVisible = true;

            UpdatePreviewDisplay();
            MarkDirty();
        }

        /// <summary>
        /// Get current caret position
        /// </summary>
        public int GetCaretPosition() => _caretPosition;
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

            // Keyboard top edge position relative to frame bottom
            float overlapAmount = keyboardHalfHeight * 0.1f;
            float marginOffset = (marginBottom - marginTop) * 1.5f * PixelToMeter / 2f;
            float keyboardCenterY = frameBottomY - keyboardHalfHeight + overlapAmount - marginOffset + verticalOffset;

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
            _originalText = inputField.text; // Save original text for cancel/restore
            gameObject.SetActive(true);

            // Ensure UI is initialized before updating preview (fixes first-time delay)
            if (!IsInitialized)
            {
                Initialize();
            }
            // If initialized but RenderTexture was released (via releaseResourcesOnHide), recreate it
            else if (_renderTexture == null)
            {
                RecreateRenderTexture();
            }

            SetVisible(true); // Reset visibility state after Hide()
            CurrentlyOpenKeyboard = this;

            // NOTE: Do NOT call SetLayerRecursive here!
            // RTTCanvasBase handles layers: DisplayQuad = VirtualObjects, Canvas = UI

            // Position relative to RTTMenuFrame
            UpdatePositionRelativeToTaskbar();

            // Use inputField's current caret position (may have been set by click handler)
            _caretPosition = _targetInputField.caretPosition;

            // Reset caret blink
            _caretBlinkTimer = 0f;
            _caretVisible = true;

            UpdatePreview();
            MarkDirty();

            Debug.Log("[RTTMobileKeyboard] Shown");
        }

        [Header("Resource Management")]
        [Tooltip("Release RenderTexture when hidden to free GPU memory. Helps with resource contention on mobile.")]
        [SerializeField] private bool releaseResourcesOnHide = true;

        public new void Hide()
        {
            base.Hide(); // Call base to properly set visibility
            _targetInputField = null;

            if (CurrentlyOpenKeyboard == this)
                CurrentlyOpenKeyboard = null;

            OnClosePressed?.Invoke();

            // Release RenderTexture to free GPU memory for other panels
            // This is especially important on mobile devices with limited resources
            if (releaseResourcesOnHide && _renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;

                // Also disable camera since it has no target anymore
                if (_uiCamera != null)
                    _uiCamera.enabled = false;

                Debug.Log("[RTTMobileKeyboard] RenderTexture released on hide");
            }

            Debug.Log("[RTTMobileKeyboard] Hidden");
        }

        /// <summary>
        /// Recreate RenderTexture after it was released.
        /// Reuses existing Camera, Canvas, and DisplayQuad.
        /// </summary>
        private void RecreateRenderTexture()
        {
            if (_renderTexture != null) return; // Already exists

            var resolution = GetResolution();
            int depthBits = GetMobileCompatibleDepthBits();
            int antiAliasing = GetMobileCompatibleAntiAliasing();
            RenderTextureFormat format = GetMobileCompatibleFormat();

            _renderTexture = new RenderTexture(resolution.x, resolution.y, depthBits, format);
            _renderTexture.antiAliasing = antiAliasing;
            _renderTexture.filterMode = FilterMode.Bilinear;
            _renderTexture.useMipMap = false;
            _renderTexture.autoGenerateMips = false;
            _renderTexture.name = $"RTT_{GetType().Name}_{GetEntityId()}";

            if (!_renderTexture.Create())
            {
                Debug.LogError("[RTTMobileKeyboard] Failed to recreate RenderTexture");
                return;
            }

            // Update references
            if (_uiCamera != null)
            {
                _uiCamera.targetTexture = _renderTexture;
                _uiCamera.enabled = true;
            }

            if (_quadMaterial != null)
                _quadMaterial.mainTexture = _renderTexture;

            Debug.Log("[RTTMobileKeyboard] RenderTexture recreated");
        }

        public void SwitchToInputField(TMP_InputField newInputField)
        {
            if (newInputField == null) return;
            _targetInputField = newInputField;
            _originalText = newInputField.text; // Save new original text
            // Don't set caret or update preview here - OnPointerClick will be called
            // right after this to set the correct caret position from click
        }

        public bool IsPartOfKeyboard(GameObject obj)
        {
            if (obj == null) return false;
            return obj.transform.IsChildOf(transform);
        }

        public TMP_InputField GetTargetInputField()
        {
            return _targetInputField;
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
        #endregion
    }

    /// <summary>
    /// Handles hover and click interactions on the preview text area.
    /// - Changes reticle to text cursor icon on hover
    /// - Moves caret position on DwellClick
    /// </summary>
    public class PreviewTextInteraction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private RTTMobileKeyboard _keyboard;
        private TextMeshProUGUI _previewText;
        private Sprite _textCursorSprite;

        public void Initialize(RTTMobileKeyboard keyboard, TextMeshProUGUI previewText)
        {
            _keyboard = keyboard;
            _previewText = previewText;

            // Load text cursor icon from Resources
            _textCursorSprite = Resources.Load<Sprite>("icon_text_cursor");
            if (_textCursorSprite == null)
            {
                // Try loading as Texture2D
                Texture2D tex = Resources.Load<Texture2D>("icon_text_cursor");
                if (tex != null)
                {
                    _textCursorSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }
            }
            // Note: Image component for raycast is added by caller (CreatePreviewRow)
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // Change reticle to text cursor icon
            if (VRGazeReticle.Instance != null && _textCursorSprite != null)
            {
                VRGazeReticle.Instance.SetCursorSprite(_textCursorSprite);
            }
            _keyboard?.MarkDirty();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Reset reticle to default
            if (VRGazeReticle.Instance != null)
            {
                VRGazeReticle.Instance.ResetCursorSprite();
            }
            _keyboard?.MarkDirty();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_keyboard == null || _previewText == null) return;

            // In RTT context, use screen position from RTTRaycastManager
            Vector2 screenPos = eventData.position;
            if (RTTRaycastManager.Instance != null && RTTRaycastManager.Instance.CurrentHit.isValid)
            {
                screenPos = RTTRaycastManager.Instance.CurrentHit.screenPosition;
            }

            // Calculate character index from click position
            int charIndex = GetCharacterIndexFromScreenPosition(screenPos);
            _keyboard.SetCaretPosition(charIndex);
        }

        /// <summary>
        /// Calculate the character index from screen position in RTT context
        /// </summary>
        private int GetCharacterIndexFromScreenPosition(Vector2 screenPosition)
        {
            if (_previewText == null) return 0;

            string text = _previewText.text;
            if (string.IsNullOrEmpty(text)) return 0;

            // Force mesh update to get accurate character info
            _previewText.ForceMeshUpdate();

            var textInfo = _previewText.textInfo;
            if (textInfo.characterCount == 0) return 0;

            // Get canvas and camera for coordinate conversion
            Canvas canvas = _previewText.canvas;
            Camera cam = canvas?.worldCamera;

            // Convert screen position to local position in text rect
            RectTransform rectTransform = _previewText.rectTransform;
            Vector2 localPoint;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPosition, cam, out localPoint))
            {
                // If conversion fails, estimate based on normalized x position
                return EstimateCharacterIndex(screenPosition, text);
            }

            // Find character by comparing x positions with character bounds
            for (int i = 0; i < textInfo.characterCount && i < text.Length; i++)
            {
                var charInfo = textInfo.characterInfo[i];
                if (!charInfo.isVisible) continue;

                float charCenterX = (charInfo.bottomLeft.x + charInfo.bottomRight.x) / 2f;

                if (localPoint.x < charCenterX)
                {
                    return i;
                }
            }

            return text.Length;
        }

        /// <summary>
        /// Estimate character index based on relative x position when coordinate conversion fails
        /// </summary>
        private int EstimateCharacterIndex(Vector2 screenPosition, string originalText)
        {
            if (string.IsNullOrEmpty(originalText)) return 0;

            // Get the rect of the preview text area from the overlay (this object)
            RectTransform overlayRect = GetComponent<RectTransform>();
            if (overlayRect == null) return originalText.Length;

            // Get canvas for reference
            Canvas canvas = _previewText.canvas;
            if (canvas == null) return originalText.Length;

            // Get render texture dimensions
            var rt = canvas.worldCamera?.targetTexture;
            if (rt == null) return originalText.Length;

            // Calculate relative X position (0-1) within the text area
            // screenPosition is in RenderTexture coordinates
            Rect overlayWorldRect = GetWorldRect(overlayRect);

            // Convert screen position to normalized position within overlay
            float normalizedX = (screenPosition.x - overlayWorldRect.xMin) / overlayWorldRect.width;
            normalizedX = Mathf.Clamp01(normalizedX);

            // Map to character index
            int estimatedIndex = Mathf.RoundToInt(normalizedX * originalText.Length);
            return Mathf.Clamp(estimatedIndex, 0, originalText.Length);
        }

        private Rect GetWorldRect(RectTransform rectTransform)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            // Get canvas for conversion
            Canvas canvas = rectTransform.GetComponentInParent<Canvas>();
            Camera cam = canvas?.worldCamera;

            if (cam != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    corners[i] = cam.WorldToScreenPoint(corners[i]);
                }
            }

            float xMin = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float xMax = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float yMin = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float yMax = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);

            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }
    }

}
