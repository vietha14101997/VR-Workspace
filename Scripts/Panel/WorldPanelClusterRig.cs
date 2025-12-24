using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class WorldPanelClusterRig : MonoBehaviour
{
    [Header("Source")]
    public WorldPanelPlus panelPrefab;
    public Texture centerTexture, leftTexture, rightTexture;

    [Header("Layout")]
    public float distanceFromCamera = 2.0f;
    public float sideGapMeters = 0.05f;
    public bool autoAngleFromGap = true;
    [Range(0f, 45f)] public float sideYawDeg = 18f;
    public float verticalOffset = 0f;

    [Header("Angle Adjustment")]
    [Tooltip("Extra gap in meters between panel edges (0 = edges touch)")]
    [Range(0f, 0.1f)] public float edgeGapMeters = 0.01f;
    [Tooltip("Whether panels should face directly toward camera (true) or have limited tilt (false)")]
    public bool panelsFaceCamera = true;

    [Header("Visibility")]
    public bool faceCameraYawOnly = true;

    [Header("Glowing Border")]
    public bool enableGlowBorder = true;
    [ColorUsage(true, true)]
    public Color glowColorA = new Color(0.3f, 1f, 1f, 1f);
    [ColorUsage(true, true)]
    public Color glowColorB = new Color(1f, 0.4f, 1f, 1f);
    [Range(0.005f, 0.05f)]
    public float borderWidth = 0.015f;
    [Range(0.01f, 0.1f)]
    public float glowSpread = 0.05f;
    [Range(0f, 0.1f)]
    public float cornerRadius = 0.03f;

    [SerializeField, HideInInspector]
    private ClusterGlowBorder _glowBorder;

    [Header("Dynamic Panels")]
    [SerializeField] private List<WorldPanelPlus> _panels = new List<WorldPanelPlus>();
    public List<WorldPanelPlus> panels => _panels;

    // Legacy compatibility - map to panels list
    public WorldPanelPlus left => _panels.Count >= 2 ? _panels[0] : null;
    public WorldPanelPlus center => _panels.Count >= 1 ? _panels[_panels.Count / 2] : null;
    public WorldPanelPlus right => _panels.Count >= 3 ? _panels[_panels.Count - 1] : null;

    Camera Cam => Application.isPlaying ? Camera.main : FindObjectOfType<Camera>();

    /// <summary>
    /// Build cluster with specified number of panels (called by ClusterAutoBinder)
    /// </summary>
    public void BuildWithPanelCount(int count)
    {
        if (count < 1) count = 1;
        if (count > 6) count = 6;

        KillChildren();
        CreatePanels(count);
        LinkNeighbors();
        LayoutFromCamera();

        EnsureGlowBorder();
        UpdateGlowBorder();

        Debug.Log($"[WorldPanelClusterRig] Built {count} panels");
    }

    [ContextMenu("Build or Rebuild Cluster (3 panels)")]
    public void BuildOrRebuild()
    {
        BuildWithPanelCount(3);
        ApplyTextures();
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

        float angleDeg = autoAngleFromGap ? (panelAngleDeg + gapAngleDeg) : sideYawDeg;

        int count = _panels.Count;
        for (int i = 0; i < count; i++)
        {
            float offset = i - (count - 1) / 2f;
            float yawDeg = offset * angleDeg;
            PlacePanelOnArc(_panels[i], yawDeg, cam, camFwd, camUp);
        }

        // Update glow border after panels are positioned
        UpdateGlowBorder();
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
        }
    }

    void ApplyTextures()
    {
        // Legacy 3-panel texture support
        if (_panels.Count >= 1 && centerTexture)
            _panels[_panels.Count / 2].contentTexture = centerTexture;
        if (_panels.Count >= 2 && leftTexture)
            _panels[0].contentTexture = leftTexture;
        if (_panels.Count >= 3 && rightTexture)
            _panels[_panels.Count - 1].contentTexture = rightTexture;

        foreach (var p in _panels)
            p.Apply();
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
        _glowBorder = null;
    }

    void EnsureGlowBorder()
    {
        if (!enableGlowBorder)
        {
            if (_glowBorder != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(_glowBorder.gameObject);
#else
                Destroy(_glowBorder.gameObject);
#endif
                _glowBorder = null;
            }
            return;
        }

        if (_glowBorder == null)
        {
            // Check if one already exists as child
            _glowBorder = GetComponentInChildren<ClusterGlowBorder>();

            if (_glowBorder == null)
            {
                var go = new GameObject("GlowBorder");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                _glowBorder = go.AddComponent<ClusterGlowBorder>();
            }
        }

        // Sync properties
        _glowBorder.colorA = glowColorA;
        _glowBorder.colorB = glowColorB;
        _glowBorder.borderWidth = borderWidth;
        _glowBorder.glowSpread = glowSpread;
        _glowBorder.cornerRadius = cornerRadius;
    }

    void UpdateGlowBorder()
    {
        if (!enableGlowBorder || _glowBorder == null) return;

        // Sync properties in case they changed
        _glowBorder.colorA = glowColorA;
        _glowBorder.colorB = glowColorB;
        _glowBorder.borderWidth = borderWidth;
        _glowBorder.glowSpread = glowSpread;
        _glowBorder.cornerRadius = cornerRadius;

        // Update the border mesh
        _glowBorder.UpdateBorder(_panels);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (_panels.Count > 0)
        {
            LayoutFromCamera();
            // Ensure glow border is created/destroyed based on toggle
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    EnsureGlowBorder();
                    UpdateGlowBorder();
                }
            };
        }
    }
#endif
}
