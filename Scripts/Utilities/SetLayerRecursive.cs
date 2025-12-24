using UnityEngine;

public class SetLayerRecursive : MonoBehaviour
{
    [Tooltip("Layer to apply to this object and all children")]
    public int targetLayer;

    void Start()
    {
        SetLayerRecursively(gameObject, targetLayer);
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;

        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
