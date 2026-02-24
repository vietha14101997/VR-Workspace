using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// RTTMediaActionBar - Floating action bar that follows the Media detail panel.
    /// Contains Play, Favourite, and Playlist buttons positioned below the detail panel.
    /// 
    /// Architecture:
    /// - Follows RTTMenuFrame (detail panel) position
    /// - Uses sphere positioning similar to RTTToolbar
    /// - Face is perpendicular to vector(center → camera)
    /// </summary>
    public class RTTMediaActionBar : MonoBehaviour
    {
        #region Events
        public event Action OnPlayClicked;
        public event Action OnFavouriteClicked;
        #endregion

        #region Private Fields
        private Transform _followTarget;
        private float _targetHeight;
        private float _panelWidth;
        private bool _initialized = false;

        // Dimensions synced with RTTToolbar/RTTTaskbar
        private float _frameHeight = 0.12f;      // Height of the invisible frame (matches Taskbar)
        private float _spacingMult = 0.145f;      // Spacing multiplier (matches Row 1 of Toolbar)

        // Buttons
        private GameObject _container;
        private Button _playButton;
        private Button _favouriteButton;
        private Image _favouriteIcon;
        private TextMeshProUGUI _favouriteText;
        private TMP_FontAsset _font;
        private bool _isFavourite = false;

        // Theme
        private Color _primaryColor;
        private Color _accentColor;

        // Animation
        private CanvasGroup _canvasGroup;
        private Coroutine _fadeCoroutine;
        private const float FADE_DURATION = 0.2f;
        private bool _isVisible = false;

        // Pending show state - waits for valid position before showing
        private bool _pendingShow = false;
        private bool _pendingShowWithFade = false;

        // Time-based guard to prevent rapid hide/show flicker
        private float _lastShowTime = 0f;
        private const float HIDE_GRACE_PERIOD = 0.5f;
        #endregion

        #region Properties
        public bool IsInitialized => _initialized;
        public Image FavouriteIcon => _favouriteIcon;

        /// <summary>
        /// Check if follow target is at a valid position (not offscreen at y=1000).
        /// </summary>
        public bool IsFollowTargetValid
        {
            get
            {
                if (_followTarget == null) return false;
                Camera cam = Camera.main;
                if (cam == null) return false;

                float dist = Vector3.Distance(cam.transform.position, _followTarget.position);
                return dist < 100f;  // Same threshold used in UpdateSpherePosition
            }
        }
        #endregion

        #region Lifecycle
        private bool _warnedBadPosition = false;
        private int _alphaVerifyFrames = 0;
        private const int ALPHA_VERIFY_FRAMES = 3;

        private void LateUpdate()
        {
            if (!_initialized || _followTarget == null) return;

            UpdateSpherePosition();

            // Handle pending show - only show when position is valid
            if ((_pendingShow || _pendingShowWithFade) && IsFollowTargetValid)
            {
                if (_pendingShow)
                {
                    _pendingShow = false;
                    DoShowImmediate();
                }
                else if (_pendingShowWithFade)
                {
                    _pendingShowWithFade = false;
                    DoShowWithFade();
                }
            }

            // Backup mechanism: verify alpha is correct when visible
            if (_isVisible && _canvasGroup != null && _fadeCoroutine == null)
            {
                if (_canvasGroup.alpha < 0.99f)
                {
                    _alphaVerifyFrames++;
                    if (_alphaVerifyFrames >= ALPHA_VERIFY_FRAMES)
                    {
                        _canvasGroup.alpha = 1f;
                        _alphaVerifyFrames = 0;
                    }
                }
                else
                {
                    _alphaVerifyFrames = 0;
                }
            }
        }

        private void OnDestroy()
        {
            if (_container != null)
            {
                Destroy(_container);
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// Initialize with follow target and theme colors.
        /// </summary>
        public void Initialize(Transform followTarget, float targetHeight, float panelWidth, Color primaryColor, Color accentColor, TMP_FontAsset font)
        {
            _followTarget = followTarget;
            _targetHeight = targetHeight;
            _panelWidth = panelWidth;
            _primaryColor = primaryColor;
            _accentColor = accentColor;
            _font = font;

            // Try to sync dimensions with RTTTaskbar if available
            RTTToolbar toolbar = RTTToolbar.Instance;
            if (toolbar != null && toolbar.TaskbarHeight > 0)
            {
                _frameHeight = toolbar.TaskbarHeight;
                // Align with Pagination (Row 1) gap = 0.145f (0.095f toolbar gap + 0.05f row offset)
                _spacingMult = 0.2f; 
            }

            CreateButtons(font);

            // Container starts active but invisible (alpha=0) so LateUpdate can position it
            _initialized = true;
        }

        /// <summary>
        /// Update favourite button visual state.
        /// </summary>
        public void UpdateFavouriteState(bool isFavourite)
        {
            _isFavourite = isFavourite;

            // Update icon
            if (_favouriteIcon != null)
            {
                Sprite icon = Resources.Load<Sprite>(isFavourite ? "icon_remove_favorite" : "icon_add_favorite");
                if (icon != null) _favouriteIcon.sprite = icon;
                _favouriteIcon.color = isFavourite ? _accentColor : Color.white;
            }

            // Update text label
            if (_favouriteText != null)
            {
                _favouriteText.text = isFavourite ? "Unfavorite" : "Favorite";
            }
        }

        /// <summary>
        /// Show/Hide the action bar with fade animation.
        /// If showing and follow target is not at valid position, queues show for when it becomes valid.
        /// </summary>
        public void SetVisible(bool visible)
        {
            _pendingShow = false;
            _pendingShowWithFade = false;

            if (_container == null || _isVisible == visible) return;

            if (visible && !IsFollowTargetValid)
            {
                _pendingShowWithFade = true;
                return;
            }

            _isVisible = visible;

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            if (_canvasGroup == null)
            {
                _canvasGroup = _container.GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = _container.AddComponent<CanvasGroup>();
                }
            }

            if (visible)
            {
                _canvasGroup.alpha = 0f;
                _container.SetActive(true);
                _fadeCoroutine = StartCoroutine(FadeCoroutine(0f, 1f, FADE_DURATION));
            }
            else
            {
                _fadeCoroutine = StartCoroutine(FadeCoroutine(_canvasGroup.alpha, 0f, FADE_DURATION, deactivateOnComplete: true));
            }
        }

        /// <summary>
        /// Immediately hide without animation (for cleanup).
        /// </summary>
        public void HideImmediate()
        {
            // Prevent rapid hide/show flicker by ignoring Hide() calls too soon after Show()
            float timeSinceShow = Time.time - _lastShowTime;
            if (_isVisible && timeSinceShow < HIDE_GRACE_PERIOD) return;

            _pendingShow = false;
            _pendingShowWithFade = false;

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            if (_container != null) _container.SetActive(false);
            _isVisible = false;
        }

        /// <summary>
        /// Immediately show without animation (for startup).
        /// If follow target is not at valid position, queues show for when it becomes valid.
        /// </summary>
        public void ShowImmediate()
        {
            if (_isVisible) return;

            _pendingShow = false;
            _pendingShowWithFade = false;

            if (!IsFollowTargetValid)
            {
                _pendingShow = true;
                return;
            }

            DoShowImmediate();
        }

        private void DoShowImmediate()
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            if (_canvasGroup == null && _container != null)
            {
                _canvasGroup = _container.GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = _container.AddComponent<CanvasGroup>();
                }
            }

            if (_canvasGroup != null) _canvasGroup.alpha = 1f;
            if (_container != null) _container.SetActive(true);
            _isVisible = true;
            _lastShowTime = Time.time;
        }

        /// <summary>
        /// Prepare for positioning: container active but invisible (alpha=0).
        /// This allows LateUpdate to run and position the ActionBar correctly.
        /// </summary>
        public void PrepareForPositioning()
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            // CanvasGroup was created in CreateButtons() with alpha=0
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            if (_container != null)
            {
                _container.SetActive(true);  // Active so LateUpdate runs for positioning
            }

            _isVisible = false;
        }

        /// <summary>
        /// Show with fade animation (after positioning is ready).
        /// If follow target is not at valid position, queues show for when it becomes valid.
        /// </summary>
        public void ShowWithFade()
        {
            if (_container == null) return;
            if (_isVisible) return;
            if (_fadeCoroutine != null) return;

            _pendingShow = false;
            _pendingShowWithFade = false;

            if (!IsFollowTargetValid)
            {
                _pendingShowWithFade = true;
                return;
            }

            DoShowWithFade();
        }

        private void DoShowWithFade()
        {
            if (_container == null) return;

            if (_canvasGroup == null)
            {
                _canvasGroup = _container.GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = _container.AddComponent<CanvasGroup>();
                }
            }

            _isVisible = true;
            _lastShowTime = Time.time;

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            _container.SetActive(true);
            _fadeCoroutine = StartCoroutine(FadeCoroutine(0f, 1f, FADE_DURATION));
        }
        #endregion

        #region Private Methods
        private void CreateButtons(TMP_FontAsset font)
        {
            // Create container for buttons
            _container = new GameObject("ActionButtonsContainer");
            _container.transform.SetParent(transform, false);

            // Add CanvasGroup immediately with alpha=0 to prevent any flash
            _canvasGroup = _container.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;

            // Add Canvas for UI buttons
            Canvas canvas = _container.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // Button configuration
            float buttonHeight = 67.5f;  // Visual height of buttons
            float iconSize = 28f;
            float fontSize = 20;
            float spacing = 8f;          
            float charWidth = 12f;
            float sidePadding = 25f;     
            float buttonSpacing = 15f;   

            // Button labels
            string playLabel = "Play";
            string favLabel = "Favorite";
            string playlistLabel = "Playlist";

            // Calculate base width for each button based on text length
            float playBaseWidth = iconSize + spacing + (playLabel.Length * charWidth) + (sidePadding * 2);
            float favBaseWidth = iconSize + spacing + (favLabel.Length * charWidth) + (sidePadding * 2);
            float playlistBaseWidth = iconSize + spacing + (playlistLabel.Length * charWidth) + (sidePadding * 2);
            float totalBaseWidth = playBaseWidth + favBaseWidth + playlistBaseWidth + (buttonSpacing * 2);

            // Canvas Setup
            float logicalPanelWidth = _panelWidth * 1000f;
            float canvasWidth = logicalPanelWidth;
            float canvasHeight = _frameHeight * 1000f;

            // Scale buttons to fill panel width while keeping text-based proportions
            float targetWidth = canvasWidth - (buttonSpacing * 2);
            float scale = targetWidth / totalBaseWidth;

            float playWidth = playBaseWidth * scale;
            float favWidth = favBaseWidth * scale;
            float playlistWidth = playlistBaseWidth * scale;

            // Total width of buttons
            float totalButtonsWidth = playWidth + favWidth + playlistWidth + (buttonSpacing * 2);

            RectTransform canvasRT = _container.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(canvasWidth, canvasHeight);
            canvasRT.localScale = Vector3.one * 0.001f;

            _container.AddComponent<CanvasScaler>();
            _container.AddComponent<GraphicRaycaster>();

            float groupStartX = -totalButtonsWidth / 2f;

            // Play Button (Cyan)
            float playX = groupStartX + playWidth / 2f;
            Sprite playIcon = Resources.Load<Sprite>("icon_play");
            Color cyanColor = new Color(0f, 0.9f, 1f);  // Cyan
            var playConfig = new VRButtonFactory.ButtonConfig
            {
                label = playLabel,
                icon = playIcon,
                themeColor = cyanColor,
                width = playWidth,
                height = buttonHeight,
                horizontalLayout = true,
                iconSize = iconSize,
                fontSize = (int)fontSize,
                font = font,
                spacing = spacing,
                cornerRadius = 0.15f,    
                borderWidth = 0.055f,    
                glowWidth = 0.06f,
                glowIntensity = 3f,
                popAmount = 0.03f
            };
            GameObject playBtn = VRButtonFactory.CreateButton(canvasRT, playConfig, () => OnPlayClicked?.Invoke());
            PositionButton(playBtn, playX);
            _playButton = playBtn.GetComponent<Button>();

            // Favourite Button (Purple)
            float favX = groupStartX + playWidth + buttonSpacing + favWidth / 2f;
            Sprite favIcon = Resources.Load<Sprite>("icon_add_favorite");
            Color purpleColor = new Color(0.76f, 0.36f, 1f);  // Purple
            var favConfig = new VRButtonFactory.ButtonConfig
            {
                label = favLabel,
                icon = favIcon,
                themeColor = purpleColor,
                width = favWidth,
                height = buttonHeight,
                horizontalLayout = true,
                iconSize = iconSize,
                fontSize = (int)fontSize,
                font = font,
                spacing = spacing,
                cornerRadius = 0.15f,
                borderWidth = 0.055f,
                glowWidth = 0.06f,
                glowIntensity = 3f,
                popAmount = 0.03f
            };
            GameObject favBtn = VRButtonFactory.CreateButton(canvasRT, favConfig, () => OnFavouriteClicked?.Invoke());
            PositionButton(favBtn, favX);
            _favouriteButton = favBtn.GetComponent<Button>();
            _favouriteIcon = favBtn.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();
            _favouriteText = favBtn.transform.Find("HitArea/Visuals/Content/Text")?.GetComponent<TextMeshProUGUI>();

            // Playlist Button (Deep Sea Blue)
            float playlistX = groupStartX + playWidth + buttonSpacing + favWidth + buttonSpacing + playlistWidth / 2f;
            Sprite playlistIcon = Resources.Load<Sprite>("icon_add_playlist");
            Color deepSeaBlue = new Color(0f, 0.4f, 0.65f);  // Deep sea blue
            var playlistConfig = new VRButtonFactory.ButtonConfig
            {
                label = playlistLabel,
                icon = playlistIcon,
                themeColor = deepSeaBlue,
                width = playlistWidth,
                height = buttonHeight,
                horizontalLayout = true,
                iconSize = iconSize,
                fontSize = (int)fontSize,
                font = font,
                spacing = spacing,
                cornerRadius = 0.15f,
                borderWidth = 0.055f,
                glowWidth = 0.06f,
                glowIntensity = 3f,
                popAmount = 0.03f
            };
            GameObject playlistBtn = VRButtonFactory.CreateButton(canvasRT, playlistConfig, null); // No action
            PositionButton(playlistBtn, playlistX);

            // Container starts INACTIVE - only becomes active when explicitly shown via ShowWithFade() or ShowImmediate()
            // This prevents ActionBar from appearing during dwell pre-loading when frame is temporarily activated
            _container.SetActive(false);
        }

        private void PositionButton(GameObject btn, float xOffset)
        {
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Anchor y=0 puts it in vertical center of the canvas (frame)
            rt.anchoredPosition = new Vector2(xOffset, 0);
        }

        private void UpdateSpherePosition()
        {
            Camera cam = Camera.main;
            if (cam == null || _followTarget == null) return;

            Vector3 cameraPos = cam.transform.position;
            Vector3 targetCenter = _followTarget.position;

            // Sanity check: if followTarget is too far from camera (> 100m), skip positioning
            float distToTarget = Vector3.Distance(cameraPos, targetCenter);
            if (distToTarget > 100f)
            {
                _warnedBadPosition = true;
                return;
            }

            if (_warnedBadPosition) _warnedBadPosition = false;

            Vector3 targetUp = _followTarget.up;

            float targetHalfHeight = _targetHeight / 2f;
            float frameHalfHeight = _frameHeight / 2f;

            // Gap calculation matching RTTToolbar
            float gap = _frameHeight * _spacingMult;

            // Step 1: Calculate top edge position E (below panel bottom)
            Vector3 targetBottom = targetCenter - targetUp * targetHalfHeight;
            Vector3 E = targetBottom - Vector3.up * gap;

            // Step 2: Calculate vector from camera to E
            Vector3 toE = E - cameraPos;
            float distToE = toE.magnitude;

            float h = frameHalfHeight;

            // Edge case: camera too close
            if (distToE < 0.001f || distToE < h)
            {
                // Fallback: simple linear positioning
                // Position is E moved down by half height
                transform.position = E - Vector3.up * h;
                Vector3 toCam = cameraPos - transform.position;
                if (toCam.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
                }
                return;
            }

            // Step 3: Calculate distance from camera to action bar center
            float rSq = distToE * distToE - h * h;
            if (rSq < 0.0001f) rSq = 0.0001f;
            float r = Mathf.Sqrt(rSq);

            // Step 4: Calculate direction and angle adjustment
            float sinBeta = h / distToE;
            sinBeta = Mathf.Clamp(sinBeta, -1f, 1f);
            float beta = Mathf.Asin(sinBeta);

            // Step 5: Calculate action bar center position
            Vector3 horizontalDir = new Vector3(toE.x, 0, toE.z);
            float horizontalDist = horizontalDir.magnitude;

            if (horizontalDist < 0.001f)
            {
                horizontalDir = _followTarget.forward;
                horizontalDir.y = 0;
                if (horizontalDir.sqrMagnitude < 0.001f) horizontalDir = Vector3.forward;
            }
            horizontalDir.Normalize();

            float currentAngle = Mathf.Atan2(toE.y, horizontalDist);
            float newAngle = currentAngle - beta;

            float newHorizontalDist = r * Mathf.Cos(newAngle);
            float newVerticalDist = r * Mathf.Sin(newAngle);

            transform.position = cameraPos + horizontalDir * newHorizontalDist + Vector3.up * newVerticalDist;

            // Step 6: Calculate rotation to face camera
            Vector3 toCamera = cameraPos - transform.position;
            if (toCamera.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
        }
        #endregion

        #region Animation
        private IEnumerator FadeCoroutine(float from, float to, float duration, bool deactivateOnComplete = false)
        {
            if (_canvasGroup == null) yield break;

            _canvasGroup.alpha = from;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _canvasGroup.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }

            _canvasGroup.alpha = to;

            if (deactivateOnComplete && _container != null)
            {
                _container.SetActive(false);
            }

            _fadeCoroutine = null;
        }
        #endregion

        #region Static Factory
        /// <summary>
        /// Create RTTMediaActionBar in VirtualObjects.
        /// </summary>
        public static RTTMediaActionBar Create(Transform followTarget, float targetHeight, float panelWidth, Color primaryColor, Color accentColor, TMP_FontAsset font)
        {
            GameObject barObj = new GameObject("RTTMediaActionBar");

            // Parent to VirtualObjects and set layer
            GameObject virtualObjects = GameObject.Find("VirtualObjects");
            int voLayer = LayerMask.NameToLayer("VirtualObjects");
            if (virtualObjects != null)
            {
                barObj.transform.SetParent(virtualObjects.transform, false);
            }
            if (voLayer >= 0)
            {
                SetLayerRecursively(barObj, voLayer);
            }

            RTTMediaActionBar actionBar = barObj.AddComponent<RTTMediaActionBar>();
            actionBar.Initialize(followTarget, targetHeight, panelWidth, primaryColor, accentColor, font);

            // Set layer on container and all children after Initialize creates them
            if (voLayer >= 0 && actionBar._container != null)
            {
                SetLayerRecursively(actionBar._container, voLayer);
            }

            return actionBar;
        }

        private static void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
        #endregion
    }

}
