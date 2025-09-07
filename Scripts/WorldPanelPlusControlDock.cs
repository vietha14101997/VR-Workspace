using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Transform))]
public class WorldPanelPlusControlDock : MonoBehaviour
{
    [Header("Screen-space Y lock")]
    public bool lockScreenYToTray = true;        // BẬT tính năng cố định khoảng cách Y theo camera
    [Range(0f, 0.5f)] public float dockViewportYOffset = 0.06f; // khoảng cách Y (đơn vị viewport)
    public enum DockYRef { TrayBottom, TrayCenter }
    public DockYRef yReference = DockYRef.TrayBottom;

    [Header("Bind")]
    public WorldPanelPlus panel;

    [Header("Freeze")]
    public bool freezeWorldRotation = true;    // KHÔNG xoay theo Tray/camera
    Quaternion _frozenWorldRot;

    [Header("Button layout")]
    public Vector2 buttonSize = new Vector2(0.08f, 0.045f);
    public float buttonGap = 0.02f;

    [Header("Visuals")]
    public Material dockMat;                   // nên dùng chung style với Tray
    public Material iconMat;                   // Unlit/Texture
    public Material iconWhiteMat;              // Unlit/Color trắng
    public float cornerRadius = 0.06f;
    public float border = 0.004f;
    public Color buttonBg = new Color(0f, 0f, 0f, 0.35f);

    [Header("Icons")]
    public Texture2D moveIconTexture;          // gán PNG cho Move nếu muốn

    Transform _backplate;
    readonly List<WorldPanelPlusDockButton> _buttons = new();
    public bool keepYawRelativeToCameraInMove = true;
    float _yawOffsetToCam = 0f;     // yawDock - yawCamera tại thời điểm khởi tạo (hoặc khi vào move)
    float _zLift = 0.01f;
    Vector3 _offsetLocalNoZ;        // offset chỉ theo Y (không có Z)
                                    // Giữ khoảng cách Y giữa đáy Tray và đỉnh Dock (đơn vị: world Y)
    float _targetGapY = 0f;    // sẽ chụp khi Build/Recompute
    bool _minimized = false;

    public void SetMinimized(bool on)
    {
        _minimized = on;

        // Ẩn/Hiện các nút (trừ nút Toggle)
        foreach (var b in _buttons)
        {
            bool isToggle = (b.type == WPDockButtonType.MinimizeToggle);
            b.gameObject.SetActive(isToggle || !on);
        }

        // Backplate: co lại = đúng kích thước 1 nút
        if (_backplate)
        {
            var size = on ? new Vector3(buttonSize.x + 0.04f, buttonSize.y + 0.04f, 1)
                          : new Vector3(_buttons.Count * buttonSize.x + (_buttons.Count - 1) * buttonGap + 0.04f,
                                        buttonSize.y + 0.04f, 1);
            _backplate.localScale = size;
        }

        // Căn giữa 1 nút toggle
        LayoutButtons();

        // Khi minimize, Dock vẫn hiển thị dù Tray ẩn → không động vào alpha ở đây
    }

    void LayoutButtons()
    {
        // Đặt các nút theo _minimized
        var list = _buttons;
        int count = 0;
        foreach (var b in list) if (b.gameObject.activeSelf) count++;

        float totalW = count * buttonSize.x + (count - 1) * buttonGap;
        float x0 = -totalW * 0.5f + buttonSize.x * 0.5f;

        int k = 0;
        foreach (var b in list)
        {
            if (!b.gameObject.activeSelf) continue;
            b.transform.localPosition = new Vector3(x0 + k * (buttonSize.x + buttonGap), 0, 0.01f);
            k++;
        }
    }

    public void SetAxisLock(WorldPanelPlus.WPAxisLock lockState)
    {
        // Khoá các NÚT của trục còn lại
        foreach (var b in _buttons)
        {
            bool isYawBtn = (b.type == WPDockButtonType.YawLeft15 || b.type == WPDockButtonType.YawRight15);
            bool isPitchBtn = (b.type == WPDockButtonType.PitchUp15 || b.type == WPDockButtonType.PitchDown15);

            bool enable = true;
            if (lockState == WorldPanelPlus.WPAxisLock.YawOnly) enable = !isPitchBtn;
            if (lockState == WorldPanelPlus.WPAxisLock.PitchOnly) enable = !isYawBtn;

            var col = b.GetComponent<BoxCollider>(); if (col) col.enabled = enable;
            // làm mờ icon bg khi khóa (tùy chọn)
            var bg = b.transform.Find("BG");
            var mr = bg ? bg.GetComponent<MeshRenderer>() : null;
            if (mr && mr.sharedMaterial.HasProperty("_FillColor"))
            {
                Color c = mr.sharedMaterial.GetColor("_FillColor");
                c.a = enable ? 0.35f : 0.12f; mr.sharedMaterial.SetColor("_FillColor", c);
            }
        }
    }

    float GetDockTopYWorldAt(Vector3 worldPos)
    {
        // Tính topY của backplate nếu Dock đặt tại worldPos và rotation hiện tại
        if (_backplate == null) return worldPos.y;
        float half = _backplate.localScale.y * 0.5f;
        // điểm top trong local của Dock là (0,+half,0)
        return (worldPos + transform.rotation * new Vector3(0, half, 0)).y;
    }

    float MeasureCurrentGapY()
    {
        var cam = Camera.main;
        float trayBottom = panel ? panel.GetTrayBottomYWorld(cam) : transform.position.y;
        float dockTop = GetDockTopYWorldAt(transform.position);
        return trayBottom - dockTop;
    }

    // Căn Dock nằm giữa theo trục X, giữ đúng khoảng cách Y và z-lift
    public void CenterXUnderTray()
    {
        if (!panel) return;
        var cam = Camera.main;

        // 1) ép offset local theo X về 0 (nằm giữa Tray)
        _offsetLocalNoZ.x = 0f;

        // 2) vị trí cơ bản + z-lift theo hướng camera
        Vector3 baseWorld = panel.transform.TransformPoint(_offsetLocalNoZ);
        Vector3 viewDir = cam ? (panel.transform.position - cam.transform.position).normalized : -panel.transform.forward;
        Vector3 zOffset = -viewDir * _zLift;
        Vector3 pos = baseWorld + zOffset;

        // 3) giữ khoảng cách Y (đáy Tray – đỉnh Dock) = _targetGapY như hiện tại
        float trayBottomY = panel.GetTrayBottomYWorld(cam);
        float dockTopY = GetDockTopYWorldAt(pos);
        float deltaY = (trayBottomY - dockTopY) - _targetGapY;
        pos.y += deltaY;

        transform.position = pos;
    }

    public void CaptureYawOffsetToCamera()
    {
        var cam = Camera.main; if (!cam) return;
        _yawOffsetToCam = transform.eulerAngles.y - cam.transform.eulerAngles.y;
    }

    void Reset() { panel = GetComponentInParent<WorldPanelPlus>(); }

    void Awake()
    {
        if (!panel) panel = GetComponentInParent<WorldPanelPlus>();
    }

    void Start()
    {
        // Nếu chưa Build (ví dụ scene có sẵn Dock), tự Build bằng material của Tray
        if (_backplate == null)
        {
            var trayMat = panel ? panel.GetTrayMaterial() : null;
            Build(trayMat);
        }
        // Đặt đúng chỗ ngay khi vào Play
        RecomputeFromPanel();
    }
    void OnEnable() { PlaceDockImmediate(); }
    void OnTransformParentChanged() { PlaceDockImmediate(); }
    void OnValidate() { if (panel) RecomputeFromPanel(); }

    public void Build(Material trayMatLike)
    {
        dockMat = trayMatLike;
        if (iconMat == null) { iconMat = new Material(Shader.Find("Unlit/Texture")); iconMat.color = Color.white; }
        if (iconWhiteMat == null) { iconWhiteMat = new Material(Shader.Find("Unlit/Color")); iconWhiteMat.color = Color.white; }

        // Backplate
        _backplate = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
        _backplate.name = "DockBackplate";
        _backplate.SetParent(transform, false);
        var col = _backplate.GetComponent<Collider>(); if (col) DestroyImmediate(col);

        // Buttons
        var types = new WPDockButtonType[] {
            WPDockButtonType.MoveMode, WPDockButtonType.YawLeft15, WPDockButtonType.YawRight15,
            WPDockButtonType.MinimizeToggle,
            WPDockButtonType.PitchUp15, WPDockButtonType.PitchDown15, WPDockButtonType.ResetFaceCamera
        };

        float totalW = types.Length * buttonSize.x + (types.Length - 1) * buttonGap;
        float x0 = -totalW * 0.5f + buttonSize.x * 0.5f;

        for (int i = 0; i < types.Length; i++)
        {
            var t = new GameObject("Btn_" + types[i]);
            t.transform.SetParent(transform, false);
            t.transform.localPosition = new Vector3(x0 + i * (buttonSize.x + buttonGap), 0, 0.01f);

            var btn = t.AddComponent<WorldPanelPlusDockButton>();
            btn.panel = panel; btn.type = types[i];

            // Dùng collider sẵn có (tránh bị 2 cái)
            var bc = t.GetComponent<BoxCollider>(); if (bc == null) bc = t.AddComponent<BoxCollider>();
            bc.size = new Vector3(buttonSize.x, buttonSize.y, 0.02f);
            bc.center = new Vector3(0, 0, 0.01f);

            // BG
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "BG";
            bg.transform.SetParent(t.transform, false);
            bg.transform.localScale = new Vector3(buttonSize.x, buttonSize.y, 1);
            var bgCol = bg.GetComponent<Collider>(); if (bgCol) DestroyImmediate(bgCol);
            var bgMr = bg.GetComponent<MeshRenderer>();
            bgMr.sharedMaterial = new Material(dockMat);

            // TẮT MASK nếu có
            if (bgMr.sharedMaterial.HasProperty("_MaskEnable"))
                bgMr.sharedMaterial.SetFloat("_MaskEnable", 0f);

            if (bgMr.sharedMaterial.HasProperty("_FillColor"))
            {
                bgMr.sharedMaterial.SetColor("_FillColor", buttonBg);
                bgMr.sharedMaterial.SetColor("_BorderColor", new Color(1, 1, 1, 0.2f));
                if (bgMr.sharedMaterial.HasProperty("_Radius")) bgMr.sharedMaterial.SetFloat("_Radius", cornerRadius * 0.6f);
                if (bgMr.sharedMaterial.HasProperty("_Border")) bgMr.sharedMaterial.SetFloat("_Border", border * 0.6f);
            }
            else
            {
                bgMr.sharedMaterial.shader = Shader.Find("Unlit/Transparent");
                bgMr.sharedMaterial.color = buttonBg;
            }

            // Icon
            var icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
            icon.name = "Icon";
            icon.transform.SetParent(t.transform, false);
            icon.transform.localScale = new Vector3(buttonSize.x * 0.55f, buttonSize.y * 0.55f, 1);
            var iconCol = icon.GetComponent<Collider>(); if (iconCol) DestroyImmediate(iconCol);
            var iconMr = icon.GetComponent<MeshRenderer>();

            if (btn.type == WPDockButtonType.MoveMode && moveIconTexture != null)
            {
                iconMr.sharedMaterial = new Material(iconMat);
                iconMr.sharedMaterial.mainTexture = moveIconTexture;
            }
            else
            {
                iconMr.enabled = false;
                BuildProceduralIcon(btn.type, t.transform);
            }

            _buttons.Add(btn);
        }
        LayoutButtons();

        // Backplate size
        _backplate.localScale = new Vector3(totalW + 0.04f, buttonSize.y + 0.04f, 1);
        var mr = _backplate.GetComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(dockMat);

        // BẮT BUỘC: tắt mask cho shader rounded (nếu có)
        if (mr.sharedMaterial.HasProperty("_MaskEnable"))
            mr.sharedMaterial.SetFloat("_MaskEnable", 0f);

        // fallback nếu shader rounded không có (GPU cũ)
        if (!mr.sharedMaterial.HasProperty("_FillColor"))
        {
            mr.sharedMaterial.shader = Shader.Find("Unlit/Transparent");
            mr.sharedMaterial.color = new Color(0, 0, 0, 0.18f);
        }
        else
        {
            mr.sharedMaterial.SetColor("_FillColor", new Color(0, 0, 0, 0.18f));
            mr.sharedMaterial.SetColor("_BorderColor", new Color(1, 1, 1, 0.18f));
            if (mr.sharedMaterial.HasProperty("_Radius")) mr.sharedMaterial.SetFloat("_Radius", cornerRadius);
            if (mr.sharedMaterial.HasProperty("_Border")) mr.sharedMaterial.SetFloat("_Border", border);
            if (mr.sharedMaterial.HasProperty("_Feather")) mr.sharedMaterial.SetFloat("_Feather", 0.003f);
        }

        _frozenWorldRot = transform.rotation;
        ComputeLocalOffset();
        AnchorToWorld(transform.position, Camera.main);
        EnsurePose();
        PlaceDockImmediate();
        _targetGapY = MeasureCurrentGapY();
    }

    void BuildProceduralIcon(WPDockButtonType type, Transform parent)
    {
        if (iconWhiteMat == null) { iconWhiteMat = new Material(Shader.Find("Unlit/Color")); iconWhiteMat.color = Color.white; }

        switch (type)
        {
            case WPDockButtonType.YawLeft15: BuildArrow(parent, Vector3.left); break;
            case WPDockButtonType.YawRight15: BuildArrow(parent, Vector3.right); break;
            case WPDockButtonType.PitchUp15: BuildArrow(parent, Vector3.up); break;
            case WPDockButtonType.PitchDown15: BuildArrow(parent, Vector3.down); break;
            case WPDockButtonType.ResetFaceCamera: BuildRefresh(parent); break;
        }
    }

    void BuildArrow(Transform parent, Vector3 dir)
    {
        float shaftLen = buttonSize.x * 0.38f;
        float shaftThick = buttonSize.y * 0.10f;
        float headLen = buttonSize.x * 0.18f;
        float headThick = shaftThick * 1.5f;

        var shaft = GameObject.CreatePrimitive(PrimitiveType.Quad);
        shaft.name = "Icon_Shaft";
        shaft.transform.SetParent(parent, false);
        shaft.GetComponent<MeshRenderer>().sharedMaterial = iconWhiteMat;
        Vector3 sScale = new Vector3(shaftLen, shaftThick, 1);
        float rot = (dir == Vector3.left || dir == Vector3.right) ? 0f : 90f;
        if (rot == 90f) sScale = new Vector3(shaftThick, shaftLen, 1);
        shaft.transform.localEulerAngles = new Vector3(0, 0, rot);
        shaft.transform.localScale = sScale;

        float sign = (dir == Vector3.right || dir == Vector3.up) ? 1f : -1f;
        float ang = (dir == Vector3.left || dir == Vector3.right) ? 0f : 90f;

        var head1 = GameObject.CreatePrimitive(PrimitiveType.Quad);
        head1.name = "Icon_Head1";
        head1.transform.SetParent(parent, false);
        head1.GetComponent<MeshRenderer>().sharedMaterial = iconWhiteMat;
        head1.transform.localScale = new Vector3(headLen, headThick, 1);
        head1.transform.localPosition = (dir == Vector3.left || dir == Vector3.right)
            ? new Vector3(sign * (shaftLen * 0.5f + headLen * 0.15f), headThick * 0.5f, 0)
            : new Vector3(headThick * 0.5f, sign * (shaftLen * 0.5f + headLen * 0.15f), 0);
        head1.transform.localEulerAngles = new Vector3(0, 0, ang + sign * -35f);

        var head2 = GameObject.CreatePrimitive(PrimitiveType.Quad);
        head2.name = "Icon_Head2";
        head2.transform.SetParent(parent, false);
        head2.GetComponent<MeshRenderer>().sharedMaterial = iconWhiteMat;
        head2.transform.localScale = new Vector3(headLen, headThick, 1);
        head2.transform.localPosition = (dir == Vector3.left || dir == Vector3.right)
            ? new Vector3(sign * (shaftLen * 0.5f + headLen * 0.15f), -headThick * 0.5f, 0)
            : new Vector3(-headThick * 0.5f, sign * (shaftLen * 0.5f + headLen * 0.15f), 0);
        head2.transform.localEulerAngles = new Vector3(0, 0, ang + sign * 35f);
    }

    void BuildRefresh(Transform parent)
    {
        int segments = 5;
        float radius = Mathf.Min(buttonSize.x, buttonSize.y) * 0.28f;
        float thickness = radius * 0.35f;
        float startDeg = 210f, sweep = 250f;
        for (int i = 0; i < segments; i++)
        {
            float t0 = (float)i / segments;
            float t1 = (float)(i + 1) / segments;
            float a0 = (startDeg + t0 * sweep) * Mathf.Deg2Rad;
            float a1 = (startDeg + t1 * sweep) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos((a0 + a1) / 2f), Mathf.Sin((a0 + a1) / 2f), 0) * radius;
            float deg = (a0 + a1) * 0.5f * Mathf.Rad2Deg;
            float len = (a1 - a0) * radius * 1.15f;
            var seg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            seg.name = "Icon_Arc_" + i;
            seg.transform.SetParent(parent, false);
            seg.GetComponent<MeshRenderer>().sharedMaterial = iconWhiteMat;
            seg.transform.localPosition = p;
            seg.transform.localEulerAngles = new Vector3(0, 0, deg);
            seg.transform.localScale = new Vector3(len, thickness, 1);
        }
        var head1 = GameObject.CreatePrimitive(PrimitiveType.Quad);
        head1.name = "Icon_RHead1";
        head1.transform.SetParent(parent, false);
        head1.GetComponent<MeshRenderer>().sharedMaterial = iconWhiteMat;
        head1.transform.localScale = new Vector3(thickness * 1.4f, thickness * 0.6f, 1);
        head1.transform.localPosition = new Vector3(-radius * 0.2f, radius * 0.55f, 0);
        head1.transform.localEulerAngles = new Vector3(0, 0, 130f);

        var head2 = GameObject.CreatePrimitive(PrimitiveType.Quad);
        head2.name = "Icon_RHead2";
        head2.transform.SetParent(parent, false);
        head2.GetComponent<MeshRenderer>().sharedMaterial = iconWhiteMat;
        head2.transform.localScale = new Vector3(thickness * 1.4f, thickness * 0.6f, 1);
        head2.transform.localPosition = new Vector3(-radius * 0.32f, radius * 0.40f, 0);
        head2.transform.localEulerAngles = new Vector3(0, 0, 220f);
    }

    void EnsurePose()
    {
        if (!panel) return;
        var cam = Camera.main;
        if (cam)
        {
            if (panel.centerDragEnabled && keepYawRelativeToCameraInMove)
            {
                // chỉ lấy yaw của camera + offset, giữ mặt phẳng thẳng đứng
                float yaw = cam.transform.eulerAngles.y + _yawOffsetToCam;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            else
            {
                Vector3 toCam = cam.transform.position - transform.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }
        }
    }

    void ComputeLocalOffset()
    {
        if (!panel) return;
        float below = (panel.height * 0.5f + panel.trayPadding + panel.trayHoverExtra);
        float halfDock = (_backplate ? _backplate.localScale.y * 0.5f : 0f);
        float margin = 0.01f;
        _offsetLocalNoZ = new Vector3(0, -(below + halfDock + margin), 0); // << KHÔNG Z ở đây
    }

    public void RecomputeFromPanel()
    {
        ComputeLocalOffset();
        EnsurePose();
        PlaceDockImmediate();
        _targetGapY = MeasureCurrentGapY();
    }

    void PlaceDockImmediate()
    {
        if (!panel) return;
        var cam = Camera.main;

        // 1) Vị trí gốc: offset local đã tính để Dock nằm dưới Tray
        Vector3 baseWorld = panel.transform.TransformPoint(_offsetLocalNoZ);

        // 2) Nhô nhẹ về phía camera (tránh chênh z khi nhìn nghiêng)
        Vector3 viewDir = cam ? (panel.transform.position - cam.transform.position).normalized : -panel.transform.forward;
        Vector3 zOffset = -viewDir * _zLift;

        transform.position = baseWorld + zOffset;
    }

    public float GetBackplateHeight()
    {
        return _backplate ? _backplate.localScale.y : (buttonSize.y + 0.04f);
    }

    public void AnchorToWorld(Vector3 worldPos, Camera cam)
    {
        if (panel == null) return;
        if (cam == null) cam = Camera.main;

        // zOffset: Dock luôn nhô theo hướng nhìn camera
        Vector3 viewDir = (cam != null)
            ? (cam.transform.position - panel.transform.position).normalized
            : -panel.transform.forward;
        Vector3 zOffset = -viewDir * _zLift;

        // Tính lại offset local (chỉ Y) để TransformPoint(offset)+zOffset == worldPos
        Vector3 wantLocal = panel.transform.InverseTransformPoint(worldPos - zOffset);
        wantLocal.z = 0f;                 // giữ đúng quy ước "no Z" cho offset
        _offsetLocalNoZ = wantLocal;
    }

    void LateUpdate()
    {
        if (!panel) return;
        var cam = Camera.main;
        // Ẩn Dock khi Tray ẩn (trừ khi đang minimize)
        bool visible = panel && (panel.dockMinimized || panel.GetTrayAlpha01() > 0.02f);
        SetDockRenderersAndColliders(visible);
        if (!visible) return;   // không cần tính tiếp

        // --- 1) Rotation (tách quay khỏi Tray, chỉ hiệu chỉnh yaw nếu cần) ---
        if (cam)
        {
            if (panel.centerDragEnabled && keepYawRelativeToCameraInMove)
            {
                // chỉ lấy yaw của camera + offset, giữ mặt phẳng thẳng đứng
                float yaw = cam.transform.eulerAngles.y + _yawOffsetToCam;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            else
            {
                Vector3 toCam = cam.transform.position - transform.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }
        }
        // --- 2) Position cơ bản: offset theo local Y + nhô về phía camera ---
        Vector3 baseWorld = panel.transform.TransformPoint(_offsetLocalNoZ);
        Vector3 viewDir = cam
            ? (panel.transform.position - cam.transform.position).normalized  // cam -> panel
            : -panel.transform.forward;
        Vector3 zOffset = -viewDir * _zLift;                                  // nhô về phía camera
        Vector3 pos = baseWorld + zOffset;

        // --- 3) Khóa khoảng cách Y giữa Tray và Dock ---
        // Mục tiêu: (TrayBottomY - DockTopY) == _targetGapY  -> hiệu chỉnh pos.y
        float trayBottomY = panel.GetTrayBottomYWorld(cam);
        float dockTopY = GetDockTopYWorldAt(pos);
        float deltaY = (trayBottomY - dockTopY) - _targetGapY;
        pos.y += deltaY;

        transform.position = pos;

        // --- 4) Alpha/backplate (giữ baseline để luôn thấy Dock) ---
        const float Baseline = 0.12f;
        float a = panel.dockMinimized ? 1f : Mathf.Max(Baseline, panel.GetTrayAlpha01());  // minimize: alpha ổn định
        var backMr = _backplate ? _backplate.GetComponent<MeshRenderer>() : null;
        if (backMr)
        {
            backMr.enabled = true;
            var m = backMr.sharedMaterial;
            if (m)
            {
                if (m.HasProperty("_MaskEnable")) m.SetFloat("_MaskEnable", 0f);
                if (m.HasProperty("_FillColor"))
                {
                    m.SetColor("_FillColor", new Color(0f, 0f, 0f, 0.18f * a));
                    m.SetColor("_BorderColor", new Color(1f, 1f, 1f, 0.18f * a));
                }
                else
                {
                    if (m.shader != Shader.Find("Unlit/Transparent"))
                        m.shader = Shader.Find("Unlit/Transparent");
                    var c = m.color; c.a = 0.18f * a; m.color = c;
                }
            }
        }
    }

    void SetDockRenderersAndColliders(bool on)
    {
        if (_backplate)
        {
            var mr = _backplate.GetComponent<MeshRenderer>();
            if (mr) mr.enabled = on;
        }
        foreach (var b in _buttons)
        {
            var mrBG = b.transform.Find("BG")?.GetComponent<MeshRenderer>();
            if (mrBG) mrBG.enabled = on;
            var ic = b.transform.Find("Icon")?.GetComponent<MeshRenderer>();
            if (ic) ic.enabled = on;
            var col = b.GetComponent<BoxCollider>();
            if (col) col.enabled = on;
        }
    }
}
