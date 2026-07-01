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
    }
}
