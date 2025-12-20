using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System;

public class VRRemoteMenu : MonoBehaviour
{
    public Color themeColor = new Color(0.0f, 0.9f, 1.0f);
    public Color accentColor = new Color(0.8f, 0.4f, 1.0f);
    public TMP_FontAsset customFont;

    private Sprite _pixelSprite;
    private Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>();

    // Event callbacks
    public event Action OnBackClicked;
    public event Action OnConnectClicked;
    public event Action OnQRClicked;

    private VRMainMenu _mainMenu;

    public void BuildUI(Transform parent, VRMainMenu mainMenu)
    {
        _mainMenu = mainMenu;
        OnBackClicked += () => _mainMenu?.ReturnToMainMenu();
        BuildUI(parent);
    }

    public void BuildUI(Transform parent)
    {

        // Canvas area: 1920 x 1080, content below status bar (~1000)
        float W = 1920f;
        float H = 1000f;
        float padX = 0f;
        float padY = 40f;

        float contentW = W - padX * 2;  // 1760

        // Điều chỉnh theo thiết kế - grid chiếm nhiều không gian hơn
        float headerH = 100f;
        float inputH = 130f;   // Host/Port row
        float gridH = 520f;    // 2 rows dropdown - tăng lên để match thiết kế
        float footerH = 100f;
        float gap = 30f;

        float y = H - padY;

        // Header
        y -= headerH;
        CreateHeader(parent, padX, y, contentW, headerH);

        // Input Row (Host / Port)
        y -= gap + inputH;
        CreateInputRow(parent, padX, y, contentW, inputH);

        // Grid 2x2 (Monitors, Resolution, Bitrate, FPS)
        y -= gap + gridH;
        CreateGrid(parent, padX, y, contentW, gridH);

        // Footer (Connect button)
        y -= gap + footerH;
        CreateFooter(parent, padX, y, contentW, footerH);
    }

    void CreateHeader(Transform parent, float x, float y, float w, float h)
    {
        var header = CreateContainer(parent, "Header", x, y, w, h);

        // Back button
        float backW = 240f;
        CreateButton(header.transform, 0, 0, backW, h, "Back", LoadIcon("back"), themeColor,
            () => OnBackClicked?.Invoke());

        // Title - font lớn hơn
        float titleX = backW + 35f;
        CreateLabel(header.transform, titleX, 0, w - titleX - 110f, h, "VR Remote Menu", 48, Color.white, true, TextAlignmentOptions.Left);

        // QR button
        float qrSize = 100f;
        CreateButton(header.transform, w - qrSize, (h - qrSize) / 2f, qrSize, qrSize, "", LoadIcon("qr"), accentColor,
            () => OnQRClicked?.Invoke());
    }

    void CreateInputRow(Transform parent, float x, float y, float w, float h)
    {
        var row = CreateContainer(parent, "InputRow", x, y, w, h);

        // Concept: Host ~60%, gap ~5%, Port ~35%
        float gapX = 40f;
        float hostW = (w - gapX) * 0.62f;
        float portW = (w - gapX) * 0.38f;

        CreateInputField(row.transform, 0, 0, hostW, h, "Host", "192.168.1.10", themeColor);
        CreateInputField(row.transform, hostW + gapX, 0, portW, h, "Port", "9000", accentColor);
    }

    void CreateGrid(Transform parent, float x, float y, float w, float h)
    {
        var grid = CreateContainer(parent, "Grid", x, y, w, h);

        // 2x2 grid with equal cells - tăng gap để match thiết kế
        float gapX = 40f;
        float gapY = 30f;
        float cellW = (w - gapX) / 2f;
        float cellH = (h - gapY) / 2f;

        // Row 1 (top) - Monitors và Resolution
        float row1Y = cellH + gapY;
        CreateDropdownBox(grid.transform, 0, row1Y, cellW, cellH,
            "Monitors", "Monitor 1", LoadIcon("monitor"), themeColor, false);
        CreateDropdownBox(grid.transform, cellW + gapX, row1Y, cellW, cellH,
            "Resolution", "1920 x 1080", LoadIcon("resolution"), accentColor, false);

        // Row 2 (bottom) - Bitrate và FPS
        CreateDropdownBox(grid.transform, 0, 0, cellW, cellH,
            "Bitrate", "10 Mbps", LoadIcon("bitrate"), themeColor, false);
        CreateDropdownBox(grid.transform, cellW + gapX, 0, cellW, cellH,
            "FPS", "60 FPS", LoadIcon("fps"), accentColor, false);
    }

    void CreateFooter(Transform parent, float x, float y, float w, float h)
    {
        var footer = CreateContainer(parent, "Footer", x, y, w, h);

        // Nút CONNECT - chiều rộng vừa phải, căn giữa
        float btnW = 650f;
        CreateConnectButton(footer.transform, (w - btnW) / 2f, 0, btnW, h);
    }

    // ==================== UI COMPONENTS ====================

    GameObject CreateContainer(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        return go;
    }

    void CreateButton(Transform parent, float x, float y, float w, float h,
        string text, Sprite icon, Color col, UnityEngine.Events.UnityAction onClick)
    {
        var btn = CreateContainer(parent, "Btn_" + text, x, y, w, h);

        // Hit Area với BoxCollider cho VR interaction
        var hitArea = new GameObject("HitArea");
        hitArea.transform.SetParent(btn.transform, false);
        var hitRT = hitArea.AddComponent<RectTransform>();
        hitRT.anchorMin = Vector2.zero;
        hitRT.anchorMax = Vector2.one;
        hitRT.offsetMin = hitRT.offsetMax = Vector2.zero;

        var hitImg = hitArea.AddComponent<Image>();
        hitImg.color = Color.clear;

        // BoxCollider cho VR raycast
        var collider = hitArea.AddComponent<BoxCollider>();
        collider.size = new Vector3(w, h, 0.1f);

        // Visual Root
        var visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(hitArea.transform, false);
        var visRT = visualRoot.AddComponent<RectTransform>();
        float expansion = -0.02f;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = visRT.offsetMax = Vector2.zero;

        // Background - match border radius and padding
        var bg = CreateBackground(visualRoot.transform, w, h, col, 0.28f, 0.12f, 0.12f);

        // Border với VRButtonRipple - viền dày gấp đôi cho button
        var borderImg = CreateBorderWithRipple(visualRoot.transform, w, h, col, false, 0.03f);

        // Content - điều chỉnh vị trí để bù cho expansion của visualRoot
        bool hasText = !string.IsNullOrEmpty(text);
        bool hasIcon = icon != null;

        // Offset để căn giữa trong HitArea (bù cho expansion)
        float offsetX = w * expansion;
        float offsetY = h * expansion;

        if (hasIcon && hasText)
        {
            float iconSize = 44f;
            float iconX = offsetX + 28f + iconSize / 2f;
            float centerY = h / 2f + offsetY;
            CreateIconWithGlow(visualRoot.transform, iconX, centerY, iconSize, icon, col);
            CreateLabel(visualRoot.transform, iconX + iconSize/2f + 18f, offsetY, w - iconX - iconSize - 24f + offsetX, h, text, 40, Color.white, true, TextAlignmentOptions.Left);
        }
        else if (hasIcon)
        {
            // Icon only - căn giữa chính xác
            float centerX = w / 2f + offsetX;
            float centerY = h / 2f + offsetY;
            CreateIconWithGlow(visualRoot.transform, centerX, centerY, 72, icon, col);
        }
        else if (hasText)
        {
            CreateLabel(visualRoot.transform, offsetX, offsetY, w, h, text, 40, Color.white, true);
        }

        // Button component
        var button = hitArea.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(onClick);

        var cb = button.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(col.r, col.g, col.b, 0.5f);
        cb.pressedColor = new Color(col.r, col.g, col.b, 0.7f);
        cb.fadeDuration = 0.1f;
        button.colors = cb;

        // VRButtonAnimation cho hiệu ứng pop
        var anim = hitArea.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visualRoot.transform;
        anim.popAmount = 0.0125f;
    }

    void CreateInputField(Transform parent, float x, float y, float w, float h,
        string label, string value, Color col)
    {
        var container = CreateContainer(parent, "Input_" + label, x, y, w, h);

        // Label above box
        float labelH = 32f;
        CreateLabel(container.transform, 10, h - labelH, w, labelH, label, 28, new Color(1,1,1,0.7f), false, TextAlignmentOptions.Left);

        // Input box
        float boxH = h - labelH - 8f;
        var box = CreateContainer(container.transform, "Box", 0, 0, w, boxH);

        // Hit Area với BoxCollider cho VR interaction (giống Button)
        var hitArea = new GameObject("HitArea");
        hitArea.transform.SetParent(box.transform, false);
        var hitRT = hitArea.AddComponent<RectTransform>();
        hitRT.anchorMin = Vector2.zero;
        hitRT.anchorMax = Vector2.one;
        hitRT.offsetMin = hitRT.offsetMax = Vector2.zero;

        var hitImg = hitArea.AddComponent<Image>();
        hitImg.color = Color.clear;

        // BoxCollider cho VR raycast
        var collider = hitArea.AddComponent<BoxCollider>();
        collider.size = new Vector3(w, boxH, 0.1f);

        // Visual Root (giống Button)
        var visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(hitArea.transform, false);
        var visRT = visualRoot.AddComponent<RectTransform>();
        float expansion = -0.02f;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = visRT.offsetMax = Vector2.zero;

        // Background - match border radius and padding
        var bg = CreateBackground(visualRoot.transform, w, boxH, col, 0.32f, 0.12f, 0.12f);

        // Border với VRButtonRipple (giống Button)
        CreateBorderWithRipple(visualRoot.transform, w, boxH, col, false, 0.008f);

        // Offset để căn giữa trong HitArea (bù cho expansion)
        float offsetX = w * expansion;
        float offsetY = boxH * expansion;

        // Value text - căn giữa theo chiều dọc
        CreateLabel(visualRoot.transform, 30 + offsetX, offsetY, w - 90, boxH, value, 42, Color.white, true, TextAlignmentOptions.Left);

        // Dropdown arrow
        CreateIcon(visualRoot.transform, w - 45f + offsetX, boxH/2f + offsetY, 26, CreateArrowSprite(), col);

        // Button component (giống Button nhưng không có VRButtonAnimation)
        var button = hitArea.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(() => Debug.Log("Input: " + label));

        var cb = button.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(col.r, col.g, col.b, 0.5f);
        cb.pressedColor = new Color(col.r, col.g, col.b, 0.7f);
        cb.fadeDuration = 0.1f;
        button.colors = cb;
    }

    void CreateDropdownBox(Transform parent, float x, float y, float w, float h,
        string label, string value, Sprite icon, Color col, bool selected)
    {
        var box = CreateContainer(parent, "Dropdown_" + label, x, y, w, h);

        // Hit Area với BoxCollider cho VR interaction (giống Button)
        var hitArea = new GameObject("HitArea");
        hitArea.transform.SetParent(box.transform, false);
        var hitRT = hitArea.AddComponent<RectTransform>();
        hitRT.anchorMin = Vector2.zero;
        hitRT.anchorMax = Vector2.one;
        hitRT.offsetMin = hitRT.offsetMax = Vector2.zero;

        var hitImg = hitArea.AddComponent<Image>();
        hitImg.color = Color.clear;

        // BoxCollider cho VR raycast
        var collider = hitArea.AddComponent<BoxCollider>();
        collider.size = new Vector3(w, h, 0.1f);

        // Visual Root (giống Button)
        var visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(hitArea.transform, false);
        var visRT = visualRoot.AddComponent<RectTransform>();
        float expansion = -0.02f;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = visRT.offsetMax = Vector2.zero;

        // Background - match border radius and padding
        var bg = CreateBackground(visualRoot.transform, w, h, col, 0.38f, 0.12f, 0.12f);

        // Border với VRButtonRipple (giống Button)
        CreateBorderWithRipple(visualRoot.transform, w, h, col, false, 0.008f);

        // Offset để căn giữa trong HitArea (bù cho expansion)
        float offsetX = w * expansion;
        float offsetY = h * expansion;

        // Layout theo thiết kế: Icon bên trái, text bên phải
        float paddingLeft = 30f;
        float iconSize = 80f;
        float iconCenterX = paddingLeft + iconSize / 2f + offsetX;
        float iconCenterY = h / 2f + offsetY;

        // Icon (căn giữa theo chiều dọc)
        if (icon != null)
        {
            CreateIcon(visualRoot.transform, iconCenterX, iconCenterY, iconSize, icon, col);
        }

        // Text area
        float textX = paddingLeft + iconSize + 25f + offsetX;
        float textW = w - textX - 60f + offsetX;

        // Label và Value căn giữa theo chiều dọc
        float labelH = 36f;
        float valueH = 50f;
        float textGap = 8f;
        float totalTextH = labelH + textGap + valueH;
        float textStartY = (h - totalTextH) / 2f + offsetY;

        // Label (phía trên)
        CreateLabel(visualRoot.transform, textX, textStartY + valueH + textGap, textW, labelH, label, 30,
            new Color(1,1,1,0.75f), false, TextAlignmentOptions.Left);

        // Value (phía dưới)
        CreateLabel(visualRoot.transform, textX, textStartY, textW, valueH, value, 42,
            Color.white, true, TextAlignmentOptions.Left);

        // Dropdown arrow (góc phải, căn giữa)
        CreateIcon(visualRoot.transform, w - 40f + offsetX, h / 2f + offsetY, 28, CreateArrowSprite(), new Color(1,1,1,0.85f));

        // Checkmark (top-left corner)
        if (selected)
        {
            CreateIcon(visualRoot.transform, 22f + offsetX, h - 22f + offsetY, 24, CreateCheckSprite(), col);
        }

        // Button component (giống Button nhưng không có VRButtonAnimation)
        var button = hitArea.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(() => Debug.Log("Dropdown: " + label));

        var cb = button.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(col.r, col.g, col.b, 0.5f);
        cb.pressedColor = new Color(col.r, col.g, col.b, 0.7f);
        cb.fadeDuration = 0.1f;
        button.colors = cb;
    }

    void CreateConnectButton(Transform parent, float x, float y, float w, float h)
    {
        var btn = CreateContainer(parent, "Btn_Connect", x, y, w, h);

        // Gradient từ themeColor (xanh) sang accentColor (tím)
        Color gradCol = Color.Lerp(themeColor, accentColor, 0.45f);

        // Background - đậm hơn để nổi bật
        var bg = CreateBackground(btn.transform, w, h, gradCol, 0.50f);

        // Border với pulse effect mạnh hơn
        CreateBorder(btn.transform, w, h, gradCol, true);

        // Text với glow mạnh - font lớn hơn
        var txt = CreateLabel(btn.transform, 0, 0, w, h, "CONNECT", 58, Color.white, true);
        AddGlow(txt, gradCol);

        AddButton(btn, bg, gradCol, () => OnConnectClicked?.Invoke());
    }

    // ==================== PRIMITIVES ====================

        Image CreateBackground(Transform parent, float w, float h, Color col, float alpha, float radius = 0.12f, float padding = 0.12f)
    {
        var go = new GameObject("Background");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();

        float exp = 0.12f;
        rt.anchorMin = new Vector2(-exp, -exp);
        rt.anchorMax = new Vector2(1+exp, 1+exp);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = false;

        var shader = Shader.Find("Custom/GlassGradientBackground");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetFloat("_CornerRadius", radius);
            mat.SetFloat("_EdgePadding", padding);
            mat.SetFloat("_Aspect", w / h);
            // Gradient đậm hơn để rõ ràng
            mat.SetColor("_ColorA", new Color(col.r, col.g, col.b, alpha * 1.1f));
            mat.SetColor("_ColorB", new Color(col.r, col.g, col.b, alpha * 0.5f));
            mat.SetFloat("_GlassAlpha", alpha * 0.9f);
            img.material = mat;
            img.color = Color.white;
        }
        else
        {
            img.color = new Color(col.r, col.g, col.b, alpha);
        }

        return img;
    }

    void CreateBorder(Transform parent, float w, float h, Color col, bool pulse = false)
    {
        var go = new GameObject("Border");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();

        float exp = 0.12f;
        rt.anchorMin = new Vector2(-exp, -exp);
        rt.anchorMax = new Vector2(1+exp, 1+exp);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = false;

        var shader = Shader.Find("Custom/GlowingElementBorder");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetFloat("_Aspect", w / h);
            mat.SetFloat("_EdgePadding", 0.12f);
            mat.SetColor("_GlowColor", Color.Lerp(col, Color.white, 0.85f));
            mat.SetFloat("_BorderWidth", 0.008f);
            mat.SetFloat("_GlowWidth", 0.035f);
            mat.SetFloat("_GlowIntensity", pulse ? 5.0f : 3.5f);
            mat.SetFloat("_CornerRadius", 0.12f);
            mat.SetFloat("_PulseEnabled", pulse ? 1f : 0f);
            if (pulse)
            {
                mat.SetFloat("_PulseSpeed", 1.8f);
                mat.SetFloat("_PulseMin", 0.8f);
                mat.SetFloat("_PulseMax", 1.4f);
            }
            img.material = mat;
        }
    }

    Image CreateBorderWithRipple(Transform parent, float w, float h, Color col, bool pulse = false, float borderWidth = 0.005f)
    {
        var go = new GameObject("Border");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();

        float exp = 0.12f;
        rt.anchorMin = new Vector2(-exp, -exp);
        rt.anchorMax = new Vector2(1+exp, 1+exp);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = false;

        var shader = Shader.Find("Custom/GlowingElementBorder");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetFloat("_Aspect", w / h);
            mat.SetFloat("_EdgePadding", 0.12f);

            Color borderGlowCol = Color.Lerp(col, Color.white, 0.75f);
            mat.SetColor("_GlowColor", borderGlowCol);

            mat.SetFloat("_BorderWidth", borderWidth);
            mat.SetFloat("_GlowWidth", 0.03f);
            mat.SetFloat("_GlowIntensity", pulse ? 5.0f : 2.5f);
            mat.SetFloat("_CornerRadius", 0.12f);
            mat.SetFloat("_PulseEnabled", pulse ? 1f : 0f);
            if (pulse)
            {
                mat.SetFloat("_PulseSpeed", 1.8f);
                mat.SetFloat("_PulseMin", 0.8f);
                mat.SetFloat("_PulseMax", 1.4f);
            }
            img.material = mat;

            // Thêm VRButtonRipple cho hiệu ứng ripple
            go.AddComponent<VRButtonRipple>().Initialize(mat, img);
        }

        return img;
    }

    void CreateIconWithGlow(Transform parent, float x, float y, float size, Sprite sprite, Color col)
    {
        var go = new GameObject("Icon");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(size, size);

        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.color = Color.Lerp(col, Color.white, 0.9f);

        // Glow Layer 1 - Sharp inner halo
        Color glowCol = Color.Lerp(col, Color.white, 0.7f);
        glowCol.a = 0.4f;
        float s1 = 2f;

        var shadow1 = go.AddComponent<Shadow>();
        shadow1.effectColor = glowCol;
        shadow1.effectDistance = new Vector2(s1, -s1);

        var shadow2 = go.AddComponent<Shadow>();
        shadow2.effectColor = glowCol;
        shadow2.effectDistance = new Vector2(-s1, s1);

        // Glow Layer 2 - Soft outer bloom
        Color bloomCol = Color.Lerp(col, Color.white, 0.8f);
        bloomCol.a = 0.15f;
        float s2 = 5f;

        var shadow3 = go.AddComponent<Shadow>();
        shadow3.effectColor = bloomCol;
        shadow3.effectDistance = new Vector2(s2, -s2);

        var shadow4 = go.AddComponent<Shadow>();
        shadow4.effectColor = bloomCol;
        shadow4.effectDistance = new Vector2(-s2, s2);
    }

    void CreateIcon(Transform parent, float x, float y, float size, Sprite sprite, Color col)
    {
        var go = new GameObject("Icon");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(size, size);

        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        // Icon màu sáng hơn để dễ nhìn
        img.color = Color.Lerp(col, Color.white, 0.95f);

        AddGlow(go, col);
    }

    GameObject CreateLabel(Transform parent, float x, float y, float w, float h,
        string text, int fontSize, Color col, bool bold,
        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject("Label_" + text);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.color = col;
        txt.alignment = align;
        txt.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false;
        txt.overflowMode = TextOverflowModes.Ellipsis;
        if (customFont) txt.font = customFont;

        return go;
    }

    void AddGlow(GameObject go, Color col)
    {
        // Glow màu sáng hơn và đậm hơn
        Color glow = Color.Lerp(col, Color.white, 0.8f);
        glow.a = 0.55f;

        var s1 = go.AddComponent<Shadow>();
        s1.effectColor = glow;
        s1.effectDistance = new Vector2(3, -3);

        var s2 = go.AddComponent<Shadow>();
        s2.effectColor = glow;
        s2.effectDistance = new Vector2(-3, 3);

        // Thêm layer glow thứ 3 để rõ hơn
        var s3 = go.AddComponent<Shadow>();
        s3.effectColor = new Color(glow.r, glow.g, glow.b, 0.3f);
        s3.effectDistance = new Vector2(0, 0);
    }

    void AddButton(GameObject go, Image targetGraphic, Color col, UnityEngine.Events.UnityAction onClick)
    {
        // Hit area
        var hit = new GameObject("HitArea");
        hit.transform.SetParent(go.transform, false);
        var hitRT = hit.AddComponent<RectTransform>();
        hitRT.anchorMin = Vector2.zero;
        hitRT.anchorMax = Vector2.one;
        hitRT.offsetMin = hitRT.offsetMax = Vector2.zero;

        var hitImg = hit.AddComponent<Image>();
        hitImg.color = Color.clear;

        var btn = hit.AddComponent<Button>();
        btn.targetGraphic = hitImg;
        btn.onClick.AddListener(onClick);

        var cb = btn.colors;
        cb.normalColor = Color.clear;
        cb.highlightedColor = new Color(col.r, col.g, col.b, 0.2f);
        cb.pressedColor = new Color(col.r, col.g, col.b, 0.35f);
        cb.fadeDuration = 0.1f;
        btn.colors = cb;
    }

    // ==================== SPRITES ====================

    Sprite LoadIcon(string name)
    {
        if (_iconCache.ContainsKey(name)) return _iconCache[name];

        var sprite = Resources.Load<Sprite>($"RemoteMenu/icon_{name}");
        if (sprite != null)
        {
            _iconCache[name] = sprite;
            return sprite;
        }

        return null;
    }

    Sprite CreateArrowSprite()
    {
        if (_iconCache.ContainsKey("arrow")) return _iconCache["arrow"];

        var tex = new Texture2D(32, 32);
        var pixels = new Color[32 * 32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        for (int y = 8; y < 24; y++)
        {
            int half = (y - 8) / 2;
            for (int x = 16 - half; x <= 16 + half; x++)
                if (x >= 0 && x < 32) pixels[y * 32 + x] = Color.white;
        }

        tex.SetPixels(pixels);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
        _iconCache["arrow"] = sprite;
        return sprite;
    }

    Sprite CreateCheckSprite()
    {
        if (_iconCache.ContainsKey("check")) return _iconCache["check"];

        var tex = new Texture2D(32, 32);
        var pixels = new Color[32 * 32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        // Simple checkmark
        for (int i = 0; i < 8; i++)
        {
            int px = 8 + i; int py = 16 + i;
            if (px < 32 && py < 32) pixels[py * 32 + px] = Color.white;
            pixels[(py-1) * 32 + px] = Color.white;
        }
        for (int i = 0; i < 12; i++)
        {
            int px = 16 + i; int py = 24 - i;
            if (px < 32 && py >= 0) pixels[py * 32 + px] = Color.white;
            if (py > 0) pixels[(py-1) * 32 + px] = Color.white;
        }

        tex.SetPixels(pixels);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
        _iconCache["check"] = sprite;
        return sprite;
    }

    Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        var tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }
}
