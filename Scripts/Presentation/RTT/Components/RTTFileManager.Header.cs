using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.Config;
using VRWorkspace.UI.Utilities;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT.Services;
using VRWorkspace.Utilities;

namespace VRWorkspace.UI.RTT.Components
{
    public partial class RTTFileManager
    {
        // View Options Popup References
        private RTTPopupMenu _viewOptionsPopup;
        private string _currentSortBy = "Name";
        private bool _isAscending = true;
        private bool _isGridView = true;
        private TextMeshProUGUI _sortTriggerText; // Reference to sort trigger text
        private TextMeshProUGUI _itemCountText; // Reference to item count label

        // PlayerPrefs keys for saving view options
        private const string PREF_SORT_BY = "FileManager_SortBy";
        private const string PREF_SORT_ASCENDING = "FileManager_SortAscending";
        private const string PREF_IS_GRID_VIEW = "FileManager_IsGridView";

        // Ellipsis Popup References (for hidden breadcrumb folders)
        private GameObject _ellipsisPopup;
        private GameObject _ellipsisButton;
        private List<(string name, string fullPath)> _hiddenFolders = new List<(string name, string fullPath)>();
        private float _ellipsisPopupWidth; // Calculated width for popup

        private float _sortTriggerWidth = 220f; // Increased 10% (was 200f)

        private void CreateHeaderRows(RectTransform parent)
        {
            // Row 1: Sort | Search | Edit
            CreateRow1(parent);

            // Row 2: Breadcrumbs (or Edit Controls in Edit Mode) | Item Count | Refresh
            CreateRow2(parent);
        }

        private void CreateRow1(RectTransform parent)
        {
            RectTransform rowRT = CreateRowContainer(parent, "Row1", 0);

            // Left: Close Button (BareIconButton - no background/border)
            _originalCloseIcon = Resources.Load<Sprite>("icon_close");
            _closeButton = VRButtonFactory.CreateBareIconButton(
                rowRT,
                75f,
                _originalCloseIcon,
                _primaryColor,
                OnCloseButtonClicked
            );
            // Store icon reference for clipboard mode swap (path: HitArea/Visuals/Content/Icon)
            _closeButtonIcon = _closeButton.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();
            RectTransform closeRT = _closeButton.GetComponent<RectTransform>();
            SetupRowElement(closeRT, new Vector2(0, 0.5f), new Vector2(20, 0)); // Left align

            // View Options Trigger Button (text only)
            var sortConfig = new VRButtonFactory.ButtonConfig
            {
                label = _currentSortBy,
                themeColor = _primaryColor,
                width = _sortTriggerWidth,
                height = 75f,  // +10% (was 68f)
                fontSize = 26, // +10% (was 24)
                font = _font,
                textOnly = true,
                borderWidth = 0.04f,
                glowWidth = 0.08f,
                glowIntensity = 4f,
                popAmount = 0.05f
            };
            GameObject sortTrigger = VRButtonFactory.CreateButton(rowRT, sortConfig, ToggleViewOptionsPopup);

            // Adjust Text Alignment to Center and save reference
            _sortTriggerText = sortTrigger.GetComponentInChildren<TextMeshProUGUI>();
            if (_sortTriggerText != null)
            {
                _sortTriggerText.alignment = TextAlignmentOptions.Center;
                _sortTriggerText.margin = Vector4.zero;
            }

            RectTransform sortRT = sortTrigger.GetComponent<RectTransform>();
            // Position centered between Close button and SearchBar
            // Close button ends at: 20 + 68 = 88 from left
            // SearchBar (width 990, centered) left edge from left: containerWidth/2 - 495
            // Gap center from left = (88 + containerWidth/2 - 495) / 2 = containerWidth/4 - 203.5
            // Convert to center-anchored: gapCenter - containerWidth/2 = -containerWidth/4 - 203.5
            float gapCenterFromCenter = -_containerWidth / 4f - 203.5f;
            sortRT.anchorMin = new Vector2(0.5f, 0.5f);
            sortRT.anchorMax = new Vector2(0.5f, 0.5f);
            sortRT.pivot = new Vector2(0.5f, 0.5f);
            sortRT.anchoredPosition = new Vector2(gapCenterFromCenter, 0);

            // Create popup (using RTTPopupMenu) with Sort Button as parent
            CreateViewOptionsPopup(sortRT);

            // Right: Edit Button (Icon) - toggles edit mode
            _originalEditIcon = Resources.Load<Sprite>("icon_edit");
            var editConfig = new VRButtonFactory.ButtonConfig
            {
                label = "Edit",
                icon = _originalEditIcon,
                themeColor = _primaryColor,
                width = 75f,  // +10% (was 68f)
                height = 75f, // +10% (was 68f)
                iconOnly = true, // Hide text
                iconSize = 39f, // +10% (was 35.2f)
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
            SetupRowElement(editRT, new Vector2(1, 0.5f), new Vector2(-20, 0)); // Right align

            // New Folder Button (symmetric to sortTrigger on right side of search bar)
            Sprite folderIcon = Resources.Load<Sprite>("icon_create_folder");
            var newFolderConfig = new VRButtonFactory.ButtonConfig
            {
                label = "Folder",
                icon = folderIcon,
                themeColor = _primaryColor,
                width = _sortTriggerWidth,
                height = 75f,  // +10% (was 68f)
                fontSize = 26, // +10% (was 24)
                font = _font,
                iconSize = 39f, // +10% (was 35.2f)
                horizontalLayout = true,  // Icon on left, text on right
                borderWidth = 0.04f,
                glowWidth = 0.08f,
                glowIntensity = 4f,
                popAmount = 0.05f
            };
            GameObject newFolderBtn = VRButtonFactory.CreateButton(
                rowRT,
                newFolderConfig,
                ShowCreateFolderPopup
            );
            RectTransform newFolderRT = newFolderBtn.GetComponent<RectTransform>();
            // Position symmetric to sortTrigger (positive X instead of negative)
            float newFolderCenterFromCenter = _containerWidth / 4f + 203.5f;
            newFolderRT.anchorMin = new Vector2(0.5f, 0.5f);
            newFolderRT.anchorMax = new Vector2(0.5f, 0.5f);
            newFolderRT.pivot = new Vector2(0.5f, 0.5f);
            newFolderRT.anchoredPosition = new Vector2(newFolderCenterFromCenter, 0);

            // Store button reference for enabling/disabling based on folder creation permission
            // Note: VRButtonFactory creates Button on HitArea child, not on wrapper
            _newFolderButton = newFolderBtn.GetComponentInChildren<Button>();
            _newFolderCanvasGroup = newFolderBtn.AddComponent<CanvasGroup>();

            // Update button state immediately based on current path permission
            UpdateNewFolderButtonState();

            // Center: Search Bar
            CreateSearchBar(rowRT);
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
                // Rebuild content to reflect current state (in case changed by list header click)
                BuildViewOptionsPopupContent();
                _viewOptionsPopup.Show();
            }
        }

        private void CreateViewOptionsPopup(Transform parent)
        {
            // Create popup config with consistent button styling from UIConstants
            var config = new RTTPopupMenu.PopupConfig
            {
                width = _sortTriggerWidth * 2.5f, // Width = 2.5x Trigger (increased 25%)
                buttonHeight = 75f, // +10% (was 68f)
                sideSpacing = 35f,  // Half of bottom padding for balanced section spacing
                rowSpacing = 11f,   // +10% (was 10f)
                fontSize = 24,      // Increased from 20 to match other popups
                iconSize = 26f,     // +10% (was 24f)
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

            // Create world-space popup (like RTTPopupInputable)
            // Pass _menuFrame.transform as reference for positioning
            _viewOptionsPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);

            // Build popup content
            BuildViewOptionsPopupContent();

            // Set position offset (relative to menu frame center, in logical pixels)
            // Position below the header row, left-aligned with sortTrigger button
            float popupWidth = config.width;

            // X: popup left edge aligned with sortTrigger button's left edge
            // sortTrigger width = _sortTriggerWidth = 220
            // sortTrigger left edge = center - width/2 = -containerWidth/4 - 203.5 - 110 = -containerWidth/4 - 313.5
            // Popup center X = sortTrigger left + popupWidth/2
            // Additional adjustment to align better visually
            float sortTriggerCenterX = -_containerWidth / 4f - 203.5f;
            float sortTriggerLeftX = sortTriggerCenterX - (_sortTriggerWidth / 2f);
            float additionalOffset = 130f;
            float offsetX = sortTriggerLeftX + (popupWidth / 2f) + additionalOffset;

            // Y: popup top below header area with gap from sortTrigger button
            // Header takes approximately 200 logical pixels (Row1 + Row2 + spacing)
            // Frame center is at 0, top is at +containerHeight/2
            // Popup top should be at containerHeight/2 - headerHeight - gap
            float headerHeight = 200f; // Row1 + Row2 + margins
            float gapFromButton = 15f; // Gap between sortTrigger button and popup top
            float estimatedPopupHeight = 500f; // DISPLAY AS + SORT BY + Ascending
            // Popup center Y = popup top - popupHeight/2
            float offsetY = (_containerHeight / 2f) - headerHeight - gapFromButton - (estimatedPopupHeight / 2f);

            _viewOptionsPopup.SetPositionOffset(new Vector2(offsetX, offsetY));
        }

        private void BuildViewOptionsPopupContent()
        {
            if (_viewOptionsPopup == null) return;

            _viewOptionsPopup.Clear();

            // Section 1: Display Mode
            Sprite gridIcon = Resources.Load<Sprite>("icon_grid");
            Sprite listIcon = Resources.Load<Sprite>("icon_list");

            _viewOptionsPopup.AddSectionBlock("DISPLAY AS", new System.Collections.Generic.List<RTTPopupMenu.ButtonData>
            {
                new RTTPopupMenu.ButtonData("Grid", () => SetDisplayMode(true), gridIcon, _isGridView),
                new RTTPopupMenu.ButtonData("List", () => SetDisplayMode(false), listIcon, !_isGridView)
            }, 2);

            // Section 2: Sort Options
            string[] sortOptions = { "Name", "Type", "Created", "Modified", "Duration", "Size" };
            var sortButtons = new System.Collections.Generic.List<RTTPopupMenu.ButtonData>();
            foreach (string option in sortOptions)
            {
                bool isSelected = (_currentSortBy == option);
                sortButtons.Add(new RTTPopupMenu.ButtonData(option, () => SetSortBy(option), null, isSelected));
            }
            _viewOptionsPopup.AddSectionBlock("SORT BY", sortButtons, 2);

            // Full-width Order button - No Separator
            Sprite orderIcon = Resources.Load<Sprite>(_isAscending ? "icon_ascending" : "icon_descending");
            string orderText = _isAscending ? "Ascending" : "Descending";
            _viewOptionsPopup.AddFullWidthButton(new RTTPopupMenu.ButtonData(orderText, ToggleSortOrder, orderIcon));

            // Build the popup
            _viewOptionsPopup.Build();
        }

        private void SetDisplayMode(bool isGrid)
        {
            if (_isGridView == isGrid) return; // No change

            _isGridView = isGrid;
            Debug.Log($"[RTTFileManager] Display mode: {(isGrid ? "Grid" : "List")}");

            // Close popup after selection
            _viewOptionsPopup?.Hide();

            // Switch views
            if (isGrid)
            {
                // Show Grid, Hide List
                if (_fileGrid != null) _fileGrid.gameObject.SetActive(true);
                if (_fileList != null) _fileList.gameObject.SetActive(false);
            }
            else
            {
                // Show List, Hide Grid
                if (_fileGrid != null) _fileGrid.gameObject.SetActive(false);

                // Create list view if not exists
                if (_fileList == null)
                {
                    CreateListView();
                }
                else
                {
                    _fileList.gameObject.SetActive(true);
                }
            }

            // Update page size based on view mode (fixed sizes)
            // Grid: 8 items (2 rows x 4 columns)
            // List: 6 items
            int itemsPerPage = isGrid ? GRID_PAGE_SIZE : LIST_PAGE_SIZE;
            _controller?.SetPageSize(itemsPerPage);

            // Refresh content with current data (keep page position)
            _controller?.RefreshCurrentFolderKeepPage();

            // Save preference
            SaveViewPreferences();
        }

        private void SetSortBy(string sortBy)
        {
            _currentSortBy = sortBy;

            // Update sort trigger text
            if (_sortTriggerText != null)
            {
                _sortTriggerText.text = sortBy;
            }

            // Update list view sort state if visible
            if (!_isGridView && _fileList != null)
            {
                _fileList.UpdateSortState(_currentSortBy, _isAscending);
            }

            // Close popup after selection
            _viewOptionsPopup?.Hide();

            // Call controller to apply sort
            _controller?.SetSortOptions(_currentSortBy, _isAscending);

            // Save preference
            SaveViewPreferences();
        }

        private void ToggleSortOrder()
        {
            _isAscending = !_isAscending;

            // Update list view sort state if visible
            if (!_isGridView && _fileList != null)
            {
                _fileList.UpdateSortState(_currentSortBy, _isAscending);
            }

            // Close popup after toggle
            _viewOptionsPopup?.Hide();

            // Call controller to apply sort
            _controller?.SetSortOptions(_currentSortBy, _isAscending);

            // Save preference
            SaveViewPreferences();
        }

        private void RefreshViewOptionsPopup()
        {
            if (_viewOptionsPopup != null)
            {
                BuildViewOptionsPopupContent();
                _viewOptionsPopup.Show();
            }
        }

        private void CreateRow2(RectTransform parent)
        {
            // Position Row2 below Row1 with spacing
            float row2Y = -(_singleRowHeight + _rowSpacing);
            RectTransform rowRT = CreateRowContainer(parent, "Row2", row2Y);

            // Right: Refresh Button (Icon)
            Sprite refreshIcon = Resources.Load<Sprite>("icon_refresh");
            var refreshConfig = new VRButtonFactory.ButtonConfig
            {
                label = "Refresh",
                icon = refreshIcon,
                themeColor = _accentColor,
                width = 75f,  // +10% (was 68f)
                height = 75f, // +10% (was 68f)
                iconOnly = true, // Hide text
                iconSize = 39f, // +10% (was 35.2f)
                borderWidth = 0.04f,
                glowWidth = 0.08f,
                glowIntensity = 4f,
                popAmount = 0.05f
            };
            GameObject refreshBtn = VRButtonFactory.CreateButton(
                rowRT,
                refreshConfig,
                () => {
                    // Smart refresh: re-scan current folder to detect new/removed files
                    // Thumbnails for removed files are cleaned up automatically
                    // Existing valid thumbnails are preserved
                    _controller?.RefreshCurrentFolder();
                }
            );
            RectTransform refreshRT = refreshBtn.GetComponent<RectTransform>();
            SetupRowElement(refreshRT, new Vector2(1, 0.5f), new Vector2(-20, 0));

            // Item Count Label (aligned with New Folder button in Row 1)
            // Create this first so we know its position for the selected count label
            GameObject countObj = new GameObject("ItemCountLabel");
            countObj.transform.SetParent(rowRT, false);

            _itemCountText = countObj.AddComponent<TextMeshProUGUI>();
            _itemCountText.text = "0 items";
            _itemCountText.font = _font;
            _itemCountText.fontSize = 31; // +10% (was 28)
            _itemCountText.fontStyle = FontStyles.Bold;
            _itemCountText.color = Color.white;
            _itemCountText.alignment = TextAlignmentOptions.Center;
            _itemCountText.raycastTarget = false;

            RectTransform countRT = countObj.GetComponent<RectTransform>();
            countRT.sizeDelta = new Vector2(_sortTriggerWidth, 75f); // +10% (was 68f)
            // Position symmetric to sortTrigger (same as New Folder button in Row 1)
            float countCenterFromCenter = _containerWidth / 4f + 203.5f;
            countRT.anchorMin = new Vector2(0.5f, 0.5f);
            countRT.anchorMax = new Vector2(0.5f, 0.5f);
            countRT.pivot = new Vector2(0.5f, 0.5f);
            countRT.anchoredPosition = new Vector2(countCenterFromCenter, 0);

            // Selected Count Label (right edge aligned with SearchBar right edge, shown in Edit Mode)
            // SearchBar is centered with width 990, so right edge is at +495 from center
            float searchBarRightEdge = 990f / 2f; // +495

            GameObject selectedObj = new GameObject("SelectedCountLabel");
            selectedObj.transform.SetParent(rowRT, false);

            _selectedCountText = selectedObj.AddComponent<TextMeshProUGUI>();
            _selectedCountText.text = "";
            _selectedCountText.font = _font;
            _selectedCountText.fontSize = 28;
            _selectedCountText.fontStyle = FontStyles.Bold;
            _selectedCountText.color = Color.white; // White color as requested
            _selectedCountText.alignment = TextAlignmentOptions.MidlineRight;
            _selectedCountText.raycastTarget = false;

            RectTransform selectedRT = selectedObj.GetComponent<RectTransform>();
            selectedRT.sizeDelta = new Vector2(280f, 75f); // Wide enough for "9999 items selected" on 1 line
            // Right edge aligned with SearchBar right edge
            selectedRT.anchorMin = new Vector2(0.5f, 0.5f);
            selectedRT.anchorMax = new Vector2(0.5f, 0.5f);
            selectedRT.pivot = new Vector2(1, 0.5f); // Right-aligned pivot
            selectedRT.anchoredPosition = new Vector2(searchBarRightEdge, 0);
            selectedObj.SetActive(false); // Hidden by default

            // Left Container for Breadcrumbs
            GameObject crumbContainer = new GameObject("Breadcrumbs");
            crumbContainer.transform.SetParent(rowRT, false);
            _breadcrumbContainer = crumbContainer.transform;

            RectTransform crumbRT = crumbContainer.AddComponent<RectTransform>();
            crumbRT.anchorMin = new Vector2(0, 0);
            crumbRT.anchorMax = new Vector2(1, 1);
            crumbRT.pivot = new Vector2(0, 0.5f);
            // Left offset 20, Right offset to leave room for scan status + item count + Refresh button
            crumbRT.offsetMin = new Vector2(20, 0);
            crumbRT.offsetMax = new Vector2(-520, 0); // More room for scan status

            // Scan Status Text (shown during category scan, positioned between breadcrumbs and item count)
            GameObject scanStatusObj = new GameObject("ScanStatusText");
            scanStatusObj.transform.SetParent(rowRT, false);

            _scanStatusText = scanStatusObj.AddComponent<TextMeshProUGUI>();
            _scanStatusText.text = "";
            _scanStatusText.font = _font;
            _scanStatusText.fontSize = 26;
            _scanStatusText.fontStyle = FontStyles.Italic;
            _scanStatusText.color = new Color(1f, 1f, 1f, 0.7f); // Semi-transparent
            _scanStatusText.alignment = TextAlignmentOptions.MidlineRight;
            _scanStatusText.raycastTarget = false;

            RectTransform scanStatusRT = scanStatusObj.GetComponent<RectTransform>();
            scanStatusRT.sizeDelta = new Vector2(200f, 75f);
            // Position to the left of item count
            float scanStatusX = countCenterFromCenter - _sortTriggerWidth / 2f - 110f;
            scanStatusRT.anchorMin = new Vector2(0.5f, 0.5f);
            scanStatusRT.anchorMax = new Vector2(0.5f, 0.5f);
            scanStatusRT.pivot = new Vector2(1, 0.5f); // Right-aligned
            scanStatusRT.anchoredPosition = new Vector2(scanStatusX, 0);

            // Edit Controls Container (hidden by default, shown in Edit Mode)
            GameObject editControlsObj = new GameObject("EditControls");
            editControlsObj.transform.SetParent(rowRT, false);
            _editControlsContainer = editControlsObj;

            RectTransform editRT = editControlsObj.AddComponent<RectTransform>();
            editRT.anchorMin = new Vector2(0, 0);
            editRT.anchorMax = new Vector2(1, 1);
            editRT.pivot = new Vector2(0, 0.5f);
            editRT.offsetMin = new Vector2(20, 0);
            editRT.offsetMax = new Vector2(-320, 0);

            // Create Edit Controls inside the container BEFORE setting inactive
            // This ensures components initialize correctly
            CreateEditControlsInRow2(editRT);

            // Hide after creating children
            editControlsObj.SetActive(false);

            // NOTE: NOT using HorizontalLayoutGroup to allow independent control of:
            // - Visual position (left to right)
            // - Sibling order (reversed, so left buttons have higher index = hit first by GraphicRaycaster)
        }

        private void CreateSearchBar(Transform parent)
        {
            if (parent == null) return;

            var config = new VRInputFieldFactory.InputFieldConfig();
            config.label = "";
            config.placeholder = "Search current folder...";
            config.width = 990f;
            config.themeColor = _primaryColor;
            // fontSize * 2.2 = height, so 34 * 2.2 = 74.8 ≈ 75
            config.inputFontSize = 34;
            config.font = _font;

            // Match Button Visuals (Sharper, Brighter Border)
            config.borderWidth = 0.04f;  // Increased 40% (was 0.025f)
            config.glowWidth = 0.08f;     // Match Button
            config.glowIntensity = 4f;  // Match Button

            config.layerName = "UI";

            GameObject searchBar = VRInputFieldFactory.CreateInputField(
                parent as RectTransform,
                config,
                OnSearchValueChanged,
                OnSearchEndEdit
            );

            // --- Customization for Search Icon ---
            // 1. Load Icon
            Sprite searchIcon = Resources.Load<Sprite>("icon_search");

            if (searchIcon != null)
            {
                // 2. Create Icon Image
                GameObject iconObj = new GameObject("SearchIcon");

                // Parent to Visuals to participate in Hover/Pop animation
                Transform visuals = searchBar.transform.Find("InputBox/HitArea/Visuals");
                if (visuals != null)
                {
                    iconObj.transform.SetParent(visuals, false);
                }
                else
                {
                    // Fallback if structure changes
                    iconObj.transform.SetParent(searchBar.transform, false);
                }

                RectTransform iconRT = iconObj.AddComponent<RectTransform>();

                // Layout: Left aligned, vertically centered
                float iconSize = 30f;
                float leftPadding = 26f;

                iconRT.anchorMin = new Vector2(0, 0.5f);
                iconRT.anchorMax = new Vector2(0, 0.5f);
                iconRT.pivot = new Vector2(0, 0.5f);
                iconRT.sizeDelta = new Vector2(iconSize, iconSize);
                iconRT.anchoredPosition = new Vector2(leftPadding, 0);

                Image iconImg = iconObj.AddComponent<Image>();
                iconImg.sprite = searchIcon;
                iconImg.color = new Color(1f, 1f, 1f, 0.8f); // Slightly transparent white

                // 3. Adjust Text Area Padding
                // Find "Text Area" recursively in the InputField hierarchy
                float textOffset = iconSize;
                Transform textArea = FindChildRecursive(searchBar.transform, "Text Area");

                if (textArea != null)
                {
                    RectTransform textAreaRT = textArea.GetComponent<RectTransform>();
                    textAreaRT.offsetMin = new Vector2(textOffset, textAreaRT.offsetMin.y);
                    Debug.Log($"[RTTFileManager] Text Area found and offsetMin set to: {textAreaRT.offsetMin}");
                }
                else
                {
                    // Fallback: adjust Placeholder and Text directly
                    var placeholder = searchBar.GetComponentInChildren<TMP_Text>();
                    if (placeholder != null)
                    {
                        placeholder.margin = new Vector4(textOffset, 0, 0, 0);
                        Debug.Log($"[RTTFileManager] Set placeholder margin to: {placeholder.margin}");
                    }
                }
            }
            // -------------------------------------

            RectTransform rt = searchBar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(config.width, 75f);
            rt.anchoredPosition = Vector2.zero;
        }

        // Breadcrumb Update Logic
        public void UpdateBreadcrumbs(string path)
        {
            Debug.Log($"[RTTFileManager] UpdateBreadcrumbs called with path: {path}, container null: {_breadcrumbContainer == null}, instance: {GetEntityId()}");

            // Update New Folder button state based on folder creation permission
            UpdateNewFolderButtonState();

            // Fallback: find breadcrumb container from hierarchy if reference is lost
            if (_breadcrumbContainer == null)
            {
                Debug.LogWarning("[RTTFileManager] Breadcrumb container reference lost, searching in hierarchy...");
                var row2 = _headerRT?.Find("Row2");
                if (row2 != null)
                {
                    _breadcrumbContainer = row2.Find("Breadcrumbs");
                    Debug.Log($"[RTTFileManager] Found breadcrumb container from hierarchy: {_breadcrumbContainer != null}");
                }
            }

            if (_breadcrumbContainer == null)
            {
                Debug.LogWarning("[RTTFileManager] Breadcrumb container is null and could not be found!");
                return;
            }

            foreach (Transform child in _breadcrumbContainer) Destroy(child.gameObject);

            if (string.IsNullOrEmpty(path)) return;

            // Convert "root" to actual absolute path
            if (path == "root")
            {
                path = FileSystemService.RootPath;
            }

            // Update side panel selection based on current path
            // Only shows selection when at exact root of a side panel item, not in subfolders
            _sidePanel?.UpdateSelectionForPath(path);

            // Get root path and create relative path for display
            string rootPath = FileSystemService.RootPath;
            string displayPath = path;

            // Create list of breadcrumb items
            var breadcrumbs = new List<(string name, string fullPath, bool isEllipsis)>();

            // Get the first breadcrumb label and path from side panel selection
            string firstBreadcrumbLabel = _sidePanel != null ? _sidePanel.GetSelectedLabel() : null;
            string selectedId = _sidePanel != null ? _sidePanel.GetSelectedId() : null;
            if (string.IsNullOrEmpty(firstBreadcrumbLabel))
            {
                firstBreadcrumbLabel = "Home"; // Fallback
            }

            // Determine the base path for the first breadcrumb based on side panel selection
            string basePath = GetBasePath(selectedId);

            // Add first breadcrumb with selected side panel label
            breadcrumbs.Add((firstBreadcrumbLabel, basePath, false));

            // If path is not base path, add subfolders
            if (path != basePath && !string.IsNullOrEmpty(path))
            {
                // Make path relative to base path for display
                if (path.StartsWith(basePath))
                {
                    displayPath = path.Substring(basePath.Length).TrimStart('/', '\\');
                }
                else if (path.StartsWith(rootPath))
                {
                    displayPath = path.Substring(rootPath.Length).TrimStart('/', '\\');
                }

                if (!string.IsNullOrEmpty(displayPath))
                {
                    string[] parts = displayPath.Split(new char[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries);
                    string currentPath = basePath;

                    var allFolders = new List<(string name, string fullPath)>();
                    foreach (string part in parts)
                    {
                        currentPath = System.IO.Path.Combine(currentPath, part);
                        // Fix encoding for folder names that may have mojibake
                        string fixedName = TextEncodingHelper.FixString(part);
                        allFolders.Add((fixedName, currentPath));
                    }

                    // Limit: Max 6 buttons total (Home + 5 folders OR Home + ... + 4 folders)
                    const int maxButtons = 6;
                    const int maxFoldersWithoutEllipsis = maxButtons - 1; // 5 folders after Home

                    if (allFolders.Count <= maxFoldersWithoutEllipsis)
                    {
                        // No truncation needed
                        _hiddenFolders.Clear();
                        foreach (var folder in allFolders)
                        {
                            breadcrumbs.Add((folder.name, folder.fullPath, false));
                        }
                    }
                    else
                    {
                        // Need ellipsis: Home > ... > last 4 folders
                        const int visibleFoldersAfterEllipsis = maxButtons - 2; // 4 folders after "..."

                        // Store hidden folders for dropdown
                        _hiddenFolders.Clear();
                        int hiddenCount = allFolders.Count - visibleFoldersAfterEllipsis;
                        for (int j = 0; j < hiddenCount; j++)
                        {
                            _hiddenFolders.Add((allFolders[j].name, allFolders[j].fullPath));
                        }

                        // Add "..." as second button (clickable - shows dropdown)
                        breadcrumbs.Add(("...", "", true));

                        // Add last N folders
                        int startIndex = allFolders.Count - visibleFoldersAfterEllipsis;
                        for (int j = startIndex; j < allFolders.Count; j++)
                        {
                            breadcrumbs.Add((allFolders[j].name, allFolders[j].fullPath, false));
                        }
                    }
                }
            }

            // Create breadcrumb chevron buttons (connected style like reference image)
            // Strategy: All buttons are pill-shaped, left buttons overlap right buttons
            float btnHeight = 75f; // +10% (was 68f)
            float btnWidth = _sortTriggerWidth * 1.2f;
            int totalCount = breadcrumbs.Count;

            // Create buttons in REVERSE order (right-to-left) for GraphicRaycaster priority
            // Left buttons created LAST = higher sibling index = hit first
            float overlapAmount = btnHeight * 0.45f; // Same as old HLG spacing
            float effectiveWidth = btnWidth - overlapAmount;

            // Cleanup old ellipsis popup if exists
            if (_ellipsisPopup != null)
            {
                Destroy(_ellipsisPopup);
                _ellipsisPopup = null;
            }
            _ellipsisButton = null;

            // Calculate popup width: same as the "..." button width
            _ellipsisPopupWidth = btnWidth;

            for (int i = totalCount - 1; i >= 0; i--)
            {
                var (name, fullPath, isEllipsis) = breadcrumbs[i];
                string targetPath = fullPath;
                bool isLast = (i == totalCount - 1);

                int zIndex = i;
                GameObject btn = CreateBreadcrumbPillButton(name, targetPath, isLast, btnWidth, btnHeight, zIndex, totalCount, isEllipsis);

                // Save reference to ellipsis button for popup
                if (isEllipsis)
                {
                    _ellipsisButton = btn;
                    CreateEllipsisPopup(btn.GetComponent<RectTransform>());
                }

                // Manual positioning (left-to-right visual order)
                RectTransform btnRT = btn.GetComponent<RectTransform>();
                btnRT.anchorMin = new Vector2(0, 0.5f);
                btnRT.anchorMax = new Vector2(0, 0.5f);
                btnRT.pivot = new Vector2(0, 0.5f);

                // X position: each button offset by effectiveWidth (width - overlap)
                float xPos = i * effectiveWidth;
                btnRT.anchoredPosition = new Vector2(xPos, 0);

                // Z-position for visual layering (left buttons closer to camera)
                // This is secondary to sibling order but helps with any physics-based raycast
                Vector3 pos = btnRT.localPosition;
                pos.z = zIndex * -0.5f; // Lower zIndex = closer (more positive in local space toward camera)
                btnRT.localPosition = pos;
            }

            Debug.Log($"[RTTFileManager] Created {breadcrumbs.Count} breadcrumbs for path: {path}");

            // Update paste button state if in clipboard mode
            if (_isClipboardMode)
            {
                UpdatePasteButtonState();
            }
        }

        /// <summary>
        /// Creates a chevron-shaped breadcrumb button.
        /// Right side: convex rounded (pill end)
        /// Left side: concave curved (inward arc)
        /// </summary>
        private GameObject CreateBreadcrumbPillButton(string label, string targetPath, bool isActive, float width, float height, int zIndex, int totalButtons, bool isEllipsis = false)
        {
            bool isFirstButton = (zIndex == 0);

            // First button (Home) always uses primary color, never shows "selected" state
            // Other buttons: accent if active (current folder), primary if not
            Color btnColor = isFirstButton ? _primaryColor : (isActive ? _accentColor : _primaryColor);
            string pathToNavigate = targetPath;

            // Button container
            GameObject btnObj = new GameObject($"Crumb_{label}");
            btnObj.transform.SetParent(_breadcrumbContainer, false);

            RectTransform btnRT = btnObj.AddComponent<RectTransform>();
            btnRT.sizeDelta = new Vector2(width, height);

            // NOTE: Z-position is set in UpdateBreadcrumbs after anchoredPosition is set

            // Background with appropriate shader
            Image bgImage = btnObj.AddComponent<Image>();
            bgImage.raycastTarget = true;

            float aspect = width / height;

            // Glass background style (like MainMenu button hover state)
            float glassAlpha = 0.2f; // Semi-transparent glass effect

            if (isFirstButton)
            {
                // First button: rounded shape matching chevron curve
                Shader pillShader = Shader.Find("Custom/GlassGradientBackgroundWide");
                if (pillShader != null)
                {
                    Material mat = new Material(pillShader);
                    mat.SetFloat("_Aspect", aspect);
                    mat.SetFloat("_CornerRadius", 0.48f); // Full semicircle (halfH = 0.5 - padding)
                    mat.SetFloat("_EdgePadding", 0.02f);
                    // Gradient colors for glass effect
                    Color colorA = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha * 1.5f);
                    Color colorB = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha * 0.5f);
                    mat.SetColor("_ColorA", colorA);
                    mat.SetColor("_ColorB", colorB);
                    mat.SetFloat("_GlassAlpha", glassAlpha);
                    mat.SetFloat("_FresnelStrength", 0.15f); // Edge glow
                    bgImage.material = mat;
                    bgImage.color = Color.white;
                }
                else
                {
                    bgImage.color = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha);
                }
            }
            else
            {
                // Subsequent buttons: chevron shape (convex right, concave left)
                Shader chevronShader = Shader.Find("Custom/ChevronBackground");
                if (chevronShader != null)
                {
                    Material mat = new Material(chevronShader);
                    mat.SetFloat("_Aspect", aspect);
                    mat.SetFloat("_EdgePadding", 0.02f);
                    mat.SetColor("_BackgroundColor", btnColor);
                    mat.SetFloat("_BackgroundAlpha", glassAlpha);
                    mat.SetFloat("_EdgeGlow", 0.15f); // Edge glow
                    mat.SetFloat("_CenterGlow", 0.1f); // Center glow
                    bgImage.material = mat;
                    bgImage.color = Color.white;
                }
                else
                {
                    bgImage.color = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha);
                }
            }

            // Text centered - constrained to visible area
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);

            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;

            // Text padding based on shape
            // Curve radius = halfHeight = height * 0.5 (full semicircle)
            float curveR = height * 0.5f;
            if (isFirstButton)
            {
                // Rounded rect: padding for both rounded corners
                textRT.offsetMin = new Vector2(curveR * 0.85f, 0); // Left padding
                textRT.offsetMax = new Vector2(-curveR * 0.75f, 0); // Right padding
            }
            else
            {
                // Chevron: small left (concave opens up space), normal right
                textRT.offsetMin = new Vector2(curveR * 1.1f, 0); // Less left padding
                textRT.offsetMax = new Vector2(-curveR * 0.6f, 0); // Right padding for convex
            }

            TextMeshProUGUI txt = textObj.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 24; // +10% (was 22)
            txt.font = _font;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
            txt.verticalAlignment = VerticalAlignmentOptions.Middle;
            txt.fontStyle = FontStyles.Bold;
            txt.raycastTarget = false;
            txt.textWrappingMode = TextWrappingModes.NoWrap; // Single line
            txt.overflowMode = TextOverflowModes.Ellipsis; // Show ... if too long

            // Button component
            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = bgImage;
            btn.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            btn.colors = colors;

            // Block dwell click for current folder (last breadcrumb) - keep visual normal
            if (isActive && !isEllipsis)
            {
                var clickLock = btnObj.AddComponent<VRButtonClickLock>();
                clickLock.Lock(); // Lock immediately - no need to click current folder
            }

            if (!isEllipsis)
            {
                // Normal breadcrumb: navigate back to folder (restores scroll position)
                btn.onClick.AddListener(() => _controller?.NavigateBack(pathToNavigate));
            }
            else
            {
                // Ellipsis: toggle dropdown showing hidden folders
                btn.onClick.AddListener(ToggleEllipsisPopup);
            }

            // BoxCollider for all clickable buttons
            BoxCollider col = btnObj.AddComponent<BoxCollider>();

            // Calculate overlap amount
            float overlapAmount = height * 0.45f; // Uses button height
            float safetyBuffer = 5f; // Extra buffer to ensure no overlap

            if (isFirstButton)
            {
                // First button: trim RIGHT side where next button overlaps
                float trimRight = overlapAmount + safetyBuffer;
                col.size = new Vector3(width - trimRight, height, 0.1f);
                col.center = new Vector3(-trimRight * 0.5f, 0, 0);
            }
            else
            {
                // Chevron buttons: trim LEFT side (overlap with previous) and RIGHT (overlap with next)
                float trimLeft = overlapAmount + safetyBuffer;
                float trimRight = (zIndex < totalButtons - 1) ? (overlapAmount + safetyBuffer) : 0;
                float totalTrim = trimLeft + trimRight;
                col.size = new Vector3(width - totalTrim, height, 0.1f);
                col.center = new Vector3((trimLeft - trimRight) * 0.5f, 0, 0);
            }

            // Set layer
            int vrLayer = LayerMask.NameToLayer("VirtualObjects");
            if (vrLayer != -1)
            {
                btnObj.layer = vrLayer;
                textObj.layer = vrLayer;
            }

            // Add hover effect (change alpha like selected state) - only for non-active buttons
            if (!isActive)
            {
                AddBreadcrumbHoverEffect(btnObj, bgImage, btnColor, isFirstButton);
            }

            return btnObj;
        }

        /// <summary>
        /// Adds animated hover effect to breadcrumb button.
        /// On hover: color transitions to accent color with smooth animation.
        /// </summary>
        private void AddBreadcrumbHoverEffect(GameObject btnObj, Image bgImage, Color baseColor, bool isFirstButton)
        {
            // Hover values - same alpha as selected state, only color changes
            float normalAlpha = 0.2f;
            float hoverAlpha = 0.2f; // Same as selected, color change is enough visual feedback

            // Use BreadcrumbHoverAnimator for smooth animated transitions
            BreadcrumbHoverAnimator animator = btnObj.AddComponent<BreadcrumbHoverAnimator>();
            animator.Initialize(bgImage, baseColor, _accentColor, normalAlpha, hoverAlpha, isFirstButton);
        }

        /// <summary>
        /// Creates simple popup for ellipsis button showing hidden folders
        /// Style: No border, glass background matching button, left-aligned text only
        /// </summary>
        private void CreateEllipsisPopup(RectTransform ellipsisButtonRT)
        {
            if (_hiddenFolders.Count == 0) return;

            float itemHeight = 55f;       // +10% (was 50f)
            float verticalPadding = 13f;   // +10% (was 12f)
            float popupHeight = (_hiddenFolders.Count * itemHeight) + (verticalPadding * 2);

            Debug.Log($"[EllipsisPopup] Creating popup: hiddenFolders={_hiddenFolders.Count}, itemHeight={itemHeight}, verticalPadding={verticalPadding}, popupHeight={popupHeight}, width={_ellipsisPopupWidth}");

            // Create popup container
            _ellipsisPopup = new GameObject("EllipsisPopup");
            _ellipsisPopup.transform.SetParent(_breadcrumbContainer, false);

            RectTransform popupRT = _ellipsisPopup.AddComponent<RectTransform>();
            popupRT.sizeDelta = new Vector2(_ellipsisPopupWidth, popupHeight);

            // Position: anchor to left edge of ellipsis button, below it
            popupRT.anchorMin = new Vector2(0, 0.5f);
            popupRT.anchorMax = new Vector2(0, 0.5f);
            popupRT.pivot = new Vector2(0, 1); // Top-left pivot

            // Get ellipsis button position (it's at index 1, so xPos = 1 * effectiveWidth)
            float btnHeightLocal = 75f; // +10% (was 68f)
            float overlapAmount = btnHeightLocal * 0.45f;
            float btnWidth = _sortTriggerWidth * 1.25f;
            float effectiveWidth = btnWidth - overlapAmount;
            float ellipsisX = effectiveWidth; // Index 1 position

            popupRT.anchoredPosition = new Vector2(ellipsisX, -btnHeightLocal / 2 - 8f);

            // Background matching VRDropdownFactory's dropdown panel style
            Image bgImage = _ellipsisPopup.AddComponent<Image>();
            float aspect = _ellipsisPopupWidth / popupHeight;

            Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (glassShader != null)
            {
                Material mat = new Material(glassShader);
                mat.SetFloat("_Aspect", aspect);
                mat.SetFloat("_CornerRadius", 0.06f);
                mat.SetFloat("_EdgePadding", 0.01f);

                // Match VRDropdownFactory's CreateViewportBackground style (increased alpha for visibility)
                float backgroundAlpha = 0.25f;
                Color col = _primaryColor;
                Color colorA = new Color(col.r * 0.8f, col.g * 0.9f, col.b, backgroundAlpha * 1.8f);
                Color colorB = new Color(col.r, col.g * 0.7f, col.b * 0.9f, backgroundAlpha * 1.5f);
                mat.SetColor("_ColorA", colorA);
                mat.SetColor("_ColorB", colorB);
                mat.SetFloat("_GradientOffset", 0f);
                mat.SetFloat("_GradientAngle", -10f);
                mat.SetFloat("_CyanRatio", 0.7f);
                mat.SetFloat("_GlassAlpha", backgroundAlpha * 1.2f);
                mat.SetFloat("_FresnelPower", 2.2f);
                mat.SetFloat("_FresnelStrength", 0.12f);

                bgImage.material = mat;
                bgImage.color = Color.white;
            }
            else
            {
                bgImage.color = new Color(_primaryColor.r, _primaryColor.g, _primaryColor.b, 0.3f);
            }

            // Create folder items
            for (int i = 0; i < _hiddenFolders.Count; i++)
            {
                var folder = _hiddenFolders[i];
                CreateEllipsisPopupItem(folder.name, folder.fullPath, i, itemHeight, verticalPadding);
            }

            // Set layer
            int vrLayer = LayerMask.NameToLayer("VirtualObjects");
            if (vrLayer != -1)
            {
                SetLayerRecursively(_ellipsisPopup, vrLayer);
            }

            // Start hidden
            _ellipsisPopup.SetActive(false);
        }

        /// <summary>
        /// Creates a single item in the ellipsis popup
        /// </summary>
        private void CreateEllipsisPopupItem(string folderName, string folderPath, int index, float itemHeight, float verticalPadding)
        {
            float horizontalPadding = 22f; // +10% (was 20f)

            GameObject itemObj = new GameObject($"Item_{index}_{folderName}");
            itemObj.transform.SetParent(_ellipsisPopup.transform, false);

            RectTransform itemRT = itemObj.AddComponent<RectTransform>();
            itemRT.anchorMin = new Vector2(0, 1);
            itemRT.anchorMax = new Vector2(1, 1);
            itemRT.pivot = new Vector2(0.5f, 1);
            itemRT.sizeDelta = new Vector2(0, itemHeight);

            float yPos = -verticalPadding - (index * itemHeight);
            itemRT.anchoredPosition = new Vector2(0, yPos);

            Debug.Log($"[EllipsisPopup] Item {index} '{folderName}': yPos={yPos}, itemHeight={itemHeight}");

            // Background for hover effect (starts transparent)
            Image btnBg = itemObj.AddComponent<Image>();
            btnBg.color = Color.clear;
            btnBg.raycastTarget = true;

            // Button with no color transition (we handle hover via EventTrigger)
            Button btn = itemObj.AddComponent<Button>();
            btn.targetGraphic = btnBg;
            btn.transition = Selectable.Transition.None;

            string path = folderPath;
            btn.onClick.AddListener(() => OnHiddenFolderSelected(path));

            // Add hover effect via EventTrigger (ColorTint doesn't work with Color.clear)
            Color hoverColor = new Color(0f, 0f, 0f, 0.27f); // Dark hover like gridItem
            UnityEngine.EventSystems.EventTrigger trigger = itemObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            UnityEngine.EventSystems.EventTrigger.Entry enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener((data) => { btnBg.color = hoverColor; });
            trigger.triggers.Add(enterEntry);

            UnityEngine.EventSystems.EventTrigger.Entry exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            exitEntry.callback.AddListener((data) => { btnBg.color = Color.clear; });
            trigger.triggers.Add(exitEntry);

            // Text label (left aligned)
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(itemObj.transform, false);

            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(horizontalPadding, 0);
            textRT.offsetMax = new Vector2(-horizontalPadding, 0);

            TextMeshProUGUI txt = textObj.AddComponent<TextMeshProUGUI>();
            txt.text = folderName;
            txt.font = _font;
            txt.fontSize = 30;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.raycastTarget = false;
            txt.textWrappingMode = TextWrappingModes.NoWrap; // Single line - prevent wrap causing vertical expansion
            txt.overflowMode = TextOverflowModes.Ellipsis; // Truncate with ... if too long
            txt.maxVisibleLines = 1;

            // BoxCollider for VR raycast
            BoxCollider col = itemObj.AddComponent<BoxCollider>();
            col.size = new Vector3(_ellipsisPopupWidth, itemHeight, 0.1f);
            col.center = Vector3.zero;
        }

        /// <summary>
        /// Toggle ellipsis popup visibility
        /// </summary>
        private void ToggleEllipsisPopup()
        {
            if (_ellipsisPopup == null) return;
            _ellipsisPopup.SetActive(!_ellipsisPopup.activeSelf);
        }

        /// <summary>
        /// Handle selection of a hidden folder from ellipsis popup
        /// </summary>
        private void OnHiddenFolderSelected(string path)
        {
            // Hide popup
            if (_ellipsisPopup != null)
            {
                _ellipsisPopup.SetActive(false);
            }

            // Navigate back to selected folder (restores scroll position)
            _controller?.NavigateBack(path);
        }

        #region Scan Status (Category Filter Mode)

        /// <summary>
        /// Show or hide the scan status animated dots (displayed in Row2 next to breadcrumbs).
        /// While scanning, displays animated ".", "..", "..." dots.
        /// </summary>
        /// <param name="show">True to show animated dots, false to hide</param>
        /// <param name="message">Unused - kept for API compatibility</param>
        /// <param name="progress">Unused - kept for API compatibility</param>
        public void ShowScanningIndicator(bool show, string message, float progress)
        {
            if (_scanStatusText == null) return;

            if (show)
            {
                // Start dots animation if not already running
                if (_dotsAnimationCoroutine == null)
                {
                    _dotsAnimationCoroutine = StartCoroutine(AnimateDotsCoroutine());
                }
            }
            else
            {
                // Stop animation and hide text
                if (_dotsAnimationCoroutine != null)
                {
                    StopCoroutine(_dotsAnimationCoroutine);
                    _dotsAnimationCoroutine = null;
                }
                _scanStatusText.text = "";
            }
        }

        /// <summary>
        /// Coroutine that animates the scan status text with cycling dots: ".", "..", "..."
        /// </summary>
        private System.Collections.IEnumerator AnimateDotsCoroutine()
        {
            int dotCount = 1;
            while (true)
            {
                _scanStatusText.text = new string('.', dotCount);
                dotCount = (dotCount % 3) + 1; // Cycle 1 -> 2 -> 3 -> 1
                yield return new WaitForSeconds(0.4f);
            }
        }

        /// <summary>
        /// Update breadcrumb for filter mode (e.g., "All Videos", "All Music").
        /// Uses existing pill button style for consistency.
        /// </summary>
        /// <param name="displayName">Name to show in breadcrumb</param>
        /// <param name="canCreateFolder">Whether folder creation is allowed (false for filter mode)</param>
        /// <param name="sidePanelId">Optional side panel item ID to select (e.g., "videos", "music")</param>
        public void UpdateBreadcrumb(string displayName, bool canCreateFolder, string sidePanelId = null)
        {
            if (_breadcrumbContainer == null) return;

            // Clear existing breadcrumbs
            foreach (Transform child in _breadcrumbContainer)
            {
                Destroy(child.gameObject);
            }

            // Use existing pill button style
            float btnHeight = 75f;
            float btnWidth = _sortTriggerWidth * 1.2f;

            // Create single pill button for filter mode (uses accent color as "active")
            GameObject btn = CreateBreadcrumbPillButton(displayName, "", true, btnWidth, btnHeight, 0, 1, false);

            RectTransform btnRT = btn.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(0, 0.5f);
            btnRT.anchorMax = new Vector2(0, 0.5f);
            btnRT.pivot = new Vector2(0, 0.5f);
            btnRT.anchoredPosition = Vector2.zero;

            // Disable new folder button in filter mode
            if (_newFolderButton != null && _newFolderCanvasGroup != null)
            {
                _newFolderButton.interactable = canCreateFolder;
                _newFolderCanvasGroup.alpha = canCreateFolder ? 1f : 0.4f;
                _newFolderCanvasGroup.interactable = canCreateFolder;
                _newFolderCanvasGroup.blocksRaycasts = canCreateFolder;

                var collider = _newFolderButton.GetComponent<BoxCollider>();
                if (collider != null)
                {
                    collider.enabled = canCreateFolder;
                }
            }

            // Update side panel selection
            if (!string.IsNullOrEmpty(sidePanelId))
            {
                _sidePanel?.SelectById(sidePanelId);
            }
            else
            {
                _sidePanel?.ClearSelection();
            }
        }

        /// <summary>
        /// Clear all items in the file grid/list (for scan mode).
        /// </summary>
        public void ClearFileView()
        {
            if (_isGridView && _fileGrid != null)
            {
                _fileGrid.Populate(new List<MockFile>(), "");
            }
            else if (_fileList != null)
            {
                _fileList.Populate(new List<MockFile>(), "");
            }
        }

        #endregion
    }
}
