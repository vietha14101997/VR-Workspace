using UnityEngine;
using System.Collections.Generic;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Manages RTT panel registration, tracking, and camera depth assignment.
    /// Extracted from RTTManager to separate concerns.
    /// </summary>
    public class RTTPanelManager
    {
        #region Fields

        private readonly List<RTTCanvasBase> _registeredPanels = new List<RTTCanvasBase>();
        private int _currentCameraDepth;
        private readonly int _startingCameraDepth;
        private readonly bool _enableLogging;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new panel manager.
        /// </summary>
        /// <param name="startingCameraDepth">Starting camera depth for RTT cameras (default -100)</param>
        /// <param name="enableLogging">Enable performance logging</param>
        public RTTPanelManager(int startingCameraDepth = -100, bool enableLogging = false)
        {
            _startingCameraDepth = startingCameraDepth;
            _currentCameraDepth = startingCameraDepth;
            _enableLogging = enableLogging;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Number of registered panels
        /// </summary>
        public int RegisteredPanelCount => _registeredPanels.Count;

        /// <summary>
        /// Number of currently visible panels
        /// </summary>
        public int VisiblePanelCount
        {
            get
            {
                int count = 0;
                foreach (var panel in _registeredPanels)
                {
                    if (panel != null && panel.IsVisible)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Read-only list of registered panels
        /// </summary>
        public IReadOnlyList<RTTCanvasBase> RegisteredPanels => _registeredPanels.AsReadOnly();

        #endregion

        #region Panel Registration

        /// <summary>
        /// Register a panel for management.
        /// </summary>
        public void RegisterPanel(RTTCanvasBase panel)
        {
            if (panel == null) return;

            if (!_registeredPanels.Contains(panel))
            {
                _registeredPanels.Add(panel);
                panel.OnRTTDestroyed += () => UnregisterPanel(panel);

                if (_enableLogging)
                    Debug.Log($"[RTTPanelManager] Registered: {panel.GetType().Name} (Total: {_registeredPanels.Count})");
            }
        }

        /// <summary>
        /// Unregister a panel.
        /// </summary>
        public void UnregisterPanel(RTTCanvasBase panel)
        {
            if (panel == null) return;

            if (_registeredPanels.Remove(panel))
            {
                if (_enableLogging)
                    Debug.Log($"[RTTPanelManager] Unregistered: {panel.GetType().Name} (Total: {_registeredPanels.Count})");
            }
        }

        /// <summary>
        /// Assign a unique camera depth for a new RTT camera.
        /// Each call returns a lower depth value to ensure proper render ordering.
        /// </summary>
        public int AssignCameraDepth()
        {
            return _currentCameraDepth--;
        }

        /// <summary>
        /// Reset camera depth to starting value.
        /// </summary>
        public void ResetCameraDepth()
        {
            _currentCameraDepth = _startingCameraDepth;
        }

        #endregion

        #region Panel Lookup

        /// <summary>
        /// Find a panel of specific type.
        /// </summary>
        public T FindPanel<T>() where T : RTTCanvasBase
        {
            foreach (var panel in _registeredPanels)
            {
                if (panel is T typedPanel)
                    return typedPanel;
            }
            return null;
        }

        /// <summary>
        /// Find all panels of specific type.
        /// </summary>
        public List<T> FindAllPanels<T>() where T : RTTCanvasBase
        {
            var result = new List<T>();
            foreach (var panel in _registeredPanels)
            {
                if (panel is T typedPanel)
                    result.Add(typedPanel);
            }
            return result;
        }

        #endregion

        #region Panel Operations

        /// <summary>
        /// Mark all panels as dirty (requiring re-render).
        /// </summary>
        public void MarkAllDirty()
        {
            foreach (var panel in _registeredPanels)
                panel?.MarkDirty();
        }

        /// <summary>
        /// Hide all panels.
        /// </summary>
        public void HideAll()
        {
            foreach (var panel in _registeredPanels)
                panel?.Hide();
        }

        /// <summary>
        /// Show all panels.
        /// </summary>
        public void ShowAll()
        {
            foreach (var panel in _registeredPanels)
                panel?.Show();
        }

        /// <summary>
        /// Remove null/destroyed panel references.
        /// </summary>
        public void CleanupDestroyedPanels()
        {
            _registeredPanels.RemoveAll(p => p == null);
        }

        #endregion

        #region Memory Calculation

        /// <summary>
        /// Calculate total texture memory used by all registered panels.
        /// </summary>
        /// <returns>Total memory in bytes</returns>
        public long CalculateTotalTextureMemory()
        {
            long total = 0;
            foreach (var panel in _registeredPanels)
            {
                if (panel == null) continue;
                var rt = panel.GetRenderTexture();
                if (rt != null)
                    total += (long)rt.width * rt.height * 4 * rt.antiAliasing;
            }
            return total;
        }

        /// <summary>
        /// Calculate total texture memory in megabytes.
        /// </summary>
        public float CalculateTotalTextureMemoryMB()
        {
            return CalculateTotalTextureMemory() / (1024f * 1024f);
        }

        #endregion
    }
}
