using UnityEngine;
using UnityEngine.UI;
using TMPro; 
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class VRMainMenu : MonoBehaviour
{
    [Header("Configuration")]
    public float panelWidth = 1.6f;
    public float panelHeight = 0.9f;
    public WorldPanelPlus menuPanelPrefab; 
    public RemotePlayMainMenu remotePlayLogic; 

    [Header("Visual Style")]
    public Color glassColor = new Color(0.0f, 0.05f, 0.15f, 0.6f); // Darker Blue Glass
    
    // Improved Neon Palette
    public Color[] buttonColors = new Color[] {
        new Color(0.0f, 0.9f, 1.0f, 1.0f), // Cyan Neon
        new Color(0.7f, 0.2f, 1.0f, 1.0f), // Purple Neon
        new Color(1.0f, 0.2f, 0.6f, 1.0f), // Pink Neon
        new Color(0.2f, 1.0f, 0.4f, 1.0f), // Green Neon
        new Color(1.0f, 0.6f, 0.0f, 1.0f), // Orange Neon
        new Color(1.0f, 0.2f, 0.2f, 1.0f)  // Red Neon
    };
    
    public Color buttonBorderColor = new Color(0.0f, 0.8f, 1.0f, 0.8f); // Default Neon Cyan
    public int fontSize = 32;

    [Header("Typography")]
    public TMP_FontAsset customFont;

    [Header("Icons")]
    public Sprite iconRemote;
    public Sprite iconBrowser;
    public Sprite iconMedia;
    public Sprite iconFiles;
    public Sprite iconSettings;
    public Sprite iconQuit;

    private WorldPanelPlus _panel;
    private Canvas _canvas;
    private GameObject _gridContainer;
    private Sprite _roundedSprite;

    [ContextMenu("Rebuild UI")]
    public void ManualRebuild()
    {
        LoadIcons();
#if UNITY_EDITOR
        FixIconImportSettings();
#endif
        if (_canvas) DestroyImmediate(_canvas.gameObject);
        if (_gridContainer) DestroyImmediate(_gridContainer);
        var existingCanvas = transform.Find("MenuCanvas");
        if (existingCanvas) DestroyImmediate(existingCanvas.gameObject);
        if (_panel && _panel.transform.Find("MenuCanvas")) 
            DestroyImmediate(_panel.transform.Find("MenuCanvas").gameObject);

        BuildInterface();
    }

    void Start()
    {
        LoadIcons();
        BuildInterface();
    }

#if UNITY_EDITOR
    void FixIconImportSettings()
    {
        string[] iconNames = { 
            "icon_remote", "icon_browser", "icon_media", 
            "icon_files", "icon_settings", "icon_quit",
            "icon_extra_1", "icon_extra_2", "icon_extra_3"
        };
        foreach (var name in iconNames)
        {
            string path = $"Assets/VR-Workspace/Resources/MainMenu/{name}.png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                Debug.Log($"[VRMainMenu] Auto-fixing texture import for {name}...");
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
            }
        }
    }
#endif

    void LoadIcons()
    {
        if (iconRemote == null) iconRemote = Resources.Load<Sprite>("MainMenu/icon_remote");
        if (iconBrowser == null) iconBrowser = Resources.Load<Sprite>("MainMenu/icon_browser");
        if (iconMedia == null) iconMedia = Resources.Load<Sprite>("MainMenu/icon_media");
        if (iconFiles == null) iconFiles = Resources.Load<Sprite>("MainMenu/icon_files");
        if (iconSettings == null) iconSettings = Resources.Load<Sprite>("MainMenu/icon_settings");
        if (iconQuit == null) iconQuit = Resources.Load<Sprite>("MainMenu/icon_quit");
    }

    Sprite GetRoundedSprite()
    {
        if (_roundedSprite != null) return _roundedSprite;

        int size = 128; 
        int radius = 35; 
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inCorner = false;
                float dist = 0f;

                if (x < radius && y < radius) { 
                    dist = Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius));
                    inCorner = true;
                }
                else if (x > size - radius && y < radius) { 
                    dist = Vector2.Distance(new Vector2(x, y), new Vector2(size - radius, radius));
                    inCorner = true;
                }
                else if (x < radius && y > size - radius) { 
                    dist = Vector2.Distance(new Vector2(x, y), new Vector2(radius, size - radius));
                    inCorner = true;
                }
                else if (x > size - radius && y > size - radius) { 
                    dist = Vector2.Distance(new Vector2(x, y), new Vector2(size - radius, size - radius));
                    inCorner = true;
                }

                if (inCorner)
                {
                    if (dist > radius) colors[y * size + x] = Color.clear;
                    else if (dist > radius - 1f) {
                        float alpha = 1f - (dist - (radius - 1f));
                        colors[y * size + x] = new Color(1, 1, 1, alpha);
                    }
                    else colors[y * size + x] = Color.white;
                }
                else
                {
                    colors[y * size + x] = Color.white;
                }
            }
        }
        
        tex.SetPixels(colors);
        tex.Apply();
        _roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return _roundedSprite;
    }

    void BuildInterface()
    {
        Debug.Log("[VRMainMenu] Building Polished Neon Grid...");

        if (menuPanelPrefab != null)
        {
            var go = Instantiate(menuPanelPrefab.gameObject, transform);
            _panel = go.GetComponent<WorldPanelPlus>();
        }
        else
        {
            _panel = GetComponent<WorldPanelPlus>();
            if (_panel == null)
            {
                var go = new GameObject("MainMenu_Panel");
                go.transform.SetParent(transform, false);
                _panel = go.AddComponent<WorldPanelPlus>();
            }
            _panel.width = panelWidth;
            _panel.height = panelHeight;
            _panel.Rebuild();
        }

        GameObject canvasGO = new GameObject("MenuCanvas");
        canvasGO.transform.SetParent(_panel.transform, false);

        float logicalWidth = 1920f;
        float logicalHeight = (logicalWidth / panelWidth) * panelHeight;

        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        
        RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(logicalWidth, logicalHeight);
        float scaleFactor = panelWidth / logicalWidth;
        canvasRT.localScale = new Vector3(scaleFactor, scaleFactor, 1f);
        canvasRT.localPosition = new Vector3(0, 0, -0.05f); 

        _canvas.gameObject.AddComponent<GraphicRaycaster>();
        
         CreateGlassPanel(canvasGO.transform, logicalWidth, logicalHeight);
        CreateText(canvasGO.transform, "VR WORKSPACE", new Vector2(0, logicalHeight/2 - 80), 60, Color.cyan, true); // Update Title to Cyan

        CreateUniformGrid(canvasGO.transform, logicalWidth, logicalHeight);
    }
    
    void CreateGlassPanel(Transform parent, float w, float h)
    {
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);
        Image img = bgObj.AddComponent<Image>();
        img.color = glassColor;
        img.type = Image.Type.Sliced;
        img.sprite = GetRoundedSprite();
        
        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; 
        rt.sizeDelta = Vector2.zero; rt.localScale = Vector3.one;
        rt.localPosition = Vector3.zero;
        
        // Neon Cyan/Purple Outline for Main Panel
        var outline = bgObj.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(0.0f, 0.8f, 1.0f, 0.6f); // Neon Cyan
        outline.effectDistance = new Vector2(3, -3);
        
        var shadow = bgObj.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0.5f, 0.0f, 1.0f, 0.4f); // Subtle Purple Glow
        shadow.effectDistance = new Vector2(5, -5);
    }

    void CreateUniformGrid(Transform parent, float w, float h)
    {
        _gridContainer = new GameObject("ButtonGrid");
        _gridContainer.transform.SetParent(parent, false);

        RectTransform rt = _gridContainer.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.05f, 0.15f); 
        rt.anchorMax = new Vector2(0.95f, 0.85f); 
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        GridLayoutGroup grid = _gridContainer.AddComponent<GridLayoutGroup>();
        
        // Slightly larger than 320 to fill space better
        float side = 330; 
        grid.cellSize = new Vector2(side, side);
        grid.spacing = new Vector2(40, 40);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3; 

        CreateGridButton("Remote Desktop", iconRemote, buttonColors[0], () => OpenRemoteDesktop());
        CreateGridButton("Browser", iconBrowser, buttonColors[1], () => Debug.Log("Browser"));
        CreateGridButton("Media", iconMedia, buttonColors[2], () => Debug.Log("Media"));
        CreateGridButton("Files", iconFiles, buttonColors[3], () => Debug.Log("Files"));
        CreateGridButton("Settings", iconSettings, buttonColors[4], () => Debug.Log("Settings"));
        CreateGridButton("Quit", iconQuit, buttonColors[5], () => Application.Quit());
    }

    void CreateGridButton(string label, Sprite icon, Color btnColor, UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnObj = new GameObject("Btn_" + label);
        btnObj.transform.SetParent(_gridContainer.transform, false);

        // 1. Background (Glassy, nearly transparent)
        Image bg = btnObj.AddComponent<Image>();
        bg.sprite = GetRoundedSprite();
        bg.type = Image.Type.Sliced;
        // Low alpha for the "inside" of the button to look like glass
        bg.color = new Color(btnColor.r, btnColor.g, btnColor.b, 0.15f);

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(onClick);
        
        // Remove default ColorBlock transitions to let our custom script handle it, 
        // or set them to be subtle. Let's just use ColorTint for the BG slightly.
        ColorBlock cb = btn.colors;
        cb.normalColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.15f);
        cb.highlightedColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.3f); // Slightly brighter gloss on hover
        cb.pressedColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.5f);
        cb.colorMultiplier = 1f;
        btn.colors = cb;
        
        // 2. Neon Boarder (Outline)
        var outline = btnObj.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = btnColor; // The neon color
        outline.effectDistance = new Vector2(3, -3); // Thicker neon rim

        // 3. Glow (Shadow) - Subtle matching glow
        var glow = btnObj.AddComponent<UnityEngine.UI.Shadow>();
        glow.effectColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.6f);
        glow.effectDistance = new Vector2(0, -6);

        // Content Container
        GameObject content = new GameObject("Content");
        content.transform.SetParent(btnObj.transform, false);
        RectTransform cRT = content.AddComponent<RectTransform>();
        cRT.anchorMin = Vector2.zero; cRT.anchorMax = Vector2.one;
        cRT.sizeDelta = Vector2.zero;

        // 4. Icon (Same color as border but whiter)
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(content.transform, false);
        Image iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = icon;
        // Lerp towards white for the "Whiter" look
        iconImg.color = Color.Lerp(btnColor, Color.white, 0.75f);
        
        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        // Icon Size
        iconRT.anchorMin = new Vector2(0.25f, 0.35f);
        iconRT.anchorMax = new Vector2(0.75f, 0.85f);
        iconRT.offsetMin = Vector2.zero; iconRT.offsetMax = Vector2.zero;
        iconImg.preserveAspect = true;

        // 5. Text
        CreateText(content.transform, label, Vector2.zero, fontSize, Color.white);
        var txtRT = content.transform.Find("TextTMP").GetComponent<RectTransform>();
        txtRT.anchorMin = new Vector2(0, 0.05f);
        txtRT.anchorMax = new Vector2(1, 0.25f);

        // 6. Interactive Animation (Hover Pop & Border White)
        var anim = btnObj.AddComponent<VRButtonAnimation>();
        anim.outline = outline;
        anim.normalBorderColor = btnColor;
        anim.highlightBorderColor = Color.white;
        anim.popAmount = 15f; // Pop out by 15 units (local Z or just scale/position)
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
        
        if (parent.GetComponent<LayoutElement>() == null && parent.GetComponent<Button>() == null && parent.name != "Content")
             rt.sizeDelta = new Vector2(600, 100);
        
        return go;
    }

    void OpenRemoteDesktop()
    {
        if (_canvas) _canvas.gameObject.SetActive(false);
        if (_panel) _panel.boardVisible = false;
        if (remotePlayLogic) remotePlayLogic.gameObject.SetActive(true);
        else 
        {
            var rp = FindObjectOfType<RemotePlayMainMenu>(true);
            if (rp) rp.gameObject.SetActive(true);
        }
    }
}

// Helper class for the hover animations
public class VRButtonAnimation : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public UnityEngine.UI.Outline outline;
    public Color normalBorderColor;
    public Color highlightBorderColor;
    public float popAmount = 20f; // Distance to pop out on Z axis

    private Vector3 _startPos;
    private bool _isHovered = false;

    void Start()
    {
        _startPos = transform.localPosition;
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (_isHovered) return;
        _isHovered = true;
        
        // Change Outline to White
        if (outline) outline.effectColor = highlightBorderColor;
        
        // Pop Out (Negative Z is usually "out" towards camera in this WorldSpace setup)
        // Check Canvas scale to ensure we move enough visible amount
        transform.localPosition = new Vector3(_startPos.x, _startPos.y, _startPos.z - popAmount);
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (!_isHovered) return;
        _isHovered = false;
        
        // Revert Outline
        if (outline) outline.effectColor = normalBorderColor;
        
        // Revert Position
        transform.localPosition = _startPos;
    }
}
