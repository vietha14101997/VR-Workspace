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

        /// <summary>
        /// Camera-independent counterpart to <see cref="ProjectToVirtualSpace"/>: a plain
        /// orthogonal projection onto refT's plane (dot product against refT.right/refT.up),
        /// with no camera-ray/perspective step at all.
        ///
        /// ProjectToVirtualSpace exists specifically for panels that visually shift closer to
        /// the camera at runtime (e.g. RTTMobileKeyboard's proximity behavior) — the camera
        /// ray keeps their logical VCS position/size stable as that shift happens. Panels that
        /// never do that (Controls/Queue/Settings/Pagination/Hub: rigidly fixed relative to
        /// refT, just at a static depth offset) get no benefit from that camera-ray step —
        /// instead it introduces perspective magnification proportional to camera distance,
        /// which is exactly what caused Hub/Controls/Queue's registered bounds to be larger
        /// than their true visual size and to disagree slightly with each frame from the
        /// camera's current position. Use this for anything at a fixed depth offset.
        /// </summary>
        public static Vector2 ProjectToVirtualSpaceOrthogonal(Vector3 worldPos, Transform refT)
        {
            if (refT == null) return new Vector2(worldPos.x, worldPos.y);
            Vector3 relative = worldPos - refT.position;
            return new Vector2(Vector3.Dot(relative, refT.right), Vector3.Dot(relative, refT.up));
        }

        /// <summary>Orthogonal counterpart to <see cref="GetProjectedSize"/> — see remarks there.</summary>
        public static Vector2 GetProjectedSizeOrthogonal(Transform targetT, Vector2 physicalSize, Transform refT)
        {
            // Unlike the camera-ray version, orthogonal panels don't need size re-derived via
            // projection at all: they're rigid (not camera-shifting), so their true
            // GetWorldSize() IS their stable VCS size. Re-deriving it by projecting
            // targetT.right/up onto refT.right/up (as a previous version of this method did)
            // shrinks the apparent size by cos(rotation angle) whenever targetT is rotated
            // relative to refT — which many side panels are, slightly, to angle toward the
            // user — making them register narrower than their true visible width. Only the
            // CENTER needs projecting (see GetVirtualCenter/ProjectToVirtualSpaceOrthogonal),
            // to place the panel correctly in VCS's flat 2D coordinate space; the size itself
            // should just pass through unchanged.
            return physicalSize;
        }

        public Vector2 GetVirtualSize(RTTCanvasBase canvas, Transform refT = null)
        {
            if (canvas == null) return Vector2.zero;
            if (refT == null) refT = GetReferenceTransform();

            Vector2 physicalSize = canvas.GetWorldSize();

            // Account for relative scale between canvas and refT
            if (refT != null && refT != canvas.transform)
            {
                Vector3 canvasLossy = canvas.transform.lossyScale;
                Vector3 refLossy = refT.lossyScale;
                physicalSize.x *= (canvasLossy.x / Mathf.Max(1e-4f, refLossy.x));
                physicalSize.y *= (canvasLossy.y / Mathf.Max(1e-4f, refLossy.y));
            }

            Vector2 size = UsesOrthogonalProjection(canvas)
                ? GetProjectedSizeOrthogonal(canvas.transform, physicalSize, refT)
                : GetProjectedSize(canvas.transform, physicalSize, refT);

            var inset = GetContentInset(canvas);
            // [HUB_DBG] Temporary trace: confirm whether GetContentInset actually returns a
            // value at this call site, and what raw/pre-inset size looks like, to isolate
            // why Queue's registered size wasn't shrinking despite ConfigureContentInset.
            if (canvas.name.Contains("Side") || canvas.name.Contains("Queue"))
            {
                var ov = canvas.GetComponent<VirtualSurfaceOverride>();
                Debug.Log($"[HUB_DBG] GetVirtualSize({canvas.name}): hasOverrideComponent={(ov != null)} overrideInstanceId={(ov != null ? ov.GetEntityId().ToString() : "N/A")} inset={(inset.HasValue ? inset.Value.ToString() : "NULL")} physicalSize(raw)={physicalSize} size(pre-inset)={size}");
            }
            if (inset.HasValue)
            {
                var i = inset.Value; // x=left, y=right, z=top, w=bottom
                float scaleX = 1f;
                float scaleY = 1f;
                if (refT != null && refT != canvas.transform)
                {
                    scaleX = canvas.transform.lossyScale.x / Mathf.Max(1e-4f, refT.lossyScale.x);
                    scaleY = canvas.transform.lossyScale.y / Mathf.Max(1e-4f, refT.lossyScale.y);
                }
                size.x -= (i.x + i.y) * scaleX;
                size.y -= (i.z + i.w) * scaleY;
            }
            return size;
        }

        public Vector2 GetVirtualCenter(RTTCanvasBase canvas, Transform refT = null)
        {
            if (canvas == null) return Vector2.zero;

            if (refT == null) refT = GetReferenceTransform();

            Vector2 center;
            if (refT != null && refT != canvas.transform)
            {
                center = UsesOrthogonalProjection(canvas)
                    ? ProjectToVirtualSpaceOrthogonal(canvas.transform.position, refT)
                    : ProjectToVirtualSpace(canvas.transform.position, refT);
            }
            else if (refT == canvas.transform)
            {
                center = Vector2.zero;
            }
            else
            {
                var pos = canvas.transform.position;
                center = new Vector2(pos.x, pos.y);
            }

            // Real UI margins are rarely symmetric (e.g. Controls: 15px left/right, 100px top,
            // 0px bottom) — shrinking size alone (assuming an unchanged center) would still
            // misplace every edge except by coincidence. Shift center by the left/right and
            // top/bottom imbalance so each edge lands exactly where GetVirtualSize's shrunk
            // size says it should, not just "smaller, but still centered on the raw quad."
            var inset = GetContentInset(canvas);
            if (inset.HasValue)
            {
                var i = inset.Value; // x=left, y=right, z=top, w=bottom
                float scaleX = 1f;
                float scaleY = 1f;
                if (refT != null && refT != canvas.transform)
                {
                    scaleX = canvas.transform.lossyScale.x / Mathf.Max(1e-4f, refT.lossyScale.x);
                    scaleY = canvas.transform.lossyScale.y / Mathf.Max(1e-4f, refT.lossyScale.y);
                }
                center.x += (i.x - i.y) * 0.5f * scaleX;
                center.y += (i.w - i.z) * 0.5f * scaleY;
            }
            return center;
        }

        /// <summary>Whether this canvas opted into camera-independent projection via its
        /// VirtualSurfaceOverride (see ProjectToVirtualSpaceOrthogonal remarks for why).</summary>
        private static bool UsesOrthogonalProjection(RTTCanvasBase canvas)
        {
            var o = canvas.GetComponent<VirtualSurfaceOverride>();
            return o != null && o.UseOrthogonalProjection;
        }

        private static Vector4? GetContentInset(RTTCanvasBase canvas)
        {
            var o = canvas.GetComponent<VirtualSurfaceOverride>();
            return o != null ? o.ContentInset : null;
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
