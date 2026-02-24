using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// Group header component for RTTMediaGrid.
    /// Displays group title and optional select-all checkbox in edit mode.
    /// </summary>
    public class RTTMediaGroupHeader : MonoBehaviour
    {
        #region Constants
        public const float HEADER_HEIGHT = 60f;
        private const float CHECKBOX_SIZE = 40f;
        private const float PADDING_LEFT = 20f;
        private const float PADDING_RIGHT = 20f;
        #endregion

        #region Private Fields
        private RectTransform _rectTransform;
        private TextMeshProUGUI _titleText;
        private GameObject _checkboxContainer;
        private Image _checkboxBg;
        private Image _checkmarkImage;
        private Button _checkboxButton;

        private MediaGroupInfo _groupInfo;
        private bool _isEditMode = false;
        private bool _isSelected = false;

        private TMP_FontAsset _font;
        private Color _primaryColor;
        private Color _accentColor;

        private Action<MediaGroupInfo, bool> _onGroupSelectionChanged;
        #endregion

        #region Properties
        public MediaGroupInfo GroupInfo => _groupInfo;
        public bool IsSelected => _isSelected;
        #endregion

        #region Initialization
        public void Initialize(TMP_FontAsset font, Color primaryColor, Color accentColor)
        {
            _font = font;
            _primaryColor = primaryColor;
            _accentColor = accentColor;

            _rectTransform = GetComponent<RectTransform>();
            if (_rectTransform == null)
            {
                _rectTransform = gameObject.AddComponent<RectTransform>();
            }

            BuildUI();
        }

        private void BuildUI()
        {
            // Preserve width if already set, only set height
            float existingWidth = _rectTransform.sizeDelta.x;
            _rectTransform.sizeDelta = new Vector2(existingWidth > 0 ? existingWidth : 800f, HEADER_HEIGHT);

            // Title Text (left-aligned)
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(transform, false);
            _titleText = titleObj.AddComponent<TextMeshProUGUI>();
            _titleText.font = _font;
            _titleText.fontSize = 28;
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.color = Color.white;
            _titleText.alignment = TextAlignmentOptions.MidlineLeft;

            RectTransform titleRT = titleObj.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 0);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.offsetMin = new Vector2(PADDING_LEFT, 0);
            titleRT.offsetMax = new Vector2(-PADDING_RIGHT - CHECKBOX_SIZE - 10, 0);

            // Checkbox Container (right-aligned, hidden by default)
            _checkboxContainer = new GameObject("Checkbox");
            _checkboxContainer.transform.SetParent(transform, false);

            RectTransform checkboxRT = _checkboxContainer.AddComponent<RectTransform>();
            checkboxRT.anchorMin = new Vector2(1, 0.5f);
            checkboxRT.anchorMax = new Vector2(1, 0.5f);
            checkboxRT.pivot = new Vector2(1, 0.5f);
            checkboxRT.sizeDelta = new Vector2(CHECKBOX_SIZE, CHECKBOX_SIZE);
            checkboxRT.anchoredPosition = new Vector2(-PADDING_RIGHT, 0);

            // Checkbox Background (solid dark color matching item hover)
            _checkboxBg = _checkboxContainer.AddComponent<Image>();
            _checkboxBg.raycastTarget = true;
            _checkboxBg.sprite = GetRoundedRectSprite();
            _checkboxBg.type = Image.Type.Sliced;
            _checkboxBg.color = new Color(0f, 0f, 0f, 0.3f);  // Same as item hover color

            // Checkmark
            GameObject checkmarkObj = new GameObject("Checkmark");
            checkmarkObj.transform.SetParent(_checkboxContainer.transform, false);
            _checkmarkImage = checkmarkObj.AddComponent<Image>();
            _checkmarkImage.color = _accentColor;

            RectTransform checkmarkRT = checkmarkObj.GetComponent<RectTransform>();
            checkmarkRT.anchorMin = Vector2.zero;
            checkmarkRT.anchorMax = Vector2.one;
            checkmarkRT.offsetMin = new Vector2(8, 8);
            checkmarkRT.offsetMax = new Vector2(-8, -8);

            // Load checkmark sprite
            Sprite checkSprite = Resources.Load<Sprite>("icon_check");
            if (checkSprite != null)
            {
                _checkmarkImage.sprite = checkSprite;
            }

            // Button component for click handling
            _checkboxButton = _checkboxContainer.AddComponent<Button>();
            _checkboxButton.onClick.AddListener(OnCheckboxClicked);

            // Hover effect
            var hoverController = _checkboxContainer.AddComponent<HoverEffectController>();
            hoverController.TargetVisuals = _checkboxContainer.transform;
            var scaleEffect = new ScaleHoverEffect()
                .WithHoverScale(1.1f)
                .WithTransitionDuration(0.1f);
            hoverController.AddEffect(scaleEffect);

            // Hide checkbox by default (shown only in edit mode)
            _checkboxContainer.SetActive(false);
            _checkmarkImage.gameObject.SetActive(false);
        }
        #endregion

        #region Public Methods
        public void Bind(MediaGroupInfo groupInfo)
        {
            _groupInfo = groupInfo;
            _titleText.text = groupInfo?.Title ?? "";
            _isSelected = false;
            UpdateCheckmarkVisual();
        }

        public void SetEditMode(bool editMode)
        {
            _isEditMode = editMode;
            _checkboxContainer.SetActive(editMode);

            if (!editMode)
            {
                _isSelected = false;
                UpdateCheckmarkVisual();
            }
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            UpdateCheckmarkVisual();
        }

        public void SetSelectionCallback(Action<MediaGroupInfo, bool> callback)
        {
            _onGroupSelectionChanged = callback;
        }

        public void OnRecycle()
        {
            _groupInfo = null;
            _isSelected = false;
            UpdateCheckmarkVisual();
        }
        #endregion

        #region Private Methods
        private void OnCheckboxClicked()
        {
            _isSelected = !_isSelected;
            UpdateCheckmarkVisual();
            _onGroupSelectionChanged?.Invoke(_groupInfo, _isSelected);
        }

        private void UpdateCheckmarkVisual()
        {
            if (_checkmarkImage != null)
            {
                _checkmarkImage.gameObject.SetActive(_isSelected);
            }

            // Checkbox background uses solid color - no color change needed
            // The checkmark visibility is sufficient to indicate selection state
        }

        private Sprite GetRoundedRectSprite()
        {
            // Try to load a rounded rect sprite, or create a simple one
            Sprite sprite = Resources.Load<Sprite>("rounded_rect");
            if (sprite != null) return sprite;

            // Create a simple rounded rect texture
            int size = 32;
            int radius = 4;  // Small radius for square-ish checkbox with slight rounding
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Check if pixel is inside rounded rect
                    bool inside = true;
                    int dx = 0, dy = 0;

                    if (x < radius) dx = radius - x;
                    else if (x >= size - radius) dx = x - (size - radius - 1);

                    if (y < radius) dy = radius - y;
                    else if (y >= size - radius) dy = y - (size - radius - 1);

                    if (dx > 0 && dy > 0)
                    {
                        inside = (dx * dx + dy * dy) <= (radius * radius);
                    }

                    pixels[y * size + x] = inside ? Color.white : Color.clear;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            // Create sprite with proper border for 9-slicing
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
        #endregion
    }

}
