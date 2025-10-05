using UnityEngine;

public class MouseInputLogger : MonoBehaviour
{
    [Header("Logging")]
    public bool enableLogs = true;
    public float moveDeltaThreshold = 0.5f;      // chỉ log move khi thay đổi đáng kể
    public float summaryEverySeconds = 1.0f;     // tóm tắt mỗi N giây

    [Header("Click/Drag detection")]
    public float clickMaxDuration = 0.3f;        // <= thế này thì tính là click
    public float doubleClickMaxGap = 0.3f;       // khoảng cách 2 lần click
    public float dragThresholdPixels = 4f;       // vượt ngưỡng => drag

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

    // click/drag state
    private Vector2 _lDownPos, _rDownPos, _mDownPos;
    private float _lDownTime, _rDownTime, _mDownTime;
    private float _lastLClickTime, _lastRClickTime, _lastMClickTime;
    private bool _draggingL, _draggingR, _draggingM;

#if ENABLE_INPUT_SYSTEM
    void OnEnable()
    {
        _ms = UnityEngine.InputSystem.Mouse.current;
        _ptr = UnityEngine.InputSystem.Pointer.current;
        _pen = UnityEngine.InputSystem.Pen.current;

        UnityEngine.InputSystem.InputSystem.onDeviceChange += OnDeviceChange;

        if (enableLogs)
        {
            Debug.Log($"[MIL] (IS) init: Mouse={_ms != null}, Pointer={_ptr != null}, Pen={_pen != null}");
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
        if (!(dev is UnityEngine.InputSystem.Mouse) &&
            !(dev is UnityEngine.InputSystem.Pointer) &&
            !(dev is UnityEngine.InputSystem.Pen)) return;

        Debug.Log($"[MIL] Device {dev.layout}/{dev.displayName}: {change}");
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
#endif
        Vector2 pos = Vector2.zero, delta = Vector2.zero;
        float wheel = 0f;
        bool lDown = false, rDown = false, mDown = false;

#if ENABLE_INPUT_SYSTEM
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
            // Pointer thường không có scroll
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

        // ---- LOG MOVE ----
        if (enableLogs && (delta - _lastDelta).sqrMagnitude > moveDeltaThreshold * moveDeltaThreshold)
        {
            Debug.Log($"[MIL] Move delta={delta} pos={pos}");
        }

        // ---- LOG SCROLL (mỗi lần lăn) ----
        if (enableLogs && Mathf.Abs(wheel - _lastWheel) > 0.0001f)
        {
            // một số thiết bị trả về giá trị rất nhỏ → nhân lên cho dễ đọc
            float steps = wheel; // để nguyên thô; bạn có thể *100 nếu muốn
            Debug.Log($"[MIL] Scroll steps={steps:+0.###;-0.###;0} (raw={wheel})");
        }

        // ---- DOWN/UP + CLICK/DOUBLECLICK + DRAG cho từng nút ----
        HandleButton("Left", lDown, ref _lWasDown, ref _lDownTime, ref _lastLClickTime,
                     ref _draggingL, ref _lDownPos, pos, delta);
        HandleButton("Right", rDown, ref _rWasDown, ref _rDownTime, ref _lastRClickTime,
                     ref _draggingR, ref _rDownPos, pos, delta);
        HandleButton("Middle", mDown, ref _mWasDown, ref _mDownTime, ref _lastMClickTime,
                     ref _draggingM, ref _mDownPos, pos, delta);

        // ---- TÓM TẮT ĐỊNH KỲ ----
        if (enableLogs)
        {
            _tSummary += Time.unscaledDeltaTime;
            if (_tSummary >= summaryEverySeconds)
            {
                _tSummary = 0f;
                Debug.Log($"[MIL] Summary pos={pos} delta={delta} L={lDown} R={rDown} M={mDown} wheel={wheel}");
            }
        }

        // lưu state
        _lastPos = pos;
        _lastDelta = delta;
        _lastWheel = wheel;
    }

    private void HandleButton(string name,
                              bool isDown,
                              ref bool wasDown,
                              ref float downTime,
                              ref float lastClickTime,
                              ref bool dragging,
                              ref Vector2 downPos,
                              Vector2 pos,
                              Vector2 delta)
    {
        if (!enableLogs) { wasDown = isDown; return; }

        // Down
        if (isDown && !wasDown)
        {
            downTime = Time.unscaledTime;
            downPos = pos;
            dragging = false;
            Debug.Log($"[MIL] {name} DOWN @ {pos}");
        }

        // Drag begin / drag
        if (isDown)
        {
            if (!dragging && (pos - downPos).sqrMagnitude > dragThresholdPixels * dragThresholdPixels)
            {
                dragging = true;
                Debug.Log($"[MIL] {name} DRAG BEGIN (start={downPos})");
            }
            if (dragging && (delta.sqrMagnitude > moveDeltaThreshold * moveDeltaThreshold))
            {
                Debug.Log($"[MIL] {name} DRAG delta={delta} pos={pos}");
            }
        }

        // Up (+ click / double click / drag end)
        if (!isDown && wasDown)
        {
            float held = Time.unscaledTime - downTime;
            if (dragging)
            {
                Debug.Log($"[MIL] {name} DRAG END  (held={held:0.###}s, from={downPos} -> {pos})");
            }
            Debug.Log($"[MIL] {name} UP   (held={held:0.###}s)");

            // Click?
            if (!dragging && held <= clickMaxDuration)
            {
                // Double click?
                if (Time.unscaledTime - lastClickTime <= doubleClickMaxGap)
                {
                    Debug.Log($"[MIL] {name} DOUBLE CLICK @ {pos}");
                    lastClickTime = 0; // reset
                }
                else
                {
                    Debug.Log($"[MIL] {name} CLICK @ {pos}");
                    lastClickTime = Time.unscaledTime;
                }
            }
        }

        wasDown = isDown;
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
            $"wheel={_lastWheel}\n";

        var rect = new Rect(8, 8, 560, 72);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(16, 12, rect.width - 16, rect.height - 16), txt);
    }
}
