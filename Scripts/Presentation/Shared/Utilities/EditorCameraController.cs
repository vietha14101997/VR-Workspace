using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.Utilities
{
    /// <summary>
    /// Editor-only camera controller that allows mouse look rotation and VirtualObjects zoom.
    /// Uses New Input System.
    /// - Move mouse to rotate camera
    /// - Mouse cursor is hidden while active
    /// - Press Alt or Escape to toggle mouse look mode
    /// - Hold Shift + Scroll to zoom VirtualObjects in/out
    /// </summary>
    public class EditorCameraController : MonoBehaviour
    {
    #if UNITY_EDITOR
        [Header("Mouse Look Settings")]
        [SerializeField] private float mouseSensitivity = 0.15f;
        [SerializeField] private float verticalClampAngle = 80f;

        [Header("State")]
        [SerializeField] private bool isMouseLookActive = true;

        private float rotationX = 0f;
        private float rotationY = 0f;

        // Input references
        private Mouse mouse;
        private Keyboard keyboard;

        private void Start()
        {
            // Get input devices
            mouse = Mouse.current;
            keyboard = Keyboard.current;

            if (mouse == null)
            {
                enabled = false;
                return;
            }

            if (keyboard == null)
            {
                enabled = false;
                return;
            }

            // Initialize rotation from current camera rotation
            Vector3 currentRotation = transform.eulerAngles;
            rotationX = currentRotation.y;
            rotationY = currentRotation.x;

            // Normalize rotationY to be within -180 to 180 range
            if (rotationY > 180f)
                rotationY -= 360f;

            // Start with mouse look active
            SetMouseLookActive(true);
        }

        private void Update()
        {
            if (keyboard == null || mouse == null) return;

            // Toggle mouse look with Alt or Escape key
            bool togglePressed = keyboard.leftAltKey.wasPressedThisFrame || 
                                keyboard.rightAltKey.wasPressedThisFrame ||
                                keyboard.escapeKey.wasPressedThisFrame;

            if (togglePressed)
            {
                SetMouseLookActive(!isMouseLookActive);
            }

            // Handle mouse look rotation
            if (isMouseLookActive)
            {
                HandleMouseLook();
            }

            // Handle VirtualObjects zoom: Shift + Mouse Scroll
            HandleZoom();
        }

        private void HandleZoom()
        {
            // Check if Shift is held
            bool shiftHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            if (!shiftHeld) return;

            // Get mouse scroll delta using New Input System
            float scrollDelta = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scrollDelta, 0f)) return;

            // Get ZoomController
            var zoomController = VirtualObjectsZoomController.Instance;
            if (zoomController == null) return;

            // Scroll up = zoom in (closer), Scroll down = zoom out (farther)
            if (scrollDelta > 0)
            {
                zoomController.ZoomIn();
            }
            else
            {
                zoomController.ZoomOut();
            }
        }

        private void HandleMouseLook()
        {
            // Get mouse delta using New Input System
            Vector2 mouseDelta = mouse.delta.ReadValue();

            float mouseX = mouseDelta.x * mouseSensitivity;
            float mouseY = mouseDelta.y * mouseSensitivity;

            // Calculate rotation
            rotationX += mouseX;
            rotationY -= mouseY;

            // Clamp vertical rotation
            rotationY = Mathf.Clamp(rotationY, -verticalClampAngle, verticalClampAngle);

            // Apply rotation to camera
            transform.rotation = Quaternion.Euler(rotationY, rotationX, 0f);
        }

        private void SetMouseLookActive(bool active)
        {
            isMouseLookActive = active;

            if (active)
            {
                // Re-enable mouse device so we can read delta for camera rotation
                if (mouse != null && !mouse.enabled)
                    InputSystem.EnableDevice(mouse);

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                // Disable mouse device so InputSystemUIInputModule ignores
                // mouse hover/click on UI elements while cursor is free
                if (mouse != null && mouse.enabled)
                    InputSystem.DisableDevice(mouse);
            }
        }

        private void OnDisable()
        {
            // Ensure cursor is visible when script is disabled
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Re-apply cursor state when application regains focus
            if (hasFocus && isMouseLookActive)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // Reset cursor when play mode stops
        private void OnApplicationQuit()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    #endif
    }
}
