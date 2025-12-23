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
    public Vector2 spacing = new Vector2(100f, 100f);
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
                VRTaskbar.FixAllIconImportSettings();
                LoadIcons();
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

        // Save button materials (VRButtonFactory structure: Wrapper/HitArea/Visuals/Background, Border)
        int btnIndex = 0;
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("Btn_")) continue;

            Transform hitArea = child.Find("HitArea");
            if (hitArea == null) continue;

            Transform visuals = hitArea.Find("Visuals");
            if (visuals == null) continue;

            // Background material (VRButtonFactory creates Background as child of Visuals)
            Transform background = visuals.Find("Background");
            if (background != null)
            {
                var bgImg = background.GetComponent<Image>();
                if (bgImg != null && bgImg.material != null)
                {
                    var savedMat = SaveOrGetMaterial(bgImg.material, $"MainMenuBtn{btnIndex}_Bg", matDir);
                    if (savedMat != null) bgImg.material = savedMat;
                }
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
        VRTaskbar.FixAllIconImportSettings();
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
        // Re-apply pixel sprites that don't serialize (VRButtonFactory structure)
        Sprite pixelSprite = null;

        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("Btn_")) continue;

            Transform hitArea = child.Find("HitArea");
            if (hitArea == null) continue;

            Transform visuals = hitArea.Find("Visuals");
            if (visuals == null) continue;

            // Lazy create pixel sprite
            if (pixelSprite == null)
            {
                Texture2D tex = new Texture2D(2, 2);
                tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
                tex.Apply();
                pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            }

            // Background (VRButtonFactory creates Background as child of Visuals)
            Transform background = visuals.Find("Background");
            if (background != null)
            {
                var bgImg = background.GetComponent<Image>();
                if (bgImg != null && bgImg.sprite == null)
                {
                    bgImg.sprite = pixelSprite;
                }
            }

            // Border
            Transform border = visuals.Find("Border");
            if (border != null)
            {
                var borderImg = border.GetComponent<Image>();
                if (borderImg != null && borderImg.sprite == null)
                {
                    borderImg.sprite = pixelSprite;
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
            btn.onClick.AddListener(actions[index]);
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
        if (iconRemote == null) iconRemote = VRTaskbar.LoadIcon("remote");
        if (iconBrowser == null) iconBrowser = VRTaskbar.LoadIcon("browser");
        if (iconMedia == null) iconMedia = VRTaskbar.LoadIcon("media");
        if (iconFiles == null) iconFiles = VRTaskbar.LoadIcon("files");
        if (iconSettings == null) iconSettings = VRTaskbar.LoadIcon("settings");
        if (iconQuit == null) iconQuit = VRTaskbar.LoadIcon("quit");
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

    void UpdateSpacingFromMenuFrame()
    {
        if (_menuFrame == null && !ValidateParent()) return;
        if (_menuFrame == null) return;

        spacing.x = (_menuFrame.contentMarginLeft + _menuFrame.contentMarginRight) / 2f - 25f;
        spacing.y = (_menuFrame.contentMarginTop + _menuFrame.contentMarginBottom) / 2f + 25f;
    }

    void BuildGrid()
    {
        if (!ValidateParent()) return;

        // Clear existing
        ClearGrid();

        // Update spacing from VRMenuFrame's content margins
        UpdateSpacingFromMenuFrame();

        // Setup RectTransform to stretch fill parent (ContentContainer)
        RectTransform rt = GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;

        // Force layout rebuild to get actual rect size
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        // Get actual container size from parent RectTransform
        RectTransform parentRT = transform.parent?.GetComponent<RectTransform>();
        float containerW, containerH;

        if (parentRT != null && parentRT.rect.width > 0 && parentRT.rect.height > 0)
        {
            containerW = parentRT.rect.width;
            containerH = parentRT.rect.height;
        }
        else
        {
            // Fallback to VRMenuFrame dimensions if parent rect not ready
            float frameWidth = _menuFrame.logicalWidth;
            float frameHeight = (frameWidth / _menuFrame.panelWidth) * _menuFrame.panelHeight;
            float statusBarHeight = frameHeight * 0.125f;
            containerW = frameWidth - _menuFrame.contentMarginLeft - _menuFrame.contentMarginRight;
            containerH = frameHeight - statusBarHeight - _menuFrame.contentMarginTop - _menuFrame.contentMarginBottom;
        }

        // Add GridLayoutGroup to this GameObject
        _gridLayout = gameObject.AddComponent<GridLayoutGroup>();

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

        // Update spacing from VRMenuFrame's content margins
        UpdateSpacingFromMenuFrame();

        _gridLayout.spacing = spacing;
        _gridLayout.constraintCount = columns;

        // Get actual container size from parent RectTransform
        RectTransform parentRT = transform.parent?.GetComponent<RectTransform>();
        float containerW, containerH;

        if (parentRT != null && parentRT.rect.width > 0 && parentRT.rect.height > 0)
        {
            containerW = parentRT.rect.width;
            containerH = parentRT.rect.height;
        }
        else
        {
            // Fallback to VRMenuFrame dimensions
            float frameWidth = _menuFrame.logicalWidth;
            float frameHeight = (frameWidth / _menuFrame.panelWidth) * _menuFrame.panelHeight;
            float statusBarHeight = frameHeight * 0.125f;
            containerW = frameWidth - _menuFrame.contentMarginLeft - _menuFrame.contentMarginRight;
            containerH = frameHeight - statusBarHeight - _menuFrame.contentMarginTop - _menuFrame.contentMarginBottom;
        }

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

    // --- BUTTON CREATION (using VRButtonFactory) ---

    void CreateGridButton(string label, Sprite icon, Color btnColor, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var config = new VRButtonFactory.ButtonConfig
        {
            label = label,
            icon = icon,
            themeColor = btnColor,
            width = size.x,
            height = size.y,
            fontSize = fontSize,
            font = customFont,
            popAmount = 0.025f
        };

        VRButtonFactory.CreateButton(transform, config, onClick);
    }

    // --- MENU ACTIONS ---

    void OpenRemoteDesktop()
    {
        SwitchToRemoteMenu();
    }

    public void SwitchToRemoteMenu()
    {
        // Get ContentContainer from VRMenuFrame
        Transform contentContainer = _menuFrame.ContentContainer;
        if (contentContainer == null)
        {
            Debug.LogError("[VRMainMenu] ContentContainer not found!");
            return;
        }

        // Create VRRemoteMenu as sibling in ContentContainer
        GameObject remoteObj = new GameObject("VRRemoteMenu");
        remoteObj.transform.SetParent(contentContainer, false);

        // Setup RectTransform to fill ContentContainer
        RectTransform remoteRT = remoteObj.AddComponent<RectTransform>();
        remoteRT.anchorMin = Vector2.zero;
        remoteRT.anchorMax = Vector2.one;
        remoteRT.offsetMin = Vector2.zero;
        remoteRT.offsetMax = Vector2.zero;

        VRRemoteMenu remoteMenu = remoteObj.AddComponent<VRRemoteMenu>();
        remoteMenu.customFont = customFont;
        remoteMenu.themeColor = buttonColors[0];

        // Pass ContentContainer's actual size
        Rect containerRect = contentContainer.GetComponent<RectTransform>().rect;
        remoteMenu.BuildUI(remoteObj.transform, this, containerRect.width, containerRect.height);

        // Destroy this VRMainMenu
        Destroy(gameObject);
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
