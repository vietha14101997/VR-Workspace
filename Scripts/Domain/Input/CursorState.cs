using System;
using UnityEngine;

namespace VRWorkspace.Domain.Input
{
    /// <summary>
    /// Snapshot of the virtual cursor's current position + state.
    /// UV is in [0,1] x [0,1] relative to the active surface.
    /// </summary>
    public readonly struct CursorState : IEquatable<CursorState>
    {
        /// <summary>Surface the cursor is currently on. <c>null</c> = none / inactive.</summary>
        public Guid? SurfaceId { get; }

        /// <summary>UV position on the surface, [0,1] x [0,1].</summary>
        public Vector2 UV { get; }

        /// <summary>Current input mode driving the cursor.</summary>
        public InputMode Mode { get; }

        /// <summary>Whether cursor visual is rendered.</summary>
        public bool IsVisible { get; }

        public CursorState(Guid? surfaceId, Vector2 uv, InputMode mode, bool isVisible)
        {
            SurfaceId = surfaceId;
            UV = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            Mode = mode;
            IsVisible = isVisible;
        }

        /// <summary>Convenience: center UV on the given surface (Decision 1).</summary>
        public static CursorState AtCenter(Guid surfaceId, InputMode mode, bool isVisible)
            => new CursorState(surfaceId, new Vector2(0.5f, 0.5f), mode, isVisible);

        /// <summary>Inactive / no surface cursor (e.g. all surfaces hidden).</summary>
        public static CursorState Inactive(InputMode mode)
            => new CursorState(null, Vector2.zero, mode, false);

        public bool Equals(CursorState other) =>
            Nullable.Equals(SurfaceId, other.SurfaceId)
            && UV.Equals(other.UV)
            && Mode == other.Mode
            && IsVisible == other.IsVisible;

        public override bool Equals(object obj) => obj is CursorState s && Equals(s);
        public override int GetHashCode() =>
            HashCode.Combine(SurfaceId, UV, Mode, IsVisible);

        public override string ToString() =>
            $"Cursor(surface={SurfaceId?.ToString() ?? "null"}, uv={UV}, mode={Mode}, visible={IsVisible})";
    }
}
