using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.Config;
using VRWorkspace.UI.Utilities;
using VRWorkspace.Media.Data;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.UI.RTT.Controllers;
using VRWorkspace.Presentation.Input.VCS;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// Main View for Media Library App.
    /// Orchestrates the 3-panel layout: Side Panel (Left), Grid (Center), Detail (Right).
    /// Based on RTTFileManager pattern with modifications:
    /// - No New Folder button
    /// - Grid only (no List view)
    /// - Edit mode: Rename and Delete only (no Copy/Move)
    /// </summary>
    public partial class RTTMediaLibrary : MonoBehaviour
    {
        #region Configuration
        private RTTMediaLibraryController _controller;
        private float _containerWidth;
        private float _containerHeight;
        private TMP_FontAsset _font;
        private Color _primaryColor;
        private Color _accentColor;
        #endregion

        #region UI References
        private RTTMenuFrame _menuFrame;
        private RTTMenuFrame _leftFrame;
        private RTTMenuFrame _rightFrame;

        private RTTMediaSidePanel _sidePanel;
        private RTTMediaGrid _grid;
        private RTTFileDetail _detailPanel;
        private RTTFilePagination _pagination;

        // Media Action Bar (below detail panel)
        private RTTMediaActionBar _mediaActionBar;
        private MediaVideoInfo? _currentVideo;

        // Track if there's an actual selected video (not just hovered)
        private bool _hasSelectedVideo = false;

        // Flag to track if initial setup is complete
        private bool _viewReady = false;

        // Edit Mode
        private bool _isEditMode = false;
        private GameObject _editButton;
        private HashSet<string> _selectedItems = new HashSet<string>();
        private GameObject _selectAllCheckbox;
        private Image _selectAllCheckmark;

        // Edit Mode UI in Row2
        private GameObject _editControlsContainer;
        private TextMeshProUGUI _selectedCountText;

        // Edit Mode Action Buttons
        private Button _renameButton;
        private Button _deleteButton;
        private CanvasGroup _renameButtonCG;
        private CanvasGroup _deleteButtonCG;
        private HoverEffectController _renameButtonHover;
        private HoverEffectController _deleteButtonHover;

        // Close button reference
        private GameObject _closeButton;
        private Image _closeButtonIcon;
        private Sprite _originalCloseIcon;
        private Sprite _originalEditIcon;
        #endregion

        #region Properties
        public RTTMediaSidePanel SidePanel => _sidePanel;
        public RTTMediaGrid Grid => _grid;
        public RTTFileDetail DetailPanel => _detailPanel;
        #endregion

        #region Events
        public event Action<MediaVideoInfo> OnVideoPlayRequested;
        public event Action OnCloseRequested;
        #endregion

        #region Header References
        private RectTransform _headerRT;
        private RectTransform _bodyRT;
        private float _singleRowHeight;
        private float _rowSpacing;
        private float _headerHeight2Rows;

        // Breadcrumb References
        private Transform _breadcrumbContainer;
        private TextMeshProUGUI _breadcrumbText;

        // View Options
        private RTTPopupMenu _viewOptionsPopup;
        private string _currentSortBy = "Name";
        private bool _isAscending = true;
        private TextMeshProUGUI _sortTriggerText;
        private TextMeshProUGUI _itemCountText;

        private float _sortTriggerWidth = 220f;

        // Group By Options
        private RTTPopupMenu _groupOptionsPopup;
        private string _currentGroupBy = "Date Added";
        private TextMeshProUGUI _groupTriggerText;

        // Rename Popup
        private RTTPopupInputable _renamePopup;
        private string _renameTargetPath;

        // Delete Confirmation Popup
        private RTTPopupMenu _deleteConfirmPopup;

        // Content fade animation
        private CanvasGroup _bodyCanvasGroup;
        private Coroutine _fadeCoroutine;
        private const float CATEGORY_TRANSITION_DURATION = 0.12f;

        // Side Panel Gap
        private const float SIDE_PANEL_GAP = 0.05f;
        #endregion

        #region Initialization
        public void Initialize(RTTMediaLibraryController controller, RTTMenuFrame menuFrame,
            float w, float h, TMP_FontAsset font, Color primary, Color accent)
        {
            _controller = controller;
            _menuFrame = menuFrame;
            _containerWidth = w;
            _containerHeight = h;
            _font = font;
            _primaryColor = primary;
            _accentColor = accent;

            BuildUI();
        }

        public void BuildUI()
        {
            // Debug.Log("[RTTMediaLibrary] Building UI...");

            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();

            if (_menuFrame != null)
            {
                CreatePanels();
            }
            else
            {
                Debug.LogWarning("[RTTMediaLibrary] Parent RTTMenuFrame not found, cannot create side panels correctly.");
            }
        }

        public void Cleanup()
        {
            _viewReady = false;

            var zoomController = VirtualObjectsZoomController.Instance;
            if (zoomController != null)
            {
                if (_leftFrame != null) zoomController.UnregisterSidePanel(_leftFrame);
                if (_rightFrame != null) zoomController.UnregisterSidePanel(_rightFrame);
            }

            if (_leftFrame != null)
            {
                Destroy(_leftFrame.gameObject);
                _leftFrame = null;
            }
            if (_rightFrame != null)
            {
                Destroy(_rightFrame.gameObject);
                _rightFrame = null;
            }
            if (_pagination != null)
            {
                Destroy(_pagination.gameObject);
                _pagination = null;
            }
            if (_mediaActionBar != null)
            {
                Debug.Log("[RTTMediaLibrary] Cleanup: Destroying _mediaActionBar");
                _mediaActionBar.HideImmediate();
                Destroy(_mediaActionBar.gameObject);
                _mediaActionBar = null;
            }

            if (_viewOptionsPopup != null)
            {
                Destroy(_viewOptionsPopup.gameObject);
                _viewOptionsPopup = null;
            }

            if (_groupOptionsPopup != null)
            {
                Destroy(_groupOptionsPopup.gameObject);
                _groupOptionsPopup = null;
            }

            if (_renamePopup != null)
            {
                Destroy(_renamePopup.gameObject);
                _renamePopup = null;
            }

            if (_deleteConfirmPopup != null)
            {
                Destroy(_deleteConfirmPopup.gameObject);
                _deleteConfirmPopup = null;
            }
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

            // Show action bar when app is re-opened (if not in edit mode)
            // BUT only if user previously selected a video - prevents showing during dwell pre-loading
            if (_mediaActionBar != null && !_isEditMode && _hasSelectedVideo)
            {
                _mediaActionBar.ShowWithFade();
            }
        }

        private void OnDisable()
        {
            if (_leftFrame != null) _leftFrame.gameObject.SetActive(false);
            if (_rightFrame != null) _rightFrame.gameObject.SetActive(false);
            if (_pagination != null) _pagination.Hide();

            if (_mediaActionBar != null) _mediaActionBar.HideImmediate();

            if (_viewOptionsPopup != null) _viewOptionsPopup.Hide();
            if (_groupOptionsPopup != null) _groupOptionsPopup.Hide();
        }

        private void OnDestroy()
        {
            Cleanup();

            if (_grid != null)
            {
                _grid.OnVideoSelected -= OnGridVideoSelected;
                _grid.OnVideoDoubleClicked -= OnGridVideoDoubleClicked;
                _grid.OnVideoHoverEnter -= OnGridVideoHoverEnter;
                _grid.OnVideoHoverExit -= OnGridVideoHoverExit;
                _grid.OnPageChanged -= OnGridPageChanged;
            }

            // RTTFileDetail doesn't have events - action buttons are handled directly

            if (_sidePanel != null)
            {
                _sidePanel.OnCategorySelected -= OnCategorySelected;
            }
        }
        #endregion

        #region Panel Creation
        private void CreatePanels()
        {
            if (_menuFrame == null) return;

            // 1. Create Pagination
            CreatePagination();

            // 2. Create Side Panels
            CreateSidePanels();

            // 3. Setup Center Grid
            StartCoroutine(CreateCenterGrid());
        }

        private void CreatePagination()
        {
            RTTToolbar toolbar = RTTToolbar.Instance;
            if (toolbar == null)
            {
                Debug.LogWarning("[RTTMediaLibrary] RTTToolbar not found, pagination positioning may be incorrect");
                toolbar = RTTToolbar.Create();
            }

            GameObject pagObj = new GameObject("MediaPagination");
            pagObj.transform.SetParent(toolbar.transform, false);
            pagObj.transform.localPosition = toolbar.GetPaginationLocalPosition();
            pagObj.transform.localRotation = Quaternion.identity;

            _pagination = pagObj.AddComponent<RTTFilePagination>();
            _pagination.Initialize(_controller);
        }

        private void CreateSidePanels()
        {
            float mainPanelWidth = _menuFrame.PanelWidth;
            float mainPanelHeight = _menuFrame.PanelHeight;
            float sideWidth = mainPanelWidth / 3f;
            float sideHeight = mainPanelHeight;

            // Left Panel (Navigation) - created with alpha=0, will fade in with main frame
            PlaceSidePanelOnSphere("MediaNavigationPanel", -1, mainPanelWidth, sideWidth, sideHeight, SIDE_PANEL_GAP, ref _leftFrame);
            if (_leftFrame != null)
            {
                // Display quad enabled but transparent - ready for coordinated fade
                _leftFrame.SetVisible(true);
                SetFrameAlpha(_leftFrame, 0f);
                StartCoroutine(CreateLeftPanelContent());
            }

            // Right Panel (Detail) - created with alpha=0, will fade in with main frame
            PlaceSidePanelOnSphere("MediaDetailPanel", 1, mainPanelWidth, sideWidth, sideHeight, SIDE_PANEL_GAP, ref _rightFrame);
            if (_rightFrame != null)
            {
                // Display quad enabled but transparent - ready for coordinated fade
                _rightFrame.SetVisible(true);
                SetFrameAlpha(_rightFrame, 0f);
                StartCoroutine(CreateRightPanelContent());
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
                quad.material.color = new UnityEngine.Color(1f, 1f, 1f, alpha);
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

        #region Content Fade Animation

        /// <summary>
        /// Fade out the content area (grid) and pagination page buttons.
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
            _fadeCoroutine = StartCoroutine(FadeContentCoroutine(1f, 0f, CATEGORY_TRANSITION_DURATION, onComplete));
        }

        /// <summary>
        /// Fade in the content area (grid) and pagination page buttons.
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

            _fadeCoroutine = StartCoroutine(FadeContentCoroutine(0f, 1f, CATEGORY_TRANSITION_DURATION, onComplete));
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

        /// <summary>
        /// Show side panels (legacy - now handled by coordinated fade in RTTAppManager).
        /// </summary>
        public void ShowSidePanels()
        {
            Debug.Log($"[RTTMediaLibrary] ShowSidePanels called - left={(_leftFrame != null)}, right={(_rightFrame != null)}");
            // Side panels now fade in with main frame via coordinated animation
            // This method kept for backward compatibility
        }

        /// <summary>
        /// Hide side panels (for app closing).
        /// </summary>
        public void HideSidePanels()
        {
            if (_leftFrame != null)
                _leftFrame.SetVisible(false);
            if (_rightFrame != null)
                _rightFrame.SetVisible(false);
        }

        private void PlaceSidePanelOnSphere(string name, int side, float mainWidth, float sideWidth, float sideHeight,
            float gap, ref RTTMenuFrame frameRef)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[RTTMediaLibrary] PlaceSidePanelOnSphere: No main camera found!");
                return;
            }

            if (_menuFrame == null)
            {
                Debug.LogError("[RTTMediaLibrary] PlaceSidePanelOnSphere: No app frame (_menuFrame) found!");
                return;
            }

            Vector3 mainRight = _menuFrame.transform.right;
            float innerEdgeOffset = (mainWidth / 2f) + gap;
            Vector3 innerEdgePos = _menuFrame.transform.position + mainRight * innerEdgeOffset * side;

            float w = sideWidth / 2f;
            Vector3 cameraPos = cam.transform.position;

            float a = innerEdgePos.x - cameraPos.x;
            float b = innerEdgePos.z - cameraPos.z;
            float distSq = a * a + b * b;
            float dist = Mathf.Sqrt(distSq);

            Vector3 panelPos;
            Quaternion panelRotation;

            if (dist < 0.001f)
            {
                panelPos = innerEdgePos + mainRight * w * side;
                panelPos.y = _menuFrame.transform.position.y;
                panelRotation = Quaternion.LookRotation(-mainRight * side, Vector3.up);
            }
            else
            {
                float rSq = distSq - w * w;
                if (rSq < 0.0001f) rSq = 0.0001f;
                float r = Mathf.Sqrt(rSq);

                float alpha = Mathf.Atan2(b, a);
                float sinArg = Mathf.Clamp((w * side) / dist, -1f, 1f);
                float theta = alpha - Mathf.Asin(sinArg);

                float dx = Mathf.Cos(theta);
                float dz = Mathf.Sin(theta);
                panelPos = new Vector3(
                    cameraPos.x + dx * r,
                    _menuFrame.transform.position.y,
                    cameraPos.z + dz * r
                );

                Vector3 toCamera = new Vector3(cameraPos.x - panelPos.x, 0, cameraPos.z - panelPos.z);
                if (toCamera.sqrMagnitude > 0.001f)
                {
                    panelRotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
                else
                {
                    panelRotation = Quaternion.LookRotation(-mainRight * side, Vector3.up);
                }
            }

            float logicalWidthPixels = (sideWidth / _menuFrame.PanelWidth) * _menuFrame.LogicalWidthValue;

            frameRef = RTTMenuFrame.Create(_menuFrame.transform, sideWidth, sideHeight, logicalWidthPixels, name);
            frameRef.transform.position = panelPos;
            frameRef.transform.rotation = panelRotation;
            frameRef.transform.localScale = Vector3.one;

            frameRef.SetContentMargins(20f, 20f, 20f, 20f);
            frameRef.SetFloatingDataEnabled(true, 5);

            var zoomController = VirtualObjectsZoomController.Instance;
            if (zoomController != null)
            {
                zoomController.RegisterSidePanel(frameRef, _menuFrame.transform, side, mainWidth, sideWidth, gap);
            }

            // Debug.Log($"[RTTMediaLibrary] Placed {name}: pos={panelPos}");
        }

        private IEnumerator CreateLeftPanelContent()
        {
            Debug.Log($"[RTTMediaLibrary] CreateLeftPanelContent started at {Time.realtimeSinceStartup:F3}s");
            while (_leftFrame.ContentContainer == null) yield return null;

            var containerSize = _leftFrame.GetContentSize();

            GameObject contentObj = new GameObject("SidePanelContent");
            contentObj.transform.SetParent(_leftFrame.ContentContainer, false);

            RectTransform rt = contentObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _sidePanel = contentObj.AddComponent<RTTMediaSidePanel>();

            // Subscribe to event BEFORE Initialize so we catch the initial selection
            _sidePanel.OnCategorySelected += OnCategorySelected;

            _sidePanel.Initialize(_controller, containerSize.x, containerSize.y, _font, _primaryColor, _accentColor);

            _leftFrame.MarkDirty();
            Debug.Log($"[RTTMediaLibrary] Left panel content created at {Time.realtimeSinceStartup:F3}s");
        }

        private IEnumerator CreateRightPanelContent()
        {
            Debug.Log($"[RTTMediaLibrary] CreateRightPanelContent started at {Time.realtimeSinceStartup:F3}s");
            while (_rightFrame.ContentContainer == null) yield return null;

            var containerSize = _rightFrame.GetContentSize();

            GameObject contentObj = new GameObject("DetailContent");
            contentObj.transform.SetParent(_rightFrame.ContentContainer, false);

            RectTransform rt = contentObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Use RTTFileDetail instead of RTTMediaDetail
            _detailPanel = contentObj.AddComponent<RTTFileDetail>();
            _detailPanel.Initialize(_primaryColor, _accentColor, _font);

            // Start with empty state - will be populated when first video is selected
            _detailPanel.ShowEmpty();

            _rightFrame.MarkDirty();

            // Create action bar below the panel (follows panel position)
            CreateMediaActionBar();

            Debug.Log($"[RTTMediaLibrary] Right panel content created at {Time.realtimeSinceStartup:F3}s");
        }

        /// <summary>
        /// Create RTTMediaActionBar that follows the detail panel.
        /// </summary>
        private void CreateMediaActionBar()
        {
            if (_rightFrame == null) return;

            // Don't create if already exists
            if (_mediaActionBar != null)
            {
                Debug.Log("[RTTMediaLibrary] ActionBar already exists, skipping creation");
                return;
            }

            // Get panel world size for positioning calculations
            Vector2 panelSize = _rightFrame.GetWorldSize();
            float targetHeight = panelSize.y;
            float panelWidth = panelSize.x;

            // Create action bar that follows the right panel
            _mediaActionBar = RTTMediaActionBar.Create(
                _rightFrame.transform,
                targetHeight,
                panelWidth,
                _primaryColor,
                _accentColor,
                _font
            );

            // Register the action bar as a VCS surface so the cursor can traverse
            // from the Right Side Panel downward into the bar (mirrors Main → Pagination).
            float frameHeight = RTTToolbar.Instance != null && RTTToolbar.Instance.TaskbarHeight > 0
                ? RTTToolbar.Instance.TaskbarHeight
                : 0.12f;
            var barCtrl = ActionBarSurfaceController.Attach(
                _mediaActionBar.gameObject,
                panelWidth,
                frameHeight,
                _rightFrame);
            // Mirror bar visibility into VCS
            _mediaActionBar.OnVisibilityChanged += visible => barCtrl?.NotifyVisible(visible);

            // Wire up events
            _mediaActionBar.OnPlayClicked += OnPlayButtonClicked;
            _mediaActionBar.OnFavouriteClicked += OnFavouriteButtonClicked;

            // Start hidden - will fade in when first video is selected
            // This creates smooth progressive loading: grid groups -> items -> select first -> detail + actionbar
            _mediaActionBar.HideImmediate();
        }

        private void ShowActionBarAfterDelay()
        {
            if (_mediaActionBar != null && !_isEditMode)
            {
                _mediaActionBar.ShowWithFade();
                Debug.Log("[RTTMediaLibrary] Media action bar shown after delay");
            }
        }
        #endregion

        #region Utility Methods
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
