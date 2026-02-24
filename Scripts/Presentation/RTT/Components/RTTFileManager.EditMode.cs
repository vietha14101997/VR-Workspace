using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using System.Threading;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.Config;
using VRWorkspace.UI.Utilities;
using VRWorkspace.UI.RTT.Services;

namespace VRWorkspace.UI.RTT.Components
{
    public partial class RTTFileManager
    {
        private void CreateEditControlsInRow2(RectTransform parent)
        {
            // Select All Checkbox (at left edge)
            CreateSelectAllCheckbox(parent);

            // SearchBar dimensions for alignment (SearchBar is centered on row, width = 990)
            // EditControls container has offsetMin=(20,0), offsetMax=(-320,0)
            // Container center offset from row center = (20 + (-320)) / 2 = -150
            // So we need to compensate: searchBarLeft_in_container = searchBarLeft_from_row_center - container_offset
            float searchBarWidth = 990f;
            float searchBarLeftFromRowCenter = -searchBarWidth / 2f; // -495
            float containerCenterOffset = (20f + (-320f)) / 2f; // -150 (container center is 150px left of row center)
            float searchBarLeftInContainer = searchBarLeftFromRowCenter - containerCenterOffset; // -495 - (-150) = -345

            // Move buttons closer to "Select all" (shift left to reduce gap)
            float buttonsStartX = searchBarLeftInContainer - 90f;
            float btnSpacing = 15f;

            // Rename button - with wider width to prevent text wrapping
            var renameBtn = CreateEditModeActionButton(parent, "Rename", "icon_rename", OnRenameClicked, minTextWidth: 130f);
            var renameRT = renameBtn.GetComponent<RectTransform>();
            renameRT.anchorMin = renameRT.anchorMax = new Vector2(0.5f, 0.5f);
            renameRT.pivot = new Vector2(0, 0.5f);
            renameRT.anchoredPosition = new Vector2(buttonsStartX, 0);
            _renameButton = renameBtn.GetComponent<Button>();
            _renameButtonCG = renameBtn.AddComponent<CanvasGroup>();
            _renameButtonHover = renameBtn.GetComponent<HoverEffectController>();

            // Copy button
            var copyBtn = CreateEditModeActionButton(parent, "Copy", "icon_copy", OnCopyClicked);
            var copyRT = copyBtn.GetComponent<RectTransform>();
            copyRT.anchorMin = copyRT.anchorMax = new Vector2(0.5f, 0.5f);
            copyRT.pivot = new Vector2(0, 0.5f);
            float copyX = buttonsStartX + renameRT.sizeDelta.x + btnSpacing;
            copyRT.anchoredPosition = new Vector2(copyX, 0);
            _copyButton = copyBtn.GetComponent<Button>();
            _copyButtonCG = copyBtn.AddComponent<CanvasGroup>();
            _copyButtonHover = copyBtn.GetComponent<HoverEffectController>();

            // Move button
            var moveBtn = CreateEditModeActionButton(parent, "Move", "icon_move_folder", OnMoveClicked);
            var moveRT = moveBtn.GetComponent<RectTransform>();
            moveRT.anchorMin = moveRT.anchorMax = new Vector2(0.5f, 0.5f);
            moveRT.pivot = new Vector2(0, 0.5f);
            float moveX = copyX + copyRT.sizeDelta.x + btnSpacing;
            moveRT.anchoredPosition = new Vector2(moveX, 0);
            _moveButton = moveBtn.GetComponent<Button>();
            _moveButtonCG = moveBtn.AddComponent<CanvasGroup>();
            _moveButtonHover = moveBtn.GetComponent<HoverEffectController>();

            // Delete button (reduced width by 5%)
            var deleteBtn = CreateEditModeActionButton(parent, "Delete", "icon_trash", OnDeleteClicked);
            var deleteRT = deleteBtn.GetComponent<RectTransform>();
            deleteRT.sizeDelta = new Vector2(deleteRT.sizeDelta.x * 0.95f, deleteRT.sizeDelta.y);
            deleteRT.anchorMin = deleteRT.anchorMax = new Vector2(0.5f, 0.5f);
            deleteRT.pivot = new Vector2(0, 0.5f);
            float deleteX = moveX + moveRT.sizeDelta.x + btnSpacing;
            deleteRT.anchoredPosition = new Vector2(deleteX, 0);
            _deleteButton = deleteBtn.GetComponent<Button>();
            _deleteButtonCG = deleteBtn.AddComponent<CanvasGroup>();
            _deleteButtonHover = deleteBtn.GetComponent<HoverEffectController>();

            // Initially disable buttons (no selection)
            UpdateActionButtonsState();
        }

        private void CreateSelectAllCheckbox(RectTransform parent)
        {
            float checkboxSize = 50f;
            float labelWidth = 150f; // Increased to fit "Select all" on one line
            float spacing = 20f;  // Doubled spacing between checkbox and label
            float leftPadding = 20f;

            // Container for checkbox + label
            GameObject container = new GameObject("SelectAllContainer");
            container.transform.SetParent(parent, false);
            RectTransform containerRT = container.AddComponent<RectTransform>();
            containerRT.anchorMin = containerRT.anchorMax = new Vector2(0, 0.5f);
            containerRT.pivot = new Vector2(0, 0.5f);
            containerRT.sizeDelta = new Vector2(checkboxSize + spacing + labelWidth, checkboxSize);
            containerRT.anchoredPosition = new Vector2(leftPadding, 0);

            // Checkbox button (glass pill style)
            _selectAllCheckbox = new GameObject("Checkbox");
            _selectAllCheckbox.transform.SetParent(container.transform, false);
            RectTransform checkboxRT = _selectAllCheckbox.AddComponent<RectTransform>();
            checkboxRT.anchorMin = checkboxRT.anchorMax = new Vector2(0, 0.5f);
            checkboxRT.pivot = new Vector2(0, 0.5f);
            checkboxRT.sizeDelta = new Vector2(checkboxSize, checkboxSize);
            checkboxRT.anchoredPosition = Vector2.zero;

            // Checkbox background (solid dark color matching item hover)
            Image checkboxBg = _selectAllCheckbox.AddComponent<Image>();
            checkboxBg.raycastTarget = true;
            checkboxBg.sprite = GetRoundedRectSprite();
            checkboxBg.type = Image.Type.Sliced;
            checkboxBg.color = new Color(0f, 0f, 0f, 0.3f);  // Same as item hover color

            // Checkmark icon (hidden by default)
            GameObject checkmarkObj = new GameObject("Checkmark");
            checkmarkObj.transform.SetParent(_selectAllCheckbox.transform, false);
            RectTransform checkmarkRT = checkmarkObj.AddComponent<RectTransform>();
            checkmarkRT.anchorMin = new Vector2(0.15f, 0.15f);
            checkmarkRT.anchorMax = new Vector2(0.85f, 0.85f);
            checkmarkRT.offsetMin = checkmarkRT.offsetMax = Vector2.zero;
            _selectAllCheckmark = checkmarkObj.AddComponent<Image>();
            _selectAllCheckmark.sprite = Resources.Load<Sprite>("icon_check_mark");
            _selectAllCheckmark.color = Color.white;
            _selectAllCheckmark.preserveAspect = true;
            _selectAllCheckmark.raycastTarget = false;
            checkmarkObj.SetActive(false);

            // Button component
            Button checkboxButton = _selectAllCheckbox.AddComponent<Button>();
            checkboxButton.transition = Selectable.Transition.None;
            checkboxButton.onClick.AddListener(OnSelectAllClicked);

            // Add scale hover effect to checkbox (no color change)
            var checkboxHoverController = _selectAllCheckbox.AddComponent<HoverEffectController>();
            checkboxHoverController.TargetVisuals = _selectAllCheckbox.transform;
            var checkboxScaleEffect = new ScaleHoverEffect()
                .WithHoverScale(1.1f)
                .WithTransitionDuration(0.1f);
            checkboxHoverController.AddEffect(checkboxScaleEffect);

            // Label "Select all"
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(container.transform, false);
            RectTransform labelRT = labelObj.AddComponent<RectTransform>();
            labelRT.anchorMin = labelRT.anchorMax = new Vector2(0, 0.5f);
            labelRT.pivot = new Vector2(0, 0.5f);
            labelRT.sizeDelta = new Vector2(labelWidth, checkboxSize);
            labelRT.anchoredPosition = new Vector2(checkboxSize + spacing, 0);

            TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.text = "Select all";
            labelText.font = _font;
            labelText.fontSize = 28;
            labelText.color = Color.white;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.raycastTarget = false;
        }

        private GameObject CreateEditModeActionButton(RectTransform parent, string label, string iconName, UnityEngine.Events.UnityAction onClick, float minTextWidth = 0f)
        {
            float btnHeight = 60f;
            float iconSize = 32f;
            float textWidth = Mathf.Max(label.Length * 18f, minTextWidth); // Approximate text width, with minimum
            float padding = 25f;
            float spacing = 10f;
            float btnWidth = padding + iconSize + spacing + textWidth + padding;

            GameObject btnObj = new GameObject($"Btn_{label}");
            btnObj.transform.SetParent(parent, false);
            RectTransform btnRT = btnObj.AddComponent<RectTransform>();
            btnRT.sizeDelta = new Vector2(btnWidth, btnHeight);

            // Background (glass pill style - rounded on both sides, no border)
            Image bgImage = btnObj.AddComponent<Image>();
            bgImage.raycastTarget = true;
            Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (glassShader != null)
            {
                Material mat = new Material(glassShader);
                float aspect = btnWidth / btnHeight;
                mat.SetFloat("_Aspect", aspect);
                mat.SetFloat("_CornerRadius", 0.48f); // Fully rounded ends
                mat.SetFloat("_EdgePadding", 0.02f);
                mat.SetColor("_ColorA", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.3f));
                mat.SetColor("_ColorB", new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.1f));
                mat.SetFloat("_GlassAlpha", 0.15f);
                mat.SetFloat("_FresnelStrength", 0.15f);
                bgImage.material = mat;
                bgImage.color = Color.white;
            }
            else
            {
                Debug.LogWarning($"[RTTFileManager] CreateEditModeActionButton: Shader 'Custom/GlassGradientBackgroundWide' not found for {label}");
                // Fallback: use white background
                bgImage.color = new Color(1f, 1f, 1f, 0.2f);
            }

            // Icon (white color)
            Sprite icon = Resources.Load<Sprite>(iconName);
            Image iconImg = null;
            if (icon != null)
            {
                GameObject iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(btnObj.transform, false);
                RectTransform iconRT = iconObj.AddComponent<RectTransform>();
                iconRT.anchorMin = iconRT.anchorMax = new Vector2(0, 0.5f);
                iconRT.pivot = new Vector2(0, 0.5f);
                iconRT.sizeDelta = new Vector2(iconSize, iconSize);
                iconRT.anchoredPosition = new Vector2(padding, 0);

                iconImg = iconObj.AddComponent<Image>();
                iconImg.sprite = icon;
                iconImg.color = Color.white; // White icon
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
            }

            // Text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = textRT.anchorMax = new Vector2(0, 0.5f);
            textRT.pivot = new Vector2(0, 0.5f);
            textRT.sizeDelta = new Vector2(textWidth, btnHeight);
            textRT.anchoredPosition = new Vector2(padding + iconSize + spacing, 0);

            TextMeshProUGUI txt = textObj.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.font = _font;
            txt.fontSize = 26;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.fontStyle = FontStyles.Bold;
            txt.raycastTarget = false;

            // Button component
            Button btn = btnObj.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(onClick);

            // Add hover effects (Scale + Background Color)
            var hoverController = btnObj.AddComponent<HoverEffectController>();
            hoverController.TargetVisuals = btnObj.transform; // Set before adding effects

            // Scale effect
            var scaleEffect = new ScaleHoverEffect()
                .WithHoverScale(1.05f)
                .WithTransitionDuration(0.1f);
            hoverController.AddEffect(scaleEffect);

            // Background color change on hover (keep alpha, change RGB to accent)
            Material btnMaterial = bgImage.material;
            Color normalColorA = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.3f);
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

            return btnObj;
        }

        #region Edit Mode
        /// <summary>
        /// Toggle edit mode. When skipRestore is true, don't restore detail panel (used when transitioning to clipboard mode).
        /// </summary>
        private void ToggleEditMode(bool skipRestore = false)
        {
            _isEditMode = !_isEditMode;

            UpdateEditButtonVisual();

            // Toggle between Breadcrumbs and Edit Controls in Row2
            if (_breadcrumbContainer != null)
                _breadcrumbContainer.gameObject.SetActive(!_isEditMode);
            if (_editControlsContainer != null)
            {
                _editControlsContainer.SetActive(_isEditMode);

                // Force layout rebuild if entering edit mode
                if (_isEditMode)
                {
                    Canvas.ForceUpdateCanvases();
                    var rt = _editControlsContainer.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                    }
                }
            }

            // Show/hide selected count text
            if (_selectedCountText != null)
                _selectedCountText.gameObject.SetActive(_isEditMode);

            // Show/hide checkboxes on items
            _fileGrid?.SetEditMode(_isEditMode);
            _fileList?.SetEditMode(_isEditMode);

            // Handle detail panel and action bar visibility based on edit mode
            if (_isEditMode)
            {
                // Entering edit mode: lock detail panel to current folder
                _controller?.SetDetailLocked(true);

                // Show current folder info and hide action bar
                if (_controller != null && _fileDetail != null)
                {
                    var folderInfo = _controller.GetCurrentFolderInfo();
                    _fileDetail.UpdateInfo(folderInfo, isCurrentFolder: true);
                }
                _fileActionBar?.SetVisible(false);

                // Update selected count when entering edit mode
                UpdateSelectedCountText();
            }
            else
            {
                // Exiting edit mode: clear checkbox selection
                _selectedItems.Clear();
                UpdateSelectAllCheckmark();
                UpdateSelectedCountText();

                // Only restore detail panel if not transitioning to clipboard mode
                if (!skipRestore)
                {
                    // Unlock detail panel (allow hover to show file details)
                    _controller?.SetDetailLocked(false);

                    // Restore detail panel if there was a selected file
                    if (_hasSelectedFile && _currentDisplayedFile.Path != null)
                    {
                        _fileDetail?.UpdateInfo(_currentDisplayedFile, isCurrentFolder: false);
                        _fileActionBar?.SetVisible(true);
                        // Restore controller's selected file so hover/unhover works correctly
                        _controller?.SelectFile(_currentDisplayedFile.Path);
                    }
                    else
                    {
                        // Check if grid/list has a visually selected file
                        string selectedPath = _isGridView
                            ? _fileGrid?.GetSelectedFilePath()
                            : _fileList?.GetSelectedFilePath();

                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            // Restore _hasSelectedFile since there's a visually selected item
                            _hasSelectedFile = true;

                            // Get file info and update detail panel
                            var fileInfo = _controller?.GetFileInfo(selectedPath);
                            if (fileInfo.HasValue)
                            {
                                _currentDisplayedFile = fileInfo.Value;
                                _fileDetail?.UpdateInfo(_currentDisplayedFile, isCurrentFolder: false);
                                _fileActionBar?.SetVisible(true);
                                // Restore controller's selected file so hover/unhover works correctly
                                _controller?.SelectFile(selectedPath);
                            }
                        }
                        else if (_controller != null && _fileDetail != null)
                        {
                            // No selected file, show current folder info
                            var folderInfo = _controller.GetCurrentFolderInfo();
                            _fileDetail.UpdateInfo(folderInfo, isCurrentFolder: true);
                        }
                    }
                }
            }
        }

        private void UpdateSelectedCountText()
        {
            // Get actual selected count from grid/list
            int count = 0;
            if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
                count = _fileGrid.GetSelectedPaths().Count;
            else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
                count = _fileList.GetSelectedPaths().Count;

            // Update the text (to the left of item count)
            if (_selectedCountText != null)
                _selectedCountText.text = count > 0 ? $"{count} item selected" : "0 item selected";
        }

        private void UpdateEditButtonVisual()
        {
            if (_editButton == null) return;

            // Determine target color based on mode (Edit/Clipboard = accent, normal = primary)
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

        private void OnSelectAllClicked()
        {
            if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
            {
                bool allSelected = _fileGrid.AreAllSelected();
                _fileGrid.SetAllSelected(!allSelected);
            }
            else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
            {
                bool allSelected = _fileList.AreAllSelected();
                _fileList.SetAllSelected(!allSelected);
            }
            UpdateSelectAllCheckmark();
            UpdateActionButtonsState();
            UpdateSelectedCountText();
        }

        private void UpdateSelectAllCheckmark()
        {
            if (_selectAllCheckmark == null) return;

            bool allSelected = false;
            if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
                allSelected = _fileGrid.AreAllSelected();
            else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
                allSelected = _fileList.AreAllSelected();

            _selectAllCheckmark.gameObject.SetActive(allSelected);
        }

        private void OnSelectionChanged()
        {
            UpdateSelectAllCheckmark();
            UpdateActionButtonsState();
            UpdateSelectedCountText();
        }

        private void UpdateActionButtonsState()
        {
            int selectedCount = 0;
            if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
                selectedCount = _fileGrid.GetSelectedPaths().Count;
            else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
                selectedCount = _fileList.GetSelectedPaths().Count;

            bool hasSelection = selectedCount > 0;
            bool hasSingleSelection = selectedCount == 1;

            // Update button interactability and visual state
            // Rename only enabled when exactly 1 item is selected
            SetButtonEnabled(_renameButton, _renameButtonCG, _renameButtonHover, hasSingleSelection);
            SetButtonEnabled(_copyButton, _copyButtonCG, _copyButtonHover, hasSelection);
            SetButtonEnabled(_moveButton, _moveButtonCG, _moveButtonHover, hasSelection);
            SetButtonEnabled(_deleteButton, _deleteButtonCG, _deleteButtonHover, hasSelection);
        }

        private void SetButtonEnabled(Button button, CanvasGroup canvasGroup, HoverEffectController hoverController, bool enabled)
        {
            if (button != null)
                button.interactable = enabled;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = enabled ? 1f : 0.4f;
                canvasGroup.interactable = enabled;
                canvasGroup.blocksRaycasts = enabled;
            }
            if (hoverController != null)
            {
                hoverController.enabled = enabled;
                // Reset hover state when disabling
                if (!enabled)
                {
                    hoverController.ResetHoverState(immediate: true);
                }
            }
        }

        private void OnRenameClicked()
        {
            var selectedPaths = GetCurrentSelectedPaths();
            Debug.Log("[RTTFileManager] Rename clicked - selected items: " + selectedPaths.Count);

            // Rename only works with exactly 1 item selected
            if (selectedPaths.Count != 1)
            {
                Debug.LogWarning("[RTTFileManager] Rename requires exactly 1 item selected");
                return;
            }

            // Get the single selected path
            foreach (var path in selectedPaths)
            {
                _renameTargetPath = path;
                break;
            }

            ShowRenamePopup();
        }

        private void OnCopyClicked()
        {
            var selectedPaths = GetCurrentSelectedPaths();
            Debug.Log("[RTTFileManager] Copy clicked - selected items: " + selectedPaths.Count);

            if (selectedPaths.Count == 0) return;

            EnterClipboardMode(ClipboardOperation.Copy, selectedPaths);
        }

        private void OnMoveClicked()
        {
            var selectedPaths = GetCurrentSelectedPaths();
            Debug.Log("[RTTFileManager] Move clicked - selected items: " + selectedPaths.Count);

            if (selectedPaths.Count == 0) return;

            EnterClipboardMode(ClipboardOperation.Move, selectedPaths);
        }

        private void OnDeleteClicked()
        {
            var selectedPaths = GetCurrentSelectedPaths();
            Debug.Log("[RTTFileManager] Delete clicked - selected items: " + selectedPaths.Count);

            if (selectedPaths.Count == 0)
            {
                Debug.LogWarning("[RTTFileManager] No items selected for deletion");
                return;
            }

            ShowDeleteConfirmPopup();
        }

        private HashSet<string> GetCurrentSelectedPaths()
        {
            if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
                return _fileGrid.GetSelectedPaths();
            else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
                return _fileList.GetSelectedPaths();
            return new HashSet<string>();
        }

        #region Clipboard Mode (Copy/Move)

        /// <summary>
        /// Handler for Close button - either closes file manager or cancels clipboard mode
        /// </summary>
        private void OnCloseButtonClicked()
        {
            if (_isClipboardMode)
            {
                ExitClipboardMode();
            }
            else
            {
                _controller?.HandleBack();
            }
        }

        /// <summary>
        /// Handler for Edit button - either toggles edit mode or performs paste
        /// </summary>
        private void OnEditButtonClicked()
        {
            if (_isClipboardMode)
            {
                OnClipboardPaste();
            }
            else
            {
                ToggleEditMode();
            }
        }

        private void EnterClipboardMode(ClipboardOperation operation, HashSet<string> items)
        {
            // 0. Save state to restore when exiting clipboard mode
            _preClipboardHasSelectedFile = _hasSelectedFile;
            _preClipboardDisplayedFile = _currentDisplayedFile;

            // 1. Store clipboard data
            _clipboardOperation = operation;
            _clipboardItems = new List<string>(items);
            _clipboardSourcePath = _controller.GetCurrentPath();
            _isClipboardMode = true;

            // 2. Exit Edit Mode (hide edit controls, checkboxes) - skip restore since we'll handle it ourselves
            if (_isEditMode)
            {
                ToggleEditMode(skipRestore: true);
            }

            // 2.5 Notify grid/list to disable visual selection
            _fileGrid?.SetClipboardMode(true);
            _fileList?.SetClipboardMode(true);

            // 3. Lock detail panel and show current folder info (like edit mode)
            _controller?.SetDetailLocked(true);
            if (_controller != null && _fileDetail != null)
            {
                var folderInfo = _controller.GetCurrentFolderInfo();
                _fileDetail.UpdateInfo(folderInfo, isCurrentFolder: true);
            }
            _fileActionBar?.SetVisible(false);

            // 4. Change Close button to Back icon
            Sprite backIcon = Resources.Load<Sprite>("icon_back");
            if (_closeButtonIcon != null && backIcon != null)
            {
                _closeButtonIcon.sprite = backIcon;
            }

            // 5. Change Edit button to Copy/Move icon
            string iconName = operation == ClipboardOperation.Copy ? "icon_copy" : "icon_move_folder";
            Sprite operationIcon = Resources.Load<Sprite>(iconName);
            UpdateEditButtonIcon(operationIcon);
            UpdateEditButtonColor(_accentColor); // Use accent color to indicate active state

            // 6. Update paste button state (edit button enabled/disabled)
            UpdatePasteButtonState();

            Debug.Log($"[RTTFileManager] Entered Clipboard Mode: {operation}, {items.Count} items from {_clipboardSourcePath}");
        }

        private void ExitClipboardMode()
        {
            _isClipboardMode = false;
            _clipboardOperation = ClipboardOperation.None;
            _clipboardItems.Clear();
            _clipboardSourcePath = null;

            // Notify grid/list to restore visual selection
            _fileGrid?.SetClipboardMode(false);
            _fileList?.SetClipboardMode(false);

            // Restore Close button icon
            if (_closeButtonIcon != null && _originalCloseIcon != null)
            {
                _closeButtonIcon.sprite = _originalCloseIcon;
            }

            // Restore Edit button icon and color
            UpdateEditButtonIcon(_originalEditIcon);
            UpdateEditButtonColor(_primaryColor);

            // Restore Edit button interactability
            var editButtonCG = _editButton?.GetComponent<CanvasGroup>();
            if (editButtonCG != null)
            {
                editButtonCG.alpha = 1f;
                editButtonCG.interactable = true;
                editButtonCG.blocksRaycasts = true;
            }

            var editBtn = _editButton?.GetComponentInChildren<Button>();
            if (editBtn != null)
            {
                editBtn.interactable = true;
            }

            // Unlock detail panel (allow hover to show file details)
            _controller?.SetDetailLocked(false);

            // Restore detail panel - prioritize grid's selected path (more reliable)
            string selectedPath = _isGridView
                ? _fileGrid?.GetSelectedFilePath()
                : _fileList?.GetSelectedFilePath();

            if (!string.IsNullOrEmpty(selectedPath))
            {
                // Grid has a selected file - use that
                _hasSelectedFile = true;
                var fileInfo = _controller?.GetFileInfo(selectedPath);
                if (fileInfo.HasValue)
                {
                    _currentDisplayedFile = fileInfo.Value;
                    _fileDetail?.UpdateInfo(_currentDisplayedFile, isCurrentFolder: false);
                    _fileActionBar?.SetVisible(true);
                    // Restore controller's selected file so hover/unhover works correctly
                    _controller?.SelectFile(selectedPath);
                }
            }
            else if (_preClipboardHasSelectedFile && _preClipboardDisplayedFile.Path != null)
            {
                // Fall back to saved state from before entering clipboard mode
                _hasSelectedFile = _preClipboardHasSelectedFile;
                _currentDisplayedFile = _preClipboardDisplayedFile;
                _fileDetail?.UpdateInfo(_currentDisplayedFile, isCurrentFolder: false);
                _fileActionBar?.SetVisible(true);
                // Restore controller's selected file so hover/unhover works correctly
                _controller?.SelectFile(_preClipboardDisplayedFile.Path);
            }
            else if (_controller != null && _fileDetail != null)
            {
                // No selected file, show current folder info
                _hasSelectedFile = false;
                var folderInfo = _controller.GetCurrentFolderInfo();
                _fileDetail.UpdateInfo(folderInfo, isCurrentFolder: true);
            }

            Debug.Log("[RTTFileManager] Exited Clipboard Mode");
        }

        private void UpdateEditButtonIcon(Sprite icon)
        {
            if (_editButton == null || icon == null) return;

            // Find icon in the button hierarchy: HitArea/Visuals/Content/Icon
            var iconImg = _editButton.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.sprite = icon;
            }
        }

        private void UpdateEditButtonColor(Color color)
        {
            if (_editButton == null) return;

            // Apply same color mixing as MaterialFactory.CreateGlowBorder:
            // "Glow color is theme color mixed with white"
            Color glowColor = Color.Lerp(color, Color.white, 0.75f);

            // 1. Update icon tint color and shadow glow
            var iconTransform = _editButton.transform.Find("HitArea/Visuals/Content/Icon");
            if (iconTransform != null)
            {
                var iconImg = iconTransform.GetComponent<Image>();
                if (iconImg != null)
                {
                    iconImg.color = UIGlowEffects.CreateIconTintColor(color);
                }
                // Update icon shadow glow colors
                UIGlowEffects.UpdateShadowColors(iconTransform.gameObject, color);
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
                    Color colorA = new Color(color.r, color.g, color.b, backgroundAlpha * 1.5f);
                    Color colorB = new Color(color.r, color.g, color.b, backgroundAlpha * 0.5f);
                    bgImage.material.SetColor("_ColorA", colorA);
                    bgImage.material.SetColor("_ColorB", colorB);
                }
            }

            // 3. Update border glow color directly
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
            var hoverController = _editButton.GetComponentInChildren<HoverEffectController>();
            if (hoverController != null)
            {
                var glowEffect = hoverController.GetEffect("glow_border") as GlowBorderHoverEffect;
                glowEffect?.UpdateSavedGlowColor(glowColor);
            }
        }

        private void UpdatePasteButtonState()
        {
            if (!_isClipboardMode) return;

            string currentPath = _controller.GetCurrentPath();
            bool canPaste = !string.Equals(currentPath, _clipboardSourcePath,
                                            StringComparison.OrdinalIgnoreCase);

            // Also check write permission
            canPaste = canPaste && FileSystemService.CanCreateFolderInPath(currentPath);

            // Prevent moving folder into itself or its subfolder
            if (canPaste && _clipboardOperation == ClipboardOperation.Move)
            {
                foreach (string sourcePath in _clipboardItems)
                {
                    if (currentPath.StartsWith(sourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        canPaste = false;
                        break;
                    }
                }
            }

            // Update Edit button (now acting as Paste) visual state
            var editButtonCG = _editButton?.GetComponent<CanvasGroup>();
            if (editButtonCG == null && _editButton != null)
            {
                editButtonCG = _editButton.AddComponent<CanvasGroup>();
            }

            if (editButtonCG != null)
            {
                editButtonCG.alpha = canPaste ? 1f : 0.4f;
                editButtonCG.interactable = canPaste;
                editButtonCG.blocksRaycasts = canPaste;
            }

            var editBtn = _editButton?.GetComponentInChildren<Button>();
            if (editBtn != null)
            {
                editBtn.interactable = canPaste;
            }
        }

        private void OnClipboardCancel()
        {
            Debug.Log("[RTTFileManager] Clipboard operation cancelled");
            ExitClipboardMode();
        }

        private void OnClipboardPaste()
        {
            if (_clipboardItems.Count == 0) return;

            string destination = _controller.GetCurrentPath();

            // Check for conflicts first
            var conflicts = CheckForConflicts(destination);

            if (conflicts.Count > 0)
            {
                ShowConflictDialog(conflicts, destination);
            }
            else
            {
                ExecutePasteOperation(destination);
            }
        }

        private List<string> CheckForConflicts(string destination)
        {
            var conflicts = new List<string>();

            foreach (string sourcePath in _clipboardItems)
            {
                string fileName = System.IO.Path.GetFileName(sourcePath);
                string destPath = System.IO.Path.Combine(destination, fileName);

                if (System.IO.File.Exists(destPath) || System.IO.Directory.Exists(destPath))
                {
                    conflicts.Add(fileName);
                }
            }

            return conflicts;
        }

        private async void ExecutePasteOperation(string destination, bool overwrite = false)
        {
            Debug.Log($"[RTTFileManager] Executing paste: {_clipboardOperation} to {destination}, overwrite={overwrite}");

            // Create cancellation source and pause token
            _operationCts = new CancellationTokenSource();
            _pauseToken = new FileOperationService.PauseToken();

            // Create and show progress popup
            if (_progressPopup == null)
            {
                CreateProgressPopup();
            }

            string title = _clipboardOperation == ClipboardOperation.Copy
                ? "Copying Files..." : "Moving Files...";
            _progressPopup.Show(title, OnOperationCancel, OnOperationPauseToggle);

            // Create progress reporter
            var progress = new Progress<FileOperationService.FileOperationProgress>(p =>
            {
                _progressPopup.UpdateProgress(p);
            });

            try
            {
                if (_clipboardOperation == ClipboardOperation.Copy)
                {
                    await _controller.CopyItemsAsync(
                        new List<string>(_clipboardItems), destination, overwrite,
                        progress, _operationCts.Token, _pauseToken);
                }
                else if (_clipboardOperation == ClipboardOperation.Move)
                {
                    await _controller.MoveItemsAsync(
                        new List<string>(_clipboardItems), destination, overwrite,
                        progress, _operationCts.Token, _pauseToken);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[RTTFileManager] Operation cancelled by user");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RTTFileManager] Operation failed: {e.Message}");
            }
            finally
            {
                _progressPopup?.Hide();
                ExitClipboardMode();
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }

        private void OnOperationCancel()
        {
            Debug.Log("[RTTFileManager] Cancel requested");
            _operationCts?.Cancel();
        }

        private void OnOperationPauseToggle(bool isPaused)
        {
            Debug.Log($"[RTTFileManager] Pause toggled: {isPaused}");
            if (isPaused)
                _pauseToken?.Pause();
            else
                _pauseToken?.Resume();
        }

        private async void ExecutePasteOperationSkipConflicts(string destination)
        {
            // Filter out items that would conflict
            var nonConflictingItems = new List<string>();

            foreach (string sourcePath in _clipboardItems)
            {
                string fileName = System.IO.Path.GetFileName(sourcePath);
                string destPath = System.IO.Path.Combine(destination, fileName);

                if (!System.IO.File.Exists(destPath) && !System.IO.Directory.Exists(destPath))
                {
                    nonConflictingItems.Add(sourcePath);
                }
            }

            if (nonConflictingItems.Count > 0)
            {
                // Create cancellation source and pause token
                _operationCts = new CancellationTokenSource();
                _pauseToken = new FileOperationService.PauseToken();

                // Create and show progress popup
                if (_progressPopup == null)
                {
                    CreateProgressPopup();
                }

                string title = _clipboardOperation == ClipboardOperation.Copy
                    ? "Copying Files..." : "Moving Files...";
                _progressPopup.Show(title, OnOperationCancel, OnOperationPauseToggle);

                // Create progress reporter
                var progress = new Progress<FileOperationService.FileOperationProgress>(p =>
                {
                    _progressPopup.UpdateProgress(p);
                });

                try
                {
                    if (_clipboardOperation == ClipboardOperation.Copy)
                    {
                        await _controller.CopyItemsAsync(
                            nonConflictingItems, destination, false,
                            progress, _operationCts.Token, _pauseToken);
                    }
                    else if (_clipboardOperation == ClipboardOperation.Move)
                    {
                        await _controller.MoveItemsAsync(
                            nonConflictingItems, destination, false,
                            progress, _operationCts.Token, _pauseToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.Log("[RTTFileManager] Operation cancelled by user");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RTTFileManager] Operation failed: {e.Message}");
                }
                finally
                {
                    _progressPopup?.Hide();
                    _operationCts?.Dispose();
                    _operationCts = null;
                }
            }

            ExitClipboardMode();
        }

        private void CreateProgressPopup()
        {
            if (_progressPopup != null) return;

            // Standardized popup config using UIConstants for consistent styling
            var config = new RTTProgressPopup.PopupConfig
            {
                width = 550f,
                padding = 26f,
                titleFontSize = 32f,
                statusFontSize = 24f,
                percentFontSize = 24f,
                buttonFontSize = 25f,
                titleHeight = 52f,
                statusHeight = 32f,
                progressBarHeight = 24f,
                buttonHeight = 66f,
                spacing = 16f,
                primaryColor = _primaryColor,
                accentColor = _accentColor,
                overlayColor = new Color(0f, 0f, 0f, UIConstants.PopupOverlayAlpha),
                font = _font,
                layerName = UIConstants.VirtualObjectsLayer,
                borderWidth = UIConstants.PopupBorderWidth,
                buttonWidth = 450f,
                buttonBorderWidth = UIConstants.PopupButtonBorderWidth,
                buttonGlowWidth = UIConstants.PopupButtonGlowWidth,
                buttonGlowIntensity = UIConstants.PopupButtonGlowIntensity,
                buttonCornerRadius = UIConstants.PopupButtonCornerRadius
            };

            _progressPopup = RTTProgressPopup.CreateWorldSpace(config, _menuFrame.transform);
        }

        private void CreateConflictPopup()
        {
            if (_conflictPopup != null) return;

            // Standardized popup config using UIConstants for consistent styling
            var config = new RTTPopupMenu.PopupConfig
            {
                width = 550f,
                buttonHeight = 66f,
                sideSpacing = 26f,
                rowSpacing = 16f,
                labelHeight = 52f,
                labelFontSize = 32,
                fontSize = 25,
                borderWidth = UIConstants.PopupBorderWidth,
                glassAlpha = UIConstants.PopupGlassAlpha,
                primaryColor = _primaryColor,
                accentColor = _accentColor,
                overlayColor = new Color(0f, 0f, 0f, UIConstants.PopupOverlayAlpha),
                font = _font,
                layerName = UIConstants.VirtualObjectsLayer,
                buttonBorderWidth = UIConstants.PopupButtonBorderWidth,
                buttonGlowWidth = UIConstants.PopupButtonGlowWidth,
                buttonGlowIntensity = UIConstants.PopupButtonGlowIntensity,
                buttonCornerRadius = UIConstants.PopupButtonCornerRadius
            };

            _conflictPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);
        }

        private void ShowConflictDialog(List<string> conflicts, string destination)
        {
            if (_conflictPopup == null)
            {
                CreateConflictPopup();
            }

            _conflictPopup.Clear();

            string title = conflicts.Count == 1
                ? $"'{conflicts[0]}' already exists"
                : $"{conflicts.Count} items already exist";

            _conflictPopup.AddSectionBlock(title, new List<RTTPopupMenu.ButtonData>());

            var replaceBtn = new RTTPopupMenu.ButtonData(
                "Replace",
                () => { _conflictPopup.Hide(); ExecutePasteOperation(destination, true); },
                null,
                false,
                _accentColor
            );

            var skipBtn = new RTTPopupMenu.ButtonData(
                "Skip",
                () => { _conflictPopup.Hide(); ExecutePasteOperationSkipConflicts(destination); },
                null,
                false,
                _primaryColor
            );

            var cancelBtn = new RTTPopupMenu.ButtonData(
                "Cancel",
                () => { _conflictPopup.Hide(); },
                null,
                false,
                _primaryColor
            );

            // Row 1: Replace + Skip (2 buttons)
            _conflictPopup.AddSectionBlock("", new List<RTTPopupMenu.ButtonData> { replaceBtn, skipBtn }, 2);
            // Row 2: Cancel (full width)
            _conflictPopup.AddSectionBlock("", new List<RTTPopupMenu.ButtonData> { cancelBtn }, 1);

            _conflictPopup.Build();
            _conflictPopup.Show();
        }

        #endregion

        private void CreateDeleteConfirmPopup()
        {
            if (_deleteConfirmPopup != null) return;

            // Standardized Yes/No popup config using UIConstants for consistent styling
            var config = new RTTPopupMenu.PopupConfig
            {
                width = 550f,
                buttonHeight = 66f,
                sideSpacing = 26f,
                rowSpacing = 16f,
                labelHeight = 52f,
                labelFontSize = 32,
                fontSize = 25,
                borderWidth = UIConstants.PopupBorderWidth,
                glassAlpha = UIConstants.PopupGlassAlpha,
                primaryColor = _primaryColor,
                accentColor = _accentColor,
                overlayColor = new Color(0f, 0f, 0f, UIConstants.PopupOverlayAlpha),
                font = _font,
                layerName = UIConstants.VirtualObjectsLayer,
                buttonBorderWidth = UIConstants.PopupButtonBorderWidth,
                buttonGlowWidth = UIConstants.PopupButtonGlowWidth,
                buttonGlowIntensity = UIConstants.PopupButtonGlowIntensity,
                buttonCornerRadius = UIConstants.PopupButtonCornerRadius
            };

            _deleteConfirmPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);
        }

        private void ShowDeleteConfirmPopup()
        {
            if (_deleteConfirmPopup == null)
            {
                CreateDeleteConfirmPopup();
            }

            // Clear previous content and rebuild
            _deleteConfirmPopup.Clear();

            // Build confirmation message - use pending paths if set, otherwise get from selection
            var pathsToDelete = _pendingDeletePaths ?? new List<string>(GetCurrentSelectedPaths());
            int count = pathsToDelete.Count;
            string itemText = count == 1 ? "item" : "items";
            string title = $"Delete {count} {itemText}?";

            // Add title section (centered)
            _deleteConfirmPopup.AddSectionBlock(title, new List<RTTPopupMenu.ButtonData>(), 2, centerTitle: true);

            // Add Yes/No buttons (accent for Yes, primary for No)
            var yesButton = new RTTPopupMenu.ButtonData(
                "Yes",
                OnDeleteConfirmed,
                null,
                false,
                _accentColor // Accent color (magenta/pink)
            );

            var noButton = new RTTPopupMenu.ButtonData(
                "No",
                OnDeleteCancelled,
                null,
                false,
                _primaryColor // Primary color (cyan)
            );

            _deleteConfirmPopup.AddSectionBlock("", new List<RTTPopupMenu.ButtonData> { yesButton, noButton }, 2);

            _deleteConfirmPopup.Build();
            _deleteConfirmPopup.Show();
        }

        private async void OnDeleteConfirmed()
        {
            // Use pending paths if set, otherwise get from selection
            var pathsToDelete = _pendingDeletePaths ?? new List<string>(GetCurrentSelectedPaths());
            Debug.Log("[RTTFileManager] Delete confirmed - deleting " + pathsToDelete.Count + " items");

            // Clear pending paths
            _pendingDeletePaths = null;

            // Hide confirmation popup
            if (_deleteConfirmPopup != null)
            {
                _deleteConfirmPopup.Hide();
            }

            // Remember if we were in edit mode to exit after delete
            bool wasInEditMode = _isEditMode;

            // Create cancellation source and pause token
            _operationCts = new CancellationTokenSource();
            _pauseToken = new FileOperationService.PauseToken();

            try
            {
                // Create and show progress popup
                if (_progressPopup == null)
                {
                    Debug.Log("[RTTFileManager] Creating progress popup...");
                    CreateProgressPopup();
                }

                if (_progressPopup == null)
                {
                    Debug.LogError("[RTTFileManager] Progress popup is null after creation!");
                }
                else
                {
                    _progressPopup.Show("Deleting Files...", OnOperationCancel, OnOperationPauseToggle);
                }

                // Create progress reporter
                var progress = new Progress<FileOperationService.FileOperationProgress>(p =>
                {
                    _progressPopup?.UpdateProgress(p);
                });

                Debug.Log($"[RTTFileManager] Starting delete for paths: {string.Join(", ", pathsToDelete)}");

                if (_controller == null)
                {
                    Debug.LogError("[RTTFileManager] Controller is null! Cannot delete.");
                    return;
                }

                var result = await _controller.DeleteItemsAsync(pathsToDelete, progress, _operationCts.Token, _pauseToken);
                Debug.Log($"[RTTFileManager] Delete operation completed: {result.SuccessCount} succeeded, {result.FailCount} failed");
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[RTTFileManager] Delete operation cancelled by user");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RTTFileManager] Delete operation failed: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                _progressPopup?.Hide();
                _operationCts?.Dispose();
                _operationCts = null;

                // Exit edit mode AFTER delete is done (use skipRestore=true since files are deleted)
                if (wasInEditMode && _isEditMode)
                {
                    try
                    {
                        ToggleEditMode(skipRestore: true);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[RTTFileManager] Error exiting edit mode after delete: {e.Message}");
                        // Force exit edit mode even if there's an error
                        _isEditMode = false;
                    }
                }
            }
        }

        private void OnDeleteCancelled()
        {
            Debug.Log("[RTTFileManager] Delete cancelled");

            // Clear pending paths
            _pendingDeletePaths = null;

            if (_deleteConfirmPopup != null)
            {
                _deleteConfirmPopup.Hide();
            }
        }
        #endregion
    }
}
