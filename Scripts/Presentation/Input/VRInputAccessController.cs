using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace VRWorkspace.VRInput
{
    /// <summary>
    /// Boot-time gatekeeper with three responsibilities:
    /// <list type="number">
    ///   <item><b>Hides the system cursor visually</b> so that the
    ///         Android/Linux/Win cursor is never visible on the VR
    ///         display when a mouse is connected.</item>
    ///   <item><b>Keeps mouse / touch / keyboard signals readable</b>
    ///         via the new Input System. A future in-app custom cursor
    ///         can read <c>Mouse.current.position</c>,
    ///         <c>Mouse.current.delta</c>, <c>Mouse.current.leftButton</c>
    ///         etc. to drive its own pointer logic. Same for
    ///         <c>Touchscreen.current</c> and <c>Keyboard.current</c>.</item>
    ///   <item><b>Replaces the EventSystem's <c>BaseInputModule</c></b>
    ///         (typically <c>InputSystemUIInputModule</c>) with a
    ///         <see cref="NullInputModule"/> so that mouse / touch /
    ///         gamepad pointer input can no longer fire UI events
    ///         directly. UI clicks are now exclusively driven by
    ///         <see cref="VRGazeReticle"/>'s Dwell-Click (or a future
    ///         custom cursor) going through
    ///         <see cref="RTTRaycastManager.SendClick"/>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Design contract for the future custom cursor:
    /// <list type="bullet">
    ///   <item><c>Mouse.current.delta.ReadValue()</c> — relative movement since last frame (works regardless of lockState)</item>
    ///   <item><c>Mouse.current.position.ReadValue()</c> — absolute screen position (requires lockState = None)</item>
    ///   <item><c>Mouse.current.leftButton.wasPressedThisFrame</c> — click detection</item>
    ///   <item>To fire UI events on the RTT panel: use
    ///         <c>RTTRaycastManager.Instance.SendClick()</c> exactly like
    ///         Reticle Dwell-Click does.</item>
    /// </list>
    /// </para>
    /// <para>
    /// We deliberately do <b>not</b> call <c>InputSystem.DisableDevice(Mouse)</c>
    /// or <c>DisableDevice(Touchscreen)</c>. The new Input System device
    /// objects must stay enabled so that <see cref="RemoteInputBridge"/>
    /// can continue to read raw BT-mouse movement and forward it to the
    /// remote desktop server, AND so the future custom cursor can use
    /// mouse signals.
    /// </para>
    /// <para>
    /// The controller self-instantiates via
    /// <c>[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]</c>, so no scene
    /// edits are required. Its Awake runs after the active scene's
    /// EventSystem has been registered with EventSystem.current.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-10000)]
    public class VRInputAccessController : MonoBehaviour
    {
        /// <summary>
        /// Auto-create a hidden GameObject carrying this controller before
        /// any scene loads. Execution order is the earliest possible so
        /// we always exist before EventSystem.Awake runs.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<VRInputAccessController>() != null) return;

            var go = new GameObject("[VRInputAccessController]");
            go.AddComponent<VRInputAccessController>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            // 1. Hide cursor immediately — even before the first frame renders.
            ApplyCursorHidden();

            // 2. Replace EventSystem input modules. EventSystem.current is
            //    guaranteed to be valid here because Awake of every scene
            //    MonoBehaviour runs after all GameObjects in the scene
            //    (and their components) have been created.
            ReplaceEventSystemInputModules();

            // 3. Hook scene-loaded callback so a fresh EventSystem in a
            //    newly-loaded scene is also gated.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Re-apply cursor + replace modules — a new EventSystem may
            // have been spawned with the default InputSystemUIInputModule.
            ApplyCursorHidden();
            ReplaceEventSystemInputModules();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Re-apply on focus regain — Android/Linux may unhide the
            // cursor when the app comes back to the foreground.
            if (hasFocus) ApplyCursorHidden();
        }

        private static void ApplyCursorHidden()
        {
            // On Android, use Locked mode to completely hide the system mouse
            // pointer and pin it to screen center. This prevents the invisible
            // cursor from reaching the status bar edge and triggering the
            // notification shade pull-down. Locked is safe here because
            // MouseDeltaDriver uses Mouse.current.delta (relative movement),
            // never Mouse.current.position (absolute), so cursor lock doesn't
            // affect VCS input at all.
            // On Editor/Desktop we keep None for normal development workflow.
#if UNITY_ANDROID && !UNITY_EDITOR
            Cursor.lockState = CursorLockMode.Locked;
#else
            Cursor.lockState = CursorLockMode.None;
#endif
            Cursor.visible = false;
        }

        /// <summary>
        /// Destroy every BaseInputModule on EventSystem.current (typically
        /// InputSystemUIInputModule), then attach a NullInputModule so
        /// no auto-driven pointer events ever reach UI.
        /// </summary>
        private static void ReplaceEventSystemInputModules()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return;

            // Strip all existing modules — they poll input devices and fire UI events.
            // Idempotent: if we run twice we still end up with exactly one NullInputModule.
            var modules = eventSystem.GetComponents<BaseInputModule>();
            foreach (var module in modules)
            {
                if (module == null) continue;
                if (module is NullInputModule) continue;
                Destroy(module);
            }

            // Attach our gatekeeper module if not already present.
            if (eventSystem.GetComponent<NullInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<NullInputModule>();
            }
        }

#if UNITY_EDITOR
        private void OnApplicationQuit()
        {
            // Restore cursor when stopping play in Editor so the OS cursor
            // isn't left hidden/locked.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
#endif
    }
}