using UnityEngine;
using System.Collections.Generic;

public enum WPHandleType
{
    Center, EdgeTop, EdgeBottom, EdgeLeft, EdgeRight,
    CornerTL, CornerTR, CornerBL, CornerBR
}

[ExecuteAlways]
public class WorldPanelPlus : MonoBehaviour
{
    public enum WPAxisLock { None, YawOnly, PitchOnly }

    [Header("Rotation clamp")]
    public float yawAccum = 0f;    // độ lệch Yaw so với gốc
    public float pitchAccum = 0f;  // độ lệch Pitch so với gốc
    public WPAxisLock axisLock = WPAxisLock.None;

    Quaternion _rotBasis;          // gốc làm chuẩn để cộng dồn yaw/pitch (được set khi FaceCamera/Reset)
    [Header("Interaction Gates")]
    public bool centerDragEnabled = false;   // kéo Center chỉ bật qua Dock Move

    [Header("Move Mode (Center)")]
    public bool moveOrbitCamera = true;     // orbit quanh camera
    public float moveOrbitLerp = 12f;      // mượt vị trí/rotation
    public float moveOrbitMin = 0.25f;    // min dist
    public float moveOrbitMax = 6.0f;     // max dist
    [HideInInspector] public Vector3? moveAnchorWorld = null;

    [Header("Panel (content) size, meters")]
    public float width = 1.2f;
    public float height = 0.72f;

    [Header("Tray visuals")]
    public float trayPadding = 0.08f;
    public float trayHoverExtra = 0.05f;   // mở rộng hover/tray
    public float trayBehind = 0.02f;
    public float trayCornerRadius = 0.06f;
    public float trayBorder = 0.004f;
    public Color trayFill = new Color(0.2f, 0.2f, 0.2f, 0.25f);
    public Color trayBorderColor = new Color(1, 1, 1, 0.85f);
    public float trayFadeInDuration = 0.25f;
    public float trayFadeOutDuration = 0.50f;

    [SerializeField, Range(0, 1)] float _trayAlpha = 0f;
    bool _wantHover = false;

    [Header("Handles & frame")]
    public float handleDepth = 0.03f;
    public float handleEdgeThickness = 0.02f;
    public float cornerHandleSize = 0.06f;
    [Range(0.2f, 1f)] public float edgeActivePercent = 0.5f;

    [Header("Generated")]
    public Transform board;
    public Transform tray;
    public Transform hintsParent;
    public Transform handlesParent;
    public Transform hover;
    public WorldPanelPlusControlDock dock;

    Material _panelMat, _trayMat, _dashMat;
    public Texture contentTexture;
    public Color panelTint = Color.white;

    float FadeSpeedIn => 1f / Mathf.Max(0.0001f, trayFadeInDuration);
    float FadeSpeedOut => 1f / Mathf.Max(0.0001f, trayFadeOutDuration);
    MaterialPropertyBlock _mpb;
    // == Minimize Dock / Force hide tray ==
    [HideInInspector] public bool dockMinimized = false;   // trạng thái Dock
    [HideInInspector] public bool forceTrayHidden = false;  // ép ẩn tray khi dockMinimized

    public void ToggleDockMinimized()
    {
        dockMinimized = !dockMinimized;
        forceTrayHidden = dockMinimized;
        if (dock) dock.SetMinimized(dockMinimized);  // báo cho Dock đổi layout
    }

    public float GetTrayBottomYWorld(Camera cam = null)
    {
        if (!tray) return transform.position.y;
        // half sizes (local of Tray)
        float sx = (width + trayPadding * 2f) * 0.5f;
        float sy = (height + trayPadding * 2f) * 0.5f;

        // 4 góc (mặt phẳng local của Tray, z = 0 vì tray là Quad)
        Vector3[] corners =
        {
        new Vector3(-sx, -sy, 0),
        new Vector3(-sx,  sy, 0),
        new Vector3( sx, -sy, 0),
        new Vector3( sx,  sy, 0),
    };

        float minY = float.PositiveInfinity;
        foreach (var c in corners)
            minY = Mathf.Min(minY, tray.TransformPoint(c).y);
        return minY;
    }

    void Reset() { Rebuild(); }
    void OnValidate() { Apply(); }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        var kill = new List<GameObject>();
        foreach (Transform c in transform) kill.Add(c.gameObject);
        foreach (var go in kill) { if (Application.isEditor) DestroyImmediate(go); else Destroy(go); }
        EnsureMaterials();

        // Board
        board = CreateQuad("Board", _panelMat).transform;
        board.localScale = new Vector3(width, height, 1);
        var mr = board.GetComponent<MeshRenderer>();
        mr.sharedMaterial = _panelMat;
        mr.sharedMaterial.mainTexture = contentTexture;
        mr.sharedMaterial.color = panelTint;

        var bc = board.gameObject.AddComponent<BoxCollider>();
        bc.size = new Vector3(1, 1, 0.02f); bc.center = new Vector3(0, 0, 0.01f);
        var brc = board.gameObject.AddComponent<WorldPanelPlusBoardRaycatcher>(); brc.panel = this;

        // Tray (phía sau board)
        tray = CreateQuad("Tray", _trayMat).transform;
        tray.localPosition = new Vector3(0, 0, -trayBehind);
        UpdateTrayScaleAndMaterial();

        // Hints
        hintsParent = new GameObject("Hints").transform; hintsParent.SetParent(transform, false);
        CreateEdgeHint("Hint_Top", new Vector3(0, height / 2f + trayPadding * 0.5f, -trayBehind * 0.5f), new Vector3(width + trayPadding * 0.5f, 0.01f, 1));
        CreateEdgeHint("Hint_Bottom", new Vector3(0, -height / 2f - trayPadding * 0.5f, -trayBehind * 0.5f), new Vector3(width + trayPadding * 0.5f, 0.01f, 1));
        CreateEdgeHint("Hint_Left", new Vector3(-width / 2f - trayPadding * 0.5f, 0, -trayBehind * 0.5f), new Vector3(0.01f, height + trayPadding * 0.5f, 1));
        CreateEdgeHint("Hint_Right", new Vector3(width / 2f + trayPadding * 0.5f, 0, -trayBehind * 0.5f), new Vector3(0.01f, height + trayPadding * 0.5f, 1));
        SetHintsActive(false);

        // Handles
        handlesParent = new GameObject("Handles").transform; handlesParent.SetParent(transform, false);
        CreateHandles();

        // Marker spheres (trừ center)
        foreach (Transform t in handlesParent)
        {
            var h = t.GetComponent<WorldPanelPlusHandle>();
            if (!h || h.type == WPHandleType.Center) continue;
            if (!t.GetComponent<WorldPanelPlusHandleSphere>()) t.gameObject.AddComponent<WorldPanelPlusHandleSphere>();
        }

        // Hover collider (không render) – object rỗng
        hover = new GameObject("Hover").transform; hover.SetParent(transform, false);
        var bcHover = hover.gameObject.AddComponent<BoxCollider>(); bcHover.isTrigger = false;
        hover.gameObject.AddComponent<WorldPanelPlusTrayRaycatcher>().panel = this;

        // Control Dock
        var dockGO = new GameObject("ControlDock");
        dockGO.transform.SetParent(transform, false);
        dock = dockGO.AddComponent<WorldPanelPlusControlDock>();
        dock.panel = this;
        dock.Build(_trayMat);

        Apply();
        _rotBasis = transform.rotation;
    }

    public float GetTrayAlpha01() => forceTrayHidden ? 0f : _trayAlpha;

    public void SetRotationBasisNow() { _rotBasis = transform.rotation; }

    public void AddYawClamped(float deg)
    {
        if (axisLock == WPAxisLock.PitchOnly) return;      // khóa trục còn lại
        yawAccum = Mathf.Clamp(yawAccum + deg, -45f, 45f); // (2) không vượt 45°
        axisLock = Mathf.Approximately(yawAccum, 0f) ? (Mathf.Approximately(pitchAccum, 0f) ? WPAxisLock.None : WPAxisLock.PitchOnly)
                                                     : WPAxisLock.YawOnly;
        ApplyAccumulatedRotation();
        if (dock) dock.SetAxisLock(axisLock);              // khóa nút ở Dock
        UpdateHandleLocks();
    }

    public void AddPitchClamped(float deg)
    {
        if (axisLock == WPAxisLock.YawOnly) return;
        pitchAccum = Mathf.Clamp(pitchAccum + deg, -45f, 45f);
        axisLock = Mathf.Approximately(pitchAccum, 0f) ? (Mathf.Approximately(yawAccum, 0f) ? WPAxisLock.None : WPAxisLock.YawOnly)
                                                       : WPAxisLock.PitchOnly;
        ApplyAccumulatedRotation();
        if (dock) dock.SetAxisLock(axisLock);
        UpdateHandleLocks();
    }

    public void ResetYawPitchLocks()
    {
        yawAccum = 0f; pitchAccum = 0f; axisLock = WPAxisLock.None;
        ApplyAccumulatedRotation();
        if (dock) dock.SetAxisLock(axisLock);
        UpdateHandleLocks();
    }

    void ApplyAccumulatedRotation()
    {
        // xoay cộng dồn quanh các TRỤC CỦA BASIS
        Quaternion q = Quaternion.AngleAxis(yawAccum, _rotBasis * Vector3.up)
                     * Quaternion.AngleAxis(pitchAccum, _rotBasis * Vector3.right)
                     * _rotBasis;
        transform.rotation = q;
    }


    void EnsureMaterials()
    {
        if (_panelMat == null) _panelMat = new Material(Shader.Find("Unlit/Texture"));

        var sRounded = Shader.Find("Unlit/WorldPanelRounded");
        if (sRounded != null && sRounded.isSupported)
        {
            _trayMat = (_trayMat && _trayMat.shader == sRounded) ? _trayMat : new Material(sRounded);
        }
        else
        {
            _trayMat = new Material(Shader.Find("Unlit/Transparent")); // << quan trọng
        }

        var sDash = Shader.Find("Unlit/WorldPanelDash");
        _dashMat = (sDash != null && sDash.isSupported)
            ? (_dashMat && _dashMat.shader == sDash ? _dashMat : new Material(sDash))
            : new Material(Shader.Find("Unlit/Color"));
    }

    void UpdateTrayScaleAndMaterial()
    {
        if (!tray) return;

        // Luôn đặt KHAY lùi ra sau bảng so với camera
        float sign = -1f; // mặc định
        var cam = Camera.main;
        if (cam)
        {
            Vector3 toCam = cam.transform.position - transform.position;
            // Nếu camera đang ở "trước mặt" (cùng phía với forward của panel) => khay đi -forward để lùi
            sign = Vector3.Dot(transform.forward, toCam) > 0f ? -1f : 1f;
        }
        tray.localPosition = new Vector3(0, 0, sign * trayBehind);
        tray.localScale = new Vector3(width + trayPadding * 2f, height + trayPadding * 2f, 1);

        var mr = tray.GetComponent<MeshRenderer>();
        var mat = mr.sharedMaterial;

        if (mat.HasProperty("_FillColor"))
        {
            mat.SetColor("_FillColor",
                new Color(trayFill.r, trayFill.g, trayFill.b, trayFill.a * _trayAlpha));
            mat.SetColor("_BorderColor",
                new Color(trayBorderColor.r, trayBorderColor.g, trayBorderColor.b, trayBorderColor.a * _trayAlpha));
            if (mat.HasProperty("_Radius")) mat.SetFloat("_Radius", trayCornerRadius);
            if (mat.HasProperty("_Border")) mat.SetFloat("_Border", trayBorder);
            if (mat.HasProperty("_Feather")) mat.SetFloat("_Feather", 0.003f);
        }
        else
        {
            // Fallback Unlit/Color
            mat.color = new Color(trayFill.r, trayFill.g, trayFill.b, trayFill.a * _trayAlpha);
        }

        var rend = tray.GetComponent<MeshRenderer>();
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        // ép tắt mask
        _mpb.SetFloat("_MaskEnable", 0f);
        // _mpb.SetFloat("_MaskEnable", trayMaskEnabled ? 1f : 0f);
        // _mpb.SetVector("_MaskCenter", new Vector4(_maskUV.x, _maskUV.y, 0, 0));
        // _mpb.SetVector("_MaskSize", new Vector4(trayMaskSizeUV.x, trayMaskSizeUV.y, 0, 0));
        // _mpb.SetFloat("_MaskFeather", trayMaskFeather);
        rend.SetPropertyBlock(_mpb);
    }

    GameObject CreateQuad(string name, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name; go.transform.SetParent(transform, false);
        var col = go.GetComponent<Collider>(); if (col) { if (Application.isEditor) DestroyImmediate(col); else Destroy(col); }
        var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        return go;
    }

    void CreateEdgeHint(string name, Vector3 localPos, Vector3 localScale)
    {
        var go = CreateQuad(name, _dashMat);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.GetComponent<MeshRenderer>().enabled = false;
        go.transform.SetParent(hintsParent, true);
    }

    void SetHintsActive(bool on)
    {
        if (!hintsParent) return;
        foreach (Transform c in hintsParent)
        {
            var mr = c.GetComponent<MeshRenderer>();
            if (mr) mr.enabled = on;
        }
    }

    void CreateHandles()
    {
        float TrayW = width + trayPadding * 2f;
        float TrayH = height + trayPadding * 2f;
        float edgeLenW = TrayW * edgeActivePercent;
        float edgeLenH = TrayH * edgeActivePercent;

        float thw = TrayW * 0.5f, thh = TrayH * 0.5f;

        CreateHandle("Handle_Center", WPHandleType.Center, Vector3.zero,
            new Vector2(TrayW - handleEdgeThickness * 2f, TrayH - handleEdgeThickness * 2f));

        CreateHandle("Handle_Top", WPHandleType.EdgeTop, new Vector3(0, thh, 0), new Vector2(edgeLenW, handleEdgeThickness));
        CreateHandle("Handle_Bottom", WPHandleType.EdgeBottom, new Vector3(0, -thh, 0), new Vector2(edgeLenW, handleEdgeThickness));
        CreateHandle("Handle_Left", WPHandleType.EdgeLeft, new Vector3(-thw, 0, 0), new Vector2(handleEdgeThickness, edgeLenH));
        CreateHandle("Handle_Right", WPHandleType.EdgeRight, new Vector3(thw, 0, 0), new Vector2(handleEdgeThickness, edgeLenH));

        CreateHandle("Handle_TL", WPHandleType.CornerTL, new Vector3(-thw, thh, 0), new Vector2(cornerHandleSize, cornerHandleSize));
        CreateHandle("Handle_TR", WPHandleType.CornerTR, new Vector3(thw, thh, 0), new Vector2(cornerHandleSize, cornerHandleSize));
        CreateHandle("Handle_BL", WPHandleType.CornerBL, new Vector3(-thw, -thh, 0), new Vector2(cornerHandleSize, cornerHandleSize));
        CreateHandle("Handle_BR", WPHandleType.CornerBR, new Vector3(thw, -thh, 0), new Vector2(cornerHandleSize, cornerHandleSize));
    }

    void CreateHandle(string name, WPHandleType type, Vector3 localPos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(handlesParent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(size.x, size.y, handleDepth);
        box.center = new Vector3(0, 0, handleDepth * 0.5f);
        if (type == WPHandleType.Center)
        {
            box.enabled = false;              // không nhận raycast
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
        }
        var h = go.AddComponent<WorldPanelPlusHandle>();
        h.type = type; h.panel = this;
    }

    public Material GetTrayMaterial()
    {
        // Ưu tiên material đang gắn trên mesh của Tray (nếu đã có), fallback _trayMat
        var mr = tray ? tray.GetComponent<MeshRenderer>() : null;
        return mr ? mr.sharedMaterial : _trayMat;
    }

    public void Apply()
    {
        EnsureMaterials();

        if (board)
        {
            board.localScale = new Vector3(width, height, 1);
            var mr = board.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _panelMat;
            mr.sharedMaterial.mainTexture = contentTexture;
            mr.sharedMaterial.color = panelTint;
            var col = board.GetComponent<BoxCollider>(); if (col) { col.size = new Vector3(1, 1, 0.02f); col.center = new Vector3(0, 0, 0.01f); }
        }
        UpdateTrayScaleAndMaterial();

        // Hints re-layout
        float TrayW = width + trayPadding * 2f;
        float TrayH = height + trayPadding * 2f;
        float thw = TrayW * 0.5f, thh = TrayH * 0.5f;

        if (hintsParent)
        {
            float bandW = TrayW * edgeActivePercent;
            float bandH = TrayH * edgeActivePercent;

            var ht = hintsParent.Find("Hint_Top");
            var hb = hintsParent.Find("Hint_Bottom");
            var hl = hintsParent.Find("Hint_Left");
            var hr = hintsParent.Find("Hint_Right");

            if (ht) { ht.localPosition = new Vector3(0, thh, 0); ht.localScale = new Vector3(bandW, 0.01f, 1); }
            if (hb) { hb.localPosition = new Vector3(0, -thh, 0); hb.localScale = new Vector3(bandW, 0.01f, 1); }
            if (hl) { hl.localPosition = new Vector3(-thw, 0, 0); hl.localScale = new Vector3(0.01f, bandH, 1); }
            if (hr) { hr.localPosition = new Vector3(thw, 0, 0); hr.localScale = new Vector3(0.01f, bandH, 1); }
        }

        // Hover collider bao cả Dock
        if (hover)
        {
            var bc = hover.GetComponent<BoxCollider>();
            float dockH = (dock != null) ? dock.GetBackplateHeight() : 0f;
            float extra = trayHoverExtra;
            bc.size = new Vector3(
                TrayW + extra * 2f,
                TrayH + extra * 2f + dockH + extra,
                handleDepth
            );
            bc.center = new Vector3(0, -(dockH + extra) * 0.5f, handleDepth * 0.5f);
        }

        // Đặt lại Dock ngay sau khi tính lại kích thước Tray
        if (dock) dock.RecomputeFromPanel();
        UpdateHandlesLayout();
    }

    public void UpdateHandleLocks()
    {
        if (!handlesParent) return;

        foreach (Transform t in handlesParent)
        {
            var h = t.GetComponent<WorldPanelPlusHandle>();
            if (!h) continue;
            var col = t.GetComponent<BoxCollider>();
            if (!col) continue;

            bool isYawEdge = (h.type == WPHandleType.EdgeLeft || h.type == WPHandleType.EdgeRight);
            bool isPitchEdge = (h.type == WPHandleType.EdgeTop || h.type == WPHandleType.EdgeBottom);

            switch (axisLock)
            {
                case WPAxisLock.None:
                    col.enabled = true;
                    break;
                case WPAxisLock.YawOnly:
                    // khóa Pitch => tắt EdgeTop/Bottom
                    col.enabled = !isPitchEdge;
                    break;
                case WPAxisLock.PitchOnly:
                    // khóa Yaw => tắt EdgeLeft/Right
                    col.enabled = !isYawEdge;
                    break;
            }
        }
    }

    void UpdateHandlesLayout()
    {
        if (!handlesParent) return;

        float TrayW = width + trayPadding * 2f;
        float TrayH = height + trayPadding * 2f;
        float edgeLenW = TrayW * edgeActivePercent;
        float edgeLenH = TrayH * edgeActivePercent;

        float thw = TrayW * 0.5f, thh = TrayH * 0.5f;

        foreach (Transform t in handlesParent)
        {
            var h = t.GetComponent<WorldPanelPlusHandle>();
            if (!h) continue;

            switch (h.type)
            {
                case WPHandleType.Center:
                    t.localPosition = Vector3.zero;
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (box)
                        {
                            box.size = new Vector3(TrayW - handleEdgeThickness * 2f, TrayH - handleEdgeThickness * 2f, handleDepth);
                            box.center = new Vector3(0, 0, handleDepth * 0.5f);
                        }
                    }
                    break;

                case WPHandleType.EdgeTop:
                    t.localPosition = new Vector3(0, thh, 0);
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (box)
                        {
                            box.size = new Vector3(edgeLenW, handleEdgeThickness, handleDepth);
                            box.center = new Vector3(0, 0, handleDepth * 0.5f);
                        }
                    }
                    break;

                case WPHandleType.EdgeBottom:
                    t.localPosition = new Vector3(0, -thh, 0);
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (box)
                        {
                            box.size = new Vector3(edgeLenW, handleEdgeThickness, handleDepth);
                            box.center = new Vector3(0, 0, handleDepth * 0.5f);
                        }
                    }
                    break;

                case WPHandleType.EdgeLeft:
                    t.localPosition = new Vector3(-thw, 0, 0);
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (box)
                        {
                            box.size = new Vector3(handleEdgeThickness, edgeLenH, handleDepth);
                            box.center = new Vector3(0, 0, handleDepth * 0.5f);
                        }
                    }
                    break;

                case WPHandleType.EdgeRight:
                    t.localPosition = new Vector3(thw, 0, 0);
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (box)
                        {
                            box.size = new Vector3(handleEdgeThickness, edgeLenH, handleDepth);
                            box.center = new Vector3(0, 0, handleDepth * 0.5f);
                        }
                    }
                    break;

                case WPHandleType.CornerTL:
                    t.localPosition = new Vector3(-thw, thh, 0);
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (box)
                        {
                            box.size = new Vector3(cornerHandleSize, cornerHandleSize, handleDepth);
                            box.center = new Vector3(0, 0, handleDepth * 0.5f);
                        }
                    }
                    break;

                case WPHandleType.CornerTR:
                    t.localPosition = new Vector3(thw, thh, 0);
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (box)
                        {
                            box.size = new Vector3(cornerHandleSize, cornerHandleSize, handleDepth);
                            box.center = new Vector3(0, 0, handleDepth * 0.5f);
                        }
                    }
                    break;

                case WPHandleType.CornerBL:
                    t.localPosition = new Vector3(-thw, -thh, 0);
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (box)
                        {
                            box.size = new Vector3(cornerHandleSize, cornerHandleSize, handleDepth);
                            box.center = new Vector3(0, 0, handleDepth * 0.5f);
                        }
                    }
                    break;

                case WPHandleType.CornerBR:
                    t.localPosition = new Vector3(thw, -thh, 0);
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (box)
                        {
                            box.size = new Vector3(cornerHandleSize, cornerHandleSize, handleDepth);
                            box.center = new Vector3(0, 0, handleDepth * 0.5f);
                        }
                    }
                    break;
            }
        }
    }

    void Update()
    {
        // 1) Quyết định alpha mục tiêu: forceTrayHidden có quyền cao nhất
        float targetAlpha = forceTrayHidden ? 0f : (_wantHover ? 1f : 0f);
        float speed = (targetAlpha > _trayAlpha) ? FadeSpeedIn : FadeSpeedOut;
        _trayAlpha = Mathf.MoveTowards(_trayAlpha, targetAlpha, speed * Time.deltaTime);

        // 2) Cập nhật vật liệu
        UpdateTrayScaleAndMaterial();

        // 3) Hover collider: tắt hẳn khi đang ép ẩn
        if (hover)
        {
            var bc = hover.GetComponent<BoxCollider>();
            if (bc) bc.enabled = !forceTrayHidden;
        }

        // 4) Nếu ép ẩn thì đảm bảo tắt hint
        if (forceTrayHidden) SetHintsActive(false);

        // 5) Marker spheres: chỉ bật khi không ép ẩn và alpha đủ lớn
        bool showMarkers = !forceTrayHidden && _trayAlpha > 0.02f;
        if (handlesParent)
        {
            foreach (Transform t in handlesParent)
            {
                var mk = t.GetComponent<WorldPanelPlusHandleSphere>();
                if (mk) mk.SetVisible(showMarkers);
            }
        }
    }

    public void OnHover(bool on, WPHandleType? handleType = null)
    {
        if (forceTrayHidden) return;
        _wantHover = on;

        bool showEdge = handleType.HasValue &&
            (handleType == WPHandleType.EdgeTop || handleType == WPHandleType.EdgeBottom ||
             handleType == WPHandleType.EdgeLeft || handleType == WPHandleType.EdgeRight);

        SetHintsActive(on && showEdge);
    }
}
