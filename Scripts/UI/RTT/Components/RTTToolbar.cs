using UnityEngine;

/// <summary>
/// RTTToolbar - Invisible container that manages sphere positioning for all taskbar-related components.
///
/// Architecture:
/// - RTTToolbar contains RTTTaskbar/RTTRemoteTaskbar (center) and RTTFilePagination/RTTTaskbarExpansion (top)
/// - Only RTTToolbar handles sphere positioning, child components use local positions
/// - Height = 3.1 × taskbar height to accommodate pagination/expansion above
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
    /// Get local Y position for child components.
    /// </summary>
    /// <param name="aboveTaskbar">True for pagination/expansion (above), false for taskbar (center)</param>
    public float GetChildLocalY(bool aboveTaskbar)
    {
        if (!aboveTaskbar) return 0f;

        // Position above taskbar with small gap
        return _taskbarHeight * 1.05f;
    }

    /// <summary>
    /// Get local position for pagination component.
    /// </summary>
    public Vector3 GetPaginationLocalPosition()
    {
        return new Vector3(0, GetChildLocalY(true), 0);
    }

    /// <summary>
    /// Get local position for expansion component.
    /// </summary>
    public Vector3 GetExpansionLocalPosition()
    {
        return new Vector3(0, GetChildLocalY(true), 0);
    }

    /// <summary>
    /// Get local position for taskbar component (center).
    /// </summary>
    public Vector3 GetTaskbarLocalPosition()
    {
        return Vector3.zero;
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

        // Try to get target height from various components
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

        float targetHalfHeight = _targetHeight / 2f;
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
