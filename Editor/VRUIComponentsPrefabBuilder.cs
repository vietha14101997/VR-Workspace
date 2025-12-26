#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;

/// <summary>
/// Editor tool to create VR UI Component prefabs.
/// Based on designs from the logs folder images.
/// </summary>
public static class VRUIComponentsPrefabBuilder
{
    private static readonly string PrefabDir = "Assets/VR-Workspace/Prefabs/UIComponents";
    private static readonly string MaterialDir = "Assets/VR-Workspace/Materials/UIComponents";
    private static readonly Color CyanColor = new Color(0.0f, 0.9f, 1.0f, 1.0f);
    private static readonly Color PurpleColor = new Color(0.8f, 0.4f, 1.0f, 1.0f);

    private static Sprite _pixelSprite;

    // Cached shaders loaded via AssetDatabase
    private static Shader _glassGradientShader;
    private static Shader _glowingGlassBorderShader;
    private static Shader _glowingElementBorderShader;

    // ==================== MENU ITEMS ====================

    [MenuItem("Tools/VR UI Components/Create All Prefabs")]
    public static void CreateAllPrefabs()
    {
        LoadShaders();
        EnsureDirectoryExists();
        CreateVRButton_Text_Icon();
        CreateVRButton_Text_Icon_Mini();
        CreateVRButton_Icon_Only();
        CreateVRButton_Text_Only();
        CreateVRDropdownBox();
        CreateVREditText();
        AssetDatabase.Refresh();
        Debug.Log("[VRUIComponentsPrefabBuilder] All prefabs created successfully!");
    }

    /// <summary>
    /// Replace runtime materials with saved asset materials (for prefab building)
    /// </summary>
    private static void ReplaceRuntimeMaterialsWithAssets(GameObject root, float w, float h)
    {
        // Find GlassBackground and replace its material
        var glassBg = root.transform.Find("GlassBackground");
        if (glassBg != null)
        {
            var img = glassBg.GetComponent<Image>();
            if (img != null && _glassGradientShader != null)
            {
                var mat = CreateAndSaveMaterial(_glassGradientShader, "FrameGlassBackground");
                if (mat != null)
                {
                    // Copy settings from VRMenuFrame.CreateGlassPanel
                    float p = 0.04f;
                    mat.SetFloat("_CornerRadius", 0.12f);
                    mat.SetFloat("_EdgePadding", p);
                    mat.SetFloat("_Aspect", w / h);

                    Color cyanGlass = new Color(0.35f, 0.9f, 1f, 0.15f);
                    Color purpleGlass = new Color(0.75f, 0.45f, 1f, 0.22f);
                    mat.SetColor("_ColorA", cyanGlass);
                    mat.SetColor("_ColorB", purpleGlass);
                    mat.SetFloat("_GradientOffset", 0f);
                    mat.SetFloat("_GradientAngle", -10f);
                    mat.SetFloat("_CyanRatio", 0.7f);
                    mat.SetFloat("_GlassAlpha", 0.08f);
                    mat.SetFloat("_FresnelPower", 2.2f);
                    mat.SetFloat("_FresnelStrength", 0.12f);

                    EditorUtility.SetDirty(mat);
                    img.material = mat;
                    img.color = Color.white;
                }
            }
        }

        // Find GlowingBorder and replace its material
        var glowingBorder = glassBg?.Find("GlowingBorder");
        if (glowingBorder != null)
        {
            var img = glowingBorder.GetComponent<Image>();
            if (img != null && _glowingGlassBorderShader != null)
            {
                var mat = CreateAndSaveMaterial(_glowingGlassBorderShader, "FrameGlowingBorder");
                if (mat != null)
                {
                    mat.SetFloat("_BorderWidth", 0.02f);
                    mat.SetFloat("_CornerRadius", 0.12f);
                    mat.SetFloat("_EdgePadding", 0.04f);
                    mat.SetFloat("_Aspect", w / h);

                    mat.SetFloat("_Layer1Width", 0.008f);
                    mat.SetFloat("_Layer1Alpha", 1.5f);
                    mat.SetFloat("_Layer2Width", 0.018f);
                    mat.SetFloat("_Layer2Alpha", 1.0f);
                    mat.SetFloat("_Layer3Width", 0.04f);
                    mat.SetFloat("_Layer3Alpha", 0.6f);
                    mat.SetFloat("_Layer4Width", 0.08f);
                    mat.SetFloat("_Layer4Alpha", 0.3f);

                    Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
                    Color purpleColor = new Color(1f, 0.4f, 1f, 1f);
                    mat.SetColor("_ColorA", cyanColor);
                    mat.SetColor("_ColorB", purpleColor);
                    mat.SetFloat("_GradientMode", 2f);
                    mat.SetFloat("_GradientAngle", -10f);
                    mat.SetFloat("_GlassAlpha", 0.02f);
                    mat.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
                    mat.SetFloat("_ShimmerSpeed", 0.4f);
                    mat.SetFloat("_ShimmerIntensity", 0.15f);

                    EditorUtility.SetDirty(mat);
                    img.material = mat;
                    img.color = Color.white;
                }
            }
        }

        // Find StatusBar > LeftGroup > RecenterBtn and replace materials
        var statusBar = root.transform.Find("StatusBar");
        if (statusBar != null)
        {
            var recenterBtn = statusBar.Find("LeftGroup/RecenterBtn");
            if (recenterBtn != null)
            {
                // Background material
                var visuals = recenterBtn.Find("Visuals");
                if (visuals != null)
                {
                    var bgImg = visuals.GetComponent<Image>();
                    if (bgImg != null && _glassGradientShader != null)
                    {
                        var mat = CreateAndSaveMaterial(_glassGradientShader, "RecenterButtonBg");
                        if (mat != null)
                        {
                            mat.SetFloat("_CornerRadius", 0.15f);
                            mat.SetFloat("_EdgePadding", 0.12f);
                            mat.SetFloat("_Aspect", 1.0f);
                            mat.SetColor("_ColorA", new Color(0f, 1f, 1f, 0.12f));
                            mat.SetColor("_ColorB", new Color(0f, 1f, 1f, 0.04f));
                            mat.SetFloat("_GlassAlpha", 0.075f);
                            EditorUtility.SetDirty(mat);
                            bgImg.material = mat;
                            bgImg.color = Color.white;
                        }
                    }

                    // Border material
                    var border = visuals.Find("Border");
                    if (border != null)
                    {
                        var borderImg = border.GetComponent<Image>();
                        if (borderImg != null && _glowingElementBorderShader != null)
                        {
                            var glowMat = CreateAndSaveMaterial(_glowingElementBorderShader, "RecenterButtonBorder");
                            if (glowMat != null)
                            {
                                glowMat.SetFloat("_Aspect", 1.0f);
                                glowMat.SetFloat("_EdgePadding", 0.12f);
                                glowMat.SetColor("_GlowColor", Color.Lerp(CyanColor, Color.white, 0.75f));
                                glowMat.SetFloat("_BorderWidth", 0.05f);
                                glowMat.SetFloat("_GlowWidth", 0.04f);
                                glowMat.SetFloat("_GlowIntensity", 2.5f);
                                glowMat.SetFloat("_CornerRadius", 0.15f);
                                glowMat.SetFloat("_PulseEnabled", 0f);
                                EditorUtility.SetDirty(glowMat);
                                borderImg.material = glowMat;

                                // Update VRButtonRipple reference
                                var ripple = border.GetComponent<VRButtonRipple>();
                                if (ripple != null)
                                {
                                    ripple.Initialize(glowMat, borderImg);
                                }
                            }
                        }
                    }
                }
            }
        }

        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/VR UI Components/VRButton_Text_Icon")]
    public static void CreateVRButton_Text_Icon()
    {
        LoadShaders();
        EnsureDirectoryExists();

        // Button size matching VRMainMenu grid buttons
        float width = 450f;
        float height = 340f;
        Color btnColor = CyanColor;

        var go = new GameObject("VRButton_Text_Icon");
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        // Hit Area
        var hitArea = CreateHitArea(go.transform, width, height);

        // Visual Root with expansion
        var visualRoot = CreateVisualRoot(hitArea.transform, 0.12f);

        // Background
        CreateGlassBackground(visualRoot.transform, width, height, btnColor, 0.12f, 0.12f);

        // Border with ripple
        CreateGlowingBorder(visualRoot.transform, width, height, btnColor, 0.12f, 0.005f);

        // Content container
        var content = CreateContainer(visualRoot.transform, "Content");
        var contentRT = content.GetComponent<RectTransform>();
        contentRT.anchorMin = Vector2.zero;
        contentRT.anchorMax = Vector2.one;
        contentRT.sizeDelta = Vector2.zero;

        // Icon (centered above text)
        var iconObj = CreateIconPlaceholder(content.transform, "Icon", 80f);
        var iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.35f, 0.42f);
        iconRT.anchorMax = new Vector2(0.65f, 0.72f);
        iconRT.offsetMin = Vector2.zero;
        iconRT.offsetMax = Vector2.zero;
        AddIconGlow(iconObj, btnColor);

        // Text (centered below icon)
        var textObj = CreateTextLabel(content.transform, "Button Text", 42, Color.white, true);
        var textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0f, 0.18f);
        textRT.anchorMax = new Vector2(1f, 0.42f);
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        // Button component
        var btn = AddButtonComponent(hitArea, visualRoot.GetComponentInChildren<Image>(), btnColor);

        // Animation
        AddButtonAnimation(hitArea, visualRoot.transform, 0.05f);

        SavePrefab(go, "VRButton_Text_Icon");
    }

    [MenuItem("Tools/VR UI Components/VRButton_Text_Icon_Mini")]
    public static void CreateVRButton_Text_Icon_Mini()
    {
        LoadShaders();
        EnsureDirectoryExists();

        // Mini button size (like Back button)
        float width = 240f;
        float height = 100f;
        Color btnColor = CyanColor;

        var go = new GameObject("VRButton_Text_Icon_Mini");
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        // Hit Area
        var hitArea = CreateHitArea(go.transform, width, height);

        // Visual Root
        var visualRoot = CreateVisualRoot(hitArea.transform, -0.02f);

        // Background
        CreateGlassBackground(visualRoot.transform, width, height, btnColor, 0.28f, 0.12f);

        // Border with ripple
        CreateGlowingBorder(visualRoot.transform, width, height, btnColor, 0.12f, 0.03f);

        // Content layout - icon left, text right
        float expansion = -0.02f;
        float offsetX = width * expansion;
        float offsetY = height * expansion;

        // Icon (left side)
        float iconSize = 44f;
        float iconX = offsetX + 28f + iconSize / 2f;
        float centerY = height / 2f + offsetY;
        var iconObj = CreateIconPlaceholder(visualRoot.transform, "Icon", iconSize);
        var iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = iconRT.anchorMax = Vector2.zero;
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = new Vector2(iconX, centerY);
        iconRT.sizeDelta = new Vector2(iconSize, iconSize);
        AddIconGlow(iconObj, btnColor);

        // Text (right of icon)
        var textObj = CreateTextLabel(visualRoot.transform, "Back", 40, Color.white, true, TextAlignmentOptions.Left);
        var textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = textRT.anchorMax = Vector2.zero;
        textRT.pivot = Vector2.zero;
        textRT.anchoredPosition = new Vector2(iconX + iconSize/2f + 18f, offsetY);
        textRT.sizeDelta = new Vector2(width - iconX - iconSize - 24f + offsetX, height);

        // Button component
        var btn = AddButtonComponent(hitArea, visualRoot.GetComponentInChildren<Image>(), btnColor);

        // Animation
        AddButtonAnimation(hitArea, visualRoot.transform, 0.0125f);

        SavePrefab(go, "VRButton_Text_Icon_Mini");
    }

    [MenuItem("Tools/VR UI Components/VRButton_Icon_Only")]
    public static void CreateVRButton_Icon_Only()
    {
        LoadShaders();
        EnsureDirectoryExists();

        // Square icon button (like QR button)
        float size = 100f;
        Color btnColor = PurpleColor;

        var go = new GameObject("VRButton_Icon_Only");
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);

        // Hit Area
        var hitArea = CreateHitArea(go.transform, size, size);

        // Visual Root
        var visualRoot = CreateVisualRoot(hitArea.transform, -0.02f);

        // Background
        CreateGlassBackground(visualRoot.transform, size, size, btnColor, 0.28f, 0.12f);

        // Border with ripple
        CreateGlowingBorder(visualRoot.transform, size, size, btnColor, 0.15f, 0.03f);

        // Icon (centered)
        float expansion = -0.02f;
        float offsetX = size * expansion;
        float offsetY = size * expansion;
        float centerX = size / 2f + offsetX;
        float centerY = size / 2f + offsetY;

        var iconObj = CreateIconPlaceholder(visualRoot.transform, "Icon", 72f);
        var iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = iconRT.anchorMax = Vector2.zero;
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = new Vector2(centerX, centerY);
        iconRT.sizeDelta = new Vector2(72f, 72f);
        AddIconGlow(iconObj, btnColor);

        // Button component
        var btn = AddButtonComponent(hitArea, visualRoot.GetComponentInChildren<Image>(), btnColor);

        // Animation
        AddButtonAnimation(hitArea, visualRoot.transform, 0.0125f);

        SavePrefab(go, "VRButton_Icon_Only");
    }

    [MenuItem("Tools/VR UI Components/VRButton_Text_Only")]
    public static void CreateVRButton_Text_Only()
    {
        LoadShaders();
        EnsureDirectoryExists();

        // Wide text button (like CONNECT button)
        float width = 650f;
        float height = 100f;
        Color btnColor = Color.Lerp(CyanColor, PurpleColor, 0.45f);

        var go = new GameObject("VRButton_Text_Only");
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        // Hit Area
        var hitArea = CreateHitArea(go.transform, width, height);

        // Visual Root
        var visualRoot = CreateVisualRoot(hitArea.transform, 0.12f);

        // Background (more opaque for action button)
        CreateGlassBackground(visualRoot.transform, width, height, btnColor, 0.50f, 0.12f);

        // Border with pulse effect
        CreateGlowingBorder(visualRoot.transform, width, height, btnColor, 0.12f, 0.008f, true);

        // Text (centered)
        var textObj = CreateTextLabel(visualRoot.transform, "CONNECT", 58, Color.white, true);
        var textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;
        AddTextGlow(textObj, btnColor);

        // Button component
        var btn = AddButtonComponent(hitArea, visualRoot.GetComponentInChildren<Image>(), btnColor);

        // Animation
        AddButtonAnimation(hitArea, visualRoot.transform, 0.025f);

        SavePrefab(go, "VRButton_Text_Only");
    }

    [MenuItem("Tools/VR UI Components/VRDropdownBox")]
    public static void CreateVRDropdownBox()
    {
        LoadShaders();
        EnsureDirectoryExists();

        // Dropdown box size (like Monitor selector)
        float width = 850f;
        float height = 245f;
        Color btnColor = CyanColor;

        var go = new GameObject("VRDropdownBox");
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        // Hit Area
        var hitArea = CreateHitArea(go.transform, width, height);

        // Visual Root
        var visualRoot = CreateVisualRoot(hitArea.transform, -0.02f);

        // Background
        CreateGlassBackground(visualRoot.transform, width, height, btnColor, 0.38f, 0.12f);

        // Border with ripple
        CreateGlowingBorder(visualRoot.transform, width, height, btnColor, 0.12f, 0.008f);

        // Content layout
        float expansion = -0.02f;
        float offsetX = width * expansion;
        float offsetY = height * expansion;

        // Icon (left side, centered vertically)
        float paddingLeft = 30f;
        float iconSize = 80f;
        float iconCenterX = paddingLeft + iconSize / 2f + offsetX;
        float iconCenterY = height / 2f + offsetY;

        var iconObj = CreateIconPlaceholder(visualRoot.transform, "Icon", iconSize);
        var iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = iconRT.anchorMax = Vector2.zero;
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = new Vector2(iconCenterX, iconCenterY);
        iconRT.sizeDelta = new Vector2(iconSize, iconSize);

        // Text area
        float textX = paddingLeft + iconSize + 25f + offsetX;
        float textW = width - textX - 60f + offsetX;

        // Label and Value vertically centered
        float labelH = 36f;
        float valueH = 50f;
        float textGap = 8f;
        float totalTextH = labelH + textGap + valueH;
        float textStartY = (height - totalTextH) / 2f + offsetY;

        // Label (top)
        var labelObj = CreateTextLabel(visualRoot.transform, "Monitors", 30, new Color(1,1,1,0.75f), false, TextAlignmentOptions.Left);
        var labelRT = labelObj.GetComponent<RectTransform>();
        labelRT.anchorMin = labelRT.anchorMax = Vector2.zero;
        labelRT.pivot = Vector2.zero;
        labelRT.anchoredPosition = new Vector2(textX, textStartY + valueH + textGap);
        labelRT.sizeDelta = new Vector2(textW, labelH);

        // Value (bottom)
        var valueObj = CreateTextLabel(visualRoot.transform, "Monitor 1", 42, Color.white, true, TextAlignmentOptions.Left);
        var valueRT = valueObj.GetComponent<RectTransform>();
        valueRT.anchorMin = valueRT.anchorMax = Vector2.zero;
        valueRT.pivot = Vector2.zero;
        valueRT.anchoredPosition = new Vector2(textX, textStartY);
        valueRT.sizeDelta = new Vector2(textW, valueH);

        // Dropdown arrow (right side, centered)
        var arrowObj = CreateArrowIcon(visualRoot.transform);
        var arrowRT = arrowObj.GetComponent<RectTransform>();
        arrowRT.anchorMin = arrowRT.anchorMax = Vector2.zero;
        arrowRT.pivot = new Vector2(0.5f, 0.5f);
        arrowRT.anchoredPosition = new Vector2(width - 40f + offsetX, height / 2f + offsetY);
        arrowRT.sizeDelta = new Vector2(28f, 28f);

        // Button component
        var btn = AddButtonComponent(hitArea, visualRoot.GetComponentInChildren<Image>(), btnColor);

        SavePrefab(go, "VRDropdownBox");
    }

    [MenuItem("Tools/VR UI Components/VREditText")]
    public static void CreateVREditText()
    {
        LoadShaders();
        EnsureDirectoryExists();

        // Edit text field size (like Port input)
        float width = 700f;
        float height = 130f;
        Color fieldColor = PurpleColor;

        var go = new GameObject("VREditText");
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        // Label above box
        float labelH = 32f;
        var labelObj = CreateTextLabel(go.transform, "Port", 28, new Color(1,1,1,0.7f), false, TextAlignmentOptions.Left);
        var labelRT = labelObj.GetComponent<RectTransform>();
        labelRT.anchorMin = labelRT.anchorMax = Vector2.zero;
        labelRT.pivot = Vector2.zero;
        labelRT.anchoredPosition = new Vector2(10f, height - labelH);
        labelRT.sizeDelta = new Vector2(width, labelH);

        // Input box
        float boxH = height - labelH - 8f;
        var boxContainer = CreateContainer(go.transform, "Box");
        var boxRT = boxContainer.GetComponent<RectTransform>();
        boxRT.anchorMin = boxRT.anchorMax = Vector2.zero;
        boxRT.pivot = Vector2.zero;
        boxRT.anchoredPosition = Vector2.zero;
        boxRT.sizeDelta = new Vector2(width, boxH);

        // Hit Area
        var hitArea = CreateHitArea(boxContainer.transform, width, boxH);

        // Visual Root
        var visualRoot = CreateVisualRoot(hitArea.transform, -0.02f);

        // Background
        CreateGlassBackground(visualRoot.transform, width, boxH, fieldColor, 0.32f, 0.12f);

        // Border with ripple
        CreateGlowingBorder(visualRoot.transform, width, boxH, fieldColor, 0.12f, 0.008f);

        // Content
        float expansion = -0.02f;
        float offsetX = width * expansion;
        float offsetY = boxH * expansion;

        // Value text (left aligned)
        var valueObj = CreateTextLabel(visualRoot.transform, "9000", 42, Color.white, true, TextAlignmentOptions.Left);
        var valueRT = valueObj.GetComponent<RectTransform>();
        valueRT.anchorMin = valueRT.anchorMax = Vector2.zero;
        valueRT.pivot = Vector2.zero;
        valueRT.anchoredPosition = new Vector2(30f + offsetX, offsetY);
        valueRT.sizeDelta = new Vector2(width - 90f, boxH);

        // Dropdown arrow
        var arrowObj = CreateArrowIcon(visualRoot.transform);
        var arrowRT = arrowObj.GetComponent<RectTransform>();
        arrowRT.anchorMin = arrowRT.anchorMax = Vector2.zero;
        arrowRT.pivot = new Vector2(0.5f, 0.5f);
        arrowRT.anchoredPosition = new Vector2(width - 45f + offsetX, boxH/2f + offsetY);
        arrowRT.sizeDelta = new Vector2(26f, 26f);

        // Button component
        var btn = AddButtonComponent(hitArea, visualRoot.GetComponentInChildren<Image>(), fieldColor);

        SavePrefab(go, "VREditText");
    }

    // ==================== HELPER METHODS ====================

    private static void LoadShaders()
    {
        if (_glassGradientShader == null)
            _glassGradientShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/VR-Workspace/Shaders/GlassGradientBackground.shader");
        if (_glowingGlassBorderShader == null)
            _glowingGlassBorderShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/VR-Workspace/Shaders/GlowingGlassBorder.shader");
        if (_glowingElementBorderShader == null)
            _glowingElementBorderShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/VR-Workspace/Shaders/GlowingElementBorder.shader");
    }

    private static Material CreateAndSaveMaterial(Shader shader, string name)
    {
        if (shader == null) return null;

        // Ensure material directory exists
        if (!AssetDatabase.IsValidFolder("Assets/VR-Workspace/Materials"))
            AssetDatabase.CreateFolder("Assets/VR-Workspace", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialDir))
            AssetDatabase.CreateFolder("Assets/VR-Workspace/Materials", "UIComponents");

        string path = $"{MaterialDir}/{name}.mat";

        // Check if material already exists
        var existingMat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existingMat != null)
        {
            return existingMat;
        }

        // Create new material
        var mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();

        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    private static void EnsureDirectoryExists()
    {
        if (!AssetDatabase.IsValidFolder("Assets/VR-Workspace"))
            AssetDatabase.CreateFolder("Assets", "VR-Workspace");
        if (!AssetDatabase.IsValidFolder("Assets/VR-Workspace/Prefabs"))
            AssetDatabase.CreateFolder("Assets/VR-Workspace", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabDir))
            AssetDatabase.CreateFolder("Assets/VR-Workspace/Prefabs", "UIComponents");
    }

    private static void SavePrefab(GameObject go, string name)
    {
        var path = Path.Combine(PrefabDir, $"{name}.prefab").Replace("\\", "/");
        PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(path);
        Object.DestroyImmediate(go);
        Debug.Log($"[VRUIComponentsPrefabBuilder] Prefab saved: {path}");
    }

    private static GameObject CreateContainer(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static GameObject CreateHitArea(Transform parent, float width, float height)
    {
        var hitArea = new GameObject("HitArea");
        hitArea.transform.SetParent(parent, false);

        var hitRT = hitArea.AddComponent<RectTransform>();
        hitRT.anchorMin = Vector2.zero;
        hitRT.anchorMax = Vector2.one;
        hitRT.offsetMin = hitRT.offsetMax = Vector2.zero;

        var hitImg = hitArea.AddComponent<Image>();
        hitImg.color = Color.clear;

        // BoxCollider for VR raycast
        var collider = hitArea.AddComponent<BoxCollider>();
        collider.size = new Vector3(width, height, 0.1f);

        return hitArea;
    }

    private static GameObject CreateVisualRoot(Transform parent, float expansion)
    {
        var visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(parent, false);

        var visRT = visualRoot.AddComponent<RectTransform>();
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = visRT.offsetMax = Vector2.zero;

        return visualRoot;
    }

    private static Image CreateGlassBackground(Transform parent, float width, float height, Color col, float alpha, float cornerRadius)
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

        if (_glassGradientShader != null)
        {
            // Create unique material name based on color
            string matName = $"ButtonGlassBg_{col.r:F2}_{col.g:F2}_{col.b:F2}_{alpha:F2}".Replace(".", "");
            var mat = CreateAndSaveMaterial(_glassGradientShader, matName);
            if (mat != null)
            {
                mat.SetFloat("_CornerRadius", cornerRadius);
                mat.SetFloat("_EdgePadding", 0.12f);
                mat.SetFloat("_Aspect", width / height);
                mat.SetColor("_ColorA", new Color(col.r, col.g, col.b, alpha * 1.1f));
                mat.SetColor("_ColorB", new Color(col.r, col.g, col.b, alpha * 0.5f));
                mat.SetFloat("_GlassAlpha", alpha * 0.9f);
                EditorUtility.SetDirty(mat);
                img.material = mat;
                img.color = Color.white;
            }
        }
        else
        {
            img.color = new Color(col.r, col.g, col.b, alpha);
        }

        return img;
    }

    private static Image CreateGlowingBorder(Transform parent, float width, float height, Color col, float cornerRadius, float borderWidth, bool pulse = false)
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

        if (_glowingElementBorderShader != null)
        {
            // Create unique material name
            string matName = $"ButtonBorder_{col.r:F2}_{col.g:F2}_{col.b:F2}_{(pulse ? "pulse" : "normal")}".Replace(".", "");
            var mat = CreateAndSaveMaterial(_glowingElementBorderShader, matName);
            if (mat != null)
            {
                mat.SetFloat("_Aspect", width / height);
                mat.SetFloat("_EdgePadding", 0.12f);

                Color borderGlowCol = Color.Lerp(col, Color.white, 0.75f);
                mat.SetColor("_GlowColor", borderGlowCol);

                mat.SetFloat("_BorderWidth", borderWidth);
                mat.SetFloat("_GlowWidth", 0.03f);
                mat.SetFloat("_GlowIntensity", pulse ? 5.0f : 2.5f);
                mat.SetFloat("_CornerRadius", cornerRadius);
                mat.SetFloat("_PulseEnabled", pulse ? 1f : 0f);

                if (pulse)
                {
                    mat.SetFloat("_PulseSpeed", 1.8f);
                    mat.SetFloat("_PulseMin", 0.8f);
                    mat.SetFloat("_PulseMax", 1.4f);
                }

                EditorUtility.SetDirty(mat);
                img.material = mat;

                // Add VRButtonRipple component
                go.AddComponent<VRButtonRipple>().Initialize(mat, img);
            }
        }

        return img;
    }

    private static GameObject CreateIconPlaceholder(Transform parent, string name, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);

        var img = go.AddComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.color = Color.white;

        return go;
    }

    private static void AddIconGlow(GameObject iconObj, Color col)
    {
        var img = iconObj.GetComponent<Image>();
        if (img != null)
        {
            img.color = Color.Lerp(col, Color.white, 0.9f);
        }

        // Glow Layer 1 - Sharp inner halo
        Color glowCol = Color.Lerp(col, Color.white, 0.7f);
        glowCol.a = 0.4f;
        float s1 = 2f;

        var shadow1 = iconObj.AddComponent<Shadow>();
        shadow1.effectColor = glowCol;
        shadow1.effectDistance = new Vector2(s1, -s1);

        var shadow2 = iconObj.AddComponent<Shadow>();
        shadow2.effectColor = glowCol;
        shadow2.effectDistance = new Vector2(-s1, s1);

        // Glow Layer 2 - Soft outer bloom
        Color bloomCol = Color.Lerp(col, Color.white, 0.8f);
        bloomCol.a = 0.15f;
        float s2 = 5f;

        var shadow3 = iconObj.AddComponent<Shadow>();
        shadow3.effectColor = bloomCol;
        shadow3.effectDistance = new Vector2(s2, -s2);

        var shadow4 = iconObj.AddComponent<Shadow>();
        shadow4.effectColor = bloomCol;
        shadow4.effectDistance = new Vector2(-s2, s2);
    }

    private static void AddTextGlow(GameObject textObj, Color col)
    {
        Color glow = Color.Lerp(col, Color.white, 0.8f);
        glow.a = 0.55f;

        var s1 = textObj.AddComponent<Shadow>();
        s1.effectColor = glow;
        s1.effectDistance = new Vector2(3, -3);

        var s2 = textObj.AddComponent<Shadow>();
        s2.effectColor = glow;
        s2.effectDistance = new Vector2(-3, 3);

        var s3 = textObj.AddComponent<Shadow>();
        s3.effectColor = new Color(glow.r, glow.g, glow.b, 0.3f);
        s3.effectDistance = new Vector2(0, 0);
    }

    private static GameObject CreateTextLabel(Transform parent, string text, int fontSize, Color color, bool bold, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject("Text_" + text);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;

        return go;
    }

    private static GameObject CreateArrowIcon(Transform parent)
    {
        var go = new GameObject("Arrow");
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(28f, 28f);

        var img = go.AddComponent<Image>();
        img.sprite = CreateArrowSprite();
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.color = new Color(1,1,1,0.85f);

        return go;
    }

    private static Button AddButtonComponent(GameObject hitArea, Image targetGraphic, Color col)
    {
        var btn = hitArea.AddComponent<Button>();
        btn.targetGraphic = targetGraphic;

        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(col.r, col.g, col.b, 0.5f);
        cb.pressedColor = new Color(col.r, col.g, col.b, 0.7f);
        cb.fadeDuration = 0.1f;
        btn.colors = cb;

        return btn;
    }

    private static void AddButtonAnimation(GameObject hitArea, Transform visualRoot, float popAmount)
    {
        var anim = hitArea.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visualRoot;
        anim.popAmount = popAmount;
    }

    // ==================== VRMENUFRAME HELPERS ====================

    private static void CreateFrameGlassBackground(Transform parent, float w, float h)
    {
        var bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);

        var img = bgObj.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.type = Image.Type.Simple;

        // Expansion to hide edge artifacts
        float p = 0.04f;
        float safeZone = 0.06f;
        float effectiveP = p + safeZone;
        float expansion = effectiveP / (1f - 2f * effectiveP);

        if (_glassGradientShader != null)
        {
            var mat = CreateAndSaveMaterial(_glassGradientShader, "FrameGlassBackground");
            if (mat != null)
            {
                mat.SetFloat("_CornerRadius", 0.12f);
                mat.SetFloat("_EdgePadding", p);
                mat.SetFloat("_Aspect", w / h);

                // Gradient: Cyan left (70%), Purple right (30%)
                Color cyanGlass = new Color(0.35f, 0.9f, 1f, 0.15f);
                Color purpleGlass = new Color(0.75f, 0.45f, 1f, 0.22f);
                mat.SetColor("_ColorA", cyanGlass);
                mat.SetColor("_ColorB", purpleGlass);
                mat.SetFloat("_GradientOffset", 0f);
                mat.SetFloat("_GradientAngle", -10f);
                mat.SetFloat("_CyanRatio", 0.7f);
                mat.SetFloat("_GlassAlpha", 0.08f);
                mat.SetFloat("_FresnelPower", 2.2f);
                mat.SetFloat("_FresnelStrength", 0.12f);

                EditorUtility.SetDirty(mat);
                img.material = mat;
                img.color = Color.white;
            }
        }
        else
        {
            Debug.LogWarning("[VRUIComponentsPrefabBuilder] GlassGradientBackground shader not found!");
            img.color = new Color(0.5f, 0.8f, 1f, 0.1f);
            expansion = 0;
        }

        // Add collider for VR interaction
        var col = bgObj.AddComponent<BoxCollider>();
        col.size = new Vector3(w, h, 0.1f);

        var rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(-expansion, -expansion);
        rt.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localPosition = Vector3.zero;
        rt.SetAsFirstSibling();
    }

    private static void CreateFrameGlowingBorder(Transform parent, float w, float h)
    {
        var borderObj = new GameObject("GlowingBorder");
        borderObj.transform.SetParent(parent, false);

        var rt = borderObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = borderObj.AddComponent<Image>();
        img.raycastTarget = false;

        if (_glowingGlassBorderShader != null)
        {
            var mat = CreateAndSaveMaterial(_glowingGlassBorderShader, "FrameGlowingBorder");
            if (mat != null)
            {
                mat.SetFloat("_BorderWidth", 0.02f);
                mat.SetFloat("_CornerRadius", 0.12f);
                mat.SetFloat("_EdgePadding", 0.04f);
                mat.SetFloat("_Aspect", w / h);

                // Multi-layer glow
                mat.SetFloat("_Layer1Width", 0.008f);
                mat.SetFloat("_Layer1Alpha", 1.5f);
                mat.SetFloat("_Layer2Width", 0.018f);
                mat.SetFloat("_Layer2Alpha", 1.0f);
                mat.SetFloat("_Layer3Width", 0.04f);
                mat.SetFloat("_Layer3Alpha", 0.6f);
                mat.SetFloat("_Layer4Width", 0.08f);
                mat.SetFloat("_Layer4Alpha", 0.3f);

                // Gradient colors - Cyan to Purple
                Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
                Color purpleColor = new Color(1f, 0.4f, 1f, 1f);
                mat.SetColor("_ColorA", cyanColor);
                mat.SetColor("_ColorB", purpleColor);
                mat.SetFloat("_GradientMode", 2f); // Diagonal
                mat.SetFloat("_GradientAngle", -10f);
                mat.SetFloat("_GlassAlpha", 0.02f);
                mat.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
                mat.SetFloat("_ShimmerSpeed", 0.4f);
                mat.SetFloat("_ShimmerIntensity", 0.15f);

                EditorUtility.SetDirty(mat);
                img.material = mat;
                img.color = Color.white;
                img.sprite = GetPixelSprite();
            }
        }
        else
        {
            Debug.LogWarning("[VRUIComponentsPrefabBuilder] GlowingGlassBorder shader not found!");
        }

        borderObj.transform.SetAsLastSibling();
    }

    private static void CreateFloatingDataEffects(Transform parent, float w, float h)
    {
        var fxContainer = new GameObject("FX_DataStream");
        fxContainer.transform.SetParent(parent, false);

        var rt = fxContainer.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        fxContainer.AddComponent<RectMask2D>();

        int particleCount = 20;
        for (int i = 0; i < particleCount; i++)
        {
            var p = new GameObject($"Bit_{i}");
            p.transform.SetParent(fxContainer.transform, false);

            var pImg = p.AddComponent<Image>();
            pImg.sprite = GetPixelSprite();

            bool cyanOrPurple = Random.value > 0.5f;
            Color baseCol = cyanOrPurple ? Color.cyan : new Color(0.8f, 0f, 1f);
            pImg.color = new Color(baseCol.r, baseCol.g, baseCol.b, Random.Range(0.1f, 0.4f));

            var pRT = p.GetComponent<RectTransform>();
            float size = Random.Range(10f, 60f);
            pRT.sizeDelta = new Vector2(size, size * Random.Range(0.2f, 1.0f));

            float startX = Random.Range(-w / 2f, w / 2f);
            float startY = Random.Range(-h / 2f, h / 2f);
            pRT.anchoredPosition = new Vector2(startX, startY);

            var anim = p.AddComponent<FloatingDataAnim>();
            anim.speed = Random.Range(10f, 40f);
            anim.range = new Vector2(w, h);
        }
    }

    private static void CreateStatusBar(Transform parent, float w, float h, float height, float separatorY)
    {
        var barObj = new GameObject("StatusBar");
        barObj.transform.SetParent(parent, false);

        var rt = barObj.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0, height);
        rt.anchoredPosition = Vector2.zero;

        float sidePadding = 0f;

        // --- LEFT GROUP (Clock + Recenter) ---
        var leftGroup = new GameObject("LeftGroup");
        leftGroup.transform.SetParent(barObj.transform, false);
        var leftRT = leftGroup.AddComponent<RectTransform>();
        leftRT.anchorMin = new Vector2(0, 0);
        leftRT.anchorMax = new Vector2(0.5f, 1);
        leftRT.pivot = new Vector2(0, 0.5f);
        leftRT.offsetMin = new Vector2(sidePadding, 0);
        leftRT.offsetMax = new Vector2(0, 0);

        var lLayout = leftGroup.AddComponent<HorizontalLayoutGroup>();
        lLayout.childAlignment = TextAnchor.MiddleLeft;
        lLayout.spacing = -680f;
        lLayout.childControlWidth = false;
        lLayout.childControlHeight = false;
        lLayout.childForceExpandHeight = false;
        lLayout.padding = new RectOffset(0, 0, 5, 5);

        // Clock
        var timeObj = CreateTextLabel(leftGroup.transform, "12:00", 42, new Color(1f, 1f, 1f, 0.9f), true, TextAlignmentOptions.MidlineLeft);
        var timeRT = timeObj.GetComponent<RectTransform>();
        var fitter = timeObj.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Recenter Button
        CreateRecenterButton(leftGroup.transform, 73f);

        // --- STATUS GROUP (Right) ---
        var statusGroup = new GameObject("StatusGroup");
        statusGroup.transform.SetParent(barObj.transform, false);
        var groupRT = statusGroup.AddComponent<RectTransform>();
        groupRT.anchorMin = new Vector2(1, 0);
        groupRT.anchorMax = new Vector2(1, 1);
        groupRT.pivot = new Vector2(1, 0.5f);
        groupRT.sizeDelta = new Vector2(400, 0);
        groupRT.anchoredPosition = new Vector2(-sidePadding, 0);

        var layout = statusGroup.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.spacing = -175f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(0, 0, 5, 5);

        // Network Icon
        var netObj = new GameObject("NetworkIcon");
        netObj.transform.SetParent(statusGroup.transform, false);
        var netImg = netObj.AddComponent<Image>();
        netImg.sprite = CreateWifiSprite();
        netImg.preserveAspect = true;
        var netRT = netObj.GetComponent<RectTransform>();
        netRT.sizeDelta = new Vector2(60, 60);

        // Battery Container
        CreateBatteryIndicator(statusGroup.transform);
    }

    private static void CreateRecenterButton(Transform parent, float size)
    {
        var recenterBtn = new GameObject("RecenterBtn");
        recenterBtn.transform.SetParent(parent, false);
        var rRT = recenterBtn.AddComponent<RectTransform>();
        rRT.sizeDelta = new Vector2(size, size);

        // Visual Root
        var visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(recenterBtn.transform, false);
        var visRT = visualRoot.AddComponent<RectTransform>();
        float expansion = 0.12f;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = Vector2.zero;
        visRT.offsetMax = Vector2.zero;

        // Background
        var bg = visualRoot.AddComponent<Image>();
        bg.sprite = GetPixelSprite();

        if (_glassGradientShader != null)
        {
            var mat = CreateAndSaveMaterial(_glassGradientShader, "RecenterButtonBg");
            if (mat != null)
            {
                mat.SetFloat("_CornerRadius", 0.15f);
                mat.SetFloat("_EdgePadding", 0.12f);
                mat.SetFloat("_Aspect", 1.0f);
                mat.SetColor("_ColorA", new Color(0f, 1f, 1f, 0.12f));
                mat.SetColor("_ColorB", new Color(0f, 1f, 1f, 0.04f));
                mat.SetFloat("_GlassAlpha", 0.075f);
                EditorUtility.SetDirty(mat);
                bg.material = mat;
                bg.color = Color.white;
            }
        }
        else
        {
            bg.color = new Color(0f, 1f, 1f, 0.15f);
        }

        // Border
        var borderObj = new GameObject("Border");
        borderObj.transform.SetParent(visualRoot.transform, false);
        var borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = Vector2.zero;

        var borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;

        if (_glowingElementBorderShader != null)
        {
            var glowMat = CreateAndSaveMaterial(_glowingElementBorderShader, "RecenterButtonBorder");
            if (glowMat != null)
            {
                glowMat.SetFloat("_Aspect", 1.0f);
                glowMat.SetFloat("_EdgePadding", 0.12f);
                glowMat.SetColor("_GlowColor", Color.Lerp(CyanColor, Color.white, 0.75f));
                glowMat.SetFloat("_BorderWidth", 0.05f);
                glowMat.SetFloat("_GlowWidth", 0.04f);
                glowMat.SetFloat("_GlowIntensity", 2.5f);
                glowMat.SetFloat("_CornerRadius", 0.15f);
                glowMat.SetFloat("_PulseEnabled", 0f);
                EditorUtility.SetDirty(glowMat);
                borderImg.material = glowMat;
                borderImg.sprite = GetPixelSprite();
                borderObj.AddComponent<VRButtonRipple>().Initialize(glowMat, borderImg);
            }
        }

        // Icon
        var iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(visualRoot.transform, false);
        var iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = Vector2.zero;
        iconRT.anchorMax = Vector2.one;
        iconRT.sizeDelta = new Vector2(-52, -52);

        var iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = CreateRecenterSprite();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        iconImg.color = Color.Lerp(CyanColor, Color.white, 0.9f);
        AddIconGlow(iconObj, CyanColor);

        // Button & Collider
        var btn = recenterBtn.AddComponent<Button>();
        btn.targetGraphic = bg;

        var col = recenterBtn.AddComponent<BoxCollider>();
        col.size = new Vector3(size, size, 0.1f);

        // Animation
        var anim = recenterBtn.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visualRoot.transform;
        anim.popAmount = 0.025f;
    }

    private static void CreateBatteryIndicator(Transform parent)
    {
        var battContainer = new GameObject("BatteryContainer");
        battContainer.transform.SetParent(parent, false);
        var battRT = battContainer.AddComponent<RectTransform>();
        battRT.sizeDelta = new Vector2(100, 50);

        var batSprite = CreateBatterySprite();

        // Background
        var bgObj = new GameObject("Bg");
        bgObj.transform.SetParent(battContainer.transform, false);
        var bgImg = bgObj.AddComponent<Image>();
        bgImg.sprite = batSprite;
        bgImg.color = new Color(0.8f, 0.8f, 0.8f, 0.5f);
        bgImg.preserveAspect = true;
        var bgRT = bgObj.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;

        // Fill
        var fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(battContainer.transform, false);
        var fillImg = fillObj.AddComponent<Image>();
        fillImg.sprite = batSprite;
        fillImg.color = Color.white;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.preserveAspect = true;
        var fillRT = fillObj.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;

        // Text
        var battTxtObj = CreateTextLabel(battContainer.transform, "100", 28, new Color(0.1f, 0.15f, 0.2f, 1f), true);
        var btRT = battTxtObj.GetComponent<RectTransform>();
        btRT.anchorMin = Vector2.zero;
        btRT.anchorMax = Vector2.one;
        btRT.sizeDelta = Vector2.zero;
        btRT.offsetMin = new Vector2(0, 0);
        btRT.offsetMax = new Vector2(-8, 0);
    }

    private static void CreateSeparatorLine(Transform parent, float w, float yPos)
    {
        var lineObj = new GameObject("SeparatorLine");
        lineObj.transform.SetParent(parent, false);

        var rt = lineObj.AddComponent<RectTransform>();
        float lineWidth = w * 1.1f;

        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(lineWidth, 2);
        rt.anchoredPosition = new Vector2(0, -yPos);

        var img = lineObj.AddComponent<Image>();
        img.sprite = CreateGradientLineSprite();
        img.raycastTarget = false;

        // Glow shadow
        var s = lineObj.AddComponent<Shadow>();
        s.effectColor = new Color(0.5f, 0f, 1f, 0.5f);
        s.effectDistance = new Vector2(0, -1);
    }

    // ==================== SPRITE GENERATORS ====================

    private static Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        var tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }

    private static Sprite CreateArrowSprite()
    {
        var tex = new Texture2D(32, 32);
        var pixels = new Color[32 * 32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        // Draw downward arrow
        for (int y = 8; y < 24; y++)
        {
            int half = (y - 8) / 2;
            for (int x = 16 - half; x <= 16 + half; x++)
                if (x >= 0 && x < 32) pixels[y * 32 + x] = Color.white;
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
    }

    private static Sprite CreateBatterySprite()
    {
        int w = 128;
        int h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var colors = new Color[w * h];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;

        int bodyW = 110;
        int radius = 16;
        int nubW = 8;
        int nubH = 24;
        int nubRadius = 4;
        int nubY = (h - nubH) / 2;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < bodyW + nubW; x++)
            {
                float alpha = 0f;
                // Draw Body
                if (x < bodyW)
                {
                    float dx = Mathf.Min(x, bodyW - 1 - x);
                    float dy = Mathf.Min(y, h - 1 - y);

                    if (dx < radius && dy < radius)
                    {
                        float d = Vector2.Distance(new Vector2(dx, dy), new Vector2(radius, radius));
                        alpha = Mathf.Clamp01((radius + 0.5f) - d);
                    }
                    else alpha = 1.0f;
                }
                // Draw Nub
                else if (x >= bodyW && x < bodyW + nubW)
                {
                    if (y >= nubY && y < nubY + nubH)
                    {
                        float nx = x - bodyW;
                        float ny = y - nubY;
                        float dx = Mathf.Min(nx, nubW - 1 - nx);
                        float dy = Mathf.Min(ny, nubH - 1 - ny);

                        if (nx > nubW - nubRadius - 1 && dy < nubRadius)
                        {
                            float d = Vector2.Distance(new Vector2(nx, dy), new Vector2(nubW - nubRadius - 1, nubRadius));
                            alpha = Mathf.Clamp01((nubRadius + 0.5f) - d);
                        }
                        else alpha = 1.0f;
                    }
                }

                if (alpha > 0) colors[y * w + x] = new Color(1, 1, 1, alpha);
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, bodyW + nubW, h), new Vector2(0.5f, 0.5f), 100, 1, SpriteMeshType.Tight);
    }

    private static Sprite CreateWifiSprite()
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var colors = new Color[size * size];

        Vector2 center = new Vector2(size / 2, 4);
        float maxRadius = size * 0.85f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                bool colored = false;

                // Dot
                if (d < size * 0.12f) colored = true;

                // Arcs (3 arcs)
                for (int i = 1; i <= 3; i++)
                {
                    float r = maxRadius * (i / 3.0f);
                    float thickness = size * 0.08f;

                    if (Mathf.Abs(d - r) < thickness)
                    {
                        Vector2 dir = (new Vector2(x, y) - center).normalized;
                        if (dir.y > 0.6f) colored = true;
                    }
                }

                colors[y * size + x] = colored ? Color.white : Color.clear;
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
    }

    private static Sprite CreateRecenterSprite()
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var colors = new Color[size * size];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;

        int cx = size / 2;
        int cy = size / 2;
        int outerR = 28;
        int innerR = 20;
        int thickness = 3;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));

                // Outer ring
                if (d >= outerR - thickness && d <= outerR)
                    colors[y * size + x] = Color.white;

                // Inner ring
                if (d >= innerR - thickness && d <= innerR)
                    colors[y * size + x] = Color.white;

                // Cross lines
                if ((Mathf.Abs(x - cx) < 2 && d <= outerR) || (Mathf.Abs(y - cy) < 2 && d <= outerR))
                    colors[y * size + x] = Color.white;
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
    }

    private static Sprite CreateGradientLineSprite()
    {
        int w = 256;
        int h = 2;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var fill = new Color[w * h];

        Color c1 = Color.cyan;
        Color c2 = new Color(0.8f, 0f, 1f); // Purple

        for (int x = 0; x < w; x++)
        {
            float t = (float)x / (w - 1);

            // Bias towards Cyan using Cubic ease in
            Color col = Color.Lerp(c1, c2, Mathf.Pow(t, 3.0f));

            // Alpha based on Sine wave for fade out at ends
            float alpha = Mathf.Sin(t * Mathf.PI);
            alpha = Mathf.Pow(alpha, 0.5f);
            col.a = alpha;

            for (int y = 0; y < h; y++)
            {
                fill[y * w + x] = col;
            }
        }

        tex.SetPixels(fill);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;

        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
    }
}
#endif
