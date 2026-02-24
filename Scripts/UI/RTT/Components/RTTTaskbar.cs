using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.Core;
using VRWorkspace.Input;
using VRWorkspace.Media.Core;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// RTTTaskbar - Logic controller that attaches to RTTMiniFrame.
    /// Manages control buttons and app slots.
    /// RTTMiniFrame handles all RTT infrastructure, visual frame, and position tracking.
    /// </summary>
    [RequireComponent(typeof(RTTMiniFrame))]
    public class RTTTaskbar : MonoBehaviour
    {
        #region Static Instance
        private static RTTTaskbar _instance;
        public static RTTTaskbar Instance => _instance;
        #endregion

        #region Configuration
        [Header("Style Resources")]
        [SerializeField] private Sprite iconQuit;
        [SerializeField] private Sprite iconSettings;
        [SerializeField] private Sprite iconEye;
        [SerializeField] private Sprite iconEyeClose;
        [SerializeField] private Sprite iconRecenter;
        [SerializeField] private Sprite iconHome;
        #endregion

        #region Private Fields
        private RTTMiniFrame _miniFrame;
        private GameObject _eyeButton;
        private List<GameObject> _appButtons = new List<GameObject>(); // UI slots (fixed positions)
        private int _activeAppSlotIndex = 0; // Currently active app's slot index (0 = Home)
        private Dictionary<int, Action> _appSlotCallbacks = new Dictionary<int, Action>();

        // Registered apps in visual order (first N go to main taskbar, rest to expansion)
        private List<RegisteredAppData> _registeredApps = new List<RegisteredAppData>();

        private class RegisteredAppData
        {
            public int SlotIndex; // Original slot index from RTTManager
            public Sprite Icon;
            public Action OnClick;
        }

        // State
        private bool _isPassthroughOn = false;
        private bool _isLightOn = true; // Default ON
        private bool _savedPassthroughState = false; // Lưu trạng thái passthrough khi tắt đèn

        // Eye expansion panel
        private RTTTaskbarExpansion _eyeExpansion;

        // App overflow expansion panel
        private RTTTaskbarExpansion _appExpansion;

        // Quit Confirmation Popup
        private RTTPopupMenu _quitConfirmPopup;
        #endregion

        #region Lifecycle
        private void Awake()
        {
            _miniFrame = GetComponent<RTTMiniFrame>();
            LoadIcons();
        }

        private void Start()
        {
            _instance = this;

            // Wait for RTTMiniFrame to initialize, then add buttons
            StartCoroutine(InitializeAfterFrame());
        }

        private System.Collections.IEnumerator InitializeAfterFrame()
        {
            // Wait for RTTMiniFrame to build UI
            yield return null;
            yield return null;

            // Add buttons to sections
            AddControlButtons();
            AddAppButtons();

            // Initialize environment controller
            if (MediaEnvironmentController.Instance != null)
            {
                MediaEnvironmentController.Instance.Initialize();
                MediaEnvironmentController.Instance.OnLightsChanged += HandleLightsChanged;
            }

            // Sync passthrough state
            SyncPassthroughWithModeController();

            // Mark dirty to re-render
            _miniFrame.MarkDirty();
        }

        private void SyncPassthroughWithModeController()
        {
            var modeController = FindFirstObjectByType<ModeController>();
            if (modeController != null)
            {
                _isPassthroughOn = modeController.mode == ViewMode.RealWorld;
                UpdateEyeButtonColor();
            }
        }

        private void CreateEyeExpansion()
        {
            if (_eyeExpansion != null) return;

            // Get RTTToolbar
            RTTToolbar toolbar = RTTToolbar.Instance;
            if (toolbar == null)
            {
                Debug.LogWarning("[RTTTaskbar] RTTToolbar not found, cannot create Eye expansion");
                return;
            }

            // Create expansion panel in RTTToolbar
            GameObject expansionObj = new GameObject("RTTEyeExpansion");
            expansionObj.transform.SetParent(toolbar.transform, false);

            _eyeExpansion = expansionObj.AddComponent<RTTTaskbarExpansion>();

            // Subscribe to events
            _eyeExpansion.OnPassthroughToggled += OnPassthroughToggled;
            _eyeExpansion.OnLightToggled += OnLightToggled;
            _eyeExpansion.OnDismissed += OnEyeExpansionDismissed;

            Debug.Log("[RTTTaskbar] Eye expansion panel created");
        }

        private void ShowEyeExpansion()
        {
            // Create expansion if not exists
            bool justCreated = false;
            if (_eyeExpansion == null)
            {
                CreateEyeExpansion();
                justCreated = true;
            }

            if (_eyeExpansion == null) return;

            // Toggle behavior: if already showing Eye options, hide it
            if (_eyeExpansion.IsVisible && _eyeExpansion.CurrentType == RTTTaskbarExpansion.ExpansionType.Eye)
            {
                Debug.Log("[RTTTaskbar] Eye clicked - hiding expansion panel (toggle)");
                _eyeExpansion.Hide();
                return;
            }

            // If just created, delay show to next frame to ensure canvas is initialized
            if (justCreated)
            {
                StartCoroutine(ShowEyeExpansionDelayed());
            }
            else
            {
                ShowEyeExpansionImmediate();
            }
        }

        private System.Collections.IEnumerator ShowEyeExpansionDelayed()
        {
            yield return null; // Wait one frame for canvas initialization

            ShowEyeExpansionImmediate();
        }

        private void ShowEyeExpansionImmediate()
        {
            if (_eyeExpansion == null) return;

            // Get Eye button world position for alignment
            Vector3? triggerPos = GetButtonWorldPosition(_eyeButton);

            // Show expansion
            _eyeExpansion.ShowEyeOptions(_isPassthroughOn, _isLightOn, triggerPos);

            // Disable passthrough button if light is OFF
            _eyeExpansion.SetPassthroughInteractable(_isLightOn);

            Debug.Log("[RTTTaskbar] Eye expansion shown");
        }

        /// <summary>
        /// Get world position of a button in the RTT canvas.
        /// Uses RectTransformUtility to get accurate bounds even with layout groups.
        /// </summary>
        private Vector3? GetButtonWorldPosition(GameObject button)
        {
            if (button == null || _miniFrame == null) return null;

            var buttonRT = button.GetComponent<RectTransform>();
            if (buttonRT == null) return null;

            // Get miniframe's canvas to calculate relative bounds
            var miniFrameCanvas = _miniFrame.GetCanvas();
            if (miniFrameCanvas == null) return null;

            var canvasRT = miniFrameCanvas.GetComponent<RectTransform>();
            if (canvasRT == null) return null;

            // Get bounds of button relative to canvas center
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRT, buttonRT);

            // Get miniframe's world size and resolution
            Vector2 worldSize = _miniFrame.GetWorldSize();
            Vector2 resolution = new Vector2(_miniFrame.TotalWidth, _miniFrame.TotalHeight);

            // Convert pixels to meters (relative to miniframe center)
            float pixelToMeter = worldSize.x / resolution.x;
            float localX = bounds.center.x * pixelToMeter;
            float localY = bounds.center.y * pixelToMeter;

            // Transform to world space
            Vector3 localRight = _miniFrame.transform.TransformDirection(Vector3.right);
            Vector3 localUp = _miniFrame.transform.TransformDirection(Vector3.up);

            Vector3 worldPos = _miniFrame.transform.position + localRight * localX + localUp * localY;
            return worldPos;
        }

        private void OnPassthroughToggled(bool isOn)
        {
            // Chặn toggle passthrough khi đèn đang tắt
            if (!_isLightOn)
            {
                Debug.Log("[RTTTaskbar] Cannot toggle passthrough while light is OFF");
                return;
            }

            _isPassthroughOn = isOn;
            Debug.Log($"[RTTTaskbar] Passthrough toggled: {(_isPassthroughOn ? "ON" : "OFF")}");

            UpdateEyeButtonColor();

            var modeController = FindFirstObjectByType<ModeController>();
            if (modeController != null)
            {
                modeController.SetMode(_isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace);
            }

            _miniFrame.MarkDirty();
        }

        private void HandleLightsChanged(bool isOn)
        {
            if (_isLightOn == isOn) return;

            _isLightOn = isOn;
            UpdateEyeButtonColor();

            if (_eyeExpansion != null)
            {
                _eyeExpansion.SetLightState(isOn);
            }

            _miniFrame.MarkDirty();
            Debug.Log($"[RTTTaskbar] Light state synchronized to: {(isOn ? "ON" : "OFF")}");
        }

        private void OnLightToggled(bool isOn)
        {
            _isLightOn = isOn;
            Debug.Log($"[RTTTaskbar] Light toggled: {(_isLightOn ? "ON" : "OFF")}");

            // Use MediaEnvironmentController for unified light/environment management
            var envController = MediaEnvironmentController.Instance;
            if (envController != null)
            {
                envController.SetLightsEnabled(isOn);
            }

            var modeController = FindFirstObjectByType<ModeController>();

            if (!_isLightOn)
            {
                // Tắt đèn: lưu trạng thái passthrough, tắt passthrough
                _savedPassthroughState = _isPassthroughOn;

                if (modeController != null)
                {
                    // Tắt passthrough trực tiếp
                    if (modeController.cameraPassthrough != null)
                    {
                        modeController.cameraPassthrough.enabled = false;
                    }

                    // Tắt backgroundReal (tránh màn hình trắng khi passthrough đang ON)
                    if (modeController.backgroundReal != null)
                    {
                        modeController.backgroundReal.enabled = false;
                    }

                    // Bật backgroundVirtual để hiển thị màu đen (clear color)
                    if (modeController.backgroundVirtual != null)
                    {
                        modeController.backgroundVirtual.enabled = true;
                    }
                }

                // Cập nhật trạng thái passthrough trong expansion panel
                _isPassthroughOn = false;
                if (_eyeExpansion != null)
                {
                    _eyeExpansion.SetPassthroughState(false);
                    _eyeExpansion.SetPassthroughInteractable(false); // Disable nút passthrough
                }
            }
            else
            {
                // Bật đèn: khôi phục virtual environment và passthrough state
                if (modeController != null)
                {
                    // Khôi phục passthrough state
                    if (_savedPassthroughState)
                    {
                        _isPassthroughOn = true;
                        // Dùng SetMode để khôi phục đầy đủ (bật backgroundReal, passthrough, ẩn virtualEnv)
                        modeController.SetMode(ViewMode.RealWorld);
                    }
                    else
                    {
                        // No passthrough mode changes needed, environment visibility handled by MediaEnvironmentController
                    }
                }

                // Enable lại nút passthrough trong expansion
                if (_eyeExpansion != null)
                {
                    _eyeExpansion.SetPassthroughState(_isPassthroughOn);
                    _eyeExpansion.SetPassthroughInteractable(true);
                }
            }

            UpdateEyeButtonColor();
            _miniFrame.MarkDirty();

            // Sync with RTTRemoteTaskbar
            if (RTTRemoteTaskbar.Instance != null)
            {
                RTTRemoteTaskbar.Instance.SyncLightState(_isLightOn, _savedPassthroughState);
            }
        }

        /// <summary>
        /// Sync light state from another taskbar (UI only, no ModeController changes).
        /// </summary>
        public void SyncLightState(bool isLightOn, bool savedPassthroughState)
        {
            if (_isLightOn == isLightOn) return;

            _isLightOn = isLightOn;
            _savedPassthroughState = savedPassthroughState;

            if (!_isLightOn)
            {
                _isPassthroughOn = false;
            }
            else if (_savedPassthroughState)
            {
                _isPassthroughOn = true;
            }

            // Update expansion panel if visible
            if (_eyeExpansion != null)
            {
                _eyeExpansion.SetPassthroughState(_isPassthroughOn);
                _eyeExpansion.SetPassthroughInteractable(_isLightOn);
            }

            UpdateEyeButtonColor();
            _miniFrame.MarkDirty();

            Debug.Log($"[RTTTaskbar] Light state synced: {(_isLightOn ? "ON" : "OFF")}");
        }

        private void OnEyeExpansionDismissed()
        {
            Debug.Log("[RTTTaskbar] Eye expansion dismissed");
        }

        private void OnDestroy()
        {
            if (MediaEnvironmentController.Instance != null)
            {
                MediaEnvironmentController.Instance.OnLightsChanged -= HandleLightsChanged;
            }

            if (_instance == this) _instance = null;

            // Cleanup eye expansion
            if (_eyeExpansion != null)
            {
                _eyeExpansion.OnPassthroughToggled -= OnPassthroughToggled;
                _eyeExpansion.OnLightToggled -= OnLightToggled;
                _eyeExpansion.OnDismissed -= OnEyeExpansionDismissed;
            }

            // Cleanup app expansion
            if (_appExpansion != null)
            {
                _appExpansion.OnAppSlotClicked -= OnAppExpansionSlotClicked;
                _appExpansion.OnDismissed -= OnAppExpansionDismissed;
            }

            // Cleanup quit confirm popup
            if (_quitConfirmPopup != null)
            {
                Destroy(_quitConfirmPopup.gameObject);
                _quitConfirmPopup = null;
            }
        }
        #endregion

        #region Button Creation
        private void AddControlButtons()
        {
            var section1 = _miniFrame.GetSection1Container();
            if (section1 == null) return;

            Color cyanColor = new Color(0f, 0.9f, 1f);
            float buttonSize = _miniFrame.ButtonSize;

            // Quit
            CreateIconButton(section1, iconQuit, "Quit", cyanColor, buttonSize, () =>
            {
                Debug.Log("[RTTTaskbar] Quit clicked");
                ShowQuitConfirmPopup();
            });

            // Settings
            CreateIconButton(section1, iconSettings, "Settings", cyanColor, buttonSize, () =>
            {
                Debug.Log("[RTTTaskbar] Settings clicked");
            });

            // Eye (opens expansion with Passthrough + Light)
            _eyeButton = CreateIconButton(section1, iconEye, "Eye", cyanColor, buttonSize, ShowEyeExpansion);

            // Recenter
            CreateIconButton(section1, iconRecenter, "Recenter", cyanColor, buttonSize, RecenterObject);
        }

        private void AddAppButtons()
        {
            var section2 = _miniFrame.GetSection2Container();
            if (section2 == null) return;

            Color cyanColor = new Color(0f, 0.9f, 1f);
            Color purpleColor = new Color(0.9f, 0.3f, 1f);
            float buttonSize = _miniFrame.ButtonSize;
            int section2Capacity = _miniFrame.Section2Capacity;

            _appButtons.Clear();

            for (int i = 0; i < section2Capacity; i++)
            {
                Sprite slotIcon = (i == 0) ? iconHome : null;
                string slotName = (i == 0) ? "Home" : $"AppSlot_{i}";
                bool isActive = (i == _activeAppSlotIndex);
                Color btnColor = isActive ? purpleColor : cyanColor;
                int capturedIndex = i;

                var btn = CreateAppButton(section2, slotIcon, slotName, btnColor, buttonSize, capturedIndex);
                _appButtons.Add(btn);

                // Disable interaction for active button
                if (isActive)
                {
                    SetAppButtonInteractable(btn, false);
                }

                if (i == 0)
                {
                    RewireHomeButton(btn);
                }
                else
                {
                    var cg = btn.GetComponent<CanvasGroup>();
                    if (cg != null) cg.alpha = 0f;
                }
            }
        }

        private GameObject CreateIconButton(Transform parent, Sprite icon, string name, Color glowColor, float buttonSize, Action onClick)
        {
            var btn = VRButtonFactory.CreateBareIconButton(
                parent, buttonSize, icon, glowColor,
                () =>
                {
                    onClick?.Invoke();
                    _miniFrame.MarkDirty();
                },
                0.05f, 0.6f
            );

            btn.name = $"Btn_{name}";
            SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));

            return btn;
        }

        private GameObject CreateAppButton(Transform parent, Sprite icon, string name, Color glowColor, float buttonSize, int index)
        {
            GameObject btn = new GameObject($"AppBtn_{name}");
            btn.transform.SetParent(parent, false);

            RectTransform rt = btn.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(buttonSize, buttonSize);

            CanvasGroup cg = btn.AddComponent<CanvasGroup>();

            if (icon != null)
            {
                var iconBtn = VRButtonFactory.CreateBareIconButton(
                    btn.transform, buttonSize, icon, glowColor,
                    () =>
                    {
                        SelectAppButton(index);
                        _miniFrame.MarkDirty();
                    },
                    0.05f, 0.6f
                );
                iconBtn.transform.SetAsFirstSibling();
                SetLayerRecursively(iconBtn, LayerMask.NameToLayer("UI"));
            }

            SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));

            return btn;
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            if (layer == -1) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
        #endregion

        #region Button Actions
        /// <summary>
        /// Update Eye button color based on whether any eye option is active.
        /// Purple if Passthrough OR Light is different from default, Cyan otherwise.
        /// </summary>
        private void UpdateEyeButtonColor()
        {
            if (_eyeButton == null) return;

            Color cyanColor = new Color(0f, 0.9f, 1f);
            Color purpleColor = new Color(0.9f, 0.3f, 1f);

            // Eye button is purple if passthrough is ON or light is OFF (non-default states)
            bool hasActiveState = _isPassthroughOn || !_isLightOn;
            Color targetColor = hasActiveState ? purpleColor : cyanColor;

            // Determine icon: eye_close when light is OFF, eye when light is ON
            Sprite targetIcon = _isLightOn ? iconEye : iconEyeClose;

            Transform iconTransform = _eyeButton.transform.Find("HitArea/Visuals/Content/Icon");
            if (iconTransform != null)
            {
                Image iconImg = iconTransform.GetComponent<Image>();
                if (iconImg != null)
                {
                    // Update icon sprite based on light state
                    if (targetIcon != null)
                    {
                        iconImg.sprite = targetIcon;
                    }

                    iconImg.color = Color.Lerp(targetColor, Color.white, 0.9f);

                    Shadow[] shadows = iconTransform.GetComponents<Shadow>();
                    if (shadows.Length >= 2)
                    {
                        Color glowCol = Color.Lerp(targetColor, Color.white, 0.7f);
                        glowCol.a = 0.4f;
                        shadows[0].effectColor = glowCol;
                        shadows[1].effectColor = glowCol;
                    }
                }
            }

            // Force hover (scale effect) when in active state (purple)
            var hoverController = _eyeButton.GetComponentInChildren<HoverEffectController>();
            if (hoverController != null)
            {
                hoverController.SetForceHover(hasActiveState);
            }
        }

        public void SetPassthrough(bool isOn)
        {
            if (_isPassthroughOn != isOn)
            {
                _isPassthroughOn = isOn;
                UpdateEyeButtonColor();

                var modeController = FindFirstObjectByType<ModeController>();
                if (modeController != null)
                {
                    modeController.SetMode(_isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace);
                }

                _miniFrame.MarkDirty();
                Debug.Log($"[RTTTaskbar] Passthrough set to {(_isPassthroughOn ? "ON" : "OFF")}");
            }
        }

        public void SetLight(bool isOn)
        {
            if (_isLightOn != isOn)
            {
                _isLightOn = isOn;
                UpdateEyeButtonColor();

                // Use MediaEnvironmentController for all lights
                var envController = MediaEnvironmentController.Instance;
                if (envController != null)
                {
                    envController.SetLightsEnabled(isOn);
                }

                _miniFrame.MarkDirty();
                Debug.Log($"[RTTTaskbar] Light set to {(_isLightOn ? "ON" : "OFF")}");
            }
        }

        private void RecenterObject()
        {
            Debug.Log("[RTTTaskbar] Recenter clicked");
            StartCoroutine(RecenterRoutine());
        }

        private System.Collections.IEnumerator RecenterRoutine()
        {
            VRGazeReticle reticle = VRGazeReticle.Instance;
            if (reticle == null) reticle = FindFirstObjectByType<VRGazeReticle>();

            if (reticle != null)
            {
                reticle.EnterRecenterMode(iconRecenter);
            }

            float duration = 2.0f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);

                if (reticle != null)
                {
                    reticle.UpdateRecenterProgress(progress);
                }

                yield return null;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                RecenterAllVirtualObjects(cam);
            }

            if (reticle != null)
            {
                reticle.ExitRecenterMode();
            }

            _miniFrame.MarkDirty();
            Debug.Log("[RTTTaskbar] Recenter complete.");
        }

        private void RecenterAllVirtualObjects(Camera cam)
        {
            GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");
            if (virtualObjectsParent == null)
            {
                Debug.LogWarning("[RTTTaskbar] VirtualObjects parent not found, falling back to primary only");
                RecenterPrimaryOnly(cam);
                return;
            }

            RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
            if (primary == null)
            {
                Debug.LogWarning("[RTTTaskbar] No primary RTTMenuFrame found");
                return;
            }

            Vector3 pivotPos = primary.transform.position;
            Quaternion pivotRot = primary.transform.rotation;

            List<Transform> children = new List<Transform>();
            List<Vector3> relativePositions = new List<Vector3>();
            List<Quaternion> relativeRotations = new List<Quaternion>();

            foreach (Transform child in virtualObjectsParent.transform)
            {
                children.Add(child);
                Vector3 relPos = Quaternion.Inverse(pivotRot) * (child.position - pivotPos);
                relativePositions.Add(relPos);
                Quaternion relRot = Quaternion.Inverse(pivotRot) * child.rotation;
                relativeRotations.Add(relRot);
            }

            Vector3 camForward = cam.transform.forward;
            camForward.y = 0;
            if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
            camForward.Normalize();

            Vector3 camPos = cam.transform.position;
            float hDist = Vector2.Distance(
                new Vector2(pivotPos.x, pivotPos.z),
                new Vector2(camPos.x, camPos.z)
            );

            Vector3 newPivotPos = camPos + camForward * hDist;
            newPivotPos.y = pivotPos.y;
            Quaternion newPivotRot = Quaternion.LookRotation(camForward);

            for (int i = 0; i < children.Count; i++)
            {
                Transform child = children[i];
                child.position = newPivotPos + newPivotRot * relativePositions[i];
                child.rotation = newPivotRot * relativeRotations[i];
            }
        }

        private void RecenterPrimaryOnly(Camera cam)
        {
            // Recenter the follow target if set
            var followTarget = _miniFrame.GetFollowTarget();
            if (followTarget != null)
            {
                RecenterTransform(followTarget, cam);
            }
            else
            {
                // No follow target, recenter this transform directly
                RecenterTransform(transform, cam);
            }
        }

        private void RecenterTransform(Transform target, Camera cam)
        {
            Vector3 camForward = cam.transform.forward;
            camForward.y = 0;
            if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
            camForward.Normalize();

            Vector3 currentPos = target.position;
            Vector3 camPos = cam.transform.position;
            float hDist = Vector2.Distance(new Vector2(currentPos.x, currentPos.z), new Vector2(camPos.x, camPos.z));

            Vector3 newPos = camPos + camForward * hDist;
            newPos.y = currentPos.y;

            target.position = newPos;
            target.rotation = Quaternion.LookRotation(camForward);
        }

        /// <summary>
        /// Select an app by its slot index (as used by RTTManager).
        /// </summary>
        private void SelectAppButton(int slotIndex)
        {
            // Skip if already active
            if (slotIndex == _activeAppSlotIndex) return;

            _activeAppSlotIndex = slotIndex;

            // Clear expansion selection when selecting main taskbar button
            if (_appExpansion != null)
            {
                _appExpansion.ClearAppSlotSelection();
            }

            UpdateAllAppButtonColors();

            Debug.Log($"[RTTTaskbar] Selected app with slot index {slotIndex}");

            // Invoke callback after selection (but not for Home which has no callback)
            if (slotIndex > 0 && _appSlotCallbacks.ContainsKey(slotIndex))
            {
                _appSlotCallbacks[slotIndex]?.Invoke();
            }

            _miniFrame.MarkDirty();
        }

        private void UpdateAllAppButtonColors()
        {
            Color cyanColor = new Color(0f, 0.9f, 1f);
            Color purpleColor = new Color(0.9f, 0.3f, 1f);

            // Home button (index 0)
            bool isHomeActive = (_activeAppSlotIndex == 0);
            var homeBtn = _appButtons[0];
            if (homeBtn != null)
            {
                SetBareIconButtonColor(homeBtn, isHomeActive ? purpleColor : cyanColor);
                SetAppButtonInteractable(homeBtn, !isHomeActive);
            }

            // App buttons (visual positions 1, 2, 3...)
            int mainCapacity = MainTaskbarAppCapacity;
            for (int visualIndex = 0; visualIndex < mainCapacity; visualIndex++)
            {
                int uiSlotIndex = visualIndex + 1; // +1 because slot 0 is Home
                var btn = _appButtons[uiSlotIndex];
                if (btn == null) continue;

                // Find which app is at this visual position
                bool isActive = false;
                if (visualIndex < _registeredApps.Count)
                {
                    var appData = _registeredApps[visualIndex];
                    isActive = (appData.SlotIndex == _activeAppSlotIndex);
                }

                SetBareIconButtonColor(btn, isActive ? purpleColor : cyanColor);
                SetAppButtonInteractable(btn, !isActive);
            }

            _miniFrame.MarkDirty();
        }

        private void SetAppButtonInteractable(GameObject container, bool interactable)
        {
            var button = container.GetComponentInChildren<Button>();
            if (button != null)
            {
                button.interactable = interactable;
            }

            // Force hover state when button is active (not interactable)
            var hoverController = container.GetComponentInChildren<HoverEffectController>();
            if (hoverController != null)
            {
                hoverController.SetForceHover(!interactable);
            }
        }

        private void SetBareIconButtonColor(GameObject container, Color color)
        {
            Transform iconTransform = null;

            foreach (Transform child in container.transform)
            {
                var hitArea = child.Find("HitArea");
                if (hitArea != null)
                {
                    var visuals = hitArea.Find("Visuals");
                    if (visuals != null)
                    {
                        var content = visuals.Find("Content");
                        if (content != null)
                        {
                            iconTransform = content.Find("Icon");
                            break;
                        }
                    }
                }
            }

            if (iconTransform == null) return;

            Image iconImg = iconTransform.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.color = Color.Lerp(color, Color.white, 0.9f);

                Shadow[] shadows = iconTransform.GetComponents<Shadow>();
                if (shadows.Length >= 2)
                {
                    Color glowCol = Color.Lerp(color, Color.white, 0.7f);
                    glowCol.a = 0.4f;
                    shadows[0].effectColor = glowCol;
                    shadows[1].effectColor = glowCol;
                }
            }
        }
        #endregion

        #region Public API
        public bool IsPassthroughOn => _isPassthroughOn;
        public bool IsLightOn => _isLightOn;
        public int ActiveAppButtonIndex => _activeAppSlotIndex;
        public bool IsHomeActive => _activeAppSlotIndex == 0;

        /// <summary>
        /// Hide the taskbar by delegating to RTTMiniFrame.
        /// Also hides the app expansion panel.
        /// </summary>
        public void Hide()
        {
            if (_miniFrame != null)
                _miniFrame.Hide();

            // Hide app expansion when taskbar hides
            if (_appExpansion != null && _appExpansion.gameObject.activeSelf)
            {
                _appExpansion.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Show the taskbar by delegating to RTTMiniFrame.
        /// Also shows the app expansion panel if it has apps.
        /// </summary>
        public void Show()
        {
            if (_miniFrame != null)
            {
                _miniFrame.Show();

                // Re-register with RTTToolbar as the active taskbar
                if (RTTToolbar.Instance != null)
                {
                    RTTToolbar.Instance.SetActiveTaskbar(_miniFrame);
                }
            }

            // Show app expansion if it has apps
            if (_appExpansion != null && _appExpansion.AppSlotCount > 0)
            {
                _appExpansion.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Get the follow target that this taskbar is tracking.
        /// Used by RTTFilePagination and RTTTaskbarExpansion to share the same target.
        /// </summary>
        public Transform GetFollowTarget() => _miniFrame?.GetFollowTarget();

        public void SelectHome()
        {
            SelectAppButton(0);
        }

        public void SelectAppButtonPublic(int index)
        {
            SelectAppButton(index);
        }
        #endregion

        #region App Slot Management
        /// <summary>
        /// Get the number of app slots in main taskbar (excluding Home at index 0).
        /// </summary>
        private int MainTaskbarAppCapacity => _appButtons.Count - 1; // Exclude Home button

        public void RegisterApp(int slotIndex, Sprite icon, Action onClick)
        {
            Debug.Log($"[RTTTaskbar] RegisterApp called - slotIndex={slotIndex}, icon={(icon != null ? icon.name : "NULL")}");

            if (slotIndex < 1)
            {
                Debug.LogWarning($"[RTTTaskbar] Invalid slot index: {slotIndex} (must be >= 1)");
                return;
            }

            // Check if already registered
            var existing = _registeredApps.Find(a => a.SlotIndex == slotIndex);
            if (existing != null)
            {
                existing.Icon = icon;
                existing.OnClick = onClick;
            }
            else
            {
                // Add to registered apps list (visual order)
                _registeredApps.Add(new RegisteredAppData
                {
                    SlotIndex = slotIndex,
                    Icon = icon,
                    OnClick = onClick
                });
            }

            _appSlotCallbacks[slotIndex] = onClick;

            // Re-render all app buttons based on visual order
            RenderAllAppButtons();

            Debug.Log($"[RTTTaskbar] Registered app slot {slotIndex}, total apps: {_registeredApps.Count}");
        }

        /// <summary>
        /// Re-render all app buttons based on visual order in _registeredApps.
        /// First N apps go to main taskbar, rest to expansion.
        /// </summary>
        private void RenderAllAppButtons()
        {
            int mainCapacity = MainTaskbarAppCapacity;

            // Clear all main taskbar app slots (except Home at index 0)
            for (int i = 1; i < _appButtons.Count; i++)
            {
                var slot = _appButtons[i];
                if (slot == null) continue;

                // Clear existing icon
                ClearSlotIcon(slot);

                var cg = slot.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 0f;
            }

            // Render apps to main taskbar (visual positions 1, 2, 3...)
            for (int visualIndex = 0; visualIndex < _registeredApps.Count && visualIndex < mainCapacity; visualIndex++)
            {
                var appData = _registeredApps[visualIndex];
                int uiSlotIndex = visualIndex + 1; // +1 because slot 0 is Home

                var slot = _appButtons[uiSlotIndex];
                if (slot == null) continue;

                var cg = slot.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;

                SetSlotIcon(slot, appData.Icon, appData.SlotIndex);
            }

            // Handle expansion apps (apps beyond main taskbar capacity)
            int expansionAppCount = Mathf.Max(0, _registeredApps.Count - mainCapacity);

            if (expansionAppCount > 0)
            {
                // Create expansion if not exists
                if (_appExpansion == null)
                {
                    CreateAppExpansion();
                }

                if (_appExpansion != null)
                {
                    // Clear existing expansion slots first (this will hide if empty)
                    _appExpansion.ClearAllAppSlots();

                    // Set expansion position (aligned with Section 2 center) before registering slots
                    Vector3 section2Center = GetSection2CenterWorldPosition();
                    _appExpansion.SetAppExpansionPosition(section2Center);

                    // Register expansion apps - RegisterAppSlot will show expansion when first slot is added
                    for (int i = mainCapacity; i < _registeredApps.Count; i++)
                    {
                        var appData = _registeredApps[i];
                        Action wrappedCallback = () =>
                        {
                            ClearMainTaskbarVisualSelection();
                            appData.OnClick?.Invoke();
                        };
                        _appExpansion.RegisterAppSlot(appData.SlotIndex, appData.Icon, wrappedCallback);
                    }
                }
            }
            else
            {
                // No expansion apps - hide expansion if exists
                if (_appExpansion != null && _appExpansion.AppSlotCount > 0)
                {
                    _appExpansion.ClearAllAppSlots();
                }
            }

            // Update visual states for active app
            UpdateAllAppButtonColors();
            _miniFrame.MarkDirty();
        }

        private void ClearSlotIcon(GameObject slot)
        {
            for (int i = slot.transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(slot.transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(slot.transform.GetChild(i).gameObject);
            }
        }

        private void CreateAppExpansion()
        {
            if (_appExpansion != null) return;

            RTTToolbar toolbar = RTTToolbar.Instance;
            if (toolbar == null)
            {
                Debug.LogWarning("[RTTTaskbar] RTTToolbar not found, cannot create App expansion");
                return;
            }

            GameObject expansionObj = new GameObject("RTTAppExpansion");
            expansionObj.transform.SetParent(toolbar.transform, false);

            _appExpansion = expansionObj.AddComponent<RTTTaskbarExpansion>();

            // Force initialize immediately so canvas is ready before we register app slots
            // (otherwise Start() runs later and canvas won't exist when ShowExpansion() is called)
            _appExpansion.ForceInitialize();

            // Subscribe to events
            _appExpansion.OnAppSlotClicked += OnAppExpansionSlotClicked;
            _appExpansion.OnDismissed += OnAppExpansionDismissed;

            Debug.Log("[RTTTaskbar] App expansion panel created");
        }

        /// <summary>
        /// Get the world position of Section 2's center (for expansion alignment).
        /// </summary>
        public Vector3 GetSection2CenterWorldPosition()
        {
            if (_miniFrame == null) return Vector3.zero;

            // Get Section 2's center X offset from frame center (in world units)
            float xOffset = _miniFrame.GetSection2CenterXOffset();

            // Get the display quad's world position (frame center)
            // The display quad is the visual representation of RTTMiniFrame
            Transform displayQuad = _miniFrame.transform.Find("DisplayQuad");
            Vector3 frameCenter = displayQuad != null ? displayQuad.position : _miniFrame.transform.position;

            // Apply the offset in the frame's local X direction
            Vector3 offsetWorld = _miniFrame.transform.right * xOffset;

            return frameCenter + offsetWorld;
        }

        private void OnAppExpansionSlotClicked(int slotIndex)
        {
            Debug.Log($"[RTTTaskbar] App expansion slot {slotIndex} clicked");

            // Update taskbar's active slot index
            _activeAppSlotIndex = slotIndex;

            // Clear main taskbar visual selection (including Home)
            ClearMainTaskbarVisualSelection();

            _miniFrame.MarkDirty();
        }

        private void OnAppExpansionDismissed()
        {
            Debug.Log("[RTTTaskbar] App expansion dismissed");
        }

        private void ClearMainTaskbarVisualSelection()
        {
            // Deselect all main taskbar buttons visually (including Home at index 0)
            Color cyanColor = new Color(0f, 0.9f, 1f);
            for (int i = 0; i < _appButtons.Count; i++)
            {
                var btn = _appButtons[i];
                if (btn != null)
                {
                    SetBareIconButtonColor(btn, cyanColor);
                    SetAppButtonInteractable(btn, true);
                }
            }
        }

        public void UnregisterApp(int slotIndex)
        {
            if (slotIndex < 1)
                return;

            // Remove from registered apps list
            int removedIndex = _registeredApps.FindIndex(a => a.SlotIndex == slotIndex);
            if (removedIndex >= 0)
            {
                _registeredApps.RemoveAt(removedIndex);
            }

            _appSlotCallbacks.Remove(slotIndex);

            // If active app was removed, switch to Home
            if (_activeAppSlotIndex == slotIndex)
            {
                _activeAppSlotIndex = 0;
                RTTManager appManager = RTTManager.Instance;
                if (appManager != null)
                {
                    appManager.SwitchToHome();
                }
            }

            // Re-render all app buttons (this shifts remaining apps left)
            RenderAllAppButtons();

            Debug.Log($"[RTTTaskbar] Unregistered app slot {slotIndex}, remaining apps: {_registeredApps.Count}");
        }

        public void SelectSlot(int slotIndex)
        {
            Debug.Log($"[RTTTaskbar] SelectSlot({slotIndex}) called, current active: {_activeAppSlotIndex}");

            if (slotIndex == 0)
            {
                // Select Home
                SelectAppButton(0);
                return;
            }

            // Find visual position of this slot index
            int visualIndex = _registeredApps.FindIndex(a => a.SlotIndex == slotIndex);
            if (visualIndex < 0)
            {
                Debug.LogWarning($"[RTTTaskbar] Slot {slotIndex} not found in registered apps");
                return;
            }

            int mainCapacity = MainTaskbarAppCapacity;

            if (visualIndex < mainCapacity)
            {
                // App is in main taskbar
                SelectAppButton(slotIndex);
            }
            else
            {
                // App is in expansion
                _activeAppSlotIndex = slotIndex;
                ClearMainTaskbarVisualSelection();
                if (_appExpansion != null)
                {
                    _appExpansion.SelectAppSlot(slotIndex);
                }
                _miniFrame.MarkDirty();
            }
        }

        private void SetSlotIcon(GameObject slot, Sprite icon, int slotIndex)
        {
            Debug.Log($"[RTTTaskbar] SetSlotIcon - slot={slot.name}, icon={(icon != null ? icon.name : "NULL")}, slotIndex={slotIndex}");

            int oldChildCount = slot.transform.childCount;
            for (int i = slot.transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                    Destroy(slot.transform.GetChild(i).gameObject);
                else
                    DestroyImmediate(slot.transform.GetChild(i).gameObject);
            }
            Debug.Log($"[RTTTaskbar] SetSlotIcon - removed {oldChildCount} old children");

            if (icon == null)
            {
                Debug.LogWarning($"[RTTTaskbar] SetSlotIcon - icon is NULL, returning early");
                return;
            }

            Color cyanColor = new Color(0f, 0.9f, 1f);
            float buttonSize = _miniFrame.ButtonSize;

            var iconBtn = VRButtonFactory.CreateBareIconButton(
                slot.transform, buttonSize, icon, cyanColor,
                () =>
                {
                    // Always call SelectAppButton - it will invoke callback and handle selection
                    SelectAppButton(slotIndex);
                    _miniFrame.MarkDirty();
                },
                0.05f, 0.6f
            );
            iconBtn.transform.SetAsFirstSibling();

            SetLayerRecursively(iconBtn, LayerMask.NameToLayer("UI"));

            Debug.Log($"[RTTTaskbar] SetSlotIcon - created iconBtn={iconBtn.name}, slot.childCount now={slot.transform.childCount}");

            Color purpleColor = new Color(0.9f, 0.3f, 1f);
            if (slotIndex == _activeAppSlotIndex)
            {
                SetBareIconButtonColor(slot, purpleColor);
                SetAppButtonInteractable(slot, false);
            }
        }

        private void HandleHomeClick()
        {
            RTTManager appManager = RTTManager.Instance;
            if (appManager != null)
            {
                appManager.SwitchToHome();
            }
            else
            {
                SelectAppButton(0);
            }
            _miniFrame.MarkDirty();
        }

        private void RewireHomeButton(GameObject homeBtn)
        {
            var button = homeBtn.GetComponentInChildren<UnityEngine.UI.Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    HandleHomeClick();
                });
            }
        }
        #endregion

        #region Quit Confirmation
        private void CreateQuitConfirmPopup()
        {
            if (_quitConfirmPopup != null) return;

            // Get the primary menu frame to center popup on it
            RTTMenuFrame primaryFrame = RTTMenuFrame.PrimaryInstance;
            Transform parentTransform = primaryFrame != null ? primaryFrame.transform : _miniFrame.transform;

            Color primaryColor = new Color(0f, 0.9f, 1f);
            Color accentColor = new Color(0.9f, 0.3f, 1f);

            var config = new RTTPopupMenu.PopupConfig
            {
                width = 550f,
                buttonHeight = 66f,
                sideSpacing = 26f,
                rowSpacing = 16f,
                labelHeight = 52f,
                labelFontSize = 32,
                fontSize = 25,
                borderWidth = 0.05f,
                primaryColor = primaryColor,
                accentColor = accentColor,
                overlayColor = new Color(0f, 0f, 0f, 0.4f),
                layerName = "VirtualObjects",
                buttonBorderWidth = 0.04f,
                buttonGlowWidth = 0.08f,
                buttonGlowIntensity = 4f,
                buttonCornerRadius = 0.12f
            };

            _quitConfirmPopup = RTTPopupMenu.CreateWorldSpace(config, parentTransform);
        }

        private void ShowQuitConfirmPopup()
        {
            if (_quitConfirmPopup == null)
            {
                CreateQuitConfirmPopup();
            }

            _quitConfirmPopup.Clear();

            // Add title
            _quitConfirmPopup.AddSectionBlock("Quit application?", new List<RTTPopupMenu.ButtonData>());

            Color accentColor = new Color(0.9f, 0.3f, 1f);
            Color primaryColor = new Color(0f, 0.9f, 1f);

            var yesButton = new RTTPopupMenu.ButtonData(
                "Yes",
                OnQuitConfirmed,
                null,
                false,
                accentColor
            );

            var noButton = new RTTPopupMenu.ButtonData(
                "No",
                OnQuitCancelled,
                null,
                false,
                primaryColor
            );

            _quitConfirmPopup.AddSectionBlock("", new List<RTTPopupMenu.ButtonData> { yesButton, noButton }, 2);

            _quitConfirmPopup.Build();
            _quitConfirmPopup.Show();
        }

        private void OnQuitConfirmed()
        {
            Debug.Log("[RTTTaskbar] Quit confirmed");

            if (_quitConfirmPopup != null)
            {
                _quitConfirmPopup.Hide();
            }

    #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
    #else
            Application.Quit();
    #endif
        }

        private void OnQuitCancelled()
        {
            Debug.Log("[RTTTaskbar] Quit cancelled");

            if (_quitConfirmPopup != null)
            {
                _quitConfirmPopup.Hide();
            }
        }
        #endregion

        #region Icons
        private void LoadIcons()
        {
            if (iconQuit == null) iconQuit = LoadIcon("quit");
            if (iconSettings == null) iconSettings = LoadIcon("settings");
            if (iconEye == null) iconEye = LoadIcon("eye");
            if (iconEyeClose == null) iconEyeClose = LoadIcon("eye_close");
            if (iconRecenter == null) iconRecenter = LoadIcon("recenter");
            if (iconHome == null) iconHome = LoadIcon("home");

            Debug.Log($"[RTTTaskbar] Icons loaded - Quit:{iconQuit != null}, Settings:{iconSettings != null}, Eye:{iconEye != null}, EyeClose:{iconEyeClose != null}, Recenter:{iconRecenter != null}, Home:{iconHome != null}");
        }

        private static HashSet<string> _warnedIcons = new HashSet<string>();

        public static Sprite LoadIcon(string name)
        {
            var sprite = Resources.Load<Sprite>($"icon_{name}");
            if (sprite == null && !_warnedIcons.Contains(name))
            {
                Debug.LogWarning($"[RTTTaskbar] Failed to load icon: icon_{name} from Resources");
                _warnedIcons.Add(name);
            }
            return sprite;
        }
        #endregion
    }

}
