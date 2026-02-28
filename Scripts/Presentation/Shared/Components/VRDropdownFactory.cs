using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.Utilities;
using VRWorkspace.UI.Config;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.UI.Components
{
    /// <summary>
    /// Factory class để tạo VR Dropdown với đầy đủ hiệu ứng:
    /// - Glass background với gradient
    /// - Glowing border với hover effect
    /// - Icon bên trái (1/3 box)
    /// - Label nhỏ phía trên + Value bên dưới + Arrow (2/3 box)
    /// - Dropdown panel với các option
    /// - VRButtonAnimation cho hover effects
    /// - BoxCollider cho VR raycast
    ///
    /// Chiều cao và icon size tự động tính từ font size:
    /// - Box height = valueFontSize * 3.5
    /// - Icon size = valueFontSize * 1.5
    /// </summary>
    public static partial class VRDropdownFactory
    {
        private static Sprite _horizontalFadeSprite;

        // Hằng số layout - now using UIConstants where possible
        private const float FONT_TO_BOX_RATIO = UIConstants.DropdownFontToBoxRatio;
        private const float FONT_TO_ICON_RATIO = UIConstants.DropdownFontToIconRatio;
        private const float ICON_ZONE_RATIO = UIConstants.DropdownIconZoneRatio;
        private const float CONTENT_PADDING = UIConstants.DropdownContentPadding;
        private const float ARROW_WIDTH = UIConstants.DropdownArrowWidth;
        private const float CONTENT_LEFT_OFFSET = UIConstants.DropdownContentLeftOffset;
        private const float ARROW_RIGHT_OFFSET = UIConstants.DropdownArrowRightOffset;

        /// <summary>
        /// Cấu hình cho Dropdown
        /// </summary>
        [System.Serializable]
        public class DropdownConfig
        {
            public string label = "Label";
            public Sprite icon;
            public Color themeColor = UIConstants.DefaultPrimaryColor;
            public float width = 300f;
            public int labelFontSize = 32;
            public int valueFontSize = 36;
            public TMP_FontAsset font;

            // Options
            public List<string> options = new List<string>();
            public List<Sprite> optionIcons = new List<Sprite>();
            public List<float> optionIconSizeMultipliers = new List<float>(); // Multiplier for each option's icon size
            public int defaultIndex = 0;

            // Visual settings
            public float cornerRadius = UIConstants.ButtonCornerRadius;
            public float edgePadding = UIConstants.ButtonEdgePadding;
            public float backgroundAlpha = UIConstants.ButtonBackgroundAlpha;
            public float borderWidth = UIConstants.DropdownBorderWidth;
            public float glowWidth = UIConstants.ButtonGlowWidth;
            public float glowIntensity = UIConstants.ButtonGlowIntensity;

            // Glassmorphism settings (uses GlassGradientBackgroundOverlay with higher Queue)
            public bool enableGlassmorphism = true;
            public float blurIntensity = 6;
            public int blurQuality = 8;
            public float glassOpacity = 0f;
            public float tintStrength = 0.1f;
            public float innerGlow = 0f;
            public float brightness = 1f;
            public float saturation = 1f;

            // Animation
            public float popAmount = UIConstants.DropdownPopAmount;

            // Dropdown panel
            public int maxVisibleOptions = UIConstants.DropdownMaxVisibleOptions;

            // Layer
            public string layerName = UIConstants.VirtualObjectsLayer;

            // Tính chiều cao box từ font size
            public float BoxHeight => valueFontSize * (string.IsNullOrEmpty(label) ? 1.8f : FONT_TO_BOX_RATIO);

            // Tính icon size từ font size
            public float IconSize => valueFontSize * FONT_TO_ICON_RATIO * 0.95f;

            // Tính chiều cao option = 0.75 chiều cao dropdown
            public float OptionHeight => BoxHeight * 0.75f;

            // Tính icon size trong option (tương ứng với chiều cao option)
            public float OptionIconSize => OptionHeight * 0.5f;
        }

        /// <summary>
        /// Tạo VR Dropdown với đầy đủ hiệu ứng
        /// Chiều cao tự động tính từ font size
        /// </summary>
        public static GameObject CreateDropdown(Transform parent, DropdownConfig config,
            System.Action<int, string> onValueChanged = null)
        {
            float boxHeight = config.BoxHeight;

            // 1. Wrapper
            GameObject wrapper = new GameObject("Dropdown_" + config.label);
            wrapper.transform.SetParent(parent, false);
            RectTransform wrapperRT = wrapper.AddComponent<RectTransform>();
            wrapperRT.sizeDelta = new Vector2(config.width, boxHeight);

            // 2. HitArea - vùng click/collider
            GameObject hitArea = new GameObject("HitArea");
            hitArea.transform.SetParent(wrapper.transform, false);
            RectTransform hitRT = UIElementBuilder.CreateFullStretch(hitArea, wrapper.transform);

            // Setup hit area with utilities
            var (hitImg, col) = UIElementBuilder.SetupHitArea(hitArea, config.width, boxHeight, config.layerName);

            // 3. Visuals - container cho visual elements
            GameObject visuals = new GameObject("Visuals");
            UIElementBuilder.CreateFullStretch(visuals, hitArea.transform);

            // 4. Background
            Image bgImg = CreateBackground(visuals.transform, config);

            // 5. Border với ripple effect
            CreateBorder(visuals.transform, config);

            // 6. Content (Icon + Label + Value + Arrow)
            TextMeshProUGUI valueTxt = CreateContent(visuals.transform, config);

            // 7. Button component
            Button btn = hitArea.AddComponent<Button>();
            btn.targetGraphic = bgImg;
            btn.transition = Selectable.Transition.None;

            // 8. Hover effects - using unified HoverEffectController
            HoverEffectController hoverController = hitArea.AddComponent<HoverEffectController>();
            hoverController.TargetVisuals = visuals.transform;

            // Add glow border and background effects
            hoverController.AddEffect(new GlowBorderHoverEffect()
                .WithShaderSwap(true)
                .WithBorderMultiplier(1f));

            hoverController.AddEffect(new BackgroundHoverEffect());

            if (config.popAmount > 0)
            {
                hoverController.AddEffect(new ZPopHoverEffect()
                    .WithPopAmount(config.popAmount));
            }

            // 9. VRButtonAnimation for ripple click effect
            VRButtonAnimation anim = hitArea.AddComponent<VRButtonAnimation>();
            anim.targetVisuals = visuals.transform;

            // 10. VRDropdown component TRƯỚC khi tạo panel
            VRDropdown dropdown = wrapper.AddComponent<VRDropdown>();

            // 11. Dropdown Panel
            GameObject dropdownPanel = CreateDropdownPanel(wrapper.transform, config, valueTxt, onValueChanged);
            dropdownPanel.SetActive(false);

            // 12. Initialize dropdown component
            dropdown.Initialize(config.options, config.defaultIndex, valueTxt, dropdownPanel, onValueChanged);

            // 13. Toggle dropdown on click (use ToggleDropdown to handle force hover)
            btn.onClick.AddListener(() =>
            {
                dropdown.ToggleDropdown();
            });

            return wrapper;
        }

        /// <summary>
        /// Tạo Dropdown với icon (như Monitors, Bitrate trong hình)
        /// Chiều cao tự động tính từ font size
        /// </summary>
        public static GameObject CreateIconDropdown(Transform parent, float width,
            string label, Sprite icon, Color color, List<string> options, int defaultIndex,
            System.Action<int, string> onValueChanged = null,
            int labelFontSize = 24, int valueFontSize = 36, TMP_FontAsset font = null)
        {
            var config = new DropdownConfig
            {
                label = label,
                icon = icon,
                themeColor = color,
                width = width,
                labelFontSize = labelFontSize,
                valueFontSize = valueFontSize,
                font = font,
                options = options,
                defaultIndex = defaultIndex
            };
            return CreateDropdown(parent, config, onValueChanged);
        }

        /// <summary>
        /// Tạo Dropdown với icon và optionIcons riêng cho từng option
        /// Chiều cao tự động tính từ font size
        /// </summary>
        public static GameObject CreateIconDropdownWithOptionIcons(Transform parent, float width,
            string label, Sprite icon, Color color, List<string> options, List<Sprite> optionIcons, int defaultIndex,
            System.Action<int, string> onValueChanged = null,
            int labelFontSize = 24, int valueFontSize = 36, TMP_FontAsset font = null,
            List<float> optionIconSizeMultipliers = null)
        {
            var config = new DropdownConfig
            {
                label = label,
                icon = icon,
                themeColor = color,
                width = width,
                labelFontSize = labelFontSize,
                valueFontSize = valueFontSize,
                font = font,
                options = options,
                optionIcons = optionIcons,
                optionIconSizeMultipliers = optionIconSizeMultipliers ?? new List<float>(),
                defaultIndex = defaultIndex
            };
            return CreateDropdown(parent, config, onValueChanged);
        }

        /// <summary>
        /// Tạo Dropdown đơn giản không có icon
        /// Chiều cao tự động tính từ font size
        /// </summary>
        public static GameObject CreateSimpleDropdown(Transform parent, float width,
            string label, Color color, List<string> options, int defaultIndex,
            System.Action<int, string> onValueChanged = null,
            int labelFontSize = 24, int valueFontSize = 36, TMP_FontAsset font = null)
        {
            var config = new DropdownConfig
            {
                label = label,
                icon = null,
                themeColor = color,
                width = width,
                labelFontSize = labelFontSize,
                valueFontSize = valueFontSize,
                font = font,
                options = options,
                defaultIndex = defaultIndex
            };
            return CreateDropdown(parent, config, onValueChanged);
        }

        /// <summary>
        /// Tính chiều cao của Dropdown dựa trên font size
        /// </summary>
        public static float CalculateHeight(int valueFontSize)
        {
            return valueFontSize * FONT_TO_BOX_RATIO;
        }

        /// <summary>
        /// Lấy VRDropdown component từ wrapper object
        /// </summary>
        public static VRDropdown GetDropdown(GameObject wrapper)
        {
            return wrapper.GetComponent<VRDropdown>();
        }

        /// <summary>
        /// Lấy index được chọn từ wrapper object
        /// </summary>
        public static int GetSelectedIndex(GameObject wrapper)
        {
            var dropdown = GetDropdown(wrapper);
            return dropdown != null ? dropdown.SelectedIndex : -1;
        }

        /// <summary>
        /// Lấy giá trị được chọn từ wrapper object
        /// </summary>
        public static string GetSelectedValue(GameObject wrapper)
        {
            var dropdown = GetDropdown(wrapper);
            return dropdown != null ? dropdown.SelectedValue : "";
        }

        /// <summary>
        /// Đặt giá trị được chọn theo index
        /// </summary>
        public static void SetSelectedIndex(GameObject wrapper, int index)
        {
            var dropdown = GetDropdown(wrapper);
            if (dropdown != null)
            {
                dropdown.SetSelectedIndex(index);
            }
        }

        /// <summary>
        /// Đặt danh sách options mới cho dropdown
        /// </summary>
        public static void SetOptions(GameObject wrapper, List<string> options, int selectedIndex = 0)
        {
            var dropdown = GetDropdown(wrapper);
            if (dropdown != null)
            {
                dropdown.SetOptions(options, selectedIndex);
            }
        }

        /// <summary>
        /// Lock/unlock a specific option in a dropdown to prevent/allow selection.
        /// </summary>
        public static void SetOptionLocked(GameObject wrapper, int index, bool locked)
        {
            var dropdown = GetDropdown(wrapper);
            dropdown?.SetOptionLocked(index, locked);
        }

        /// <summary>
        /// Enable/disable dropdown interaction với visual feedback
        /// </summary>
        public static void SetInteractable(GameObject wrapper, bool interactable)
        {
            if (wrapper == null) return;

            // Find HitArea button
            var hitArea = wrapper.transform.Find("HitArea");
            if (hitArea != null)
            {
                var button = hitArea.GetComponent<Button>();
                if (button != null) button.interactable = interactable;
            }

            // Visual feedback - dim when disabled
            var canvasGroup = wrapper.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = wrapper.AddComponent<CanvasGroup>();
            canvasGroup.alpha = interactable ? 1f : 0.5f;
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = interactable;
        }

    }

    /// <summary>
    /// Component quản lý state của VR Dropdown
    /// </summary>
    public class VRDropdown : MonoBehaviour
    {
        private List<string> _options;
        private int _selectedIndex;
        private TextMeshProUGUI _valueTxt;
        private GameObject _dropdownPanel;
        private System.Action<int, string> _onValueChanged;
        private HoverEffectController _hoverController;  // Reference to hover controller for dropdown button
        private HashSet<int> _lockedOptions = new HashSet<int>();  // Per-option lock state

        // For RTT mode: create a world-space floating panel to avoid RenderTexture clipping
        private RTTCanvasBase _rttCanvasBase;
        private GameObject _worldSpaceDropdownRoot;  // Root object for world-space dropdown
        private Canvas _worldSpaceCanvas;
        private bool _isRTTMode = false;
        private bool _rttModeChecked = false;

        // Static reference to currently open dropdown (for VRGazeReticle to check)
        public static VRDropdown CurrentlyOpenDropdown { get; private set; }

        private class OptionRef
        {
            public Image background;
            public Image checkmark;
            public Color themeColor;
            public HoverEffectController hoverController;
        }
        private Dictionary<int, OptionRef> _optionRefs = new Dictionary<int, OptionRef>();

        public int SelectedIndex => _selectedIndex;
        public GameObject DropdownPanel => _isRTTMode && _worldSpaceDropdownRoot != null ? _worldSpaceDropdownRoot : _dropdownPanel;
        public bool IsOpen => _isRTTMode ? _worldSpaceDropdownRoot != null : (_dropdownPanel != null && _dropdownPanel.activeSelf);
        public string SelectedValue => _options != null && _selectedIndex >= 0 && _selectedIndex < _options.Count
            ? _options[_selectedIndex] : "";

        public void Initialize(List<string> options, int defaultIndex, TextMeshProUGUI valueTxt,
            GameObject dropdownPanel, System.Action<int, string> onValueChanged)
        {
            _options = options;
            _selectedIndex = defaultIndex;
            _valueTxt = valueTxt;
            _dropdownPanel = dropdownPanel;
            _onValueChanged = onValueChanged;

            // Find HoverEffectController in HitArea child
            var hitArea = transform.Find("HitArea");
            if (hitArea != null)
            {
                _hoverController = hitArea.GetComponent<HoverEffectController>();
            }
        }

        public void RegisterOption(int index, Image background, Image checkmark, Color themeColor, HoverEffectController hoverController = null)
        {
            _optionRefs[index] = new OptionRef
            {
                background = background,
                checkmark = checkmark,
                themeColor = themeColor,
                hoverController = hoverController
            };
        }

        public void UpdateSelection(int newIndex)
        {
            foreach (var kvp in _optionRefs)
            {
                if (kvp.Value.background != null)
                {
                    kvp.Value.background.color = Color.clear;
                    var btn = kvp.Value.background.GetComponent<Button>();
                    if (btn != null)
                    {
                        var colors = btn.colors;
                        colors.normalColor = Color.clear;
                        btn.colors = colors;
                    }
                }
                if (kvp.Value.checkmark != null)
                {
                    kvp.Value.checkmark.color = Color.clear;
                }
                // Clear selected state for hover effect
                if (kvp.Value.hoverController != null)
                {
                    kvp.Value.hoverController.SetForceHover(false);
                }
            }

            _selectedIndex = newIndex;
            if (_optionRefs.TryGetValue(newIndex, out var optRef))
            {
                Color selectedBgColor = new Color(optRef.themeColor.r, optRef.themeColor.g, optRef.themeColor.b, 0.15f);
                if (optRef.background != null)
                {
                    optRef.background.color = selectedBgColor;
                    var btn = optRef.background.GetComponent<Button>();
                    if (btn != null)
                    {
                        var colors = btn.colors;
                        colors.normalColor = selectedBgColor;
                        btn.colors = colors;
                    }
                }
                if (optRef.checkmark != null)
                {
                    // Full white for maximum visibility
                    optRef.checkmark.color = Color.white;
                }
                // Set selected state for hover effect
                if (optRef.hoverController != null)
                {
                    optRef.hoverController.SetForceHover(true);
                }
            }
        }

        public void SetSelectedIndex(int index, bool bypassLock = false)
        {
            if (_options == null || index < 0 || index >= _options.Count) return;

            // Prevent selecting locked options (unless bypassed for programmatic use)
            if (!bypassLock && _lockedOptions.Contains(index)) return;

            UpdateSelection(index);
            // Strip "(Recommended)" and "(Locked)" suffixes from displayed value
            string displayValue = _options[index]
                .Replace(" (Recommended)", "")
                .Replace(" (Locked)", "")
                .Trim();
            if (_valueTxt != null)
            {
                _valueTxt.text = displayValue;
            }
            _onValueChanged?.Invoke(index, displayValue);
        }

        public void SetOptions(List<string> options, int selectedIndex = 0)
        {
            _options = options;

            // Update option labels in the dropdown panel
            UpdateOptionLabels();

            SetSelectedIndex(selectedIndex);
        }

        /// <summary>
        /// Lock/unlock a specific option to prevent/allow selection.
        /// Locked options are visually dimmed and cannot be clicked.
        /// </summary>
        public void SetOptionLocked(int index, bool locked)
        {
            if (locked)
                _lockedOptions.Add(index);
            else
                _lockedOptions.Remove(index);

            UpdateLockedOptionVisuals();
        }

        /// <summary>Check if a specific option is locked.</summary>
        public bool IsOptionLocked(int index) => _lockedOptions.Contains(index);

        /// <summary>
        /// Update visual state for locked options (dimmed alpha, non-interactable).
        /// </summary>
        private void UpdateLockedOptionVisuals()
        {
            if (_dropdownPanel == null) return;

            foreach (int lockedIdx in _lockedOptions)
            {
                Transform optionTransform = FindChildRecursive(_dropdownPanel.transform, "Option_" + lockedIdx);
                if (optionTransform == null) continue;

                // Dim the entire option
                var canvasGroup = optionTransform.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = optionTransform.gameObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0.35f;
                canvasGroup.interactable = false;

                // Add "(Locked)" suffix to text if not already present
                Transform textTransform = optionTransform.Find("Text");
                if (textTransform != null)
                {
                    var txt = textTransform.GetComponent<TextMeshProUGUI>();
                    if (txt != null && !txt.text.Contains("(Locked)"))
                    {
                        txt.text = txt.text + " (Locked)";
                    }
                }
            }

            // Ensure unlocked options are restored
            if (_options == null) return;
            for (int i = 0; i < _options.Count; i++)
            {
                if (_lockedOptions.Contains(i)) continue;

                Transform optionTransform = FindChildRecursive(_dropdownPanel.transform, "Option_" + i);
                if (optionTransform == null) continue;

                var canvasGroup = optionTransform.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1f;
                    canvasGroup.interactable = true;
                }
            }
        }

        /// <summary>
        /// Update the text labels in the dropdown panel to match current _options list.
        /// This ensures "(Recommended)" suffix is displayed correctly after SetOptions is called.
        /// </summary>
        private void UpdateOptionLabels()
        {
            if (_dropdownPanel == null || _options == null) return;

            // Find all option items in the dropdown panel
            // Structure: DropdownPanel > Viewport > Visuals > Content > Options > Option_X > Text
            for (int i = 0; i < _options.Count; i++)
            {
                // Find Option_X by name
                Transform optionTransform = FindChildRecursive(_dropdownPanel.transform, "Option_" + i);
                if (optionTransform != null)
                {
                    // Find Text child
                    Transform textTransform = optionTransform.Find("Text");
                    if (textTransform != null)
                    {
                        TextMeshProUGUI txt = textTransform.GetComponent<TextMeshProUGUI>();
                        if (txt != null)
                        {
                            txt.text = _options[i];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively find a child Transform by name
        /// </summary>
        private Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }

            return null;
        }

        public void CloseDropdown()
        {
            // Cleanup world-space dropdown if in RTT mode
            if (_isRTTMode)
            {
                CleanupWorldSpaceDropdown();
            }
            else if (_dropdownPanel != null)
            {
                _dropdownPanel.SetActive(false);
            }

            // Release force hover when panel closes
            if (_hoverController != null)
            {
                _hoverController.SetForceHover(false);
            }
            // Clear static reference
            if (CurrentlyOpenDropdown == this)
            {
                CurrentlyOpenDropdown = null;
            }
        }

        public void OpenDropdown()
        {
            // Close any other open dropdown first
            if (CurrentlyOpenDropdown != null && CurrentlyOpenDropdown != this)
            {
                CurrentlyOpenDropdown.CloseDropdown();
            }

            if (_dropdownPanel != null)
            {
                // Check if we're in RTT mode
                CheckRTTMode();

                if (_isRTTMode)
                {
                    // Create world-space dropdown to avoid RenderTexture clipping
                    CreateWorldSpaceDropdown();
                }
                else
                {
                    _dropdownPanel.SetActive(true);
                }
            }
            // Force hover when panel opens
            if (_hoverController != null)
            {
                _hoverController.SetForceHover(true);
            }
            // Set static reference
            CurrentlyOpenDropdown = this;
        }

        /// <summary>
        /// Check if we're inside an RTT (Render-to-Texture) canvas
        /// </summary>
        private void CheckRTTMode()
        {
            if (_rttModeChecked) return;
            _rttModeChecked = true;

            // Look for RTTCanvasBase in parents
            _rttCanvasBase = GetComponentInParent<RTTCanvasBase>();
            _isRTTMode = _rttCanvasBase != null;
        }

        /// <summary>
        /// Create a world-space Canvas for the dropdown panel that floats in front of the RTT DisplayQuad.
        /// This completely bypasses the RenderTexture clipping issue.
        /// </summary>
        private void CreateWorldSpaceDropdown()
        {
            if (_rttCanvasBase == null) return;

            // Get the DisplayQuad to position our world-space dropdown
            MeshRenderer displayQuad = _rttCanvasBase.GetDisplayQuad();
            if (displayQuad == null) return;

            // Get dropdown button's RectTransform
            RectTransform dropdownRT = GetComponent<RectTransform>();
            if (dropdownRT == null) return;

            // Get panel dimensions from the original panel
            RectTransform originalPanelRT = _dropdownPanel.GetComponent<RectTransform>();
            float panelWidth = originalPanelRT.rect.width;
            float panelHeight = originalPanelRT.rect.height;

            // If width is 0 (stretch anchors), use dropdown width
            if (panelWidth <= 0)
            {
                panelWidth = dropdownRT.rect.width;
            }
            if (panelHeight <= 0)
            {
                panelHeight = originalPanelRT.sizeDelta.y;
            }

            // Lấy UI Camera từ RTT
            Camera uiCamera = _rttCanvasBase.GetUICamera();
            if (uiCamera == null) return;

            // Lấy world corners của dropdown button
            Vector3[] worldCorners = new Vector3[4];
            dropdownRT.GetWorldCorners(worldCorners);
            // 0=bottom-left, 1=top-left, 2=top-right, 3=bottom-right

            // Tính bottom-center trong world space của UI Camera
            Vector3 bottomCenterWorld = (worldCorners[0] + worldCorners[3]) / 2f;

            // Chuyển sang viewport coordinates (0-1) của UI Camera
            Vector3 viewportPos = uiCamera.WorldToViewportPoint(bottomCenterWorld);

            // Viewport coordinates chính là normalized position trên DisplayQuad
            Vector2 bottomCenterNorm = new Vector2(viewportPos.x, viewportPos.y);

            // Get RTT resolution and world size
            Vector2Int rttResolution = _rttCanvasBase.CurrentResolution;
            Vector2 worldSize = _rttCanvasBase.GetWorldSize();

            // Calculate pixels per world unit
            float pixelsPerWorldUnitX = rttResolution.x / worldSize.x;
            float pixelsPerWorldUnitY = rttResolution.y / worldSize.y;

            // Calculate panel world dimensions
            float worldPanelWidth = panelWidth / pixelsPerWorldUnitX;
            float worldPanelHeight = panelHeight / pixelsPerWorldUnitY;

            // Map normalized canvas position to DisplayQuad world position
            // DisplayQuad is centered, so we map from (-0.5 to 0.5) * worldSize
            Vector3 quadCenter = displayQuad.transform.position;
            Vector3 quadRight = displayQuad.transform.right;
            Vector3 quadUp = displayQuad.transform.up;
            Vector3 quadForward = displayQuad.transform.forward;

            // Calculate bottom-center position on the quad in world space
            Vector3 dropdownBottomCenter = quadCenter
                + quadRight * (bottomCenterNorm.x - 0.5f) * worldSize.x
                + quadUp * (bottomCenterNorm.y - 0.5f) * worldSize.y;

            // Create world-space root object
            if (_worldSpaceDropdownRoot != null)
            {
                Object.Destroy(_worldSpaceDropdownRoot);
            }

            _worldSpaceDropdownRoot = new GameObject("WorldSpaceDropdown_" + gameObject.name);

            // Parent to VirtualObjects for better hierarchy organization
            GameObject virtualObjects = GameObject.Find("VirtualObjects");
            if (virtualObjects != null)
            {
                _worldSpaceDropdownRoot.transform.SetParent(virtualObjects.transform, true);
            }

            // Fix Issue 2: Calculate proper gap from original panel's anchoredPosition (-25f pixels)
            // Original panel has anchoredPosition = (0, -25f), need to convert this to world space
            float panelGapPixels = 25f; // From CreateDropdownPanel: anchoredPosition = new Vector2(0, -25f)
            float worldPanelGap = panelGapPixels / pixelsPerWorldUnitY;

            // Set layer to VirtualObjects for VR raycast
            int vrLayer = LayerMask.NameToLayer(UIConstants.VirtualObjectsLayer);
            if (vrLayer == -1) vrLayer = LayerMask.NameToLayer("Default");
            _worldSpaceDropdownRoot.layer = vrLayer;

            // Create World Space Canvas FIRST so we can set pivot before positioning
            _worldSpaceCanvas = _worldSpaceDropdownRoot.AddComponent<Canvas>();
            _worldSpaceCanvas.renderMode = RenderMode.WorldSpace;

            // Setup RectTransform for canvas with TOP-CENTER pivot (like original panel)
            // Original panel has pivot = (0.5, 1) which means position refers to top-center
            RectTransform worldCanvasRT = _worldSpaceDropdownRoot.GetComponent<RectTransform>();
            worldCanvasRT.pivot = new Vector2(0.5f, 1f); // Top-center pivot
            worldCanvasRT.sizeDelta = new Vector2(panelWidth, panelHeight);

            // Scale canvas to match world dimensions
            float scaleX = worldPanelWidth / panelWidth;
            float scaleY = worldPanelHeight / panelHeight;
            _worldSpaceDropdownRoot.transform.localScale = new Vector3(scaleX, scaleY, 1f);

            // Position with TOP-CENTER pivot: position is where top-center of panel will be
            // dropdownBottomCenter is the bottom of dropdown button
            // We want panel's top to be at dropdownBottomCenter - gap (slightly below dropdown)
            Vector3 panelPosition = dropdownBottomCenter - quadForward * 0.005f; // 5mm in front
            panelPosition -= quadUp * worldPanelGap; // Only gap, no half-height since pivot is at top

            _worldSpaceDropdownRoot.transform.position = panelPosition;
            _worldSpaceDropdownRoot.transform.rotation = displayQuad.transform.rotation;

            // Add CanvasScaler for consistent sizing
            CanvasScaler scaler = _worldSpaceDropdownRoot.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 100f;

            // Add GraphicRaycaster for UI interaction
            _worldSpaceDropdownRoot.AddComponent<GraphicRaycaster>();

            // Note: No BoxCollider on the root - each option already has its own BoxCollider for VR raycast

            // Clone the dropdown panel content to this world-space canvas
            GameObject clonedPanel = Object.Instantiate(_dropdownPanel, _worldSpaceDropdownRoot.transform);
            clonedPanel.name = "DropdownPanelClone";

            // Setup cloned panel RectTransform
            RectTransform clonedRT = clonedPanel.GetComponent<RectTransform>();
            clonedRT.anchorMin = Vector2.zero;
            clonedRT.anchorMax = Vector2.one;
            clonedRT.offsetMin = Vector2.zero;
            clonedRT.offsetMax = Vector2.zero;
            clonedRT.localPosition = Vector3.zero;
            clonedRT.localRotation = Quaternion.identity;
            clonedRT.localScale = Vector3.one;

            // Set layer recursively
            SetLayerRecursively(_worldSpaceDropdownRoot, vrLayer);

            // Activate the cloned panel
            clonedPanel.SetActive(true);

            // Re-register option click handlers for the cloned panel
            ReconnectClonedPanelHandlers(clonedPanel);
        }

        /// <summary>
        /// Reconnect click handlers for cloned dropdown panel options
        /// </summary>
        private void ReconnectClonedPanelHandlers(GameObject clonedPanel)
        {
            // Find all buttons in the cloned panel and reconnect their handlers
            Button[] buttons = clonedPanel.GetComponentsInChildren<Button>(true);

            int optionIndex = 0;
            foreach (Button btn in buttons)
            {
                // Skip if this is a scroll rect or other non-option button
                if (!btn.gameObject.name.StartsWith("Option_")) continue;

                // Parse index from name
                string indexStr = btn.gameObject.name.Replace("Option_", "");
                if (int.TryParse(indexStr, out int idx))
                {
                    int capturedIndex = idx;
                    string optionText = _options != null && capturedIndex < _options.Count ? _options[capturedIndex] : "";

                    // Clear existing listeners and add new one
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() =>
                    {
                        // Strip "(Recommended)" suffix from displayed value
                        string displayValue = optionText.Replace(" (Recommended)", "").Trim();
                        if (_valueTxt != null)
                        {
                            _valueTxt.text = displayValue;
                        }
                        UpdateSelection(capturedIndex);
                        CloseDropdown();
                        _onValueChanged?.Invoke(capturedIndex, displayValue);
                    });

                    // Re-add hover effects to cloned HoverEffectController
                    // When panel is cloned, _activeEffects (runtime list) is lost
                    HoverEffectController hoverController = btn.GetComponent<HoverEffectController>();
                    if (hoverController != null)
                    {
                        // Get theme color from button's highlightedColor (was set during CreateOptionItem)
                        Color themeColor = btn.colors.highlightedColor;
                        // Reconstruct the full color with stronger hover intensity
                        Color bgHoverColor = new Color(themeColor.r, themeColor.g, themeColor.b, 0.55f);

                        // Re-add background color effect
                        hoverController.AddEffect(new ColorHoverEffect()
                            .WithTargetChild("")
                            .WithHoverColor(bgHoverColor));

                        // Set selected state for the currently selected option
                        bool isSelected = (capturedIndex == _selectedIndex);
                        hoverController.SetForceHover(isSelected);
                    }

                    optionIndex++;
                }
            }
        }

        /// <summary>
        /// Cleanup world-space dropdown when closing
        /// </summary>
        private void CleanupWorldSpaceDropdown()
        {
            if (_worldSpaceDropdownRoot != null)
            {
                Object.Destroy(_worldSpaceDropdownRoot);
                _worldSpaceDropdownRoot = null;
                _worldSpaceCanvas = null;
            }
        }

        /// <summary>
        /// Tính vị trí center của một RectTransform trên Canvas (trong Canvas local space)
        /// Đi ngược hierarchy từ element lên đến canvas để tính tổng offset
        /// </summary>
        private Vector2 GetPositionOnCanvas(RectTransform element, RectTransform canvas)
        {
            // Sử dụng TransformPoint để chuyển đổi từ local space của element sang world space
            // Sau đó chuyển sang local space của canvas
            Vector3 worldPos = element.TransformPoint(Vector3.zero); // Center của element trong world
            Vector3 canvasLocalPos = canvas.InverseTransformPoint(worldPos);
            return new Vector2(canvasLocalPos.x, canvasLocalPos.y);
        }

        /// <summary>
        /// Recursively set layer for GameObject and all children
        /// </summary>
        private void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        public void ToggleDropdown()
        {
            // Use IsOpen property which handles both RTT and non-RTT modes
            if (IsOpen)
            {
                CloseDropdown();
            }
            else
            {
                OpenDropdown();
            }
        }

        /// <summary>
        /// Check if a GameObject is part of this dropdown's panel (option items)
        /// </summary>
        public bool IsPartOfDropdownPanel(GameObject obj)
        {
            if (obj == null) return false;

            // Check if obj is a child of the dropdown panel (non-RTT mode)
            if (_dropdownPanel != null)
            {
                Transform current = obj.transform;
                while (current != null)
                {
                    if (current.gameObject == _dropdownPanel)
                    {
                        return true;
                    }
                    current = current.parent;
                }
            }

            // Check if obj is a child of the world-space dropdown (RTT mode)
            if (_worldSpaceDropdownRoot != null)
            {
                Transform current = obj.transform;
                while (current != null)
                {
                    if (current.gameObject == _worldSpaceDropdownRoot)
                    {
                        return true;
                    }
                    current = current.parent;
                }
            }

            return false;
        }

        private void OnDisable()
        {
            CloseDropdown();
        }
    }

}
