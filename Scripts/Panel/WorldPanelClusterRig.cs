using UnityEngine;

[ExecuteAlways]
public class WorldPanelClusterRig : MonoBehaviour
{
    [Header("Source")]
    public WorldPanelPlus panelPrefab;
    public Texture centerTexture, leftTexture, rightTexture;

    [Header("Layout")]
    public float distanceFromCamera = 2.0f;     // R
    public float sideGapMeters = 0.12f;         // dùng khi không lấy theo Hover Extra
    public bool autoAngleFromGap = true;
    public bool gapFromTrayHoverExtra = true;   // << NEW: dùng trayHoverExtra làm khoảng cách
    [Range(0f, 45f)] public float sideYawDeg = 18f;
    public float verticalOffset = 0f;

    [Header("Visibility in cluster")]
    public bool hideTrayAndDock = true;
    public bool faceCameraYawOnly = true;

    [Header("Built panels (readonly)")]
    public WorldPanelPlus left, center, right;

    Camera Cam => Application.isPlaying ? Camera.main : FindObjectOfType<Camera>();

    [ContextMenu("Build or Rebuild Cluster")]
    public void BuildOrRebuild()
    {
        KillChildren();
        CreateThreePanels();
        LinkNeighbors();
        ApplyTextures();
        ApplyClusterVisibility(true);   // ẩn tray/dock nếu cần
        LayoutFromCamera();
    }

    [ContextMenu("Enter Cluster Mode (hide Tray & Dock)")]
    public void EnterClusterMode() { ApplyClusterVisibility(true); LayoutFromCamera(); }

    [ContextMenu("Exit Cluster Mode (show Tray & Dock)")]
    public void ExitClusterMode() { ApplyClusterVisibility(false); LayoutFromCamera(); }

    void Update()
    {
        if (!Application.isPlaying && center) LayoutFromCamera();
    }

    void LayoutFromCamera()
    {
        var cam = Cam; if (!cam || !center) return;

        // Cơ sở yaw-only
        Vector3 camFwd = cam.transform.forward;
        Vector3 camUp = Vector3.up;
        if (faceCameraYawOnly) { camFwd.y = 0f; if (camFwd.sqrMagnitude < 1e-6f) camFwd = cam.transform.forward; camFwd.Normalize(); }
        Vector3 clusterCenter = cam.transform.position + camFwd * distanceFromCamera + camUp * verticalOffset;
        transform.SetPositionAndRotation(clusterCenter, Quaternion.LookRotation(-camFwd, camUp));

        // === Góc hai bên ===
        float angleDeg;
        if (autoAngleFromGap)
        {
            // Bề rộng chỉ tính theo Board (trayPadding=0 trong cluster)
            float w = center.width;
            // Dùng khoảng cách = trayHoverExtra nếu bật tuỳ chọn, ngược lại dùng sideGapMeters
            float gap = gapFromTrayHoverExtra ? Mathf.Max(0f, center.trayHoverExtra) : Mathf.Max(0f, sideGapMeters);

            float halfA = Mathf.Rad2Deg * Mathf.Atan((w * 0.5f) / distanceFromCamera);
            float gapA = Mathf.Rad2Deg * Mathf.Atan(gap / distanceFromCamera);  // khoảng cách giữa mép = gap
            angleDeg = (halfA * 2f) + gapA;
        }
        else angleDeg = sideYawDeg;

        void PlacePanel(WorldPanelPlus p, float yawDeg)
        {
            if (!p) return;
            Quaternion yaw = Quaternion.AngleAxis(yawDeg, camUp);
            Vector3 dir = yaw * camFwd;
            Vector3 pos = cam.transform.position + dir * distanceFromCamera + camUp * verticalOffset;
            // +Z của panel hướng ra xa camera (camera nhìn vào mặt trước)
            Vector3 faceDir = (pos - cam.transform.position).normalized;
            Quaternion rot = Quaternion.LookRotation(faceDir, camUp);
            p.transform.SetPositionAndRotation(pos, rot);
            p.EnforceNoRoll();

            // Đảm bảo tham số tray khi ở trong cluster
            if (hideTrayAndDock) { p.forceTrayHidden = true; if (p.dock) p.dock.SetMinimized(true); }
            // Luôn set trayPadding = 0 trong cluster
            if (Mathf.Abs(p.trayPadding) > 1e-6f) { p.trayPadding = 0f; p.Apply(); }
        }

        // Trái/Phải đúng chiều nhìn từ camera
        PlacePanel(center, 0f);
        PlacePanel(left, -angleDeg);
        PlacePanel(right, +angleDeg);
    }

    void CreateThreePanels()
    {
        center = CreateOne("Panel_Center");
        left = CreateOne("Panel_Left");
        right = CreateOne("Panel_Right");

        // đồng bộ kích thước
        if (left) { left.width = center.width; left.height = center.height; }
        if (right) { right.width = center.width; right.height = center.height; }
        // padding = 0 trong cluster
        // if (left) left.trayPadding = 0f;
        // if (center) center.trayPadding = 0f;
        // if (right) right.trayPadding = 0f;

        if (left) left.Apply(); if (center) center.Apply(); if (right) right.Apply();
    }

    WorldPanelPlus CreateOne(string name)
    {
        WorldPanelPlus p;
        if (panelPrefab)
        {
            var go = Instantiate(panelPrefab.gameObject, transform); go.name = name;
            p = go.GetComponent<WorldPanelPlus>();
        }
        else
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            p = go.AddComponent<WorldPanelPlus>(); p.Rebuild();
        }
        // Ẩn Tray/Dock khi ở cluster
        p.forceTrayHidden = hideTrayAndDock;
        p.dockMinimized = hideTrayAndDock;
        p.Apply();
        p.SetHandlesCenterOnly(hideTrayAndDock);
        return p;
    }

    void LinkNeighbors()
    {
        if (!center) return;
        if (left) { center.neighborLeft = left; left.neighborRight = center; }
        if (right) { center.neighborRight = right; right.neighborLeft = center; }
    }

    void ApplyTextures()
    {
        if (center && centerTexture) center.contentTexture = centerTexture;
        if (left && leftTexture) left.contentTexture = leftTexture;
        if (right && rightTexture) right.contentTexture = rightTexture;
        if (left) left.Apply(); if (center) center.Apply(); if (right) right.Apply();
    }

    void ApplyClusterVisibility(bool on)
    {
        hideTrayAndDock = on;
        void SetOne(WorldPanelPlus p)
        {
            if (!p) return;
            p.forceTrayHidden = on;
            if (p.dock) p.dock.SetMinimized(on);
            // padding = 0 trong cluster
            if (on) p.trayPadding = 0f;
            p.Apply();
            p.SetHandlesCenterOnly(on);
        }
        SetOne(left); SetOne(center); SetOne(right);
    }

    void KillChildren()
    {
#if UNITY_EDITOR
        for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
#else
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
#endif
        left = center = right = null;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (center) { ApplyClusterVisibility(hideTrayAndDock); LayoutFromCamera(); }
    }
#endif
}
