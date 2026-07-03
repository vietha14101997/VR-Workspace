using System;
using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Presentation.Input.VCS
{
    /// <summary>
    /// Registers the mediaPlayer UI union (controls panel + side panels + settings panel)
    /// as a VCS surface so the mouse cursor is bounded inside it. EdgePolicy.None prevents
    /// cursor from escaping; IsVisible is toggled by RTTMediaControlsPanel.Show/Hide so
    /// cursor disappears when controls auto-hide.
    ///
    /// Pattern reference: ActionBarSurfaceController — re-registers every LateUpdate to
    /// track camera-follow + settings toggle (which changes layout).
    /// </summary>
    public sealed class MediaPlayerSurfaceController : MonoBehaviour
    {
        // ── Surface identity ─────────────────────────────────────────────
        private Guid _surfaceId;
        private bool _registered;

        // Desired visibility, set via NotifyVisible(). Must survive re-registration:
        // RegisterOrUpdateSurface() creates a *new* VirtualSurface whenever bounds change
        // (e.g. side/settings frame toggled active on Show/Hide), and VirtualSurface's
        // constructor always defaults IsVisible=true. Without caching it here, that
        // reconstruction silently wipes out a Hide()-driven IsVisible=false on the very
        // next frame, so the world-space cursor never actually disappears.
        private bool _isVisible = true;

        // ── Inner frames (whitelist, NOT Overlay/MenuButton) ────────────
        private Transform _controlsFrame;
        private Transform _sideFrame;
        private Transform _settingsFrame;

        // ── Cached VCS coords (re-registered only on change) ─────────────
        private Vector2 _physicalCenter;
        private Vector2 _physicalSize;

        private Transform _refFrameTransform =>
            RTTMenuFrame.PrimaryInstance != null
                ? RTTMenuFrame.PrimaryInstance.transform
                : null;

        // ── Static factory ─────────────────────────────────────────────────
        /// <summary>
        /// Attach a controller to the PlayerControlsGroup root.
        /// </summary>
        public static MediaPlayerSurfaceController Attach(
            GameObject playerControlsGroup,
            GameObject controlsFrame,
            GameObject sideFrame,
            GameObject settingsFrame)
        {
            if (playerControlsGroup == null) return null;
            var ctrl = playerControlsGroup.AddComponent<MediaPlayerSurfaceController>();
            ctrl._controlsFrame = controlsFrame != null ? controlsFrame.transform : null;
            ctrl._sideFrame = sideFrame != null ? sideFrame.transform : null;
            ctrl._settingsFrame = settingsFrame != null ? settingsFrame.transform : null;
            ctrl._surfaceId = Guid.NewGuid();
            ctrl._registered = false;
            return ctrl;
        }

        // ── Public API ─────────────────────────────────────────────────────
        /// <summary>Called by RTTMediaControlsPanel on Show/Hide.</summary>
        public void NotifyVisible(bool visible)
        {
            _isVisible = visible;
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null) return;
            vcs.NotifySurfaceVisibilityChanged(_surfaceId, visible);
        }

        // ── Unity lifecycle ───────────────────────────────────────────────
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

        // ── Surface registration ──────────────────────────────────────────
        private void RegisterOrUpdateSurface()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null) return;

            // Compute union bounds in world space.
            Bounds? worldBounds = ComputeWorldBounds();
            if (!worldBounds.HasValue) return;

            var refT = _refFrameTransform;
            if (refT == null) return;

            // Project to VCS space (meters relative to RTTMenuFrame).
            Vector2 newCenter = RTTCanvasAutoRegistrar.ProjectToVirtualSpace(
                worldBounds.Value.center, refT);

            // Compute projected size by projecting corners (handles rotation).
            Bounds b = worldBounds.Value;
            Vector3 ext = b.extents;
            Vector3 c1 = b.center + new Vector3(ext.x, ext.y, 0);
            Vector3 c2 = b.center + new Vector3(-ext.x, ext.y, 0);
            Vector3 c3 = b.center + new Vector3(ext.x, -ext.y, 0);
            Vector2 p1 = RTTCanvasAutoRegistrar.ProjectToVirtualSpace(c1, refT);
            Vector2 p2 = RTTCanvasAutoRegistrar.ProjectToVirtualSpace(c2, refT);
            Vector2 p3 = RTTCanvasAutoRegistrar.ProjectToVirtualSpace(c3, refT);
            Vector2 newSize = new Vector2(
                Mathf.Abs(p1.x - p2.x),
                Mathf.Abs(p1.y - p3.y));

            // Skip if unchanged (cheap fast-path).
            if (_registered && _physicalCenter == newCenter && _physicalSize == newSize)
                return;

            _physicalCenter = newCenter;
            _physicalSize = newSize;

            var surface = new VirtualSurface(
                _surfaceId,
                newCenter,
                newSize,
                EdgePolicy.None,             // Cursor cannot escape bounds
                SurfacePriority.SidePanel,   // Slightly above standard panels
                this);                       // RuntimeRef so ClickDispatcher can introspect

            vcs.RegisterSurface(surface);
            _registered = true;

            // VirtualSurface's constructor always defaults IsVisible=true. Re-apply our
            // cached desired visibility so a Hide() that happened moments earlier (and
            // triggered this very re-registration by toggling side/settings frames
            // active) isn't silently undone — otherwise the world-space cursor never
            // actually hides. IsVisible's setter is internal to VRWorkspace.Input, so we
            // go through the same public API RTTMediaControlsPanel.Hide()/Show() use.
            if (!_isVisible)
            {
                vcs.NotifySurfaceVisibilityChanged(_surfaceId, false);
            }
        }

        private Bounds? ComputeWorldBounds()
        {
            // Use controls frame as seed — must exist.
            if (_controlsFrame == null) return null;

            var bounds = new Bounds(_controlsFrame.position, Vector3.zero);

            // Encapsulate controls quad (the actual display area, not just frame root).
            var controlsCanvas = _controlsFrame.GetComponentInChildren<RTTCanvasBase>();
            if (controlsCanvas != null)
            {
                var quad = controlsCanvas.GetQuadCollider();
                if (quad != null) bounds.Encapsulate(quad.bounds);
            }
            else
            {
                bounds.Encapsulate(_controlsFrame.GetComponent<Collider>()?.bounds ?? new Bounds(_controlsFrame.position, Vector3.zero));
            }

            // Side panel
            if (_sideFrame != null)
            {
                var sideCanvas = _sideFrame.GetComponentInChildren<RTTCanvasBase>();
                if (sideCanvas != null)
                {
                    var quad = sideCanvas.GetQuadCollider();
                    if (quad != null) bounds.Encapsulate(quad.bounds);
                }
                else
                {
                    var col = _sideFrame.GetComponent<Collider>();
                    if (col != null) bounds.Encapsulate(col.bounds);
                }
            }

            // Settings panel (only when opened)
            if (_settingsFrame != null && _settingsFrame.gameObject.activeInHierarchy)
            {
                var settingsCanvas = _settingsFrame.GetComponentInChildren<RTTCanvasBase>();
                if (settingsCanvas != null)
                {
                    var quad = settingsCanvas.GetQuadCollider();
                    if (quad != null) bounds.Encapsulate(quad.bounds);
                }
                else
                {
                    var col = _settingsFrame.GetComponent<Collider>();
                    if (col != null) bounds.Encapsulate(col.bounds);
                }
            }

            return bounds;
        }

        // ── Helpers for click dispatcher & auto-hide timer ───────────────
        /// <summary>Returns true if the given world position lies inside the surface bounds.</summary>
        public bool ContainsWorldPosition(Vector3 worldPos)
        {
            var bounds = ComputeWorldBounds();
            if (!bounds.HasValue) return false;
            return bounds.Value.Contains(worldPos);
        }

        /// <summary>Returns true if the world position is inside controls frame (UI element).</summary>
        public bool IsInsideControlsFrame(Vector3 worldPos)
        {
            if (_controlsFrame == null) return false;
            var col = _controlsFrame.GetComponentInChildren<Collider>();
            return col != null && col.bounds.Contains(worldPos);
        }

        /// <summary>Returns true if world position is inside side panel (UI element).</summary>
        public bool IsInsideSidePanel(Vector3 worldPos)
        {
            if (_sideFrame == null) return false;
            var col = _sideFrame.GetComponentInChildren<Collider>();
            return col != null && col.bounds.Contains(worldPos);
        }

        /// <summary>Returns true if world position is inside settings panel (UI element).</summary>
        public bool IsInsideSettingsPanel(Vector3 worldPos)
        {
            if (_settingsFrame == null) return false;
            if (!_settingsFrame.gameObject.activeInHierarchy) return false;
            var col = _settingsFrame.GetComponentInChildren<Collider>();
            return col != null && col.bounds.Contains(worldPos);
        }
    }
}