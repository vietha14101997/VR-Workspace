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

    // =========================
    // Build Toggles (new)
    // =========================
    [Header("Build Toggles")]
    [Tooltip("Sinh các edge/corner handles (khuyến nghị: OFF nếu không dùng).")]
    public bool generateHandles = true;
    [Tooltip("Chỉ sinh 4 corner handles (bật nếu muốn giữ 4 góc).")]
    public bool generateCornerHandles = true;
    [Tooltip("Tự thêm sphere marker lên các handle (trừ Center).")]
    public bool generateHandleSpheres = false;
    [Tooltip("Sinh & cho phép Hints.")]
    public bool generateHints = false;
    [Tooltip("Dùng shader có mờ rìa cho Board.")]
    public bool useBoardEdgeFeather = true;

    [Header("Rotation clamp")]
    public float yawAccum = 0f;
    public float pitchAccum = 0f;
    public WPAxisLock axisLock = WPAxisLock.None;

    Quaternion _rotBasis;

    [Header("Hover behaviour")]
    public bool hoverWorldAligned = true;   // giữ Hover không xoay theo panel
    public bool hoverFaceCameraYawOnly = true; // Hover chỉ xoay theo yaw camera

    [Header("Interaction Gates")]
    public bool centerDragEnabled = false;

    [Header("Move Mode (Center)")]
    public bool moveOrbitCamera = true;
    public float moveOrbitLerp = 12f;
    public float moveOrbitMin = 0.25f;
    public float moveOrbitMax = 6.0f;
    [HideInInspector] public Vector3? moveAnchorWorld = null;

    [Header("Panel (content) size, meters")]
    public float width = 1.2f;
    public float height = 0.72f;

    [Header("Tray visuals")]
    public float trayPadding = 0.045f;
    public float trayHoverExtra = 0.035f;
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

    //=== Board visuals ===
    [Header("Board visuals")]
    public Texture contentTexture;
    public Color panelTint = Color.white;
    public bool boardVisible = true;
    [Range(0, 1)] public float boardAlpha = 1f;

    Material _panelMat, _trayMat, _dashMat;
    float FadeSpeedIn => 1f / Mathf.Max(0.0001f, trayFadeInDuration);
    float FadeSpeedOut => 1f / Mathf.Max(0.0001f, trayFadeOutDuration);
    MaterialPropertyBlock _mpb;

    [HideInInspector] public bool dockMinimized = false;
    [HideInInspector] public bool forceTrayHidden = false;

    [HideInInspector] public bool isResizing = false;
    // cache để không tạo Material fallback nhiều lần
    [SerializeField, HideInInspector] private Material _fallbackPanelMat;

    private Material GetPanelMaterial()
    {
        if (_panelMat != null) return _panelMat;

        if (_fallbackPanelMat == null)
        {
            var sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Sprites/Default"); // fallback cuối
            _fallbackPanelMat = new Material(sh) { name = "WorldPanelPlus_FallbackPanel" };
        }
        return _fallbackPanelMat;
    }

#if UNITY_EDITOR
    private bool _shaderPropertiesNeedUpdate = true;
#endif

    public void SetResizeMode(bool on)
    {
        isResizing = on;

        if (board)
        {
            var col = board.GetComponent<BoxCollider>();
            if (col) col.enabled = !on;
        }
        if (hover)
        {
            var bc = hover.GetComponent<BoxCollider>();
            if (bc) bc.enabled = true;
        }
    }

    public void ToggleDockMinimized()
    {
        dockMinimized = !dockMinimized;
        forceTrayHidden = dockMinimized;
        if (dock) dock.SetMinimized(dockMinimized);
    }

    public float GetTrayBottomYWorld(Camera cam = null)
    {
        if (!tray) return transform.position.y;
        float sx = (width + trayPadding * 2f) * 0.5f;
        float sy = (height + trayPadding * 2f) * 0.5f;
        Vector3[] corners =
        {
            new Vector3(-sx, -sy, 0),
            new Vector3(-sx,  sy, 0),
            new Vector3( sx, -sy, 0),
            new Vector3( sx,  sy, 0),
        };
        float minY = float.PositiveInfinity;
        foreach (var c in corners) minY = Mathf.Min(minY, tray.TransformPoint(c).y);
        return minY;
    }

    void Reset() { Rebuild(); }

    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
#if UNITY_EDITOR
        _shaderPropertiesNeedUpdate = true;
#endif
        // LUÔN bảo đảm material trước, kể cả khi board/tray đã tồn tại
        EnsureMaterials();

        // Trong lúc Validate có thể child chưa kịp tái tạo → đừng Apply nếu thiếu
        if (!board || !tray) return;

        Apply();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        // clear children
        var kill = new List<GameObject>();
        foreach (Transform c in transform) kill.Add(c.gameObject);
        foreach (var go in kill) { if (Application.isEditor) DestroyImmediate(go); else Destroy(go); }

        EnsureMaterials();

        // === Board ===
        board = CreateQuad("Board", _panelMat).transform;
        board.localScale = new Vector3(width, height, 1);
        var mrBoard = board.GetComponent<MeshRenderer>();
        mrBoard.sharedMaterial = _panelMat;
        mrBoard.sharedMaterial.mainTexture = contentTexture;
        mrBoard.enabled = boardVisible;
        ApplyBoardTint(mrBoard);

        var bc = board.gameObject.AddComponent<BoxCollider>();
        bc.size = new Vector3(1, 1, 0.02f);
        bc.center = new Vector3(0, 0, 0.01f);
        var brc = board.gameObject.AddComponent<WorldPanelPlusBoardRaycatcher>(); brc.panel = this;

        // === Tray ===
        tray = CreateQuad("Tray", _trayMat).transform;
        tray.localPosition = new Vector3(0, 0, -trayBehind);
        UpdateTrayScaleAndMaterial();

        // === Hints ===
        hintsParent = new GameObject("Hints").transform; hintsParent.SetParent(transform, false);
        if (generateHints)
        {
            // Nếu muốn bật Hint dải cạnh thì bổ sung CreateEdgeHint(...) ở đây.
            // Ví dụ (đang để tắt mặc định):
            //CreateEdgeHint("Hint_Top", new Vector3(0, height/2f + trayPadding*0.5f, -trayBehind*0.5f), new Vector3(width + trayPadding*0.5f, 0.01f, 1));
        }
        SetHintsActive(false);

        // === Handles ===
        handlesParent = new GameObject("Handles").transform;
        handlesParent.SetParent(transform, false);
        CreateHandlesConditional();

        // === Marker spheres ===
        if (generateHandleSpheres && handlesParent)
        {
            foreach (Transform t in handlesParent)
            {
                var h = t.GetComponent<WorldPanelPlusHandle>();
                if (!h || h.type == WPHandleType.Center) continue;
                if (!t.GetComponent<WorldPanelPlusHandleSphere>()) t.gameObject.AddComponent<WorldPanelPlusHandleSphere>();
            }
        }

        // === Hover collider ===
        hover = new GameObject("Hover").transform; hover.SetParent(transform, false);
        var bcHover = hover.gameObject.AddComponent<BoxCollider>(); bcHover.isTrigger = false;
        hover.gameObject.AddComponent<WorldPanelPlusTrayRaycatcher>().panel = this;

        // === Control Dock ===
        var dockGO = new GameObject("ControlDock");
        dockGO.transform.SetParent(transform, false);
        dock = dockGO.AddComponent<WorldPanelPlusControlDock>();
        dock.panel = this;
        dock.Build();

        Apply();
        _rotBasis = transform.rotation;
    }

    public float GetTrayAlpha01() => forceTrayHidden ? 0f : _trayAlpha;

    public void SetRotationBasisNow() { _rotBasis = transform.rotation; }

    public void AddYawClamped(float deg)
    {
        if (axisLock == WPAxisLock.PitchOnly) return;
        yawAccum = Mathf.Clamp(yawAccum + deg, -15f, 15f);   // was ±45
        axisLock = Mathf.Approximately(yawAccum, 0f) ? (Mathf.Approximately(pitchAccum, 0f) ? WPAxisLock.None : WPAxisLock.PitchOnly) : WPAxisLock.YawOnly;
        ApplyAccumulatedRotationStable();
        if (dock) dock.SetAxisLock(axisLock);
        UpdateHandleLocks();
    }

    public void AddPitchClamped(float deg)
    {
        if (axisLock == WPAxisLock.YawOnly) return;
        pitchAccum = Mathf.Clamp(pitchAccum + deg, -15f, 15f); // was ±45
        axisLock = Mathf.Approximately(pitchAccum, 0f) ? (Mathf.Approximately(yawAccum, 0f) ? WPAxisLock.None : WPAxisLock.YawOnly) : WPAxisLock.PitchOnly;
        ApplyAccumulatedRotationStable();
        if (dock) dock.SetAxisLock(axisLock);
        UpdateHandleLocks();
    }

    public void ResetYawPitchLocks()
    {
        yawAccum = 0f; pitchAccum = 0f; axisLock = WPAxisLock.None;
        ApplyAccumulatedRotationStable();
        if (dock) dock.SetAxisLock(axisLock);
        UpdateHandleLocks();
    }

    void EnsureMaterials()
    {
        // Board: chọn shader theo toggle (tắt mờ rìa => Unlit/Texture)
        Shader sBoard = useBoardEdgeFeather ? Shader.Find("Unlit/WorldPanelBoard") : Shader.Find("Unlit/Texture");
        if (sBoard == null) sBoard = Shader.Find("Unlit/Texture");
        _panelMat ??= new Material(sBoard);

        // Tray
        var sRounded = Shader.Find("Unlit/WorldPanelRounded");
        if (sRounded != null && sRounded.isSupported)
            _trayMat = (_trayMat && _trayMat.shader == sRounded) ? _trayMat : new Material(sRounded);
        else
            _trayMat = new Material(Shader.Find("Unlit/Transparent"));

        // Dash
        var sDash = Shader.Find("Unlit/WorldPanelDash");
        _dashMat = (sDash != null && sDash.isSupported)
            ? (_dashMat && _dashMat.shader == sDash ? _dashMat : new Material(sDash))
            : new Material(Shader.Find("Unlit/Color"));
    }

    void UpdateTrayScaleAndMaterial()
    {
        if (!tray) return;

        var mr = tray.GetComponent<MeshRenderer>();
        if (!mr) mr = tray.gameObject.AddComponent<MeshRenderer>();

        EnsureMaterials();

        if (mr.sharedMaterial == null)
            mr.sharedMaterial = _trayMat != null ? new Material(_trayMat) : new Material(Shader.Find("Unlit/Transparent"));

        var mat = mr.sharedMaterial;
        if (mat == null)
        {
            mat = new Material(Shader.Find("Unlit/Transparent"));
            mr.sharedMaterial = mat;
        }

        float sign = -1f;
        var cam = Camera.main;
        if (cam)
        {
            Vector3 toCam = cam.transform.position - transform.position;
            sign = Vector3.Dot(transform.forward, toCam) > 0f ? -1f : 1f;
        }
        tray.localPosition = new Vector3(0, 0, sign * trayBehind);

        tray.localScale = new Vector3(width + trayPadding * 2f, height + trayPadding * 2f, 1);

        if (mat.HasProperty("_MaskEnable")) mat.SetFloat("_MaskEnable", 0f);

        if (mat.HasProperty("_FillColor"))
        {
            mat.SetColor("_FillColor", new Color(trayFill.r, trayFill.g, trayFill.b, trayFill.a * _trayAlpha));
            mat.SetColor("_BorderColor", new Color(trayBorderColor.r, trayBorderColor.g, trayBorderColor.b, trayBorderColor.a * _trayAlpha));
            if (mat.HasProperty("_Radius")) mat.SetFloat("_Radius", trayCornerRadius);
            if (mat.HasProperty("_Border")) mat.SetFloat("_Border", trayBorder);
            if (mat.HasProperty("_Feather")) mat.SetFloat("_Feather", 0.003f);
        }
        else
        {
            if (mat.shader != Shader.Find("Unlit/Transparent"))
                mat.shader = Shader.Find("Unlit/Transparent");
            mat.color = new Color(trayFill.r, trayFill.g, trayFill.b, trayFill.a * _trayAlpha);
        }

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        _mpb.SetFloat("_MaskEnable", 0f);
        mr.SetPropertyBlock(_mpb);
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
        if (!generateHints) on = false; // guard cứng
        foreach (Transform c in hintsParent)
        {
            var mr = c.GetComponent<MeshRenderer>();
            if (mr) mr.enabled = on;
        }
    }

    void CreateHandlesConditional()
    {
        float TrayW = width + trayPadding * 2f;
        float TrayH = height + trayPadding * 2f;
        float thw = TrayW * 0.5f, thh = TrayH * 0.5f;

        // Luôn tạo Center (collider disable & IgnoreRaycast như cũ)
        CreateHandle("Handle_Center", WPHandleType.Center, Vector3.zero,
            new Vector2(TrayW - handleEdgeThickness * 2f, TrayH - handleEdgeThickness * 2f));

        // Nếu muốn edges => có thể mở thêm (đang tắt mặc định)
        if (generateHandles)
        {
            // float edgeLenW = TrayW * edgeActivePercent;
            // float edgeLenH = TrayH * edgeActivePercent;
            // CreateHandle("Handle_Top", WPHandleType.EdgeTop,    new Vector3(0, thh, 0),    new Vector2(edgeLenW, handleEdgeThickness));
            // CreateHandle("Handle_Bottom", WPHandleType.EdgeBottom,new Vector3(0, -thh, 0),  new Vector2(edgeLenW, handleEdgeThickness));
            // CreateHandle("Handle_Left", WPHandleType.EdgeLeft,  new Vector3(-thw, 0, 0),   new Vector2(handleEdgeThickness, edgeLenH));
            // CreateHandle("Handle_Right", WPHandleType.EdgeRight,new Vector3(thw, 0, 0),    new Vector2(handleEdgeThickness, edgeLenH));
        }

        if (generateCornerHandles)
        {
            CreateHandle("Handle_TL", WPHandleType.CornerTL, new Vector3(-thw, thh, 0), new Vector2(cornerHandleSize, cornerHandleSize));
            CreateHandle("Handle_TR", WPHandleType.CornerTR, new Vector3(thw, thh, 0), new Vector2(cornerHandleSize, cornerHandleSize));
            CreateHandle("Handle_BL", WPHandleType.CornerBL, new Vector3(-thw, -thh, 0), new Vector2(cornerHandleSize, cornerHandleSize));
            CreateHandle("Handle_BR", WPHandleType.CornerBR, new Vector3(thw, -thh, 0), new Vector2(cornerHandleSize, cornerHandleSize));
        }
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
            box.enabled = false;
            int lr = LayerMask.NameToLayer("Ignore Raycast");
            if (lr >= 0) go.layer = lr;
        }

        // Nếu không bật generateHandles/generateCornerHandles thì các handle (trừ Center) sẽ không được tạo
        var h = go.AddComponent<WorldPanelPlusHandle>();
        h.type = type; h.panel = this;
    }

    public Material GetTrayMaterial()
    {
        var mr = tray ? tray.GetComponent<MeshRenderer>() : null;
        return mr ? mr.sharedMaterial : _trayMat;
    }

    public void Apply()
    {
        // Nếu thiếu những thành phần cơ bản thì không làm gì cả
        if (!board || !tray) return;

        // BẢO ĐẢM shader/material LUÔN sẵn sàng trước khi set vào renderer
        EnsureMaterials();

#if UNITY_EDITOR
        if (_shaderPropertiesNeedUpdate)
        {
            UpdateBoardShaderProperties();
            _shaderPropertiesNeedUpdate = false;
        }
#else
        UpdateBoardShaderProperties();
#endif

        // --- Board ---
        if (board)
        {
            board.localScale = new Vector3(width, height, 1);

            var mr = board.GetComponent<MeshRenderer>();
            if (mr)
            {
                // Bảo đảm luôn có material
                var mat = GetPanelMaterial();
                if (mr.sharedMaterial != mat) mr.sharedMaterial = mat;

                // Bảo vệ truy cập null
                if (mr.sharedMaterial != null)
                {
                    mr.sharedMaterial.mainTexture = contentTexture;
                    mr.enabled = boardVisible;
                    ApplyBoardTint(mr);
                }
            }

            var col = board.GetComponent<BoxCollider>();
            if (col) { col.size = new Vector3(1, 1, 0.02f); col.center = new Vector3(0, 0, 0.01f); }
        }

        UpdateTrayScaleAndMaterial();

        float TrayW = width + trayPadding * 2f;
        float TrayH = height + trayPadding * 2f;

        // --- Hover collider (bảo đảm tồn tại) ---
        if (hover)
        {
            var bc = hover.GetComponent<BoxCollider>();
            if (!bc) bc = hover.gameObject.AddComponent<BoxCollider>(); // đảm bảo có collider
            float dockH = (dock != null) ? dock.GetBackplateHeight() : 0f;
            float extra = trayHoverExtra;
            bc.size = new Vector3(
                TrayW + extra * 2f,
                TrayH + extra * 2f + dockH + extra,
                handleDepth
            );
            bc.center = new Vector3(0, -(dockH + extra) * 0.5f, handleDepth * 0.5f);
        }

        if (dock) dock.RecomputeFromPanel();
        UpdateHandlesLayout();
    }

    void ApplyBoardTint(MeshRenderer mr)
    {
        if (!mr) return;
        var tint = panelTint;
        tint.a = boardVisible ? Mathf.Clamp01(boardAlpha) : 0f;

        if (mr.sharedMaterial.HasProperty("_Color"))
            mr.sharedMaterial.SetColor("_Color", tint);
        else
            mr.sharedMaterial.color = tint;

        mr.sharedMaterial.mainTexture = contentTexture;
        mr.sortingOrder = 0;
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
                    col.enabled = true; break;
                case WPAxisLock.YawOnly:
                    col.enabled = !isPitchEdge; break;
                case WPAxisLock.PitchOnly:
                    col.enabled = !isYawEdge; break;
            }
        }
    }

    void UpdateHandlesLayout()
    {
        if (!handlesParent) return;

        float TrayW = width + trayPadding * 2f;
        float TrayH = height + trayPadding * 2f;
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

                case WPHandleType.CornerTL:
                    if (!generateCornerHandles) { t.gameObject.SetActive(false); break; }
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
                    if (!generateCornerHandles) { t.gameObject.SetActive(false); break; }
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
                    if (!generateCornerHandles) { t.gameObject.SetActive(false); break; }
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
                    if (!generateCornerHandles) { t.gameObject.SetActive(false); break; }
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

                    // Nếu sau này bật edge handles, bổ sung case tương tự.
            }
        }
    }

    void Update()
    {
        float targetAlpha = forceTrayHidden ? 0f : (_wantHover ? 1f : 0f);
        float speed = (targetAlpha > _trayAlpha) ? FadeSpeedIn : FadeSpeedOut;
        _trayAlpha = Mathf.MoveTowards(_trayAlpha, targetAlpha, speed * Time.deltaTime);

        UpdateTrayScaleAndMaterial();

        if (hover)
        {
            var bc = hover.GetComponent<BoxCollider>();
            if (bc) bc.enabled = !forceTrayHidden;
        }

        if (forceTrayHidden) SetHintsActive(false);

        bool showMarkers = generateHandleSpheres && !forceTrayHidden && _trayAlpha > 0.02f;
        if (handlesParent)
        {
            foreach (Transform t in handlesParent)
            {
                var mk = t.GetComponent<WorldPanelPlusHandleSphere>();
                if (mk) mk.SetVisible(showMarkers);
            }
        }
        LateUpdate();
    }

    void LateUpdate()
    {
        if (!hover || !hoverWorldAligned) return;

        var cam = Camera.main;
        if (cam)
        {
            if (hoverFaceCameraYawOnly)
            {
                Vector3 toCam = cam.transform.position - transform.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-6f)
                    hover.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }
            else
            {
                hover.rotation = Quaternion.identity;
            }
        }
        else
        {
            hover.rotation = Quaternion.identity;
        }
    }

    public void OnHover(bool on, WPHandleType? handleType = null)
    {
        if (forceTrayHidden) return;
        _wantHover = on;
        SetHintsActive(false); // luôn off trừ khi generateHints bật và bạn tự show
    }

    void UpdateBoardShaderProperties()
    {
        if (_panelMat != null)
        {
            // màu/alpha Board theo panelTint & boardAlpha
            var tint = panelTint; tint.a = boardVisible ? Mathf.Clamp01(boardAlpha) : 0f;
            if (_panelMat.HasProperty("_Color")) _panelMat.SetColor("_Color", tint);
            else _panelMat.color = tint;

            // Chỉ set tham số mờ rìa khi đang dùng shader có mờ rìa
            if (useBoardEdgeFeather && _panelMat.shader != null && _panelMat.shader.name == "Unlit/WorldPanelBoard")
            {
                if (_panelMat.HasProperty("_PanelSize"))
                    _panelMat.SetVector("_PanelSize", new Vector4(width, height, 0, 0));
                if (_panelMat.HasProperty("_CornerRadius"))
                    _panelMat.SetFloat("_CornerRadius", trayCornerRadius);
                if (_panelMat.HasProperty("_EdgeFeather"))
                    _panelMat.SetFloat("_EdgeFeather", 0.003f);
            }

            if (_panelMat.HasProperty("_Surface")) _panelMat.SetFloat("_Surface", 1f); // Transparent (URP)
            _panelMat.renderQueue = 3000;
        }
    }

    // === NO-ROLL helpers ===
    public void EnforceNoRoll()
    {
        var f = transform.forward;
        if (f.sqrMagnitude < 1e-6f) return;
        transform.rotation = Quaternion.LookRotation(f, Vector3.up); // z=0
    }
    static Quaternion NoRollOf(Quaternion q)
    {
        var f = q * Vector3.forward;
        if (f.sqrMagnitude < 1e-6f) return q;
        return Quaternion.LookRotation(f, Vector3.up); // z=0
    }

    // === Xoay giữ Dock đứng yên + bảo toàn gap Y giữa Tray và Dock ===
    void ApplyAccumulatedRotationStable()
    {
        var cam = Camera.main;
        var dock = this.dock;
        Vector3 pivot = dock ? dock.transform.position : transform.position;

        // Lưu gap Y hiện tại để phục hồi sau xoay
        float gapY0 = 0f;
        if (dock)
        {
            float trayBottom = GetTrayBottomYWorld(cam);
            float dockTop = dock.transform.TransformPoint(0, dock.GetBackplateHeight() * 0.5f, 0).y;
            gapY0 = trayBottom - dockTop;
        }

        // Tính rotation mới theo yaw/pitch accum trên basis
        Quaternion q = Quaternion.AngleAxis(yawAccum, _rotBasis * Vector3.up)
                     * Quaternion.AngleAxis(pitchAccum, _rotBasis * Vector3.right)
                     * _rotBasis;
        q = NoRollOf(q); // chặn roll tuyệt đối

        // Quay panel quanh pivot (Dock) thay vì quanh tâm panel
        Quaternion delta = q * Quaternion.Inverse(transform.rotation);
        Vector3 r = transform.position - pivot;
        transform.position = pivot + delta * r;
        transform.rotation = q;

        // Giữ Dock đứng yên đúng worldPos pivot và phục hồi gap Y
        if (dock)
        {
            dock.AnchorToWorld(pivot, cam);
            float trayBottom = GetTrayBottomYWorld(cam);
            float dockTop = dock.transform.TransformPoint(0, dock.GetBackplateHeight() * 0.5f, 0).y;
            float dY = gapY0 - (trayBottom - dockTop);
            if (Mathf.Abs(dY) > 1e-6f)
            {
                transform.position += Vector3.up * dY;
                dock.AnchorToWorld(pivot, cam);
            }
        }

        UpdateTrayScaleAndMaterial();
    }
}