using UnityEngine.EventSystems;

namespace VRWorkspace.VRInput
{
    /// <summary>
    /// No-op BaseInputModule that intentionally ignores ALL pointer input
    /// (mouse, touch, gamepad, XR controller).
    ///
    /// When this module is installed on an EventSystem, no UI events are fired
    /// from automatic input device polling. UI interaction must go through
    /// explicit <see cref="ExecuteEvents.Execute(UnityEngine.GameObject, UnityEngine.EventSystems.PointerEventData, UnityEngine.EventSystems.ExecuteEvents.EventFunction{T})"/>
    /// calls (e.g. RTTRaycastManager.SendClick fired by Reticle Dwell-Click
    /// or a future custom cursor).
    ///
    /// Raw InputSystem devices (Mouse / Touchscreen / Keyboard / Gamepad)
    /// remain enabled — components like <see cref="RemoteInputBridge"/> can
    /// still read Mouse.current.delta and Keyboard.current.allKeys to forward
    /// Bluetooth mouse/keyboard input to the remote desktop.
    /// </summary>
    /// <remarks>
    /// KISS rationale: replacing the EventSystem's input module is the
    /// surgical fix — it leaves the rest of the input system untouched and
    /// doesn't require disabling input devices globally (which would break
    /// remote-desktop input forwarding).
    /// </remarks>
    public class NullInputModule : BaseInputModule
    {
        /// <summary>
        /// Always invoked once per frame by EventSystem. Intentionally empty
        /// — we don't process pointer state, hover, or click events here.
        /// </summary>
        public override void Process() { }
    }
}