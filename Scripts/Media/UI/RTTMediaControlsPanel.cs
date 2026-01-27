using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Playback controls panel for VR video player.
/// Contains play/pause, seek bar, volume, speed, and settings buttons.
/// Auto-hides during playback after 3 seconds of inactivity.
/// </summary>
public class RTTMediaControlsPanel : MonoBehaviour
{
    #region Constants
    private const float PANEL_HEIGHT = 120f;
    private const float PANEL_PADDING = 30f;
    private const float PLAY_BUTTON_SIZE = 80f;
    private const float NAV_BUTTON_SIZE = 60f;
    private const float SMALL_BUTTON_SIZE = 50f;
    private const float AUTO_HIDE_DELAY = 3f;
    private const float FADE_DURATION = 0.3f;
    #endregion

    #region Events
    public event Action OnPlayPause;
    public event Action OnPrevious;
    public event Action OnNext;
    public event Action<float> OnSeek;
    public event Action<float> OnVolumeChanged;
    public event Action<float> OnSpeedChanged;
    public event Action OnSettingsClicked;
#pragma warning disable CS0067 // Event reserved for fullscreen toggle feature
    public event Action OnFullscreenToggle;
#pragma warning restore CS0067
    public event Action OnBackClicked;
    #endregion

    #region Properties
    public bool IsVisible { get; private set; } = true;
    public bool IsPlaying { get; private set; } = false;
    #endregion

    #region Private Fields
    private float _width;
    private float _height;
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    // UI References
    private CanvasGroup _canvasGroup;
    private Button _playPauseButton;
    private Image _playPauseIcon;
    private Button _prevButton;
    private Button _nextButton;
    private VRSliderControl _seekSlider;
    private TextMeshProUGUI _currentTimeText;
    private TextMeshProUGUI _totalTimeText;
    private Button _volumeButton;
    private VRSliderControl _volumeSlider;
    private Button _speedButton;
    private TextMeshProUGUI _speedText;
    private Button _settingsButton;
    private Button _backButton;

    // State
    private float _duration = 0f;
    private float _currentTime = 0f;
    private float _volume = 1f;
    private float _speed = 1f;
    private bool _isSeeking = false;
    private float _autoHideTimer = 0f;
    private Coroutine _fadeCoroutine;
    #endregion

    #region Initialization
    public void Initialize(float w, float h, TMP_FontAsset font, Color primary, Color accent)
    {
        _width = w;
        _height = PANEL_HEIGHT;
        _font = font;
        _primaryColor = primary;
        _accentColor = accent;

        BuildUI();
    }

    private void BuildUI()
    {
        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(_width, _height);

        // Canvas group for fade
        _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Background
        var bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.7f);

        // Main container with horizontal layout
        GameObject containerObj = new GameObject("Container");
        containerObj.transform.SetParent(transform, false);

        var containerRT = containerObj.AddComponent<RectTransform>();
        containerRT.anchorMin = Vector2.zero;
        containerRT.anchorMax = Vector2.one;
        containerRT.offsetMin = new Vector2(PANEL_PADDING, 15);
        containerRT.offsetMax = new Vector2(-PANEL_PADDING, -15);

        var containerLayout = containerObj.AddComponent<VerticalLayoutGroup>();
        containerLayout.spacing = 10;
        containerLayout.childControlWidth = true;
        containerLayout.childControlHeight = false;
        containerLayout.childForceExpandWidth = true;
        containerLayout.childForceExpandHeight = false;

        // Top row: Timeline
        CreateTimelineRow(containerObj.transform);

        // Bottom row: Controls
        CreateControlsRow(containerObj.transform);
    }

    private void CreateTimelineRow(Transform parent)
    {
        GameObject rowObj = new GameObject("TimelineRow");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = 40;
        rowLE.preferredHeight = 40;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 15;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        // Current time
        _currentTimeText = CreateTimeText(rowObj.transform, "0:00", 80);

        // Seek slider
        float sliderWidth = _width - PANEL_PADDING * 2 - 80 * 2 - 15 * 2;
        _seekSlider = VRSliderFactory.CreateTimelineSlider(rowObj.transform, sliderWidth, _font, _primaryColor);

        var sliderLE = _seekSlider.gameObject.AddComponent<LayoutElement>();
        sliderLE.minWidth = sliderWidth;
        sliderLE.flexibleWidth = 1;

        _seekSlider.OnValueChanged += OnSeekValueChanged;
        _seekSlider.OnDragStarted += OnSeekStart;
        _seekSlider.OnDragEnded += OnSeekEnd;

        // Total time
        _totalTimeText = CreateTimeText(rowObj.transform, "0:00", 80);
    }

    private void CreateControlsRow(Transform parent)
    {
        GameObject rowObj = new GameObject("ControlsRow");
        rowObj.transform.SetParent(parent, false);

        var rowLE = rowObj.AddComponent<LayoutElement>();
        rowLE.minHeight = 50;
        rowLE.preferredHeight = 50;

        var rowLayout = rowObj.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        // Back button
        _backButton = CreateControlButton(rowObj.transform, "<", SMALL_BUTTON_SIZE);
        _backButton.onClick.AddListener(() => OnBackClicked?.Invoke());

        // Flexible spacer
        CreateFlexibleSpacer(rowObj.transform);

        // Previous button
        _prevButton = CreateControlButton(rowObj.transform, "<<", NAV_BUTTON_SIZE);
        _prevButton.onClick.AddListener(() => OnPrevious?.Invoke());

        // Play/Pause button
        _playPauseButton = CreateControlButton(rowObj.transform, "Play", PLAY_BUTTON_SIZE, true);
        _playPauseButton.onClick.AddListener(() => OnPlayPause?.Invoke());
        _playPauseIcon = _playPauseButton.GetComponentInChildren<Image>();

        // Next button
        _nextButton = CreateControlButton(rowObj.transform, ">>", NAV_BUTTON_SIZE);
        _nextButton.onClick.AddListener(() => OnNext?.Invoke());

        // Flexible spacer
        CreateFlexibleSpacer(rowObj.transform);

        // Volume button
        _volumeButton = CreateControlButton(rowObj.transform, "Vol", SMALL_BUTTON_SIZE);
        // Volume slider (compact)
        CreateVolumeSlider(rowObj.transform);

        // Speed button
        _speedButton = CreateControlButton(rowObj.transform, "1x", SMALL_BUTTON_SIZE);
        _speedText = _speedButton.GetComponentInChildren<TextMeshProUGUI>();
        _speedButton.onClick.AddListener(CycleSpeed);

        // Settings button
        _settingsButton = CreateControlButton(rowObj.transform, "...", SMALL_BUTTON_SIZE);
        _settingsButton.onClick.AddListener(() => OnSettingsClicked?.Invoke());
    }

    private TextMeshProUGUI CreateTimeText(Transform parent, string initialText, float width)
    {
        GameObject textObj = new GameObject("TimeText");
        textObj.transform.SetParent(parent, false);

        var textLE = textObj.AddComponent<LayoutElement>();
        textLE.minWidth = width;
        textLE.preferredWidth = width;

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = initialText;
        text.font = _font;
        text.fontSize = 24;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        return text;
    }

    private Button CreateControlButton(Transform parent, string label, float size, bool isPrimary = false)
    {
        GameObject buttonObj = new GameObject($"Btn_{label}");
        buttonObj.transform.SetParent(parent, false);

        var buttonLE = buttonObj.AddComponent<LayoutElement>();
        buttonLE.minWidth = size;
        buttonLE.minHeight = size;
        buttonLE.preferredWidth = size;
        buttonLE.preferredHeight = size;

        var bgImage = buttonObj.AddComponent<Image>();
        bgImage.color = isPrimary ? _primaryColor : new Color(1, 1, 1, 0.15f);

        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = bgImage;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.font = _font;
        text.fontSize = isPrimary ? 28 : 22;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        // Hover effect
        var hoverController = buttonObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.1f));

        // VR Collider
        var collider = buttonObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(size, size, 10);
        collider.center = new Vector3(0, 0, -5);

        return button;
    }

    private void CreateVolumeSlider(Transform parent)
    {
        var volumeSlider = VRSliderFactory.CreateSlider(
            parent, 100, 30, _font, _primaryColor,
            VRSliderFactory.SliderStyle.Volume, 0, 1);

        var sliderLE = volumeSlider.gameObject.AddComponent<LayoutElement>();
        sliderLE.minWidth = 100;
        sliderLE.preferredWidth = 100;

        _volumeSlider = volumeSlider;
        _volumeSlider.SetValue(_volume, false);
        _volumeSlider.OnValueChanged += (value) => OnVolumeChanged?.Invoke(value);
    }

    private void CreateFlexibleSpacer(Transform parent)
    {
        GameObject spacer = new GameObject("FlexibleSpacer");
        spacer.transform.SetParent(parent, false);

        var le = spacer.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Update playback state display.
    /// </summary>
    public void SetPlayState(bool isPlaying)
    {
        IsPlaying = isPlaying;

        // Update play/pause button text
        var text = _playPauseButton.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
        {
            text.text = isPlaying ? "||" : ">";
        }

        // Reset auto-hide timer when state changes
        ResetAutoHideTimer();
    }

    /// <summary>
    /// Update current playback time.
    /// </summary>
    public void SetCurrentTime(float seconds)
    {
        if (_isSeeking) return;

        _currentTime = seconds;
        _currentTimeText.text = FormatTime(seconds);

        // Update slider
        if (_duration > 0)
        {
            _seekSlider.SetValueWithoutNotify(seconds / _duration);
        }
    }

    /// <summary>
    /// Set total duration.
    /// </summary>
    public void SetDuration(float seconds)
    {
        _duration = seconds;
        _totalTimeText.text = FormatTime(seconds);
    }

    /// <summary>
    /// Set volume level (0-1).
    /// </summary>
    public void SetVolume(float volume)
    {
        _volume = Mathf.Clamp01(volume);
        _volumeSlider?.SetValueWithoutNotify(_volume);
    }

    /// <summary>
    /// Set playback speed.
    /// </summary>
    public void SetSpeed(float speed)
    {
        _speed = speed;
        if (_speedText != null)
        {
            _speedText.text = $"{speed:0.#}x";
        }
    }

    /// <summary>
    /// Show the controls panel.
    /// </summary>
    public void Show()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }
        _fadeCoroutine = StartCoroutine(FadeIn());
        IsVisible = true;
        ResetAutoHideTimer();
    }

    /// <summary>
    /// Hide the controls panel.
    /// </summary>
    public void Hide()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }
        _fadeCoroutine = StartCoroutine(FadeOut());
        IsVisible = false;
    }

    /// <summary>
    /// Reset auto-hide timer.
    /// </summary>
    public void ResetAutoHideTimer()
    {
        _autoHideTimer = AUTO_HIDE_DELAY;
    }

    /// <summary>
    /// Notify that user interacted with controls.
    /// </summary>
    public void OnUserInteraction()
    {
        if (!IsVisible)
        {
            Show();
        }
        ResetAutoHideTimer();
    }
    #endregion

    #region Private Methods
    private void OnSeekValueChanged(float normalizedValue)
    {
        if (_isSeeking)
        {
            // Update time display during seek
            _currentTimeText.text = FormatTime(normalizedValue * _duration);
        }
    }

    private void OnSeekStart()
    {
        _isSeeking = true;
    }

    private void OnSeekEnd()
    {
        _isSeeking = false;
        // Invoke seek event with actual time
        OnSeek?.Invoke(_seekSlider.NormalizedValue * _duration);
    }

    private void CycleSpeed()
    {
        // Cycle through common speeds: 0.5, 0.75, 1, 1.25, 1.5, 2
        float[] speeds = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };
        int currentIndex = 0;

        for (int i = 0; i < speeds.Length; i++)
        {
            if (Mathf.Approximately(_speed, speeds[i]))
            {
                currentIndex = i;
                break;
            }
        }

        int nextIndex = (currentIndex + 1) % speeds.Length;
        SetSpeed(speeds[nextIndex]);
        OnSpeedChanged?.Invoke(speeds[nextIndex]);
    }

    private string FormatTime(float seconds)
    {
        if (seconds < 0) seconds = 0;

        int hours = (int)(seconds / 3600);
        int minutes = (int)((seconds % 3600) / 60);
        int secs = (int)(seconds % 60);

        if (hours > 0)
        {
            return $"{hours}:{minutes:D2}:{secs:D2}";
        }
        return $"{minutes}:{secs:D2}";
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
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;
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
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;
    }
    #endregion

    #region Unity Lifecycle
    private void Update()
    {
        // Auto-hide during playback
        if (IsPlaying && IsVisible && !_isSeeking)
        {
            _autoHideTimer -= Time.deltaTime;
            if (_autoHideTimer <= 0)
            {
                Hide();
            }
        }
    }

    private void OnDestroy()
    {
        if (_seekSlider != null)
        {
            _seekSlider.OnValueChanged -= OnSeekValueChanged;
            _seekSlider.OnDragStarted -= OnSeekStart;
            _seekSlider.OnDragEnded -= OnSeekEnd;
        }
    }
    #endregion
}
