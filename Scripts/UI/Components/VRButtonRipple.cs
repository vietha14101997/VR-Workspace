using UnityEngine;
using UnityEngine.UI;

namespace VRWorkspace.UI.Components
{
    /// <summary>
    /// Stub component for backward compatibility.
    /// Shimmer effect is handled by VRButtonAnimation via _HoverAmount.
    /// </summary>
    public class VRButtonRipple : MonoBehaviour
    {
        public void Initialize(Material mat, Image img) { }
        public void TriggerRipple(Vector2 normalizedPosition) { }
        public void TriggerFlash() { }
    }

}
