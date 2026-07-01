namespace VRWorkspace.Domain.Input
{
    /// <summary>
    /// Active input source for cursor control in VRWorkSpace.
    /// Transitions are driven by InputModeController (device-detection).
    /// </summary>
    public enum InputMode
    {
        /// <summary>Default. Reticle dwell-click on gaze target.</summary>
        Gaze = 0,

        /// <summary>BT/USB mouse delta drives a virtual 2D cursor.</summary>
        Mouse = 1,

        /// <summary>Bluetooth joystick / gamepad left-stick drives a virtual 2D cursor.</summary>
        Gamepad = 2
    }

    /// <summary>
    /// Static utilities for InputMode transitions.
    /// Priority: Gamepad > Mouse > Gaze (device-active-wins).
    /// </summary>
    public static class InputModeRules
    {
        /// <summary>
        /// Given current available devices, return the preferred input mode.
        /// Gamepad wins over Mouse (explicit user intent).
        /// </summary>
        public static InputMode Resolve(bool gamepadConnected, bool mouseDeltaActive)
        {
            if (gamepadConnected) return InputMode.Gamepad;
            if (mouseDeltaActive) return InputMode.Mouse;
            return InputMode.Gaze;
        }
    }
}
