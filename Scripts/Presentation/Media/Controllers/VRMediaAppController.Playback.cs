// Extracted to:
//   Assets/VR-Workspace/Scripts/Presentation/Media/Controllers/MediaPlaybackCoordinator.cs
//   Assets/VR-Workspace/Scripts/Presentation/Media/Controllers/MediaFFmpegHandler.cs
//
// This partial file is retained as the glue layer that:
//   - Owns StartPlayback / StopPlayback (called by coordinator via IPlaybackCoordinatorContext)
//   - Owns InitializePlaybackSystem / InitializeProjectionSystem

using UnityEngine;
using Debug = UnityEngine.Debug;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.Projections;
using VRWorkspace.Media.Utils;

namespace VRWorkspace.Media.Core
{
    public partial class VRMediaAppController
    {
        #region Playback Control

        /// <summary>
        /// Start video playback. Positions projection + controls, then delegates to player controller.
        /// Called by <see cref="MediaPlaybackCoordinator"/> via IPlaybackCoordinatorContext.
        /// </summary>
        private void StartPlayback(MediaVideoInfo video)
        {
            if (_playerController == null)
                BuildPlayerUI();

            if (ProjectionSystem != null && _menuFramePosition != Vector3.zero)
                ProjectionSystem.SetTargetPosition(_menuFramePosition, _menuFrameRotation);

            if (_controlsContainer != null && _menuFramePosition != Vector3.zero)
            {
                Vector3 controlsPos = _menuFramePosition + _menuFrameRotation * new Vector3(0, -0.625f, 0);
                _controlsContainer.transform.position = controlsPos;
                _controlsContainer.transform.rotation = Quaternion.identity;

                if (_playerControlsGroup != null)
                {
                    _playerControlsBaseLocalPos = Vector3.zero;
                    ApplyUISettingsToControlsGroup();
                }

                if (_controlsFrameObject != null)
                    _controlsFrameObject.transform.rotation = _menuFrameRotation;

                UpdateSideControlsFacing();
                _controlsContainer.SetActive(true);
            }

            ProjectionDetector.DetectProjectionAndStereo(
                video.Path, video.Width, video.Height, out var projType, out _);
            bool willBeImmersive = !ProjectionDetector.SupportsScreenSettings(projType);

            if (_menuButtonFrameObject != null && _menuFramePosition != Vector3.zero)
            {
                if (willBeImmersive && Camera.main != null) PositionMenuButtonImmersive(Camera.main);
                else PositionMenuButtonFlat();
            }

            if (willBeImmersive && Camera.main != null && _controlsContainer != null)
                SetupControlsFollowCamera(Camera.main);
            else
                _controlsFollowCamera = false;

            PositionSideControlsForProjection(willBeImmersive);

            _playerController?.PlayVideo(video);
        }

        private void StopPlayback()
        {
            _playerController?.Stop();
            CurrentVideo = null;
        }

        #endregion
    }
}
