using UnityEngine;
using UnityEngine.UI;

public class VRButtonAnimation : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.IPointerClickHandler
{
    public Transform targetVisuals; // Target to animate
    public float popAmount = 0.1f;  // Set Default to 0.1

    private bool _isHovered = false;
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

                // Set aspect ratio for correct border rendering
                UpdateBorderAspect();
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
    }

    private float _currentHoverAmount = 0f;

    void Update()
    {
        float targetZ = _isHovered ? -popAmount : 0f;
        _currentPop = Mathf.Lerp(_currentPop, targetZ, Time.unscaledDeltaTime * 10f);

        float targetScale = _isHovered ? 1.05f : 1.0f;
        _currentValidScale = Mathf.Lerp(_currentValidScale, targetScale, Time.unscaledDeltaTime * 10f);

        if (targetVisuals != null)
        {
            targetVisuals.localPosition = new Vector3(0, 0, _currentPop);
            targetVisuals.localScale = new Vector3(_currentValidScale, _currentValidScale, 1f);
        }

        // Smooth hover amount transition
        float targetHover = _isHovered ? 1f : 0f;
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

        // Swap border shader to hover shader
        if (_borderMaterial != null && _hoverBorderShader != null)
        {
            _borderMaterial.shader = _hoverBorderShader;
            UpdateBorderAspect();
        }
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        _isHovered = false;

        // Restore original border shader
        if (_borderMaterial != null && _originalBorderShader != null)
        {
            _borderMaterial.shader = _originalBorderShader;
            UpdateBorderAspect();
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

    private void UpdateBorderAspect()
    {
        if (_borderMaterial == null || _borderRectTransform == null) return;

        float width = _borderRectTransform.rect.width;
        float height = _borderRectTransform.rect.height;
        if (height > 0.001f)
        {
            float aspect = width / height;
            _borderMaterial.SetFloat("_Aspect", aspect);
        }
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
