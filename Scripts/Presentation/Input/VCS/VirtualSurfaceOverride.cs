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
        [SerializeField] private bool useOrthogonalProjection = false;
        [SerializeField] private bool useContentInset = false;
        [SerializeField] private Vector4 contentInsetMeters = Vector4.zero; // x=left, y=right, z=top, w=bottom

        public EdgePolicy Edges   => edges;
        public int        Priority => priority;

        /// <summary>
        /// Opt-in: use a camera-independent orthogonal projection (dot product against
        /// refT.right/refT.up) instead of RTTCanvasAutoRegistrar's default camera-ray
        /// projection when computing this canvas's VCS center/size. The camera-ray version
        /// exists for panels that visually shift closer to the camera at runtime (e.g. the
        /// mobile keyboard's proximity behavior); for a panel that's rigidly fixed relative
        /// to the reference frame (just at a static depth offset, like Controls/Queue/
        /// Settings/Pagination in the video player), that camera-ray step introduces
        /// perspective magnification proportional to camera distance instead of helping —
        /// making the registered bounds visibly larger than the panel's true size and
        /// causing that size to drift as the camera moves. Set this true for any such panel.
        /// </summary>
        public bool UseOrthogonalProjection => useOrthogonalProjection;

        /// <summary>
        /// If set (via <see cref="ConfigureContentInset"/>), VCS registration shrinks the raw
        /// canvas.GetWorldSize() by these per-edge amounts (metres) — and shifts the center
        /// accordingly, since real UI margins are rarely symmetric (e.g. Controls has 15px
        /// left/right, 100px top, 0px bottom — a plain uniform shrink-around-center would
        /// still misplace every edge except by coincidence). x=left, y=right, z=top, w=bottom.
        /// Use this for any panel whose display quad is intentionally larger than its true
        /// visual/interactive content (background glow, decorative spacer rows, etc.).
        /// </summary>
        public Vector4? ContentInset => useContentInset ? contentInsetMeters : (Vector4?)null;

        /// <summary>Call before ForceInitialize(), alongside Configure(). left/right/top/bottom
        /// are in metres, measured inward from the canvas's raw GetWorldSize() edges.</summary>
        public void ConfigureContentInset(float left, float right, float top, float bottom)
        {
            useContentInset = true;
            contentInsetMeters = new Vector4(left, right, top, bottom);
        }

        /// <summary>
        /// Programmatic setup for code that builds panels at runtime (e.g.
        /// MediaPlayerUIBuilder) rather than authoring EdgePolicy/priority in the Inspector.
        /// Must be called BEFORE the canvas's RTTCanvasBase.Initialize() runs (i.e. before
        /// ForceInitialize()/OnEnable's first pass) — RTTCanvasAutoRegistrar.ResolveOverride()
        /// reads this component's values only once, at OnAnyRTTSurfaceCreated time.
        /// </summary>
        public void Configure(EdgePolicy newEdges, int newPriority, bool orthogonalProjection = false)
        {
            edges = newEdges;
            priority = newPriority;
            useOrthogonalProjection = orthogonalProjection;
        }
    }
}
