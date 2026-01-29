using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

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
    public event Action OnPlaylistClicked;
    #endregion

    #region Configuration
    [Header("Positioning")]
    [SerializeField] private float gapMultiplier = 0.05f;  // Gap between panel and action bar
    [SerializeField] private float barHeight = 0.08f;      // Height of action bar in world units
    #endregion

    #region Private Fields
    private Transform _followTarget;
    private float _targetHeight;
    private float _panelWidth;  // Width of the side panel
    private bool _initialized = false;

    // Buttons
    private GameObject _container;
    private Button _playButton;
    private Button _favouriteButton;
    private Button _playlistButton;
    private Image _favouriteIcon;
    private TMP_FontAsset _font;
    private bool _isFavourite = false;

    // Theme
    private Color _primaryColor;
    private Color _accentColor;
    #endregion

    #region Properties
    public bool IsInitialized => _initialized;
    public Image FavouriteIcon => _favouriteIcon;
    #endregion

    #region Lifecycle
    private void LateUpdate()
    {
        if (!_initialized || _followTarget == null) return;

        UpdateSpherePosition();
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

        CreateButtons(font);

        _initialized = true;
        Debug.Log($"[RTTMediaActionBar] Initialized, following: {_followTarget?.name}, panelWidth: {_panelWidth}");
    }

    /// <summary>
    /// Update favourite button visual state.
    /// </summary>
    public void UpdateFavouriteState(bool isFavourite)
    {
        _isFavourite = isFavourite;
        if (_favouriteIcon == null) return;

        Sprite icon = Resources.Load<Sprite>(isFavourite ? "icon_remove_favorite" : "icon_add_favorite");
        if (icon != null) _favouriteIcon.sprite = icon;
        _favouriteIcon.color = isFavourite ? _accentColor : Color.white;
    }

    /// <summary>
    /// Show/Hide the action bar.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (_container != null)
        {
            _container.SetActive(visible);
        }
    }
    #endregion

    #region Private Methods
    private void CreateButtons(TMP_FontAsset font)
    {
        // Create container for buttons
        _container = new GameObject("ActionButtonsContainer");
        _container.transform.SetParent(transform, false);

        // Add Canvas for UI buttons
        Canvas canvas = _container.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        // Button configuration
        float buttonHeight = 67.5f;
        float iconSize = 28f;
        float fontSize = 20;
        float spacing = 8f;          // Tighter spacing between icon and text
        float charWidth = 12f;
        float sidePadding = 25f;     // Padding on left/right edges of button content
        float buttonSpacing = 15f;   // Space between buttons on the bar

        // Button labels
        string playLabel = "Play";
        string favLabel = "Favorite";
        string playlistLabel = "Playlist";

        // Calculate width for each button: Icon + Gap + Text + Side Padding
        float playWidth = iconSize + spacing + (playLabel.Length * charWidth) + (sidePadding * 2);
        float favWidth = iconSize + spacing + (favLabel.Length * charWidth) + (sidePadding * 2);
        float playlistWidth = iconSize + spacing + (playlistLabel.Length * charWidth) + (sidePadding * 2);

        // Total width of all buttons + spacing between them
        float totalButtonsWidth = playWidth + favWidth + playlistWidth + (buttonSpacing * 2);

        // Canvas size based on panel width
        float logicalPanelWidth = _panelWidth * 1000f;
        float canvasWidth = logicalPanelWidth;
        float canvasHeight = buttonHeight + 20f;

        RectTransform canvasRT = _container.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(canvasWidth, canvasHeight);
        canvasRT.localScale = Vector3.one * 0.001f;

        // Add CanvasScaler and GraphicRaycaster
        _container.AddComponent<CanvasScaler>();
        _container.AddComponent<GraphicRaycaster>();

        // Center the button group within the canvas
        float groupStartX = -totalButtonsWidth / 2f;

        // Play Button (icon left, text right)
        float playX = groupStartX + playWidth / 2f;
        Sprite playIcon = Resources.Load<Sprite>("icon_play");
        var playConfig = new VRButtonFactory.ButtonConfig
        {
            label = playLabel,
            icon = playIcon,
            themeColor = _accentColor,
            width = playWidth,
            height = buttonHeight,
            horizontalLayout = true,
            iconSize = iconSize,
            fontSize = (int)fontSize,
            font = font,
            spacing = spacing,
            cornerRadius = 0.15f,    // Reduced corner radius (was 0.25f)
            borderWidth = 0.055f,    // Thicker border
            glowWidth = 0.06f,
            glowIntensity = 3f,
            popAmount = 0.03f
        };
        GameObject playBtn = VRButtonFactory.CreateButton(canvasRT, playConfig, () => OnPlayClicked?.Invoke());
        PositionButton(playBtn, playX);
        _playButton = playBtn.GetComponent<Button>();

        // Favourite Button (icon left, text right)
        float favX = groupStartX + playWidth + buttonSpacing + favWidth / 2f;
        Sprite favIcon = Resources.Load<Sprite>("icon_add_favorite");
        var favConfig = new VRButtonFactory.ButtonConfig
        {
            label = favLabel,
            icon = favIcon,
            themeColor = _primaryColor,
            width = favWidth,
            height = buttonHeight,
            horizontalLayout = true,
            iconSize = iconSize,
            fontSize = (int)fontSize,
            font = font,
            spacing = spacing,
            cornerRadius = 0.15f,    // Reduced corner radius
            borderWidth = 0.055f,
            glowWidth = 0.06f,
            glowIntensity = 3f,
            popAmount = 0.03f
        };
        GameObject favBtn = VRButtonFactory.CreateButton(canvasRT, favConfig, () => OnFavouriteClicked?.Invoke());
        PositionButton(favBtn, favX);
        _favouriteButton = favBtn.GetComponent<Button>();
        _favouriteIcon = favBtn.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();

        // Playlist Button (icon left, text right)
        float playlistX = groupStartX + playWidth + buttonSpacing + favWidth + buttonSpacing + playlistWidth / 2f;
        Sprite playlistIcon = Resources.Load<Sprite>("icon_add_playlist");
        var playlistConfig = new VRButtonFactory.ButtonConfig
        {
            label = playlistLabel,
            icon = playlistIcon,
            themeColor = _primaryColor,
            width = playlistWidth,
            height = buttonHeight,
            horizontalLayout = true,
            iconSize = iconSize,
            fontSize = (int)fontSize,
            font = font,
            spacing = spacing,
            cornerRadius = 0.15f,    // Reduced corner radius
            borderWidth = 0.055f,
            glowWidth = 0.06f,
            glowIntensity = 3f,
            popAmount = 0.03f
        };
        GameObject playlistBtn = VRButtonFactory.CreateButton(canvasRT, playlistConfig, () => OnPlaylistClicked?.Invoke());
        PositionButton(playlistBtn, playlistX);
        _playlistButton = playlistBtn.GetComponent<Button>();

        Debug.Log($"[RTTMediaActionBar] Buttons created - Play: {playWidth}px, Favorite: {favWidth}px, Playlist: {playlistWidth}px");
    }

    private void PositionButton(GameObject btn, float xOffset)
    {
        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(xOffset, 0);
    }

    private void UpdateSpherePosition()
    {
        Camera cam = Camera.main;
        if (cam == null || _followTarget == null) return;

        Vector3 cameraPos = cam.transform.position;
        Vector3 targetCenter = _followTarget.position;
        Vector3 targetUp = _followTarget.up;

        float targetHalfHeight = _targetHeight / 2f;
        float barHalfHeight = barHeight / 2f;
        
        // Gap is 10% of button height (closely matches Toolbar spacing)
        float buttonHeightWorld = 0.0675f;
        float gap = buttonHeightWorld * 0.1f;

        // Step 1: Calculate top edge position E (below panel bottom)
        Vector3 targetBottom = targetCenter - targetUp * targetHalfHeight;
        Vector3 E = targetBottom - Vector3.up * gap;

        // Step 2: Calculate vector from camera to E
        Vector3 toE = E - cameraPos;
        float distToE = toE.magnitude;

        float h = barHalfHeight;

        // Edge case: camera too close
        if (distToE < 0.001f || distToE < h)
        {
            // Fallback: simple linear positioning
            transform.position = targetBottom - Vector3.up * (gap + h);
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

    #region Static Factory
    /// <summary>
    /// Create RTTMediaActionBar in VirtualObjects.
    /// </summary>
    public static RTTMediaActionBar Create(Transform followTarget, float targetHeight, float panelWidth, Color primaryColor, Color accentColor, TMP_FontAsset font)
    {
        GameObject barObj = new GameObject("RTTMediaActionBar");

        // Parent to VirtualObjects
        GameObject virtualObjects = GameObject.Find("VirtualObjects");
        if (virtualObjects != null)
        {
            barObj.transform.SetParent(virtualObjects.transform, false);
        }

        RTTMediaActionBar actionBar = barObj.AddComponent<RTTMediaActionBar>();
        actionBar.Initialize(followTarget, targetHeight, panelWidth, primaryColor, accentColor, font);

        return actionBar;
    }
    #endregion
}
