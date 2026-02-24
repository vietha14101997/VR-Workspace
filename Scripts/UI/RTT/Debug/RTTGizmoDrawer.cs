using UnityEngine;
using System.Collections.Generic;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.UI.RTT.Debug
{
    /// <summary>
    /// Debug gizmo drawer for RTT panels.
    /// Draws quad bounds, raycast rays, and UV hit points in Scene view.
    /// </summary>
    public class RTTGizmoDrawer : MonoBehaviour
    {
        [Header("Gizmo Settings")]
        [SerializeField] private bool drawQuadBounds = true;
        [SerializeField] private bool drawRaycastRays = true;
        [SerializeField] private bool drawHitPoints = true;

        [Header("Colors")]
        [SerializeField] private Color quadBoundsColor = Color.cyan;
        [SerializeField] private Color rayColor = Color.green;
        [SerializeField] private Color hitPointColor = Color.red;
        [SerializeField] private Color missRayColor = Color.gray;

        [Header("Settings")]
        [SerializeField] private float hitPointSize = 0.02f;
    #pragma warning disable 0414 // Reserved for max ray visualization length
        [SerializeField] private float rayLength = 10f;
    #pragma warning restore 0414

        // Cache for drawing rays
        private static List<RayDrawData> _raysToDraw = new List<RayDrawData>();
        private static List<Vector3> _hitPoints = new List<Vector3>();

        private struct RayDrawData
        {
            public Vector3 origin;
            public Vector3 direction;
            public float distance;
            public bool hit;
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            // Draw quad bounds for all registered panels
            if (drawQuadBounds && RTTManager.Instance != null)
            {
                foreach (var panel in RTTManager.Instance.GetRegisteredPanels())
                {
                    if (panel == null) continue;
                    DrawPanelBounds(panel);
                }
            }

            // Draw cached rays
            if (drawRaycastRays)
            {
                foreach (var ray in _raysToDraw)
                {
                    Gizmos.color = ray.hit ? rayColor : missRayColor;
                    Gizmos.DrawRay(ray.origin, ray.direction * ray.distance);
                }
            }

            // Draw hit points
            if (drawHitPoints)
            {
                Gizmos.color = hitPointColor;
                foreach (var point in _hitPoints)
                {
                    Gizmos.DrawWireSphere(point, hitPointSize);
                }
            }
        }

        private void DrawPanelBounds(RTTCanvasBase panel)
        {
            var quad = panel.GetDisplayQuad();
            if (quad == null) return;

            Gizmos.color = panel.IsVisible ? quadBoundsColor : new Color(quadBoundsColor.r, quadBoundsColor.g, quadBoundsColor.b, 0.3f);
            Gizmos.matrix = quad.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(1, 1, 0.01f));
            Gizmos.matrix = Matrix4x4.identity;

            // Draw panel name
            #if UNITY_EDITOR
            var worldSize = panel.GetWorldSize();
            Vector3 labelPos = quad.transform.position + quad.transform.up * (worldSize.y / 2f + 0.05f);
            UnityEditor.Handles.Label(labelPos, panel.name);
            #endif
        }

        /// <summary>
        /// Add a ray to be drawn in the next gizmo pass.
        /// Call this from RTTRaycastManager.
        /// </summary>
        public static void AddRay(Vector3 origin, Vector3 direction, float distance, bool hit)
        {
            if (_raysToDraw.Count > 10)
                _raysToDraw.RemoveAt(0);

            _raysToDraw.Add(new RayDrawData
            {
                origin = origin,
                direction = direction,
                distance = distance,
                hit = hit
            });
        }

        /// <summary>
        /// Add a hit point to be drawn.
        /// </summary>
        public static void AddHitPoint(Vector3 point)
        {
            if (_hitPoints.Count > 20)
                _hitPoints.RemoveAt(0);

            _hitPoints.Add(point);
        }

        /// <summary>
        /// Clear all cached draw data.
        /// </summary>
        public static void Clear()
        {
            _raysToDraw.Clear();
            _hitPoints.Clear();
        }
    }

}
