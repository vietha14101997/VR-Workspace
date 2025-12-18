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
    private Sprite _pixelSprite;

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

    Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }

    Material CreateGlowingMaterial(Color glowColor, float intensity = 1.0f, float borderWidth = 0.04f, float glowWidth = 0.06f, float cornerRadius = 0.12f, float bgAlpha = 0.08f)
    {
        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            Material mat = new Material(glowShader);
            
            // UV-based settings (works for any size!)
            mat.SetColor("_GlowColor", glowColor);
            mat.SetFloat("_GlowIntensity", intensity);
            mat.SetFloat("_BorderWidth", borderWidth);
            mat.SetFloat("_GlowWidth", glowWidth);
            mat.SetFloat("_CornerRadius", cornerRadius);
            
            // Background
            mat.SetFloat("_BackgroundAlpha", bgAlpha);
            mat.SetColor("_BackgroundColor", new Color(glowColor.r * 0.3f, glowColor.g * 0.3f, glowColor.b * 0.3f, 1f));
            
            // Animation
            mat.SetFloat("_PulseEnabled", 1f);
            mat.SetFloat("_PulseSpeed", 2f);
            mat.SetFloat("_PulseIntensity", 0.1f);
            
            return mat;
        }
        return null;
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
        CreateText(container.transform, labelText, 24, themeColor, 200);

        // InputField Background with Glowing Border
        GameObject inputBg = new GameObject("InputBg");
        inputBg.transform.SetParent(container.transform, false);
        Image bg = inputBg.AddComponent<Image>();
        
        // Apply glowing material (UV-based, works for any size)
        Material glowMat = CreateGlowingMaterial(themeColor, 0.9f, 0.03f, 0.05f, 0.1f, 0.06f);
        if (glowMat != null)
        {
            bg.material = glowMat;
            bg.sprite = GetPixelSprite();
            bg.color = Color.white;
        }
        else
        {
            bg.color = new Color(1, 1, 1, 0.1f);
        }
        
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
        taRT.offsetMin = new Vector2(15, 0); taRT.offsetMax = new Vector2(-15, 0);

        // Text Component
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        RectTransform tRT = textObj.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = 24;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;
        if (customFont != null) text.font = customFont;
        
        input.textComponent = text;
        input.textViewport = taRT;

        // Placeholder
        GameObject placeObj = new GameObject("Placeholder");
        placeObj.transform.SetParent(textArea.transform, false);
        RectTransform pRT = placeObj.AddComponent<RectTransform>();
        pRT.anchorMin = Vector2.zero; pRT.anchorMax = Vector2.one;
        pRT.offsetMin = Vector2.zero; pRT.offsetMax = Vector2.zero;
        TextMeshProUGUI placeText = placeObj.AddComponent<TextMeshProUGUI>();
        placeText.fontSize = 24;
        placeText.color = new Color(themeColor.r, themeColor.g, themeColor.b, 0.5f);
        placeText.text = placeholder;
        placeText.fontStyle = FontStyles.Italic;
        placeText.alignment = TextAlignmentOptions.Left;
        if (customFont != null) placeText.font = customFont;

        input.placeholder = placeText;

        if (labelText.Contains("Host")) _hostInput = input;
        else _portInput = input;
    }

    void CreateLabelDropdown(Transform parent, string labelText, List<string> options)
    {
        GameObject container = new GameObject($"Drop_{labelText}");
        container.transform.SetParent(parent, false);
        RectTransform rt = container.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 80); // Slightly taller

        VerticalLayoutGroup vlg = container.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.childControlHeight = false;
        vlg.childForceExpandHeight = false;
        
        // Label - Use theme color
        GameObject labelContainer = new GameObject("LabelContainer");
        labelContainer.transform.SetParent(container.transform, false);
        LayoutElement labelLE = labelContainer.AddComponent<LayoutElement>();
        labelLE.preferredHeight = 25;
        CreateText(labelContainer.transform, labelText, 20, themeColor, 0);

        // Dropdown with Glowing Border
        GameObject dropObj = new GameObject("Dropdown");
        dropObj.transform.SetParent(container.transform, false);
        LayoutElement dropLE = dropObj.AddComponent<LayoutElement>();
        dropLE.preferredHeight = 45;
        dropLE.flexibleWidth = 1;
        
        Image bg = dropObj.AddComponent<Image>();
        
        // Apply glowing material (UV-based)
        Material glowMat = CreateGlowingMaterial(themeColor, 0.9f, 0.035f, 0.055f, 0.12f, 0.1f);
        if (glowMat != null)
        {
            bg.material = glowMat;
            bg.sprite = GetPixelSprite();
            bg.color = Color.white;
        }
        else
        {
            bg.color = new Color(0, 0, 0, 0.5f);
        }
        
        RectTransform dropRT = dropObj.GetComponent<RectTransform>();

        TMP_Dropdown dropdown = dropObj.AddComponent<TMP_Dropdown>();

        // Label Text
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(dropObj.transform, false);
        RectTransform lRT = labelObj.AddComponent<RectTransform>();
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one;
        lRT.offsetMin = new Vector2(15, 0); lRT.offsetMax = new Vector2(-35, 0);
        TextMeshProUGUI labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
        labelTmp.text = options.Count > 0 ? options[0] : "";
        labelTmp.fontSize = 20;
        labelTmp.alignment = TextAlignmentOptions.Left;
        labelTmp.color = Color.white;
        if (customFont != null) labelTmp.font = customFont;
        dropdown.captionText = labelTmp;

        // Arrow (Dropdown indicator)
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(dropObj.transform, false);
        RectTransform aRT = arrowObj.AddComponent<RectTransform>();
        aRT.anchorMin = new Vector2(1, 0.5f); aRT.anchorMax = new Vector2(1, 0.5f);
        aRT.sizeDelta = new Vector2(18, 10);
        aRT.anchoredPosition = new Vector2(-18, 0);
        Image arrowImg = arrowObj.AddComponent<Image>(); 
        arrowImg.color = themeColor;

        // Template (The open list)
        GameObject template = new GameObject("Template");
        template.transform.SetParent(dropObj.transform, false);
        template.SetActive(false);
        RectTransform tRT = template.AddComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0, 0); tRT.anchorMax = new Vector2(1, 0);
        tRT.pivot = new Vector2(0.5f, 1);
        tRT.anchoredPosition = new Vector2(0, 2);
        tRT.sizeDelta = new Vector2(0, 150);
        
        Image tImg = template.AddComponent<Image>();
        // Glowing template background (UV-based)
        Material templateMat = CreateGlowingMaterial(themeColor, 0.8f, 0.03f, 0.05f, 0.1f, 0.2f);
        if (templateMat != null)
        {
            tImg.material = templateMat;
            tImg.sprite = GetPixelSprite();
            tImg.color = Color.white;
        }
        else
        {
            tImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
        }
        
        ScrollRect sr = template.AddComponent<ScrollRect>();
        sr.content = null;
        
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

        // Visuals - Use glowing material
        Image img = btnObj.AddComponent<Image>();
        
        if (prominent)
        {
            // Prominent button (Connect) - Strong glow
            Material glowMat = CreateGlowingMaterial(themeColor, 1.2f, 0.05f, 0.1f, 0.15f, 0.15f);
            if (glowMat != null)
            {
                // Create gradient by blending with purple
                Color purpleAccent = new Color(0.75f, 0.35f, 1f, 1f);
                glowMat.SetColor("_GlowColor", Color.Lerp(themeColor, purpleAccent, 0.3f));
                glowMat.SetFloat("_PulseIntensity", 0.15f);
                img.material = glowMat;
                img.sprite = GetPixelSprite();
                img.color = Color.white;
            }
            else
            {
                img.color = themeColor;
            }
        }
        else
        {
            // Normal button - Subtle glow
            Material glowMat = CreateGlowingMaterial(themeColor, 0.9f, 0.035f, 0.06f, 0.12f, 0.08f);
            if (glowMat != null)
            {
                img.material = glowMat;
                img.sprite = GetPixelSprite();
                img.color = Color.white;
            }
            else
            {
                img.color = new Color(1, 1, 1, 0.1f);
            }
        }

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        
        // Configure hover effect
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        cb.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        cb.fadeDuration = 0.1f;
        btn.colors = cb;

        // Text
        CreateText(btnObj.transform, text, 24, prominent ? new Color(0.1f, 0.1f, 0.15f, 1f) : Color.white, 0);
        
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
