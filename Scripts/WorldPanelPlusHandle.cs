using UnityEngine;
using UnityEngine.EventSystems;

public class WorldPanelPlusHandle : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    public WPHandleType type;
    public WorldPanelPlus panel;

    // Tuning
    public float rotateDegPerMeter = 90f;   // cho edges (xoay theo dịch chuyển)
    public float scaleMin = 0.2f, scaleMax = 5f;

    // Drag plane & refs
    Plane _dragPlane;
    Vector3 _startHitWorld;
    Vector3 _refLocalHit;
    Vector3 _grabLocal;

    Vector3 _panelStartPos;
    Quaternion _panelStartRot;
    float _startW, _startH;

    bool _dragging = false;

    // ===== Move-orbit quanh camera =====
    Camera _dragCam;
    float _grabCamDist;
    float _orbitYawAccum, _orbitPitchAccum;
    float _lastCamYaw, _lastCamPitch;
    Quaternion _moveRotOffset;

    // Move mode (anchor = nút Move)
    Vector3 _moveAnchor;          // vị trí world của nút Move tại thời điểm bắt đầu
    bool _isCornerResizing = false;

    // ===== Hover =====
    public void OnPointerEnter(PointerEventData e) { if (panel) panel.OnHover(true, type); }
    public void OnPointerExit(PointerEventData e) { if (panel) panel.OnHover(false, type); }

    // ===== UI path (Editor/Mouse) =====
    public void OnPointerDown(PointerEventData e)
    {
        var cam = e.pressEventCamera ? e.pressEventCamera : Camera.main;
        BeginDragFromRay(cam.ScreenPointToRay(e.position), cam);
    }
    public void OnDrag(PointerEventData e)
    {
        var cam = e.pressEventCamera ? e.pressEventCamera : Camera.main;
        UpdateDragFromRay(cam.ScreenPointToRay(e.position), cam, Time.deltaTime);
    }
    public void OnPointerUp(PointerEventData e) { EndDrag(); }

    // ===== Gaze path (Cardboard) =====
    public void BeginDragFromRay(Ray r, Camera cam)
    {
        if (!panel) return;

        _dragCam = cam;
        _dragPlane = new Plane(-panel.transform.forward, panel.transform.position);

        if (_dragPlane.Raycast(r, out float d))
        {
            _startHitWorld = r.GetPoint(d);
            _refLocalHit = panel.transform.InverseTransformPoint(_startHitWorld);
            _grabLocal = _refLocalHit;
        }

        _panelStartPos = panel.transform.position;
        _panelStartRot = panel.transform.rotation;
        _startW = panel.width; _startH = panel.height;

        // === Corner => vào chế độ resize (target = Tray) ===
        _isCornerResizing =
            (type == WPHandleType.CornerTL || type == WPHandleType.CornerTR ||
             type == WPHandleType.CornerBL || type == WPHandleType.CornerBR);
        if (_isCornerResizing) panel.SetResizeMode(true);

        if (type == WPHandleType.Center && _dragCam)
        {
            // Anchor đã được set khi bấm nút Move; fallback về dock/panel nếu thiếu
            _moveAnchor = panel.moveAnchorWorld.HasValue
                ? panel.moveAnchorWorld.Value
                : (panel.dock ? panel.dock.transform.position : panel.transform.position);

            // 1) Khoảng cách camera → anchor
            _grabCamDist = Vector3.Distance(_dragCam.transform.position, _moveAnchor);

            // 2) Hướng ban đầu từ camera → anchor (để KHÔNG nhảy frame đầu)
            Vector3 v = (_moveAnchor - _dragCam.transform.position);
            if (v.sqrMagnitude < 1e-6f) v = _dragCam.transform.forward;
            v.Normalize();
            float yaw0 = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;                               // [-180..180]
            float pitch0 = Mathf.Atan2(-v.y, Mathf.Max(1e-6f, new Vector2(v.x, v.z).magnitude)) // "ngửa" dương
                           * Mathf.Rad2Deg;

            _orbitYawAccum = yaw0;
            _orbitPitchAccum = pitch0;

            _lastCamYaw = _dragCam.transform.eulerAngles.y;
            _lastCamPitch = _dragCam.transform.eulerAngles.x;

            // 3) Giữ lệch xoay ban đầu của panel so với "nhìn vào camera"
            Vector3 toCam = _dragCam.transform.position - panel.transform.position;
            if (toCam.sqrMagnitude < 1e-6f) toCam = -panel.transform.forward;
            Quaternion lookAtCam = Quaternion.LookRotation(toCam.normalized, Vector3.up);
            _moveRotOffset = Quaternion.Inverse(lookAtCam) * panel.transform.rotation;
        }

        _dragging = true;
        panel.OnHover(true, type);
    }

    public void UpdateDragFromRay(Ray r, Camera cam, float dt)
    {
        if (!_dragging || !panel) return;

        if (type != WPHandleType.Center)
        {
            if (!_dragPlane.Raycast(r, out float d)) return;
            var hitWorld = r.GetPoint(d);
            var hitLocal = panel.transform.InverseTransformPoint(hitWorld);

            switch (type)
            {
                // case WPHandleType.EdgeTop:
                // case WPHandleType.EdgeBottom:
                //     {
                //         float dyLocal = hitLocal.y - _refLocalHit.y;
                //         float deltaDeg = dyLocal * rotateDegPerMeter;
                //         panel.AddPitchClamped(deltaDeg);   // <-- dùng API kẹp + lock
                //         _refLocalHit = hitLocal;
                //         break;
                //     }
                // case WPHandleType.EdgeLeft:
                // case WPHandleType.EdgeRight:
                //     {
                //         float dxLocal = hitLocal.x - _refLocalHit.x;
                //         float deltaDeg = -dxLocal * rotateDegPerMeter;
                //         panel.AddYawClamped(deltaDeg);     // <-- dùng API kẹp + lock
                //         _refLocalHit = hitLocal;
                //         break;
                //     }
                case WPHandleType.CornerTL:
                case WPHandleType.CornerTR:
                case WPHandleType.CornerBL:
                case WPHandleType.CornerBR:
                    {
                        ScaleUniformFromLocal(hitLocal);
                        break;
                    }
            }
            return;
        }

        // CENTER: Move-orbit quanh camera 360° (unbounded yaw)
        if (panel.moveOrbitCamera && _dragCam)
        {
            // 1) Giữ khoảng cách neo ban đầu
            float dist = Mathf.Clamp(_grabCamDist, panel.moveOrbitMin, panel.moveOrbitMax);

            // 2) Đặt anchor MỚI đúng trên tia nhìn tại khoảng cách dist
            Vector3 wantAnchor = r.origin + r.direction.normalized * dist;

            // 3) Dịch panel sao cho anchor (nút Move) “theo” reticle
            Vector3 delta = wantAnchor - _moveAnchor;
            panel.transform.position += delta;

            // 4) Cập nhật neo để khung hình sau tiếp tục mượt
            Transform moveTr = panel && panel.dock ? panel.dock.GetMoveButtonTransform() : null;
            if (moveTr)
            {
                _moveAnchor = moveTr.position;
            }
            else
            {
                _moveAnchor = wantAnchor; // fallback nếu vì lý do nào đó chưa có Dock/nút
            }
            panel.moveAnchorWorld = _moveAnchor;

            // 5) Giữ lệch xoay ban đầu so với camera (giữ cảm giác tự nhiên)
            Vector3 toCamNow = _dragCam.transform.position - panel.transform.position;
            if (toCamNow.sqrMagnitude > 1e-6f)
            {
                Quaternion lookAtNow = Quaternion.LookRotation(toCamNow.normalized, Vector3.up);
                Quaternion wantRot = lookAtNow * _moveRotOffset;
                float k = (panel.moveOrbitLerp > 0f) ? (1f - Mathf.Exp(-panel.moveOrbitLerp * dt)) : 1f;
                panel.transform.rotation = Quaternion.Slerp(panel.transform.rotation, wantRot, k);
                panel.EnforceNoRoll();
            }
        }
        else
        {
            // fallback kéo theo mặt phẳng panel
            if (!_dragPlane.Raycast(r, out float d)) return;
            var hitWorld = r.GetPoint(d);
            var targetWorld = hitWorld - panel.transform.TransformVector(_grabLocal)
                                        + panel.transform.TransformVector(_refLocalHit);
            panel.transform.position = targetWorld;
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
    void RotateAroundLocalStable(Vector3 localAxis, float deltaDeg)
    {
        // _panelStartRot đã được set khi bắt đầu kéo
        Vector3 worldAxisAtStart = _panelStartRot * localAxis;
        panel.transform.rotation = Quaternion.AngleAxis(deltaDeg, worldAxisAtStart) * _panelStartRot;
    }

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
