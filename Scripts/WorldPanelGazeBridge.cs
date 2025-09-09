using UnityEngine;

/// Kết nối Cardboard gaze ray tới WorldPanelPlus + dwell + stillness-exit + Dock
public class WorldPanelGazeBridge : MonoBehaviour
{
    [Header("Refs")]
    public Camera eventCamera;
    public LayerMask interactMask = ~0;
    public float maxDistance = 10f;

    [Header("Gaze dwell")]
    public bool useDwellActivation = true;
    public float dwellSeconds = 1f;
    public float dwellMaxAngle = 3f;

    [Header("Thoát khi giữ yên")]
    public float exitAfterSeconds = 2f;
    public float stillAngleDeg = 0.4f;

    WorldPanelPlus _hoverPanel, _candidatePanel;
    WorldPanelPlusHandle _hoverHandle, _activeHandle, _candidateHandle;
    float _dwellTimer, _stillTimer;
    Vector3 _dwellDir, _prevDir;
    bool _prevPressed;
    Collider _lockoutCollider;

    void Reset() => eventCamera = GetComponent<Camera>();

    void Update()
    {
        if (!eventCamera) eventCamera = Camera.main;
        if (!eventCamera) return;

        var ray = new Ray(eventCamera.transform.position, eventCamera.transform.forward);
        var (hitPanel, hitHandle, hitDockBtn, hitCol) = GetBestHit(ray);

        // lockout
        if (_lockoutCollider && hitCol == _lockoutCollider)
            hitPanel = null; // vô hiệu nếu vẫn trỏ vào cùng collider
        else if (_lockoutCollider && hitCol != _lockoutCollider)
            _lockoutCollider = null;

        // Hover
        UpdateHover(hitPanel, hitHandle);

        // Trigger input
        bool pressed = IsPressed();
        bool down = pressed && !_prevPressed;
        bool up = !pressed && _prevPressed;
        _prevPressed = pressed;

        // Dwell
        if (_activeHandle == null)
        {
            if (hitDockBtn) HandleDwellOnDockButton(hitDockBtn, ray, hitCol);
            else HandleDwellOnHandles(hitPanel, hitHandle, ray);
        }

        // Bắt đầu kéo (click)
        if (_activeHandle == null && down) TryStartDrag(hitHandle, ray);

        // Đang kéo
        if (_activeHandle && (pressed || useDwellActivation))
        {
            _activeHandle.UpdateDragFromRay(ray, eventCamera, Time.deltaTime);
            UpdateStillness(ray, hitCol);
        }

        // Kết thúc kéo
        if (_activeHandle && up) TryEndDrag(hitCol);
    }

    #region --- Hover & Raycast ---
    (WorldPanelPlus, WorldPanelPlusHandle, WorldPanelPlusDockButton, Collider) GetBestHit(Ray ray)
    {
        var hits = Physics.RaycastAll(ray, maxDistance, interactMask);
        int bestScore = int.MinValue; float bestDist = float.MaxValue;

        WorldPanelPlus panel = null;
        WorldPanelPlusHandle handle = null;
        WorldPanelPlusDockButton dock = null;
        Collider col = null;

        foreach (var h in hits)
        {
            // Handle
            if (h.collider.TryGetComponent(out WorldPanelPlusHandle hd))
            {
                if (hd.type == WPHandleType.Center) continue;
                int score = (hd.type == WPHandleType.CornerTL || hd.type == WPHandleType.CornerTR ||
                             hd.type == WPHandleType.CornerBL || hd.type == WPHandleType.CornerBR) ? 3 : 2;

                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; handle = hd; dock = null; panel = hd.panel; col = h.collider; }
                continue;
            }

            // Dock button
            if (h.collider.TryGetComponent(out WorldPanelPlusDockButton db))
            {
                int score = 1;
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; handle = null; dock = db; panel = db.panel; col = h.collider; }
                continue;
            }

            // Board raycatcher
            if (h.collider.TryGetComponent(out WorldPanelPlusBoardRaycatcher board))
            {
                int score = 0;
                var p = board.panel;
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; handle = null; dock = null; panel = p; col = h.collider; }
                continue;
            }

            // Tray raycatcher
            if (h.collider.TryGetComponent(out WorldPanelPlusTrayRaycatcher tray))
            {
                int score = 0;
                var p = tray.panel;
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; handle = null; dock = null; panel = p; col = h.collider; }
                continue;
            }
        }

        return (panel, handle, dock, col);
    }

    void UpdateHover(WorldPanelPlus panel, WorldPanelPlusHandle handle)
    {
        if (panel != _hoverPanel)
        {
            if (_hoverPanel) _hoverPanel.OnHover(false, null);
            _hoverPanel = panel; _hoverHandle = handle;
        }
        if (panel) panel.OnHover(true, handle ? (WPHandleType?)handle.type : null);
    }
    #endregion

    #region --- Drag logic ---
    void TryStartDrag(WorldPanelPlusHandle h, Ray ray)
    {
        if (!h) return;
        if (h.type != WPHandleType.Center || (h.panel && h.panel.centerDragEnabled))
        {
            _activeHandle = h;
            _activeHandle.BeginDragFromRay(ray, eventCamera);
            _stillTimer = 0f; _prevDir = ray.direction;
        }
    }

    void TryEndDrag(Collider hitCol)
    {
        _activeHandle.EndDrag();
        if (_activeHandle.type == WPHandleType.Center && _activeHandle.panel)
        {
            _activeHandle.panel.centerDragEnabled = false;
            _activeHandle.panel.moveAnchorWorld = null;
        }
        _activeHandle = null;
        if (hitCol) _lockoutCollider = hitCol;
    }

    void UpdateStillness(Ray ray, Collider hitCol)
    {
        float ang = Vector3.Angle(_prevDir, ray.direction);
        _stillTimer = (ang < stillAngleDeg) ? _stillTimer + Time.deltaTime : 0f;
        _prevDir = ray.direction;

        if (_stillTimer >= exitAfterSeconds) TryEndDrag(hitCol);
    }
    #endregion

    #region --- Dwell ---
    void ResetDwell(Ray ray, WorldPanelPlus p = null, WorldPanelPlusHandle h = null)
    {
        _candidatePanel = p; _candidateHandle = h;
        _dwellTimer = 0f; _dwellDir = ray.direction;
    }

    void HandleDwellOnDockButton(WorldPanelPlusDockButton btn, Ray ray, Collider hitCol)
    {
        if (_candidatePanel != btn.panel || _candidateHandle)
            ResetDwell(ray, btn.panel);
        else
        {
            if (Vector3.Angle(_dwellDir, ray.direction) <= dwellMaxAngle) _dwellTimer += Time.deltaTime;
            else ResetDwell(ray, btn.panel);

            if (_dwellTimer >= dwellSeconds)
            {
                ExecuteDockAction(btn, ray);
                _lockoutCollider = hitCol;
                _dwellTimer = 0f;
            }
        }
    }

    void HandleDwellOnHandles(WorldPanelPlus p, WorldPanelPlusHandle h, Ray ray)
    {
        if (p != _candidatePanel || h != _candidateHandle) ResetDwell(ray, p, h);
        else if (useDwellActivation && p)
        {
            if (Vector3.Angle(_dwellDir, ray.direction) <= dwellMaxAngle) _dwellTimer += Time.deltaTime;
            else ResetDwell(ray, p, h);

            if (_dwellTimer >= dwellSeconds && h && h.type != WPHandleType.Center)
            {
                _activeHandle = h;
                _activeHandle.BeginDragFromRay(ray, eventCamera);
                _stillTimer = 0f; _prevDir = ray.direction;
                _dwellTimer = 0f;
            }
        }
    }
    #endregion

    #region --- Dock actions (giữ nguyên logic cũ) ---
    void ExecuteDockAction(WorldPanelPlusDockButton btn, Ray ray) { /* ... giữ nguyên code cũ ... */ }
    WorldPanelPlusHandle GetCenterHandle(WorldPanelPlus p) { /* ... */ return null; }
    void RotateStable(WorldPanelPlus p, Vector3 localAxis, float deg) { /* ... */ }
    void FaceCamera(WorldPanelPlus p) { /* ... */ }
    #endregion

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
