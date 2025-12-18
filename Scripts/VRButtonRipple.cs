using UnityEngine;
using UnityEngine.UI;

public class VRButtonRipple : MonoBehaviour
{
    private Material _material;
    private Image _image;
    private bool _isAnimating = false;
    private float _rippleProgress = 0f;
    private float _rippleDuration = 0.5f;
    
    public void Initialize(Material mat, Image img)
    {
        _material = mat;
        _image = img;
    }
    
    public void TriggerRipple(Vector2 normalizedPosition)
    {
        if (_material == null) return;
        
        if (_material.HasProperty("_RippleCenter"))
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
        }
        
        if (_material.HasProperty("_RippleProgress"))
            _material.SetFloat("_RippleProgress", _rippleProgress);
    }
}
