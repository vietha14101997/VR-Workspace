using UnityEngine;
using System;
using System.Collections.Generic;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Helper class for RTT app lifecycle management.
    /// Handles app tracking, slot management, and state queries.
    ///
    /// Note: Coroutine-based operations (transitions, frame creation) remain in RTTManager
    /// as they require MonoBehaviour. This class handles the data/state management aspects.
    /// </summary>
    public class RTTAppLifecycleHelper
    {
        #region Fields

        private readonly Dictionary<string, RTTAppInstance> _activeApps = new Dictionary<string, RTTAppInstance>();
        private readonly Dictionary<string, RTTAppInstance> _preparingApps = new Dictionary<string, RTTAppInstance>();
        private string _currentVisibleAppId;
        private string _pendingOpenAppId;
        private bool _isTransitioning;
        private readonly int _maxOpenApps;
        private readonly int _maxOverflowSlots;

        #endregion

        #region Events

        /// <summary>
        /// Fired when an app is opened
        /// </summary>
        public event Action<string, RTTAppInstance> OnAppOpened;

        /// <summary>
        /// Fired when an app is closed
        /// </summary>
        public event Action<string> OnAppClosed;

        /// <summary>
        /// Fired when visible app changes
        /// </summary>
        public event Action<string> OnVisibleAppChanged;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new app lifecycle helper.
        /// </summary>
        /// <param name="maxOpenApps">Maximum number of main app slots</param>
        /// <param name="maxOverflowSlots">Maximum number of overflow app slots</param>
        public RTTAppLifecycleHelper(int maxOpenApps = 3, int maxOverflowSlots = 5)
        {
            _maxOpenApps = maxOpenApps;
            _maxOverflowSlots = maxOverflowSlots;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Currently visible app ID (null if main menu is visible)
        /// </summary>
        public string CurrentVisibleAppId
        {
            get => _currentVisibleAppId;
            set
            {
                if (_currentVisibleAppId != value)
                {
                    _currentVisibleAppId = value;
                    OnVisibleAppChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// Pending app ID to open (for prepare → open flow)
        /// </summary>
        public string PendingOpenAppId
        {
            get => _pendingOpenAppId;
            set => _pendingOpenAppId = value;
        }

        /// <summary>
        /// Whether a transition is in progress
        /// </summary>
        public bool IsTransitioning
        {
            get => _isTransitioning;
            set => _isTransitioning = value;
        }

        /// <summary>
        /// Whether main menu is visible (no app is visible)
        /// </summary>
        public bool IsMainMenuVisible => _currentVisibleAppId == null;

        /// <summary>
        /// Currently visible app instance (null if main menu)
        /// </summary>
        public RTTAppInstance CurrentApp =>
            _currentVisibleAppId != null && _activeApps.ContainsKey(_currentVisibleAppId)
                ? _activeApps[_currentVisibleAppId]
                : null;

        /// <summary>
        /// Read-only collection of open app IDs
        /// </summary>
        public IReadOnlyCollection<string> OpenAppIds => _activeApps.Keys;

        /// <summary>
        /// Number of currently open apps
        /// </summary>
        public int OpenAppCount => _activeApps.Count;

        #endregion

        #region App Queries

        /// <summary>
        /// Check if an app is currently open.
        /// </summary>
        public bool IsAppOpen(string appId)
        {
            return _activeApps.ContainsKey(appId);
        }

        /// <summary>
        /// Check if an app is being prepared.
        /// </summary>
        public bool IsAppPreparing(string appId)
        {
            return _preparingApps.ContainsKey(appId);
        }

        /// <summary>
        /// Check if an app is fully prepared and ready to show.
        /// </summary>
        public bool IsAppPrepared(string appId)
        {
            if (_activeApps.ContainsKey(appId)) return true;
            if (!_preparingApps.ContainsKey(appId)) return false;
            return _preparingApps[appId].IsPrepared;
        }

        /// <summary>
        /// Get an app instance by ID.
        /// </summary>
        public RTTAppInstance GetApp(string appId)
        {
            return _activeApps.TryGetValue(appId, out var app) ? app : null;
        }

        /// <summary>
        /// Get a preparing app instance by ID.
        /// </summary>
        public RTTAppInstance GetPreparingApp(string appId)
        {
            return _preparingApps.TryGetValue(appId, out var app) ? app : null;
        }

        #endregion

        #region Slot Management

        /// <summary>
        /// Get the next available taskbar slot index.
        /// Returns -1 if no slots available.
        /// </summary>
        public int GetNextAvailableSlot()
        {
            int totalMaxSlots = _maxOpenApps + _maxOverflowSlots;

            for (int i = 1; i <= totalMaxSlots; i++)
            {
                bool slotUsed = false;

                // Check active apps
                foreach (var app in _activeApps.Values)
                {
                    if (app.TaskbarSlotIndex == i)
                    {
                        slotUsed = true;
                        break;
                    }
                }

                // Check preparing apps
                if (!slotUsed)
                {
                    foreach (var app in _preparingApps.Values)
                    {
                        if (app.TaskbarSlotIndex == i)
                        {
                            slotUsed = true;
                            break;
                        }
                    }
                }

                if (!slotUsed) return i;
            }

            return -1;
        }

        #endregion

        #region App Registration

        /// <summary>
        /// Register an app as active.
        /// </summary>
        public void RegisterActiveApp(string appId, RTTAppInstance instance)
        {
            _activeApps[appId] = instance;
            OnAppOpened?.Invoke(appId, instance);
        }

        /// <summary>
        /// Register an app as preparing.
        /// </summary>
        public void RegisterPreparingApp(string appId, RTTAppInstance instance)
        {
            _preparingApps[appId] = instance;
        }

        /// <summary>
        /// Move a preparing app to active apps.
        /// </summary>
        public bool PromotePreparingApp(string appId)
        {
            if (_preparingApps.TryGetValue(appId, out var instance))
            {
                _preparingApps.Remove(appId);
                _activeApps[appId] = instance;
                OnAppOpened?.Invoke(appId, instance);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Remove an app from active apps.
        /// </summary>
        public RTTAppInstance RemoveActiveApp(string appId)
        {
            if (_activeApps.TryGetValue(appId, out var instance))
            {
                _activeApps.Remove(appId);
                OnAppClosed?.Invoke(appId);
                return instance;
            }
            return null;
        }

        /// <summary>
        /// Cancel all preparing apps.
        /// Returns list of cancelled instances for cleanup.
        /// </summary>
        public List<RTTAppInstance> CancelAllPreparations()
        {
            var cancelled = new List<RTTAppInstance>(_preparingApps.Values);
            _preparingApps.Clear();
            return cancelled;
        }

        /// <summary>
        /// Cancel a specific preparing app.
        /// </summary>
        public RTTAppInstance CancelPreparation(string appId)
        {
            if (_preparingApps.TryGetValue(appId, out var instance))
            {
                _preparingApps.Remove(appId);
                return instance;
            }
            return null;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Clear all app tracking data.
        /// Does not destroy GameObjects - call this after cleanup.
        /// </summary>
        public void Clear()
        {
            _activeApps.Clear();
            _preparingApps.Clear();
            _currentVisibleAppId = null;
            _pendingOpenAppId = null;
            _isTransitioning = false;
        }

        #endregion
    }
}
