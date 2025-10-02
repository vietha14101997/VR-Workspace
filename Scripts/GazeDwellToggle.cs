using UnityEngine;

public class GazeDwellToggle : MonoBehaviour
{
    [Header("Refs")]
    public Camera vrCamera;                 // kéo VRCamera (Overlay) vào
    public ModeController modeController;   // tham chiếu script ở trên

    [Header("Gaze")]
    public float dwellTime = 2.0f;          // nhìn đủ 2s thì chuyển mode
    public float maxDistance = 10f;         // tầm ray
    public LayerMask hitMask;               // mặc định VirtualObjects
    public bool requireExitToRetrigger = true;

    float _timer;
    bool _armed = true;
    Renderer _rend;
    MaterialPropertyBlock _mpb;
    int _colorId = Shader.PropertyToID("_BaseColor"); // URP Lit
    int _fallbackColorId = Shader.PropertyToID("_Color");
    int _fillId = Shader.PropertyToID("_Fill");
    bool _hasBaseColor;
    bool _hasColor;
    bool _hasFill;
    Color _baseColor = Color.white;

    void Awake()
    {
        _rend = GetComponentInChildren<Renderer>();
        if (_rend)
        {
            _mpb = new MaterialPropertyBlock();

            var mat = _rend.sharedMaterial;
            if (mat != null)
            {
                _hasBaseColor = mat.HasProperty(_colorId);
                _hasColor = mat.HasProperty(_fallbackColorId);
                _hasFill = mat.HasProperty(_fillId);

                if (_hasBaseColor) _baseColor = mat.GetColor(_colorId);
                else if (_hasColor) _baseColor = mat.GetColor(_fallbackColorId);
            }
        }

        if (hitMask.value == 0)
            hitMask = LayerMask.GetMask("VirtualObjects");
    }

    void Update()
    {
        if (!vrCamera || !modeController) return;

        var ray = new Ray(vrCamera.transform.position, vrCamera.transform.forward);
        bool hitSelf = Physics.Raycast(ray, out var hit, maxDistance, hitMask)
                       && hit.collider && hit.collider.transform.IsChildOf(transform);

        if (hitSelf && _armed)
        {
            _timer += Time.deltaTime;
            SetProgressVisual(_timer / dwellTime);

            if (_timer >= dwellTime)
            {
                // Gọi transition mượt (nếu ModeController có fadeCanvas sẽ fade)
                modeController.ToggleMode();

                _timer = 0f;
                if (requireExitToRetrigger) _armed = false;
            }
        }
        else
        {
            _timer = 0f;
            SetProgressVisual(0f);
            if (!hitSelf) _armed = true;
        }
    }

    void SetProgressVisual(float t)
    {
        if (_rend == null || _mpb == null) return;
        t = Mathf.Clamp01(t);

        _rend.GetPropertyBlock(_mpb);

        if (_hasFill)
        {
            _mpb.SetFloat(_fillId, t); // nếu shader có tham số fill/progress
        }
        else
        {
            var c = Color.Lerp(_baseColor, Color.green, t);
            if (_hasBaseColor) _mpb.SetColor(_colorId, c);
            else if (_hasColor) _mpb.SetColor(_fallbackColorId, c);
        }

        _rend.SetPropertyBlock(_mpb);
    }
}
