using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Manages RTT app lifecycle - opening, closing, switching, and transitions.
    /// Extracted from RTTManager for cleaner separation of concerns.
    /// </summary>
    public class RTTAppManager : MonoBehaviour
    {
        #region Fields

        // App state tracking
        private Dictionary<string, RTTAppInstance> _activeApps = new Dictionary<string, RTTAppInstance>();
        private Dictionary<string, RTTAppInstance> _preparingApps = new Dictionary<string, RTTAppInstance>();
        private string _currentVisibleAppId = null;
        private string _pendingOpenAppId = null;
        private bool _isTransitioning = false;

        // References (set by RTTManager)
        private RTTMenu _menu;
        private RTTMenuFrame _mainMenuFrame;
        private RTTTaskbar _taskbar;
        private RTTAppRegistry _appRegistry;
        private Transform _frameParent;
        private GameObject _mainMenuContent;

        // Transition settings
        private float _transitionOutDuration = 0.1f;
        private float _transitionInDuration = 0.15f;
        private bool _useFadeTransition = true;
        private bool _useScaleTransition = false;

        // Callbacks
        private Action<RTTAppInstance> _createAppContentCallback;
        private Action<RTTManager.MenuState> _onMenuStateChanged;

        #endregion

        #region Events

        public event Action<string, RTTAppInstance> OnAppOpened;
        public event Action<string> OnAppClosed;
        public event Action<string> OnVisibleAppChanged;

        #endregion

        #region Properties

        public bool IsMainMenuVisible => _currentVisibleAppId == null;
        public string CurrentVisibleAppId => _currentVisibleAppId;
        public bool IsTransitioning => _isTransitioning;
        public int OpenAppCount => _activeApps.Count;

        public RTTAppInstance CurrentApp =>
            _currentVisibleAppId != null && _activeApps.ContainsKey(_currentVisibleAppId)
                ? _activeApps[_currentVisibleAppId]
                : null;

        public IReadOnlyCollection<string> OpenAppIds => _activeApps.Keys;

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize the app manager with required references.
        /// </summary>
        public void Initialize(
            RTTMenu menu,
            RTTMenuFrame mainMenuFrame,
            RTTTaskbar taskbar,
            RTTAppRegistry appRegistry,
            Transform frameParent,
            float transitionOutDuration,
            float transitionInDuration,
            bool useFadeTransition,
            bool useScaleTransition)
        {
            _menu = menu;
            _mainMenuFrame = mainMenuFrame;
            _taskbar = taskbar;
            _appRegistry = appRegistry;
            _frameParent = frameParent;
            _transitionOutDuration = transitionOutDuration;
            _transitionInDuration = transitionInDuration;
            _useFadeTransition = useFadeTransition;
            _useScaleTransition = useScaleTransition;
        }

        /// <summary>
        /// Set the main menu content reference (for showing/hiding).
        /// </summary>
        public void SetMainMenuContent(GameObject content)
        {
            _mainMenuContent = content;
        }

        /// <summary>
        /// Set callback for creating app-specific content.
        /// </summary>
        public void SetCreateContentCallback(Action<RTTAppInstance> callback)
        {
            _createAppContentCallback = callback;
        }

        /// <summary>
        /// Set callback for menu state changes.
        /// </summary>
        public void SetMenuStateCallback(Action<RTTManager.MenuState> callback)
        {
            _onMenuStateChanged = callback;
        }

        #endregion

        #region Public API

        public RTTAppInstance OpenApp(string appId)
        {
            if (_activeApps.ContainsKey(appId))
            {
                SwitchToApp(appId);
                return _activeApps[appId];
            }

            if (_preparingApps.ContainsKey(appId))
            {
                StartCoroutine(WaitAndSwitchToPreparedApp(appId));
                return _preparingApps[appId];
            }

            int slotIndex = GetNextAvailableSlot();
            if (slotIndex == -1)
            {
                Debug.LogWarning("[RTTAppManager] No available app slots");
                return null;
            }

            Sprite icon = GetAppIcon(appId);
            var instance = new RTTAppInstance(appId)
            {
                TaskbarSlotIndex = slotIndex,
                Icon = icon
            };

            _activeApps[appId] = instance;
            StartCoroutine(CreateAppFrameAndContent(instance));

            Debug.Log($"[RTTAppManager] Opening app: {appId} in slot {slotIndex}");
            return instance;
        }

        public void PrepareApp(string appId)
        {
            _pendingOpenAppId = appId;

            if (_activeApps.ContainsKey(appId) || _preparingApps.ContainsKey(appId))
                return;

            CancelAllPreparations();

            int slotIndex = GetNextAvailableSlot();
            if (slotIndex == -1)
            {
                Debug.LogWarning("[RTTAppManager] No available app slots for prepare");
                return;
            }

            Sprite icon = GetAppIcon(appId);
            var instance = new RTTAppInstance(appId)
            {
                TaskbarSlotIndex = slotIndex,
                Icon = icon
            };

            _preparingApps[appId] = instance;
            StartCoroutine(PrepareAppFrameAsync(instance));

            Debug.Log($"[RTTAppManager] Preparing app: {appId} in slot {slotIndex}");
        }

        public bool IsAppPrepared(string appId)
        {
            if (_activeApps.ContainsKey(appId)) return true;
            if (!_preparingApps.ContainsKey(appId)) return false;
            return _preparingApps[appId].IsPrepared;
        }

        public void OpenPreparedApp(string appId)
        {
            if (_pendingOpenAppId != appId)
            {
                Debug.Log($"[RTTAppManager] Skipping open for {appId} - user clicked {_pendingOpenAppId} instead");
                return;
            }

            _pendingOpenAppId = null;

            if (_activeApps.ContainsKey(appId))
            {
                SwitchToApp(appId);
                return;
            }

            if (_preparingApps.ContainsKey(appId))
            {
                StartCoroutine(WaitAndSwitchToPreparedApp(appId));
                return;
            }

            OpenApp(appId);
        }

        public void SwitchToApp(string appId)
        {
            if (!_activeApps.ContainsKey(appId))
            {
                Debug.LogWarning($"[RTTAppManager] App not open: {appId}");
                return;
            }

            if (_isTransitioning)
            {
                SwitchToAppImmediate(appId);
                return;
            }

            StartCoroutine(SwitchToAppWithTransition(appId));
        }

        public void SwitchToHome()
        {
            if (_currentVisibleAppId == null)
            {
                _taskbar?.SelectSlot(0);
                return;
            }

            if (_isTransitioning)
            {
                SwitchToHomeImmediate();
                return;
            }

            StartCoroutine(SwitchToHomeWithTransition());
        }

        public void CloseApp(string appId)
        {
            if (!_activeApps.ContainsKey(appId)) return;

            if (_currentVisibleAppId == appId && !_isTransitioning)
                StartCoroutine(CloseAppWithTransition(appId));
            else
                CloseAppInternal(appId, skipSwitchToHome: false);
        }

        public bool IsAppOpen(string appId) => _activeApps.ContainsKey(appId);

        public RTTAppInstance GetApp(string appId) =>
            _activeApps.TryGetValue(appId, out var app) ? app : null;

        public IReadOnlyCollection<string> GetOpenAppIds() => _activeApps.Keys;

        #endregion

        #region Internal Methods

        private void CancelAllPreparations()
        {
            foreach (var kvp in new Dictionary<string, RTTAppInstance>(_preparingApps))
            {
                if (kvp.Value.Controller != null && kvp.Value.Controller.gameObject != null)
                    Destroy(kvp.Value.Controller.gameObject);
                if (kvp.Value.Frame != null)
                {
                    if (_menu != null)
                        _menu.DestroyFrame(kvp.Value.Frame);
                    else
                        Destroy(kvp.Value.Frame.gameObject);
                }
                Debug.Log($"[RTTAppManager] Cancelled preparation for: {kvp.Key}");
            }
            _preparingApps.Clear();
        }

        private void HideCurrentView()
        {
            if (_currentVisibleAppId == null)
            {
                if (_mainMenuFrame != null)
                    _mainMenuFrame.gameObject.SetActive(false);
            }
            else if (_activeApps.ContainsKey(_currentVisibleAppId))
            {
                var currentApp = _activeApps[_currentVisibleAppId];
                if (currentApp.Frame != null)
                    currentApp.Frame.gameObject.SetActive(false);
                currentApp.IsVisible = false;
            }
        }

        private void CloseAppInternal(string appId, bool skipSwitchToHome)
        {
            if (!_activeApps.ContainsKey(appId)) return;

            var app = _activeApps[appId];

            if (!skipSwitchToHome && _currentVisibleAppId == appId)
            {
                if (app.Frame != null)
                    app.Frame.gameObject.SetActive(false);
                app.IsVisible = false;

                if (_mainMenuFrame != null)
                {
                    _mainMenuFrame.gameObject.SetActive(true);
                    _mainMenuFrame.SetAsPrimaryFrame();
                }

                if (_mainMenuContent != null)
                    _mainMenuContent.SetActive(true);

                _currentVisibleAppId = null;
                _onMenuStateChanged?.Invoke(RTTManager.MenuState.MainMenu);
                _taskbar?.SelectSlot(0);
            }

            // Cleanup controller
            if (app.Controller != null)
            {
                var cleanupMethod = app.Controller.GetType().GetMethod("Cleanup");
                if (cleanupMethod != null)
                {
                    try { cleanupMethod.Invoke(app.Controller, null); }
                    catch (Exception e) { Debug.LogWarning($"[RTTAppManager] Cleanup failed: {e.Message}"); }
                }
                if (app.Controller.gameObject != null)
                    Destroy(app.Controller.gameObject);
            }

            if (app.Frame != null)
            {
                if (_menu != null)
                    _menu.DestroyFrame(app.Frame);
                else
                    Destroy(app.Frame.gameObject);
            }

            if (_taskbar != null && app.TaskbarSlotIndex > 0)
                _taskbar.UnregisterApp(app.TaskbarSlotIndex);

            _activeApps.Remove(appId);
            OnAppClosed?.Invoke(appId);
            Debug.Log($"[RTTAppManager] Closed app: {appId}");
        }

        private int GetNextAvailableSlot()
        {
            int mainSlots = _appRegistry?.maxOpenApps ?? 3;
            int maxOverflowSlots = 5;
            int totalMaxSlots = mainSlots + maxOverflowSlots;

            for (int i = 1; i <= totalMaxSlots; i++)
            {
                bool slotUsed = false;
                foreach (var app in _activeApps.Values)
                {
                    if (app.TaskbarSlotIndex == i) { slotUsed = true; break; }
                }
                if (!slotUsed)
                {
                    foreach (var app in _preparingApps.Values)
                    {
                        if (app.TaskbarSlotIndex == i) { slotUsed = true; break; }
                    }
                }
                if (!slotUsed) return i;
            }
            return -1;
        }

        private Sprite GetAppIcon(string appId)
        {
            if (_appRegistry != null)
            {
                var icon = _appRegistry.GetIcon(appId);
                if (icon != null) return icon;
            }
            return Resources.Load<Sprite>($"icon_{appId}");
        }

        private void SwitchToAppImmediate(string appId)
        {
            if (!_activeApps.ContainsKey(appId)) return;

            HideCurrentView();

            var newApp = _activeApps[appId];
            if (newApp.Frame != null)
            {
                newApp.Frame.gameObject.SetActive(true);
                newApp.Frame.SetAsPrimaryFrame();
                newApp.IsVisible = true;
            }

            _currentVisibleAppId = appId;
            OnVisibleAppChanged?.Invoke(appId);
            _onMenuStateChanged?.Invoke(RTTManager.MenuState.RemoteMenu);
            _taskbar?.SelectSlot(newApp.TaskbarSlotIndex);
        }

        private void SwitchToHomeImmediate()
        {
            HideCurrentView();

            if (_mainMenuFrame != null)
            {
                _mainMenuFrame.gameObject.SetActive(true);
                _mainMenuFrame.SetAsPrimaryFrame();
            }

            if (_mainMenuContent != null)
                _mainMenuContent.SetActive(true);

            _currentVisibleAppId = null;
            OnVisibleAppChanged?.Invoke(null);
            _onMenuStateChanged?.Invoke(RTTManager.MenuState.MainMenu);
            _taskbar?.SelectSlot(0);
        }

        #endregion

        #region Coroutines

        private IEnumerator WaitAndSwitchToPreparedApp(string appId)
        {
            while (_preparingApps.ContainsKey(appId) && !IsAppPrepared(appId))
                yield return null;

            if (_preparingApps.ContainsKey(appId))
            {
                var instance = _preparingApps[appId];
                _preparingApps.Remove(appId);
                _activeApps[appId] = instance;
                StartCoroutine(SwitchToPreparedAppWithTransition(instance));
            }
        }

        private IEnumerator CreateAppFrameAndContent(RTTAppInstance instance)
        {
            if (_mainMenuFrame == null)
            {
                Debug.LogError("[RTTAppManager] MainMenuFrame is null");
                yield break;
            }

            _isTransitioning = true;

            // Animate out
            if (_useFadeTransition && _transitionOutDuration > 0)
                yield return StartCoroutine(AnimateFrameFade(_mainMenuFrame, 1f, 0f, _transitionOutDuration, true));
            else if (_useScaleTransition && _transitionOutDuration > 0)
                yield return StartCoroutine(AnimateFrameScale(_mainMenuFrame.transform, 1f, 0.9f, _transitionOutDuration, true));

            // Create frame
            if (_menu != null)
            {
                instance.Frame = _menu.CreateAppFrame(
                    instance.AppId,
                    _mainMenuFrame.PanelWidth,
                    _mainMenuFrame.PanelHeight,
                    _mainMenuFrame.LogicalWidthValue
                );
            }
            else
            {
                instance.Frame = RTTMenuFrame.Create(
                    _frameParent,
                    _mainMenuFrame.PanelWidth,
                    _mainMenuFrame.PanelHeight,
                    _mainMenuFrame.LogicalWidthValue,
                    name: $"RTTMenuFrame_{instance.AppId}"
                );
            }

            instance.Frame.transform.position = _mainMenuFrame.transform.position;
            instance.Frame.transform.rotation = _mainMenuFrame.transform.rotation;
            instance.Frame.transform.localScale = Vector3.one;

            // Wait for ContentContainer
            int waitFrames = 0;
            while (instance.Frame.ContentContainer == null && waitFrames < 60)
            {
                waitFrames++;
                yield return null;
            }

            if (instance.Frame.ContentContainer == null)
            {
                Debug.LogError($"[RTTAppManager] ContentContainer not ready for {instance.AppId}");
                ResetFrameAlpha(_mainMenuFrame);
                _isTransitioning = false;
                yield break;
            }

            yield return null;

            // Create content via callback
            _createAppContentCallback?.Invoke(instance);

            // Hide MainMenu, show new frame
            _mainMenuFrame.gameObject.SetActive(false);
            ResetFrameAlpha(_mainMenuFrame);

            instance.Frame.SetVisible(true);
            ResetFrameAlpha(instance.Frame);
            instance.Frame.SetAsPrimaryFrame();
            instance.IsVisible = true;
            _currentVisibleAppId = instance.AppId;

            // Animate in
            if (_useFadeTransition && _transitionInDuration > 0)
            {
                var newQuad = instance.Frame.GetDisplayQuad();
                if (newQuad?.material != null)
                    newQuad.material.color = new Color(1f, 1f, 1f, 0f);
                yield return StartCoroutine(AnimateFrameFade(instance.Frame, 0f, 1f, _transitionInDuration, false));
            }
            else if (_useScaleTransition && _transitionInDuration > 0)
            {
                instance.Frame.transform.localScale = Vector3.one * 0.9f;
                yield return StartCoroutine(AnimateFrameScale(instance.Frame.transform, 0.9f, 1f, _transitionInDuration, false));
            }

            // Register with taskbar
            _taskbar?.RegisterApp(instance.TaskbarSlotIndex, instance.Icon, () => SwitchToApp(instance.AppId));
            _taskbar?.SelectSlot(instance.TaskbarSlotIndex);

            _onMenuStateChanged?.Invoke(RTTManager.MenuState.RemoteMenu);
            _isTransitioning = false;

            OnAppOpened?.Invoke(instance.AppId, instance);
            OnVisibleAppChanged?.Invoke(instance.AppId);

            Debug.Log($"[RTTAppManager] App fully opened: {instance.AppId}");
        }

        private IEnumerator PrepareAppFrameAsync(RTTAppInstance instance)
        {
            if (_mainMenuFrame == null)
            {
                Debug.LogError("[RTTAppManager] MainMenuFrame is null");
                yield break;
            }

            // Create frame (hidden)
            if (_menu != null)
            {
                instance.Frame = _menu.CreateAppFrame(
                    instance.AppId,
                    _mainMenuFrame.PanelWidth,
                    _mainMenuFrame.PanelHeight,
                    _mainMenuFrame.LogicalWidthValue
                );
            }
            else
            {
                instance.Frame = RTTMenuFrame.Create(
                    _frameParent,
                    _mainMenuFrame.PanelWidth,
                    _mainMenuFrame.PanelHeight,
                    _mainMenuFrame.LogicalWidthValue,
                    name: $"RTTMenuFrame_{instance.AppId}"
                );
            }

            instance.Frame.transform.position = _mainMenuFrame.transform.position;
            instance.Frame.transform.rotation = _mainMenuFrame.transform.rotation;
            instance.Frame.transform.localScale = Vector3.one;

            // CRITICAL: Set visibility to false BEFORE SetActive to prevent 1-frame alpha flicker
            // This ensures display quad is created with enabled=false from the start
            instance.Frame.SetVisible(false);
            instance.Frame.gameObject.SetActive(true);

            // Wait for ContentContainer
            int waitFrames = 0;
            while (instance.Frame.ContentContainer == null && waitFrames < 60)
            {
                waitFrames++;
                yield return null;
            }

            if (instance.Frame.ContentContainer == null)
            {
                Debug.LogError($"[RTTAppManager] ContentContainer not ready for prepare: {instance.AppId}");
                yield break;
            }

            yield return null;

            // Create content via callback
            _createAppContentCallback?.Invoke(instance);

            // Start background data preparation if controller supports it
            if (instance.Controller is IDataBindable bindable)
            {
                bindable.PrepareDataAsync();
            }

            // Mark as prepared BEFORE hiding
            instance.IsPrepared = true;

            // Now hide the frame
            instance.Frame.gameObject.SetActive(false);

            Debug.Log($"[RTTAppManager] App prepared (hidden): {instance.AppId}");
        }

        private IEnumerator SwitchToPreparedAppWithTransition(RTTAppInstance instance)
        {
            _isTransitioning = true;

            // Animate out current
            if (_useFadeTransition && _transitionOutDuration > 0)
                yield return StartCoroutine(AnimateFrameFade(_mainMenuFrame, 1f, 0f, _transitionOutDuration, true));

            // Hide main menu
            _mainMenuFrame.gameObject.SetActive(false);
            ResetFrameAlpha(_mainMenuFrame);

            // Show prepared frame
            instance.Frame.gameObject.SetActive(true);
            instance.Frame.SetVisible(true);
            instance.Frame.SetAsPrimaryFrame();
            instance.IsVisible = true;
            _currentVisibleAppId = instance.AppId;

            // Animate in
            if (_useFadeTransition && _transitionInDuration > 0)
            {
                var quad = instance.Frame.GetDisplayQuad();
                if (quad?.material != null)
                    quad.material.color = new Color(1f, 1f, 1f, 0f);
                yield return StartCoroutine(AnimateFrameFade(instance.Frame, 0f, 1f, _transitionInDuration, false));
            }
            else
            {
                // No fade transition - ensure alpha is reset to 1 (was set to 0 during preparation)
                ResetFrameAlpha(instance.Frame);
            }

            // Register with taskbar
            _taskbar?.RegisterApp(instance.TaskbarSlotIndex, instance.Icon, () => SwitchToApp(instance.AppId));
            _taskbar?.SelectSlot(instance.TaskbarSlotIndex);

            _onMenuStateChanged?.Invoke(RTTManager.MenuState.RemoteMenu);
            _isTransitioning = false;

            // Safe data binding after transition completes
            // This shows loading spinner if data not ready yet
            if (instance.Controller is IDataBindable bindable)
            {
                yield return StartCoroutine(bindable.BindDataSafely());
            }

            OnAppOpened?.Invoke(instance.AppId, instance);
            OnVisibleAppChanged?.Invoke(instance.AppId);
        }

        private IEnumerator SwitchToAppWithTransition(string appId)
        {
            if (!_activeApps.ContainsKey(appId)) yield break;

            _isTransitioning = true;

            RTTMenuFrame currentFrame = _currentVisibleAppId == null
                ? _mainMenuFrame
                : (_activeApps.ContainsKey(_currentVisibleAppId) ? _activeApps[_currentVisibleAppId].Frame : null);

            // Animate out
            if (currentFrame != null && _useFadeTransition && _transitionOutDuration > 0)
                yield return StartCoroutine(AnimateFrameFade(currentFrame, 1f, 0f, _transitionOutDuration, true));

            // Hide current
            HideCurrentView();
            if (currentFrame != null)
                ResetFrameAlpha(currentFrame);

            // Show new app
            var newApp = _activeApps[appId];
            if (newApp.Frame != null)
            {
                newApp.Frame.gameObject.SetActive(true);
                newApp.Frame.SetVisible(true);
                newApp.Frame.SetAsPrimaryFrame();
                newApp.IsVisible = true;
            }
            _currentVisibleAppId = appId;

            // Animate in
            if (newApp.Frame != null && _useFadeTransition && _transitionInDuration > 0)
            {
                var quad = newApp.Frame.GetDisplayQuad();
                if (quad?.material != null)
                    quad.material.color = new Color(1f, 1f, 1f, 0f);
                yield return StartCoroutine(AnimateFrameFade(newApp.Frame, 0f, 1f, _transitionInDuration, false));
            }
            else if (newApp.Frame != null)
            {
                // No fade transition - ensure alpha is visible
                ResetFrameAlpha(newApp.Frame);
            }

            _taskbar?.SelectSlot(newApp.TaskbarSlotIndex);
            _onMenuStateChanged?.Invoke(RTTManager.MenuState.RemoteMenu);
            _isTransitioning = false;

            OnVisibleAppChanged?.Invoke(appId);
        }

        private IEnumerator SwitchToHomeWithTransition()
        {
            _isTransitioning = true;

            RTTMenuFrame currentFrame = _currentVisibleAppId != null && _activeApps.ContainsKey(_currentVisibleAppId)
                ? _activeApps[_currentVisibleAppId].Frame
                : null;

            // Animate out
            if (currentFrame != null && _useFadeTransition && _transitionOutDuration > 0)
                yield return StartCoroutine(AnimateFrameFade(currentFrame, 1f, 0f, _transitionOutDuration, true));

            // Hide current app
            HideCurrentView();
            if (currentFrame != null)
                ResetFrameAlpha(currentFrame);

            // Show main menu
            if (_mainMenuFrame != null)
            {
                _mainMenuFrame.gameObject.SetActive(true);
                _mainMenuFrame.SetAsPrimaryFrame();
            }
            if (_mainMenuContent != null)
                _mainMenuContent.SetActive(true);

            _currentVisibleAppId = null;

            // Animate in
            if (_mainMenuFrame != null && _useFadeTransition && _transitionInDuration > 0)
            {
                var quad = _mainMenuFrame.GetDisplayQuad();
                if (quad?.material != null)
                    quad.material.color = new Color(1f, 1f, 1f, 0f);
                yield return StartCoroutine(AnimateFrameFade(_mainMenuFrame, 0f, 1f, _transitionInDuration, false));
            }
            else if (_mainMenuFrame != null)
            {
                // No fade transition - ensure alpha is visible
                ResetFrameAlpha(_mainMenuFrame);
            }

            _taskbar?.SelectSlot(0);
            _onMenuStateChanged?.Invoke(RTTManager.MenuState.MainMenu);
            _isTransitioning = false;

            OnVisibleAppChanged?.Invoke(null);
        }

        private IEnumerator CloseAppWithTransition(string appId)
        {
            if (!_activeApps.ContainsKey(appId)) yield break;

            _isTransitioning = true;

            var app = _activeApps[appId];

            // Animate out
            if (app.Frame != null && _useFadeTransition && _transitionOutDuration > 0)
                yield return StartCoroutine(AnimateFrameFade(app.Frame, 1f, 0f, _transitionOutDuration, true));

            _isTransitioning = false;
            CloseAppInternal(appId, skipSwitchToHome: false);
        }

        #endregion

        #region Animation Helpers

        private IEnumerator AnimateFrameFade(RTTMenuFrame frame, float from, float to, float duration, bool isOut)
        {
            if (frame == null) yield break;

            var quad = frame.GetDisplayQuad();
            if (quad?.material == null) yield break;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = Mathf.Lerp(from, to, t);
                quad.material.color = new Color(1f, 1f, 1f, alpha);
                yield return null;
            }

            quad.material.color = new Color(1f, 1f, 1f, to);
        }

        private IEnumerator AnimateFrameScale(Transform target, float from, float to, float duration, bool isOut)
        {
            if (target == null) yield break;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float scale = Mathf.Lerp(from, to, t);
                target.localScale = Vector3.one * scale;
                yield return null;
            }

            target.localScale = Vector3.one * to;
        }

        private void ResetFrameAlpha(RTTMenuFrame frame)
        {
            if (frame == null) return;
            var quad = frame.GetDisplayQuad();
            if (quad?.material != null)
                quad.material.color = Color.white;
        }

        #endregion

        #region Cleanup

        public void CleanupAllApps()
        {
            foreach (var appId in new List<string>(_activeApps.Keys))
            {
                CloseAppInternal(appId, skipSwitchToHome: true);
            }
            CancelAllPreparations();
        }

        private void OnDestroy()
        {
            CleanupAllApps();
        }

        #endregion
    }
}
