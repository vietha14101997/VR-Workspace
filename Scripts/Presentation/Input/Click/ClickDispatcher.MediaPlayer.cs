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
    /// This file complements ClickDispatcher.Popup.cs (popup-first branch)
    /// and ClickDispatcher.cs (RTT canvas / ActionBar branches).
    /// </summary>
    public sealed partial class ClickDispatcher
    {
        // Cached reference to the active mediaPlayer controller (set on first mediaPlayer click)
        private MediaPlayerSurfaceController _lastMediaPlayerSurface;
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
                Debug.Log($"[MP_DBG-C] TryDispatchMediaPlayerClick EARLY RETURN: vcs={vcs != null}, cursor.SurfaceId.HasValue={vcs?.Cursor.SurfaceId.HasValue}");
                return false;
            }
            if (!vcs.Surfaces.TryGet(vcs.Cursor.SurfaceId.Value, out var surface))
            {
                Debug.Log($"[MP_DBG-C] TryDispatchMediaPlayerClick EARLY RETURN: surface not found for SurfaceId={vcs.Cursor.SurfaceId}");
                return false;
            }

            var mediaPlayer = surface.RuntimeRef as MediaPlayerSurfaceController;
            if (mediaPlayer == null)
            {
                Debug.Log($"[MP_DBG-C] TryDispatchMediaPlayerClick EARLY RETURN: RuntimeRef is NOT MediaPlayerSurfaceController, it's={surface.RuntimeRef?.GetType().Name}");
                return false;
            }
            Debug.Log($"[MP_DBG-C] TryDispatchMediaPlayerClick entry, surface.IsVisible={surface.IsVisible}");

            // Cache reference for later use
            _lastMediaPlayerSurface = mediaPlayer;
            _lastMediaPlayerControlsPanel = FindMediaPlayerControlsPanel(mediaPlayer);
            _lastMenuButtonFrame = FindMenuButtonFrame(mediaPlayer);
            Debug.Log($"[MP_DBG-C] _lastMediaPlayerControlsPanel={_lastMediaPlayerControlsPanel != null}, _lastMenuButtonFrame={_lastMenuButtonFrame != null}");

            // CASE 1: Controls hidden → Show them (wake from hidden)
            if (!surface.IsVisible)
            {
                Debug.Log($"[MP_DBG-D] CASE 1 detected: surface.IsVisible=false, calling Show()");
                if (_lastMediaPlayerControlsPanel != null)
                {
                    _lastMediaPlayerControlsPanel.Show();
                    Debug.Log($"[MP_DBG-E] Show() called, IsVisible now={_lastMediaPlayerControlsPanel.IsVisible}");
                }
                else
                {
                    Debug.Log($"[MP_DBG-D] CASE 1 BUT _lastMediaPlayerControlsPanel is NULL!");
                }
                return true;
            }

            // CASE 2: Controls visible → check if click hits UI element or empty area
            // Cast ray against popup layer to detect hit (cursor visual may be on RTTMenuFrame but popup layer = mediaPlayer)
            RaycastHit popupHit;
            bool hitSomething = Physics.Raycast(ray, out popupHit, 10f, GetPopupRaycastMask(), QueryTriggerInteraction.Collide);

            if (!hitSomething)
            {
                // Ray missed everything in popup mask → empty space → Hide
                if (_lastMediaPlayerControlsPanel != null)
                {
                    _lastMediaPlayerControlsPanel.Hide();
                }
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
            if (_lastMediaPlayerControlsPanel != null)
            {
                _lastMediaPlayerControlsPanel.Hide();
            }
            return true;
        }

        private bool IsPartOfMediaPlayerRTT(GameObject go)
        {
            if (go == null) return false;
            // Walk up parents to find RTTCanvasBase
            Transform current = go.transform;
            while (current != null)
            {
                var canvas = current.GetComponent<RTTCanvasBase>();
                if (canvas != null)
                {
                    // Check if this canvas is part of mediaPlayer UI (controls, side, settings)
                    if (canvas.transform.IsChildOf(_lastMediaPlayerSurface.transform) ||
                        canvas.transform == _lastMediaPlayerSurface.transform)
                    {
                        return true;
                    }
                }
                current = current.parent;
            }
            return false;
        }

        private static RTTMediaControlsPanel FindMediaPlayerControlsPanel(MediaPlayerSurfaceController surface)
        {
            // Walk children of PlayerControlsGroup to find RTTMediaControlsPanel
            var panel = surface.GetComponentInChildren<RTTMediaControlsPanel>(true);
            return panel;
        }

        /// <summary>
        /// Find the menu button frame GameObject within PlayerControlsGroup children.
        /// Returns null if not found (e.g., menu button hidden by mode-aware code).
        /// </summary>
        private static GameObject FindMenuButtonFrame(MediaPlayerSurfaceController surface)
        {
            // The menu button frame is a sibling/child of mediaPlayer UI. Look for any
            // GameObject named "MenuButtonFrame" or similar in PlayerControlsGroup hierarchy.
            if (surface == null) return null;
            var transforms = surface.GetComponentsInChildren<Transform>(true);
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