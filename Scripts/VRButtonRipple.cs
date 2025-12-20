using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Component giữ lại để tương thích ngược.
/// Hiệu ứng click đã được vô hiệu hóa - chỉ giữ hiệu ứng hover của Button.
/// </summary>
public class VRButtonRipple : MonoBehaviour
{
    public void Initialize(Material mat, Image img) { }
    public void TriggerRipple(Vector2 normalizedPosition) { }
    public void TriggerFlash() { }
}
