using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class WorldPanelPlus : MonoBehaviour
{
    [Header("Panel size (meters)")]
    public float width = 1.600f;
    public float height = 0.9f;

    [Header("Board visuals")]
    public Texture contentTexture;
    public Color panelTint = Color.white;
    public bool boardVisible = true;
    [Range(0, 1)] public float boardAlpha = 1f;

    [Header("Board edge feather (rounded border)")]
    public bool useBoardEdgeFeather = true;
    public Color boardEdgeColor = Color.white;
    [Range(0, 0.25f)] public float boardEdgeWidthUV = 0.02f;
    [Range(0, 1f)] public float boardEdgeMinAlpha = 0.10f;
    [Range(0, 1f)] public float boardEdgeSoften = 0.35f;
    public bool boardEdgeRespectContentAlpha = true;
    [Range(0, 0.1f)] public float boardCornerRadius = 0.03f;

    [Header("Cursor")]
    public bool cursorEnable = true;
    public float cursorSpeedPerPixel = 0.0015f;
    public Texture2D cursorTexture;
    public bool cursorInvertY = false;

    [HideInInspector] public WorldPanelCursor cursor;

    [Header("Cluster linking (optional)")]
    public WorldPanelPlus neighborLeft;
    public WorldPanelPlus neighborRight;
    public WorldPanelPlus neighborUp;
    public WorldPanelPlus neighborDown;

    [Header("Generated")]
    public Transform board;

    Material _panelMat;
    [SerializeField, HideInInspector] private Material _fallbackPanelMat;

    static Texture GetSafeTex(Texture t) => t ? t : Texture2D.blackTexture;

    void EnsureDefaultContentTexture()
    {
        if (contentTexture == null) contentTexture = Texture2D.blackTexture;
    }

    #region Cursor Methods

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
        if (cursor) cursor.SetVisible(cursor.visible);
    }

    public void CursorFocusStealFrom(WorldPanelPlus fromPanel, float startU01, float startV01)
    {
        EnsureCursor();
        CursorFocusBegin(board.TransformPoint(new Vector3(startU01 - 0.5f, startV01 - 0.5f, 0)), Camera.main);
    }

    public void CursorFocusBegin(Vector3 worldHit, Camera cam)
    {
        if (!cursorEnable) return;
        EnsureCursor();
        if (!cursor) return;

        Vector3 local = board.InverseTransformPoint(worldHit);
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

    public static void CursorMoveInCluster(ref WorldPanelPlus active, float dxPixel, float dyPixel)
    {
        if (!active || !active.cursor || !active.cursor.visible) return;

        float du = dxPixel * active.cursorSpeedPerPixel;
        float signY = active.cursorInvertY ? -1f : 1f;
        float dv = dyPixel * active.cursorSpeedPerPixel * signY;

        float newU = active.cursor.u + du;
        float newV = active.cursor.v + dv;

        bool goLeft = newU < 0f;
        bool goRight = newU > 1f;
        bool goDown = newV < 0f;
        bool goUp = newV > 1f;

        if (!goLeft && !goRight && !goDown && !goUp)
        {
            active.cursor.SetUV(Mathf.Clamp01(newU), Mathf.Clamp01(newV));
            return;
        }

        float overX = Mathf.Max(-newU, newU - 1f, 0f);
        float overY = Mathf.Max(-newV, newV - 1f, 0f);

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

        active.cursor.SetUV(Mathf.Clamp01(newU), Mathf.Clamp01(newV));
    }

    static bool SwitchNeighbor(ref WorldPanelPlus active, WorldPanelPlus next, float u01, float v01)
    {
        if (next == null) return false;

        if (active && active.cursor) active.cursor.SetVisible(false);

        next.EnsureCursor();
        next.cursor.SetVisible(true);
        next.cursor.SetUV(Mathf.Clamp01(u01), Mathf.Clamp01(v01), silent: true);

        active = next;
        return true;
    }

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

    public void CursorClickDown() { if (cursor && cursor.visible) cursor.ClickDown(); }
    public void CursorClickUp() { if (cursor && cursor.visible) cursor.ClickUp(); }

    #endregion

    #region Visibility

    public void SetVisible(bool visible)
    {
        boardVisible = visible;
        if (board)
        {
            var mr = board.GetComponent<MeshRenderer>();
            if (mr) mr.enabled = visible;
            var col = board.GetComponent<BoxCollider>();
            if (col) col.enabled = visible;
        }
        if (!visible && cursor) cursor.SetVisible(false);
    }

    public void Show() => SetVisible(true);
    public void Hide() => SetVisible(false);

    #endregion

    #region Build & Apply

    void Reset() { Rebuild(); }

    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        EnsureMaterials();
        if (!board) return;
        Apply();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        EnsureDefaultContentTexture();

        // Destroy all children
        var kill = new List<GameObject>();
        foreach (Transform c in transform) kill.Add(c.gameObject);
        foreach (var go in kill)
        {
            if (Application.isEditor) DestroyImmediate(go);
            else Destroy(go);
        }

        EnsureMaterials();

        // Create Board
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

        Apply();
        EnsureCursor();
    }

    void EnsureMaterials()
    {
        Shader sBoard = useBoardEdgeFeather ? Shader.Find("Unlit/WorldPanelBoard") : Shader.Find("Unlit/Texture");
        if (sBoard == null) sBoard = Shader.Find("Unlit/Texture");
        _panelMat ??= new Material(sBoard);
    }

    GameObject CreateQuad(string name, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(transform, false);

        var col = go.GetComponent<Collider>();
        if (col)
        {
            if (Application.isEditor) DestroyImmediate(col);
            else Destroy(col);
        }

        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return go;
    }

    public void Apply()
    {
        bool keepCursorVisible = (cursor != null && cursor.visible);
        EnsureDefaultContentTexture();

        if (!board) return;

        EnsureMaterials();

        // Check if ClusterPanelVisual is managing the board scale
        var clusterVisual = GetComponent<ClusterPanelVisual>();
        if (clusterVisual == null)
        {
            // No cluster visual - apply full panel scale
            board.localScale = new Vector3(width, height, 1);
        }
        // If ClusterPanelVisual exists, it manages the board scale via ApplyContentMargins()

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
        if (col)
        {
            col.size = new Vector3(1, 1, 0.02f);
            col.center = new Vector3(0, 0, 0.01f);
            col.enabled = boardVisible;
        }

        UpdateBoardShaderProperties();

        EnsureCursor();
        if (cursor != null) cursor.SetVisible(keepCursorVisible);
    }

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

    void ApplyBoardTint(MeshRenderer mr)
    {
        if (!mr) return;

        float a = Mathf.Clamp01(boardAlpha);
        bool opaque = a >= 0.999f;

        var mat = mr.sharedMaterial;
        if (!mat) mat = mr.sharedMaterial = new Material(Shader.Find("Unlit/WorldPanelBoard"));

        if (!(useBoardEdgeFeather && mat.shader && mat.shader.name == "Unlit/WorldPanelBoard"))
        {
            var shOpaque = Shader.Find("Unlit/Texture");
            var shTrans = Shader.Find("Unlit/Transparent");
            if (mat.shader != (opaque ? shOpaque : shTrans))
                mat.shader = (opaque ? shOpaque : shTrans);
        }

        mat.mainTexture = GetSafeTex(contentTexture);

        var c = panelTint;
        c.a = boardVisible ? a : 0f;
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        else mat.color = c;

        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void UpdateBoardShaderProperties()
    {
        if (_panelMat == null) return;

        var tint = panelTint;
        tint.a = boardVisible ? Mathf.Clamp01(boardAlpha) : 0f;
        if (_panelMat.HasProperty("_Color")) _panelMat.SetColor("_Color", tint);
        else _panelMat.color = tint;

        if (useBoardEdgeFeather && _panelMat.shader != null && _panelMat.shader.name == "Unlit/WorldPanelBoard")
        {
            if (_panelMat.HasProperty("_BorderColor")) _panelMat.SetColor("_BorderColor", boardEdgeColor);
            if (_panelMat.HasProperty("_BorderWidth")) _panelMat.SetFloat("_BorderWidth", boardEdgeWidthUV);
            if (_panelMat.HasProperty("_MinAlphaEdge")) _panelMat.SetFloat("_MinAlphaEdge", boardEdgeMinAlpha);
            if (_panelMat.HasProperty("_Soften")) _panelMat.SetFloat("_Soften", boardEdgeSoften);
            if (_panelMat.HasProperty("_EdgeRespectContentAlpha"))
                _panelMat.SetFloat("_EdgeRespectContentAlpha", boardEdgeRespectContentAlpha ? 1f : 0f);
            if (_panelMat.HasProperty("_PanelSize"))
                _panelMat.SetVector("_PanelSize", new Vector4(width, height, 0, 0));
            if (_panelMat.HasProperty("_CornerRadius"))
                _panelMat.SetFloat("_CornerRadius", boardCornerRadius);
            if (_panelMat.HasProperty("_EdgeFeather"))
                _panelMat.SetFloat("_EdgeFeather", 0.003f);
        }

        if (_panelMat.HasProperty("_Surface")) _panelMat.SetFloat("_Surface", 1f);
        _panelMat.renderQueue = 3000;
    }

    #endregion
}
