using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Simple click handler for VR buttons.
/// Triggers ripple effect on click.
/// NOTE: Hover effects are now handled by HoverEffectController.
/// This component is kept for backward compatibility with existing prefabs.
/// </summary>
public class VRButtonAnimation : MonoBehaviour, IPointerClickHandler
{
    [Header("Target")]
    [Tooltip("Target visuals transform (used for ripple position calculation)")]
    public Transform targetVisuals;

    [Header("Legacy Fields (Not Used)")]
    [Tooltip("These fields are kept for backward compatibility but are no longer used. Hover effects are handled by HoverEffectController.")]
    public float popAmount = 0.1f;
    public float hoverScaleAmount = 0.05f;
    public float hoverBorderMultiplier = 1f;

    public void OnPointerClick(PointerEventData eventData)
    {
        // Trigger ripple effect
        var ripple = GetComponentInChildren<VRButtonRipple>();
        if (ripple != null)
        {
            // Calculate click position in normalized coordinates
            RectTransform rt = targetVisuals?.GetComponent<RectTransform>();
            if (rt != null && eventData != null)
            {
                Vector2 localPoint;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out localPoint);
                Vector2 normalizedPos = new Vector2(
                    (localPoint.x / rt.rect.width) + 0.5f,
                    (localPoint.y / rt.rect.height) + 0.5f
                );
                ripple.TriggerRipple(normalizedPos);
            }
            else
            {
                ripple.TriggerRipple(new Vector2(0.5f, 0.5f));
            }
        }
    }

    /// <summary>
    /// Trigger ripple at a specific normalized position
    /// </summary>
    public void TriggerRippleAt(Vector2 normalizedPos)
    {
        var ripple = GetComponentInChildren<VRButtonRipple>();
        ripple?.TriggerRipple(normalizedPos);
    }

    /// <summary>
    /// Trigger ripple at center
    /// </summary>
    public void TriggerRippleCenter()
    {
        TriggerRippleAt(new Vector2(0.5f, 0.5f));
    }
}
