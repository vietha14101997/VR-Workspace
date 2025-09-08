using UnityEngine;
using System.Collections.Generic;
using System.Collections;

[ExecuteAlways]
[RequireComponent(typeof(Transform))]
public partial class WorldPanelPlusControlDock : MonoBehaviour
{
    [Header("Screen-space Y lock")]
    public bool lockScreenYToTray = true;
    [Range(0f, 0.5f)] public float dockViewportYOffset = 0.06f;
    public enum DockYRef { TrayBottom, TrayCenter }
    public DockYRef yReference = DockYRef.TrayBottom;

    [Header("Bind")]
    public WorldPanelPlus panel;

    [Header("Freeze")]
    public bool freezeWorldRotation = true;
    Quaternion _frozenWorldRot;

    [Header("Button layout")]
    public Vector2 buttonSize = new Vector2(0.08f, 0.045f);
    public float buttonGap = 0.02f;

    [Header("Visuals")]
    public Material dockMat;
    public Material iconMat;
    public Material iconWhiteMat;
    public float cornerRadius = 0.06f;
    public float border = 0.004f;
    public Color buttonBg = new Color(0f, 0f, 0f, 0.35f);

    [Header("Icons")]
    public Texture2D moveIconTexture;

    Transform _backplate;
    readonly List<WorldPanelPlusDockButton> _buttons = new();
    public bool keepYawRelativeToCameraInMove = true;
    float _yawOffsetToCam = 0f;
    float _zLift = 0.01f;
    Vector3 _offsetLocalNoZ;
    float _targetGapY = 0f;

    bool _minimized = false;
    Coroutine _dockAnim;
    readonly Dictionary<WorldPanelPlusDockButton, Vector3> _expandedPos = new();
    Transform _toggleBtnTr;
    float _animDuration = 0.25f;
    Transform _visualRoot;
    Transform _neutralRoot;
    Vector3 _lastNeutralRef;
    bool _built;
    Vector3 _lastLossyDock;

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        _buttons.Clear();
        _backplate = null;
        _visualRoot = null;
        _neutralRoot = null;
    }

    void OnEnable()
    {
        if (!panel) panel = GetComponentInParent<WorldPanelPlus>();
        ApplyMinimizeVisualState();
    }

    void OnValidate()
    {
        ApplyMinimizeVisualState();
    }

    public void SetMinimized(bool on)
    {
        if (_minimized == on) return;
        _minimized = on;

        if (_dockAnim != null) StopCoroutine(_dockAnim);
        _dockAnim = StartCoroutine(AnimateDock(on));
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
            bool isYaw = b.type == WPDockButtonType.YawLeft15 || b.type == WPDockButtonType.YawRight15;
            bool isPitch = b.type == WPDockButtonType.PitchUp15 || b.type == WPDockButtonType.PitchDown15;

            bool enable = true;
            if (lockState == WorldPanelPlus.WPAxisLock.YawOnly) enable = !isPitch;
            if (lockState == WorldPanelPlus.WPAxisLock.PitchOnly) enable = !isYaw;

            // Tắt/bật collider để reticle không bấm được
            var bc = b.GetComponent<BoxCollider>(); if (bc) bc.enabled = enable; // fix: enable, không phải 'enabled'

            // Làm mờ nền nút (BG) cho biết nút bị khóa
            var bgMr = b.transform.Find("BG")?.GetComponent<MeshRenderer>();
            if (bgMr && bgMr.sharedMaterial)
            {
                if (bgMr.sharedMaterial.HasProperty("_FillColor"))
                {
                    var c = bgMr.sharedMaterial.GetColor("_FillColor");
                    c.a = enable ? 0.35f : 0.12f; // mờ khi khóa
                    bgMr.sharedMaterial.SetColor("_FillColor", c);
                }
                else
                {
                    var c = bgMr.sharedMaterial.color; c.a = enable ? 0.35f : 0.12f;
                    bgMr.sharedMaterial.color = c;
                }
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
    void OnTransformParentChanged() { PlaceDockImmediate(); }

    public void Build(Material trayMatLike)
    {
        if (_built) ClearChildren();
        _built = true;

        var vr = new GameObject("VisualRoot");
        vr.transform.SetParent(transform, false);
        _visualRoot = vr.transform;

        var neutral = new GameObject("NeutralScale").transform;
        neutral.SetParent(_visualRoot, false);
        _neutralRoot = neutral;
        RefreshNeutralScale();

        dockMat = trayMatLike;
        if (iconMat == null) iconMat = new Material(Shader.Find("Unlit/Texture"));
        if (iconWhiteMat == null) iconWhiteMat = new Material(Shader.Find("Unlit/Color")) { color = Color.white };

        var types = new[]{
            WPDockButtonType.MoveMode, WPDockButtonType.YawLeft15, WPDockButtonType.YawRight15,
            WPDockButtonType.MinimizeToggle, WPDockButtonType.PitchUp15, WPDockButtonType.PitchDown15,
            WPDockButtonType.ResetFaceCamera };

        float extraGap = 0.03f;
        float totalW = types.Length * buttonSize.x + (types.Length - 1) * (buttonGap + extraGap);
        float x0 = -totalW * 0.5f + buttonSize.x * 0.5f;

        _backplate = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
        _backplate.name = "DockBackplate";
        _backplate.SetParent(_neutralRoot, false);
        var col = _backplate.GetComponent<Collider>(); if (col) DestroyImmediate(col);

        // Store expanded size for animation
        float expandedWidth = totalW + border * 2;
        float expandedHeight = buttonSize.y + border * 2;

        // Start with minimized size if _minimized is true
        float startWidth = _minimized ? (buttonSize.x + border * 2) : expandedWidth;
        _backplate.localScale = new Vector3(startWidth, expandedHeight, 1);
        var mr = _backplate.GetComponent<MeshRenderer>();
        // Sử dụng shader riêng cho Dock
        var dockShader = Shader.Find("Unlit/WorldPanelDock");
        Material newMat;
        if (dockShader != null)
        {
            newMat = new Material(dockShader);
        }
        else
        {
            newMat = new Material(trayMatLike);
        }
        mr.sharedMaterial = newMat;
        var m = mr.sharedMaterial;

        // Thiết lập các thuộc tính chung
        if (m.HasProperty("_FillColor"))
        {
            m.SetColor("_FillColor", new Color(0f, 0f, 0f, 0.22f));
            m.SetColor("_BorderColor", new Color(1f, 1f, 1f, 0.35f));
        }

        // Thiết lập thuộc tính riêng của Dock shader
        if (dockShader != null)
        {
            if (m.HasProperty("_Radius")) m.SetFloat("_Radius", cornerRadius);
            if (m.HasProperty("_Border")) m.SetFloat("_Border", border);
            if (m.HasProperty("_Feather")) m.SetFloat("_Feather", 0.006f);
            if (m.HasProperty("_BorderFade")) m.SetFloat("_BorderFade", 0.5f);
        }
        // Fallback cho Tray shader
        else if (m.HasProperty("_MaskEnable"))
        {
            m.SetFloat("_MaskEnable", 1f);
            if (m.HasProperty("_CornerRadius")) m.SetFloat("_CornerRadius", cornerRadius);
            if (m.HasProperty("_Border")) m.SetFloat("_Border", border);
        }

        for (int i = 0; i < types.Length; i++)
        {
            var go = new GameObject("Btn_" + types[i]);
            go.transform.SetParent(_neutralRoot, false);
            go.transform.localPosition = new Vector3(x0 + i * (buttonSize.x + buttonGap + extraGap), 0, 0.01f);

            var btn = go.AddComponent<WorldPanelPlusDockButton>();
            btn.panel = panel; btn.type = types[i];
            if (btn.type == WPDockButtonType.MinimizeToggle) _toggleBtnTr = go.transform;

            var bc = go.AddComponent<BoxCollider>();
            SetColliderWorldSize(bc, new Vector3(buttonSize.x, buttonSize.y, 0.02f), 0.01f);

            var icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
            icon.name = "Icon";
            icon.transform.SetParent(go.transform, false);
            // Thay đổi từ chữ nhật sang hình vuông, lấy kích thước nhỏ nhất
            float iconSize = Mathf.Min(buttonSize.x, buttonSize.y);
            icon.transform.localScale = new Vector3(iconSize, iconSize, 1);
            icon.transform.localPosition = new Vector3(0, 0, 0.015f);
            DestroyImmediate(icon.GetComponent<Collider>());
            var iconMr = icon.GetComponent<MeshRenderer>();

            // Set material trong suốt cho icon
            var transparentMat = new Material(Shader.Find("Unlit/Transparent"));
            transparentMat.color = new Color(1, 1, 1, 0);
            iconMr.sharedMaterial = transparentMat;

            // if (btn.type == WPDockButtonType.MoveMode && moveIconTexture != null)
            // {
            //     iconMr.sharedMaterial = new Material(iconMat);
            //     iconMr.sharedMaterial.mainTexture = moveIconTexture;
            // }
            // else
            // {
            //     iconMr.enabled = false;
            // }

            _buttons.Add(btn);
        }

        _frozenWorldRot = transform.rotation;
        ComputeLocalOffset();
        AnchorToWorld(transform.position, Camera.main);
        EnsurePose();
        PlaceDockImmediate();
        _targetGapY = MeasureCurrentGapY();
        CaptureExpandedLayout();
        ApplyMinimizeVisualState();
    }

    void ApplyMinimizeVisualState()
    {
        if (_buttons == null) return;
        foreach (var b in _buttons)
        {
            if (!b) continue;
            bool isToggle = (_toggleBtnTr && b.transform == _toggleBtnTr);
            bool show = !_minimized || isToggle;

            // Kích hoạt/vô hiệu hóa GameObject của button
            b.gameObject.SetActive(show);

            // Lấy tất cả Renderer (bao gồm cả icon) và ẩn/hiện chúng
            var renderers = b.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.enabled = show;
            }

            // Lấy tất cả Collider và ẩn/hiện chúng  
            var colliders = b.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider == null) continue;
                collider.enabled = show;
            }
        }

        // Cập nhật kích thước backplate
        ResizeBackplateToActiveButtons();
    }

    void ResizeBackplateToActiveButtons()
    {
        if (!_backplate) return;

        // Đếm số nút đang active
        int activeCount = 0;
        foreach (var b in _buttons)
        {
            if (b && b.gameObject.activeSelf) activeCount++;
        }

        // Đảm bảo luôn có ít nhất 1 nút (nút minimize)
        activeCount = Mathf.Max(activeCount, 1);

        // Tính toán lại kích thước backplate
        float totalW = activeCount * buttonSize.x + (activeCount - 1) * buttonGap;
        float padding = 0.04f; // padding cho backplate

        // Cập nhật scale của backplate
        if (_backplate)
        {
            _backplate.localScale = new Vector3(totalW + padding, buttonSize.y + padding, 1);
        }

        // Cập nhật layout các nút
        float x0 = -(totalW * 0.5f) + (buttonSize.x * 0.5f);
        int index = 0;

        foreach (var b in _buttons)
        {
            if (b && b.gameObject.activeSelf)
            {
                b.transform.localPosition = new Vector3(
                    x0 + index * (buttonSize.x + buttonGap),
                    0,
                    0.01f
                );
                index++;
            }
        }

        // Cập nhật visibility
        foreach (var b in _buttons)
        {
            if (!b) continue;
            bool on = b.gameObject.activeSelf;
            foreach (var r in b.GetComponentsInChildren<Renderer>(true))
            {
                if (r) r.enabled = on;
            }
        }
    }

    void RefreshNeutralScale()
    {
        if (!_neutralRoot) return;
        Vector3 refLossy = _neutralRoot.parent ? _neutralRoot.parent.lossyScale : Vector3.one;
        float ix = 1f / Mathf.Max(1e-5f, refLossy.x);
        float iy = 1f / Mathf.Max(1e-5f, refLossy.y);
        float iz = 1f / Mathf.Max(1e-5f, refLossy.z);
        _neutralRoot.localScale = new Vector3(ix, iy, iz);
        _lastNeutralRef = refLossy;
    }

    static void SetColliderWorldSize(BoxCollider bc, Vector3 worldSize, float worldZCenter = 0f)
    {
        var t = bc.transform;
        Vector3 ls = t.lossyScale;
        float sx = Mathf.Max(1e-5f, ls.x);
        float sy = Mathf.Max(1e-5f, ls.y);
        float sz = Mathf.Max(1e-5f, ls.z);
        bc.size = new Vector3(worldSize.x / sx, worldSize.y / sy, worldSize.z / sz);
        bc.center = new Vector3(0f, 0f, worldZCenter / sz);
    }

    void EnsureButtonColliders()
    {
        if (_buttons == null || _buttons.Count == 0) return;
        RefreshNeutralScale();
        foreach (var b in _buttons)
        {
            if (!b) continue;
            var bc = b.GetComponent<BoxCollider>();
            if (!bc) continue;
            SetColliderWorldSize(bc, new Vector3(buttonSize.x, buttonSize.y, 0.02f), 0.01f);
        }
    }

    void CaptureExpandedLayout()
    {
        _expandedPos.Clear();
        foreach (var b in _buttons)
            _expandedPos[b] = b.transform.localPosition;  // vị trí khi mở rộng
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

        // 0) Hiển thị Dock khi: đang minimize hoặc Tray đang hiện
        bool visible = panel.GetTrayAlpha01() > 0.02f || _minimized;
        SetDockRenderersAndColliders(visible);
        if (!visible) return;

        // 1) Rotation: luôn face camera; nếu đang move và muốn giữ yaw relative thì chỉ lấy yaw
        if (cam)
        {
            if (panel.centerDragEnabled && keepYawRelativeToCameraInMove)
            {
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

        // 2) Position: offset theo local + nhô về phía camera
        Vector3 baseWorld = panel.transform.TransformPoint(_offsetLocalNoZ);
        Vector3 viewDir = cam ? (panel.transform.position - cam.transform.position).normalized
                              : -panel.transform.forward;
        Vector3 zOffset = -viewDir * _zLift;
        Vector3 pos = baseWorld + zOffset;

        // 3) Khóa khoảng cách Y giữa Tray và Dock
        float trayBottomY = panel.GetTrayBottomYWorld(cam);
        float dockTopY = GetDockTopYWorldAt(pos);
        float deltaY = trayBottomY - dockTopY - _targetGapY;
        pos.y += deltaY;
        transform.position = pos;

        if (_neutralRoot && (_neutralRoot.parent && (_neutralRoot.parent.lossyScale - _lastNeutralRef).sqrMagnitude > 1e-8f))
        {
            RefreshNeutralScale();
        }

        if ((transform.lossyScale - _lastLossyDock).sqrMagnitude > 1e-6f)
        {
            foreach (var b in _buttons)
            {
                var bc = b.GetComponent<BoxCollider>();
                if (bc) SetColliderWorldSize(bc, new Vector3(buttonSize.x, buttonSize.y, 0.02f), 0.01f);
            }
            _lastLossyDock = transform.lossyScale;
        }

        // 4) Cập nhật alpha backplate (không baseline, chỉ khi visible)
        var backMr = _backplate ? _backplate.GetComponent<MeshRenderer>() : null;
        if (backMr && backMr.sharedMaterial)
        {
            float a = panel.dockMinimized ? 1f : panel.GetTrayAlpha01(); // minimize: alpha ổn định
            var m = backMr.sharedMaterial;

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

        if ((transform.lossyScale - _lastLossyDock).sqrMagnitude > 1e-6f)
        {
            foreach (var b in _buttons)
            {
                var bc = b.GetComponent<BoxCollider>();
                if (bc) SetColliderWorldSize(bc, new Vector3(buttonSize.x, buttonSize.y, 0.02f), 0.01f);
            }
            _lastLossyDock = transform.lossyScale;
        }
    }

    void SetDockRenderersAndColliders(bool on)
    {
        if (_visualRoot)
            _visualRoot.gameObject.SetActive(on || _minimized); // vẫn hiển thị toggle khi minimize

        var cols = GetComponentsInChildren<Collider>(true);
        foreach (var c in cols)
        {
            if (!c) continue;
            bool isToggle = (_toggleBtnTr && c.transform.IsChildOf(_toggleBtnTr));
            bool allow = (!_minimized) || isToggle;
            c.enabled = on && allow && c.gameObject.activeInHierarchy;
        }
    }
}
