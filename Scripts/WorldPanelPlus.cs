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
    public float yawAccum = 0f;
    public float pitchAccum = 0f;
    public WPAxisLock axisLock = WPAxisLock.None;

    Quaternion _rotBasis;

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
    public float trayPadding = 0.025f;
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
    public bool boardVisible = true;               // NEW: cho phép bật/tắt Board
    [Range(0, 1)] public float boardAlpha = 1f;     // NEW: alpha riêng cho Board

    Material _panelMat, _trayMat, _dashMat;
    float FadeSpeedIn => 1f / Mathf.Max(0.0001f, trayFadeInDuration);
    float FadeSpeedOut => 1f / Mathf.Max(0.0001f, trayFadeOutDuration);
    MaterialPropertyBlock _mpb;

    [HideInInspector] public bool dockMinimized = false;
    [HideInInspector] public bool forceTrayHidden = false;

    [HideInInspector] public bool isResizing = false;

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
        if (!board || !tray)
        {
            EnsureMaterials();
            return;
        }
        Apply();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        var kill = new List<GameObject>();
        foreach (Transform c in transform) kill.Add(c.gameObject);
        foreach (var go in kill) { if (Application.isEditor) DestroyImmediate(go); else Destroy(go); }

        EnsureMaterials();

        // === Board (hiển thị texture) ===
        board = CreateQuad("Board", _panelMat).transform;
        board.localScale = new Vector3(width, height, 1);
        var mrBoard = board.GetComponent<MeshRenderer>();
        mrBoard.sharedMaterial = _panelMat;
        mrBoard.sharedMaterial.mainTexture = contentTexture;
        mrBoard.enabled = boardVisible;

        // màu + alpha theo panelTint & boardAlpha
        ApplyBoardTint(mrBoard);

        var bc = board.gameObject.AddComponent<BoxCollider>();
        bc.size = new Vector3(1, 1, 0.02f);
        bc.center = new Vector3(0, 0, 0.01f);
        var brc = board.gameObject.AddComponent<WorldPanelPlusBoardRaycatcher>(); brc.panel = this;

        // Tray (behind)
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

        // Marker spheres (except center)
        foreach (Transform t in handlesParent)
        {
            var h = t.GetComponent<WorldPanelPlusHandle>();
            if (!h || h.type == WPHandleType.Center) continue;
            if (!t.GetComponent<WorldPanelPlusHandleSphere>()) t.gameObject.AddComponent<WorldPanelPlusHandleSphere>();
        }

        // Hover collider (covers tray + dock)
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
        if (axisLock == WPAxisLock.PitchOnly) return;
        yawAccum = Mathf.Clamp(yawAccum + deg, -45f, 45f);
        axisLock = Mathf.Approximately(yawAccum, 0f)
            ? (Mathf.Approximately(pitchAccum, 0f) ? WPAxisLock.None : WPAxisLock.PitchOnly)
            : WPAxisLock.YawOnly;
        ApplyAccumulatedRotation();
        if (dock) dock.SetAxisLock(axisLock);
        UpdateHandleLocks();
    }

    public void AddPitchClamped(float deg)
    {
        if (axisLock == WPAxisLock.YawOnly) return;
        pitchAccum = Mathf.Clamp(pitchAccum + deg, -45f, 45f);
        axisLock = Mathf.Approximately(pitchAccum, 0f)
            ? (Mathf.Approximately(yawAccum, 0f) ? WPAxisLock.None : WPAxisLock.YawOnly)
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
        Quaternion q = Quaternion.AngleAxis(yawAccum, _rotBasis * Vector3.up)
                     * Quaternion.AngleAxis(pitchAccum, _rotBasis * Vector3.right)
                     * _rotBasis;
        transform.rotation = q;
    }

    void EnsureMaterials()
    {
        var boardShader = Shader.Find("Unlit/WorldPanelBoard");
        if (_panelMat == null)
        {
            _panelMat = new Material(boardShader != null ? boardShader : Shader.Find("Unlit/Texture"));
            // if (boardShader != null)
            // {
            //     _panelMat.SetFloat("_EdgeFadeX", 0f);
            //     _panelMat.SetFloat("_EdgeFadeY", 0f);
            //     _panelMat.SetFloat("_EdgeFade", 0f);
            //     _panelMat.SetFloat("_FadeAmount", 0f);
            // }
        }

        var sRounded = Shader.Find("Unlit/WorldPanelRounded");
        if (sRounded != null && sRounded.isSupported)
            _trayMat = (_trayMat && _trayMat.shader == sRounded) ? _trayMat : new Material(sRounded);
        else
            _trayMat = new Material(Shader.Find("Unlit/Transparent"));

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
            box.enabled = false;
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
        }
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
        if (!board || !tray) return;

#if UNITY_EDITOR
        if (_shaderPropertiesNeedUpdate)
        {
            UpdateBoardShaderProperties();
            _shaderPropertiesNeedUpdate = false;
        }
#else
        UpdateBoardShaderProperties();
#endif

        if (board)
        {
            board.localScale = new Vector3(width, height, 1);
            var mr = board.GetComponent<MeshRenderer>();
            if (mr)
            {
                mr.sharedMaterial = _panelMat;
                mr.sharedMaterial.mainTexture = contentTexture;
                mr.enabled = boardVisible;
                ApplyBoardTint(mr);
            }
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

        // Hover collider covers Dock too
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

    void UpdateBoardShaderProperties()
    {
        if (_panelMat != null)
        {
            // màu/alpha Board theo panelTint & boardAlpha
            var tint = panelTint; tint.a = boardVisible ? Mathf.Clamp01(boardAlpha) : 0f;
            if (_panelMat.HasProperty("_Color")) _panelMat.SetColor("_Color", tint);
            else _panelMat.color = tint;

            if (_panelMat.shader != null && _panelMat.shader.name == "Unlit/WorldPanelBoard")
            {
                float thick = 0.05f;
                float thin = 0.03f;

                bool widthIsLonger = width >= height;
                float edgeX = widthIsLonger ? thin : thick;
                float edgeY = widthIsLonger ? thick : thin;

                if (_panelMat.HasProperty("_EdgeFadeX")) _panelMat.SetFloat("_EdgeFadeX", edgeX);
                if (_panelMat.HasProperty("_EdgeFadeY")) _panelMat.SetFloat("_EdgeFadeY", edgeY);
                if (_panelMat.HasProperty("_EdgeMinAlpha")) _panelMat.SetFloat("_EdgeMinAlpha", 0.40f);

                if (_panelMat.HasProperty("_PanelSize"))
                    _panelMat.SetVector("_PanelSize", new Vector4(width, height, 0, 0));
            }

            if (_panelMat.HasProperty("_Surface")) _panelMat.SetFloat("_Surface", 1f); // Transparent (URP)
            _panelMat.renderQueue = 3000;
        }
    }

#if UNITY_EDITOR
    private bool _shaderPropertiesNeedUpdate = true;
#endif
}
