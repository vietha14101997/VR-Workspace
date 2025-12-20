using UnityEngine;
using UnityEngine.UI;

public class VRButtonRipple : MonoBehaviour
{
    private Material _material;
    private Image _image;
    private bool _isAnimating = false;
    private float _rippleProgress = 0f;
    private float _rippleDuration = 0.5f;
    private bool _initialized = false;

    void Start()
    {
        // Auto-initialize if not already initialized (for prefab loading)
        if (_material == null)
        {
            TryAutoInitialize();
        }
    }

    void TryAutoInitialize()
    {
        _image = GetComponent<Image>();
        if (_image != null && _image.material != null)
        {
            // Tạo material instance để tránh ảnh hưởng các button khác
            _material = _image.material;

            // Kiểm tra shader properties
            if (_material.HasProperty("_RippleProgress") && _material.HasProperty("_RippleCenter"))
            {
                _initialized = true;
            }
            else
            {
                Debug.LogWarning($"[VRButtonRipple] Material on {gameObject.name} missing ripple properties. Shader: {_material.shader.name}");
            }
        }
    }

    public void Initialize(Material mat, Image img)
    {
        _material = mat;
        _image = img;

        if (_material != null && _material.HasProperty("_RippleProgress") && _material.HasProperty("_RippleCenter"))
        {
            _initialized = true;
        }
    }

    public void TriggerRipple(Vector2 normalizedPosition)
    {
        // Try to auto-initialize if material is missing
        if (_material == null || !_initialized)
        {
            TryAutoInitialize();
        }

        if (_material == null || !_initialized)
        {
            Debug.LogWarning($"[VRButtonRipple] Cannot trigger ripple - material not initialized on {gameObject.name}");
            return;
        }

        _material.SetVector("_RippleCenter", new Vector4(normalizedPosition.x, normalizedPosition.y, 0, 0));
        _rippleProgress = 0f;
        _isAnimating = true;
    }

    void Update()
    {
        if (!_isAnimating || _material == null) return;

        _rippleProgress += Time.unscaledDeltaTime / _rippleDuration;

        if (_rippleProgress >= 1f)
        {
            _rippleProgress = 0f;
            _isAnimating = false;

            // Reset ripple progress to 0 để ẩn ripple effect
            if (_material.HasProperty("_RippleProgress"))
                _material.SetFloat("_RippleProgress", 0f);
            return;
        }

        if (_material.HasProperty("_RippleProgress"))
            _material.SetFloat("_RippleProgress", _rippleProgress);
    }
}
