using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Marker component on Board quad to identify which panel it belongs to.
/// Used by WorldPanelGazeBridge to detect panel under gaze.
/// </summary>
public class WorldPanelPlusBoardRaycatcher : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public WorldPanelPlus panel;

    public void OnPointerEnter(PointerEventData eventData) { }
    public void OnPointerExit(PointerEventData eventData) { }
}
