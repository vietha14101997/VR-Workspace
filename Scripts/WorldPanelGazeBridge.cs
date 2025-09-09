using UnityEngine;

/// Kết nối Cardboard gaze ray tới WorldPanelPlus + dwell + stillness-exit + Dock
public class WorldPanelGazeBridge : MonoBehaviour
{
    public Camera eventCamera;
    public LayerMask interactMask = ~0;
    public float maxDistance = 10f;

    [Header("Gaze dwell")]
    public bool useDwellActivation = true;
    public float dwellSeconds = 1.0f;
    public float dwellMaxAngle = 3.0f;   // độ rung trong dwell

    [Header("Thoát khi giữ yên")]
    public float exitAfterSeconds = 2f;
    public float stillAngleDeg = 0.4f;

    WorldPanelPlus _hoverPanel;
    WorldPanelPlusHandle _hoverHandle;
    WorldPanelPlusHandle _activeHandle;

    WorldPanelPlusHandle _candidateHandle;
    WorldPanelPlus _candidatePanel;
    float _dwellTimer = 0f;
    Vector3 _dwellDir;

    bool _prevPressed = false;
    float _stillTimer = 0f;
    Vector3 _prevDir;

    Collider _lockoutCollider = null;

    void Reset() { eventCamera = GetComponent<Camera>(); }

    void Update()
    {
        if (!eventCamera) eventCamera = Camera.main;
        if (!eventCamera) return;

        Ray ray = new Ray(eventCamera.transform.position, eventCamera.transform.forward);
        var hits = Physics.RaycastAll(ray, maxDistance, interactMask.value);

        WorldPanelPlusHandle hitHandle = null;
        WorldPanelPlusDockButton hitDockBtn = null;
        WorldPanelPlus hitPanel = null;
        Collider hitCol = null;

        int bestScore = int.MinValue; float bestDist = float.MaxValue;

        foreach (var h in hits)
        {
            var handle = h.collider.GetComponent<WorldPanelPlusHandle>();
            var dockB = h.collider.GetComponent<WorldPanelPlusDockButton>();
            var board = h.collider.GetComponent<WorldPanelPlusBoardRaycatcher>();
            var hoverC = h.collider.GetComponent<WorldPanelPlusTrayRaycatcher>();

            if (handle)
            {
                if (handle.type == WPHandleType.Center) continue;
                int score =
                    (handle.type == WPHandleType.CornerTL || handle.type == WPHandleType.CornerTR ||
                     handle.type == WPHandleType.CornerBL || handle.type == WPHandleType.CornerBR) ? 3 : 2;
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; hitHandle = handle; hitDockBtn = null; hitPanel = handle.panel; hitCol = h.collider; }
            }
            else if (dockB)
            {
                int score = 1;
                var p = dockB.panel;
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; hitHandle = null; hitDockBtn = dockB; hitPanel = p; hitCol = h.collider; }
            }
            else if (board || hoverC)
            {
                int score = 0;
                var p = board ? board.panel : hoverC.panel;
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; hitHandle = null; hitDockBtn = null; hitPanel = p; hitCol = h.collider; }
            }
        }

        // lockout: nếu vẫn trỏ vào collider bị khóa → không kích hoạt lại
        if (_lockoutCollider != null && hitCol == _lockoutCollider) { hitHandle = null; hitDockBtn = null; }
        else if (_lockoutCollider != null && hitCol != _lockoutCollider) { _lockoutCollider = null; }

        // Hover
        bool hovering = (hitPanel != null);
        if (!hovering && _hoverPanel) { _hoverPanel.OnHover(false, null); _hoverPanel = null; _hoverHandle = null; }
        if (hovering) { hitPanel.OnHover(true, hitHandle ? (WPHandleType?)hitHandle.type : null); _hoverPanel = hitPanel; _hoverHandle = hitHandle; }

        // Triggers
        bool pressed = IsPressed();
        bool down = pressed && !_prevPressed;
        bool up = !pressed && _prevPressed;
        _prevPressed = pressed;

        // Dwell: DockButton ưu tiên hơn Handle
        if (_activeHandle == null)
        {
            if (hitDockBtn != null) HandleDwellOnDockButton(hitDockBtn, ray, hitCol);
            else HandleDwellOnHandles(hitPanel, hitHandle, ray);
        }

        // Nhấn để bắt đầu kéo (tùy chọn)
        if (_activeHandle == null && down && hitHandle != null)
        {
            if (hitHandle.type != WPHandleType.Center
                || (hitHandle.panel && hitHandle.panel.centerDragEnabled))
            {
                _activeHandle = hitHandle;
                _activeHandle.BeginDragFromRay(ray, eventCamera);
                _stillTimer = 0f; _prevDir = ray.direction;
            }
        }

        // Kéo + thoát khi giữ yên
        if (_activeHandle != null && (pressed || useDwellActivation))
        {
            _activeHandle.UpdateDragFromRay(ray, eventCamera, Time.deltaTime);

            float ang = Vector3.Angle(_prevDir, ray.direction);
            if (ang < stillAngleDeg) _stillTimer += Time.deltaTime; else _stillTimer = 0f;
            _prevDir = ray.direction;

            if (_stillTimer >= exitAfterSeconds)
            {
                _activeHandle.EndDrag();
                if (_activeHandle && _activeHandle.panel && _activeHandle.type == WPHandleType.Center)
                {
                    _activeHandle.panel.centerDragEnabled = false;
                    _activeHandle.panel.moveAnchorWorld = null;
                }
                _activeHandle = null;
                if (hitCol) _lockoutCollider = hitCol; // buộc rời
            }
        }

        // Nhả
        if (_activeHandle != null && up)
        {
            _activeHandle.EndDrag();
            if (_activeHandle && _activeHandle.panel && _activeHandle.type == WPHandleType.Center)
            {
                _activeHandle.panel.centerDragEnabled = false;
                _activeHandle.panel.moveAnchorWorld = null;
            }
            _activeHandle = null;
            if (hitCol) _lockoutCollider = hitCol;
        }
    }

    void HandleDwellOnDockButton(WorldPanelPlusDockButton btn, Ray ray, Collider hitCol)
    {
        if (_candidatePanel != btn.panel || _candidateHandle != null)
        {
            _candidatePanel = btn.panel; _candidateHandle = null; _dwellTimer = 0f; _dwellDir = ray.direction;
        }
        else
        {
            float a = Vector3.Angle(_dwellDir, ray.direction);
            if (a <= dwellMaxAngle) _dwellTimer += Time.deltaTime; else { _dwellTimer = 0f; _dwellDir = ray.direction; }
            if (_dwellTimer >= dwellSeconds)
            {
                ExecuteDockAction(btn, ray);
                _lockoutCollider = hitCol;
                _dwellTimer = 0f;
            }
        }
    }

    void HandleDwellOnHandles(WorldPanelPlus hitPanel, WorldPanelPlusHandle hitHandle, Ray ray)
    {
        if (hitPanel != _candidatePanel || hitHandle != _candidateHandle)
        { _candidatePanel = hitPanel; _candidateHandle = hitHandle; _dwellTimer = 0f; _dwellDir = ray.direction; }
        else if (useDwellActivation && _candidatePanel != null)
        {
            float a = Vector3.Angle(_dwellDir, ray.direction);
            if (a <= dwellMaxAngle) _dwellTimer += Time.deltaTime; else { _dwellTimer = 0f; _dwellDir = ray.direction; }

            if (_dwellTimer >= dwellSeconds)
            {
                var start = _candidateHandle;

                // >>> NEW: Không bao giờ khởi động kéo với Center qua dwell
                if (start != null && start.type != WPHandleType.Center)
                {
                    _activeHandle = start;
                    _activeHandle.BeginDragFromRay(ray, eventCamera);
                    _stillTimer = 0f; _prevDir = ray.direction;
                }
                _dwellTimer = 0f;
            }
        }
    }

    void ExecuteDockAction(WorldPanelPlusDockButton btn, Ray ray)
    {
        var p = btn.panel; if (!p) return;

        bool isYaw = btn.type == WPDockButtonType.YawLeft15 || btn.type == WPDockButtonType.YawRight15;
        bool isPitch = btn.type == WPDockButtonType.PitchUp15 || btn.type == WPDockButtonType.PitchDown15;

        if ((p.axisLock == WorldPanelPlus.WPAxisLock.YawOnly && isPitch) || (p.axisLock == WorldPanelPlus.WPAxisLock.PitchOnly && isYaw))
        {
            return;
        }

        switch (btn.type)
        {
            case WPDockButtonType.MoveMode:
                {
                    p.centerDragEnabled = true;
                    p.moveAnchorWorld = btn.transform.position;    // anchor = nút Move
                    if (p.dock) p.dock.CaptureYawOffsetToCamera();

                    // Bắt đầu drag Center ngay lập tức
                    var c = GetCenterHandle(p);
                    if (c != null)
                    {
                        _activeHandle = c;
                        _activeHandle.BeginDragFromRay(ray, eventCamera);
                        _stillTimer = 0f; _prevDir = ray.direction;
                    }
                    break;
                }
            case WPDockButtonType.YawLeft15:
                {
                    p.centerDragEnabled = false;
                    p.AddYawClamped(-15f);
                    break;
                }
            case WPDockButtonType.YawRight15:
                {
                    p.centerDragEnabled = false;
                    p.AddYawClamped(15f);
                    break;
                }
            case WPDockButtonType.MinimizeToggle:
                {
                    p.ToggleDockMinimized();
                    break;
                }
            case WPDockButtonType.PitchUp15:
                {
                    p.centerDragEnabled = false;
                    p.AddYawClamped(15f);
                    break;
                }

            case WPDockButtonType.PitchDown15:
                {
                    p.centerDragEnabled = false;
                    p.AddYawClamped(-15f);
                    break;
                }

            case WPDockButtonType.ResetFaceCamera:
                {
                    FaceCamera(p);
                    p.SetRotationBasisNow();   // gốc mới
                    p.ResetYawPitchLocks();    // mở khóa 2 trục
                    if (p.dock)
                    {
                        // thay vì AnchorToWorld(...) hãy căn X ngay
                        p.dock.CenterXUnderTray();
                    }
                    break;
                }
        }
    }

    WorldPanelPlusHandle GetCenterHandle(WorldPanelPlus p)
    {
        foreach (var h in p.GetComponentsInChildren<WorldPanelPlusHandle>())
            if (h.type == WPHandleType.Center) return h;
        return null;
    }

    void RotateStable(WorldPanelPlus p, Vector3 localAxis, float deg)
    {
        if (!p) return;
        var dock = p.dock;
        var cam = eventCamera ? eventCamera : Camera.main;

        // 1) Pivot = worldPos của Dock (nếu có) hoặc Panel
        Vector3 pivot = dock ? dock.transform.position : p.transform.position;

        // 2) Chụp gapY hiện tại để bảo toàn sau xoay
        float gapY0 = 0f;
        if (dock) gapY0 = p.GetTrayBottomYWorld(cam) - dock.transform.TransformPoint(0, dock.GetBackplateHeight() * 0.5f, 0).y;

        // 3) Xoay panel quanh trục đi qua pivot
        Quaternion q = Quaternion.AngleAxis(deg, p.transform.rotation * localAxis);
        Vector3 r = p.transform.position - pivot;
        p.transform.position = pivot + q * r;
        p.transform.rotation = q * p.transform.rotation;

        // 4) Giữ Dock đứng yên tại worldPos pivot
        if (dock) dock.AnchorToWorld(pivot, cam);

        // 5) Đẩy panel theo trục +Y để khôi phục đúng gapY
        if (dock)
        {
            float trayBottom = p.GetTrayBottomYWorld(cam);
            float dockTop = dock.transform.TransformPoint(0, dock.GetBackplateHeight() * 0.5f, 0).y;
            float gapNow = trayBottom - dockTop;
            float dY = gapY0 - gapNow;
            if (Mathf.Abs(dY) > 1e-6f)
            {
                p.transform.position += Vector3.up * dY;
                // Re-anchor lại lần nữa sau khi đã dịch panel
                dock.AnchorToWorld(pivot, cam);
            }
        }
    }

    void FaceCamera(WorldPanelPlus p)
    {
        var cam = eventCamera ? eventCamera : Camera.main;
        if (!cam) return;
        Vector3 toCam = cam.transform.position - p.transform.position;
        if (toCam.sqrMagnitude < 1e-6f) return;
        var look = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        p.transform.rotation = look;
    }

    bool IsPressed()
    {
#if UNITY_ANDROID
        try { return Google.XR.Cardboard.Api.IsTriggerPressed; } catch { }
#endif
        if (Input.touchCount > 0)
        {
            var ph = Input.GetTouch(0).phase;
            return ph != TouchPhase.Ended && ph != TouchPhase.Canceled;
        }
        return Input.GetMouseButton(0);
    }
}
