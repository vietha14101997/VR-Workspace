using System;
using UnityEngine;

namespace VRWorkspace.Domain.Input
{
    /// <summary>
    /// Immutable descriptor for an RTT panel participating in the VCS virtual coordinate system.
    /// Unit: meters (Decision 7) — matches RTTCanvasBase.worldWidth/worldHeight 1:1.
    ///
    /// Identity is established by <see cref="SurfaceId"/>; <see cref="Center"/> and
    /// <see cref="Size"/> are absolute in the virtual plane (Main Menu = origin 0,0).
    /// Visibility is mutable via <see cref="IsVisible"/>; the arbiter decides this on
    /// OnVisibilityChanged callbacks from the RTT layer.
    /// </summary>
    [System.Serializable]
    public sealed class VirtualSurface
    {
        /// <summary>Stable identity. Required, never null after construction.</summary>
        public Guid SurfaceId { get; }

        /// <summary>Absolute center in virtual space (meters).</summary>
        public Vector2 Center { get; }

        /// <summary>World-space dimensions (meters).</summary>
        public Vector2 Size { get; }

        /// <summary>Which edges allow cursor traversal.</summary>
        public EdgePolicy Edges { get; }

        /// <summary>Higher wins. Used for cursor snap-to-highest + overlap resolution.</summary>
        public int Priority { get; }

        /// <summary>Whether this surface can currently receive cursor/click input.</summary>
        public bool IsVisible { get; internal set; }

        /// <summary>Optional reference to underlying Unity object (RTTCanvasBase / popup).</summary>
        public object RuntimeRef { get; }

        public VirtualSurface(
            Guid surfaceId,
            Vector2 center,
            Vector2 size,
            EdgePolicy edges,
            int priority = SurfacePriority.Default,
            object runtimeRef = null)
        {
            if (surfaceId == Guid.Empty)
                throw new ArgumentException("SurfaceId must be non-empty", nameof(surfaceId));
            if (size.x <= 0f || size.y <= 0f)
                throw new ArgumentException("Size components must be positive", nameof(size));

            SurfaceId  = surfaceId;
            Center     = center;
            Size       = size;
            Edges      = edges;
            Priority   = priority;
            RuntimeRef = runtimeRef;
            IsVisible  = true;
        }

        /// <summary>Bounds in meters: center ± size/2.</summary>
        public Rect Bounds => new Rect(
            Center.x - Size.x * 0.5f,
            Center.y - Size.y * 0.5f,
            Size.x,
            Size.y);

        /// <summary>Test whether an EdgeDirection is allowed by this surface's EdgePolicy.</summary>
        public bool Allows(EdgeDirection dir) => (Edges & DirToFlag(dir)) != 0;

        private static EdgePolicy DirToFlag(EdgeDirection d) => d switch
        {
            EdgeDirection.Left  => EdgePolicy.Left,
            EdgeDirection.Right => EdgePolicy.Right,
            EdgeDirection.Up    => EdgePolicy.Up,
            EdgeDirection.Down  => EdgePolicy.Down,
            _ => EdgePolicy.None
        };
    }
}
