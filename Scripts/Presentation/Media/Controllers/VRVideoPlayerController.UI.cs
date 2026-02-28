using UnityEngine;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.Projections;

namespace VRWorkspace.Media.Core
{
    /// <summary>
    /// UI state updates, subtitle management, UI refresh methods, seek bar, time display.
    /// </summary>
    public partial class VRVideoPlayerController
    {
        #region UI State Updates

        /// <summary>
        /// Handle playback state changes and update controls panel UI accordingly.
        /// </summary>
        private void HandleStateChanged(VideoPlaybackEngine.PlaybackState state)
        {
            bool isPlaying = state == VideoPlaybackEngine.PlaybackState.Playing;
            _controlsPanel?.SetPlayState(isPlaying);

            // Show loading status during preparation/buffering
            if (state == VideoPlaybackEngine.PlaybackState.Loading)
            {
                _controlsPanel?.SetTitle("Preparing...");
            }
            else if (state == VideoPlaybackEngine.PlaybackState.Buffering)
            {
                _controlsPanel?.SetTitle("Buffering...");
            }
            else if (state == VideoPlaybackEngine.PlaybackState.Playing && _currentVideo.HasValue)
            {
                _controlsPanel?.SetTitle(_currentVideo.Value.Title);
            }
        }

        /// <summary>
        /// Handle time updates from the playback engine and push to the seek bar / time display.
        /// </summary>
        private void HandleTimeUpdate(double currentTime)
        {
            _controlsPanel?.SetCurrentTime((float)currentTime);
        }

        /// <summary>
        /// Notify the controls panel that a seek operation has completed so it can re-enable the seek bar.
        /// </summary>
        private void HandleSeekCompleted()
        {
            _controlsPanel?.OnSeekCompleted();
        }

        /// <summary>
        /// Refresh the controls panel title and play-state after playback ends.
        /// Called by HandlePlaybackEnded in the main file after saving settings.
        /// </summary>
        private void RefreshUIOnPlaybackEnded()
        {
            if (_controlsPanel != null)
            {
                _controlsPanel.SetCurrentTime((float)(_playbackEngine?.Duration ?? 0));
                _controlsPanel.SetPlayState(false);
                _controlsPanel.Show();
            }
        }

        /// <summary>
        /// Push the current video texture to the controls panel preview frame.
        /// Called after the video is prepared and the texture is available.
        /// </summary>
        private void RefreshPreviewTexture()
        {
            if (_controlsPanel == null || _playbackEngine == null) return;

            _controlsPanel.SetDuration((float)_playbackEngine.Duration);

            // Set aspect ratio for preview frame
            float ratio = 16f / 9f; // Default
            if (_playbackEngine.UseNV12Output && _playbackEngine.YPlaneTexture != null)
            {
                ratio = (float)_playbackEngine.YPlaneTexture.width / _playbackEngine.YPlaneTexture.height;
            }
            else if (_playbackEngine.OutputTexture != null)
            {
                ratio = (float)_playbackEngine.OutputTexture.width / _playbackEngine.OutputTexture.height;
            }

            if (ratio > 0)
            {
                _controlsPanel.SetPreviewAspectRatio(ratio);
            }

            // Set the main video texture as the preview (mirrors playback)
            if (_playbackEngine.OutputTexture != null)
            {
                _controlsPanel.SetPreviewTexture(_playbackEngine.OutputTexture);
            }
        }

        #endregion
    }
}
