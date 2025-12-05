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
    public float sideGapMeters = 0.05f;  // Reduced gap between panels
    public bool autoAngleFromGap = true;
    public bool gapFromTrayHoverExtra = true;  // Use sideGapMeters instead
    [Range(0f, 45f)] public float sideYawDeg = 18f;
    public float verticalOffset = 0f;

    [Header("Angle Adjustment")]
    [Tooltip("Extra angle multiplier for fewer panels (1-2 panels get wider spread)")]
    [Range(1f, 3f)] public float fewPanelsAngleMultiplier = 1.0f;
    [Tooltip("Minimum angle between panels in degrees")]
    [Range(0f, 30f)] public float minAngleDeg = 0f;  // No minimum, use calculated angle
    [Tooltip("Max inward tilt of side panels (0=flat facing forward, 90=face camera directly)")]
    [Range(0f, 45f)] public float maxPanelTiltDeg = 10f;

    [Header("Visibility in cluster")]
    public bool hideTrayAndDock = true;
    public bool faceCameraYawOnly = true;

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
        ApplyClusterVisibility(true);
        LayoutFromCamera();

        Debug.Log($"[WorldPanelClusterRig] Built {count} panels");
    }

    [ContextMenu("Build or Rebuild Cluster (3 panels)")]
    public void BuildOrRebuild()
    {
        BuildWithPanelCount(3);
        ApplyTextures();
    }

    [ContextMenu("Enter Cluster Mode (hide Tray & Dock)")]
    public void EnterClusterMode() { ApplyClusterVisibility(true); LayoutFromCamera(); }

    [ContextMenu("Exit Cluster Mode (show Tray & Dock)")]
    public void ExitClusterMode() { ApplyClusterVisibility(false); LayoutFromCamera(); }

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
        transform.SetPositionAndRotation(clusterCenter, Quaternion.LookRotation(-camFwd, camUp));

        // Calculate angle between panels
        float angleDeg;
        var refPanel = _panels[_panels.Count / 2]; // center panel for reference
        if (autoAngleFromGap && refPanel)
        {
            float w = refPanel.width;
            float gap = gapFromTrayHoverExtra ? Mathf.Max(0f, refPanel.trayHoverExtra) : Mathf.Max(0f, sideGapMeters);
            float halfA = Mathf.Rad2Deg * Mathf.Atan((w * 0.5f) / distanceFromCamera);
            float gapA = Mathf.Rad2Deg * Mathf.Atan(gap / distanceFromCamera);
            angleDeg = (halfA * 2f) + gapA;
        }
        else
        {
            angleDeg = sideYawDeg;
        }

        // Apply minimum angle and multiplier for few panels
        angleDeg = Mathf.Max(angleDeg, minAngleDeg);
        if (_panels.Count <= 2)
        {
            angleDeg *= fewPanelsAngleMultiplier;
        }

        // Place panels symmetrically around center view (0 degrees)
        // Odd count: center panel at 0°, others spread evenly
        // Even count: panels straddle 0° symmetrically
        int count = _panels.Count;
        for (int i = 0; i < count; i++)
        {
            // Offset from center: for count=3 → [-1, 0, +1], for count=4 → [-1.5, -0.5, +0.5, +1.5]
            float offset = i - (count - 1) / 2f;
            float yawDeg = offset * angleDeg;
            PlacePanel(_panels[i], yawDeg, cam, camFwd, camUp);
        }
    }

    void PlacePanel(WorldPanelPlus p, float yawDeg, Camera cam, Vector3 camFwd, Vector3 camUp)
    {
        if (!p) return;

        Quaternion yaw = Quaternion.AngleAxis(yawDeg, camUp);
        Vector3 dir = yaw * camFwd;
        Vector3 pos = cam.transform.position + dir * distanceFromCamera + camUp * verticalOffset;

        // Limit panel tilt: instead of facing camera directly, only tilt by maxPanelTiltDeg
        // yawDeg is panel position angle, tiltDeg is how much panel rotates inward
        float tiltDeg = Mathf.Sign(yawDeg) * Mathf.Min(Mathf.Abs(yawDeg), maxPanelTiltDeg);
        Quaternion tiltRot = Quaternion.AngleAxis(tiltDeg, camUp);
        Vector3 faceDir = tiltRot * camFwd;

        Quaternion rot = Quaternion.LookRotation(faceDir, camUp);
        p.transform.SetPositionAndRotation(pos, rot);
        p.EnforceNoRoll();

        if (hideTrayAndDock)
        {
            p.forceTrayHidden = true;
            p.dockHardHidden = true;
            if (p.dock) p.dock.SetMinimized(true);
        }
        if (Mathf.Abs(p.trayPadding) > 1e-6f)
        {
            p.trayPadding = 0f;
            p.Apply();
        }
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

        p.forceTrayHidden = hideTrayAndDock;
        p.dockMinimized = hideTrayAndDock;
        p.dockHardHidden = hideTrayAndDock;
        p.Apply();
        p.SetHandlesCenterOnly(hideTrayAndDock);
        return p;
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

    void ApplyClusterVisibility(bool on)
    {
        hideTrayAndDock = on;
        foreach (var p in _panels)
        {
            if (!p) continue;
            p.forceTrayHidden = on;
            p.dockHardHidden = on;
            if (p.dock) p.dock.SetMinimized(on);
            if (on) p.trayPadding = 0f;
            p.Apply();
            p.SetHandlesCenterOnly(on);
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
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (_panels.Count > 0)
        {
            ApplyClusterVisibility(hideTrayAndDock);
            LayoutFromCamera();
        }
    }
#endif
}
