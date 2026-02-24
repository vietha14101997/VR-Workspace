using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using System;
using System.Collections;
using System.Runtime.InteropServices;
using TMPro;

using TouchPhase = UnityEngine.TouchPhase;

namespace VRWorkspace.VRInput
{
    /// <summary>
    /// Controls Non-VR mode: mono rendering, virtual joystick camera control,
    /// virtual action buttons (A = instant click), and disables dwell auto-click.
    ///
    /// VR toggle button rendered via Cardboard native widget API (same rendering level
    /// as SDK's gear/close buttons) when XR is active, and via Canvas overlay when in
    /// non-VR mono mode.
    /// </summary>
    public class NonVRModeController : MonoBehaviour
    {
        #region Singleton
        public static NonVRModeController Instance { get; private set; }
        #endregion

        #region State
        public bool IsNonVRMode { get; private set; } = false;
        public event Action<bool> OnModeChanged; // true = non-VR
        #endregion

        #region Config
        private const float DEFAULT_FOV = 60f;
        private const float ROTATION_SPEED = 120f; // degrees/second at full stick
        private const float PITCH_CLAMP = 80f;

        // Joystick sizing (in reference resolution units)
        private const float JOYSTICK_OUTER_RADIUS = 100f;
        private const float JOYSTICK_INNER_RADIUS = 35f;
        private const float JOYSTICK_DEAD_ZONE = 0.1f;

        // Button sizing
        private const float BTN_A_SIZE = 130f;
        private const float BTN_SMALL_SIZE = 85f;
        private const float BTN_SPACING = 20f;

        // Colors
        private static readonly Color BTN_BG = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color BTN_BG_PRESSED = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color BTN_BORDER = new Color(1f, 1f, 1f, 0.4f);

        // Native widget sizing (matches Cardboard SDK Widget.cs)
        private const int BUTTON_SIZE_DP = 42;
        private const int BUTTON_PADDING_DP = 9;

        // Canvas toggle button sizing
        private const float CANVAS_TOGGLE_SIZE = 70f;
        #endregion

        #region UI References (Canvas — non-VR mode only)
        private Canvas _overlayCanvas;
        private GameObject _joystickArea;
        private GameObject _buttonsArea;
        private RectTransform _joystickThumb;
        private RectTransform _joystickOuter;
        private GameObject _canvasToggleBtn;
        #endregion

        #region Camera Control
        private float _cameraPitch = 0f;
        private float _cameraYaw = 0f;
        private Vector2 _joystickInput;
        private int _joystickTouchId = -1;
        private Vector2 _joystickCenter;
        private float _joystickRadiusPx;
        #endregion

        #region Sprites (cached for Canvas UI)
        private static Sprite _circleSprite;
        private static Sprite _ringSprite;
        #endregion

        #region Native Widget
        private Texture2D _vrToggleTexture;
        private bool _nativeWidgetActive;
        #endregion

        #region Native Cardboard Widget API
    #if UNITY_ANDROID && !UNITY_EDITOR
        [DllImport("GfxPluginCardboard")]
        private static extern void CardboardUnity_setWidgetCount(int count);

        [DllImport("GfxPluginCardboard")]
        private static extern void CardboardUnity_setWidgetParams(
            int i, IntPtr texture, int x, int y, int width, int height);
    #endif
        #endregion

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _vrToggleTexture = Resources.Load<Texture2D>("icon_cardboard");
            if (_vrToggleTexture == null)
                Debug.LogError("[NonVRMode] icon_cardboard not found in Resources!");

            // Disable mouse device on Android only (not in Editor).
            // This prevents mouse emulation from interfering with touch/gaze UI.
    #if UNITY_ANDROID && !UNITY_EDITOR
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                UnityEngine.InputSystem.InputSystem.DisableDevice(mouse);
                Debug.Log("[NonVRMode] Mouse device disabled on Android.");
            }
    #endif

            CreateOverlayUI();
        }

        private void Update()
        {
            if (IsNonVRMode)
            {
                ProcessJoystickInput();
            }

            CheckVRToggleTouch();
        }

        private void LateUpdate()
        {
            if (IsNonVRMode)
            {
                ApplyCameraRotation();
            }

            UpdateNativeWidget();
        }

        #region Mode Switching
        public void ToggleVRMode()
        {
            if (IsNonVRMode)
                StartCoroutine(EnterVRModeRoutine());
            else
                ExitVRMode();
        }

        private void ExitVRMode()
        {
            Debug.Log("[NonVRMode] Exiting VR mode...");

            // 1. Stop XR subsystems → mono rendering
            var xrManager = XRGeneralSettings.Instance?.Manager;
            if (xrManager != null && xrManager.isInitializationComplete)
            {
                xrManager.StopSubsystems();
                xrManager.DeinitializeLoader();
                Debug.Log("[NonVRMode] XR stopped and deinitialized.");
            }

            // 2. Reset camera
            var cam = Camera.main;
            if (cam != null)
            {
                cam.ResetAspect();
                cam.fieldOfView = DEFAULT_FOV;

                // 3. Disable TrackedPoseDriver
                var tpd = cam.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                if (tpd != null) tpd.enabled = false;

                // 4. Capture current camera rotation as starting point
                Vector3 euler = cam.transform.eulerAngles;
                _cameraYaw = euler.y;
                _cameraPitch = euler.x;
                if (_cameraPitch > 180f) _cameraPitch -= 360f;
            }

            // 5. Disable dwell click & head stabilization
            var reticle = VRGazeReticle.Instance;
            if (reticle != null)
            {
                reticle.dwellClickEnabled = false;
                reticle.headStabilizationEnabled = false;
            }

            // 6. Show virtual controls
            if (_joystickArea != null) _joystickArea.SetActive(true);
            if (_buttonsArea != null) _buttonsArea.SetActive(true);
            if (_canvasToggleBtn != null) _canvasToggleBtn.SetActive(true);

            _nativeWidgetActive = false;
            IsNonVRMode = true;
            OnModeChanged?.Invoke(true);
            Debug.Log("[NonVRMode] Non-VR mode active.");
        }

        private IEnumerator EnterVRModeRoutine()
        {
            Debug.Log("[NonVRMode] Entering VR mode...");

            // 1. Start XR subsystems
            var xrManager = XRGeneralSettings.Instance?.Manager;
            if (xrManager != null)
            {
                yield return xrManager.InitializeLoader();

                if (xrManager.activeLoader != null)
                {
                    xrManager.StartSubsystems();
                    Debug.Log("[NonVRMode] XR started.");
                }
                else
                {
                    Debug.LogError("[NonVRMode] Failed to initialize XR loader.");
                    yield break;
                }
            }

            // 2. Re-enable TrackedPoseDriver
            var cam = Camera.main;
            if (cam != null)
            {
                var tpd = cam.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                if (tpd != null) tpd.enabled = true;
            }

            // 3. Re-enable dwell click & head stabilization
            var reticle = VRGazeReticle.Instance;
            if (reticle != null)
            {
                reticle.dwellClickEnabled = true;
                reticle.headStabilizationEnabled = true;
            }

            // 4. Hide virtual controls
            if (_joystickArea != null) _joystickArea.SetActive(false);
            if (_buttonsArea != null) _buttonsArea.SetActive(false);
            if (_canvasToggleBtn != null) _canvasToggleBtn.SetActive(false);

            // 5. Reset joystick state
            _joystickInput = Vector2.zero;
            _joystickTouchId = -1;

            _nativeWidgetActive = true;
            IsNonVRMode = false;
            OnModeChanged?.Invoke(false);
            Debug.Log("[NonVRMode] VR mode active.");
        }
        #endregion

        #region Joystick Input
        private void ProcessJoystickInput()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);

                // New touch on left side of screen → start joystick
                if (touch.phase == TouchPhase.Began && touch.position.x < Screen.width * 0.35f
                    && touch.position.y < Screen.height * 0.6f)
                {
                    if (_joystickTouchId == -1)
                    {
                        _joystickTouchId = touch.fingerId;
                        _joystickCenter = touch.position;
                        PositionJoystickAt(touch.position);
                    }
                }

                if (touch.fingerId == _joystickTouchId)
                {
                    if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                    {
                        Vector2 delta = touch.position - _joystickCenter;
                        _joystickInput = Vector2.ClampMagnitude(delta / _joystickRadiusPx, 1f);

                        if (_joystickInput.magnitude < JOYSTICK_DEAD_ZONE)
                            _joystickInput = Vector2.zero;

                        UpdateJoystickThumb();
                    }

                    if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        _joystickInput = Vector2.zero;
                        _joystickTouchId = -1;

                        if (_joystickThumb != null)
                            _joystickThumb.anchoredPosition = Vector2.zero;
                    }
                }
            }
        }

        private void ApplyCameraRotation()
        {
            var cam = Camera.main;
            if (cam == null) return;

            _cameraYaw += _joystickInput.x * ROTATION_SPEED * Time.deltaTime;
            _cameraPitch -= _joystickInput.y * ROTATION_SPEED * Time.deltaTime;
            _cameraPitch = Mathf.Clamp(_cameraPitch, -PITCH_CLAMP, PITCH_CLAMP);

            cam.transform.rotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
        }

        private void PositionJoystickAt(Vector2 screenPos)
        {
            if (_joystickOuter == null || _overlayCanvas == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _overlayCanvas.GetComponent<RectTransform>(),
                screenPos, null, out Vector2 canvasPos);

            _joystickOuter.anchoredPosition = canvasPos;
        }

        private void UpdateJoystickThumb()
        {
            if (_joystickThumb == null) return;
            _joystickThumb.anchoredPosition = _joystickInput * JOYSTICK_OUTER_RADIUS;
        }
        #endregion

        #region Native Widget (VR mode — renders inside Cardboard compositor)

        private static int DpToPixels(int dp)
        {
            float dpi = Screen.dpi > 0 ? Screen.dpi : 160f;
            return (int)(dpi / 160f * dp);
        }

        /// <summary>
        /// Clickable rect in full-screen coordinates (bottom-right, symmetric with gear at top-right).
        /// </summary>
        private RectInt GetVRToggleButtonRect()
        {
            int buttonSize = DpToPixels(BUTTON_SIZE_DP);
            return new RectInt(
                (int)Screen.safeArea.xMax - buttonSize,  // Right edge (same X as gear)
                (int)Screen.safeArea.yMin,                // Bottom edge (gear is at yMax)
                buttonSize,
                buttonSize
            );
        }

        /// <summary>
        /// Render rect in safe-area-relative coordinates (with padding inset).
        /// </summary>
        private RectInt GetVRToggleRenderRect()
        {
            RectInt rect = GetVRToggleButtonRect();
            // Translate to safe area frame (same as Widget.cs TranslateToSafeAreaFrame)
            rect.x -= (int)Screen.safeArea.xMin;
            rect.y -= (int)Screen.safeArea.yMin;
            // Apply padding
            int padding = DpToPixels(BUTTON_PADDING_DP);
            rect.xMin += padding;
            rect.xMax -= padding;
            rect.yMin += padding;
            rect.yMax -= padding;
            return rect;
        }

        private void UpdateNativeWidget()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            bool xrActive = XRGeneralSettings.Instance?.Manager?.isInitializationComplete == true;

            if (xrActive && _vrToggleTexture != null)
            {
                RectInt renderRect = GetVRToggleRenderRect();
                // SDK sets widget count to 3 in RecalculateRectangles.
                // We override to 4 and add our widget as #3.
                // This runs in LateUpdate (after Update where SDK recalculates).
                // Rendering happens after LateUpdate, so it sees count=4.
                CardboardUnity_setWidgetCount(4);
                CardboardUnity_setWidgetParams(
                    3,
                    _vrToggleTexture.GetNativeTexturePtr(),
                    renderRect.x, renderRect.y, renderRect.width, renderRect.height);
                _nativeWidgetActive = true;
            }
            else
            {
                _nativeWidgetActive = false;
            }
    #endif
        }

        private void CheckVRToggleTouch()
        {
            // Only check touch for native widget in VR mode.
            // In non-VR mode, Canvas Button component handles it.
            if (IsNonVRMode) return;

    #if UNITY_ANDROID && !UNITY_EDITOR
            if (!_nativeWidgetActive) return;

            // Use new Input System (same as Cardboard SDK's Api.IsGearButtonPressed)
            var touchScreen = UnityEngine.InputSystem.Touchscreen.current;
            if (touchScreen == null || !touchScreen.enabled) return;

            var touches = touchScreen.touches;
            if (touches.Count == 0) return;

            var touch = touches[0];
            if (touch.phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.Began) return;

            Vector2Int touchPos = Vector2Int.RoundToInt(touch.position.ReadValue());
            RectInt clickRect = GetVRToggleButtonRect();

            if (clickRect.Contains(touchPos))
            {
                ToggleVRMode();
            }
    #endif
        }
        #endregion

        #region UI Creation (Canvas — joystick & action buttons for non-VR mode)
        private void CreateOverlayUI()
        {
            var canvasGO = new GameObject("NonVROverlayCanvas");
            canvasGO.transform.SetParent(transform);
            _overlayCanvas = canvasGO.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 9999;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            _joystickRadiusPx = JOYSTICK_OUTER_RADIUS * (Screen.width / 1920f);

            EnsureEventSystem();

            CreateJoystick(canvasGO.transform);
            CreateActionButtons(canvasGO.transform);
            CreateCanvasToggleButton(canvasGO.transform);

            _joystickArea.SetActive(false);
            _buttonsArea.SetActive(false);
            _canvasToggleBtn.SetActive(false);
        }

        private void CreateCanvasToggleButton(Transform parent)
        {
            _canvasToggleBtn = new GameObject("VRToggleBtn");
            _canvasToggleBtn.transform.SetParent(parent, false);

            var rt = _canvasToggleBtn.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(CANVAS_TOGGLE_SIZE, CANVAS_TOGGLE_SIZE);
            rt.anchoredPosition = new Vector2(-10f, 10f);

            // Background circle
            var bg = _canvasToggleBtn.AddComponent<Image>();
            bg.sprite = GetCircleSprite();
            bg.color = new Color(0f, 0f, 0f, 0.5f);
            bg.raycastTarget = true;

            // Icon
            if (_vrToggleTexture != null)
            {
                var iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(_canvasToggleBtn.transform, false);
                var iconRT = iconGO.AddComponent<RectTransform>();
                iconRT.anchorMin = new Vector2(0.15f, 0.15f);
                iconRT.anchorMax = new Vector2(0.85f, 0.85f);
                iconRT.offsetMin = Vector2.zero;
                iconRT.offsetMax = Vector2.zero;

                var iconImg = iconGO.AddComponent<RawImage>();
                iconImg.texture = _vrToggleTexture;
                iconImg.color = Color.white;
                iconImg.raycastTarget = false;
            }

            var button = _canvasToggleBtn.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => ToggleVRMode());
        }

        private void CreateJoystick(Transform parent)
        {
            _joystickArea = new GameObject("JoystickArea");
            _joystickArea.transform.SetParent(parent, false);

            var areaRT = _joystickArea.AddComponent<RectTransform>();
            areaRT.anchorMin = new Vector2(0f, 0f);
            areaRT.anchorMax = new Vector2(0f, 0f);
            areaRT.pivot = new Vector2(0f, 0f);
            areaRT.sizeDelta = new Vector2(JOYSTICK_OUTER_RADIUS * 3f, JOYSTICK_OUTER_RADIUS * 3f);
            areaRT.anchoredPosition = new Vector2(30f, 80f);

            // Outer ring
            GameObject outerObj = new GameObject("JoystickOuter");
            outerObj.transform.SetParent(_joystickArea.transform, false);
            _joystickOuter = outerObj.AddComponent<RectTransform>();
            _joystickOuter.anchorMin = new Vector2(0.5f, 0.5f);
            _joystickOuter.anchorMax = new Vector2(0.5f, 0.5f);
            _joystickOuter.pivot = new Vector2(0.5f, 0.5f);
            _joystickOuter.sizeDelta = new Vector2(JOYSTICK_OUTER_RADIUS * 2f, JOYSTICK_OUTER_RADIUS * 2f);

            var outerImg = outerObj.AddComponent<Image>();
            outerImg.sprite = GetRingSprite();
            outerImg.color = new Color(1f, 1f, 1f, 0.25f);
            outerImg.raycastTarget = false;

            // Inner thumb
            GameObject thumbObj = new GameObject("JoystickThumb");
            thumbObj.transform.SetParent(outerObj.transform, false);
            _joystickThumb = thumbObj.AddComponent<RectTransform>();
            _joystickThumb.anchorMin = new Vector2(0.5f, 0.5f);
            _joystickThumb.anchorMax = new Vector2(0.5f, 0.5f);
            _joystickThumb.pivot = new Vector2(0.5f, 0.5f);
            _joystickThumb.sizeDelta = new Vector2(JOYSTICK_INNER_RADIUS * 2f, JOYSTICK_INNER_RADIUS * 2f);

            var thumbImg = thumbObj.AddComponent<Image>();
            thumbImg.sprite = GetCircleSprite();
            thumbImg.color = new Color(1f, 1f, 1f, 0.5f);
            thumbImg.raycastTarget = false;
        }

        private void CreateActionButtons(Transform parent)
        {
            _buttonsArea = new GameObject("ButtonsArea");
            _buttonsArea.transform.SetParent(parent, false);

            var areaRT = _buttonsArea.AddComponent<RectTransform>();
            areaRT.anchorMin = new Vector2(1f, 0f);
            areaRT.anchorMax = new Vector2(1f, 0f);
            areaRT.pivot = new Vector2(1f, 0f);
            areaRT.sizeDelta = new Vector2(200f, 500f);
            areaRT.anchoredPosition = new Vector2(-10f, 100f);

            float yOffset = 0f;

            // Button A (large) — Instant Click
            CreateActionButton(_buttonsArea.transform, "A", BTN_A_SIZE, yOffset,
                () => VRGazeReticle.Instance?.PerformInstantClick());
            yOffset += BTN_A_SIZE + BTN_SPACING;

            // Button B — Back/Recenter
            CreateActionButton(_buttonsArea.transform, "B", BTN_SMALL_SIZE, yOffset,
                () => { _cameraPitch = 0f; });
            yOffset += BTN_SMALL_SIZE + BTN_SPACING;

            // Button G — Reserved for grip/grab
            CreateActionButton(_buttonsArea.transform, "G", BTN_SMALL_SIZE, yOffset,
                () => Debug.Log("[NonVRMode] G button pressed (reserved)"));
        }

        private void CreateActionButton(Transform parent, string label, float size, float yOffset, Action onClick)
        {
            GameObject btnObj = new GameObject($"Btn_{label}");
            btnObj.transform.SetParent(parent, false);

            var rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(0f, yOffset);

            var bg = btnObj.AddComponent<Image>();
            bg.sprite = GetCircleSprite();
            bg.color = BTN_BG;
            bg.raycastTarget = true;

            GameObject ringObj = new GameObject("Ring");
            ringObj.transform.SetParent(btnObj.transform, false);
            var ringRT = ringObj.AddComponent<RectTransform>();
            ringRT.anchorMin = Vector2.zero;
            ringRT.anchorMax = Vector2.one;
            ringRT.offsetMin = Vector2.zero;
            ringRT.offsetMax = Vector2.zero;
            var ringImg = ringObj.AddComponent<Image>();
            ringImg.sprite = GetRingSprite();
            ringImg.color = BTN_BORDER;
            ringImg.raycastTarget = false;

            var button = btnObj.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.None;

            Image capturedBg = bg;
            button.onClick.AddListener(() =>
            {
                onClick?.Invoke();
                StartCoroutine(ButtonPressFeedback(capturedBg, btnObj.transform));
            });

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            var textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = size * 0.35f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }

        private IEnumerator ButtonPressFeedback(Image bg, Transform btnTransform)
        {
            Color originalColor = bg.color;
            Vector3 originalScale = btnTransform.localScale;

            bg.color = BTN_BG_PRESSED;
            btnTransform.localScale = originalScale * 0.9f;

            yield return new WaitForSeconds(0.1f);

            bg.color = originalColor;
            btnTransform.localScale = originalScale;
        }

        private void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;

            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
        #endregion

        #region Sprite Generation (for Canvas UI elements)
        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;

            int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = size / 2f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01(radius - dist);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }

        private static Sprite GetRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;

            int size = 128;
            float ringWidth = 4f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float outerRadius = size / 2f - 1f;
            float innerRadius = outerRadius - ringWidth;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float outerAlpha = Mathf.Clamp01(outerRadius - dist);
                    float innerAlpha = Mathf.Clamp01(dist - innerRadius);
                    float alpha = Mathf.Min(outerAlpha, innerAlpha);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _ringSprite;
        }
        #endregion
    }

}
