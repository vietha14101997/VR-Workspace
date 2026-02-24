using UnityEngine;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Manages immersive mode (hide/show system UI).
    /// Extracted from RTTManager for Single Responsibility.
    /// </summary>
    public class RTTImmersiveModeController
    {
        private bool _wasTaskbarVisible;
        private bool _wasMenuVisible;
        private bool _isImmersiveMode;

        public bool IsImmersiveMode => _isImmersiveMode;

        /// <summary>
        /// Enter immersive mode: Hide global system UI (Taskbar, Main Menu).
        /// </summary>
        public void EnterImmersiveMode(RTTTaskbar taskbar, RTTMenuFrame mainMenuFrame)
        {
            if (_isImmersiveMode) return;
            _isImmersiveMode = true;

            _wasTaskbarVisible = taskbar != null && taskbar.gameObject.activeSelf;
            _wasMenuVisible = mainMenuFrame != null && mainMenuFrame.gameObject.activeSelf;

            if (taskbar != null) taskbar.gameObject.SetActive(false);
            if (mainMenuFrame != null) mainMenuFrame.gameObject.SetActive(false);

            Debug.Log("[RTTImmersiveModeController] Entered Immersive Mode");
        }

        /// <summary>
        /// Exit immersive mode: Restore global system UI state.
        /// </summary>
        public void ExitImmersiveMode(RTTTaskbar taskbar, RTTMenuFrame mainMenuFrame)
        {
            if (!_isImmersiveMode) return;
            _isImmersiveMode = false;

            if (taskbar != null && _wasTaskbarVisible) taskbar.gameObject.SetActive(true);
            if (mainMenuFrame != null && _wasMenuVisible) mainMenuFrame.gameObject.SetActive(true);

            Debug.Log("[RTTImmersiveModeController] Exited Immersive Mode");
        }
    }
}
