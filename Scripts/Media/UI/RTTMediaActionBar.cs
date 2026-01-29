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
    private bool _initialized = false;

    // Buttons
    private GameObject _container;
    private Button _playButton;
    private Button _favouriteButton;
    private Button _playlistButton;
    private Image _favouriteIcon;

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
    public void Initialize(Transform followTarget, float targetHeight, Color primaryColor, Color accentColor, TMP_FontAsset font)
    {
        _followTarget = followTarget;
        _targetHeight = targetHeight;
        _primaryColor = primaryColor;
        _accentColor = accentColor;

        CreateButtons(font);

        _initialized = true;
        Debug.Log($"[RTTMediaActionBar] Initialized, following: {_followTarget?.name}");
    }

    /// <summary>
    /// Update favourite button visual state.
    /// </summary>
    public void UpdateFavouriteState(bool isFavourite)
    {
        if (_favouriteIcon == null) return;

        Sprite icon = Resources.Load<Sprite>(isFavourite ? "icon_heart_filled" : "icon_heart_outline");
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

        RectTransform canvasRT = _container.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(300, 100);
        canvasRT.localScale = Vector3.one * 0.001f;  // Scale down to world units

        // Add CanvasScaler and GraphicRaycaster
        _container.AddComponent<CanvasScaler>();
        _container.AddComponent<GraphicRaycaster>();

        // Button configuration
        float buttonSize = 75f;
        float spacing = 20f;
        float totalWidth = (buttonSize * 3) + (spacing * 2);
        float startX = -totalWidth / 2f + buttonSize / 2f;

        // Play Button
        Sprite playIcon = Resources.Load<Sprite>("icon_play");
        var playConfig = new VRButtonFactory.ButtonConfig
        {
            label = "",
            icon = playIcon,
            themeColor = _accentColor,
            width = buttonSize,
            height = buttonSize,
            iconOnly = true,
            iconSize = 39f,
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject playBtn = VRButtonFactory.CreateButton(canvasRT, playConfig, () => OnPlayClicked?.Invoke());
        PositionButton(playBtn, startX);
        _playButton = playBtn.GetComponent<Button>();

        // Favourite Button
        Sprite heartIcon = Resources.Load<Sprite>("icon_heart_outline");
        var favConfig = new VRButtonFactory.ButtonConfig
        {
            label = "",
            icon = heartIcon,
            themeColor = _primaryColor,
            width = buttonSize,
            height = buttonSize,
            iconOnly = true,
            iconSize = 39f,
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject favBtn = VRButtonFactory.CreateButton(canvasRT, favConfig, () => OnFavouriteClicked?.Invoke());
        PositionButton(favBtn, startX + buttonSize + spacing);
        _favouriteButton = favBtn.GetComponent<Button>();
        _favouriteIcon = favBtn.transform.Find("HitArea/Visuals/Content/Icon")?.GetComponent<Image>();

        // Playlist Button
        Sprite playlistIcon = Resources.Load<Sprite>("icon_playlist_add");
        var playlistConfig = new VRButtonFactory.ButtonConfig
        {
            label = "",
            icon = playlistIcon,
            themeColor = _primaryColor,
            width = buttonSize,
            height = buttonSize,
            iconOnly = true,
            iconSize = 39f,
            borderWidth = 0.04f,
            glowWidth = 0.08f,
            glowIntensity = 4f,
            popAmount = 0.05f
        };
        GameObject playlistBtn = VRButtonFactory.CreateButton(canvasRT, playlistConfig, () => OnPlaylistClicked?.Invoke());
        PositionButton(playlistBtn, startX + (buttonSize + spacing) * 2);
        _playlistButton = playlistBtn.GetComponent<Button>();

        Debug.Log("[RTTMediaActionBar] Buttons created");
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
        float gap = _targetHeight * gapMultiplier;

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
    public static RTTMediaActionBar Create(Transform followTarget, float targetHeight, Color primaryColor, Color accentColor, TMP_FontAsset font)
    {
        GameObject barObj = new GameObject("RTTMediaActionBar");

        // Parent to VirtualObjects
        GameObject virtualObjects = GameObject.Find("VirtualObjects");
        if (virtualObjects != null)
        {
            barObj.transform.SetParent(virtualObjects.transform, false);
        }

        RTTMediaActionBar actionBar = barObj.AddComponent<RTTMediaActionBar>();
        actionBar.Initialize(followTarget, targetHeight, primaryColor, accentColor, font);

        return actionBar;
    }
    #endregion
}
