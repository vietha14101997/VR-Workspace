using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections;
using TMPro;
using VRWorkspace.UI.HoverEffects;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// Playback controls panel for VR video player.
    /// Contains play/pause, seek bar, volume, speed, and settings buttons.
    /// Auto-hides during playback after 10 seconds of inactivity (unless hovering).
    /// </summary>
    public class RTTMediaControlsPanel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        #region Constants
        // Layout - 3 Equal Zones (A, B, C)
        private const float TOTAL_HEIGHT = 700f;
        private const float ZONE_HEIGHT = 200f;
        private const float TOP_SPACER = 100f; // 50% of ZONE_HEIGHT — empty space above Zone A

        // Zone A (Header) - NO horizontal padding, buttons touch edges
        private const float HEADER_BUTTON_SIZE = 100f; // 50% of ZONE_HEIGHT

        // Zone B & C - Inside Background with percentage-based spacing
        private const float BG_VERTICAL_SPACING_RATIO = 0.05f;    // 5% of background height for padding
        private const float BG_HORIZONTAL_SPACING_RATIO = 0.03f;  // 3% of background width
        private const float ZONE_SPACING_RATIO = 0.25f;           // 25% of zone height for spacing between Zone B and C

        // Zone B - Title & Timeline
        private const float TIMELINE_SLIDER_RATIO = 0.65f; // 65% of available width

        // Zone C - Controls (sizes calculated dynamically based on Zone C height)
        private const float PLAY_BUTTON_HEIGHT_RATIO = 1.125f;  // 112.5% of Zone C height (increased from 1.0)
        private const float OTHER_BUTTON_HEIGHT_RATIO = 0.625f; // 62.5% of Zone C height for all other buttons (increased from 0.5)

        // Styling
        private static readonly Color BG_COLOR = new Color(0.173f, 0.173f, 0.173f, 0.75f);
        private static readonly Color HEADER_BTN_BG_COLOR = new Color(0f, 0f, 0f, 0.75f);
        private static readonly Color THEME_COLOR = new Color(1f, 0.2f, 0.2f, 1f); // Reticle red
        private const float BG_CORNER_RADIUS = 20f;
        private const float ZONE_A_BOTTOM_MARGIN = 50f;

        // Menu Button
        private const string ICON_MENU = "icon_menu";

        private const float FADE_DURATION = 0.2f;
        #endregion

        #region Events
        public event Action OnPlayPause;
        public event Action<float> OnSeek;
        public event Action<float> OnVolumeChanged;
        public event Action<float> OnSpeedChanged;
        public event Action OnBackClicked;
    #pragma warning disable CS0067 // Event not yet used - reserved for future playlist feature
        public event Action OnPlaylistClicked;
    #pragma warning restore CS0067
        public event Action OnVRModeClicked;
        public event Action OnHeadsetModeClicked;
        public event Action OnRecenterClicked;
        public event Action OnEnvironmentClicked;
        /// <summary>Fired when controls panel visibility changes. Parameter: true=visible, false=hidden.</summary>
        public event Action<bool> OnVisibilityChanged;
        /// <summary>Fired when settings button is clicked.</summary>
        public event Action OnSettingsClicked;
        #endregion

        #region Properties
        public bool IsVisible { get; private set; } = true;
        public bool IsPlaying { get; private set; } = false;
        public float Volume => _volume;
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
        private TextMeshProUGUI _titleText;
        private MarqueeText _titleMarquee;
        private TextMeshProUGUI _currentTimeText;
        private TextMeshProUGUI _totalTimeText;
        private Button _volumeButton;
        private Image _volumeIcon;
        private VRSliderControl _volumeSlider;
        private Button _speedButton;
        private TextMeshProUGUI _speedText;
        private Button _settingsButton;
        private Button _backButton;
        private Button _recenterButton;
        // External world-space menu button + dismiss overlay (managed by VRMediaAppController)
        private GameObject _menuButtonFrameObject;
        private GameObject _overlayFrameObject;
        private GameObject _sideControlsFrameObject;
        private RTTFilePagination _queuePagination;
        private BoxCollider _parentFrameCollider; // Cached collider of parent RTT frame's display quad

        // Settings panel toggle
        private RTTMediaSettingsPanel _settingsPanel;
        private GameObject _queuePanelObject;
        private GameObject _settingsFrameObject;
        private bool _settingsActive = false;
        private Image _settingsButtonIconImage;
        private Image _settingsButtonBgImage;

        // Volume persistence
        private const string PREF_VOLUME = "MediaPlayer_Volume";

        // State
        private float _duration = 0f;
        private float _currentTime = 0f;
        private float _volume = 1f;
        private float _previousVolume = 1f;
        private float _speed = 1f;
        private bool _isSeeking = false;
        private Coroutine _fadeCoroutine;

        // Sprites
        private static Sprite _circleSprite;
        private static Sprite _circleOutlineSprite;

        // Icon names for each button (use icon_record as placeholder)
        private const string ICON_BACK = "icon_exit";
        private const string ICON_AB_LOOP = "icon_record";
        private const string ICON_RECENTER = "icon_recenter";
        private const string ICON_REPEAT = "icon_loop";
        private const string ICON_SETTINGS = "icon_settings";
        private const string ICON_VOLUME = "icon_volume";
        private const string ICON_VOLUME_MUTE = "icon_volume_mute";
        private const string ICON_BACKWARD = "icon_backward";
        private const string ICON_PLAY = "icon_play";
        private const string ICON_PAUSE = "icon_pause";
        private const string ICON_FORWARD = "icon_forward";
        private const string ICON_ENVIRONMENT = "icon_enviroment";
        private const string ICON_3D = "icon_cube";
        #endregion

        #region Initialization
        public void Initialize(float w, float h, TMP_FontAsset font, Color primary, Color accent)
        {
            _width = w;
            _height = TOTAL_HEIGHT; // Use new constant
            _font = font;
            _primaryColor = primary;
            _accentColor = accent;

            // Load persisted volume (default 100%)
            _volume = PlayerPrefs.GetFloat(PREF_VOLUME, 1f);
            _previousVolume = _volume;

            BuildUI();

            // Apply loaded volume to slider and icon
            SetVolume(_volume);
        }

        private void BuildUI()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();

            rt.anchorMin = new Vector2(0.5f, 0);
            rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(_width, _height);

            // Invisible raycast target covering entire panel for IPointerEnterHandler/IPointerExitHandler
            var panelHitArea = gameObject.AddComponent<Image>();
            panelHitArea.color = Color.clear;
            panelHitArea.raycastTarget = true;

            // === Content Wrapper (owns CanvasGroup for fade, separate from MenuButton) ===
            GameObject content = new GameObject("Content");
            content.transform.SetParent(transform, false);

            var contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = Vector2.zero;
            contentRT.anchorMax = Vector2.one;
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;

            // CanvasGroup on Content (NOT root) so MenuButton can fade independently
            _canvasGroup = content.AddComponent<CanvasGroup>();

            float contentWidth = _width;

            // VerticalLayoutGroup on Content (moved from root)
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = ZONE_A_BOTTOM_MARGIN; // Gap between Zone A and Background
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childAlignment = TextAnchor.UpperCenter;
            int hoverMargin = 15; // Extra space so scale hover effects on edge buttons aren't clipped
            layout.padding = new RectOffset(hoverMargin, hoverMargin, (int)TOP_SPACER, 0);

            // === ZONE A: HEADER (Outside Background) ===
            CreateZoneA(content.transform, contentWidth);

            // === MAIN BODY CONTAINER (For Zone B & C) ===
            GameObject mainBody = new GameObject("MainBodyContainer");
            mainBody.transform.SetParent(content.transform, false);

            var bodyRT = mainBody.AddComponent<RectTransform>();
            // Background height = remaining height after top spacer + Zone A + gap
            float bodyHeight = _height - TOP_SPACER - ZONE_HEIGHT - ZONE_A_BOTTOM_MARGIN;
            bodyRT.sizeDelta = new Vector2(contentWidth, bodyHeight); // Use contentWidth!

            var bodyLE = mainBody.AddComponent<LayoutElement>();
            bodyLE.minHeight = bodyHeight;
            bodyLE.preferredHeight = bodyHeight;
            bodyLE.minWidth = contentWidth;
            bodyLE.preferredWidth = contentWidth;

            // Background Image - Uniform Dark Transparent with rounded corners
            var bg = mainBody.AddComponent<Image>();
            bg.color = BG_COLOR;
            // Apply rounded corners using a rounded rect sprite
            bg.sprite = CreateRoundedRectSprite(BG_CORNER_RADIUS);
            bg.type = Image.Type.Sliced;

            // Calculate spacing based on percentages (5% vertical, 3% horizontal)
            int paddingH = Mathf.RoundToInt(contentWidth * BG_HORIZONTAL_SPACING_RATIO);  // 3%
            int paddingV = Mathf.RoundToInt(bodyHeight * BG_VERTICAL_SPACING_RATIO);       // 5%

            // Calculate zone spacing = 25% of zone height
            // bodyContentHeight = bodyHeight - 2*paddingV, zones share equally minus spacing
            // zoneHeight ≈ bodyContentHeight / 2.25 (accounting for 25% spacing)
            float bodyContentHeight = bodyHeight - 2 * paddingV;
            float estimatedZoneHeight = bodyContentHeight / 2.25f;
            int zoneSpacing = Mathf.RoundToInt(estimatedZoneHeight * ZONE_SPACING_RATIO);  // 25% of zone height

            // Vertical Layout for Zone B & C
            var bodyLayout = mainBody.AddComponent<VerticalLayoutGroup>();
            bodyLayout.spacing = zoneSpacing;  // 25% of zone height spacing between Zone B and Zone C
            bodyLayout.padding = new RectOffset(paddingH, paddingH, paddingV, paddingV);  // 3% horizontal, 5% vertical
            bodyLayout.childControlHeight = true;
            bodyLayout.childControlWidth = true;
            bodyLayout.childForceExpandHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childAlignment = TextAnchor.MiddleCenter;

            // Add hover logic to background
            AttachHoverEvents(mainBody);

            // === ZONE B: INFO (Title & Timeline) ===
            // bodyHeight is also the preview frame height
            CreateZoneB(mainBody.transform, bodyHeight);

            // === ZONE C: CONTROLS (Audio, Playback, Advanced) ===
            CreateZoneC(mainBody.transform);
        }

        /// <summary>
        /// Zone A: Header row with 5 round buttons, outside the main background.
        /// </summary>
        private void CreateZoneA(Transform parent, float contentWidth)
        {
            GameObject zoneA = new GameObject("ZoneA_Header");
            zoneA.transform.SetParent(parent, false);

            var le = zoneA.AddComponent<LayoutElement>();
            le.minHeight = ZONE_HEIGHT;
            le.preferredHeight = ZONE_HEIGHT;
            le.minWidth = contentWidth;
            le.preferredWidth = contentWidth;

            // Horizontal Layout - Distribute buttons with space between
            var layout = zoneA.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 0; // No direct spacing, using FlexibleSpacers
            // NO horizontal padding - buttons touch edges
            int verticalPadding = Mathf.RoundToInt((ZONE_HEIGHT - HEADER_BUTTON_SIZE) / 2f);
            layout.padding = new RectOffset(0, 0, verticalPadding, verticalPadding);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;  // Must be TRUE for spacers' flexibleWidth to work
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false; // Don't force buttons to expand, only spacers
            layout.childForceExpandHeight = false;

            // Button 1: Exit (Left)
            _backButton = CreateRoundIconButton(zoneA.transform, ICON_BACK, HEADER_BUTTON_SIZE);
            _backButton.onClick.AddListener(() => OnBackClicked?.Invoke());

            // Spacer between button 1 and 2
            CreateFlexibleSpacer(zoneA.transform);

            // Button 2: A-B Loop
            var abLoopBtn = CreateRoundIconButton(zoneA.transform, ICON_AB_LOOP, HEADER_BUTTON_SIZE);
            abLoopBtn.onClick.AddListener(() => OnVRModeClicked?.Invoke());

            // Spacer between button 2 and 3
            CreateFlexibleSpacer(zoneA.transform);

            // Button 3: Recenter
            _recenterButton = CreateRoundIconButton(zoneA.transform, ICON_RECENTER, HEADER_BUTTON_SIZE);
            _recenterButton.onClick.AddListener(() => OnRecenterClicked?.Invoke());

            // Spacer between button 3 and 4
            CreateFlexibleSpacer(zoneA.transform);

            // Button 4: Repeat
            var repeatBtn = CreateRoundIconButton(zoneA.transform, ICON_REPEAT, HEADER_BUTTON_SIZE);
            repeatBtn.onClick.AddListener(() => OnHeadsetModeClicked?.Invoke());

            // Spacer between button 4 and 5
            CreateFlexibleSpacer(zoneA.transform);

            // Button 5: Settings (Right)
            _settingsButton = CreateRoundIconButton(zoneA.transform, ICON_SETTINGS, HEADER_BUTTON_SIZE);
            var settingsIconObj = _settingsButton.transform.Find("IconImage");
            if (settingsIconObj != null) _settingsButtonIconImage = settingsIconObj.GetComponent<Image>();
            _settingsButtonBgImage = _settingsButton.GetComponent<Image>();
            _settingsButton.onClick.AddListener(ToggleSettingsPanel);
        }

        /// <summary>
        /// Zone B: Info section with Title (MarqueeText) and Timeline.
        /// </summary>
        private void CreateZoneB(Transform parent, float previewHeight)
        {
            GameObject zoneB = new GameObject("ZoneB_Info");
            zoneB.transform.SetParent(parent, false);

            var le = zoneB.AddComponent<LayoutElement>();
            le.flexibleHeight = 1.3f; // Share space equally with Zone C (increased from 1.0 to give more room)

            var vLayout = zoneB.AddComponent<VerticalLayoutGroup>();
            vLayout.spacing = 0;  // No spacing - title expands, timeline at bottom
            vLayout.padding = new RectOffset(0, 0, 0, 40); // 40px bottom padding to lift slider away from Zone C
            vLayout.childControlHeight = true;
            vLayout.childForceExpandHeight = false;  // Don't force expand - let flexibleHeight work
            vLayout.childControlWidth = true;
            vLayout.childForceExpandWidth = true;
            vLayout.childAlignment = TextAnchor.UpperCenter;  // Align to top, timeline will be pushed to bottom

            // 3% spacing (same as timeline row)
            float horizontalSpacing = _width * BG_HORIZONTAL_SPACING_RATIO;

            // --- Row 1: Title Row (same structure as Timeline Row for alignment) ---
            GameObject titleRow = new GameObject("TitleRow");
            titleRow.transform.SetParent(zoneB.transform, false);

            var titleRowLE = titleRow.AddComponent<LayoutElement>();
            titleRowLE.minHeight = 60f;  // Fixed height for title row (fontSize 40)
            titleRowLE.preferredHeight = 60f;

            var titleRowLayout = titleRow.AddComponent<HorizontalLayoutGroup>();
            titleRowLayout.childAlignment = TextAnchor.MiddleCenter;
            titleRowLayout.childControlWidth = true;
            titleRowLayout.childControlHeight = true;
            titleRowLayout.childForceExpandWidth = false;
            titleRowLayout.childForceExpandHeight = true;
            titleRowLayout.spacing = horizontalSpacing;  // Same spacing as timeline

            // Left spacer (same width as currentTime label = 110)
            GameObject leftSpacer = new GameObject("LeftSpacer");
            leftSpacer.transform.SetParent(titleRow.transform, false);
            var leftSpacerLE = leftSpacer.AddComponent<LayoutElement>();
            leftSpacerLE.minWidth = 110;
            leftSpacerLE.preferredWidth = 110;

            // Title text (flexible width, same as slider)
            GameObject titleTextObj = new GameObject("TitleText");
            titleTextObj.transform.SetParent(titleRow.transform, false);

            var titleTextLE = titleTextObj.AddComponent<LayoutElement>();
            titleTextLE.flexibleWidth = 1;  // Same as slider - takes remaining space

            _titleText = titleTextObj.AddComponent<TextMeshProUGUI>();
            _titleText.text = "Video Title";
            _titleText.font = _font;
            _titleText.fontSize = 40;
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.color = Color.white;
            _titleText.alignment = TextAlignmentOptions.MidlineLeft;  // MidlineLeft for correct MarqueeText positioning
            _titleText.textWrappingMode = TextWrappingModes.NoWrap;
            _titleText.overflowMode = TextOverflowModes.Ellipsis;

            // Add MarqueeText behavior with centerWhenFits for proper centering
            _titleMarquee = MarqueeText.Setup(_titleText, 80f, centerWhenFits: true);

            // Right spacer (same width as totalTime label = 110)
            GameObject rightSpacer = new GameObject("RightSpacer");
            rightSpacer.transform.SetParent(titleRow.transform, false);
            var rightSpacerLE = rightSpacer.AddComponent<LayoutElement>();
            rightSpacerLE.minWidth = 110;
            rightSpacerLE.preferredWidth = 110;

            // --- Spacer to push timeline down (fills remaining space) ---
            GameObject bottomSpacer = new GameObject("BottomSpacer");
            bottomSpacer.transform.SetParent(zoneB.transform, false);
            var bottomSpacerLE = bottomSpacer.AddComponent<LayoutElement>();
            bottomSpacerLE.flexibleHeight = 1f;  // Takes all remaining space, pushing timeline to bottom

            // --- Row 2: Timeline (Fixed height at bottom) ---
            GameObject timelineRow = new GameObject("TimelineRow");
            timelineRow.transform.SetParent(zoneB.transform, false);

            var timelineRowLE = timelineRow.AddComponent<LayoutElement>();
            timelineRowLE.minHeight = 50f;  // Fixed height for timeline row
            timelineRowLE.preferredHeight = 50f;

            var timelineLayout = timelineRow.AddComponent<HorizontalLayoutGroup>();
            timelineLayout.childAlignment = TextAnchor.MiddleCenter;
            timelineLayout.childControlWidth = true;  // Control width for flexible slider
            timelineLayout.childControlHeight = true;
            timelineLayout.childForceExpandWidth = false;
            timelineLayout.childForceExpandHeight = true; // Stretch children to fill row — slider track/handle at y=0.5, text uses MidlineRight/Left
            timelineLayout.spacing = horizontalSpacing;  // 3% spacing between elements
            timelineLayout.padding = new RectOffset(0, 0, 0, 0);  // No extra padding - time labels at edges

            // Current time (at left edge)
            _currentTimeText = CreateText(timelineRow.transform, "00:00:00", 36, TextAlignmentOptions.MidlineRight);
            _currentTimeText.enableAutoSizing = true;
            _currentTimeText.fontSizeMin = 16;
            _currentTimeText.fontSizeMax = 36;
            var tLe = _currentTimeText.gameObject.AddComponent<LayoutElement>();
            tLe.minWidth = 160;
            tLe.preferredWidth = 160;

            // Seek slider - flexible width to fill remaining space
            _seekSlider = VRSliderFactory.CreateTimelineSlider(timelineRow.transform, 100, _font, THEME_COLOR, previewHeight);
            var sLe = _seekSlider.gameObject.AddComponent<LayoutElement>();
            sLe.flexibleWidth = 1;  // Expand to fill remaining space

            _seekSlider.OnValueChanged += OnSeekValueChanged;
            _seekSlider.OnDragStarted += OnSeekStart;
            _seekSlider.OnDragEnded += OnSeekEnd;
            // Slider value is normalized (0-1), so multiply by duration for display
            _seekSlider.OnFormatPreview = val => {
                float seconds = val * _duration;
                if (seconds < 3600f)
                {
                    int m = (int)(seconds / 60);
                    int s = (int)(seconds % 60);
                    return $"{m:D2}:{s:D2}";
                }
                return FormatTime(seconds);
            };
            AttachHoverEvents(_seekSlider.gameObject);

            // Double track and fill thickness + set fill color to hover button color
            var seekTrackBg = _seekSlider.transform.Find("TrackBackground");
            if (seekTrackBg != null)
            {
                var trt = seekTrackBg.GetComponent<RectTransform>();
                trt.sizeDelta = new Vector2(trt.sizeDelta.x, trt.sizeDelta.y * 2f);
            }
            var seekFill = _seekSlider.transform.Find("Fill");
            if (seekFill != null)
            {
                var frt = seekFill.GetComponent<RectTransform>();
                frt.sizeDelta = new Vector2(frt.sizeDelta.x, frt.sizeDelta.y * 2f);
                var fillImage = seekFill.GetComponent<Image>();
                if (fillImage != null)
                {
                    fillImage.color = new Color(
                        Mathf.Lerp(THEME_COLOR.r, 1f, 0.15f),
                        Mathf.Lerp(THEME_COLOR.g, 1f, 0.15f),
                        Mathf.Lerp(THEME_COLOR.b, 1f, 0.15f), 1f);
                }
            }

            // Total time (at right edge)
            _totalTimeText = CreateText(timelineRow.transform, "00:00:00", 36, TextAlignmentOptions.MidlineLeft);
            _totalTimeText.enableAutoSizing = true;
            _totalTimeText.fontSizeMin = 16;
            _totalTimeText.fontSizeMax = 36;
            var ttLe = _totalTimeText.gameObject.AddComponent<LayoutElement>();
            ttLe.minWidth = 160;
            ttLe.preferredWidth = 160;
        }

        /// <summary>
        /// Zone C: Controls section with Volume, Playback, and Advanced buttons.
        /// Uses group containers for Left (Volume), Center (Playback), Right (Advanced).
        /// </summary>
        private void CreateZoneC(Transform parent)
        {
            GameObject zoneC = new GameObject("ZoneC_Controls");
            zoneC.transform.SetParent(parent, false);

            var le = zoneC.AddComponent<LayoutElement>();
            le.flexibleHeight = 1.2f; // Share space (increased from 1.0)

            // Calculate Zone C height for button sizing
            float bodyHeight = TOTAL_HEIGHT - TOP_SPACER - ZONE_HEIGHT - ZONE_A_BOTTOM_MARGIN;
            float bodyContentHeight = bodyHeight - 2 * (bodyHeight * BG_VERTICAL_SPACING_RATIO);

            // Estimate spacing roughly (Zone B 1.3 + Zone C 1.2 = 2.5 parts, plus ~0.25 part spacing)
            float zoneSpacing = bodyContentHeight * (0.25f / 2.75f);
            float zoneCHeight = (bodyContentHeight - zoneSpacing) * (1.2f / 2.5f);

            // Calculate button sizes based on Zone C height minus padding
            float verticalPadding = 20f; // Bottom padding
            float availableHeight = zoneCHeight - verticalPadding;

            float playButtonSize = availableHeight * 0.9f;     // 90% of available height
            float otherButtonSize = playButtonSize * 0.5f;     // 50% of play button size

            // 3% horizontal spacing (used within groups)
            float horizontalSpacing = _width * BG_HORIZONTAL_SPACING_RATIO;

            // Main layout - no spacing between groups, FlexibleSpacers handle distribution
            var layout = zoneC.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 0;  // No spacing at main level - groups handle internal spacing
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = false;
            layout.padding = new RectOffset(0, 0, 0, (int)verticalPadding); // Add bottom padding

            // Calculate group width for symmetric layout (ensures CenterGroup is centered)
            // LeftGroup: button + spacing + slider = otherButtonSize + spacing + 300
            // RightGroup: button + spacing + button = otherButtonSize + spacing + otherButtonSize
            float leftGroupWidth = otherButtonSize + horizontalSpacing + 300f;
            float rightGroupWidth = otherButtonSize + horizontalSpacing + otherButtonSize;
            float symmetricWidth = Mathf.Max(leftGroupWidth, rightGroupWidth);

            // === Left Group Container: Volume button + Slider ===
            GameObject leftGroup = new GameObject("LeftGroup_Volume");
            leftGroup.transform.SetParent(zoneC.transform, false);

            var leftGroupLE = leftGroup.AddComponent<LayoutElement>();
            leftGroupLE.minWidth = symmetricWidth;  // Match right group for symmetric layout

            var leftLayout = leftGroup.AddComponent<HorizontalLayoutGroup>();
            leftLayout.spacing = horizontalSpacing;  // 3% spacing between volume button and slider
            leftLayout.childAlignment = TextAnchor.MiddleLeft;  // Align content to left within group
            leftLayout.childControlWidth = false;
            leftLayout.childForceExpandWidth = false;

            _volumeButton = CreateIconOnlyButton(leftGroup.transform, ICON_VOLUME, otherButtonSize);
            _volumeButton.onClick.AddListener(ToggleMute);
            _volumeIcon = _volumeButton.transform.Find("IconImage")?.GetComponent<Image>();

            _volumeSlider = VRSliderFactory.CreateVolumeSlider(leftGroup.transform, 300, _font, THEME_COLOR);
            _volumeSlider.OnValueChanged += (v) =>
            {
                SetVolume(v); // Update _volume + icon + persist to PlayerPrefs
                OnVolumeChanged?.Invoke(v);
            };
            AttachHoverEvents(_volumeSlider.gameObject);

            // Double track and fill thickness + set fill color to hover button color
            var volTrackBg = _volumeSlider.transform.Find("TrackBackground");
            if (volTrackBg != null)
            {
                var trt = volTrackBg.GetComponent<RectTransform>();
                trt.sizeDelta = new Vector2(trt.sizeDelta.x, trt.sizeDelta.y * 2f);
            }
            var volFill = _volumeSlider.transform.Find("Fill");
            if (volFill != null)
            {
                var frt = volFill.GetComponent<RectTransform>();
                frt.sizeDelta = new Vector2(frt.sizeDelta.x, frt.sizeDelta.y * 2f);
                var fillImage = volFill.GetComponent<Image>();
                if (fillImage != null)
                {
                    fillImage.color = new Color(
                        Mathf.Lerp(THEME_COLOR.r, 1f, 0.15f),
                        Mathf.Lerp(THEME_COLOR.g, 1f, 0.15f),
                        Mathf.Lerp(THEME_COLOR.b, 1f, 0.15f), 1f);
                }
            }

            // Flexible spacer to push center group
            CreateFlexibleSpacer(zoneC.transform);

            // === Center Group Container: Playback controls ===
            GameObject centerGroup = new GameObject("CenterGroup_Playback");
            centerGroup.transform.SetParent(zoneC.transform, false);
            var centerLayout = centerGroup.AddComponent<HorizontalLayoutGroup>();
            centerLayout.spacing = horizontalSpacing;  // 3% spacing between prev, play, next
            centerLayout.childAlignment = TextAnchor.MiddleCenter;
            centerLayout.childControlWidth = false;
            centerLayout.childForceExpandWidth = false;

            float seekButtonSize = otherButtonSize * 1.3f; // 30% larger for Backward/Forward
            _prevButton = CreateIconOnlyButton(centerGroup.transform, ICON_BACKWARD, seekButtonSize);
            _prevButton.onClick.AddListener(() => SeekRelative(-10f));

            // Play/Pause button: full Zone C height, icon includes circle built-in
            _playPauseButton = CreateIconOnlyButton(centerGroup.transform, ICON_PLAY, playButtonSize, true);
            _playPauseButton.onClick.AddListener(() => OnPlayPause?.Invoke());
            _playPauseIcon = _playPauseButton.transform.Find("IconImage")?.GetComponent<Image>();

            _nextButton = CreateIconOnlyButton(centerGroup.transform, ICON_FORWARD, seekButtonSize);
            _nextButton.onClick.AddListener(() => SeekRelative(10f));

            // Flexible spacer to push right group
            CreateFlexibleSpacer(zoneC.transform);

            // === Right Group Container: Environment + 3D ===
            GameObject rightGroup = new GameObject("RightGroup_Advanced");
            rightGroup.transform.SetParent(zoneC.transform, false);

            var rightGroupLE = rightGroup.AddComponent<LayoutElement>();
            rightGroupLE.minWidth = symmetricWidth;  // Match left group for symmetric layout

            var rightLayout = rightGroup.AddComponent<HorizontalLayoutGroup>();
            rightLayout.spacing = horizontalSpacing;  // 3% spacing between environment and 3D
            rightLayout.childAlignment = TextAnchor.MiddleRight;  // Align content to right within group (changed from MiddleCenter)
            rightLayout.childControlWidth = false;
            rightLayout.childForceExpandWidth = false;

            float advancedButtonSize = otherButtonSize * 1.2f; // 20% larger than standard buttons
            Button envBtn = CreateIconOnlyButton(rightGroup.transform, ICON_ENVIRONMENT, advancedButtonSize);
            envBtn.onClick.AddListener(() => OnEnvironmentClicked?.Invoke());

            Button threeDBtn = CreateIconOnlyButton(rightGroup.transform, ICON_3D, advancedButtonSize);
            threeDBtn.onClick.AddListener(() => OnVRModeClicked?.Invoke());
        }

        /// <summary>
        /// Creates a round icon button for Zone A header (BareIconButton style).
        /// Uses icon sprite instead of text.
        /// </summary>
        private Button CreateRoundIconButton(Transform parent, string iconName, float size)
        {
            GameObject buttonObj = new GameObject($"Btn_{iconName}");
            buttonObj.transform.SetParent(parent, false);

            var buttonLE = buttonObj.AddComponent<LayoutElement>();
            buttonLE.minWidth = size;
            buttonLE.minHeight = size;
            buttonLE.preferredWidth = size;
            buttonLE.preferredHeight = size;

            // Round background - dark transparent with circular sprite
            var bgImage = buttonObj.AddComponent<Image>();
            bgImage.sprite = GetCircleSprite();
            bgImage.color = HEADER_BTN_BG_COLOR;
            bgImage.type = Image.Type.Simple;

            var button = buttonObj.AddComponent<Button>();
            button.targetGraphic = bgImage;

            // Icon Image (instead of text)
            GameObject iconObj = new GameObject("IconImage");
            iconObj.transform.SetParent(buttonObj.transform, false);
            var iconRT = iconObj.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.2f, 0.2f);
            iconRT.anchorMax = new Vector2(0.8f, 0.8f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;

            var iconImage = iconObj.AddComponent<Image>();
            iconImage.sprite = Resources.Load<Sprite>(iconName);
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false; // Only button root handles raycasts

            // Hover effects - scale + subtle color tint on icon (white → light pastel accent)
            var hoverController = buttonObj.AddComponent<HoverEffectController>();
            hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.15f));
            hoverController.AddEffect(new ColorHoverEffect()
                .WithTargetChild("IconImage")
                .WithHoverColor(new Color(
                    Mathf.Lerp(THEME_COLOR.r, 1f, 0.15f),
                    Mathf.Lerp(THEME_COLOR.g, 1f, 0.15f),
                    Mathf.Lerp(THEME_COLOR.b, 1f, 0.15f),
                    1f)));

            var collider = buttonObj.AddComponent<BoxCollider>();
            collider.size = new Vector3(size, size, 10);
            collider.center = new Vector3(0, 0, -5);

            AttachHoverEvents(buttonObj);
            return button;
        }

        /// <summary>
        /// Creates an icon-only button (no background) for Zone C controls.
        /// Uses icon sprite instead of text.
        /// </summary>
        /// <param name="iconSize">Optional: specific icon size for primary button (Play/Pause) where button and icon sizes differ</param>
        private Button CreateIconOnlyButton(Transform parent, string iconName, float size, bool isPrimary = false, float iconSize = 0f)
        {
            GameObject buttonObj = new GameObject($"Btn_{iconName}");
            buttonObj.transform.SetParent(parent, false);

            var buttonLE = buttonObj.AddComponent<LayoutElement>();
            buttonLE.minWidth = size;
            buttonLE.minHeight = size;
            buttonLE.preferredWidth = size;
            buttonLE.preferredHeight = size;

            Image bgImage;

            // Add RectTransform with fixed size to maintain aspect ratio
            var buttonRT = buttonObj.GetComponent<RectTransform>() ?? buttonObj.AddComponent<RectTransform>();
            buttonRT.sizeDelta = new Vector2(size, size);

            // Transparent background (icon-only) — play/pause icons include circle built-in
            bgImage = buttonObj.AddComponent<Image>();
            bgImage.color = Color.clear;

            var button = buttonObj.AddComponent<Button>();
            button.targetGraphic = bgImage;

            // Icon Image (instead of text)
            GameObject iconObj = new GameObject("IconImage");
            iconObj.transform.SetParent(buttonObj.transform, false);
            var iconRT = iconObj.AddComponent<RectTransform>();

            if (isPrimary)
            {
                // Icon fills full button (play/pause icons include circle built-in)
                iconRT.anchorMin = Vector2.zero;
                iconRT.anchorMax = Vector2.one;
                iconRT.offsetMin = Vector2.zero;
                iconRT.offsetMax = Vector2.zero;
            }
            else
            {
                iconRT.anchorMin = new Vector2(0.1f, 0.1f);
                iconRT.anchorMax = new Vector2(0.9f, 0.9f);
                iconRT.offsetMin = Vector2.zero;
                iconRT.offsetMax = Vector2.zero;
            }

            var iconImage = iconObj.AddComponent<Image>();
            iconImage.sprite = Resources.Load<Sprite>(iconName);
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false; // Only button root handles raycasts

            // Hover effects - scale + subtle color tint on icon (white → light pastel accent)
            var hoverController = buttonObj.AddComponent<HoverEffectController>();
            hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.1f));
            hoverController.AddEffect(new ColorHoverEffect()
                .WithTargetChild("IconImage")
                .WithHoverColor(new Color(
                    Mathf.Lerp(THEME_COLOR.r, 1f, 0.15f),
                    Mathf.Lerp(THEME_COLOR.g, 1f, 0.15f),
                    Mathf.Lerp(THEME_COLOR.b, 1f, 0.15f),
                    1f)));

            var collider = buttonObj.AddComponent<BoxCollider>();
            collider.size = new Vector3(size, size, 10);
            collider.center = new Vector3(0, 0, -5);

            AttachHoverEvents(buttonObj);
            return button;
        }

        private TextMeshProUGUI CreateText(Transform parent, string content, float fontSize, TextAlignmentOptions alignment)
        {
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(parent, false);

            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.font = _font;
            text.fontStyle = FontStyles.Bold;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;

            return text;
        }

        /// <summary>
        /// Creates a circular sprite for round buttons.
        /// </summary>
        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;

            int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] colors = new Color[size * size];

            float center = size / 2f;
            float radius = size / 2f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01(radius - dist + 0.5f); // Anti-aliased edge
                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;

            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
            return _circleSprite;
        }

        /// <summary>
        /// Creates a circular outline sprite for the play/pause button.
        /// </summary>
        private static Sprite GetCircleOutlineSprite()
        {
            if (_circleOutlineSprite != null) return _circleOutlineSprite;

            int size = 256;
            float strokeWidth = 10f;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] colors = new Color[size * size];

            float center = size / 2f;
            float outerRadius = size / 2f - 1f;
            float innerRadius = outerRadius - strokeWidth;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));

                    // Smooth anti-aliasing (high-res texture ensures quality)
                    float outerAlpha = Mathf.Clamp01(outerRadius - dist + 0.5f);
                    float innerAlpha = Mathf.Clamp01(dist - innerRadius + 0.5f);

                    float alpha = Mathf.Min(outerAlpha, innerAlpha);
                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;

            _circleOutlineSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
            return _circleOutlineSprite;
        }

        /// <summary>
        /// Creates a rounded rectangle sprite for background with 9-slice support.
        /// </summary>
        public static Sprite CreateRoundedRectSprite(float cornerRadius)
        {
            int size = 64;
            int radius = Mathf.RoundToInt(cornerRadius * size / 100f); // Scale radius
            if (radius < 4) radius = 4;
            if (radius > size / 2 - 1) radius = size / 2 - 1;

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] colors = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = 1f;

                    // Check corners
                    Vector2 cornerCenter = Vector2.zero;
                    bool isCorner = false;

                    // Bottom-left corner
                    if (x < radius && y < radius)
                    {
                        cornerCenter = new Vector2(radius, radius);
                        isCorner = true;
                    }
                    // Bottom-right corner
                    else if (x >= size - radius && y < radius)
                    {
                        cornerCenter = new Vector2(size - radius - 1, radius);
                        isCorner = true;
                    }
                    // Top-left corner
                    else if (x < radius && y >= size - radius)
                    {
                        cornerCenter = new Vector2(radius, size - radius - 1);
                        isCorner = true;
                    }
                    // Top-right corner
                    else if (x >= size - radius && y >= size - radius)
                    {
                        cornerCenter = new Vector2(size - radius - 1, size - radius - 1);
                        isCorner = true;
                    }

                    if (isCorner)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), cornerCenter);
                        alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    }

                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;

            // Create 9-sliced sprite with borders at the corner radius
            Vector4 border = new Vector4(radius + 1, radius + 1, radius + 1, radius + 1);
            return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, border);
        }

        private void CreateFlexibleSpacer(Transform parent)
        {
            GameObject spacer = new GameObject("FlexibleSpacer");
            spacer.transform.SetParent(parent, false);

            var le = spacer.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;
        }

        // Helper for mute toggle
        private void ToggleMute()
        {
            if (_volume > 0)
            {
                _previousVolume = _volume;
                SetVolume(0f);
                OnVolumeChanged?.Invoke(0f);
            }
            else
            {
                float restore = _previousVolume > 0 ? _previousVolume : 1f;
                SetVolume(restore);
                OnVolumeChanged?.Invoke(restore);
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Set external world-space objects for overlay dismiss and menu button toggle.
        /// </summary>
        public void SetExternalFrames(GameObject overlayFrame, GameObject menuButtonFrame)
        {
            _overlayFrameObject = overlayFrame;
            _menuButtonFrameObject = menuButtonFrame;
        }

        /// <summary>
        /// Set the side controls frame reference for synchronized visibility.
        /// </summary>
        public void SetSideControlsFrame(GameObject sideFrame)
        {
            _sideControlsFrameObject = sideFrame;
        }

        /// <summary>
        /// Set the queue pagination reference for synchronized visibility.
        /// </summary>
        public void SetQueuePagination(RTTFilePagination pagination)
        {
            _queuePagination = pagination;
        }

        /// <summary>
        /// Set the video title.
        /// </summary>
        public void SetTitle(string title)
        {
            // Use MarqueeText.SetText() for proper centering (calls CheckOverflow and UpdateTextPosition)
            if (_titleMarquee != null)
                _titleMarquee.SetText(title);
            else if (_titleText != null)
                _titleText.text = title;
        }
        /// <summary>
        /// Update playback state display.
        /// </summary>
        public void SetPlayState(bool isPlaying)
        {
            IsPlaying = isPlaying;

            // Update play/pause button icon
            if (_playPauseIcon != null)
            {
                string iconName = isPlaying ? ICON_PAUSE : ICON_PLAY;
                _playPauseIcon.sprite = Resources.Load<Sprite>(iconName);
            }

        }

        /// <summary>
        /// Set references for settings panel toggle.
        /// </summary>
        public void SetSettingsPanel(RTTMediaSettingsPanel settingsPanel, GameObject queuePanelObject,
            GameObject settingsFrameObject, GameObject sideControlsFrameObject)
        {
            _settingsPanel = settingsPanel;
            _queuePanelObject = queuePanelObject;
            _settingsFrameObject = settingsFrameObject;
            _sideControlsFrameObject = sideControlsFrameObject;
        }

        /// <summary>
        /// Set settings button selected state (icon color).
        /// Updates ColorHoverEffect's original color so it doesn't reset on pointer exit.
        /// </summary>
        public void SetSettingsButtonSelected(bool selected)
        {
            // Selected = same as hover color (THEME_COLOR blended with 15% white)
            Color iconColor = selected ? new Color(
                Mathf.Lerp(THEME_COLOR.r, 1f, 0.15f),
                Mathf.Lerp(THEME_COLOR.g, 1f, 0.15f),
                Mathf.Lerp(THEME_COLOR.b, 1f, 0.15f),
                1f) : Color.white;
            if (_settingsButtonIconImage != null)
                _settingsButtonIconImage.color = iconColor;

            // Update ColorHoverEffect's original color so pointer exit restores to correct color
            var hoverCtrl = _settingsButton?.GetComponent<HoverEffectController>();
            if (hoverCtrl != null)
            {
                var colorEffect = hoverCtrl.GetEffect("color") as ColorHoverEffect;
                colorEffect?.SetOriginalColor(iconColor);
            }
        }

        /// <summary>
        /// Reset side panel to Queue view (hide Settings). Called when opening a new video.
        /// </summary>
        public void ResetToQueueView()
        {
            _settingsActive = false;
            SetSettingsButtonSelected(false);
            if (_settingsFrameObject != null) _settingsFrameObject.SetActive(false);
            if (_sideControlsFrameObject != null) _sideControlsFrameObject.SetActive(true);
            if (_queuePagination != null) _queuePagination.ShowImmediate();
        }

        /// <summary>
        /// Toggle settings panel visibility. Hides queue when showing settings and vice versa.
        /// </summary>
        private void ToggleSettingsPanel()
        {
            _settingsActive = !_settingsActive;
            SetSettingsButtonSelected(_settingsActive);

            if (_settingsActive)
            {
                // Hide Queue frame, show Settings frame
                if (_sideControlsFrameObject != null) _sideControlsFrameObject.SetActive(false);
                if (_settingsFrameObject != null) _settingsFrameObject.SetActive(true);
                if (_settingsPanel != null) _settingsPanel.NavigateToMainMenu();
                if (_queuePagination != null) _queuePagination.HideImmediate();
            }
            else
            {
                // Show Queue frame, hide Settings frame
                if (_settingsFrameObject != null) _settingsFrameObject.SetActive(false);
                if (_sideControlsFrameObject != null) _sideControlsFrameObject.SetActive(true);
                if (_queuePagination != null) _queuePagination.ShowImmediate();
            }

            OnSettingsClicked?.Invoke();
        }

        private float _nextAllowedUpdateTime = 0f; // Prevent older time updates from overriding user seek

        /// <summary>
        /// Update current playback time.
        /// </summary>
        public void SetCurrentTime(float seconds)
        {
            if (_isSeeking) return;

            // If user just sought, ignore updates from engine for a moment 
            // to prevent slider jumping back to old time before seek completes
            if (Time.time < _nextAllowedUpdateTime) return;

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

            // Set timeline step: 0.125s minimum granularity (normalized)
            if (_seekSlider != null && _duration > 0)
            {
                _seekSlider.SetStep(0.125f / _duration);
            }
        }

        /// <summary>
        /// Set volume level (0-1).
        /// </summary>
        public void SetVolume(float volume)
        {
            _volume = Mathf.Clamp01(volume);
            _volumeSlider?.SetValueWithoutNotify(_volume);

            // Update volume icon (mute vs normal)
            if (_volumeIcon != null)
            {
                string iconName = _volume <= 0 ? ICON_VOLUME_MUTE : ICON_VOLUME;
                _volumeIcon.sprite = Resources.Load<Sprite>(iconName);
            }

            // Persist volume (save actual level, not muted state)
            if (_volume > 0)
            {
                PlayerPrefs.SetFloat(PREF_VOLUME, _volume);
            }
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
        /// Set the aspect ratio for the seek bar preview frame.
        /// </summary>
        public void SetPreviewAspectRatio(float ratio)
        {
            _seekSlider?.SetPreviewAspectRatio(ratio);
        }

        /// <summary>
        /// Set the texture for the seek bar preview frame (e.g. video frame).
        /// </summary>
        public void SetPreviewTexture(Texture texture)
        {
            _seekSlider?.SetPreviewImage(texture);
        }

        /// <summary>
        /// Show the controls panel.
        /// </summary>
        public void Show()
        {
            Debug.Log($"[RTTCP_DBG-F] Show() called, gameObject.activeSelf={gameObject.activeSelf}, gameObject.activeInHierarchy={gameObject.activeInHierarchy}");
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }
            _fadeCoroutine = StartCoroutine(FadeIn());
            IsVisible = true;
            Debug.Log($"[RTTCP_DBG-F] Show() set IsVisible=true");

            // Re-enable parent frame's display quad collider (was disabled on hide)
            SetParentFrameColliderEnabled(true);

            // Show overlay, hide menu button (controls visible → overlay catches dismiss clicks)
            if (_overlayFrameObject != null) _overlayFrameObject.SetActive(true);
            if (_menuButtonFrameObject != null) _menuButtonFrameObject.SetActive(false);
            if (_sideControlsFrameObject != null) _sideControlsFrameObject.SetActive(true);
            // Restore correct side panel state (Queue or Settings)
            if (_settingsActive)
            {
                if (_sideControlsFrameObject != null) _sideControlsFrameObject.SetActive(false);
                _settingsFrameObject?.SetActive(true);
                // Hide pagination when settings is active
            }
            else
            {
                _settingsFrameObject?.SetActive(false);
                if (_sideControlsFrameObject != null) _sideControlsFrameObject.SetActive(true);
                if (_queuePagination != null) _queuePagination.ShowImmediate();
            }

            OnVisibilityChanged?.Invoke(true);
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

            // KEEP overlay ACTIVE so DismissButton.onClick can fire as wake-up trigger
            // when user clicks in empty area while controls are hidden. The menu button
            // is hidden in Mouse/Gamepad mode by VRMediaAppController.ApplyMenuButtonVisibilityByMode.
            if (_menuButtonFrameObject != null) _menuButtonFrameObject.SetActive(true);
            if (_sideControlsFrameObject != null) _sideControlsFrameObject.SetActive(false);
            if (_queuePagination != null) _queuePagination.HideImmediate();
            // Also hide settings frame when controls hide
            _settingsFrameObject?.SetActive(false);

            OnVisibilityChanged?.Invoke(false);
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
        }

        #region Pointer Interfaces
        public void OnPointerEnter(PointerEventData eventData)
        {
        }

        public void OnPointerExit(PointerEventData eventData)
        {
        }
        #endregion
        #endregion

        #region Unity Lifecycle
        private void Update()
        {
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

        #region Private Methods
        private void AttachHoverEvents(GameObject go)
        {
            if (go == null) return;
            var relay = go.AddComponent<HoverEventRelay>();
            relay.Initialize(this);
        }

        /// <summary>
        /// Seek forward or backward by a relative number of seconds.
        /// </summary>
        private void SeekRelative(float seconds)
        {
            if (_duration <= 0) return;
            float newTime = Mathf.Clamp(_currentTime + seconds, 0f, _duration);
            float normalized = newTime / _duration;
            _seekSlider.SetValueWithoutNotify(normalized);
            _currentTimeText.text = FormatTime(newTime);
            _nextAllowedUpdateTime = Time.time + 3.0f;
            OnSeek?.Invoke(newTime);
        }

        private void OnSeekValueChanged(float normalizedValue)
        {
            // Update time display
            _currentTimeText.text = FormatTime(normalizedValue * _duration);

            // If interactive seek (click/drag), update tooltip
            if (_isSeeking)
            {
                // Just update text while dragging
            }
            else
            {
                // Immediate seek on click (not dragging)
                _nextAllowedUpdateTime = Time.time + 3.0f; // Fallback timeout - cleared early by OnSeekCompleted
                OnSeek?.Invoke(normalizedValue * _duration);
            }
        }

        private void OnSeekStart()
        {
            _isSeeking = true;
        }

        private void OnSeekEnd()
        {
            _isSeeking = false;
            _nextAllowedUpdateTime = Time.time + 3.0f; // Fallback timeout - cleared early by OnSeekCompleted
            // Invoke seek event with actual time
            OnSeek?.Invoke(_seekSlider.NormalizedValue * _duration);
        }

        /// <summary>
        /// Called by the controller when a seek operation completes.
        /// Unblocks time updates that were blocked during the seek.
        /// </summary>
        public void OnSeekCompleted()
        {
            _nextAllowedUpdateTime = 0f;
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

            // Always show HH:mm:ss format
            return $"{hours:D2}:{minutes:D2}:{secs:D2}";
        }

        private IEnumerator FadeIn()
        {
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;

            float elapsed = 0f;
            float startAlpha = _canvasGroup.alpha;

            while (elapsed < FADE_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / FADE_DURATION;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, t);
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
                float t = elapsed / FADE_DURATION;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
                yield return null;
            }

            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            // Disable parent frame's display quad collider so it doesn't block
            // Physics.Raycast from reaching the menu button behind it (at 3.5m).
            SetParentFrameColliderEnabled(false);
        }

        private void SetParentFrameColliderEnabled(bool enabled)
        {
            if (_parentFrameCollider == null)
            {
                var parentFrame = GetComponentInParent<RTTCanvasBase>();
                if (parentFrame != null)
                {
                    _parentFrameCollider = parentFrame.GetQuadCollider();
                }
            }
            if (_parentFrameCollider != null)
            {
                _parentFrameCollider.enabled = enabled;
            }
        }
        #endregion


        /// <summary>
        /// Calculates the distance from the bottom of the panel to the bottom of Zone B.
        /// Used to align external popups (like ProjectionPopup) with Zone B.
        /// </summary>
        public static float GetZoneBBottomOffset()
        {
            float bodyHeight = TOTAL_HEIGHT - TOP_SPACER - ZONE_HEIGHT - ZONE_A_BOTTOM_MARGIN; // 350
            float paddingV = bodyHeight * BG_VERTICAL_SPACING_RATIO; // 17.5
            float availableHeight = bodyHeight - (2 * paddingV); // 315

            float estimatedZoneHeight = availableHeight / 2.25f; // 140
            float zoneSpacing = Mathf.RoundToInt(estimatedZoneHeight * ZONE_SPACING_RATIO); // 35

            float relativeHeightB = 1.3f;
            float relativeHeightC = 1.2f;
            float totalRelative = relativeHeightB + relativeHeightC;

            float actualHeightAvailableForZones = availableHeight - zoneSpacing; // 280
            float heightC = actualHeightAvailableForZones * (relativeHeightC / totalRelative); // 134.4

            // Bottom of Zone B is (PaddingBottom + HeightC + Spacing) from bottom of panel background
            // Added +58 buffer to account for Zone B's internal bottom margin (40px) and spacing to feel correct
            return paddingV + heightC + zoneSpacing + 58f;
        }
    }

    public class HoverEventRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private RTTMediaControlsPanel _panel;
        public void Initialize(RTTMediaControlsPanel panel) => _panel = panel;

        public void OnPointerEnter(PointerEventData eventData) => _panel?.OnPointerEnter(eventData);
        public void OnPointerExit(PointerEventData eventData) => _panel?.OnPointerExit(eventData);
    }

}
