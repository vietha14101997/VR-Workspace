using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.UI.Utilities;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// RTTMediaLibrary partial: Edit mode toggling, button visuals, selection state, edit action buttons.
    /// </summary>
    public partial class RTTMediaLibrary
    {
        private void ToggleEditMode()
        {
            _isEditMode = !_isEditMode;

            // Toggle visibility
            if (_breadcrumbContainer != null)
                _breadcrumbContainer.gameObject.SetActive(!_isEditMode);
            if (_editControlsContainer != null)
                _editControlsContainer.SetActive(_isEditMode);
            if (_itemCountText != null)
                _itemCountText.gameObject.SetActive(!_isEditMode);
            if (_selectedCountText != null)
                _selectedCountText.gameObject.SetActive(_isEditMode);

            // Update edit button icon
            UpdateEditButtonVisual();

            // Show/hide checkboxes on items
            _grid?.SetEditMode(_isEditMode);

            // Handle detail panel and action bar visibility based on edit mode
            if (_isEditMode)
            {
                // Entering edit mode: clear controller's selected video state
                _controller?.ClearSelectedVideo();

                // Clear detail panel and hide action bar
                _detailPanel?.ShowEmpty();
                _mediaActionBar?.SetVisible(false);

                // Update selected count when entering edit mode
                UpdateSelectedCountText();
            }
            else
            {
                // Exiting edit mode: clear checkbox selection
                ClearSelection();
                UpdateSelectAllCheckmark();
                UpdateSelectedCountText();

                // Restore detail panel if there was a selected video
                if (_hasSelectedVideo && _currentVideo.HasValue)
                {
                    var mockFile = ConvertToMockFile(_currentVideo.Value);
                    _detailPanel?.UpdateInfo(mockFile, isCurrentFolder: false);
                    _mediaActionBar?.SetVisible(true);
                    // Restore controller's selected video so hover/unhover works correctly
                    _controller?.SelectVideo(_currentVideo.Value);
                }
                else
                {
                    // Check if grid has a visually selected video
                    var selectedVideo = _grid?.SelectedVideo;
                    if (selectedVideo.HasValue)
                    {
                        // Restore _hasSelectedVideo since there's a visually selected item
                        _hasSelectedVideo = true;
                        _currentVideo = selectedVideo.Value;

                        var mockFile = ConvertToMockFile(selectedVideo.Value);
                        _detailPanel?.UpdateInfo(mockFile, isCurrentFolder: false);
                        _mediaActionBar?.SetVisible(true);
                        // Restore controller's selected video
                        _controller?.SelectVideo(selectedVideo.Value);
                    }
                    else
                    {
                        // No selected video, show empty detail panel
                        _detailPanel?.ShowEmpty();
                    }
                }
            }
        }

        private void UpdateEditButtonVisual()
        {
            if (_editButton == null) return;

            // Determine target color based on mode (Edit = accent, normal = primary)
            Color themeColor = _isEditMode ? _accentColor : _primaryColor;
            Color glowColor = Color.Lerp(themeColor, Color.white, 0.75f);

            // 1. Update icon sprite and tint color
            var iconTransform = _editButton.transform.Find("HitArea/Visuals/Content/Icon");
            if (iconTransform != null)
            {
                var iconImg = iconTransform.GetComponent<Image>();
                if (iconImg != null)
                {
                    iconImg.sprite = Resources.Load<Sprite>(_isEditMode ? "icon_check_mark" : "icon_edit");
                    iconImg.color = UIGlowEffects.CreateIconTintColor(themeColor);
                }
                // Update icon shadow glow colors
                UIGlowEffects.UpdateShadowColors(iconTransform.gameObject, themeColor);
            }

            // 2. Update background material colors
            var bgTransform = _editButton.transform.Find("HitArea/Visuals/Background");
            if (bgTransform != null)
            {
                var bgImage = bgTransform.GetComponent<Image>();
                if (bgImage != null && bgImage.material != null)
                {
                    // Match MaterialFactory.CreateGlassBackground color formula
                    float backgroundAlpha = 0.08f;
                    Color colorA = new Color(themeColor.r, themeColor.g, themeColor.b, backgroundAlpha * 1.5f);
                    Color colorB = new Color(themeColor.r, themeColor.g, themeColor.b, backgroundAlpha * 0.5f);
                    bgImage.material.SetColor("_ColorA", colorA);
                    bgImage.material.SetColor("_ColorB", colorB);
                }
            }

            // 3. Update border glow color
            var borderTransform = _editButton.transform.Find("HitArea/Visuals/Border");
            if (borderTransform != null)
            {
                var borderImage = borderTransform.GetComponent<Image>();
                if (borderImage != null && borderImage.material != null && borderImage.material.HasProperty("_GlowColor"))
                {
                    borderImage.material.SetColor("_GlowColor", glowColor);
                }
            }

            // 4. Update GlowBorderHoverEffect's saved color so it persists through hover state changes
            var hoverController = _editButton.transform.Find("HitArea")?.GetComponent<HoverEffectController>();
            if (hoverController != null)
            {
                var glowEffect = hoverController.GetEffect("glow_border") as GlowBorderHoverEffect;
                glowEffect?.UpdateSavedGlowColor(glowColor);
            }
        }

        private void UpdateSelectedCountText()
        {
            if (_selectedCountText != null)
            {
                int count = _selectedItems.Count;
                _selectedCountText.text = count > 0 ? $"{count} selected" : "";
            }
        }

        private void UpdateActionButtonsState()
        {
            bool hasSelection = _selectedItems.Count > 0;
            bool singleSelection = _selectedItems.Count == 1;

            // Rename requires single selection
            SetButtonEnabled(_renameButton, _renameButtonCG, _renameButtonHover, singleSelection);

            // Delete requires any selection
            SetButtonEnabled(_deleteButton, _deleteButtonCG, _deleteButtonHover, hasSelection);
        }

        private void SetButtonEnabled(Button btn, CanvasGroup cg, HoverEffectController hover, bool enabled)
        {
            if (btn != null) btn.interactable = enabled;
            if (cg != null)
            {
                cg.alpha = enabled ? 1f : 0.4f;
                cg.interactable = enabled;
                cg.blocksRaycasts = enabled;
            }
            if (hover != null) hover.enabled = enabled;

            var collider = btn?.GetComponent<BoxCollider>();
            if (collider != null) collider.enabled = enabled;
        }

        private void UpdateSelectAllCheckmark()
        {
            if (_selectAllCheckmark == null) return;
            bool allSelected = _grid != null && _grid.AreAllSelected();
            _selectAllCheckmark.gameObject.SetActive(allSelected);
        }

        public void ClearSelection()
        {
            _selectedItems.Clear();
            _grid?.SetAllSelected(false);
            UpdateSelectAllCheckmark();
            UpdateSelectedCountText();
            UpdateActionButtonsState();
        }

        public void AddToSelection(string videoPath)
        {
            _selectedItems.Add(videoPath);
            UpdateSelectAllCheckmark();
            UpdateSelectedCountText();
            UpdateActionButtonsState();
        }

        public void RemoveFromSelection(string videoPath)
        {
            _selectedItems.Remove(videoPath);
            UpdateSelectAllCheckmark();
            UpdateSelectedCountText();
            UpdateActionButtonsState();
        }

        public bool IsSelected(string videoPath)
        {
            return _selectedItems.Contains(videoPath);
        }

        public System.Collections.Generic.HashSet<string> GetSelectedItems()
        {
            return new System.Collections.Generic.HashSet<string>(_selectedItems);
        }

        private void CreateSelectAllCheckbox(Transform parent)
        {
            float checkboxSize = 50f;
            float labelWidth = 150f;
            float spacing = 20f;  // Doubled spacing between checkbox and label
            float leftPadding = 20f;

            GameObject container = new GameObject("SelectAllContainer");
            container.transform.SetParent(parent, false);
            RectTransform containerRT = container.AddComponent<RectTransform>();
            containerRT.anchorMin = containerRT.anchorMax = new Vector2(0, 0.5f);
            containerRT.pivot = new Vector2(0, 0.5f);
            containerRT.sizeDelta = new Vector2(checkboxSize + spacing + labelWidth, checkboxSize);
            containerRT.anchoredPosition = new Vector2(leftPadding, 0);

            // Checkbox button
            _selectAllCheckbox = new GameObject("Checkbox");
            _selectAllCheckbox.transform.SetParent(container.transform, false);
            RectTransform checkboxRT = _selectAllCheckbox.AddComponent<RectTransform>();
            checkboxRT.anchorMin = checkboxRT.anchorMax = new Vector2(0, 0.5f);
            checkboxRT.pivot = new Vector2(0, 0.5f);
            checkboxRT.sizeDelta = new Vector2(checkboxSize, checkboxSize);
            checkboxRT.anchoredPosition = Vector2.zero;

            Image checkboxBg = _selectAllCheckbox.AddComponent<Image>();
            checkboxBg.raycastTarget = true;
            checkboxBg.sprite = GetRoundedRectSprite();
            checkboxBg.type = Image.Type.Sliced;
            checkboxBg.color = new Color(0f, 0f, 0f, 0.3f);  // Same as item hover color

            // Checkmark icon
            GameObject checkmarkObj = new GameObject("Checkmark");
            checkmarkObj.transform.SetParent(_selectAllCheckbox.transform, false);
            RectTransform checkmarkRT = checkmarkObj.AddComponent<RectTransform>();
            checkmarkRT.anchorMin = new Vector2(0.15f, 0.15f);
            checkmarkRT.anchorMax = new Vector2(0.85f, 0.85f);
            checkmarkRT.offsetMin = Vector2.zero;
            checkmarkRT.offsetMax = Vector2.zero;

            _selectAllCheckmark = checkmarkObj.AddComponent<Image>();
            _selectAllCheckmark.sprite = Resources.Load<Sprite>("icon_check_mark");
            _selectAllCheckmark.color = Color.white;
            _selectAllCheckmark.preserveAspect = true;
            checkmarkObj.SetActive(false);

            // Button
            Button checkboxBtn = _selectAllCheckbox.AddComponent<Button>();
            checkboxBtn.transition = Selectable.Transition.None;
            checkboxBtn.onClick.AddListener(OnSelectAllClicked);

            // VR Collider
            var collider = _selectAllCheckbox.AddComponent<BoxCollider>();
            collider.size = new Vector3(checkboxSize, checkboxSize, 10);
            collider.center = new Vector3(checkboxSize / 2f, 0, -5);

            // Hover effect
            var hoverController = _selectAllCheckbox.AddComponent<HoverEffectController>();
            hoverController.TargetVisuals = _selectAllCheckbox.transform;
            var scaleEffect = new ScaleHoverEffect()
                .WithHoverScale(1.1f)
                .WithTransitionDuration(0.1f);
            hoverController.AddEffect(scaleEffect);

            // Label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(container.transform, false);
            RectTransform labelRT = labelObj.AddComponent<RectTransform>();
            labelRT.anchorMin = labelRT.anchorMax = new Vector2(0, 0.5f);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.sizeDelta = new Vector2(labelWidth, checkboxSize);
            labelRT.anchoredPosition = new Vector2(checkboxSize + spacing, 0);

            TextMeshProUGUI labelTMP = labelObj.AddComponent<TextMeshProUGUI>();
            labelTMP.text = "Select all";
            labelTMP.font = _font;
            labelTMP.fontSize = 28;
            labelTMP.fontStyle = FontStyles.Bold;
            labelTMP.color = Color.white;
            labelTMP.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private GameObject CreateEditModeActionButton(Transform parent, string label, string iconName, UnityEngine.Events.UnityAction onClick, float minTextWidth = 80f)
        {
            float btnHeight = 75f;
            float iconSize = 35f;
            float padding = 22f;
            float spacing = 11f;
            float textWidth = Mathf.Max(minTextWidth, label.Length * 18f);
            float btnWidth = padding + iconSize + spacing + textWidth + padding;

            GameObject btnObj = new GameObject($"Btn_{label}");
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(btnWidth, btnHeight);

            // Glass background
            Image bg = btnObj.AddComponent<Image>();
            Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (glassShader != null)
            {
                Material mat = new Material(glassShader);
                mat.SetFloat("_Aspect", btnWidth / btnHeight);
                mat.SetFloat("_CornerRadius", 0.48f);
                mat.SetFloat("_EdgePadding", 0.02f);
                mat.SetColor("_ColorA", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.25f));
                mat.SetColor("_ColorB", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.1f));
                mat.SetFloat("_GlassAlpha", 0.2f);
                mat.SetFloat("_FresnelStrength", 0.15f);
                bg.material = mat;
                bg.color = Color.white;
            }
            else
            {
                bg.color = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.2f);
            }

            // Button
            Button btn = btnObj.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(onClick);

            // Hover effects (Scale + Background Color)
            var hoverController = btnObj.AddComponent<HoverEffectController>();
            hoverController.TargetVisuals = btnObj.transform; // Set before adding effects

            var scaleEffect = new ScaleHoverEffect()
                .WithHoverScale(1.05f)
                .WithTransitionDuration(0.15f);
            hoverController.AddEffect(scaleEffect);

            // Background color change on hover (keep alpha, change RGB to accent)
            Material btnMaterial = bg.material;
            Color normalColorA = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.25f);
            Color normalColorB = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.1f);
            Color hoverColorA = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.3f);
            Color hoverColorB = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.1f);

            hoverController.OnHoverStateChanged += (isHovered) =>
            {
                if (btnMaterial != null)
                {
                    btnMaterial.SetColor("_ColorA", isHovered ? hoverColorA : normalColorA);
                    btnMaterial.SetColor("_ColorB", isHovered ? hoverColorB : normalColorB);
                }
            };

            // Icon
            Sprite iconSprite = Resources.Load<Sprite>(iconName);
            if (iconSprite != null)
            {
                GameObject iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(btnObj.transform, false);
                RectTransform iconRT = iconObj.AddComponent<RectTransform>();
                iconRT.anchorMin = new Vector2(0, 0.5f);
                iconRT.anchorMax = new Vector2(0, 0.5f);
                iconRT.pivot = new Vector2(0, 0.5f);
                iconRT.sizeDelta = new Vector2(iconSize, iconSize);
                iconRT.anchoredPosition = new Vector2(padding, 0);

                Image iconImg = iconObj.AddComponent<Image>();
                iconImg.sprite = iconSprite;
                iconImg.color = Color.white;
                iconImg.preserveAspect = true;
            }

            // Text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = new Vector2(0, 0);
            textRT.anchorMax = new Vector2(1, 1);
            textRT.offsetMin = new Vector2(padding + iconSize + spacing, 0);
            textRT.offsetMax = new Vector2(-padding, 0);

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.font = _font;
            tmp.fontSize = 28;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            // VR Collider
            var collider = btnObj.AddComponent<BoxCollider>();
            collider.size = new Vector3(btnWidth, btnHeight, 10);
            collider.center = new Vector3(btnWidth / 2f, 0, -5);

            return btnObj;
        }
    }
}
