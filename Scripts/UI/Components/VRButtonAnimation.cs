using UnityEngine;
using UnityEngine.UI;

public class VRButtonAnimation : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.IPointerClickHandler
{
    public Transform targetVisuals; // Target to animate
    public float popAmount = 0.1f;  // Z-axis pop amount (for World Space Canvas with perspective)
    public float hoverScaleAmount = 0.05f; // Scale increase when hovering (0.05 = 5%, 0.15 = 15%)
    public float hoverBorderMultiplier = 1f; // Border width multiplier when hovering (default 1 = no change)

    private bool _isHovered = false;
    private bool _forceHover = false;  // Force hover state (e.g., when dropdown panel is open)
    public bool IsForceHover => _forceHover;  // Public accessor for force hover state
    private float _currentPop = 0f;
    private float _currentValidScale = 1.0f;

    // Shader material references for hover state
    private Material _borderMaterial;
    private Material _backgroundMaterial;

    // Border shader swap for hover effect
    private Image _borderImage;
    private RectTransform _borderRectTransform;
    private Shader _originalBorderShader;
    private Shader _hoverBorderShader;

    // Saved border properties to restore after shader swap
    private float _savedBorderWidth;
    private float _savedGlowWidth;
    private float _savedCornerRadius;
    private float _savedEdgePadding;
    private float _savedGlowIntensity;
    private Color _savedGlowColor;

    // Saved layer properties for GlowingGlassBorder shader
    private float _savedLayer1Width;
    private float _savedLayer2Width;
    private float _savedLayer3Width;
    private float _savedLayer4Width;

    void Start()
    {
        if (targetVisuals == null) return;

        // Try to find border material
        var borderObj = targetVisuals.Find("Border");
        if (borderObj != null)
        {
            var img = borderObj.GetComponent<Image>();
            if (img != null && img.material != null)
            {
                // Create material instance to avoid affecting other buttons
                _borderMaterial = new Material(img.material);
                img.material = _borderMaterial;
                _borderImage = img;
                _borderRectTransform = borderObj.GetComponent<RectTransform>();

                // Save original shader and find hover shader
                _originalBorderShader = _borderMaterial.shader;
                _hoverBorderShader = Shader.Find("Custom/GlowingGlassBorder");

                // Save border properties for restoration after shader swap
                SaveBorderProperties();

                // Set aspect ratio for correct border rendering
                UpdateBorderProperties();
            }
        }

        // Try to find background material (direct child with Image or named "Background" or "ConnectBackground")
        var bgImg = targetVisuals.GetComponent<Image>();
        if (bgImg != null && bgImg.material != null && bgImg.material.HasProperty("_HoverAmount"))
        {
            _backgroundMaterial = bgImg.material;
        }
        else
        {
            // Try finding Background or ConnectBackground child
            string[] bgNames = { "Background", "ConnectBackground" };
            foreach (var bgName in bgNames)
            {
                var bgObj = targetVisuals.Find(bgName);
                if (bgObj != null)
                {
                    bgImg = bgObj.GetComponent<Image>();
                    if (bgImg != null && bgImg.material != null && bgImg.material.HasProperty("_HoverAmount"))
                    {
                        _backgroundMaterial = bgImg.material;
                        break;
                    }
                }
            }
        }

        // If force hover was set before Start(), apply hover state now that materials are ready
        if (_forceHover)
        {
            // Initialize animation values to hover state immediately (no animation)
            _currentHoverAmount = 1f;
            _currentValidScale = 1.05f;
            _currentPop = -popAmount;

            // Apply hover shader
            if (_borderMaterial != null && _hoverBorderShader != null)
            {
                _borderMaterial.shader = _hoverBorderShader;
                UpdateBorderProperties();
            }

            // Apply hover amount to materials
            if (_borderMaterial != null && _borderMaterial.HasProperty("_HoverAmount"))
            {
                _borderMaterial.SetFloat("_HoverAmount", 1f);
            }
            if (_backgroundMaterial != null && _backgroundMaterial.HasProperty("_HoverAmount"))
            {
                _backgroundMaterial.SetFloat("_HoverAmount", 1f);
            }

            // Apply visual transform
            if (targetVisuals != null)
            {
                targetVisuals.localPosition = new Vector3(0, 0, _currentPop);
                targetVisuals.localScale = new Vector3(_currentValidScale, _currentValidScale, 1f);
            }
        }
    }

    private float _currentHoverAmount = 0f;

    void Update()
    {
        bool effectiveHover = _isHovered || _forceHover;

        float targetZ = effectiveHover ? -popAmount : 0f;
        _currentPop = Mathf.Lerp(_currentPop, targetZ, Time.unscaledDeltaTime * 10f);

        // Scale effect - works for both World Space and RTT (use hoverScaleAmount for visibility)
        float targetScale = effectiveHover ? (1f + hoverScaleAmount) : 1.0f;
        _currentValidScale = Mathf.Lerp(_currentValidScale, targetScale, Time.unscaledDeltaTime * 10f);

        if (targetVisuals != null)
        {
            targetVisuals.localPosition = new Vector3(0, 0, _currentPop);
            targetVisuals.localScale = new Vector3(_currentValidScale, _currentValidScale, 1f);
        }

        // Smooth hover amount transition
        float targetHover = effectiveHover ? 1f : 0f;
        _currentHoverAmount = Mathf.Lerp(_currentHoverAmount, targetHover, Time.unscaledDeltaTime * 8f);

        // Update border material hover amount (viền sáng lên)
        if (_borderMaterial != null && _borderMaterial.HasProperty("_HoverAmount"))
        {
            _borderMaterial.SetFloat("_HoverAmount", _currentHoverAmount);
        }

        // Update background material hover amount (nền sáng lên)
        if (_backgroundMaterial != null && _backgroundMaterial.HasProperty("_HoverAmount"))
        {
            _backgroundMaterial.SetFloat("_HoverAmount", _currentHoverAmount);
        }
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        _isHovered = true;

        // Only swap border shader if not already in force hover state (avoid double hover)
        if (!_forceHover && _borderMaterial != null && _hoverBorderShader != null)
        {
            _borderMaterial.shader = _hoverBorderShader;
            UpdateBorderProperties();
        }
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        _isHovered = false;

        // Only restore original shader if not force hovering
        if (!_forceHover && _borderMaterial != null && _originalBorderShader != null)
        {
            _borderMaterial.shader = _originalBorderShader;
            UpdateBorderProperties();
        }
    }

    /// <summary>
    /// Force hover state on/off (used when dropdown panel is open)
    /// </summary>
    public void SetForceHover(bool force)
    {
        _forceHover = force;

        // Update border shader based on force hover state
        if (_borderMaterial != null)
        {
            if (force && _hoverBorderShader != null)
            {
                _borderMaterial.shader = _hoverBorderShader;
                UpdateBorderProperties();
            }
            else if (!force && !_isHovered && _originalBorderShader != null)
            {
                _borderMaterial.shader = _originalBorderShader;
                UpdateBorderProperties();
            }
        }
    }

    /// <summary>
    /// Reset hover state completely (clears both isHovered and forceHover).
    /// Optionally snap to default state immediately without animation.
    /// </summary>
    /// <param name="immediate">If true, snap to default state without animation</param>
    public void ResetHoverState(bool immediate = false)
    {
        _isHovered = false;
        _forceHover = false;

        // Restore original border shader
        if (_borderMaterial != null && _originalBorderShader != null)
        {
            _borderMaterial.shader = _originalBorderShader;
            UpdateBorderProperties();
        }

        if (immediate)
        {
            // Snap to default state immediately (no animation)
            _currentHoverAmount = 0f;
            _currentValidScale = 1f;
            _currentPop = 0f;

            if (targetVisuals != null)
            {
                targetVisuals.localPosition = Vector3.zero;
                targetVisuals.localScale = Vector3.one;
            }

            // Reset material hover amounts
            if (_borderMaterial != null && _borderMaterial.HasProperty("_HoverAmount"))
            {
                _borderMaterial.SetFloat("_HoverAmount", 0f);
            }
            if (_backgroundMaterial != null && _backgroundMaterial.HasProperty("_HoverAmount"))
            {
                _backgroundMaterial.SetFloat("_HoverAmount", 0f);
            }
        }
    }

    public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
    {
        // Trigger ripple effect
        var ripple = GetComponentInChildren<VRButtonRipple>();
        if (ripple != null)
        {
            // Calculate click position in normalized coordinates
            RectTransform rt = targetVisuals?.GetComponent<RectTransform>();
            if (rt != null)
            {
                Vector2 localPoint;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out localPoint);
                Vector2 normalizedPos = new Vector2(
                    (localPoint.x / rt.rect.width) + 0.5f,
                    (localPoint.y / rt.rect.height) + 0.5f
                );
                ripple.TriggerRipple(normalizedPos);
            }
            else
            {
                ripple.TriggerRipple(new Vector2(0.5f, 0.5f));
            }
        }
    }

    /// <summary>
    /// Save border material properties before shader swap
    /// </summary>
    private void SaveBorderProperties()
    {
        if (_borderMaterial == null) return;

        if (_borderMaterial.HasProperty("_BorderWidth"))
            _savedBorderWidth = _borderMaterial.GetFloat("_BorderWidth");
        if (_borderMaterial.HasProperty("_GlowWidth"))
            _savedGlowWidth = _borderMaterial.GetFloat("_GlowWidth");
        if (_borderMaterial.HasProperty("_CornerRadius"))
            _savedCornerRadius = _borderMaterial.GetFloat("_CornerRadius");
        if (_borderMaterial.HasProperty("_EdgePadding"))
            _savedEdgePadding = _borderMaterial.GetFloat("_EdgePadding");
        if (_borderMaterial.HasProperty("_GlowIntensity"))
            _savedGlowIntensity = _borderMaterial.GetFloat("_GlowIntensity");
        if (_borderMaterial.HasProperty("_GlowColor"))
            _savedGlowColor = _borderMaterial.GetColor("_GlowColor");

        // Save default layer widths for GlowingGlassBorder shader (used when hovering)
        // These are the default values from the shader
        _savedLayer1Width = 0.015f;
        _savedLayer2Width = 0.04f;
        _savedLayer3Width = 0.08f;
        _savedLayer4Width = 0.15f;
    }

    /// <summary>
    /// Update border properties after shader swap (restore saved values)
    /// Apply 4x border width multiplier when hovering
    /// </summary>
    private void UpdateBorderProperties()
    {
        if (_borderMaterial == null || _borderRectTransform == null) return;

        bool effectiveHover = _isHovered || _forceHover;

        // Update aspect ratio
        float width = _borderRectTransform.rect.width;
        float height = _borderRectTransform.rect.height;
        if (height > 0.001f)
        {
            float aspect = width / height;
            _borderMaterial.SetFloat("_Aspect", aspect);
        }

        // Restore saved properties after shader swap
        // Apply hoverBorderMultiplier to border width when hovering
        float borderMultiplier = effectiveHover ? hoverBorderMultiplier : 1f;
        if (_borderMaterial.HasProperty("_BorderWidth"))
            _borderMaterial.SetFloat("_BorderWidth", _savedBorderWidth * borderMultiplier);
        if (_borderMaterial.HasProperty("_GlowWidth"))
            _borderMaterial.SetFloat("_GlowWidth", _savedGlowWidth);
        if (_borderMaterial.HasProperty("_CornerRadius"))
            _borderMaterial.SetFloat("_CornerRadius", _savedCornerRadius);
        if (_borderMaterial.HasProperty("_EdgePadding"))
            _borderMaterial.SetFloat("_EdgePadding", _savedEdgePadding);
        if (_borderMaterial.HasProperty("_GlowIntensity"))
            _borderMaterial.SetFloat("_GlowIntensity", _savedGlowIntensity);
        if (_borderMaterial.HasProperty("_GlowColor"))
            _borderMaterial.SetColor("_GlowColor", _savedGlowColor);

        // Apply multiplier to layer widths for GlowingGlassBorder shader when hovering
        if (_borderMaterial.HasProperty("_Layer1Width"))
            _borderMaterial.SetFloat("_Layer1Width", _savedLayer1Width * borderMultiplier);
        if (_borderMaterial.HasProperty("_Layer2Width"))
            _borderMaterial.SetFloat("_Layer2Width", _savedLayer2Width * borderMultiplier);
        if (_borderMaterial.HasProperty("_Layer3Width"))
            _borderMaterial.SetFloat("_Layer3Width", _savedLayer3Width * borderMultiplier);
        if (_borderMaterial.HasProperty("_Layer4Width"))
            _borderMaterial.SetFloat("_Layer4Width", _savedLayer4Width * borderMultiplier);
    }

    void OnDestroy()
    {
        // Cleanup material instance to avoid memory leak
        if (_borderMaterial != null)
        {
            Destroy(_borderMaterial);
        }
    }
}
