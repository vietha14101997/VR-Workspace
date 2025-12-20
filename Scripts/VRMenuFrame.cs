using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// VR Menu Frame - World Space UI panel with glass effect and status bar.
/// Canvas is directly on this GameObject (merged, no child MenuCanvas).
/// Uses logicalWidth for pixel-perfect UI scaling.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(GraphicRaycaster))]
public class VRMenuFrame : MonoBehaviour
{
    [Header("Panel Size (meters)")]
    [Tooltip("Physical width of the panel in meters")]
    public float panelWidth = 1.6f;
    [Tooltip("Physical height of the panel in meters")]
    public float panelHeight = 0.9f;

    [Header("Logical Size (pixels)")]
    [Tooltip("Logical width in pixels for UI layout calculations")]
    public float logicalWidth = 1920f;

    [Header("Frame Configuration")]
    [Tooltip("Distance from top edge to separator line in logical pixels")]
    public float separatorOffset = 80f;
    public float sidePadding = 0f;

    [Header("Visual Config")]
    public Color glassColor = new Color(1.0f, 1.0f, 1.0f, 0.098f);
    public Color panelBorderColor = new Color(1.0f, 1.0f, 1.0f, 0.392f);

    [Header("Glowing Border Config")]
    [ColorUsage(true, true)]
    public Color glowColorA = new Color(0f, 1.5f, 2f, 1f);
    [ColorUsage(true, true)]
    public Color glowColorB = new Color(1.2f, 0.3f, 2f, 1f);
    public float glowIntensity = 1.2f;
    public float borderThickness = 3f;
    public float edgePadding = 0.04f;
    public float glowSpread = 40f;
    public float shimmerSpeed = 0.4f;
    public float hdrBoost = 1.8f;

    [Header("Style Resources")]
    public TMP_FontAsset customFont;
    public Sprite iconSignal;
    public Sprite iconWifi;
    public Sprite iconBattery;

    // Internal Resources
    private Sprite _roundedSprite;
    private Sprite _signalSprite;
    private Sprite _batterySprite;
    private Sprite _pixelSprite;
    private Dictionary<int, Sprite> _borderSprites = new Dictionary<int, Sprite>();

    // Status References
    private TextMeshProUGUI _clockText;
    private TextMeshProUGUI _batteryText;
    private Image _networkIcon;
    private Image _batteryFillImage;

    // Components (on this GameObject)
    public Canvas Canvas { get; private set; }
    public RectTransform CanvasRect { get; private set; }
    public RectTransform ContentContainer { get; private set; }

    // Calculated values
    private float LogicalHeight => (logicalWidth / panelWidth) * panelHeight;
    private float ScaleFactor => panelWidth / logicalWidth;
    private float Aspect => panelWidth / panelHeight;

#if UNITY_EDITOR
    void OnEnable()
    {
        if (!Application.isPlaying)
        {
            UpdateMaterialAspectRatiosEditor();
        }
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    // Update RectTransform when dimensions change
                    var rt = GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.sizeDelta = new Vector2(logicalWidth, LogicalHeight);
                        rt.localScale = new Vector3(ScaleFactor, ScaleFactor, 1f);
                    }
                    UpdateMaterialAspectRatiosEditor();
                }
            };
        }
    }

    void UpdateMaterialAspectRatiosEditor()
    {
        float aspect = Aspect;

        var glassBg = transform.Find("GlassBackground");
        if (glassBg != null)
        {
            var img = glassBg.GetComponent<Image>();
            if (img != null && img.material != null)
            {
                img.material.SetFloat("_Aspect", aspect);
                EditorUtility.SetDirty(img.material);
            }

            var glowBorder = glassBg.Find("GlowingBorder");
            if (glowBorder != null)
            {
                var borderImg = glowBorder.GetComponent<Image>();
                if (borderImg != null && borderImg.material != null)
                {
                    borderImg.material.SetFloat("_Aspect", aspect);
                    EditorUtility.SetDirty(borderImg.material);
                }
            }
        }
    }
#endif

    void Start()
    {
        LoadIcons();

        // Get/Setup Canvas on this GameObject
        Canvas = GetComponent<Canvas>();
        CanvasRect = GetComponent<RectTransform>();

        if (Canvas == null)
        {
            Canvas = gameObject.AddComponent<Canvas>();
        }
        Canvas.renderMode = RenderMode.WorldSpace;

        // Ensure GraphicRaycaster exists
        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        // Check for existing content from prefab
        if (ContentContainer == null)
        {
            TryInitializeExisting();
        }

        // Build if needed
        if (ContentContainer == null)
        {
            Build();
        }
    }

    void TryInitializeExisting()
    {
        // Find ContentContainer
        Transform contentTransform = transform.Find("ContentContainer");
        if (contentTransform != null)
        {
            ContentContainer = contentTransform.GetComponent<RectTransform>();
        }

        // Update materials
        UpdateMaterialAspectRatios();

        // Setup status bar references
        SetupStatusBarReferences(transform);

        // Re-apply runtime sprites (they don't serialize in prefabs)
        ReapplyRuntimeSprites();

        // Re-initialize floating data animations
        ReinitializeFloatingDataEffects();

        // Re-register recenter button click event
        SetupRecenterButtonListener();

        // Ensure layers are set for VRGazeReticle raycast
        SetupVRLayers();

        if (ContentContainer != null)
        {
            Debug.Log("[VRMenuFrame] Initialized from existing content");
        }
    }

    void SetupVRLayers()
    {
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer == -1) return;

        float h = LogicalHeight;

        // GlassBackground - fix collider size in logical pixels
        Transform glassBg = transform.Find("GlassBackground");
        if (glassBg != null)
        {
            BoxCollider bgCol = glassBg.GetComponent<BoxCollider>();
            if (bgCol != null)
            {
                glassBg.gameObject.layer = vrLayer;

                // Recalculate expansion
                float p = edgePadding;
                float safeZone = 0.06f;
                float effectiveP = p + safeZone;
                float expansion = effectiveP / (1f - 2f * effectiveP);

                // Fix collider size if it's wrong
                float expandedW = logicalWidth * (1f + 2f * expansion);
                float expandedH = h * (1f + 2f * expansion);
                if (Mathf.Abs(bgCol.size.x - expandedW) > 1f || bgCol.size.z > 0.1f)
                {
                    bgCol.size = new Vector3(expandedW, expandedH, 0.01f);
                    bgCol.center = Vector3.zero;
                }
            }
        }

        // RecenterBtn - ensure collider uses logical pixels
        Transform recenterBtn = transform.Find("StatusBar/LeftGroup/RecenterBtn");
        if (recenterBtn != null)
        {
            BoxCollider btnCol = recenterBtn.GetComponent<BoxCollider>();
            if (btnCol != null)
            {
                recenterBtn.gameObject.layer = vrLayer;

                // Fix collider size if it's wrong
                float btnSize = 72f;
                if (Mathf.Abs(btnCol.size.x - btnSize) > 1f || btnCol.size.z > 0.2f)
                {
                    btnCol.size = new Vector3(btnSize, btnSize, 0.1f);
                    btnCol.center = new Vector3(0, 0, -0.1f);
                }
            }
        }
    }

    /// <summary>
    /// Re-register the recenter button click listener when loading from prefab
    /// </summary>
    void SetupRecenterButtonListener()
    {
        Transform recenterBtn = transform.Find("StatusBar/LeftGroup/RecenterBtn");
        if (recenterBtn == null) return;

        // VRButtonFactory structure: RecenterBtn/HitArea has Button component
        Transform hitArea = recenterBtn.Find("HitArea");
        if (hitArea == null) return;

        Button btn = hitArea.GetComponent<Button>();
        if (btn == null)
        {
            btn = hitArea.gameObject.AddComponent<Button>();

            // Set target graphic (VRButtonFactory: Visuals/Background)
            Transform visuals = hitArea.Find("Visuals");
            if (visuals != null)
            {
                Transform background = visuals.Find("Background");
                if (background != null)
                {
                    Image bgImg = background.GetComponent<Image>();
                    if (bgImg != null) btn.targetGraphic = bgImg;
                }
            }
        }

        // Disable flash effect on click
        btn.transition = Selectable.Transition.None;

        // Remove old listeners and add fresh one
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(RecenterObject);
    }

    /// <summary>
    /// Re-apply sprites that are generated at runtime (not serialized in prefabs)
    /// </summary>
    void ReapplyRuntimeSprites()
    {
        // Re-apply GlassBackground and GlowingBorder sprites (must be before early returns)
        Transform glassBg = transform.Find("GlassBackground");
        if (glassBg != null)
        {
            // Re-apply GlassBackground sprite
            var glassBgImg = glassBg.GetComponent<Image>();
            if (glassBgImg != null && glassBgImg.sprite == null)
            {
                glassBgImg.sprite = GetPixelSprite();
            }

            // Re-apply GlowingBorder sprite
            Transform glowBorder = glassBg.Find("GlowingBorder");
            if (glowBorder != null)
            {
                var borderImg = glowBorder.GetComponent<Image>();
                if (borderImg != null && borderImg.sprite == null)
                {
                    borderImg.sprite = GetPixelSprite();
                }
            }
        }

        Transform statusBar = transform.Find("StatusBar");
        if (statusBar == null) return;

        // Re-apply separator line gradient sprite
        Transform separatorLine = statusBar.Find("SeparatorLine");
        if (separatorLine != null)
        {
            var lineImg = separatorLine.GetComponent<Image>();
            if (lineImg != null)
            {
                lineImg.sprite = GetGradientLineSprite();
            }
        }

        Transform statusGroup = statusBar.Find("StatusGroup");
        if (statusGroup == null) return;

        // Re-apply battery sprite
        Transform battContainer = statusGroup.Find("BatteryContainer");
        if (battContainer != null)
        {
            Sprite batSprite = GetBatterySprite();

            Transform bg = battContainer.Find("Bg");
            if (bg != null)
            {
                var bgImg = bg.GetComponent<Image>();
                if (bgImg != null) bgImg.sprite = batSprite;
            }

            Transform fill = battContainer.Find("Fill");
            if (fill != null)
            {
                var fillImg = fill.GetComponent<Image>();
                if (fillImg != null) fillImg.sprite = batSprite;
            }
        }

        // Re-apply network icon sprite
        Transform netIcon = statusGroup.Find("NetworkIcon");
        if (netIcon != null)
        {
            var netImg = netIcon.GetComponent<Image>();
            if (netImg != null)
            {
                netImg.sprite = iconWifi ?? GetWifiSprite();
            }
        }
    }

    /// <summary>
    /// Re-initialize floating data animations when loading from prefab
    /// </summary>
    void ReinitializeFloatingDataEffects()
    {
        Transform glassBg = transform.Find("GlassBackground");
        if (glassBg == null) return;

        Transform fxContainer = glassBg.Find("FX_DataStream");
        if (fxContainer == null) return;

        float w = logicalWidth;
        float h = LogicalHeight;

        var anims = fxContainer.GetComponentsInChildren<FloatingDataAnim>();
        foreach (var anim in anims)
        {
            anim.range = new Vector2(w, h);
            anim.ForceReinitialize();
        }
    }

    void UpdateMaterialAspectRatios()
    {
        float aspect = Aspect;

        var glassBg = transform.Find("GlassBackground");
        if (glassBg != null)
        {
            var img = glassBg.GetComponent<Image>();
            if (img != null && img.material != null)
            {
                img.material = new Material(img.material);
                img.material.SetFloat("_Aspect", aspect);
            }

            var glowBorder = glassBg.Find("GlowingBorder");
            if (glowBorder != null)
            {
                var borderImg = glowBorder.GetComponent<Image>();
                if (borderImg != null && borderImg.material != null)
                {
                    borderImg.material = new Material(borderImg.material);
                    borderImg.material.SetFloat("_Aspect", aspect);
                }
            }
        }
    }

    void SetupStatusBarReferences(Transform root)
    {
        Transform statusBar = root.Find("StatusBar");
        if (statusBar == null) return;

        Transform leftGroup = statusBar.Find("LeftGroup");
        if (leftGroup != null)
        {
            TextMeshProUGUI[] texts = leftGroup.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0)
            {
                _clockText = texts[0];
            }
        }

        Transform statusGroup = statusBar.Find("StatusGroup");
        if (statusGroup != null)
        {
            Transform netIcon = statusGroup.Find("NetworkIcon");
            if (netIcon != null)
            {
                _networkIcon = netIcon.GetComponent<Image>();
            }

            Transform battContainer = statusGroup.Find("BatteryContainer");
            if (battContainer != null)
            {
                Transform fill = battContainer.Find("Fill");
                if (fill != null)
                {
                    _batteryFillImage = fill.GetComponent<Image>();
                }

                TextMeshProUGUI[] battTexts = battContainer.GetComponentsInChildren<TextMeshProUGUI>();
                if (battTexts.Length > 0)
                {
                    _batteryText = battTexts[0];
                }
            }
        }
    }

    void Update()
    {
        UpdateClock();
        UpdateNetwork();
        UpdateBattery();
    }

    [ContextMenu("Rebuild Frame")]
    public void Rebuild()
    {
        // Clean up existing children
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        ContentContainer = null;
        Build();
    }

#if UNITY_EDITOR
    [ContextMenu("Build And Save As Prefab")]
    public void BuildAndSaveAsPrefab()
    {
        Rebuild();
        SaveMaterialsAsAssets();

        string prefabDir = "Assets/VR-Workspace/Prefabs/UI";
        if (!AssetDatabase.IsValidFolder("Assets/VR-Workspace/Prefabs"))
            AssetDatabase.CreateFolder("Assets/VR-Workspace", "Prefabs");
        if (!AssetDatabase.IsValidFolder(prefabDir))
            AssetDatabase.CreateFolder("Assets/VR-Workspace/Prefabs", "UI");

        string prefabPath = $"{prefabDir}/VRMenuFrame.prefab";

        if (PrefabUtility.IsPartOfPrefabInstance(gameObject))
        {
            PrefabUtility.UnpackPrefabInstance(gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        PrefabUtility.SaveAsPrefabAssetAndConnect(gameObject, prefabPath, InteractionMode.UserAction);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[VRMenuFrame] Prefab saved to: {prefabPath}");
    }

    void SaveMaterialsAsAssets()
    {
        string matDir = "Assets/VR-Workspace/Materials/UIComponents";
        if (!AssetDatabase.IsValidFolder("Assets/VR-Workspace/Materials"))
            AssetDatabase.CreateFolder("Assets/VR-Workspace", "Materials");
        if (!AssetDatabase.IsValidFolder(matDir))
            AssetDatabase.CreateFolder("Assets/VR-Workspace/Materials", "UIComponents");

        float aspect = Aspect;

        var glassBg = transform.Find("GlassBackground");
        if (glassBg != null)
        {
            var img = glassBg.GetComponent<Image>();
            if (img != null && img.material != null)
            {
                var savedMat = SaveOrGetMaterial(img.material, "FrameGlassBackground", matDir);
                if (savedMat != null)
                {
                    savedMat.SetFloat("_Aspect", aspect);
                    EditorUtility.SetDirty(savedMat);
                    img.material = savedMat;
                }
            }

            var glowBorder = glassBg.Find("GlowingBorder");
            if (glowBorder != null)
            {
                var borderImg = glowBorder.GetComponent<Image>();
                if (borderImg != null && borderImg.material != null)
                {
                    var savedMat = SaveOrGetMaterial(borderImg.material, "FrameGlowingBorder", matDir);
                    if (savedMat != null)
                    {
                        savedMat.SetFloat("_Aspect", aspect);
                        EditorUtility.SetDirty(savedMat);
                        borderImg.material = savedMat;
                    }
                }
            }
        }

        // VRButtonFactory structure: RecenterBtn/HitArea/Visuals/Background, Border
        var recenterBtn = transform.Find("StatusBar/LeftGroup/RecenterBtn");
        if (recenterBtn != null)
        {
            var hitArea = recenterBtn.Find("HitArea");
            if (hitArea != null)
            {
                var visuals = hitArea.Find("Visuals");
                if (visuals != null)
                {
                    var background = visuals.Find("Background");
                    if (background != null)
                    {
                        var bgImg = background.GetComponent<Image>();
                        if (bgImg != null && bgImg.material != null)
                        {
                            var savedMat = SaveOrGetMaterial(bgImg.material, "RecenterButtonBg", matDir);
                            if (savedMat != null) bgImg.material = savedMat;
                        }
                    }

                    var border = visuals.Find("Border");
                    if (border != null)
                    {
                        var borderImg = border.GetComponent<Image>();
                        if (borderImg != null && borderImg.material != null)
                        {
                            var savedMat = SaveOrGetMaterial(borderImg.material, "RecenterButtonBorder", matDir);
                            if (savedMat != null)
                            {
                                borderImg.material = savedMat;
                                var ripple = border.GetComponent<VRButtonRipple>();
                                if (ripple != null) ripple.Initialize(savedMat, borderImg);
                            }
                        }
                    }
                }
            }
        }

        AssetDatabase.SaveAssets();
    }

    Material SaveOrGetMaterial(Material runtimeMat, string name, string dir)
    {
        string path = $"{dir}/{name}.mat";

        var existingMat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existingMat != null)
        {
            existingMat.CopyPropertiesFromMaterial(runtimeMat);
            EditorUtility.SetDirty(existingMat);
            return existingMat;
        }

        var newMat = new Material(runtimeMat);
        AssetDatabase.CreateAsset(newMat, path);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }
#endif

    /// <summary>
    /// Build the complete menu frame hierarchy using configured dimensions.
    /// </summary>
    public void Build()
    {
        Build(panelWidth, panelHeight, logicalWidth);
    }

    /// <summary>
    /// Build the complete menu frame hierarchy with custom dimensions.
    /// </summary>
    public void Build(float width, float height, float logicWidth = 1920f)
    {
        // Store dimensions
        panelWidth = width;
        panelHeight = height;
        logicalWidth = logicWidth;

        float logicalHeight = LogicalHeight;
        float scaleFactor = ScaleFactor;

        // Setup this GameObject's RectTransform
        CanvasRect = GetComponent<RectTransform>();
        CanvasRect.sizeDelta = new Vector2(logicalWidth, logicalHeight);
        CanvasRect.localScale = new Vector3(scaleFactor, scaleFactor, 1f);
        CanvasRect.localPosition = Vector3.zero;

        float w = logicalWidth;
        float h = logicalHeight;

        // 1. Glass Background & Borders
        CreateGlassPanel(transform, w, h);

        // 2. Status Bar
        float statusBarHeight = h * 0.125f;
        CreateStatusBar(transform, w, h, statusBarHeight);

        // 3. Content Container
        GameObject contentObj = new GameObject("ContentContainer");
        contentObj.transform.SetParent(transform, false);
        ContentContainer = contentObj.AddComponent<RectTransform>();

        ContentContainer.anchorMin = Vector2.zero;
        ContentContainer.anchorMax = Vector2.one;
        ContentContainer.offsetMin = Vector2.zero;
        ContentContainer.offsetMax = new Vector2(0, -statusBarHeight);
    }

    // --- LOGIC ---

    void UpdateClock()
    {
        if (_clockText != null)
            _clockText.text = DateTime.Now.ToString("HH:mm");
    }

    void UpdateNetwork()
    {
        if (_networkIcon != null)
        {
            if (Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork)
            {
                _networkIcon.sprite = iconWifi;
                _networkIcon.color = Color.white;
            }
            else if (Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork)
            {
                _networkIcon.sprite = GetSignalSprite();
                _networkIcon.color = Color.white;
            }
            else
            {
                _networkIcon.sprite = iconWifi;
                _networkIcon.color = new Color(1, 1, 1, 0.3f);
            }
        }
    }

    void UpdateBattery()
    {
        if (_batteryText != null)
        {
            float battLevel = SystemInfo.batteryLevel;
            float displayLevel = (battLevel < 0) ? 1.0f : battLevel;

            string battStr = Mathf.FloorToInt(displayLevel * 100).ToString();
            _batteryText.text = battStr;

            if (_batteryFillImage != null)
            {
                _batteryFillImage.fillAmount = displayLevel;
                _batteryFillImage.color = Color.white;
            }
        }
    }

    // --- CREATION HELPERS (logical pixels) ---

    void CreateGlassPanel(Transform parent, float w, float h)
    {
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);
        Image img = bgObj.AddComponent<Image>();

        img.type = Image.Type.Simple;
        img.sprite = GetPixelSprite();

        float p = edgePadding;
        float safeZone = 0.06f;
        float effectiveP = p + safeZone;
        float expansion = effectiveP / (1f - 2f * effectiveP);

        float aspect = w / h;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);

            glassMat.SetFloat("_CornerRadius", 0.12f);
            glassMat.SetFloat("_EdgePadding", p);
            glassMat.SetFloat("_Aspect", aspect);

            Color cyanGlass = new Color(0.35f, 0.9f, 1f, 0.15f);
            Color purpleGlass = new Color(0.75f, 0.45f, 1f, 0.22f);
            glassMat.SetColor("_ColorA", cyanGlass);
            glassMat.SetColor("_ColorB", purpleGlass);
            glassMat.SetFloat("_GradientOffset", 0f);
            glassMat.SetFloat("_GradientAngle", -10f);
            glassMat.SetFloat("_CyanRatio", 0.7f);
            glassMat.SetFloat("_GlassAlpha", 0.08f);
            glassMat.SetFloat("_FresnelPower", 2.2f);
            glassMat.SetFloat("_FresnelStrength", 0.12f);

            img.material = glassMat;
            img.color = Color.white;
        }
        else
        {
            img.color = glassColor;
            expansion = 0;
        }

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(-expansion, -expansion);
        rt.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localPosition = Vector3.zero;
        rt.SetAsFirstSibling();

        // Collider uses logical pixels (will be scaled by Canvas localScale to match physical size)
        float expandedW = w * (1f + expansion);
        float expandedH = h * (1f + expansion);
        BoxCollider bgCol = bgObj.AddComponent<BoxCollider>();
        bgCol.size = new Vector3(expandedW, expandedH, 0.01f); // thin collider
        bgCol.center = new Vector3(0, 0, -0.01f);

        // Set layer to VirtualObjects for VRGazeReticle raycast
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) bgObj.layer = vrLayer;

        CreateGlowingBorder(bgObj.transform, w, h);
        CreateFloatingDataEffects(bgObj.transform, w, h);
    }

    void CreateGlowingBorder(Transform parent, float w, float h)
    {
        GameObject borderObj = new GameObject("GlowingBorder");
        borderObj.transform.SetParent(parent, false);

        RectTransform rt = borderObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;

        float aspect = w / h;

        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);

            glowMat.SetFloat("_BorderWidth", 0.02f);
            glowMat.SetFloat("_CornerRadius", 0.12f);
            glowMat.SetFloat("_EdgePadding", edgePadding);
            glowMat.SetFloat("_Aspect", aspect);

            glowMat.SetFloat("_Layer1Width", 0.008f);
            glowMat.SetFloat("_Layer1Alpha", 1.5f);
            glowMat.SetFloat("_Layer2Width", 0.018f);
            glowMat.SetFloat("_Layer2Alpha", 1.0f);
            glowMat.SetFloat("_Layer3Width", 0.04f);
            glowMat.SetFloat("_Layer3Alpha", 0.6f);
            glowMat.SetFloat("_Layer4Width", 0.08f);
            glowMat.SetFloat("_Layer4Alpha", 0.3f);

            Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
            Color purpleColor = new Color(1f, 0.4f, 1f, 1f);
            glowMat.SetColor("_ColorA", cyanColor);
            glowMat.SetColor("_ColorB", purpleColor);
            glowMat.SetFloat("_GradientMode", 2f);
            glowMat.SetFloat("_GradientAngle", -10f);
            glowMat.SetFloat("_GlassAlpha", 0.02f);
            glowMat.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
            glowMat.SetFloat("_ShimmerSpeed", 0.1f);  // Slow, constant speed
            glowMat.SetFloat("_ShimmerIntensity", 0.2f);
            glowMat.SetFloat("_LightSize", 0.008f);   // Same as border width
            glowMat.SetFloat("_LightGlow", 0.008f);

            borderImg.material = glowMat;
            // borderImg.color = Color.white;
            borderImg.sprite = GetPixelSprite();
        }
        else
        {
            Debug.LogWarning("[VRMenuFrame] GlowingGlassBorder shader not found, using fallback.");
            CreateBorderFallback(parent);
        }

        borderObj.transform.SetAsLastSibling();
    }

    void CreateBorderFallback(Transform parent)
    {
        CreateBorder(parent, 12, new Color(0.6f, 0.9f, 1.0f, 0.9f), 0);
        CreateBorder(parent, 24, new Color(0.0f, 0.5f, 1.0f, 0.15f), 1, new Vector2(-6, -6), new Vector2(6, 6));
    }

    void CreateBorder(Transform parent, int thickness, Color col, int siblingIndex, Vector2 offMin = default, Vector2 offMax = default)
    {
        GameObject borderObj = new GameObject($"PanelBorder_{thickness}");
        borderObj.transform.SetParent(parent, false);
        if (siblingIndex >= 0) borderObj.transform.SetSiblingIndex(siblingIndex);

        RectTransform rt = borderObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;

        Image img = borderObj.AddComponent<Image>();
        img.sprite = GetBorderSprite(thickness);
        img.type = Image.Type.Sliced;
        img.color = col;
        img.raycastTarget = false;

        var glow = borderObj.AddComponent<Shadow>();
        glow.effectColor = new Color(col.r, col.g, col.b, 0.6f);
        glow.effectDistance = Vector2.zero;
    }

    void CreateFloatingDataEffects(Transform parent, float w, float h)
    {
        GameObject fxContainer = new GameObject("FX_DataStream");
        fxContainer.transform.SetParent(parent, false);
        RectTransform rt = fxContainer.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        fxContainer.AddComponent<RectMask2D>();

        int particleCount = 20;
        for (int i = 0; i < particleCount; i++)
        {
            GameObject p = new GameObject($"Bit_{i}");
            p.transform.SetParent(fxContainer.transform, false);

            Image pImg = p.AddComponent<Image>();
            pImg.sprite = GetPixelSprite();

            bool cyanOrPurple = Random.value > 0.5f;
            Color baseCol = cyanOrPurple ? Color.cyan : new Color(0.8f, 0f, 1f);
            pImg.color = new Color(baseCol.r, baseCol.g, baseCol.b, Random.Range(0.1f, 0.4f));

            RectTransform pRT = p.GetComponent<RectTransform>();
            float size = Random.Range(10f, 60f); // logical pixels
            pRT.sizeDelta = new Vector2(size, size * Random.Range(0.2f, 1.0f));

            float startX = Random.Range(-w / 2f, w / 2f);
            float startY = Random.Range(-h / 2f, h / 2f);
            pRT.anchoredPosition = new Vector2(startX, startY);

            var anim = p.AddComponent<FloatingDataAnim>();
            anim.speed = Random.Range(10f, 40f); // logical pixels/second
            anim.range = new Vector2(w, h);
        }
    }

    // --- ASSET LOADERS ---

    void LoadIcons()
    {
        if (iconSignal == null) iconSignal = Resources.Load<Sprite>("MainMenu/icon_signal");
        if (iconSignal == null) iconSignal = GetSignalSprite();

        if (iconWifi == null) iconWifi = Resources.Load<Sprite>("MainMenu/icon_wifi");
        if (iconWifi == null) iconWifi = GetWifiSprite();

        if (iconBattery == null) iconBattery = Resources.Load<Sprite>("MainMenu/icon_battery");
    }

    Sprite GetRoundedSprite()
    {
        if (_roundedSprite != null) return _roundedSprite;
        int size = 512;
        int radius = 80;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inCorner = (x < radius && y < radius) || (x > size - radius && y < radius) ||
                                (x < radius && y > size - radius) || (x > size - radius && y > size - radius);

                if (inCorner)
                {
                    float cx = (x < size / 2) ? radius : size - radius - 1;
                    float cy = (y < size / 2) ? radius : size - radius - 1;
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    float alpha = Mathf.Clamp01((radius + 0.5f) - d);
                    colors[y * size + x] = new Color(1, 1, 1, alpha);
                }
                else colors[y * size + x] = Color.white;
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        _roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return _roundedSprite;
    }

    Sprite GetBorderSprite(int thickness)
    {
        if (_borderSprites.ContainsKey(thickness) && _borderSprites[thickness] != null)
            return _borderSprites[thickness];

        int size = 512;
        int radius = 80;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inCorner = (x < radius && y < radius) || (x > size - radius && y < radius) ||
                                (x < radius && y > size - radius) || (x > size - radius && y > size - radius);

                float alpha = 0f;
                if (inCorner)
                {
                    float cx = (x < size / 2) ? radius : size - radius - 1;
                    float cy = (y < size / 2) ? radius : size - radius - 1;
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));

                    float outerAlpha = Mathf.Clamp01((radius + 0.5f) - d);
                    float innerEdge = radius - thickness;
                    float innerAlpha = Mathf.Clamp01(d - (innerEdge - 0.5f));
                    alpha = outerAlpha * innerAlpha;
                }
                else
                {
                    float dx = Mathf.Min(x, size - 1 - x);
                    float dy = Mathf.Min(y, size - 1 - y);
                    float minDist = Mathf.Min(dx, dy);
                    if (minDist < thickness + 1) alpha = Mathf.Clamp01((thickness + 0.5f) - minDist);
                }
                colors[y * size + x] = new Color(1, 1, 1, alpha);
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        _borderSprites[thickness] = s;
        return s;
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

    Sprite GetBatterySprite()
    {
        if (_batterySprite != null) return _batterySprite;
        int w = 128;
        int h = 64;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] colors = new Color[w * h];
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
        _batterySprite = Sprite.Create(tex, new Rect(0, 0, bodyW + nubW, h), new Vector2(0.5f, 0.5f), 100, 1, SpriteMeshType.Tight);
        return _batterySprite;
    }

    Sprite GetSignalSprite()
    {
        if (_signalSprite != null) return _signalSprite;
        int w = 64;
        int h = 64;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] fill = new Color[w * h];
        for (int i = 0; i < fill.Length; i++) fill[i] = Color.clear;
        for (int i = 0; i < 4; i++)
        {
            int barH = (int)((i + 1) / 4f * h);
            int barW = 10;
            int xOffset = 4 + i * 14;
            for (int y = 0; y < barH; y++)
                for (int x = 0; x < barW; x++)
                    fill[y * w + (x + xOffset)] = Color.white;
        }
        tex.SetPixels(fill);
        tex.Apply();
        _signalSprite = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.one * 0.5f);
        return _signalSprite;
    }

    Sprite _wifiSprite;
    Sprite GetWifiSprite()
    {
        if (_wifiSprite != null) return _wifiSprite;
        int size = 72;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        Vector2 center = new Vector2(size / 2, 4);
        float maxRadius = size * 0.85f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                bool colored = false;

                if (d < size * 0.12f) colored = true;

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
        _wifiSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
        return _wifiSprite;
    }

    GameObject CreateText(Transform parent, string content, Vector2 pos, float fontSize, Color c, bool bold = false)
    {
        var go = new GameObject("TextTMP");
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<TextMeshProUGUI>();
        if (customFont != null) txt.font = customFont;
        txt.text = content;
        txt.fontSize = fontSize;
        txt.color = c;
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        txt.raycastTarget = false;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(400f, 100f); // logical pixels
        return go;
    }

    Sprite _recenterSprite;
    Sprite GetRecenterSprite()
    {
        if (_recenterSprite != null) return _recenterSprite;
        string resPath = "MainMenu/recenter_icon";
        _recenterSprite = Resources.Load<Sprite>(resPath);

#if UNITY_EDITOR
        if (_recenterSprite == null)
        {
            string fullPath = "Assets/VR-Workspace/Resources/MainMenu/recenter_icon.png";
            var importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
            if (importer != null)
            {
                bool changed = false;
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    _recenterSprite = Resources.Load<Sprite>(resPath);
                    Debug.Log($"[VRMenuFrame] Auto-fixed Texture settings for {fullPath}");
                }
            }
        }
#endif

        if (_recenterSprite == null)
        {
            Debug.LogWarning($"Could not find '{resPath}' in Resources. Ensure file exists and is set to Sprite.");
        }
        return _recenterSprite;
    }

    void CreateStatusBar(Transform parent, float w, float h, float statusBarHeight)
    {
        GameObject barObj = new GameObject("StatusBar");
        barObj.transform.SetParent(parent, false);
        RectTransform rt = barObj.AddComponent<RectTransform>();

        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0, 0);
        rt.sizeDelta = new Vector2(0, statusBarHeight);
        rt.anchoredPosition = new Vector2(0, -statusBarHeight / 2f);

        // --- LEFT GROUP (Clock + Recenter) ---
        GameObject leftGroup = new GameObject("LeftGroup");
        leftGroup.transform.SetParent(barObj.transform, false);
        RectTransform leftRT = leftGroup.AddComponent<RectTransform>();
        leftRT.anchorMin = new Vector2(0, 0);
        leftRT.anchorMax = new Vector2(0.5f, 1);
        leftRT.pivot = new Vector2(0, 0.5f);
        leftRT.offsetMin = new Vector2(sidePadding, 0);
        leftRT.offsetMax = new Vector2(0, 0);

        // Clock
        float clockFontSize = 42f;
        GameObject timeObj = CreateText(leftGroup.transform, "12:00", Vector2.zero, clockFontSize, new Color(1f, 1f, 1f, 0.9f), true);
        RectTransform timeRT = timeObj.GetComponent<RectTransform>();
        timeRT.sizeDelta = new Vector2(120f, statusBarHeight);
        timeRT.anchorMin = new Vector2(0, 0.5f);
        timeRT.anchorMax = new Vector2(0, 0.5f);
        timeRT.pivot = new Vector2(0, 0.5f);
        timeRT.anchoredPosition = Vector2.zero;

        _clockText = timeObj.GetComponent<TextMeshProUGUI>();
        _clockText.alignment = TextAlignmentOptions.MidlineLeft;

        // Recenter Button
        float btnSize = 72f;
        CreateRecenterButton(leftGroup.transform, btnSize, timeRT.rect.width);

        // --- STATUS GROUP (Right) ---
        GameObject statusGroup = new GameObject("StatusGroup");
        statusGroup.transform.SetParent(barObj.transform, false);
        RectTransform groupRT = statusGroup.AddComponent<RectTransform>();
        groupRT.anchorMin = new Vector2(1, 0);
        groupRT.anchorMax = new Vector2(1, 1);
        groupRT.pivot = new Vector2(1, 0.5f);
        groupRT.sizeDelta = new Vector2(300f, 0);
        groupRT.anchoredPosition = new Vector2(0, 0);

        // Battery Container
        float battWidth = CreateBatteryIndicator(statusGroup.transform);

        // Network Icon
        GameObject netObj = new GameObject("NetworkIcon");
        netObj.transform.SetParent(statusGroup.transform, false);
        _networkIcon = netObj.AddComponent<Image>();
        _networkIcon.sprite = iconWifi;
        _networkIcon.preserveAspect = true;
        RectTransform netRT = netObj.GetComponent<RectTransform>();
        netRT.anchorMin = new Vector2(1, 0.5f);
        netRT.anchorMax = new Vector2(1, 0.5f);
        netRT.pivot = new Vector2(1, 0.5f);
        netRT.sizeDelta = new Vector2(60f, 60f);
        netRT.anchoredPosition = new Vector2(-battWidth - 30f, 0);

        // --- SEPARATOR LINE ---
        CreateSeparator(barObj.transform, w, statusBarHeight);
    }

    void CreateRecenterButton(Transform parent, float size, float clockWidth)
    {
        // Use VRButtonFactory to create icon button
        var config = new VRButtonFactory.ButtonConfig
        {
            label = "Recenter",
            icon = GetRecenterSprite(),
            themeColor = glowColorB,
            width = size,
            height = size,
            iconOnly = true,
            iconSize = size * 0.45f,
            borderWidth = 0.025f,
            cornerRadius = 0.15f,
            popAmount = 0.0125f
        };

        GameObject btn = VRButtonFactory.CreateButton(parent, config, RecenterObject);

        // Rename and position
        btn.name = "RecenterBtn";
        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(0, 0.5f);
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(clockWidth + 30f, 0);
    }

    float CreateBatteryIndicator(Transform parent)
    {
        float battWidth = 100f;
        float battHeight = 50f;

        GameObject battContainer = new GameObject("BatteryContainer");
        battContainer.transform.SetParent(parent, false);
        RectTransform battRT = battContainer.AddComponent<RectTransform>();
        battRT.anchorMin = new Vector2(1, 0.5f);
        battRT.anchorMax = new Vector2(1, 0.5f);
        battRT.pivot = new Vector2(1, 0.5f);
        battRT.sizeDelta = new Vector2(battWidth, battHeight);
        battRT.anchoredPosition = new Vector2(0, 0);

        Sprite batSprite = GetBatterySprite();

        // Bg
        GameObject bgObj = new GameObject("Bg");
        bgObj.transform.SetParent(battContainer.transform, false);
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.sprite = batSprite;
        bgImg.color = new Color(0.8f, 0.8f, 0.8f, 0.5f);
        bgImg.preserveAspect = true;
        RectTransform bgRT = bgObj.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;

        // Fill
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(battContainer.transform, false);
        _batteryFillImage = fillObj.AddComponent<Image>();
        _batteryFillImage.sprite = batSprite;
        _batteryFillImage.color = Color.white;
        _batteryFillImage.type = Image.Type.Filled;
        _batteryFillImage.fillMethod = Image.FillMethod.Horizontal;
        _batteryFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        _batteryFillImage.preserveAspect = true;
        RectTransform fillRT = fillObj.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;

        // Text
        float battFontSize = 30f;
        GameObject battTxtObj = CreateText(battContainer.transform, "100", Vector2.zero, battFontSize, new Color(0.1f, 0.15f, 0.2f, 1f), true);
        RectTransform btRT = battTxtObj.GetComponent<RectTransform>();
        btRT.anchorMin = Vector2.zero;
        btRT.anchorMax = Vector2.one;
        btRT.sizeDelta = Vector2.zero;
        btRT.offsetMin = new Vector2(0, 0);
        btRT.offsetMax = new Vector2(-8f, 0);

        _batteryText = battTxtObj.GetComponent<TextMeshProUGUI>();
        _batteryText.alignment = TextAlignmentOptions.Center;
        _batteryText.fontStyle = FontStyles.Bold;

        return battRT.rect.width;
    }

    void CreateSeparator(Transform parent, float w, float yPos)
    {
        GameObject lineObj = new GameObject("SeparatorLine");
        lineObj.transform.SetParent(parent, false);
        RectTransform rt = lineObj.AddComponent<RectTransform>();

        rt.anchorMin = new Vector2(0.5f, 0);
        rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(w, parent.GetComponent<RectTransform>().sizeDelta.y * 0.03f);
        rt.anchoredPosition = new Vector2(0, 0);

        Image img = lineObj.AddComponent<Image>();
        img.sprite = GetGradientLineSprite();
        img.raycastTarget = false;

        Shadow s = lineObj.AddComponent<Shadow>();
        s.effectColor = new Color(0.5f, 0f, 1f, 0.5f);
        s.effectDistance = new Vector2(0, -1f);
    }

    Sprite _gradientLineSprite;
    Sprite GetGradientLineSprite()
    {
        if (_gradientLineSprite != null) return _gradientLineSprite;

        int w = 256;
        int h = 2;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] fill = new Color[w * h];

        Color c1 = Color.cyan;
        Color c2 = new Color(0.8f, 0f, 1f);

        for (int x = 0; x < w; x++)
        {
            float t = (float)x / (w - 1);
            Color col = Color.Lerp(c1, c2, Mathf.Pow(t, 3.0f));
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

        _gradientLineSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        return _gradientLineSprite;
    }

    // --- RECENTER ---

    public void RecenterObject()
    {
        StartCoroutine(RecenterRoutine());
    }

    System.Collections.IEnumerator RecenterRoutine()
    {
        VRGazeReticle reticle = VRGazeReticle.Instance;
        if (reticle == null) reticle = FindObjectOfType<VRGazeReticle>();

        if (reticle != null)
        {
            reticle.EnterRecenterMode(GetRecenterSprite());
        }

        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / duration);

            if (reticle != null) reticle.UpdateRecenterProgress(p);

            yield return null;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            PerformRecenterLogic(cam);
        }

        if (reticle != null)
        {
            reticle.ExitRecenterMode();
        }
    }

    void PerformRecenterLogic(Camera cam)
    {
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 currentPos = transform.position;
        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(new Vector2(currentPos.x, currentPos.z), new Vector2(camPos.x, camPos.z));

        Vector3 newPos = camPos + camForward * hDist;
        newPos.y = currentPos.y;

        transform.position = newPos;
        transform.rotation = Quaternion.LookRotation(camForward);

        Debug.Log("[VRMenuFrame] Recenter complete.");
    }
}
