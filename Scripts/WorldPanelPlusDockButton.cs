using UnityEngine;
using UnityEngine.EventSystems;

public enum WPDockButtonType
{
    MoveMode,        // bật chế độ kéo-di chuyển (Center) qua Dock
    YawLeft15,       // xoay Y -15°
    YawRight15,      // xoay Y +15°
    MinimizeToggle, // thu nhỏ/hiện Dock
    PitchUp15,       // xoay X +15°
    PitchDown15,     // xoay X -15°
    ResetFaceCamera  // reset: đối diện camera (giữ vị trí)
}

[RequireComponent(typeof(BoxCollider))]
public class WorldPanelPlusDockButton : MonoBehaviour, IPointerDownHandler
{
    [HideInInspector] public WorldPanelPlus panel;
    public WPDockButtonType type;

    // Visual đơn giản (quad, dùng material của Tray)
    public void BuildVisual(Material mat, Vector2 size, float zOffset)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Visual";
        quad.transform.SetParent(transform, false);
        quad.transform.localScale = new Vector3(size.x, size.y, 1);
        quad.transform.localPosition = new Vector3(0, 0, zOffset);

        var col = quad.GetComponent<Collider>(); if (col) GameObject.DestroyImmediate(col);
        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (type == WPDockButtonType.MoveMode && panel)
        {
            panel.moveAnchorWorld = transform.position;   // cố định anchor
        }
    }
}
