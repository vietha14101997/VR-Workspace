using UnityEngine;
using VRWorkspace.Panel;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// RTTToolbar - Invisible container that manages sphere positioning for all taskbar-related components.
    ///
    /// Layout (3 rows, each separated by 0.05 × taskbar height):
    /// - Row 1 (top):    RTTFilePagination, RTTTaskbarExpansion (when RTTRemoteTaskbar is active)
    /// - Row 2 (center): RTTTaskbar / RTTRemoteTaskbar
    /// - Row 3 (bottom): RTTTaskbarExpansion (when RTTTaskbar is active - Eye expansion)
    ///
    /// Architecture:
    /// - RTTToolbar contains all taskbar-related components
    /// - Only RTTToolbar handles sphere positioning, child components use local positions
    /// - Height = 3.1 × taskbar height to accommodate all 3 rows
    ///
    /// Sphere Positioning Rules:
    /// 1. Top edge of toolbar touches plane at gap distance below target bottom
    /// 2. Toolbar face is perpendicular to vector(center → camera)
    /// </summary>
    public class RTTToolbar : MonoBehaviour
    {
        #region Singleton
        private static RTTToolbar _instance;
        public static RTTToolbar Instance => _instance;
        #endregion

        #region Configuration
        [Header("Size")]
        [SerializeField] private float heightMultiplier = 3.1f;

        [Header("Positioning")]
        [SerializeField] private float spacingMultiplier = 0.095f;
        #endregion

        #region Private Fields
        private Transform _followTarget;
        private float _toolbarHeight;
        private float _toolbarWidth;
        private float _taskbarHeight;
        private float _targetHeight;

        private RTTMiniFrame _activeTaskbarFrame;
        private WorldPanelClusterRig _activeClusterRig;
        private bool _initialized = false;
        #endregion

        #region Properties
        public float ToolbarHeight => _toolbarHeight;
        public float TaskbarHeight => _taskbarHeight;
        public Transform FollowTarget => _followTarget;
        public bool IsInitialized => _initialized;
        #endregion

        #region Lifecycle
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[RTTToolbar] Duplicate instance destroyed");
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void LateUpdate()
        {
            if (!_initialized || _followTarget == null) return;

            UpdateSpherePosition();
        }
        #endregion

        #region Public API
        /// <summary>
        /// Initialize the toolbar with the active taskbar.
        /// Call this when taskbar is created or switched.
        /// </summary>
        public void SetActiveTaskbar(RTTMiniFrame miniFrame)
        {
            if (miniFrame == null)
            {
                Debug.LogWarning("[RTTToolbar] SetActiveTaskbar called with null miniFrame");
                return;
            }

            _activeTaskbarFrame = miniFrame;
            _followTarget = miniFrame.GetFollowTarget();
            _activeClusterRig = null; // Clear cluster rig when switching taskbar

            // Calculate toolbar dimensions based on taskbar
            Vector2 taskbarSize = miniFrame.GetWorldSize();
            _taskbarHeight = taskbarSize.y;
            _toolbarWidth = taskbarSize.x;
            _toolbarHeight = _taskbarHeight * heightMultiplier;

            // Get target height for positioning calculations
            UpdateTargetHeight();

            _initialized = true;

            Debug.Log($"[RTTToolbar] Initialized with taskbar. Size: {_toolbarWidth:F3}x{_toolbarHeight:F3}m, followTarget: {_followTarget?.name}");
        }

        /// <summary>
        /// Update the follow target (called when taskbar's target changes).
        /// </summary>
        public void UpdateFollowTarget()
        {
            if (_activeTaskbarFrame != null)
            {
                _followTarget = _activeTaskbarFrame.GetFollowTarget();
                UpdateTargetHeight();
            }
        }

        /// <summary>
        /// Get local Y position for Row 1 (above taskbar).
        /// Used for RTTFilePagination and RTTTaskbarExpansion when RTTRemoteTaskbar is active.
        /// </summary>
        public float GetRow1LocalY()
        {
            // Row 1: Above taskbar with small gap (0.05 × taskbar height)
            return _taskbarHeight * 1.05f;
        }

        /// <summary>
        /// Get local Y position for Row 3 (below taskbar).
        /// Used for RTTTaskbarExpansion when RTTTaskbar is active.
        /// </summary>
        public float GetRow3LocalY()
        {
            // Row 3: Below taskbar with small gap (0.05 × taskbar height)
            return -_taskbarHeight * 1.05f;
        }

        /// <summary>
        /// Get local position for pagination component (always Row 1).
        /// </summary>
        public Vector3 GetPaginationLocalPosition()
        {
            return new Vector3(0, GetRow1LocalY(), 0);
        }

        /// <summary>
        /// Get local position for expansion component.
        /// Row 1 (above) when RTTRemoteTaskbar, Row 3 (below) when RTTTaskbar.
        /// </summary>
        public Vector3 GetExpansionLocalPosition()
        {
            // Check if active taskbar is RTTTaskbar (not RTTRemoteTaskbar)
            bool isRTTTaskbar = _activeTaskbarFrame != null &&
                _activeTaskbarFrame.GetComponent<RTTTaskbar>() != null;

            // RTTTaskbar uses Row 3 (below), RTTRemoteTaskbar uses Row 1 (above)
            float localY = isRTTTaskbar ? GetRow3LocalY() : GetRow1LocalY();
            return new Vector3(0, localY, 0);
        }

        /// <summary>
        /// Get local position for expansion component - explicitly above (Row 1).
        /// </summary>
        public Vector3 GetExpansionLocalPositionAbove()
        {
            return new Vector3(0, GetRow1LocalY(), 0);
        }

        /// <summary>
        /// Get local position for expansion component - explicitly below (Row 3).
        /// </summary>
        public Vector3 GetExpansionLocalPositionBelow()
        {
            return new Vector3(0, GetRow3LocalY(), 0);
        }

        /// <summary>
        /// Get local position for taskbar component (Row 2 - center).
        /// </summary>
        public Vector3 GetTaskbarLocalPosition()
        {
            return Vector3.zero;
        }

        /// <summary>
        /// Set the active cluster rig for dynamic target height calculation.
        /// When set, the toolbar uses the cluster rig's scaled bounds instead of follow target bounds.
        /// </summary>
        public void SetActiveClusterRig(WorldPanelClusterRig rig)
        {
            _activeClusterRig = rig;
            UpdateTargetHeight();
        }

        /// <summary>
        /// Check if the active taskbar is RTTTaskbar (not RTTRemoteTaskbar).
        /// </summary>
        public bool IsRTTTaskbarActive()
        {
            return _activeTaskbarFrame != null &&
                _activeTaskbarFrame.GetComponent<RTTTaskbar>() != null;
        }
        #endregion

        #region Private Methods
        private void UpdateTargetHeight()
        {
            if (_followTarget == null)
            {
                _targetHeight = 0.9f; // Default fallback
                return;
            }

            // Always use follow target height as base (NOT cluster rig).
            // Scale adjustment is handled separately in UpdateSpherePosition via delta.
            var targetCanvas = _followTarget.GetComponent<RTTCanvasBase>();
            if (targetCanvas != null)
            {
                _targetHeight = targetCanvas.GetWorldSize().y;
                return;
            }

            var targetMenu = _followTarget.GetComponent<RTTMenu>();
            if (targetMenu != null)
            {
                _targetHeight = targetMenu.GetWorldSize().y;
                return;
            }

            var clusterRig = _followTarget.GetComponent<WorldPanelClusterRig>();
            if (clusterRig != null)
            {
                Bounds bounds = clusterRig.GetFollowBounds();
                _targetHeight = bounds.size.y;
                return;
            }

            var renderer = _followTarget.GetComponent<Renderer>();
            if (renderer != null)
            {
                _targetHeight = renderer.bounds.size.y;
                return;
            }

            _targetHeight = 0.9f; // Fallback
        }

        private void UpdateSpherePosition()
        {
            Camera cam = Camera.main;
            if (cam == null || _followTarget == null) return;

            Vector3 cameraPos = cam.transform.position;
            Vector3 targetCenter = _followTarget.position;
            Vector3 targetUp = _followTarget.up;

            // Use base _targetHeight (from follow target / RTTMenu) plus scale adjustment.
            // At default scale (slider 0.5 → localScale 1.0): no adjustment → matches normal taskbar.
            // When scale changes: bottom edge moves → add delta so toolbar tracks it.
            float effectiveHeight = _targetHeight;
            if (_activeClusterRig != null)
            {
                float unscaledHeight = _activeClusterRig.GetFollowBounds().size.y;
                float currentScale = _activeClusterRig.transform.lossyScale.y;
                // Delta = how much the bottom edge moved from default scale position
                effectiveHeight += unscaledHeight * (currentScale - 1.0f);
            }

            float targetHalfHeight = effectiveHeight / 2f;
            float toolbarHalfHeight = _toolbarHeight / 2f;
            float gap = _taskbarHeight * spacingMultiplier;

            // Step 1: Calculate top edge position E
            Vector3 targetBottom = targetCenter - targetUp * targetHalfHeight;
            Vector3 E = targetBottom - Vector3.up * gap;

            // Step 2: Calculate vector from camera to E
            Vector3 toE = E - cameraPos;
            float distToE = toE.magnitude;

            float h = toolbarHalfHeight;

            // Edge case: camera too close
            if (distToE < 0.001f || distToE < h)
            {
                // Fallback: simple linear positioning
                transform.position = targetBottom - Vector3.up * (gap + h);
                Vector3 toCam = cameraPos - transform.position;
                if (toCam.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
                }
                return;
            }

            // Step 3: Calculate distance from camera to toolbar center
            float rSq = distToE * distToE - h * h;
            if (rSq < 0.0001f) rSq = 0.0001f;
            float r = Mathf.Sqrt(rSq);

            // Step 4: Calculate direction and angle adjustment
            float sinBeta = h / distToE;
            sinBeta = Mathf.Clamp(sinBeta, -1f, 1f);
            float beta = Mathf.Asin(sinBeta);

            // Step 5: Calculate toolbar center position
            Vector3 horizontalDir = new Vector3(toE.x, 0, toE.z);
            float horizontalDist = horizontalDir.magnitude;

            if (horizontalDist < 0.001f)
            {
                horizontalDir = _followTarget.forward;
                horizontalDir.y = 0;
                if (horizontalDir.sqrMagnitude < 0.001f) horizontalDir = Vector3.forward;
            }
            horizontalDir.Normalize();

            float currentAngle = Mathf.Atan2(toE.y, horizontalDist);
            float newAngle = currentAngle - beta;

            float newHorizontalDist = r * Mathf.Cos(newAngle);
            float newVerticalDist = r * Mathf.Sin(newAngle);

            transform.position = cameraPos + horizontalDir * newHorizontalDist + Vector3.up * newVerticalDist;

            // Step 6: Calculate rotation to face camera
            Vector3 toCamera = cameraPos - transform.position;
            if (toCamera.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
        }
        #endregion

        #region Static Factory
        /// <summary>
        /// Create RTTToolbar in VirtualObjects.
        /// </summary>
        public static RTTToolbar Create()
        {
            GameObject toolbarObj = new GameObject("RTTToolbar");

            // Parent to VirtualObjects
            GameObject virtualObjects = GameObject.Find("VirtualObjects");
            if (virtualObjects != null)
            {
                toolbarObj.transform.SetParent(virtualObjects.transform, false);
            }

            RTTToolbar toolbar = toolbarObj.AddComponent<RTTToolbar>();

            Debug.Log("[RTTToolbar] Created");
            return toolbar;
        }
        #endregion
    }

}
