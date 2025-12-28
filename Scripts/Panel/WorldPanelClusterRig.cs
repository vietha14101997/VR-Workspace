using UnityEngine;
using System.Collections.Generic;

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
    public float distanceFromCamera = 2.0f;
    [Tooltip("Extra gap in meters between panel edges (0 = edges touch)")]
    [Range(0f, 0.1f)] public float edgeGapMeters = 0f;
    [Tooltip("Whether panels should face directly toward camera (true) or have limited tilt (false)")]
    public bool panelsFaceCamera = true;
    [Tooltip("How much panels tilt toward camera (0 = flat/parallel, 1 = fully facing camera)")]
    [Range(0f, 1f)] public float panelTiltFactor = 1f;
    public float verticalOffset = 0f;
    public bool faceCameraYawOnly = true;

    [Header("Cluster Visuals")]
    [Tooltip("Enable seamless glass background and glowing border across all panels")]
    public bool enableClusterVisuals = true;

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
    public void BuildWithPanelCount(int count)
    {
        if (count < 1) count = 1;
        if (count > 6) count = 6;

        KillChildren();
        CreatePanels(count);
        LinkNeighbors();
        LayoutFromCamera();

        if (enableClusterVisuals)
        {
            ApplyClusterVisuals();
        }

        Debug.Log($"[WorldPanelClusterRig] Built {count} panels" +
            (enableClusterVisuals ? " with cluster visuals" : ""));
    }

    [ContextMenu("Build or Rebuild Cluster (3 panels)")]
    public void BuildOrRebuild()
    {
        BuildWithPanelCount(3);
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

        // Calculate base angle for rotation (used for tilt calculation)
        float boardAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(boardWidth / 2f / distanceFromCamera);
        float gapAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(edgeGapMeters / 2f / distanceFromCamera);
        float angleDeg = boardAngleDeg + gapAngleDeg;

        int count = _panels.Count;

        // Calculate rotation for each panel
        float[] rotYawDegs = new float[count];
        for (int i = 0; i < count; i++)
        {
            float offset = i - (count - 1) / 2f;
            float yawDeg = offset * angleDeg;
            rotYawDegs[i] = panelsFaceCamera ? yawDeg * panelTiltFactor : 0f;
        }

        // Desired center position (should stay fixed)
        Vector3 desiredCenter = cam.transform.position + camFwd * distanceFromCamera + camUp * verticalOffset;

        // Position panels using pair-based approach
        if (count % 2 == 1)
        {
            // Odd count: center panel at index count/2
            int centerIdx = count / 2;
            PositionPanelAtCenter(_panels[centerIdx], rotYawDegs[centerIdx], cam, camFwd, camUp);

            // Work outward to the left
            for (int i = centerIdx - 1; i >= 0; i--)
                PositionPanelLeftOf(_panels[i], _panels[i + 1], rotYawDegs[i], rotYawDegs[i + 1], boardWidth, camFwd, camUp);

            // Work outward to the right
            for (int i = centerIdx + 1; i < count; i++)
                PositionPanelRightOf(_panels[i], _panels[i - 1], rotYawDegs[i], rotYawDegs[i - 1], boardWidth, camFwd, camUp);
        }
        else
        {
            // Even count: center pair at indices count/2-1 and count/2
            int leftCenter = count / 2 - 1;
            int rightCenter = count / 2;
            PositionCenterPair(_panels[leftCenter], _panels[rightCenter],
                rotYawDegs[leftCenter], rotYawDegs[rightCenter], boardWidth, cam, camFwd, camUp);

            // Work outward to the left
            for (int i = leftCenter - 1; i >= 0; i--)
                PositionPanelLeftOf(_panels[i], _panels[i + 1], rotYawDegs[i], rotYawDegs[i + 1], boardWidth, camFwd, camUp);

            // Work outward to the right
            for (int i = rightCenter + 1; i < count; i++)
                PositionPanelRightOf(_panels[i], _panels[i - 1], rotYawDegs[i], rotYawDegs[i - 1], boardWidth, camFwd, camUp);
        }

        // Calculate actual center and apply correction to keep cluster centered
        // Only correct perpendicular to camFwd (don't move cluster forward/backward)
        Vector3 actualCenter = Vector3.zero;
        foreach (var p in _panels)
            actualCenter += p.transform.position;
        actualCenter /= count;

        Vector3 correction = desiredCenter - actualCenter;
        // Remove the component along camFwd to prevent forward/backward drift
        correction -= Vector3.Dot(correction, camFwd) * camFwd;
        foreach (var p in _panels)
            p.transform.position += correction;
    }

    Vector3 GetPanelRightVector(float rotYawDeg, Vector3 camFwd, Vector3 camUp)
    {
        // Panel faces AWAY from camera, so its forward is -camFwd rotated by rotYawDeg
        Quaternion rot = Quaternion.AngleAxis(rotYawDeg, camUp);
        Vector3 panelForward = rot * camFwd;  // direction panel is looking (away from camera)
        // Right vector: Cross(up, forward) in Unity's left-handed system
        return Vector3.Cross(camUp, panelForward).normalized;
    }

    Quaternion GetPanelRotation(float rotYawDeg, Vector3 camFwd, Vector3 camUp)
    {
        // Panel faces AWAY from camera (toward the user viewing the panel)
        Quaternion yawRot = Quaternion.AngleAxis(rotYawDeg, camUp);
        Vector3 panelForward = yawRot * camFwd;
        return Quaternion.LookRotation(panelForward, camUp);
    }

    void PositionPanelAtCenter(WorldPanelPlus p, float rotYawDeg, Camera cam, Vector3 camFwd, Vector3 camUp)
    {
        if (!p) return;

        Vector3 pos = cam.transform.position + camFwd * distanceFromCamera + camUp * verticalOffset;
        Quaternion rot = GetPanelRotation(rotYawDeg, camFwd, camUp);
        p.transform.SetPositionAndRotation(pos, rot);
    }

    void PositionCenterPair(WorldPanelPlus leftPanel, WorldPanelPlus rightPanel,
        float leftRotYawDeg, float rightRotYawDeg, float boardWidth, Camera cam, Vector3 camFwd, Vector3 camUp)
    {
        if (!leftPanel || !rightPanel) return;

        // Junction point at center
        Vector3 junction = cam.transform.position + camFwd * distanceFromCamera + camUp * verticalOffset;

        // Left panel: right edge at junction
        Vector3 leftRight = GetPanelRightVector(leftRotYawDeg, camFwd, camUp);
        Vector3 leftPos = junction - (boardWidth / 2f) * leftRight;
        leftPanel.transform.SetPositionAndRotation(leftPos, GetPanelRotation(leftRotYawDeg, camFwd, camUp));

        // Right panel: left edge at junction
        Vector3 rightRight = GetPanelRightVector(rightRotYawDeg, camFwd, camUp);
        Vector3 rightPos = junction + (boardWidth / 2f) * rightRight;
        rightPanel.transform.SetPositionAndRotation(rightPos, GetPanelRotation(rightRotYawDeg, camFwd, camUp));
    }

    void PositionPanelLeftOf(WorldPanelPlus panel, WorldPanelPlus neighbor,
        float panelRotYawDeg, float neighborRotYawDeg, float boardWidth, Vector3 camFwd, Vector3 camUp)
    {
        if (!panel || !neighbor) return;

        // Get neighbor's left edge position
        Vector3 neighborRight = GetPanelRightVector(neighborRotYawDeg, camFwd, camUp);
        Vector3 neighborLeftEdge = neighbor.transform.position - (boardWidth / 2f) * neighborRight;

        // This panel's right edge should be at neighborLeftEdge
        Vector3 panelRight = GetPanelRightVector(panelRotYawDeg, camFwd, camUp);
        Vector3 pos = neighborLeftEdge - (boardWidth / 2f) * panelRight;

        panel.transform.SetPositionAndRotation(pos, GetPanelRotation(panelRotYawDeg, camFwd, camUp));
    }

    void PositionPanelRightOf(WorldPanelPlus panel, WorldPanelPlus neighbor,
        float panelRotYawDeg, float neighborRotYawDeg, float boardWidth, Vector3 camFwd, Vector3 camUp)
    {
        if (!panel || !neighbor) return;

        // Get neighbor's right edge position
        Vector3 neighborRight = GetPanelRightVector(neighborRotYawDeg, camFwd, camUp);
        Vector3 neighborRightEdge = neighbor.transform.position + (boardWidth / 2f) * neighborRight;

        // This panel's left edge should be at neighborRightEdge
        Vector3 panelRight = GetPanelRightVector(panelRotYawDeg, camFwd, camUp);
        Vector3 pos = neighborRightEdge + (boardWidth / 2f) * panelRight;

        panel.transform.SetPositionAndRotation(pos, GetPanelRotation(panelRotYawDeg, camFwd, camUp));
    }

    void CreatePanels(int count)
    {
        _panels.Clear();

        for (int i = 0; i < count; i++)
        {
            string name = $"Panel_{i}";
            var p = CreateOne(name);
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
        SetLayerRecursively(p.gameObject, gameObject.layer);

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
        _panelVisuals.Clear();

        int count = _panels.Count;
        for (int i = 0; i < count; i++)
        {
            var panel = _panels[i];
            if (panel == null) continue;

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

        foreach (var visual in _panelVisuals)
        {
            if (visual != null)
            {
                ApplyVisualSettings(visual);
                visual.UpdateSize();
            }
        }
    }

    void KillChildren()
    {
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