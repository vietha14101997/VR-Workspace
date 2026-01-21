using UnityEngine;
using TMPro;

/// <summary>
/// ScriptableObject for centralized RTT UI theme configuration.
/// Controls all colors, gradients, and visual settings across the RTT system.
/// </summary>
[CreateAssetMenu(fileName = "RTTTheme", menuName = "VR-Workspace/RTT Theme Config")]
public class RTTThemeConfig : ScriptableObject
{
    #region Font
    [Header("Typography")]
    [Tooltip("Default font for all UI text.\n\n" +
             "IMPORTANT: To display Chinese/Japanese/Korean characters correctly, " +
             "the font must have CJK fallback fonts configured.\n\n" +
             "Use 'Tools > TMP Font Fallback Setup' to add fallback fonts, " +
             "or 'Tools > TMP Font Auto Builder' to create font assets with automatic fallback.")]
    public TMP_FontAsset font;
    #endregion

    #region Primary Colors
    [Header("Primary Colors")]
    [Tooltip("Primary theme color (Cyan) - used for buttons, highlights, icons")]
    public Color primaryColor = new Color(0f, 0.9f, 1f);

    [Tooltip("Accent/Secondary color (Purple) - used for active states, alternating items")]
    public Color accentColor = new Color(0.76f, 0.36f, 1f);
    #endregion

    #region Glass Background
    [Header("Glass Background")]
    [Tooltip("Glass gradient color A (Cyan/DeepSeaBlue)")]
    public Color glassColorA = new Color(0f, 0.55f, 0.65f, 0.35f);

    [Tooltip("Glass gradient color B (DeepSeaBluePurple)")]
    public Color glassColorB = new Color(0.30f, 0.12f, 0.50f, 0.32f);

    [Tooltip("Glass overall alpha/opacity")]
    [Range(0f, 1f)]
    public float glassAlpha = 0.65f;

    [Tooltip("Glass gradient angle in degrees")]
    public float glassGradientAngle = -10f;

    [Tooltip("Ratio of color A in gradient (0-1)")]
    [Range(0f, 1f)]
    public float glassGradientRatio = 0.7f;

    [Tooltip("Fresnel power for glass edge effect")]
    public float glassFresnelPower = 2.2f;

    [Tooltip("Fresnel strength")]
    [Range(0f, 1f)]
    public float glassFresnelStrength = 0.12f;
    #endregion

    #region Glowing Border
    [Header("Glowing Border")]
    [Tooltip("Border glow color A (Cyan)")]
    [ColorUsage(true, true)]
    public Color glowColorA = new Color(0.3f, 1f, 1f, 1f);

    [Tooltip("Border glow color B (Purple)")]
    [ColorUsage(true, true)]
    public Color glowColorB = new Color(1f, 0.4f, 1f, 1f);

    [Header("Border HDR (for panels)")]
    [Tooltip("HDR Cyan for bright glow effects")]
    [ColorUsage(true, true)]
    public Color borderHDRCyan = new Color(0f, 1.5f, 2f, 1f);

    [Tooltip("HDR Purple for bright glow effects")]
    [ColorUsage(true, true)]
    public Color borderHDRPurple = new Color(1.2f, 0.3f, 2f, 1f);

    [Header("Glow Layer Settings")]
    [Tooltip("Layer 1 width")]
    public float glowLayer1Width = 0.01f;
    [Tooltip("Layer 1 alpha multiplier")]
    public float glowLayer1Alpha = 1.5f;

    [Tooltip("Layer 2 width")]
    public float glowLayer2Width = 0.02f;
    [Tooltip("Layer 2 alpha multiplier")]
    public float glowLayer2Alpha = 1.0f;

    [Tooltip("Layer 3 width")]
    public float glowLayer3Width = 0.045f;
    [Tooltip("Layer 3 alpha multiplier")]
    public float glowLayer3Alpha = 0.6f;

    [Tooltip("Layer 4 width")]
    public float glowLayer4Width = 0.09f;
    [Tooltip("Layer 4 alpha multiplier")]
    public float glowLayer4Alpha = 0.3f;
    #endregion

    #region Button Colors
    [Header("Button Colors")]
    [Tooltip("Button active/selected color (usually accent)")]
    public Color buttonActiveColor = new Color(0.9f, 0.3f, 1f);

    [Tooltip("Button inactive/default color (usually primary)")]
    public Color buttonInactiveColor = new Color(0f, 0.9f, 1f);

    [Tooltip("Button hover highlight color")]
    public Color buttonHoverColor = new Color(0.3f, 1f, 1f, 0.3f);

    [Tooltip("Button disabled color")]
    public Color buttonDisabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
    #endregion

    #region Connect Button Gradient
    [Header("Connect Button (Special)")]
    [Tooltip("Connect button gradient color A")]
    public Color connectColorA = new Color(0.2f, 0.9f, 1f);

    [Tooltip("Connect button gradient color B")]
    public Color connectColorB = new Color(0.1f, 0.4f, 0.8f);

    [Tooltip("Connect button gradient color C")]
    public Color connectColorC = new Color(0.7f, 0.3f, 1f);

    [Tooltip("Connect button shimmer speed")]
    public float connectShimmerSpeed = 0.4f;

    [Tooltip("Connect button HDR boost")]
    public float connectHDRBoost = 1.8f;
    #endregion

    #region Status Colors
    [Header("Status Colors")]
    [Tooltip("Success/Excellent status color (Green)")]
    public Color statusExcellent = new Color(0.2f, 1f, 0.4f);

    [Tooltip("Good status color (Yellow-Green)")]
    public Color statusGood = new Color(0.5f, 1f, 0.3f);

    [Tooltip("Warning/Fair status color (Orange)")]
    public Color statusWarning = new Color(1f, 0.8f, 0.2f);

    [Tooltip("Error/Poor status color (Red)")]
    public Color statusError = new Color(1f, 0.4f, 0.3f);
    #endregion

    #region Floating Particles
    [Header("Floating Data Particles")]
    [Tooltip("Particle color A (usually primary)")]
    public Color particleColorA = new Color(0.3f, 1f, 1f, 0.3f);

    [Tooltip("Particle color B (usually accent)")]
    public Color particleColorB = new Color(1f, 0.4f, 1f, 0.3f);

    [Tooltip("Particle alpha range min")]
    [Range(0f, 1f)]
    public float particleAlphaMin = 0.1f;

    [Tooltip("Particle alpha range max")]
    [Range(0f, 1f)]
    public float particleAlphaMax = 0.4f;
    #endregion

    #region Text Colors
    [Header("Text Colors")]
    [Tooltip("Primary text color")]
    public Color textPrimary = Color.white;

    [Tooltip("Secondary/dimmed text color")]
    public Color textSecondary = new Color(0.7f, 0.7f, 0.7f);

    [Tooltip("Placeholder text color")]
    public Color textPlaceholder = new Color(0.5f, 0.5f, 0.5f);
    #endregion

    #region Runtime Change Event
    /// <summary>
    /// Event fired when any theme property changes at runtime.
    /// Subscribe to this to react to individual property changes.
    /// </summary>
    public static event System.Action OnAnyThemePropertyChanged;

    /// <summary>
    /// Call this after modifying any property at runtime to notify listeners.
    /// </summary>
    public void NotifyPropertyChanged()
    {
        OnAnyThemePropertyChanged?.Invoke();
    }

    /// <summary>
    /// Set primary color and notify listeners.
    /// </summary>
    public void SetPrimaryColor(Color color)
    {
        primaryColor = color;
        NotifyPropertyChanged();
    }

    /// <summary>
    /// Set accent color and notify listeners.
    /// </summary>
    public void SetAccentColor(Color color)
    {
        accentColor = color;
        NotifyPropertyChanged();
    }

    /// <summary>
    /// Set glass colors and notify listeners.
    /// </summary>
    public void SetGlassColors(Color colorA, Color colorB)
    {
        glassColorA = colorA;
        glassColorB = colorB;
        NotifyPropertyChanged();
    }

    /// <summary>
    /// Set glow colors and notify listeners.
    /// </summary>
    public void SetGlowColors(Color colorA, Color colorB)
    {
        glowColorA = colorA;
        glowColorB = colorB;
        NotifyPropertyChanged();
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Get alternating color based on index (primary/accent pattern)
    /// </summary>
    public Color GetAlternatingColor(int index)
    {
        return index % 2 == 0 ? primaryColor : accentColor;
    }

    /// <summary>
    /// Get status color based on quality level (0-1)
    /// </summary>
    public Color GetStatusColor(float quality)
    {
        if (quality >= 0.9f) return statusExcellent;
        if (quality >= 0.7f) return statusGood;
        if (quality >= 0.4f) return statusWarning;
        return statusError;
    }

    /// <summary>
    /// Get particle color (alternating)
    /// </summary>
    public Color GetParticleColor(int index)
    {
        return index % 2 == 0 ? particleColorA : particleColorB;
    }

    /// <summary>
    /// Apply glass gradient settings to a material
    /// </summary>
    public void ApplyGlassToMaterial(Material material)
    {
        if (material == null) return;

        material.SetColor("_ColorA", glassColorA);
        material.SetColor("_ColorB", glassColorB);
        material.SetFloat("_GlassAlpha", glassAlpha);
        material.SetFloat("_Angle", glassGradientAngle);
        material.SetFloat("_Ratio", glassGradientRatio);
        material.SetFloat("_FresnelPower", glassFresnelPower);
        material.SetFloat("_FresnelStrength", glassFresnelStrength);
    }

    /// <summary>
    /// Apply glow border settings to a material
    /// </summary>
    public void ApplyGlowToMaterial(Material material)
    {
        if (material == null) return;

        material.SetColor("_ColorA", glowColorA);
        material.SetColor("_ColorB", glowColorB);
        material.SetFloat("_Layer1Width", glowLayer1Width);
        material.SetFloat("_Layer1Alpha", glowLayer1Alpha);
        material.SetFloat("_Layer2Width", glowLayer2Width);
        material.SetFloat("_Layer2Alpha", glowLayer2Alpha);
        material.SetFloat("_Layer3Width", glowLayer3Width);
        material.SetFloat("_Layer3Alpha", glowLayer3Alpha);
        material.SetFloat("_Layer4Width", glowLayer4Width);
        material.SetFloat("_Layer4Alpha", glowLayer4Alpha);
    }

    /// <summary>
    /// Apply connect button settings to a material
    /// </summary>
    public void ApplyConnectButtonToMaterial(Material material)
    {
        if (material == null) return;

        material.SetColor("_ColorA", connectColorA);
        material.SetColor("_ColorB", connectColorB);
        material.SetColor("_ColorC", connectColorC);
        material.SetFloat("_ShimmerSpeed", connectShimmerSpeed);
        material.SetFloat("_HDRBoost", connectHDRBoost);
    }
    #endregion
}
