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
    public Color colorIdle = new Color(1f, 1f, 1f, 0.9f);
    public Color colorInteract = new Color(1f, 0f, 0f, 1f);

    private Image _reticleImage;
    private Camera _cam;
    private float _baseScale; 

    void Start()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = Camera.main;
        
        CreateReticle();
    }

    void CreateReticle()
    {
        // 1. Tạo Canvas con gắn thẳng vào Camera
        GameObject canvasObj = new GameObject("GazeReticleCanvas");
        canvasObj.transform.SetParent(_cam.transform, false);
        
        Canvas c = canvasObj.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        c.sortingOrder = 32767; // Vẽ đè lên tất cả các UI khác

        // 2. Tạo Chấm Trắng
        GameObject imgObj = new GameObject("Dot");
        imgObj.transform.SetParent(canvasObj.transform, false);
        
        _reticleImage = imgObj.AddComponent<Image>();
        _reticleImage.sprite = GetCircleSprite();
        _reticleImage.color = colorIdle;
        _reticleImage.raycastTarget = false; 

        RectTransform imgRT = imgObj.GetComponent<RectTransform>();
        imgRT.sizeDelta = new Vector2(100, 100); 
        imgRT.anchoredPosition = Vector3.zero;

        // 3. LOGIC "DÍNH VÀO CAMERA": Đặt tại NearClipPlane + 0.01
        // Vị trí này đảm bảo Reticle luôn nằm giữa Camera và bất kỳ vật thể nào khác (vì vật thể gần hơn sẽ bị Clip)
        float zDepth = _cam.nearClipPlane + 0.01f;

        RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(0, 0); 
        canvasRT.localScale = Vector3.one; 
        canvasRT.localPosition = new Vector3(0, 0, zDepth); // Gần như bằng 0 (so với world scale)
        canvasRT.localRotation = Quaternion.identity;

        // 4. Tính toán Scale để chấm không bị "to quá khổ" khi để quá gần
        // Công thức: scale theo tỷ lệ độ sâu
        _baseScale = (reticleSize / 100f) * zDepth; 
        
        imgRT.localScale = Vector3.one * _baseScale;
    }

    void Update()
    {
        CheckGaze();
    }

    void CheckGaze()
    {
        // Ray bắn thẳng từ tâm Camera
        Ray ray = new Ray(_cam.transform.position, _cam.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 100f))
        {
            // Tương tác nếu vật có Button, Toggle, hoặc BoxCollider (nút VR)
            bool isInteractable = hit.collider.GetComponent<Button>() != null 
                               || hit.collider.GetComponent<Toggle>() != null 
                               || hit.collider.GetComponent<BoxCollider>() != null;

            SetState(isInteractable);
        }
        else
        {
            SetState(false);
        }
    }

    void SetState(bool active)
    {
        if (_reticleImage)
        {
            // Màu
            Color targetCol = active ? colorInteract : colorIdle;
            _reticleImage.color = Color.Lerp(_reticleImage.color, targetCol, Time.deltaTime * 20f);
            
            // Phình to nhẹ (1.8x)
            float scaleMult = active ? 1.8f : 1.0f;
            float targetScale = _baseScale * scaleMult;
            
            float currentScale = _reticleImage.rectTransform.localScale.x;
            float newScale = Mathf.Lerp(currentScale, targetScale, Time.deltaTime * 20f);
            
            _reticleImage.rectTransform.localScale = new Vector3(newScale, newScale, 1f);
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
