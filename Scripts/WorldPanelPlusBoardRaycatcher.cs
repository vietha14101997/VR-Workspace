using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Catches pointer enter/exit on the content quad to toggle the tray fade.
/// Requires EventSystem + PhysicsRaycaster on the camera.
/// </summary>
public class WorldPanelPlusBoardRaycatcher : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public WorldPanelPlus panel;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (panel) panel.OnHover(true, null);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (panel) panel.OnHover(false, null);
    }
}