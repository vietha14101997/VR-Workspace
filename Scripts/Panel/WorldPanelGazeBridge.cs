using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// Gaze bridge – Focus panel bằng dwell hoặc tap -> warp cursor khi ĐỔI panel.
/// Con trỏ KHÔNG chạy theo reticle liên tục.
/// Điều khiển cursor bằng chuột/trackpad.
public class WorldPanelGazeBridge : MonoBehaviour
{
    public Camera eventCamera;
    public LayerMask interactMask = ~0;
    public float maxDistance = 100f;

    [Header("Focus dwell (đổi panel)")]
    public float focusDwellSeconds = 1.0f;
    [Range(1f, 15f)] public float focusDwellMaxAngle = 8f;
    [Range(0f, 1f)] public float focusDwellSmoothing = 0.25f;

    [Header("Debug")]
    public bool debugLogs = false;

    // Focus state
    WorldPanelPlus _focusedPanel;
    WorldPanelPlus _focusCandidate;
    float _focusDwellTimer = 0f;
    Vector3 _focusBaselineDir;
    Vector3 _focusSmoothedDir;

    bool _prevPressed = false;

    // Mouse delta
    Vector2 _prevMousePos;
    bool _hasPrevMouse;

    void Awake()
    {
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

        Ray ray = new Ray(eventCamera.transform.position, eventCamera.transform.forward);
        var hits = Physics.RaycastAll(ray, maxDistance, interactMask.value);

        // Tìm panel dưới tầm nhìn (chỉ Board)
        WorldPanelPlus hitPanel = null;
        Vector3 warpPoint = Vector3.zero;
        bool warpPointValid = false;
        float bestDist = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            var board = h.collider.GetComponent<WorldPanelPlusBoardRaycatcher>();
            if (board && h.distance < bestDist)
            {
                bestDist = h.distance;
                hitPanel = board.panel;
                warpPoint = h.point;
                warpPointValid = true;
            }
        }

        // Input states
        bool pressed = IsPressed();
        bool down = pressed && !_prevPressed;
        bool up = !pressed && _prevPressed;
        _prevPressed = pressed;

        // Tap-to-focus
        if (down && hitPanel != null && hitPanel != _focusedPanel)
        {
            if (WorldPanelPlus.InSameCluster(_focusedPanel, hitPanel))
            {
                _focusCandidate = null;
                _focusDwellTimer = 0f;
            }
            else
            {
                if (!warpPointValid && hitPanel.board)
                    warpPoint = hitPanel.board.position;
                SetFocusTo(hitPanel, warpPoint, eventCamera);
            }
        }

        // Focus bằng dwell
        if (hitPanel != null && hitPanel != _focusedPanel && !WorldPanelPlus.InSameCluster(_focusedPanel, hitPanel))
        {
            if (_focusCandidate != hitPanel)
            {
                _focusCandidate = hitPanel;
                _focusDwellTimer = 0f;
                _focusBaselineDir = ray.direction;
                _focusSmoothedDir = ray.direction;
                if (debugLogs) Debug.Log("[Gaze] start dwell candidate=" + _focusCandidate.name);
            }
            else
            {
                float k = Mathf.Clamp01(focusDwellSmoothing);
                _focusSmoothedDir = Vector3.Slerp(_focusSmoothedDir, ray.direction, k);
                float a = Vector3.Angle(_focusBaselineDir, _focusSmoothedDir);

                if (a <= focusDwellMaxAngle) _focusDwellTimer += Time.deltaTime;
                else { _focusBaselineDir = _focusSmoothedDir; _focusDwellTimer = 0f; }

                if (_focusDwellTimer >= focusDwellSeconds)
                {
                    Vector3 wp = warpPointValid ? warpPoint : (hitPanel.board ? hitPanel.board.position : Vector3.zero);
                    SetFocusTo(hitPanel, wp, eventCamera);
                }
            }
        }
        else
        {
            _focusCandidate = null;
            _focusDwellTimer = 0f;
        }

        // Điều khiển cursor bằng chuột
        if (_focusedPanel && _focusedPanel.cursor && _focusedPanel.cursor.visible)
        {
            var md = GetMouseDeltaUniversal();
            if (md.sqrMagnitude > 0.000001f)
                WorldPanelPlus.CursorMoveInCluster(ref _focusedPanel, md.x, md.y);
            if (down) _focusedPanel.CursorClickDown();
            if (up) _focusedPanel.CursorClickUp();
        }

        // Đảm bảo cursor panel focus luôn bật
        if (_focusedPanel && _focusedPanel.cursor && !_focusedPanel.cursor.visible)
            _focusedPanel.cursor.SetVisible(true);
    }

    void SetFocusTo(WorldPanelPlus newPanel, Vector3 worldPoint, Camera cam)
    {
        if (newPanel == null) return;

        if (_focusedPanel && debugLogs) Debug.Log("[Gaze] focus -> " + newPanel.name);
        if (_focusedPanel) _focusedPanel.CursorFocusEnd();

        _focusedPanel = newPanel;
        _focusedPanel.EnsureCursor();
        _focusedPanel.CursorFocusBegin(worldPoint, cam);
        _focusDwellTimer = 0f;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidTryPointerCapture(true);
#endif
    }

    bool IsPressed()
    {
#if UNITY_ANDROID
        try { if (Google.XR.Cardboard.Api.IsTriggerPressed) return true; } catch { }
#endif

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.leftButton.isPressed;

        if (Pointer.current != null)
            return Pointer.current.press.isPressed;

        if (Pen.current != null)
            return Pen.current.tip.isPressed;
#endif

        if (Input.touchCount > 0)
        {
            var ph = Input.GetTouch(0).phase;
            return ph != UnityEngine.TouchPhase.Ended && ph != UnityEngine.TouchPhase.Canceled;
        }

#if !ENABLE_INPUT_SYSTEM || UNITY_EDITOR
        return Input.GetMouseButton(0);
#else
        return false;
#endif
    }

    Vector2 GetMouseDeltaUniversal()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.delta.ReadValue();

        if (Pointer.current != null)
            return Pointer.current.delta.ReadValue();
#endif
        float dx = Input.GetAxisRaw("Mouse X");
        float dy = Input.GetAxisRaw("Mouse Y");
        if (Mathf.Abs(dx) > 1e-4f || Mathf.Abs(dy) > 1e-4f)
            return new Vector2(dx, dy);

        var pos = (Vector2)Input.mousePosition;
        if (!_hasPrevMouse) { _prevMousePos = pos; _hasPrevMouse = true; return Vector2.zero; }
        Vector2 d = pos - _prevMousePos;
        _prevMousePos = pos;
        return d;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    public static void AndroidTryPointerCapture(bool enable)
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
            using (var decor = window.Call<AndroidJavaObject>("getDecorView"))
            {
                if (enable)
                    decor.Call("releasePointerCapture");
            }
        }
        catch { }
    }
#endif
}
