using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VRWorkspace.VRInput
{
    using VRWorkspace.Media.UI;
    using VRWorkspace.UI.Components;
    using VRWorkspace.UI.RTT.Components;
    using VRWorkspace.UI.RTT.Input;
    #if UNITY_EDITOR
    using UnityEditor;
    #endif

    public partial class VRGazeReticle : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Kích thước visual (ảo) của chấm tại khoảng cách 1m.")]
        public float reticleSize = 0.005f;

        public Color colorInteract = new Color(1f, 0f, 0f, 1f);

        [Header("RTT Integration")]
        [Tooltip("Enable RTT (Render-to-Texture) raycast for RTT panels")]
        public bool useRTTRaycast = true;

        [Header("Dwell Click Settings")]
        [Tooltip("Thời gian phải giữ yên reticle trước khi bắt đầu đếm click (giây)")]
        public float dwellStartDelay = 0.5f;

        [Tooltip("Thời gian đếm ngược để click sau khi bắt đầu dwell (giây)")]
        public float dwellClickTime = 1.0f;

        [Tooltip("Ngưỡng di chuyển tối đa (góc độ) để coi là đứng yên")]
        public float dwellMovementThreshold = 2.0f;

        [Tooltip("Bật/tắt tính năng Dwell Click")]
        public bool dwellClickEnabled = true;

        [Header("Head Stabilization")]
        [Tooltip("Bật/tắt tính năng ổn định đầu (giảm rung lắc)")]
        public bool headStabilizationEnabled = true;

        [Tooltip("Hệ số smoothing khi đứng yên (thấp = ít noise hơn, camera ổn định hơn)")]
        [Range(0.01f, 0.15f)]
        public float stillSmoothingFactor = 0.05f;

        [Tooltip("Hệ số smoothing khi quay đầu (cao = responsive hơn)")]
        [Range(0.5f, 0.99f)]
        public float movingSmoothingFactor = 0.90f;

        [Tooltip("Ngưỡng vận tốc góc (độ/giây) coi là 'đứng yên'")]
        [Range(0.5f, 10f)]
        public float stillThreshold = 1.5f;

        [Tooltip("Ngưỡng vận tốc góc (độ/giây) coi là 'quay nhanh'")]
        [Range(10f, 60f)]
        public float fastThreshold = 25f;

        [Header("Dead Zone")]
        [Tooltip("Ngưỡng dead zone (độ/giây) - chuyển động dưới mức này bị bỏ qua hoàn toàn. Khi đặt máy xuống bàn, gyro vẫn dao động ~1-2°/s do MEMS bias → đặt 2.0-3.0 để chống drift")]
        [Range(0.1f, 3f)]
        public float deadZoneThreshold = 2.5f;

        [Tooltip("Ngưỡng để thoát khỏi trạng thái khóa (độ/giây). Phải > deadZoneThreshold để tạo hysteresis, tránh dao động LOCKED/MOVING khi gyro dao động quanh biên")]
        [Range(0.5f, 10f)]
        public float unlockThreshold = 5.0f;

        [Header("Compass Yaw Correction")]
        [Tooltip("Bật/tắt chỉnh yaw drift bằng compass (cần thiết vì Cardboard XR không dùng compass)")]
        public bool compassCorrectionEnabled = true;

        [Tooltip("Cường độ chỉnh yaw theo compass (thấp = mượt hơn, ít giật)")]
        [Range(0.005f, 0.1f)]
        public float compassCorrectionStrength = 0.02f;

        [Tooltip("Hệ số lọc low-pass cho compass heading (thấp = lọc mạnh, ít nhiễu)")]
        [Range(0.005f, 0.1f)]
        public float compassFilterAlpha = 0.02f;

        private Image _reticleImage;
        private Camera _cam;
        private RectTransform _canvasRT;
        private int _layerMask;

        // Custom cursor support
        private Sprite _defaultSprite;
        private Sprite _currentCustomSprite;

        // Recenter State
        private bool _isRecentering = false;
        private GameObject _recenterGroup;
        private Image _recenterBg;
        private Image _recenterIcon;
        private Image _recenterRing;
        private float _recenterDistance = 2.0f; // Distance from camera

        // State tracking for Hover events
        private GameObject _currentHitObj;
        private PointerEventData _pointerData;

        // Dwell Click State
        private Vector3 _lastGazeDirection;
        private float _stableTime = 0f;
        private float _dwellProgress = 0f;
        private bool _isDwelling = false;
        private bool _dwellClickTriggered = false;
        private Image _dwellRing;
        private GameObject _dwellableTarget;
        private RaycastHit _lastHit;

        // RTT State
        private RTTHitResult _lastRTTHit;

        // Blocked target tracking (for popup close-on-click)
        private GameObject _blockedDwellTarget;

        // Last clicked button tracking (prevents continuous clicking same button)
        private GameObject _lastClickedButton;

        // RTT click cooldown (prevents rapid clicks when popup rebuilds)
        private float _lastRTTClickTime;
        private const float kRTTClickCooldown = 0.3f;

        // Head Stabilization State
        private Quaternion _stabilizedRotation;
        private Vector3 _previousEuler;
        private float _currentSmoothingFactor;
        private bool _stabilizationInitialized = false;

        // Dead zone lock state
        private Quaternion _lockedRotation;
        private bool _isLocked = false;

        // Compass yaw correction state
        private bool _compassInitialized = false;
        private float _filteredCompassHeading = 0f;
        private float _compassYawOffset = 0f;

        // Singleton access helper (optional, or use FindObjectOfType)
        public static VRGazeReticle Instance { get; private set; }

        /// <summary>
        /// Reset static singleton at the start of each Play session.
        /// Without this, Instance keeps a ghost reference to a destroyed object
        /// on the 2nd Play onwards (Unity doesn't reset static fields on Play exit).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticInstance()
        {
            Instance = null;
        }

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;

            // Tìm Layer "VirtualObjects"
            int layerIndex = LayerMask.NameToLayer("VirtualObjects");
            if (layerIndex != -1)
            {
                _layerMask = 1 << layerIndex;
            }
            else
            {
                Debug.LogWarning("[VRGazeReticle] Layer 'VirtualObjects' not found! Reticle won't work correctly.");
                _layerMask = 0;
            }

            CreateReticle();
            _pointerData = new PointerEventData(EventSystem.current);
            _lastGazeDirection = _cam.transform.forward;

            // Initialize head stabilization (dead zone + smoothing + compass yaw correction)
            InitializeStabilization();

            // Bật compass cho yaw correction (Cardboard XR không dùng compass nên yaw sẽ drift nếu thiếu)
            if (compassCorrectionEnabled)
            {
                Input.compass.enabled = true;
            }
        }

        void Update()
        {
            if (_isRecentering)
            {
                UpdateRecenterPosition();
            }
            else
            {
                CheckGaze();
            }
        }

        void CheckGaze()
        {
            // Reset scale canvas về chuẩn vì RecenterMode có thể đã đổi nó
            if (_canvasRT.localScale != Vector3.one) _canvasRT.localScale = Vector3.one;

            Ray ray = new Ray(_cam.transform.position, _cam.transform.forward);
            Vector3 currentGazeDir = _cam.transform.forward;

            // Priority check: If there's an open world-space dropdown, check physics raycast first
            // The world-space dropdown is positioned in front of RTT panels, so physics raycast takes priority
            if (VRDropdown.CurrentlyOpenDropdown != null && VRDropdown.CurrentlyOpenDropdown.IsOpen)
            {
                RaycastHit worldDropdownHit;
                if (_layerMask != 0 && Physics.Raycast(ray, out worldDropdownHit, 100.0f, _layerMask))
                {
                    GameObject hitObj = worldDropdownHit.collider.gameObject;

                    // Check if this hit is part of the open dropdown's world-space panel
                    if (VRDropdown.CurrentlyOpenDropdown.IsPartOfDropdownPanel(hitObj))
                    {
                        // Process as standard physics hit for the dropdown
                        if (!_reticleImage.enabled) _reticleImage.enabled = true;

                        float dist = worldDropdownHit.distance;
                        if (dist < _cam.nearClipPlane) dist = _cam.nearClipPlane + 0.05f;
                        _canvasRT.localPosition = new Vector3(0, 0, dist);

                        float scale = (reticleSize / 100f) * dist;
                        float finalScale = scale * _customCursorScaleMultiplier;
                        _reticleImage.rectTransform.localScale = new Vector3(finalScale, finalScale, 1f);

                        if (_dwellRing != null)
                        {
                            _dwellRing.rectTransform.localScale = new Vector3(finalScale, finalScale, 1f);
                        }

                        if (_currentHitObj != hitObj)
                        {
                            HandlePointerExit(_currentHitObj);
                            HandlePointerEnter(hitObj);
                            _currentHitObj = hitObj;
                            ResetDwellState();
                        }

                        _lastHit = worldDropdownHit;

                        if (dwellClickEnabled && _currentHitObj != null)
                        {
                            ProcessDwellClick(currentGazeDir, hitObj, worldDropdownHit);
                        }

                        _lastGazeDirection = currentGazeDir;
                        return;
                    }
                }
            }

            // Try RTT raycast first if enabled
            if (useRTTRaycast && RTTRaycastManager.Instance != null)
            {
                _lastRTTHit = RTTRaycastManager.Instance.Raycast(ray);

                if (_lastRTTHit.isValid)
                {
                    if (!_reticleImage.enabled) _reticleImage.enabled = true;

                    float dist = _lastRTTHit.distance;
                    if (dist < _cam.nearClipPlane) dist = _cam.nearClipPlane + 0.05f;
                    _canvasRT.localPosition = new Vector3(0, 0, dist);

                    float scale = (reticleSize / 100f) * dist;
                    float finalScale = scale * _customCursorScaleMultiplier;
                    _reticleImage.rectTransform.localScale = new Vector3(finalScale, finalScale, 1f);

                    // Dwell ring scales with cursor (stays proportional to cursor size)
                    if (_dwellRing != null)
                    {
                        _dwellRing.rectTransform.localScale = new Vector3(finalScale, finalScale, 1f);
                    }

                    // Handle hover state changes for RTT
                    GameObject hitObj = _lastRTTHit.hitUIElement;

                    // Check if popup is open - block hover on objects outside popup
                    bool hasOpenPopup = RTTPopupMenu.CurrentlyOpenPopup != null;
                    bool isInsidePopup = hasOpenPopup && RTTPopupMenu.CurrentlyOpenPopup.IsPartOfPopupPanel(hitObj);
                    bool blockInteraction = hasOpenPopup && !isInsidePopup;

                    if (blockInteraction)
                    {
                        // Popup is open, target is outside popup
                        // Block hover but track target for dwell-to-close
                        if (_currentHitObj != null)
                        {
                            HandlePointerExit(_currentHitObj);
                            _currentHitObj = null;
                        }

                        // Only reset dwell if blocked target changed
                        if (_blockedDwellTarget != hitObj)
                        {
                            _blockedDwellTarget = hitObj;
                            ResetDwellState();
                        }
                    }
                    else
                    {
                        // Normal hover processing (no popup or inside popup)
                        _blockedDwellTarget = null;

                        if (_currentHitObj != hitObj)
                        {
                            // Exit old object (both RTT and non-RTT)
                            if (_currentHitObj != null)
                            {
                                HandlePointerExit(_currentHitObj);
                            }

                            _currentHitObj = hitObj;
                            ResetDwellState();
                        }
                    }

                    // Process dwell click for RTT
                    if (dwellClickEnabled && hitObj != null)
                    {
                        // If popup is open and clicking outside, only close popup (don't process normal click)
                        if (blockInteraction)
                        {
                            ProcessPopupCloseOnlyRTT(currentGazeDir);
                        }
                        else
                        {
                            ProcessDwellClickRTT(currentGazeDir);
                        }
                    }

                    _lastGazeDirection = currentGazeDir;
                    return;
                }
            }

            // Fallback to standard physics raycast
            RaycastHit hit;

            if (_layerMask != 0 && Physics.Raycast(ray, out hit, 100.0f, _layerMask))
            {
                if (!_reticleImage.enabled) _reticleImage.enabled = true;

                float dist = hit.distance;
                if (dist < _cam.nearClipPlane) dist = _cam.nearClipPlane + 0.05f;
                _canvasRT.localPosition = new Vector3(0, 0, dist);

                float scale = (reticleSize / 100f) * dist;
                float finalScale = scale * _customCursorScaleMultiplier;
                _reticleImage.rectTransform.localScale = new Vector3(finalScale, finalScale, 1f);

                // Dwell ring scales with cursor (stays proportional to cursor size)
                if (_dwellRing != null)
                {
                    _dwellRing.rectTransform.localScale = new Vector3(finalScale, finalScale, 1f);
                }

                GameObject hitObj = hit.collider.gameObject;

                // Check if popup is open - block hover on objects outside popup
                bool hasOpenPopup = RTTPopupMenu.CurrentlyOpenPopup != null;
                bool isInsidePopup = hasOpenPopup && RTTPopupMenu.CurrentlyOpenPopup.IsPartOfPopupPanel(hitObj);
                bool blockInteraction = hasOpenPopup && !isInsidePopup;

                if (blockInteraction)
                {
                    // Popup is open, target is outside popup
                    // Block hover but track target for dwell-to-close
                    if (_currentHitObj != null)
                    {
                        HandlePointerExit(_currentHitObj);
                        _currentHitObj = null;
                    }

                    // Only reset dwell if blocked target changed
                    if (_blockedDwellTarget != hitObj)
                    {
                        _blockedDwellTarget = hitObj;
                        ResetDwellState();
                    }
                }
                else
                {
                    // Normal hover processing (no popup or inside popup)
                    _blockedDwellTarget = null;

                    if (_currentHitObj != hitObj)
                    {
                        HandlePointerExit(_currentHitObj);
                        HandlePointerEnter(hitObj);
                        _currentHitObj = hitObj;
                        ResetDwellState();
                    }
                }

                // Lưu hit info để sử dụng khi click
                _lastHit = hit;

                // Xử lý Dwell Click
                if (dwellClickEnabled && hitObj != null)
                {
                    // If popup is open and clicking outside, only close popup
                    if (blockInteraction)
                    {
                        ProcessPopupCloseOnly(currentGazeDir);
                    }
                    else
                    {
                        ProcessDwellClick(currentGazeDir, hitObj, hit);
                    }
                }
            }
            else
            {
                if (_reticleImage.enabled) _reticleImage.enabled = false;

                if (_currentHitObj != null)
                {
                    HandlePointerExit(_currentHitObj);
                    _currentHitObj = null;
                }
                _blockedDwellTarget = null;
                ResetDwellState();
            }

            _lastGazeDirection = currentGazeDir;
        }

        /// <summary>
        /// Perform instant click on whatever the reticle is currently pointing at.
        /// Used by non-VR mode virtual A button to bypass dwell timer.
        /// </summary>
        public void PerformInstantClick()
        {
            // RTT panel hit takes priority
            if (useRTTRaycast && _lastRTTHit.isValid && _lastRTTHit.hitUIElement != null)
            {
                SendRTTClickAndLock(_lastRTTHit.hitUIElement);
                return;
            }

            // World object hit
            if (_currentHitObj != null)
            {
                HandlePointerClick(_currentHitObj);
            }
        }

        /// <summary>
        /// Force-reset dwell state and clear current hit target.
        /// Call after programmatic UI dismissal (e.g. dropdown close) to prevent
        /// phantom dwell clicks on stale targets.
        /// </summary>
        public void ForceResetDwellState()
        {
            if (_currentHitObj != null)
            {
                HandlePointerExit(_currentHitObj);
                _currentHitObj = null;
            }
            _lastClickedButton = null;
            _blockedDwellTarget = null;
            ResetDwellState();
        }

        /// <summary>
        /// Send RTT click and remember the button to prevent continuous clicking
        /// </summary>
        void SendRTTClickAndLock(GameObject target)
        {
            if (RTTRaycastManager.Instance != null)
            {
                RTTRaycastManager.Instance.SendClick();
            }

            // Remember clicked button - will be cleared when reticle exits any button
            _lastClickedButton = target;

            // Record click time to prevent rapid clicks when popup rebuilds
            _lastRTTClickTime = Time.time;
        }

        /// <summary>
        /// Process dwell click for RTT panels
        /// </summary>
        void ProcessDwellClickRTT(Vector3 currentGazeDir)
        {
            if (!_lastRTTHit.isValid || _lastRTTHit.hitUIElement == null)
            {
                ResetDwellState();
                return;
            }

            GameObject target = _lastRTTHit.hitUIElement;

            // Skip if same button was just clicked (must move reticle away first)
            if (_lastClickedButton != null && target == _lastClickedButton)
            {
                return;
            }

            // Skip if within cooldown period (prevents rapid clicks when popup rebuilds with new buttons)
            if (Time.time - _lastRTTClickTime < kRTTClickCooldown)
            {
                return;
            }

            // Check if dropdown is open - allow dwell on ANY object to close it
            bool hasOpenDropdown = VRDropdown.CurrentlyOpenDropdown != null;
            bool isDropdownOption = hasOpenDropdown && VRDropdown.CurrentlyOpenDropdown.IsPartOfDropdownPanel(target);

            // Check if expansion panel is open
            bool hasOpenExpansion = RTTTaskbarExpansion.CurrentlyOpenExpansion != null;
            bool isExpansionOption = hasOpenExpansion && RTTTaskbarExpansion.CurrentlyOpenExpansion.IsPartOfExpansionPanel(target);

            // Check if popup menu is open
            bool hasOpenPopup = RTTPopupMenu.CurrentlyOpenPopup != null;
            bool isPopupOption = hasOpenPopup && RTTPopupMenu.CurrentlyOpenPopup.IsPartOfPopupPanel(target);

            // Check if RTT keyboard is open
            bool hasOpenKeyboard = RTTMobileKeyboard.CurrentlyOpenKeyboard != null;
            bool isKeyboardPart = hasOpenKeyboard && RTTMobileKeyboard.CurrentlyOpenKeyboard.IsPartOfKeyboard(target);

            // Check if target is an InputField
            VRInputFieldTrigger inputFieldTrigger = target.GetComponent<VRInputFieldTrigger>();
            if (inputFieldTrigger == null) inputFieldTrigger = target.GetComponentInParent<VRInputFieldTrigger>();

            // Check if target is a Dropdown
            VRDropdown targetDropdown = target.GetComponent<VRDropdown>();
            if (targetDropdown == null) targetDropdown = target.GetComponentInParent<VRDropdown>();

            // Check if target is dwellable
            bool isDwellableTarget = IsDwellable(target);

            // If no dropdown/keyboard/expansion/popup open and target is not dwellable, skip
            if (!hasOpenDropdown && !hasOpenKeyboard && !hasOpenExpansion && !hasOpenPopup && !isDwellableTarget)
            {
                ResetDwellState();
                return;
            }

            // If keyboard is open and target is keyboard background (not a button), skip dwell
            if (hasOpenKeyboard && isKeyboardPart && !isDwellableTarget)
            {
                ResetDwellState();
                return;
            }

            // If popup is open and target is popup background (not a button), skip dwell
            if (hasOpenPopup && isPopupOption && !isDwellableTarget)
            {
                ResetDwellState();
                return;
            }

            // Calculate angle moved
            float angleMoved = Vector3.Angle(_lastGazeDirection, currentGazeDir);

            // Reset if moved too much
            if (angleMoved > dwellMovementThreshold * Time.deltaTime * 10f)
            {
                ResetDwellState();
                return;
            }

            // Already clicked
            if (_dwellClickTriggered)
            {
                return;
            }

            // Accumulate stable time
            _stableTime += Time.deltaTime;

            // Phase 1: Wait for delay
            if (_stableTime < dwellStartDelay)
            {
                return;
            }

            // Phase 2: Show progress ring
            if (!_isDwelling)
            {
                _isDwelling = true;
                _dwellableTarget = target;
                if (_dwellRing != null)
                {
                    _dwellRing.enabled = true;
                    _dwellRing.fillAmount = 0f;
                }
            }

            // Calculate progress
            float dwellElapsed = _stableTime - dwellStartDelay;
            _dwellProgress = Mathf.Clamp01(dwellElapsed / dwellClickTime);

            // Update visual
            if (_dwellRing != null)
            {
                _dwellRing.fillAmount = _dwellProgress;
            }

            // Phase 3: Click when done
            if (_dwellProgress >= 1f)
            {
                _dwellClickTriggered = true;

                if (_dwellRing != null)
                {
                    _dwellRing.enabled = false;
                }

                // Priority 1: Handle keyboard click-outside
                if (hasOpenKeyboard && !isKeyboardPart)
                {
                    // Clicking outside keyboard
                    if (inputFieldTrigger != null && inputFieldTrigger.InputField != null)
                    {
                        var currentTarget = RTTMobileKeyboard.CurrentlyOpenKeyboard.GetTargetInputField();
                        if (inputFieldTrigger.InputField != currentTarget)
                        {
                            // Clicking a DIFFERENT InputField - switch keyboard target first
                            RTTMobileKeyboard.CurrentlyOpenKeyboard.SwitchToInputField(inputFieldTrigger.InputField);
                            // Continue to SendClick below to set caret position via OnPointerClick
                        }
                        // Clicking InputField - let SendClick happen to set caret position
                    }
                    else if (targetDropdown != null)
                    {
                        // Clicking a dropdown - close keyboard and open dropdown
                        RTTMobileKeyboard.CurrentlyOpenKeyboard.Hide();
                        targetDropdown.OpenDropdown();
                        // Mark RTT panel dirty for re-render
                        _lastRTTHit.panel?.MarkDirty();
                        return;
                    }
                    else
                    {
                        // Clicking on other functional object (button) - close keyboard first
                        RTTMobileKeyboard.CurrentlyOpenKeyboard.Hide();
                        // Continue to perform the button click below
                    }
                }

                // Priority 2: Handle dropdown click-outside
                if (hasOpenDropdown)
                {
                    if (isDropdownOption)
                    {
                        // Target is a dropdown option - perform normal click via RTTRaycastManager
                        SendRTTClickAndLock(target);
                    }
                    else if (targetDropdown != null && targetDropdown != VRDropdown.CurrentlyOpenDropdown)
                    {
                        // Clicking another dropdown - close current and open new one
                        VRDropdown.CurrentlyOpenDropdown.CloseDropdown();
                        targetDropdown.OpenDropdown();
                        // Mark RTT panel dirty for re-render
                        _lastRTTHit.panel?.MarkDirty();
                    }
                    else
                    {
                        // Target is NOT part of the dropdown - close dropdown instead of clicking
                        VRDropdown.CurrentlyOpenDropdown.CloseDropdown();
                        // Mark RTT panel dirty for re-render
                        _lastRTTHit.panel?.MarkDirty();
                    }
                    return;
                }

                // Priority 3: Handle expansion panel click-outside
                if (hasOpenExpansion)
                {
                    if (isExpansionOption)
                    {
                        // Target is an expansion option - perform normal click via RTTRaycastManager
                        SendRTTClickAndLock(target);
                    }
                    else if (isDwellableTarget)
                    {
                        // Target is a dwellable button (could be expansion trigger) - perform click
                        // The button's click handler will decide whether to toggle/switch expansion
                        SendRTTClickAndLock(target);
                    }
                    else
                    {
                        // Target is NOT part of the expansion and not a button - close expansion panel
                        RTTTaskbarExpansion.CurrentlyOpenExpansion.Hide();
                        // Mark RTT panel dirty for re-render
                        _lastRTTHit.panel?.MarkDirty();
                    }
                    return;
                }

                // Priority 4: Handle popup menu click-outside
                if (hasOpenPopup)
                {
                    if (isPopupOption && isDwellableTarget)
                    {
                        // Target is part of popup and dwellable - perform normal click via RTTRaycastManager
                        SendRTTClickAndLock(target);
                    }
                    else if (isPopupOption)
                    {
                        // Target is part of popup but NOT dwellable (e.g., selected button) - do nothing
                        return;
                    }
                    else if (isDwellableTarget)
                    {
                        // Target is a dwellable button outside popup - close popup and perform click
                        RTTPopupMenu.CurrentlyOpenPopup.Hide();
                        SendRTTClickAndLock(target);
                        // Mark RTT panel dirty for re-render
                        _lastRTTHit.panel?.MarkDirty();
                    }
                    else
                    {
                        // Target is NOT part of the popup and not a button - close popup
                        RTTPopupMenu.CurrentlyOpenPopup.Hide();
                        // Mark RTT panel dirty for re-render
                        _lastRTTHit.panel?.MarkDirty();
                    }
                    return;
                }

                // Normal click - use RTTRaycastManager to send click
                SendRTTClickAndLock(target);
            }
        }

        /// <summary>
        /// Special dwell handler when popup is open - ONLY closes popup, no other actions
        /// </summary>
        void ProcessPopupCloseOnlyRTT(Vector3 currentGazeDir)
        {
            // Calculate angle moved
            float angleMoved = Vector3.Angle(_lastGazeDirection, currentGazeDir);

            // Reset if moved too much
            if (angleMoved > dwellMovementThreshold * Time.deltaTime * 10f)
            {
                ResetDwellState();
                return;
            }

            // Already clicked
            if (_dwellClickTriggered)
            {
                return;
            }

            // Accumulate stable time
            _stableTime += Time.deltaTime;

            // Phase 1: Wait for delay
            if (_stableTime < dwellStartDelay)
            {
                return;
            }

            // Phase 2: Show progress ring
            if (!_isDwelling)
            {
                _isDwelling = true;
                if (_dwellRing != null)
                {
                    _dwellRing.enabled = true;
                    _dwellRing.fillAmount = 0f;
                }
            }

            // Calculate progress
            float dwellElapsed = _stableTime - dwellStartDelay;
            _dwellProgress = Mathf.Clamp01(dwellElapsed / dwellClickTime);

            // Update visual
            if (_dwellRing != null)
            {
                _dwellRing.fillAmount = _dwellProgress;
            }

            // Phase 3: Close popup when done (NO other action)
            if (_dwellProgress >= 1f)
            {
                _dwellClickTriggered = true;

                if (_dwellRing != null)
                {
                    _dwellRing.enabled = false;
                }

                // Only close popup - do NOT send click to any object
                RTTPopupMenu.CurrentlyOpenPopup?.Hide();
                _lastRTTHit.panel?.MarkDirty();
            }
        }

        /// <summary>
        /// Special dwell handler for physics raycast when popup is open - ONLY closes popup
        /// </summary>
        void ProcessPopupCloseOnly(Vector3 currentGazeDir)
        {
            // Calculate angle moved
            float angleMoved = Vector3.Angle(_lastGazeDirection, currentGazeDir);

            // Reset if moved too much
            if (angleMoved > dwellMovementThreshold * Time.deltaTime * 10f)
            {
                ResetDwellState();
                return;
            }

            // Already clicked
            if (_dwellClickTriggered)
            {
                return;
            }

            // Accumulate stable time
            _stableTime += Time.deltaTime;

            // Phase 1: Wait for delay
            if (_stableTime < dwellStartDelay)
            {
                return;
            }

            // Phase 2: Show progress ring
            if (!_isDwelling)
            {
                _isDwelling = true;
                if (_dwellRing != null)
                {
                    _dwellRing.enabled = true;
                    _dwellRing.fillAmount = 0f;
                }
            }

            // Calculate progress
            float dwellElapsed = _stableTime - dwellStartDelay;
            _dwellProgress = Mathf.Clamp01(dwellElapsed / dwellClickTime);

            // Update visual
            if (_dwellRing != null)
            {
                _dwellRing.fillAmount = _dwellProgress;
            }

            // Phase 3: Close popup when done (NO other action)
            if (_dwellProgress >= 1f)
            {
                _dwellClickTriggered = true;

                if (_dwellRing != null)
                {
                    _dwellRing.enabled = false;
                }

                // Only close popup - do NOT click any object
                RTTPopupMenu.CurrentlyOpenPopup?.Hide();
            }
        }

        void ProcessDwellClick(Vector3 currentGazeDir, GameObject target, RaycastHit hit)
        {
            // Skip if same button was just clicked (must move reticle away first)
            if (_lastClickedButton != null && target == _lastClickedButton)
            {
                return;
            }

            // Check if dropdown is open - allow dwell on ANY object to close it
            bool hasOpenDropdown = VRDropdown.CurrentlyOpenDropdown != null;
            bool isDropdownOption = hasOpenDropdown && VRDropdown.CurrentlyOpenDropdown.IsPartOfDropdownPanel(target);

            // Check if expansion panel is open
            bool hasOpenExpansion = RTTTaskbarExpansion.CurrentlyOpenExpansion != null;
            bool isExpansionOption = hasOpenExpansion && RTTTaskbarExpansion.CurrentlyOpenExpansion.IsPartOfExpansionPanel(target);

            // Check if popup menu is open
            bool hasOpenPopup = RTTPopupMenu.CurrentlyOpenPopup != null;
            bool isPopupOption = hasOpenPopup && RTTPopupMenu.CurrentlyOpenPopup.IsPartOfPopupPanel(target);

            // Check if keyboard is open (RTTMobileKeyboard only)
            bool hasOpenKeyboard = RTTMobileKeyboard.CurrentlyOpenKeyboard != null;
            bool isKeyboardPart = hasOpenKeyboard && RTTMobileKeyboard.CurrentlyOpenKeyboard.IsPartOfKeyboard(target);

            // Check if target is an InputField
            VRInputFieldTrigger inputFieldTrigger = target.GetComponent<VRInputFieldTrigger>();
            if (inputFieldTrigger == null) inputFieldTrigger = target.GetComponentInParent<VRInputFieldTrigger>();

            // Check if target is a Dropdown
            VRDropdown targetDropdown = target.GetComponent<VRDropdown>();
            if (targetDropdown == null) targetDropdown = target.GetComponentInParent<VRDropdown>();

            bool isDwellableTarget = IsDwellable(target);

            // If no dropdown/keyboard/expansion/popup open and target is not dwellable, skip
            if (!hasOpenDropdown && !hasOpenKeyboard && !hasOpenExpansion && !hasOpenPopup && !isDwellableTarget)
            {
                ResetDwellState();
                return;
            }

            // If keyboard is open and target is keyboard background (not a button), skip dwell
            if (hasOpenKeyboard && isKeyboardPart && !isDwellableTarget)
            {
                ResetDwellState();
                return;
            }

            // If popup is open and target is popup background (not a button), skip dwell
            if (hasOpenPopup && isPopupOption && !isDwellableTarget)
            {
                ResetDwellState();
                return;
            }

            // Tính góc di chuyển từ frame trước
            float angleMoved = Vector3.Angle(_lastGazeDirection, currentGazeDir);

            // Nếu di chuyển quá nhiều, reset
            if (angleMoved > dwellMovementThreshold * Time.deltaTime * 10f)
            {
                ResetDwellState();
                return;
            }

            // Đã click rồi thì không click lại cho đến khi rời target
            if (_dwellClickTriggered)
            {
                return;
            }

            // Tích lũy thời gian đứng yên
            _stableTime += Time.deltaTime;

            // Phase 1: Chờ đủ thời gian delay trước khi bắt đầu hiển thị progress
            if (_stableTime < dwellStartDelay)
            {
                return;
            }

            // Phase 2: Bắt đầu hiển thị progress ring
            if (!_isDwelling)
            {
                _isDwelling = true;
                _dwellableTarget = target;
                if (_dwellRing != null)
                {
                    _dwellRing.enabled = true;
                    _dwellRing.fillAmount = 0f;
                }
            }

            // Tính progress (từ 0 đến 1)
            float dwellElapsed = _stableTime - dwellStartDelay;
            _dwellProgress = Mathf.Clamp01(dwellElapsed / dwellClickTime);

            // Cập nhật visual
            if (_dwellRing != null)
            {
                _dwellRing.fillAmount = _dwellProgress;
            }

            // Phase 3: Click khi đủ thời gian
            if (_dwellProgress >= 1f)
            {
                _dwellClickTriggered = true;

                // Ẩn ring ngay sau khi click
                if (_dwellRing != null)
                {
                    _dwellRing.enabled = false;
                }

                Vector2 normalizedHitPoint = CalculateNormalizedHitPoint(hit);

                // Priority 1: Handle keyboard click-outside
                if (hasOpenKeyboard && !isKeyboardPart)
                {
                    // Clicking outside keyboard
                    if (inputFieldTrigger != null && inputFieldTrigger.InputField != null)
                    {
                        // Clicking another InputField - switch keyboard target
                        RTTMobileKeyboard.CurrentlyOpenKeyboard.SwitchToInputField(inputFieldTrigger.InputField);
                    }
                    else if (targetDropdown != null)
                    {
                        // Clicking a dropdown - close keyboard and open dropdown
                        RTTMobileKeyboard.CurrentlyOpenKeyboard.Hide();
                        targetDropdown.OpenDropdown();
                    }
                    else
                    {
                        // Clicking elsewhere - close keyboard
                        RTTMobileKeyboard.CurrentlyOpenKeyboard.Hide();
                    }
                    return;
                }

                // Priority 2: Handle dropdown click-outside
                if (hasOpenDropdown)
                {
                    if (isDropdownOption)
                    {
                        // Target is a dropdown option - perform normal click
                        HandlePointerClick(target, normalizedHitPoint);
                    }
                    else if (targetDropdown != null && targetDropdown != VRDropdown.CurrentlyOpenDropdown)
                    {
                        // Clicking another dropdown - close current and open new one
                        VRDropdown.CurrentlyOpenDropdown.CloseDropdown();
                        targetDropdown.OpenDropdown();
                    }
                    else
                    {
                        // Target is NOT part of the dropdown - close dropdown instead of clicking
                        VRDropdown.CurrentlyOpenDropdown.CloseDropdown();
                    }
                    return;
                }

                // Priority 3: Handle expansion panel click-outside
                if (hasOpenExpansion)
                {
                    if (isExpansionOption)
                    {
                        // Target is an expansion option - perform normal click
                        HandlePointerClick(target, normalizedHitPoint);
                    }
                    else if (isDwellableTarget)
                    {
                        // Target is a dwellable button (could be expansion trigger) - perform click
                        // The button's click handler will decide whether to toggle/switch expansion
                        HandlePointerClick(target, normalizedHitPoint);
                    }
                    else
                    {
                        // Target is NOT part of the expansion and not a button - close expansion panel
                        RTTTaskbarExpansion.CurrentlyOpenExpansion.Hide();
                    }
                    return;
                }

                // Priority 4: Handle popup menu click-outside
                if (hasOpenPopup)
                {
                    if (isPopupOption)
                    {
                        // Target is part of popup - perform normal click
                        HandlePointerClick(target, normalizedHitPoint);
                    }
                    else if (isDwellableTarget)
                    {
                        // Target is a dwellable button outside popup - close popup and perform click
                        RTTPopupMenu.CurrentlyOpenPopup.Hide();
                        HandlePointerClick(target, normalizedHitPoint);
                    }
                    else
                    {
                        // Target is NOT part of the popup and not a button - close popup
                        RTTPopupMenu.CurrentlyOpenPopup.Hide();
                    }
                    return;
                }

                // No popup open - perform normal click
                if (isDwellableTarget)
                {
                    HandlePointerClick(target, normalizedHitPoint);
                }
            }
        }

        Vector2 CalculateNormalizedHitPoint(RaycastHit hit)
        {
            // Sử dụng ray từ camera để tính điểm giao với mặt phẳng của button
            // Điều này chính xác hơn hit.point vì hit.point có thể ở trên bề mặt z của collider

            Transform buttonTransform = hit.transform;

            // Tìm Visuals để lấy RectTransform chính xác
            Transform visuals = buttonTransform.Find("Visuals");
            RectTransform rectTransform = null;

            if (visuals != null)
            {
                rectTransform = visuals.GetComponent<RectTransform>();
            }

            if (rectTransform == null)
            {
                rectTransform = buttonTransform.GetComponent<RectTransform>();
            }

            if (rectTransform != null)
            {
                // Tạo ray từ camera
                Ray gazeRay = new Ray(_cam.transform.position, _cam.transform.forward);

                // Tạo plane từ RectTransform
                // Sử dụng -forward (hướng về phía camera) để đảm bảo raycast hoạt động
                // với buttons ở mọi hướng (kể cả buttons bên lề)
                Vector3 planeNormal = -rectTransform.forward;
                Plane buttonPlane = new Plane(planeNormal, rectTransform.position);

                float distance;
                if (buttonPlane.Raycast(gazeRay, out distance))
                {
                    // Điểm giao trên mặt phẳng
                    Vector3 worldPoint = gazeRay.GetPoint(distance);

                    // Convert sang local space của RectTransform
                    Vector3 localPoint = rectTransform.InverseTransformPoint(worldPoint);

                    // Lấy rect bounds
                    Rect rect = rectTransform.rect;

                    // Tính normalized position (0-1)
                    float normalizedX = (localPoint.x - rect.x) / rect.width;
                    float normalizedY = (localPoint.y - rect.y) / rect.height;

                    return new Vector2(
                        Mathf.Clamp01(normalizedX),
                        Mathf.Clamp01(normalizedY)
                    );
                }
                else
                {
                    // Fallback: nếu plane raycast thất bại, sử dụng hit.point trực tiếp
                    Vector3 localPoint = rectTransform.InverseTransformPoint(hit.point);
                    Rect rect = rectTransform.rect;

                    float normalizedX = (localPoint.x - rect.x) / rect.width;
                    float normalizedY = (localPoint.y - rect.y) / rect.height;

                    return new Vector2(
                        Mathf.Clamp01(normalizedX),
                        Mathf.Clamp01(normalizedY)
                    );
                }
            }

            // Fallback với BoxCollider - sử dụng x, y từ hit point
            BoxCollider boxCol = hit.collider as BoxCollider;
            if (boxCol != null)
            {
                Vector3 localHitPoint = hit.transform.InverseTransformPoint(hit.point);
                Vector3 size = boxCol.size;

                // Tính normalized dựa trên x, y (bỏ qua z)
                float normalizedX = (localHitPoint.x + size.x / 2f) / size.x;
                float normalizedY = (localHitPoint.y + size.y / 2f) / size.y;

                return new Vector2(
                    Mathf.Clamp01(normalizedX),
                    Mathf.Clamp01(normalizedY)
                );
            }

            // Fallback: trả về trung tâm
            return new Vector2(0.5f, 0.5f);
        }

        bool IsDwellable(GameObject obj)
        {
            if (obj == null) return false;

            // Check if button has VRButtonClickLock and is locked
            VRButtonClickLock clickLock = VRButtonClickLock.FindOnButton(obj);
            if (clickLock != null && clickLock.IsLocked) return false;

            // Check if button is already selected (block dwell on active options)
            VRSelectedButton selectedBtn = obj.GetComponent<VRSelectedButton>();
            if (selectedBtn == null) selectedBtn = obj.GetComponentInParent<VRSelectedButton>();
            if (selectedBtn != null && selectedBtn.IsSelected) return false;

            // Check if this is a selected grid/list item
            RTTMediaGridItem mediaGridItem = obj.GetComponentInParent<RTTMediaGridItem>();
            if (mediaGridItem != null && mediaGridItem.IsItemSelected) return false;

            RTTFileGridItem fileGridItem = obj.GetComponentInParent<RTTFileGridItem>();
            if (fileGridItem != null && fileGridItem.IsItemSelected) return false;

            RTTFileListItem fileListItem = obj.GetComponentInParent<RTTFileListItem>();
            if (fileListItem != null && fileListItem.IsItemSelected) return false;

            // Kiểm tra có Button hoặc IPointerClickHandler không
            Button btn = obj.GetComponentInParent<Button>();
            if (btn != null && btn.interactable) return true;

            // Kiểm tra Selectable (base class cho Button, InputField, Dropdown, etc.)
            // Nếu không interactable thì không cho dwell click
            Selectable selectable = obj.GetComponentInParent<Selectable>();
            if (selectable != null && !selectable.interactable) return false;

            IPointerClickHandler clickHandler = obj.GetComponentInParent<IPointerClickHandler>();
            if (clickHandler != null) return true;

            return false;
        }

        void ResetDwellState()
        {
            _stableTime = 0f;
            _dwellProgress = 0f;
            _isDwelling = false;
            _dwellClickTriggered = false;
            _dwellableTarget = null;
            // Note: Don't reset _blockedDwellTarget here - it's managed separately in CheckGaze

            if (_dwellRing != null)
            {
                _dwellRing.enabled = false;
                _dwellRing.fillAmount = 0f;
                _dwellRing.color = colorInteract; // Reset về màu reticle
            }
        }

        void HandlePointerEnter(GameObject obj)
        {
            if (obj == null) return;
            ExecuteEvents.Execute(obj, _pointerData, ExecuteEvents.pointerEnterHandler);

            Selectable selectable = obj.GetComponentInParent<Selectable>();
            if (selectable) selectable.OnPointerEnter(_pointerData);
        }

        void HandlePointerExit(GameObject obj)
        {
            if (obj == null) return;
            ExecuteEvents.Execute(obj, _pointerData, ExecuteEvents.pointerExitHandler);

            Selectable selectable = obj.GetComponentInParent<Selectable>();
            if (selectable) selectable.OnPointerExit(_pointerData);

            // Clear last clicked button when exiting any button - allows clicking again
            _lastClickedButton = null;
        }

        void HandlePointerClick(GameObject obj)
        {
            HandlePointerClick(obj, new Vector2(0.5f, 0.5f));
        }

        void HandlePointerClick(GameObject obj, Vector2 normalizedHitPoint)
        {
            if (obj == null) return;

            // Tìm Button để trigger click
            Button btn = obj.GetComponentInParent<Button>();
            GameObject target = btn != null ? btn.gameObject : obj;

            // Fire PointerDown first (for EventTrigger animations like keyboard keys)
            ExecuteEvents.Execute(target, _pointerData, ExecuteEvents.pointerDownHandler);

            // ExecuteEvents.Execute với pointerClickHandler sẽ:
            // 1. Gọi VRButtonAnimation.OnPointerClick -> TriggerFlash
            // 2. Gọi Button.OnPointerClick -> Press() -> onClick.Invoke()
            // Nên không cần gọi btn.onClick.Invoke() riêng nữa
            ExecuteEvents.Execute(target, _pointerData, ExecuteEvents.pointerClickHandler);

            // Fire PointerUp after click (for EventTrigger animations)
            ExecuteEvents.Execute(target, _pointerData, ExecuteEvents.pointerUpHandler);

            // Remember clicked button - will be cleared when reticle exits any button
            _lastClickedButton = obj;
        }

        #region Head Stabilization

        /// <summary>
        /// Approach: Trust Cardboard XR (TrackedPoseDriver) for base rotation.
        /// Add only: Dead Zone + Adaptive Smoothing + Compass Yaw Correction.
        /// Compass is needed because Cardboard XR does NOT use magnetometer,
        /// so yaw will drift over time without an absolute reference.
        /// </summary>
        void InitializeStabilization()
        {
            if (_cam != null && headStabilizationEnabled)
            {
                _stabilizedRotation = _cam.transform.rotation;
                _lockedRotation = _stabilizedRotation;
                _previousEuler = _cam.transform.eulerAngles;
                _currentSmoothingFactor = stillSmoothingFactor;
                _isLocked = false;
                _stabilizationInitialized = true;
            }
        }

        void LateUpdate()
        {
            if (headStabilizationEnabled && _stabilizationInitialized && !_isRecentering)
            {
                ApplyHeadStabilization();
            }
        }

        void ApplyHeadStabilization()
        {
            if (_cam == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Cardboard XR (TrackedPoseDriver) đã set rotation trước LateUpdate
            Quaternion rawRotation = _cam.transform.rotation;
            Vector3 currentEuler = rawRotation.eulerAngles;
            Vector3 deltaEuler = DeltaAngles(_previousEuler, currentEuler);
            float angularSpeed = deltaEuler.magnitude / dt;

            // === HYSTERESIS: dùng ngưỡng khác nhau tùy trạng thái ===
            // Tránh dao động LOCKED/MOVING khi gyro dao động quanh biên
            // (vd: gyro noise 1-2°/s khi đặt máy xuống bàn sẽ không còn
            // "lái" _stabilizedRotation theo rawRotation qua Slerp).
            float threshold = _isLocked ? unlockThreshold : deadZoneThreshold;

            // === DEAD ZONE: dưới ngưỡng → khóa camera hoàn toàn ===
            if (angularSpeed < threshold)
            {
                if (!_isLocked)
                {
                    _lockedRotation = _stabilizedRotation;
                    _isLocked = true;
                }

                // Khi đứng yên, vẫn áp dụng compass correction nhẹ để sửa drift tích lũy
                if (compassCorrectionEnabled)
                {
                    ApplyCompassYawCorrection(ref _lockedRotation, dt);
                }

                _cam.transform.rotation = _lockedRotation;
            }
            else
            {
                // Trên ngưỡng dead zone → user đang quay đầu thật
                _isLocked = false;

                _currentSmoothingFactor = CalculateAdaptiveSmoothingFactor(angularSpeed);
                _stabilizedRotation = Quaternion.Slerp(_stabilizedRotation, rawRotation, _currentSmoothingFactor);

                // Compass correction khi di chuyển (nhẹ hơn)
                if (compassCorrectionEnabled)
                {
                    ApplyCompassYawCorrection(ref _stabilizedRotation, dt);
                }

                _cam.transform.rotation = _stabilizedRotation;
            }

            _previousEuler = currentEuler;
        }

        /// <summary>
        /// Sửa yaw drift bằng compass. Cardboard XR không dùng magnetometer
        /// nên yaw sẽ trôi dần theo thời gian — compass là tham chiếu tuyệt đối duy nhất cho yaw.
        /// </summary>
        void ApplyCompassYawCorrection(ref Quaternion rotation, float dt)
        {
            // Chỉ dùng compass khi dữ liệu đáng tin cậy
            // headingAccuracy < 0 = invalid, > 45° = quá nhiễu
            if (Input.compass.headingAccuracy < 0f || Input.compass.headingAccuracy > 45f)
                return;

            float compassHeading = Input.compass.trueHeading;

            if (!_compassInitialized)
            {
                _filteredCompassHeading = compassHeading;
                _compassYawOffset = Mathf.DeltaAngle(compassHeading, rotation.eulerAngles.y);
                _compassInitialized = true;
                return;
            }

            // Low-pass filter compass (circular) để giảm nhiễu
            float headingDelta = Mathf.DeltaAngle(_filteredCompassHeading, compassHeading);
            _filteredCompassHeading += headingDelta * compassFilterAlpha;
            _filteredCompassHeading = (_filteredCompassHeading % 360f + 360f) % 360f;

            // Yaw kỳ vọng theo compass
            float expectedYaw = (_filteredCompassHeading + _compassYawOffset) % 360f;
            if (expectedYaw < 0f) expectedYaw += 360f;

            // Sai lệch giữa camera hiện tại và compass
            float yawError = Mathf.DeltaAngle(rotation.eulerAngles.y, expectedYaw);

            // Nếu sai lệch > 20° → user đã quay vật lý, re-sync offset
            if (Mathf.Abs(yawError) > 20f)
            {
                _compassYawOffset = Mathf.DeltaAngle(_filteredCompassHeading, rotation.eulerAngles.y);
                return;
            }

            // Chỉnh yaw từ từ, tránh giật
            float correction = yawError * compassCorrectionStrength * dt;
            rotation = Quaternion.AngleAxis(correction, Vector3.up) * rotation;
        }

        float CalculateAdaptiveSmoothingFactor(float angularSpeed)
        {
            if (angularSpeed >= fastThreshold)
                return movingSmoothingFactor;
            if (angularSpeed <= stillThreshold)
                return stillSmoothingFactor;

            float t = (angularSpeed - stillThreshold) / (fastThreshold - stillThreshold);
            t = t * t * (3f - 2f * t); // SmoothStep
            return Mathf.Lerp(stillSmoothingFactor, movingSmoothingFactor, t);
        }

        Vector3 DeltaAngles(Vector3 from, Vector3 to)
        {
            return new Vector3(
                Mathf.DeltaAngle(from.x, to.x),
                Mathf.DeltaAngle(from.y, to.y),
                Mathf.DeltaAngle(from.z, to.z)
            );
        }

        /// <summary>
        /// Reset stabilization state. Call this after recentering.
        /// </summary>
        public void ResetStabilization()
        {
            if (_cam != null)
            {
                _stabilizedRotation = _cam.transform.rotation;
                _lockedRotation = _stabilizedRotation;
                _previousEuler = _cam.transform.eulerAngles;
                _isLocked = false;
                _compassInitialized = false;
            }
        }

        #endregion

    }

}
