using UnityEngine;
using UnityEngine.UI;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// Hover effect that creates a glowing border using shader swapping and material properties.
    /// Ported from VRButtonAnimation hover logic.
    /// </summary>
    [System.Serializable]
    public class GlowBorderHoverEffect : HoverEffectBase
    {
        public override string EffectId => "glow_border";

        [Header("Border Target")]
        [Tooltip("Name of the border child object. If empty, looks for 'Border'.")]
        [SerializeField] private string borderChildName = "Border";

        [Header("Shader Settings")]
        [Tooltip("Swap shader on hover for enhanced glow effect")]
        [SerializeField] private bool swapShader = true;

        [Tooltip("Original shader name (when not hovered)")]
        [SerializeField] private string normalShaderName = "Custom/GlowingElementBorder";

        [Tooltip("Hover shader name (when hovered)")]
        [SerializeField] private string hoverShaderName = "Custom/GlowingGlassBorder";

        [Header("Glow Parameters")]
        [Tooltip("Color of the glow effect")]
        [SerializeField] private Color glowColor = new Color(0f, 0.9f, 1f);

        [Tooltip("Intensity multiplier for glow when hovered")]
        [SerializeField] private float hoverGlowIntensity = 2.5f;

        [Tooltip("Border width multiplier when hovered")]
        [SerializeField] private float hoverBorderMultiplier = 1.5f;

        [Tooltip("Glow width multiplier when hovered")]
        [SerializeField] private float hoverGlowWidthMultiplier = 1.5f;

        [Header("Alpha Fade")]
        [Tooltip("If true, fades border alpha instead of/in addition to glow properties")]
        [SerializeField] private bool useAlphaFade = false;

        // Runtime references
        private Image _borderImage;
        private Material _borderMaterial;
        private RectTransform _borderRectTransform;
        private Shader _originalShader;
        private Shader _hoverShader;

        // Saved original properties
        private float _savedBorderWidth;
        private float _savedGlowWidth;
        private float _savedCornerRadius;
        private float _savedEdgePadding;
        private float _savedGlowIntensity;
        private Color _savedGlowColor;

        // Layer widths for GlowingGlassBorder shader
        private float _savedLayer1Width;
        private float _savedLayer2Width;
        private float _savedLayer3Width;
        private float _savedLayer4Width;

        private bool _hasSwappedShader = false;

        public override void Initialize(HoverEffectController controller)
        {
            base.Initialize(controller);

            if (controller.TargetVisuals == null)
            {
                #if UNITY_EDITOR
                Debug.LogWarning($"[GlowBorderHoverEffect] TargetVisuals is null on {controller.gameObject.name}");
                #endif
                return;
            }

            // Find border image
            string childName = string.IsNullOrEmpty(borderChildName) ? "Border" : borderChildName;
            Transform borderTransform = controller.TargetVisuals.Find(childName);

            #if UNITY_EDITOR
            if (borderTransform == null)
                Debug.LogWarning($"[GlowBorderHoverEffect] Could not find '{childName}' under {controller.TargetVisuals.name} on {controller.gameObject.name}");
            #endif

            if (borderTransform != null)
            {
                _borderImage = borderTransform.GetComponent<Image>();
                _borderRectTransform = borderTransform.GetComponent<RectTransform>();

                if (_borderImage != null && _borderImage.material != null)
                {
                    // Create material instance to avoid affecting other elements
                    _borderMaterial = new Material(_borderImage.material);
                    _borderImage.material = _borderMaterial;

                    // Cache shaders
                    _originalShader = _borderMaterial.shader;
                    if (swapShader && !string.IsNullOrEmpty(hoverShaderName))
                    {
                        _hoverShader = Shader.Find(hoverShaderName);
                    }

                    // Save original properties
                    SaveOriginalProperties();

                    // Update aspect ratio
                    UpdateAspectRatio();

                    // Initialize alpha if using alpha fade
                    if (useAlphaFade)
                    {
                        SetBorderAlpha(0f);
                    }

                    #if UNITY_EDITOR
                    Debug.Log($"[GlowBorderHoverEffect] Initialized successfully on {controller.gameObject.name}, material={_borderMaterial.name}");
                    #endif
                }
                #if UNITY_EDITOR
                else
                {
                    Debug.LogWarning($"[GlowBorderHoverEffect] Border Image or material is null on {controller.gameObject.name}");
                }
                #endif
            }
        }

        private void SaveOriginalProperties()
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

            // Calculate layer widths for GlowingGlassBorder shader
            float baseWidth = (_savedBorderWidth > 0.005f) ? _savedBorderWidth : 0.03f;
            _savedLayer1Width = baseWidth * 0.5f;
            _savedLayer2Width = baseWidth * 1.2f;
            _savedLayer3Width = baseWidth * 2.5f;
            _savedLayer4Width = baseWidth * 4.0f;
        }

        private void UpdateAspectRatio()
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

        protected override void ApplyEffect(float progress)
        {
            if (_borderMaterial == null) return;

            // Handle shader swap at threshold
            if (swapShader && _hoverShader != null)
            {
                bool shouldUseHoverShader = progress > 0.01f;

                if (shouldUseHoverShader && !_hasSwappedShader)
                {
                    _borderMaterial.shader = _hoverShader;
                    RestoreGeometryProperties();
                    _hasSwappedShader = true;
                }
                else if (!shouldUseHoverShader && _hasSwappedShader)
                {
                    _borderMaterial.shader = _originalShader;
                    RestoreGeometryProperties();
                    _hasSwappedShader = false;
                }
            }

            // Interpolate hover amount
            if (_borderMaterial.HasProperty("_HoverAmount"))
            {
                _borderMaterial.SetFloat("_HoverAmount", progress);
            }

            // Interpolate border width
            if (_borderMaterial.HasProperty("_BorderWidth"))
            {
                float targetWidth = Mathf.Lerp(_savedBorderWidth, _savedBorderWidth * hoverBorderMultiplier, progress);
                _borderMaterial.SetFloat("_BorderWidth", targetWidth);
            }

            // Interpolate glow width
            if (_borderMaterial.HasProperty("_GlowWidth"))
            {
                float targetGlowWidth = Mathf.Lerp(_savedGlowWidth, _savedGlowWidth * hoverGlowWidthMultiplier, progress);
                _borderMaterial.SetFloat("_GlowWidth", targetGlowWidth);
            }

            // Interpolate glow intensity
            if (_borderMaterial.HasProperty("_GlowIntensity"))
            {
                float targetIntensity = Mathf.Lerp(_savedGlowIntensity, hoverGlowIntensity, progress);
                _borderMaterial.SetFloat("_GlowIntensity", targetIntensity);
            }

            // Interpolate layer widths for GlowingGlassBorder shader
            float layerMultiplier = Mathf.Lerp(1f, hoverBorderMultiplier, progress);
            if (_borderMaterial.HasProperty("_Layer1Width"))
                _borderMaterial.SetFloat("_Layer1Width", _savedLayer1Width * layerMultiplier);
            if (_borderMaterial.HasProperty("_Layer2Width"))
                _borderMaterial.SetFloat("_Layer2Width", _savedLayer2Width * layerMultiplier);
            if (_borderMaterial.HasProperty("_Layer3Width"))
                _borderMaterial.SetFloat("_Layer3Width", _savedLayer3Width * layerMultiplier);
            if (_borderMaterial.HasProperty("_Layer4Width"))
                _borderMaterial.SetFloat("_Layer4Width", _savedLayer4Width * layerMultiplier);

            // Alpha fade if enabled
            if (useAlphaFade)
            {
                SetBorderAlpha(progress);
            }
        }

        private void RestoreGeometryProperties()
        {
            if (_borderMaterial == null) return;

            // Restore geometry properties after shader swap
            UpdateAspectRatio();

            if (_borderMaterial.HasProperty("_CornerRadius"))
                _borderMaterial.SetFloat("_CornerRadius", _savedCornerRadius);
            if (_borderMaterial.HasProperty("_EdgePadding"))
                _borderMaterial.SetFloat("_EdgePadding", _savedEdgePadding);
            if (_borderMaterial.HasProperty("_GlowColor"))
                _borderMaterial.SetColor("_GlowColor", _savedGlowColor);
        }

        private void SetBorderAlpha(float alpha)
        {
            if (_borderImage != null)
            {
                _borderImage.color = new Color(1f, 1f, 1f, alpha);
            }
        }

        public override void Cleanup()
        {
            if (_borderMaterial != null)
            {
                Object.Destroy(_borderMaterial);
                _borderMaterial = null;
            }
        }

        #region Fluent API for configuration

        public GlowBorderHoverEffect WithBorderChild(string childName)
        {
            borderChildName = childName;
            return this;
        }

        public GlowBorderHoverEffect WithShaderSwap(bool swap)
        {
            swapShader = swap;
            return this;
        }

        public GlowBorderHoverEffect WithGlowColor(Color color)
        {
            glowColor = color;
            return this;
        }

        public GlowBorderHoverEffect WithGlowIntensity(float intensity)
        {
            hoverGlowIntensity = intensity;
            return this;
        }

        public GlowBorderHoverEffect WithBorderMultiplier(float multiplier)
        {
            hoverBorderMultiplier = multiplier;
            return this;
        }

        public GlowBorderHoverEffect WithAlphaFade(bool useAlpha)
        {
            useAlphaFade = useAlpha;
            return this;
        }

        public GlowBorderHoverEffect WithTransitionDuration(float duration)
        {
            transitionDuration = Mathf.Max(0f, duration);
            return this;
        }

        #endregion
    }
}
