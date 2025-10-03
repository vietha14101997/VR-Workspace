using UnityEngine;
using System.Collections.Generic;

public enum WPHandleType
{
    Center, CornerTL, CornerTR, CornerBL, CornerBR
}

[ExecuteAlways]
public class WorldPanelPlus : MonoBehaviour
{
    [Header("Build Toggles")]
    public bool generateHandles = false;
    public bool generateCornerHandles = true;
    public bool generateHandleSpheres = true;
    [Tooltip("Hiện marker cả ở Center handle")]
    public bool markerIncludeCenter = true;
    public bool generateHints = false;
    public bool useBoardEdgeFeather = true;

    [Header("Hover behaviour")]
    public bool hoverWorldAligned = true;
    public bool hoverFaceCameraYawOnly = true;

    [Header("Move (Center handle)")]
    public bool moveOrbitCamera = true;
    public float moveOrbitLerp = 12f;
    public float moveOrbitMin = 0.25f;
    public float moveOrbitMax = 6.0f;

    [Header("Panel (content) size, meters")]
    public float width = 1.366f;
    public float height = 0.768f;

    [Header("Tray visuals")]
    public float trayPadding = 0.05f;
    public float trayHoverExtra = 0.025f;
    public float trayBehind = 0.02f;
    public float trayCornerRadius = 0.03f;
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
    public float cornerHandleSize = 0.06f;  // resize
    [Range(0.2f, 1f)] public float edgeActivePercent = 0.5f;

    [Header("Generated")]
    public Transform board;
    public Transform tray;
    public Transform hintsParent;
    public Transform handlesParent;
    public Transform hover;
    public WorldPanelPlusControlDock dock;

    [Header("Cursor")]
    public bool cursorEnable = true;
    public float cursorSpeedPerPixel = 0.0015f;   // độ nhạy khi dịch chuyển bằng chuột (ΔUV mỗi pixel)
    public Texture2D cursorTexture;               // để trống thì sẽ nạp Resources/icon_cursor
    public bool cursorInvertY = false;

    [HideInInspector] public WorldPanelCursor cursor;

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
    [SerializeField, HideInInspector] private Material _fallbackPanelMat;

    [Header("Cluster linking (optional)")]
    public WorldPanelPlus neighborLeft;
    public WorldPanelPlus neighborRight;
    public WorldPanelPlus neighborUp;
    public WorldPanelPlus neighborDown;

    static Texture GetSafeTex(Texture t) => t ? t : Texture2D.blackTexture;

    void EnsureDefaultContentTexture()
    {
        if (contentTexture == null) contentTexture = Texture2D.blackTexture;
    }

    // gọi khi panel này sắp nhận con trỏ từ panel khác
    public void CursorFocusStealFrom(WorldPanelPlus fromPanel, float startU01, float startV01)
    {
        EnsureCursor();
        CursorFocusBegin(board.TransformPoint(new Vector3(startU01 - 0.5f, startV01 - 0.5f, 0)), Camera.main);
    }

    // Duyệt BFS qua các neighbor để kiểm tra 2 panel có cùng cụm không
    public static bool InSameCluster(WorldPanelPlus a, WorldPanelPlus b, int maxHops = 32)
    {
        if (!a || !b) return false;
        if (a == b) return true;

        var visited = new HashSet<WorldPanelPlus>();
        var q = new Queue<WorldPanelPlus>();
        q.Enqueue(a); visited.Add(a);

        int hops = 0;
        while (q.Count > 0 && hops++ < maxHops)
        {
            var p = q.Dequeue();
            if (!p) continue;
            if (p == b) return true;

            void Push(WorldPanelPlus n)
            {
                if (n != null && !visited.Contains(n)) { visited.Add(n); q.Enqueue(n); }
            }
            Push(p.neighborLeft);
            Push(p.neighborRight);
            Push(p.neighborUp);
            Push(p.neighborDown);
        }
        return false;
    }

    public void EnsureCursor()
    {
        if (!cursorEnable) { if (cursor) cursor.SetVisible(false); return; }
        if (!board) return;
        if (cursor == null)
        {
            var go = new GameObject("Cursor");
            go.transform.SetParent(board, false);
            cursor = go.AddComponent<WorldPanelCursor>();
            if (cursorTexture) cursor.cursorTexture = cursorTexture;
        }
        cursor.AttachToBoard(board, width, height);
        cursor.SetVisible(false);
    }

    public void CursorFocusBegin(Vector3 worldHit, Camera cam)
    {
        if (!cursorEnable) return;
        EnsureCursor();
        if (!cursor) return;

        // Tính UV từ điểm va chạm lên Board
        Vector3 local = board.InverseTransformPoint(worldHit);  // x,y ∈ [-0.5..0.5]
        float u = Mathf.Clamp01(local.x + 0.5f);
        float v = Mathf.Clamp01(local.y + 0.5f);
        cursor.SetUV(u, v, silent: true);
        cursor.SetVisible(true);
    }

    public void CursorFocusEnd()
    {
        if (cursor) cursor.SetVisible(false);
    }

    public void CursorMoveByMouseDelta(float dxPixel, float dyPixel)
    {
        if (!cursor || !cursor.visible) return;
        float du = dxPixel * cursorSpeedPerPixel;
        float signY = cursorInvertY ? -1f : 1f;
        float dv = dyPixel * cursorSpeedPerPixel * signY;
        cursor.NudgeUV(du, dv);
    }

    /// Di chuyển con trỏ bằng delta pixel, có hỗ trợ nhảy qua láng giềng trong cụm.
    /// - active sẽ có thể bị đổi sang panel láng giềng (ref).
    public static void CursorMoveInCluster(ref WorldPanelPlus active, float dxPixel, float dyPixel)
    {
        if (!active || !active.cursor || !active.cursor.visible) return;

        float du = dxPixel * active.cursorSpeedPerPixel;
        float signY = active.cursorInvertY ? -1f : 1f;
        float dv = dyPixel * active.cursorSpeedPerPixel * signY;

        // Giá trị mới (chưa clamp)
        float newU = active.cursor.u + du;
        float newV = active.cursor.v + dv;

        bool goLeft = newU < 0f;
        bool goRight = newU > 1f;
        bool goDown = newV < 0f;
        bool goUp = newV > 1f;

        // Nếu chưa vượt biên: cập nhật bình thường
        if (!goLeft && !goRight && !goDown && !goUp)
        {
            active.cursor.SetUV(Mathf.Clamp01(newU), Mathf.Clamp01(newV));
            return;
        }

        // Ưu tiên trục có overflow lớn hơn (tránh kẹt ở góc)
        float overX = Mathf.Max(-newU, newU - 1f, 0f); // lượng vượt biên theo X (dương)
        float overY = Mathf.Max(-newV, newV - 1f, 0f); // theo Y

        // Thử theo trục lớn hơn trước
        if (overX >= overY)
        {
            if (goLeft && SwitchNeighbor(ref active, active.neighborLeft, 1f + newU, Mathf.Clamp01(newV))) return;
            if (goRight && SwitchNeighbor(ref active, active.neighborRight, newU - 1f, Mathf.Clamp01(newV))) return;
            if (goDown && SwitchNeighbor(ref active, active.neighborDown, Mathf.Clamp01(newU), 1f + newV)) return;
            if (goUp && SwitchNeighbor(ref active, active.neighborUp, Mathf.Clamp01(newU), newV - 1f)) return;
        }
        else
        {
            if (goDown && SwitchNeighbor(ref active, active.neighborDown, Mathf.Clamp01(newU), 1f + newV)) return;
            if (goUp && SwitchNeighbor(ref active, active.neighborUp, Mathf.Clamp01(newU), newV - 1f)) return;
            if (goLeft && SwitchNeighbor(ref active, active.neighborLeft, 1f + newU, Mathf.Clamp01(newV))) return;
            if (goRight && SwitchNeighbor(ref active, active.neighborRight, newU - 1f, Mathf.Clamp01(newV))) return;
        }

        // Không có láng giềng theo hướng vượt biên -> kẹp trong panel hiện tại
        active.cursor.SetUV(Mathf.Clamp01(newU), Mathf.Clamp01(newV));
    }

    static bool SwitchNeighbor(ref WorldPanelPlus active, WorldPanelPlus next, float u01, float v01)
    {
        if (next == null) return false;

        // Ẩn cursor panel cũ
        if (active && active.cursor) active.cursor.SetVisible(false);

        // Bật ở panel mới & đặt UV đầu vào (clamp để an toàn)
        next.EnsureCursor();
        next.cursor.SetVisible(true);
        next.cursor.SetUV(Mathf.Clamp01(u01), Mathf.Clamp01(v01), silent: true);

        active = next; // đổi panel đang active
        return true;
    }

    public void CursorClickDown() { if (cursor && cursor.visible) cursor.ClickDown(); }
    public void CursorClickUp() { if (cursor && cursor.visible) cursor.ClickUp(); }

    private Material GetPanelMaterial()
    {
        if (_panelMat != null) return _panelMat;
        if (_fallbackPanelMat == null)
        {
            var sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Sprites/Default");
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
        EnsureMaterials();
        if (!board || !tray) return;
        Apply();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        EnsureDefaultContentTexture();
        var kill = new List<GameObject>();
        foreach (Transform c in transform) kill.Add(c.gameObject);
        foreach (var go in kill) { if (Application.isEditor) DestroyImmediate(go); else Destroy(go); }

        EnsureMaterials();

        // === Board ===
        board = CreateQuad("Board", _panelMat).transform;
        board.localScale = new Vector3(width, height, 1);
        var mrBoard = board.GetComponent<MeshRenderer>();
        mrBoard.sharedMaterial = _panelMat;
        mrBoard.sharedMaterial.mainTexture = GetSafeTex(contentTexture);
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
        SetHintsActive(false);

        // === Handles ===
        handlesParent = new GameObject("Handles").transform; handlesParent.SetParent(transform, false);
        CreateHandlesConditional();

        EnsureHandleSpheresAttached();

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
        EnsureCursor();
    }

    public float GetTrayAlpha01() => forceTrayHidden ? 0f : _trayAlpha;

    void EnsureMaterials()
    {
        Shader sBoard = useBoardEdgeFeather ? Shader.Find("Unlit/WorldPanelBoard") : Shader.Find("Unlit/Texture");
        if (sBoard == null) sBoard = Shader.Find("Unlit/Texture");
        _panelMat ??= new Material(sBoard);

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

    void EnsureHandleSpheresAttached()
    {
        if (!handlesParent) return;

        foreach (Transform t in handlesParent)
        {
            var h = t.GetComponent<WorldPanelPlusHandle>();
            if (!h) continue;

            // Nếu đang tắt tính năng -> ẩn marker nếu có
            if (!generateHandleSpheres)
            {
                var mkOff = t.GetComponent<WorldPanelPlusHandleSphere>();
                if (mkOff) mkOff.SetVisible(false);
                continue;
            }

            // Nếu không muốn hiện ở Center
            if (!markerIncludeCenter && h.type == WPHandleType.Center)
            {
                var mkOff = t.GetComponent<WorldPanelPlusHandleSphere>();
                if (mkOff) mkOff.SetVisible(false);
                continue;
            }

            // Gắn marker nếu thiếu
            if (!t.GetComponent<WorldPanelPlusHandleSphere>())
                t.gameObject.AddComponent<WorldPanelPlusHandleSphere>();
        }
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

    void SetHintsActive(bool on)
    {
        if (!hintsParent) return;
        if (!generateHints) on = false;
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

        // CENTER HANDLE: đặt giữa cạnh TOP của Tray
        CreateHandle("Handle_Center", WPHandleType.Center, new Vector3(0, thh, 0), new Vector2(cornerHandleSize, cornerHandleSize));

        // Corner handles để RESIZE (uniform)
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

        // Center giữ collider bật và layer mặc định để nhận raycast
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
        EnsureDefaultContentTexture();
        ApplyBoardTint(board.GetComponent<MeshRenderer>());
        if (!board || !tray) return;

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

        if (board)
        {
            board.localScale = new Vector3(width, height, 1);

            var mr = board.GetComponent<MeshRenderer>();
            if (mr)
            {
                var mat = GetPanelMaterial();
                if (mr.sharedMaterial != mat) mr.sharedMaterial = mat;
                if (mr.sharedMaterial != null)
                {
                    mr.sharedMaterial.mainTexture = GetSafeTex(contentTexture);
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

        if (hover)
        {
            var bc = hover.GetComponent<BoxCollider>();
            if (!bc) bc = hover.gameObject.AddComponent<BoxCollider>();
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
        EnsureCursor();
    }

    void ApplyBoardTint(MeshRenderer mr)
    {
        if (!mr) return;

        // Chọn shader: Opaque nếu alpha~1, Transparent nếu <1
        var shOpaque = Shader.Find("Unlit/Texture");
        var shTrans = Shader.Find("Unlit/Transparent");

        float a = Mathf.Clamp01(boardAlpha);       // alpha global của board
        bool opaque = a >= 0.999f;

        var mat = mr.sharedMaterial;
        if (!mat)
            mat = mr.sharedMaterial = new Material(opaque ? shOpaque : shTrans);
        else if (mat.shader != (opaque ? shOpaque : shTrans))
            mat.shader = (opaque ? shOpaque : shTrans);

        // Texture luôn hợp lệ (UnityBlack nếu None)
        mat.mainTexture = GetSafeTex(contentTexture);

        // Màu tint: dùng panelTint nhưng ép alpha = boardAlpha
        var c = panelTint;
        c.a = a;
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);

        // Các tuỳ chọn thêm (an toàn)
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        // renderQueue để mặc định theo shader (Opaque/Transparent) là đủ
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
                    t.localPosition = new Vector3(0, thh, 0);
                    {
                        var box = t.GetComponent<BoxCollider>();
                        if (box)
                        {
                            box.size = new Vector3(cornerHandleSize, cornerHandleSize, handleDepth);
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
            }
        }
    }

    public void SetHandlesCenterOnly(bool centerOnly)
    {
        if (!handlesParent) return;
        var hs = handlesParent.GetComponentsInChildren<WorldPanelPlusHandle>(true);
        foreach (var h in hs)
        {
            // Nếu enum của bạn khác tên, chỉ cần sửa điều kiện dưới
            bool isCenter = (h.type == WPHandleType.Center);
            h.gameObject.SetActive(centerOnly ? isCenter : true);
        }

        // Nếu có Hint/hover riêng cho các góc/cạnh, có thể ẩn luôn:
        if (hintsParent) hintsParent.gameObject.SetActive(!centerOnly);
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
        EnsureHandleSpheresAttached();

        if (handlesParent)
        {
            foreach (Transform t in handlesParent)
            {
                var h = t.GetComponent<WorldPanelPlusHandle>();
                if (!h) continue;

                var mk = t.GetComponent<WorldPanelPlusHandleSphere>();
                if (mk)
                {
                    bool allowThis = markerIncludeCenter || h.type != WPHandleType.Center;
                    mk.SetVisible(showMarkers && allowThis);
                }
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
        SetHintsActive(false);
    }

    void UpdateBoardShaderProperties()
    {
        if (_panelMat != null)
        {
            var tint = panelTint; tint.a = boardVisible ? Mathf.Clamp01(boardAlpha) : 0f;
            if (_panelMat.HasProperty("_Color")) _panelMat.SetColor("_Color", tint);
            else _panelMat.color = tint;

            if (useBoardEdgeFeather && _panelMat.shader != null && _panelMat.shader.name == "Unlit/WorldPanelBoard")
            {
                if (_panelMat.HasProperty("_PanelSize"))
                    _panelMat.SetVector("_PanelSize", new Vector4(width, height, 0, 0));
                if (_panelMat.HasProperty("_CornerRadius"))
                    _panelMat.SetFloat("_CornerRadius", trayCornerRadius);
                if (_panelMat.HasProperty("_EdgeFeather"))
                    _panelMat.SetFloat("_EdgeFeather", 0.003f);
            }

            if (_panelMat.HasProperty("_Surface")) _panelMat.SetFloat("_Surface", 1f);
            _panelMat.renderQueue = 3000;
        }
    }

    public void EnforceNoRoll()
    {
        var f = transform.forward;
        if (f.sqrMagnitude < 1e-6f) return;
        transform.rotation = Quaternion.LookRotation(f, Vector3.up);
    }
}
