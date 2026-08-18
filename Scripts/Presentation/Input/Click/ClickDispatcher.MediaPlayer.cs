using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRWorkspace.Domain.Input;
using VRWorkspace.Presentation.Input.Cursor;
using VRWorkspace.Presentation.Input.VCS;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.Media.UI;

namespace VRWorkspace.Presentation.Input.Click
{
    /// <summary>
    /// Partial class: mediaPlayer click dispatch.
    /// Handles:
    ///   1. Click in mediaPlayer bounds but NOT on UI element → Hide controls
    ///   2. Click when controls are hidden → Show controls (cursor appears)
    ///
    /// Under the hub-and-spoke VCS topology (see MediaPlayerHubSurfaceController)
    /// the cursor's "current surface" while inside the mediaPlayer UI can be any of
    /// four distinct VirtualSurfaces: the Controls/Queue/Settings RTTCanvasBase
    /// panels, or the invisible Hub. There is no longer a single merged surface to
    /// type-check against, so instead we identify "this surface belongs to the
    /// mediaPlayer" by walking up to find a MediaPlayerHubSurfaceController — which
    /// is attached directly on the PlayerControlsGroup root that all four surfaces
    /// share as an ancestor.
    ///
    /// This file complements ClickDispatcher.Popup.cs (popup-first branch)
    /// and ClickDispatcher.cs (RTT canvas / ActionBar branches).
    /// </summary>
    public sealed partial class ClickDispatcher
    {
        // Cached reference to the active mediaPlayer's root (PlayerControlsGroup transform,
        // shared by Controls/Hub/Queue/Settings), set on first mediaPlayer click.
        private Transform _lastMediaPlayerRoot;
        private RTTMediaControlsPanel _lastMediaPlayerControlsPanel;
        private GameObject _lastMenuButtonFrame;  // Menu button frame (must not be intercepted)

        /// <summary>
        /// Try to dispatch a mediaPlayer click. Returns true if the click was consumed by mediaPlayer.
        /// Called from OnClickDispatched before falling through to RTT canvas / ActionBar branches.
        /// </summary>
        private bool TryDispatchMediaPlayerClick(Camera cam, Ray ray)
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || !vcs.Cursor.SurfaceId.HasValue)
            {
                return false;
            }
            if (!vcs.Surfaces.TryGet(vcs.Cursor.SurfaceId.Value, out var surface))
            {
                return false;
            }

            Transform mediaPlayerRoot = GetMediaPlayerRoot(surface.RuntimeRef);
            if (mediaPlayerRoot == null)
            {
                return false;
            }

            // Cache reference for later use
            _lastMediaPlayerRoot = mediaPlayerRoot;
            _lastMediaPlayerControlsPanel = FindMediaPlayerControlsPanel(mediaPlayerRoot);
            _lastMenuButtonFrame = FindMenuButtonFrame(mediaPlayerRoot);

            // CASE 1: Controls hidden → Show them (wake from hidden)
            if (!surface.IsVisible)
            {
                _lastMediaPlayerControlsPanel?.Show();
                return true;
            }

            // CASE 2: Controls visible → check if click hits UI element or empty area
            // Cast ray against popup layer to detect hit (cursor visual may be on RTTMenuFrame but popup layer = mediaPlayer)
            RaycastHit popupHit;
            bool hitSomething = Physics.Raycast(ray, out popupHit, 10f, GetPopupRaycastMask(), QueryTriggerInteraction.Collide);

            if (!hitSomething)
            {
                // Ray missed everything in popup mask → empty space → Hide
                _lastMediaPlayerControlsPanel?.Hide();
                return true;
            }

            GameObject hitGO = popupHit.collider.gameObject;

            // BUG 2 FIX: if hit is the menu button, let its onClick handler fire Show().
            // Returning false falls through to standard RTT/Button click dispatch.
            if (_lastMenuButtonFrame != null && IsChildOf(hitGO, _lastMenuButtonFrame))
            {
                return false;
            }

            // Check if hit is part of any RTT canvas (controlsPanel/sidePanel/settingsPanel)
            // — RTT canvases have BoxColliders on their display quads that are in the popup layer (VirtualObjects)
            if (IsPartOfMediaPlayerRTT(hitGO))
            {
                // UI element hit → let it fall through to RTT canvas raycast via TryPopupRaycast result
                return false;
            }

            // Hit something that's NOT a UI element (could be overlay or other) → Hide
            _lastMediaPlayerControlsPanel?.Hide();
            return true;
        }

        /// <summary>
        /// Given a VirtualSurface's RuntimeRef, returns the mediaPlayer's PlayerControlsGroup
        /// transform if that surface belongs to the (currently open) mediaPlayer UI, or null
        /// otherwise. Handles both the Hub (RuntimeRef is the hub controller itself, attached
        /// directly on PlayerControlsGroup) and the Controls/Queue/Settings canvases
        /// (RuntimeRef is their RTTCanvasBase, which lives somewhere under the same root).
        /// </summary>
        private static Transform GetMediaPlayerRoot(object runtimeRef)
        {
            if (runtimeRef is MediaPlayerHubSurfaceController hub)
            {
                return hub.transform;
            }

            if (runtimeRef is RTTCanvasBase canvas)
            {
                var siblingHub = canvas.GetComponentInParent<MediaPlayerHubSurfaceController>();
                if (siblingHub != null) return siblingHub.transform;
            }

            return null;
        }

        private bool IsPartOfMediaPlayerRTT(GameObject go)
        {
            if (go == null || _lastMediaPlayerRoot == null) return false;
            // Walk up parents to find RTTCanvasBase
            Transform current = go.transform;
            while (current != null)
            {
                var canvas = current.GetComponent<RTTCanvasBase>();
                if (canvas != null)
                {
                    // Check if this canvas is part of mediaPlayer UI (controls, side, settings)
                    if (canvas.transform.IsChildOf(_lastMediaPlayerRoot))
                    {
                        return true;
                    }
                }
                current = current.parent;
            }
            return false;
        }

        private static RTTMediaControlsPanel FindMediaPlayerControlsPanel(Transform playerControlsRoot)
        {
            if (playerControlsRoot == null) return null;
            return playerControlsRoot.GetComponentInChildren<RTTMediaControlsPanel>(true);
        }

        /// <summary>
        /// Find the menu button frame GameObject within PlayerControlsGroup children.
        /// Returns null if not found (e.g., menu button hidden by mode-aware code).
        /// </summary>
        private static GameObject FindMenuButtonFrame(Transform playerControlsRoot)
        {
            if (playerControlsRoot == null) return null;
            var transforms = playerControlsRoot.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t.name == "MenuButtonFrame") return t.gameObject;
            }
            return null;
        }

        /// <summary>
        /// Check if `obj` is a child of `parent` (or equal to it) in the hierarchy.
        /// </summary>
        private static bool IsChildOf(GameObject obj, GameObject parent)
        {
            if (obj == null || parent == null) return false;
            Transform current = obj.transform;
            while (current != null)
            {
                if (current.gameObject == parent) return true;
                current = current.parent;
            }
            return false;
        }
    }
}
