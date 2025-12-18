using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class VRRemoteMenu : MonoBehaviour
{
    // --- CONFIGURATION ---
    public Color themeColor = new Color(0.0f, 0.9f, 1.0f); // Cyan
    public Color accentColor = new Color(0.8f, 0.4f, 1.0f); // Purple
    public TMP_FontAsset customFont;

    private VRMainMenu _mainMenu;
    private Sprite _pixelSprite;
    private Dictionary<string, Sprite> _cachedIcons = new Dictionary<string, Sprite>();

    public void BuildUI(Transform parent, VRMainMenu mainMenu)
    {
        _mainMenu = mainMenu;

        // Parent = ContentContainer (already below separator line at 80px from top)
        // ContentContainer height = 1080 - 80 = 1000px
        // We'll use ABSOLUTE positioning for clarity

        float totalWidth = 1920f;
        float totalHeight = 1000f; // Adjusted for separatorOffset (80px): 1080 - 80 = 1000

        float padding = 60f;
        float topPadding = 30f; // Extra spacing from separator line
        float contentWidth = totalWidth - (padding * 2); // 1800px

        // === HEADER (Top: 0-80px) ===
        CreateHeaderSimple(parent, padding, totalHeight - 80f - topPadding, contentWidth, 80f);

        // === INPUT ROW (Top: 100-230px) ===
        CreateInputRowSimple(parent, padding, totalHeight - 230f - topPadding, contentWidth, 130f);

        // === GRID 2x2 (Top: 255-555px) ===
        CreateGridSimple(parent, padding, totalHeight - 580f - topPadding, contentWidth, 300f);

        // === FOOTER (Bottom: 0-100px) ===
        CreateFooterSimple(parent, padding, 40f, contentWidth, 100f);
    }

    void CreateHeaderSimple(Transform parent, float x, float y, float w, float h)
    {
        GameObject header = new GameObject("Header");
        header.transform.SetParent(parent, false);
        RectTransform rt = header.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        // Back button (left)
        CreateStyledButton(header.transform, 0, 5, 200, 70, "Back", GetIcon("back"), themeColor,
            () => _mainMenu.ReturnToMainMenu(), true);

        // Title (center)
        CreateSimpleText(header.transform, w/2 - 250, 10, 500, 60, "VR Remote Menu", 52, Color.white, true);

        // QR button (right)
        CreateStyledButton(header.transform, w - 90, 5, 90, 70, "", GetIcon("qr"), accentColor,
            () => Debug.Log("QR"), false);
    }

    void CreateInputRowSimple(Transform parent, float x, float y, float w, float h)
    {
        GameObject row = new GameObject("InputRow");
        row.transform.SetParent(parent, false);
        RectTransform rt = row.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        float hostW = w * 0.58f;
        float portW = w * 0.38f;
        float gap = w - hostW - portW;

        // Host input
        CreateStyledInputBox(row.transform, 0, 0, hostW, h, "Host", "192.168.1.10", themeColor);

        // Port input
        CreateStyledInputBox(row.transform, hostW + gap, 0, portW, h, "Port", "9000", accentColor);
    }

    void CreateGridSimple(Transform parent, float x, float y, float w, float h)
    {
        GameObject grid = new GameObject("Grid");
        grid.transform.SetParent(parent, false);
        RectTransform rt = grid.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        float cellW = (w - 50f) / 2f; // 2 columns, 50px gap
        float cellH = (h - 50f) / 2f; // 2 rows, 50px gap
        float gapX = 50f;
        float gapY = 50f;

        // Row 1
        CreateStyledGridItem(grid.transform, 0, cellH + gapY, cellW, cellH,
            "Monitor", "Monitor 1", GetIcon("monitor"), themeColor, true);
        CreateStyledGridItem(grid.transform, cellW + gapX, cellH + gapY, cellW, cellH,
            "Resolution", "1920 x 1080", GetIcon("resolution"), accentColor, false);

        // Row 2
        CreateStyledGridItem(grid.transform, 0, 0, cellW, cellH,
            "Bitrate", "10 Mbps", GetIcon("bitrate"), themeColor, false);
        CreateStyledGridItem(grid.transform, cellW + gapX, 0, cellW, cellH,
            "FPS", "60 FPS", GetIcon("fps"), accentColor, false);
    }

    void CreateFooterSimple(Transform parent, float x, float y, float w, float h)
    {
        GameObject footer = new GameObject("Footer");
        footer.transform.SetParent(parent, false);
        RectTransform rt = footer.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        // CONNECT button (centered)
        float btnW = 560f;
        float btnX = (w - btnW) / 2f;
        CreateConnectButton(footer.transform, btnX, 0, btnW, 90f);
    }

    // === HELPERS ===

    void CreateStyledButton(Transform parent, float x, float y, float w, float h, string label, Sprite icon, Color col, UnityEngine.Events.UnityAction onClick, bool hasLabel)
    {
        GameObject btn = new GameObject("Btn_" + (string.IsNullOrEmpty(label) ? "Icon" : label));
        btn.transform.SetParent(parent, false);
        RectTransform rt = btn.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        // Visual root with expansion for shader effects
        GameObject visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(btn.transform, false);
        RectTransform visRT = visualRoot.AddComponent<RectTransform>();
        float expansion = 0.12f;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = Vector2.zero;
        visRT.offsetMax = Vector2.zero;

        // Background with shader
        Image bg = visualRoot.AddComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.raycastTarget = false;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", 0.12f);
            glassMat.SetFloat("_EdgePadding", 0.12f);
            glassMat.SetFloat("_Aspect", w / h);
            glassMat.SetColor("_ColorA", new Color(col.r, col.g, col.b, 0.12f));
            glassMat.SetColor("_ColorB", new Color(col.r, col.g, col.b, 0.04f));
            glassMat.SetFloat("_GlassAlpha", 0.075f);
            bg.material = glassMat;
            bg.color = Color.white;
        }
        else
        {
            bg.color = new Color(col.r, col.g, col.b, 0.12f);
        }

        // Border with glow
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(visualRoot.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;

        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);
            glowMat.SetFloat("_Aspect", w / h);
            glowMat.SetFloat("_EdgePadding", 0.12f);
            Color borderGlowCol = Color.Lerp(col, Color.white, 0.75f);
            glowMat.SetColor("_GlowColor", borderGlowCol);
            glowMat.SetFloat("_BorderWidth", 0.005f);
            glowMat.SetFloat("_GlowWidth", 0.03f);
            glowMat.SetFloat("_GlowIntensity", 2.5f);
            glowMat.SetFloat("_CornerRadius", 0.12f);
            glowMat.SetFloat("_PulseEnabled", 0f);
            borderImg.material = glowMat;
            borderImg.sprite = GetPixelSprite();
        }

        // Icon with glow
        if (icon != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(visualRoot.transform, false);
            RectTransform iRT = iconObj.AddComponent<RectTransform>();

            if (hasLabel)
            {
                iRT.anchorMin = new Vector2(0.12f, 0.5f);
                iRT.anchorMax = new Vector2(0.12f, 0.5f);
                iRT.pivot = new Vector2(0.5f, 0.5f);
                iRT.sizeDelta = new Vector2(42, 42);
            }
            else
            {
                iRT.anchorMin = new Vector2(0.5f, 0.5f);
                iRT.anchorMax = new Vector2(0.5f, 0.5f);
                iRT.pivot = new Vector2(0.5f, 0.5f);
                iRT.sizeDelta = new Vector2(48, 48);
            }

            Image iImg = iconObj.AddComponent<Image>();
            iImg.sprite = icon;
            iImg.preserveAspect = true;
            iImg.raycastTarget = false;
            iImg.color = Color.Lerp(col, Color.white, 0.9f);

            // Glow effects
            Color glowCol = Color.Lerp(col, Color.white, 0.7f);
            glowCol.a = 0.4f;
            float s1 = 2f;

            iconObj.AddComponent<Shadow>().effectColor = glowCol;
            iconObj.GetComponent<Shadow>().effectDistance = new Vector2(s1, -s1);

            iconObj.AddComponent<Shadow>().effectColor = glowCol;
            iconObj.GetComponents<Shadow>()[1].effectDistance = new Vector2(-s1, s1);

            Color bloomCol = Color.Lerp(col, Color.white, 0.8f);
            bloomCol.a = 0.15f;
            float s2 = 5f;

            iconObj.AddComponent<Shadow>().effectColor = bloomCol;
            iconObj.GetComponents<Shadow>()[2].effectDistance = new Vector2(s2, -s2);

            iconObj.AddComponent<Shadow>().effectColor = bloomCol;
            iconObj.GetComponents<Shadow>()[3].effectDistance = new Vector2(-s2, s2);
        }

        // Label text
        if (!string.IsNullOrEmpty(label) && hasLabel)
        {
            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(visualRoot.transform, false);
            RectTransform tRT = txtObj.AddComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0.25f, 0f);
            tRT.anchorMax = new Vector2(1f, 1f);
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;
            TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 32;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Left;
            txt.verticalAlignment = VerticalAlignmentOptions.Middle;
            txt.fontStyle = FontStyles.Bold;
            txt.raycastTarget = false;
            if (customFont) txt.font = customFont;
        }

        // Button component
        Button b = btn.AddComponent<Button>();
        b.targetGraphic = bg;
        b.onClick.AddListener(onClick);

        ColorBlock cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(col.r, col.g, col.b, 0.5f);
        cb.pressedColor = new Color(col.r, col.g, col.b, 0.7f);
        cb.fadeDuration = 0.1f;
        b.colors = cb;
    }

    void CreateSimpleText(Transform parent, float x, float y, float w, float h, string text, int size, Color col, bool bold)
    {
        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(parent, false);
        RectTransform rt = txtObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.fontSize = size;
        txt.color = col;
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        if (customFont) txt.font = customFont;
    }

    void CreateStyledInputBox(Transform parent, float x, float y, float w, float h, string label, string value, Color col)
    {
        GameObject container = new GameObject("Input_" + label);
        container.transform.SetParent(parent, false);
        RectTransform rt = container.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        // Label
        CreateSimpleText(container.transform, 0, h - 40, w, 35, label, 28, new Color(1, 1, 1, 0.8f), false);

        // Input box
        GameObject box = new GameObject("Box");
        box.transform.SetParent(container.transform, false);
        RectTransform boxRT = box.AddComponent<RectTransform>();
        boxRT.anchorMin = Vector2.zero;
        boxRT.anchorMax = Vector2.zero;
        boxRT.pivot = Vector2.zero;
        boxRT.anchoredPosition = new Vector2(0, 0);
        boxRT.sizeDelta = new Vector2(w, 85);

        // Visual root with expansion
        GameObject visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(box.transform, false);
        RectTransform visRT = visualRoot.AddComponent<RectTransform>();
        float expansion = 0.08f;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = Vector2.zero;
        visRT.offsetMax = Vector2.zero;

        // Background with shader
        Image bg = visualRoot.AddComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.raycastTarget = false;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", 0.15f);
            glassMat.SetFloat("_EdgePadding", 0.08f);
            glassMat.SetFloat("_Aspect", w / 85);
            glassMat.SetColor("_ColorA", new Color(col.r, col.g, col.b, 0.15f));
            glassMat.SetColor("_ColorB", new Color(col.r, col.g, col.b, 0.05f));
            glassMat.SetFloat("_GlassAlpha", 0.08f);
            bg.material = glassMat;
            bg.color = Color.white;
        }
        else
        {
            bg.color = new Color(col.r, col.g, col.b, 0.15f);
        }

        // Border with glow
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(visualRoot.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;

        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);
            glowMat.SetFloat("_Aspect", w / 85);
            glowMat.SetFloat("_EdgePadding", 0.08f);
            Color borderGlowCol = Color.Lerp(col, Color.white, 0.75f);
            glowMat.SetColor("_GlowColor", borderGlowCol);
            glowMat.SetFloat("_BorderWidth", 0.004f);
            glowMat.SetFloat("_GlowWidth", 0.025f);
            glowMat.SetFloat("_GlowIntensity", 2.0f);
            glowMat.SetFloat("_CornerRadius", 0.15f);
            glowMat.SetFloat("_PulseEnabled", 0f);
            borderImg.material = glowMat;
            borderImg.sprite = GetPixelSprite();
        }

        // Dropdown icon
        GameObject dropIcon = new GameObject("DropdownIcon");
        dropIcon.transform.SetParent(visualRoot.transform, false);
        RectTransform dropRT = dropIcon.AddComponent<RectTransform>();
        dropRT.anchorMin = new Vector2(1f, 0.5f);
        dropRT.anchorMax = new Vector2(1f, 0.5f);
        dropRT.pivot = new Vector2(1f, 0.5f);
        dropRT.anchoredPosition = new Vector2(-20, 0);
        dropRT.sizeDelta = new Vector2(24, 24);

        Image dropImg = dropIcon.AddComponent<Image>();
        dropImg.sprite = GetIcon("arrow_down");
        dropImg.preserveAspect = true;
        dropImg.raycastTarget = false;
        dropImg.color = Color.Lerp(col, Color.white, 0.85f);

        // Value text
        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(visualRoot.transform, false);
        RectTransform txtRT = txtObj.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(25, 0);
        txtRT.offsetMax = new Vector2(-60, 0);

        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.text = value;
        txt.fontSize = 34;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Left;
        txt.verticalAlignment = VerticalAlignmentOptions.Middle;
        txt.fontStyle = FontStyles.Bold;
        txt.raycastTarget = false;
        if (customFont) txt.font = customFont;
    }

    void CreateStyledGridItem(Transform parent, float x, float y, float w, float h, string label, string value, Sprite icon, Color col, bool selected)
    {
        GameObject item = new GameObject("Item_" + label);
        item.transform.SetParent(parent, false);
        RectTransform rt = item.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        // Visual root with expansion
        GameObject visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(item.transform, false);
        RectTransform visRT = visualRoot.AddComponent<RectTransform>();
        float expansion = 0.1f;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = Vector2.zero;
        visRT.offsetMax = Vector2.zero;

        // Background with shader
        Image bg = visualRoot.AddComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.raycastTarget = false;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", 0.15f);
            glassMat.SetFloat("_EdgePadding", 0.1f);
            glassMat.SetFloat("_Aspect", w / h);
            glassMat.SetColor("_ColorA", new Color(col.r, col.g, col.b, 0.15f));
            glassMat.SetColor("_ColorB", new Color(col.r, col.g, col.b, 0.05f));
            glassMat.SetFloat("_GlassAlpha", 0.08f);
            bg.material = glassMat;
            bg.color = Color.white;
        }
        else
        {
            bg.color = new Color(col.r, col.g, col.b, 0.15f);
        }

        // Border with glow
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(visualRoot.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;

        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);
            glowMat.SetFloat("_Aspect", w / h);
            glowMat.SetFloat("_EdgePadding", 0.1f);
            Color borderGlowCol = Color.Lerp(col, Color.white, 0.75f);
            glowMat.SetColor("_GlowColor", borderGlowCol);
            glowMat.SetFloat("_BorderWidth", 0.005f);
            glowMat.SetFloat("_GlowWidth", 0.03f);
            glowMat.SetFloat("_GlowIntensity", 2.2f);
            glowMat.SetFloat("_CornerRadius", 0.15f);
            glowMat.SetFloat("_PulseEnabled", 0f);
            borderImg.material = glowMat;
            borderImg.sprite = GetPixelSprite();
        }

        // Icon with glow
        if (icon != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(visualRoot.transform, false);
            RectTransform iRT = iconObj.AddComponent<RectTransform>();
            iRT.anchorMin = new Vector2(0, 0.5f);
            iRT.anchorMax = new Vector2(0, 0.5f);
            iRT.pivot = new Vector2(0, 0.5f);
            iRT.anchoredPosition = new Vector2(35, 0);
            iRT.sizeDelta = new Vector2(60, 60);

            Image iImg = iconObj.AddComponent<Image>();
            iImg.sprite = icon;
            iImg.preserveAspect = true;
            iImg.raycastTarget = false;
            iImg.color = Color.Lerp(col, Color.white, 0.9f);

            // Glow effects
            Color glowCol = Color.Lerp(col, Color.white, 0.7f);
            glowCol.a = 0.4f;
            float s1 = 2f;

            iconObj.AddComponent<Shadow>().effectColor = glowCol;
            iconObj.GetComponent<Shadow>().effectDistance = new Vector2(s1, -s1);

            iconObj.AddComponent<Shadow>().effectColor = glowCol;
            iconObj.GetComponents<Shadow>()[1].effectDistance = new Vector2(-s1, s1);

            Color bloomCol = Color.Lerp(col, Color.white, 0.8f);
            bloomCol.a = 0.15f;
            float s2 = 5f;

            iconObj.AddComponent<Shadow>().effectColor = bloomCol;
            iconObj.GetComponents<Shadow>()[2].effectDistance = new Vector2(s2, -s2);

            iconObj.AddComponent<Shadow>().effectColor = bloomCol;
            iconObj.GetComponents<Shadow>()[3].effectDistance = new Vector2(-s2, s2);
        }

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(visualRoot.transform, false);
        RectTransform labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0.5f);
        labelRT.anchorMax = new Vector2(1, 0.5f);
        labelRT.pivot = new Vector2(0, 0.5f);
        labelRT.anchoredPosition = new Vector2(110, 10);
        labelRT.sizeDelta = new Vector2(-150, 30);

        TextMeshProUGUI labelTxt = labelObj.AddComponent<TextMeshProUGUI>();
        labelTxt.text = label;
        labelTxt.fontSize = 24;
        labelTxt.color = new Color(1, 1, 1, 0.7f);
        labelTxt.alignment = TextAlignmentOptions.Left;
        labelTxt.verticalAlignment = VerticalAlignmentOptions.Middle;
        labelTxt.raycastTarget = false;
        if (customFont) labelTxt.font = customFont;

        // Value
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(visualRoot.transform, false);
        RectTransform valueRT = valueObj.AddComponent<RectTransform>();
        valueRT.anchorMin = new Vector2(0, 0.5f);
        valueRT.anchorMax = new Vector2(1, 0.5f);
        valueRT.pivot = new Vector2(0, 0.5f);
        valueRT.anchoredPosition = new Vector2(110, -20);
        valueRT.sizeDelta = new Vector2(-180, 40);

        TextMeshProUGUI valueTxt = valueObj.AddComponent<TextMeshProUGUI>();
        valueTxt.text = value;
        valueTxt.fontSize = 36;
        valueTxt.color = Color.white;
        valueTxt.alignment = TextAlignmentOptions.Left;
        valueTxt.verticalAlignment = VerticalAlignmentOptions.Middle;
        valueTxt.fontStyle = FontStyles.Bold;
        valueTxt.raycastTarget = false;
        if (customFont) valueTxt.font = customFont;

        // Dropdown icon
        GameObject dropIcon = new GameObject("DropdownIcon");
        dropIcon.transform.SetParent(visualRoot.transform, false);
        RectTransform dropRT = dropIcon.AddComponent<RectTransform>();
        dropRT.anchorMin = new Vector2(1f, 0.5f);
        dropRT.anchorMax = new Vector2(1f, 0.5f);
        dropRT.pivot = new Vector2(1f, 0.5f);
        dropRT.anchoredPosition = new Vector2(-25, 0);
        dropRT.sizeDelta = new Vector2(28, 28);

        Image dropImg = dropIcon.AddComponent<Image>();
        dropImg.sprite = GetIcon("arrow_down");
        dropImg.preserveAspect = true;
        dropImg.raycastTarget = false;
        dropImg.color = Color.Lerp(col, Color.white, 0.85f);

        // Checkmark
        if (selected)
        {
            GameObject check = new GameObject("Check");
            check.transform.SetParent(visualRoot.transform, false);
            RectTransform cRT = check.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0, 1);
            cRT.anchorMax = new Vector2(0, 1);
            cRT.pivot = new Vector2(0, 1);
            cRT.anchoredPosition = new Vector2(15, -15);
            cRT.sizeDelta = new Vector2(36, 36);

            Image cImg = check.AddComponent<Image>();
            cImg.sprite = GetIcon("check");
            cImg.preserveAspect = true;
            cImg.raycastTarget = false;
            cImg.color = Color.Lerp(col, Color.white, 0.95f);

            // Glow for checkmark
            Color checkGlow = Color.Lerp(col, Color.white, 0.8f);
            checkGlow.a = 0.5f;

            check.AddComponent<Shadow>().effectColor = checkGlow;
            check.GetComponent<Shadow>().effectDistance = new Vector2(2, -2);

            check.AddComponent<Shadow>().effectColor = checkGlow;
            check.GetComponents<Shadow>()[1].effectDistance = new Vector2(-2, 2);
        }

        // Button component
        Button btn = item.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => Debug.Log("Clicked " + label));

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(col.r, col.g, col.b, 0.5f);
        cb.pressedColor = new Color(col.r, col.g, col.b, 0.7f);
        cb.fadeDuration = 0.1f;
        btn.colors = cb;
    }

    void CreateConnectButton(Transform parent, float x, float y, float w, float h)
    {
        GameObject btn = new GameObject("Btn_Connect");
        btn.transform.SetParent(parent, false);
        RectTransform rt = btn.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        // Visual root with expansion
        GameObject visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(btn.transform, false);
        RectTransform visRT = visualRoot.AddComponent<RectTransform>();
        float expansion = 0.08f;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = Vector2.zero;
        visRT.offsetMax = Vector2.zero;

        // Background with gradient shader
        Image bg = visualRoot.AddComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.raycastTarget = false;

        Color gradCol = Color.Lerp(themeColor, accentColor, 0.5f);

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", 0.15f);
            glassMat.SetFloat("_EdgePadding", 0.08f);
            glassMat.SetFloat("_Aspect", w / h);
            glassMat.SetColor("_ColorA", new Color(gradCol.r, gradCol.g, gradCol.b, 0.25f));
            glassMat.SetColor("_ColorB", new Color(gradCol.r, gradCol.g, gradCol.b, 0.1f));
            glassMat.SetFloat("_GlassAlpha", 0.15f);
            bg.material = glassMat;
            bg.color = Color.white;
        }
        else
        {
            bg.color = new Color(gradCol.r, gradCol.g, gradCol.b, 0.25f);
        }

        // Border with glow
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(visualRoot.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;

        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);
            glowMat.SetFloat("_Aspect", w / h);
            glowMat.SetFloat("_EdgePadding", 0.08f);
            Color borderGlowCol = Color.Lerp(gradCol, Color.white, 0.7f);
            glowMat.SetColor("_GlowColor", borderGlowCol);
            glowMat.SetFloat("_BorderWidth", 0.006f);
            glowMat.SetFloat("_GlowWidth", 0.04f);
            glowMat.SetFloat("_GlowIntensity", 3.0f);
            glowMat.SetFloat("_CornerRadius", 0.15f);
            glowMat.SetFloat("_PulseEnabled", 1f);
            glowMat.SetFloat("_PulseSpeed", 1.5f);
            glowMat.SetFloat("_PulseMin", 0.7f);
            glowMat.SetFloat("_PulseMax", 1.2f);
            borderImg.material = glowMat;
            borderImg.sprite = GetPixelSprite();
        }

        // Text
        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(visualRoot.transform, false);
        RectTransform txtRT = txtObj.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero;
        txtRT.offsetMax = Vector2.zero;

        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.text = "CONNECT";
        txt.fontSize = 48;
        txt.color = Color.white;
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        txt.verticalAlignment = VerticalAlignmentOptions.Middle;
        txt.raycastTarget = false;
        if (customFont) txt.font = customFont;

        // Text glow
        Color textGlow = Color.Lerp(gradCol, Color.white, 0.8f);
        textGlow.a = 0.3f;

        txtObj.AddComponent<Shadow>().effectColor = textGlow;
        txtObj.GetComponent<Shadow>().effectDistance = new Vector2(2, -2);

        txtObj.AddComponent<Shadow>().effectColor = textGlow;
        txtObj.GetComponents<Shadow>()[1].effectDistance = new Vector2(-2, 2);

        // Button component
        Button b = btn.AddComponent<Button>();
        b.targetGraphic = bg;
        b.onClick.AddListener(() => Debug.Log("Connect"));

        ColorBlock cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(gradCol.r, gradCol.g, gradCol.b, 0.6f);
        cb.pressedColor = new Color(gradCol.r, gradCol.g, gradCol.b, 0.8f);
        cb.fadeDuration = 0.15f;
        b.colors = cb;
    }

    // Icons (copy from original)
    Sprite GetIcon(string name)
    {
        if (_cachedIcons.ContainsKey(name)) return _cachedIcons[name];
        int w = 64; int h = 64;
        Texture2D tex = new Texture2D(w, h);
        Color[] fill = new Color[w*h];
        for(int k=0; k<fill.Length; k++) fill[k] = Color.clear;

        if (name == "arrow_down") DrawArrow(fill, w, h);
        else if (name == "monitor") DrawMonitor(fill, w, h);
        else if (name == "resolution") DrawGrid(fill, w, h);
        else if (name == "bitrate") DrawGauge(fill, w, h);
        else if (name == "fps") DrawFPS(fill, w, h);
        else if (name == "check") DrawCheck(fill, w, h);
        else if (name == "back") DrawBackArrow(fill, w, h);
        else if (name == "qr") DrawQR(fill, w, h);

        tex.SetPixels(fill);
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0,0,w,h), new Vector2(0.5f, 0.5f));
        _cachedIcons[name] = s;
        return s;
    }

    void DrawMonitor(Color[] p, int w, int h) {
        FillRect(p, w, 10, 16, 44, 28);
        FillRect(p, w, 13, 19, 38, 22);
        FillRect(p, w, 28, 8, 8, 10);
        FillRect(p, w, 20, 4, 24, 5);
    }
    void DrawGrid(Color[] p, int w, int h) {
        FillRect(p, w, 8, 12, 48, 40);
        for(int y=15; y<49; y++) for(int x=11; x<53; x++) p[y*w+x] = Color.clear;
        for(int x=20; x<50; x+=14) FillRect(p, w, x, 15, 2, 34);
        for(int y=23; y<50; y+=13) FillRect(p, w, 11, y, 42, 2);
    }
    void DrawGauge(Color[] p, int w, int h) {
        Vector2 center = new Vector2(32, 16);
        for(int y=0; y<h; y++) {
            for(int x=0; x<w; x++) {
                float d = Vector2.Distance(new Vector2(x,y), center);
                if (d > 18 && d < 22 && y > 16) p[y*w+x] = Color.white;
                if (d > 12 && d < 15 && y > 16 && x > 20 && x < 44) p[y*w+x] = Color.white;
            }
        }
        for(int i=0; i<15; i++) {
            int nx = 32 + i; int ny = 16 + i;
            if (nx < w && ny < h) FillRect(p, w, nx, ny, 2, 2);
        }
    }
    void DrawFPS(Color[] p, int w, int h) {
        DrawGauge(p, w, h);
        for(int i=0; i<5; i++) {
            float angle = (i * 30f - 60f) * Mathf.Deg2Rad;
            int tx = (int)(32 + Mathf.Cos(angle) * 20);
            int ty = (int)(16 + Mathf.Sin(angle) * 20);
            if (tx >= 0 && tx < w && ty >= 0 && ty < h) FillRect(p, w, tx, ty, 2, 4);
        }
    }
    void DrawArrow(Color[] p, int w, int h) {
        for(int y=20; y<44; y++) {
            int halfW = (y - 20) / 2;
            int startX = 32 - halfW;
            int endX = 32 + halfW;
            for(int x=startX; x<=endX && x<w; x++) {
                if (x >= 0) p[y*w+x] = Color.white;
            }
        }
    }
    void DrawCheck(Color[] p, int w, int h) {
        for(int i=0; i<12; i++) {
            int x = 18 + i/2; int y = 28 + i;
            if (x < w && y < h) FillRect(p, w, x, y, 3, 3);
        }
        for(int i=0; i<18; i++) {
            int x = 24 + i/2; int y = 40 - i;
            if (x < w && y >= 0 && y < h) FillRect(p, w, x, y, 3, 3);
        }
    }
    void DrawBackArrow(Color[] p, int w, int h) {
        FillRect(p, w, 16, 28, 36, 8);
        for(int i=0; i<10; i++) {
            FillRect(p, w, 16-i, 32-i, 4, 2);
            FillRect(p, w, 16-i, 32+i, 4, 2);
        }
    }
    void DrawQR(Color[] p, int w, int h) {
        int[][] corners = new int[][] {
            new int[] {8, 8}, new int[] {44, 8}, new int[] {8, 44}
        };
        foreach(int[] c in corners) {
            FillRect(p, w, c[0], c[1], 12, 12);
            FillRect(p, w, c[0]+3, c[1]+3, 6, 6);
            for(int y=c[1]+3; y<c[1]+9; y++)
                for(int x=c[0]+3; x<c[0]+9; x++)
                    p[y*w+x] = Color.clear;
        }
        for(int y=24; y<54; y+=4) {
            for(int x=24; x<54; x+=4) {
                if ((x+y)%7 < 3) FillRect(p, w, x, y, 3, 3);
            }
        }
    }
    void FillRect(Color[] p, int w, int x, int y, int rw, int rh) {
        for(int j=y; j<y+rh && j<w; j++) for(int i=x; i<x+rw && i<w; i++) {
            if (i>=0 && j>=0) p[j*w+i] = Color.white;
        }
    }

    Sprite GetPixelSprite()
    {
        if (_pixelSprite) return _pixelSprite;
        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }
}
