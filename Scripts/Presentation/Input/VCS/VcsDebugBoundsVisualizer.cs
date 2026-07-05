using System;
using System.Collections.Generic;
using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.Presentation.Input.VCS
{
    /// <summary>
    /// Draws the ACTUAL registered VCS bounds (Center/Size, as currently held in the
    /// VirtualCursorSpace registry — not a re-derivation) for a fixed set of surfaces, as
    /// colored wireframe rectangles rendered in-world (visible in the VR headset, not just
    /// the Unity Editor scene view like Gizmos would be).
    ///
    /// Purpose: every previous round of fixing Controls/Hub/Queue size mismatches was done by
    /// tracing layout code (expandedWidth, VerticalLayoutGroup padding, etc.) and computing
    /// insets by hand — a process prone to missing an intermediate margin. This visualizer
    /// answers "where does VCS actually think this surface's edges are, right now" directly,
    /// so remaining mismatches can be measured by eye against the rendered UI instead of
    /// re-deriving numbers from source.
    ///
    /// Toggle via <see cref="Enabled"/> (static, defaults true while iterating on this).
    /// </summary>
    public sealed class VcsDebugBoundsVisualizer : MonoBehaviour
    {
        /// <summary>Master on/off switch. Set false (or delete this component's GameObject)
        /// once bounds are confirmed to match visuals — this is a diagnostic tool, not meant
        /// to ship enabled.</summary>
        public static bool Enabled = false; // disabled: renders a huge/mispositioned line in-game (Game view/headset) — needs its own debugging separate from the Hub/Controls sizing work. Cursor-at-edge screenshots are the reliable comparison method for now.

        private const float LineWidth = 0.004f;

        private class Entry
        {
            public string Label;
            public Guid SurfaceId;
            public LineRenderer Renderer;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private Transform _refT;
        private float _depth;

        /// <summary>
        /// Create (or update, if already created) a visualizer tracking the given surfaces.
        /// depthSource is any RTTCanvasBase already known to sit at the correct display depth
        /// (e.g. Controls) — every wireframe is drawn at that same depth along refT.forward,
        /// matching MediaPlayerHubSurfaceController's own depth handling.
        /// </summary>
        public static VcsDebugBoundsVisualizer Attach(
            GameObject host, Transform refT, RTTCanvasBase depthSource,
            params (string label, Guid surfaceId, Color color)[] tracked)
        {
            if (!Enabled || host == null || refT == null) return null;

            var v = host.AddComponent<VcsDebugBoundsVisualizer>();
            v._refT = refT;

            var depthQuad = depthSource != null ? depthSource.GetQuadCollider() : null;
            v._depth = depthQuad != null
                ? Vector3.Dot(depthQuad.transform.position - refT.position, refT.forward)
                : 0f;

            foreach (var (label, id, color) in tracked)
            {
                var lineGO = new GameObject($"[VcsDebugBounds] {label}");
                lineGO.transform.SetParent(host.transform, false);
                var lr = lineGO.AddComponent<LineRenderer>();
                lr.positionCount = 5; // closed rectangle loop
                lr.loop = false;
                lr.useWorldSpace = true;
                lr.widthMultiplier = LineWidth;
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = color;
                lr.endColor = color;
                lr.numCapVertices = 2;

                v._entries.Add(new Entry { Label = label, SurfaceId = id, Renderer = lr });
            }

            return v;
        }

        /// <summary>Register one more surface after construction (e.g. Pagination, whose
        /// surfaceId isn't known until its own RTTCanvasBase registers).</summary>
        public void Track(string label, Guid surfaceId, Color color)
        {
            var lineGO = new GameObject($"[VcsDebugBounds] {label}");
            lineGO.transform.SetParent(transform, false);
            var lr = lineGO.AddComponent<LineRenderer>();
            lr.positionCount = 5;
            lr.loop = false;
            lr.useWorldSpace = true;
            lr.widthMultiplier = LineWidth;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = color;
            lr.endColor = color;
            lr.numCapVertices = 2;

            _entries.Add(new Entry { Label = label, SurfaceId = surfaceId, Renderer = lr });
        }

        private void LateUpdate()
        {
            if (_refT == null) return;

            var vcs = VirtualCursorSpace.Instance;
            bool globalOn = Enabled && vcs != null;

            foreach (var e in _entries)
            {
                if (!globalOn || !vcs.Surfaces.TryGet(e.SurfaceId, out var surface) || !surface.IsVisible)
                {
                    e.Renderer.enabled = false;
                    continue;
                }

                e.Renderer.enabled = true;

                float halfW = surface.Size.x * 0.5f;
                float halfH = surface.Size.y * 0.5f;
                Vector2 c = surface.Center;

                Vector3 WorldAt(float x, float y) =>
                    _refT.position + _refT.right * x + _refT.up * y + _refT.forward * _depth;

                Vector3 bl = WorldAt(c.x - halfW, c.y - halfH);
                Vector3 br = WorldAt(c.x + halfW, c.y - halfH);
                Vector3 tr = WorldAt(c.x + halfW, c.y + halfH);
                Vector3 tl = WorldAt(c.x - halfW, c.y + halfH);

                e.Renderer.SetPosition(0, bl);
                e.Renderer.SetPosition(1, br);
                e.Renderer.SetPosition(2, tr);
                e.Renderer.SetPosition(3, tl);
                e.Renderer.SetPosition(4, bl);
            }
        }
    }
}
