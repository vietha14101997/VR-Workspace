using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Layout mode for panel positioning in cluster
/// </summary>
public enum ClusterLayoutMode
{
    /// <summary>
    /// Dynamic: panels spread evenly across the arc based on count
    /// </summary>
    Dynamic,

    /// <summary>
    /// FixedThreeSlot: panels are placed in fixed positions as if always 3-panel system
    /// - 1 panel: center slot only
    /// - 2 panels: center + right slots (left empty)
    /// - 3 panels: all three slots filled
    /// Maximum 3 panels in this mode
    /// </summary>
    FixedThreeSlot
}

/// <summary>
/// Manages a cluster of WorldPanelPlus panels arranged in an arc.
/// Handles dynamic panel creation, positioning, and neighbor linking.
/// Uses ClusterVisualCurved or ClusterVisualFlatPlanar for seamless visual appearance.
/// </summary>
[ExecuteAlways]
public class WorldPanelClusterRig : MonoBehaviour
{
    [Header("Source")]
    public WorldPanelPlus panelPrefab;

    [Header("Layout")]
    [Tooltip("Dynamic: panels spread evenly. FixedThreeSlot: panels use fixed 3-slot positions (max 3 panels)")]
    public ClusterLayoutMode layoutMode = ClusterLayoutMode.FixedThreeSlot;
    [Tooltip("When true, panels are positioned relative to parent origin instead of camera. Used when parented inside RTTMenu.")]
    public bool useParentOrigin = false;
    [Tooltip("Extra gap in meters between panel edges (0 = edges touch)")]
    [Range(0f, 0.1f)] public float edgeGapMeters = 0f;
    [Tooltip("Whether panels should face directly toward camera (true) or have limited tilt (false)")]
    public bool panelsFaceCamera = true;
    public float verticalOffset = 0f;
    public bool faceCameraYawOnly = true;
    [Tooltip("Overlap amount (in meters) between adjacent panels to eliminate seams")]
    [Range(0f, 0.01f)] public float panelOverlap = 0.0015f;

    [Header("Cluster Visuals")]
    [Tooltip("Enable seamless glass background and glowing border across all panels")]
    public bool enableClusterVisuals = true;
    [Tooltip("Use curved mesh visual (true) or per-panel visual (false). Curved provides true seamless appearance.")]
    public bool useCurvedVisual = false;

    private ClusterVisualCurved _curvedVisual;
    private ClusterVisualFlatPlanar _flatPlanarVisual;

    [Header("Visual Settings")]
    [SerializeField] private float cornerRadius = 0.04f;
    [SerializeField] private float edgePadding = 0.009f;
    [SerializeField] private float borderWidth = 0.025f; // Matches RTTMenuFrame
    [SerializeField] private float lightSize = 0.01f;    // Matches RTTMenuFrame
    [Tooltip("Extra size (in meters) for glow overflow on outer edges")]
    [SerializeField] private float glowExpansion = 0.1f;
    [Tooltip("Size of the solid visual frame extending beyond content (in meters)")]
    [SerializeField] private float frameMargin = 0.12f; // ~15% extension (Increased from 0.06f)
    [Tooltip("Size (in meters) the Visual mesh expands beyond Board bounds. Use 0 for same size as Board.")]
    [Range(0f, 0.3f)]
    [SerializeField] private float visualExpansion = 0.075f;
    [Tooltip("Content margin ratio (content is inset by this fraction)")]
    [SerializeField] private float contentMarginHorizontal = 0.04f;
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorA = new Color(0.3f, 1f, 1f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorB = new Color(1f, 0.4f, 1f, 1f);
    [SerializeField] private Color glassColorA = new Color(0f, 0.55f, 0.65f, 0.35f);
    [SerializeField] private Color glassColorB = new Color(0.30f, 0.12f, 0.50f, 0.32f);

    [Header("Dynamic Panels")]
    [SerializeField] private List<WorldPanelPlus> _panels = new List<WorldPanelPlus>();
    public List<WorldPanelPlus> panels => _panels;

    // Track enabled/disabled state per panel (true = enabled, false = disabled)
    private List<bool> _panelEnabledStates = new List<bool>();

    // Event fired when panel enabled state changes
    public event System.Action<int, bool> OnPanelEnabledChanged;

    Camera Cam => Application.isPlaying ? Camera.main : FindObjectOfType<Camera>();

    /// <summary>
    /// Get arc radius for panel positioning.
    /// The user wants R to match distance, capped at 1.8m (1800R).
    /// </summary>
    public float ArcRadius
    {
        get
        {
            var zoomController = VirtualObjectsZoomController.Instance;
            if (zoomController != null && zoomController.IsInitialized)
            {
                return Mathf.Min(zoomController.CurrentDistance, 1.8f);
            }
            return 1.8f; // Default fallback
        }
    }

    /// <summary>
    /// Gets the actual distance to the viewer.
    /// Used for cluster positioning, while ArcRadius is used for curvature.
    /// </summary>
    private float ViewerDistance
    {
        get
        {
            var zoomController = VirtualObjectsZoomController.Instance;
            if (zoomController != null && zoomController.IsInitialized)
            {
                return zoomController.CurrentDistance;
            }
            return 1.8f;
        }
    }

    /// <summary>
    /// Returns true if using curved visual that requires arc-based cursor mapping.
    /// FlatPlanar mode does NOT require this - each panel is flat, only joints are folded.
    /// </summary>
    public bool IsUsingUnifiedVisual
    {
        get
        {
            // Only CurvedSurround mode requires arc-based cursor mapping
            // FlatPlanar mode has flat panels, cursor should use board.TransformPoint
            return useCurvedVisual && _curvedVisual != null;
        }
    }

    /// <summary>
    /// Returns true if using FlatPlanar visual mode.
    /// In this mode, panels are flat quads positioned on arc with yaw rotation,
    /// but the board transforms are positioned linearly (z=0, no rotation).
    /// Cursor needs special handling to match mesh positions.
    /// </summary>
    public bool IsUsingFlatPlanarVisual
    {
        get
        {
            return !useCurvedVisual && _flatPlanarVisual != null;
        }
    }

    /// <summary>
    /// Get the content mesh local position from active visual (curved or flat planar).
    /// This ensures cursor is drawn on the same plane as the rendered content.
    /// </summary>
    public Vector3 GetUnifiedContentLocalPosition()
    {
        if (useCurvedVisual && _curvedVisual != null)
            return _curvedVisual.ContentLocalPosition;
        if (!useCurvedVisual && _flatPlanarVisual != null)
            return _flatPlanarVisual.ContentLocalPosition;
        return Vector3.zero;
    }

    /// <summary>
    /// Get the content mesh local position from curved visual (for cursor synchronization).
    /// This ensures cursor is drawn on the same plane as the rendered content.
    /// </summary>
    [System.Obsolete("Use GetUnifiedContentLocalPosition instead")]
    public Vector3 GetCurvedContentLocalPosition()
    {
        return GetUnifiedContentLocalPosition();
    }

    /// <summary>
    /// Get FlatPlanar panel position and orientation for cursor mapping.
    /// Returns the panel center position and right vector in ClusterRig local space,
    /// matching FlatPlanarMeshGenerator exactly.
    /// </summary>
    /// <param name="enabledPanelIndex">Index within enabled panels (0, 1, 2...)</param>
    /// <param name="enabledCount">Total number of enabled panels</param>
    /// <param name="panelCenter">Output: Panel center in ClusterRig local space</param>
    /// <param name="panelRight">Output: Panel right vector in ClusterRig local space</param>
    /// <param name="panelNormal">Output: Panel normal in ClusterRig local space</param>
    public void GetFlatPlanarPanelTransform(int enabledPanelIndex, int enabledCount,
        out Vector3 panelCenter, out Vector3 panelRight, out Vector3 panelNormal)
    {
        // Get reference panel dimensions
        var refPanel = _panels.Count > 0 ? _panels[_panels.Count / 2] : null;
        float panelWidth = refPanel ? refPanel.width : 1f;
        float panelHeight = refPanel ? refPanel.height : 0.5625f;

        // FlatPlanar mode uses FULL panel width (margin = 0) for mesh generation
        // This matches ClusterVisualFlatPlanar.GenerateMeshes() which sets meshMarginH = 0
        float boardWidth = panelWidth;  // No margin reduction
        float arcRadius = ArcRadius;

        // Calculate angle per panel (same formula as FlatPlanarMeshGenerator with margin=0)
        float boardAngleRad = 2f * Mathf.Atan(boardWidth / 2f / arcRadius);
        float gapAngleRad = 2f * Mathf.Atan(edgeGapMeters / 2f / arcRadius);
        float overlapAngleRad = 2f * Mathf.Atan(panelOverlap / 2f / arcRadius);
        float angleDeg = (boardAngleRad + gapAngleRad - overlapAngleRad) * Mathf.Rad2Deg;

        // Get yaw angle for this panel (same logic as FlatPlanarMeshGenerator.GetPanelYaws)
        float panelYawDeg = enabledCount switch
        {
            1 => 0f,
            2 => enabledPanelIndex == 0 ? 0f : angleDeg,
            3 => (enabledPanelIndex - 1) * angleDeg,
            _ => (enabledPanelIndex - (enabledCount - 1) / 2f) * angleDeg
        };

        float panelYawRad = panelYawDeg * Mathf.Deg2Rad;

        // Panel center position in ClusterRig local space (same as FlatPlanarMeshGenerator)
        float panelCenterX = Mathf.Sin(panelYawRad) * arcRadius;
        float panelCenterZ = Mathf.Cos(panelYawRad) * arcRadius - arcRadius;
        panelCenter = new Vector3(panelCenterX, 0f, panelCenterZ);

        // Panel orientation vectors (same as FlatPlanarMeshGenerator)
        panelRight = new Vector3(Mathf.Cos(panelYawRad), 0f, -Mathf.Sin(panelYawRad));
        panelNormal = new Vector3(-Mathf.Sin(panelYawRad), 0f, -Mathf.Cos(panelYawRad));
    }

    /// <summary>
    /// Get the board dimensions used for FlatPlanar cursor mapping.
    /// IMPORTANT: FlatPlanar mode uses FULL panel dimensions (margin = 0) for mesh generation,
    /// so cursor must use the same dimensions to match.
    /// </summary>
    public void GetFlatPlanarBoardDimensions(out float boardWidth, out float boardHeight)
    {
        var refPanel = _panels.Count > 0 ? _panels[_panels.Count / 2] : null;
        float panelWidth = refPanel ? refPanel.width : 1f;
        float panelHeight = refPanel ? refPanel.height : 0.5625f;

        // FlatPlanar mode uses full panel dimensions (meshMarginH = 0, meshMarginV = 0)
        // This matches ClusterVisualFlatPlanar.GenerateMeshes() which sets margins to 0
        boardWidth = panelWidth;
        boardHeight = panelHeight;
    }

    /// <summary>
    /// Build cluster with specified number of panels
    /// </summary>
    /// <param name="count">Number of panels to create</param>
    /// <param name="skipSampleTextures">If true, don't assign sample textures to panels (used when streaming will provide textures)</param>
    public void BuildWithPanelCount(int count, bool skipSampleTextures = false)
    {
        if (count < 1) count = 1;

        // FixedThreeSlot mode only supports max 3 panels
        int maxPanels = layoutMode == ClusterLayoutMode.FixedThreeSlot ? 3 : 6;
        if (count > maxPanels) count = maxPanels;

        // Ensure cluster is inside VirtualObjects parent and has correct layer
        // Skip when using parent-origin mode (parent is managed by caller)
        if (!useParentOrigin)
        {
            EnsureVirtualObjectsParent();
        }

        KillChildren();
        CreatePanels(count, skipSampleTextures);
        LinkNeighbors();
        LayoutFromCamera();

        if (enableClusterVisuals)
        {
            ApplyClusterVisuals();
        }

        Debug.Log($"[WorldPanelClusterRig] Built {count} panels" +
            (enableClusterVisuals ? " with cluster visuals" : "") +
            (skipSampleTextures ? " (no sample textures)" : ""));
    }

    /// <summary>
    /// Get the world size of the cluster based on enabled panel bounds.
    /// Returns Vector2 (width, height) in world units.
    /// </summary>
    public Vector2 GetWorldSize()
    {
        if (_panels == null || _panels.Count == 0)
        {
            return Vector2.zero;
        }

        // Calculate bounds from enabled panels only
        Bounds combinedBounds = new Bounds();
        bool first = true;

        for (int i = 0; i < _panels.Count; i++)
        {
            var panel = _panels[i];
            if (panel == null) continue;

            // Skip disabled panels
            bool isEnabled = i < _panelEnabledStates.Count ? _panelEnabledStates[i] : true;
            if (!isEnabled) continue;

            // Get panel's world bounds
            Bounds panelBounds = new Bounds(panel.transform.position, Vector3.zero);

            // Use panel's width and height
            float halfWidth = panel.width / 2f;
            float halfHeight = panel.height / 2f;

            // Expand bounds based on panel's local axes
            Vector3 right = panel.transform.right * halfWidth;
            Vector3 up = panel.transform.up * halfHeight;

            panelBounds.Encapsulate(panel.transform.position + right + up);
            panelBounds.Encapsulate(panel.transform.position + right - up);
            panelBounds.Encapsulate(panel.transform.position - right + up);
            panelBounds.Encapsulate(panel.transform.position - right - up);

            if (first)
            {
                combinedBounds = panelBounds;
                first = false;
            }
            else
            {
                combinedBounds.Encapsulate(panelBounds);
            }
        }

        // Return width (x) and height (y)
        return new Vector2(combinedBounds.size.x, combinedBounds.size.y);
    }

    /// <summary>
    /// Get the combined bounds of enabled panels in world space.
    /// </summary>
    public Bounds GetWorldBounds()
    {
        Bounds combinedBounds = new Bounds(transform.position, Vector3.zero);

        if (_panels == null || _panels.Count == 0)
        {
            return combinedBounds;
        }

        bool first = true;
        for (int i = 0; i < _panels.Count; i++)
        {
            var panel = _panels[i];
            if (panel == null) continue;

            // Skip disabled panels
            bool isEnabled = i < _panelEnabledStates.Count ? _panelEnabledStates[i] : true;
            if (!isEnabled) continue;

            // Get panel's world bounds
            Bounds panelBounds = new Bounds(panel.transform.position, Vector3.zero);

            // Include visual expansion
            float expandedWidth = panel.width + (visualExpansion * 2f);
            float expandedHeight = panel.height + (visualExpansion * 2f);

            float halfWidth = expandedWidth / 2f;
            float halfHeight = expandedHeight / 2f;

            Vector3 right = panel.transform.right * halfWidth;
            Vector3 up = panel.transform.up * halfHeight;

            panelBounds.Encapsulate(panel.transform.position + right + up);
            panelBounds.Encapsulate(panel.transform.position + right - up);
            panelBounds.Encapsulate(panel.transform.position - right + up);
            panelBounds.Encapsulate(panel.transform.position - right - up);

            if (first)
            {
                combinedBounds = panelBounds;
                first = false;
            }
            else
            {
                combinedBounds.Encapsulate(panelBounds);
            }
        }

        return combinedBounds;
    }

    /// <summary>
    /// Get bounds for taskbar follow positioning.
    /// - FixedThreeSlot: Returns bounds of center panel only
    /// - Dynamic: Returns combined bounds of all panels
    /// </summary>
    public Bounds GetFollowBounds()
    {
        if (_panels == null || _panels.Count == 0)
        {
            return new Bounds(transform.position, Vector3.zero);
        }

        // FixedThreeSlot mode: use center panel bounds
        if (layoutMode == ClusterLayoutMode.FixedThreeSlot)
        {
            // In FixedThreeSlot:
            // 1 panel: index 0 is center
            // 2 panels: index 0 is center, index 1 is right
            // 3 panels: index 0 is left, index 1 is center, index 2 is right
            int centerIndex = _panels.Count == 3 ? 1 : 0;
            var centerPanel = _panels[centerIndex];

            if (centerPanel != null)
            {
                return GetPanelBounds(centerPanel);
            }
        }

        // Dynamic mode: use combined bounds of all panels
        return GetWorldBounds();
    }

    /// <summary>
    /// Get bounds of a single panel.
    /// </summary>
    private Bounds GetPanelBounds(WorldPanelPlus panel)
    {
        Bounds bounds = new Bounds(panel.transform.position, Vector3.zero);

        // Include visual expansion in bounds calculation so taskbar
        // positions relative to the outer visual edge, not just the board content
        float expandedWidth = panel.width + (visualExpansion * 2f);
        float expandedHeight = panel.height + (visualExpansion * 2f);

        float halfWidth = expandedWidth / 2f;
        float halfHeight = expandedHeight / 2f;

        Vector3 right = panel.transform.right * halfWidth;
        Vector3 up = panel.transform.up * halfHeight;

        bounds.Encapsulate(panel.transform.position + right + up);
        bounds.Encapsulate(panel.transform.position + right - up);
        bounds.Encapsulate(panel.transform.position - right + up);
        bounds.Encapsulate(panel.transform.position - right - up);

        return bounds;
    }

    #region Panel Enable/Disable

    /// <summary>
    /// Enable or disable a specific panel by index.
    /// Disabled panels are hidden and excluded from layout.
    /// </summary>
    /// <param name="panelIndex">Index of the panel (0-based, corresponds to monitor index)</param>
    /// <param name="enabled">True to enable, false to disable</param>
    public void SetPanelEnabled(int panelIndex, bool enabled)
    {
        if (panelIndex < 0 || panelIndex >= _panels.Count)
        {
            Debug.LogWarning($"[WorldPanelClusterRig] SetPanelEnabled: Invalid index {panelIndex}");
            return;
        }

        // Ensure state list is properly sized
        while (_panelEnabledStates.Count < _panels.Count)
        {
            _panelEnabledStates.Add(true);
        }

        bool wasEnabled = _panelEnabledStates[panelIndex];
        if (wasEnabled == enabled) return; // No change

        _panelEnabledStates[panelIndex] = enabled;

        var panel = _panels[panelIndex];
        if (panel != null)
        {
            // Hide/show the panel
            panel.SetVisible(enabled);
        }

        // Reposition enabled panels WITHOUT moving the cluster itself
        // This keeps the cluster and taskbar in their current positions
        LayoutPanelsInPlace();

        // Rebuild cluster visuals (mesh regeneration needed for panel count change)
        if (enableClusterVisuals)
        {
            RebuildClusterVisuals();
        }

        // Fire event
        OnPanelEnabledChanged?.Invoke(panelIndex, enabled);

        Debug.Log($"[WorldPanelClusterRig] Panel {panelIndex} {(enabled ? "enabled" : "disabled")}. " +
            $"Enabled panels: {GetEnabledPanelCount()}/{_panels.Count}");
    }

    /// <summary>
    /// Check if a panel is enabled.
    /// </summary>
    public bool IsPanelEnabled(int panelIndex)
    {
        if (panelIndex < 0 || panelIndex >= _panels.Count) return false;
        if (panelIndex >= _panelEnabledStates.Count) return true; // Default enabled
        return _panelEnabledStates[panelIndex];
    }

    /// <summary>
    /// Get the number of currently enabled panels.
    /// </summary>
    public int GetEnabledPanelCount()
    {
        int count = 0;
        for (int i = 0; i < _panels.Count; i++)
        {
            bool isEnabled = i < _panelEnabledStates.Count ? _panelEnabledStates[i] : true;
            if (isEnabled) count++;
        }
        return count;
    }

    /// <summary>
    /// Get all enabled panel indices.
    /// </summary>
    public List<int> GetEnabledPanelIndices()
    {
        var result = new List<int>();
        for (int i = 0; i < _panels.Count; i++)
        {
            bool isEnabled = i < _panelEnabledStates.Count ? _panelEnabledStates[i] : true;
            if (isEnabled) result.Add(i);
        }
        return result;
    }

    /// <summary>
    /// Enable all panels.
    /// </summary>
    public void EnableAllPanels()
    {
        for (int i = 0; i < _panels.Count; i++)
        {
            SetPanelEnabled(i, true);
        }
    }

    #endregion

    /// <summary>
    /// Ensures the cluster rig is placed inside a VirtualObjects parent and has the correct layer
    /// </summary>
    void EnsureVirtualObjectsParent()
    {
        const string VIRTUAL_OBJECTS_NAME = "VirtualObjects";
        int virtualObjectsLayer = LayerMask.NameToLayer(VIRTUAL_OBJECTS_NAME);

        if (virtualObjectsLayer == -1)
        {
            Debug.LogWarning($"[WorldPanelClusterRig] Layer '{VIRTUAL_OBJECTS_NAME}' not found. Please create it in Edit > Project Settings > Tags and Layers.");
            return;
        }

        // Find or create VirtualObjects parent
        GameObject virtualObjectsParent = GameObject.Find(VIRTUAL_OBJECTS_NAME);
        if (virtualObjectsParent == null)
        {
            virtualObjectsParent = new GameObject(VIRTUAL_OBJECTS_NAME);
            virtualObjectsParent.layer = virtualObjectsLayer;
        }

        // Move this cluster rig under VirtualObjects if not already
        if (transform.parent != virtualObjectsParent.transform)
        {
            transform.SetParent(virtualObjectsParent.transform, true);
        }

        // Set layer for the cluster rig itself
        SetLayerRecursively(gameObject, virtualObjectsLayer);
    }

    [ContextMenu("Build or Rebuild Cluster (3 panels)")]
    public void BuildOrRebuild()
    {
        BuildWithPanelCount(3);
    }

    /// <summary>
    /// Set panel cluster style dynamically (can be called during streaming).
    /// Does NOT recreate panels - only updates layout mode and visual settings.
    /// </summary>
    /// <param name="isCurvedSurround">True for Curved Surround (Dynamic + curved), false for Flat Planar (FixedThreeSlot + flat)</param>
    public void SetStyle(bool isCurvedSurround)
    {
        var oldLayoutMode = layoutMode;
        var oldCurved = useCurvedVisual;

        if (isCurvedSurround)
        {
            layoutMode = ClusterLayoutMode.Dynamic;
            useCurvedVisual = true;
        }
        else
        {
            layoutMode = ClusterLayoutMode.FixedThreeSlot;
            useCurvedVisual = false;
        }

        // Only update if something changed
        if (oldLayoutMode != layoutMode || oldCurved != useCurvedVisual)
        {
            Debug.Log($"[WorldPanelClusterRig] Style changed: {(isCurvedSurround ? "Curved Surround" : "Flat Planar")} " +
                $"(layout={layoutMode}, curved={useCurvedVisual})");

            // Refresh layout and visuals
            RefreshLayoutAndVisuals();
        }
    }

    /// <summary>
    /// Refresh layout and visuals without recreating panels.
    /// Used for style switching during streaming.
    /// </summary>
    private void RefreshLayoutAndVisuals()
    {
        // Re-layout panels based on new mode
        LayoutFromCamera();

        // Re-apply cluster visuals
        if (enableClusterVisuals)
        {
            ApplyClusterVisuals();
        }
    }

    private float _lastUpdateArcRadius = -1f;

    void Update()
    {
        if (!Application.isPlaying && _panels.Count > 0)
        {
            LayoutFromCamera();
        }
        else if (Application.isPlaying && _panels.Count > 0)
        {
            // Detect arc radius changes (e.g. from zoom controller) and trigger layout refresh
            float currentR = ArcRadius;
            if (Mathf.Abs(currentR - _lastUpdateArcRadius) > 0.001f)
            {
                _lastUpdateArcRadius = currentR;
                LayoutFromCamera();
            }
        }
    }

    void LayoutFromCamera()
    {
        // Use parent-origin positioning when parented inside RTTMenu
        if (useParentOrigin)
        {
            LayoutFromParentOrigin();
            return;
        }

        var cam = Cam;
        if (!cam || _panels.Count == 0) return;

        Vector3 camFwd = cam.transform.forward;
        Vector3 camUp = Vector3.up;
        if (faceCameraYawOnly)
        {
            camFwd.y = 0f;
            if (camFwd.sqrMagnitude < 1e-6f) camFwd = cam.transform.forward;
            camFwd.Normalize();
        }

        Vector3 clusterCenter = cam.transform.position + camFwd * ViewerDistance + camUp * verticalOffset;
        transform.SetPositionAndRotation(clusterCenter, Quaternion.LookRotation(camFwd, camUp));

        var refPanel = _panels[_panels.Count / 2];
        float panelWidth = refPanel ? refPanel.width : 1f;

        // Use ArcRadius for curvature math (this is now capped at 1.8m)
        float currentArcRadius = ArcRadius;

        // For Flat Planar mode (FixedThreeSlot + !useCurvedVisual), use FULL panel width for spacing
        // because boards are NOT scaled down by margins - they fill the entire panel area
        // For Curved mode, use board width (panel minus margins) for spacing
        float spacingWidth;
        if (layoutMode == ClusterLayoutMode.FixedThreeSlot && !useCurvedVisual)
        {
            // Flat Planar: use full panel width so full-size boards touch edge-to-edge
            spacingWidth = panelWidth;
        }
        else
        {
            // Curved mode: use board width (panel minus margins) for spacing so Board edges touch
            spacingWidth = panelWidth * (1f - 2f * contentMarginHorizontal);
        }

        float boardAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(spacingWidth / 2f / currentArcRadius);
        float gapAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(edgeGapMeters / 2f / currentArcRadius);
        // Apply overlap to eliminate seams between adjacent panels
        float overlapAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(panelOverlap / 2f / currentArcRadius);
        float angleDeg = boardAngleDeg + gapAngleDeg - overlapAngleDeg;

        // Create list of ONLY enabled panels to allow reflow into primary slots
        var enabledPanels = new List<WorldPanelPlus>();
        for (int i = 0; i < _panels.Count; i++)
        {
            if (IsPanelEnabled(i)) enabledPanels.Add(_panels[i]);
        }

        int enabledCount = enabledPanels.Count;
        if (enabledCount == 0) return;

        if (layoutMode == ClusterLayoutMode.FixedThreeSlot)
        {
            // FixedThreeSlot: enabled panels are placed in fixed 3-slot positions
            // Slot positions: -1 (left), 0 (center), 1 (right)
            // Layout based on enabled panel count
            int[] slotOffsets = enabledCount switch
            {
                1 => new[] { 0 },
                2 => new[] { 0, 1 },
                _ => new[] { -1, 0, 1 }
            };

            for (int i = 0; i < enabledCount; i++)
            {
                float yawDeg = slotOffsets[i] * angleDeg;
                PlacePanelOnArc(enabledPanels[i], yawDeg, cam, camFwd, camUp);
            }
        }
        else
        {
            // Dynamic mode: enabled panels spread evenly across the arc
            for (int i = 0; i < enabledCount; i++)
            {
                float offset = i - (enabledCount - 1) / 2f;
                float yawDeg = offset * angleDeg;
                PlacePanelOnArc(enabledPanels[i], yawDeg, cam, camFwd, camUp);
            }
        }
    }

    /// <summary>
    /// Layout panels relative to parent origin (0,0,0) instead of camera.
    /// Used when ClusterRig is parented inside RTTMenu for synchronized positioning.
    /// - Flat Planar: center panel at local (0,0,0)
    /// - Curved Surround: arc center at local (0,0,0)
    /// </summary>
    void LayoutFromParentOrigin()
    {
        if (_panels.Count == 0) return;

        // Keep cluster at local origin
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        var refPanel = _panels[_panels.Count / 2];
        float panelWidth = refPanel ? refPanel.width : 1f;
        float panelHeight = refPanel ? refPanel.height : 0.5625f;

        // Calculate spacing width based on mode
        float spacingWidth;
        if (layoutMode == ClusterLayoutMode.FixedThreeSlot && !useCurvedVisual)
        {
            spacingWidth = panelWidth;
        }
        else
        {
            spacingWidth = panelWidth * (1f - 2f * contentMarginHorizontal);
        }

        // Get enabled panels
        var enabledPanels = new List<WorldPanelPlus>();
        for (int i = 0; i < _panels.Count; i++)
        {
            if (IsPanelEnabled(i)) enabledPanels.Add(_panels[i]);
        }

        int enabledCount = enabledPanels.Count;
        if (enabledCount == 0) return;

        if (layoutMode == ClusterLayoutMode.FixedThreeSlot && !useCurvedVisual)
        {
            // Flat Planar mode: panels positioned linearly
            // Center panel at local (0, 0, 0)
            int[] slotOffsets = enabledCount switch
            {
                1 => new[] { 0 },
                2 => new[] { 0, 1 },
                _ => new[] { -1, 0, 1 }
            };

            for (int i = 0; i < enabledCount; i++)
            {
                var panel = enabledPanels[i];
                if (panel == null) continue;

                // Calculate horizontal offset from center
                float xOffset = slotOffsets[i] * (spacingWidth + edgeGapMeters - panelOverlap);
                Vector3 localPos = new Vector3(xOffset, verticalOffset, 0);
                Quaternion localRot = Quaternion.identity; // Face forward (Z+)

                panel.transform.localPosition = localPos;
                panel.transform.localRotation = localRot;
            }
        }
        else
        {
            // Curved Surround mode: panels on arc around origin
            // Arc center at local (0, 0, 0)
            float currentArcRadius = ArcRadius;
            float boardAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(spacingWidth / 2f / currentArcRadius);
            float gapAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(edgeGapMeters / 2f / currentArcRadius);
            float overlapAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(panelOverlap / 2f / currentArcRadius);
            float angleDeg = boardAngleDeg + gapAngleDeg - overlapAngleDeg;

            for (int i = 0; i < enabledCount; i++)
            {
                var panel = enabledPanels[i];
                if (panel == null) continue;

                // Calculate angle offset from center
                float offset = i - (enabledCount - 1) / 2f;
                float yawDeg = offset * angleDeg;
                float yawRad = yawDeg * Mathf.Deg2Rad;

                // Position on arc: sin for X, cos for Z offset from radius
                // Panel at angle θ: x = sin(θ) * r, z = cos(θ) * r - r
                // This puts center panel at (0, 0, 0) and others on arc
                float x = Mathf.Sin(yawRad) * currentArcRadius;
                float z = Mathf.Cos(yawRad) * currentArcRadius - currentArcRadius;

                Vector3 localPos = new Vector3(x, verticalOffset, z);

                // Rotation: face outward from arc center (toward camera position)
                // Panel forward points toward camera (which is at -Z direction from arc)
                Quaternion localRot = Quaternion.Euler(0, yawDeg, 0);

                panel.transform.localPosition = localPos;
                panel.transform.localRotation = localRot;
            }
        }

        Debug.Log($"[WorldPanelClusterRig] LayoutFromParentOrigin: {enabledCount} panels, " +
            $"mode={(layoutMode == ClusterLayoutMode.FixedThreeSlot ? "FlatPlanar" : "CurvedSurround")}");
    }

    /// <summary>
    /// Reposition enabled panels within the cluster WITHOUT moving the cluster itself.
    /// Used when enabling/disabling panels to keep cluster and taskbar in place.
    /// </summary>
    void LayoutPanelsInPlace()
    {
        if (_panels.Count == 0) return;

        // Use parent-origin positioning when enabled
        if (useParentOrigin)
        {
            LayoutFromParentOrigin();
            return;
        }

        // Use cluster's current position and rotation (don't move it)
        Vector3 clusterCenter = transform.position;
        Vector3 clusterFwd = transform.forward;
        Vector3 clusterUp = Vector3.up;

        var refPanel = _panels[_panels.Count / 2];
        float panelWidth = refPanel ? refPanel.width : 1f;
        float currentArcRadius = ArcRadius;

        // For Flat Planar mode (FixedThreeSlot + !useCurvedVisual), use FULL panel width for spacing
        // because boards are NOT scaled down by margins - they fill the entire panel area
        // For Curved mode, use board width (panel minus margins) for spacing
        float spacingWidth;
        if (layoutMode == ClusterLayoutMode.FixedThreeSlot && !useCurvedVisual)
        {
            // Flat Planar: use full panel width so full-size boards touch edge-to-edge
            spacingWidth = panelWidth;
        }
        else
        {
            // Curved mode: use board width (panel minus margins) for spacing so Board edges touch
            spacingWidth = panelWidth * (1f - 2f * contentMarginHorizontal);
        }
        float boardAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(spacingWidth / 2f / currentArcRadius);
        float gapAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(edgeGapMeters / 2f / currentArcRadius);
        // Apply overlap to eliminate seams between adjacent panels
        float overlapAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(panelOverlap / 2f / currentArcRadius);
        float angleDeg = boardAngleDeg + gapAngleDeg - overlapAngleDeg;

        // Create list of ONLY enabled panels to allow reflow into primary slots
        var enabledPanels = new List<WorldPanelPlus>();
        for (int i = 0; i < _panels.Count; i++)
        {
            if (IsPanelEnabled(i)) enabledPanels.Add(_panels[i]);
        }

        int enabledCount = enabledPanels.Count;
        if (enabledCount == 0) return;

        if (layoutMode == ClusterLayoutMode.FixedThreeSlot)
        {
            int[] slotOffsets = enabledCount switch
            {
                1 => new[] { 0 },
                2 => new[] { 0, 1 },
                _ => new[] { -1, 0, 1 }
            };

            for (int i = 0; i < enabledCount; i++)
            {
                float yawDeg = slotOffsets[i] * angleDeg;
                PlacePanelOnArcInPlace(enabledPanels[i], yawDeg, clusterCenter, clusterFwd, clusterUp);
            }
        }
        else
        {
            for (int i = 0; i < enabledCount; i++)
            {
                float offset = i - (enabledCount - 1) / 2f;
                float yawDeg = offset * angleDeg;
                PlacePanelOnArcInPlace(enabledPanels[i], yawDeg, clusterCenter, clusterFwd, clusterUp);
            }
        }
    }

    /// <summary>
    /// Place a panel on the arc relative to cluster center (not camera).
    /// </summary>
    void PlacePanelOnArcInPlace(WorldPanelPlus p, float yawDeg, Vector3 clusterCenter, Vector3 clusterFwd, Vector3 clusterUp)
    {
        if (!p) return;

        Quaternion yaw = Quaternion.AngleAxis(yawDeg, clusterUp);
        Vector3 dir = yaw * clusterFwd;
        float currentArcRadius = ArcRadius;
        Vector3 pos = clusterCenter + dir * currentArcRadius - clusterFwd * currentArcRadius;

        Quaternion rot;
        if (panelsFaceCamera)
        {
            rot = Quaternion.LookRotation(dir, clusterUp);
        }
        else
        {
            rot = Quaternion.LookRotation(-clusterFwd, clusterUp);
        }

        p.transform.SetPositionAndRotation(pos, rot);
    }

    void PlacePanelOnArc(WorldPanelPlus p, float yawDeg, Camera cam, Vector3 camFwd, Vector3 camUp)
    {
        if (!p) return;

        Quaternion yaw = Quaternion.AngleAxis(yawDeg, camUp);
        Vector3 dir = yaw * camFwd;
        float currentArcRadius = ArcRadius;
        Vector3 pos = cam.transform.position + dir * currentArcRadius + camUp * verticalOffset;

        Quaternion rot;
        if (panelsFaceCamera)
        {
            rot = Quaternion.LookRotation(dir, camUp);
        }
        else
        {
            rot = Quaternion.LookRotation(-camFwd, camUp);
        }

        p.transform.SetPositionAndRotation(pos, rot);
    }

    void CreatePanels(int count, bool skipSampleTextures = false)
    {
        _panels.Clear();
        _panelEnabledStates.Clear();

        // Load sample textures for testing curved mode (unless skipped)
        Texture2D[] sampleTextures = skipSampleTextures ? new Texture2D[0] : LoadSampleTextures();

        for (int i = 0; i < count; i++)
        {
            string name = $"Panel_{i}";
            var p = CreateOne(name);

            // Assign sample texture for visual testing (cycle through available samples)
            // Skip if streaming will provide textures
            if (sampleTextures.Length > 0)
            {
                p.contentTexture = sampleTextures[i % sampleTextures.Length];
                p.Apply(); // Apply to update board texture
            }

            _panels.Add(p);
            _panelEnabledStates.Add(true); // All panels enabled by default
        }

        // Sync sizes from first panel
        if (_panels.Count > 1)
        {
            var first = _panels[0];
            for (int i = 1; i < _panels.Count; i++)
            {
                _panels[i].width = first.width;
                _panels[i].height = first.height;
                _panels[i].Apply();
            }
        }
    }

    /// <summary>
    /// Load sample textures from Resources for visual testing
    /// </summary>
    Texture2D[] LoadSampleTextures()
    {
        var textures = new System.Collections.Generic.List<Texture2D>();

        // Try to load sample_1 and sample_2 from Resources/WorldPanelPlus/
        var sample1 = Resources.Load<Texture2D>("WorldPanelPlus/sample_1");
        var sample2 = Resources.Load<Texture2D>("WorldPanelPlus/sample_2");

        if (sample1 != null) textures.Add(sample1);
        if (sample2 != null) textures.Add(sample2);

        if (textures.Count == 0)
        {
            Debug.LogWarning("[WorldPanelClusterRig] No sample textures found in Resources/WorldPanelPlus/");
        }
        else
        {
            Debug.Log($"[WorldPanelClusterRig] Loaded {textures.Count} sample textures for testing");
        }

        return textures.ToArray();
    }

    WorldPanelPlus CreateOne(string name)
    {
        WorldPanelPlus p;
        if (panelPrefab)
        {
            var go = Instantiate(panelPrefab.gameObject, transform);
            go.name = name;
            p = go.GetComponent<WorldPanelPlus>();
        }
        else
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            p = go.AddComponent<WorldPanelPlus>();
            p.Rebuild();
        }

        p.Apply();

        // Set VirtualObjects layer for the panel
        int virtualObjectsLayer = LayerMask.NameToLayer("VirtualObjects");
        if (virtualObjectsLayer != -1)
        {
            SetLayerRecursively(p.gameObject, virtualObjectsLayer);
        }
        else
        {
            SetLayerRecursively(p.gameObject, gameObject.layer);
        }

        return p;
    }

    void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    void LinkNeighbors()
    {
        for (int i = 0; i < _panels.Count; i++)
        {
            var p = _panels[i];
            p.neighborLeft = (i > 0) ? _panels[i - 1] : null;
            p.neighborRight = (i < _panels.Count - 1) ? _panels[i + 1] : null;
            p.neighborUp = null;
            p.neighborDown = null;
        }
    }

    /// <summary>
    /// Apply ClusterPanelVisual to all panels for seamless appearance
    /// </summary>
    void ApplyClusterVisuals()
    {
        if (useCurvedVisual)
        {
            ApplyCurvedVisual();
        }
        else
        {
            ApplyFlatPlanarVisual();
        }
    }

    /// <summary>
    /// Apply curved mesh visual for true seamless appearance
    /// </summary>
    void ApplyCurvedVisual()
    {
        // Remove flat planar visual if present (check component explicitly in case ref is null)
        if (_flatPlanarVisual == null) _flatPlanarVisual = GetComponent<ClusterVisualFlatPlanar>();
        if (_flatPlanarVisual != null)
        {
            if (Application.isPlaying)
                Destroy(_flatPlanarVisual);
            else
                DestroyImmediate(_flatPlanarVisual);
            _flatPlanarVisual = null;
        }

        // Hide individual panel boards - curved visual will render content
        foreach (var panel in _panels)
        {
            if (panel == null) continue;

            // Hide the panel's board - curved visual will render content
            // Use SetVisible(false) which properly sets boardVisible AND disables renderer/collider
            // This prevents Apply() from re-enabling the renderer
            panel.SetVisible(false);
        }

        // Get or create ClusterVisualCurved
        _curvedVisual = GetComponent<ClusterVisualCurved>();
        if (_curvedVisual == null)
        {
            _curvedVisual = gameObject.AddComponent<ClusterVisualCurved>();
        }

        // Initialize curved visual
        _curvedVisual.Initialize();

        // Apply colors
        _curvedVisual.SetGlowColors(glowColorA, glowColorB);
        _curvedVisual.SetGlassColors(glassColorA, glassColorB);
        // Sync corner & border settings
        _curvedVisual.SetCornerSettings(cornerRadius, edgePadding);
        _curvedVisual.SetBorderSettings(borderWidth, lightSize);
        _curvedVisual.SetFrameMargin(frameMargin);
        _curvedVisual.SetGlowExpansion(glowExpansion);
        _curvedVisual.SetVisualExpansion(visualExpansion);
    }

    /// <summary>
    /// Apply flat planar visual - boards stay at FULL size with NO corner radius.
    /// ClusterVisualFlatPlanar creates the background/border mesh separately.
    /// </summary>
    void ApplyFlatPlanarVisual()
    {
        // Remove curved visual if present (check component explicitly in case ref is null)
        if (_curvedVisual == null) _curvedVisual = GetComponent<ClusterVisualCurved>();
        if (_curvedVisual != null)
        {
            if (Application.isPlaying)
                Destroy(_curvedVisual);
            else
                DestroyImmediate(_curvedVisual);
            _curvedVisual = null;
        }

        // Hide individual panel boards - unified content layer will render content
        // This eliminates the seam between panels
        foreach (var panel in _panels)
        {
            if (panel == null) continue;

            // Hide the panel's board - unified content layer renders all content
            panel.SetVisible(false);
        }

        // Get or create ClusterVisualFlatPlanar for background/border
        _flatPlanarVisual = GetComponent<ClusterVisualFlatPlanar>();
        if (_flatPlanarVisual == null)
        {
            _flatPlanarVisual = gameObject.AddComponent<ClusterVisualFlatPlanar>();
        }

        // Initialize flat planar visual
        _flatPlanarVisual.Initialize();

        // Apply colors
        _flatPlanarVisual.SetGlowColors(glowColorA, glowColorB);
        _flatPlanarVisual.SetGlassColors(glassColorA, glassColorB);
        _flatPlanarVisual.SetCornerSettings(cornerRadius, edgePadding); // Ensure Flat Planar also gets settings
        _flatPlanarVisual.SetBorderSettings(borderWidth, lightSize);
        _flatPlanarVisual.SetFrameMargin(frameMargin);
        _flatPlanarVisual.SetGlowExpansion(glowExpansion);
        _flatPlanarVisual.SetVisualExpansion(visualExpansion);

        Debug.Log($"[WorldPanelClusterRig] Applied Flat Planar: unified content layer, boards hidden");
    }

    /// <summary>
    /// Refresh cluster visuals (call after changing visual settings)
    /// </summary>
    [ContextMenu("Refresh Cluster Visuals")]
    public void RefreshClusterVisuals()
    {
        if (!enableClusterVisuals) return;

        if (useCurvedVisual && _curvedVisual != null)
        {
            _curvedVisual.SetGlowColors(glowColorA, glowColorB);
            _curvedVisual.SetGlassColors(glassColorA, glassColorB);
            _curvedVisual.SetCornerSettings(cornerRadius, edgePadding);
            _curvedVisual.SetBorderSettings(borderWidth, lightSize);
            _curvedVisual.SetFrameMargin(frameMargin);
            _curvedVisual.SetGlowExpansion(glowExpansion);
            _curvedVisual.SetVisualExpansion(visualExpansion);
            _curvedVisual.UpdateContentTextures();
        }
        else if (!useCurvedVisual && _flatPlanarVisual != null)
        {
            // Flat Planar mode - update ClusterVisualFlatPlanar
            _flatPlanarVisual.SetGlowColors(glowColorA, glowColorB);
            _flatPlanarVisual.SetGlassColors(glassColorA, glassColorB);
            _flatPlanarVisual.SetCornerSettings(cornerRadius, edgePadding);
            _flatPlanarVisual.SetBorderSettings(borderWidth, lightSize);
            _flatPlanarVisual.SetFrameMargin(frameMargin);
            _flatPlanarVisual.SetGlowExpansion(glowExpansion);
            _flatPlanarVisual.SetVisualExpansion(visualExpansion);
        }
    }

    /// <summary>
    /// Rebuild cluster visuals (call after enabling/disabling panels to regenerate meshes)
    /// </summary>
    public void RebuildClusterVisuals()
    {
        if (!enableClusterVisuals) return;

        // Try to get visual references if null (might exist but not referenced)
        if (_curvedVisual == null)
        {
            _curvedVisual = GetComponent<ClusterVisualCurved>();
        }
        if (_flatPlanarVisual == null)
        {
            _flatPlanarVisual = GetComponent<ClusterVisualFlatPlanar>();
        }

        if (useCurvedVisual)
        {
            // Curved Surround mode
            if (_curvedVisual != null)
            {
                Debug.Log($"[WorldPanelClusterRig] RebuildClusterVisuals: Rebuilding curved visual, enabledPanels={GetEnabledPanelCount()}");
                
                // Ensure flat visual is removed
                if (_flatPlanarVisual == null) _flatPlanarVisual = GetComponent<ClusterVisualFlatPlanar>();
                if (_flatPlanarVisual != null)
                {
                    if (Application.isPlaying) Destroy(_flatPlanarVisual);
                    else DestroyImmediate(_flatPlanarVisual);
                    _flatPlanarVisual = null;
                }

                _curvedVisual.Rebuild();
            }
            else
            {
                Debug.Log("[WorldPanelClusterRig] RebuildClusterVisuals: Creating new curved visual");
                ApplyCurvedVisual();
            }
        }
        else
        {
            // Flat Planar mode - rebuild ClusterVisualFlatPlanar
            Debug.Log($"[WorldPanelClusterRig] RebuildClusterVisuals: Rebuilding flat planar visual, enabledPanels={GetEnabledPanelCount()}");
            ApplyFlatPlanarVisual();
        }
    }

    void KillChildren()
    {
        // Cleanup curved visual
        if (_curvedVisual != null)
        {
#if UNITY_EDITOR
            DestroyImmediate(_curvedVisual);
#else
            Destroy(_curvedVisual);
#endif
            _curvedVisual = null;
        }

        // Cleanup flat planar visual
        if (_flatPlanarVisual != null)
        {
#if UNITY_EDITOR
            DestroyImmediate(_flatPlanarVisual);
#else
            Destroy(_flatPlanarVisual);
#endif
            _flatPlanarVisual = null;
        }

#if UNITY_EDITOR
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
#else
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
#endif
        _panels.Clear();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (_panels.Count > 0)
        {
            LayoutFromCamera();
        }

        // Auto-refresh visuals when inspector values change
        if (enableClusterVisuals)
        {
            RefreshClusterVisuals();
        }
    }
#endif
}