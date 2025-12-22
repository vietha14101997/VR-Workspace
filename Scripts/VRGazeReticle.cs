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

    [Header("Dwell Click Settings")]
    [Tooltip("Thời gian phải giữ yên reticle trước khi bắt đầu đếm click (giây)")]
    public float dwellStartDelay = 0.5f;

    [Tooltip("Thời gian đếm ngược để click sau khi bắt đầu dwell (giây)")]
    public float dwellClickTime = 1.0f;

    [Tooltip("Ngưỡng di chuyển tối đa (góc độ) để coi là đứng yên")]
    public float dwellMovementThreshold = 2.0f;

    [Tooltip("Bật/tắt tính năng Dwell Click")]
    public bool dwellClickEnabled = true;

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

    // Dwell Click State
    private Vector3 _lastGazeDirection;
    private float _stableTime = 0f;
    private float _dwellProgress = 0f;
    private bool _isDwelling = false;
    private bool _dwellClickTriggered = false;
    private Image _dwellRing;
    private GameObject _dwellableTarget;
    private RaycastHit _lastHit;
    
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
        _lastGazeDirection = _cam.transform.forward;
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

        // 3. Tạo Dwell Progress Ring (around the reticle dot)
        CreateDwellRing(canvasObj, zTestMat);

        // 4. Tạo Recenter UI (Hidden by default)
        CreateRecenterUI(canvasObj, zTestMat);
    }

    void CreateDwellRing(GameObject parentCanvas, Material overlayMat)
    {
        GameObject ringObj = new GameObject("DwellRing");
        ringObj.transform.SetParent(parentCanvas.transform, false);
        ringObj.layer = parentCanvas.layer;

        _dwellRing = ringObj.AddComponent<Image>();
        _dwellRing.sprite = GetRingSprite();
        _dwellRing.type = Image.Type.Filled;
        _dwellRing.fillMethod = Image.FillMethod.Radial360;
        _dwellRing.fillOrigin = (int)Image.Origin360.Top;
        _dwellRing.fillClockwise = true;
        _dwellRing.color = colorInteract; // Cùng màu với reticle khi va chạm
        _dwellRing.fillAmount = 0f;
        _dwellRing.material = overlayMat;
        _dwellRing.raycastTarget = false;

        RectTransform ringRT = ringObj.GetComponent<RectTransform>();
        ringRT.sizeDelta = new Vector2(300, 300); // Larger than the dot
        ringRT.localScale = Vector3.one;
        ringRT.anchoredPosition = Vector3.zero;

        _dwellRing.enabled = false;
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
        _recenterRing.sprite = GetRingSprite(4.2f); // Mỏng hơn 1/5 so với dwellRing
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
        Vector3 currentGazeDir = _cam.transform.forward;

        if (_layerMask != 0 && Physics.Raycast(ray, out hit, 100.0f, _layerMask))
        {
            if (!_reticleImage.enabled) _reticleImage.enabled = true;

            float dist = hit.distance;
            if (dist < _cam.nearClipPlane) dist = _cam.nearClipPlane + 0.05f;
            _canvasRT.localPosition = new Vector3(0, 0, dist);

            float scale = (reticleSize / 100f) * dist;
            _reticleImage.rectTransform.localScale = new Vector3(scale, scale, 1f);

            // Scale dwell ring theo khoảng cách
            if (_dwellRing != null)
            {
                _dwellRing.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }

            GameObject hitObj = hit.collider.gameObject;
            if (_currentHitObj != hitObj)
            {
                HandlePointerExit(_currentHitObj);
                HandlePointerEnter(hitObj);
                _currentHitObj = hitObj;
                ResetDwellState(); // Reset khi đổi target
            }

            // Lưu hit info để sử dụng khi click
            _lastHit = hit;

            // Xử lý Dwell Click
            if (dwellClickEnabled && _currentHitObj != null)
            {
                ProcessDwellClick(currentGazeDir, hitObj, hit);
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
            ResetDwellState();
        }

        _lastGazeDirection = currentGazeDir;
    }

    void ProcessDwellClick(Vector3 currentGazeDir, GameObject target, RaycastHit hit)
    {
        // Check if dropdown is open - allow dwell on ANY object to close it
        bool hasOpenDropdown = VRDropdown.CurrentlyOpenDropdown != null;
        bool isDropdownOption = hasOpenDropdown && VRDropdown.CurrentlyOpenDropdown.IsPartOfDropdownPanel(target);
        bool isDwellableTarget = IsDwellable(target);

        // If no dropdown open and target is not dwellable, skip
        if (!hasOpenDropdown && !isDwellableTarget)
        {
            ResetDwellState();
            return;
        }

        // Tính góc di chuyển từ frame trước
        float angleMoved = Vector3.Angle(_lastGazeDirection, currentGazeDir);

        // Nếu di chuyển quá nhiều, reset
        if (angleMoved > dwellMovementThreshold * Time.deltaTime * 10f)
        {
            ResetDwellState();
            return;
        }

        // Đã click rồi thì không click lại cho đến khi rời target
        if (_dwellClickTriggered)
        {
            return;
        }

        // Tích lũy thời gian đứng yên
        _stableTime += Time.deltaTime;

        // Phase 1: Chờ đủ thời gian delay trước khi bắt đầu hiển thị progress
        if (_stableTime < dwellStartDelay)
        {
            return;
        }

        // Phase 2: Bắt đầu hiển thị progress ring
        if (!_isDwelling)
        {
            _isDwelling = true;
            _dwellableTarget = target;
            if (_dwellRing != null)
            {
                _dwellRing.enabled = true;
                _dwellRing.fillAmount = 0f;
            }
        }

        // Tính progress (từ 0 đến 1)
        float dwellElapsed = _stableTime - dwellStartDelay;
        _dwellProgress = Mathf.Clamp01(dwellElapsed / dwellClickTime);

        // Cập nhật visual
        if (_dwellRing != null)
        {
            _dwellRing.fillAmount = _dwellProgress;
        }

        // Phase 3: Click khi đủ thời gian
        if (_dwellProgress >= 1f)
        {
            _dwellClickTriggered = true;

            // Ẩn ring ngay sau khi click
            if (_dwellRing != null)
            {
                _dwellRing.enabled = false;
            }

            // Check if there's an open dropdown
            if (hasOpenDropdown)
            {
                if (isDropdownOption)
                {
                    // Target is a dropdown option - perform normal click
                    Vector2 normalizedHitPoint = CalculateNormalizedHitPoint(hit);
                    HandlePointerClick(target, normalizedHitPoint);
                }
                else
                {
                    // Target is NOT part of the dropdown - close dropdown instead of clicking
                    VRDropdown.CurrentlyOpenDropdown.CloseDropdown();
                }
            }
            else if (isDwellableTarget)
            {
                // No dropdown open and target is dwellable - perform normal click
                Vector2 normalizedHitPoint = CalculateNormalizedHitPoint(hit);
                HandlePointerClick(target, normalizedHitPoint);
            }
        }
    }

    Vector2 CalculateNormalizedHitPoint(RaycastHit hit)
    {
        // Sử dụng ray từ camera để tính điểm giao với mặt phẳng của button
        // Điều này chính xác hơn hit.point vì hit.point có thể ở trên bề mặt z của collider

        Transform buttonTransform = hit.transform;

        // Tìm Visuals để lấy RectTransform chính xác
        Transform visuals = buttonTransform.Find("Visuals");
        RectTransform rectTransform = null;

        if (visuals != null)
        {
            rectTransform = visuals.GetComponent<RectTransform>();
        }

        if (rectTransform == null)
        {
            rectTransform = buttonTransform.GetComponent<RectTransform>();
        }

        if (rectTransform != null)
        {
            // Tạo ray từ camera
            Ray gazeRay = new Ray(_cam.transform.position, _cam.transform.forward);

            // Tạo plane từ RectTransform
            // Sử dụng -forward (hướng về phía camera) để đảm bảo raycast hoạt động
            // với buttons ở mọi hướng (kể cả buttons bên lề)
            Vector3 planeNormal = -rectTransform.forward;
            Plane buttonPlane = new Plane(planeNormal, rectTransform.position);

            float distance;
            if (buttonPlane.Raycast(gazeRay, out distance))
            {
                // Điểm giao trên mặt phẳng
                Vector3 worldPoint = gazeRay.GetPoint(distance);

                // Convert sang local space của RectTransform
                Vector3 localPoint = rectTransform.InverseTransformPoint(worldPoint);

                // Lấy rect bounds
                Rect rect = rectTransform.rect;

                // Tính normalized position (0-1)
                float normalizedX = (localPoint.x - rect.x) / rect.width;
                float normalizedY = (localPoint.y - rect.y) / rect.height;

                return new Vector2(
                    Mathf.Clamp01(normalizedX),
                    Mathf.Clamp01(normalizedY)
                );
            }
            else
            {
                // Fallback: nếu plane raycast thất bại, sử dụng hit.point trực tiếp
                Vector3 localPoint = rectTransform.InverseTransformPoint(hit.point);
                Rect rect = rectTransform.rect;

                float normalizedX = (localPoint.x - rect.x) / rect.width;
                float normalizedY = (localPoint.y - rect.y) / rect.height;

                return new Vector2(
                    Mathf.Clamp01(normalizedX),
                    Mathf.Clamp01(normalizedY)
                );
            }
        }

        // Fallback với BoxCollider - sử dụng x, y từ hit point
        BoxCollider boxCol = hit.collider as BoxCollider;
        if (boxCol != null)
        {
            Vector3 localHitPoint = hit.transform.InverseTransformPoint(hit.point);
            Vector3 size = boxCol.size;

            // Tính normalized dựa trên x, y (bỏ qua z)
            float normalizedX = (localHitPoint.x + size.x / 2f) / size.x;
            float normalizedY = (localHitPoint.y + size.y / 2f) / size.y;

            return new Vector2(
                Mathf.Clamp01(normalizedX),
                Mathf.Clamp01(normalizedY)
            );
        }

        // Fallback: trả về trung tâm
        return new Vector2(0.5f, 0.5f);
    }

    bool IsDwellable(GameObject obj)
    {
        if (obj == null) return false;

        // Kiểm tra có Button hoặc IPointerClickHandler không
        Button btn = obj.GetComponentInParent<Button>();
        if (btn != null && btn.interactable) return true;

        IPointerClickHandler clickHandler = obj.GetComponentInParent<IPointerClickHandler>();
        if (clickHandler != null) return true;

        return false;
    }

    void ResetDwellState()
    {
        _stableTime = 0f;
        _dwellProgress = 0f;
        _isDwelling = false;
        _dwellClickTriggered = false;
        _dwellableTarget = null;

        if (_dwellRing != null)
        {
            _dwellRing.enabled = false;
            _dwellRing.fillAmount = 0f;
            _dwellRing.color = colorInteract; // Reset về màu reticle
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

    void HandlePointerClick(GameObject obj)
    {
        HandlePointerClick(obj, new Vector2(0.5f, 0.5f));
    }

    void HandlePointerClick(GameObject obj, Vector2 normalizedHitPoint)
    {
        if (obj == null) return;

        // Tìm Button để trigger click
        Button btn = obj.GetComponentInParent<Button>();
        GameObject target = btn != null ? btn.gameObject : obj;

        // ExecuteEvents.Execute với pointerClickHandler sẽ:
        // 1. Gọi VRButtonAnimation.OnPointerClick -> TriggerFlash
        // 2. Gọi Button.OnPointerClick -> Press() -> onClick.Invoke()
        // Nên không cần gọi btn.onClick.Invoke() riêng nữa
        ExecuteEvents.Execute(target, _pointerData, ExecuteEvents.pointerClickHandler);
    }

    void TriggerRippleEffect(GameObject obj, Vector2 normalizedHitPoint)
    {
        if (obj == null) return;

        // Tìm tất cả VRButtonRipple trong hierarchy của button
        VRButtonRipple[] ripples = null;

        // Strategy 1: Tìm từ parent gốc của button (bao gồm tất cả children)
        Transform buttonRoot = obj.transform;

        // Đi lên để tìm root của button (thường là object có Button component)
        Button btn = obj.GetComponentInParent<Button>();
        if (btn != null)
        {
            buttonRoot = btn.transform;
        }

        // Lấy tất cả VRButtonRipple trong button
        ripples = buttonRoot.GetComponentsInChildren<VRButtonRipple>(true);

        if (ripples != null && ripples.Length > 0)
        {
            Debug.Log($"[VRGazeReticle] Found {ripples.Length} VRButtonRipple(s) on {buttonRoot.name}");
            // Trigger tất cả ripple effects
            foreach (var ripple in ripples)
            {
                Debug.Log($"[VRGazeReticle] Triggering flash on {ripple.gameObject.name}");
                ripple.TriggerRipple(normalizedHitPoint);
            }
        }
        else
        {
            Debug.LogWarning($"[VRGazeReticle] No VRButtonRipple found for {obj.name}, buttonRoot: {buttonRoot.name}");
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
    
    Sprite GetRingSprite(float thickness = 21f)
    {
        int res = 128;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        Color[] c = new Color[res*res];
        float radius = res / 2f; 
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
