using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class VRMenuFrame : MonoBehaviour
{
    [Header("Frame Configuration")]
    public float topMargin = 150f; // StatusBar Area
    public float sidePadding = 80f;

    [Header("Visual Config")]
    public Color glassColor = new Color(1.0f, 1.0f, 1.0f, 0.09803921568f);
    public Color panelBorderColor = new Color(1.0f, 1.0f, 1.0f, 0.39215686274f);

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

    void Start()
    {
        LoadIcons();
    }

    void Update()
    {
        UpdateClock();
        UpdateNetwork();
        UpdateBattery();
    }

    public void Build(float width, float height)
    {
        // 1. Create Glass Background & Borders
        CreateGlassPanel(transform, width, height);

        // 2. Create Status Bar
        CreateStatusBar(transform, width, height, topMargin);

        // 3. Create Content Container
        GameObject contentObj = new GameObject("ContentContainer");
        contentObj.transform.SetParent(transform, false);
        ContentContainer = contentObj.AddComponent<RectTransform>();
        
        // Fill the space below status bar
        ContentContainer.anchorMin = Vector2.zero;
        ContentContainer.anchorMax = Vector2.one;
        ContentContainer.offsetMin = Vector2.zero;
        ContentContainer.offsetMax = new Vector2(0, -topMargin); // Push down by topMargin
        
        // Add a layer for Raycasting/Interaction if needed, or leave empty
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
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);
        Image img = bgObj.AddComponent<Image>();
        
        img.type = Image.Type.Sliced;
        img.sprite = GetRoundedSprite();
        
        Shader blurShader = Shader.Find("Custom/UIBlurBackground");
        if (blurShader != null)
        {
            Material blurMat = new Material(blurShader);
            blurMat.SetFloat("_Radius", 4.0f); 
            img.material = blurMat;
            img.color = glassColor; 
        }
        else
        {
            img.color = glassColor;
        }

        BoxCollider bgCol = bgObj.AddComponent<BoxCollider>();
        bgCol.size = new Vector3(w, h, 0.1f);
        
        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; 
        rt.sizeDelta = Vector2.zero; rt.localScale = Vector3.one;
        rt.localPosition = Vector3.zero;
        rt.SetAsFirstSibling();
        
        // Borders
        CreateBorder(bgObj.transform, 12, new Color(0.6f, 0.9f, 1.0f, 0.9f), 0);
        CreateBorder(bgObj.transform, 24, new Color(0.0f, 0.5f, 1.0f, 0.15f), 1, new Vector2(-6, -6), new Vector2(6, 6));

        // FX
        CreateFloatingDataEffects(bgObj.transform, w, h);
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

    void CreateStatusBar(Transform parent, float w, float h, float height)
    {
        GameObject barObj = new GameObject("StatusBar");
        barObj.transform.SetParent(parent, false);
        RectTransform rt = barObj.AddComponent<RectTransform>();
        
        rt.anchorMin = new Vector2(0, 1); 
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0, height);
        rt.anchoredPosition = Vector2.zero;
        
        // --- CLOCK ---
        GameObject timeObj = CreateText(barObj.transform, "12:00", Vector2.zero, 42, new Color(1f, 1f, 1f, 0.9f), true);
        RectTransform timeRT = timeObj.GetComponent<RectTransform>();
        timeRT.anchorMin = new Vector2(0, 0); 
        timeRT.anchorMax = new Vector2(0.5f, 1);
        timeRT.pivot = new Vector2(0, 0.5f);
        timeRT.offsetMin = new Vector2(sidePadding, 0);
        timeRT.offsetMax = new Vector2(0, 0);
        timeRT.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
        _clockText = timeRT.GetComponent<TextMeshProUGUI>();

        // --- STATUS GROUP ---
        GameObject statusGroup = new GameObject("StatusGroup");
        statusGroup.transform.SetParent(barObj.transform, false);
        RectTransform groupRT = statusGroup.AddComponent<RectTransform>();
        groupRT.anchorMin = new Vector2(1, 0); 
        groupRT.anchorMax = new Vector2(1, 1);
        groupRT.pivot = new Vector2(1, 0.5f);
        groupRT.sizeDelta = new Vector2(400, 0); 
        groupRT.anchoredPosition = new Vector2(-sidePadding, 0);

        HorizontalLayoutGroup layout = statusGroup.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.spacing = -175f; // Matches user request
        layout.childControlWidth = false;
        layout.childControlHeight = false;

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
    }

    // --- ASSET LOADERS ---
    
    void LoadIcons()
    {
        if (iconSignal == null) iconSignal = Resources.Load<Sprite>("MainMenu/icon_signal");
        if (iconWifi == null) iconWifi = Resources.Load<Sprite>("MainMenu/icon_wifi");
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

        int bodyW = 114; 
        int radius = 12; 
        int nubW = 10;   
        int nubH = 28;   
        int nubY = (h - nubH) / 2;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                colors[y * w + x] = Color.clear;
                if (x < bodyW)
                {
                    bool inCorner = (x < radius && y < radius) || 
                                    (x > bodyW - radius - 1 && y < radius) ||
                                    (x < radius && y > h - radius - 1) || 
                                    (x > bodyW - radius - 1 && y > h - radius - 1);
                    if (inCorner)
                    {
                        float cx = (x < bodyW/2) ? radius : bodyW - radius - 1;
                        float cy = (y < h/2) ? radius : h - radius - 1;
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                        float alpha = Mathf.Clamp01((radius + 0.5f) - d);
                        colors[y*w+x] = new Color(1,1,1,alpha);
                    }
                    else if (y >= 0 && y < h && x >= 0) colors[y*w+x] = Color.white;
                }
                if (x >= bodyW && x < bodyW + nubW)
                {
                    if (y >= nubY && y < nubY + nubH)
                    {
                        int nr = 4;
                        bool inNubCorner = (x > bodyW + nubW - nr - 1 && y < nubY + nr) ||
                                           (x > bodyW + nubW - nr - 1 && y > nubY + nubH - nr - 1);
                        if (inNubCorner)
                        {
                            float cx = bodyW + nubW - nr - 1;
                            float cy = (y < h/2) ? nubY + nr : nubY + nubH - nr - 1;
                            float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                            float alpha = Mathf.Clamp01((nr + 0.5f) - d);
                            colors[y*w+x] = new Color(1,1,1,alpha);
                        }
                        else colors[y*w+x] = Color.white;
                    }
                }
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        _batterySprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
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
