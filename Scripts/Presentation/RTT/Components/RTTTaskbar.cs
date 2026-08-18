using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.VRInput;
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

        /// <summary>
        /// Reset static singleton at the start of each Play session.
        /// RTTTaskbar assigns _instance in Start() (not Awake), which means a ghost
        /// reference from the previous Play session would survive and block the
        /// duplicate-destroy guard during the 2nd Play onwards.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticInstance()
        {
            _instance = null;
        }
        #endregion

        #region Configuration
        [Header("Style Resources")]
        [SerializeField] private Sprite iconQuit;
        [SerializeField] private Sprite iconSettings;
        [SerializeField] private Sprite iconLightOn;
        [SerializeField] private Sprite iconLightOff;
        [SerializeField] private Sprite iconRecenter;
        [SerializeField] private Sprite iconHome;
        #endregion

        #region Private Fields
        private RTTMiniFrame _miniFrame;
        private GameObject _lightButton;
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

        private bool _isLightOn = true; // Default ON
        private MediaEnvironmentController _environmentController;

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
            _environmentController = MediaEnvironmentController.Instance;
            if (_environmentController != null)
            {
                _environmentController.Initialize();
                _environmentController.OnLightsChanged += HandleLightsChanged;
                _isLightOn = _environmentController.LightsEnabled;
                UpdateLightButtonVisual();
            }

            // Mark dirty to re-render
            _miniFrame.MarkDirty();
        }

        private void HandleLightsChanged(bool isOn)
        {
            if (_isLightOn == isOn) return;

            _isLightOn = isOn;
            UpdateLightButtonVisual();

            _miniFrame.MarkDirty();
            Debug.Log($"[RTTTaskbar] Light state synchronized to: {(isOn ? "ON" : "OFF")}");
        }

        private void ToggleLights()
        {
            _environmentController?.ToggleLights();
        }

        private void OnDestroy()
        {
            if (_environmentController != null)
            {
                _environmentController.OnLightsChanged -= HandleLightsChanged;
            }

            if (_instance == this) _instance = null;

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

            // Light toggle
            _lightButton = CreateIconButton(section1, iconLightOn, "Light", cyanColor, buttonSize, ToggleLights);

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
        /// Update the direct light toggle from the authoritative environment state.
        /// </summary>
        private void UpdateLightButtonVisual()
        {
            if (_lightButton == null) return;

            Color cyanColor = new Color(0f, 0.9f, 1f);
            Color purpleColor = new Color(0.9f, 0.3f, 1f);

            bool isNonDefault = !_isLightOn;
            Color targetColor = isNonDefault ? purpleColor : cyanColor;
            Sprite targetIcon = _isLightOn ? iconLightOn : iconLightOff;

            Transform iconTransform = _lightButton.transform.Find("HitArea/Visuals/Content/Icon");
            if (iconTransform != null)
            {
                Image iconImg = iconTransform.GetComponent<Image>();
                if (iconImg != null)
                {
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

            var hoverController = _lightButton.GetComponentInChildren<HoverEffectController>();
            if (hoverController != null)
            {
                hoverController.SetForceHover(isNonDefault);
            }
        }

        public void SetLight(bool isOn)
        {
            _environmentController?.SetLightsEnabled(isOn);
        }

        private void RecenterObject()
        {
            Debug.Log("[RTTTaskbar] Recenter clicked");
            Transform fallback = _miniFrame != null ? _miniFrame.GetFollowTarget() : transform;
            StartCoroutine(VirtualObjectsRecenter.RunWithReticleProgress(
                this, fallback, iconRecenter, () => _miniFrame?.MarkDirty()));
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
            if (iconLightOn == null) iconLightOn = LoadIcon("light_on");
            if (iconLightOff == null) iconLightOff = LoadIcon("light_off");
            if (iconRecenter == null) iconRecenter = LoadIcon("recenter");
            if (iconHome == null) iconHome = LoadIcon("home");

            Debug.Log($"[RTTTaskbar] Icons loaded - Quit:{iconQuit != null}, Settings:{iconSettings != null}, LightOn:{iconLightOn != null}, LightOff:{iconLightOff != null}, Recenter:{iconRecenter != null}, Home:{iconHome != null}");
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
