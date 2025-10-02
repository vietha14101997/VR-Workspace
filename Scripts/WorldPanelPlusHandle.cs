using UnityEngine;
using UnityEngine.EventSystems;

public class WorldPanelPlusHandle : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    public WPHandleType type;
    public WorldPanelPlus panel;

    // Tuning
    public float scaleMin = 0.2f, scaleMax = 5f;  // chỉ cho resize

    // Drag plane & refs
    Plane _dragPlane;
    Vector3 _startHitWorld;
    Vector3 _refLocalHit;
    Vector3 _grabLocal;

    float _startW, _startH;
    bool _dragging = false;

    // Move
    Camera _dragCam;
    float _grabCamDist;
    Quaternion _moveRotOffset;
    Vector3 _moveAnchor; // neo = chính handle_center (world)
    bool _isCornerResizing = false;

    public void OnPointerEnter(PointerEventData e) { if (panel) panel.OnHover(true, type); }
    public void OnPointerExit(PointerEventData e) { if (panel) panel.OnHover(false, type); }

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

        _startW = panel.width; _startH = panel.height;

        // Corner => resize mode
        _isCornerResizing = (type == WPHandleType.CornerTL || type == WPHandleType.CornerTR || type == WPHandleType.CornerBL || type == WPHandleType.CornerBR);
        if (_isCornerResizing) panel.SetResizeMode(true);

        if (type == WPHandleType.Center && _dragCam)
        {
            // Neo = vị trí hiện tại của center handle
            _moveAnchor = transform.position;
            _grabCamDist = Vector3.Distance(_dragCam.transform.position, _moveAnchor);

            // Giữ lệch xoay ban đầu của panel so với "nhìn vào camera"
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
            ScaleUniformFromLocal(hitLocal);
            return;
        }

        // CENTER: Move-orbit quanh camera
        if (panel.moveOrbitCamera && _dragCam)
        {
            float dist = Mathf.Clamp(_grabCamDist, panel.moveOrbitMin, panel.moveOrbitMax);
            Vector3 wantAnchor = r.origin + r.direction.normalized * dist;
            Vector3 delta = wantAnchor - _moveAnchor;
            panel.transform.position += delta;
            _moveAnchor = transform.position; // cập nhật neo về đúng center handle

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
