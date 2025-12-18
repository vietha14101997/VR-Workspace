using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class VRRemoteMenu : MonoBehaviour
{
    // Configuration
    public Color themeColor = new Color(0.0f, 0.9f, 1.0f); // Cyan
    public TMP_FontAsset customFont;
    
    // UI References
    private TMP_InputField _hostInput;
    private TMP_InputField _portInput;
    private TMP_Dropdown _monitorDropdown;
    private TMP_Dropdown _resDropdown;
    private TMP_Dropdown _bitrateDropdown;
    private TMP_Dropdown _fpsDropdown;

    private VRMainMenu _mainMenu; // To go back

    public void BuildUI(Transform parent, VRMainMenu mainMenu)
    {
        _mainMenu = mainMenu;

        // Create Container
        GameObject container = new GameObject("RemoteMenu_Container");
        container.transform.SetParent(parent, false);
        RectTransform rt = container.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.offsetMin = new Vector2(50, 50); // Padding
        rt.offsetMax = new Vector2(-50, -50);

        // 1. Header (Back & QR)
        CreateHeader(container.transform);

        // 2. Form Area (Middle)
        CreateForm(container.transform);

        // 3. Footer (Connect)
        CreateFooter(container.transform);
    }

    void CreateHeader(Transform parent)
    {
        GameObject header = new GameObject("Header");
        header.transform.SetParent(parent, false);
        RectTransform rt = header.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, 100);
        rt.anchoredPosition = Vector2.zero;

        // Back Button (Top Left)
        CreateButton(header.transform, "< Back", new Vector2(150, 60), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -20), () => 
        {
            if (_mainMenu != null) _mainMenu.ReturnToMainMenu();
        });

        // QR Scan Button (Top Right)
        CreateButton(header.transform, "Scan QR", new Vector2(150, 60), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -20), () => 
        {
            Debug.Log("Scan QR Clicked");
        });
    }

    void CreateForm(Transform parent)
    {
        GameObject form = new GameObject("FormContainer");
        form.transform.SetParent(parent, false);
        RectTransform rt = form.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.2f, 0.2f); // Centered area
        rt.anchorMax = new Vector2(0.8f, 0.85f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        VerticalLayoutGroup vlg = form.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 30;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlHeight = false;
        vlg.childControlWidth = true;

        // Host IP
        CreateLabelInput(form.transform, "Host IP:", "192.168.1.10");

        // Port
        CreateLabelInput(form.transform, "Port:", "8080");

        // Dropbox Row 1
        GameObject row1 = CreateRow(form.transform);
        CreateLabelDropdown(row1.transform, "Monitors", new List<string> { "Display 1", "Display 2" });
        CreateLabelDropdown(row1.transform, "Resolution", new List<string> { "1080p", "1440p", "4K" });

        // Dropbox Row 2
        GameObject row2 = CreateRow(form.transform);
        CreateLabelDropdown(row2.transform, "Bitrate", new List<string> { "10 Mbps", "20 Mbps", "50 Mbps" });
        CreateLabelDropdown(row2.transform, "FPS", new List<string> { "30", "60", "90", "120" });
    }

    void CreateFooter(Transform parent)
    {
        GameObject footer = new GameObject("Footer");
        footer.transform.SetParent(parent, false);
        RectTransform rt = footer.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(0, 120);
        rt.anchoredPosition = Vector2.zero;

        // Connect Button
        CreateButton(footer.transform, "CONNECT", new Vector2(300, 80), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, () => 
        {
            Debug.Log("Connect Clicked");
        }, true);
    }

    // --- Helpers ---

    GameObject CreateRow(Transform parent)
    {
        GameObject row = new GameObject("Row");
        row.transform.SetParent(parent, false);
        RectTransform rt = row.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 80);
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childForceExpandWidth = true;
        return row;
    }

    void CreateLabelInput(Transform parent, string labelText, string placeholder)
    {
        GameObject container = new GameObject($"Inp_{labelText}");
        container.transform.SetParent(parent, false);
        RectTransform rt = container.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 60);

        HorizontalLayoutGroup hlg = container.AddComponent<HorizontalLayoutGroup>();
        hlg.childControlWidth = false;
        hlg.childForceExpandWidth = false;
        hlg.spacing = 20;
        hlg.childAlignment = TextAnchor.MiddleLeft;

        // Label
        CreateText(container.transform, labelText, 24, Color.white, 200);

        // InputField Background
        GameObject inputBg = new GameObject("InputBg");
        inputBg.transform.SetParent(container.transform, false);
        Image bg = inputBg.AddComponent<Image>();
        bg.color = new Color(1, 1, 1, 0.1f);
        LayoutElement le = inputBg.AddComponent<LayoutElement>();
        le.preferredWidth = 400;
        le.preferredHeight = 50;

        // Input Field TMP
        TMP_InputField input = inputBg.AddComponent<TMP_InputField>();
        
        // Text Area
        GameObject textArea = new GameObject("TextArea");
        textArea.transform.SetParent(inputBg.transform, false);
        RectTransform taRT = textArea.AddComponent<RectTransform>();
        taRT.anchorMin = Vector2.zero; taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(10, 0); taRT.offsetMax = new Vector2(-10, 0);

        // Text Component
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        RectTransform tRT = textObj.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = 24;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;
        
        input.textComponent = text;
        input.textViewport = taRT;

        // Placeholder
        GameObject placeObj = new GameObject("Placeholder");
        placeObj.transform.SetParent(textArea.transform, false);
        RectTransform pRT = placeObj.AddComponent<RectTransform>();
        pRT.anchorMin = Vector2.zero; pRT.anchorMax = Vector2.one;
        TextMeshProUGUI placeText = placeObj.AddComponent<TextMeshProUGUI>();
        placeText.fontSize = 24;
        placeText.color = new Color(1,1,1,0.5f);
        placeText.text = placeholder;
        placeText.fontStyle = FontStyles.Italic;
        placeText.alignment = TextAlignmentOptions.Left;

        input.placeholder = placeText;

        if (labelText.Contains("Host")) _hostInput = input;
        else _portInput = input;
    }

    void CreateLabelDropdown(Transform parent, string labelText, List<string> options)
    {
        GameObject container = new GameObject($"Drop_{labelText}");
        container.transform.SetParent(parent, false);
        RectTransform rt = container.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 60);

        VerticalLayoutGroup vlg = container.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 5;
        
        // Label
        CreateText(container.transform, labelText, 20, Color.cyan, 0);

        // Dropdown
        GameObject dropObj = new GameObject("Dropdown");
        dropObj.transform.SetParent(container.transform, false);
        Image bg = dropObj.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.5f); // Dark bg
        RectTransform dropRT = dropObj.GetComponent<RectTransform>();
        dropRT.sizeDelta = new Vector2(0, 40); // Height

        TMP_Dropdown dropdown = dropObj.AddComponent<TMP_Dropdown>();

        // Label Text
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(dropObj.transform, false);
        RectTransform lRT = labelObj.AddComponent<RectTransform>();
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one;
        lRT.offsetMin = new Vector2(10,0); lRT.offsetMax = new Vector2(-20,0);
        TextMeshProUGUI labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.text = options.Count > 0 ? options[0] : "";
        labelTmp.fontSize = 20;
        labelTmp.alignment = TextAlignmentOptions.Left;
        labelTmp.color = Color.white;
        dropdown.captionText = labelTmp;

        // Arrow
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(dropObj.transform, false);
        RectTransform aRT = arrowObj.AddComponent<RectTransform>();
        aRT.anchorMin = new Vector2(1, 0.5f); aRT.anchorMax = new Vector2(1, 0.5f);
        aRT.sizeDelta = new Vector2(20, 20);
        aRT.anchoredPosition = new Vector2(-15, 0);
        Image arrowImg = arrowObj.AddComponent<Image>(); 
        arrowImg.color = Color.cyan; // Placeholder arrow

        // Template (The open list) - simplified
        // Proper dropdown setup in code is complex, keeping it minimal for sketch (it won't open without Template setup)
        // Set basic template structure...
        GameObject template = new GameObject("Template");
        template.transform.SetParent(dropObj.transform, false);
        template.SetActive(false);
        RectTransform tRT = template.AddComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0, 0); tRT.anchorMax = new Vector2(1, 0);
        tRT.pivot = new Vector2(0.5f, 1);
        tRT.anchoredPosition = new Vector2(0, 2);
        tRT.sizeDelta = new Vector2(0, 150);
        
        Image tImg = template.AddComponent<Image>();
        tImg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        ScrollRect sr = template.AddComponent<ScrollRect>();
        sr.content = null; // Needs viewport setup... 
        // For Draft: We just register options. The dropdown might not visually expand correctly without full template hierarchy
        // but the data will be there.
        
        dropdown.AddOptions(options);
        
        // Assign to fields
        if(labelText.Contains("Monitor")) _monitorDropdown = dropdown;
        else if(labelText.Contains("Res")) _resDropdown = dropdown;
        else if(labelText.Contains("Bitrate")) _bitrateDropdown = dropdown;
        else if(labelText.Contains("FPS")) _fpsDropdown = dropdown;
    }

    void CreateButton(Transform parent, string text, Vector2 size, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick, bool prominent = false)
    {
        GameObject btnObj = new GameObject($"Btn_{text}");
        btnObj.transform.SetParent(parent, false);
        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        // Visuals
        Image img = btnObj.AddComponent<Image>();
        img.color = prominent ? themeColor : new Color(1, 1, 1, 0.1f);
        if(!prominent)
        {
            // Add outline or something
        }

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        // Text
        CreateText(btnObj.transform, text, 24, prominent ? Color.black : Color.white, 0);
        
        // Collider for VR
        BoxCollider col = btnObj.AddComponent<BoxCollider>();
        col.size = new Vector3(size.x, size.y, 0.1f);
    }

    void CreateText(Transform parent, string content, int fontSize, Color color, float width)
    {
        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(parent, false);
        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.text = content;
        txt.fontSize = fontSize;
        txt.color = color;
        txt.alignment = TextAlignmentOptions.Center;
        if(customFont != null) txt.font = customFont;
        
        if (width > 0)
        {
            LayoutElement le = txtObj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
        }
        else
        {
            RectTransform rt = txtObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
