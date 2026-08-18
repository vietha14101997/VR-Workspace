using UnityEngine;

namespace VRWorkspace.Domain.Input
{
    /// <summary>
    /// Runtime configuration for VCS cursor movement + input drivers.
    /// All defaults match Decision 3 (sticky edge 5%) and field-tested values.
    /// </summary>
    [System.Serializable]
    public struct InputConfig
    {
        /// <summary>Multiplier on <c>Mouse.current.delta</c> m/s to surface UV space (≈ 1/panel-width).</summary>
        public float MouseSensitivity;

        /// <summary>Multiplier on gamepad left-stick to UV space (Time.deltaTime-aware).</summary>
        public float GamepadSensitivity;

        /// <summary>Stick dead-zone radius in normalized [0,1].</summary>
        public float StickDeadZone;

        /// <summary>Invert Y axis for mouse delta (true = screen-style "up is positive").</summary>
        public bool InvertY;

        /// <summary>
        /// Buffer zone (0..0.1 = 0..10% of surface dimension) near an edge that
        /// delays traversal to avoid jitter (Decision 3).
        /// </summary>
        [Range(0f, 0.1f)] public float StickyEdgeBuffer;

        /// <summary>Dwell click seconds. Used only by Gaze mode (kept here for parity).</summary>
        public float DwellClickSeconds;

        public static InputConfig Default => new InputConfig
        {
            MouseSensitivity   = 0.0008f,
            GamepadSensitivity = 1.2f,
            StickDeadZone      = 0.15f,
            InvertY            = false,
            StickyEdgeBuffer   = 0.05f,
            DwellClickSeconds  = 1.0f
        };
    }
}
