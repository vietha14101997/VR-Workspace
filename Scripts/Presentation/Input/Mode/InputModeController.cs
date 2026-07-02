using UnityEngine;
using UnityEngine.InputSystem;
using VRWorkspace.Domain.Input;
using VRWorkspace.Presentation.Input.Cursor;
using VRWorkspace.VRInput;

namespace VRWorkspace.Presentation.Input.Mode
{
    /// <summary>
    /// Determines which <see cref="InputMode"/> is currently active based on connected
    /// devices + recent input activity, and toggles the right subsystems.
    ///
    /// Priority: Gamepad &gt; Mouse &gt; Gaze.
    ///   - Gamepad present → Gamepad mode (left stick drives cursor).
    ///   - Mouse delta detected → Mouse mode.
    ///   - Otherwise → Gaze mode (default).
    ///
    /// Gamepad "presence" is sticky: once we are in Gamepad mode, the mode only reverts
    /// when the gamepad is removed. Mouse mode is "active if delta observed recently"
    /// to avoid thrashing when the user rests their hand on a BT mouse.
    /// </summary>
    [DefaultExecutionOrder(-7000)]
    public sealed class InputModeController : MonoBehaviour
    {
        public static InputModeController Instance { get; private set; }

        [SerializeField] private float mouseIdleToGazeSeconds = 5f;

        private InputMode _currentMode = InputMode.Gaze;
        private float _lastMouseDeltaTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[InputModeController]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<InputModeController>();
        }

        private void OnEnable()
        {
            if (DeviceWatcher.Instance != null)
                DeviceWatcher.Instance.DeviceChanged += OnDeviceChanged;
        }

        private void OnDisable()
        {
            if (DeviceWatcher.Instance != null)
                DeviceWatcher.Instance.DeviceChanged -= OnDeviceChanged;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            bool mouseActive = IsMouseDeltaActive();
            if (mouseActive) _lastMouseDeltaTime = Time.unscaledTime;
            TrySwitchMode();
        }

        private void OnDeviceChanged(InputDevice device, InputDeviceChange change)
        {
            if (change != InputDeviceChange.Added
             && change != InputDeviceChange.Removed
             && change != InputDeviceChange.Reconnected
             && change != InputDeviceChange.Disconnected)
                return;
            TrySwitchMode();
        }

        private bool IsMouseDeltaActive()
        {
            var m = Mouse.current;
            if (m == null || !m.added) return false;
            Vector2 d = m.delta.ReadValue();
            return d.sqrMagnitude > 0.0001f;
        }

        private void TrySwitchMode()
        {
            bool gamepadConnected = Gamepad.current != null && Gamepad.current.added;
            float idleFor = Time.unscaledTime - _lastMouseDeltaTime;
            bool mouseDeltaActive = Mouse.current != null
                                    && Mouse.current.added
                                    && idleFor < mouseIdleToGazeSeconds;

            InputMode next = InputModeRules.Resolve(gamepadConnected, mouseDeltaActive);
            if (next == _currentMode) return;

            _currentMode = next;
            ApplyMode(next);
        }

        private void ApplyMode(InputMode mode)
        {
            // Note: VRGazeReticle.useRTTRaycast and dwellClickEnabled are public fields
            // (file Presentation/Input/VRGazeReticle.cs:25/38). Toggling them disables
            // the gaze path entirely while non-Gaze modes are active.
            if (mode == InputMode.Gaze)
            {
                if (VRGazeReticle.Instance != null)
                {
                    VRGazeReticle.Instance.useRTTRaycast = true;
                    VRGazeReticle.Instance.dwellClickEnabled = true;
                    VRGazeReticle.Instance.SetReticleVisible(true);
                }
                WorldSpaceCursorRenderer.Instance?.SetVisible(false);

                VirtualCursorSpace.GetOrCreate().RequestModeSwitch(mode);
                // Hide cursor by setting state to inactive
                var vcs = VirtualCursorSpace.Instance;
                vcs.SetCursorUV(vcs.Cursor.UV); // fires move event but keeps state
            }
            else
            {
                if (VRGazeReticle.Instance != null)
                {
                    VRGazeReticle.Instance.useRTTRaycast = false;
                    VRGazeReticle.Instance.dwellClickEnabled = false;
                    VRGazeReticle.Instance.ForceResetDwellState();
                    VRGazeReticle.Instance.SetReticleVisible(false);
                }

                VirtualCursorSpace.GetOrCreate().RequestModeSwitch(mode);
                // NOTE: SnapCursorToCenter picks the highest-priority visible surface.
                // If an app (Media/Browser) is open with the same priority as Main Menu,
                // Main Menu wins (first registered). CursorAppFollower will re-snap to the
                // newly-opened app on OnAppOpened — but if InputModeController runs AFTER
                // the snap, the cursor ends up back on Main Menu. We solve this by
                // skipping the snap-to-center if the cursor is already bound to a surface.
                var vcs = VirtualCursorSpace.Instance;
                if (vcs.Cursor.SurfaceId == null)
                {
                    vcs.SnapCursorToCenter();
                }
                WorldSpaceCursorRenderer.Instance?.SetVisible(true);
            }
        }
    }
}
