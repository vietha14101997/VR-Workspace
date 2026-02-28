using UnityEngine;

namespace VRWorkspace.UI.Components
{
    /// <summary>
    /// Simple component to track if a button is currently "selected" in a UI group.
    /// Used by VRGazeReticle to block dwell clicking on elements that are already active.
    /// </summary>
    public class VRSelectedButton : MonoBehaviour
    {
        public bool IsSelected = false;
    }

}
