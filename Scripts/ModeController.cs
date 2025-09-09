using UnityEngine;

public enum ViewMode { VirtualSpace, RealWorld }

public class ModeController : MonoBehaviour
{
    [Header("Bind")]
    [SerializeField] private Camera backgroundReal;      // BG camera showing passthrough quad
    [SerializeField] private Camera backgroundVirtual;   // BG camera for virtual skybox/etc
    [SerializeField] private CameraPassthrough cameraPassthrough;
    [SerializeField] private GameObject virtualEnvironment;

    [Header("Transition")]
    [Min(0f)] public float transitionDuration = 0.5f;

    [Header("State")]
    public ViewMode mode = ViewMode.VirtualSpace;

    // internals
    bool _transitioning;
    Coroutine _co;
    static readonly WaitForEndOfFrame _endOfFrame = new();

    void Start()
    {
        if (!backgroundReal || !backgroundVirtual || !cameraPassthrough)
        {
            Debug.LogError("[ModeController] Missing references.");
            enabled = false;
            return;
        }
        // snap to initial state without animation on first frame
        Apply(instant: true);
    }

    public void ToggleMode()
    {
        SetMode(mode == ViewMode.VirtualSpace ? ViewMode.RealWorld : ViewMode.VirtualSpace);
    }

    public void SetMode(ViewMode newMode, bool instant = false)
    {
        if (mode == newMode && !_transitioning && !instant) return;
        mode = newMode;
        Apply(instant);
    }

    void Apply(bool instant = false)
    {
        if (_co != null) { StopCoroutine(_co); _co = null; }
        if (instant || transitionDuration <= 0f)
        {
            SetFinalStates(mode == ViewMode.RealWorld);
        }
        else
        {
            _co = StartCoroutine(TransitionRoutine(mode == ViewMode.RealWorld));
        }
    }

    System.Collections.IEnumerator TransitionRoutine(bool toReal)
    {
        if (_transitioning) yield break;
        _transitioning = true;

        // pre-enable what's needed before blend
        if (toReal)
        {
            backgroundReal.enabled = true;
            cameraPassthrough.enabled = true;
            yield return _endOfFrame; // let camera/texture warm up one frame
        }
        else
        {
            backgroundVirtual.enabled = true;
            if (virtualEnvironment) virtualEnvironment.SetActive(true);
        }

        float t0 = Time.time;
        while (true)
        {
            float t = Mathf.InverseLerp(0f, transitionDuration, Time.time - t0);
            if (t >= 1f) break;

            // smoothstep
            float s = t * t * (3f - 2f * t);
            // (optional) hook to fade post-process/UI based on s if needed
            yield return null;
        }

        SetFinalStates(toReal);
        _transitioning = false;
        _co = null;
    }

    void SetFinalStates(bool real)
    {
        // Real world
        backgroundReal.enabled = real;
        cameraPassthrough.enabled = real;

        // Virtual world
        backgroundVirtual.enabled = !real;
        if (virtualEnvironment) virtualEnvironment.SetActive(!real);
    }
}
