using UnityEngine;
using System;

[ExecuteAlways]
public class WorldPanelCursor : MonoBehaviour
{
    [Header("Visual")]
    public Texture2D cursorTexture;          // auto nạp từ Resources nếu để trống
    public float sizeMeters = 0.035f;        // kích thước con trỏ (m)
    public float zOffset = 0.001f;           // nổi lên khỏi mặt board một chút

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
}