using UnityEngine;
using UnityEngine.InputSystem;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Presentation.Input.Drivers
{
    /// <summary>
    /// Drives VCS cursor from a Bluetooth joystick / gamepad left-stick.
    ///
    /// Velocity-based movement (delta = stick × sensitivity × dt) so small sticks
    /// still move the cursor at a slow, controllable rate.
    ///
    /// Buttons supported (MVP):
    ///   - ButtonSouth OR ButtonWest (e.g. A / X) = click (VR-Park has single click/touch).
    ///   - ButtonEast (e.g. B) = back (close keyboard &gt; popup &gt; dropdown).
    ///
    /// Reserved (out-of-scope): shoulder buttons (could become volume up/down).
    /// </summary>
    [DefaultExecutionOrder(-6000)]
    public sealed class GamepadStickDriver : MonoBehaviour
    {
        public static GamepadStickDriver Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[GamepadStickDriver]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GamepadStickDriver>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || vcs.Mode != InputMode.Gamepad) return;

            var gp = Gamepad.current;
            if (gp == null || !gp.added) return;

            ApplyStickMovement(vcs, gp);
            ApplyButtons(vcs, gp);
        }

        private static void ApplyStickMovement(VirtualCursorSpace vcs, Gamepad gp)
        {
            Vector2 stick = gp.leftStick.ReadValue();
            float deadZone = vcs.Config.StickDeadZone;
            float magSq = stick.sqrMagnitude;
            if (magSq < deadZone * deadZone) return;

            // Radial dead-zone: ramp from 0 at edge of dead zone to 1 at full deflection.
            float mag = Mathf.Sqrt(magSq);
            float ramped = (mag - deadZone) / Mathf.Max(1e-4f, 1f - deadZone);
            Vector2 dir = stick / mag;
            Vector2 normalized = dir * Mathf.Clamp01(ramped);

            Vector2 uvDelta = normalized * vcs.Config.GamepadSensitivity * Time.unscaledDeltaTime;
            if (vcs.Config.InvertY) uvDelta.y = -uvDelta.y;

            vcs.MoveCursor(uvDelta);
        }

        private static void ApplyButtons(VirtualCursorSpace vcs, Gamepad gp)
        {
            // Click = buttonSouth (A) OR buttonWest (X). VR-Park-style single-button controllers
            // map primary to either; accepting both makes the driver controller-agnostic.
            if ((gp.buttonSouth != null && gp.buttonSouth.wasPressedThisFrame)
             || (gp.buttonWest  != null && gp.buttonWest.wasPressedThisFrame))
            {
                vcs.RaiseClick();
                return;
            }

            // Back = buttonEast (B). Priority cascade mirrors VRGazeReticle.ProcessDwellClickRTT.
            if (gp.buttonEast != null && gp.buttonEast.wasPressedThisFrame)
            {
                if (RTTMobileKeyboard.CurrentlyOpenKeyboard != null)
                {
                    RTTMobileKeyboard.CurrentlyOpenKeyboard.Hide();
                    return;
                }
                if (RTTPopupMenu.CurrentlyOpenPopup != null)
                {
                    RTTPopupMenu.CurrentlyOpenPopup.Hide();
                    return;
                }
                // Fallback: snap cursor to main menu
                vcs.SnapCursorToCenter();
            }
        }
    }
}
