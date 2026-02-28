using UnityEngine;
using System.Collections;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.Projections;
using VRWorkspace.Media.UI;
using VRWorkspace.Media.Utils;

namespace VRWorkspace.Media.Core
{
    /// <summary>
    /// VRVideoPlayerController partial: Per-video settings cache - auto-save, restore, and shader float helpers.
    /// </summary>
    public partial class VRVideoPlayerController
    {
        private void StartAutoSaveTimer()
        {
            StopAutoSaveTimer();
            _autoSaveCoroutine = StartCoroutine(AutoSaveLoop());
        }

        private void StopAutoSaveTimer()
        {
            if (_autoSaveCoroutine != null)
            {
                StopCoroutine(_autoSaveCoroutine);
                _autoSaveCoroutine = null;
            }
        }

        private IEnumerator AutoSaveLoop()
        {
            var wait = new WaitForSeconds(10f);
            while (true)
            {
                yield return wait;
                SaveCurrentVideoSettings();
            }
        }

        private void SaveCurrentVideoSettings()
        {
            if (_isStopped) return; // Already saved during Stop(), engine state is stale
            if (_currentVideo == null || string.IsNullOrEmpty(_currentVideo.Value.Path)) return;

            try
            {
                var entry = new VideoSettingsEntry
                {
                    FilePath = _currentVideo.Value.Path,
                    Projection = (int)(_projectionSystem?.CurrentProjection ?? VideoProjectionType.Flat),
                    Stereo = (int)(_projectionSystem?.CurrentStereoMode ?? StereoMode.Mono),
                    Monitor = (int)_currentMonitor,
                    Environment = (int)_currentEnv,
                    PlaybackPosition = _playbackEngine?.CurrentTime ?? 0,
                    PlaybackSpeed = _playbackEngine?.PlaybackSpeed ?? 1f,
                    Brightness = GetCurrentShaderFloat("_Brightness", 1f),
                    Contrast = GetCurrentShaderFloat("_Contrast", 1f),
                    Saturation = GetCurrentShaderFloat("_Saturation", 1f),
                    Sharpness = GetCurrentShaderFloat("_Sharpness", 0.5f),
                    Tint = GetCurrentShaderFloat("_Tint", 0f),
                    Temperature = GetCurrentShaderFloat("_Temperature", 0f),
                    ScreenDistance = _displaySettings.Distance,
                    ScreenScale = _displaySettings.Scale,
                    ScreenCurvature = _displaySettings.Curvature,
                    AspectRatio = _projectionSystem?.GetAspectRatioOverride() ?? "default",
                    FOVZoom = GetImmersiveFOV(),
                    ImmTilt = GetCurrentShaderFloat("_Tilt", 0f),
                    ImmYaw = GetCurrentShaderFloat("_YawOffset", 0f),
                    VerticalShift = GetCurrentShaderFloat("_VerticalShift", 0f),
                    HorizontalShift = GetCurrentShaderFloat("_HorizontalShift", 0f),
                    LRInverse = GetCurrentShaderFloat("_LRInverse", 0f) > 0.5f,
                    LastAccessedTicks = System.DateTime.UtcNow.Ticks
                };

                VideoSettingsCache.Set(entry.FilePath, entry);
                VideoSettingsCache.FlushToDisk();
                Debug.Log($"[VRVideoPlayerController] Saved settings: Position={entry.PlaybackPosition:F1}s, Brightness={entry.Brightness:F2}, AR={entry.AspectRatio}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VRVideoPlayerController] Failed to save video settings: {ex.Message}");
            }
        }

        private void RestoreCachedSettings()
        {
            if (_cachedEntry == null) return;

            // Restore picture adjustments
            ApplyShaderFloat("_Brightness", _cachedEntry.Brightness);
            ApplyShaderFloat("_Contrast", _cachedEntry.Contrast);
            ApplyShaderFloat("_Saturation", _cachedEntry.Saturation);
            ApplyShaderFloat("_Sharpness", _cachedEntry.Sharpness);
            ApplyShaderFloat("_Tint", _cachedEntry.Tint);
            ApplyShaderFloat("_Temperature", _cachedEntry.Temperature);

            // Restore display settings (flat mode only)
            if (ProjectionDetector.SupportsScreenSettings((VideoProjectionType)_cachedEntry.Projection))
            {
                _displaySettings.Distance = _cachedEntry.ScreenDistance;
                _displaySettings.Scale = _cachedEntry.ScreenScale;
                _projectionSystem?.UpdateDisplay(_displaySettings);

                // Restore aspect ratio
                _projectionSystem?.SetAspectRatioOverride(_cachedEntry.AspectRatio ?? "default");
            }

            // Restore immersive settings
            if (_projectionSystem?.ActiveRenderer is ImmersiveSphereRenderer imm)
            {
                if (_cachedEntry.FOVZoom > 0f) imm.SetFieldOfView(_cachedEntry.FOVZoom);
                imm.SetShaderFloat("_Tilt", _cachedEntry.ImmTilt);
                imm.SetShaderFloat("_VerticalShift", _cachedEntry.VerticalShift);
                imm.SetShaderFloat("_HorizontalShift", _cachedEntry.HorizontalShift);
                if (_cachedEntry.LRInverse) imm.SetShaderFloat("_LRInverse", 1f);
            }

            // Restore playback speed
            if (_playbackEngine != null && _cachedEntry.PlaybackSpeed > 0f)
                _playbackEngine.PlaybackSpeed = _cachedEntry.PlaybackSpeed;

            // Restore environment: set lights based on cached env type
            if (_environmentController != null)
            {
                switch ((RTTMediaProjectionPopup.EnvironmentType)_cachedEntry.Environment)
                {
                    case RTTMediaProjectionPopup.EnvironmentType.Cinema:
                        _environmentController.SetLightsEnabled(false);
                        break;
                    case RTTMediaProjectionPopup.EnvironmentType.LightOff:
                        _environmentController.SetLightsEnabled(false);
                        _environmentController.HideEnvironment();
                        break;
                    case RTTMediaProjectionPopup.EnvironmentType.Room:
                        _environmentController.SetLightsEnabled(true);
                        _environmentController.ShowEnvironment();
                        break;
                }
            }

            Debug.Log($"[VRVideoPlayerController] Restored cached settings: Brightness={_cachedEntry.Brightness:F2}, " +
                $"Speed={_cachedEntry.PlaybackSpeed:F2}, FOV={_cachedEntry.FOVZoom:F0}");

            _cachedEntry = null; // consumed
        }

        private void ApplyShaderFloat(string param, float value)
        {
            if (_projectionSystem?.ActiveRenderer is ImmersiveSphereRenderer imm)
                imm.SetShaderFloat(param, value);
            else if (_projectionSystem?.ActiveRenderer is FlatProjectionRenderer flat)
                flat.SetBoardShaderFloat(param, value);
        }

        private float GetCurrentShaderFloat(string param, float defaultVal)
        {
            if (_projectionSystem?.ActiveRenderer is ImmersiveSphereRenderer imm)
                return imm.GetShaderFloat(param, defaultVal);
            if (_projectionSystem?.ActiveRenderer is FlatProjectionRenderer flat)
                return flat.GetBoardShaderFloat(param, defaultVal);
            return defaultVal;
        }

        private float GetImmersiveFOV()
        {
            if (_projectionSystem?.ActiveRenderer is ImmersiveSphereRenderer imm)
                return imm.CurrentFOV;
            return 0f;
        }
    }
}
