using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// VR Main Menu - Must be a child of VRMenuFrame.
/// Creates 6-button grid layout automatically on attach.
/// GridLayoutGroup is applied directly to this GameObject.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class VRMainMenu : MonoBehaviour
{
    [Header("Button Configuration")]
    public Color[] buttonColors = new Color[] {
        new Color(0.0f, 0.9019607843137255f, 1.0f, 1.0f),
        new Color(0.0f, 0.9019607843137255f, 1.0f, 1.0f),
        new Color(0.7568627450980392f, 0.3568627450980392f, 1.0f, 1.0f),
        new Color(0.0f, 0.9019607843137255f, 1.0f, 1.0f),
        new Color(0.7568627450980392f, 0.3568627450980392f, 1.0f, 1.0f),
        new Color(0.0f, 0.9019607843137255f, 1.0f, 1.0f),
    };

    public int fontSize = 42;

    [Header("Grid Layout")]
    public int columns = 3;
    public int rows = 2;
    public Vector2 spacing = new Vector2(180f, 150f);
    public float targetAspect = 1.4f;

    [Header("Typography")]
    public TMP_FontAsset customFont;

    [Header("Icons")]
    public Sprite iconRemote;
    public Sprite iconBrowser;
    public Sprite iconMedia;
    public Sprite iconFiles;
    public Sprite iconSettings;
    public Sprite iconQuit;

    // Internal
    private VRMenuFrame _menuFrame;
    private GridLayoutGroup _gridLayout;
    private Sprite _pixelSprite;
    private bool _isBuilt = false;
    private Vector2 _currentCellSize;

    // --- VALIDATION: Must be child of VRMenuFrame ---

#if UNITY_EDITOR
    void Reset()
    {
        // Called when component is first added
        if (!ValidateParent())
        {
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    EditorUtility.DisplayDialog("VRMainMenu",
                        "VRMainMenu must be placed inside a VRMenuFrame's ContentContainer.\n\n" +
                        "Please add this component to an object that is a child of VRMenuFrame.",
                        "OK");
                    DestroyImmediate(this);
                }
            };
            return;
        }

        // Auto-build on attach
        EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                LoadIcons();
                FixIconImportSettings();
                BuildGrid();
            }
        };
    }

    void OnValidate()
    {
        if (!Application.isPlaying && _isBuilt)
        {
            EditorApplication.delayCall += () =>
            {
                if (this != null && _gridLayout != null)
                {
                    UpdateGridLayout();
                }
            };
        }
    }

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

        string prefabPath = $"{prefabDir}/VRMainMenu.prefab";

        if (PrefabUtility.IsPartOfPrefabInstance(gameObject))
        {
            PrefabUtility.UnpackPrefabInstance(gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        PrefabUtility.SaveAsPrefabAssetAndConnect(gameObject, prefabPath, InteractionMode.UserAction);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[VRMainMenu] Prefab saved to: {prefabPath}");
    }

    void SaveMaterialsAsAssets()
    {
        string matDir = "Assets/VR-Workspace/Materials/UIComponents";
        if (!AssetDatabase.IsValidFolder("Assets/VR-Workspace/Materials"))
            AssetDatabase.CreateFolder("Assets/VR-Workspace", "Materials");
        if (!AssetDatabase.IsValidFolder(matDir))
            AssetDatabase.CreateFolder("Assets/VR-Workspace/Materials", "UIComponents");

        // Save button materials
        int btnIndex = 0;
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("Btn_")) continue;

            Transform hitArea = child.Find("HitArea");
            if (hitArea == null) continue;

            Transform visuals = hitArea.Find("Visuals");
            if (visuals == null) continue;

            // Background material
            var bgImg = visuals.GetComponent<Image>();
            if (bgImg != null && bgImg.material != null)
            {
                var savedMat = SaveOrGetMaterial(bgImg.material, $"MainMenuBtn{btnIndex}_Bg", matDir);
                if (savedMat != null) bgImg.material = savedMat;
            }

            // Border material
            Transform border = visuals.Find("Border");
            if (border != null)
            {
                var borderImg = border.GetComponent<Image>();
                if (borderImg != null && borderImg.material != null)
                {
                    var savedMat = SaveOrGetMaterial(borderImg.material, $"MainMenuBtn{btnIndex}_Border", matDir);
                    if (savedMat != null)
                    {
                        borderImg.material = savedMat;
                        var ripple = border.GetComponent<VRButtonRipple>();
                        if (ripple != null) ripple.Initialize(savedMat, borderImg);
                    }
                }
            }

            btnIndex++;
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

    void FixIconImportSettings()
    {
        string[] mainMenuIcons = {
            "icon_remote", "icon_browser", "icon_media",
            "icon_files", "icon_settings", "icon_quit", "icon_wifi", "icon_signal"
        };

        foreach (var name in mainMenuIcons)
        {
            FixSingleIconImport($"Assets/VR-Workspace/Resources/MainMenu/{name}.png");
        }

        string[] remoteMenuIcons = {
            "icon_monitor", "icon_resolution", "icon_bitrate", "icon_fps",
            "icon_back", "icon_qr"
        };

        foreach (var name in remoteMenuIcons)
        {
            FixSingleIconImport($"Assets/VR-Workspace/Resources/RemoteMenu/{name}.png");
        }
    }

    void FixSingleIconImport(string path)
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
                if (changed) importer.SaveAndReimport();
            }
        }
        catch { }
    }
#endif

    bool ValidateParent()
    {
        // Find VRMenuFrame in parents
        _menuFrame = GetComponentInParent<VRMenuFrame>();
        if (_menuFrame == null)
        {
            Debug.LogError("[VRMainMenu] Must be placed inside a VRMenuFrame!");
            return false;
        }
        return true;
    }

    void Awake()
    {
        if (!ValidateParent())
        {
            enabled = false;
            return;
        }
    }

    void Start()
    {
        if (!Application.isPlaying) return;

        // Runtime: check if already built in editor
        if (_isBuilt && _gridLayout != null) return;

        // Try to initialize from existing prefab content
        if (TryInitializeExisting()) return;

        LoadIcons();
#if UNITY_EDITOR
        FixIconImportSettings();
#endif
        BuildGrid();
    }

    bool TryInitializeExisting()
    {
        // Check if we have existing buttons (loaded from prefab)
        _gridLayout = GetComponent<GridLayoutGroup>();
        if (_gridLayout == null) return false;

        int buttonCount = 0;
        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Btn_")) buttonCount++;
        }

        if (buttonCount >= 6)
        {
            _isBuilt = true;
            ReapplyRuntimeSprites();
            SetupButtonListeners();
            SetupButtonColliders();
            Debug.Log("[VRMainMenu] Initialized from existing prefab content");
            return true;
        }

        return false;
    }

    void ReapplyRuntimeSprites()
    {
        // Re-apply pixel sprites that don't serialize
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("Btn_")) continue;

            Transform hitArea = child.Find("HitArea");
            if (hitArea == null) continue;

            Transform visuals = hitArea.Find("Visuals");
            if (visuals == null) continue;

            // Background
            var bgImg = visuals.GetComponent<Image>();
            if (bgImg != null && bgImg.sprite == null)
            {
                bgImg.sprite = GetPixelSprite();
            }

            // Border
            Transform border = visuals.Find("Border");
            if (border != null)
            {
                var borderImg = border.GetComponent<Image>();
                if (borderImg != null && borderImg.sprite == null)
                {
                    borderImg.sprite = GetPixelSprite();
                }
            }
        }
    }

    void SetupButtonColliders()
    {
        // Fix collider size and layer for VRGazeReticle when loading from prefab
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");

        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("Btn_")) continue;

            Transform hitArea = child.Find("HitArea");
            if (hitArea == null) continue;

            BoxCollider col = hitArea.GetComponent<BoxCollider>();
            if (col == null) continue;

            // Set layer
            if (vrLayer != -1) hitArea.gameObject.layer = vrLayer;

            // Fix collider size if it's wrong
            RectTransform parentRT = child.GetComponent<RectTransform>();
            if (parentRT != null)
            {
                Vector2 size = parentRT.rect.size;
                if (size.x > 0 && size.y > 0 && (col.size.x < 100f || col.size.z > 1f))
                {
                    col.size = new Vector3(size.x, size.y, 0.1f);
                    col.center = new Vector3(0, 0, -0.1f);
                }
            }
        }
    }

    void SetupButtonListeners()
    {
        // Re-register button click listeners when loading from prefab
        string[] buttonNames = { "Btn_Remote Desktop", "Btn_Browser", "Btn_Media", "Btn_Files", "Btn_Settings", "Btn_Quit" };
        UnityEngine.Events.UnityAction[] actions = {
            () => OpenRemoteDesktop(),
            () => Debug.Log("Browser"),
            () => Debug.Log("Media"),
            () => Debug.Log("Files"),
            () => Debug.Log("Settings"),
            () => Debug.Log("Quit")
        };

        for (int i = 0; i < buttonNames.Length; i++)
        {
            Transform btnTransform = transform.Find(buttonNames[i]);
            if (btnTransform == null) continue;

            Transform hitArea = btnTransform.Find("HitArea");
            if (hitArea == null) continue;

            Button btn = hitArea.GetComponent<Button>();
            if (btn == null) continue;

            btn.onClick.RemoveAllListeners();
            int index = i;
            btn.onClick.AddListener(() => StartCoroutine(DelayedAction(actions[index], 0.25f)));
        }
    }

    void OnEnable()
    {
        if (!Application.isPlaying)
        {
            // Editor: re-initialize if needed
            if (!_isBuilt || _gridLayout == null)
            {
                ValidateParent();
                LoadIcons();
                BuildGrid();
            }
        }
    }

    void LoadIcons()
    {
        if (iconRemote == null) iconRemote = Resources.Load<Sprite>("MainMenu/icon_remote");
        if (iconBrowser == null) iconBrowser = Resources.Load<Sprite>("MainMenu/icon_browser");
        if (iconMedia == null) iconMedia = Resources.Load<Sprite>("MainMenu/icon_media");
        if (iconFiles == null) iconFiles = Resources.Load<Sprite>("MainMenu/icon_files");
        if (iconSettings == null) iconSettings = Resources.Load<Sprite>("MainMenu/icon_settings");
        if (iconQuit == null) iconQuit = Resources.Load<Sprite>("MainMenu/icon_quit");
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

    // --- MAIN BUILD ---

    [ContextMenu("Rebuild Grid")]
    public void Rebuild()
    {
        ClearGrid();
        LoadIcons();
        BuildGrid();
    }

    void ClearGrid()
    {
        // Remove all button children
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        // Remove GridLayoutGroup if exists
        _gridLayout = GetComponent<GridLayoutGroup>();
        if (_gridLayout != null)
        {
            if (Application.isPlaying)
                Destroy(_gridLayout);
            else
                DestroyImmediate(_gridLayout);
        }
        _gridLayout = null;
        _isBuilt = false;
    }

    void BuildGrid()
    {
        if (!ValidateParent()) return;

        // Clear existing
        ClearGrid();

        // Setup RectTransform
        RectTransform rt = GetComponent<RectTransform>();

        // Get container size from VRMenuFrame
        float containerWidth = _menuFrame.logicalWidth;
        float containerHeight = (_menuFrame.logicalWidth / _menuFrame.panelWidth) * _menuFrame.panelHeight;

        // Account for status bar
        float statusBarHeight = containerHeight * 0.125f;
        float contentHeight = containerHeight - statusBarHeight;

        float w = containerWidth;
        float h = contentHeight;

        // Margins
        float marginPX = 40f;
        float topMargin = 40f;
        float xMin = marginPX / w;
        float xMax = 1f - xMin;
        float yMin = marginPX / h;
        float yMax = 1f - (topMargin / h);

        // Setup anchors with margins
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;

        // Add GridLayoutGroup to this GameObject
        _gridLayout = gameObject.AddComponent<GridLayoutGroup>();

        float containerW = w * (xMax - xMin);
        float containerH = h * (yMax - yMin);

        float totalSpacingW = spacing.x * (columns - 1);
        float totalSpacingH = spacing.y * (rows - 1);

        float maxW = (containerW - totalSpacingW) / columns;
        float maxH = (containerH - totalSpacingH) / rows;

        float finalH = maxH;
        float finalW = finalH * targetAspect;

        if (finalW > maxW)
        {
            finalW = maxW;
            finalH = finalW / targetAspect;
        }

        _currentCellSize = new Vector2(finalW, finalH);

        _gridLayout.cellSize = _currentCellSize;
        _gridLayout.spacing = spacing;
        _gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        _gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        _gridLayout.childAlignment = TextAnchor.MiddleCenter;
        _gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _gridLayout.constraintCount = columns;

        // --- CREATE 6 BUTTONS ---
        CreateGridButton("Remote Desktop", iconRemote, buttonColors[0], _currentCellSize, () => OpenRemoteDesktop());
        CreateGridButton("Browser", iconBrowser, buttonColors[1], _currentCellSize, () => Debug.Log("Browser"));
        CreateGridButton("Media", iconMedia, buttonColors[2], _currentCellSize, () => Debug.Log("Media"));
        CreateGridButton("Files", iconFiles, buttonColors[3], _currentCellSize, () => Debug.Log("Files"));
        CreateGridButton("Settings", iconSettings, buttonColors[4], _currentCellSize, () => Debug.Log("Settings"));
        CreateGridButton("Quit", iconQuit, buttonColors[5], _currentCellSize, () => Debug.Log("Quit"));

        _isBuilt = true;
        Debug.Log("[VRMainMenu] Grid built with 6 buttons");
    }

    void UpdateGridLayout()
    {
        if (_gridLayout == null) return;
        if (_menuFrame == null && !ValidateParent()) return;

        _gridLayout.spacing = spacing;
        _gridLayout.constraintCount = columns;

        // Recalculate cell size
        float containerWidth = _menuFrame.logicalWidth;
        float containerHeight = (_menuFrame.logicalWidth / _menuFrame.panelWidth) * _menuFrame.panelHeight;
        float statusBarHeight = containerHeight * 0.125f;
        float h = containerHeight - statusBarHeight;
        float w = containerWidth;

        float marginPX = 40f;
        float topMargin = 40f;
        float xMin = marginPX / w;
        float xMax = 1f - xMin;
        float yMin = marginPX / h;
        float yMax = 1f - (topMargin / h);

        float containerW = w * (xMax - xMin);
        float containerH = h * (yMax - yMin);

        float totalSpacingW = spacing.x * (columns - 1);
        float totalSpacingH = spacing.y * (rows - 1);

        float maxW = (containerW - totalSpacingW) / columns;
        float maxH = (containerH - totalSpacingH) / rows;

        float finalH = maxH;
        float finalW = finalH * targetAspect;

        if (finalW > maxW)
        {
            finalW = maxW;
            finalH = finalW / targetAspect;
        }

        _gridLayout.cellSize = new Vector2(finalW, finalH);
    }

    // --- BUTTON CREATION ---

    void CreateGridButton(string label, Sprite icon, Color btnColor, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject wrapper = new GameObject("Btn_" + label);
        wrapper.transform.SetParent(transform, false);
        RectTransform wrapperRT = wrapper.AddComponent<RectTransform>();

        CreateButtonVisuals(wrapper, label, icon, btnColor, onClick, size);
    }

    void CreateButtonVisuals(GameObject parent, string label, Sprite icon, Color btnColor, UnityEngine.Events.UnityAction onClick, Vector2 size)
    {
        // 1. Hit Area
        GameObject btnHitObj = new GameObject("HitArea");
        btnHitObj.transform.SetParent(parent.transform, false);
        RectTransform hitRT = btnHitObj.AddComponent<RectTransform>();
        hitRT.anchorMin = Vector2.zero;
        hitRT.anchorMax = Vector2.one;
        hitRT.offsetMin = Vector2.zero;
        hitRT.offsetMax = Vector2.zero;

        Image hitImg = btnHitObj.AddComponent<Image>();
        hitImg.color = Color.clear;

        // Collider - use logical pixels, very thin
        BoxCollider col = btnHitObj.AddComponent<BoxCollider>();
        col.size = new Vector3(size.x, size.y, 0.1f);
        col.center = new Vector3(0, 0, -0.1f); // Slightly forward so buttons are in front of background

        // Set layer to VirtualObjects for VRGazeReticle raycast
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1) btnHitObj.layer = vrLayer;

        // 2. Visual Root
        GameObject visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(btnHitObj.transform, false);
        RectTransform visRT = visualRoot.AddComponent<RectTransform>();

        float expansion = 0.12f;
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = Vector2.zero;
        visRT.offsetMax = Vector2.zero;

        // Background
        Image bg = visualRoot.AddComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.type = Image.Type.Simple;
        bg.raycastTarget = false;

        Color baseTint = new Color(btnColor.r, btnColor.g, btnColor.b, 0.08f);

        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", 0.12f);
            glassMat.SetFloat("_EdgePadding", 0.12f);
            glassMat.SetFloat("_Aspect", size.x / size.y);

            glassMat.SetColor("_ColorA", new Color(btnColor.r, btnColor.g, btnColor.b, 0.12f));
            glassMat.SetColor("_ColorB", new Color(btnColor.r, btnColor.g, btnColor.b, 0.04f));
            glassMat.SetFloat("_GlassAlpha", 0.075f);

            bg.material = glassMat;
            bg.color = Color.white;
        }
        else
        {
            bg.color = baseTint;
        }

        // Button Interaction
        Button btn = btnHitObj.AddComponent<Button>();
        btn.targetGraphic = bg;
        if (Application.isPlaying)
        {
            btn.onClick.AddListener(() => StartCoroutine(DelayedAction(onClick, 0.25f)));
        }

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.5f);
        cb.pressedColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.7f);
        cb.fadeDuration = 0.1f;
        btn.colors = cb;

        // --- BORDER ---
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
            glowMat.SetFloat("_Aspect", size.x / size.y);
            glowMat.SetFloat("_EdgePadding", 0.12f);

            Color borderGlowCol = Color.Lerp(btnColor, Color.white, 0.75f);
            glowMat.SetColor("_GlowColor", borderGlowCol);

            glowMat.SetFloat("_BorderWidth", 0.005f);
            glowMat.SetFloat("_GlowWidth", 0.03f);
            glowMat.SetFloat("_GlowIntensity", 2.5f);
            glowMat.SetFloat("_CornerRadius", 0.12f);
            glowMat.SetFloat("_PulseEnabled", 0f);

            borderImg.material = glowMat;
            borderImg.sprite = GetPixelSprite();

            borderObj.AddComponent<VRButtonRipple>().Initialize(glowMat, borderImg);
        }

        // --- CONTENT ---
        GameObject content = new GameObject("Content");
        content.transform.SetParent(visualRoot.transform, false);
        RectTransform cRT = content.AddComponent<RectTransform>();
        cRT.anchorMin = Vector2.zero;
        cRT.anchorMax = Vector2.one;
        cRT.sizeDelta = Vector2.zero;

        // Icon
        if (icon != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(content.transform, false);
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            iconImg.color = Color.Lerp(btnColor, Color.white, 0.9f);

            // Glow effects
            Color glowCol = Color.Lerp(btnColor, Color.white, 0.7f);
            glowCol.a = 0.4f;
            float s1 = 2f;

            iconObj.AddComponent<Shadow>().effectColor = glowCol;
            iconObj.GetComponent<Shadow>().effectDistance = new Vector2(s1, -s1);

            iconObj.AddComponent<Shadow>().effectColor = glowCol;
            iconObj.GetComponents<Shadow>()[1].effectDistance = new Vector2(-s1, s1);

            Color bloomCol = Color.Lerp(btnColor, Color.white, 0.8f);
            bloomCol.a = 0.15f;
            float s2 = 5f;

            iconObj.AddComponent<Shadow>().effectColor = bloomCol;
            iconObj.GetComponents<Shadow>()[2].effectDistance = new Vector2(s2, -s2);

            iconObj.AddComponent<Shadow>().effectColor = bloomCol;
            iconObj.GetComponents<Shadow>()[3].effectDistance = new Vector2(-s2, s2);

            RectTransform iRT = iconObj.GetComponent<RectTransform>();
            iRT.anchorMin = new Vector2(0.35f, 0.42f);
            iRT.anchorMax = new Vector2(0.65f, 0.72f);
            iRT.offsetMin = Vector2.zero;
            iRT.offsetMax = Vector2.zero;
        }

        // Text
        GameObject tObj = CreateText(content.transform, label, Vector2.zero, fontSize, Color.white, true);
        RectTransform tRT = tObj.GetComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0f, 0.18f);
        tRT.anchorMax = new Vector2(1f, 0.42f);
        tRT.offsetMin = Vector2.zero;
        tRT.offsetMax = Vector2.zero;

        // Animation
        var anim = btnHitObj.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visualRoot.transform;
        anim.popAmount = 0.05f;
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
        rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
        rt.sizeDelta = new Vector2(400, 100);

        return go;
    }

    // --- DELAYED ACTION ---

    IEnumerator DelayedAction(UnityEngine.Events.UnityAction action, float delay)
    {
        yield return new WaitForSeconds(delay);
        action?.Invoke();
    }

    // --- MENU ACTIONS ---

    void OpenRemoteDesktop()
    {
        SwitchToRemoteMenu();
    }

    public void SwitchToRemoteMenu()
    {
        // Clear current grid
        ClearGrid();

        // Create Remote Menu
        GameObject remoteObj = new GameObject("VRRemoteMenu_Logic");
        remoteObj.transform.SetParent(transform, false);
        VRRemoteMenu remoteMenu = remoteObj.AddComponent<VRRemoteMenu>();
        remoteMenu.customFont = customFont;
        remoteMenu.themeColor = buttonColors[0];

        remoteMenu.BuildUI(transform, this);
    }

    public void ReturnToMainMenu()
    {
        // Clear and rebuild
        ClearGrid();
        BuildGrid();
    }

    public void ShowMainMenu()
    {
        ReturnToMainMenu();
    }

    // --- LAYER UTILITY ---

    void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }
}
