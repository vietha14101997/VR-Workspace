using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VRWorkspace.Presentation.Input
{
    /// <summary>
    /// Hub for <c>InputSystem.onDeviceChange</c> events. The Input system doesn't
    /// expose convenient static Add/Remove APIs for devices in user code, so we route
    /// them through this singleton and re-broadcast as a managed C# event.
    ///
    /// Auto-bootstraps BeforeSceneLoad so it captures the very first device-change
    /// event of a Play session (including reconnecting a Bluetooth mouse mid-game).
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    public sealed class DeviceWatcher : MonoBehaviour
    {
        public static DeviceWatcher Instance { get; private set; }

        /// <summary>
        /// Fired whenever an input device is added, removed, or reconnected.
        /// Consumers should query connected devices themselves with
        /// <c>Mouse.current</c>, <c>Gamepad.current</c>, <c>Keyboard.current</c>.
        /// </summary>
        public event Action<InputDevice, InputDeviceChange> DeviceChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[DeviceWatcher]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DeviceWatcher>();
        }

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            DeviceChanged?.Invoke(device, change);
        }
    }
}
