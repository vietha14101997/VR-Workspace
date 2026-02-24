using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using System;
using System.Collections.Generic;
using VRWorkspace.UI.Config;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// RTTPopupMenu - A reusable popup menu component using RTTMenuFrame style.
    /// Supports two modes:
    /// 1) Canvas mode: Popup as child of trigger element (legacy)
    /// 2) World-space mode: Standalone popup with dark overlay (like RTTPopupInputable)
    ///
    /// Content sections:
    /// 1) Section Block: Title label + Grid of VRButtonFactory buttons
    /// 2) Full-Width Button: Single button spanning full popup width
    ///
    /// Features:
    /// - Configurable width, auto-calculated height based on content
    /// - Glass background with glow border (RTTMenuFrame style)
    /// - World-space mode with curved dark overlay blocking all other UI
    /// </summary>
    public class RTTPopupMenu : MonoBehaviour
    {
        #region Nested Classes

        /// <summary>
        /// Configuration for the popup
        /// </summary>
        [System.Serializable]
        public class PopupConfig
        {
            public float width = 260f;
            public float buttonHeight = 40f;
            public float sideSpacing = 10f;   // Spacing between sections and padding
            public float rowSpacing = 8f;     // Spacing between button rows in grid
            public float labelHeight = 18f;
            public int labelFontSize = 18;  // Font size for section labels/titles
            public int fontSize = 15;       // Font size for buttons
            public float iconSize = 20f;
            public float borderWidth = 0.05f; // Popup border thickness (shader normalized value)
            public float glassAlpha = 0.55f;  // Popup background transparency (match RTTPopupInputable)
            public Color backgroundColor = new Color(0.12f, 0.12f, 0.16f, 0.96f);
            public Color primaryColor = new Color(0f, 0.9f, 1f);
            public Color accentColor = new Color(0.76f, 0.36f, 1f);
            public Color overlayColor = new Color(0f, 0f, 0f, 0.4f); // Overlay darkness
            public TMP_FontAsset font;
            public string layerName = "VirtualObjects";

            // Button styling (like sortTrigger style)
            public float buttonBorderWidth = 0.04f;
            public float buttonGlowWidth = 0.08f;
            public float buttonGlowIntensity = 4f;
            public float buttonCornerRadius = 0.12f;
        }

        /// <summary>
        /// Data for a single button
        /// </summary>
        [System.Serializable]
        public class ButtonData
        {
            public string text;
            public Sprite icon;           // null = text only
            public Color? color;          // null = use primaryColor
            public bool isSelected;
            public UnityAction onClick;

            public ButtonData() { }

            public ButtonData(string text, UnityAction onClick, Sprite icon = null, bool isSelected = false, Color? color = null)
            {
                this.text = text;
                this.onClick = onClick;
                this.icon = icon;
                this.isSelected = isSelected;
                this.color = color;
            }
        }

        /// <summary>
        /// Section type enum
        /// </summary>
        public enum PopupSectionType
        {
            SectionBlock,     // Type 1: Title + Grid
            FullWidthButton   // Type 2: Single button full width
        }

        /// <summary>
        /// Internal section tracking
        /// </summary>
        private class PopupSection
        {
            public PopupSectionType type;
            public string title;
            public List<ButtonData> buttons;
            public int columns;
            public bool centerTitle;
        }

        #endregion

        #region Private Fields

        private PopupConfig _config;
        private GameObject _popupObject;
        private GameObject _blockerObject;
        private RectTransform _popupRT;
        private VerticalLayoutGroup _contentLayout;
        private List<PopupSection> _sections = new List<PopupSection>();
        private bool _isBuilt = false;
        private PopupSectionType _lastSectionType = PopupSectionType.SectionBlock;
        private GameObject _contentContainer;
        private Material _borderMaterial;

        // World-space mode fields
        private bool _isWorldSpaceMode = false;
        private Canvas _worldCanvas;
        private Camera _mainCamera;
        private Transform _referenceTransform; // Reference element for positioning
        private Vector2 _positionOffset = Vector2.zero; // Offset from reference element

        // External overlays for world-space mode
        private List<GameObject> _externalOverlays = new List<GameObject>();

        private static Sprite _pixelSprite;
        private const float kBorderInset = 6f; // Visual border thickness offset
        private const float WORLD_SPACE_SCALE = 0.001f; // Scale for world-space canvas
        private const float WORLD_SPACE_OFFSET = 0.02f; // Distance in front of reference (meters)

        #endregion

        #region Public Properties

        public bool IsVisible => _isWorldSpaceMode
            ? gameObject.activeSelf
            : (_popupObject != null && _popupObject.activeSelf);
        public RectTransform PopupRectTransform => _popupRT;

        /// <summary>
        /// Static reference to currently open popup (for click-outside-to-close behavior)
        /// </summary>
        public static RTTPopupMenu CurrentlyOpenPopup { get; private set; }

        /// <summary>
        /// Event fired when popup is hidden (including from click-outside)
        /// </summary>
        public event Action OnHide;

        #endregion

        #region Factory Methods

        /// <summary>
        /// Create a new RTTPopupMenu (canvas mode - legacy)
        /// </summary>
        /// <param name="parent">Parent transform for the popup</param>
        /// <param name="config">Popup configuration</param>
        /// <returns>RTTPopupMenu instance</returns>
        public static RTTPopupMenu Create(Transform parent, PopupConfig config)
        {
            GameObject menuObj = new GameObject("RTTPopupMenu");
            menuObj.transform.SetParent(parent, false);

            RTTPopupMenu menu = menuObj.AddComponent<RTTPopupMenu>();
            menu._config = config ?? new PopupConfig();
            menu._isWorldSpaceMode = false;
            menu.BuildPopupContainer();

            return menu;
        }

        /// <summary>
        /// Create with default config (canvas mode)
        /// </summary>
        public static RTTPopupMenu Create(Transform parent, float width, Color primaryColor, TMP_FontAsset font = null)
        {
            var config = new PopupConfig
            {
                width = width,
                primaryColor = primaryColor,
                font = font
            };
            return Create(parent, config);
        }

        /// <summary>
        /// Create a world-space RTTPopupMenu with dark overlay.
        /// The popup floats in front of the reference element and blocks all other UI.
        /// </summary>
        /// <param name="config">Popup configuration</param>
        /// <param name="referenceTransform">Reference transform to position relative to</param>
        /// <returns>RTTPopupMenu instance in world-space mode</returns>
        public static RTTPopupMenu CreateWorldSpace(PopupConfig config, Transform referenceTransform)
        {
            config = config ?? new PopupConfig();

            // Create root object
            GameObject rootObj = new GameObject("RTTPopupMenu_WorldSpace");

            // Find main camera
            Camera mainCam = FindMainCamera();

            // Create world-space Canvas
            Canvas canvas = rootObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = mainCam;
            canvas.sortingOrder = 100; // Higher sorting order to render in front of other canvases

            // Initial canvas size (will be updated when Build is called)
            RectTransform canvasRT = rootObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(config.width + 40f, 400f);
            canvasRT.localScale = Vector3.one * WORLD_SPACE_SCALE;

            // Add GraphicRaycaster for UI interaction
            rootObj.AddComponent<GraphicRaycaster>();

            // Add CanvasScaler
            CanvasScaler scaler = rootObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            // Set layer
            int layer = LayerMask.NameToLayer(config.layerName);
            Debug.Log($"[RTTPopupMenu] CreateWorldSpace: layer '{config.layerName}' = {layer}, mainCam: {(mainCam != null ? mainCam.name : "null")}");
            if (layer != -1)
            {
                rootObj.layer = layer;
            }

            // Create popup component
            RTTPopupMenu menu = rootObj.AddComponent<RTTPopupMenu>();
            menu._config = config;
            menu._isWorldSpaceMode = true;
            menu._worldCanvas = canvas;
            menu._mainCamera = mainCam;
            menu._referenceTransform = referenceTransform;

            menu.BuildWorldSpacePopup();

            // Hidden by default
            rootObj.SetActive(false);

            Debug.Log($"[RTTPopupMenu] Created world-space popup");

            return menu;
        }

        /// <summary>
        /// Find the main camera (VR center eye or main camera)
        /// </summary>
        private static Camera FindMainCamera()
        {
            string[] cameraNames = { "CenterEyeAnchor", "Main Camera", "PlayerCamera", "Camera" };
            foreach (var name in cameraNames)
            {
                GameObject camObj = GameObject.Find(name);
                if (camObj != null)
                {
                    Camera cam = camObj.GetComponent<Camera>();
                    if (cam != null && cam.gameObject.activeInHierarchy) return cam;
                }
            }

            if (Camera.main != null) return Camera.main;

            Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in allCameras)
            {
                if (!cam.name.Contains("UI") && cam.gameObject.activeInHierarchy)
                {
                    return cam;
                }
            }

            return null;
        }

        #endregion

        #region Public API - Add Sections

        /// <summary>
        /// Add a Section Block (Type 1): Title + Grid of buttons
        /// </summary>
        public RTTPopupMenu AddSectionBlock(string title, List<ButtonData> buttons, int columns = 2, bool centerTitle = false)
        {
            _sections.Add(new PopupSection
            {
                type = PopupSectionType.SectionBlock,
                title = title,
                buttons = buttons,
                columns = columns,
                centerTitle = centerTitle
            });
            return this;
        }

        /// <summary>
        /// Add a Full-Width Button (Type 2): Single button spanning full width
        /// </summary>
        public RTTPopupMenu AddFullWidthButton(ButtonData button)
        {
            _sections.Add(new PopupSection
            {
                type = PopupSectionType.FullWidthButton,
                buttons = new List<ButtonData> { button },
                columns = 1
            });
            return this;
        }

        /// <summary>
        /// Add a separator line
        /// </summary>
        public RTTPopupMenu AddSeparator()
        {
            _sections.Add(new PopupSection
            {
                type = PopupSectionType.FullWidthButton,
                buttons = null,
                columns = 0
            });
            return this;
        }

        #endregion

        #region Public API - Build & Lifecycle

        /// <summary>
        /// Build the popup UI from added sections
        /// </summary>
        public RTTPopupMenu Build()
        {
            if (_isBuilt)
            {
                ClearContent();
            }

            float totalHeight = CalculateTotalHeight();
            _popupRT.sizeDelta = new Vector2(_config.width, totalHeight);

            // Update canvas size for world-space mode
            if (_isWorldSpaceMode && _worldCanvas != null)
            {
                RectTransform canvasRT = _worldCanvas.GetComponent<RectTransform>();
                canvasRT.sizeDelta = new Vector2(_config.width + 40f, totalHeight + 40f);
            }

            UpdateBackgroundAspect(totalHeight);

            PopupSectionType? prevSectionType = null;
            for (int i = 0; i < _sections.Count; i++)
            {
                var section = _sections[i];

                // Add spacer between sections (not before first section)
                if (prevSectionType.HasValue)
                {
                    float spacing = GetSpacingBetweenSections(prevSectionType.Value, section.type);
                    BuildSpacer(spacing);
                }

                BuildSection(section);
                _lastSectionType = section.type;
                prevSectionType = section.type;

                // Add spacer after title-only sections (has title but no buttons)
                bool isTitleOnly = section.type == PopupSectionType.SectionBlock
                    && !string.IsNullOrEmpty(section.title)
                    && (section.buttons == null || section.buttons.Count == 0);
                if (isTitleOnly)
                {
                    BuildTitleSpacer();
                }
            }

            _isBuilt = true;
            return this;
        }

        /// <summary>
        /// Get spacing between two section types.
        /// FullWidthButton after SectionBlock uses rowSpacing (closer).
        /// All other transitions use sideSpacing.
        /// </summary>
        private float GetSpacingBetweenSections(PopupSectionType prev, PopupSectionType current)
        {
            // FullWidthButton (like Ascending) should be closer to the section above
            if (prev == PopupSectionType.SectionBlock && current == PopupSectionType.FullWidthButton)
            {
                return _config.rowSpacing;
            }
            return _config.sideSpacing;
        }

        /// <summary>
        /// Build a spacer with specified height
        /// </summary>
        private void BuildSpacer(float height)
        {
            if (height <= 0) return;

            GameObject spacer = new GameObject("Spacer");
            spacer.transform.SetParent(_contentContainer.transform, false);
            SetLayerRecursively(spacer, _popupObject.layer);

            LayoutElement le = spacer.AddComponent<LayoutElement>();
            le.preferredHeight = height;
        }

        /// <summary>
        /// Show the popup
        /// </summary>
        public void Show()
        {
            Debug.Log($"[RTTPopupMenu] Show called, isBuilt: {_isBuilt}, isWorldSpaceMode: {_isWorldSpaceMode}");

            if (!_isBuilt)
            {
                Build();
            }

            // Close any other open popup first
            if (CurrentlyOpenPopup != null && CurrentlyOpenPopup != this)
            {
                CurrentlyOpenPopup.Hide();
            }

            if (_isWorldSpaceMode)
            {
                // Position popup in front of reference
                PositionInFrontOfReference();

                // Activate the entire GameObject
                gameObject.SetActive(true);

                // Create external overlays (visual + blocking)
                CreateExternalOverlays();

                Debug.Log($"[RTTPopupMenu] Shown in world-space mode at {transform.position}");
            }
            else
            {
                // Canvas mode: show blocker and popup
                if (_blockerObject != null) _blockerObject.SetActive(true);
                if (_popupObject != null) _popupObject.SetActive(true);
            }

            CurrentlyOpenPopup = this;
        }

        /// <summary>
        /// Hide the popup
        /// </summary>
        public void Hide()
        {
            // Destroy external overlays
            DestroyExternalOverlays();

            if (_isWorldSpaceMode)
            {
                gameObject.SetActive(false);
            }
            else
            {
                if (_blockerObject != null) _blockerObject.SetActive(false);
                if (_popupObject != null) _popupObject.SetActive(false);
            }

            if (CurrentlyOpenPopup == this)
            {
                CurrentlyOpenPopup = null;
            }

            OnHide?.Invoke();
        }

        /// <summary>
        /// Toggle popup visibility
        /// </summary>
        public void Toggle()
        {
            if (IsVisible) Hide();
            else Show();
        }

        /// <summary>
        /// Check if a GameObject is part of this popup panel
        /// </summary>
        public bool IsPartOfPopupPanel(GameObject obj)
        {
            if (obj == null || _popupObject == null) return false;

            Transform current = obj.transform;
            while (current != null)
            {
                if (current.gameObject == _popupObject || current.gameObject == gameObject)
                    return true;
                current = current.parent;
            }
            return false;
        }

        /// <summary>
        /// Clear all sections and rebuild
        /// </summary>
        public void Clear()
        {
            _sections.Clear();
            ClearContent();
            _isBuilt = false;
        }

        /// <summary>
        /// Set popup position offset from reference (for world-space mode)
        /// </summary>
        public void SetPositionOffset(Vector2 offset)
        {
            _positionOffset = offset;
        }

        /// <summary>
        /// Set popup position (anchored) - for canvas mode
        /// </summary>
        public void SetPosition(Vector2 anchoredPosition)
        {
            if (_popupRT != null)
            {
                _popupRT.anchoredPosition = anchoredPosition;
            }
        }

        /// <summary>
        /// Set anchor and pivot - for canvas mode
        /// </summary>
        public void SetAnchor(Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            if (_popupRT != null)
            {
                _popupRT.anchorMin = anchorMin;
                _popupRT.anchorMax = anchorMax;
                _popupRT.pivot = pivot;
            }
        }

        #endregion

        #region Private - Build Methods (World-Space)

        private void BuildWorldSpacePopup()
        {
            int layer = LayerMask.NameToLayer(_config.layerName);

            // Create popup panel centered on canvas
            _popupObject = new GameObject("PopupPanel");
            _popupObject.transform.SetParent(transform, false);

            _popupRT = _popupObject.AddComponent<RectTransform>();
            _popupRT.anchorMin = new Vector2(0.5f, 0.5f);
            _popupRT.anchorMax = new Vector2(0.5f, 0.5f);
            _popupRT.pivot = new Vector2(0.5f, 0.5f);
            _popupRT.sizeDelta = new Vector2(_config.width, 100f); // Placeholder
            _popupRT.anchoredPosition = Vector2.zero;

            // Background
            CreateBackground();

            // Glow Border
            CreateGlowBorder();

            // Content Container
            _contentContainer = new GameObject("Content");
            _contentContainer.transform.SetParent(_popupObject.transform, false);
            RectTransform contentRT = _contentContainer.AddComponent<RectTransform>();
            contentRT.anchorMin = Vector2.zero;
            contentRT.anchorMax = Vector2.one;
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;

            // Content Layout
            VerticalLayoutGroup vlg = _contentContainer.AddComponent<VerticalLayoutGroup>();
            float horizontalPadding = ((_config.rowSpacing * 0.75f) + kBorderInset) * 1.75f;
            float topPadding = (_config.rowSpacing + kBorderInset) * 2.5f;
            float bottomPadding = (_config.rowSpacing + kBorderInset) * 1.5f;  // Smaller bottom for visual balance
            vlg.padding = new RectOffset((int)horizontalPadding, (int)horizontalPadding, (int)topPadding, (int)bottomPadding);
            vlg.spacing = 0;  // Spacing handled manually via BuildSpacer for fine-grained control
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            _contentLayout = vlg;

            // Add BoxCollider to block physics raycast (prevent click-through to blocking overlays behind)
            BoxCollider panelCollider = _popupObject.AddComponent<BoxCollider>();
            panelCollider.size = new Vector3(_config.width, 100f, 0.1f); // Height will be updated in Build()
            panelCollider.center = Vector3.zero;

            // Add Selectable with interactable=false to prevent dwell behavior
            // VRGazeReticle.IsDwellable() checks: if (selectable != null && !selectable.interactable) return false;
            Selectable blockingSelectable = _popupObject.AddComponent<Selectable>();
            blockingSelectable.interactable = false;
            blockingSelectable.transition = Selectable.Transition.None;

            // Apply layer
            if (layer != -1) SetLayerRecursively(gameObject, layer);
        }

        /// <summary>
        /// Position popup in front of reference transform
        /// </summary>
        private void PositionInFrontOfReference()
        {
            if (_referenceTransform == null)
            {
                Debug.LogWarning("[RTTPopupMenu] No reference transform set");
                return;
            }

            // Get reference position and orientation
            Vector3 refPosition = _referenceTransform.position;
            Vector3 refForward = _referenceTransform.forward;
            Quaternion refRotation = _referenceTransform.rotation;

            // Position popup slightly in front of menu frame for raycast priority
            // sortingOrder = 100 ensures popup renders in front visually
            float zOffset = 0.02f; // 2cm in front so raycast hits popup before menu's blocking overlay
            Vector3 popupPosition = refPosition - refForward * zOffset;

            // Apply offset in local space (X/Y position on the menu)
            if (_positionOffset != Vector2.zero)
            {
                popupPosition += _referenceTransform.right * _positionOffset.x * WORLD_SPACE_SCALE;
                popupPosition += _referenceTransform.up * _positionOffset.y * WORLD_SPACE_SCALE;
            }

            transform.position = popupPosition;
            transform.rotation = refRotation;

            Debug.Log($"[RTTPopupMenu] Positioned at {popupPosition}, offset=({_positionOffset.x}, {_positionOffset.y}), ref={_referenceTransform.name}");
        }

        #endregion

        #region Private - External Overlays (World-Space Mode)

        private void CreateExternalOverlays()
        {
            // 1. Create world-space visual overlay (dark curved mesh)
            CreateWorldSpaceVisualOverlay();

            // 2. Create invisible blocking overlays on other RTT canvases
            CreateCanvasBlockingOverlays();
        }

        private void CreateWorldSpaceVisualOverlay()
        {
            Camera mainCam = _mainCamera ?? FindMainCamera();
            if (mainCam == null)
            {
                Debug.LogWarning("[RTTPopupMenu] Could not find main camera for world overlay");
                return;
            }

            Debug.Log($"[RTTPopupMenu] Creating world overlay using camera: {mainCam.name}");

            GameObject overlayObj = CreateWorldSpaceOverlay(mainCam);
            if (overlayObj != null)
            {
                _externalOverlays.Add(overlayObj);
            }
        }

        private void CreateCanvasBlockingOverlays()
        {
            GameObject virtualObjects = GameObject.Find("VirtualObjects");
            if (virtualObjects == null) return;

            RTTCanvasBase[] allFrames = virtualObjects.GetComponentsInChildren<RTTCanvasBase>(true);

            foreach (var frame in allFrames)
            {
                Canvas frameCanvas = frame.GetCanvas();
                if (frameCanvas == null) continue;

                // Skip if this popup is on this frame's canvas
                if (_isWorldSpaceMode)
                {
                    // In world-space mode, block all frames
                }
                else
                {
                    if (frameCanvas.transform == transform.parent) continue;
                }

                // Skip RTTMobileKeyboard
                if (frame is RTTMobileKeyboard) continue;

                GameObject overlay = CreateInvisibleCanvasOverlay(frameCanvas, frame);
                if (overlay != null)
                {
                    _externalOverlays.Add(overlay);
                }
            }
        }

        private GameObject CreateInvisibleCanvasOverlay(Canvas canvas, RTTCanvasBase frame)
        {
            int layer = LayerMask.NameToLayer(_config.layerName);

            GameObject overlayObj = new GameObject("PopupBlockingOverlay");
            overlayObj.transform.SetParent(canvas.transform, false);
            overlayObj.transform.SetAsLastSibling();

            if (layer != -1) overlayObj.layer = layer;

            RectTransform overlayRT = overlayObj.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            Image overlayImg = overlayObj.AddComponent<Image>();
            overlayImg.sprite = GetPixelSprite();
            overlayImg.raycastTarget = true;
            overlayImg.color = new Color(0f, 0f, 0f, 0f); // Invisible

            // Click overlay to close popup
            Button overlayBtn = overlayObj.AddComponent<Button>();
            overlayBtn.transition = Selectable.Transition.None;
            overlayBtn.onClick.AddListener(Hide);

            // BoxCollider for VR raycast
            Vector2 size = frame.GetWorldSize();
            BoxCollider col = overlayObj.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x * 1000f, size.y * 1000f, 0.1f);
            col.center = Vector3.zero;

            return overlayObj;
        }

        private GameObject CreateWorldSpaceOverlay(Camera camera)
        {
            GameObject overlayObj = new GameObject("WorldSpacePopupOverlay");

            MeshFilter meshFilter = overlayObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = overlayObj.AddComponent<MeshRenderer>();

            // Create flat quad mesh
            Mesh mesh = CreateFlatQuadMesh(20f, 20f); // Large flat quad
            meshFilter.mesh = mesh;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            Material mat = new Material(shader);
            mat.color = _config.overlayColor;
            // Render above most objects but below popup/inputable/keyboard canvases
            // Canvas sortingOrder handles UI layering separately
            mat.renderQueue = 3500;
            meshRenderer.material = mat;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            // Position flat quad in front of camera
            float distance = 2.5f;
            overlayObj.transform.position = camera.transform.position + camera.transform.forward * distance;
            overlayObj.transform.rotation = camera.transform.rotation;
            overlayObj.transform.SetParent(camera.transform);

            // Set to Ignore Raycast layer so it doesn't block reticle
            overlayObj.layer = LayerMask.NameToLayer("Ignore Raycast");

            // Ensure no collider exists that could block raycast
            Collider existingCol = overlayObj.GetComponent<Collider>();
            if (existingCol != null) Destroy(existingCol);

            Debug.Log($"[RTTPopupMenu] Flat world overlay created at distance {distance}");

            return overlayObj;
        }

        private Mesh CreateFlatQuadMesh(float width, float height)
        {
            Mesh mesh = new Mesh();

            float halfW = width / 2f;
            float halfH = height / 2f;

            Vector3[] vertices = new Vector3[4]
            {
                new Vector3(-halfW, -halfH, 0), // bottom-left
                new Vector3(halfW, -halfH, 0),  // bottom-right
                new Vector3(-halfW, halfH, 0),  // top-left
                new Vector3(halfW, halfH, 0)    // top-right
            };

            Vector2[] uvs = new Vector2[4]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0, 1),
                new Vector2(1, 1)
            };

            int[] triangles = new int[6]
            {
                0, 2, 1, // first triangle
                2, 3, 1  // second triangle
            };

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            return mesh;
        }

        private void DestroyExternalOverlays()
        {
            foreach (var overlay in _externalOverlays)
            {
                if (overlay != null)
                {
                    MeshRenderer renderer = overlay.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.material != null)
                    {
                        Destroy(renderer.material);
                    }
                    MeshFilter filter = overlay.GetComponent<MeshFilter>();
                    if (filter != null && filter.mesh != null)
                    {
                        Destroy(filter.mesh);
                    }
                    Destroy(overlay);
                }
            }
            _externalOverlays.Clear();
        }

        #endregion

        #region Private - Build Methods (Canvas Mode)

        private void BuildPopupContainer()
        {
            int layer = LayerMask.NameToLayer(_config.layerName);

            // Create invisible blocker
            CreateBlocker(layer);

            // Main popup container
            _popupObject = new GameObject("PopupContainer");
            _popupObject.transform.SetParent(transform, false);

            if (layer != -1) _popupObject.layer = layer;

            _popupRT = _popupObject.AddComponent<RectTransform>();
            _popupRT.anchorMin = new Vector2(0, 1);
            _popupRT.anchorMax = new Vector2(0, 1);
            _popupRT.pivot = new Vector2(0, 1);
            _popupRT.sizeDelta = new Vector2(_config.width, 100f);

            Canvas canvas = _popupObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 200;
            _popupObject.AddComponent<GraphicRaycaster>();

            CreateBackground();
            CreateGlowBorder();

            _contentContainer = new GameObject("Content");
            _contentContainer.transform.SetParent(_popupObject.transform, false);
            RectTransform contentRT = _contentContainer.AddComponent<RectTransform>();
            contentRT.anchorMin = Vector2.zero;
            contentRT.anchorMax = Vector2.one;
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;

            VerticalLayoutGroup vlg = _contentContainer.AddComponent<VerticalLayoutGroup>();
            float horizontalPadding = ((_config.rowSpacing * 0.75f) + kBorderInset) * 1.75f;
            float topPadding = (_config.rowSpacing + kBorderInset) * 2.5f;
            float bottomPadding = (_config.rowSpacing + kBorderInset) * 1.5f;  // Smaller bottom for visual balance
            vlg.padding = new RectOffset((int)horizontalPadding, (int)horizontalPadding, (int)topPadding, (int)bottomPadding);
            vlg.spacing = 0;  // Spacing handled manually via BuildSpacer for fine-grained control
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            _contentLayout = vlg;

            _popupObject.SetActive(false);
        }

        private void CreateBlocker(int layer)
        {
            _blockerObject = new GameObject("PopupBlocker");
            _blockerObject.transform.SetParent(transform, false);

            if (layer != -1) _blockerObject.layer = layer;

            RectTransform blockerRT = _blockerObject.AddComponent<RectTransform>();
            blockerRT.anchorMin = Vector2.zero;
            blockerRT.anchorMax = Vector2.one;
            blockerRT.offsetMin = Vector2.zero;
            blockerRT.offsetMax = Vector2.zero;

            Canvas blockerCanvas = _blockerObject.AddComponent<Canvas>();
            blockerCanvas.overrideSorting = true;
            blockerCanvas.sortingOrder = 199;
            _blockerObject.AddComponent<GraphicRaycaster>();

            GameObject blockerImgObj = new GameObject("BlockerImage");
            blockerImgObj.transform.SetParent(_blockerObject.transform, false);
            if (layer != -1) blockerImgObj.layer = layer;

            RectTransform imgRT = blockerImgObj.AddComponent<RectTransform>();
            imgRT.anchorMin = Vector2.zero;
            imgRT.anchorMax = Vector2.one;
            imgRT.offsetMin = Vector2.zero;
            imgRT.offsetMax = Vector2.zero;

            Image blockerImg = blockerImgObj.AddComponent<Image>();
            blockerImg.color = Color.clear;
            blockerImg.raycastTarget = true;

            Button blockerBtn = blockerImgObj.AddComponent<Button>();
            blockerBtn.transition = Selectable.Transition.None;
            blockerBtn.onClick.AddListener(Hide);

            _blockerObject.SetActive(false);
        }

        private void CreateBackground()
        {
            Image bgImg = _popupObject.AddComponent<Image>();
            bgImg.sprite = GetPixelSprite();

            Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (glassShader != null)
            {
                Material mat = new Material(glassShader);
                mat.SetFloat("_CornerRadius", UIConstants.PopupCornerRadius + 0.01f); // Slightly larger than border
                mat.SetFloat("_EdgePadding", UIConstants.PopupEdgePadding);
                mat.SetFloat("_Aspect", _config.width / 100f);

                mat.SetColor("_ColorA", UIConstants.PopupGlassColorA);
                mat.SetColor("_ColorB", UIConstants.PopupGlassColorB);
                mat.SetFloat("_GlassAlpha", _config.glassAlpha);
                mat.SetFloat("_GradientOffset", 0f);
                mat.SetFloat("_GradientAngle", -10f);
                mat.SetFloat("_CyanRatio", 0.7f);
                mat.SetFloat("_FresnelPower", 2.2f);
                mat.SetFloat("_FresnelStrength", 0.12f);

                bgImg.material = mat;
                bgImg.color = Color.white;
            }
            else
            {
                bgImg.color = _config.backgroundColor;
            }
        }

        private void CreateGlowBorder()
        {
            GameObject borderObj = new GameObject("GlowBorder");
            borderObj.transform.SetParent(_popupObject.transform, false);

            RectTransform borderRT = borderObj.AddComponent<RectTransform>();
            borderRT.anchorMin = Vector2.zero;
            borderRT.anchorMax = Vector2.one;
            borderRT.offsetMin = Vector2.zero;
            borderRT.offsetMax = Vector2.zero;

            Image borderImg = borderObj.AddComponent<Image>();
            borderImg.sprite = GetPixelSprite();
            borderImg.raycastTarget = false;

            Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
            if (glowShader != null)
            {
                Material mat = new Material(glowShader);
                mat.SetFloat("_StrokeEnabled", 0);
                mat.SetFloat("_BorderWidth", UIConstants.PopupBorderWidth);
                mat.SetFloat("_CornerRadius", UIConstants.PopupCornerRadius);
                mat.SetFloat("_EdgePadding", UIConstants.PopupEdgePadding);
                mat.SetFloat("_Aspect", _config.width / 100f);

                // Glow border layer widths from UIConstants
                mat.SetFloat("_Layer1Width", UIConstants.PopupGlowLayer1Width);
                mat.SetFloat("_Layer1Alpha", UIConstants.PopupGlowLayer1Alpha);
                mat.SetFloat("_Layer2Width", UIConstants.PopupGlowLayer2Width);
                mat.SetFloat("_Layer2Alpha", UIConstants.PopupGlowLayer2Alpha);
                mat.SetFloat("_Layer3Width", UIConstants.PopupGlowLayer3Width);
                mat.SetFloat("_Layer3Alpha", UIConstants.PopupGlowLayer3Alpha);
                mat.SetFloat("_Layer4Width", UIConstants.PopupGlowLayer4Width);
                mat.SetFloat("_Layer4Alpha", UIConstants.PopupGlowLayer4Alpha);

                mat.SetColor("_ColorA", UIConstants.PopupGlowColorA);
                mat.SetColor("_ColorB", UIConstants.PopupGlowColorB);
                mat.SetFloat("_GradientMode", 2f);
                mat.SetFloat("_GradientAngle", -10f);
                mat.SetFloat("_GlassAlpha", 0.02f);
                mat.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
                mat.SetFloat("_ShimmerSpeed", 0.4f);
                mat.SetFloat("_ShimmerIntensity", 0.2f);
                mat.SetFloat("_LightSize", 0.008f);
                mat.SetFloat("_LightGlow", 0.008f);

                borderImg.material = mat;
                _borderMaterial = mat;
            }
        }

        private void UpdateBackgroundAspect(float height)
        {
            Image bgImg = _popupObject.GetComponent<Image>();
            if (bgImg != null && bgImg.material != null)
            {
                bgImg.material.SetFloat("_Aspect", _config.width / height);
            }

            if (_borderMaterial != null)
            {
                _borderMaterial.SetFloat("_Aspect", _config.width / height);
            }

            // Update BoxCollider size for world-space mode
            if (_isWorldSpaceMode)
            {
                BoxCollider col = _popupObject.GetComponent<BoxCollider>();
                if (col != null)
                {
                    float borderPadding = 20f;
                    col.size = new Vector3(_config.width + borderPadding, height + borderPadding, 0.1f);
                }
            }
        }

        private void BuildSection(PopupSection section)
        {
            if (section.type == PopupSectionType.SectionBlock)
            {
                BuildSectionBlock(section);
            }
            else if (section.type == PopupSectionType.FullWidthButton)
            {
                if (section.buttons == null)
                {
                    BuildSeparator();
                }
                else
                {
                    BuildFullWidthButton(section.buttons[0]);
                }
            }
        }

        private void BuildSectionBlock(PopupSection section)
        {
            GameObject sectionObj = new GameObject("Section_" + section.title);
            sectionObj.transform.SetParent(_contentContainer.transform, false);
            SetLayerRecursively(sectionObj, _popupObject.layer);

            VerticalLayoutGroup sectionVLG = sectionObj.AddComponent<VerticalLayoutGroup>();
            sectionVLG.spacing = _config.rowSpacing;
            sectionVLG.childControlWidth = true;
            sectionVLG.childControlHeight = true;
            sectionVLG.childForceExpandWidth = true;
            sectionVLG.childForceExpandHeight = false;

            int buttonCount = section.buttons != null ? section.buttons.Count : 0;
            int rowCount = buttonCount > 0 ? Mathf.CeilToInt((float)buttonCount / section.columns) : 0;
            float gridHeight = rowCount > 0 ? (rowCount * _config.buttonHeight) + ((rowCount - 1) * _config.rowSpacing) : 0f;

            // Skip label height when title is empty
            bool hasTitle = !string.IsNullOrEmpty(section.title);
            float sectionHeight = hasTitle ? (_config.labelHeight + _config.rowSpacing + gridHeight) : gridHeight;

            LayoutElement sectionLE = sectionObj.AddComponent<LayoutElement>();
            sectionLE.preferredHeight = sectionHeight;

            // Only create label if title is not empty
            if (hasTitle)
            {
                CreateSectionLabel(sectionObj.transform, section.title, section.centerTitle);
            }

            GameObject gridObj = new GameObject("Grid_" + section.title);
            gridObj.transform.SetParent(sectionObj.transform, false);
            SetLayerRecursively(gridObj, _popupObject.layer);

            GridLayoutGroup glg = gridObj.AddComponent<GridLayoutGroup>();
            float cellWidth = CalculateCellWidth(section.columns);
            glg.cellSize = new Vector2(cellWidth, _config.buttonHeight);
            glg.spacing = new Vector2(_config.rowSpacing, _config.rowSpacing);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = section.columns;
            glg.childAlignment = TextAnchor.UpperCenter;

            LayoutElement gridLE = gridObj.AddComponent<LayoutElement>();
            gridLE.preferredHeight = gridHeight;

            if (section.buttons != null)
            {
                foreach (var btnData in section.buttons)
                {
                    CreateButton(gridObj.transform, btnData, cellWidth);
                }
            }
        }

        private void BuildFullWidthButton(ButtonData btnData)
        {
            float horizontalPadding = ((_config.rowSpacing * 0.75f) + kBorderInset) * 1.75f;
            float buttonWidth = _config.width - (horizontalPadding * 2);
            CreateButton(_contentContainer.transform, btnData, buttonWidth, true);
        }

        private void BuildSeparator()
        {
            GameObject sep = new GameObject("Separator");
            sep.transform.SetParent(_contentContainer.transform, false);
            SetLayerRecursively(sep, _popupObject.layer);

            Image sepImg = sep.AddComponent<Image>();
            sepImg.color = new Color(0.5f, 0.7f, 0.8f, 0.4f);

            LayoutElement sepLE = sep.AddComponent<LayoutElement>();
            sepLE.preferredHeight = 1f;
        }

        private void BuildTitleSpacer()
        {
            // Add spacer after title-only section
            // VLG spacing is 0, so spacer height = desired gap
            float spacerHeight = _config.labelHeight;
            if (spacerHeight <= 0) return;

            GameObject spacer = new GameObject("TitleSpacer");
            spacer.transform.SetParent(_contentContainer.transform, false);
            SetLayerRecursively(spacer, _popupObject.layer);

            LayoutElement spacerLE = spacer.AddComponent<LayoutElement>();
            spacerLE.preferredHeight = spacerHeight;
        }

        private void CreateSectionLabel(Transform parent, string text, bool centerTitle = false)
        {
            GameObject labelObj = new GameObject("Label_" + text);
            labelObj.transform.SetParent(parent, false);
            SetLayerRecursively(labelObj, _popupObject.layer);

            TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
            label.text = text;
            // Use button fontSize for labels (matches visual hierarchy better)
            label.fontSize = _config.fontSize;
            label.font = _config.font;
            label.color = Color.white;
            label.fontStyle = FontStyles.Bold;
            // Center or left align based on parameter
            label.alignment = centerTitle ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            // No extra left margin since VLG already has padding - align with buttons
            float leftMargin = 0f;
            float rightMargin = 0f;
            float topMargin = 0f;  // Controlled by VLG padding
            float bottomMargin = _config.rowSpacing * 2.0f;  // Space between label and buttons below
            label.margin = new Vector4(leftMargin, topMargin, rightMargin, bottomMargin);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;

            LayoutElement le = labelObj.AddComponent<LayoutElement>();
            le.preferredHeight = _config.labelHeight;
        }

        private void CreateButton(Transform parent, ButtonData data, float width, bool isFullWidth = false)
        {
            Color btnColor = data.color ?? (data.isSelected ? _config.accentColor : _config.primaryColor);

            // Use full ButtonConfig for better styling
            var btnConfig = new VRButtonFactory.ButtonConfig
            {
                label = data.text,
                icon = data.icon,
                themeColor = btnColor,
                width = width,
                height = _config.buttonHeight,
                fontSize = _config.fontSize,
                font = _config.font,
                textOnly = data.icon == null,
                horizontalLayout = data.icon != null,
                iconSize = _config.iconSize,
                // Better button styling
                borderWidth = _config.buttonBorderWidth,
                glowWidth = _config.buttonGlowWidth,
                glowIntensity = _config.buttonGlowIntensity,
                cornerRadius = _config.buttonCornerRadius,
                popAmount = 0.05f,
                layerName = _config.layerName
            };

            GameObject btn = VRButtonFactory.CreateButton(parent, btnConfig, data.onClick);

            if (data.isSelected)
            {
                var clickLock = btn.AddComponent<VRButtonClickLock>();
                clickLock.Lock();
            }

            LayoutElement le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.AddComponent<LayoutElement>();

            le.preferredWidth = width;
            le.preferredHeight = _config.buttonHeight;

            if (!isFullWidth)
            {
                le.flexibleWidth = 0;
                le.minWidth = width;
            }
            else
            {
                le.flexibleWidth = 1;
            }

            SetLayerRecursively(btn, _popupObject.layer);
        }

        #endregion

        #region Private - Calculation Methods

        private float CalculateTotalHeight()
        {
            // Must match VLG padding (asymmetric for visual balance)
            float topPadding = (_config.rowSpacing + kBorderInset) * 2.5f;
            float bottomPadding = (_config.rowSpacing + kBorderInset) * 1.5f;
            float height = topPadding + bottomPadding;

            for (int i = 0; i < _sections.Count; i++)
            {
                var section = _sections[i];

                if (section.type == PopupSectionType.SectionBlock)
                {
                    // Only add label height if title is not empty
                    if (!string.IsNullOrEmpty(section.title))
                    {
                        height += _config.labelHeight;
                        height += _config.rowSpacing;
                    }

                    int buttonCount = section.buttons != null ? section.buttons.Count : 0;
                    int rowCount = buttonCount > 0 ? Mathf.CeilToInt((float)buttonCount / section.columns) : 0;
                    float gridHeight = rowCount > 0 ? (rowCount * _config.buttonHeight) + ((rowCount - 1) * _config.rowSpacing) : 0f;
                    height += gridHeight;

                    // Add title spacer height for title-only sections
                    bool isTitleOnly = !string.IsNullOrEmpty(section.title) && buttonCount == 0;
                    if (isTitleOnly)
                    {
                        // VLG spacing is 0, so just add the spacer height
                        height += _config.labelHeight;
                    }
                }
                else if (section.type == PopupSectionType.FullWidthButton)
                {
                    if (section.buttons == null)
                    {
                        height += 1f;
                    }
                    else
                    {
                        height += _config.buttonHeight;
                    }
                }

                // Add spacing before next section (if any)
                if (i < _sections.Count - 1)
                {
                    var nextSection = _sections[i + 1];
                    // Use rowSpacing for FullWidthButton after SectionBlock (like Ascending button)
                    float spacing = GetSpacingBetweenSections(section.type, nextSection.type);
                    height += spacing;
                }
            }

            return height;
        }

        private float CalculateCellWidth(int columns)
        {
            float horizontalPadding = ((_config.rowSpacing * 0.75f) + kBorderInset) * 1.75f;
            float availableWidth = _config.width - (horizontalPadding * 2);
            float totalSpacing = (columns - 1) * _config.rowSpacing;
            return (availableWidth - totalSpacing) / columns;
        }

        #endregion

        #region Private - Utility Methods

        private void ClearContent()
        {
            if (_contentContainer == null) return;

            for (int i = _contentContainer.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(_contentContainer.transform.GetChild(i).gameObject);
            }
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static Sprite GetPixelSprite()
        {
            if (_pixelSprite != null) return _pixelSprite;

            Texture2D tex = new Texture2D(2, 2);
            tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
            tex.Apply();
            _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            return _pixelSprite;
        }

        #endregion

        #region Unity Lifecycle

        private void OnDestroy()
        {
            if (CurrentlyOpenPopup == this)
            {
                CurrentlyOpenPopup = null;
            }

            DestroyExternalOverlays();

            if (_popupObject != null)
            {
                Image bgImg = _popupObject.GetComponent<Image>();
                if (bgImg != null && bgImg.material != null)
                {
                    Destroy(bgImg.material);
                }
            }

            if (_borderMaterial != null)
            {
                Destroy(_borderMaterial);
            }
        }

        #endregion
    }

}
