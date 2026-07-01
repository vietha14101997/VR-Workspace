using System;
using System.Collections.Generic;
using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;

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

        private void OnSurfaceCreated(RTTCanvasBase canvas)
        {
            if (canvas == null || _idMap.ContainsKey(canvas)) return;

            // Re-register after RT recycle: if we already know this canvas, just update.
            if (!_idMap.TryGetValue(canvas, out var surfaceId))
            {
                surfaceId = Guid.NewGuid();
                _idMap[canvas] = surfaceId;
            }

            var pos = canvas.transform.position;
            var size = canvas.GetWorldSize();
            var (edges, priority) = ResolveOverride(canvas) ?? (EdgePolicy.All, SurfacePriority.Default);

            var surface = new VirtualSurface(
                surfaceId,
                new Vector2(pos.x, pos.y),
                new Vector2(size.x, size.y),
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

        /// <summary>Optional override component: per-canvas edge policy + priority + virtual center.</summary>
        private (EdgePolicy edges, int priority)? ResolveOverride(RTTCanvasBase canvas)
        {
            var o = canvas.GetComponent<VirtualSurfaceOverride>();
            if (o == null) return null;
            return (o.Edges, o.Priority);
        }

        /// <summary>Public lookup: VCS surface id for a given canvas, or null if not yet registered.</summary>
        public Guid? TryGetSurfaceId(RTTCanvasBase canvas)
        {
            if (canvas == null) return null;
            return _idMap.TryGetValue(canvas, out var id) ? (Guid?)id : null;
        }
    }
}
