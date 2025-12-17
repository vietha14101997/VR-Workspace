using UnityEngine;
using UnityEngine.UI;
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
    }

    void CreateReticle()
    {
        // 1. Tạo Canvas con
        GameObject canvasObj = new GameObject("GazeReticleCanvas");
        canvasObj.transform.SetParent(_cam.transform, false);
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

        // 2. Tạo Chấm
        GameObject imgObj = new GameObject("Dot");
        imgObj.transform.SetParent(canvasObj.transform, false);
        imgObj.layer = _cam.gameObject.layer;
        
        _reticleImage = imgObj.AddComponent<Image>();
        _reticleImage.sprite = GetCircleSprite();
        _reticleImage.color = colorInteract;
        _reticleImage.raycastTarget = false; 
        
        // Mặc định ẩn
        _reticleImage.enabled = false;

        // Giữ ZTest Always để không bị xuyên tường (khi ở đúng vị trí bề mặt)
        Material zTestMat = new Material(Shader.Find("UI/Default"));
        zTestMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        _reticleImage.material = zTestMat;

        RectTransform imgRT = imgObj.GetComponent<RectTransform>();
        imgRT.sizeDelta = new Vector2(100, 100); 
        imgRT.localScale = Vector3.one;
        imgRT.anchoredPosition = Vector3.zero;
    }

    void Update()
    {
        CheckGaze();
    }

    void CheckGaze()
    {
        Ray ray = new Ray(_cam.transform.position, _cam.transform.forward);
        RaycastHit hit;

        // Chỉ raycast vào layer VirtualObjects
        if (_layerMask != 0 && Physics.Raycast(ray, out hit, 100.0f, _layerMask))
        {
            if (!_reticleImage.enabled) _reticleImage.enabled = true;

            // Di chuyển Reticle tới đúng khoảng cách va chạm
            // Điều này giải quyết vấn đề "lác mắt" (convergence conflict)
            float dist = hit.distance;
            // Đảm bảo không quá gần camera (near clip)
            if (dist < _cam.nearClipPlane) dist = _cam.nearClipPlane + 0.05f;

            _canvasRT.localPosition = new Vector3(0, 0, dist);

            // Tính scale để giữ kích thước hiển thị ổn định (perspective compensation)
            // Scale tỉ lệ thuận với distance
            float scale = (reticleSize / 100f) * dist;
            _reticleImage.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
        else
        {
            // Không va chạm với VirtualObjects -> Ẩn
            if (_reticleImage.enabled) _reticleImage.enabled = false;
        }
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
}
