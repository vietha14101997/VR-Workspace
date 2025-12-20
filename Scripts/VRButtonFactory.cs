using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Factory class để tạo VR Button với đầy đủ hiệu ứng:
/// - Glass background với gradient
/// - Glowing border với hover effect
/// - Icon với glow shadows
/// - VRButtonAnimation cho hover (scale, z-pop, shader _HoverAmount)
/// - BoxCollider cho VR raycast
///
/// Cấu trúc tạo ra:
/// - Wrapper (Btn_[label])
///   - HitArea (Image clear, BoxCollider, Button, VRButtonAnimation)
///     - Visuals (expansion để border không bị cắt)
///       - Background (GlassGradientBackground shader)
///       - Border (GlowingElementBorder shader + VRButtonRipple)
///       - Content
///         - Icon (với Shadow glow)
///         - TextTMP
/// </summary>
public static class VRButtonFactory
{
    private static Sprite _pixelSprite;

    /// <summary>
    /// Cấu hình cho Button
    /// </summary>
    [System.Serializable]
    public class ButtonConfig
    {
        public string label = "Button";
        public Sprite icon;
        public Color themeColor = new Color(0f, 0.9f, 1f);
        public float width = 200f;
        public float height = 80f;
        public int fontSize = 40;
        public TMP_FontAsset font;

        // Layout
        public bool iconOnly = false;
        public bool textOnly = false;
        public float iconSize = 44f;
        public float iconPadding = 28f;

        // Visual settings
        public float cornerRadius = 0.12f;
        public float edgePadding = 0.12f;
        public float backgroundAlpha = 0.08f;
        public float borderWidth = 0.005f;
        public float glowWidth = 0.03f;
        public float glowIntensity = 2.5f;

        // Animation
        public float popAmount = 0.05f;
        public bool enablePulse = false;
        public float pulseSpeed = 2f;

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

        float expansion = config.edgePadding;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = Vector2.zero;
        visRT.offsetMax = Vector2.zero;

        // 4. Background
        Image bgImg = CreateBackground(visuals.transform, config);

        // 5. Border với ripple effect
        CreateBorder(visuals.transform, config);

        // 6. Content (Icon + Text)
        CreateContent(visuals.transform, config);

        // 7. Button component
        Button btn = hitArea.AddComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.transition = Selectable.Transition.None; // Tắt flash effect
        if (onClick != null)
        {
            btn.onClick.AddListener(onClick);
        }

        // 8. VRButtonAnimation cho hover effects
        VRButtonAnimation anim = hitArea.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visuals.transform;
        anim.popAmount = config.popAmount;

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
            glowIntensity = enablePulse ? 5f : 2.5f,
            popAmount = 0.05f
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

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
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
            // Icon only - căn giữa
            CreateIconWithGlow(content.transform, 0.5f, 0.5f, config.iconSize, config.icon, config.themeColor, true);
        }
        else if (config.textOnly)
        {
            // Text only - căn giữa
            CreateText(content.transform, config.label, config.fontSize, config.font, config.themeColor, true);
        }
        else if (hasIcon && hasText)
        {
            // Icon + Text layout
            float iconCenterX = config.iconPadding + config.iconSize / 2f;
            float iconAnchorX = iconCenterX / config.width;

            CreateIconWithGlow(content.transform, iconAnchorX, 0.57f, config.iconSize, config.icon, config.themeColor, false);
            CreateTextBelow(content.transform, config.label, config.fontSize, config.font, 0.18f, 0.42f);
        }
        else if (hasText)
        {
            // Text only fallback
            CreateText(content.transform, config.label, config.fontSize, config.font, config.themeColor, false);
        }
    }

    private static void CreateIconWithGlow(Transform parent, float anchorX, float anchorY,
        float size, Sprite sprite, Color col, bool useAnchorCenter)
    {
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(parent, false);
        RectTransform rt = iconObj.AddComponent<RectTransform>();

        if (useAnchorCenter)
        {
            rt.anchorMin = new Vector2(anchorX - 0.15f, anchorY - 0.15f);
            rt.anchorMax = new Vector2(anchorX + 0.15f, anchorY + 0.15f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        else
        {
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

        // Glow Layer 2 - Soft outer bloom
        Color bloomCol = Color.Lerp(col, Color.white, 0.8f);
        bloomCol.a = 0.15f;
        float s2 = 5f;

        Shadow shadow3 = iconObj.AddComponent<Shadow>();
        shadow3.effectColor = bloomCol;
        shadow3.effectDistance = new Vector2(s2, -s2);

        Shadow shadow4 = iconObj.AddComponent<Shadow>();
        shadow4.effectColor = bloomCol;
        shadow4.effectDistance = new Vector2(-s2, s2);
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

    private static Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }
}
