using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// RTTFileActionBar - Floating action bar that follows the File Manager detail panel.
/// Contains Open, Rename, and Delete buttons positioned below the detail panel.
/// </summary>
public class RTTFileActionBar : MonoBehaviour
{
    #region Events
    public event Action OnOpenClicked;
    public event Action OnRenameClicked;
    public event Action OnDeleteClicked;
    #endregion

    #region Private Fields
    private Transform _followTarget;
    private float _targetHeight;
    private float _panelWidth;
    private bool _initialized = false;

    // Dimensions synced with RTTToolbar/RTTTaskbar
    private float _frameHeight = 0.12f;
    private float _spacingMult = 0.145f;

    // Buttons
    private GameObject _container;
    private Button _openButton;
    private Button _renameButton;
    private Button _deleteButton;
    private TMP_FontAsset _font;

    // Theme
    private Color _primaryColor;
    private Color _accentColor;
    #endregion

    #region Properties
    public bool IsInitialized => _initialized;
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

        // Try to sync dimensions with RTTTaskbar if available
        RTTToolbar toolbar = RTTToolbar.Instance;
        if (toolbar != null && toolbar.TaskbarHeight > 0)
        {
            _frameHeight = toolbar.TaskbarHeight;
            _spacingMult = 0.2f;
        }

        CreateButtons(font);

        _initialized = true;
        Debug.Log($"[RTTFileActionBar] Initialized, following: {_followTarget?.name}");
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

    /// <summary>
    /// Update button states based on selection.
    /// </summary>
    public void UpdateButtonStates(bool canOpen, bool canRename, bool canDelete)
    {
        SetButtonEnabled(_openButton, canOpen);
        SetButtonEnabled(_renameButton, canRename);
        SetButtonEnabled(_deleteButton, canDelete);
    }

    private void SetButtonEnabled(Button btn, bool enabled)
    {
        if (btn == null) return;
        btn.interactable = enabled;

        var cg = btn.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = enabled ? 1f : 0.4f;
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
        float spacing = 8f;
        float charWidth = 12f;
        float sidePadding = 25f;
        float buttonSpacing = 15f;

        // Button labels
        string openLabel = "Open";
        string renameLabel = "Rename";
        string deleteLabel = "Delete";

        // Canvas Setup
        float logicalPanelWidth = _panelWidth * 1000f;
        float canvasWidth = logicalPanelWidth;
        float canvasHeight = _frameHeight * 1000f;

        // Calculate base widths based on text length
        float openBaseWidth = iconSize + spacing + (openLabel.Length * charWidth) + (sidePadding * 2);
        float renameBaseWidth = iconSize + spacing + (renameLabel.Length * charWidth) + (sidePadding * 2);
        float deleteBaseWidth = iconSize + spacing + (deleteLabel.Length * charWidth) + (sidePadding * 2);
        float totalBaseWidth = openBaseWidth + renameBaseWidth + deleteBaseWidth + (buttonSpacing * 2);

        // Scale buttons to fill panel width while keeping text-based proportions
        // Use same approach as RTTMediaActionBar - no side margin
        float targetWidth = canvasWidth - (buttonSpacing * 2);  // Full width minus spacing between buttons
        float scale = targetWidth / totalBaseWidth;

        float openWidth = openBaseWidth * scale;
        float renameWidth = renameBaseWidth * scale;
        float deleteWidth = deleteBaseWidth * scale;

        // Total width of buttons
        float totalButtonsWidth = openWidth + renameWidth + deleteWidth + (buttonSpacing * 2);

        RectTransform canvasRT = _container.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(canvasWidth, canvasHeight);
        canvasRT.localScale = Vector3.one * 0.001f;

        _container.AddComponent<CanvasScaler>();
        _container.AddComponent<GraphicRaycaster>();

        float groupStartX = -totalButtonsWidth / 2f;

        // Open Button (Cyan)
        float openX = groupStartX + openWidth / 2f;
        Sprite openIcon = Resources.Load<Sprite>("icon_open_file");
        Color cyanColor = new Color(0f, 0.9f, 1f);
        var openConfig = new VRButtonFactory.ButtonConfig
        {
            label = openLabel,
            icon = openIcon,
            themeColor = cyanColor,
            width = openWidth,
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
        GameObject openBtn = VRButtonFactory.CreateButton(canvasRT, openConfig, () => OnOpenClicked?.Invoke());
        PositionButton(openBtn, openX);
        _openButton = openBtn.GetComponent<Button>();
        openBtn.AddComponent<CanvasGroup>();

        // Rename Button (Purple)
        float renameX = groupStartX + openWidth + buttonSpacing + renameWidth / 2f;
        Sprite renameIcon = Resources.Load<Sprite>("icon_rename");
        Color purpleColor = new Color(0.76f, 0.36f, 1f);
        var renameConfig = new VRButtonFactory.ButtonConfig
        {
            label = renameLabel,
            icon = renameIcon,
            themeColor = purpleColor,
            width = renameWidth,
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
        GameObject renameBtn = VRButtonFactory.CreateButton(canvasRT, renameConfig, () => OnRenameClicked?.Invoke());
        PositionButton(renameBtn, renameX);
        _renameButton = renameBtn.GetComponent<Button>();
        renameBtn.AddComponent<CanvasGroup>();

        // Delete Button (Deep Sea Blue)
        float deleteX = groupStartX + openWidth + buttonSpacing + renameWidth + buttonSpacing + deleteWidth / 2f;
        Sprite deleteIcon = Resources.Load<Sprite>("icon_trash");
        Color deepSeaBlue = new Color(0f, 0.4f, 0.65f);
        var deleteConfig = new VRButtonFactory.ButtonConfig
        {
            label = deleteLabel,
            icon = deleteIcon,
            themeColor = deepSeaBlue,
            width = deleteWidth,
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
        GameObject deleteBtn = VRButtonFactory.CreateButton(canvasRT, deleteConfig, () => OnDeleteClicked?.Invoke());
        PositionButton(deleteBtn, deleteX);
        _deleteButton = deleteBtn.GetComponent<Button>();
        deleteBtn.AddComponent<CanvasGroup>();

        Debug.Log($"[RTTFileActionBar] Buttons created");
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
        float frameHalfHeight = _frameHeight / 2f;

        float gap = _frameHeight * _spacingMult;

        Vector3 targetBottom = targetCenter - targetUp * targetHalfHeight;
        Vector3 E = targetBottom - Vector3.up * gap;

        Vector3 toE = E - cameraPos;
        float distToE = toE.magnitude;

        float h = frameHalfHeight;

        if (distToE < 0.001f || distToE < h)
        {
            transform.position = E - Vector3.up * h;
            Vector3 toCam = cameraPos - transform.position;
            if (toCam.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }
            return;
        }

        float rSq = distToE * distToE - h * h;
        if (rSq < 0.0001f) rSq = 0.0001f;
        float r = Mathf.Sqrt(rSq);

        float sinBeta = h / distToE;
        sinBeta = Mathf.Clamp(sinBeta, -1f, 1f);
        float beta = Mathf.Asin(sinBeta);

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

        Vector3 toCamera = cameraPos - transform.position;
        if (toCamera.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        }
    }
    #endregion

    #region Static Factory
    /// <summary>
    /// Create RTTFileActionBar in VirtualObjects.
    /// </summary>
    public static RTTFileActionBar Create(Transform followTarget, float targetHeight, float panelWidth, Color primaryColor, Color accentColor, TMP_FontAsset font)
    {
        GameObject barObj = new GameObject("RTTFileActionBar");

        GameObject virtualObjects = GameObject.Find("VirtualObjects");
        if (virtualObjects != null)
        {
            barObj.transform.SetParent(virtualObjects.transform, false);
        }

        RTTFileActionBar actionBar = barObj.AddComponent<RTTFileActionBar>();
        actionBar.Initialize(followTarget, targetHeight, panelWidth, primaryColor, accentColor, font);

        return actionBar;
    }
    #endregion
}
