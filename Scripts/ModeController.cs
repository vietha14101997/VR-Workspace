using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum ViewMode { VirtualSpace, RealWorld }

public class ModeController : MonoBehaviour
{
    public Camera backgroundReal;     // BackgroundCamera_Real
    public Camera backgroundVirtual;  // BackgroundCamera_Virtual
    public GameObject virtualObjects; // luôn bật
    public GameObject virtualEnvironment; // chỉ để tiện bật/tắt nếu muốn giấu ở Real
    public WorldTransitionManager transitionManager; // Reference to transition manager
    public float transitionDuration = 0.5f;
    
    private bool isTransitioning;
    private Coroutine transitionCoroutine;

    public ViewMode mode = ViewMode.VirtualSpace;

    void Start()
    {
        // Ensure we have a reference to the transition manager
        if (transitionManager == null)
        {
            transitionManager = FindObjectOfType<WorldTransitionManager>();
            if (transitionManager == null)
            {
                Debug.LogError("WorldTransitionManager not found in scene!");
            }
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

        // Start the visual transition using WorldTransitionManager
        if (transitionManager != null)
        {
            transitionManager.SetWorldMode(mode, false);
        }

        // Wait for half the transition time before changing actual game objects
        yield return new WaitForSeconds(transitionDuration * 0.5f);

        // Apply actual mode change at the transition midpoint
        bool real = (mode == ViewMode.RealWorld);
        
        // These cameras are now controlled by WorldTransitionManager
        // but we still need to update the game objects
        if (virtualObjects) virtualObjects.SetActive(true);
        if (virtualEnvironment) virtualEnvironment.SetActive(!real);

        // Wait for the rest of the transition to complete
        yield return new WaitForSeconds(transitionDuration * 0.5f);

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
