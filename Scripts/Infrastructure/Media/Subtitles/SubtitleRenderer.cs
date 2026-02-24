using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using TMPro;

namespace VRWorkspace.Media.Subtitles
{
    /// <summary>
    /// VR-optimized subtitle renderer with advanced styling and positioning.
    /// Can follow the video screen or be positioned in world space.
    /// </summary>
    public class SubtitleRenderer : MonoBehaviour
    {
        #region Constants
        private const float CANVAS_SCALE = 0.001f;  // 1mm per pixel
        private const float DEFAULT_WIDTH = 1920f;
        private const float DEFAULT_HEIGHT = 200f;
        private const float FADE_DURATION = 0.2f;
        #endregion

        #region Enums
        public enum PositionMode
        {
            FollowScreen,       // Position below video screen
            WorldSpace,         // Fixed world position
            FollowCamera        // Follow camera view
        }

        public enum StylePreset
        {
            Default,            // White with black outline
            Cinematic,          // White with subtle shadow
            HighContrast,       // Yellow on black background
            Minimal             // White with no background
        }
        #endregion

        #region Properties
        public bool IsVisible => _canvasGroup != null && _canvasGroup.alpha > 0.5f;
        public string CurrentText => _subtitleText?.text ?? "";
        #endregion

        #region Settings
        [Header("Position")]
        public PositionMode Mode = PositionMode.FollowScreen;
        public float DistanceFromCamera = 2f;
        public float VerticalOffset = -0.5f;

        [Header("Style")]
        public StylePreset Preset = StylePreset.Default;
        public float FontSize = 42f;
        public Color TextColor = Color.white;
        public Color BackgroundColor = new Color(0, 0, 0, 0.6f);
        public float OutlineWidth = 0.15f;
        public Color OutlineColor = Color.black;
        public float ShadowOffset = 2f;

        [Header("Animation")]
        public bool UseFadeAnimation = true;
        public bool UseTypewriterEffect = false;
        public float TypewriterSpeed = 50f;  // Characters per second
        #endregion

        #region Private Fields
        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private RectTransform _canvasRT;
        private Image _backgroundImage;
        private TextMeshProUGUI _subtitleText;
        private Transform _targetScreen;
        private float _screenHeight;
        private Coroutine _fadeCoroutine;
        private Coroutine _typewriterCoroutine;
        private bool _isInitialized;
        #endregion

        #region Initialization
        public void Initialize(TMP_FontAsset font = null)
        {
            if (_isInitialized) return;

            CreateCanvas();
            CreateBackground();
            CreateText(font);
            ApplyPreset(Preset);

            Hide(immediate: true);
            _isInitialized = true;

            Debug.Log("[SubtitleRenderer] Initialized");
        }

        private void CreateCanvas()
        {
            GameObject canvasObj = new GameObject("SubtitleCanvas");
            canvasObj.transform.SetParent(transform);
            canvasObj.transform.localPosition = Vector3.zero;
            canvasObj.transform.localRotation = Quaternion.identity;

            _canvas = canvasObj.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            _canvasRT = _canvas.GetComponent<RectTransform>();
            _canvasRT.sizeDelta = new Vector2(DEFAULT_WIDTH, DEFAULT_HEIGHT);
            _canvasRT.localScale = Vector3.one * CANVAS_SCALE;

            _canvasGroup = canvasObj.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
        }

        private void CreateBackground()
        {
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(_canvas.transform, false);

            var bgRT = bgObj.AddComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = new Vector2(-20, -10);
            bgRT.offsetMax = new Vector2(20, 10);

            _backgroundImage = bgObj.AddComponent<Image>();
            _backgroundImage.color = BackgroundColor;

            // Rounded corners (if supported)
            // Could use a sliced sprite for rounded corners
        }

        private void CreateText(TMP_FontAsset font)
        {
            GameObject textObj = new GameObject("SubtitleText");
            textObj.transform.SetParent(_canvas.transform, false);

            var textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(30, 15);
            textRT.offsetMax = new Vector2(-30, -15);

            _subtitleText = textObj.AddComponent<TextMeshProUGUI>();
            _subtitleText.font = font;
            _subtitleText.fontSize = FontSize;
            _subtitleText.color = TextColor;
            _subtitleText.alignment = TextAlignmentOptions.Center;
            _subtitleText.textWrappingMode = TextWrappingModes.Normal;
            _subtitleText.overflowMode = TextOverflowModes.Truncate;
            _subtitleText.text = "";

            // Outline
            _subtitleText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, OutlineWidth);
            _subtitleText.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, OutlineColor);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Show subtitle text.
        /// </summary>
        public void Show(string text)
        {
            if (!_isInitialized)
                Initialize();

            if (_typewriterCoroutine != null)
            {
                StopCoroutine(_typewriterCoroutine);
                _typewriterCoroutine = null;
            }

            if (UseTypewriterEffect && !string.IsNullOrEmpty(text))
            {
                _typewriterCoroutine = StartCoroutine(TypewriterShow(text));
            }
            else
            {
                _subtitleText.text = text;
            }

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }

            if (UseFadeAnimation)
            {
                _fadeCoroutine = StartCoroutine(FadeIn());
            }
            else
            {
                _canvasGroup.alpha = 1f;
            }
        }

        /// <summary>
        /// Hide subtitle.
        /// </summary>
        public void Hide(bool immediate = false)
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            if (_typewriterCoroutine != null)
            {
                StopCoroutine(_typewriterCoroutine);
                _typewriterCoroutine = null;
            }

            if (immediate || !UseFadeAnimation)
            {
                if (_canvasGroup != null)
                    _canvasGroup.alpha = 0f;
            }
            else
            {
                _fadeCoroutine = StartCoroutine(FadeOut());
            }
        }

        /// <summary>
        /// Set target screen transform for FollowScreen mode.
        /// </summary>
        public void SetTargetScreen(Transform screen, float screenHeight)
        {
            _targetScreen = screen;
            _screenHeight = screenHeight;
        }

        /// <summary>
        /// Set world space position directly.
        /// </summary>
        public void SetWorldPosition(Vector3 position, Quaternion rotation)
        {
            if (_canvas != null)
            {
                _canvas.transform.position = position;
                _canvas.transform.rotation = rotation;
            }
        }

        /// <summary>
        /// Apply style preset.
        /// </summary>
        public void ApplyPreset(StylePreset preset)
        {
            Preset = preset;

            switch (preset)
            {
                case StylePreset.Default:
                    TextColor = Color.white;
                    BackgroundColor = new Color(0, 0, 0, 0.6f);
                    OutlineWidth = 0.15f;
                    OutlineColor = Color.black;
                    break;

                case StylePreset.Cinematic:
                    TextColor = Color.white;
                    BackgroundColor = new Color(0, 0, 0, 0.4f);
                    OutlineWidth = 0.05f;
                    OutlineColor = new Color(0, 0, 0, 0.5f);
                    break;

                case StylePreset.HighContrast:
                    TextColor = new Color(1f, 1f, 0.2f);  // Yellow
                    BackgroundColor = new Color(0, 0, 0, 0.9f);
                    OutlineWidth = 0.2f;
                    OutlineColor = Color.black;
                    break;

                case StylePreset.Minimal:
                    TextColor = Color.white;
                    BackgroundColor = Color.clear;
                    OutlineWidth = 0.1f;
                    OutlineColor = Color.black;
                    break;
            }

            ApplyStyle();
        }

        /// <summary>
        /// Update font size.
        /// </summary>
        public void SetFontSize(float size)
        {
            FontSize = Mathf.Clamp(size, 16f, 72f);
            if (_subtitleText != null)
            {
                _subtitleText.fontSize = FontSize;
            }
        }

        /// <summary>
        /// Set vertical offset from screen center.
        /// </summary>
        public void SetVerticalOffset(float offset)
        {
            VerticalOffset = offset;
        }
        #endregion

        #region Private Methods
        private void ApplyStyle()
        {
            if (_subtitleText != null)
            {
                _subtitleText.color = TextColor;
                _subtitleText.fontSize = FontSize;

                // Update outline
                if (_subtitleText.fontMaterial != null)
                {
                    _subtitleText.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, OutlineWidth);
                    _subtitleText.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, OutlineColor);
                }
            }

            if (_backgroundImage != null)
            {
                _backgroundImage.color = BackgroundColor;
            }
        }

        private void UpdatePosition()
        {
            if (_canvas == null) return;

            switch (Mode)
            {
                case PositionMode.FollowScreen:
                    if (_targetScreen != null)
                    {
                        Vector3 offset = _targetScreen.up * VerticalOffset;
                        _canvas.transform.position = _targetScreen.position + offset;
                        _canvas.transform.rotation = _targetScreen.rotation;
                    }
                    break;

                case PositionMode.FollowCamera:
                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        Vector3 forward = cam.transform.forward;
                        forward.y = 0;
                        forward.Normalize();

                        Vector3 position = cam.transform.position +
                                          forward * DistanceFromCamera +
                                          Vector3.up * VerticalOffset;

                        _canvas.transform.position = position;
                        _canvas.transform.LookAt(cam.transform);
                        _canvas.transform.Rotate(0, 180, 0);  // Face camera
                    }
                    break;

                case PositionMode.WorldSpace:
                    // Position set manually via SetWorldPosition
                    break;
            }
        }

        private IEnumerator FadeIn()
        {
            float elapsed = 0f;
            float startAlpha = _canvasGroup.alpha;

            while (elapsed < FADE_DURATION)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, elapsed / FADE_DURATION);
                yield return null;
            }

            _canvasGroup.alpha = 1f;
            _fadeCoroutine = null;
        }

        private IEnumerator FadeOut()
        {
            float elapsed = 0f;
            float startAlpha = _canvasGroup.alpha;

            while (elapsed < FADE_DURATION)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / FADE_DURATION);
                yield return null;
            }

            _canvasGroup.alpha = 0f;
            _fadeCoroutine = null;
        }

        private IEnumerator TypewriterShow(string fullText)
        {
            _subtitleText.text = "";
            int charIndex = 0;

            while (charIndex < fullText.Length)
            {
                // Handle HTML/rich text tags
                if (fullText[charIndex] == '<')
                {
                    int closeIndex = fullText.IndexOf('>', charIndex);
                    if (closeIndex > charIndex)
                    {
                        _subtitleText.text = fullText.Substring(0, closeIndex + 1);
                        charIndex = closeIndex + 1;
                        continue;
                    }
                }

                charIndex++;
                _subtitleText.text = fullText.Substring(0, charIndex);

                yield return new WaitForSeconds(1f / TypewriterSpeed);
            }

            _subtitleText.text = fullText;
            _typewriterCoroutine = null;
        }
        #endregion

        #region Unity Lifecycle
        private void Update()
        {
            if (IsVisible)
            {
                UpdatePosition();
            }
        }

        private void OnDestroy()
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }

            if (_typewriterCoroutine != null)
            {
                StopCoroutine(_typewriterCoroutine);
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
        #endregion
    }
}
