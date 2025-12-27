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

        float panelAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(panelWidth / 2f / distanceFromCamera);
        float gapAngleDeg = 2f * Mathf.Rad2Deg * Mathf.Atan(edgeGapMeters / 2f / distanceFromCamera);

        float angleDeg = panelAngleDeg + gapAngleDeg;

        int count = _panels.Count;
        for (int i = 0; i < count; i++)
        {
            float offset = i - (count - 1) / 2f;
            float yawDeg = offset * angleDeg;
            PlacePanelOnArc(_panels[i], yawDeg, cam, camFwd, camUp);
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
