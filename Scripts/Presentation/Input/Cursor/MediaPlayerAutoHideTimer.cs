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
    /// Listens to VCS.OnCursorMoved to detect activity. "Empty area" is now simply
    /// "the cursor's current VCS surface is the invisible Hub surface" — under the
    /// hub-and-spoke topology (see MediaPlayerHubSurfaceController) the Hub already
    /// covers exactly the region that is inside mediaPlayer bounds but not on top of
    /// Controls/Queue/Settings (those are separate, precisely-sized surfaces), so no
    /// extra world-space Contains/IsInside checks are needed here anymore.
    /// </summary>
    public sealed class MediaPlayerAutoHideTimer : MonoBehaviour
    {
        // Configuration
        [SerializeField] private float _hideDelaySeconds = 10f;

        // References
        private MediaPlayerHubSurfaceController _hub;
        private RTTMediaControlsPanel _controlsPanel;

        // State
        private float _idleSeconds;
        private Vector2 _lastCursorUV;
        private bool _subscribed;

        // ── Static factory ─────────────────────────────────────────────
        public static MediaPlayerAutoHideTimer Attach(
            GameObject playerControlsGroup,
            MediaPlayerHubSurfaceController hub,
            RTTMediaControlsPanel controlsPanel)
        {
            if (playerControlsGroup == null) return null;
            var t = playerControlsGroup.AddComponent<MediaPlayerAutoHideTimer>();
            t._hub = hub;
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
            if (_hub == null || _controlsPanel == null) return;
            if (!_controlsPanel.IsVisible) return;

            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || !vcs.Cursor.SurfaceId.HasValue) return;
            if (!vcs.Surfaces.TryGet(vcs.Cursor.SurfaceId.Value, out var surface)) return;
            if (!ReferenceEquals(surface.RuntimeRef, _hub)) return; // Cursor not in the empty Hub area
            if (!surface.IsVisible) return;

            _idleSeconds += Time.deltaTime;
            if (_idleSeconds >= _hideDelaySeconds)
            {
                _controlsPanel.Hide();
                _idleSeconds = 0f;
            }
        }
    }
}
