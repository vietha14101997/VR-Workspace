using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.Config;
using VRWorkspace.UI.Utilities;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// RTTMediaLibrary partial: Header row construction, search bar, view/group options popups.
    /// </summary>
    public partial class RTTMediaLibrary
    {
        private void CreateHeaderRows(RectTransform parent)
        {
            // Row 1: Close | Sort | Search | Edit (No New Folder)
            CreateRow1(parent);

            // Row 2: Breadcrumbs (or Edit Controls) | Item Count | Refresh
            CreateRow2(parent);
        }

        private void CreateRow1(RectTransform parent)
        {
            RectTransform rowRT = CreateRowContainer(parent, "Row1", 0);

            // Left: Close Button
            _originalCloseIcon = Resources.Load<Sprite>("icon_close");
            _closeButton = VRButtonFactory.CreateBareIconButton(
                rowRT,
                75f,
                _originalCloseIcon,
                _primaryColor,
                OnCloseButtonClicked
            );
            _closeButtonIcon = _closeButton.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();
            RectTransform closeRT = _closeButton.GetComponent<RectTransform>();
            SetupRowElement(closeRT, new Vector2(0, 0.5f), new Vector2(20, 0));

            // Sort Button
            var sortConfig = new VRButtonFactory.ButtonConfig
            {
                label = _currentSortBy,
                themeColor = _primaryColor,
                width = _sortTriggerWidth,
                height = 75f,
                fontSize = 26,
                font = _font,
                textOnly = true,
                borderWidth = 0.04f,
                glowWidth = 0.08f,
                glowIntensity = 4f,
                popAmount = 0.05f
            };
            GameObject sortTrigger = VRButtonFactory.CreateButton(rowRT, sortConfig, ToggleViewOptionsPopup);

            _sortTriggerText = sortTrigger.GetComponentInChildren<TextMeshProUGUI>();
            if (_sortTriggerText != null)
            {
                _sortTriggerText.alignment = TextAlignmentOptions.Center;
                _sortTriggerText.margin = Vector4.zero;
            }

            RectTransform sortRT = sortTrigger.GetComponent<RectTransform>();
            float gapCenterFromCenter = -_containerWidth / 4f - 203.5f;
            sortRT.anchorMin = new Vector2(0.5f, 0.5f);
            sortRT.anchorMax = new Vector2(0.5f, 0.5f);
            sortRT.pivot = new Vector2(0.5f, 0.5f);
            sortRT.anchoredPosition = new Vector2(gapCenterFromCenter, 0);

            CreateViewOptionsPopup(sortRT);

            // Group By Trigger Button (symmetric to sortTrigger on right side of search bar)
            var groupConfig = new VRButtonFactory.ButtonConfig
            {
                label = _currentGroupBy,
                themeColor = _primaryColor,
                width = _sortTriggerWidth,
                height = 75f,
                fontSize = 26,
                font = _font,
                textOnly = true,
                borderWidth = 0.04f,
                glowWidth = 0.08f,
                glowIntensity = 4f,
                popAmount = 0.05f
            };
            GameObject groupTrigger = VRButtonFactory.CreateButton(rowRT, groupConfig, ToggleGroupOptionsPopup);

            _groupTriggerText = groupTrigger.GetComponentInChildren<TextMeshProUGUI>();
            if (_groupTriggerText != null)
            {
                _groupTriggerText.alignment = TextAlignmentOptions.Center;
                _groupTriggerText.margin = Vector4.zero;
            }

            RectTransform groupRT = groupTrigger.GetComponent<RectTransform>();
            float groupCenterFromCenter = _containerWidth / 4f + 203.5f;  // Symmetric to sortTrigger (positive X)
            groupRT.anchorMin = new Vector2(0.5f, 0.5f);
            groupRT.anchorMax = new Vector2(0.5f, 0.5f);
            groupRT.pivot = new Vector2(0.5f, 0.5f);
            groupRT.anchoredPosition = new Vector2(groupCenterFromCenter, 0);

            CreateGroupOptionsPopup(groupRT);

            // Right: Edit Button
            _originalEditIcon = Resources.Load<Sprite>("icon_edit");
            var editConfig = new VRButtonFactory.ButtonConfig
            {
                label = "Edit",
                icon = _originalEditIcon,
                themeColor = _primaryColor,
                width = 75f,
                height = 75f,
                iconOnly = true,
                iconSize = 39f,
                borderWidth = 0.04f,
                glowWidth = 0.08f,
                glowIntensity = 4f,
                popAmount = 0.05f
            };
            _editButton = VRButtonFactory.CreateButton(
                rowRT,
                editConfig,
                OnEditButtonClicked
            );
            RectTransform editRT = _editButton.GetComponent<RectTransform>();
            SetupRowElement(editRT, new Vector2(1, 0.5f), new Vector2(-20, 0));

            // NO New Folder button for Media Library

            // Center: Search Bar
            CreateSearchBar(rowRT);
        }

        private void CreateRow2(RectTransform parent)
        {
            float row2Y = -(_singleRowHeight + _rowSpacing);
            RectTransform rowRT = CreateRowContainer(parent, "Row2", row2Y);

            // Right: Refresh Button
            Sprite refreshIcon = Resources.Load<Sprite>("icon_refresh");
            var refreshConfig = new VRButtonFactory.ButtonConfig
            {
                label = "Refresh",
                icon = refreshIcon,
                themeColor = _accentColor,
                width = 75f,
                height = 75f,
                iconOnly = true,
                iconSize = 39f,
                borderWidth = 0.04f,
                glowWidth = 0.08f,
                glowIntensity = 4f,
                popAmount = 0.05f
            };
            GameObject refreshBtn = VRButtonFactory.CreateButton(
                rowRT,
                refreshConfig,
                () => {
                    // Smart refresh: incremental scan to detect new/removed files
                    // Thumbnails for removed files are cleaned up automatically
                    // Existing valid thumbnails are preserved
                    _controller?.SmartRefresh();
                }
            );
            RectTransform refreshRT = refreshBtn.GetComponent<RectTransform>();
            SetupRowElement(refreshRT, new Vector2(1, 0.5f), new Vector2(-20, 0));

            // Item Count Label
            GameObject countObj = new GameObject("ItemCountLabel");
            countObj.transform.SetParent(rowRT, false);

            _itemCountText = countObj.AddComponent<TextMeshProUGUI>();
            _itemCountText.text = "0 items";
            _itemCountText.font = _font;
            _itemCountText.fontSize = 31;
            _itemCountText.fontStyle = FontStyles.Bold;
            _itemCountText.color = Color.white;
            _itemCountText.alignment = TextAlignmentOptions.Center;
            _itemCountText.raycastTarget = false;

            RectTransform countRT = countObj.GetComponent<RectTransform>();
            countRT.sizeDelta = new Vector2(_sortTriggerWidth, 75f);
            float countCenterFromCenter = _containerWidth / 4f + 203.5f;
            countRT.anchorMin = new Vector2(0.5f, 0.5f);
            countRT.anchorMax = new Vector2(0.5f, 0.5f);
            countRT.pivot = new Vector2(0.5f, 0.5f);
            countRT.anchoredPosition = new Vector2(countCenterFromCenter, 0);

            // Selected Count Label (Edit Mode)
            float searchBarRightEdge = 990f / 2f;

            GameObject selectedObj = new GameObject("SelectedCountLabel");
            selectedObj.transform.SetParent(rowRT, false);

            _selectedCountText = selectedObj.AddComponent<TextMeshProUGUI>();
            _selectedCountText.text = "";
            _selectedCountText.font = _font;
            _selectedCountText.fontSize = 28;
            _selectedCountText.fontStyle = FontStyles.Bold;
            _selectedCountText.color = Color.white;
            _selectedCountText.alignment = TextAlignmentOptions.MidlineRight;
            _selectedCountText.raycastTarget = false;

            RectTransform selectedRT = selectedObj.GetComponent<RectTransform>();
            selectedRT.sizeDelta = new Vector2(280f, 75f);
            selectedRT.anchorMin = new Vector2(0.5f, 0.5f);
            selectedRT.anchorMax = new Vector2(0.5f, 0.5f);
            selectedRT.pivot = new Vector2(1, 0.5f);
            selectedRT.anchoredPosition = new Vector2(searchBarRightEdge, 0);
            selectedObj.SetActive(false);

            // Breadcrumb Container (holds pill-shaped segments like RTTFileManager)
            GameObject crumbContainer = new GameObject("Breadcrumbs");
            crumbContainer.layer = LayerMask.NameToLayer("UI");
            crumbContainer.transform.SetParent(rowRT, false);

            // IMPORTANT: Add RectTransform FIRST, then store the reference
            // Storing transform before AddComponent<RectTransform> can cause stale reference issues
            RectTransform crumbRT = crumbContainer.AddComponent<RectTransform>();
            _breadcrumbContainer = crumbRT; // Store RectTransform as the container reference

            crumbRT.anchorMin = new Vector2(0, 0);
            crumbRT.anchorMax = new Vector2(1, 1);  // Stretch full width like RTTFileManager
            crumbRT.pivot = new Vector2(0, 0.5f);
            crumbRT.offsetMin = new Vector2(20f, 0);
            crumbRT.offsetMax = new Vector2(-520f, 0);  // Leave room for item count and refresh button

            Debug.Log($"[RTTMediaLibrary] CreateRow2: _breadcrumbContainer SET to {_breadcrumbContainer.name}, instanceID={GetInstanceID()}");

            // Edit Controls Container (hidden by default)
            CreateEditControlsInRow2(rowRT);
        }

        private void CreateEditControlsInRow2(RectTransform parent)
        {
            _editControlsContainer = new GameObject("EditControls");
            _editControlsContainer.transform.SetParent(parent, false);

            RectTransform editRT = _editControlsContainer.AddComponent<RectTransform>();
            editRT.anchorMin = new Vector2(0, 0);
            editRT.anchorMax = new Vector2(1, 1);
            editRT.pivot = new Vector2(0, 0.5f);
            editRT.offsetMin = new Vector2(20, 0);
            editRT.offsetMax = new Vector2(-320, 0);

            // Select All Checkbox
            CreateSelectAllCheckbox(_editControlsContainer.transform);

            // Positioning for buttons
            float searchBarWidth = 990f;
            float searchBarLeftFromRowCenter = -searchBarWidth / 2f;
            float containerCenterOffset = (20f + (-320f)) / 2f;
            float searchBarLeftInContainer = searchBarLeftFromRowCenter - containerCenterOffset;
            float buttonsStartX = searchBarLeftInContainer - 90f;
            float btnSpacing = 15f;

            // Rename Button
            var renameBtn = CreateEditModeActionButton(_editControlsContainer.transform, "Rename", "icon_rename", OnRenameClicked, 130f);
            var renameRT = renameBtn.GetComponent<RectTransform>();
            renameRT.anchorMin = renameRT.anchorMax = new Vector2(0.5f, 0.5f);
            renameRT.pivot = new Vector2(0, 0.5f);
            renameRT.anchoredPosition = new Vector2(buttonsStartX, 0);
            _renameButton = renameBtn.GetComponent<Button>();
            _renameButtonCG = renameBtn.AddComponent<CanvasGroup>();
            _renameButtonHover = renameBtn.GetComponent<HoverEffectController>();

            // Delete Button (NO Copy/Move for Media Library)
            var deleteBtn = CreateEditModeActionButton(_editControlsContainer.transform, "Delete", "icon_trash", OnDeleteClicked, 100f);
            var deleteRT = deleteBtn.GetComponent<RectTransform>();
            deleteRT.anchorMin = deleteRT.anchorMax = new Vector2(0.5f, 0.5f);
            deleteRT.pivot = new Vector2(0, 0.5f);
            float deleteX = buttonsStartX + renameRT.sizeDelta.x + btnSpacing;
            deleteRT.anchoredPosition = new Vector2(deleteX, 0);
            _deleteButton = deleteBtn.GetComponent<Button>();
            _deleteButtonCG = deleteBtn.AddComponent<CanvasGroup>();
            _deleteButtonHover = deleteBtn.GetComponent<HoverEffectController>();

            UpdateActionButtonsState();

            _editControlsContainer.SetActive(false);
        }

        private void CreateSearchBar(Transform parent)
        {
            if (parent == null) return;

            var config = new VRInputFieldFactory.InputFieldConfig();
            config.label = "";
            config.placeholder = "Search media...";
            config.width = 990f;
            config.themeColor = _primaryColor;
            config.inputFontSize = 34;
            config.font = _font;
            config.borderWidth = 0.04f;
            config.glowWidth = 0.08f;
            config.glowIntensity = 4f;
            config.layerName = "UI";

            GameObject searchBar = VRInputFieldFactory.CreateInputField(
                parent as RectTransform,
                config,
                OnSearchValueChanged,
                OnSearchEndEdit
            );

            // Search Icon
            Sprite searchIcon = Resources.Load<Sprite>("icon_search");
            if (searchIcon != null)
            {
                GameObject iconObj = new GameObject("SearchIcon");
                Transform visuals = searchBar.transform.Find("InputBox/HitArea/Visuals");
                if (visuals != null)
                {
                    iconObj.transform.SetParent(visuals, false);
                }
                else
                {
                    iconObj.transform.SetParent(searchBar.transform, false);
                }

                RectTransform iconRT = iconObj.AddComponent<RectTransform>();
                float iconSize = 30f;
                float leftPadding = 26f;

                iconRT.anchorMin = new Vector2(0, 0.5f);
                iconRT.anchorMax = new Vector2(0, 0.5f);
                iconRT.pivot = new Vector2(0, 0.5f);
                iconRT.sizeDelta = new Vector2(iconSize, iconSize);
                iconRT.anchoredPosition = new Vector2(leftPadding, 0);

                Image iconImg = iconObj.AddComponent<Image>();
                iconImg.sprite = searchIcon;
                iconImg.color = new Color(1f, 1f, 1f, 0.8f);

                // Adjust text area padding
                float textOffset = iconSize;
                Transform textArea = FindChildRecursive(searchBar.transform, "Text Area");
                if (textArea != null)
                {
                    RectTransform textAreaRT = textArea.GetComponent<RectTransform>();
                    textAreaRT.offsetMin = new Vector2(textOffset, textAreaRT.offsetMin.y);
                }
            }

            // Position centered with explicit height to match buttons
            RectTransform searchRT = searchBar.GetComponent<RectTransform>();
            searchRT.anchorMin = new Vector2(0.5f, 0.5f);
            searchRT.anchorMax = new Vector2(0.5f, 0.5f);
            searchRT.pivot = new Vector2(0.5f, 0.5f);
            searchRT.sizeDelta = new Vector2(config.width, 75f);
            searchRT.anchoredPosition = Vector2.zero;
        }

        private void CreateViewOptionsPopup(Transform parent)
        {
            // Consistent popup config using UIConstants
            var config = new RTTPopupMenu.PopupConfig
            {
                width = _sortTriggerWidth * 2.5f,
                buttonHeight = 75f,
                sideSpacing = 35f,
                rowSpacing = 11f,
                fontSize = 24,
                iconSize = 26f,
                primaryColor = _primaryColor,
                accentColor = _accentColor,
                overlayColor = new Color(0f, 0f, 0f, UIConstants.PopupOverlayAlpha),
                font = _font,
                layerName = UIConstants.VirtualObjectsLayer,
                // Consistent button styling across all popups
                borderWidth = UIConstants.PopupBorderWidth,
                glassAlpha = UIConstants.PopupGlassAlpha,
                buttonBorderWidth = UIConstants.PopupButtonBorderWidth,
                buttonGlowWidth = UIConstants.PopupButtonGlowWidth,
                buttonGlowIntensity = UIConstants.PopupButtonGlowIntensity,
                buttonCornerRadius = UIConstants.PopupButtonCornerRadius
            };

            _viewOptionsPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);

            BuildViewOptionsPopupContent();

            // Position offset calculation (same approach as RTTFileManager)
            float popupWidth = config.width;

            // X: popup left edge aligned with sortTrigger button's left edge
            float sortTriggerCenterX = -_containerWidth / 4f - 203.5f;
            float sortTriggerLeftX = sortTriggerCenterX - (_sortTriggerWidth / 2f);
            float additionalOffset = 130f;
            float offsetX = sortTriggerLeftX + (popupWidth / 2f) + additionalOffset;

            // Y: popup top below header area with gap from sortTrigger button
            // Header takes approximately 200 logical pixels (Row1 + Row2 + spacing)
            // Frame center is at 0, top is at +containerHeight/2
            // Popup top should be at containerHeight/2 - headerHeight - gap
            float headerHeight = 200f;
            float gapFromButton = 15f; // Gap between sortTrigger button and popup top
            float estimatedPopupHeight = 360f; // SORT BY + Ascending (adjusted to match RTTFileManager gap)
            float offsetY = (_containerHeight / 2f) - headerHeight - gapFromButton - (estimatedPopupHeight / 2f);

            _viewOptionsPopup.SetPositionOffset(new Vector2(offsetX, offsetY));
        }

        private void BuildViewOptionsPopupContent()
        {
            if (_viewOptionsPopup == null) return;

            _viewOptionsPopup.Clear();

            // NO "DISPLAY AS" section - MediaLibrary is Grid-only

            // Section: Sort Options (2 columns like FileManager)
            // Name, Type, Created, Modified, Duration, Size (same as FileManager)
            string[] sortOptions = { "Name", "Type", "Created", "Modified", "Duration", "Size" };
            var sortButtons = new System.Collections.Generic.List<RTTPopupMenu.ButtonData>();
            foreach (string option in sortOptions)
            {
                bool isSelected = (_currentSortBy == option);
                sortButtons.Add(new RTTPopupMenu.ButtonData(option, () => SetSortBy(option), null, isSelected));
            }
            _viewOptionsPopup.AddSectionBlock("SORT BY", sortButtons, 2);

            // Full-width Order button
            Sprite orderIcon = Resources.Load<Sprite>(_isAscending ? "icon_ascending" : "icon_descending");
            string orderText = _isAscending ? "Ascending" : "Descending";
            _viewOptionsPopup.AddFullWidthButton(new RTTPopupMenu.ButtonData(orderText, ToggleSortOrder, orderIcon));

            // Build the popup
            _viewOptionsPopup.Build();
        }

        private void ToggleSortOrder()
        {
            SetAscending(!_isAscending);
        }

        private void ToggleViewOptionsPopup()
        {
            if (_viewOptionsPopup == null) return;

            if (_viewOptionsPopup.IsVisible)
            {
                _viewOptionsPopup.Hide();
            }
            else
            {
                BuildViewOptionsPopupContent();
                _viewOptionsPopup.Show();
            }
        }

        private void SetSortBy(string sortBy)
        {
            _currentSortBy = sortBy;
            if (_sortTriggerText != null) _sortTriggerText.text = sortBy;
            _controller?.SetSortBy(sortBy);
            _viewOptionsPopup?.Hide();
        }

        private void SetAscending(bool ascending)
        {
            _isAscending = ascending;
            _controller?.SetAscending(ascending);
            _viewOptionsPopup?.Hide();
        }

        // ===== GROUP BY POPUP =====

        private void ToggleGroupOptionsPopup()
        {
            if (_groupOptionsPopup == null) return;

            if (_groupOptionsPopup.IsVisible)
            {
                _groupOptionsPopup.Hide();
            }
            else
            {
                BuildGroupOptionsPopupContent();
                _groupOptionsPopup.Show();
            }
        }

        private void CreateGroupOptionsPopup(Transform parent)
        {
            // Consistent popup config using UIConstants
            var config = new RTTPopupMenu.PopupConfig
            {
                width = _sortTriggerWidth * 2.5f,
                buttonHeight = 75f,
                sideSpacing = 35f,
                rowSpacing = 11f,
                fontSize = 24,
                iconSize = 26f,
                primaryColor = _primaryColor,
                accentColor = _accentColor,
                overlayColor = new Color(0f, 0f, 0f, UIConstants.PopupOverlayAlpha),
                font = _font,
                layerName = UIConstants.VirtualObjectsLayer,
                // Consistent button styling across all popups
                borderWidth = UIConstants.PopupBorderWidth,
                glassAlpha = UIConstants.PopupGlassAlpha,
                buttonBorderWidth = UIConstants.PopupButtonBorderWidth,
                buttonGlowWidth = UIConstants.PopupButtonGlowWidth,
                buttonGlowIntensity = UIConstants.PopupButtonGlowIntensity,
                buttonCornerRadius = UIConstants.PopupButtonCornerRadius
            };

            _groupOptionsPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);

            BuildGroupOptionsPopupContent();

            // Position offset calculation (mirror of ViewOptionsPopup - align to right side)
            float popupWidth = config.width;
            float groupTriggerCenterX = _containerWidth / 4f + 203.5f;
            float groupTriggerRightX = groupTriggerCenterX + (_sortTriggerWidth / 2f);
            float offsetX = groupTriggerRightX - (popupWidth / 2f) - 130f;

            float headerHeight = 200f;
            float gapFromButton = 15f;
            float estimatedPopupHeight = 180f;  // Smaller popup, use smaller height to align top edge with SortBy
            float offsetY = (_containerHeight / 2f) - headerHeight - gapFromButton - (estimatedPopupHeight / 2f);

            _groupOptionsPopup.SetPositionOffset(new Vector2(offsetX, offsetY));
        }

        private void BuildGroupOptionsPopupContent()
        {
            if (_groupOptionsPopup == null) return;

            _groupOptionsPopup.Clear();

            // GROUP BY options (2 columns, 4 options) - removed None and Folder
            string[] groupOptions = { "Date Added", "Duration", "Resolution", "Format" };
            var groupButtons = new System.Collections.Generic.List<RTTPopupMenu.ButtonData>();

            foreach (string option in groupOptions)
            {
                bool isSelected = (_currentGroupBy == option);
                string capturedOption = option;
                groupButtons.Add(new RTTPopupMenu.ButtonData(option, () => SetGroupBy(capturedOption), null, isSelected));
            }
            _groupOptionsPopup.AddSectionBlock("GROUP BY", groupButtons, 2);

            _groupOptionsPopup.Build();
        }

        private void SetGroupBy(string groupBy)
        {
            _currentGroupBy = groupBy;
            if (_groupTriggerText != null) _groupTriggerText.text = groupBy;
            _controller?.SetGroupBy(groupBy);
            _groupOptionsPopup?.Hide();
        }

        private RectTransform CreateRowContainer(Transform parent, string name, float yPos)
        {
            GameObject rowObj = new GameObject(name);
            rowObj.transform.SetParent(parent, false);
            RectTransform rt = rowObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, _singleRowHeight);
            rt.anchoredPosition = new Vector2(0, yPos);
            return rt;
        }

        private void SetupRowElement(RectTransform rt, Vector2 anchor, Vector2 anchoredPos)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            if (anchor.x == 0.5f) rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
        }
    }
}
