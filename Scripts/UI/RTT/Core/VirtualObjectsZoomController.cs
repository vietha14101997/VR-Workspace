using UnityEngine;
using System.Collections.Generic;

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
    [SerializeField] private float maxDistance = 2.0f;

    [Tooltip("Default/initial distance from camera")]
    [SerializeField] private float defaultDistance = 1.8f;

    [Tooltip("Distance change per zoom step")]
    [SerializeField] private float zoomStep = 0.1f;

    [Header("References")]
    [Tooltip("Root transform of VirtualObjects (auto-detected if null)")]
    [SerializeField] private Transform virtualObjectsRoot;
    #endregion

    #region Private Fields
    private float _currentDistance;
    private RTTMenuFrame _primaryFrame;
    private List<RTTMenuFrame> _sidePanels = new List<RTTMenuFrame>();
    private RTTMiniFrame _taskbarFrame;
    private bool _initialized = false;
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
    /// Zoom in by one step (move closer to camera)
    /// </summary>
    public void ZoomIn()
    {
        SetZoomDistance(_currentDistance - zoomStep);
    }

    /// <summary>
    /// Zoom out by one step (move farther from camera)
    /// </summary>
    public void ZoomOut()
    {
        SetZoomDistance(_currentDistance + zoomStep);
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
    /// Register a side panel for sphere positioning updates on zoom
    /// </summary>
    public void RegisterSidePanel(RTTMenuFrame panel)
    {
        if (panel != null && !_sidePanels.Contains(panel))
        {
            _sidePanels.Add(panel);
            Debug.Log($"[VirtualObjectsZoomController] Registered side panel: {panel.name}");
        }
    }

    /// <summary>
    /// Unregister a side panel
    /// </summary>
    public void UnregisterSidePanel(RTTMenuFrame panel)
    {
        if (_sidePanels.Remove(panel))
        {
            Debug.Log($"[VirtualObjectsZoomController] Unregistered side panel: {panel?.name}");
        }
    }

    /// <summary>
    /// Clear all registered side panels
    /// </summary>
    public void ClearSidePanels()
    {
        _sidePanels.Clear();
    }
    #endregion

    #region Sphere Positioning
    /// <summary>
    /// Update all registered side panels to maintain sphere positioning after zoom.
    /// Side panels stay on sphere surface with radius = currentDistance, facing camera.
    /// </summary>
    private void UpdateSidePanelsOnSphere()
    {
        Camera cam = Camera.main;
        if (cam == null || _primaryFrame == null) return;

        Vector3 cameraPos = cam.transform.position;

        // Remove null entries
        _sidePanels.RemoveAll(p => p == null);

        foreach (var panel in _sidePanels)
        {
            if (panel == null || !panel.gameObject.activeInHierarchy)
            {
                continue;
            }

            // Update face-to-camera orientation
            UpdatePanelFaceToCamera(panel, cameraPos);
        }

        // Update taskbar face-to-camera (it handles its own positioning via follow target)
        if (_taskbarFrame != null)
        {
            // RTTMiniFrame already updates face-to-camera in LateUpdate
            // But we can trigger immediate update if needed for smoother zoom
        }
    }

    /// <summary>
    /// Update a panel's orientation to face the camera.
    /// Vector (Panel -> Camera) should be perpendicular to panel surface.
    /// </summary>
    private void UpdatePanelFaceToCamera(RTTMenuFrame panel, Vector3 cameraPos)
    {
        Vector3 panelPos = panel.transform.position;
        Vector3 toCamera = cameraPos - panelPos;

        if (toCamera.sqrMagnitude < 0.001f)
        {
            // Panel at camera position, use camera forward as fallback
            toCamera = -Camera.main.transform.forward;
        }

        // Face camera: panel's forward points away from camera
        panel.transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
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

    #region Recenter Support
    /// <summary>
    /// Called after recenter operation to recalculate current distance.
    /// </summary>
    public void OnRecenter()
    {
        if (_primaryFrame == null || Camera.main == null) return;

        // Recalculate distance after recenter
        _currentDistance = Vector3.Distance(
            Camera.main.transform.position,
            _primaryFrame.transform.position
        );

        _currentDistance = Mathf.Clamp(_currentDistance, minDistance, maxDistance);

        Debug.Log($"[VirtualObjectsZoomController] Recenter: distance recalculated to {_currentDistance:F2}m");
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
