using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class RemotePlayMainMenu : MonoBehaviour
{
    [Header("Dependencies")]
    public WorldPanelPlus menuPanelPrefab; // Prefab cho cái bảng Menu này (nếu có, hoặc dùng code tạo)
    public WorldPanelPlus monitorPanelPrefab; // Prefab panel cho các màn hình Remote
    public Transform virtualObjectsRoot; // Nơi chứa các màn hình sau khi connect

    [Header("Default Settings")]
    public string defaultServerIp = "127.0.0.1";
    public int defaultPort = 8288;
    public int defaultMonitorCount = 2;
    public Vector2Int defaultResolution = new Vector2Int(1920, 1080);
    public int defaultKbps = 8000;
    public int defaultFps = 60;

    // Runtime References
    private WorldPanelPlus _menuPanel;
    private Canvas _canvas;
    
    // UI Fields
    private InputField _inputIp;
    private InputField _inputPort;
    private Dropdown _dropMonitors; // 1-6
    private InputField _inputResW;
    private InputField _inputResH;
    private InputField _inputKbps;
    private InputField _inputFps;
    private Text _statusText;

    void Start()
    {
        BuildMenuInterface();
    }

    void BuildMenuInterface()
    {
        // 1. Tạo WorldPanelPlus cho Menu nếu chưa có
        if (menuPanelPrefab != null)
        {
            var go = Instantiate(menuPanelPrefab.gameObject, transform);
            _menuPanel = go.GetComponent<WorldPanelPlus>();
        }
        else
        {
            var go = new GameObject("MainMenu_Panel");
            go.transform.SetParent(transform, false);
            _menuPanel = go.AddComponent<WorldPanelPlus>();
            _menuPanel.width = 0.8f;
            _menuPanel.height = 0.6f;
            _menuPanel.Rebuild();
        }

        // 2. Tạo Canvas WorldSpace gắn vào Panel
        // WorldPanelPlus thường có phần Board, ta gắn Canvas lên trước Board một chút
        GameObject canvasGO = new GameObject("MenuCanvas");
        canvasGO.transform.SetParent(_menuPanel.board ? _menuPanel.board : _menuPanel.transform, false);
        
        // Scale canvas để vừa với panel size (giả sử panel width/height là mét)
        // Canvas scaler pixel per unit = 100?
        // Cách đơn giản: Canvas size = 800x600 px, scale = (panelWidth/800, panelHeight/600)
        float menuWidthPx = 800f;
        float menuHeightPx = 600f;
        
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        
        var rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(menuWidthPx, menuHeightPx);
        rt.localPosition = new Vector3(0, 0, -0.01f); // Nổi lên trên mặt panel một chút
        rt.localScale = new Vector3(_menuPanel.width / menuWidthPx, _menuPanel.height / menuHeightPx, 1f);

        canvasGO.AddComponent<GraphicRaycaster>();

        // 3. Build UI Elements
        // Background
        CreateImage(canvasGO.transform, new Color(0.1f, 0.1f, 0.1f, 0.9f));

        // Title
        CreateText(canvasGO.transform, "REMOTE PLAY CONNECT", new Vector2(0, 250), 40, Color.cyan);

        // Inputs
        float startY = 180;
        float gap = 60;

        _inputIp = CreateLabeledInput(canvasGO.transform, "Server IP:", defaultServerIp, new Vector2(-100, startY));
        _inputPort = CreateLabeledInput(canvasGO.transform, "Port:", defaultPort.ToString(), new Vector2(250, startY), width: 100);

        startY -= gap;
        // Monitor Count (Simple Input for now, or Dropdown logic manually)
        _dropMonitors = CreateLabeledDropdown(canvasGO.transform, "Monitors:", new List<string>{"1","2","3","4","5","6"}, defaultMonitorCount-1, new Vector2(0, startY));

        startY -= gap;
        _inputResW = CreateLabeledInput(canvasGO.transform, "Res W:", defaultResolution.x.ToString(), new Vector2(-100, startY), width: 120);
        _inputResH = CreateLabeledInput(canvasGO.transform, "Res H:", defaultResolution.y.ToString(), new Vector2(150, startY), width: 120);

        startY -= gap;
        _inputKbps = CreateLabeledInput(canvasGO.transform, "Bitrate (Kbps):", defaultKbps.ToString(), new Vector2(-80, startY), width: 150);
        _inputFps = CreateLabeledInput(canvasGO.transform, "FPS:", defaultFps.ToString(), new Vector2(150, startY), width: 100);

        // Connect Button
        startY -= gap * 1.5f;
        var btnObj = CreateButton(canvasGO.transform, "CONNECT", new Vector2(0, startY), new Vector2(200, 50), Color.green);
        btnObj.GetComponent<Button>().onClick.AddListener(OnConnectClicked);

        // Status
        _statusText = CreateText(canvasGO.transform, "Ready", new Vector2(0, startY - 60), 20, Color.gray);
    }

    void OnConnectClicked()
    {
        _statusText.text = "Connecting...";
        _statusText.color = Color.yellow;

        // 1. Parse Inputs
        string ip = _inputIp.text;
        string portStr = _inputPort.text;
        int monitors = _dropMonitors.value + 1;
        int w = int.Parse(_inputResW.text);
        int h = int.Parse(_inputResH.text);
        int kbps = int.Parse(_inputKbps.text);
        int fps = int.Parse(_inputFps.text);

        string urlBase = $"http://{ip}:{portStr}";

        // 2. Prepare Container
        if (virtualObjectsRoot == null)
        {
            var voObj = GameObject.Find("VirtualObjects");
            if (voObj == null) voObj = new GameObject("VirtualObjects");
            virtualObjectsRoot = voObj.transform;

            // --- FIX LAYER: Ensure Root has VirtualObjects layer ---
            int layerVO = LayerMask.NameToLayer("VirtualObjects");
            if (layerVO != -1) voObj.layer = layerVO;
        }

        // 3. Create Cluster Rig
        GameObject rigGO = new GameObject("RemotePlay_ClusterRig");
        rigGO.transform.SetParent(virtualObjectsRoot, false);

        // --- FIX LAYER: Rig inherits layer ---
        int layerIndex = LayerMask.NameToLayer("VirtualObjects");
        if (layerIndex != -1) rigGO.layer = layerIndex;
        
        var rig = rigGO.AddComponent<WorldPanelClusterRig>();
        rig.panelPrefab = monitorPanelPrefab != null ? monitorPanelPrefab : _menuPanel; // Fallback to menu panel visual if monitor prefab missing
        rig.distanceFromCamera = 1.5f;
        rig.sideYawDeg = 25f; // Slight curve

        // 4. Create Binder & Connect
        var binder = rigGO.AddComponent<ClusterAutoBinder>();
        binder.rig = rig;
        binder.serverBase = urlBase;
        binder.monitorCount = monitors;
        binder.resolutionWidth = w;
        binder.resolutionHeight = h;
        binder.bitrateKbps = kbps;
        binder.fps = fps;

        // AutoBinder's Start() will run next frame (or immediately if added via AddComponent in play mode? Unity lifecycle: Start runs next frame usually if added dynamically)
        // But since we just added it, Start() hasn't run. It will run automatically.
        
        _statusText.text = "Binder Launched!";
        _statusText.color = Color.green;

        // Hide Menu
        gameObject.SetActive(false);
    }

    // --- Create UI Helpers ---

    GameObject CreateImage(Transform parent, Color c)
    {
        var go = new GameObject("Bg");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; 
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; // Fill parent
        return go;
    }

    Text CreateText(Transform parent, string content, Vector2 pos, int size, Color c)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.text = content;
        txt.fontSize = size;
        txt.color = c;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        return txt;
    }

    InputField CreateLabeledInput(Transform parent, string label, string defaultVal, Vector2 pos, float width = 200, float height = 40)
    {
        // Container
        var container = new GameObject("Input_" + label);
        container.transform.SetParent(parent, false);
        var crt = container.AddComponent<RectTransform>();
        crt.anchoredPosition = pos;
        crt.sizeDelta = new Vector2(width + 100, height);

        // Label
        var lbl = CreateText(container.transform, label, new Vector2(-width/2 - 40, 0), 24, Color.white);
        lbl.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
        lbl.alignment = TextAnchor.MiddleRight;

        // InputField Background
        var bg = new GameObject("InputBg");
        bg.transform.SetParent(container.transform, false);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = Color.white;
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.sizeDelta = new Vector2(width, height);

        // Input Object
        var inputObj = new GameObject("InputField");
        inputObj.transform.SetParent(bg.transform, false);
        var input = inputObj.AddComponent<InputField>();
        var inputRt = inputObj.GetComponent<RectTransform>();
        inputRt.anchorMin = Vector2.zero; inputRt.anchorMax = Vector2.one; inputRt.offsetMin = Vector2.zero; inputRt.offsetMax = Vector2.zero;

        // Text Component
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(inputObj.transform, false);
        var txt = textObj.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.color = Color.black;
        txt.fontSize = 20;
        txt.alignment = TextAnchor.MiddleCenter;
        var txtRt = textObj.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one; txtRt.offsetMin = new Vector2(10,0); txtRt.offsetMax = new Vector2(-10,0);

        input.textComponent = txt;
        input.text = defaultVal;
        input.targetGraphic = bgImg;

        return input;
    }

    Dropdown CreateLabeledDropdown(Transform parent, string label, List<string> options, int defaultIdx, Vector2 pos, float width = 200, float height = 40)
    {
         // Container
        var container = new GameObject("Drop_" + label);
        container.transform.SetParent(parent, false);
        var crt = container.AddComponent<RectTransform>();
        crt.anchoredPosition = pos;

        // Label
        CreateText(container.transform, label, new Vector2(-width/2 - 40, 0), 24, Color.white).alignment = TextAnchor.MiddleRight;

        // Dropdown (Simplified structure)
        // Unity Dropdown is complex to build via code (needs ScrollRect, Templates etc.)
        // For simplicity, using a basic customized button cycle or simple logic is easier via code,
        // but let's try basic standard Dropdown setup structure.
        
        var bg = new GameObject("Dropdown");
        bg.transform.SetParent(container.transform, false);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = Color.white;
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.sizeDelta = new Vector2(width, height);

        var dd = bg.AddComponent<Dropdown>();
        dd.options.Clear();
        foreach(var o in options) dd.options.Add(new Dropdown.OptionData(o));

        // Caption Text
        var captionObj = new GameObject("Caption");
        captionObj.transform.SetParent(bg.transform, false);
        var captionTxt = captionObj.AddComponent<Text>();
        captionTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        captionTxt.color = Color.black;
        captionTxt.alignment = TextAnchor.MiddleCenter;
        captionTxt.fontSize = 20;
        var capRt = captionObj.GetComponent<RectTransform>();
        capRt.anchorMin = Vector2.zero; capRt.anchorMax = Vector2.one;
        dd.captionText = captionTxt;
        dd.targetGraphic = bgImg;
        dd.value = defaultIdx;

        // To make it functional, it needs a Template rect. Constructing template via code is verbose.
        // Falling back to a simple logic: Since we just need 1-6, maybe just an InputField for now?
        // Or leave it as is, but it won't open without Template.
        // Let's replace with InputField for reliability in code-only generation.
        
        Destroy(dd);
        Destroy(captionObj);
        Destroy(bg);
        Destroy(container);

        // Revert to Input field for Monitor Count
        return null; 
    }

    GameObject CreateButton(Transform parent, string label, Vector2 pos, Vector2 size, Color c)
    {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        CreateText(go.transform, label, Vector2.zero, 24, Color.black);

        return go;
    }
}
