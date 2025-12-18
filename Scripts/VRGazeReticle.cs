using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class VRGazeReticle : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Kích thước visual (ảo) của chấm tại khoảng cách 1m.")]
    public float reticleSize = 0.01f; 
    
    public Color colorInteract = new Color(1f, 0f, 0f, 1f);

    private Image _reticleImage;
    private Camera _cam;
    private RectTransform _canvasRT;
    private int _layerMask;

    // Recenter State
    private bool _isRecentering = false;
    private GameObject _recenterGroup;
    private Image _recenterBg;
    private Image _recenterIcon;
    private Image _recenterRing;
    private float _recenterDistance = 2.0f; // Distance from camera

    // State tracking for Hover events
    private GameObject _currentHitObj;
    private PointerEventData _pointerData;
    
    // Singleton access helper (optional, or use FindObjectOfType)
    public static VRGazeReticle Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = Camera.main;
        
        // Tìm Layer "VirtualObjects"
        int layerIndex = LayerMask.NameToLayer("VirtualObjects");
        if (layerIndex != -1)
        {
            _layerMask = 1 << layerIndex;
        }
        else
        {
            Debug.LogWarning("[VRGazeReticle] Layer 'VirtualObjects' not found! Reticle won't work correctly.");
            _layerMask = 0;
        }

        CreateReticle();
        _pointerData = new PointerEventData(EventSystem.current);
    }

    void CreateReticle()
    {
        // 1. Tạo Canvas con
        GameObject canvasObj = new GameObject("GazeReticleCanvas");
        canvasObj.transform.SetParent(_cam.transform, false);
        // Important: Keep layer same as Cam to be visible
        canvasObj.layer = _cam.gameObject.layer; 
        
        Canvas c = canvasObj.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        c.sortingOrder = 30000;

        // Lưu RectTransform để di chuyển depth
        _canvasRT = canvasObj.GetComponent<RectTransform>();
        _canvasRT.sizeDelta = Vector2.zero; 
        _canvasRT.localScale = Vector3.one; 
        _canvasRT.localPosition = Vector3.zero;
        _canvasRT.localRotation = Quaternion.identity;

        // 2. Tạo Chấm (Standard Reticle)
        GameObject imgObj = new GameObject("Dot");
        imgObj.transform.SetParent(canvasObj.transform, false);
        imgObj.layer = _cam.gameObject.layer;
        
        _reticleImage = imgObj.AddComponent<Image>();
        _reticleImage.sprite = GetCircleSprite();
        _reticleImage.color = colorInteract;
        _reticleImage.raycastTarget = false; 
        
        _reticleImage.enabled = false;

        // Giữ ZTest Always để không bị xuyên tường
        Material zTestMat = new Material(Shader.Find("UI/Default"));
        zTestMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        _reticleImage.material = zTestMat;

        RectTransform imgRT = imgObj.GetComponent<RectTransform>();
        imgRT.sizeDelta = new Vector2(100, 100); 
        imgRT.localScale = Vector3.one;
        imgRT.anchoredPosition = Vector3.zero;

        // 3. Tạo Recenter UI (Hidden by default)
        CreateRecenterUI(canvasObj, zTestMat);
    }

    void CreateRecenterUI(GameObject parentCanvas, Material overlayMat)
    {
        _recenterGroup = new GameObject("RecenterGroup");
        _recenterGroup.transform.SetParent(parentCanvas.transform, false);
        _recenterGroup.layer = parentCanvas.layer;
        
        RectTransform grpRT = _recenterGroup.AddComponent<RectTransform>();
        grpRT.anchorMin = Vector2.zero; grpRT.anchorMax = Vector2.zero;
        // Size referencing roughly 20-30cm in world space when scaled properly
        grpRT.sizeDelta = new Vector2(256, 256); 
        grpRT.anchoredPosition = Vector3.zero;

        // A. Background (Dark Circle)
        GameObject bgObj = new GameObject("Bg");
        bgObj.transform.SetParent(_recenterGroup.transform, false);
        _recenterBg = bgObj.AddComponent<Image>();
        _recenterBg.sprite = GetCircleSprite();
        _recenterBg.color = new Color(0, 0, 0, 0.4f); // Semi-transparent dark
        _recenterBg.material = overlayMat;
        RectTransform bgRT = bgObj.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;

        // B. Icon (White, Center)
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(_recenterGroup.transform, false);
        _recenterIcon = iconObj.AddComponent<Image>();
        _recenterIcon.preserveAspect = true;
        _recenterIcon.color = Color.white;
        _recenterIcon.material = overlayMat;
        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        // Make icon smaller than background
        iconRT.anchorMin = Vector2.zero; iconRT.anchorMax = Vector2.one;
        iconRT.sizeDelta = new Vector2(-100, -100); 
        iconRT.anchoredPosition = Vector2.zero;

        // C. Progress Ring
        GameObject ringObj = new GameObject("Ring");
        ringObj.transform.SetParent(_recenterGroup.transform, false);
        _recenterRing = ringObj.AddComponent<Image>();
        _recenterRing.sprite = GetRingSprite();
        _recenterRing.type = Image.Type.Filled;
        _recenterRing.fillMethod = Image.FillMethod.Radial360;
        _recenterRing.fillOrigin = (int)Image.Origin360.Top;
        _recenterRing.fillClockwise = true; // "Vẽ từ từ" - usually clockwise
        _recenterRing.color = new Color(1f, 0f, 0.4f, 1f); // Pink/Reddish color from user reference or just White? 
        // User image shows a Pink/Magenta ring. I'll use a bright color, or default White.
        // Let's use magenta to match the "neon" vibe.
        _recenterRing.color = new Color(1f, 0.2f, 0.6f); 
        _recenterRing.fillAmount = 0f;
        _recenterRing.material = overlayMat;
        
        RectTransform ringRT = ringObj.GetComponent<RectTransform>();
        ringRT.anchorMin = Vector2.zero; ringRT.anchorMax = Vector2.one;
        ringRT.sizeDelta = Vector2.zero;

        _recenterGroup.SetActive(false);
    }

    public void EnterRecenterMode(Sprite icon)
    {
        _isRecentering = true;
        // Hide standard reticle
        if (_reticleImage != null) _reticleImage.enabled = false;
        
        // Show Recenter UI
        if (_recenterGroup != null)
        {
            _recenterGroup.SetActive(true);
            if (icon != null) _recenterIcon.sprite = icon;
            _recenterRing.fillAmount = 0f;
        }
    }

    public void UpdateRecenterProgress(float progress)
    {
        if (_recenterRing != null)
            _recenterRing.fillAmount = progress;
    }

    public void ExitRecenterMode()
    {
        _isRecentering = false;
        if (_recenterGroup != null) _recenterGroup.SetActive(false);
        
        // Restore standard reticle state based on current gaze next frame
    }

    void Update()
    {
        if (_isRecentering)
        {
            UpdateRecenterPosition();
        }
        else
        {
            CheckGaze();
        }
    }

    void UpdateRecenterPosition()
    {
        // Position fixed distance from camera
        if (_canvasRT == null || _cam == null) return;

        // We reset the canvas local position relative to camera parent
        // However, CreateReticle sets parent to _cam. 
        // So localPosition (0,0, dist) puts it at center of view.
        // We want it to stay there regardless of raycast hits.
        
        _canvasRT.localPosition = new Vector3(0, 0, _recenterDistance);
        _canvasRT.localRotation = Quaternion.identity;

        // Constant size on screen
        // "Reticle Size" logic: 
        // scale = (reticleSize / 100f) * dist; -> at 1m, scale is reticleSize/100.
        // For Recenter UI (256px), we want it to look like maybe 20cm?
        // Let's scale it so it's readable.
        
        float scale = 0.0004f * _recenterDistance; 
        _canvasRT.localScale = Vector3.one * scale;
    }

    void CheckGaze()
    {
        // Reset scale canvas về chuẩn vì RecenterMode có thể đã đổi nó
        if (_canvasRT.localScale != Vector3.one) _canvasRT.localScale = Vector3.one;

        Ray ray = new Ray(_cam.transform.position, _cam.transform.forward);
        RaycastHit hit;

        if (_layerMask != 0 && Physics.Raycast(ray, out hit, 100.0f, _layerMask))
        {
            if (!_reticleImage.enabled) _reticleImage.enabled = true;

            float dist = hit.distance;
            if (dist < _cam.nearClipPlane) dist = _cam.nearClipPlane + 0.05f;
            _canvasRT.localPosition = new Vector3(0, 0, dist);

            float scale = (reticleSize / 100f) * dist;
            _reticleImage.rectTransform.localScale = new Vector3(scale, scale, 1f);

            GameObject hitObj = hit.collider.gameObject;
            if (_currentHitObj != hitObj)
            {
                HandlePointerExit(_currentHitObj);
                HandlePointerEnter(hitObj);
                _currentHitObj = hitObj;
            }
        }
        else
        {
            if (_reticleImage.enabled) _reticleImage.enabled = false;
            
            if (_currentHitObj != null)
            {
                HandlePointerExit(_currentHitObj);
                _currentHitObj = null;
            }
        }
    }

    void HandlePointerEnter(GameObject obj)
    {
        if (obj == null) return;
        ExecuteEvents.Execute(obj, _pointerData, ExecuteEvents.pointerEnterHandler);
        
        Selectable selectable = obj.GetComponentInParent<Selectable>();
        if (selectable) selectable.OnPointerEnter(_pointerData);
    }

    void HandlePointerExit(GameObject obj)
    {
        if (obj == null) return;
        ExecuteEvents.Execute(obj, _pointerData, ExecuteEvents.pointerExitHandler);

        Selectable selectable = obj.GetComponentInParent<Selectable>();
        if (selectable) selectable.OnPointerExit(_pointerData);
    }

    Sprite GetCircleSprite()
    {
        int res = 64;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        Color[] c = new Color[res*res];
        float radius = res / 2f;
        Vector2 center = new Vector2(radius, radius);

        for(int y=0; y<res; y++)
        {
            for(int x=0; x<res; x++)
            {
                float d = Vector2.Distance(new Vector2(x,y), center);
                float alpha = Mathf.Clamp01(radius - d); 
                alpha = Mathf.Pow(alpha, 2f); 
                c[y*res+x] = new Color(1,1,1, alpha);
            }
        }
        tex.SetPixels(c);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0,0,res,res), new Vector2(0.5f,0.5f));
    }
    
    Sprite GetRingSprite()
    {
        int res = 128;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        Color[] c = new Color[res*res];
        float radius = res / 2f;
        float thickness = 10f; 
        Vector2 center = new Vector2(radius, radius);

        for(int y=0; y<res; y++)
        {
            for(int x=0; x<res; x++)
            {
                float d = Vector2.Distance(new Vector2(x,y), center);
                if (d > radius) { c[y*res+x] = Color.clear; continue; }
                
                float edgeAlpha = Mathf.Clamp01     (radius - d); 
                float innerAlpha = Mathf.Clamp01(d - (radius - thickness)); 
                
                float alpha = edgeAlpha * innerAlpha;
                alpha = Mathf.Pow(alpha, 0.5f);
                
                c[y*res+x] = new Color(1,1,1, alpha);
            }
        }
        tex.SetPixels(c);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0,0,res,res), new Vector2(0.5f,0.5f));
    }
}
