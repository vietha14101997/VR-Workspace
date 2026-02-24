using UnityEngine;
using VRWorkspace.CameraUtils;
using VRWorkspace.Media.Core;

namespace VRWorkspace.Core
{
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
        public float transitionDuration = 0f;

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

            // Initialize MediaEnvironmentController
            if (MediaEnvironmentController.Instance != null)
            {
                MediaEnvironmentController.Instance.Initialize();
            }

            Apply();
        }

        public void ToggleMode()
        {
            mode = (mode == ViewMode.VirtualSpace) ? ViewMode.RealWorld : ViewMode.VirtualSpace;
            Apply();
        }

        /// <summary>
        /// Set mode directly (used by VRTaskbar passthrough button).
        /// </summary>
        public void SetMode(ViewMode newMode)
        {
            if (mode != newMode)
            {
                mode = newMode;
                Apply();
            }
        }

        System.Collections.IEnumerator TransitionRoutine(bool toRealWorld)
        {
            if (isTransitioning) yield break;
            isTransitioning = true;

            bool real = (mode == ViewMode.RealWorld);

            // Enable necessary cameras for transition
            if (real)
            {
                backgroundReal.enabled = true;
                cameraPassthrough.enabled = true;
                yield return new WaitForEndOfFrame(); // Wait for camera to initialize
            }

            // Set final states
            if (real)
            {
                backgroundVirtual.enabled = false;

                // Use MediaEnvironmentController for robust visibility tracking
                if (MediaEnvironmentController.Instance != null)
                {
                    MediaEnvironmentController.Instance.UpdateEnvironmentVisibility(false, "Passthrough");
                }
                else if (virtualEnvironment)
                {
                    virtualEnvironment.SetActive(false);
                }
            }
            else
            {
                backgroundReal.enabled = false;
                cameraPassthrough.enabled = false;
                backgroundVirtual.enabled = true;

                // Use MediaEnvironmentController for robust visibility tracking
                if (MediaEnvironmentController.Instance != null)
                {
                    MediaEnvironmentController.Instance.UpdateEnvironmentVisibility(true, "Passthrough");
                }
                else if (virtualEnvironment)
                {
                    virtualEnvironment.SetActive(true);
                }
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

}
