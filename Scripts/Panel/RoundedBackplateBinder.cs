using UnityEngine;

[ExecuteAlways]
public class RoundedBackplateBinder : MonoBehaviour
{
    Renderer _r; Transform _t;
    public float border = 0.004f;
    public float fallbackRadiusWhenExpanded = 0.06f;
    public bool forceCircle = false;

    void Awake() { _r = GetComponent<Renderer>(); _t = transform; }
    void OnEnable() { Sync(); }
#if UNITY_EDITOR
    void OnValidate() { Sync(); }
#endif
    void LateUpdate() { Sync(); }

    public void SetForceCircle(bool on) { forceCircle = on; }

    void Sync()
    {
        if (!_r || !_t) return; var m = _r.sharedMaterial; if (!m) return;
        var s = _t.localScale; if (m.HasProperty("_RectWH")) m.SetVector("_RectWH", new Vector4(s.x, s.y, 0, 0)); if (m.HasProperty("_Border")) m.SetFloat("_Border", border);
        float halfMin = 0.5f * Mathf.Min(s.x, s.y); float r = forceCircle ? halfMin : Mathf.Min(halfMin, fallbackRadiusWhenExpanded);
        if (m.HasProperty("_Radius")) m.SetFloat("_Radius", r);
    }
}
