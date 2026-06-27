using UnityEngine;
using System.Collections.Generic;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Controls zoom by moving VirtualObjects container toward/away from camera.
    /// Maintains sphere-based positioning for side objects and updates face-to-camera.
    ///
    /// Key principles:
    /// - Zoom direction = vector from Camera to Primary (NOT camera.forward)
    /// - This ensures zoom maintains relative positions regardless of camera orientation
    /// - Side panels stay on sphere surface with face-to-camera orientation
    /// </summary>
    public class VirtualObjectsZoomController : MonoBehaviour
    {
        #region Singleton
        private static VirtualObjectsZoomController _instance;
        public static VirtualObjectsZoomController Instance => _instance;
        #endregion

        #region Configuration
        [Header("Zoom Settings")]
        [Tooltip("Minimum distance from camera (closest zoom)")]
        [SerializeField] private float minDistance = 1.0f;

        [Tooltip("Maximum distance from camera (farthest zoom)")]
        [SerializeField] private float maxDistance = 2.5f;

        [Tooltip("Default/initial distance from camera")]
        [SerializeField] private float defaultDistance = 2.0f;

        [Tooltip("Distance change per zoom step")]
        [SerializeField] private float zoomStep = 0.1f;

        [Header("References")]
        [Tooltip("Root transform of VirtualObjects (auto-detected if null)")]
        [SerializeField] private Transform virtualObjectsRoot;
        #endregion

        #region Private Fields
        private float _currentDistance;
        private RTTMenuFrame _primaryFrame;
        private List<SidePanelInfo> _sidePanelInfos = new List<SidePanelInfo>();
        private RTTMiniFrame _taskbarFrame;
        private bool _initialized = false;

        // Zoom override for immersive video mode (FOV-based zoom)
        private System.Action _zoomInOverride;
        private System.Action _zoomOutOverride;

        /// <summary>
        /// Stores information needed to recalculate side panel position on zoom
        /// </summary>
        private class SidePanelInfo
        {
            public RTTMenuFrame frame;
            public Transform mainPanel;
            public int side; // -1 for left, +1 for right
            public float mainWidth;
            public float sideWidth;
            public float gap;
        }

        /// <summary>
        /// Stores information needed to recalculate bottom panel (taskbar) position on zoom.
        /// Bottom panels are positioned below their target with face-to-camera orientation.
        /// </summary>
        private class BottomPanelInfo
        {
            public RTTCanvasBase panel;       // The bottom panel (taskbar, pagination, expansion)
            public Transform target;          // The target panel to follow
            public float targetHeight;        // Height of target in meters
            public float panelHeight;         // Height of bottom panel in meters
            public float gap;                 // Gap between target bottom and panel top in meters
        }

        private List<BottomPanelInfo> _bottomPanelInfos = new List<BottomPanelInfo>();
        #endregion

        #region Properties
        /// <summary>Current distance from camera to primary frame</summary>
        public float CurrentDistance => _currentDistance;

        /// <summary>Minimum allowed zoom distance</summary>
        public float MinDistance => minDistance;

        /// <summary>Maximum allowed zoom distance</summary>
        public float MaxDistance => maxDistance;

        /// <summary>Default zoom distance</summary>
        public float DefaultDistance => defaultDistance;

        /// <summary>Whether the controller is initialized and ready</summary>
        public bool IsInitialized => _initialized;
        #endregion

        #region Lifecycle
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[VirtualObjectsZoomController] Duplicate instance destroyed");
                Destroy(this);
                return;
            }
            _instance = this;
            _currentDistance = defaultDistance;
        }

        private void Start()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initialize the zoom controller. Auto-finds references if not set.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            // Find VirtualObjects root if not assigned
            if (virtualObjectsRoot == null)
            {
                var vo = GameObject.Find("VirtualObjects");
                if (vo != null)
                {
                    virtualObjectsRoot = vo.transform;
                }
                else
                {
                    // Try parent transform (if attached to VirtualObjects)
                    virtualObjectsRoot = transform;
                }
            }

            // Find primary frame
            if (_primaryFrame == null)
            {
                _primaryFrame = RTTMenuFrame.PrimaryInstance;
            }

            // Find taskbar
            if (_taskbarFrame == null)
            {
                var taskbar = RTTTaskbar.Instance;
                if (taskbar != null)
                {
                    _taskbarFrame = taskbar.GetComponent<RTTMiniFrame>();
                }
            }

            // Move VirtualObjects to default distance on initialization
            if (_primaryFrame != null && Camera.main != null && virtualObjectsRoot != null)
            {
                // Calculate current distance
                float currentDist = Vector3.Distance(
                    Camera.main.transform.position,
                    _primaryFrame.transform.position
                );

                _currentDistance = currentDist;
                _initialized = true;

                // If current distance is different from default, move to default
                if (!Mathf.Approximately(currentDist, defaultDistance))
                {
                    Debug.Log($"[VirtualObjectsZoomController] Moving VirtualObjects from {currentDist:F2}m to default {defaultDistance:F2}m");
                    SetZoomDistance(defaultDistance);
                }
                else
                {
                    Debug.Log($"[VirtualObjectsZoomController] Initialized. Already at default distance: {_currentDistance:F2}m");
                }
            }
            else
            {
                _currentDistance = defaultDistance;
                _initialized = true;
                Debug.Log($"[VirtualObjectsZoomController] Initialized (no primary frame yet). Default distance: {_currentDistance:F2}m");
            }
        }

        /// <summary>
        /// Configure zoom settings. Call this before Initialize() for custom values.
        /// </summary>
        public void Configure(float min, float max, float defaultDist, float step = 0.1f)
        {
            minDistance = Mathf.Max(0.1f, min);
            maxDistance = Mathf.Max(minDistance + 0.1f, max);
            defaultDistance = Mathf.Clamp(defaultDist, minDistance, maxDistance);
            zoomStep = Mathf.Max(0.01f, step);

            Debug.Log($"[VirtualObjectsZoomController] Configured: min={minDistance}m, max={maxDistance}m, default={defaultDistance}m, step={zoomStep}m");
        }

        /// <summary>
        /// Set the primary frame reference (called by RTTBootstrapper or RTTManager)
        /// </summary>
        public void SetPrimaryFrame(RTTMenuFrame frame)
        {
            _primaryFrame = frame;

            // Recalculate current distance
            if (_primaryFrame != null && Camera.main != null)
            {
                _currentDistance = Vector3.Distance(
                    Camera.main.transform.position,
                    _primaryFrame.transform.position
                );
                _currentDistance = Mathf.Clamp(_currentDistance, minDistance, maxDistance);
            }
        }

        /// <summary>
        /// Set the taskbar frame reference
        /// </summary>
        public void SetTaskbarFrame(RTTMiniFrame frame)
        {
            _taskbarFrame = frame;
        }
        #endregion

        #region Public API - Zoom Control
        /// <summary>
        /// Set zoom distance. Moves VirtualObjects container along Camera->Primary vector.
        /// </summary>
        /// <param name="newDistance">Target distance in meters (clamped to min/max)</param>
        public void SetZoomDistance(float newDistance)
        {
            newDistance = Mathf.Clamp(newDistance, minDistance, maxDistance);

            if (Mathf.Approximately(_currentDistance, newDistance))
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[VirtualObjectsZoomController] No main camera found");
                return;
            }

            if (_primaryFrame == null)
            {
                _primaryFrame = RTTMenuFrame.PrimaryInstance;
                if (_primaryFrame == null)
                {
                    Debug.LogWarning("[VirtualObjectsZoomController] No primary frame found");
                    return;
                }
            }

            if (virtualObjectsRoot == null)
            {
                Debug.LogWarning("[VirtualObjectsZoomController] No VirtualObjects root found");
                return;
            }

            Vector3 cameraPos = cam.transform.position;
            Vector3 primaryPos = _primaryFrame.transform.position;

            // CRITICAL: Direction from Camera to Primary, NOT camera.forward
            // This ensures zoom maintains relative positions regardless of camera orientation
            Vector3 toPrimary = primaryPos - cameraPos;
            float currentDist = toPrimary.magnitude;

            if (currentDist < 0.001f)
            {
                // Camera too close to primary, use camera forward as fallback
                toPrimary = cam.transform.forward;
                currentDist = _currentDistance;
            }

            Vector3 direction = toPrimary.normalized;

            // Calculate delta distance to move
            float deltaDist = newDistance - currentDist;

            // Move entire VirtualObjects container along this direction
            virtualObjectsRoot.position += direction * deltaDist;

            _currentDistance = newDistance;

            // Update side panels to maintain sphere positioning and face-to-camera
            UpdateSidePanelsOnSphere();

            Debug.Log($"[VirtualObjectsZoomController] Zoom set to {newDistance:F2}m (delta: {deltaDist:F3}m)");
        }

        /// <summary>
        /// Zoom in by one step (move closer to camera).
        /// If a zoom override is set (e.g. immersive video FOV zoom), delegates to it.
        /// </summary>
        public void ZoomIn()
        {
            if (_zoomInOverride != null) { _zoomInOverride(); return; }
            SetZoomDistance(_currentDistance - zoomStep);
        }

        /// <summary>
        /// Zoom out by one step (move farther from camera).
        /// If a zoom override is set (e.g. immersive video FOV zoom), delegates to it.
        /// </summary>
        public void ZoomOut()
        {
            if (_zoomOutOverride != null) { _zoomOutOverride(); return; }
            SetZoomDistance(_currentDistance + zoomStep);
        }

        /// <summary>
        /// Set zoom override callbacks. When set, ZoomIn/ZoomOut delegate to these
        /// instead of moving VirtualObjects. Used by immersive video for FOV-based zoom.
        /// </summary>
        public void SetZoomOverride(System.Action zoomIn, System.Action zoomOut)
        {
            _zoomInOverride = zoomIn;
            _zoomOutOverride = zoomOut;
        }

        /// <summary>
        /// Clear zoom override, restoring normal VirtualObjects movement zoom.
        /// </summary>
        public void ClearZoomOverride()
        {
            _zoomInOverride = null;
            _zoomOutOverride = null;
        }

        /// <summary>
        /// Reset zoom to default distance
        /// </summary>
        public void ResetZoom()
        {
            SetZoomDistance(defaultDistance);
        }

        /// <summary>
        /// Get normalized zoom value (0 = closest, 1 = farthest)
        /// </summary>
        public float GetNormalizedZoom()
        {
            return Mathf.InverseLerp(minDistance, maxDistance, _currentDistance);
        }

        /// <summary>
        /// Set zoom using normalized value (0 = closest, 1 = farthest)
        /// </summary>
        public void SetNormalizedZoom(float normalized01)
        {
            float distance = Mathf.Lerp(minDistance, maxDistance, Mathf.Clamp01(normalized01));
            SetZoomDistance(distance);
        }
        #endregion

        #region Side Panel Registration
        /// <summary>
        /// Register a side panel for sphere positioning updates on zoom.
        /// Stores information needed to recalculate position when zoom changes.
        /// </summary>
        /// <param name="panel">The side panel frame</param>
        /// <param name="mainPanel">The main panel this side panel is attached to</param>
        /// <param name="side">-1 for left, +1 for right</param>
        /// <param name="mainWidth">Width of the main panel in meters</param>
        /// <param name="sideWidth">Width of the side panel in meters</param>
        /// <param name="gap">Gap between main panel and side panel in meters</param>
        public void RegisterSidePanel(RTTMenuFrame panel, Transform mainPanel, int side, float mainWidth, float sideWidth, float gap)
        {
            if (panel == null) return;

            // Check if already registered
            foreach (var info in _sidePanelInfos)
            {
                if (info.frame == panel) return;
            }

            var newInfo = new SidePanelInfo
            {
                frame = panel,
                mainPanel = mainPanel,
                side = side,
                mainWidth = mainWidth,
                sideWidth = sideWidth,
                gap = gap
            };

            _sidePanelInfos.Add(newInfo);
            Debug.Log($"[VirtualObjectsZoomController] Registered side panel: {panel.name} (side={side}, mainW={mainWidth}, sideW={sideWidth}, gap={gap})");
        }

        /// <summary>
        /// Legacy register method - only updates rotation, not position
        /// </summary>
        public void RegisterSidePanel(RTTMenuFrame panel)
        {
            if (panel == null) return;

            // Check if already registered
            foreach (var info in _sidePanelInfos)
            {
                if (info.frame == panel) return;
            }

            // Register with minimal info (rotation-only updates)
            var newInfo = new SidePanelInfo
            {
                frame = panel,
                mainPanel = null,
                side = 0,
                mainWidth = 0,
                sideWidth = 0,
                gap = 0
            };

            _sidePanelInfos.Add(newInfo);
            Debug.Log($"[VirtualObjectsZoomController] Registered side panel (rotation-only): {panel.name}");
        }

        /// <summary>
        /// Unregister a side panel
        /// </summary>
        public void UnregisterSidePanel(RTTMenuFrame panel)
        {
            int removed = _sidePanelInfos.RemoveAll(info => info.frame == panel);
            if (removed > 0)
            {
                Debug.Log($"[VirtualObjectsZoomController] Unregistered side panel: {panel?.name}");
            }
        }

        /// <summary>
        /// Clear all registered side panels
        /// </summary>
        public void ClearSidePanels()
        {
            _sidePanelInfos.Clear();
        }
        #endregion

        #region Bottom Panel Registration
        /// <summary>
        /// Register a bottom panel (taskbar, pagination, expansion) for sphere positioning updates on zoom.
        /// Bottom panels are positioned below their target with top edge at gap distance from target bottom.
        /// </summary>
        /// <param name="panel">The bottom panel</param>
        /// <param name="target">The target transform to follow</param>
        /// <param name="targetHeight">Height of the target in meters</param>
        /// <param name="panelHeight">Height of the bottom panel in meters</param>
        /// <param name="gap">Gap between target bottom and panel top in meters</param>
        public void RegisterBottomPanel(RTTCanvasBase panel, Transform target, float targetHeight, float panelHeight, float gap)
        {
            if (panel == null) return;

            // Check if already registered - update if so
            foreach (var info in _bottomPanelInfos)
            {
                if (info.panel == panel)
                {
                    // Update existing registration
                    info.target = target;
                    info.targetHeight = targetHeight;
                    info.panelHeight = panelHeight;
                    info.gap = gap;
                    return;
                }
            }

            var newInfo = new BottomPanelInfo
            {
                panel = panel,
                target = target,
                targetHeight = targetHeight,
                panelHeight = panelHeight,
                gap = gap
            };

            _bottomPanelInfos.Add(newInfo);
            Debug.Log($"[VirtualObjectsZoomController] Registered bottom panel: {panel.name} (targetH={targetHeight:F3}, panelH={panelHeight:F3}, gap={gap:F3})");
        }

        /// <summary>
        /// Unregister a bottom panel
        /// </summary>
        public void UnregisterBottomPanel(RTTCanvasBase panel)
        {
            int removed = _bottomPanelInfos.RemoveAll(info => info.panel == panel);
            if (removed > 0)
            {
                Debug.Log($"[VirtualObjectsZoomController] Unregistered bottom panel: {panel?.name}");
            }
        }

        /// <summary>
        /// Clear all registered bottom panels
        /// </summary>
        public void ClearBottomPanels()
        {
            _bottomPanelInfos.Clear();
        }

        /// <summary>
        /// Calculate sphere position for a bottom panel.
        /// Ensures:
        /// 1. Top edge of panel is at gap distance below target bottom
        /// 2. Panel face is perpendicular to vector(panel center → camera)
        ///
        /// Mathematical approach:
        /// - Calculate top edge position E (target bottom - gap in world down direction)
        /// - Find panel center P such that P is on sphere surface and panel faces camera
        /// - Uses vertical plane geometry similar to horizontal side panel positioning
        /// </summary>
        /// <param name="target">Target transform to position below</param>
        /// <param name="targetHalfHeight">Half height of target in meters</param>
        /// <param name="panelHalfHeight">Half height of panel in meters</param>
        /// <param name="gap">Gap between target bottom and panel top in meters</param>
        /// <param name="position">Output: calculated world position for panel center</param>
        /// <param name="rotation">Output: calculated rotation to face camera</param>
        public void CalculateBottomPanelSpherePosition(
            Transform target,
            float targetHalfHeight,
            float panelHalfHeight,
            float gap,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            Camera cam = Camera.main;
            if (cam == null || target == null) return;

            Vector3 cameraPos = cam.transform.position;
            Vector3 targetCenter = target.position;
            Vector3 targetUp = target.up;

            // Step 1: Calculate top edge position E
            // Top edge should be at: target bottom - gap (in world down direction toward camera)
            Vector3 targetBottom = targetCenter - targetUp * targetHalfHeight;
            Vector3 E = targetBottom - Vector3.up * gap;

            // Step 2: Calculate vector from camera to E
            Vector3 toE = E - cameraPos;
            float distToE = toE.magnitude;

            float h = panelHalfHeight;

            // Edge case: camera too close to top edge
            if (distToE < 0.001f || distToE < h)
            {
                // Fallback: simple linear positioning below target
                position = targetBottom - Vector3.up * (gap + h);
                Vector3 toCam = cameraPos - position;
                if (toCam.sqrMagnitude > 0.001f)
                {
                    rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
                }
                else
                {
                    rotation = target.rotation;
                }
                return;
            }

            // Step 3: Calculate distance from camera to panel center
            // r² = |E-C|² - h²
            float rSq = distToE * distToE - h * h;
            if (rSq < 0.0001f) rSq = 0.0001f;
            float r = Mathf.Sqrt(rSq);

            // Step 4: Calculate direction and angle adjustment
            Vector3 dirToE = toE.normalized;

            // Calculate adjustment angle β = arcsin(h / distToE)
            float sinBeta = h / distToE;
            sinBeta = Mathf.Clamp(sinBeta, -1f, 1f);
            float beta = Mathf.Asin(sinBeta);

            // Step 5: Calculate panel center position
            // Work in vertical plane containing camera and E
            // Horizontal direction from camera to E
            Vector3 horizontalDir = new Vector3(toE.x, 0, toE.z);
            float horizontalDist = horizontalDir.magnitude;

            if (horizontalDist < 0.001f)
            {
                // Camera directly above/below E - use target's forward as reference
                horizontalDir = target.forward;
                horizontalDir.y = 0;
                if (horizontalDir.sqrMagnitude < 0.001f) horizontalDir = Vector3.forward;
            }
            horizontalDir.Normalize();

            // Current elevation angle from camera to E
            float currentAngle = Mathf.Atan2(toE.y, horizontalDist);

            // New angle: rotate downward by beta (panel center is below top edge)
            float newAngle = currentAngle - beta;

            // Calculate new horizontal and vertical distances
            float newHorizontalDist = r * Mathf.Cos(newAngle);
            float newVerticalDist = r * Mathf.Sin(newAngle);

            position = cameraPos + horizontalDir * newHorizontalDist + Vector3.up * newVerticalDist;

            // Step 6: Calculate rotation to face camera
            Vector3 toCamera = cameraPos - position;
            if (toCamera.sqrMagnitude > 0.001f)
            {
                rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
            else
            {
                rotation = target.rotation;
            }
        }
        #endregion

        #region Sphere Positioning
        /// <summary>
        /// Update all registered side panels to maintain sphere positioning after zoom.
        /// Recalculates position so inner edge stays on main panel's plane.
        /// </summary>
        private void UpdateSidePanelsOnSphere()
        {
            Camera cam = Camera.main;
            if (cam == null || _primaryFrame == null) return;

            Vector3 cameraPos = cam.transform.position;

            // Remove null entries
            _sidePanelInfos.RemoveAll(info => info.frame == null);

            foreach (var info in _sidePanelInfos)
            {
                if (info.frame == null || !info.frame.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // If we have full positioning info, recalculate position
                if (info.mainPanel != null && info.side != 0)
                {
                    RecalculateSidePanelPosition(info, cameraPos);
                }
                else
                {
                    // Legacy: only update rotation
                    UpdatePanelFaceToCamera(info.frame, cameraPos);
                }
            }

            // Update taskbar face-to-camera (it handles its own positioning via follow target)
            if (_taskbarFrame != null)
            {
                // RTTMiniFrame already updates face-to-camera in LateUpdate
            }

            // Update bottom panels (taskbars, pagination, expansion)
            UpdateBottomPanelsOnSphere();
        }

        /// <summary>
        /// Update all registered bottom panels to maintain sphere positioning after zoom.
        /// Recalculates position so top edge stays at correct distance below target.
        /// </summary>
        private void UpdateBottomPanelsOnSphere()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // Remove null entries
            _bottomPanelInfos.RemoveAll(info => info.panel == null);

            foreach (var info in _bottomPanelInfos)
            {
                if (info.panel == null || !info.panel.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (info.target == null)
                {
                    continue;
                }

                CalculateBottomPanelSpherePosition(
                    info.target,
                    info.targetHeight / 2f,
                    info.panelHeight / 2f,
                    info.gap,
                    out Vector3 newPos,
                    out Quaternion newRot
                );

                info.panel.transform.position = newPos;
                info.panel.transform.rotation = newRot;
            }
        }

        /// <summary>
        /// Recalculate side panel position so:
        /// 1. Inner edge lies exactly on main panel's plane
        /// 2. Panel surface is perpendicular to vector (center -> camera)
        ///
        /// Mathematical solution:
        /// Given inner edge E and camera C, find panel center P such that:
        /// - P is at distance w (half-width) from E along panel's right direction
        /// - Panel faces camera from P
        ///
        /// Solution uses trigonometry:
        /// - r = sqrt(|E-C|² - w²) = distance from camera to panel center
        /// - θ = atan2(Ez-Cz, Ex-Cx) - arcsin(w*side / |E-C|)
        /// - P = C + (cos(θ), 0, sin(θ)) * r
        /// </summary>
        private void RecalculateSidePanelPosition(SidePanelInfo info, Vector3 cameraPos)
        {
            if (info.mainPanel == null || info.frame == null) return;

            // Step 1: Calculate inner edge position on main panel's plane
            Vector3 mainRight = info.mainPanel.right;
            float innerEdgeOffset = (info.mainWidth / 2f) + info.gap;
            Vector3 innerEdgePos = info.mainPanel.position + mainRight * innerEdgeOffset * info.side;

            // Step 2: Calculate in horizontal plane (XZ)
            float w = info.sideWidth / 2f; // half width
            int s = info.side; // ±1

            // Vector from camera to inner edge (horizontal only)
            float a = innerEdgePos.x - cameraPos.x;
            float b = innerEdgePos.z - cameraPos.z;
            float distSq = a * a + b * b;
            float dist = Mathf.Sqrt(distSq);

            // Edge case: camera too close to inner edge
            if (dist < 0.001f)
            {
                // Fallback: use main panel's right direction
                Vector3 fallbackPos = innerEdgePos + mainRight * w * s;
                fallbackPos.y = info.mainPanel.position.y;
                info.frame.transform.position = fallbackPos;
                info.frame.transform.rotation = Quaternion.LookRotation(-mainRight * s, Vector3.up);
                return;
            }

            // Step 3: Calculate distance from camera to panel center
            // r² = |E-C|² - w²
            float rSq = distSq - w * w;
            if (rSq < 0.0001f) rSq = 0.0001f; // Clamp to avoid negative sqrt
            float r = Mathf.Sqrt(rSq);

            // Step 4: Calculate direction angle θ
            // θ = atan2(b, a) - arcsin(w * side / dist)
            // Note: SUBTRACT because inner edge is on the INNER side of panel
            float alpha = Mathf.Atan2(b, a);
            float sinArg = (w * s) / dist;
            sinArg = Mathf.Clamp(sinArg, -1f, 1f); // Ensure valid range for arcsin
            float theta = alpha - Mathf.Asin(sinArg);

            // Step 5: Calculate panel center position
            float dx = Mathf.Cos(theta);
            float dz = Mathf.Sin(theta);
            Vector3 panelPos = new Vector3(
                cameraPos.x + dx * r,
                info.mainPanel.position.y, // Keep same Y as main panel
                cameraPos.z + dz * r
            );

            // Step 6: Calculate rotation to face camera from center
            Vector3 toCamera = new Vector3(cameraPos.x - panelPos.x, 0, cameraPos.z - panelPos.z);
            Quaternion panelRotation;
            if (toCamera.sqrMagnitude > 0.001f)
            {
                panelRotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
            else
            {
                panelRotation = Quaternion.LookRotation(-mainRight * s, Vector3.up);
            }

            // Apply position and rotation
            info.frame.transform.position = panelPos;
            info.frame.transform.rotation = panelRotation;
        }

        /// <summary>
        /// Update a panel's orientation to face the camera.
        /// Vector (Panel -> Camera) should be perpendicular to panel surface.
        /// </summary>
        private void UpdatePanelFaceToCamera(RTTMenuFrame panel, Vector3 cameraPos)
        {
            Vector3 panelPos = panel.transform.position;
            Vector3 toCameraHorizontal = cameraPos - panelPos;
            toCameraHorizontal.y = 0; // Project to horizontal for upright panel

            if (toCameraHorizontal.sqrMagnitude < 0.001f)
            {
                toCameraHorizontal = -Camera.main.transform.forward;
                toCameraHorizontal.y = 0;
            }

            // Face camera: panel's forward points away from camera
            panel.transform.rotation = Quaternion.LookRotation(-toCameraHorizontal.normalized, Vector3.up);
        }

        /// <summary>
        /// Calculate position on sphere for a side panel.
        /// Used when creating new side panels after zoom has changed.
        /// </summary>
        /// <param name="arcAngle">Horizontal angle from primary forward (degrees)</param>
        /// <param name="primaryHeight">Y position to maintain</param>
        /// <returns>World position on sphere surface</returns>
        public Vector3 CalculateSpherePosition(float arcAngle, float primaryHeight)
        {
            Camera cam = Camera.main;
            if (cam == null || _primaryFrame == null)
            {
                return Vector3.zero;
            }

            Vector3 cameraPos = cam.transform.position;

            // Get primary's forward direction (yaw only)
            Vector3 primaryForward = _primaryFrame.transform.forward;
            primaryForward.y = 0;
            if (primaryForward.sqrMagnitude < 0.001f)
            {
                primaryForward = Vector3.forward;
            }
            primaryForward.Normalize();

            // Rotate around Y axis by arcAngle
            Quaternion horizontalRotation = Quaternion.AngleAxis(arcAngle, Vector3.up);
            Vector3 direction = horizontalRotation * (-primaryForward);

            // Position on sphere surface
            Vector3 position = cameraPos + direction * _currentDistance;
            position.y = primaryHeight;

            return position;
        }

        /// <summary>
        /// Calculate rotation to face camera from a given position.
        /// </summary>
        public Quaternion CalculateFaceCameraRotation(Vector3 position)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return Quaternion.identity;
            }

            Vector3 toCamera = cam.transform.position - position;
            if (toCamera.sqrMagnitude < 0.001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        }
        #endregion

        #region Editor
    #if UNITY_EDITOR
        private void OnValidate()
        {
            // Ensure valid ranges
            minDistance = Mathf.Max(0.1f, minDistance);
            maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
            defaultDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
            zoomStep = Mathf.Max(0.01f, zoomStep);
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 cameraPos = cam.transform.position;

            // Draw min distance sphere
            Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
            Gizmos.DrawWireSphere(cameraPos, minDistance);

            // Draw max distance sphere
            Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
            Gizmos.DrawWireSphere(cameraPos, maxDistance);

            // Draw current distance sphere
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireSphere(cameraPos, _currentDistance);
        }
    #endif
        #endregion
    }

}
