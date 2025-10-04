using UnityEngine;

public class MouseInputLogger : MonoBehaviour
{
    [Header("Logging")]
    public bool enableLogs = true;
    public float moveDeltaThreshold = 0.5f;   // chỉ log khi dịch chuyển đáng kể
    public float summaryEverySeconds = 1.0f;  // tóm tắt mỗi N giây

    [Header("On-screen overlay")]
    public bool showOverlay = true;

#if ENABLE_INPUT_SYSTEM
    // Input System
    private UnityEngine.InputSystem.Mouse _ms;
    private UnityEngine.InputSystem.Pointer _ptr;
    private UnityEngine.InputSystem.Pen _pen;
#endif

    // state trước đó
    private Vector2 _lastPos;
    private Vector2 _lastDelta;
    private float _lastWheel;
    private bool _lWasDown, _rWasDown, _mWasDown;
    private float _tSummary;

#if ENABLE_INPUT_SYSTEM
    void OnEnable()
    {
        _ms = UnityEngine.InputSystem.Mouse.current;
        _ptr = UnityEngine.InputSystem.Pointer.current;
        _pen = UnityEngine.InputSystem.Pen.current;

        UnityEngine.InputSystem.InputSystem.onDeviceChange += OnDeviceChange;

        if (enableLogs)
        {
            Debug.Log($"[MIL] (Input System) init: ms={_ms != null}, ptr={_ptr != null}, pen={_pen != null}");
        }
    }

    void OnDisable()
    {
        UnityEngine.InputSystem.InputSystem.onDeviceChange -= OnDeviceChange;
    }

    private void OnDeviceChange(UnityEngine.InputSystem.InputDevice dev,
                                UnityEngine.InputSystem.InputDeviceChange change)
    {
        if (!enableLogs) return;
        // Chỉ quan tâm đến Mouse/Pointer/Pen
        if (!(dev is UnityEngine.InputSystem.Mouse) &&
            !(dev is UnityEngine.InputSystem.Pointer) &&
            !(dev is UnityEngine.InputSystem.Pen)) return;

        Debug.Log($"[MIL] Device {dev.layout}/{dev.displayName}: {change}");
        // Refresh tham chiếu
        _ms = UnityEngine.InputSystem.Mouse.current;
        _ptr = UnityEngine.InputSystem.Pointer.current;
        _pen = UnityEngine.InputSystem.Pen.current;
    }
#endif

    void Update()
    {
        // ---- Lấy dữ liệu chuột/Pointer theo Input System nếu có ----
#if ENABLE_INPUT_SYSTEM
        bool hasIS = false;
        Vector2 pos = Vector2.zero, delta = Vector2.zero;
        float wheel = 0f;
        bool lDown = false, rDown = false, mDown = false;

        if (_ms != null)
        {
            hasIS = true;
            pos = _ms.position.ReadValue();
            delta = _ms.delta.ReadValue();
            var scr = _ms.scroll.ReadValue();
            wheel = scr.y;

            lDown = _ms.leftButton.isPressed;
            rDown = _ms.rightButton.isPressed;
            mDown = _ms.middleButton.isPressed;
        }
        else if (_ptr != null) // nhiều máy Android chỉ có Pointer
        {
            hasIS = true;
            pos = _ptr.position.ReadValue();
            delta = _ptr.delta.ReadValue();
            // Pointer thường không có scroll, giữ 0
            lDown = _ptr.press.isPressed;
        }
        else if (_pen != null)
        {
            hasIS = true;
            pos = _pen.position.ReadValue();
            delta = _pen.delta.ReadValue();
            lDown = _pen.tip.isPressed;
        }
#endif

        // ---- Fallback Legacy (Editor, hoặc khi đang bật Both) ----
#if !ENABLE_INPUT_SYSTEM || UNITY_EDITOR
        if (
#if ENABLE_INPUT_SYSTEM
            !hasIS
#else
            true
#endif
        )
        {
            var lp = Input.mousePosition;
            pos = new Vector2(lp.x, lp.y);
            delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            wheel = Input.GetAxis("Mouse ScrollWheel");

            lDown = Input.GetMouseButton(0);
            rDown = Input.GetMouseButton(1);
            mDown = Input.GetMouseButton(2);
        }
#endif

        // ---- Log theo thay đổi ----
        if (enableLogs)
        {
            // Move
            if ((delta - _lastDelta).sqrMagnitude > moveDeltaThreshold * moveDeltaThreshold)
            {
                Debug.Log($"[MIL] Move delta={delta} pos={pos}");
            }

            // Wheel
            if (Mathf.Abs(wheel - _lastWheel) > 0.0001f)
            {
                Debug.Log($"[MIL] Scroll wheel={wheel}");
            }

            // Buttons
            if (lDown != _lWasDown) Debug.Log($"[MIL] Left  {(lDown ? "DOWN" : "UP")}");
            if (rDown != _rWasDown) Debug.Log($"[MIL] Right {(rDown ? "DOWN" : "UP")}");
            if (mDown != _mWasDown) Debug.Log($"[MIL] Middle {(mDown ? "DOWN" : "UP")}");

            // Tóm tắt định kỳ
            _tSummary += Time.unscaledDeltaTime;
            if (_tSummary >= summaryEverySeconds)
            {
                _tSummary = 0f;
                Debug.Log($"[MIL] Summary pos={pos} delta={delta} L={lDown} R={rDown} M={mDown}");
            }
        }

        // lưu state
        _lastPos = pos;
        _lastDelta = delta;
        _lastWheel = wheel;
        _lWasDown = lDown;
        _rWasDown = rDown;
        _mWasDown = mDown;
    }

    void OnGUI()
    {
        if (!showOverlay) return;

        string txt =
            $"MouseInputLogger\n" +
#if ENABLE_INPUT_SYSTEM
            $"IS Mouse={(UnityEngine.InputSystem.Mouse.current != null)}  " +
            $"Pointer={(UnityEngine.InputSystem.Pointer.current != null)}\n" +
#else
            $"Input System: OFF\n" +
#endif
            $"pos={_lastPos}  delta={_lastDelta}\n" +
            $"L={_lWasDown} R={_rWasDown} M={_mWasDown}  wheel={_lastWheel}\n";

        var rect = new Rect(8, 8, 560, 64);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(16, 12, rect.width - 16, rect.height - 16), txt);
    }
}
