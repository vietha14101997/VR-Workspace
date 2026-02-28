using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.Config;
using VRWorkspace.UI.Utilities;
using VRWorkspace.Media.Core;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Controllers;
using VRWorkspace.UI.RTT.Services;
using VRWorkspace.Utilities;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// Main View for File Manager App.
    /// Orchestrates the 3-panel layout: Side Panel (Left), Grid (Center), Detail (Right).
    /// </summary>
    public partial class RTTFileManager : MonoBehaviour
    {
        #region Configuration
        private RTTFileManagerController _controller;
        private float _containerWidth;
        private float _containerHeight;
        private TMP_FontAsset _font;
        private Color _primaryColor;
        private Color _accentColor;
        #endregion

        #region UI References
        private RTTMenuFrame _menuFrame; // Main Center Frame (Parent)
        private RTTMenuFrame _leftFrame;
        private RTTMenuFrame _rightFrame;

        private RTTFileSidePanel _sidePanel;
        private RTTFileGrid _fileGrid;
        private RTTFileList _fileList;
        private RTTFilePagination _pagination;
        private RTTPopupInputable _createFolderPopup;
        private RTTFileActionBar _fileActionBar;
        private MockFile _currentDisplayedFile;

        // New Folder button reference (for enabling/disabling based on permissions)
        private Button _newFolderButton;
        private CanvasGroup _newFolderCanvasGroup;

        // Flag to track if initial setup is complete (used to prevent premature Show in OnEnable)
        private bool _viewReady = false;

        // Edit Mode
        private bool _isEditMode = false;
        private GameObject _editButton;
        private HashSet<string> _selectedItems = new HashSet<string>();
        private GameObject _selectAllCheckbox;
        private Image _selectAllCheckmark;

        // Edit Mode UI in Row2
        private GameObject _editControlsContainer; // Container for edit mode controls (replaces breadcrumbs)
        private TextMeshProUGUI _selectedCountText; // "X selected" text to the left of item count (shown in Edit Mode)

        // Edit Mode Action Buttons
        private Button _renameButton;
        private Button _copyButton;
        private Button _moveButton;
        private Button _deleteButton;
        private CanvasGroup _renameButtonCG;
        private CanvasGroup _copyButtonCG;
        private CanvasGroup _moveButtonCG;
        private CanvasGroup _deleteButtonCG;
        private HoverEffectController _renameButtonHover;
        private HoverEffectController _copyButtonHover;
        private HoverEffectController _moveButtonHover;
        private HoverEffectController _deleteButtonHover;

        // Rename Popup
        private RTTPopupInputable _renamePopup;
        private string _renameTargetPath; // Path of item being renamed

        // Delete Confirmation Popup
        private RTTPopupMenu _deleteConfirmPopup;

        // Clipboard Mode (for Copy/Move operations)
        private bool _isClipboardMode = false;
        private ClipboardOperation _clipboardOperation;
        private List<string> _clipboardItems = new List<string>();
        private string _clipboardSourcePath;

        // State to restore when exiting clipboard mode
        private bool _preClipboardHasSelectedFile = false;
        private MockFile _preClipboardDisplayedFile;

        private enum ClipboardOperation { None, Copy, Move }

        // Close button reference (for clipboard mode icon/function swap)
        private GameObject _closeButton;
        private Image _closeButtonIcon;
        private Sprite _originalCloseIcon;
        private Sprite _originalEditIcon;

        // Conflict Dialog
        private RTTPopupMenu _conflictPopup;

        // Progress Popup (for async copy/move operations)
        private RTTProgressPopup _progressPopup;
        private CancellationTokenSource _operationCts;
        private FileOperationService.PauseToken _pauseToken;

        // Pending delete paths (for action bar delete or single file delete)
        private List<string> _pendingDeletePaths;

        // Track if there's an actual selected file (not just hovered)
        private bool _hasSelectedFile = false;

        // Scan Status Text (for category filter mode - shown in Row2 next to breadcrumbs)
        private TextMeshProUGUI _scanStatusText;
        private Coroutine _dotsAnimationCoroutine;

        // Content fade animation
        private CanvasGroup _bodyCanvasGroup;
        private Coroutine _fadeCoroutine;
        private const float FOLDER_TRANSITION_DURATION = 0.12f;
        #endregion

        #region Initialization
        public void Initialize(RTTFileManagerController controller, float w, float h, TMP_FontAsset font, Color primary, Color accent)
        {
            _controller = controller;
            _containerWidth = w;
            _containerHeight = h;
            _font = font;
            _primaryColor = primary;
            _accentColor = accent;

            // Try to find parent frame
            _menuFrame = GetComponentInParent<RTTMenuFrame>();
        }

        /// <summary>
        /// Get all frames (main + side panels) for coordinated fade animation.
        /// </summary>
        public List<RTTMenuFrame> GetAllFrames()
        {
            var frames = new List<RTTMenuFrame>();
            if (_menuFrame != null) frames.Add(_menuFrame);
            if (_leftFrame != null) frames.Add(_leftFrame);
            if (_rightFrame != null) frames.Add(_rightFrame);
            return frames;
        }

        private void OnEnable()
        {
            // Ensure content is fully opaque when enabled
            // This prevents being stuck at low alpha if a fade coroutine was interrupted during background reset
            SetContentAlpha(1f);

            if (!_viewReady) return;

            if (_leftFrame != null) _leftFrame.gameObject.SetActive(true);
            if (_rightFrame != null) _rightFrame.gameObject.SetActive(true);
            if (_pagination != null) _pagination.Show();

            // Show action bar if a file was selected
            if (_fileActionBar != null && _hasSelectedFile && !_isClipboardMode)
            {
                _fileActionBar.SetVisible(true);
            }
        }

        /// <summary>
        /// Set alpha of a frame's display quad.
        /// </summary>
        private void SetFrameAlpha(RTTMenuFrame frame, float alpha)
        {
            if (frame == null) return;
            var quad = frame.GetDisplayQuad();
            if (quad?.material != null)
                quad.material.color = new Color(1f, 1f, 1f, alpha);
        }

        #region Content Fade Animation

        /// <summary>
        /// Fade out the content area (grid/list) and pagination page buttons.
        /// Returns immediately, fade runs async.
        /// Call the callback when fade completes.
        /// </summary>
        public void FadeOutContent(Action onComplete)
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }
            // Also fade out pagination page buttons (frame and arrows stay visible)
            if (_pagination != null)
            {
                _pagination.FadeOutPageButtons();
            }
            _fadeCoroutine = StartCoroutine(FadeContentCoroutine(1f, 0f, FOLDER_TRANSITION_DURATION, onComplete));
        }

        /// <summary>
        /// Fade in the content area (grid/list) and pagination page buttons.
        /// Returns immediately, fade runs async.
        /// </summary>
        public void FadeInContent(Action onComplete = null)
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }
            // Also fade in pagination page buttons
            if (_pagination != null)
            {
                _pagination.FadeInPageButtons();
            }

            // If not active, coroutine won't run. Set alpha immediately.
            if (!gameObject.activeInHierarchy)
            {
                SetContentAlpha(1f);
                onComplete?.Invoke();
                return;
            }

            _fadeCoroutine = StartCoroutine(FadeContentCoroutine(0f, 1f, FOLDER_TRANSITION_DURATION, onComplete));
        }

        /// <summary>
        /// Set content alpha immediately (no animation).
        /// </summary>
        public void SetContentAlpha(float alpha)
        {
            if (_bodyCanvasGroup != null)
            {
                _bodyCanvasGroup.alpha = alpha;
            }
        }

        private IEnumerator FadeContentCoroutine(float from, float to, float duration, Action onComplete)
        {
            if (_bodyCanvasGroup == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            _bodyCanvasGroup.alpha = from;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _bodyCanvasGroup.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }

            _bodyCanvasGroup.alpha = to;
            _fadeCoroutine = null;
            onComplete?.Invoke();
        }

        #endregion

        // This method seems to be intended for the controller, not the view.
        // The view's responsibility is to display selection, not manage the selected file state directly.
        // The UpdateGrid method already takes a selectedPath to update the view's selection.
        // If a method is needed here, it would be to visually highlight a file.
        // public void SelectFile(string path)
        // {
        //      Debug.Log($"[Controller] Selected: {path}");
        //      // Find in current directory (works for both files and folders)
        //      _selectedFile = _currentDirectoryFiles.Find(f => f.Path == path);
        //      UpdateDetailView();
        // }

        public void BuildUI()
        {
            // Load saved preferences
            LoadViewPreferences();

            // Setup Main RectTransform
            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();

            // Create Back Button (Standard for all apps)
            // Moved to Side Panel as per new design requirements
            // CreateBackButton();

            // Initialize 3-Panel Layout
            if (_menuFrame != null)
            {
                CreatePanels();
            }
            else
            {
                Debug.LogWarning("[RTTFileManager] Parent RTTMenuFrame not found, cannot create side panels correctly.");
            }
        }

        private void LoadViewPreferences()
        {
            _currentSortBy = PlayerPrefs.GetString(PREF_SORT_BY, "Name");
            _isAscending = PlayerPrefs.GetInt(PREF_SORT_ASCENDING, 1) == 1;
            _isGridView = PlayerPrefs.GetInt(PREF_IS_GRID_VIEW, 1) == 1;
        }

        private void SaveViewPreferences()
        {
            PlayerPrefs.SetString(PREF_SORT_BY, _currentSortBy);
            PlayerPrefs.SetInt(PREF_SORT_ASCENDING, _isAscending ? 1 : 0);
            PlayerPrefs.SetInt(PREF_IS_GRID_VIEW, _isGridView ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void Cleanup()
        {
            // Reset state for potential recreation
            _viewReady = false;

            // Unregister side panels from ZoomController before destroying
            var zoomController = VirtualObjectsZoomController.Instance;
            if (zoomController != null)
            {
                if (_leftFrame != null) zoomController.UnregisterSidePanel(_leftFrame);
                if (_rightFrame != null) zoomController.UnregisterSidePanel(_rightFrame);
            }

            // Destroy side panels when main app is closed
            if (_leftFrame != null) Destroy(_leftFrame.gameObject);
            if (_rightFrame != null) Destroy(_rightFrame.gameObject);
            if (_pagination != null) Destroy(_pagination.gameObject);

            // Destroy world-space popups (not parented to this object)
            if (_createFolderPopup != null) Destroy(_createFolderPopup.gameObject);
            _createFolderPopup = null;

            if (_renamePopup != null) Destroy(_renamePopup.gameObject);
            _renamePopup = null;

            if (_viewOptionsPopup != null) Destroy(_viewOptionsPopup.gameObject);
            _viewOptionsPopup = null;

            if (_deleteConfirmPopup != null) Destroy(_deleteConfirmPopup.gameObject);
            _deleteConfirmPopup = null;

            if (_conflictPopup != null) Destroy(_conflictPopup.gameObject);
            _conflictPopup = null;

            if (_progressPopup != null) Destroy(_progressPopup.gameObject);
            _progressPopup = null;

            if (_fileActionBar != null)
            {
                _fileActionBar.HideImmediate();
                Destroy(_fileActionBar.gameObject);
            }
            _fileActionBar = null;

            // Cancel any ongoing operation
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _operationCts = null;
        }

        private void OnDisable()
        {
            // Hide external components when this view is disabled (e.g. app switch)
            if (_leftFrame != null) _leftFrame.gameObject.SetActive(false);
            if (_rightFrame != null) _rightFrame.gameObject.SetActive(false);
            if (_pagination != null) _pagination.Hide();

            // Hide action bar immediately when switching apps
            if (_fileActionBar != null) _fileActionBar.HideImmediate();

            // Hide world-space popup
            if (_viewOptionsPopup != null) _viewOptionsPopup.Hide();
        }

        private void OnDestroy()
        {
            Cleanup();
        }
        #endregion

        #region Internal Helpers
        private void CreateBackButton()
        {
            // Use VRButtonFactory to create standard back button
            var btn = VRButtonFactory.CreateHorizontalIconTextButton(
                transform,
                200f,
                60f,
                "BACK",
                null, // TODO: Load Back Icon
                _primaryColor,
                () => _controller?.HandleBack(),
                32,
                _font
            );

            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(40f, -40f);
        }

        private void CreatePanels()
        {
            if (_menuFrame == null) return;

            // 1. Create Pagination (Phase 4) - Create first to ensure it's ready for invalidation in OnViewReady
            CreatePagination();

            // 2. Create Side Panels
            CreateSidePanels();

            // 3. Setup Center Grid (Main Frame Content)
            StartCoroutine(CreateCenterGrid());
        }

        private void CreatePagination()
        {
            // Get RTTToolbar to parent pagination into
            RTTToolbar toolbar = RTTToolbar.Instance;
            if (toolbar == null)
            {
                Debug.LogWarning("[RTTFileManager] RTTToolbar not found, pagination positioning may be incorrect");
                // Fallback: create toolbar
                toolbar = RTTToolbar.Create();
            }

            GameObject pagObj = new GameObject("FilePagination");

            // Parent into RTTToolbar
            pagObj.transform.SetParent(toolbar.transform, false);

            // Set local position (above taskbar area)
            pagObj.transform.localPosition = toolbar.GetPaginationLocalPosition();
            pagObj.transform.localRotation = Quaternion.identity;

            _pagination = pagObj.AddComponent<RTTFilePagination>();
            _pagination.Initialize(_controller);
        }

        // Header References
        private RectTransform _headerRT;
        private RectTransform _bodyRT;
        private float _singleRowHeight;
        private float _rowSpacing; // Spacing between header rows
        private float _headerHeight2Rows; // Height with 2 rows


        // Breadcrumb References
        private Transform _breadcrumbContainer;

        private IEnumerator CreateCenterGrid()
        {
            // Wait for ContentContainer of the main frame
             while (_menuFrame.ContentContainer == null) yield return null;

             // Placeholder text removal
             foreach(Transform child in _menuFrame.ContentContainer)
             {
                 if (child.name == "PlaceholderText") Destroy(child.gameObject);
             }

            // Get Dimensions
            Vector2 contentSize = _menuFrame.GetContentSize();
            float panelHeight = contentSize.y;

            // Logic kích thước mới: Header gốc (2 rows) = 19.8% panel (+10% from 18%).
            // Mỗi row = 9.9%. Row 3 sẽ thêm 9.9% khi hiện.
            float headerBaseHeight = panelHeight * 0.198f; // +10% (was 0.18f)
            _singleRowHeight = headerBaseHeight / 2f;

            // Ensure minimum row height to fit 75f buttons (+10% from 68f)
            if (_singleRowHeight < 88f) // +10% (was 80f)
            {
                _singleRowHeight = 88f;
            }

            // Row spacing = ~19% of row height (reduced by 25%)
            _rowSpacing = _singleRowHeight * 0.1875f;

            // Header height = 2 rows + 1 gap between them
            _headerHeight2Rows = (_singleRowHeight * 2f) + _rowSpacing;

            float bottomPadding = panelHeight * 0.02f;

            // 1. Create Body Object (Grid/List Container)
            GameObject bodyObj = new GameObject("Body");
            bodyObj.transform.SetParent(_menuFrame.ContentContainer, false);
            _bodyRT = bodyObj.AddComponent<RectTransform>();
            _bodyRT.anchorMin = Vector2.zero;
            _bodyRT.anchorMax = Vector2.one;
            _bodyRT.offsetMax = new Vector2(0, -_headerHeight2Rows);
            _bodyRT.offsetMin = new Vector2(0, bottomPadding);

            bodyObj.AddComponent<RectMask2D>();

            // Add CanvasGroup for smooth transitions and fade animations during folder navigation
            _bodyCanvasGroup = bodyObj.AddComponent<CanvasGroup>();
            _bodyCanvasGroup.alpha = 0f; // Start hidden for smooth fade-in

            // 2. Create Header Container
            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(_menuFrame.ContentContainer, false);
            _headerRT = headerObj.AddComponent<RectTransform>();
            _headerRT.anchorMin = new Vector2(0, 1);
            _headerRT.anchorMax = new Vector2(1, 1);
            _headerRT.pivot = new Vector2(0.5f, 1);
            _headerRT.anchoredPosition = Vector2.zero;
            _headerRT.sizeDelta = new Vector2(0, _headerHeight2Rows);

            // 3. Build Header Rows
            CreateHeaderRows(_headerRT);

            // 4. Create Grid Inside Body
            GameObject gridObj = new GameObject("FileGrid");
            gridObj.transform.SetParent(_bodyRT, false);
            RectTransform gridRT = gridObj.AddComponent<RectTransform>();
            gridRT.anchorMin = Vector2.zero;
            gridRT.anchorMax = Vector2.one;
            gridRT.offsetMin = Vector2.zero;
            gridRT.offsetMax = Vector2.zero;

            _fileGrid = gridObj.AddComponent<RTTFileGrid>();
            // Calculate size based on BODY size
            float bodyHeight = panelHeight - _headerHeight2Rows - bottomPadding;
            _fileGrid.Initialize(_controller, contentSize.x, bodyHeight, _font);

            // Set interaction callbacks
            _fileGrid.SetItemCallbacks(
                onHoverEnter: (path) => _controller?.HoverFile(path),
                onHoverExit: (path) => _controller?.UnhoverFile(path),
                onClick: (path, isFolder) => OnItemClicked(path, isFolder)
            );
            _fileGrid.SetSelectionChangedCallback(OnSelectionChanged);

            // Render Order
            headerObj.transform.SetAsLastSibling();
            bodyObj.transform.SetAsFirstSibling();

            // Apply saved view mode preference
            if (!_isGridView)
            {
                // User prefers list view - create it and hide grid
                _fileGrid.gameObject.SetActive(false);
                CreateListView();
            }

            // Mark setup complete - enables OnEnable/OnDisable to manage visibility for app switching
            // NOTE: Do NOT show pagination here - it will be shown by OnEnable() when the frame is actually displayed
            // During preparation, the frame is at +1000 units but pagination is at normal position (near taskbar)
            _viewReady = true;

            // Apply saved sort options to controller before loading data (without triggering refresh)
            _controller?.SetSortOptionsNoRefresh(_currentSortBy, _isAscending);

            _controller.OnViewReady();

            // Initialize page size AFTER OnViewReady has loaded data (deferred to next frame for safety)
            StartCoroutine(InitializePageSizeDeferred());

            // Smooth fade-in of content after construction
            FadeInContent();
        }

        // Fixed page sizes per user requirement
        private const int GRID_PAGE_SIZE = 8;  // 2 rows x 4 columns
        private const int LIST_PAGE_SIZE = 6;

        private IEnumerator InitializePageSizeDeferred()
        {
            // Wait one frame to ensure grid has populated
            yield return null;

            // Use fixed page sizes: Grid=8, List=6
            int itemsPerPage = _isGridView ? GRID_PAGE_SIZE : LIST_PAGE_SIZE;
            _controller?.SetPageSize(itemsPerPage);
        }

        private void CreateListView()
        {
            if (_bodyRT == null)
            {
                Debug.LogError("[RTTFileManager] Cannot create list view - body container not ready!");
                return;
            }

            // Create list view in the same body container as grid
            GameObject listObj = new GameObject("FileList");
            listObj.transform.SetParent(_bodyRT, false);

            RectTransform listRT = listObj.AddComponent<RectTransform>();
            listRT.anchorMin = Vector2.zero;
            listRT.anchorMax = Vector2.one;
            listRT.offsetMin = Vector2.zero;
            listRT.offsetMax = Vector2.zero;

            _fileList = listObj.AddComponent<RTTFileList>();

            // Calculate size based on body
            Vector2 contentSize = _menuFrame.GetContentSize();
            float _headerHeight2Rows = (_singleRowHeight * 2f) + _rowSpacing; // 2 rows + spacing
            float bottomPadding = contentSize.y * 0.02f;
            float bodyHeight = contentSize.y - _headerHeight2Rows - bottomPadding;

            _fileList.Initialize(_controller, contentSize.x, bodyHeight, _font, _primaryColor, _accentColor, OnListHeaderColumnClicked);
            _fileList.UpdateSortState(_currentSortBy, _isAscending);

            // Set interaction callbacks
            _fileList.SetItemCallbacks(
                onHoverEnter: (path) => _controller?.HoverFile(path),
                onHoverExit: (path) => _controller?.UnhoverFile(path),
                onClick: (path, isFolder) => OnItemClicked(path, isFolder)
            );
            _fileList.SetSelectionChangedCallback(OnSelectionChanged);
        }

        private void OnListHeaderColumnClicked(string columnName)
        {
            // Toggle ascending/descending if clicking same column
            if (_currentSortBy == columnName)
            {
                _isAscending = !_isAscending;
            }
            else
            {
                _currentSortBy = columnName;
                _isAscending = true;
            }

            // Update sort trigger text
            if (_sortTriggerText != null)
            {
                _sortTriggerText.text = _currentSortBy;
            }

            // Update list header arrows
            if (_fileList != null)
            {
                _fileList.UpdateSortState(_currentSortBy, _isAscending);
            }

            // Apply sort
            _controller?.SetSortOptions(_currentSortBy, _isAscending);
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
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

        /// <summary>
        /// Get base path for a side panel item ID
        /// </summary>
        private string GetBasePath(string id)
        {
            return id switch
            {
                "internal" => FileSystemService.RootPath,
                "sdcard" => FileSystemService.GetSDCardPath(),
                "downloads" => FileSystemService.GetDownloadsPath(),
                "videos" => FileSystemService.GetVideosPath(),
                "music" => FileSystemService.GetMusicPath(),
                "recent" => FileSystemService.RootPath, // Recent uses root as base
                _ => FileSystemService.RootPath
            };
        }

        private void OnItemClicked(string path, bool isFolder)
        {
            // In edit mode, toggle checkbox instead of opening
            if (_isEditMode)
            {
                if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
                    _fileGrid.ToggleItemSelection(path);
                else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
                    _fileList.ToggleItemSelection(path);
                return;
            }

            if (isFolder)
            {
                // Clear file selection when navigating to folder
                _hasSelectedFile = false;
                if (_fileActionBar != null) _fileActionBar.SetVisible(false);

                // Navigate into the folder (saves clicked folder's index for accurate back navigation)
                _controller?.NavigateToFolder(path);
            }
            else
            {
                // Mark that we have a selected file
                _hasSelectedFile = true;

                // Select the file visually on the grid/list
                if (_fileGrid != null && _fileGrid.gameObject.activeInHierarchy)
                    _fileGrid.SelectFile(path);
                else if (_fileList != null && _fileList.gameObject.activeInHierarchy)
                    _fileList.SelectFile(path);

                // Notify controller of selection (updates detail panel)
                _controller?.SelectFile(path);
            }
        }

        private void OnSearchValueChanged(string value)
        {
            _controller.SearchFiles(value);
        }

        private void OnSearchEndEdit(string value)
        {
            _controller.SearchFiles(value);
        }
        #endregion

        #region Public Update Methods

        public void UpdatePagination(int current, int total)
        {
             if (_pagination != null)
             {
                 _pagination.SetPage(current, total);
             }
        }

        public void ScrollToPage(int page)
        {
            if (_isGridView && _fileGrid != null)
            {
                int visibleRows = _fileGrid.GetVisibleRowsForPagination();
                _fileGrid.ScrollToPage(page, visibleRows);
            }
            else if (!_isGridView && _fileList != null)
            {
                int visibleRows = _fileList.GetVisibleRowsForPagination();
                _fileList.ScrollToPage(page, visibleRows);
            }
        }

        /// <summary>
        /// Get current scroll position (normalized 0-1, where 1 = top)
        /// </summary>
        public float GetScrollPosition()
        {
            if (_isGridView && _fileGrid != null)
            {
                return _fileGrid.GetScrollPosition();
            }
            else if (!_isGridView && _fileList != null)
            {
                return _fileList.GetScrollPosition();
            }
            return 1f; // Default to top
        }

        /// <summary>
        /// Set scroll position (normalized 0-1, where 1 = top)
        /// </summary>
        public void SetScrollPosition(float normalizedPosition)
        {
            if (_isGridView && _fileGrid != null)
            {
                _fileGrid.SetScrollPosition(normalizedPosition);
            }
            else if (!_isGridView && _fileList != null)
            {
                _fileList.SetScrollPosition(normalizedPosition);
            }
        }

        public void UpdateGrid(System.Collections.Generic.List<MockFile> files, string selectedPath = "")
        {
            // Exit edit mode when folder changes (but not clipboard mode)
            if (_isEditMode && !_isClipboardMode)
            {
                ToggleEditMode();
            }

            // Clear file selection when folder changes
            _hasSelectedFile = false;
            if (_fileActionBar != null) _fileActionBar.SetVisible(false);

            if (_isGridView)
            {
                if (_fileGrid != null)
                {
                    _fileGrid.Populate(files, selectedPath);
                }
                else
                {
                    Debug.LogWarning("[RTTFileManager] FileGrid not ready yet!");
                }
            }
            else
            {
                if (_fileList != null)
                {
                    _fileList.Populate(files, selectedPath);
                    _fileList.UpdateSortState(_currentSortBy, _isAscending);
                }
                else
                {
                    Debug.LogWarning("[RTTFileManager] FileList not ready yet!");
                }
            }
        }

        /// <summary>
        /// Update the item count label in header
        /// </summary>
        public void UpdateItemCount(int totalItems)
        {
            if (_itemCountText != null)
            {
                _itemCountText.text = totalItems == 1 ? "1 item" : $"{totalItems} items";
            }
        }

        #endregion

        #region Sprite Helpers
        // Cached rounded rectangle sprite for checkboxes
        private static Sprite _cachedRoundedSprite;

        private static Sprite GetRoundedRectSprite()
        {
            if (_cachedRoundedSprite != null) return _cachedRoundedSprite;

            int size = 64;
            int radius = 6;  // Small radius for square-ish checkbox with slight rounding
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[size * size];
            float halfSize = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - halfSize + 0.5f);
                    float dy = Mathf.Abs(y - halfSize + 0.5f);

                    float innerHalfX = halfSize - radius;
                    float innerHalfY = halfSize - radius;

                    float qx = Mathf.Max(dx - innerHalfX, 0f);
                    float qy = Mathf.Max(dy - innerHalfY, 0f);
                    float dist = Mathf.Sqrt(qx * qx + qy * qy) - radius;

                    float alpha = 1f - Mathf.Clamp01((dist + 0.5f) / 1.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            int border = radius + 2;
            _cachedRoundedSprite = Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                Vector2.one * 0.5f,
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border)
            );

            return _cachedRoundedSprite;
        }
        #endregion
    }

}
