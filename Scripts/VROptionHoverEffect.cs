using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Handles hover effect for dropdown option items.
/// Shows a glowing border with the GlowingGlassBorder shader when hovered.
/// Similar to VRButtonAnimation but optimized for option items.
/// </summary>
public class VROptionHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Border Settings")]
    public Image borderImage;
    public Color glowColor = new Color(0.0f, 0.9f, 1.0f);
    public float cornerRadius = 0.12f;
    public float edgePadding = 0.12f;
    public float borderWidth = 0.04f;
    public float glowWidth = 0.04f;
    public float glowIntensity = 2.5f;

    private Material _borderMaterial;
    private RectTransform _rectTransform;
    private bool _isHovered = false;
    private bool _isSelected = false;
    private float _currentAlpha = 0f;
    private float _targetAlpha = 0f;

    // Static reference to track currently hovered option across all instances
    private static VROptionHoverEffect _currentlyHoveredOption = null;

    // Animation speed
    private const float FADE_SPEED = 12f;

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    void Start()
    {
        if (borderImage == null) return;

        // Create GlowingElementBorder material instance (same as panel border for consistency)
        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            _borderMaterial = new Material(glowShader);
            borderImage.material = _borderMaterial;
            borderImage.raycastTarget = false;

            // Configure shader properties
            ConfigureBorderMaterial();

            // Start with invisible border
            SetBorderAlpha(0f);
        }
    }

    void Update()
    {
        // Smooth alpha transition
        if (!Mathf.Approximately(_currentAlpha, _targetAlpha))
        {
            _currentAlpha = Mathf.Lerp(_currentAlpha, _targetAlpha, Time.unscaledDeltaTime * FADE_SPEED);

            // Snap to target when close enough
            if (Mathf.Abs(_currentAlpha - _targetAlpha) < 0.01f)
            {
                _currentAlpha = _targetAlpha;
            }

            SetBorderAlpha(_currentAlpha);
        }
    }

    private void ConfigureBorderMaterial()
    {
        if (_borderMaterial == null || borderImage == null) return;

        // Get aspect ratio from border image's RectTransform (not the option)
        RectTransform borderRT = borderImage.GetComponent<RectTransform>();
        if (borderRT == null) return;

        float width = borderRT.rect.width;
        float height = borderRT.rect.height;
        float aspect = height > 0.001f ? width / height : 1f;

        // Border geometry - same as panel border (GlowingElementBorder shader)
        _borderMaterial.SetFloat("_Aspect", aspect);
        _borderMaterial.SetFloat("_CornerRadius", cornerRadius);
        _borderMaterial.SetFloat("_EdgePadding", edgePadding);

        // Glow color - same as panel border
        Color borderGlowCol = Color.Lerp(glowColor, Color.white, 0.75f);
        _borderMaterial.SetColor("_GlowColor", borderGlowCol);

        // Border and glow parameters - match panel border
        _borderMaterial.SetFloat("_BorderWidth", borderWidth);
        _borderMaterial.SetFloat("_GlowWidth", glowWidth);
        _borderMaterial.SetFloat("_GlowIntensity", glowIntensity);
        _borderMaterial.SetFloat("_PulseEnabled", 0f);
    }

    private void SetBorderAlpha(float alpha)
    {
        if (borderImage != null)
        {
            borderImage.color = new Color(1f, 1f, 1f, alpha);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovered = true;
        _currentlyHoveredOption = this;
        _targetAlpha = 1f;

        // Update aspect ratio in case size changed
        ConfigureBorderMaterial();

        // Notify all selected options to update their state
        NotifySelectedOptionsToUpdate();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovered = false;

        // Clear static reference if this was the hovered option
        if (_currentlyHoveredOption == this)
        {
            _currentlyHoveredOption = null;
        }

        // Update target alpha based on selection state
        UpdateTargetAlpha();

        // Notify all selected options to update their state
        NotifySelectedOptionsToUpdate();
    }

    /// <summary>
    /// Update target alpha based on current hover and selection state
    /// </summary>
    private void UpdateTargetAlpha()
    {
        if (_isHovered)
        {
            _targetAlpha = 1f;
        }
        else if (_isSelected)
        {
            // Selected option shows full hover if no other option is being hovered
            bool noOtherHover = (_currentlyHoveredOption == null || _currentlyHoveredOption == this);
            _targetAlpha = noOtherHover ? 1f : 0f;
        }
        else
        {
            _targetAlpha = 0f;
        }
    }

    /// <summary>
    /// Notify all selected options to update their visual state
    /// Called when hover state changes
    /// </summary>
    private static void NotifySelectedOptionsToUpdate()
    {
        // Find all VROptionHoverEffect instances and update selected ones
        var allEffects = FindObjectsOfType<VROptionHoverEffect>();
        foreach (var effect in allEffects)
        {
            if (effect._isSelected && !effect._isHovered)
            {
                effect.UpdateTargetAlpha();
            }
        }
    }

    /// <summary>
    /// Force show/hide the hover effect (useful for initial selected state)
    /// </summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        UpdateTargetAlpha();
    }

    /// <summary>
    /// Immediately show hover effect without animation
    /// </summary>
    public void ShowImmediate()
    {
        _currentAlpha = 1f;
        _targetAlpha = 1f;
        SetBorderAlpha(1f);
    }

    /// <summary>
    /// Immediately hide hover effect without animation
    /// </summary>
    public void HideImmediate()
    {
        _currentAlpha = 0f;
        _targetAlpha = 0f;
        SetBorderAlpha(0f);
    }

    void OnDisable()
    {
        // Clear static reference if this was the hovered option
        if (_currentlyHoveredOption == this)
        {
            _currentlyHoveredOption = null;
            NotifySelectedOptionsToUpdate();
        }
    }

    void OnDestroy()
    {
        // Clear static reference if this was the hovered option
        if (_currentlyHoveredOption == this)
        {
            _currentlyHoveredOption = null;
        }

        // Clean up material instance
        if (_borderMaterial != null)
        {
            Destroy(_borderMaterial);
        }
    }

    void OnRectTransformDimensionsChange()
    {
        // Update material when size changes
        ConfigureBorderMaterial();
    }
}
