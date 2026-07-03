using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.Presentation.Input.VCS;
using VRWorkspace.Media.UI;

namespace VRWorkspace.Presentation.Input.Cursor
{
    /// <summary>
    /// Auto-hides the mediaPlayer controls after 10s of cursor inactivity in the
    /// "empty area" (inside mediaPlayer bounds but NOT on controls/side/settings panels).
    /// Parity with Gaze mode auto-hide behavior.
    ///
    /// Listens to VCS.OnCursorMoved to detect activity, and queries
    /// MediaPlayerSurfaceController.IsInsideControlsFrame/IsInsideSidePanel/etc.
    /// to determine "empty area" status.
    /// </summary>
    public sealed class MediaPlayerAutoHideTimer : MonoBehaviour
    {
        // Configuration
        [SerializeField] private float _hideDelaySeconds = 10f;

        // References
        private MediaPlayerSurfaceController _surface;
        private RTTMediaControlsPanel _controlsPanel;

        // State
        private float _idleSeconds;
        private Vector2 _lastCursorUV;
        private bool _subscribed;

        // ── Static factory ─────────────────────────────────────────────
        public static MediaPlayerAutoHideTimer Attach(
            GameObject playerControlsGroup,
            MediaPlayerSurfaceController surface,
            RTTMediaControlsPanel controlsPanel)
        {
            if (playerControlsGroup == null) return null;
            var t = playerControlsGroup.AddComponent<MediaPlayerAutoHideTimer>();
            t._surface = surface;
            t._controlsPanel = controlsPanel;
            return t;
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null) return;
            vcs.OnCursorMoved += OnCursorMoved;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            var vcs = VirtualCursorSpace.Instance;
            if (vcs != null) vcs.OnCursorMoved -= OnCursorMoved;
            _subscribed = false;
        }

        private void OnCursorMoved(CursorState cursor)
        {
            // Reset idle timer on any cursor movement.
            _idleSeconds = 0f;
            _lastCursorUV = cursor.UV;
        }

        private void Update()
        {
            if (_surface == null || _controlsPanel == null) return;
            if (!_controlsPanel.IsVisible) return;

            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || !vcs.Cursor.SurfaceId.HasValue) return;
            if (!vcs.Surfaces.TryGet(vcs.Cursor.SurfaceId.Value, out var surface)) return;
            if (surface.RuntimeRef != _surface) return; // Cursor on different surface
            if (!surface.IsVisible) return;

            // Compute cursor world position from VCS state.
            Vector3 cursorWorldPos = ComputeCursorWorldPos(vcs, surface);
            if (cursorWorldPos == Vector3.zero) return;

            // Check if cursor is in "empty area" (in bounds but not on UI elements).
            bool inBounds = _surface.ContainsWorldPosition(cursorWorldPos);
            bool inEmptyArea = inBounds
                && !_surface.IsInsideControlsFrame(cursorWorldPos)
                && !_surface.IsInsideSidePanel(cursorWorldPos)
                && !_surface.IsInsideSettingsPanel(cursorWorldPos);

            if (!inEmptyArea)
            {
                _idleSeconds = 0f;
                return;
            }

            _idleSeconds += Time.deltaTime;
            if (_idleSeconds >= _hideDelaySeconds)
            {
                _controlsPanel.Hide();
                _idleSeconds = 0f;
            }
        }

        /// <summary>
        /// Compute cursor world position from VCS state (UV on surface → world on surface quad).
        /// Reuses same math as WorldSpaceCursorRenderer.
        /// </summary>
        private Vector3 ComputeCursorWorldPos(VirtualCursorSpace vcs, VirtualSurface surface)
        {
            var canvas = surface.RuntimeRef as object;
            // Try to find the canvas via the surface's RuntimeRef cast (MediaPlayerSurfaceController case)
            // For now, use RTTMenuFrame as the reference plane (mediaPlayer follows it).
            // We need access to the actual quad — let's find it via the controlsFrame.

            if (_surface == null) return Vector3.zero;

            // Use WorldSpaceCursorRenderer's CursorWorldPosition if available (latest visual pos)
            var renderer = WorldSpaceCursorRenderer.Instance;
            if (renderer != null && renderer.CursorWorldPosition != Vector3.zero)
            {
                return renderer.CursorWorldPosition;
            }

            return Vector3.zero;
        }
    }
}