using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// VR Menu Frame - World Space UI panel with glass effect.
/// Canvas is directly on this GameObject (merged, no child MenuCanvas).
/// Uses logicalWidth for pixel-perfect UI scaling.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(GraphicRaycaster))]
public class VRMenuFrame : MonoBehaviour
{
    [Header("Primary Frame")]
    [Tooltip("Only one VRMenuFrame can be primary at a time. VRTaskbar positions relative to the primary frame.")]
    [SerializeField]
    private bool _primary = false;

    public bool Primary
    {
        get => _primary;
        set
        {
            if (_primary == value) return;
            _primary = value;
            if (_primary)
            {
                SetAsPrimary();
            }
        }
    }

    // Static reference to the current primary VRMenuFrame
    private static VRMenuFrame _primaryInstance;
    public static VRMenuFrame PrimaryInstance => _primaryInstance;

    [Header("Panel Size (meters)")]
    [Tooltip("Physical width of the panel in meters")]
    public float panelWidth = 1.6f;
    [Tooltip("Physical height of the panel in meters")]
    public float panelHeight = 0.9f;

    [Header("Logical Size (pixels)")]
    [Tooltip("Logical width in pixels for UI layout calculations")]
    public float logicalWidth = 1920f;

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
    [Tooltip("Padding for glow effect outside glass (0 = no expansion)")]
    [Range(0f, 0.1f)]
    public float glowExpansion = 0.02f;
    public float glowSpread = 40f;

    public float shimmerSpeed = 0.4f;
    public float hdrBoost = 1.8f;

    [Header("Content Margin (pixels)")]
    [Tooltip("Margin to shrink ContentContainer relative to parent")]
    public float contentMarginLeft = 20f;
    public float contentMarginRight = 20f;
    public float contentMarginTop = 10f;
    public float contentMarginBottom = 20f;

    [Header("Style Resources")]
    public TMP_FontAsset customFont;

    // Internal Resources
    private Sprite _roundedSprite;
    private Sprite _pixelSprite;
    private Dictionary<int, Sprite> _borderSprites = new Dictionary<int, Sprite>();

    // Components (on this GameObject)
    public Canvas Canvas { get; private set; }
    public RectTransform CanvasRect { get; private set; }
    public RectTransform ContentContainer { get; private set; }

    // Calculated values
    private float LogicalHeight => (logicalWidth / panelWidth) * panelHeight;
    private float ScaleFactor => panelWidth / logicalWidth;
    private float Aspect => panelWidth / panelHeight;

    void SetAsPrimary()
    {
        // Unset previous primary
        if (_primaryInstance != null && _primaryInstance != this)
        {
            _primaryInstance._primary = false;
        }
        _primaryInstance = this;
    }

    void OnEnable()
    {
        // Register as primary if marked
        if (_primary)
        {
            SetAsPrimary();
        }
        // If no primary exists, become primary
        else if (_primaryInstance == null)
        {
            _primary = true;
            _primaryInstance = this;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UpdateMaterialAspectRatiosEditor();
        }
#endif
    }

    void OnDisable()
    {
        // Clear primary reference if this was the primary
        if (_primaryInstance == this)
        {
            _primaryInstance = null;

            // Find another VRMenuFrame to become primary
            var allFrames = FindObjectsOfType<VRMenuFrame>();
            foreach (var frame in allFrames)
            {
                if (frame != this && frame.isActiveAndEnabled)
                {
                    frame._primary = true;
                    _primaryInstance = frame;
                    break;
                }
            }
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    // Handle primary toggle in editor
                    if (_primary && _primaryInstance != this)
                    {
                        SetAsPrimary();
                    }

                    // Update RectTransform when dimensions change
                    var rt = GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.sizeDelta = new Vector2(logicalWidth, LogicalHeight);
                        rt.localScale = new Vector3(ScaleFactor, ScaleFactor, 1f);
                    }
                    UpdateMaterialAspectRatiosEditor();
                    UpdateContentMarginEditor();
                }
            };
        }
    }

    void UpdateContentMarginEditor()
    {
        Transform contentTransform = transform.Find("ContentContainer");
        if (contentTransform != null)
        {
            var contentRect = contentTransform.GetComponent<RectTransform>();
            if (contentRect != null)
            {
                contentRect.offsetMin = new Vector2(contentMarginLeft, contentMarginBottom);
                contentRect.offsetMax = new Vector2(-contentMarginRight, -contentMarginTop);
            }
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

        // Re-apply runtime sprites (they don't serialize in prefabs)
        ReapplyRuntimeSprites();

        // Re-initialize floating data animations
        ReinitializeFloatingDataEffects();

        // Ensure layers are set for VRGazeReticle raycast
        SetupVRLayers();

        if (ContentContainer != null)
        {
            // Apply margin to existing ContentContainer
            ContentContainer.offsetMin = new Vector2(contentMarginLeft, contentMarginBottom);
            ContentContainer.offsetMax = new Vector2(-contentMarginRight, -contentMarginTop);
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

                float expansion = glowExpansion;
                float expandedW = logicalWidth * (1f + 2f * expansion);
                float expandedH = h * (1f + 2f * expansion);
                if (Mathf.Abs(bgCol.size.x - expandedW) > 1f || bgCol.size.z > 0.1f)
                {
                    bgCol.size = new Vector3(expandedW, expandedH, 0.01f);
                    bgCol.center = Vector3.zero;
                }
            }
        }
    }

    /// <summary>
    /// Re-apply sprites that are generated at runtime (not serialized in prefabs)
    /// </summary>
    void ReapplyRuntimeSprites()
    {
        // Re-apply GlassBackground and GlowingBorder sprites
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

        // 2. Content Container
        GameObject contentObj = new GameObject("ContentContainer");
        contentObj.transform.SetParent(transform, false);
        ContentContainer = contentObj.AddComponent<RectTransform>();

        ContentContainer.anchorMin = Vector2.zero;
        ContentContainer.anchorMax = Vector2.one;
        ContentContainer.offsetMin = new Vector2(contentMarginLeft, contentMarginBottom);
        ContentContainer.offsetMax = new Vector2(-contentMarginRight, -contentMarginTop);
    }

    /// <summary>
    /// Add a RectTransform as content, automatically stretching to fill ContentContainer.
    /// </summary>
    public void SetContent(RectTransform content)
    {
        if (ContentContainer == null)
        {
            Debug.LogWarning("[VRMenuFrame] ContentContainer not initialized");
            return;
        }

        content.SetParent(ContentContainer, false);
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        content.localScale = Vector3.one;
    }

    /// <summary>
    /// Add a GameObject as content, automatically stretching to fill ContentContainer.
    /// </summary>
    public void SetContent(GameObject content)
    {
        RectTransform rt = content.GetComponent<RectTransform>();
        if (rt == null)
        {
            rt = content.AddComponent<RectTransform>();
        }
        SetContent(rt);
    }

    // --- CREATION HELPERS (logical pixels) ---

    void CreateGlassPanel(Transform parent, float w, float h)
    {
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);
        Image img = bgObj.AddComponent<Image>();

        img.type = Image.Type.Simple;
        img.sprite = GetPixelSprite();

        // Use glowExpansion directly - small value for glow effect padding
        float expansion = glowExpansion;
        float edgePad = glowExpansion > 0 ? glowExpansion / (1f + 2f * glowExpansion) : 0f;

        float aspect = w / h;

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);

            glassMat.SetFloat("_CornerRadius", 0.12f);
            glassMat.SetFloat("_EdgePadding", edgePad);
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
        float expandedW = w * (1f + 2f * expansion);
        float expandedH = h * (1f + 2f * expansion);
        BoxCollider bgCol = bgObj.AddComponent<BoxCollider>();
        bgCol.size = new Vector3(expandedW, expandedH, 0.01f); // thin collider
        bgCol.center = new Vector3(0, 0, -0.01f);

        // Set layer to VirtualObjects for VRGazeReticle raycast
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) bgObj.layer = vrLayer;

        CreateGlowingBorder(bgObj.transform, w, h, edgePad);
        CreateFloatingDataEffects(bgObj.transform, w, h);
    }

    void CreateGlowingBorder(Transform parent, float w, float h, float edgePad)
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

            glowMat.SetFloat("_StrokeEnabled", 0);

            glowMat.SetFloat("_BorderWidth", 0.02f);
            glowMat.SetFloat("_CornerRadius", 0.12f);
            glowMat.SetFloat("_EdgePadding", edgePad);
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

        // GlassBackground is expanded by glowExpansion, compensate for that plus content margins
        float expansionPxW = w * glowExpansion;
        float expansionPxH = h * glowExpansion;

        rt.offsetMin = new Vector2(
            expansionPxW + contentMarginLeft,
            expansionPxH + contentMarginBottom
        );
        rt.offsetMax = new Vector2(
            -(expansionPxW + contentMarginRight),
            -(expansionPxH + contentMarginTop)
        );

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

}
