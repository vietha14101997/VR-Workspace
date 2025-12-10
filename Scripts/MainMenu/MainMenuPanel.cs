using UnityEngine;
using System;
using System.Collections.Generic;

public class MainMenuPanel : MonoBehaviour
{
    [Header("Panel Settings")]
    public float panelWidth = 1.2f;
    public float panelHeight = 0.7f;

    [Header("Header")]
    public float headerHeight = 0.06f;
    public string headerTitle = "HOME";

    [Header("Grid Layout")]
    public float cardWidth = 0.18f;
    public float cardHeight = 0.14f;
    public float cardSpacing = 0.03f;
    public float gridPadding = 0.05f;
    public int columns = 4;

    [Header("Colors")]
    public Color panelColor = new Color(0.12f, 0.12f, 0.14f, 0.95f);
    public Color headerColor = new Color(0.08f, 0.08f, 0.10f, 1f);
    public Color cardColor = new Color(0.18f, 0.18f, 0.20f, 1f);
    public Color cardHoverColor = new Color(0.25f, 0.25f, 0.28f, 1f);
    public Color accentColor = new Color(0.9f, 0.2f, 0.4f, 1f);
    public Color textColor = Color.white;

    [Header("References")]
    public Camera vrCamera;

    [Header("Runtime")]
    public bool isVisible = true;

    public event Action<string> OnMenuItemClicked;

    private WorldPanelPlus _worldPanel;
    private GameObject _uiRoot;
    private List<MainMenuButton> _buttons = new List<MainMenuButton>();
    private List<Material> _materials = new List<Material>();

    public static MainMenuPanel Instance { get; private set; }

    [System.Serializable]
    public class MenuItem
    {
        public string id;
        public string label;
        public Color iconColor = Color.white;
    }

    public MenuItem[] menuItems = new MenuItem[]
    {
        new MenuItem { id = "remote", label = "Remote", iconColor = new Color(0.9f, 0.2f, 0.4f) },
        new MenuItem { id = "screens", label = "Screens", iconColor = new Color(0.2f, 0.6f, 0.9f) },
        new MenuItem { id = "settings", label = "Settings", iconColor = new Color(0.5f, 0.5f, 0.5f) },
        new MenuItem { id = "mode", label = "Mode", iconColor = new Color(0.2f, 0.8f, 0.4f) },
    };

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        if (vrCamera == null) vrCamera = Camera.main;
        BuildMenu();
    }

    public void BuildMenu()
    {
        ClearMenu();

        // Create WorldPanelPlus as base
        var panelGO = new GameObject("WorldPanelBase");
        panelGO.transform.SetParent(transform, false);
        panelGO.layer = LayerMask.NameToLayer("VirtualObjects");

        _worldPanel = panelGO.AddComponent<WorldPanelPlus>();
        _worldPanel.width = panelWidth;
        _worldPanel.height = panelHeight;
        _worldPanel.panelTint = panelColor;
        _worldPanel.generateHandles = true;
        _worldPanel.generateCornerHandles = false;
        _worldPanel.generateHandleSpheres = true;
        _worldPanel.cursorEnable = false;
        _worldPanel.trayFill = new Color(0.1f, 0.1f, 0.12f, 0.5f);
        _worldPanel.trayBorderColor = new Color(1f, 1f, 1f, 0.3f);

        // Set content texture to solid color
        var bgTex = new Texture2D(2, 2);
        Color[] pixels = new Color[4];
        for (int i = 0; i < 4; i++) pixels[i] = panelColor;
        bgTex.SetPixels(pixels);
        bgTex.Apply();
        _worldPanel.contentTexture = bgTex;

        _worldPanel.Rebuild();

        // Create UI elements on top of the panel
        _uiRoot = new GameObject("UIRoot");
        _uiRoot.transform.SetParent(panelGO.transform, false);
        _uiRoot.transform.localPosition = new Vector3(0, 0, -0.005f);
        _uiRoot.layer = LayerMask.NameToLayer("VirtualObjects");

        BuildHeader();
        BuildGrid();

        PositionInFrontOfCamera();
    }

    void BuildHeader()
    {
        float headerY = panelHeight / 2f - headerHeight / 2f - 0.01f;

        // Header background
        var headerBG = CreateQuad("HeaderBG", headerColor, 
            new Vector3(0, headerY, 0), 
            new Vector3(panelWidth - 0.02f, headerHeight, 1f));

        // Title text
        CreateText(headerTitle, new Vector3(0, headerY, -0.002f), 0.035f, TextAnchor.MiddleCenter);

        // Left side - "New collection" style button (optional decoration)
        float leftX = -panelWidth / 2f + 0.12f;
        var addBtn = CreateCardButton("add_collection", "+", leftX, headerY, 0.08f, headerHeight * 0.7f);

        // Right side - accent button "Add website" style
        float rightX = panelWidth / 2f - 0.1f;
        var accentBtn = CreateAccentButton("add_item", "+ Add", rightX, headerY, 0.12f, headerHeight * 0.7f);
    }

    void BuildGrid()
    {
        float startY = panelHeight / 2f - headerHeight - gridPadding - cardHeight / 2f - 0.02f;
        float startX = -panelWidth / 2f + gridPadding + cardWidth / 2f;

        for (int i = 0; i < menuItems.Length; i++)
        {
            int col = i % columns;
            int row = i / columns;

            float x = startX + col * (cardWidth + cardSpacing);
            float y = startY - row * (cardHeight + cardSpacing);

            CreateMenuCard(menuItems[i], new Vector3(x, y, -0.003f));
        }
    }

    void CreateMenuCard(MenuItem item, Vector3 localPos)
    {
        var cardGO = new GameObject($"Card_{item.id}");
        cardGO.transform.SetParent(_uiRoot.transform, false);
        cardGO.transform.localPosition = localPos;
        cardGO.layer = LayerMask.NameToLayer("VirtualObjects");

        // Card background
        var cardBG = CreateQuad("CardBG", cardColor, Vector3.zero, new Vector3(cardWidth, cardHeight, 1f), cardGO.transform);

        // Icon area (colored square)
        float iconSize = 0.05f;
        float iconY = 0.015f;
        var iconQuad = CreateQuad("Icon", item.iconColor, 
            new Vector3(0, iconY, -0.001f), 
            new Vector3(iconSize, iconSize, 1f), 
            cardGO.transform);

        // Icon symbol (simple geometric representation)
        CreateIconSymbol(item.id, new Vector3(0, iconY, -0.002f), iconSize * 0.6f, cardGO.transform);

        // Label
        float labelY = -cardHeight / 2f + 0.025f;
        CreateText(item.label, new Vector3(0, labelY, -0.001f), 0.022f, TextAnchor.MiddleCenter, cardGO.transform);

        // Collider for interaction
        var col = cardGO.AddComponent<BoxCollider>();
        col.size = new Vector3(cardWidth, cardHeight, 0.02f);
        col.center = new Vector3(0, 0, 0.01f);

        // Button component
        var btn = cardGO.AddComponent<MainMenuButton>();
        btn.label = item.id;
        btn.normalColor = cardColor;
        btn.hoverColor = cardHoverColor;
        btn.buttonMaterial = cardBG.GetComponent<MeshRenderer>().sharedMaterial;
        btn.menuPanel = this;
        _buttons.Add(btn);
    }

    void CreateIconSymbol(string id, Vector3 localPos, float size, Transform parent)
    {
        // Simple geometric icons
        var iconGO = new GameObject("IconSymbol");
        iconGO.transform.SetParent(parent, false);
        iconGO.transform.localPosition = localPos;
        iconGO.layer = LayerMask.NameToLayer("VirtualObjects");

        string symbol = id switch
        {
            "remote" => ">",
            "screens" => "[]",
            "settings" => "*",
            "mode" => "O",
            _ => "?"
        };

        var tm = iconGO.AddComponent<TextMesh>();
        tm.text = symbol;
        tm.fontSize = 80;
        tm.characterSize = size * 0.015f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var mr = iconGO.GetComponent<MeshRenderer>();
        if (mr) mr.sortingOrder = 10;
    }

    GameObject CreateCardButton(string id, string label, float x, float y, float w, float h)
    {
        var btnGO = new GameObject($"Btn_{id}");
        btnGO.transform.SetParent(_uiRoot.transform, false);
        btnGO.transform.localPosition = new Vector3(x, y, -0.002f);
        btnGO.layer = LayerMask.NameToLayer("VirtualObjects");

        var bg = CreateQuad("BG", new Color(0.25f, 0.25f, 0.28f, 1f), Vector3.zero, new Vector3(w, h, 1f), btnGO.transform);
        CreateText(label, new Vector3(0, 0, -0.001f), 0.025f, TextAnchor.MiddleCenter, btnGO.transform);

        var col = btnGO.AddComponent<BoxCollider>();
        col.size = new Vector3(w, h, 0.02f);

        return btnGO;
    }

    GameObject CreateAccentButton(string id, string label, float x, float y, float w, float h)
    {
        var btnGO = new GameObject($"Btn_{id}");
        btnGO.transform.SetParent(_uiRoot.transform, false);
        btnGO.transform.localPosition = new Vector3(x, y, -0.002f);
        btnGO.layer = LayerMask.NameToLayer("VirtualObjects");

        var bg = CreateQuad("BG", accentColor, Vector3.zero, new Vector3(w, h, 1f), btnGO.transform);
        CreateText(label, new Vector3(0, 0, -0.001f), 0.022f, TextAnchor.MiddleCenter, btnGO.transform);

        var col = btnGO.AddComponent<BoxCollider>();
        col.size = new Vector3(w, h, 0.02f);

        return btnGO;
    }

    GameObject CreateQuad(string name, Color color, Vector3 localPos, Vector3 scale, Transform parent = null)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(parent ?? _uiRoot.transform, false);
        quad.transform.localPosition = localPos;
        quad.transform.localScale = scale;
        quad.layer = LayerMask.NameToLayer("VirtualObjects");

        var col = quad.GetComponent<Collider>();
        if (col) Destroy(col);

        var mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = color;
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        _materials.Add(mat);

        return quad;
    }

    void CreateText(string text, Vector3 localPos, float size, TextAnchor anchor, Transform parent = null)
    {
        var textGO = new GameObject("Text_" + text.Replace(" ", ""));
        textGO.transform.SetParent(parent ?? _uiRoot.transform, false);
        textGO.transform.localPosition = localPos;
        textGO.layer = LayerMask.NameToLayer("VirtualObjects");

        var tm = textGO.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 100;
        tm.characterSize = size * 0.01f;
        tm.anchor = anchor;
        tm.alignment = TextAlignment.Center;
        tm.color = textColor;
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    public void PositionInFrontOfCamera()
    {
        if (vrCamera == null) vrCamera = Camera.main;
        if (vrCamera == null) return;

        float distance = 1.5f;
        Vector3 forward = vrCamera.transform.forward;
        forward.y = 0;
        forward.Normalize();
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        Vector3 pos = vrCamera.transform.position + forward * distance;
        pos.y = vrCamera.transform.position.y;

        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    public void OnButtonClicked(string id)
    {
        Debug.Log($"[MainMenu] Button clicked: {id}");
        OnMenuItemClicked?.Invoke(id);

        switch (id)
        {
            case "remote":
                OpenRemoteDesktop();
                break;
            case "screens":
                OpenVirtualScreens();
                break;
            case "settings":
                OpenSettings();
                break;
            case "mode":
                ToggleMode();
                break;
        }
    }

    void OpenRemoteDesktop()
    {
        Debug.Log("[MainMenu] Opening Remote Desktop...");
        var pcClient = FindObjectOfType<PCStreamClient>();
        if (pcClient != null && pcClient.worldPanel != null)
        {
            pcClient.worldPanel.gameObject.SetActive(true);
            pcClient.enabled = true;
        }
        Hide();
    }

    void OpenVirtualScreens()
    {
        Debug.Log("[MainMenu] Opening Virtual Screens...");
        Hide();
    }

    void OpenSettings()
    {
        Debug.Log("[MainMenu] Opening Settings...");
    }

    void ToggleMode()
    {
        var modeController = FindObjectOfType<ModeController>();
        if (modeController != null)
        {
            modeController.ToggleMode();
        }
    }

    public void Show()
    {
        isVisible = true;
        gameObject.SetActive(true);
        PositionInFrontOfCamera();
    }

    public void Hide()
    {
        isVisible = false;
        gameObject.SetActive(false);
    }

    public void Toggle()
    {
        if (isVisible) Hide();
        else Show();
    }

    void ClearMenu()
    {
        _buttons.Clear();
        foreach (var mat in _materials)
        {
            if (mat) Destroy(mat);
        }
        _materials.Clear();

        if (_worldPanel != null)
        {
            if (Application.isPlaying) Destroy(_worldPanel.gameObject);
            else DestroyImmediate(_worldPanel.gameObject);
            _worldPanel = null;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ClearMenu();
    }
}
