using UnityEngine;
using VRWorkspace.UI.Config;

namespace VRWorkspace.UI.Utilities
{
    /// <summary>
    /// Factory class for creating UI materials with cached shaders.
    /// Centralizes material creation to avoid repeated Shader.Find() calls
    /// and duplicated material setup code.
    /// </summary>
    public static class MaterialFactory
    {
        #region Cached Shaders

        private static Shader _glassBackgroundShader;
        private static Shader _glowBorderShader;
        private static Shader _glowingGlassBorderShader;
        private static Shader _connectButtonShader;

        /// <summary>
        /// Glass background shader (GlassGradientBackgroundWide)
        /// </summary>
        public static Shader GlassBackgroundShader
        {
            get
            {
                if (_glassBackgroundShader == null)
                    _glassBackgroundShader = Shader.Find("Custom/GlassGradientBackgroundWide");
                return _glassBackgroundShader;
            }
        }

        /// <summary>
        /// Glow border shader (GlowingElementBorder)
        /// </summary>
        public static Shader GlowBorderShader
        {
            get
            {
                if (_glowBorderShader == null)
                    _glowBorderShader = Shader.Find("Custom/GlowingElementBorder");
                return _glowBorderShader;
            }
        }

        /// <summary>
        /// Advanced glow border shader (GlowingGlassBorder) — used by popups and frames.
        /// Multi-layer SDF border with gradient, shimmer, and glass tint.
        /// </summary>
        public static Shader GlowingGlassBorderShader
        {
            get
            {
                if (_glowingGlassBorderShader == null)
                    _glowingGlassBorderShader = Shader.Find("Custom/GlowingGlassBorder");
                return _glowingGlassBorderShader;
            }
        }

        /// <summary>
        /// Connect button shader (GlowingConnectButton)
        /// </summary>
        public static Shader ConnectButtonShader
        {
            get
            {
                if (_connectButtonShader == null)
                    _connectButtonShader = Shader.Find("Custom/GlowingConnectButton");
                return _connectButtonShader;
            }
        }

        /// <summary>
        /// Pre-initialize all shaders at startup for better performance.
        /// Call this from RTTManager.Awake() or similar initialization point.
        /// </summary>
        public static void Initialize()
        {
            var _ = GlassBackgroundShader;
            var __ = GlowBorderShader;
            var ___ = GlowingGlassBorderShader;
            var ____ = ConnectButtonShader;
        }

        #endregion

        #region Glass Background Material

        /// <summary>
        /// Creates a glass background material with gradient effect.
        /// </summary>
        /// <param name="cornerRadius">Corner radius (typically 0.12f)</param>
        /// <param name="edgePadding">Edge padding (typically 0.06f)</param>
        /// <param name="aspect">Width / Height ratio</param>
        /// <param name="baseColor">Base theme color</param>
        /// <param name="backgroundAlpha">Background opacity (typically 0.08f)</param>
        /// <returns>Configured material or null if shader not found</returns>
        public static Material CreateGlassBackground(float cornerRadius, float edgePadding,
            float aspect, Color baseColor, float backgroundAlpha)
        {
            if (GlassBackgroundShader == null) return null;

            Material mat = new Material(GlassBackgroundShader);
            mat.SetFloat("_CornerRadius", cornerRadius);
            mat.SetFloat("_EdgePadding", edgePadding);
            mat.SetFloat("_Aspect", aspect);
            mat.SetColor("_ColorA", new Color(baseColor.r, baseColor.g, baseColor.b, backgroundAlpha * 1.5f));
            mat.SetColor("_ColorB", new Color(baseColor.r, baseColor.g, baseColor.b, backgroundAlpha * 0.5f));
            mat.SetFloat("_GlassAlpha", backgroundAlpha);

            return mat;
        }

        /// <summary>
        /// Creates a glass background material with custom gradient colors for dropdowns.
        /// </summary>
        public static Material CreateGlassBackgroundWithGradient(float cornerRadius, float edgePadding,
            float aspect, Color baseColor, float backgroundAlpha,
            float gradientOffset = 0f, float gradientAngle = -10f, float cyanRatio = 0.7f,
            float fresnelPower = 2.2f, float fresnelStrength = 0.12f)
        {
            if (GlassBackgroundShader == null) return null;

            Material mat = new Material(GlassBackgroundShader);
            mat.SetFloat("_CornerRadius", cornerRadius);
            mat.SetFloat("_EdgePadding", edgePadding);
            mat.SetFloat("_Aspect", aspect);

            // Gradient colors based on theme color
            Color colorA = new Color(baseColor.r * 0.8f, baseColor.g * 0.9f, baseColor.b, backgroundAlpha * 1.5f);
            Color colorB = new Color(baseColor.r, baseColor.g * 0.7f, baseColor.b * 0.9f, backgroundAlpha * 1.2f);
            mat.SetColor("_ColorA", colorA);
            mat.SetColor("_ColorB", colorB);

            mat.SetFloat("_GradientOffset", gradientOffset);
            mat.SetFloat("_GradientAngle", gradientAngle);
            mat.SetFloat("_CyanRatio", cyanRatio);
            mat.SetFloat("_GlassAlpha", backgroundAlpha);
            mat.SetFloat("_FresnelPower", fresnelPower);
            mat.SetFloat("_FresnelStrength", fresnelStrength);

            return mat;
        }

        #endregion

        #region Glow Border Material

        /// <summary>
        /// Creates a glowing border material.
        /// </summary>
        /// <param name="aspect">Width / Height ratio</param>
        /// <param name="cornerRadius">Corner radius (typically 0.12f)</param>
        /// <param name="edgePadding">Edge padding (typically 0.06f)</param>
        /// <param name="baseColor">Base theme color</param>
        /// <param name="borderWidth">Border width (typically 0.025f)</param>
        /// <param name="glowWidth">Glow width (typically 0.04f)</param>
        /// <param name="glowIntensity">Glow intensity (typically 2.5f)</param>
        /// <param name="enablePulse">Enable pulse animation</param>
        /// <param name="pulseSpeed">Pulse animation speed</param>
        /// <returns>Configured material or null if shader not found</returns>
        public static Material CreateGlowBorder(float aspect, float cornerRadius, float edgePadding,
            Color baseColor, float borderWidth, float glowWidth, float glowIntensity,
            bool enablePulse = false, float pulseSpeed = 2f)
        {
            if (GlowBorderShader == null) return null;

            Material mat = new Material(GlowBorderShader);
            mat.SetFloat("_Aspect", aspect);
            mat.SetFloat("_EdgePadding", edgePadding);
            mat.SetFloat("_CornerRadius", cornerRadius);

            // Glow color is theme color mixed with white
            Color borderGlowCol = Color.Lerp(baseColor, Color.white, 0.75f);
            mat.SetColor("_GlowColor", borderGlowCol);

            mat.SetFloat("_BorderWidth", borderWidth);
            mat.SetFloat("_GlowWidth", glowWidth);
            mat.SetFloat("_GlowIntensity", glowIntensity);
            mat.SetFloat("_PulseEnabled", enablePulse ? 1f : 0f);

            if (enablePulse)
            {
                mat.SetFloat("_PulseSpeed", pulseSpeed);
            }

            return mat;
        }

        #endregion

        #region Connect Button Material

        /// <summary>
        /// Creates a special connect button material with gradient, glow, and shimmer effects.
        /// </summary>
        public static Material CreateConnectButton(float aspect, float cornerRadius, float edgePadding,
            Color colorA, Color colorB, Color colorC, float backgroundAlpha,
            bool enablePulse = false, float pulseSpeed = 2f)
        {
            // Try shader first
            Material mat = null;
            if (ConnectButtonShader != null)
            {
                mat = new Material(ConnectButtonShader);
            }
            else
            {
                // Fallback to Resources material
                Material baseMat = Resources.Load<Material>("GlowingConnectButton");
                if (baseMat != null && baseMat.shader != null &&
                    baseMat.shader.name != "Hidden/InternalErrorShader")
                {
                    mat = new Material(baseMat);
                }
            }

            if (mat == null)
            {
                Debug.LogError("[MaterialFactory] GlowingConnectButton shader/material not found!");
                return null;
            }

            // Border settings
            mat.SetFloat("_EdgePadding", edgePadding);
            mat.SetFloat("_BorderWidth", 0.028f);
            mat.SetFloat("_CornerRadius", cornerRadius);
            mat.SetFloat("_Aspect", aspect);

            // Gradient colors
            mat.SetColor("_ColorA", colorA);
            mat.SetColor("_ColorB", colorB);
            mat.SetColor("_ColorC", colorC);
            mat.SetFloat("_GradientAngle", 0f);
            mat.SetFloat("_MidPoint1", 0.35f);
            mat.SetFloat("_MidPoint2", 0.70f);

            // Background
            mat.SetFloat("_BgAlpha", backgroundAlpha);
            mat.SetFloat("_BgGradientStrength", 1.0f);

            // Glow layers
            mat.SetFloat("_Layer1Alpha", 1.5f);
            mat.SetFloat("_Layer2Alpha", 1.0f);
            mat.SetFloat("_Layer3Alpha", 0.5f);
            mat.SetFloat("_Layer4Alpha", 0.25f);

            // Pulse animation
            mat.SetFloat("_PulseEnabled", enablePulse ? 1f : 0f);
            mat.SetFloat("_PulseSpeed", pulseSpeed);
            mat.SetFloat("_PulseIntensity", 0.2f);

            // Shimmer effect
            mat.SetFloat("_ShimmerEnabled", 1f);
            mat.SetFloat("_ShimmerSpeed", 0.5f);
            mat.SetFloat("_ShimmerWidth", 0.15f);
            mat.SetFloat("_ShimmerIntensity", 1.0f);

            // Inner glow
            mat.SetFloat("_InnerGlowEnabled", 1f);
            mat.SetFloat("_InnerGlowWidth", 0.08f);
            mat.SetFloat("_InnerGlowAlpha", 0.3f);

            return mat;
        }

        #endregion

        #region Popup Materials

        /// <summary>
        /// Creates a popup glass background material with all standard properties from UIConstants.
        /// </summary>
        /// <param name="aspect">Initial aspect ratio (width / height).</param>
        /// <param name="glassAlpha">Glass opacity. Defaults to UIConstants.PopupGlassAlpha.</param>
        /// <returns>Configured material, or null if shader not found.</returns>
        public static Material CreatePopupBackground(float aspect, float glassAlpha = UIConstants.PopupGlassAlpha)
        {
            if (GlassBackgroundShader == null) return null;

            Material mat = new Material(GlassBackgroundShader);
            mat.SetFloat("_CornerRadius", UIConstants.PopupCornerRadius + 0.01f);
            mat.SetFloat("_EdgePadding", UIConstants.PopupEdgePadding);
            mat.SetFloat("_Aspect", aspect);

            mat.SetColor("_ColorA", UIConstants.PopupGlassColorA);
            mat.SetColor("_ColorB", UIConstants.PopupGlassColorB);
            mat.SetFloat("_GlassAlpha", glassAlpha);
            mat.SetFloat("_GradientOffset", 0f);
            mat.SetFloat("_GradientAngle", -10f);
            mat.SetFloat("_CyanRatio", 0.7f);
            mat.SetFloat("_FresnelPower", 2.2f);
            mat.SetFloat("_FresnelStrength", 0.12f);

            return mat;
        }

        /// <summary>
        /// Creates a popup glow border material using the GlowingGlassBorder shader.
        /// All values sourced from UIConstants for consistent rendering across all popup types.
        /// </summary>
        /// <param name="aspect">Initial aspect ratio (width / height).</param>
        /// <returns>Configured material, or null if shader not found.</returns>
        public static Material CreatePopupGlowBorder(float aspect)
        {
            if (GlowingGlassBorderShader == null) return null;

            Material mat = new Material(GlowingGlassBorderShader);
            mat.SetFloat("_StrokeEnabled", 0);
            mat.SetFloat("_BorderWidth", UIConstants.PopupBorderWidth);
            mat.SetFloat("_CornerRadius", UIConstants.PopupCornerRadius);
            mat.SetFloat("_EdgePadding", UIConstants.PopupEdgePadding);
            mat.SetFloat("_Aspect", aspect);

            // Glow layers
            mat.SetFloat("_Layer1Width", UIConstants.PopupGlowLayer1Width);
            mat.SetFloat("_Layer1Alpha", UIConstants.PopupGlowLayer1Alpha);
            mat.SetFloat("_Layer2Width", UIConstants.PopupGlowLayer2Width);
            mat.SetFloat("_Layer2Alpha", UIConstants.PopupGlowLayer2Alpha);
            mat.SetFloat("_Layer3Width", UIConstants.PopupGlowLayer3Width);
            mat.SetFloat("_Layer3Alpha", UIConstants.PopupGlowLayer3Alpha);
            mat.SetFloat("_Layer4Width", UIConstants.PopupGlowLayer4Width);
            mat.SetFloat("_Layer4Alpha", UIConstants.PopupGlowLayer4Alpha);

            // Gradient colors
            mat.SetColor("_ColorA", UIConstants.PopupGlowColorA);
            mat.SetColor("_ColorB", UIConstants.PopupGlowColorB);
            mat.SetFloat("_GradientMode", 2f);
            mat.SetFloat("_GradientAngle", -10f);
            mat.SetFloat("_GlassAlpha", 0.02f);
            mat.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
            mat.SetFloat("_ShimmerSpeed", 0.4f);
            mat.SetFloat("_ShimmerIntensity", 0.2f);
            mat.SetFloat("_LightSize", 0.008f);
            mat.SetFloat("_LightGlow", 0.008f);

            return mat;
        }

        /// <summary>
        /// Updates _Aspect and normalizes border/glow widths so every popup has
        /// identical physical border thickness regardless of its pixel dimensions.
        /// Values in UIConstants are authored for PopupBorderReferenceHeight (500px).
        /// </summary>
        public static void UpdatePopupAspect(Material bgMaterial, Material borderMaterial, float width, float height)
        {
            float aspect = width / height;

            // Normalization factor: keeps physical border thickness constant.
            // Reference = Display As popup (~500px height). A popup half as tall
            // gets 2× the UV-space width so the on-screen line looks identical.
            float n = UIConstants.PopupBorderReferenceHeight / height;

            if (bgMaterial != null)
            {
                bgMaterial.SetFloat("_Aspect", aspect);
                bgMaterial.SetFloat("_CornerRadius", (UIConstants.PopupCornerRadius + 0.01f) * n);
                bgMaterial.SetFloat("_EdgePadding", UIConstants.PopupEdgePadding * n);
            }

            if (borderMaterial != null)
            {
                borderMaterial.SetFloat("_Aspect", aspect);
                borderMaterial.SetFloat("_BorderWidth", UIConstants.PopupBorderWidth * n);
                borderMaterial.SetFloat("_CornerRadius", UIConstants.PopupCornerRadius * n);
                borderMaterial.SetFloat("_EdgePadding", UIConstants.PopupEdgePadding * n);

                // Glow layers — widths normalized, alphas stay constant
                borderMaterial.SetFloat("_Layer1Width", UIConstants.PopupGlowLayer1Width * n);
                borderMaterial.SetFloat("_Layer2Width", UIConstants.PopupGlowLayer2Width * n);
                borderMaterial.SetFloat("_Layer3Width", UIConstants.PopupGlowLayer3Width * n);
                borderMaterial.SetFloat("_Layer4Width", UIConstants.PopupGlowLayer4Width * n);
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Calculates aspect ratio from dimensions.
        /// </summary>
        public static float CalculateAspect(float width, float height)
        {
            return width / height;
        }

        /// <summary>
        /// Creates a glow color from base color (mix with white).
        /// </summary>
        public static Color CreateGlowColor(Color baseColor, float whiteMix = 0.75f)
        {
            return Color.Lerp(baseColor, Color.white, whiteMix);
        }

        #endregion
    }
}
