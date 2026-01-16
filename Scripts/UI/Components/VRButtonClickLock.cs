using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Safety lock for VR buttons to prevent continuous clicking.
/// After a click, the button is locked until the reticle/pointer exits the button area.
/// This prevents accidental double-clicks and rapid repeated clicks.
///
/// This component works with VRGazeReticle's dwell click system.
/// - Lock() is called by VRGazeReticle when dwell click is triggered
/// - Unlock() is called by VRGazeReticle when pointer exits the button
/// </summary>
public class VRButtonClickLock : MonoBehaviour
{
    private bool _isLocked = false;

    /// <summary>
    /// Check if button is currently locked
    /// </summary>
    public bool IsLocked => _isLocked;

    /// <summary>
    /// Lock the button (called after click)
    /// </summary>
    public void Lock()
    {
        _isLocked = true;
    }

    /// <summary>
    /// Unlock the button (called when pointer exits)
    /// </summary>
    public void Unlock()
    {
        _isLocked = false;
    }

    /// <summary>
    /// Find VRButtonClickLock from a target GameObject (searches in parent and children hierarchy)
    /// </summary>
    public static VRButtonClickLock FindOnButton(GameObject target)
    {
        if (target == null) return null;

        // Try to find on the target itself
        var clickLock = target.GetComponent<VRButtonClickLock>();
        if (clickLock != null) return clickLock;

        // Try to find on parent (Button is usually on HitArea)
        clickLock = target.GetComponentInParent<VRButtonClickLock>();
        if (clickLock != null) return clickLock;

        // Try to find in children (in case target is Wrapper and HitArea is child)
        clickLock = target.GetComponentInChildren<VRButtonClickLock>();
        return clickLock;
    }
}
