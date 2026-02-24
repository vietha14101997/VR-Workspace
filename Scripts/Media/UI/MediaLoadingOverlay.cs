using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// Loading overlay for video preparation.
    /// Shows loading spinner and progress information.
    /// </summary>
    public class MediaLoadingOverlay : MonoBehaviour
    {
        #region Constants
        private const float SPINNER_SIZE = 80f;
        private const float SPINNER_SPEED = 360f;  // Degrees per second
        private const float FADE_DURATION = 0.3f;
        #endregion

        #region Properties
        public bool IsVisible { get; private set; }
        public float Progress { get; private set; }
        public string StatusText { get; private set; }
        #endregion

        #region Private Fields
        private GameObject _overlay;
        private CanvasGroup _canvasGroup;
        private Image _spinnerImage;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _progressText;
        private Slider _progressBar;
        private Coroutine _fadeCoroutine;
        private bool _isSpinning;
        #endregion

        #region Initialization
        public void Initialize(TMP_FontAsset font, Color primaryColor)
        {
            BuildUI(font, primaryColor);
            Hide();
        }

        private void BuildUI(TMP_FontAsset font, Color primaryColor)
        {
            // Main overlay
            _overlay = new GameObject("LoadingOverlay");
            _overlay.transform.SetParent(transform, false);

            var overlayRT = _overlay.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            // Background (semi-transparent black)
            var bg = _overlay.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.85f);

            // Canvas group for fade
            _canvasGroup = _overlay.AddComponent<CanvasGroup>();

            // Center container
            GameObject centerObj = new GameObject("Center");
            centerObj.transform.SetParent(_overlay.transform, false);

            var centerRT = centerObj.AddComponent<RectTransform>();
            centerRT.anchorMin = new Vector2(0.5f, 0.5f);
            centerRT.anchorMax = new Vector2(0.5f, 0.5f);
            centerRT.sizeDelta = new Vector2(300, 250);

            var centerLayout = centerObj.AddComponent<VerticalLayoutGroup>();
            centerLayout.spacing = 20;
            centerLayout.childAlignment = TextAnchor.MiddleCenter;
            centerLayout.childControlWidth = true;
            centerLayout.childControlHeight = false;
            centerLayout.childForceExpandWidth = true;
            centerLayout.childForceExpandHeight = false;

            // Spinner
            CreateSpinner(centerObj.transform, primaryColor);

            // Status text
            _statusText = CreateText(centerObj.transform, "Loading video...", font, 28);

            // Progress bar
            CreateProgressBar(centerObj.transform, primaryColor);

            // Progress percentage
            _progressText = CreateText(centerObj.transform, "0%", font, 22, new Color(1, 1, 1, 0.7f));
        }

        private void CreateSpinner(Transform parent, Color primaryColor)
        {
            GameObject spinnerObj = new GameObject("Spinner");
            spinnerObj.transform.SetParent(parent, false);

            var spinnerLE = spinnerObj.AddComponent<LayoutElement>();
            spinnerLE.minHeight = SPINNER_SIZE;
            spinnerLE.preferredHeight = SPINNER_SIZE;

            // Spinner container for rotation
            GameObject rotatorObj = new GameObject("Rotator");
            rotatorObj.transform.SetParent(spinnerObj.transform, false);

            var rotatorRT = rotatorObj.AddComponent<RectTransform>();
            rotatorRT.anchorMin = new Vector2(0.5f, 0.5f);
            rotatorRT.anchorMax = new Vector2(0.5f, 0.5f);
            rotatorRT.sizeDelta = new Vector2(SPINNER_SIZE, SPINNER_SIZE);

            // Create spinner circle segments
            CreateSpinnerSegments(rotatorObj.transform, primaryColor);

            _spinnerImage = rotatorObj.AddComponent<Image>();
            _spinnerImage.color = Color.clear;  // Invisible, just for rotation
        }

        private void CreateSpinnerSegments(Transform parent, Color primaryColor)
        {
            int segments = 12;
            float segmentAngle = 360f / segments;

            for (int i = 0; i < segments; i++)
            {
                GameObject segObj = new GameObject($"Segment_{i}");
                segObj.transform.SetParent(parent, false);

                var segRT = segObj.AddComponent<RectTransform>();
                segRT.anchorMin = new Vector2(0.5f, 0.5f);
                segRT.anchorMax = new Vector2(0.5f, 0.5f);
                segRT.sizeDelta = new Vector2(8, 20);
                segRT.anchoredPosition = new Vector2(0, SPINNER_SIZE / 2 - 15);

                // Rotate around center
                segRT.localRotation = Quaternion.Euler(0, 0, -i * segmentAngle);

                var segImage = segObj.AddComponent<Image>();
                // Fade opacity based on position
                float alpha = 1f - (float)i / segments * 0.7f;
                segImage.color = new Color(primaryColor.r, primaryColor.g, primaryColor.b, alpha);
            }
        }

        private void CreateProgressBar(Transform parent, Color primaryColor)
        {
            GameObject progressObj = new GameObject("ProgressBar");
            progressObj.transform.SetParent(parent, false);

            var progressLE = progressObj.AddComponent<LayoutElement>();
            progressLE.minHeight = 8;
            progressLE.preferredHeight = 8;

            // Background
            var bgImage = progressObj.AddComponent<Image>();
            bgImage.color = new Color(1, 1, 1, 0.2f);

            // Slider
            _progressBar = progressObj.AddComponent<Slider>();
            _progressBar.minValue = 0;
            _progressBar.maxValue = 1;
            _progressBar.interactable = false;

            // Fill area
            GameObject fillAreaObj = new GameObject("FillArea");
            fillAreaObj.transform.SetParent(progressObj.transform, false);

            var fillAreaRT = fillAreaObj.AddComponent<RectTransform>();
            fillAreaRT.anchorMin = Vector2.zero;
            fillAreaRT.anchorMax = Vector2.one;
            fillAreaRT.offsetMin = Vector2.zero;
            fillAreaRT.offsetMax = Vector2.zero;

            // Fill
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(fillAreaObj.transform, false);

            var fillRT = fillObj.AddComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = new Vector2(0, 1);
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;

            var fillImage = fillObj.AddComponent<Image>();
            fillImage.color = primaryColor;

            _progressBar.fillRect = fillRT;
        }

        private TextMeshProUGUI CreateText(Transform parent, string text, TMP_FontAsset font, int fontSize, Color? color = null)
        {
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(parent, false);

            var textLE = textObj.AddComponent<LayoutElement>();
            textLE.minHeight = fontSize + 10;
            textLE.preferredHeight = fontSize + 10;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.color = color ?? Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            return tmp;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Show loading overlay.
        /// </summary>
        public void Show(string status = "Loading video...")
        {
            if (_fadeCoroutine != null)
                StopCoroutine(_fadeCoroutine);

            StatusText = status;
            _statusText.text = status;
            Progress = 0;
            _progressBar.value = 0;
            _progressText.text = "0%";

            gameObject.SetActive(true);
            _overlay.SetActive(true);
            _fadeCoroutine = StartCoroutine(FadeIn());
            _isSpinning = true;
            IsVisible = true;
        }

        /// <summary>
        /// Hide loading overlay.
        /// </summary>
        public void Hide()
        {
            if (_fadeCoroutine != null)
                StopCoroutine(_fadeCoroutine);

            _fadeCoroutine = StartCoroutine(FadeOut());
            _isSpinning = false;
            IsVisible = false;
        }

        /// <summary>
        /// Update progress (0-1).
        /// </summary>
        public void SetProgress(float progress)
        {
            Progress = Mathf.Clamp01(progress);
            _progressBar.value = Progress;
            _progressText.text = $"{(int)(Progress * 100)}%";
        }

        /// <summary>
        /// Update status text.
        /// </summary>
        public void SetStatus(string status)
        {
            StatusText = status;
            _statusText.text = status;
        }

        /// <summary>
        /// Show indeterminate loading (no progress).
        /// </summary>
        public void ShowIndeterminate(string status = "Loading...")
        {
            Show(status);
            _progressBar.gameObject.SetActive(false);
            _progressText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Show loading with progress.
        /// </summary>
        public void ShowWithProgress(string status = "Loading video...")
        {
            Show(status);
            _progressBar.gameObject.SetActive(true);
            _progressText.gameObject.SetActive(true);
        }
        #endregion

        #region Private Methods
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
            _overlay.SetActive(false);
            gameObject.SetActive(false);
        }
        #endregion

        #region Unity Lifecycle
        private void Update()
        {
            // Rotate spinner
            if (_isSpinning && _spinnerImage != null)
            {
                var parent = _spinnerImage.transform.parent;
                if (parent != null)
                {
                    parent.Rotate(0, 0, -SPINNER_SPEED * Time.deltaTime);
                }
            }
        }
        #endregion
    }

}
