using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// VR Taskbar - Compact World Space UI bar with glass effect.
/// All dimensions are in logical pixels, scaled to match VRMenuFrame pixel density.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(GraphicRaycaster))]
public class VRTaskbar : MonoBehaviour
{
    [Header("Logical Size (pixels)")]
    [Tooltip("Logical width in pixels for UI layout calculations")]
    public float logicalWidth = 1060f;
    [Tooltip("Logical height in pixels for UI layout calculations")]
    public float logicalHeight = 128f;

    [Header("Visual Config")]
    public Color glassColor = new Color(1.0f, 1.0f, 1.0f, 0.098f);

    [Header("Glassmorphism")]
    [Tooltip("Enable glassmorphism blur effect")]
    public bool enableGlassmorphism = true;
    [Range(0, 40)]
    [Tooltip("Blur intensity - higher = more blur")]
    public float blurIntensity = 2f;
    [Range(1, 8)]
    [Tooltip("Blur quality - higher = smoother")]
    public int blurQuality = 3;
    [Range(0, 1)]
    [Tooltip("Glass opacity - how opaque the glass overlay is")]
    public float glassOpacity = 0f;
    [Range(0, 1)]
    [Tooltip("Tint strength - how much color tint to apply")]
    public float tintStrength = 0.1f;
    [Range(0, 0.5f)]
    [Tooltip("Inner glow at edges")]
    public float innerGlow = 0f;
    [Range(0.9f, 1.3f)]
    [Tooltip("Overall brightness")]
    public float brightness = 1f;
    [Range(0.5f, 1f)]
    [Tooltip("Color saturation")]
    public float saturation = 1f;

    [Header("Glowing Border Config")]
    [ColorUsage(true, true)]
    public Color glowColorA = new Color(0f, 1.5f, 2f, 1f);
    [ColorUsage(true, true)]
    public Color glowColorB = new Color(1.2f, 0.3f, 2f, 1f);
    [Tooltip("Padding for glow effect outside glass (0 = no expansion)")]
    [Range(0f, 0.1f)]
    public float glowExpansion = 0.02f;

    [Header("Content Margin (pixels)")]
    [Tooltip("Margin to shrink ContentContainer relative to parent")]
    public float contentMarginLeft = 0f;
    public float contentMarginRight = 10f;
    public float contentMarginTop = 10f;
    public float contentMarginBottom = 10f;

    [Header("Section Layout")]
    [Tooltip("Fixed width of Section 1 (left) in pixels")]
    public float section1Width = 400f;
    [Tooltip("Fixed width of Section 2 (middle - status) in pixels")]
    public float section2Width = 398f;
    [Tooltip("Fixed width of Section 3 (right - app buttons) in pixels")]
    public float section3Width = 206f;
    [Tooltip("Spacing width between sections")]
    public float sectionSpacing = 37f;
    [Tooltip("Button size in pixels")]
    public float buttonSize = 90f;
    [Tooltip("Spacing between buttons")]
    public float buttonSpacing = 12f;
    [Tooltip("Maximum number of app buttons in Section 3")]
    public int maxAppButtons = 4;

    [Header("Style Resources")]
    public TMP_FontAsset customFont;
    public Sprite iconQuit;
    public Sprite iconSettings;
    public Sprite iconPassthrough;
    public Sprite iconRecenter;
    public Sprite iconHome;
    public Sprite iconWifi;
    public Sprite iconBattery;

    [Header("Position Tracking")]
    [Tooltip("If true, taskbar will auto-position relative to the primary VRMenuFrame")]
    public bool followPrimaryFrame = true;
    [Tooltip("Multiplier for spacing below primary frame (1.25 = 125% of taskbar height)")]
    public float spacingMultiplier = 1.5f;

    [Header("Initial Orientation")]
    [Tooltip("If true, taskbar will face the camera on initialization (including pitch), then stay fixed")]
    public bool faceOnInit = true;
    private bool _hasInitializedOrientation = false;

    // Internal Resources
    private Sprite _pixelSprite;
    private Sprite _batterySprite;
    private Sprite _wifiSprite;
    private Sprite _roundedMaskSprite;

    // Status References
    private TextMeshProUGUI _clockText;
    private TextMeshProUGUI _batteryText;
    private Image _networkIcon;
    private Image _batteryFillImage;

    // Section References
    private RectTransform _section1;
    private RectTransform _section2;
    private RectTransform _section3;
    private List<GameObject> _appButtons = new List<GameObject>();

    // Passthrough Toggle State (controls ModeController)
    private GameObject _passthroughButton;
    private bool _isPassthroughOn = false;
    private ModeController _modeController;

    // Section 2 App Buttons - Radio button behavior (only one active at a time)
    private int _activeAppButtonIndex = 0; // Default: Home (index 0) is active

    // Components
    public Canvas Canvas { get; private set; }
    public RectTransform CanvasRect { get; private set; }
    public RectTransform ContentContainer { get; private set; }

    // Scale factor: matches VRMenuFrame pixel density (1.6m / 1920px)
    private const float PixelToMeter = 1.6f / 1920f;

    // Calculated values
    private float ScaleFactor => PixelToMeter;
    private float Aspect => logicalWidth / logicalHeight;

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
                    var rt = GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.sizeDelta = new Vector2(logicalWidth, logicalHeight);
                        rt.localScale = new Vector3(ScaleFactor, ScaleFactor, 1f);
                    }
                    UpdateMaterialAspectRatiosEditor();
                    UpdateContentMarginEditor();
                    UpdatePositionRelativeToPrimary();
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
        Canvas = GetComponent<Canvas>();
        CanvasRect = GetComponent<RectTransform>();

        if (Canvas == null)
        {
            Canvas = gameObject.AddComponent<Canvas>();
        }
        Canvas.renderMode = RenderMode.WorldSpace;

        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        if (ContentContainer == null)
        {
            TryInitializeExisting();
        }

        if (ContentContainer == null)
        {
            Build();
        }

        // Orient towards camera once at initialization
        if (faceOnInit && !_hasInitializedOrientation)
        {
            OrientTowardsCamera();
        }
    }

    /// <summary>
    /// Orient taskbar to face the camera (including pitch).
    /// This makes the taskbar easier to see when looking down.
    /// Similar to WorldPanelPlus moveOrbitCamera behavior but only applied once.
    /// </summary>
    void OrientTowardsCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;

        Vector3 toCamera = cam.transform.position - transform.position;
        if (toCamera.sqrMagnitude < 1e-6f) return;

        // LookRotation with -toCamera makes the panel face the camera
        // Using Vector3.up as the up vector to prevent roll
        transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        _hasInitializedOrientation = true;
    }

    void Update()
    {
        UpdateClock();
        UpdateBattery();
        UpdateNetwork();
    }

    void LateUpdate()
    {
        UpdatePositionRelativeToPrimary();
    }

    void UpdateClock()
    {
        if (_clockText != null)
            _clockText.text = DateTime.Now.ToString("HH:mm");
    }

    void UpdateNetwork()
    {
        if (_networkIcon != null)
        {
            Color iconColor = Color.Lerp(glowColorA, Color.white, 0.9f);
            if (Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork)
            {
                _networkIcon.sprite = iconWifi ?? GetWifiSprite();
                _networkIcon.color = iconColor;
            }
            else if (Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork)
            {
                _networkIcon.sprite = iconWifi ?? GetWifiSprite();
                _networkIcon.color = iconColor;
            }
            else
            {
                _networkIcon.sprite = iconWifi ?? GetWifiSprite();
                _networkIcon.color = new Color(iconColor.r, iconColor.g, iconColor.b, 0.3f);
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

    /// <summary>
    /// Updates taskbar position to stay below the primary VRMenuFrame.
    /// posX = VRPrimary.posX
    /// posY = VRPrimary.posY - (VRPrimary.height / 2) - (taskbar.height * spacingMultiplier)
    /// </summary>
    void UpdatePositionRelativeToPrimary()
    {
        if (!followPrimaryFrame) return;

        VRMenuFrame primary = VRMenuFrame.PrimaryInstance;
        if (primary == null) return;

        Vector3 primaryPos = primary.transform.position;
        float primaryHalfHeight = primary.panelHeight / 2f;
        float taskbarPhysicalHeight = logicalHeight * PixelToMeter;

        // Calculate new position
        Vector3 newPos = transform.position;
        newPos.x = primaryPos.x;
        newPos.y = primaryPos.y - primaryHalfHeight - (taskbarPhysicalHeight * spacingMultiplier);
        newPos.z = primaryPos.z;

        transform.position = newPos;

        // Only match rotation with primary if faceOnInit is disabled
        // or if orientation hasn't been initialized yet (will be done in Start)
        if (!faceOnInit)
        {
            transform.rotation = primary.transform.rotation;
        }
    }

    void TryInitializeExisting()
    {
        Transform contentTransform = transform.Find("ContentContainer");
        if (contentTransform != null)
        {
            ContentContainer = contentTransform.GetComponent<RectTransform>();
        }

        UpdateMaterialAspectRatios();
        ReapplyRuntimeSprites();
        ReinitializeFloatingDataEffects();
        SetupVRLayers();
        SetupButtonColliders();
        SetupButtonListeners();
        FindStatusReferences();
        FindAppButtonReferences();
        SyncPassthroughWithModeController();

        if (ContentContainer != null)
        {
            // Apply margin to existing ContentContainer
            ContentContainer.offsetMin = new Vector2(contentMarginLeft, contentMarginBottom);
            ContentContainer.offsetMax = new Vector2(-contentMarginRight, -contentMarginTop);
            Debug.Log("[VRTaskbar] Initialized from existing content");
        }
    }

    /// <summary>
    /// Setup button colliders for VRGazeReticle when loading from prefab
    /// </summary>
    void SetupButtonColliders()
    {
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");

        // Find all buttons in Section1, Section2
        foreach (Transform section in new[] {
            transform.Find("ContentContainer/Section1_Left"),
            transform.Find("ContentContainer/Section2_Apps")
        })
        {
            if (section == null) continue;

            foreach (Transform child in section)
            {
                if (!child.name.StartsWith("Btn_") && !child.name.StartsWith("AppBtn_")) continue;

                Transform hitArea = child.Find("HitArea");
                if (hitArea == null) continue;

                BoxCollider col = hitArea.GetComponent<BoxCollider>();
                if (col == null) continue;

                // Set layer
                if (vrLayer != -1) hitArea.gameObject.layer = vrLayer;

                // Fix collider size - must be in front of GlassBackground (z=-0.1f)
                RectTransform parentRT = child.GetComponent<RectTransform>();
                if (parentRT != null)
                {
                    Vector2 size = parentRT.rect.size;
                    if (size.x > 0 && size.y > 0)
                    {
                        col.size = new Vector3(size.x, size.y, 0.1f);
                        col.center = new Vector3(0, 0, -0.1f);  // In front, towards camera
                    }
                }
            }
        }
    }

    /// <summary>
    /// Re-register button click listeners when loading from prefab
    /// </summary>
    void SetupButtonListeners()
    {
        // Section 1 buttons - button names are based on icon sprite names (icon_quit, icon_settings, icon_recenter)
        Transform section1 = transform.Find("ContentContainer/Section1_Left");
        if (section1 != null)
        {
            foreach (Transform child in section1)
            {
                if (!child.name.StartsWith("Btn_")) continue;

                Transform hitArea = child.Find("HitArea");
                if (hitArea == null) continue;

                Button btn = hitArea.GetComponent<Button>();
                if (btn == null) continue;

                btn.onClick.RemoveAllListeners();

                // Determine action based on button name
                string btnName = child.name.ToLower();
                if (btnName.Contains("quit"))
                {
                    btn.onClick.AddListener(() => {
                        Debug.Log("[VRTaskbar] Quit clicked");
                        #if UNITY_EDITOR
                        UnityEditor.EditorApplication.isPlaying = false;
                        #else
                        Application.Quit();
                        #endif
                    });
                }
                else if (btnName.Contains("settings"))
                {
                    btn.onClick.AddListener(() => Debug.Log("[VRTaskbar] Settings clicked"));
                }
                else if (btnName.Contains("passthrough"))
                {
                    _passthroughButton = child.gameObject;
                    btn.onClick.AddListener(TogglePassthrough);
                }
                else if (btnName.Contains("recenter"))
                {
                    btn.onClick.AddListener(RecenterObject);
                }
            }
        }

        // Section 2 - App buttons (radio button behavior)
        Transform appContainer = transform.Find("ContentContainer/Section2_Apps");
        if (appContainer != null)
        {
            int index = 0;
            foreach (Transform child in appContainer)
            {
                if (!child.name.StartsWith("AppBtn_")) continue;

                Transform hitArea = child.Find("HitArea");
                if (hitArea == null) { index++; continue; }

                Button btn = hitArea.GetComponent<Button>();
                if (btn == null) { index++; continue; }

                btn.onClick.RemoveAllListeners();
                int capturedIndex = index; // Capture for closure
                btn.onClick.AddListener(() => SelectAppButton(capturedIndex));
                index++;
            }

            // Apply initial active state colors
            UpdateAllAppButtonColors();
        }
    }

    /// <summary>
    /// Find and cache app button references from existing hierarchy
    /// </summary>
    void FindAppButtonReferences()
    {
        _appButtons.Clear();
        Transform appContainer = transform.Find("ContentContainer/Section2_Apps");
        if (appContainer == null) return;

        foreach (Transform child in appContainer)
        {
            if (child.name.StartsWith("AppBtn_"))
            {
                _appButtons.Add(child.gameObject);
            }
        }
    }

    /// <summary>
    /// Find and assign status UI references from existing hierarchy
    /// </summary>
    void FindStatusReferences()
    {
        // Find ClockText
        Transform clockTransform = FindDeepChild(transform, "ClockText");
        if (clockTransform != null)
        {
            _clockText = clockTransform.GetComponent<TextMeshProUGUI>();
        }

        // Find BatteryText
        Transform batteryTextTransform = FindDeepChild(transform, "BatteryText");
        if (batteryTextTransform != null)
        {
            _batteryText = batteryTextTransform.GetComponent<TextMeshProUGUI>();
        }

        // Find Battery Fill
        Transform batteryFillTransform = FindDeepChild(transform, "Fill");
        if (batteryFillTransform != null && batteryFillTransform.parent != null &&
            batteryFillTransform.parent.name == "BatteryContainer")
        {
            _batteryFillImage = batteryFillTransform.GetComponent<Image>();
        }

        // Find NetworkIcon
        Transform networkTransform = FindDeepChild(transform, "NetworkIcon");
        if (networkTransform != null)
        {
            _networkIcon = networkTransform.GetComponent<Image>();
        }
    }

    /// <summary>
    /// Recursively find a child transform by name
    /// </summary>
    static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;

            Transform result = FindDeepChild(child, name);
            if (result != null)
                return result;
        }
        return null;
    }

    void SetupVRLayers()
    {
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer == -1) return;

        Transform glassBg = transform.Find("GlassBackground");
        if (glassBg != null)
        {
            BoxCollider bgCol = glassBg.GetComponent<BoxCollider>();
            if (bgCol != null)
            {
                glassBg.gameObject.layer = vrLayer;

                float expansion = glowExpansion;
                float expandedW = logicalWidth * (1f + 2f * expansion);
                float expandedH = logicalHeight * (1f + 2f * expansion);
                if (Mathf.Abs(bgCol.size.x - expandedW) > 1f || bgCol.size.z > 0.1f)
                {
                    bgCol.size = new Vector3(expandedW, expandedH, 0.01f);
                    bgCol.center = new Vector3(0, 0, 0.05f);  // Behind canvas plane
                }
            }
        }
    }

    void ReapplyRuntimeSprites()
    {
        Transform glassBg = transform.Find("GlassBackground");
        if (glassBg != null)
        {
            var glassBgImg = glassBg.GetComponent<Image>();
            if (glassBgImg != null && glassBgImg.sprite == null)
            {
                glassBgImg.sprite = GetPixelSprite();
            }

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

        // Reapply battery sprite (runtime-generated, not saved in prefab)
        Transform batteryContainer = FindDeepChild(transform, "BatteryContainer");
        if (batteryContainer != null)
        {
            Sprite batSprite = GetBatterySprite();

            Transform bgTransform = batteryContainer.Find("Bg");
            if (bgTransform != null)
            {
                var bgImg = bgTransform.GetComponent<Image>();
                if (bgImg != null && bgImg.sprite == null)
                {
                    bgImg.sprite = batSprite;
                }
            }

            Transform fillTransform = batteryContainer.Find("Fill");
            if (fillTransform != null)
            {
                var fillImg = fillTransform.GetComponent<Image>();
                if (fillImg != null && fillImg.sprite == null)
                {
                    fillImg.sprite = batSprite;
                }
            }
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

    [ContextMenu("Rebuild Taskbar")]
    public void Rebuild()
    {
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

        // Re-orient towards camera after rebuild
        if (faceOnInit)
        {
            _hasInitializedOrientation = false;
            OrientTowardsCamera();
        }
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

        string prefabPath = $"{prefabDir}/VRTaskbar.prefab";

        if (PrefabUtility.IsPartOfPrefabInstance(gameObject))
        {
            PrefabUtility.UnpackPrefabInstance(gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        PrefabUtility.SaveAsPrefabAssetAndConnect(gameObject, prefabPath, InteractionMode.UserAction);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[VRTaskbar] Prefab saved to: {prefabPath}");
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
                var savedMat = SaveOrGetMaterial(img.material, "TaskbarGlassBackground", matDir);
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
                    var savedMat = SaveOrGetMaterial(borderImg.material, "TaskbarGlowingBorder", matDir);
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

    /// <summary>
    /// Fix import settings for all icon files (icon_*.png) in Resources folder.
    /// Sets textureType to Sprite, disables mipmaps, and sets compression to Uncompressed.
    /// </summary>
    public static void FixAllIconImportSettings()
    {
        string resourceFolder = "Assets/VR-Workspace/Resources";

        if (!AssetDatabase.IsValidFolder(resourceFolder)) return;

        int fixedCount = 0;

        // Find all Texture2D in Resources folder (non-recursive by default, but FindAssets searches subdirs)
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { resourceFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path);

            // Only process files starting with "icon_"
            if (!fileName.StartsWith("icon_")) continue;

            if (FixSingleIconImport(path))
            {
                fixedCount++;
            }
        }

        if (fixedCount > 0)
        {
            Debug.Log($"[VRTaskbar] Fixed import settings for {fixedCount} icon(s)");
        }
    }

    static bool FixSingleIconImport(string path)
    {
        try
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                bool changed = false;

                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    changed = true;
                }
                if (importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    changed = true;
                }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    return true;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[VRTaskbar] Failed to fix import for {path}: {e.Message}");
        }
        return false;
    }

    [MenuItem("VR-Workspace/Fix All Icon Import Settings")]
    public static void FixAllIconImportSettingsMenu()
    {
        FixAllIconImportSettings();
    }
#endif

    public void Build()
    {
        Build(logicalWidth, logicalHeight);
    }

    public void Build(float width, float height)
    {
        logicalWidth = width;
        logicalHeight = height;

        float scaleFactor = ScaleFactor;

        CanvasRect = GetComponent<RectTransform>();
        CanvasRect.sizeDelta = new Vector2(logicalWidth, logicalHeight);
        CanvasRect.localScale = new Vector3(scaleFactor, scaleFactor, 1f);
        CanvasRect.localPosition = Vector3.zero;

        float w = logicalWidth;
        float h = logicalHeight;

        // Fix icon import settings and load icons
#if UNITY_EDITOR
        FixAllIconImportSettings();
#endif
        LoadIcons();

        // Glass Background & Borders
        CreateGlassPanel(transform, w, h);

        // Content Container
        GameObject contentObj = new GameObject("ContentContainer");
        contentObj.transform.SetParent(transform, false);
        ContentContainer = contentObj.AddComponent<RectTransform>();

        ContentContainer.anchorMin = Vector2.zero;
        ContentContainer.anchorMax = Vector2.one;
        ContentContainer.offsetMin = new Vector2(contentMarginLeft, contentMarginBottom);
        ContentContainer.offsetMax = new Vector2(-contentMarginRight, -contentMarginTop);

        // Calculate content dimensions
        float contentHeight = h - contentMarginTop - contentMarginBottom;

        // Layout: Section1 | Spacing | Section2 | Spacing | Section3
        // Section1 (left): Quit, Settings, Recenter buttons
        // Section2 (middle): App buttons
        // Section3 (right): Network, Battery, Clock (status)

        // Section 1 (Left) - Control buttons
        CreateSection1(ContentContainer, contentHeight);

        // Section 2 (Middle - App buttons) - positioned after Section1 + spacing
        float section2XPos = section1Width + sectionSpacing;
        CreateSection2(ContentContainer, section2XPos, section2Width, contentHeight);

        // Section 3 (Right - Status) - positioned after Section2 + spacing
        float section3XPos = section2XPos + section2Width + sectionSpacing / 2;
        CreateSection3(ContentContainer, section3XPos, contentHeight);

        // Sync passthrough button with ModeController
        SyncPassthroughWithModeController();
    }

    void LoadIcons()
    {
        if (iconQuit == null) iconQuit = LoadIcon("quit");
        if (iconSettings == null) iconSettings = LoadIcon("settings");
        if (iconPassthrough == null) iconPassthrough = LoadIcon("passthrough");
        if (iconRecenter == null) iconRecenter = LoadIcon("recenter");
        if (iconHome == null) iconHome = LoadIcon("home");
        if (iconWifi == null) iconWifi = LoadIcon("wifi");
        if (iconBattery == null) iconBattery = LoadIcon("battery");
    }

    /// <summary>
    /// Load icon từ Resources folder bằng tên (không cần prefix "icon_")
    /// </summary>
    public static Sprite LoadIcon(string name)
    {
        return Resources.Load<Sprite>($"icon_{name}");
    }

    /// <summary>
    /// Section 1 (Left): Contains Quit, Settings, Recenter buttons
    /// </summary>
    void CreateSection1(RectTransform parent, float height)
    {
        GameObject section = new GameObject("Section1_Left");
        section.transform.SetParent(parent, false);
        _section1 = section.AddComponent<RectTransform>();

        // Anchor to left
        _section1.anchorMin = new Vector2(0, 0);
        _section1.anchorMax = new Vector2(0, 1);
        _section1.pivot = new Vector2(0, 0.5f);
        _section1.sizeDelta = new Vector2(section1Width, 0);
        _section1.anchoredPosition = Vector2.zero;

        // Horizontal layout for buttons
        HorizontalLayoutGroup layout = section.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = buttonSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(8, 8, 0, 0);

        // Create 4 BareIconButtons
        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color purpleColor = new Color(0.9f, 0.3f, 1f);

        // Quit button
        var quitBtn = VRButtonFactory.CreateBareIconButton(
            section.transform, buttonSize, iconQuit, cyanColor,
            () => {
                Debug.Log("[VRTaskbar] Quit clicked");
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
                #else
                Application.Quit();
                #endif
            }, 0.05f, 0.6f
        );

        // Settings button
        var settingsBtn = VRButtonFactory.CreateBareIconButton(
            section.transform, buttonSize, iconSettings, cyanColor,
            () => Debug.Log("[VRTaskbar] Settings clicked"), 0.05f, 0.6f
        );

        // Passthrough button (toggle on/off)
        var passthroughBtn = VRButtonFactory.CreateBareIconButton(
            section.transform, buttonSize, iconPassthrough, cyanColor,
            TogglePassthrough, 0.05f, 0.6f
        );
        _passthroughButton = passthroughBtn;

        // Recenter button
        var recenterBtn = VRButtonFactory.CreateBareIconButton(
            section.transform, buttonSize, iconRecenter, cyanColor,
            RecenterObject, 0.05f, 0.6f
        );
    }

    /// <summary>
    /// Section 2 (Middle): App buttons with HorizontalLayoutGroup
    /// Pre-creates 4 button slots (Home + 3 empty placeholders)
    /// </summary>
    void CreateSection2(RectTransform parent, float xPos, float width, float height)
    {
        GameObject section = new GameObject("Section2_Apps");
        section.transform.SetParent(parent, false);
        _section2 = section.AddComponent<RectTransform>();

        // Position at xPos from left
        _section2.anchorMin = new Vector2(0, 0);
        _section2.anchorMax = new Vector2(0, 1);
        _section2.pivot = new Vector2(0, 0.5f);
        _section2.sizeDelta = new Vector2(width, 0);
        _section2.anchoredPosition = new Vector2(xPos, 0);

        // Horizontal layout for buttons directly on Section2_Apps (like Section 1)
        HorizontalLayoutGroup layout = section.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = buttonSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(8, 8, 0, 0);

        // Pre-create all 4 button slots
        _appButtons.Clear();
        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color purpleColor = new Color(0.9f, 0.3f, 1f);

        for (int i = 0; i < maxAppButtons; i++)
        {
            Sprite slotIcon = (i == 0) ? iconHome : null; // Only Home has icon initially
            string slotName = (i == 0) ? "Home" : $"AppSlot_{i}";
            bool isPlaceholder = (i != 0);
            bool isActive = (i == _activeAppButtonIndex); // Home (index 0) is active by default

            // Active button is purple, inactive is cyan
            Color btnColor = isActive ? purpleColor : cyanColor;
            int capturedIndex = i; // Capture for closure
            var btn = CreateAppButtonSlot(_section2, slotIcon, slotName, btnColor, isPlaceholder, capturedIndex);
            _appButtons.Add(btn);
        }
    }

    /// <summary>
    /// Create a button slot for Section 2
    /// </summary>
    GameObject CreateAppButtonSlot(RectTransform parent, Sprite icon, string name, Color glowColor, bool isPlaceholder, int buttonIndex)
    {
        GameObject btn;

        if (isPlaceholder)
        {
            // Create invisible placeholder button
            btn = new GameObject($"AppBtn_{name}");
            btn.transform.SetParent(parent, false);

            RectTransform rt = btn.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(buttonSize, buttonSize);

            // Add CanvasGroup to control visibility
            CanvasGroup cg = btn.AddComponent<CanvasGroup>();
            cg.alpha = 0f; // Invisible initially
            cg.interactable = false;
            cg.blocksRaycasts = false;

            // Create icon placeholder (will be set later)
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(btn.transform, false);
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.color = glowColor;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            RectTransform iconRT = iconObj.GetComponent<RectTransform>();
            float iconScale = 0.6f;
            iconRT.anchorMin = new Vector2(0.5f - iconScale / 2f, 0.5f - iconScale / 2f);
            iconRT.anchorMax = new Vector2(0.5f + iconScale / 2f, 0.5f + iconScale / 2f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;
        }
        else
        {
            // Create actual button with icon - radio button behavior
            btn = VRButtonFactory.CreateBareIconButton(
                parent, buttonSize, icon, glowColor,
                () => SelectAppButton(buttonIndex), 0.05f, 0.65f
            );
            btn.name = $"AppBtn_{name}";
        }

        return btn;
    }

    /// <summary>
    /// Tạo Battery indicator với kích thước bằng icon của các button khác
    /// </summary>
    void CreateBatteryIndicator(Transform parent)
    {
        // Kích thước bằng icon trong button (buttonSize * iconScale)
        // float iconScale = 0.4f;
        float battHeight = 40f; // buttonSize * iconScale;
        float battWidth = battHeight * 2f; // Battery có tỷ lệ 2:1

        GameObject battContainer = new GameObject("BatteryContainer");
        battContainer.transform.SetParent(parent, false);
        RectTransform battRT = battContainer.AddComponent<RectTransform>();
        battRT.sizeDelta = new Vector2(battWidth, battHeight);

        Sprite batSprite = GetBatterySprite();

        // Background
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
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

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
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;

        // Black shadow for Battery Fill
        Color battShadowCol = new Color(0f, 0f, 0f, 0.15f);
        Shadow battShadow1 = fillObj.AddComponent<Shadow>();
        battShadow1.effectColor = battShadowCol;
        battShadow1.effectDistance = new Vector2(2f, -2f);
        Shadow battShadow2 = fillObj.AddComponent<Shadow>();
        battShadow2.effectColor = battShadowCol;
        battShadow2.effectDistance = new Vector2(-2f, 2f);

        // Text
        GameObject battTxtObj = new GameObject("BatteryText");
        battTxtObj.transform.SetParent(battContainer.transform, false);
        _batteryText = battTxtObj.AddComponent<TextMeshProUGUI>();
        if (customFont != null) _batteryText.font = customFont;
        _batteryText.text = "100";
        _batteryText.fontSize = 24f;
        _batteryText.color = Color.black;
        _batteryText.alignment = TextAlignmentOptions.Center;
        _batteryText.fontStyle = FontStyles.Bold;
        _batteryText.raycastTarget = false;

        // White shadow for Battery Text
        Color txtShadowCol = new Color(1f, 1f, 1f, 0.15f);
        Shadow txtShadow1 = battTxtObj.AddComponent<Shadow>();
        txtShadow1.effectColor = txtShadowCol;
        txtShadow1.effectDistance = new Vector2(2f, -2f);
        Shadow txtShadow2 = battTxtObj.AddComponent<Shadow>();
        txtShadow2.effectColor = txtShadowCol;
        txtShadow2.effectDistance = new Vector2(-2f, 2f);

        RectTransform btRT = battTxtObj.GetComponent<RectTransform>();
        btRT.anchorMin = Vector2.zero;
        btRT.anchorMax = Vector2.one;
        btRT.sizeDelta = Vector2.zero;
        btRT.offsetMin = new Vector2(0, 0);
        btRT.offsetMax = new Vector2(-5f, 0);
    }

    /// <summary>
    /// Section 3 (Right): Network button, Battery indicator, Clock
    /// </summary>
    void CreateSection3(RectTransform parent, float xPos, float height)
    {
        GameObject section = new GameObject("Section3_Status");
        section.transform.SetParent(parent, false);
        _section3 = section.AddComponent<RectTransform>();

        // Position at xPos from left
        _section3.anchorMin = new Vector2(0, 0);
        _section3.anchorMax = new Vector2(0, 1);
        _section3.pivot = new Vector2(0, 0.5f);
        _section3.sizeDelta = new Vector2(section3Width, 0);
        _section3.anchoredPosition = new Vector2(xPos, 0);

        // Upper section (2/3 height): Network + Battery
        GameObject upperSection = new GameObject("StatusGroup");
        upperSection.transform.SetParent(section.transform, false);
        RectTransform upperRT = upperSection.AddComponent<RectTransform>();
        upperRT.anchorMin = new Vector2(0, 0.33f);
        upperRT.anchorMax = new Vector2(1, 1);
        upperRT.offsetMin = Vector2.zero;
        upperRT.offsetMax = Vector2.zero;

        HorizontalLayoutGroup upperLayout = upperSection.AddComponent<HorizontalLayoutGroup>();
        upperLayout.spacing = buttonSpacing * 2.5f;
        upperLayout.childAlignment = TextAnchor.MiddleCenter;
        upperLayout.childControlWidth = false;
        upperLayout.childControlHeight = false;
        upperLayout.childForceExpandWidth = false;
        upperLayout.childForceExpandHeight = false;

        // Network Icon (simple image, same size as button icons)
        float iconSize = buttonSize * 0.6f;
        GameObject netObj = new GameObject("NetworkIcon");
        netObj.transform.SetParent(upperSection.transform, false);
        _networkIcon = netObj.AddComponent<Image>();
        _networkIcon.sprite = iconWifi ?? GetWifiSprite();
        _networkIcon.preserveAspect = true;
        _networkIcon.color = Color.Lerp(glowColorA, Color.white, 0.9f);
        RectTransform netRT = netObj.GetComponent<RectTransform>();
        netRT.sizeDelta = new Vector2(iconSize, iconSize);

        // Glow effects for NetworkIcon (matching VRButtonFactory style)
        Color netGlowCol = Color.Lerp(glowColorA, Color.white, 0.7f);
        netGlowCol.a = 0.4f;
        Shadow netShadow1 = netObj.AddComponent<Shadow>();
        netShadow1.effectColor = netGlowCol;
        netShadow1.effectDistance = new Vector2(2f, -2f);
        Shadow netShadow2 = netObj.AddComponent<Shadow>();
        netShadow2.effectColor = netGlowCol;
        netShadow2.effectDistance = new Vector2(-2f, 2f);

        Color netBloomCol = Color.Lerp(glowColorA, Color.white, 0.8f);
        netBloomCol.a = 0.15f;
        Shadow netShadow3 = netObj.AddComponent<Shadow>();
        netShadow3.effectColor = netBloomCol;
        netShadow3.effectDistance = new Vector2(5f, -5f);
        Shadow netShadow4 = netObj.AddComponent<Shadow>();
        netShadow4.effectColor = netBloomCol;
        netShadow4.effectDistance = new Vector2(-5f, 5f);

        // Battery Indicator
        CreateBatteryIndicator(upperSection.transform);

        // Lower section (1/3 height): Clock
        GameObject lowerSection = new GameObject("ClockGroup");
        lowerSection.transform.SetParent(section.transform, false);
        RectTransform lowerRT = lowerSection.AddComponent<RectTransform>();
        lowerRT.anchorMin = new Vector2(0, 0);
        lowerRT.anchorMax = new Vector2(1, 0.33f);
        lowerRT.offsetMin = Vector2.zero;
        lowerRT.offsetMax = Vector2.zero;

        // Clock text
        GameObject clockObj = new GameObject("ClockText");
        clockObj.transform.SetParent(lowerSection.transform, false);
        _clockText = clockObj.AddComponent<TextMeshProUGUI>();
        if (customFont != null) _clockText.font = customFont;
        _clockText.text = DateTime.Now.ToString("HH:mm");
        _clockText.fontSize = 32f;
        _clockText.color = Color.white;
        _clockText.alignment = TextAlignmentOptions.Center;
        _clockText.fontStyle = FontStyles.Bold;
        _clockText.raycastTarget = false;

        RectTransform clockRT = clockObj.GetComponent<RectTransform>();
        clockRT.anchorMin = Vector2.zero;
        clockRT.anchorMax = Vector2.one;
        clockRT.offsetMin = Vector2.zero;
        clockRT.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Activate an app button slot with icon and click handler.
    /// Slot 0 is reserved for Home. Returns the activated slot or null if no slots available.
    /// </summary>
    public GameObject AddAppButton(Sprite icon, string name, UnityEngine.Events.UnityAction onClick)
    {
        if (_appButtons == null || _appButtons.Count == 0)
        {
            Debug.LogWarning("[VRTaskbar] App buttons not initialized");
            return null;
        }

        // Find first empty placeholder slot (skip slot 0 which is Home)
        int slotIndex = -1;
        for (int i = 1; i < _appButtons.Count; i++)
        {
            CanvasGroup cg = _appButtons[i].GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha < 0.5f)
            {
                slotIndex = i;
                break;
            }
        }

        if (slotIndex < 0)
        {
            Debug.LogWarning("[VRTaskbar] No empty app slots available");
            return null;
        }

        // Activate the placeholder slot
        return ActivateAppSlot(slotIndex, icon, name, onClick);
    }

    /// <summary>
    /// Activate a specific app slot with icon and click handler
    /// </summary>
    public GameObject ActivateAppSlot(int slotIndex, Sprite icon, string name, UnityEngine.Events.UnityAction onClick)
    {
        if (slotIndex < 0 || slotIndex >= _appButtons.Count)
        {
            Debug.LogWarning($"[VRTaskbar] Invalid slot index: {slotIndex}");
            return null;
        }

        GameObject slot = _appButtons[slotIndex];
        slot.name = $"AppBtn_{name}";

        // Set icon
        Transform iconTransform = slot.transform.Find("Icon");
        if (iconTransform != null)
        {
            Image iconImg = iconTransform.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.sprite = icon;
            }
        }

        // Make visible and interactable
        CanvasGroup cg = slot.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        // Add click handler (need to add Button component if not exists)
        Button btn = slot.GetComponent<Button>();
        if (btn == null)
        {
            btn = slot.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
        }
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(onClick);

        // Add glow effects like other buttons
        AddButtonGlowEffects(slot);

        return slot;
    }

    /// <summary>
    /// Add glow effects to a button (matching VRButtonFactory style)
    /// </summary>
    void AddButtonGlowEffects(GameObject button)
    {
        Transform iconTransform = button.transform.Find("Icon");
        if (iconTransform == null) return;

        // Check if shadows already exist
        if (iconTransform.GetComponents<Shadow>().Length >= 2) return;

        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color glowCol = Color.Lerp(cyanColor, Color.white, 0.7f);
        glowCol.a = 0.4f;

        Shadow shadow1 = iconTransform.gameObject.AddComponent<Shadow>();
        shadow1.effectColor = glowCol;
        shadow1.effectDistance = new Vector2(2f, -2f);

        Shadow shadow2 = iconTransform.gameObject.AddComponent<Shadow>();
        shadow2.effectColor = glowCol;
        shadow2.effectDistance = new Vector2(-2f, 2f);

        Color bloomCol = Color.Lerp(cyanColor, Color.white, 0.8f);
        bloomCol.a = 0.15f;

        Shadow shadow3 = iconTransform.gameObject.AddComponent<Shadow>();
        shadow3.effectColor = bloomCol;
        shadow3.effectDistance = new Vector2(5f, -5f);

        Shadow shadow4 = iconTransform.gameObject.AddComponent<Shadow>();
        shadow4.effectColor = bloomCol;
        shadow4.effectDistance = new Vector2(-5f, 5f);
    }

    /// <summary>
    /// Deactivate an app slot (hide it but keep the placeholder)
    /// </summary>
    public void DeactivateAppSlot(int slotIndex)
    {
        if (slotIndex < 1 || slotIndex >= _appButtons.Count) return; // Can't deactivate Home (slot 0)

        GameObject slot = _appButtons[slotIndex];

        // Hide and disable
        CanvasGroup cg = slot.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        // Clear icon
        Transform iconTransform = slot.transform.Find("Icon");
        if (iconTransform != null)
        {
            Image iconImg = iconTransform.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.sprite = null;
            }
        }

        // Remove click handlers
        Button btn = slot.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
        }

        slot.name = $"AppBtn_AppSlot_{slotIndex}";
    }

    /// <summary>
    /// Remove an app button (deactivate the slot)
    /// </summary>
    public void RemoveAppButton(GameObject button)
    {
        int slotIndex = _appButtons.IndexOf(button);
        if (slotIndex > 0) // Can't remove Home (slot 0)
        {
            DeactivateAppSlot(slotIndex);
        }
    }

    void UpdateGlassPanelSize()
    {
        Transform glassBg = transform.Find("GlassBackground");
        if (glassBg == null) return;

        // Update collider size
        BoxCollider bgCol = glassBg.GetComponent<BoxCollider>();
        if (bgCol != null)
        {
            float expansion = glowExpansion;
            float expandedW = logicalWidth * (1f + 2f * expansion);
            float expandedH = logicalHeight * (1f + 2f * expansion);
            bgCol.size = new Vector3(expandedW, expandedH, 0.01f);
        }
    }

    // --- PASSTHROUGH TOGGLE (Controls ModeController) ---

    /// <summary>
    /// Toggle passthrough mode on/off.
    /// ON = RealWorld mode, OFF = VirtualSpace mode.
    /// Changes button icon color between cyan (off) and purple (on).
    /// </summary>
    public void TogglePassthrough()
    {
        _isPassthroughOn = !_isPassthroughOn;
        UpdatePassthroughButtonColor();
        ApplyPassthroughMode();
        Debug.Log($"[VRTaskbar] Passthrough {(_isPassthroughOn ? "ON (RealWorld)" : "OFF (VirtualSpace)")}");
    }

    /// <summary>
    /// Apply passthrough mode to ModeController.
    /// ON = RealWorld, OFF = VirtualSpace.
    /// </summary>
    void ApplyPassthroughMode()
    {
        // Find ModeController if not cached
        if (_modeController == null)
        {
            _modeController = FindObjectOfType<ModeController>();
        }

        if (_modeController != null)
        {
            ViewMode targetMode = _isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace;
            _modeController.SetMode(targetMode);
        }
    }

    /// <summary>
    /// Sync passthrough button state with ModeController on initialization.
    /// </summary>
    void SyncPassthroughWithModeController()
    {
        if (_modeController == null)
        {
            _modeController = FindObjectOfType<ModeController>();
        }

        if (_modeController != null)
        {
            // Sync: RealWorld = ON, VirtualSpace = OFF
            _isPassthroughOn = (_modeController.mode == ViewMode.RealWorld);
            UpdatePassthroughButtonColor();
        }
    }

    /// <summary>
    /// Update passthrough button icon color based on current state.
    /// </summary>
    void UpdatePassthroughButtonColor()
    {
        if (_passthroughButton == null) return;

        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color purpleColor = new Color(0.9f, 0.3f, 1f);
        Color targetColor = _isPassthroughOn ? purpleColor : cyanColor;

        // Find Icon image in the button hierarchy
        Transform iconTransform = _passthroughButton.transform.Find("HitArea/Visuals/Content/Icon");
        if (iconTransform != null)
        {
            Image iconImg = iconTransform.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.color = Color.Lerp(targetColor, Color.white, 0.9f);

                // Update shadow glow colors
                Shadow[] shadows = iconTransform.GetComponents<Shadow>();
                if (shadows.Length >= 4)
                {
                    // Layer 1 - Sharp inner halo
                    Color glowCol = Color.Lerp(targetColor, Color.white, 0.7f);
                    glowCol.a = 0.4f;
                    shadows[0].effectColor = glowCol;
                    shadows[1].effectColor = glowCol;

                    // Layer 2 - Soft outer bloom
                    Color bloomCol = Color.Lerp(targetColor, Color.white, 0.8f);
                    bloomCol.a = 0.15f;
                    shadows[2].effectColor = bloomCol;
                    shadows[3].effectColor = bloomCol;
                }
            }
        }
    }

    /// <summary>
    /// Get current passthrough state
    /// </summary>
    public bool IsPassthroughOn => _isPassthroughOn;

    /// <summary>
    /// Set passthrough state directly (without toggling)
    /// </summary>
    public void SetPassthrough(bool on)
    {
        if (_isPassthroughOn != on)
        {
            _isPassthroughOn = on;
            UpdatePassthroughButtonColor();
            Debug.Log($"[VRTaskbar] Passthrough set to {(_isPassthroughOn ? "ON" : "OFF")}");
        }
    }

    // --- APP BUTTON SELECTION (Radio Button Behavior) ---

    /// <summary>
    /// Select an app button by index (radio button behavior).
    /// Only one button can be active at a time.
    /// If the button is already active, do nothing.
    /// </summary>
    public void SelectAppButton(int index)
    {
        // If already active, do nothing (can't click active button)
        if (index == _activeAppButtonIndex) return;

        // Validate index
        if (index < 0 || index >= _appButtons.Count) return;

        // Check if button is visible (not a placeholder)
        CanvasGroup cg = _appButtons[index].GetComponent<CanvasGroup>();
        if (cg != null && cg.alpha < 0.5f) return; // Skip invisible placeholders

        int previousIndex = _activeAppButtonIndex;
        _activeAppButtonIndex = index;
        UpdateAllAppButtonColors();

        string buttonName = _appButtons[index].name.Replace("AppBtn_", "");
        Debug.Log($"[VRTaskbar] App button selected: {buttonName} (index {index})");
    }

    /// <summary>
    /// Update all app button colors based on active state.
    /// Active button is purple, inactive buttons are cyan.
    /// </summary>
    void UpdateAllAppButtonColors()
    {
        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color purpleColor = new Color(0.9f, 0.3f, 1f);

        for (int i = 0; i < _appButtons.Count; i++)
        {
            if (_appButtons[i] == null) continue;

            bool isActive = (i == _activeAppButtonIndex);
            Color targetColor = isActive ? purpleColor : cyanColor;

            UpdateSingleAppButtonColor(_appButtons[i], targetColor);
        }
    }

    /// <summary>
    /// Update a single app button's icon color and glow.
    /// </summary>
    void UpdateSingleAppButtonColor(GameObject button, Color targetColor)
    {
        if (button == null) return;

        // Find Icon image in the button hierarchy
        Transform iconTransform = button.transform.Find("HitArea/Visuals/Content/Icon");
        if (iconTransform != null)
        {
            Image iconImg = iconTransform.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.color = Color.Lerp(targetColor, Color.white, 0.9f);

                // Update shadow glow colors
                Shadow[] shadows = iconTransform.GetComponents<Shadow>();
                if (shadows.Length >= 4)
                {
                    // Layer 1 - Sharp inner halo
                    Color glowCol = Color.Lerp(targetColor, Color.white, 0.7f);
                    glowCol.a = 0.4f;
                    shadows[0].effectColor = glowCol;
                    shadows[1].effectColor = glowCol;

                    // Layer 2 - Soft outer bloom
                    Color bloomCol = Color.Lerp(targetColor, Color.white, 0.8f);
                    bloomCol.a = 0.15f;
                    shadows[2].effectColor = bloomCol;
                    shadows[3].effectColor = bloomCol;
                }
            }
        }
    }

    /// <summary>
    /// Get the currently active app button index.
    /// </summary>
    public int ActiveAppButtonIndex => _activeAppButtonIndex;

    /// <summary>
    /// Check if Home button is currently active.
    /// </summary>
    public bool IsHomeActive => _activeAppButtonIndex == 0;

    /// <summary>
    /// Select Home button (convenience method).
    /// </summary>
    public void SelectHome()
    {
        SelectAppButton(0);
    }

    // --- RECENTER ---

    /// <summary>
    /// Recenter the primary VRMenuFrame and this taskbar to face the camera.
    /// Shows visual feedback via VRGazeReticle during the recenter process.
    /// </summary>
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
            reticle.EnterRecenterMode(iconRecenter);
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
            RecenterAllVirtualObjects(cam);
        }

        if (reticle != null)
        {
            reticle.ExitRecenterMode();
        }

        Debug.Log("[VRTaskbar] Recenter complete.");
    }

    /// <summary>
    /// Recenter all objects in VirtualObjects parent.
    /// Uses the primary VRMenuFrame as the pivot point.
    /// All other objects maintain their relative positions and rotations.
    /// </summary>
    void RecenterAllVirtualObjects(Camera cam)
    {
        // Find VirtualObjects parent
        GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");
        if (virtualObjectsParent == null)
        {
            Debug.LogWarning("[VRTaskbar] VirtualObjects parent not found, falling back to primary only");
            RecenterPrimaryOnly(cam);
            return;
        }

        VRMenuFrame primary = VRMenuFrame.PrimaryInstance;
        if (primary == null)
        {
            Debug.LogWarning("[VRTaskbar] No primary VRMenuFrame found");
            return;
        }

        // Store pivot point (primary's position and rotation)
        Vector3 pivotPos = primary.transform.position;
        Quaternion pivotRot = primary.transform.rotation;

        // Collect all children and their relative transforms
        List<Transform> children = new List<Transform>();
        List<Vector3> relativePositions = new List<Vector3>();
        List<Quaternion> relativeRotations = new List<Quaternion>();

        foreach (Transform child in virtualObjectsParent.transform)
        {
            children.Add(child);
            // Calculate position relative to pivot
            Vector3 relPos = Quaternion.Inverse(pivotRot) * (child.position - pivotPos);
            relativePositions.Add(relPos);
            // Calculate rotation relative to pivot
            Quaternion relRot = Quaternion.Inverse(pivotRot) * child.rotation;
            relativeRotations.Add(relRot);
        }

        // Calculate new pivot position and rotation (facing camera)
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(
            new Vector2(pivotPos.x, pivotPos.z),
            new Vector2(camPos.x, camPos.z)
        );

        Vector3 newPivotPos = camPos + camForward * hDist;
        newPivotPos.y = pivotPos.y;
        Quaternion newPivotRot = Quaternion.LookRotation(camForward);

        // Apply new transforms to all children
        for (int i = 0; i < children.Count; i++)
        {
            Transform child = children[i];
            // Restore relative position and rotation with new pivot
            child.position = newPivotPos + newPivotRot * relativePositions[i];
            child.rotation = newPivotRot * relativeRotations[i];
        }
    }

    /// <summary>
    /// Fallback: recenter only the primary VRMenuFrame (old behavior)
    /// </summary>
    void RecenterPrimaryOnly(Camera cam)
    {
        VRMenuFrame primary = VRMenuFrame.PrimaryInstance;
        if (primary != null)
        {
            RecenterTransform(primary.transform, cam, primary.panelHeight);
        }

        if (!followPrimaryFrame)
        {
            float taskbarPhysicalHeight = logicalHeight * PixelToMeter;
            RecenterTransform(transform, cam, taskbarPhysicalHeight);
        }
    }

    /// <summary>
    /// Recenter a transform to face the camera while maintaining horizontal distance.
    /// </summary>
    void RecenterTransform(Transform target, Camera cam, float panelHeight)
    {
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 currentPos = target.position;
        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(new Vector2(currentPos.x, currentPos.z), new Vector2(camPos.x, camPos.z));

        Vector3 newPos = camPos + camForward * hDist;
        newPos.y = currentPos.y;

        target.position = newPos;
        target.rotation = Quaternion.LookRotation(camForward);
    }

    /// <summary>
    /// Add a RectTransform as content, automatically stretching to fill ContentContainer.
    /// </summary>
    public void SetContent(RectTransform content)
    {
        if (ContentContainer == null)
        {
            Debug.LogWarning("[VRTaskbar] ContentContainer not initialized");
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

            glassMat.SetFloat("_CornerRadius", 0.18f);
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

            // Glassmorphism settings
            glassMat.SetFloat("_BlurEnabled", enableGlassmorphism ? 1f : 0f);
            glassMat.SetFloat("_BlurRadius", blurIntensity);
            glassMat.SetFloat("_BlurIterations", blurQuality);
            glassMat.SetFloat("_GlassOpacity", glassOpacity);
            glassMat.SetFloat("_TintStrength", tintStrength);
            glassMat.SetFloat("_InnerGlow", innerGlow);
            glassMat.SetFloat("_Brightness", brightness);
            glassMat.SetFloat("_Saturation", saturation);

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

        float expandedW = w * (1f + 2f * expansion);
        float expandedH = h * (1f + 2f * expansion);
        BoxCollider bgCol = bgObj.AddComponent<BoxCollider>();
        bgCol.size = new Vector3(expandedW, expandedH, 0.01f);
        bgCol.center = new Vector3(0, 0, 0.05f);  // Behind canvas plane so button colliders are hit first

        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) bgObj.layer = vrLayer;

        // Calculate separator positions in UV space (0-1)
        // Separators are at the center of each spacing region
        float contentWidth = w - contentMarginLeft - contentMarginRight;
        float sep1X = (15f + section1Width + sectionSpacing / 2f) / w;
        float sep2X = (contentMarginLeft + section1Width + sectionSpacing + section2Width + sectionSpacing / 4f  * 0.5f) / w;
        Vector4 separatorPositions = new Vector4(sep1X, sep2X, 0, 0);

        CreateGlowingBorder(bgObj.transform, w, h, edgePad, separatorPositions);
        CreateFloatingDataEffects(bgObj.transform, w, h);
    }

    void CreateGlowingBorder(Transform parent, float w, float h, float edgePad, Vector4 separatorPositions = default)
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

            glowMat.SetFloat("_BorderWidth", 0.06f); // 50% thicker
            glowMat.SetFloat("_CornerRadius", 0.24f);
            glowMat.SetFloat("_EdgePadding", edgePad);
            glowMat.SetFloat("_Aspect", aspect);

            glowMat.SetFloat("_Layer1Width", 0.03f);
            glowMat.SetFloat("_Layer1Alpha", 1.5f);
            glowMat.SetFloat("_Layer2Width", 0.06f);
            glowMat.SetFloat("_Layer2Alpha", 1.0f);
            glowMat.SetFloat("_Layer3Width", 0.1275f);
            glowMat.SetFloat("_Layer3Alpha", 0.6f);
            glowMat.SetFloat("_Layer4Width", 0.27f);
            glowMat.SetFloat("_Layer4Alpha", 0.3f);

            Color cyanColor = new Color(0.3f, 1f, 1f, 1f);
            Color purpleColor = new Color(1f, 0.4f, 1f, 1f);
            glowMat.SetColor("_ColorA", cyanColor);
            glowMat.SetColor("_ColorB", purpleColor);
            glowMat.SetFloat("_GradientMode", 2f);
            glowMat.SetFloat("_GradientAngle", -10f);
            glowMat.SetFloat("_GlassAlpha", 0.02f);
            glowMat.SetColor("_GlassTint", new Color(0.9f, 0.95f, 1f, 1f));
            glowMat.SetFloat("_ShimmerSpeed", 0.1f);
            glowMat.SetFloat("_ShimmerIntensity", 0.2f);
            glowMat.SetFloat("_LightSize", 0.008f);
            glowMat.SetFloat("_LightGlow", 0.008f);

            // Separator settings (only for VRTaskbar, VRMenuFrame won't set these)
            int separatorCount = 0;
            if (separatorPositions.x > 0.01f) separatorCount++;
            if (separatorPositions.y > 0.01f) separatorCount++;
            if (separatorPositions.z > 0.01f) separatorCount++;
            if (separatorPositions.w > 0.01f) separatorCount++;

            if (separatorCount > 0)
            {
                glowMat.SetFloat("_SeparatorCount", separatorCount);
                glowMat.SetVector("_SeparatorPositions", separatorPositions);
                glowMat.SetFloat("_SeparatorWidth", 0.003f);
                glowMat.SetFloat("_SeparatorGlowWidth", 0.012f);
                glowMat.SetFloat("_SeparatorAlpha", 0.7f);
            }

            borderImg.material = glowMat;
            borderImg.sprite = GetPixelSprite();
        }
        else
        {
            Debug.LogWarning("[VRTaskbar] GlowingGlassBorder shader not found.");
        }

        borderObj.transform.SetAsLastSibling();
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

        // Use Mask with rounded sprite instead of RectMask2D for rounded corner clipping
        Image maskImage = fxContainer.AddComponent<Image>();
        maskImage.sprite = GetRoundedMaskSprite();
        maskImage.type = Image.Type.Sliced;
        maskImage.color = Color.white;
        maskImage.raycastTarget = false;

        Mask mask = fxContainer.AddComponent<Mask>();
        mask.showMaskGraphic = false; // Hide the mask image, only use for clipping

        // Fewer particles for smaller taskbar
        int particleCount = 8;
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
            float size = Random.Range(8f, 40f); // Smaller particles for taskbar
            pRT.sizeDelta = new Vector2(size, size * Random.Range(0.2f, 1.0f));

            float startX = Random.Range(-w / 2f, w / 2f);
            float startY = Random.Range(-h / 2f, h / 2f);
            pRT.anchoredPosition = new Vector2(startX, startY);

            var anim = p.AddComponent<FloatingDataAnim>();
            anim.speed = Random.Range(8f, 30f); // Slower for smaller area
            anim.range = new Vector2(w, h);
        }
    }

    void ReinitializeFloatingDataEffects()
    {
        Transform glassBg = transform.Find("GlassBackground");
        if (glassBg == null) return;

        Transform fxContainer = glassBg.Find("FX_DataStream");
        if (fxContainer == null) return;

        float w = logicalWidth;
        float h = logicalHeight;

        var anims = fxContainer.GetComponentsInChildren<FloatingDataAnim>();
        foreach (var anim in anims)
        {
            anim.range = new Vector2(w, h);
            anim.ForceReinitialize();
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

    Sprite GetRoundedMaskSprite()
    {
        if (_roundedMaskSprite != null) return _roundedMaskSprite;

        int size = 128;
        int radius = 32; // ~25% corner radius to match border
        int border = radius; // Border for 9-slice

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float alpha = 1f;

                // Check if in corner region
                int cornerX = -1, cornerY = -1;
                if (x < radius && y < radius) { cornerX = radius; cornerY = radius; }
                else if (x >= size - radius && y < radius) { cornerX = size - radius - 1; cornerY = radius; }
                else if (x < radius && y >= size - radius) { cornerX = radius; cornerY = size - radius - 1; }
                else if (x >= size - radius && y >= size - radius) { cornerX = size - radius - 1; cornerY = size - radius - 1; }

                if (cornerX >= 0)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cornerX, cornerY));
                    alpha = Mathf.Clamp01(radius + 0.5f - dist);
                }

                colors[y * size + x] = new Color(1, 1, 1, alpha);
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        _roundedMaskSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        return _roundedMaskSprite;
    }

}
