using System;
using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Presentation.Input.VCS
{
    /// <summary>
    /// Invisible surface occupying the video display area, replacing the old single merged
    /// "controls + side + settings" bounding box with a proper hub-and-spoke VCS topology:
    ///
    ///   [Hub] --Down--> [Controls UI]
    ///   [Hub] --Right-> [Queue] or [Settings] (mutually exclusive, whichever is open)
    ///
    /// Geometry (side panel assumed to the right of Controls, sideControlsSide > 0 — the
    /// default/only layout this project currently builds; mirrored automatically if a
    /// negative sideControlsSide is ever used):
    ///   Hub.Left   = Controls.Left
    ///   Hub.Bottom = Controls.Top
    ///   Hub.Right  = ActiveSide.Left   (near edge, since side sits to Hub's right)
    ///   Hub.Top    = ActiveSide.Top
    ///
    /// "ActiveSide" is Settings while Settings is open, otherwise Queue (Queue's authored
    /// size/position is read even while its GameObject is inactive, purely as the geometric
    /// reference — the Right edge simply won't find a visible candidate to traverse into
    /// until one of the two panels is actually shown, at which point MoveCursor's normal
    /// visible-surface gating takes over).
    ///
    /// EdgePolicy: only Down + Right (or Down + Left, mirrored) are granted — every other
    /// edge (Up, and the side away from the panel) is intentionally absent, so
    /// TryTraverseEdge finds nothing there and MoveCursor just clamps, matching "outer edges
    /// of every surface block the cursor from escaping."
    ///
    /// Has no display quad of its own, so it implements <see cref="IVirtualCursorAnchor"/>
    /// (a lightweight invisible child Transform) rather than relying on RTTCanvasBase/
    /// ActionBarSurfaceController — WorldSpaceCursorRenderer's existing lossyScale
    /// compensation then keeps the cursor's rendered size identical to every other surface.
    /// </summary>
    public sealed class MediaPlayerHubSurfaceController : MonoBehaviour, IVirtualCursorAnchor
    {
        private Guid _surfaceId;
        private bool _registered;
        private bool _isVisible = true;

        private RTTCanvasBase _controlsCanvas;
        private RTTCanvasBase _queueCanvas;
        private RTTCanvasBase _settingsCanvas;
        private int _sideControlsSide;

        private Transform _anchor;
        private Vector2 _physicalSize;

        // Cached VCS coords (re-registered only on change)
        private Vector2 _lastCenter;
        private float _dbgLogTimer;
        private Vector2 _lastSize;

        public Transform AnchorTransform => _anchor;
        public Vector2 PhysicalSize => _physicalSize;

        /// <summary>
        /// Stable VCS surface id, assigned once in <see cref="Attach"/> and reused across
        /// every re-registration in <see cref="RegisterOrUpdateSurface"/>. Exposed so callers
        /// (e.g. VRMediaAppController) can explicitly hand the cursor off onto the hub
        /// system via <c>VirtualCursorSpace.SnapCursorTo</c> — nothing does this
        /// automatically, so without an explicit snap the cursor stays wherever it was
        /// before the player UI opened (e.g. the app's outer RTTMenuFrame) and never
        /// enters the hub/controls/queue/settings bounds at all.
        /// </summary>
        public Guid SurfaceId => _surfaceId;

        private Transform _refFrameTransform =>
            RTTMenuFrame.PrimaryInstance != null
                ? RTTMenuFrame.PrimaryInstance.transform
                : null;

        /// <summary>Settings takes precedence while open; otherwise Queue is the geometric
        /// reference regardless of its own active state (see class remarks).</summary>
        private RTTCanvasBase ActiveSideCanvas =>
            (_settingsCanvas != null && _settingsCanvas.gameObject.activeInHierarchy)
                ? _settingsCanvas
                : _queueCanvas;

        public static MediaPlayerHubSurfaceController Attach(
            GameObject playerControlsGroup,
            RTTCanvasBase controlsCanvas,
            RTTCanvasBase queueCanvas,
            RTTCanvasBase settingsCanvas,
            int sideControlsSide)
        {
            if (playerControlsGroup == null || controlsCanvas == null) return null;

            var ctrl = playerControlsGroup.AddComponent<MediaPlayerHubSurfaceController>();
            ctrl._controlsCanvas = controlsCanvas;
            ctrl._queueCanvas = queueCanvas;
            ctrl._settingsCanvas = settingsCanvas;
            ctrl._sideControlsSide = sideControlsSide;
            ctrl._surfaceId = Guid.NewGuid();
            ctrl._registered = false;

            var anchorGO = new GameObject("[HubCursorAnchor]");
            anchorGO.transform.SetParent(playerControlsGroup.transform, false);
            ctrl._anchor = anchorGO.transform;

            return ctrl;
        }

        /// <summary>Called by RTTMediaControlsPanel on Show/Hide, mirroring
        /// MediaPlayerSurfaceController — the hub should disappear together with controls.</summary>
        public void NotifyVisible(bool visible)
        {
            _isVisible = visible;
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null) return;
            vcs.NotifySurfaceVisibilityChanged(_surfaceId, visible);
        }

        private void LateUpdate()
        {
            RegisterOrUpdateSurface();
        }

        private void OnDestroy()
        {
            Unregister();
        }

        public void Unregister()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs != null && _registered)
            {
                vcs.UnregisterSurface(_surfaceId);
                _registered = false;
            }
        }

        private void RegisterOrUpdateSurface()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null) return;

            var sideCanvas = ActiveSideCanvas;
            if (_controlsCanvas == null || sideCanvas == null) return;

            var refT = _refFrameTransform;
            if (refT == null) return;

            if (!TryGetProjectedEdges(_controlsCanvas, refT, out var controlsLeft, out _, out var controlsTop, out _))
                return;
            if (!TryGetProjectedEdges(sideCanvas, refT, out var sideLeft, out var sideRight, out var sideTop, out _))
                return;

            bool rightSide = _sideControlsSide >= 0;

            float left, right, top, bottom;
            EdgePolicy edges;

            if (rightSide)
            {
                // Side panel sits to the right of Controls.
                left = controlsLeft;
                right = sideLeft;      // near edge of the side panel
                top = sideTop;
                bottom = controlsTop;
                edges = EdgePolicy.Down | EdgePolicy.Right;
            }
            else
            {
                // Mirrored: side panel sits to the left of Controls.
                if (!TryGetProjectedEdges(_controlsCanvas, refT, out _, out var controlsRight, out _, out _))
                    return;

                left = sideRight;      // near edge of the side panel
                right = controlsRight;
                top = sideTop;
                bottom = controlsTop;
                edges = EdgePolicy.Down | EdgePolicy.Left;
            }

            Vector2 newCenter = new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f);
            Vector2 newSize = new Vector2(Mathf.Abs(right - left), Mathf.Abs(top - bottom));

            if (newSize.x <= 0.001f || newSize.y <= 0.001f) return; // degenerate, skip this frame

            // [HUB_DBG] Throttled comparison: Hub's own computed edges vs the raw
            // GetVirtualCenter/GetVirtualSize this frame for Controls and the active side
            // panel. If Hub.left != controlsLeft or Hub.top != sideTop by more than a hair,
            // that's the exact numeric overshoot to chase (formula/depth/parallax mismatch)
            // rather than a re-registration/tie-break issue (already fixed).
            _dbgLogTimer -= Time.deltaTime;
            if (_dbgLogTimer <= 0f)
            {
                _dbgLogTimer = 2f;
                Debug.Log($"[HUB_DBG] left={left:F4} right={right:F4} top={top:F4} bottom={bottom:F4} | controlsLeft={controlsLeft:F4} controlsTop={controlsTop:F4} | sideLeft={sideLeft:F4} sideRight={sideRight:F4} sideTop={sideTop:F4} | hubCenter={newCenter} hubSize={newSize}");
            }

            // Keep the anchor transform positioned/oriented in world space so
            // WorldSpaceCursorRenderer's targetT.position / .right / .up / .forward /
            // .rotation / .lossyScale all behave exactly like a real panel's quad transform.
            UpdateAnchorTransform(refT, newCenter, newSize);

            if (_registered && _lastCenter == newCenter && _lastSize == newSize) return;

            _lastCenter = newCenter;
            _lastSize = newSize;
            _physicalSize = newSize;

            var surface = new VirtualSurface(
                _surfaceId,
                newCenter,
                newSize,
                edges,
                SurfacePriority.SidePanel, // > outer background's Default(100); wins ties in TryTraverseEdge
                this); // RuntimeRef = this (IVirtualCursorAnchor)

            vcs.RegisterSurface(surface);
            _registered = true;

            // VirtualSurface's constructor always defaults IsVisible=true — reapply our
            // cached desired visibility (same pattern as MediaPlayerSurfaceController).
            if (!_isVisible)
            {
                vcs.NotifySurfaceVisibilityChanged(_surfaceId, false);
            }
        }

        /// <summary>
        /// Positions/orients/scales the invisible anchor Transform to match the reference
        /// frame's plane, sized+centered to the hub's computed VCS rect, converted back to
        /// world space. Mirrors how RTTMenuFrame quads sit in the same plane as refT.
        /// </summary>
        private void UpdateAnchorTransform(Transform refT, Vector2 vcsCenter, Vector2 vcsSize)
        {
            if (_anchor == null) return;

            // Depth (along refT.forward) that the real UI quads actually sit at, and the
            // rotation Hub's anchor should actually use. refT is the outer Main Menu's own
            // transform — this app billboards each panel independently to face the camera, so
            // Main Menu's rotation is NOT the same as Controls/Queue/Settings' own (they're at
            // a different screen position, so their billboard angle differs). Using refT's
            // rotation for Hub's anchor made it diverge from its true neighbors' orientation —
            // worst exactly at the seams, which is what produced the reported arc when crossing
            // from Hub's top edge into Queue. Controls is the always-present reference here
            // (Queue/Settings are assumed to share its rotation — same rigid
            // PlayerControlsGroup composition).
            var controlsQuad = _controlsCanvas != null ? _controlsCanvas.GetQuadCollider() : null;

            _anchor.rotation = controlsQuad != null ? controlsQuad.transform.rotation : refT.rotation;
            _anchor.localScale = Vector3.one;

            float depth = 0f;
            if (controlsQuad != null)
            {
                depth = Vector3.Dot(controlsQuad.transform.position - refT.position, refT.forward);
            }

            Vector3 worldPos = refT.position
                + refT.right * vcsCenter.x
                + refT.up * vcsCenter.y
                + refT.forward * depth;
            _anchor.position = worldPos;
        }

        /// <summary>
        /// Returns an RTTCanvasBase's left/right/top/bottom edges (VCS meters), computed via
        /// the EXACT SAME formula RTTCanvasAutoRegistrar uses to register that canvas's own
        /// surface (canvas.transform.position + canvas.GetWorldSize(), projected through
        /// ProjectToVirtualSpace) — NOT a separately-derived quad-collider projection.
        ///
        /// This must match bit-for-bit: any discrepancy between how Hub computes "where
        /// Queue's left edge is" and where Queue's own VirtualSurface is actually registered
        /// leaves a gap/overlap that TryTraverseEdge's ray cast can miss, falling through to
        /// whatever larger surface (e.g. the outer Main Menu RTTMenuFrame, EdgePolicy.All)
        /// happens to also cover that point — exactly the "cursor escapes past Queue" bug.
        /// </summary>
        private static bool TryGetProjectedEdges(
            RTTCanvasBase canvas, Transform refT,
            out float left, out float right, out float top, out float bottom)
        {
            left = right = top = bottom = 0f;
            if (canvas == null) return false;

            var registrar = RTTCanvasAutoRegistrar.Instance;
            if (registrar == null) return false;

            Vector2 center = registrar.GetVirtualCenter(canvas, refT);
            Vector2 size = registrar.GetVirtualSize(canvas, refT);

            left = center.x - size.x * 0.5f;
            right = center.x + size.x * 0.5f;
            top = center.y + size.y * 0.5f;
            bottom = center.y - size.y * 0.5f;
            return true;
        }
    }
}
