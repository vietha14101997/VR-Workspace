using UnityEngine;
using VRWorkspace.Domain.Input;

namespace VRWorkspace.Presentation.Input.VCS
{
    /// <summary>
    /// Per-canvas override: lets a scene designer specify EdgePolicy + priority
    /// for an RTT surface without changing RTTCanvasBase itself.
    ///
    /// If absent, the auto-registrar defaults to <see cref="EdgePolicy.All"/> and
    /// <see cref="SurfacePriority.Default"/>.
    /// </summary>
    public sealed class VirtualSurfaceOverride : MonoBehaviour
    {
        [SerializeField] private EdgePolicy edges = EdgePolicy.All;
        [SerializeField] private int priority = SurfacePriority.Default;

        public EdgePolicy Edges   => edges;
        public int        Priority => priority;

        /// <summary>
        /// Programmatic setup for code that builds panels at runtime (e.g.
        /// MediaPlayerUIBuilder) rather than authoring EdgePolicy/priority in the Inspector.
        /// Must be called BEFORE the canvas's RTTCanvasBase.Initialize() runs (i.e. before
        /// ForceInitialize()/OnEnable's first pass) — RTTCanvasAutoRegistrar.ResolveOverride()
        /// reads this component's values only once, at OnAnyRTTSurfaceCreated time.
        /// </summary>
        public void Configure(EdgePolicy newEdges, int newPriority)
        {
            edges = newEdges;
            priority = newPriority;
        }
    }
}
