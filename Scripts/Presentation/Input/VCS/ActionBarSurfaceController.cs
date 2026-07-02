using System;
using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Presentation.Input.VCS
{
    /// <summary>
    /// Registers a non-RTTCanvasBase action bar (RTTFileActionBar, RTTMediaActionBar, …)
    /// as a <see cref="VirtualSurface"/> in the <see cref="VirtualCursorSpace"/> so that the
    /// virtual cursor can traverse from its parent Right Side Panel downward into it, and
    /// back up again — mirroring how the Main Surface connects to the Pagination bar.
    ///
    /// Usage: call <see cref="ActionBarSurfaceController.Attach"/> right after creating the
    /// action bar, passing the action bar's Transform, its physical size, and the parent
    /// RTTCanvasBase (the Right Side Panel).  Then call
    /// <see cref="NotifyVisible"/> whenever the bar shows / hides.
    /// </summary>
    public sealed class ActionBarSurfaceController : MonoBehaviour
    {
        // ── surface identity ──────────────────────────────────────────────────────────
        private Guid    _surfaceId;
        private bool    _registered;

        // ── configuration ─────────────────────────────────────────────────────────────
        private Vector2        _physicalSize;   // metres  (width × height)
        private RTTCanvasBase  _parentCanvas;   // Right Side Panel that "owns" this bar
        private bool           _isVisible;

        /// <summary>Physical size of the action bar in metres (width × height).</summary>
        public Vector2 PhysicalSize => _physicalSize;

        // ─────────────────────────────────────────────────────────────────────────────
        #region Static Factory
        /// <summary>
        /// Attaches an <see cref="ActionBarSurfaceController"/> to <paramref name="actionBarRoot"/>
        /// and immediately registers a VCS surface for it.
        /// </summary>
        /// <param name="actionBarRoot">The action bar's root GameObject.</param>
        /// <param name="physicalWidth">Physical width in metres (= panel width).</param>
        /// <param name="physicalHeight">Physical height in metres (= frame height, ~0.12 m).</param>
        /// <param name="parentRightPanel">The RTTCanvasBase of the Right Side Panel above the bar.</param>
        public static ActionBarSurfaceController Attach(
            GameObject    actionBarRoot,
            float         physicalWidth,
            float         physicalHeight,
            RTTCanvasBase parentRightPanel)
        {
            if (actionBarRoot == null) return null;

            var ctrl = actionBarRoot.AddComponent<ActionBarSurfaceController>();
            ctrl._physicalSize  = new Vector2(physicalWidth, physicalHeight);
            ctrl._parentCanvas  = parentRightPanel;
            ctrl._surfaceId     = Guid.NewGuid();
            ctrl._isVisible     = false;           // hidden initially

            ctrl.RegisterOrUpdateSurface();
            return ctrl;
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────────────
        #region Public API
        /// <summary>
        /// Call this whenever the action bar becomes visible or hidden so VCS knows
        /// whether the cursor is allowed to enter it.
        /// </summary>
        public void NotifyVisible(bool visible)
        {
            if (_isVisible == visible) return;
            _isVisible = visible;

            var vcs = VirtualCursorSpace.Instance;
            if (vcs != null) vcs.NotifySurfaceVisibilityChanged(_surfaceId, visible);
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────────────
        #region Unity Lifecycle
        private void LateUpdate()
        {
            RegisterOrUpdateSurface();
        }

        private void OnDestroy()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs != null && _registered)
            {
                vcs.UnregisterSurface(_surfaceId);
                _registered = false;
            }
        }
        #endregion

        // ─────────────────────────────────────────────────────────────────────────────
        #region Private Helpers
        private void RegisterOrUpdateSurface()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null) return;

            // Project world position and size into VCS space (same logic as RTTCanvasAutoRegistrar)
            Vector2 center = ComputeVCSCenter();
            Vector2 size = ComputeVCSSize();

            if (_registered && vcs.Surfaces.TryGet(_surfaceId, out var existing))
            {
                if (existing.Center == center && existing.Size == size) return;

                // Position / size changed — re-register in place
                var updated = new VirtualSurface(
                    _surfaceId,
                    center,
                    size,
                    EdgePolicy.Up,
                    SurfacePriority.Default,
                    this);                    // RuntimeRef = this, so cursor renderer can use our Transform
                vcs.RegisterSurface(updated);
                vcs.NotifySurfaceVisibilityChanged(_surfaceId, _isVisible);
            }
            else if (!_registered)
            {
                var surface = new VirtualSurface(
                    _surfaceId,
                    center,
                    size,
                    EdgePolicy.Up,
                    SurfacePriority.Default,
                    this);                    // RuntimeRef = this, so cursor renderer can use our Transform
                vcs.RegisterSurface(surface);
                vcs.NotifySurfaceVisibilityChanged(_surfaceId, _isVisible);
                _registered = true;
            }
        }

        private Vector2 ComputeVCSCenter()
        {
            Transform refT = GetReferenceTransform();
            return RTTCanvasAutoRegistrar.ProjectToVirtualSpace(transform.position, refT);
        }

        private Vector2 ComputeVCSSize()
        {
            Transform refT = GetReferenceTransform();
            return RTTCanvasAutoRegistrar.GetProjectedSize(transform, _physicalSize, refT);
        }

        private static Transform GetReferenceTransform()
        {
            var refFrame = RTTMenuFrame.PrimaryInstance;
            if (refFrame != null) return refFrame.transform;

            var activeFrame = FindAnyObjectByType<RTTMenuFrame>();
            if (activeFrame != null) return activeFrame.transform;

            return null;
        }
        #endregion
    }
}
