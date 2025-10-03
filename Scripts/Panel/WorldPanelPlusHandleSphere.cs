using UnityEngine;

/// Tạo một sphere nhỏ làm dấu ở mỗi handle (trừ Center)
[RequireComponent(typeof(WorldPanelPlusHandle))]
public class WorldPanelPlusHandleSphere : MonoBehaviour
{
    public float radius = 0.01f;          // bi nhỏ
    public float forwardOffset = 0.01f;   // hơi nổi lên phía trước
    MeshRenderer _mr;

    static Material _defMat;
    static Material GetMat()
    {
        if (_defMat == null)
        {
            var shader = Shader.Find("Unlit/Color");
            shader ??= Shader.Find("Unlit/Texture"); // Fallback
            _defMat = new Material(shader);
            _defMat.color = Color.white;
        }
        return _defMat;
    }

    void Awake()
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = "Marker";
        s.transform.SetParent(transform, false);
        s.transform.localScale = Vector3.one * radius * 2f;
        s.transform.localPosition = new Vector3(0, 0, forwardOffset);
        var col = s.GetComponent<Collider>(); if (col) GameObject.DestroyImmediate(col);
        _mr = s.GetComponent<MeshRenderer>();
        _mr.sharedMaterial = GetMat(); // tránh hiện màu tím
    }

    public void SetVisible(bool on) { if (_mr) _mr.enabled = on; }
}
