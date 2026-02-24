using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Debug = UnityEngine.Debug;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.Projections;
using VRWorkspace.Media.UI;
using VRWorkspace.Media.Utils;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.Media.Core
{
    public partial class VRMediaAppController
    {
        #region Initialization

        private void InitializePlaybackSystem()
        {
            GameObject engineObj = new GameObject("VideoPlaybackEngine");
            engineObj.transform.SetParent(transform);
            PlaybackEngine = engineObj.AddComponent<VideoPlaybackEngine>();
        }

        private void InitializeProjectionSystem()
        {
            // Find VirtualObjects to parent the projection for zoom support
            GameObject virtualObjects = GameObject.Find("VirtualObjects");
            Transform projectionParent = virtualObjects != null ? virtualObjects.transform : transform;

            if (virtualObjects != null)
            {
                Debug.Log("[VRMediaAppController] Projection will be parented to VirtualObjects for zoom support");
            }
            else
            {
                Debug.LogWarning("[VRMediaAppController] VirtualObjects not found, zoom may not work");
            }

            GameObject projectionObj = new GameObject("VRVideoProjectionSystem");
            projectionObj.transform.SetParent(projectionParent, false);
            ProjectionSystem = projectionObj.AddComponent<VRVideoProjectionSystem>();

            // Initialize with camera rig (or main camera if no rig)
            Transform cameraRig = Camera.main?.transform.parent ?? Camera.main?.transform;

            if (cameraRig == null)
            {
                Debug.LogWarning("[VRMediaAppController] Camera rig not found, using this transform as parent");
                cameraRig = transform;
            }
            else
            {
                Debug.Log($"[VRMediaAppController] Found camera rig: {cameraRig.name}");
            }

            ProjectionSystem.Initialize(cameraRig);
        }

        #endregion

        #region Playback Control

        /// <summary>
        /// Start video playback using the player controller.
        /// </summary>
        private void StartPlayback(MediaVideoInfo video)
        {
            // Ensure player UI is built
            if (_playerController == null)
            {
                BuildPlayerUI();
            }

            // Position projection at menu frame location (if we have saved position)
            if (ProjectionSystem != null && _menuFramePosition != Vector3.zero)
            {
                ProjectionSystem.SetTargetPosition(_menuFramePosition, _menuFrameRotation);
            }

            // Position controls below the video screen (EXACT same as original)
            if (_controlsContainer != null && _menuFramePosition != Vector3.zero)
            {
                Vector3 controlsPos = _menuFramePosition + _menuFrameRotation * new Vector3(0, -0.625f, 0);
                _controlsContainer.transform.position = controlsPos;
                _controlsContainer.transform.rotation = Quaternion.identity;

                // _playerControlsGroup stays at local zero; UI Settings offsets applied on top
                if (_playerControlsGroup != null)
                {
                    _playerControlsBaseLocalPos = Vector3.zero;
                    ApplyUISettingsToControlsGroup();
                }

                // VideoControlsFrame faces the camera
                if (_controlsFrameObject != null)
                {
                    _controlsFrameObject.transform.rotation = _menuFrameRotation;
                }

                // SideControlsFrame faces camera independently
                UpdateSideControlsFacing();

                _controlsContainer.SetActive(true);
            }

            // Detect projection type for positioning
            ProjectionDetector.DetectProjectionAndStereo(
                video.Path, video.Width, video.Height,
                out var projType, out _);
            bool willBeImmersive = !ProjectionDetector.SupportsScreenSettings(projType);

            // Position menu button below the video screen (in VirtualObjects, follows zoom)
            if (_menuButtonFrameObject != null && _menuFramePosition != Vector3.zero)
            {
                if (willBeImmersive && Camera.main != null)
                {
                    PositionMenuButtonImmersive(Camera.main);
                }
                else
                {
                    PositionMenuButtonFlat();
                }
            }

            // Immersive mode: controls container follows camera position each frame
            if (willBeImmersive && Camera.main != null && _controlsContainer != null)
            {
                SetupControlsFollowCamera(Camera.main);
            }
            else
            {
                _controlsFollowCamera = false;
            }

            // Adjust side controls + pagination Y for immersive mode (raise to eye level)
            // This must run regardless of _menuButtonFrameObject state
            PositionSideControlsForProjection(willBeImmersive);

            // Use player controller to handle playback
            if (_playerController != null)
            {
                _playerController.PlayVideo(video);
            }
        }

        /// <summary>
        /// Stop current playback.
        /// </summary>
        private void StopPlayback()
        {
            if (_playerController != null)
            {
                _playerController.Stop();
            }

            CurrentVideo = null;
        }

        #endregion

        #region Mode Transition Coroutines

        private IEnumerator FadeInPlayerAfterDirectPlay(MediaVideoInfo videoInfo)
        {
            yield return StartCoroutine(AnimatePlayerFade(0f, 1f, FADE_IN_DURATION));

            _transitionCoroutine = null;
            OnModeChanged?.Invoke(AppMode.Player);
            OnVideoStarted?.Invoke(videoInfo);
            Debug.Log($"[VRMediaAppController] Direct play started: {videoInfo.Title}");
        }

        private IEnumerator FadeOutAndCleanupDirectPlay()
        {
            // Fade out player
            yield return StartCoroutine(AnimatePlayerFade(1f, 0f, FADE_OUT_DURATION));

            RTTManager.Instance?.ExitImmersiveMode();
            HidePlayerUI();

            // Invoke callback to re-show the caller (File Manager)
            _onDirectPlayExit?.Invoke();
            _onDirectPlayExit = null;

            // Cleanup and destroy
            Cleanup();

            if (_activeDirectPlayer == this)
                _activeDirectPlayer = null;

            Destroy(gameObject);
        }

        private IEnumerator SwitchToLibraryWithFade()
        {
            // Fade out player
            yield return StartCoroutine(AnimatePlayerFade(1f, 0f, FADE_OUT_DURATION));

            // Exit Immersive Mode (restores Taskbar and MenuFrame)
            RTTManager.Instance?.ExitImmersiveMode();

            // Show library UI at alpha=0, hide player
            ShowLibraryUI();
            HidePlayerUI();
            SetAllLibraryFramesAlpha(0f);

            // Fade in library frames
            yield return StartCoroutine(AnimateLibraryFade(0f, 1f, FADE_IN_DURATION));

            _transitionCoroutine = null;
            OnModeChanged?.Invoke(AppMode.Library);
            Debug.Log("[VRMediaAppController] Switched to Library mode");
        }

        private IEnumerator SwitchToPlayerWithFade(MediaVideoInfo video)
        {
            // Fade out library frames
            yield return StartCoroutine(AnimateLibraryFade(1f, 0f, FADE_OUT_DURATION));

            // Hide library UI (saves position)
            HideLibraryUI();

            // Enter Immersive Mode (hides Taskbar and MenuFrame)
            RTTManager.Instance?.EnterImmersiveMode();

            ShowPlayerUI();

            // Populate queue AFTER ShowPlayerUI (items need active parent for HoverEffectController.Awake)
            // but BEFORE StartPlayback so RTT frame renders correct content
            if (_queuePanel != null)
            {
                var queue = MediaPlaylistService.Instance.GetPlaybackQueue();
                int currentIdx = MediaPlaylistService.Instance.CurrentQueueIndex;
                _queuePanel.SetQueue(queue, currentIdx);
            }

            // Start playback with player at alpha=0
            SetAllPlayerFramesAlpha(0f);
            StartPlayback(video);

            // Fade in player
            yield return StartCoroutine(AnimatePlayerFade(0f, 1f, FADE_IN_DURATION));

            _transitionCoroutine = null;
            OnModeChanged?.Invoke(AppMode.Player);
            OnVideoStarted?.Invoke(video);
            Debug.Log($"[VRMediaAppController] Switched to Player mode: {video.Title}");
        }

        #endregion

        #region Error & Fallback Handling

        private void HandlePlaybackFailed(string error, bool isCodecError, string codecName, string containerFormat)
        {
            if (_errorDialog == null)
            {
                SwitchToLibrary();
                return;
            }

            // Store info for action buttons
            _lastFailedVideoPath = _playerController?.CurrentVideo?.Path;
            _lastFailedContainerFormat = containerFormat;

            bool needsRemux = !string.IsNullOrEmpty(containerFormat) || isCodecError;

            if (needsRemux)
            {
                bool canRemux = !string.IsNullOrEmpty(FindFFmpeg());

                if (canRemux && !string.IsNullOrEmpty(_lastFailedVideoPath))
                {
                    // FFmpeg available - auto-remux immediately without user interaction
                    Debug.Log($"[VRMediaAppController] Auto-remuxing: container={containerFormat ?? "MP4"}, codec={codecName ?? "unknown"}");
                    StartCoroutine(RemuxAndPlayCoroutine(_lastFailedVideoPath));
                    return;
                }

                // FFmpeg not found - show dialog with appropriate options
                string formatInfo = !string.IsNullOrEmpty(containerFormat)
                    ? $"Format: {containerFormat}"
                    : $"Codec: {GetFriendlyCodecName(codecName ?? "unknown")}";

    #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                // Windows: offer to download FFmpeg
                _errorDialog.ShowError(
                    !string.IsNullOrEmpty(containerFormat) ? MediaErrorDialog.ErrorType.UnsupportedContainer : MediaErrorDialog.ErrorType.CodecNotSupported,
                    $"{formatInfo}\n\nFFmpeg is needed to convert this video.\nIt will be downloaded automatically (~80 MB).");
                _errorDialog.SetRetryLabel("Download & Convert");
    #else
                // Mobile/other: no download option, show Open in Player
                _errorDialog.ShowError(
                    !string.IsNullOrEmpty(containerFormat) ? MediaErrorDialog.ErrorType.UnsupportedContainer : MediaErrorDialog.ErrorType.CodecNotSupported,
                    formatInfo);
    #endif
            }
            else
            {
                _errorDialog.Show("Playback Error", error, showRetry: false, showOpenExternal: true);
            }
        }

        private void HandleRetryOrConvert()
        {
            if (string.IsNullOrEmpty(_lastFailedVideoPath))
            {
                _errorDialog?.Hide();
                SwitchToLibrary();
                return;
            }

            // If FFmpeg is available, remux directly
            if (!string.IsNullOrEmpty(FindFFmpeg()))
            {
                _errorDialog?.Hide();
                StartCoroutine(RemuxAndPlayCoroutine(_lastFailedVideoPath));
                return;
            }

    #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // FFmpeg not found - download it first, then remux
            StartCoroutine(DownloadAndConvertCoroutine(_lastFailedVideoPath));
    #else
            _errorDialog?.Hide();
            SwitchToLibrary();
    #endif
        }

    #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private IEnumerator DownloadAndConvertCoroutine(string videoPath)
        {
            // Show the dialog as progress display (keep it visible)
            _errorDialog?.Show("Preparing...", "Setting up video converter...", showRetry: false, showOpenExternal: false);

            bool downloadSuccess = false;
            yield return StartCoroutine(DownloadFFmpegCoroutine(success => downloadSuccess = success));

            if (downloadSuccess && !string.IsNullOrEmpty(FindFFmpeg()))
            {
                _errorDialog?.Hide();
                StartCoroutine(RemuxAndPlayCoroutine(videoPath));
            }
            else
            {
                _errorDialog?.Show("Download Failed",
                    "Could not download FFmpeg.\nCheck your internet connection and try again.",
                    showRetry: true, showOpenExternal: true);
                _errorDialog?.SetRetryLabel("Retry Download");
            }
        }
    #endif

        private IEnumerator RemuxAndPlayCoroutine(string sourcePath)
        {
            string ffmpegPath = FindFFmpeg();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                Debug.LogError("[VRMediaAppController] FFmpeg not found");
                SwitchToLibrary();
                yield break;
            }

            // Create temp output path (same dir, .remuxed.mp4)
            string dir = Path.GetDirectoryName(sourcePath);
            string nameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
            string outputPath = Path.Combine(dir, $"{nameNoExt}.remuxed.mp4");

            // Skip remux if already done
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 1024)
            {
                Debug.Log($"[VRMediaAppController] Using existing remuxed file: {outputPath}");
                PlayRemuxedVideo(outputPath, sourcePath);
                yield break;
            }

            Debug.Log($"[VRMediaAppController] Starting FFmpeg remux: {sourcePath} -> {outputPath}");

            // Show a simple progress indication
            _errorDialog?.Show("Converting...", "Remuxing video to MP4 format.\nThis should be fast (no re-encoding).", showRetry: false, showOpenExternal: false);

            // Run FFmpeg: copy all streams to MP4 container
            bool processComplete = false;
            bool processSuccess = false;
            string processError = null;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-i \"{sourcePath}\" -c copy -movflags faststart -y \"{outputPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    };

                    using (var process = Process.Start(psi))
                    {
                        string stderr = process.StandardError.ReadToEnd();
                        process.WaitForExit(120000); // 2 minute timeout

                        if (process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 1024)
                        {
                            processSuccess = true;
                        }
                        else
                        {
                            processError = $"FFmpeg exit code: {process.ExitCode}";
                            if (stderr.Length > 200)
                                processError += $"\n{stderr.Substring(stderr.Length - 200)}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    processError = ex.Message;
                }
                processComplete = true;
            });

            // Wait for FFmpeg to finish
            float remuxStart = Time.time;
            while (!processComplete)
            {
                if (Time.time - remuxStart > 130f) // 130s safety timeout
                {
                    processError = "Remux timeout";
                    break;
                }
                yield return null;
            }

            if (processSuccess)
            {
                Debug.Log($"[VRMediaAppController] Remux complete in {Time.time - remuxStart:F1}s: {outputPath}");
                _errorDialog?.Hide();
                PlayRemuxedVideo(outputPath, sourcePath);
            }
            else
            {
                Debug.LogError($"[VRMediaAppController] Remux failed: {processError}");
                // Show error and offer "Open in Player" instead
                _errorDialog?.Show("Conversion Failed", $"FFmpeg could not convert this video.\n\n{processError}", showRetry: false, showOpenExternal: true);
            }
        }

        private void PlayRemuxedVideo(string remuxedPath, string originalPath)
        {
            if (_playerController == null)
            {
                Debug.LogError("[VRMediaAppController] PlayerController is null, cannot play remuxed video");
                SwitchToLibrary();
                return;
            }

            // Create a MediaVideoInfo for the remuxed file based on the original
            var video = _playerController.CurrentVideo;
            if (video.HasValue)
            {
                var remuxedVideo = video.Value;
                remuxedVideo.Path = remuxedPath;

                Debug.Log($"[VRMediaAppController] Playing remuxed video: {remuxedPath}");
                _playerController.PlayVideo(remuxedVideo);
            }
            else
            {
                var remuxedVideo = MediaVideoInfo.FromPath(remuxedPath);
                _playerController.PlayVideo(remuxedVideo);
            }
        }

        private void HandleOpenInExternalPlayer()
        {
            _errorDialog?.Hide();

            if (!string.IsNullOrEmpty(_lastFailedVideoPath))
            {
                Debug.Log($"[VRMediaAppController] Opening in system player: {_lastFailedVideoPath}");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _lastFailedVideoPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VRMediaAppController] Failed to open external player: {ex.Message}");
                }
            }

            SwitchToLibrary();
        }

        private static string GetFriendlyCodecName(string fourcc)
        {
            if (string.IsNullOrEmpty(fourcc)) return "Unknown";

            switch (fourcc.ToLowerInvariant())
            {
                case "hev1":
                case "hvc1":
                    return "HEVC (H.265)";
                case "av01":
                    return "AV1";
                case "avc1":
                case "avc3":
                    return "H.264";
                case "vp09":
                    return "VP9";
                default:
                    return fourcc.ToUpperInvariant();
            }
        }

        #endregion

        #region FFmpeg Discovery

        private static string _cachedFFmpegPath = null;
        private static bool _ffmpegSearched = false;

        /// <summary>
        /// Find FFmpeg executable in PATH or common locations.
        /// Searches auto-download location, system PATH, common install dirs,
        /// package managers (Scoop, Chocolatey), and sibling project directories.
        /// </summary>
        private static string FindFFmpeg()
        {
            if (_ffmpegSearched) return _cachedFFmpegPath;
            _ffmpegSearched = true;

            var searchPaths = new List<string>
            {
                // Auto-downloaded FFmpeg (highest priority - known good)
                Path.Combine(Application.persistentDataPath, "ffmpeg", "ffmpeg.exe"),
                // System PATH
                "ffmpeg",
                // Common install locations
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                // Unity project locations
                Path.Combine(Application.streamingAssetsPath, "ffmpeg.exe"),
                Path.Combine(Application.dataPath, "..", "ffmpeg", "ffmpeg.exe"),
                // Chocolatey
                @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
            };

            // Scoop (user-specific)
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                searchPaths.Add(Path.Combine(userProfile, "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe"));
            }

            // Sibling RemotePlayServer project (development environment)
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                string remotePlayDir = Path.Combine(projectRoot, "RemotePlayServer", "bin");
                if (Directory.Exists(remotePlayDir))
                {
                    foreach (string config in new[] { "Debug", "Release" })
                    {
                        string configDir = Path.Combine(remotePlayDir, config);
                        if (Directory.Exists(configDir))
                        {
                            // Search in net* subdirectories (e.g. net9.0-windows10.0.26100.0)
                            foreach (string netDir in Directory.GetDirectories(configDir, "net*"))
                            {
                                string candidate = Path.Combine(netDir, "bin", "ffmpeg.exe");
                                searchPaths.Add(candidate);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors searching sibling projects
            }

            foreach (string path in searchPaths)
            {
                try
                {
                    // For file paths, check existence first to avoid slow process spawn
                    if (path != "ffmpeg" && !File.Exists(path))
                        continue;

                    var psi = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "-version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var process = Process.Start(psi))
                    {
                        process.WaitForExit(3000);
                        if (process.ExitCode == 0)
                        {
                            _cachedFFmpegPath = path;
                            Debug.Log($"[VRMediaAppController] Found FFmpeg at: {path}");
                            return path;
                        }
                    }
                }
                catch
                {
                    // Not found at this path, try next
                }
            }

            Debug.LogWarning("[VRMediaAppController] FFmpeg not found in PATH or common locations");
            return null;
        }

        /// <summary>
        /// Reset FFmpeg search cache so next FindFFmpeg() call re-searches.
        /// Call after auto-downloading FFmpeg.
        /// </summary>
        private static void ResetFFmpegCache()
        {
            _ffmpegSearched = false;
            _cachedFFmpegPath = null;
        }

        #endregion

        #region FFmpeg Download (Windows only)

    #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const string FFMPEG_DOWNLOAD_URL = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        private bool _isDownloadingFFmpeg = false;

        /// <summary>
        /// Download FFmpeg binary and save to persistentDataPath.
        /// Shows progress in the error dialog.
        /// </summary>
        private IEnumerator DownloadFFmpegCoroutine(System.Action<bool> onComplete)
        {
            if (_isDownloadingFFmpeg)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            _isDownloadingFFmpeg = true;

            string destDir = Path.Combine(Application.persistentDataPath, "ffmpeg");
            string destPath = Path.Combine(destDir, "ffmpeg.exe");

            // Already downloaded?
            if (File.Exists(destPath))
            {
                ResetFFmpegCache();
                _isDownloadingFFmpeg = false;
                onComplete?.Invoke(true);
                yield break;
            }

            _errorDialog?.UpdateProgress("Downloading FFmpeg...\nThis is a one-time download (~80 MB).");

            string zipPath = Path.Combine(Application.temporaryCachePath, "ffmpeg-download.zip");

            // Download
            using (var request = UnityWebRequest.Get(FFMPEG_DOWNLOAD_URL))
            {
                request.downloadHandler = new DownloadHandlerFile(zipPath) { removeFileOnAbort = true };
                var op = request.SendWebRequest();

                while (!op.isDone)
                {
                    float progress = request.downloadProgress;
                    if (progress >= 0)
                    {
                        int pct = Mathf.RoundToInt(progress * 100f);
                        _errorDialog?.UpdateProgress($"Downloading FFmpeg... {pct}%\nThis is a one-time download (~80 MB).");
                    }
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[VRMediaAppController] FFmpeg download failed: {request.error}");
                    _isDownloadingFFmpeg = false;
                    onComplete?.Invoke(false);
                    yield break;
                }
            }

            _errorDialog?.UpdateProgress("Extracting FFmpeg...");
            yield return null;

            // Extract ffmpeg.exe from zip in background thread
            bool extractSuccess = false;
            string extractError = null;
            bool extractDone = false;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    using (var archive = ZipFile.OpenRead(zipPath))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
                                && entry.Length > 0)
                            {
                                entry.ExtractToFile(destPath, overwrite: true);
                                extractSuccess = true;
                                break;
                            }
                        }
                    }

                    // Clean up zip
                    if (File.Exists(zipPath))
                        File.Delete(zipPath);
                }
                catch (Exception ex)
                {
                    extractError = ex.Message;
                }
                extractDone = true;
            });

            while (!extractDone) yield return null;

            _isDownloadingFFmpeg = false;

            if (extractSuccess)
            {
                Debug.Log($"[VRMediaAppController] FFmpeg downloaded to: {destPath}");
                ResetFFmpegCache();
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError($"[VRMediaAppController] FFmpeg extraction failed: {extractError}");
                onComplete?.Invoke(false);
            }
        }
    #endif

        #endregion
    }
}
