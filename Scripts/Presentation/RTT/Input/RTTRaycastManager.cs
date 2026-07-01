using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.UI.RTT.Input
{
    /// <summary>
    /// Manages raycast from VR gaze/controller into RTT panels.
    /// Converts world-space raycast hits to canvas-space UI events.
    ///
    /// Usage:
    /// 1. Call Raycast(ray) each frame to update hit state
    /// 2. Use IsHoveringUI() to check if hovering a UI element
    /// 3. Call SendClick() when user triggers click
    /// 4. Call SendScroll() for scroll input
    /// </summary>
    public class RTTRaycastManager : MonoBehaviour
    {
        #region Singleton
        private static RTTRaycastManager _instance;
        private static bool _applicationQuitting = false;

        public static RTTRaycastManager Instance
        {
            get
            {
                if (_applicationQuitting)
                    return null;

                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<RTTRaycastManager>();

                    if (_instance == null)
                    {
                        var go = new GameObject("RTTRaycastManager");
                        _instance = go.AddComponent<RTTRaycastManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Reset static singleton state at the start of each Play session.
        /// Without this, _applicationQuitting stays true after the previous session's
        /// OnApplicationQuit, causing Instance to return null on the next Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            _applicationQuitting = false;
        }
        #endregion

        #region Configuration
        [Header("Raycast Settings")]
        [Tooltip("Maximum distance for raycasts")]
        [SerializeField] private float maxRaycastDistance = 10f;

        [Tooltip("Layer mask for raycastable RTT quads (auto-initializes to VirtualObjects if -1)")]
        [SerializeField] private LayerMask raycastLayerMask = -1;

        [Tooltip("Whether to hit trigger colliders")]
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

        [Header("Debug")]
        [Tooltip("Show debug rays in Scene view")]
        [SerializeField] private bool showDebugRays = false;

        [Tooltip("Log hit information to console")]
        [SerializeField] private bool logHitInfo = false;
        #endregion

        #region State
        // Panel data cache for performance
        private Dictionary<RTTCanvasBase, RTTPanelRaycastData> _panelDataCache
            = new Dictionary<RTTCanvasBase, RTTPanelRaycastData>();

        // Current and previous hit results
        private RTTHitResult _currentHit;
        private RTTHitResult _previousHit;

        // Hover tracking
        private GameObject _currentHoveredObject;
        private GameObject _previousHoveredObject;

        // Reusable event data (avoid GC)
        private PointerEventData _pointerEventData;
        private List<RaycastResult> _raycastResults = new List<RaycastResult>();
        #endregion

        #region Properties
        /// <summary>Current hit result</summary>
        public RTTHitResult CurrentHit => _currentHit;

        /// <summary>Is currently hovering a UI element</summary>
        public bool IsHoveringUI => _currentHit.isValid && _currentHit.hitUIElement != null;

        /// <summary>Currently hovered GameObject</summary>
        public GameObject HoveredObject => _currentHoveredObject;
        #endregion

        #region Lifecycle
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Move to root if not already (DontDestroyOnLoad only works for root GameObjects)
            if (transform.parent != null)
                transform.SetParent(null);

            DontDestroyOnLoad(gameObject);

            // Initialize pointer event data
            if (EventSystem.current != null)
            {
                _pointerEventData = new PointerEventData(EventSystem.current);
            }
        }

        private void Start()
        {
            // Ensure pointer event data is created
            if (_pointerEventData == null && EventSystem.current != null)
            {
                _pointerEventData = new PointerEventData(EventSystem.current);
            }

            // Auto-initialize layer mask if not set
            if (raycastLayerMask == -1)
            {
                int virtualObjectsLayer = LayerMask.NameToLayer("VirtualObjects");
                if (virtualObjectsLayer != -1)
                {
                    raycastLayerMask = 1 << virtualObjectsLayer;
                    Debug.Log($"[RTTRaycastManager] Auto-initialized layer mask to VirtualObjects (layer {virtualObjectsLayer})");
                }
                else
                {
                    Debug.LogWarning("[RTTRaycastManager] VirtualObjects layer not found, using all layers");
                }
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }
        #endregion

        #region Public API
        /// <summary>
        /// Perform raycast from a ray into all RTT panels.
        /// Call this each frame to update hover state.
        /// </summary>
        public RTTHitResult Raycast(Ray ray)
        {
            // Ensure pointer event data exists
            if (_pointerEventData == null)
            {
                if (EventSystem.current != null)
                    _pointerEventData = new PointerEventData(EventSystem.current);
                else
                    return new RTTHitResult();
            }

            _previousHit = _currentHit;
            _currentHit = new RTTHitResult();

            // Step 1: Physics raycast to find DisplayQuad (explicitly hit triggers)
            if (!Physics.Raycast(ray, out RaycastHit physicsHit, maxRaycastDistance, raycastLayerMask, triggerInteraction))
            {
                HandleNoHit();

                if (showDebugRays)
                    Debug.DrawRay(ray.origin, ray.direction * maxRaycastDistance, Color.red);

                return _currentHit;
            }

            // Step 2: Find RTTCanvasBase from collider
            var panel = FindRTTPanel(physicsHit.collider);
            if (panel == null)
            {
                HandleNoHit();
                return _currentHit;
            }

            // Step 3: Get or create cached panel data
            var panelData = GetOrCreatePanelData(panel);
            if (panelData.graphicRaycaster == null)
            {
                HandleNoHit();
                return _currentHit;
            }

            // Step 4: Calculate UV from hit point (textureCoord doesn't work with BoxCollider)
            Vector2 uv = CalculateUVFromHitPoint(physicsHit.point, panelData);
            Vector2 screenPos = UVToScreenPosition(uv, panelData);

            if (logHitInfo)
            {
                Debug.Log($"[RTTRaycast] Panel: {panel.name}, WorldHit: {physicsHit.point}, UV: ({uv.x:F3}, {uv.y:F3}), Screen: ({screenPos.x:F0}, {screenPos.y:F0})");
            }

            // Step 5: Perform UI raycast on main canvas
            _raycastResults.Clear();
            _pointerEventData.position = screenPos;
            panelData.graphicRaycaster.Raycast(_pointerEventData, _raycastResults);

            // Step 5b: Also raycast against nested GraphicRaycasters (for dropdown panels, etc.)
            // Nested Canvases with overrideSorting=true need their own GraphicRaycaster
            RaycastNestedCanvases(panelData.graphicRaycaster.transform, screenPos);

            // Sort results by depth (higher sorting order = closer to camera, then higher depth = children first)
            if (_raycastResults.Count > 1)
            {
                _raycastResults.Sort((a, b) =>
                {
                    int cmp = b.sortingOrder.CompareTo(a.sortingOrder);
                    return cmp != 0 ? cmp : b.depth.CompareTo(a.depth);
                });
            }

            // Step 6: Build hit result
            if (_raycastResults.Count > 0)
            {
                _currentHit = new RTTHitResult
                {
                    isValid = true,
                    panel = panel,
                    worldHitPoint = physicsHit.point,
                    worldHitNormal = physicsHit.normal,
                    uvCoordinate = uv, // Use calculated UV, not textureCoord
                    screenPosition = screenPos,
                    hitUIElement = _raycastResults[0].gameObject,
                    raycastResult = _raycastResults[0],
                    distance = physicsHit.distance
                };

                if (logHitInfo)
                {
                    Debug.Log($"[RTTRaycast] UI Hit: {_currentHit.hitUIElement.name}");
                }
            }
            else
            {
                // Hit panel but no UI element
                _currentHit = new RTTHitResult
                {
                    isValid = true,
                    panel = panel,
                    worldHitPoint = physicsHit.point,
                    worldHitNormal = physicsHit.normal,
                    uvCoordinate = uv, // Use calculated UV
                    screenPosition = screenPos,
                    hitUIElement = null,
                    distance = physicsHit.distance
                };

                if (logHitInfo)
                {
                    Debug.Log($"[RTTRaycast] Panel hit but no UI element at screen pos ({screenPos.x:F0}, {screenPos.y:F0})");
                }
            }

            // Step 7: Handle hover state changes
            HandleHoverStateChanges();

            if (showDebugRays)
            {
                Debug.DrawLine(ray.origin, physicsHit.point, Color.green);
                Debug.DrawRay(physicsHit.point, physicsHit.normal * 0.1f, Color.blue);
            }

            return _currentHit;
        }

        /// <summary>
        /// Raycast against nested GraphicRaycasters in the canvas hierarchy.
        /// This handles nested Canvases with overrideSorting (like dropdown panels).
        /// </summary>
        private void RaycastNestedCanvases(Transform root, Vector2 screenPos)
        {
            // Find all GraphicRaycasters in children (excluding the root one we already used)
            var nestedRaycasters = root.GetComponentsInChildren<GraphicRaycaster>(false);

            foreach (var raycaster in nestedRaycasters)
            {
                // Skip the root raycaster (already processed)
                if (raycaster.transform == root) continue;

                // Check if the raycaster's canvas is active and has overrideSorting
                var canvas = raycaster.GetComponent<Canvas>();
                if (canvas == null || !canvas.gameObject.activeInHierarchy) continue;

                // Raycast against this nested raycaster
                raycaster.Raycast(_pointerEventData, _raycastResults);
            }
        }

        /// <summary>
        /// Send a click event to the currently hovered UI element.
        /// Returns true if a clickable element was found and clicked.
        /// </summary>
        public bool SendClick()
        {
            if (!_currentHit.isValid || _currentHit.hitUIElement == null)
                return false;

            var target = _currentHit.hitUIElement;

            // Try VRInputFieldTrigger first (for VR keyboard support)
            // This needs to be checked before Button because InputField clicks should open keyboard
            var inputFieldTrigger = target.GetComponent<VRInputFieldTrigger>();
            if (inputFieldTrigger == null)
                inputFieldTrigger = target.GetComponentInParent<VRInputFieldTrigger>();

            if (inputFieldTrigger != null && inputFieldTrigger.InputField != null && inputFieldTrigger.InputField.interactable)
            {
                // Execute click on the trigger to show keyboard
                ExecuteEvents.Execute(inputFieldTrigger.gameObject, _pointerEventData, ExecuteEvents.pointerClickHandler);
                _currentHit.panel?.MarkDirty();

                if (logHitInfo)
                    Debug.Log($"[RTTRaycast] Clicked InputField: {inputFieldTrigger.name}");

                return true;
            }

            // Try Button
            var button = target.GetComponentInParent<Button>();
            if (button != null && button.interactable)
            {
                string buttonName = button.name; // Cache name before click handler might destroy it

                // Execute click through EventSystem
                ExecuteEvents.Execute(button.gameObject, _pointerEventData, ExecuteEvents.pointerClickHandler);

                // Button might be destroyed by click handler, check before accessing
                if (button != null)
                {
                    // Trigger VRButtonRipple if exists
                    var ripple = button.GetComponentInChildren<VRButtonRipple>();
                    if (ripple != null)
                    {
                        Vector2 normalizedPos = new Vector2(
                            _currentHit.uvCoordinate.x,
                            _currentHit.uvCoordinate.y
                        );
                        ripple.TriggerRipple(normalizedPos);
                    }
                }

                // Mark panel dirty for re-render
                _currentHit.panel?.MarkDirty();

                if (logHitInfo)
                    Debug.Log($"[RTTRaycast] Clicked: {buttonName}");

                return true;
            }

            // Try other clickable elements (IPointerClickHandler)
            var clickHandlers = target.GetComponentsInParent<IPointerClickHandler>();
            if (clickHandlers != null && clickHandlers.Length > 0)
            {
                foreach (var handler in clickHandlers)
                {
                    var mb = handler as MonoBehaviour;
                    if (mb != null && mb.enabled)
                    {
                        ExecuteEvents.Execute(mb.gameObject, _pointerEventData, ExecuteEvents.pointerClickHandler);
                        _currentHit.panel?.MarkDirty();
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Send pointer down event
        /// </summary>
        public void SendPointerDown()
        {
            if (!_currentHit.isValid || _currentHit.hitUIElement == null)
                return;

            ExecuteEvents.Execute(_currentHit.hitUIElement, _pointerEventData, ExecuteEvents.pointerDownHandler);
            _currentHit.panel?.MarkDirty();
        }

        /// <summary>
        /// Send pointer up event
        /// </summary>
        public void SendPointerUp()
        {
            if (!_currentHit.isValid || _currentHit.hitUIElement == null)
                return;

            ExecuteEvents.Execute(_currentHit.hitUIElement, _pointerEventData, ExecuteEvents.pointerUpHandler);
            _currentHit.panel?.MarkDirty();
        }

        /// <summary>
        /// Send scroll event to the currently hovered element
        /// </summary>
        public void SendScroll(Vector2 scrollDelta)
        {
            if (!_currentHit.isValid || _currentHit.hitUIElement == null)
                return;

            _pointerEventData.scrollDelta = scrollDelta;

            // Find scroll handler
            var scrollHandler = _currentHit.hitUIElement.GetComponentInParent<IScrollHandler>();
            if (scrollHandler != null)
            {
                var mb = scrollHandler as MonoBehaviour;
                if (mb != null)
                {
                    ExecuteEvents.Execute(mb.gameObject, _pointerEventData, ExecuteEvents.scrollHandler);
                    _currentHit.panel?.MarkDirty();
                }
            }
        }

        #region VCS Extension (Virtual Cursor Space)
        /// <summary>
        /// Set the cursor's hovered UI element from a UV coordinate on a given panel.
        /// Mirrors the post-Raycast() state-update path so the existing click dispatch logic works.
        /// </summary>
        public bool SetHoverFromUV(RTTCanvasBase panel, Vector2 uv)
        {
            if (!TryBuildHitFromUV(panel, uv, out var panelData, out var hit, out var uvCoord))
                return false;

            var previousHover = _currentHoveredObject;
            _currentHit = new RTTHitResult
            {
                isValid = hit != null,
                hitUIElement = hit,
                panel = panel,
                uvCoordinate = uvCoord
            };
            _currentHoveredObject = hit;
            if (previousHover != hit)
            {
                if (previousHover != null) SendPointerExit(previousHover);
                if (hit != null) SendPointerEnter(hit);
            }
            panel.MarkDirty();
            return hit != null;
        }

        /// <summary>
        /// Send a click event to the UI element at the given UV on a specific panel.
        /// Used by VCS drivers (mouse/gamepad) to dispatch clicks without going through gaze raycast.
        /// </summary>
        public bool SendClickAtUV(RTTCanvasBase panel, Vector2 uv)
        {
            if (!SetHoverFromUV(panel, uv))
            {
                Debug.Log($"[SendClickAtUV] SetHoverFromUV returned false for '{panel.name}' UV=({uv.x:F3},{uv.y:F3}) — no GraphicRaycaster hit");
                return false;
            }
            // Diagnostic: dump element rect + raycast index for full debug.
            var rt0 = _raycastResults[0];
            Debug.Log($"[SendClickAtUV] panel='{panel.name}' UV=({uv.x:F3},{uv.y:F3}) hitElement='{_currentHit.hitUIElement?.name ?? "null"}' depth={rt0.depth} sortingOrder={rt0.sortingOrder} canvasScreenPos={rt0.screenPosition} worldPos={rt0.worldPosition}");
            bool sent = SendClick();
            return sent;
        }

        /// <summary>Inverse of CalculateUVFromHitPoint: rebuild cached raycast state from (panel, UV).</summary>
        private bool TryBuildHitFromUV(
            RTTCanvasBase panel,
            Vector2 uv,
            out RTTPanelRaycastData panelData,
            out GameObject hitUIElement,
            out Vector2 uvCoordinate)
        {
            hitUIElement = null;
            uvCoordinate = Vector2.zero;
            panelData = null;

            if (panel == null) return false;
            panelData = GetOrCreatePanelData(panel);
            if (panelData == null || panelData.graphicRaycaster == null || panelData.renderTexture == null)
                return false;

            Vector2 screenPos = UVToScreenPosition(uv, panelData);
            _pointerEventData.position = screenPos;
            // NOTE: PointerEventData.pressEventCamera and enterEventCamera are read-only
            // in Unity's EventSystems; we cannot assign them. GraphicRaycaster falls back
            // through: pressEventCamera → enterEventCamera → canvas.worldCamera.
            // RTTCanvasBase.SetupCanvas sets canvas.worldCamera = _uiCamera, so the
            // fallback resolves correctly without us needing to set anything.
            var cv = panelData.graphicRaycaster.GetComponent<Canvas>();
            var canvasRect = cv != null ? cv.GetComponent<RectTransform>().rect.ToString() : "null";
            Debug.Log($"[TryBuildHitFromUV] panel='{panel.name}' uv=({uv.x:F3},{uv.y:F3}) rt=({panelData.renderTexture.width}x{panelData.renderTexture.height}) screenPos=({screenPos.x:F0},{screenPos.y:F0}) canvasRect={canvasRect}");

            _raycastResults.Clear();
            panelData.graphicRaycaster.Raycast(_pointerEventData, _raycastResults);
            // Also raycast against nested canvases (dropdowns, etc.) — same as gaze Raycast does.
            RaycastNestedCanvases(panelData.graphicRaycaster.transform, screenPos);

            if (_raycastResults.Count == 0) return false;
            // Sort by sortingOrder desc, then depth desc — match gaze Raycast ordering.
            if (_raycastResults.Count > 1)
            {
                _raycastResults.Sort((a, b) =>
                {
                    int cmp = b.sortingOrder.CompareTo(a.sortingOrder);
                    return cmp != 0 ? cmp : b.depth.CompareTo(a.depth);
                });
            }

            var first = _raycastResults[0];
            hitUIElement = first.gameObject;
            uvCoordinate = uv;
            return hitUIElement != null;
        }
        #endregion

        /// <summary>
        /// Get the current hit result
        /// </summary>
        public RTTHitResult GetCurrentHit()
        {
            return _currentHit;
        }

        /// <summary>
        /// Clear the panel data cache
        /// </summary>
        public void ClearCache()
        {
            _panelDataCache.Clear();
        }

        /// <summary>
        /// Force clear current hover state (useful when closing panels)
        /// </summary>
        public void ClearHoverState()
        {
            if (_currentHoveredObject != null)
            {
                SendPointerExit(_currentHoveredObject);
                _currentHoveredObject = null;
            }
            _currentHit = new RTTHitResult();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Find RTTCanvasBase from a collider
        /// </summary>
        private RTTCanvasBase FindRTTPanel(Collider collider)
        {
            // Check cache first for performance
            foreach (var kvp in _panelDataCache)
            {
                if (kvp.Value.quadCollider == collider)
                    return kvp.Key;
            }

            // Search in hierarchy
            return collider.GetComponentInParent<RTTCanvasBase>();
        }

        /// <summary>
        /// Get or create cached panel data
        /// </summary>
        private RTTPanelRaycastData GetOrCreatePanelData(RTTCanvasBase panel)
        {
            if (_panelDataCache.TryGetValue(panel, out var data))
            {
                // Validate cached data still valid
                if (data.IsValid())
                    return data;

                // Invalid, recreate
                _panelDataCache.Remove(panel);
            }

            // Create new cache entry
            data = new RTTPanelRaycastData
            {
                panel = panel,
                renderTexture = panel.GetRenderTexture(),
                graphicRaycaster = panel.GetGraphicRaycaster(),
                quadCollider = panel.GetQuadCollider()
            };

            _panelDataCache[panel] = data;

            // Subscribe to cleanup when panel destroyed
            panel.OnRTTDestroyed += () =>
            {
                if (_panelDataCache.ContainsKey(panel))
                    _panelDataCache.Remove(panel);
            };

            return data;
        }

        /// <summary>
        /// Calculate UV coordinates from world-space hit point on a panel
        /// Uses the collider's transform for accurate calculation
        /// </summary>
        private Vector2 CalculateUVFromHitPoint(Vector3 worldHitPoint, RTTPanelRaycastData panelData)
        {
            // Use collider's transform (which is the display quad's transform)
            // The quad mesh is a unit quad (-0.5 to +0.5) scaled by worldSize
            // The collider is sized (1, 1, 0.01f) in the quad's local space

            if (panelData.quadCollider == null)
                return new Vector2(0.5f, 0.5f);

            // Convert hit point to collider's local space
            // In local space, the collider spans from -0.5 to +0.5 in X and Y
            Vector3 localHitPoint = panelData.quadCollider.transform.InverseTransformPoint(worldHitPoint);

            // Convert local position to UV (0-1 range)
            // Local X: -0.5 to +0.5 -> UV X: 0 to 1
            // Local Y: -0.5 to +0.5 -> UV Y: 0 to 1
            float u = localHitPoint.x + 0.5f;
            float v = localHitPoint.y + 0.5f;

            // Clamp to valid range
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            return new Vector2(u, v);
        }

        /// <summary>
        /// Convert UV coordinates to screen position for GraphicRaycaster
        /// UV origin is bottom-left, Unity screen coordinates also have (0,0) at bottom-left
        /// </summary>
        private Vector2 UVToScreenPosition(Vector2 uv, RTTPanelRaycastData panelData)
        {
            var rt = panelData.renderTexture;
            if (rt == null)
                return Vector2.zero;

            // UV (0,0) = bottom-left, Screen (0,0) = bottom-left in Unity
            // Direct mapping - no Y flip needed
            float screenX = uv.x * rt.width;
            float screenY = uv.y * rt.height;

            return new Vector2(screenX, screenY);
        }

        /// <summary>
        /// Handle hover state changes between frames
        /// </summary>
        private void HandleHoverStateChanges()
        {
            _previousHoveredObject = _currentHoveredObject;
            _currentHoveredObject = _currentHit.hitUIElement;

            // Object changed
            if (_currentHoveredObject != _previousHoveredObject)
            {
                // Exit previous
                if (_previousHoveredObject != null)
                {
                    SendPointerExit(_previousHoveredObject);
                    _previousHit.panel?.MarkDirty();
                }

                // Enter current
                if (_currentHoveredObject != null)
                {
                    SendPointerEnter(_currentHoveredObject);
                    _currentHit.panel?.MarkDirty();
                }
            }
        }

        /// <summary>
        /// Send pointer enter event to a GameObject
        /// </summary>
        private void SendPointerEnter(GameObject target)
        {
            if (target == null) return;

            // Standard Unity pointer enter event - try target first, then parents
            var enterHandler = target.GetComponent<IPointerEnterHandler>();
            if (enterHandler == null)
                enterHandler = target.GetComponentInParent<IPointerEnterHandler>();

            if (enterHandler != null)
            {
                var mb = enterHandler as MonoBehaviour;
                if (mb != null)
                    ExecuteEvents.Execute(mb.gameObject, _pointerEventData, ExecuteEvents.pointerEnterHandler);
            }

            // Unified HoverEffectController - handles all hover effects
            var hoverController = target.GetComponent<HoverEffectController>();
            if (hoverController == null)
                hoverController = target.GetComponentInParent<HoverEffectController>();
            hoverController?.OnPointerEnter(null);
        }

        /// <summary>
        /// Send pointer exit event to a GameObject
        /// </summary>
        private void SendPointerExit(GameObject target)
        {
            if (target == null) return;

            // Standard Unity pointer exit event - try target first, then parents
            var exitHandler = target.GetComponent<IPointerExitHandler>();
            if (exitHandler == null)
                exitHandler = target.GetComponentInParent<IPointerExitHandler>();

            if (exitHandler != null)
            {
                var mb = exitHandler as MonoBehaviour;
                if (mb != null)
                    ExecuteEvents.Execute(mb.gameObject, _pointerEventData, ExecuteEvents.pointerExitHandler);
            }

            // Unified HoverEffectController - handles all hover effects
            var hoverController = target.GetComponent<HoverEffectController>();
            if (hoverController == null)
                hoverController = target.GetComponentInParent<HoverEffectController>();
            hoverController?.OnPointerExit(null);
        }

        /// <summary>
        /// Handle no hit case
        /// </summary>
        private void HandleNoHit()
        {
            // Exit previous hover if any
            if (_currentHoveredObject != null)
            {
                SendPointerExit(_currentHoveredObject);
                _previousHit.panel?.MarkDirty();
            }

            _currentHoveredObject = null;
        }
        #endregion
    }

    #region Data Structures
    /// <summary>
    /// Result of an RTT raycast
    /// </summary>
    [System.Serializable]
    public struct RTTHitResult
    {
        /// <summary>Is the hit valid (did we hit an RTT panel)</summary>
        public bool isValid;

        /// <summary>The RTT panel that was hit</summary>
        public RTTCanvasBase panel;

        /// <summary>World-space hit point on the display quad</summary>
        public Vector3 worldHitPoint;

        /// <summary>World-space normal at hit point</summary>
        public Vector3 worldHitNormal;

        /// <summary>UV coordinate on the display quad (0-1)</summary>
        public Vector2 uvCoordinate;

        /// <summary>Screen position in the render texture</summary>
        public Vector2 screenPosition;

        /// <summary>The UI element that was hit (null if hit panel but no element)</summary>
        public GameObject hitUIElement;

        /// <summary>The full raycast result from GraphicRaycaster</summary>
        [System.NonSerialized]
        public RaycastResult raycastResult;

        /// <summary>Distance from ray origin to hit point</summary>
        public float distance;
    }

    /// <summary>
    /// Cached data for RTT panel raycasting
    /// </summary>
    public class RTTPanelRaycastData
    {
        public RTTCanvasBase panel;
        public RenderTexture renderTexture;
        public GraphicRaycaster graphicRaycaster;
        public BoxCollider quadCollider;

        /// <summary>Check if cached data is still valid</summary>
        public bool IsValid()
        {
            return panel != null &&
                   renderTexture != null &&
                   graphicRaycaster != null &&
                   quadCollider != null;
        }
    }
    #endregion

}
