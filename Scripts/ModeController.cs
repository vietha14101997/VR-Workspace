using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum ViewMode { VirtualSpace, RealWorld }

public class ModeController : MonoBehaviour
{
    public Camera backgroundReal;     // BackgroundCamera_Real
    public Camera backgroundVirtual;  // BackgroundCamera_Virtual
    public CameraPassthrough cameraPassthrough; // Script trên PassthroughQuad
    public GameObject virtualEnvironment; // Virtual environment objects
    public float transitionDuration = 0.5f;

    private bool isTransitioning;
    private Coroutine transitionCoroutine;

    public ViewMode mode = ViewMode.VirtualSpace;

    void Start()
    {
        // Validate required references
        if (backgroundReal == null || backgroundVirtual == null)
        {
            Debug.LogError("Background cameras not set in ModeController!");
            return;
        }

        if (cameraPassthrough == null)
        {
            Debug.LogError("CameraPassthrough reference not set in ModeController!");
            return;
        }

        Apply();
    }

    public void ToggleMode()
    {
        mode = (mode == ViewMode.VirtualSpace) ? ViewMode.RealWorld : ViewMode.VirtualSpace;
        Apply();
    }

    System.Collections.IEnumerator TransitionRoutine(bool toRealWorld)
    {
        if (isTransitioning) yield break;
        isTransitioning = true;

        float startTime = Time.time;
        bool real = (mode == ViewMode.RealWorld);

        // Enable necessary cameras for transition
        if (real)
        {
            backgroundReal.enabled = true;
            cameraPassthrough.enabled = true;
            yield return new WaitForEndOfFrame(); // Wait for camera to initialize
        }

        // Animate transition
        while (Time.time - startTime < transitionDuration)
        {
            float t = (Time.time - startTime) / transitionDuration;

            // Apply smoothstep for more natural easing
            t = t * t * (3f - 2f * t); // Smoothstep formula

            // Fade virtual environment opacity if needed
            if (virtualEnvironment)
            {
                // You can add fade effect here if needed
            }

            yield return null;
        }

        // Set final states
        if (real)
        {
            backgroundVirtual.enabled = false;
            if (virtualEnvironment) virtualEnvironment.SetActive(false);
        }
        else
        {
            backgroundReal.enabled = false;
            cameraPassthrough.enabled = false;
            backgroundVirtual.enabled = true;
            if (virtualEnvironment) virtualEnvironment.SetActive(true);
        }

        isTransitioning = false;
    }

    void Apply()
    {
        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        bool toRealWorld = (mode == ViewMode.RealWorld);
        transitionCoroutine = StartCoroutine(TransitionRoutine(toRealWorld));
    }
}
