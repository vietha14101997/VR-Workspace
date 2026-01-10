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
/// Optionally applies seamless ClusterPanelVisual for connected appearance.
/// </summary>
[ExecuteAlways]
public class WorldPanelClusterRig : MonoBehaviour
{
    [Header("Source")]
    public WorldPanelPlus panelPrefab;

    [Header("Layout")]
    [Tooltip("Dynamic: panels spread evenly. FixedThreeSlot: panels use fixed 3-slot positions (max 3 panels)")]
    public ClusterLayoutMode layoutMode = ClusterLayoutMode.FixedThreeSlot;
    public float distanceFromCamera = 2.0f;
    [Tooltip("Extra gap in meters between panel edges (0 = edges touch)")]
    [Range(0f, 0.1f)] public float edgeGapMeters = 0f;
    [Tooltip("Whether panels should face directly toward camera (true) or have limited tilt (false)")]
    public bool panelsFaceCamera = true;
    public float verticalOffset = 0f;
    public bool faceCameraYawOnly = true;
    [Tooltip("Overlap amount (in meters) between adjacent panels to eliminate seams")]
    [Range(0f, 0.01f)] public float panelOverlap = 0.01f;

    [Header("Cluster Visuals")]
    [Tooltip("Enable seamless glass background and glowing border across all panels")]
    public bool enableClusterVisuals = true;
    [Tooltip("Use curved mesh visual (true) or per-panel visual (false). Curved provides true seamless appearance.")]
    public bool useCurvedVisual = false;

    private ClusterVisualCurved _curvedVisual;

    [Header("Visual Settings")]
    [SerializeField] private float cornerRadius = 0.04f;
    [SerializeField] private float edgePadding = 0.009f;
    [Tooltip("Extra size (in meters) for glow overflow on outer edges")]
    [SerializeField] private float glowExpansion = 0.05f;
    [Tooltip("Overlap amount (in meters) at panel junctions for seamless appearance")]
    [SerializeField] private float junctionOverlap = 0.01f;
    [Tooltip("Content margin ratio (content is inset by this fraction)")]
    [SerializeField] private float contentMarginHorizontal = 0.04f;
    [SerializeField] private float contentMarginVertical = 0.045f;
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorA = new Color(0.3f, 1f, 1f, 1f);
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColorB = new Color(1f, 0.4f, 1f, 1f);
    [SerializeField] private Color glassColorA = new Color(0f, 0.55f, 0.65f, 0.35f);
    [SerializeField] private Color glassColorB = new Color(0.30f, 0.12f, 0.50f, 0.32f);

    [Header("Dynamic Panels")]
    [SerializeField] private List<WorldPanelPlus> _panels = new List<WorldPanelPlus>();
    public List<WorldPanelPlus> panels => _panels;

    [SerializeField, HideInInspector]
    private List<ClusterPanelVisual> _panelVisuals = new List<ClusterPanelVisual>();

    Camera Cam => Application.isPlaying ? Camera.main : FindObjectOfType<Camera>();

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
        EnsureVirtualObjectsParent();

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
    /// Get the world size of the cluster based on panel bounds.
    /// Returns Vector2 (width, height) in world units.
    /// </summary>
    public Vector2 GetWorldSize()
    {
        if (_panels == null || _panels.Count == 0)
        {
            return Vector2.zero;
        }

        // Calculate bounds from all panels
        Bounds combinedBounds = new Bounds();
        bool first = true;

        foreach (var panel in _panels)
        {
            if (panel == null) continue;

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
    /// Get the combined bounds of all panels in world space.
    /// </summary>
    public Bounds GetWorldBounds()
    {
        Bounds combinedBounds = new Bounds(transform.position, Vector3.zero);

        if (_panels == null || _panels.Count == 0)
        {
            return combinedBounds;
        }

        bool first = true;
        foreach (var panel in _panels)
        {
            if (panel == null) continue;

            // Get panel's world bounds
            Bounds panelBounds = new Bounds(panel.transform.position, Vector3.zero);

            float halfWidth = panel.width / 2f;
            float halfHeight = panel.height / 2f;

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

        float halfWidth = panel.width / 2f;
        float halfHeight = panel.height / 2f;

        Vector3 right = panel.transform.right * halfWidth;
        Vector3 up = panel.transform.up * halfHeight;

        bounds.Encapsulate(panel.transform.position + right + up);
        bounds.Encapsulate(panel.transform.position + right - up);
        bounds.Encapsulate(panel.transform.position - right + up);
        bounds.Encapsulate(panel.transform.position - right - up);

        return bounds;
    }

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

    void Update()
    {
        if (!Application.isPlaying && _panels.Count > 0) LayoutFromCamera();
    }

    void LayoutFromCamera()
    {
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

        Vector3 clusterCenter = cam.transform.position + camFwd * distanceFromCamera + camUp * verticalOffset;
        transform.SetPositionAndRotation(clusterCenter, Quaternion.LookRotation(camFwd, camUp));

        var refPanel = _panels[_panels.Count / 2];
        float panelWidth = refPanel ? refPanel.width : 1f;

        // Use Board width (panel minus margins) for spacing so Board edges touch
        float boardWidth = panelWidth * (1f - 2f * contentMarginHorizontal);

        float boardAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(boardWidth / 2f / distanceFromCamera);
        float gapAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(edgeGapMeters / 2f / distanceFromCamera);

        float angleDeg = boardAngleDeg + gapAngleDeg;

        int count = _panels.Count;

        if (layoutMode == ClusterLayoutMode.FixedThreeSlot)
        {
            // FixedThreeSlot: panels are placed in fixed 3-slot positions
            // Slot positions: -1 (left), 0 (center), 1 (right)
            // 1 panel:  center only       → slot 0
            // 2 panels: center + right    → slots 0, 1
            // 3 panels: left + center + right → slots -1, 0, 1
            int[] slotOffsets = count switch
            {
                1 => new[] { 0 },
                2 => new[] { 0, 1 },
                _ => new[] { -1, 0, 1 }
            };

            for (int i = 0; i < count; i++)
            {
                float yawDeg = slotOffsets[i] * angleDeg;
                PlacePanelOnArc(_panels[i], yawDeg, cam, camFwd, camUp);
            }
        }
        else
        {
            // Dynamic mode: panels spread evenly across the arc
            for (int i = 0; i < count; i++)
            {
                float offset = i - (count - 1) / 2f;
                float yawDeg = offset * angleDeg;
                PlacePanelOnArc(_panels[i], yawDeg, cam, camFwd, camUp);
            }
        }
    }

    void PlacePanelOnArc(WorldPanelPlus p, float yawDeg, Camera cam, Vector3 camFwd, Vector3 camUp)
    {
        if (!p) return;

        Quaternion yaw = Quaternion.AngleAxis(yawDeg, camUp);
        Vector3 dir = yaw * camFwd;
        Vector3 pos = cam.transform.position + dir * distanceFromCamera + camUp * verticalOffset;

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
            ApplyPerPanelVisuals();
        }
    }

    /// <summary>
    /// Apply curved mesh visual for true seamless appearance
    /// </summary>
    void ApplyCurvedVisual()
    {
        // Remove any per-panel visuals
        foreach (var visual in _panelVisuals)
        {
            if (visual != null)
            {
                if (Application.isPlaying)
                    Destroy(visual);
                else
                    DestroyImmediate(visual);
            }
        }
        _panelVisuals.Clear();

        // Hide individual panel boards and remove per-panel visuals
        foreach (var panel in _panels)
        {
            if (panel == null) continue;

            // Remove ClusterPanelVisual if exists
            var existingVisual = panel.GetComponent<ClusterPanelVisual>();
            if (existingVisual != null)
            {
                if (Application.isPlaying)
                    Destroy(existingVisual);
                else
                    DestroyImmediate(existingVisual);
            }

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
    }

    /// <summary>
    /// Apply per-panel visual (original approach with EdgeMask)
    /// </summary>
    void ApplyPerPanelVisuals()
    {
        // Remove curved visual if present
        if (_curvedVisual != null)
        {
            if (Application.isPlaying)
                Destroy(_curvedVisual);
            else
                DestroyImmediate(_curvedVisual);
            _curvedVisual = null;
        }

        _panelVisuals.Clear();

        int count = _panels.Count;
        for (int i = 0; i < count; i++)
        {
            var panel = _panels[i];
            if (panel == null) continue;

            // Re-enable board visibility (may have been disabled by curved visual mode)
            // Use SetVisible(true) which properly sets boardVisible AND enables renderer/collider
            panel.SetVisible(true);

            // Get or add ClusterPanelVisual component
            var visual = panel.GetComponent<ClusterPanelVisual>();
            if (visual == null)
            {
                visual = panel.gameObject.AddComponent<ClusterPanelVisual>();
            }

            // Configure position in cluster
            visual.SetClusterPosition(i, count);

            // Initialize visuals
            visual.Initialize();

            // Apply custom settings via serialized fields reflection or direct access
            ApplyVisualSettings(visual);

            _panelVisuals.Add(visual);
        }
    }

    /// <summary>
    /// Apply visual settings from rig to individual panel visual
    /// </summary>
    void ApplyVisualSettings(ClusterPanelVisual visual)
    {
        if (visual == null) return;

        visual.SetGlowColors(glowColorA, glowColorB);
        visual.SetGlassColors(glassColorA, glassColorB);
        visual.SetCornerSettings(cornerRadius, edgePadding);
        visual.SetGlowExpansion(glowExpansion);
        visual.SetJunctionOverlap(junctionOverlap);
        visual.SetContentMargins(contentMarginHorizontal, contentMarginVertical);
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
            _curvedVisual.UpdateContentTextures();
        }
        else
        {
            foreach (var visual in _panelVisuals)
            {
                if (visual != null)
                {
                    ApplyVisualSettings(visual);
                    visual.UpdateSize();
                }
            }
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

#if UNITY_EDITOR
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
#else
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
#endif
        _panels.Clear();
        _panelVisuals.Clear();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (_panels.Count > 0)
        {
            LayoutFromCamera();
        }
    }
#endif
}