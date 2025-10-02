using UnityEngine;
using System.Collections;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum ViewMode { VirtualSpace, RealWorld }

public class ModeController : MonoBehaviour
{
    [Header("Cameras")]
    public Camera backgroundReal;          // Base (Culling: Passthrough) — LUÔN BẬT
    public Camera backgroundVirtual;       // Overlay (Culling: VirtualEnvironment)

    [Header("Passthrough")]
    public CameraPassthrough cameraPassthrough; // gắn trên PassthroughQuad (con của backgroundReal)

    [Header("Scene Roots (optional)")]
    public GameObject virtualEnvironment;  // tắt/bật mesh môi trường nếu muốn tiết kiệm

    [Header("Transition (optional)")]
    public CanvasGroup fadeCanvas;         // UI full-screen đen (alpha 0..1). Có thể để trống
    public float transitionDuration = 0.25f;

    [Header("State")]
    public ViewMode mode = ViewMode.VirtualSpace;

    bool _isTransitioning;
    Coroutine _co;

    void Start()
    {
        // Kiểm tra nhưng KHÔNG dừng hẳn—để bạn có thể chạy 1 chế độ tối thiểu
        if (!backgroundReal) Debug.LogError("ModeController: backgroundReal (Base) is not set.");
        if (!backgroundVirtual) Debug.LogError("ModeController: backgroundVirtual (Overlay) is not set.");
        if (!cameraPassthrough) Debug.LogWarning("ModeController: cameraPassthrough not set (RealWorld mode will be blank).");

        // Base luôn bật để giữ stack ổn định
        if (backgroundReal) backgroundReal.enabled = true;

        // Áp dụng trạng thái ban đầu không cần fade
        ApplyImmediate(mode);
    }

    public void ToggleMode()
    {
        SetMode(mode == ViewMode.VirtualSpace ? ViewMode.RealWorld : ViewMode.VirtualSpace);
    }

    public void SetMode(ViewMode newMode)
    {
        if (mode == newMode) return;
        mode = newMode;

        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(TransitionRoutine(mode));
    }

    IEnumerator TransitionRoutine(ViewMode targetMode)
    {
        if (_isTransitioning) yield break;
        _isTransitioning = true;

        // Fade out
        if (fadeCanvas)
        {
            float t = 0f;
            while (t < transitionDuration)
            {
                t += Time.unscaledDeltaTime;
                fadeCanvas.alpha = Mathf.Clamp01(t / transitionDuration);
                yield return null;
            }
            fadeCanvas.alpha = 1f;
        }

        // Chuyển trạng thái NGAY LÚC NÀY (Base vẫn bật)
        ApplyImmediate(targetMode);

        // Fade in
        if (fadeCanvas)
        {
            float t = 0f;
            while (t < transitionDuration)
            {
                t += Time.unscaledDeltaTime;
                fadeCanvas.alpha = 1f - Mathf.Clamp01(t / transitionDuration);
                yield return null;
            }
            fadeCanvas.alpha = 0f;
        }

        _isTransitioning = false;
        _co = null;
    }

    void ApplyImmediate(ViewMode m)
    {
        bool toVirtual = (m == ViewMode.VirtualSpace);

        // Base luôn bật
        if (backgroundReal) backgroundReal.enabled = true;

        // Toggle Overlay Virtual
        if (backgroundVirtual) backgroundVirtual.enabled = toVirtual;

        // Toggle Passthrough (script chạy trên quad thuộc Base)
        if (cameraPassthrough) cameraPassthrough.enabled = !toVirtual;

        // Tùy chọn tắt/bật mesh môi trường
        if (virtualEnvironment) virtualEnvironment.SetActive(toVirtual);
    }
}
