using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// Gaze bridge – Focus panel bằng dwell 1s (hoặc tap) -> warp cursor 1 lần khi ĐỔI panel.
/// Con trỏ KHÔNG chạy theo reticle liên tục.
/// Bản vá Android: chống rung, chấp nhận hit Hover/Tray (nội suy điểm warp lên mặt phẳng Board),
/// gom delta chuột đa nền tảng, và pointer-capture để ẩn con trỏ hệ thống & lấy relative mouse.
public class WorldPanelGazeBridge : MonoBehaviour
{
    public Camera eventCamera;
    public LayerMask interactMask = ~0;
    public float maxDistance = 100f;

    [Header("Handle dwell (kéo bằng nhìn)")]
    public bool useDwellActivation = true;
    public float dwellSeconds = 1.0f;                 // dwell cho handle
    public float dwellMaxAngle = 3.0f;                // độ lệch cho dwell handle

    [Header("Focus dwell (đổi panel)")]
    public float focusDwellSeconds = 1.0f;            // dwell để đổi focus panel
    [Range(1f, 15f)] public float focusDwellMaxAngle = 8f;     // dung sai rung cho Android
    [Range(0f, 1f)] public float focusDwellSmoothing = 0.25f; // low-pass 0..1

    [Header("Thoát khi giữ yên (đang kéo)")]
    public float exitAfterSeconds = 2f;
    public float stillAngleDeg = 0.4f;

    [Header("Debug")]
    public bool debugLogs = false;

    // ===== Focus state
    WorldPanelPlus _focusedPanel;
    WorldPanelPlus _focusCandidate;
    float _focusDwellTimer = 0f;
    Vector3 _focusBaselineDir;
    Vector3 _focusSmoothedDir;

    // ===== Hover & handle drag
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

    // ===== Mouse delta universal
    Vector2 _prevMousePos;
    bool _hasPrevMouse;

    void Awake()
    {
        // Nếu quên set mask -> không để 0 (lọc hết)
        if (interactMask.value == 0) interactMask = ~0;
    }

    void Reset() { eventCamera = GetComponent<Camera>(); }

    void OnDisable()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidTryPointerCapture(false);
#endif
    }

    void Update()
    {
        if (!eventCamera) eventCamera = Camera.main;
        if (!eventCamera) return;

        // Ray từ giữa màn hình
        Ray ray = new Ray(eventCamera.transform.position, eventCamera.transform.forward);
        var hits = Physics.RaycastAll(ray, maxDistance, interactMask.value);

        // Chọn "best hit": Handle(20) > Board(10) > Hover/Tray(1)
        WorldPanelPlusHandle hitHandle = null;
        WorldPanelPlus hitPanel = null;
        Collider hitCol = null;
        int bestScore = int.MinValue;
        float bestDist = float.MaxValue;
        RaycastHit bestHit = default;
        bool haveBestHit = false;

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            var handle = h.collider.GetComponent<WorldPanelPlusHandle>();
            var board = h.collider.GetComponent<WorldPanelPlusBoardRaycatcher>();
            var hoverC = h.collider.GetComponent<WorldPanelPlusTrayRaycatcher>();

            if (handle)
            {
                int score = 20;
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; hitHandle = handle; hitPanel = handle.panel; hitCol = h.collider; bestHit = h; haveBestHit = true; }
            }
            else if (board)
            {
                int score = 10;
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; hitHandle = null; hitPanel = board.panel; hitCol = h.collider; bestHit = h; haveBestHit = true; }
            }
            else if (hoverC)
            {
                int score = 1;
                if (score > bestScore || (score == bestScore && h.distance < bestDist))
                { bestScore = score; bestDist = h.distance; hitHandle = null; hitPanel = hoverC.panel; hitCol = h.collider; bestHit = h; haveBestHit = true; }
            }
        }

        // Khoá collider tạm sau khi thả để tránh bắt lại ngay
        if (_lockoutCollider != null && hitCol == _lockoutCollider) { hitHandle = null; }
        else if (_lockoutCollider != null && hitCol != _lockoutCollider) { _lockoutCollider = null; }

        // Hover chỉ để highlight (không warp cursor tại đây)
        bool hovering = (hitPanel != null);
        if (!hovering && _hoverPanel)
        {
            _hoverPanel.OnHover(false, null);
            _hoverPanel = null; _hoverHandle = null;
        }
        if (hovering)
        {
            hitPanel.OnHover(true, hitHandle ? (WPHandleType?)hitHandle.type : null);
            _hoverPanel = hitPanel; _hoverHandle = hitHandle;
        }

        // ======= Xác định panel dưới tầm nhìn + điểm warp (kể cả khi hit Hover/Tray)
        WorldPanelPlus panelUnderGaze = null;
        Vector3 warpPoint = Vector3.zero;
        bool warpPointValid = false;

        if (haveBestHit)
        {
            var br = bestHit.collider.GetComponent<WorldPanelPlusBoardRaycatcher>();
            if (br)
            {
                panelUnderGaze = br.panel;
                warpPoint = bestHit.point;
                warpPointValid = true;
            }
            else
            {
                var tr = bestHit.collider.GetComponent<WorldPanelPlusTrayRaycatcher>();
                if (tr)
                {
                    panelUnderGaze = tr.panel;
                    if (panelUnderGaze && panelUnderGaze.board)
                    {
                        var tf = panelUnderGaze.board;
                        Plane pl = new Plane(tf.forward, tf.position);
                        float t;
                        if (pl.Raycast(ray, out t))
                        {
                            warpPoint = ray.origin + ray.direction * t;
                            warpPointValid = true;
                        }
                    }
                }
            }
        }

        // ======= Input states
        bool pressed = IsPressed();
        bool down = pressed && !_prevPressed;
        bool up = !pressed && _prevPressed;
        _prevPressed = pressed;

        // ======= Tap-to-focus (fallback tức thời)
        if (down && panelUnderGaze != null && panelUnderGaze != _focusedPanel)
        {
            // Nếu cùng cụm → không đổi focus
            if (WorldPanelPlus.InSameCluster(_focusedPanel, panelUnderGaze))
            {
                _focusCandidate = null;
                _focusDwellTimer = 0f;
            }
            else
            {
                if (!warpPointValid && panelUnderGaze && panelUnderGaze.board)
                    warpPoint = panelUnderGaze.board.position;
                SetFocusTo(panelUnderGaze, warpPoint, eventCamera);
            }
        }

        // ======= Focus bằng dwell (warp ONE-SHOT khi đổi panel)
        if (panelUnderGaze != null && panelUnderGaze != _focusedPanel && !WorldPanelPlus.InSameCluster(_focusedPanel, panelUnderGaze))
        {
            if (_focusCandidate != panelUnderGaze)
            {
                _focusCandidate = panelUnderGaze;
                _focusDwellTimer = 0f;
                _focusBaselineDir = ray.direction;
                _focusSmoothedDir = ray.direction;
                if (debugLogs) Debug.Log("[Gaze] start dwell candidate=" + _focusCandidate.name);
            }
            else
            {
                // Low-pass filter chống rung
                float k = Mathf.Clamp01(focusDwellSmoothing);
                _focusSmoothedDir = Vector3.Slerp(_focusSmoothedDir, ray.direction, k);
                float a = Vector3.Angle(_focusBaselineDir, _focusSmoothedDir);

                if (a <= focusDwellMaxAngle) _focusDwellTimer += Time.deltaTime;
                else { _focusBaselineDir = _focusSmoothedDir; _focusDwellTimer = 0f; }

                if (_focusDwellTimer >= focusDwellSeconds)
                {
                    Vector3 wp = warpPointValid
                        ? warpPoint
                        : (panelUnderGaze && panelUnderGaze.board ? panelUnderGaze.board.position : bestHit.point);
                    SetFocusTo(panelUnderGaze, wp, eventCamera);
                }
            }
        }
        else
        {
            _focusCandidate = null;
            _focusDwellTimer = 0f;
        }

        // ======= Kéo handle bằng dwell/nhấn như cũ
        if (_activeHandle == null) HandleDwellOnHandles(hitPanel, hitHandle, ray);

        if (_activeHandle == null && down && hitHandle != null)
        {
            _activeHandle = hitHandle;
            _activeHandle.BeginDragFromRay(ray, eventCamera);
            _stillTimer = 0f; _prevDir = ray.direction;
        }

        if (_activeHandle != null && (pressed || useDwellActivation))
        {
            _activeHandle.UpdateDragFromRay(ray, eventCamera, Time.deltaTime);
            if (_activeHandle.type == WPHandleType.Center && _activeHandle.panel)
                _activeHandle.panel.EnforceNoRoll();

            float ang = Vector3.Angle(_prevDir, ray.direction);
            if (ang < stillAngleDeg) _stillTimer += Time.deltaTime; else _stillTimer = 0f;
            _prevDir = ray.direction;

            if (_stillTimer >= exitAfterSeconds)
            {
                _activeHandle.EndDrag();
                _activeHandle = null;
                if (hitCol) _lockoutCollider = hitCol;
            }
        }

        if (_activeHandle != null && up)
        {
            _activeHandle.EndDrag();
            _activeHandle = null;
            if (hitCol) _lockoutCollider = hitCol;
        }

        // ======= Chuột phần cứng chỉ điều khiển con trỏ của PANEL ĐANG FOCUS
        if (_activeHandle == null && _focusedPanel && _focusedPanel.cursor && _focusedPanel.cursor.visible)
        {
            var md = GetMouseDeltaUniversal();
            if (md.sqrMagnitude > 0.000001f)
                WorldPanelPlus.CursorMoveInCluster(ref _focusedPanel, md.x, md.y);
            if (down) _focusedPanel.CursorClickDown();
            if (up) _focusedPanel.CursorClickUp();
        }

        // Đảm bảo cursor panel đã focus luôn bật (phòng case bị tắt bởi nhánh khác)
        if (_focusedPanel && _focusedPanel.cursor && !_focusedPanel.cursor.visible)
            _focusedPanel.cursor.SetVisible(true);
    }

    // ===== helpers =====

    void SetFocusTo(WorldPanelPlus newPanel, Vector3 worldPoint, Camera cam)
    {
        if (newPanel == null) return;

        if (_focusedPanel && debugLogs) Debug.Log("[Gaze] focus -> " + newPanel.name);
        if (_focusedPanel) _focusedPanel.CursorFocusEnd();

        _focusedPanel = newPanel;
        _focusedPanel.EnsureCursor();                 // an toàn nếu chưa có
        _focusedPanel.CursorFocusBegin(worldPoint, cam); // warp ONE-SHOT
        _focusDwellTimer = 0f;

        // Android: bắt pointer capture để ẩn OS cursor & nhận relative mouse
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidTryPointerCapture(true);
#endif
    }

    void HandleDwellOnHandles(WorldPanelPlus hitPanel, WorldPanelPlusHandle hitHandle, Ray ray)
    {
        if (hitPanel != _candidatePanel || hitHandle != _candidateHandle)
        {
            _candidatePanel = hitPanel;
            _candidateHandle = hitHandle;
            _dwellTimer = 0f;
            _dwellDir = ray.direction;
        }
        else if (useDwellActivation && _candidatePanel != null && _candidateHandle != null)
        {
            float a = Vector3.Angle(_dwellDir, ray.direction);
            if (a <= dwellMaxAngle) _dwellTimer += Time.deltaTime;
            else { _dwellTimer = 0f; _dwellDir = ray.direction; }

            if (_dwellTimer >= dwellSeconds)
            {
                _activeHandle = _candidateHandle;
                _activeHandle.BeginDragFromRay(ray, eventCamera);
                _stillTimer = 0f; _prevDir = ray.direction;
                _dwellTimer = 0f;
            }
        }
    }

    bool IsPressed()
    {
#if UNITY_ANDROID
        // Cardboard trigger nếu có
        try { return Google.XR.Cardboard.Api.IsTriggerPressed; } catch { }
#endif
        if (Input.touchCount > 0)
        {
            var ph = Input.GetTouch(0).phase; // legacy Input
            return ph != UnityEngine.TouchPhase.Ended && ph != UnityEngine.TouchPhase.Canceled;
        }
        return Input.GetMouseButton(0);
    }

    Vector2 GetMouseDeltaUniversal()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            return Mouse.current.delta.ReadValue(); // relative pixels/frame
        }
#endif
        float dx = Input.GetAxisRaw("Mouse X");
        float dy = Input.GetAxisRaw("Mouse Y");
        if (Mathf.Abs(dx) > 1e-4f || Mathf.Abs(dy) > 1e-4f)
            return new Vector2(dx, dy);

        // Fallback: tự tính theo mousePosition
        var pos = (Vector2)Input.mousePosition;
        if (!_hasPrevMouse) { _prevMousePos = pos; _hasPrevMouse = true; return Vector2.zero; }
        Vector2 d = pos - _prevMousePos;
        _prevMousePos = pos;
        return d;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // API 26+: bắt/nhả pointer capture để ẩn OS cursor và nhận relative mouse event
    void AndroidTryPointerCapture(bool capture)
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
            using (var view = window.Call<AndroidJavaObject>("getDecorView"))
            {
                if (capture) view.Call("requestPointerCapture");
                else         view.Call("releasePointerCapture");
            }
        }
        catch { /* Thiết bị cũ hơn API 26: bỏ qua */ }
    }
#endif
}
