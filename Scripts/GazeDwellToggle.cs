using UnityEngine;

public class GazeDwellToggle : MonoBehaviour
{
    [Header("Refs")]
    public Camera vrCamera;              // kéo VRCamera (Main Camera) vào
    public ModeController modeController;// script đổi Virtual/Real (tên bạn đang dùng)
    [Header("Gaze")]
    public float dwellTime = 2.0f;       // nhìn đủ 2s thì chuyển mode
    public float maxDistance = 10f;      // tầm ray
    public LayerMask hitMask;            // chọn VirtualObjects
    public bool requireExitToRetrigger = true; // phải rời mắt mới cho kích lại

    float _timer;
    bool _armed = true;                  // chờ được kích lại
    Renderer _rend;
    Color _baseColor;
    MaterialPropertyBlock _mpb;

    void Awake()
    {
        _rend = GetComponentInChildren<Renderer>();
        if (_rend) { _mpb = new MaterialPropertyBlock(); _baseColor = _rend.sharedMaterial.HasProperty("_Color") ? _rend.sharedMaterial.color : Color.white; }
        if (hitMask.value == 0) hitMask = LayerMask.GetMask("VirtualObjects");
    }

    void Update()
    {
        if (!vrCamera || !modeController) return;

        var ray = new Ray(vrCamera.transform.position, vrCamera.transform.forward);
        bool hitTreasure = Physics.Raycast(ray, out var hit, maxDistance, hitMask)
                           && hit.collider.transform.IsChildOf(transform);

        if (hitTreasure && _armed)
        {
            _timer += Time.deltaTime;
            SetProgressVisual(_timer / dwellTime);
            if (_timer >= dwellTime)
            {
                modeController.ToggleMode();
                _timer = 0f;
                if (requireExitToRetrigger) _armed = false; // phải nhìn chỗ khác rồi mới cho lần sau
            }
        }
        else
        {
            // Reset khi rời mắt
            _timer = 0f;
            SetProgressVisual(0f);
            if (!hitTreasure) _armed = true;
        }
    }

    void SetProgressVisual(float t)
    {
        if (_rend == null || _mpb == null) return;
        Color c = Color.Lerp(_baseColor, Color.green, Mathf.Clamp01(t));
        _rend.GetPropertyBlock(_mpb);
        _mpb.SetColor("_Color", c);
        _rend.SetPropertyBlock(_mpb);
    }
}
