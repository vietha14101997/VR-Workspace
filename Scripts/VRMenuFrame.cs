using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class VRMenuFrame : MonoBehaviour
{
    [Header("Panel Size")]
    public float panelWidth = 1.6f;
    public float panelHeight = 0.9f;
    public float logicalWidth = 1920f;

    [Header("Auto Build")]
    [Tooltip("Automatically build frame on Start()")]
    public bool autoBuild = true;

    [Header("Frame Configuration")]
    public float separatorOffset = 80f; // Distance from top edge to separator line
    public float sidePadding = 0f;

    [Header("Visual Config")]
    public Color glassColor = new Color(1.0f, 1.0f, 1.0f, 0.09803921568f);
    public Color panelBorderColor = new Color(1.0f, 1.0f, 1.0f, 0.39215686274f);

    [Header("Glowing Border Config")]
    [ColorUsage(true, true)]
    public Color glowColorA = new Color(0f, 1.5f, 2f, 1f); // Cyan HDR
    [ColorUsage(true, true)]
    public Color glowColorB = new Color(1.2f, 0.3f, 2f, 1f); // Purple HDR
    public float glowIntensity = 1.2f; // Slightly reduced
    public float borderThickness = 3f; // Reduced from 8f to 3f for thinner look
    public float edgePadding = 0.04f; // Added padding property
    public float glowSpread = 40f; // Reduced spread for sharper look
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

    // Public Access
    public RectTransform ContentContainer { get; private set; }
    public Canvas Canvas { get; private set; }
    public RectTransform CanvasRect { get; private set; }

#if UNITY_EDITOR
    void OnEnable()
    {
        // Update materials in Editor mode when component is enabled
        if (!Application.isPlaying)
        {
            UpdateMaterialAspectRatiosEditor();
        }
    }
    void OnValidate()
    {
        // Update materials when properties change in Editor
        if (!Application.isPlaying)
        {
            // Delay to avoid issues during serialization
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    UpdateMaterialAspectRatiosEditor();
                }
            };
        }
    }

    /// <summary>
    /// Update materials in Editor mode (modifies asset directly)
    /// </summary>
    void UpdateMaterialAspectRatiosEditor()
    {
        float logicalHeight = (logicalWidth / panelWidth) * panelHeight;
        float aspect = logicalWidth / logicalHeight;

        var glassBg = transform.Find("MenuCanvas/GlassBackground");
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

        // Check for existing MenuCanvas from Editor/Prefab
        if (Canvas == null)
        {
            TryInitializeExistingCanvas();
        }

        // If still no canvas and autoBuild is enabled, create new one
        if (autoBuild && Canvas == null)
        {
            Build();
        }
    }

    /// <summary>
    /// Initialize references from existing MenuCanvas (from prefab/editor).
    /// Also updates material aspect ratios to fix distortion issues.
    /// </summary>
    void TryInitializeExistingCanvas()
    {
        Transform existingCanvas = transform.Find("MenuCanvas");
        if (existingCanvas == null) return;

        Canvas = existingCanvas.GetComponent<Canvas>();
        if (Canvas == null) return;

        CanvasRect = existingCanvas.GetComponent<RectTransform>();

        // Find ContentContainer
        Transform contentTransform = existingCanvas.Find("ContentContainer");
        if (contentTransform != null)
        {
            ContentContainer = contentTransform.GetComponent<RectTransform>();
        }

        // Update material aspect ratios to fix border distortion
        UpdateMaterialAspectRatios();

        // Setup status bar references
        SetupStatusBarReferences(existingCanvas);

        Debug.Log("[VRMenuFrame] Initialized from existing MenuCanvas");
    }

    /// <summary>
    /// Update aspect ratio in materials to match current panel dimensions.
    /// This fixes border distortion when using prefab-saved materials.
    /// </summary>
    void UpdateMaterialAspectRatios()
    {
        float logicalHeight = (logicalWidth / panelWidth) * panelHeight;
        float aspect = logicalWidth / logicalHeight;

        // Update GlassBackground material
        var glassBg = transform.Find("MenuCanvas/GlassBackground");
        if (glassBg != null)
        {
            var img = glassBg.GetComponent<Image>();
            if (img != null && img.material != null)
            {
                // Clone material to avoid modifying shared asset
                img.material = new Material(img.material);
                img.material.SetFloat("_Aspect", aspect);
            }

            // Update GlowingBorder material
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

    /// <summary>
    /// Setup references to status bar elements for runtime updates
    /// </summary>
    void SetupStatusBarReferences(Transform canvasTransform)
    {
        Transform statusBar = canvasTransform.Find("StatusBar");
        if (statusBar == null) return;

        // Find clock text in LeftGroup
        Transform leftGroup = statusBar.Find("LeftGroup");
        if (leftGroup != null)
        {
            TextMeshProUGUI[] texts = leftGroup.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0)
            {
                _clockText = texts[0];
            }
        }

        // Find network icon and battery in StatusGroup
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
        // Clean up existing
        if (Canvas != null)
        {
            if (Application.isPlaying)
                Destroy(Canvas.gameObject);
            else
                DestroyImmediate(Canvas.gameObject);
        }

        var existingCanvas = transform.Find("MenuCanvas");
        if (existingCanvas)
        {
            if (Application.isPlaying)
                Destroy(existingCanvas.gameObject);
            else
                DestroyImmediate(existingCanvas.gameObject);
        }

        Build();
    }

#if UNITY_EDITOR
    [ContextMenu("Build And Save As Prefab")]
    public void BuildAndSaveAsPrefab()
    {
        // 1. Clean up existing
        var existingCanvas = transform.Find("MenuCanvas");
        if (existingCanvas)
        {
            DestroyImmediate(existingCanvas.gameObject);
        }
        Canvas = null;

        // 2. Build
        LoadIcons();
        Build();

        // 3. Save materials as assets and replace runtime materials
        SaveMaterialsAsAssets();

        // 4. Save as prefab
        string prefabDir = "Assets/VR-Workspace/Prefabs/UI";
        if (!AssetDatabase.IsValidFolder(prefabDir))
        {
            AssetDatabase.CreateFolder("Assets/VR-Workspace/Prefabs", "UI");
        }

        string prefabPath = $"{prefabDir}/VRMenuFrame.prefab";

        // Unpack if this is already a prefab instance
        if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(gameObject))
        {
            UnityEditor.PrefabUtility.UnpackPrefabInstance(gameObject, UnityEditor.PrefabUnpackMode.Completely, UnityEditor.InteractionMode.AutomatedAction);
        }

        UnityEditor.PrefabUtility.SaveAsPrefabAssetAndConnect(gameObject, prefabPath, UnityEditor.InteractionMode.UserAction);
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

        float logicalHeight = (logicalWidth / panelWidth) * panelHeight;
        float w = logicalWidth;
        float h = logicalHeight;

        // Find and save GlassBackground material
        var glassBg = transform.Find("MenuCanvas/GlassBackground");
        if (glassBg != null)
        {
            var img = glassBg.GetComponent<Image>();
            if (img != null && img.material != null)
            {
                var savedMat = SaveOrGetMaterial(img.material, "FrameGlassBackground", matDir);
                if (savedMat != null)
                {
                    // Update aspect ratio in case dimensions changed
                    savedMat.SetFloat("_Aspect", w / h);
                    EditorUtility.SetDirty(savedMat);
                    img.material = savedMat;
                }
            }

            // Find and save GlowingBorder material
            var glowBorder = glassBg.Find("GlowingBorder");
            if (glowBorder != null)
            {
                var borderImg = glowBorder.GetComponent<Image>();
                if (borderImg != null && borderImg.material != null)
                {
                    var savedMat = SaveOrGetMaterial(borderImg.material, "FrameGlowingBorder", matDir);
                    if (savedMat != null)
                    {
                        savedMat.SetFloat("_Aspect", w / h);
                        EditorUtility.SetDirty(savedMat);
                        borderImg.material = savedMat;
                    }
                }
            }
        }

        // Find and save RecenterBtn materials
        var recenterBtn = transform.Find("MenuCanvas/StatusBar/LeftGroup/RecenterBtn");
        if (recenterBtn != null)
        {
            var visuals = recenterBtn.Find("Visuals");
            if (visuals != null)
            {
                // Background
                var bgImg = visuals.GetComponent<Image>();
                if (bgImg != null && bgImg.material != null)
                {
                    var savedMat = SaveOrGetMaterial(bgImg.material, "RecenterButtonBg", matDir);
                    if (savedMat != null) bgImg.material = savedMat;
                }

                // Border
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
                            // Update VRButtonRipple reference
                            var ripple = border.GetComponent<VRButtonRipple>();
                            if (ripple != null) ripple.Initialize(savedMat, borderImg);
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

        // Check if already exists
        var existingMat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existingMat != null)
        {
            // Copy properties from runtime material to existing asset
            existingMat.CopyPropertiesFromMaterial(runtimeMat);
            EditorUtility.SetDirty(existingMat);
            return existingMat;
        }

        // Create new asset from runtime material
        var newMat = new Material(runtimeMat);
        AssetDatabase.CreateAsset(newMat, path);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }
#endif

    /// <summary>
    /// Build the complete menu frame hierarchy using configured panel size.
    /// MenuCanvas > GlassBackground > StatusBar > SeparatorLine > ContentContainer
    /// </summary>
    public void Build()
    {
        Build(panelWidth, panelHeight, logicalWidth);
    }

    /// <summary>
    /// Build the complete menu frame hierarchy with custom dimensions.
    /// MenuCanvas > GlassBackground > StatusBar > SeparatorLine > ContentContainer
    /// </summary>
    public void Build(float width, float height, float logicWidth = 1920f)
    {
        // Store dimensions
        panelWidth = width;
        panelHeight = height;
        logicalWidth = logicWidth;

        float logicalHeight = (logicalWidth / panelWidth) * panelHeight;

        // 1. Create MenuCanvas
        GameObject canvasGO = new GameObject("MenuCanvas");
        canvasGO.transform.SetParent(transform, false);

        Canvas = canvasGO.AddComponent<Canvas>();
        Canvas.renderMode = RenderMode.WorldSpace;

        CanvasRect = canvasGO.GetComponent<RectTransform>();
        CanvasRect.sizeDelta = new Vector2(logicalWidth, logicalHeight);
        float scaleFactor = panelWidth / logicalWidth;
        CanvasRect.localScale = new Vector3(scaleFactor, scaleFactor, 1f);
        CanvasRect.localPosition = Vector3.zero;

        canvasGO.AddComponent<GraphicRaycaster>();

        // 2. Create Glass Background & Borders
        CreateGlassPanel(CanvasRect, logicalWidth, logicalHeight);

        // 3. Create Status Bar
        CreateStatusBar(CanvasRect, logicalWidth, logicalHeight, separatorOffset);

        // 4. Create Content Container
        GameObject contentObj = new GameObject("ContentContainer");
        contentObj.transform.SetParent(CanvasRect, false);
        ContentContainer = contentObj.AddComponent<RectTransform>();

        // Fill the space BELOW separator line
        ContentContainer.anchorMin = Vector2.zero;
        ContentContainer.anchorMax = Vector2.one;
        ContentContainer.offsetMin = Vector2.zero;
        ContentContainer.offsetMax = new Vector2(0, -separatorOffset);
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
                // _batteryFillImage.color = (displayLevel < 0.2f) ? new Color(1f, 0.3f, 0.3f) : Color.white; 
                // Keeping it white as per design request
                _batteryFillImage.color = Color.white;
            }
        }
    }

    // --- CREATION HELPERS ---

    void CreateGlassPanel(Transform parent, float w, float h)
    {
        // 1. Glass Background Layer with Gradient
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);
        Image img = bgObj.AddComponent<Image>();
        
        img.type = Image.Type.Simple;
        img.sprite = GetPixelSprite(); // Use pixel sprite, shader handles corners
        
        // Calculate Expansion to compensate for Shader Padding AND Inner Glow overlap
        // We add an extra buffer to push the visual border completely outside the logical area
        float p = edgePadding;
        float safeZone = 0.06f; // Increased buffer for safety
        float effectiveP = p + safeZone;
        float expansion = effectiveP / (1f - 2f * effectiveP);
        
        // Use new GlassGradientBackground shader with rounded corners
        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            
            // Corner radius - MUST match border shader (increased for smoother curves)
            glassMat.SetFloat("_CornerRadius", 0.12f);
            glassMat.SetFloat("_EdgePadding", p); // Set padding
            
            // Fix Aspect Ratio for rounded corners
            float aspect = (h > 0) ? (w / h) : 1.0f;
            glassMat.SetFloat("_Aspect", aspect);
            
            // Gradient: Cyan left (70%), Purple right (30%), angled
            Color cyanGlass = new Color(0.35f, 0.9f, 1f, 0.15f);
            Color purpleGlass = new Color(0.75f, 0.45f, 1f, 0.22f);
            glassMat.SetColor("_ColorA", cyanGlass);
            glassMat.SetColor("_ColorB", purpleGlass);
            glassMat.SetFloat("_GradientOffset", 0f);
            glassMat.SetFloat("_GradientAngle", -10f);
            glassMat.SetFloat("_CyanRatio", 0.7f);
            
            // Glass effect + center glow
            glassMat.SetFloat("_GlassAlpha", 0.08f);
            glassMat.SetFloat("_FresnelPower", 2.2f);
            glassMat.SetFloat("_FresnelStrength", 0.12f);
            
            img.material = glassMat;
            img.color = Color.white;
        }
        else
        {
            // Fallback to simple color
            img.color = glassColor;
            expansion = 0;
        }

        BoxCollider bgCol = bgObj.AddComponent<BoxCollider>();
        bgCol.size = new Vector3(w, h, 0.1f);
        
        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(-expansion, -expansion); 
        rt.anchorMax = new Vector2(1f + expansion, 1f + expansion); 
        rt.sizeDelta = Vector2.zero; 
        rt.localScale = Vector3.one;
        rt.localPosition = Vector3.zero;
        rt.SetAsFirstSibling();
        
        // 2. Glowing Border Layer (using new shader)
        CreateGlowingBorder(bgObj.transform, w, h);
        
        // 3. FX
        CreateFloatingDataEffects(bgObj.transform, w, h);
    }

    void CreateGlowingBorder(Transform parent, float w, float h)
    {
        GameObject borderObj = new GameObject("GlowingBorder");
        borderObj.transform.SetParent(parent, false);
        
        RectTransform rt = borderObj.AddComponent<RectTransform>();
        // Match exactly the parent size - shader will handle the glow overflow
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        
        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;
        
        // Try to use Glowing Glass Border shader
        Shader glowShader = Shader.Find("Custom/GlowingGlassBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);
            
            // UV-based border settings - Match glass panel corner EXACTLY
            glowMat.SetFloat("_BorderWidth", 0.02f);
            glowMat.SetFloat("_CornerRadius", 0.12f);  // MUST match GlassGradientBackground
            glowMat.SetFloat("_EdgePadding", edgePadding); // Set padding
            
            // Aspect Ratio Correction
            float aspect = (h > 0) ? (w / h) : 1.0f;
            glowMat.SetFloat("_Aspect", aspect);
            
            // Multi-layer glow - STRONG values for visible layers
            glowMat.SetFloat("_Layer1Width", 0.008f); // Very thin inner core
            glowMat.SetFloat("_Layer1Alpha", 1.5f);   
            glowMat.SetFloat("_Layer2Width", 0.018f); // Thin mid glow
            glowMat.SetFloat("_Layer2Alpha", 1.0f);   
            glowMat.SetFloat("_Layer3Width", 0.04f); // Reduced outer
            glowMat.SetFloat("_Layer3Alpha", 0.6f);   
            glowMat.SetFloat("_Layer4Width", 0.08f); // Reduced ambient
            glowMat.SetFloat("_Layer4Alpha", 0.3f);
            
            // Gradient colors - BRIGHT Cyan to Purple
            Color cyanColor = new Color(0.3f, 1f, 1f, 1f);    // Bright cyan
            Color purpleColor = new Color(1f, 0.4f, 1f, 1f);  // Bright purple
            glowMat.SetColor("_ColorA", cyanColor);
            glowMat.SetColor("_ColorB", purpleColor);
            glowMat.SetFloat("_GradientMode", 2f); // Diagonal
            glowMat.SetFloat("_GradientAngle", -10f); // Match glass background
            
            // Glass background
            glowMat.SetFloat("_GlassAlpha", 0.02f);
            glowMat.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
            
            // Animation
            glowMat.SetFloat("_ShimmerSpeed", shimmerSpeed);
            glowMat.SetFloat("_ShimmerIntensity", 0.15f);
            
            borderImg.material = glowMat;
            borderImg.color = Color.white;
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
        // Original border style as fallback
        CreateBorder(parent, 12, new Color(0.6f, 0.9f, 1.0f, 0.9f), 0);
        CreateBorder(parent, 24, new Color(0.0f, 0.5f, 1.0f, 0.15f), 1, new Vector2(-6, -6), new Vector2(6, 6));
    }

    void CreateBorder(Transform parent, int thickness, Color col, int siblingIndex, Vector2 offMin = default, Vector2 offMax = default)
    {
        GameObject borderObj = new GameObject($"PanelBorder_{thickness}");
        borderObj.transform.SetParent(parent, false);
        if(siblingIndex >= 0) borderObj.transform.SetSiblingIndex(siblingIndex);
        
        RectTransform rt = borderObj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        
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
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
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
            float size = Random.Range(10f, 60f);
            pRT.sizeDelta = new Vector2(size, size * Random.Range(0.2f, 1.0f)); 
            
            float startX = Random.Range(-w/2f, w/2f);
            float startY = Random.Range(-h/2f, h/2f);
            pRT.anchoredPosition = new Vector2(startX, startY);

            var anim = p.AddComponent<FloatingDataAnim>();
            anim.speed = Random.Range(10f, 40f);
            anim.range = new Vector2(w, h);
        }
    }



    // --- ASSET LOADERS ---
    
    void LoadIcons()
    {
        if (iconSignal == null) iconSignal = Resources.Load<Sprite>("MainMenu/icon_signal");
        if (iconSignal == null) iconSignal = GetSignalSprite(); // Fallback

        if (iconWifi == null) iconWifi = Resources.Load<Sprite>("MainMenu/icon_wifi");
        if (iconWifi == null) iconWifi = GetWifiSprite(); // Fallback

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
        int w = 128; int h = 64; 
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] colors = new Color[w * h];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;

        int bodyW = 110; 
        int radius = 16;  // Smoother corners as requested
        int nubW = 8;    
        int nubH = 24;    
        int nubRadius = 4; // Rounded nub corners
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
                        
                        float dx = Mathf.Min(nx, nubW - 1 - nx); // Though only right side is really visible
                        float dy = Mathf.Min(ny, nubH - 1 - ny);
                        
                        // Round right-side corners of nub
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
        int w = 64; int h = 64;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color[] fill = new Color[w*h];
        for(int i=0; i<fill.Length; i++) fill[i] = Color.clear;
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
        _signalSprite = Sprite.Create(tex, new Rect(0,0,w,h), Vector2.one * 0.5f);
        return _signalSprite;
    }

    Sprite _wifiSprite;
    Sprite GetWifiSprite()
    {
        if (_wifiSprite != null) return _wifiSprite;
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        Vector2 center = new Vector2(size/2, 4); // Bottom center, slightly raised
        float maxRadius = size * 0.85f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                bool colored = false;

                // Dot
                if (d < size * 0.12f) colored = true;
                
                // Arcs
                // Draw 3 arcs
                for (int i = 1; i <= 3; i++)
                {
                    float r = maxRadius * (i / 3.0f);
                    float thickness = size * 0.08f; // Thicker lines
                    
                    if (Mathf.Abs(d - r) < thickness)
                    {
                        // Angle check for cone (approx 90 degrees)
                        Vector2 dir = (new Vector2(x, y) - center).normalized;
                        // dir.y is sin(angle). 0.707 is 45 deg from horizontal. 
                        // We want 45 to 135 deg.
                        if (dir.y > 0.6f) colored = true; 
                    }
                }

                colors[y * size + x] = colored ? Color.white : Color.clear;
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        _wifiSprite = Sprite.Create(tex, new Rect(0,0,size,size), Vector2.one * 0.5f);
        return _wifiSprite;
    }




    GameObject CreateText(Transform parent, string content, Vector2 pos, int size, Color c, bool bold = false)
    {
        var go = new GameObject("TextTMP");
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<TextMeshProUGUI>();
        if (customFont != null) txt.font = customFont;
        txt.text = content;
        txt.fontSize = size;
        txt.color = c;
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        txt.raycastTarget = false;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(400, 100);
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
            // Attempt to auto-fix import settings
            string fullPath = "Assets/VR-Workspace/Resources/MainMenu/recenter_icon.png";
            var importer = UnityEditor.AssetImporter.GetAtPath(fullPath) as UnityEditor.TextureImporter;
            if (importer != null)
            {
                bool changed = false;
                if (importer.textureType != UnityEditor.TextureImporterType.Sprite)
                {
                    importer.textureType = UnityEditor.TextureImporterType.Sprite;
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

    void CreateStatusBar(Transform parent, float w, float h, float separatorY)
    {
        GameObject barObj = new GameObject("StatusBar");
        barObj.transform.SetParent(parent, false);
        RectTransform rt = barObj.AddComponent<RectTransform>();

        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        
        // --- LEFT GROUP (Clock + Recenter) ---
        GameObject leftGroup = new GameObject("LeftGroup");
        leftGroup.transform.SetParent(barObj.transform, false);
        RectTransform leftRT = leftGroup.AddComponent<RectTransform>();
        leftRT.anchorMin = new Vector2(0, 0); 
        leftRT.anchorMax = new Vector2(0.5f, 1);
        leftRT.pivot = new Vector2(0, 0.5f);
        leftRT.offsetMin = new Vector2(sidePadding, 0); // Left padding
        leftRT.offsetMax = new Vector2(0, 0);
        
        HorizontalLayoutGroup lLayout = leftGroup.AddComponent<HorizontalLayoutGroup>();
        lLayout.childAlignment = TextAnchor.MiddleLeft; // Changed to MiddleLeft for vertical center alignment
        lLayout.spacing = -680f;
        lLayout.childControlWidth = false;
        lLayout.childControlHeight = false;
        lLayout.childForceExpandHeight = false;
        lLayout.padding = new RectOffset(0, 0, 5, 5); // Add small top/bottom padding for center alignment

        // 1. CLOCK
        GameObject timeObj = CreateText(leftGroup.transform, "12:00", Vector2.zero, 42, new Color(1f, 1f, 1f, 0.9f), true);
        RectTransform timeRT = timeObj.GetComponent<RectTransform>();
        ContentSizeFitter fitter = timeObj.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        
        _clockText = timeObj.GetComponent<TextMeshProUGUI>();
        _clockText.alignment = TextAlignmentOptions.MidlineLeft;

        // 2. RECENTER BUTTON (Hit Area)
        GameObject recenterBtn = new GameObject("RecenterBtn");
        recenterBtn.transform.SetParent(leftGroup.transform, false);
        RectTransform rRT = recenterBtn.AddComponent<RectTransform>();
        rRT.sizeDelta = new Vector2(73, 73); 

        // 3. VISUAL ROOT (Animated)
        GameObject visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(recenterBtn.transform, false);
        RectTransform visRT = visualRoot.AddComponent<RectTransform>();
        visRT.anchorMin = Vector2.zero; visRT.anchorMax = Vector2.one;
        visRT.sizeDelta = Vector2.zero;

        // A. Background (Glass)
        Image recenterBg = visualRoot.AddComponent<Image>();
        recenterBg.sprite = GetPixelSprite(); // Use pixel sprite for shader
        
        // Oversize expansion to hide edge artifacts
        float expansion = 0.12f; 
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = Vector2.zero; visRT.offsetMax = Vector2.zero;

        Color btnColor = Color.cyan;
        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", 0.15f); 
            glassMat.SetFloat("_EdgePadding", 0.12f); 
            glassMat.SetFloat("_Aspect", 1.0f); // Square button
            
            glassMat.SetColor("_ColorA", new Color(btnColor.r, btnColor.g, btnColor.b, 0.12f)); 
            glassMat.SetColor("_ColorB", new Color(btnColor.r, btnColor.g, btnColor.b, 0.04f)); 
            glassMat.SetFloat("_GlassAlpha", 0.075f); 
            
            recenterBg.material = glassMat;
            recenterBg.color = Color.white;
        }
        else
        {
            recenterBg.color = new Color(0f, 1f, 1f, 0.15f);
        }

        // B. Border
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(visualRoot.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero; borderRT.offsetMax = Vector2.zero;
        
        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;
        
        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);
            glowMat.SetFloat("_Aspect", 1.0f); 
            glowMat.SetFloat("_EdgePadding", 0.12f); 
            
            Color borderGlowCol = Color.Lerp(btnColor, Color.white, 0.75f);
            glowMat.SetColor("_GlowColor", borderGlowCol);
            
            glowMat.SetFloat("_BorderWidth", 0.05f); 
            glowMat.SetFloat("_GlowWidth", 0.04f); 
            glowMat.SetFloat("_GlowIntensity", 2.5f); 
            glowMat.SetFloat("_CornerRadius", 0.15f); 
            glowMat.SetFloat("_PulseEnabled", 0f);
             
            borderImg.material = glowMat;
            borderImg.sprite = GetPixelSprite();
            
            borderObj.AddComponent<VRButtonRipple>().Initialize(glowMat, borderImg);
        }

        // C. Icon -> Child of VisualRoot
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(visualRoot.transform, false);
        RectTransform iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = Vector2.zero; iconRT.anchorMax = Vector2.one;
        iconRT.sizeDelta = new Vector2(-52, -52); 
        
        Image iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = GetRecenterSprite();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;

        // Icon Glow & Whiteness (Sync with main buttons)
        iconImg.color = Color.Lerp(btnColor, Color.white, 0.9f);
        
        Color iconGlowCol = Color.Lerp(btnColor, Color.white, 0.7f);
        iconGlowCol.a = 0.4f;
        Shadow iGlow1 = iconObj.AddComponent<Shadow>();
        iGlow1.effectColor = iconGlowCol;
        iGlow1.effectDistance = new Vector2(2f, -2f);

        Shadow iGlow2 = iconObj.AddComponent<Shadow>();
        iGlow2.effectColor = iconGlowCol;
        iGlow2.effectDistance = new Vector2(-2f, 2f);

        Color iBloomCol = Color.Lerp(btnColor, Color.white, 0.8f);
        iBloomCol.a = 0.15f;
        Shadow iBloom1 = iconObj.AddComponent<Shadow>();
        iBloom1.effectColor = iBloomCol;
        iBloom1.effectDistance = new Vector2(4f, -4f);

        Shadow iBloom2 = iconObj.AddComponent<Shadow>();
        iBloom2.effectColor = iBloomCol;
        iBloom2.effectDistance = new Vector2(-4f, 4f);

        // D. Logic & Physics (On Hit Area)
        Button btn = recenterBtn.AddComponent<Button>();
        btn.targetGraphic = recenterBg; 
        btn.onClick.AddListener(RecenterObject);
        
        BoxCollider col = recenterBtn.AddComponent<BoxCollider>();
        col.size = new Vector3(73, 73, 0.1f);

        // E. Animation
        VRButtonAnimation anim = recenterBtn.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visualRoot.transform;
        anim.popAmount = 0.025f;

        // --- STATUS GROUP (Right) ---
        GameObject statusGroup = new GameObject("StatusGroup");
        statusGroup.transform.SetParent(barObj.transform, false);
        RectTransform groupRT = statusGroup.AddComponent<RectTransform>();
        groupRT.anchorMin = new Vector2(1, 0); 
        groupRT.anchorMax = new Vector2(1, 1);
        groupRT.pivot = new Vector2(1, 0.5f);
        groupRT.sizeDelta = new Vector2(400, 0); 
        groupRT.anchoredPosition = new Vector2(-sidePadding, 0);

        HorizontalLayoutGroup layout = statusGroup.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight; // Changed to MiddleRight for vertical center alignment
        layout.spacing = -175f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(0, 0, 5, 5); // Add small top/bottom padding for center alignment

        // 1. Network Icon
        GameObject netObj = new GameObject("NetworkIcon");
        netObj.transform.SetParent(statusGroup.transform, false);
        _networkIcon = netObj.AddComponent<Image>();
        _networkIcon.sprite = iconWifi; 
        _networkIcon.preserveAspect = true;
        RectTransform netRT = netObj.GetComponent<RectTransform>();
        netRT.sizeDelta = new Vector2(60, 60);

        // 2. Battery Container
        GameObject battContainer = new GameObject("BatteryContainer");
        battContainer.transform.SetParent(statusGroup.transform, false);
        RectTransform battRT = battContainer.AddComponent<RectTransform>();
        battRT.sizeDelta = new Vector2(100, 50); 
        
        Sprite batSprite = GetBatterySprite(); 

        // Bg
        GameObject bgObj = new GameObject("Bg");
        bgObj.transform.SetParent(battContainer.transform, false);
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.sprite = batSprite; 
        bgImg.color = new Color(0.8f, 0.8f, 0.8f, 0.5f); 
        bgImg.preserveAspect = true;
        RectTransform bgRT = bgObj.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
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
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;

        // Text
        GameObject battTxtObj = CreateText(battContainer.transform, "100", Vector2.zero, 28, new Color(0.1f, 0.15f, 0.2f, 1f), true);
        RectTransform btRT = battTxtObj.GetComponent<RectTransform>();
        btRT.anchorMin = Vector2.zero; btRT.anchorMax = Vector2.one;
        btRT.sizeDelta = Vector2.zero;
        btRT.offsetMin = new Vector2(0,0); btRT.offsetMax = new Vector2(-8, 0); 
        
        _batteryText = battTxtObj.GetComponent<TextMeshProUGUI>();
        _batteryText.alignment = TextAlignmentOptions.Center;
        _batteryText.fontStyle = FontStyles.Bold;

        // --- SEPARATOR LINE ---
        // Position at the bottom of status bar (use parent transform to position at frame top, not inside statusbar)
        CreateSeparator(parent, w, separatorY);
    }

    void CreateSeparator(Transform parent, float w, float yPos)
    {
        GameObject lineObj = new GameObject("SeparatorLine");
        lineObj.transform.SetParent(parent, false);
        RectTransform rt = lineObj.AddComponent<RectTransform>();

        // Calculate separator width based on menu width
        // Using ratio slightly wider than menu for visual effect
        float lineWidth = w * 1.1f;

        // Anchor to top edge of frame
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f); // Pivot at top so line draws downward from StatusBar bottom edge
        rt.sizeDelta = new Vector2(lineWidth, 2); // 2px height
        rt.anchoredPosition = new Vector2(0, -yPos); // Position based on separatorOffset variable

        Image img = lineObj.AddComponent<Image>();
        img.sprite = GetGradientLineSprite();
        img.raycastTarget = false;

        // Add a subtle glow/shadow
        Shadow s = lineObj.AddComponent<Shadow>();
        s.effectColor = new Color(0.5f, 0f, 1f, 0.5f);
        s.effectDistance = new Vector2(0, -1);
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
        Color c2 = new Color(0.8f, 0f, 1f); // Purple

        for (int x = 0; x < w; x++)
        {
            float t = (float)x / (w - 1);
            
            // 1. Bias towards Cyan (0) using Cubic ease in
            Color col = Color.Lerp(c1, c2, Mathf.Pow(t, 3.0f));
            
            // 2. Alpha based on Sine wave for fade out at ends
            // Sin(0) = 0, Sin(PI/2) = 1, Sin(PI) = 0
            float alpha = Mathf.Sin(t * Mathf.PI);
            
            // Sharpen the ends slightly so it's not too faint in middle
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

    // --- SPRITE GENERATORS ---

    Sprite _smallRoundedSprite;
    Sprite GetSmallRoundedSprite()
    {
        if (_smallRoundedSprite != null) return _smallRoundedSprite;
        int size = 128; 
        int radius = 20; // Adjusted to ~15% of size (matches Main Menu 80/512 ratio)
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
        _smallRoundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return _smallRoundedSprite;
    }

    Sprite _smallBorderSprite;
    Sprite GetSmallBorderSprite()
    {
        if (_smallBorderSprite != null) return _smallBorderSprite;
        int size = 128;
        int radius = 20; // Matches rounded sprite radius
        int thickness = 3;

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
        _smallBorderSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return _smallBorderSprite;
    }

    
    public void RecenterObject()
    {
        StartCoroutine(RecenterRoutine());
    }

    System.Collections.IEnumerator RecenterRoutine()
    {
        // 1. Get Reticle
        VRGazeReticle reticle = VRGazeReticle.Instance; 
        if (reticle == null) reticle = FindObjectOfType<VRGazeReticle>();

        // 2. Start Animation
        if (reticle != null)
        {
            reticle.EnterRecenterMode(GetRecenterSprite());
        }

        // 3. Countdown 2s
        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / duration);
            
            if (reticle != null) reticle.UpdateRecenterProgress(p);
            
            yield return null;
        }

        // 4. Perform Action
        Camera cam = Camera.main;
        if (cam != null)
        {
            PerformRecenterLogic(cam);
        }

        // 5. Restore Reticle
        if (reticle != null)
        {
            reticle.ExitRecenterMode();
        }
    }

    void PerformRecenterLogic(Camera cam)
    {
        // Calculate horizontal forward vector of camera
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        // Get current horizontal distance
        Vector3 currentPos = transform.position;
        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(new Vector2(currentPos.x, currentPos.z), new Vector2(camPos.x, camPos.z));

        // Calculate new position
        // "Move on sphere": Maintain distance
        // "No height change": Maintain Y
        Vector3 newPos = camPos + camForward * hDist;
        newPos.y = currentPos.y; 

        transform.position = newPos;
        transform.rotation = Quaternion.LookRotation(camForward);
        
        Debug.Log("[VRMenuFrame] Recenter complete.");
    }
}

public class FloatingDataAnim : MonoBehaviour
{
    public float speed;
    public Vector2 range; 
    private RectTransform _rt;
    private Vector2 _dir;

    void Start()
    {
        _rt = GetComponent<RectTransform>();
        _dir = new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.2f, 0.8f)).normalized; 
        if (Random.value > 0.5f) _dir.y *= -1; 
    }

    void Update()
    {
        if (_rt == null) return;
        _rt.anchoredPosition += _dir * speed * Time.deltaTime;

        float halfW = range.x / 2f + 50f;
        float halfH = range.y / 2f + 50f;

        if (_rt.anchoredPosition.y > halfH) _rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), -halfH);
        else if (_rt.anchoredPosition.y < -halfH) _rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), halfH);
        
        if (_rt.anchoredPosition.x > halfW) _rt.anchoredPosition = new Vector2(-halfW, Random.Range(-halfH, halfH));
        else if (_rt.anchoredPosition.x < -halfW) _rt.anchoredPosition = new Vector2(halfW, Random.Range(-halfH, halfH));
    }
}
