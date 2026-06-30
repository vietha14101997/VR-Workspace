using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace VRWorkspace.VRInput
{
    /// <summary>
    /// Boot-time gatekeeper that owns two responsibilities:
    /// <list type="number">
    ///   <item>Hides the system cursor and locks it in place so that
    ///         the Android/Linux/Win cursor is never visible on the
    ///         VR display when a mouse is connected.</item>
    ///   <item>Replaces the EventSystem's <c>BaseInputModule</c>
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
    /// We deliberately do <b>not</b> call <c>InputSystem.DisableDevice(Mouse)</c>
    /// or <c>DisableDevice(Touchscreen)</c>. The new Input System device
    /// objects must stay enabled so that <see cref="RemoteInputBridge"/>
    /// can continue to read raw BT-mouse movement and forward it to the
    /// remote desktop server.
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
            Cursor.lockState = CursorLockMode.Locked;
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