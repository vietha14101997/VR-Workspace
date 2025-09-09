using UnityEngine;
using UnityEngine.EventSystems;

public class WorldPanelPlusHandle : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    public WPHandleType type;
    public WorldPanelPlus panel;

    [Header("Tuning")]
    public float rotateDegPerMeter = 90f;   // tốc độ xoay theo dịch chuyển trên mặt phẳng
    public float scaleMin = 0.2f, scaleMax = 5f;

    // === Drag plane & snapshot ===
    Plane _dragPlane;
    Vector3 _refLocalHit;       // điểm chạm local tại frame trước
    Vector3 _grabLocal;         // offset giữ khi kéo fallback

    Vector3 _panelStartPos;
    Quaternion _panelStartRot;
    float _startW, _startH;

    bool _dragging;

    // === Move-orbit quanh camera (Center) ===
    Camera _dragCam;
    float _grabCamDist;
    float _orbitYawAccum, _orbitPitchAccum;
    float _lastCamYaw, _lastCamPitch;
    Quaternion _moveRotOffset;
    Vector3 _moveAnchor;        // world anchor do nút Move chụp lại lúc bắt đầu
    bool _isCornerResizing;

    // ===== Hover =====
    public void OnPointerEnter(PointerEventData e)
    {
        if (panel) panel.OnHover(true, type);
    }
    public void OnPointerExit(PointerEventData e)
    {
        if (panel) panel.OnHover(false, type);
    }

    // ===== UI path (Editor/Mouse) =====
    public void OnPointerDown(PointerEventData e)
    {
        var cam = e.pressEventCamera ? e.pressEventCamera : Camera.main;
        if (!cam) return;
        BeginDragFromRay(cam.ScreenPointToRay(e.position), cam);
    }

    public void OnDrag(PointerEventData e)
    {
        var cam = e.pressEventCamera ? e.pressEventCamera : Camera.main;
        if (!cam) return;
        UpdateDragFromRay(cam.ScreenPointToRay(e.position), cam, Time.deltaTime);
    }

    public void OnPointerUp(PointerEventData e) => EndDrag();

    // ===== Gaze path (Cardboard) =====
    public void BeginDragFromRay(Ray r, Camera cam)
    {
        if (!panel) return;

        _dragCam = cam;
        var tPanel = panel.transform;

        // Mặt phẳng kéo cố định tại thời điểm bắt đầu
        _dragPlane = new Plane(-tPanel.forward, tPanel.position);

        if (_dragPlane.Raycast(r, out float d))
        {
            var startHitWorld = r.GetPoint(d);
            _refLocalHit = tPanel.InverseTransformPoint(startHitWorld);
            _grabLocal = _refLocalHit;
        }

        _panelStartPos = tPanel.position;
        _panelStartRot = tPanel.rotation;
        _startW = panel.width; _startH = panel.height;

        _isCornerResizing =
            (type == WPHandleType.CornerTL || type == WPHandleType.CornerTR ||
             type == WPHandleType.CornerBL || type == WPHandleType.CornerBR);
        if (_isCornerResizing) panel.SetResizeMode(true);

        if (type == WPHandleType.Center && _dragCam)
        {
            // Ưu tiên anchor do Dock ghi; fallback: vị trí Dock → Panel
            _moveAnchor = panel.moveAnchorWorld.HasValue
                ? panel.moveAnchorWorld.Value
                : (panel.dock ? panel.dock.transform.position : tPanel.position);

            // 1) Khoảng cách camera → anchor
            _grabCamDist = Vector3.Distance(_dragCam.transform.position, _moveAnchor);

            // 2) Góc phương vị/độ ngửa ban đầu theo vector camera→anchor
            Vector3 v = (_moveAnchor - _dragCam.transform.position);
            if (v.sqrMagnitude < 1e-6f) v = _dragCam.transform.forward;
            v.Normalize();

            _orbitYawAccum = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; // [-180..180]
            _orbitPitchAccum = Mathf.Atan2(-v.y, Mathf.Max(1e-6f, new Vector2(v.x, v.z).magnitude)) * Mathf.Rad2Deg;

            _lastCamYaw = _dragCam.transform.eulerAngles.y;
            _lastCamPitch = _dragCam.transform.eulerAngles.x;

            // 3) Giữ lệch xoay ban đầu của panel so với “nhìn vào camera”
            Vector3 toCam = _dragCam.transform.position - tPanel.position;
            if (toCam.sqrMagnitude < 1e-6f) toCam = -tPanel.forward;
            _moveRotOffset = Quaternion.Inverse(Quaternion.LookRotation(toCam.normalized, Vector3.up)) * tPanel.rotation;
        }

        _dragging = true;
        panel.OnHover(true, type);
    }

    public void UpdateDragFromRay(Ray r, Camera cam, float dt)
    {
        if (!_dragging || !panel) return;
        var tPanel = panel.transform;

        if (type != WPHandleType.Center)
        {
            if (!_dragPlane.Raycast(r, out float d)) return;
            var hitWorld = r.GetPoint(d);
            var hitLocal = tPanel.InverseTransformPoint(hitWorld);

            switch (type)
            {
                case WPHandleType.EdgeTop:
                case WPHandleType.EdgeBottom:
                    {
                        float dy = hitLocal.y - _refLocalHit.y;
                        panel.AddPitchClamped(dy * rotateDegPerMeter);
                        _refLocalHit = hitLocal;
                        break;
                    }
                case WPHandleType.EdgeLeft:
                case WPHandleType.EdgeRight:
                    {
                        float dx = hitLocal.x - _refLocalHit.x;
                        panel.AddYawClamped(-dx * rotateDegPerMeter);
                        _refLocalHit = hitLocal;
                        break;
                    }
                default:
                    {
                        // Corner scale giữ tỉ lệ
                        ScaleUniformFromLocal(hitLocal);
                        break;
                    }
            }
            return;
        }

        // ===== CENTER: Move-orbit quanh camera =====
        if (panel.moveOrbitCamera && _dragCam)
        {
            float camYaw = _dragCam.transform.eulerAngles.y;
            float camPitch = _dragCam.transform.eulerAngles.x;

            _orbitYawAccum += Mathf.DeltaAngle(_lastCamYaw, camYaw);
            _orbitPitchAccum += Mathf.DeltaAngle(_lastCamPitch, camPitch);
            _lastCamYaw = camYaw; _lastCamPitch = camPitch;
            _orbitPitchAccum = Mathf.Clamp(_orbitPitchAccum, -80f, 80f);

            float dist = Mathf.Clamp(_grabCamDist, panel.moveOrbitMin, panel.moveOrbitMax);

            Quaternion dirRot = Quaternion.Euler(_orbitPitchAccum, _orbitYawAccum, 0f);
            Vector3 dir = dirRot * Vector3.forward;

            // Anchor mới mong muốn
            Vector3 wantAnchor = _dragCam.transform.position + dir * dist;

            // Dịch chuyển tấm theo chênh lệch anchor
            Vector3 delta = wantAnchor - _moveAnchor;
            tPanel.position += delta;
            _moveAnchor = wantAnchor;
            panel.moveAnchorWorld = _moveAnchor; // lưu để lần tới không “nhảy”

            // Khóa lệch xoay ban đầu theo camera (có nội suy nếu đặt moveOrbitLerp)
            Vector3 toCamNow = _dragCam.transform.position - tPanel.position;
            if (toCamNow.sqrMagnitude > 1e-6f)
            {
                Quaternion look = Quaternion.LookRotation(toCamNow.normalized, Vector3.up);
                Quaternion wantRot = look * _moveRotOffset;
                float k = (panel.moveOrbitLerp > 0f) ? (1f - Mathf.Exp(-panel.moveOrbitLerp * dt)) : 1f;
                tPanel.rotation = Quaternion.Slerp(tPanel.rotation, wantRot, k);
            }
        }
        else
        {
            // Fallback: kéo theo mặt phẳng
            if (!_dragPlane.Raycast(r, out float d)) return;
            var hitWorld = r.GetPoint(d);
            var targetWorld = hitWorld - tPanel.TransformVector(_grabLocal) + tPanel.TransformVector(_refLocalHit);
            tPanel.position = targetWorld;
        }
    }

    public void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (_isCornerResizing) { panel.SetResizeMode(false); _isCornerResizing = false; }
        if (panel) panel.OnHover(false, type);
    }

    // ===== Helpers =====
    void ScaleUniformFromLocal(Vector3 currentLocal)
    {
        float half = Mathf.Max(Mathf.Abs(currentLocal.x), Mathf.Abs(currentLocal.y));
        float startHalf = Mathf.Max(_startW, _startH) * 0.5f;
        float k = Mathf.Clamp(half / Mathf.Max(1e-5f, startHalf), scaleMin, scaleMax);
        float aspect = _startW / Mathf.Max(1e-5f, _startH);

        panel.width = _startW * k;
        panel.height = panel.width / Mathf.Max(1e-5f, aspect);
        panel.Apply();
    }
}

/// Marker sphere cho các handle (trừ Center)
[RequireComponent(typeof(WorldPanelPlusHandle))]
public class WorldPanelPlusHandleSphere : MonoBehaviour
{
    [Header("Marker")]
    public float radius = 0.01f;        // bán kính sphere nhỏ
    public float forwardOffset = 0.01f; // hơi nổi lên phía trước

    MeshRenderer _mr;
    static Material _defMat;

    static Material DefaultMat
    {
        get
        {
            if (_defMat == null)
            {
                _defMat = new Material(Shader.Find("Unlit/Color")) { color = Color.white };
            }
            return _defMat;
        }
    }

    void Awake()
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Marker";
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = 2f * radius * Vector3.one;
        marker.transform.localPosition = new Vector3(0, 0, forwardOffset);

        DestroyImmediate(marker.GetComponent<Collider>());

        _mr = marker.GetComponent<MeshRenderer>();
        if (_mr) _mr.sharedMaterial = DefaultMat; // tránh hiện màu tím khi thiếu mat
    }

    public void SetVisible(bool on)
    {
        if (_mr) _mr.enabled = on;
    }
}
