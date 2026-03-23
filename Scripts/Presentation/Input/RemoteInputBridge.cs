using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VRWorkspace.Core;
using VRWorkspace.ViewModels;

namespace VRWorkspace.VRInput
{
    /// <summary>
    /// Captures Bluetooth mouse/keyboard input from Android and forwards to server
    /// via ConnectionViewModel → PhaseProtocolClient → "input" DataChannel.
    /// Uses Unity Input System package (1.7.0+).
    /// </summary>
    public class RemoteInputBridge : MonoBehaviour
    {
        [Header("Mouse Sensitivity")]
        [Tooltip("Multiplier applied to mouse delta before sending to server")]
        [Range(0.1f, 10f)]
        public float mouseSensitivity = 1.0f;

        [Header("Scroll Settings")]
        [Tooltip("Multiplier for scroll wheel delta (Windows WHEEL_DELTA=120 per notch)")]
        public float scrollMultiplier = 40f;

        [Header("Key Repeat")]
        [Tooltip("Delay before key repeat starts (seconds)")]
        public float keyRepeatDelay = 0.5f;
        [Tooltip("Interval between key repeats (seconds)")]
        public float keyRepeatInterval = 0.033f; // ~30 repeats/sec, matches Windows default

        private ConnectionViewModel _viewModel;
        private bool _isActive;

        // Key repeat state: track held keys with their timestamps
        private readonly Dictionary<Key, float> _heldKeyTimestamps = new Dictionary<Key, float>();
        private readonly Dictionary<Key, float> _heldKeyNextRepeat = new Dictionary<Key, float>();

        // Unity InputSystem Key → Windows Virtual Key mapping
        private static readonly Dictionary<Key, ushort> KeyToVK = new Dictionary<Key, ushort>
        {
            // Letters (VK_A=0x41 .. VK_Z=0x5A)
            { Key.A, 0x41 }, { Key.B, 0x42 }, { Key.C, 0x43 }, { Key.D, 0x44 },
            { Key.E, 0x45 }, { Key.F, 0x46 }, { Key.G, 0x47 }, { Key.H, 0x48 },
            { Key.I, 0x49 }, { Key.J, 0x4A }, { Key.K, 0x4B }, { Key.L, 0x4C },
            { Key.M, 0x4D }, { Key.N, 0x4E }, { Key.O, 0x4F }, { Key.P, 0x50 },
            { Key.Q, 0x51 }, { Key.R, 0x52 }, { Key.S, 0x53 }, { Key.T, 0x54 },
            { Key.U, 0x55 }, { Key.V, 0x56 }, { Key.W, 0x57 }, { Key.X, 0x58 },
            { Key.Y, 0x59 }, { Key.Z, 0x5A },
            // Numbers (VK_0=0x30 .. VK_9=0x39)
            { Key.Digit0, 0x30 }, { Key.Digit1, 0x31 }, { Key.Digit2, 0x32 },
            { Key.Digit3, 0x33 }, { Key.Digit4, 0x34 }, { Key.Digit5, 0x35 },
            { Key.Digit6, 0x36 }, { Key.Digit7, 0x37 }, { Key.Digit8, 0x38 },
            { Key.Digit9, 0x39 },
            // Function keys (VK_F1=0x70 .. VK_F12=0x7B)
            { Key.F1, 0x70 }, { Key.F2, 0x71 }, { Key.F3, 0x72 }, { Key.F4, 0x73 },
            { Key.F5, 0x74 }, { Key.F6, 0x75 }, { Key.F7, 0x76 }, { Key.F8, 0x77 },
            { Key.F9, 0x78 }, { Key.F10, 0x79 }, { Key.F11, 0x7A }, { Key.F12, 0x7B },
            // Modifiers
            { Key.LeftShift, 0xA0 }, { Key.RightShift, 0xA1 },
            { Key.LeftCtrl, 0xA2 }, { Key.RightCtrl, 0xA3 },
            { Key.LeftAlt, 0xA4 }, { Key.RightAlt, 0xA5 },
            { Key.LeftMeta, 0x5B }, { Key.RightMeta, 0x5C },
            // Navigation
            { Key.Enter, 0x0D }, { Key.NumpadEnter, 0x0D },
            { Key.Escape, 0x1B }, { Key.Tab, 0x09 }, { Key.Space, 0x20 },
            { Key.Backspace, 0x08 }, { Key.Delete, 0x2E }, { Key.Insert, 0x2D },
            { Key.Home, 0x24 }, { Key.End, 0x23 },
            { Key.PageUp, 0x21 }, { Key.PageDown, 0x22 },
            { Key.UpArrow, 0x26 }, { Key.DownArrow, 0x28 },
            { Key.LeftArrow, 0x25 }, { Key.RightArrow, 0x27 },
            // Punctuation (OEM keys)
            { Key.Comma, 0xBC }, { Key.Period, 0xBE },
            { Key.Slash, 0xBF }, { Key.Backslash, 0xDC },
            { Key.LeftBracket, 0xDB }, { Key.RightBracket, 0xDD },
            { Key.Semicolon, 0xBA }, { Key.Quote, 0xDE },
            { Key.Backquote, 0xC0 },
            { Key.Minus, 0xBD }, { Key.Equals, 0xBB },
            // Misc
            { Key.CapsLock, 0x14 }, { Key.NumLock, 0x90 },
            { Key.PrintScreen, 0x2C }, { Key.Pause, 0x13 },
        };

        public void SetActive(bool active)
        {
            _isActive = active;
            if (active)
            {
                // Lock cursor to capture all mouse input (hides Android system cursor)
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
                AppLog.Log("[RemoteInput] Activated — mouse captured, keyboard forwarding enabled");
            }
            else
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
                _heldKeyTimestamps.Clear();
                _heldKeyNextRepeat.Clear();
                AppLog.Log("[RemoteInput] Deactivated — mouse released");
            }
        }

        private void Awake()
        {
#pragma warning disable CS0618 // ServiceLocator is obsolete — VContainer migration pending
            _viewModel = ServiceLocator.Get<ConnectionViewModel>();
#pragma warning restore CS0618
        }

        private void Update()
        {
            if (!_isActive || _viewModel == null) return;

            try
            {
                ProcessMouse();
                ProcessKeyboard();
                ProcessGamepad();
            }
            catch (System.Exception ex)
            {
                // Input System can throw when BT devices disconnect mid-frame.
                // Catch here to prevent native crash propagation.
                Debug.LogWarning($"[RemoteInput] Input processing error (device disconnected?): {ex.Message}");
            }
        }

        private void ProcessMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Mouse movement (relative delta)
            Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
            if (Mathf.Abs(delta.x) > 0.01f || Mathf.Abs(delta.y) > 0.01f)
            {
                // Flip Y: Unity Y-up → Windows Y-down
                _viewModel.SendMouseMove((short)delta.x, (short)(-delta.y));
            }

            // Mouse buttons
            if (mouse.leftButton.wasPressedThisFrame)    _viewModel.SendMouseButton(0, true);
            if (mouse.leftButton.wasReleasedThisFrame)   _viewModel.SendMouseButton(0, false);
            if (mouse.rightButton.wasPressedThisFrame)   _viewModel.SendMouseButton(1, true);
            if (mouse.rightButton.wasReleasedThisFrame)  _viewModel.SendMouseButton(1, false);
            if (mouse.middleButton.wasPressedThisFrame)  _viewModel.SendMouseButton(2, true);
            if (mouse.middleButton.wasReleasedThisFrame) _viewModel.SendMouseButton(2, false);

            // Mouse scroll wheel
            Vector2 scroll = mouse.scroll.ReadValue();
            if (Mathf.Abs(scroll.y) > 0.1f)
                _viewModel.SendMouseWheel((short)(scroll.y / 120f * scrollMultiplier));
            if (Mathf.Abs(scroll.x) > 0.1f)
                _viewModel.SendMouseWheel(0, (short)(scroll.x / 120f * scrollMultiplier));
        }

        private void ProcessKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            float now = Time.unscaledTime;

            foreach (var key in kb.allKeys)
            {
                if (key.wasPressedThisFrame)
                {
                    if (KeyToVK.TryGetValue(key.keyCode, out ushort vk))
                    {
                        _viewModel.SendKey(vk, true);
                        // Start repeat timer
                        _heldKeyTimestamps[key.keyCode] = now;
                        _heldKeyNextRepeat[key.keyCode] = now + keyRepeatDelay;
                    }
                }
                else if (key.wasReleasedThisFrame)
                {
                    if (KeyToVK.TryGetValue(key.keyCode, out ushort vk))
                    {
                        _viewModel.SendKey(vk, false);
                        _heldKeyTimestamps.Remove(key.keyCode);
                        _heldKeyNextRepeat.Remove(key.keyCode);
                    }
                }
                else if (key.isPressed && _heldKeyNextRepeat.TryGetValue(key.keyCode, out float nextRepeat))
                {
                    // Key repeat: re-send keydown at interval (simulates OS key repeat)
                    if (now >= nextRepeat)
                    {
                        if (KeyToVK.TryGetValue(key.keyCode, out ushort vk))
                            _viewModel.SendKey(vk, true);
                        _heldKeyNextRepeat[key.keyCode] = now + keyRepeatInterval;
                    }
                }
            }
        }

        private void ProcessGamepad()
        {
            var gp = Gamepad.current;
            if (gp == null) return;

            // Map Unity Gamepad buttons to XINPUT_GAMEPAD flags
            ushort buttons = 0;
            if (gp.dpad.up.isPressed)            buttons |= 0x0001;
            if (gp.dpad.down.isPressed)          buttons |= 0x0002;
            if (gp.dpad.left.isPressed)          buttons |= 0x0004;
            if (gp.dpad.right.isPressed)         buttons |= 0x0008;
            if (gp.startButton.isPressed)        buttons |= 0x0010;
            if (gp.selectButton.isPressed)       buttons |= 0x0020;
            if (gp.leftStickButton.isPressed)    buttons |= 0x0040;
            if (gp.rightStickButton.isPressed)   buttons |= 0x0080;
            if (gp.leftShoulder.isPressed)       buttons |= 0x0100;
            if (gp.rightShoulder.isPressed)      buttons |= 0x0200;
            if (gp.buttonSouth.isPressed)        buttons |= 0x1000; // A
            if (gp.buttonEast.isPressed)         buttons |= 0x2000; // B
            if (gp.buttonWest.isPressed)         buttons |= 0x4000; // X
            if (gp.buttonNorth.isPressed)        buttons |= 0x8000; // Y

            byte lt = (byte)(gp.leftTrigger.ReadValue() * 255f);
            byte rt = (byte)(gp.rightTrigger.ReadValue() * 255f);
            short lx = (short)(gp.leftStick.ReadValue().x * 32767f);
            short ly = (short)(gp.leftStick.ReadValue().y * 32767f);
            short rx = (short)(gp.rightStick.ReadValue().x * 32767f);
            short ry = (short)(gp.rightStick.ReadValue().y * 32767f);

            // Only send if any input is active (avoid flooding with idle state)
            if (buttons != 0 || lt > 10 || rt > 10 ||
                Mathf.Abs(lx) > 3000 || Mathf.Abs(ly) > 3000 ||
                Mathf.Abs(rx) > 3000 || Mathf.Abs(ry) > 3000)
            {
                _viewModel.SendGamepadState(buttons, lt, rt, lx, ly, rx, ry);
            }
            else if (_lastGamepadActive)
            {
                // Send one final neutral state when gamepad goes idle
                _viewModel.SendGamepadState(0, 0, 0, 0, 0, 0, 0);
                _lastGamepadActive = false;
                return;
            }
            _lastGamepadActive = buttons != 0 || lt > 10 || rt > 10 ||
                Mathf.Abs(lx) > 3000 || Mathf.Abs(ly) > 3000 ||
                Mathf.Abs(rx) > 3000 || Mathf.Abs(ry) > 3000;
        }

        private bool _lastGamepadActive;

        private void OnDisable()
        {
            if (_isActive) SetActive(false);
        }
    }
}
