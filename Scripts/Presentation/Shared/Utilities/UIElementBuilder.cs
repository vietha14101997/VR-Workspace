using UnityEngine;
using UnityEngine.UI;

namespace VRWorkspace.UI.Utilities
{
    /// <summary>
    /// Utility class for creating and configuring UI element RectTransforms.
    /// Centralizes common RectTransform setup patterns to reduce code duplication.
    /// </summary>
    public static class UIElementBuilder
    {
        #region RectTransform Setup

        /// <summary>
        /// Creates a RectTransform that stretches to fill its parent completely.
        /// Equivalent to: anchorMin=0,0  anchorMax=1,1  offsetMin=0,0  offsetMax=0,0
        /// </summary>
        public static RectTransform CreateFullStretch(GameObject obj, Transform parent = null)
        {
            if (parent != null) obj.transform.SetParent(parent, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// Creates a RectTransform centered in its parent with a fixed size.
        /// </summary>
        public static RectTransform CreateCentered(GameObject obj, Transform parent, Vector2 size)
        {
            if (parent != null) obj.transform.SetParent(parent, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>
        /// Creates a RectTransform with custom anchor and offset settings.
        /// </summary>
        public static RectTransform CreateAnchored(GameObject obj, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            if (parent != null) obj.transform.SetParent(parent, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        /// <summary>
        /// Creates a RectTransform anchored to a specific position with fixed size.
        /// Common anchors: TopLeft(0,1), TopRight(1,1), BottomLeft(0,0), BottomRight(1,0), Center(0.5,0.5)
        /// </summary>
        public static RectTransform CreateAnchoredPosition(GameObject obj, Transform parent,
            Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 anchoredPosition)
        {
            if (parent != null) obj.transform.SetParent(parent, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPosition;
            return rt;
        }

        /// <summary>
        /// Creates a RectTransform that stretches horizontally but has fixed height at top.
        /// </summary>
        public static RectTransform CreateTopStretch(GameObject obj, Transform parent, float height)
        {
            if (parent != null) obj.transform.SetParent(parent, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// Creates a RectTransform that stretches horizontally but has fixed height at bottom.
        /// </summary>
        public static RectTransform CreateBottomStretch(GameObject obj, Transform parent, float height)
        {
            if (parent != null) obj.transform.SetParent(parent, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        #endregion

        #region GameObject Creation with RectTransform

        /// <summary>
        /// Creates a new GameObject with a full-stretch RectTransform.
        /// </summary>
        public static GameObject CreateFullStretchObject(string name, Transform parent)
        {
            GameObject obj = new GameObject(name);
            CreateFullStretch(obj, parent);
            return obj;
        }

        /// <summary>
        /// Creates a new GameObject with a centered RectTransform.
        /// </summary>
        public static GameObject CreateCenteredObject(string name, Transform parent, Vector2 size)
        {
            GameObject obj = new GameObject(name);
            CreateCentered(obj, parent, size);
            return obj;
        }

        #endregion

        #region Apply to Existing RectTransform

        /// <summary>
        /// Applies full-stretch settings to an existing RectTransform.
        /// </summary>
        public static void ApplyFullStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Applies centered settings to an existing RectTransform.
        /// </summary>
        public static void ApplyCentered(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
        }

        #endregion

        #region Hit Area Setup

        /// <summary>
        /// Sets up a standard VR hit area with BoxCollider and transparent Image.
        /// Used for buttons, input fields, dropdowns, etc.
        /// </summary>
        public static (Image hitImage, BoxCollider collider) SetupHitArea(GameObject hitArea,
            float width, float height, string layerName = "VirtualObjects")
        {
            // Transparent image for UI raycast
            Image hitImg = hitArea.AddComponent<Image>();
            hitImg.color = Color.clear;

            // BoxCollider for VR raycast
            BoxCollider col = hitArea.AddComponent<BoxCollider>();
            col.size = new Vector3(width, height, 0.1f);
            col.center = new Vector3(0, 0, -0.1f);

            // Set layer
            int vrLayer = LayerMask.NameToLayer(layerName);
            if (vrLayer != -1) hitArea.layer = vrLayer;

            return (hitImg, col);
        }

        #endregion
    }
}
