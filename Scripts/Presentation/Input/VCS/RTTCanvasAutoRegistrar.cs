using System;
using System.Collections.Generic;
using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Presentation.Input.VCS
{
    /// <summary>
    /// Bridges <c>RTTCanvasBase</c> static lifecycle broadcasts (Phase 5 hooks) into the
    /// VirtualCursorSpace arbiter. Auto-bootstraps before any scene loads so
    /// canvases created at runtime get auto-registered without per-component wiring.
    ///
    /// Default registration:
    ///   - Center  = canvas.transform.position projected onto XY plane (meters).
    ///   - Size    = canvas.GetWorldSize() (meters).
    ///   - Edges   = EdgePolicy.All (panels authorize all 4 directions by default;
    ///               callers can override by adding a <see cref="VirtualSurfaceOverride"/> component).
    ///   - Priority= <see cref="SurfacePriority.Default"/>.
    ///
    /// Position is acceptable as-is if canvas == Main Menu (whose world origin is (0,0)).
    /// Other panels inherit their placed world position via sphere/follow math that
    /// already runs in RTTAppManager and friends.
    /// </summary>
    [DefaultExecutionOrder(-8000)]
    public sealed class RTTCanvasAutoRegistrar : MonoBehaviour
    {
        public static RTTCanvasAutoRegistrar Instance { get; private set; }
        private readonly Dictionary<RTTCanvasBase, Guid> _idMap = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[RTTCanvasAutoRegistrar]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RTTCanvasAutoRegistrar>();
        }

        private void Awake()
        {
            RTTCanvasBase.OnAnyRTTSurfaceCreated    += OnSurfaceCreated;
            RTTCanvasBase.OnAnyRTTSurfaceDestroyed  += OnSurfaceDestroyed;
            RTTCanvasBase.OnAnyRTTVisibilityChanged += OnVisibilityChanged;
        }

        private void OnDestroy()
        {
            RTTCanvasBase.OnAnyRTTSurfaceCreated    -= OnSurfaceCreated;
            RTTCanvasBase.OnAnyRTTSurfaceDestroyed  -= OnSurfaceDestroyed;
            RTTCanvasBase.OnAnyRTTVisibilityChanged -= OnVisibilityChanged;
            _idMap.Clear();
            if (Instance == this) Instance = null;
        }

        private Transform GetReferenceTransform()
        {
            var refFrame = RTTMenuFrame.PrimaryInstance;
            if (refFrame != null) return refFrame.transform;

            var activeFrame = FindAnyObjectByType<RTTMenuFrame>();
            if (activeFrame != null) return activeFrame.transform;

            return null;
        }

        /// <summary>
        /// Project a 3D world position onto a reference transform's XY plane along the camera ray.
        /// This ensures panels shifted closer to the camera (e.g. RTTMobileKeyboard via cameraProximity)
        /// are projected back to their logical 2D coordinate positions in VCS space, preventing gaps/jumps.
        /// </summary>
        public static Vector2 ProjectToVirtualSpace(Vector3 worldPos, Transform refT)
        {
            if (refT == null) return new Vector2(worldPos.x, worldPos.y);

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 cameraPos = cam.transform.position;
                Vector3 rayDir = worldPos - cameraPos;
                float denom = Vector3.Dot(rayDir, refT.forward);
                if (Mathf.Abs(denom) > 0.0001f)
                {
                    float t = Vector3.Dot(refT.position - cameraPos, refT.forward) / denom;
                    Vector3 projectedPoint = cameraPos + t * rayDir;
                    Vector3 localPos = refT.InverseTransformPoint(projectedPoint);
                    return new Vector2(localPos.x, localPos.y);
                }
            }

            // Fallback
            Vector3 relativePos = worldPos - refT.position;
            Vector3 localPos2 = refT.InverseTransformDirection(relativePos);
            return new Vector2(localPos2.x, localPos2.y);
        }

        public static Vector2 GetProjectedSize(Transform targetT, Vector2 physicalSize, Transform refT)
        {
            if (refT == null || refT == targetT)
            {
                return physicalSize;
            }

            Vector3 center = targetT.position;
            Vector2 projCenter = ProjectToVirtualSpace(center, refT);
            Vector2 projRight  = ProjectToVirtualSpace(center + targetT.right * (physicalSize.x / 2f), refT);
            Vector2 projTop    = ProjectToVirtualSpace(center + targetT.up * (physicalSize.y / 2f), refT);

            float projW = Mathf.Abs(projRight.x - projCenter.x) * 2f;
            float projH = Mathf.Abs(projTop.y - projCenter.y) * 2f;

            return new Vector2(
                Mathf.Max(projW, physicalSize.x),
                Mathf.Max(projH, physicalSize.y)
            );
        }

        public Vector2 GetVirtualSize(RTTCanvasBase canvas, Transform refT = null)
        {
            if (canvas == null) return Vector2.zero;
            if (refT == null) refT = GetReferenceTransform();
            return GetProjectedSize(canvas.transform, canvas.GetWorldSize(), refT);
        }


        public Vector2 GetVirtualCenter(RTTCanvasBase canvas, Transform refT = null)
        {
            if (canvas == null) return Vector2.zero;

            if (refT == null) refT = GetReferenceTransform();

            if (refT != null && refT != canvas.transform)
            {
                return ProjectToVirtualSpace(canvas.transform.position, refT);
            }

            if (refT == canvas.transform)
            {
                return Vector2.zero;
            }

            var pos = canvas.transform.position;
            return new Vector2(pos.x, pos.y);
        }

        private void OnSurfaceCreated(RTTCanvasBase canvas)
        {
            if (canvas == null || _idMap.ContainsKey(canvas)) return;

            // Re-register after RT recycle: if we already know this canvas, just update.
            if (!_idMap.TryGetValue(canvas, out var surfaceId))
            {
                surfaceId = Guid.NewGuid();
                _idMap[canvas] = surfaceId;
            }

            var size = GetVirtualSize(canvas);
            var (edges, priority) = ResolveOverride(canvas) ?? (EdgePolicy.All, SurfacePriority.Default);

            var surface = new VirtualSurface(
                surfaceId,
                GetVirtualCenter(canvas),
                size,
                edges,
                priority,
                canvas);

            VirtualCursorSpace.GetOrCreate().RegisterSurface(surface);
        }

        private void OnSurfaceDestroyed(RTTCanvasBase canvas)
        {
            if (canvas == null) return;
            if (_idMap.TryGetValue(canvas, out var id))
            {
                VirtualCursorSpace.Instance?.UnregisterSurface(id);
                _idMap.Remove(canvas);
            }
        }

        private void OnVisibilityChanged(RTTCanvasBase canvas, bool isVisible)
        {
            if (canvas == null) return;
            if (_idMap.TryGetValue(canvas, out var id))
            {
                VirtualCursorSpace.Instance?.NotifySurfaceVisibilityChanged(id, isVisible);
            }
        }

        private void LateUpdate()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null) return;

            Transform refT = GetReferenceTransform();

            foreach (var kvp in _idMap)
            {
                var canvas = kvp.Key;
                if (canvas == null) continue;

                var id = kvp.Value;
                Vector2 currentCenter = GetVirtualCenter(canvas, refT);
                Vector2 currentSize = GetVirtualSize(canvas, refT);

                if (vcs.Surfaces.TryGet(id, out var surface))
                {
                    if (surface.Center == currentCenter && surface.Size == currentSize)
                    {
                        continue;
                    }

                    var updated = new VirtualSurface(
                        id,
                        currentCenter,
                        currentSize,
                        surface.Edges,
                        surface.Priority,
                        canvas);
                    vcs.RegisterSurface(updated);
                    vcs.NotifySurfaceVisibilityChanged(id, surface.IsVisible);
                }
            }
        }

        /// <summary>Optional override component: per-canvas edge policy + priority + virtual center.</summary>
        private (EdgePolicy edges, int priority)? ResolveOverride(RTTCanvasBase canvas)
        {
            var o = canvas.GetComponent<VirtualSurfaceOverride>();
            if (o != null) return (o.Edges, o.Priority);

            if (canvas is RTTMobileKeyboard)
            {
                return (EdgePolicy.Up, SurfacePriority.Modal);
            }

            return null;
        }

        /// <summary>Public lookup: VCS surface id for a given canvas, or null if not yet registered.</summary>
        public Guid? TryGetSurfaceId(RTTCanvasBase canvas)
        {
            if (canvas == null) return null;
            return _idMap.TryGetValue(canvas, out var id) ? (Guid?)id : null;
        }
    }
}
