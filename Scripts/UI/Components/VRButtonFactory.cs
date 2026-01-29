using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Factory class để tạo VR Button với đầy đủ hiệu ứng:
/// - Glass background với gradient
/// - Glowing border với hover effect
/// - Icon với glow shadows
/// - VRButtonAnimation cho hover (scale, z-pop, shader _HoverAmount)
/// - BoxCollider cho VR raycast
///
/// Các loại Button:
/// - CreateButton: Full button với config tùy chỉnh
/// - CreateIconButton: Icon vuông với khung
/// - CreateBareIconButton: Chỉ icon, không khung/text (frameless)
/// - CreateLabelButton: Icon + Text dọc
/// - CreateTextButton: Chỉ text
/// - CreateHorizontalIconTextButton: Icon trái + Text phải
///
/// Cấu trúc tạo ra:
/// - Wrapper (Btn_[label])
///   - HitArea (Image clear, BoxCollider, Button, VRButtonAnimation)
///     - Visuals (expansion để border không bị cắt, hoặc full size nếu frameless)
///       - Background (GlassGradientBackground shader) - bỏ qua nếu frameless
///       - Border (GlowingElementBorder shader + VRButtonRipple) - bỏ qua nếu frameless
///       - Content
///         - Icon (với Shadow glow)
///         - TextTMP
/// </summary>
public static class VRButtonFactory
{
    private static Sprite _pixelSprite;

    #region Theme Support

    /// <summary>
    /// Get primary color from theme or fallback
    /// </summary>
    public static Color GetPrimaryColor()
    {
        return RTTManager.Instance?.Theme?.primaryColor ?? new Color(0f, 0.9f, 1f);
    }

    /// <summary>
    /// Get accent color from theme or fallback
    /// </summary>
    public static Color GetAccentColor()
    {
        return RTTManager.Instance?.Theme?.accentColor ?? new Color(0.76f, 0.36f, 1f);
    }

    /// <summary>
    /// Get alternating color (primary/accent) from theme
    /// </summary>
    public static Color GetAlternatingColor(int index)
    {
        return RTTManager.Instance?.GetAlternatingColor(index) ??
            (index % 2 == 0 ? GetPrimaryColor() : GetAccentColor());
    }

    /// <summary>
    /// Get button active color from theme
    /// </summary>
    public static Color GetButtonActiveColor()
    {
        return RTTManager.Instance?.Theme?.buttonActiveColor ?? new Color(0.9f, 0.3f, 1f);
    }

    /// <summary>
    /// Get button inactive color from theme
    /// </summary>
    public static Color GetButtonInactiveColor()
    {
        return RTTManager.Instance?.Theme?.buttonInactiveColor ?? new Color(0f, 0.9f, 1f);
    }

    /// <summary>
    /// Get connect button colors from theme
    /// </summary>
    public static void GetConnectButtonColors(out Color colorA, out Color colorB, out Color colorC)
    {
        var theme = RTTManager.Instance?.Theme;
        if (theme != null)
        {
            colorA = theme.connectColorA;
            colorB = theme.connectColorB;
            colorC = theme.connectColorC;
        }
        else
        {
            colorA = new Color(0.2f, 0.9f, 1f);
            colorB = new Color(0.1f, 0.4f, 0.8f);
            colorC = new Color(0.7f, 0.3f, 1f);
        }
    }

    /// <summary>
    /// Get effective color for a button config.
    /// Uses theme color if useThemeColors is true, otherwise uses specified themeColor.
    /// </summary>
    private static Color GetEffectiveColor(ButtonConfig config)
    {
        if (config.useThemeColors)
        {
            return GetPrimaryColor();
        }
        return config.themeColor;
    }

    #endregion

    /// <summary>
    /// Cấu hình cho Button
    /// </summary>
    [System.Serializable]
    public class ButtonConfig
    {
        public string label = "Button";
        public Sprite icon;
        public Color themeColor = new Color(0f, 0.9f, 1f);
        public bool useThemeColors = false; // If true, use colors from RTTThemeConfig
        public float width = 200f;
        public float height = 80f;
        public int fontSize = 40;
        public TMP_FontAsset font;

        // Layout
        public bool iconOnly = false;
        public bool textOnly = false;
        public bool horizontalLayout = false; // Icon trái, Text phải (như nút Back)
        public float iconSize = 44f;
        public float iconPadding = 28f;
        public float spacing = 18f;

        // Visual settings
        public float cornerRadius = 0.12f;
        public float edgePadding = 0.06f;  // Match Space key value for Android compatibility
        public float backgroundAlpha = 0.08f;
        public float borderWidth = 0.025f; 
        public float glowWidth = 0.04f;    
        public float glowIntensity = 2.5f;

        // Animation
        public float popAmount = 0.05f;
        public float hoverScaleAmount = 0.05f; // Scale increase when hovering (0.05 = 5%, 0.15 = 15%)
        public bool enablePulse = false;
        public float pulseSpeed = 2f;

        // Frameless mode (chỉ có icon, không có background và border)
        public bool frameless = false;

        // Special Connect Button shader (all-in-one gradient + glow + shimmer)
        public bool useConnectButtonShader = false;
        public Color connectColorA = new Color(0.2f, 0.9f, 1f);  // Cyan
        public Color connectColorB = new Color(0.1f, 0.4f, 0.8f); // Deep Sea Blue
        public Color connectColorC = new Color(0.7f, 0.3f, 1f);  // Purple

        // Layer
        public string layerName = "VirtualObjects";
    }

    /// <summary>
    /// Tạo VR Button với đầy đủ hiệu ứng
    /// </summary>
    public static GameObject CreateButton(Transform parent, ButtonConfig config, UnityEngine.Events.UnityAction onClick)
    {
        // 1. Wrapper
        GameObject wrapper = new GameObject("Btn_" + config.label);
        wrapper.transform.SetParent(parent, false);
        RectTransform wrapperRT = wrapper.AddComponent<RectTransform>();
        wrapperRT.sizeDelta = new Vector2(config.width, config.height);

        // 2. HitArea - vùng click/collider
        GameObject hitArea = new GameObject("HitArea");
        hitArea.transform.SetParent(wrapper.transform, false);
        RectTransform hitRT = hitArea.AddComponent<RectTransform>();
        hitRT.anchorMin = Vector2.zero;
        hitRT.anchorMax = Vector2.one;
        hitRT.offsetMin = Vector2.zero;
        hitRT.offsetMax = Vector2.zero;

        // Transparent image cho UI raycast
        Image hitImg = hitArea.AddComponent<Image>();
        hitImg.color = Color.clear;

        // BoxCollider cho VR raycast
        BoxCollider col = hitArea.AddComponent<BoxCollider>();
        col.size = new Vector3(config.width, config.height, 0.1f);
        col.center = new Vector3(0, 0, -0.1f);

        // Set layer
        int vrLayer = LayerMask.NameToLayer(config.layerName);
        if (vrLayer != -1) hitArea.layer = vrLayer;

        // 3. Visuals - container cho visual elements với expansion
        GameObject visuals = new GameObject("Visuals");
        visuals.transform.SetParent(hitArea.transform, false);
        RectTransform visRT = visuals.AddComponent<RectTransform>();

        Image bgImg = null;

        if (config.frameless)
        {
            // Frameless mode: không cần expansion, không có background/border
            visRT.anchorMin = Vector2.zero;
            visRT.anchorMax = Vector2.one;
            visRT.offsetMin = Vector2.zero;
            visRT.offsetMax = Vector2.zero;

            // Chỉ tạo Content (Icon)
            CreateContent(visuals.transform, config);

            // Tạo transparent image cho button target graphic
            bgImg = visuals.AddComponent<Image>();
            bgImg.color = Color.clear;
            bgImg.raycastTarget = false;
        }
        else if (config.useConnectButtonShader)
        {
            // Special Connect Button: all-in-one shader with gradient + glow + shimmer
            // Keep within bounds - edge padding in shader handles visual margin
            visRT.anchorMin = Vector2.zero;
            visRT.anchorMax = Vector2.one;
            visRT.offsetMin = Vector2.zero;
            visRT.offsetMax = Vector2.zero;

            // Single layer with Connect Button shader (combines background + border)
            bgImg = CreateConnectButtonBackground(visuals.transform, config);

            // Content (Text)
            CreateContent(visuals.transform, config);
        }
        else
        {
            // Keep within bounds - edge padding in shader handles visual margin
            visRT.anchorMin = Vector2.zero;
            visRT.anchorMax = Vector2.one;
            visRT.offsetMin = Vector2.zero;
            visRT.offsetMax = Vector2.zero;

            // 4. Background
            bgImg = CreateBackground(visuals.transform, config);

            // 5. Border với ripple effect
            CreateBorder(visuals.transform, config);

            // 6. Content (Icon + Text)
            CreateContent(visuals.transform, config);
        }

        // 7. Button component
        Button btn = hitArea.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.transition = Selectable.Transition.None; // Tắt flash effect
        if (onClick != null)
        {
            btn.onClick.AddListener(onClick);
        }

        // 8. Hover effects - using unified HoverEffectController
        HoverEffectController hoverController = hitArea.AddComponent<HoverEffectController>();
        hoverController.TargetVisuals = visuals.transform;

        // Add effects based on config
        // Note: Connect button shader handles glow/border internally, skip GlowBorderHoverEffect
        if (!config.frameless && !config.useConnectButtonShader)
        {
            hoverController.AddEffect(new GlowBorderHoverEffect()
                .WithShaderSwap(true)
                .WithBorderMultiplier(1f));

            hoverController.AddEffect(new BackgroundHoverEffect());
        }

        hoverController.AddEffect(new ScaleHoverEffect()
            .WithHoverScale(1f + config.hoverScaleAmount));

        if (config.popAmount > 0)
        {
            hoverController.AddEffect(new ZPopHoverEffect()
                .WithPopAmount(config.popAmount));
        }

        // 9. VRButtonAnimation for ripple click effect
        VRButtonAnimation anim = hitArea.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visuals.transform;

        return wrapper;
    }

    /// <summary>
    /// Tạo Button đơn giản chỉ có icon (hình vuông)
    /// </summary>
    public static GameObject CreateIconButton(Transform parent, float size, Sprite icon, Color color,
        UnityEngine.Events.UnityAction onClick, float popAmount = 0.0125f)
    {
        var config = new ButtonConfig
        {
            label = icon != null ? icon.name : "IconBtn",
            icon = icon,
            themeColor = color,
            width = size,
            height = size,
            iconOnly = true,
            iconSize = size * 0.55f,
            cornerRadius = 0.15f,
            popAmount = popAmount
        };
        return CreateButton(parent, config, onClick);
    }

    /// <summary>
    /// Tạo Button chỉ có icon, không có khung (background/border) và text
    /// Icon có glow effect và hover animation
    /// </summary>
    /// <param name="hoverScaleAmount">Scale increase on hover (0.05=5%, 0.15=15%). Use higher values for RTT panels where Z-pop doesn't work.</param>
    public static GameObject CreateBareIconButton(Transform parent, float size, Sprite icon, Color color,
        UnityEngine.Events.UnityAction onClick, float popAmount = 0.005f, float iconScale = 0.7f, float hoverScaleAmount = 0.15f)
    {
        var config = new ButtonConfig
        {
            label = icon != null ? icon.name : "BareIconBtn",
            icon = icon,
            themeColor = color,
            width = size,
            height = size,
            iconOnly = true,
            iconSize = size * iconScale,
            frameless = true,
            popAmount = popAmount,
            hoverScaleAmount = hoverScaleAmount
        };
        return CreateButton(parent, config, onClick);
    }

    /// <summary>
    /// Tạo Button với icon và text (giống VRMainMenu)
    /// </summary>
    public static GameObject CreateLabelButton(Transform parent, float width, float height,
        string label, Sprite icon, Color color, UnityEngine.Events.UnityAction onClick,
        int fontSize = 40, TMP_FontAsset font = null)
    {
        var config = new ButtonConfig
        {
            label = label,
            icon = icon,
            themeColor = color,
            width = width,
            height = height,
            fontSize = fontSize,
            font = font,
            popAmount = 0.05f
        };
        return CreateButton(parent, config, onClick);
    }

    /// <summary>
    /// Tạo Button chỉ có text
    /// </summary>
    public static GameObject CreateTextButton(Transform parent, float width, float height,
        string label, Color color, UnityEngine.Events.UnityAction onClick,
        int fontSize = 40, TMP_FontAsset font = null, bool enablePulse = false)
    {
        var config = new ButtonConfig
        {
            label = label,
            themeColor = color,
            width = width,
            height = height,
            fontSize = fontSize,
            font = font,
            textOnly = true,
            enablePulse = enablePulse,
            glowIntensity = enablePulse ? 5f : 3.5f,
            popAmount = 0.05f
        };
        return CreateButton(parent, config, onClick);
    }

    /// <summary>
    /// Tạo Button hình chữ nhật với Icon bên trái, Text bên phải (như nút Back)
    /// </summary>
    public static GameObject CreateHorizontalIconTextButton(Transform parent, float width, float height,
        string label, Sprite icon, Color color, UnityEngine.Events.UnityAction onClick,
        int fontSize = 40, TMP_FontAsset font = null, float iconSize = 44f)
    {
        var config = new ButtonConfig
        {
            label = label,
            icon = icon,
            themeColor = color,
            width = width,
            height = height,
            fontSize = fontSize,
            font = font,
            horizontalLayout = true,
            iconSize = iconSize,
            iconPadding = 28f,
            backgroundAlpha = 0.28f,
            borderWidth = 0.042f, // Increased 40% (was 0.03f)
            popAmount = 0.0125f
        };
        return CreateButton(parent, config, onClick);
    }

    // ==================== INTERNAL HELPERS ====================

    private static Image CreateBackground(Transform parent, ButtonConfig config)
    {
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(parent, false);
        RectTransform rt = bgObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = bgObj.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = false;

        float aspect = config.width / config.height;
        Color col = config.themeColor;

        // Use Wide shader for better Android GPU compatibility
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
            Material mat = new Material(glassShader);
            mat.SetFloat("_CornerRadius", config.cornerRadius);
            mat.SetFloat("_EdgePadding", config.edgePadding);
            mat.SetFloat("_Aspect", aspect);
            mat.SetColor("_ColorA", new Color(col.r, col.g, col.b, config.backgroundAlpha * 1.5f));
            mat.SetColor("_ColorB", new Color(col.r, col.g, col.b, config.backgroundAlpha * 0.5f));
            mat.SetFloat("_GlassAlpha", config.backgroundAlpha);
            img.material = mat;
            img.color = Color.white;
        }
        else
        {
            img.color = new Color(col.r, col.g, col.b, config.backgroundAlpha);
        }

        return img;
    }

    /// <summary>
    /// Tạo background cho Connect Button với shader đặc biệt (gradient + glow + shimmer)
    /// </summary>
    private static Image CreateConnectButtonBackground(Transform parent, ButtonConfig config)
    {
        GameObject bgObj = new GameObject("ConnectBackground");
        bgObj.transform.SetParent(parent, false);
        RectTransform rt = bgObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = bgObj.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = false;

        float aspect = config.width / config.height;

        // Try to find the shader first
        Shader connectShader = Shader.Find("Custom/GlowingConnectButton");

        // Load base material from Resources and clone it
        Material baseMat = Resources.Load<Material>("GlowingConnectButton");

        // Create material - prefer from shader directly for reliability
        Material mat = null;
        if (connectShader != null)
        {
            mat = new Material(connectShader);
        }
        else if (baseMat != null && baseMat.shader != null && baseMat.shader.name != "Hidden/InternalErrorShader")
        {
            mat = new Material(baseMat);
        }

        if (mat != null)
        {

            // Border settings
            mat.SetFloat("_EdgePadding", config.edgePadding);
            mat.SetFloat("_BorderWidth", 0.028f); // Increased 40% (was 0.02f)
            mat.SetFloat("_CornerRadius", config.cornerRadius);
            mat.SetFloat("_Aspect", aspect);

            // Gradient colors (3-color: 35% Cyan -> 35% Deep Sea Blue -> 30% Purple)
            // Use theme colors if configured
            Color colorA, colorB, colorC;
            if (config.useThemeColors)
            {
                GetConnectButtonColors(out colorA, out colorB, out colorC);
            }
            else
            {
                colorA = config.connectColorA;
                colorB = config.connectColorB;
                colorC = config.connectColorC;
            }
            mat.SetColor("_ColorA", colorA);
            mat.SetColor("_ColorB", colorB);
            mat.SetColor("_ColorC", colorC);
            mat.SetFloat("_GradientAngle", 0f);
            mat.SetFloat("_MidPoint1", 0.35f);  // Cyan zone ends at 35%
            mat.SetFloat("_MidPoint2", 0.70f);  // Blue zone ends at 70% (35%+35%)

            // Background
            mat.SetFloat("_BgAlpha", config.backgroundAlpha);
            mat.SetFloat("_BgGradientStrength", 1.0f);

            // Glow layers
            mat.SetFloat("_Layer1Alpha", 1.5f);
            mat.SetFloat("_Layer2Alpha", 1.0f);
            mat.SetFloat("_Layer3Alpha", 0.5f);
            mat.SetFloat("_Layer4Alpha", 0.25f);

            // Pulse animation
            mat.SetFloat("_PulseEnabled", config.enablePulse ? 1f : 0f);
            mat.SetFloat("_PulseSpeed", config.pulseSpeed);
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

            img.material = mat;
            img.color = Color.white;

            // Add ripple effect
            bgObj.AddComponent<VRButtonRipple>().Initialize(mat, img);
        }
        else
        {
            // Fallback nếu không tìm thấy shader hoặc material
            Debug.LogError("[VRButtonFactory] GlowingConnectButton shader/material not found! Shader.Find returned: " +
                (connectShader != null ? connectShader.name : "null"));
            Color fallbackColor = Color.Lerp(config.connectColorA, config.connectColorB, 0.5f);
            fallbackColor.a = config.backgroundAlpha;
            img.color = fallbackColor;
        }

        return img;
    }

    private static void CreateBorder(Transform parent, ButtonConfig config)
    {
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(parent, false);
        RectTransform rt = borderObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = borderObj.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = false;

        float aspect = config.width / config.height;
        Color col = config.themeColor;

        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            Material mat = new Material(glowShader);
            mat.SetFloat("_Aspect", aspect);
            mat.SetFloat("_EdgePadding", config.edgePadding);
            mat.SetFloat("_CornerRadius", config.cornerRadius);

            Color borderGlowCol = Color.Lerp(col, Color.white, 0.75f);
            mat.SetColor("_GlowColor", borderGlowCol);

            mat.SetFloat("_BorderWidth", config.borderWidth);
            mat.SetFloat("_GlowWidth", config.glowWidth);
            mat.SetFloat("_GlowIntensity", config.glowIntensity);
            mat.SetFloat("_PulseEnabled", config.enablePulse ? 1f : 0f);

            if (config.enablePulse)
            {
                mat.SetFloat("_PulseSpeed", config.pulseSpeed);
            }

            img.material = mat;

            // VRButtonRipple cho ripple effect
            borderObj.AddComponent<VRButtonRipple>().Initialize(mat, img);
        }
    }

    private static void CreateContent(Transform parent, ButtonConfig config)
    {
        GameObject content = new GameObject("Content");
        content.transform.SetParent(parent, false);
        RectTransform cRT = content.AddComponent<RectTransform>();
        cRT.anchorMin = Vector2.zero;
        cRT.anchorMax = Vector2.one;
        cRT.offsetMin = Vector2.zero;
        cRT.offsetMax = Vector2.zero;

        bool hasIcon = config.icon != null && !config.textOnly;
        bool hasText = !string.IsNullOrEmpty(config.label) && !config.iconOnly;

        if (config.iconOnly && config.icon != null)
        {
            // Icon only - căn giữa, tính anchor spread dựa trên iconSize và button size
            float anchorHalfW = (config.iconSize / 2f) / config.width;
            float anchorHalfH = (config.iconSize / 2f) / config.height;
            CreateIconWithGlow(content.transform, 0.5f, 0.5f, anchorHalfW, anchorHalfH, config.icon, config.themeColor, true);
        }
        else if (config.textOnly)
        {
            // Text only - căn giữa
            CreateText(content.transform, config.label, config.fontSize, config.font, config.themeColor, true);
        }
        else if (config.horizontalLayout && hasIcon && hasText)
        {
            // Horizontal layout: Icon trái, Text phải (như nút Back)
            CreateHorizontalIconText(content.transform, config);
        }
        else if (hasIcon && hasText)
        {
            // Vertical layout: Icon trên, Text dưới (default) - dùng fixed anchors
            CreateIconWithGlow(content.transform, 0.5f, 0.57f, 0f, 0f, config.icon, config.themeColor, false);
            CreateTextBelow(content.transform, config.label, config.fontSize, config.font, 0.18f, 0.42f);
        }
        else if (hasText)
        {
            // Text only fallback
            CreateText(content.transform, config.label, config.fontSize, config.font, config.themeColor, false);
        }
    }

    private static void CreateIconWithGlow(Transform parent, float anchorX, float anchorY,
        float anchorHalfW, float anchorHalfH, Sprite sprite, Color col, bool useAnchorCenter)
    {
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(parent, false);
        RectTransform rt = iconObj.AddComponent<RectTransform>();

        if (useAnchorCenter)
        {
            // Icon-only buttons: sử dụng anchor spread được tính toán
            rt.anchorMin = new Vector2(anchorX - anchorHalfW, anchorY - anchorHalfH);
            rt.anchorMax = new Vector2(anchorX + anchorHalfW, anchorY + anchorHalfH);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        else
        {
            // Vertical layout (icon trên, text dưới): dùng fixed anchors
            rt.anchorMin = new Vector2(0.35f, 0.42f);
            rt.anchorMax = new Vector2(0.65f, 0.72f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        Image img = iconObj.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.color = Color.Lerp(col, Color.white, 0.9f);

        // Glow Layer 1 - Sharp inner halo
        Color glowCol = Color.Lerp(col, Color.white, 0.7f);
        glowCol.a = 0.4f;
        float s1 = 2f;

        Shadow shadow1 = iconObj.AddComponent<Shadow>();
        shadow1.effectColor = glowCol;
        shadow1.effectDistance = new Vector2(s1, -s1);

        Shadow shadow2 = iconObj.AddComponent<Shadow>();
        shadow2.effectColor = glowCol;
        shadow2.effectDistance = new Vector2(-s1, s1);
    }

    private static void CreateText(Transform parent, string text, int fontSize, TMP_FontAsset font, Color col, bool addGlow)
    {
        GameObject txtObj = new GameObject("TextTMP");
        txtObj.transform.SetParent(parent, false);
        RectTransform rt = txtObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = FontStyles.Bold;
        txt.raycastTarget = false;
        if (font != null) txt.font = font;

        if (addGlow)
        {
            Color glow = Color.Lerp(col, Color.white, 0.8f);
            glow.a = 0.55f;

            Shadow s1 = txtObj.AddComponent<Shadow>();
            s1.effectColor = glow;
            s1.effectDistance = new Vector2(3, -3);

            Shadow s2 = txtObj.AddComponent<Shadow>();
            s2.effectColor = glow;
            s2.effectDistance = new Vector2(-3, 3);
        }
    }

    private static void CreateTextBelow(Transform parent, string text, int fontSize, TMP_FontAsset font,
        float anchorYMin, float anchorYMax)
    {
        GameObject txtObj = new GameObject("TextTMP");
        txtObj.transform.SetParent(parent, false);
        RectTransform rt = txtObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, anchorYMin);
        rt.anchorMax = new Vector2(1f, anchorYMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = FontStyles.Bold;
        txt.raycastTarget = false;
        if (font != null) txt.font = font;
    }

    /// <summary>
    /// Tạo layout ngang: Icon bên trái, Text bên phải
    /// </summary>
    /// <summary>
    /// Tạo layout ngang: Icon bên trái, Text bên phải
    /// Uses a centered container with HorizontalLayoutGroup to ensure Icon + Text are centered as a unit
    /// </summary>
    private static void CreateHorizontalIconText(Transform parent, ButtonConfig config)
    {
        // 1. Create a centered container acting as the group wrapper
        GameObject container = new GameObject("CenteredContent");
        container.transform.SetParent(parent, false);
        RectTransform containerRT = container.AddComponent<RectTransform>();
        // Center in parent
        containerRT.anchorMin = new Vector2(0.5f, 0.5f);
        containerRT.anchorMax = new Vector2(0.5f, 0.5f);
        containerRT.pivot = new Vector2(0.5f, 0.5f);
        containerRT.anchoredPosition = Vector2.zero;
        
        // 2. Add Layout Group to the container
        HorizontalLayoutGroup layout = container.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = config.spacing;
        layout.childControlWidth = false; 
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        // 3. Add ContentSizeFitter so container hugs the children
        ContentSizeFitter containerCsf = container.AddComponent<ContentSizeFitter>();
        containerCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        containerCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 4. Icon Object
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(container.transform, false);
        RectTransform iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.sizeDelta = new Vector2(config.iconSize, config.iconSize);

        Image iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = config.icon;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        iconImg.color = Color.Lerp(config.themeColor, Color.white, 0.9f);

        // Icon glow effects
        Color glowCol = Color.Lerp(config.themeColor, Color.white, 0.7f);
        glowCol.a = 0.4f;
        float s1 = 2f;

        Shadow shadow1 = iconObj.AddComponent<Shadow>();
        shadow1.effectColor = glowCol;
        shadow1.effectDistance = new Vector2(s1, -s1);

        Shadow shadow2 = iconObj.AddComponent<Shadow>();
        shadow2.effectColor = glowCol;
        shadow2.effectDistance = new Vector2(-s1, s1);

        // 5. Text Object
        GameObject txtObj = new GameObject("TextTMP");
        txtObj.transform.SetParent(container.transform, false);
        
        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.text = config.label;
        txt.fontSize = config.fontSize;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Left;
        txt.verticalAlignment = VerticalAlignmentOptions.Middle;
        txt.fontStyle = FontStyles.Bold;
        txt.raycastTarget = false;
        if (config.font != null) txt.font = config.font;

        // Auto-size text to fit content so LayoutGroup knows width
        ContentSizeFitter csf = txtObj.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private static Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }

    // ==================== UTILITY METHODS ====================

    /// <summary>
    /// Bật/tắt khả năng tương tác của button
    /// </summary>
    public static void SetInteractable(GameObject wrapper, bool interactable)
    {
        if (wrapper == null) return;

        // Tìm Button component trong HitArea
        Button btn = wrapper.GetComponentInChildren<Button>();
        if (btn != null)
        {
            btn.interactable = interactable;
        }

        // Làm mờ visual khi disabled
        Transform visuals = wrapper.transform.Find("HitArea/Visuals");
        if (visuals != null)
        {
            CanvasGroup cg = visuals.GetComponent<CanvasGroup>();
            if (cg == null) cg = visuals.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = interactable ? 1f : 0.4f;
        }
    }

    /// <summary>
    /// Thay đổi glow color của BareIconButton
    /// Updates icon tint and shadow glow colors
    /// </summary>
    public static void SetBareIconButtonGlowColor(GameObject wrapper, Color newColor)
    {
        if (wrapper == null) return;

        // Find Icon in hierarchy: wrapper > HitArea > Visuals > Content > Icon
        Transform visuals = wrapper.transform.Find("HitArea/Visuals");
        if (visuals == null) return;

        Transform content = visuals.Find("Content");
        if (content == null) return;

        Transform iconTransform = content.Find("Icon");
        if (iconTransform == null) return;

        // Update icon tint color
        Image iconImg = iconTransform.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.color = Color.Lerp(newColor, Color.white, 0.9f);
        }

        // Update shadow glow colors
        Color glowCol = Color.Lerp(newColor, Color.white, 0.7f);
        glowCol.a = 0.4f;

        Shadow[] shadows = iconTransform.GetComponents<Shadow>();
        foreach (var shadow in shadows)
        {
            shadow.effectColor = glowCol;
        }
    }

    /// <summary>
    /// Change the icon sprite of a BareIconButton.
    /// </summary>
    public static void SetBareIconButtonSprite(GameObject wrapper, Sprite newSprite)
    {
        if (wrapper == null || newSprite == null) return;

        // Find Icon in hierarchy: wrapper > HitArea > Visuals > Content > Icon
        Transform visuals = wrapper.transform.Find("HitArea/Visuals");
        if (visuals == null) return;

        Transform content = visuals.Find("Content");
        if (content == null) return;

        Transform iconTransform = content.Find("Icon");
        if (iconTransform == null) return;

        // Update icon sprite
        Image iconImg = iconTransform.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.sprite = newSprite;
        }
    }
}
