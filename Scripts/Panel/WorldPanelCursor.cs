using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using VRWorkspace.Streaming;

[ExecuteAlways]
public class WorldPanelCursor : MonoBehaviour
{
    [Header("Visual")]
    public Texture2D cursorTexture;          // auto nạp từ Resources nếu để trống
    public float sizeMeters = 0.035f;        // kích thước con trỏ (m)
    public float zOffset = 0.001f;           // nổi lên khỏi mặt board một chút

    [Header("Behavior")]
    public bool faceCamera = false;  // User request: disable camera dependency
    public bool invertNormal = true; // Standard Quad front is -Z

    [Header("State (readonly)")]
    [Range(0, 1)] public float u = 0.5f;      // UV.x trong [0..1]
    [Range(0, 1)] public float v = 0.5f;      // UV.y trong [0..1]
    public bool visible = false;

    [Header("Cursor State")]
    public CursorType currentCursorType = CursorType.Arrow;
    public long currentCursorId = 0;

    MeshRenderer _mr;
    MeshFilter _mf;
    Material _mat;
    public Material cursorMaterial;
    Transform _board;        // transform của quad Board
    float _boardW = 1f, _boardH = 1f;

    public Action<float, float> onMovedUV;    // (u,v)
    public Action onClickDown;
    public Action onClickUp;

    // ==================== Static Cursor Cache ====================

    /// <summary>
    /// Cache entry for cursor textures received from server.
    /// </summary>
    public struct CursorCacheEntry
    {
        public Texture2D texture;
        public int hotspotX;
        public int hotspotY;
        public long lastUsedTicks;
    }

    /// <summary>
    /// Static cache for all cursor textures received from server.
    /// Key is cursorId (server's hCursor handle as Int64).
    /// </summary>
    private static Dictionary<long, CursorCacheEntry> _cursorCache = new Dictionary<long, CursorCacheEntry>();

    private const int MAX_CACHE_SIZE = 100;

    /// <summary>
    /// Cache a cursor texture received from server.
    /// </summary>
    public static void CacheCursor(long cursorId, Texture2D texture, int hotspotX, int hotspotY)
    {
        // LRU eviction if cache exceeds limit
        if (_cursorCache.Count >= MAX_CACHE_SIZE && !_cursorCache.ContainsKey(cursorId))
        {
            // Find and remove oldest entry
            var oldest = _cursorCache.OrderBy(kv => kv.Value.lastUsedTicks).First();
            if (oldest.Value.texture != null)
                Destroy(oldest.Value.texture);
            _cursorCache.Remove(oldest.Key);
        }

        _cursorCache[cursorId] = new CursorCacheEntry
        {
            texture = texture,
            hotspotX = hotspotX,
            hotspotY = hotspotY,
            lastUsedTicks = DateTime.Now.Ticks
        };
    }

    /// <summary>
    /// Check if a cursor is in the cache.
    /// </summary>
    public static bool HasCursor(long cursorId)
    {
        return _cursorCache.ContainsKey(cursorId);
    }

    /// <summary>
    /// Try to get a cursor from cache.
    /// </summary>
    public static bool TryGetCursor(long cursorId, out CursorCacheEntry entry)
    {
        if (_cursorCache.TryGetValue(cursorId, out entry))
        {
            // Update last used time
            entry.lastUsedTicks = DateTime.Now.Ticks;
            _cursorCache[cursorId] = entry;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clear all cached cursors.
    /// </summary>
    public static void ClearCache()
    {
        foreach (var entry in _cursorCache.Values)
        {
            if (entry.texture != null)
                Destroy(entry.texture);
        }
        _cursorCache.Clear();
        Debug.Log("[WorldPanelCursor] Cursor cache cleared");
    }

    /// <summary>
    /// Set cursor by ID from cache.
    /// Returns true if cursor was found and applied.
    /// </summary>
    public bool SetCursorById(long cursorId)
    {
        currentCursorId = cursorId;

        if (TryGetCursor(cursorId, out var entry))
        {
            if (entry.texture != null && entry.texture != cursorTexture)
            {
                cursorTexture = entry.texture;
                ApplyCursorTexture();
            }
            return true;
        }
        // Cursor not in cache yet - wait for cursor_image message
        return false;
    }

    /// <summary>
    /// Apply current cursor texture to material.
    /// </summary>
    private void ApplyCursorTexture()
    {
        EnsureBuilt();
        if (_mr?.sharedMaterial != null && cursorTexture != null)
        {
            _mr.sharedMaterial.SetTexture("_MainTex", cursorTexture);
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
                var sh = Shader.Find("WP/Cursor");
                if (sh == null) sh = Shader.Find("Unlit/Transparent");
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
        if (_mr) _mr.enabled = on;
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
        if (invertNormal) n = -n;

        // 4) Bảo đảm đẩy TỚI camera (nếu enabled)
        if (faceCamera)
        {
            Camera cam = Camera.main;
            if (cam)
            {
                Vector3 toCam = cam.transform.position - worldPoint;
                if (Vector3.Dot(n, toCam) < 0f) n = -n;
            }
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
}