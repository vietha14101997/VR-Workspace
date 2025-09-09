using UnityEngine;
using UnityEngine.EventSystems;

public enum WPDockButtonType
{
    MoveMode,        // bật chế độ kéo-di chuyển (Center) qua Dock
    YawLeft15,       // xoay Y -15°
    YawRight15,      // xoay Y +15°
    MinimizeToggle,  // thu nhỏ/hiện Dock
    PitchUp15,       // xoay X +15°
    PitchDown15,     // xoay X -15°
    ResetFaceCamera  // reset: đối diện camera (giữ vị trí)
}

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class WorldPanelPlusDockButton : MonoBehaviour, IPointerDownHandler
{
    [HideInInspector] public WorldPanelPlus panel;
    public WPDockButtonType type;

    const string kVisualName = "Visual";

    /// <summary>
    /// Dựng phần nhìn (quad) dùng chung material với Tray.
    /// Tự hủy collider phát sinh từ Primitive và căn đúng kích thước.
    /// </summary>
    public void BuildVisual(Material mat, Vector2 size, float zOffset)
    {
        // Tìm/tao con "Visual"
        Transform visTr = transform.Find(kVisualName);
        GameObject quadGo;
        if (visTr == null)
        {
            quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = kVisualName;
            quadGo.transform.SetParent(transform, false);
        }
        else
        {
            quadGo = visTr.gameObject;
        }

        // Kích thước + vị trí
        quadGo.transform.localScale = new Vector3(size.x, size.y, 1f);
        quadGo.transform.localPosition = new Vector3(0f, 0f, zOffset);

        // Hủy collider auto của Primitive nếu có
        var autoCol = quadGo.GetComponent<Collider>();
#if UNITY_EDITOR
        if (autoCol) DestroyImmediate(autoCol);
#else
        if (autoCol) Destroy(autoCol);
#endif

        // Áp vật liệu
        var mr = quadGo.GetComponent<MeshRenderer>();
        if (!mr) mr = quadGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;

        // Bảo đảm collider của chính nút có kích thước khớp (theo world scale)
        var bc = GetComponent<BoxCollider>();
        if (bc)
        {
            // Quy đổi size mong muốn (world) -> local size collider
            Vector3 ls = transform.lossyScale;
            float sx = Mathf.Max(1e-5f, ls.x);
            float sy = Mathf.Max(1e-5f, ls.y);
            float sz = Mathf.Max(1e-5f, ls.z);
            // Độ dày Z mỏng nhẹ để raycast ổn định
            const float worldDepth = 0.02f;
            const float zCenter = 0.01f;
            bc.size = new Vector3(size.x / sx, size.y / sy, worldDepth / sz);
            bc.center = new Vector3(0f, 0f, zCenter / sz);
        }
    }

    /// <summary>
    /// Sự kiện nhấn (Cardboard/Pointer). Chỉ thực thi tối thiểu để không lệch luồng WorldPanelGazeBridge.
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (type == WPDockButtonType.MoveMode && panel)
        {
            // Cố định anchor tại vị trí nút; bật cờ cho phép kéo Center nếu hệ khác cần.
            panel.moveAnchorWorld = transform.position;
            // KHÔNG tự bật centerDragEnabled ở đây để tránh xung đột —
            // WorldPanelGazeBridge sẽ đảm nhiệm khởi tạo kéo theo ray.
        }
    }
}
