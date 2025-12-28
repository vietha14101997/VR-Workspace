using UnityEngine;
using System;

[ExecuteAlways]
public class WorldPanelCursor : MonoBehaviour
{
    [Header("Visual")]
    public Texture2D cursorTexture;          // auto nạp từ Resources nếu để trống
    public float sizeMeters = 0.035f;        // kích thước con trỏ (m)
    public float zOffset = 0.001f;           // nổi lên khỏi mặt board một chút

    [Header("Force Visibility (Android Debug)")]
    [Tooltip("Force cursor always visible - use for Android debugging")]
    public bool forceAlwaysVisible = false;

    [Header("State (readonly)")]
    [Range(0, 1)] public float u = 0.5f;      // UV.x trong [0..1]
    [Range(0, 1)] public float v = 0.5f;      // UV.y trong [0..1]
    public bool visible = false;

    MeshRenderer _mr;
    MeshFilter _mf;
    Material _mat;
    public Material cursorMaterial;
    Transform _board;        // transform của quad Board
    float _boardW = 1f, _boardH = 1f;

    public Action<float, float> onMovedUV;    // (u,v)
    public Action onClickDown;
    public Action onClickUp;

    void Awake()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Auto-enable force visibility on Android for debugging
        forceAlwaysVisible = true;
        Debug.Log("[WorldPanelCursor] Awake: Auto-enabled forceAlwaysVisible on Android");
#endif
    }

    void OnEnable()
    {
        EnsureBuilt();
        if (forceAlwaysVisible || visible)
        {
            if (_mr) _mr.enabled = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            Debug.Log($"[WorldPanelCursor] OnEnable: Ensuring cursor visible, _mr={_mr != null}, _mr.enabled={_mr?.enabled}");
#endif
        }
    }

    void EnsureBuilt()
    {
        if (!_mf)
        {
            _mf = gameObject.GetComponent<MeshFilter>();
            if (!_mf) _mf = gameObject.AddComponent<MeshFilter>();
        }
        if (!_mr)
        {
            _mr = gameObject.GetComponent<MeshRenderer>();
            if (!_mr) _mr = gameObject.AddComponent<MeshRenderer>();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log($"[WorldPanelCursor] EnsureBuilt: _mf={_mf != null}, _mr={_mr != null}");
#endif
        if (_mf.sharedMesh == null)
        {
            var m = new Mesh();
            // Shifted so top-left (Tip) matches origin (0,0)
            m.vertices = new[] { 
                new Vector3(0f, -1f, 0),   // BL
                new Vector3(1f, -1f, 0),   // BR
                new Vector3(1f, 0f, 0),    // TR
                new Vector3(0f, 0f, 0)     // TL (Tip)
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            _mf.sharedMesh = m;
        }
        if (_mat == null)
        {
            if (!cursorMaterial)
                cursorMaterial = Resources.Load<Material>("WorldPanelCursor"); // Assets/Resources/WorldPanelCursor.mat
            if (cursorMaterial) _mat = new Material(cursorMaterial); // instance riêng
            else
            {
                // Try multiple shader fallbacks for maximum compatibility
                Shader sh = Shader.Find("WP/Cursor");
#if UNITY_ANDROID && !UNITY_EDITOR
                Debug.Log($"[WorldPanelCursor] Shader WP/Cursor found: {sh != null}");
#endif
                if (sh == null)
                {
                    sh = Shader.Find("Unlit/Transparent");
#if UNITY_ANDROID && !UNITY_EDITOR
                    Debug.Log($"[WorldPanelCursor] Fallback Unlit/Transparent found: {sh != null}");
#endif
                }
                if (sh == null)
                {
                    sh = Shader.Find("UI/Default");
#if UNITY_ANDROID && !UNITY_EDITOR
                    Debug.Log($"[WorldPanelCursor] Fallback UI/Default found: {sh != null}");
#endif
                }
                if (sh == null)
                {
                    sh = Shader.Find("Sprites/Default");
#if UNITY_ANDROID && !UNITY_EDITOR
                    Debug.Log($"[WorldPanelCursor] Fallback Sprites/Default found: {sh != null}");
#endif
                }
                if (sh == null)
                {
                    sh = Shader.Find("Standard");
#if UNITY_ANDROID && !UNITY_EDITOR
                    Debug.LogError("[WorldPanelCursor] No suitable shader found! Using Standard as last resort");
#endif
                }
                _mat = new Material(sh);
            }
        }
        if (_mr.sharedMaterial != _mat) _mr.sharedMaterial = _mat;

        if (cursorTexture == null)
            cursorTexture = Resources.Load<Texture2D>("icon_cursor"); // đặt file vào Assets/Resources/icon_cursor.png

        _mr.sharedMaterial.SetTexture("_MainTex", cursorTexture);
        _mr.sharedMaterial.SetColor("_Color", Color.white);

        _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _mr.receiveShadows = false;
        _mr.sharedMaterial.renderQueue = 5000;

        if (_mr.sharedMaterial.HasProperty("_ZWrite"))
            _mr.sharedMaterial.SetInt("_ZWrite", 0);

        if (_mr.sharedMaterial.HasProperty("_ZTest"))
            _mr.sharedMaterial.SetInt("_ZTest", 8);

#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log($"[WorldPanelCursor] EnsureBuilt complete: " +
            $"mesh={_mf.sharedMesh != null}, " +
            $"mat={_mat != null}, " +
            $"shader={_mat?.shader?.name}, " +
            $"tex={cursorTexture != null}, " +
            $"mr.enabled={_mr.enabled}");
#endif
    }

    public void AttachToBoard(Transform board, float boardWidth, float boardHeight)
    {
        EnsureBuilt();
        _board = board; _boardW = Mathf.Max(1e-5f, boardWidth); _boardH = Mathf.Max(1e-5f, boardHeight);
        transform.SetParent(board, false);
        transform.localRotation = Quaternion.identity;
        UpdateScaleMeters();              // <— thay cho transform.localScale = Vector3.one * sizeMeters;
        ApplyPoseFromUV(true);
    }

    public void SetVisible(bool on)
    {
        visible = on;
        bool actualVisible = on || forceAlwaysVisible;
        if (_mr) _mr.enabled = actualVisible;
#if UNITY_ANDROID && !UNITY_EDITOR
        // Android: Log visibility changes for debugging
        if (forceAlwaysVisible)
            Debug.Log($"[WorldPanelCursor] SetVisible({on}) -> forced to visible, _mr.enabled={_mr?.enabled}");
#endif
    }

    void LateUpdate()
    {
        // Force visibility enforcement - especially important for Android
        if (forceAlwaysVisible && _mr != null && !_mr.enabled)
        {
            _mr.enabled = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            Debug.Log("[WorldPanelCursor] LateUpdate: Re-enabling forced visible cursor");
#endif
        }
    }

    public void SetUV(float uu, float vv, bool silent = false)
    {
        u = Mathf.Clamp01(uu);
        v = Mathf.Clamp01(vv);
        ApplyPoseFromUV(false);
        if (!silent) onMovedUV?.Invoke(u, v);
    }

    public void NudgeUV(float du, float dv) => SetUV(u + du, v + dv);

    public void ClickDown() => onClickDown?.Invoke();
    public void ClickUp() => onClickUp?.Invoke();

    void ApplyPoseFromUV(bool forceRebuild)
    {
        if (forceRebuild) EnsureBuilt();
        if (_board == null) return;

        // 1) Tọa độ local trên mặt quad: x,y ∈ [-0.5..0.5]
        float lx = u - 0.5f;
        float ly = v - 0.5f;
        Vector3 local = new(lx, ly, 0f);

        // 2) Chuyển sang world
        Vector3 worldPoint = _board.TransformPoint(local);

        // 3) Lấy pháp tuyến bề mặt (hướng "mặt trước" của Board)
        Vector3 n = _board.forward;

        // 4) Bảo đảm đẩy TỚI camera (nếu pháp tuyến đang quay lưng camera thì đảo dấu)
        Camera cam = Camera.main;
        if (cam)
        {
            Vector3 toCam = cam.transform.position - worldPoint;
            if (Vector3.Dot(n, toCam) < 0f) n = -n;
        }

        // 5) Đặt vị trí thế giới và xoay "ốp" theo mặt Board
        float zWorldOffset = Mathf.Max(1e-4f, zOffset); // mét
        transform.position = worldPoint + n * zWorldOffset;
        transform.rotation = _board.rotation;  // cùng hướng với mặt Board
    }

    void UpdateScaleMeters()
    {
        if (_board == null) return;
        var s = _board.lossyScale;
        // Chia ngược scale của Board để sizeMeters là kích thước thật tính theo thế giới
        float sx = sizeMeters / Mathf.Max(1e-6f, s.x);
        float sy = sizeMeters / Mathf.Max(1e-6f, s.y);
        transform.localScale = new Vector3(sx, sy, 1f);
    }

    /// <summary>
    /// Debug method to validate and log cursor state - call from outside to diagnose issues
    /// </summary>
    public string DebugValidateCursor()
    {
        EnsureBuilt();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== WorldPanelCursor Debug ===");
        sb.AppendLine($"GameObject: {gameObject.name}, active={gameObject.activeInHierarchy}");
        sb.AppendLine($"Transform: pos={transform.position}, scale={transform.localScale}");
        sb.AppendLine($"Board: {(_board != null ? _board.name : "NULL")}");
        sb.AppendLine($"UV: ({u:F3}, {v:F3})");
        sb.AppendLine($"Visible: {visible}, ForceAlwaysVisible: {forceAlwaysVisible}");
        sb.AppendLine($"MeshFilter: {(_mf != null ? "OK" : "NULL")}, Mesh: {(_mf?.sharedMesh != null ? "OK" : "NULL")}");
        sb.AppendLine($"MeshRenderer: {(_mr != null ? "OK" : "NULL")}, enabled={_mr?.enabled}");
        sb.AppendLine($"Material: {(_mat != null ? _mat.name : "NULL")}");
        sb.AppendLine($"Shader: {(_mat?.shader != null ? _mat.shader.name : "NULL")}");
        sb.AppendLine($"Texture: {(cursorTexture != null ? cursorTexture.name : "NULL")}");
        sb.AppendLine($"RenderQueue: {_mat?.renderQueue}");

        var result = sb.ToString();
        Debug.Log(result);
        return result;
    }

    /// <summary>
    /// Force cursor to be visible immediately - use for debugging on Android
    /// </summary>
    public void ForceShowNow()
    {
        forceAlwaysVisible = true;
        EnsureBuilt();
        if (_mr != null)
        {
            _mr.enabled = true;
            visible = true;
        }
        ApplyPoseFromUV(true);
        Debug.Log($"[WorldPanelCursor] ForceShowNow called - mr.enabled={_mr?.enabled}, visible={visible}");
    }
}
