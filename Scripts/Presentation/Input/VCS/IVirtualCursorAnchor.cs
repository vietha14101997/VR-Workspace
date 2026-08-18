using UnityEngine;

namespace VRWorkspace.Presentation.Input.VCS
{
    /// <summary>
    /// Contract for a <see cref="VRWorkspace.Domain.Input.VirtualSurface.RuntimeRef"/> that
    /// isn't backed by an RTTCanvasBase (visible RTT panel) or an ActionBarSurfaceController,
    /// but still needs to host and correctly position/scale the world-space cursor —
    /// e.g. an invisible "hub" surface with no display quad of its own.
    ///
    /// WorldSpaceCursorRenderer checks for this interface as a third fallback (after
    /// RTTCanvasBase and ActionBarSurfaceController) when resolving where to parent/scale
    /// the cursor for the active surface. Implementers just need *some* Transform (used for
    /// position/rotation/scale-compensation) and the physical size (metres) used to map the
    /// cursor's UV to a world hotspot on that Transform — the same two inputs RTTCanvasBase
    /// and ActionBarSurfaceController already provide via their own properties.
    /// </summary>
    public interface IVirtualCursorAnchor
    {
        /// <summary>Transform the cursor parents to, and whose rotation/lossyScale it matches
        /// (lossyScale is divided out so the cursor's rendered world size stays constant
        /// regardless of this transform's scale — same compensation already applied for
        /// RTTCanvasBase/ActionBarSurfaceController surfaces).</summary>
        Transform AnchorTransform { get; }

        /// <summary>Physical size (metres, width x height) used to map cursor UV [0,1] to a
        /// world-space hotspot on <see cref="AnchorTransform"/>.</summary>
        Vector2 PhysicalSize { get; }
    }
}
