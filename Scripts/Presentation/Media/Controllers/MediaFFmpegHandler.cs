using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Debug = UnityEngine.Debug;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.UI;

namespace VRWorkspace.Presentation.Media.Controllers
{
    /// <summary>
    /// Handles all FFmpeg-related concerns:
    ///   - Discovery / path caching (<see cref="FindFFmpeg"/>)
    ///   - Auto-download on Windows (<see cref="DownloadFFmpegCoroutine"/>)
    ///   - Video remux to MP4 (<see cref="RemuxAndPlayCoroutine"/>)
    ///   - Friendly codec-name lookup (<see cref="GetFriendlyCodecName"/>)
    ///   - Error dialog interactions for codec/container failures
    ///
    /// Plain C# class. Requires a MonoBehaviour owner to run coroutines.
    /// </summary>
    public class MediaFFmpegHandler
    {
        // ------------------------------------------------------------------ //
        //  FFmpeg path cache (process-wide singleton state)                    //
        // ------------------------------------------------------------------ //

        private static string _cachedFFmpegPath;
        private static bool _ffmpegSearched;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const string FFMPEG_DOWNLOAD_URL =
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        private bool _isDownloadingFFmpeg;
#endif

        // ------------------------------------------------------------------ //
        //  Dependencies                                                         //
        // ------------------------------------------------------------------ //

        private readonly MonoBehaviour _runner;
        private readonly IFFmpegHandlerContext _ctx;

        // ------------------------------------------------------------------ //
        //  Constructor                                                          //
        // ------------------------------------------------------------------ //

        public MediaFFmpegHandler(MonoBehaviour runner, IFFmpegHandlerContext context)
        {
            _runner = runner;
            _ctx = context;
        }

        // ------------------------------------------------------------------ //
        //  Public API                                                           //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// React to a playback failure.  Decides whether to auto-remux, show an
        /// error dialog with Download option, or fall back to "open in player".
        /// </summary>
        public void HandlePlaybackFailed(
            string error,
            bool isCodecError,
            string codecName,
            string containerFormat)
        {
            var errorDialog = _ctx.ErrorDialog;
            if (errorDialog == null)
            {
                _ctx.SwitchToLibrary();
                return;
            }

            _ctx.LastFailedVideoPath = _ctx.PlayerController?.CurrentVideo?.Path;
            _ctx.LastFailedContainerFormat = containerFormat;

            bool needsRemux = !string.IsNullOrEmpty(containerFormat) || isCodecError;

            if (needsRemux)
            {
                bool canRemux = !string.IsNullOrEmpty(FindFFmpeg());

                if (canRemux && !string.IsNullOrEmpty(_ctx.LastFailedVideoPath))
                {
                    Debug.Log($"[MediaFFmpegHandler] Auto-remuxing: container={containerFormat ?? "MP4"}, codec={codecName ?? "unknown"}");
                    _runner.StartCoroutine(RemuxAndPlayCoroutine(_ctx.LastFailedVideoPath));
                    return;
                }

                string formatInfo = !string.IsNullOrEmpty(containerFormat)
                    ? $"Format: {containerFormat}"
                    : $"Codec: {GetFriendlyCodecName(codecName ?? "unknown")}";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                errorDialog.ShowError(
                    !string.IsNullOrEmpty(containerFormat)
                        ? MediaErrorDialog.ErrorType.UnsupportedContainer
                        : MediaErrorDialog.ErrorType.CodecNotSupported,
                    $"{formatInfo}\n\nFFmpeg is needed to convert this video.\nIt will be downloaded automatically (~80 MB).");
                errorDialog.SetRetryLabel("Download & Convert");
#else
                errorDialog.ShowError(
                    !string.IsNullOrEmpty(containerFormat)
                        ? MediaErrorDialog.ErrorType.UnsupportedContainer
                        : MediaErrorDialog.ErrorType.CodecNotSupported,
                    formatInfo);
#endif
            }
            else
            {
                errorDialog.Show("Playback Error", error, showRetry: false, showOpenExternal: true);
            }
        }

        /// <summary>
        /// Called when the user presses Retry / "Download and Convert" in the error dialog.
        /// </summary>
        public void HandleRetryOrConvert()
        {
            if (string.IsNullOrEmpty(_ctx.LastFailedVideoPath))
            {
                _ctx.ErrorDialog?.Hide();
                _ctx.SwitchToLibrary();
                return;
            }

            if (!string.IsNullOrEmpty(FindFFmpeg()))
            {
                _ctx.ErrorDialog?.Hide();
                _runner.StartCoroutine(RemuxAndPlayCoroutine(_ctx.LastFailedVideoPath));
                return;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _runner.StartCoroutine(DownloadAndConvertCoroutine(_ctx.LastFailedVideoPath));
#else
            _ctx.ErrorDialog?.Hide();
            _ctx.SwitchToLibrary();
#endif
        }

        /// <summary>
        /// Open the failed video in the OS default media player.
        /// </summary>
        public void HandleOpenInExternalPlayer()
        {
            _ctx.ErrorDialog?.Hide();

            if (!string.IsNullOrEmpty(_ctx.LastFailedVideoPath))
            {
                Debug.Log($"[MediaFFmpegHandler] Opening in system player: {_ctx.LastFailedVideoPath}");
                try
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = _ctx.LastFailedVideoPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MediaFFmpegHandler] Failed to open external player: {ex.Message}");
                }
            }

            _ctx.SwitchToLibrary();
        }

        // ------------------------------------------------------------------ //
        //  FFmpeg discovery                                                     //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Find FFmpeg executable in PATH or common locations.
        /// Caches the result for subsequent calls; call <see cref="ResetFFmpegCache"/>
        /// after a fresh download to force re-discovery.
        /// </summary>
        public static string FindFFmpeg()
        {
            if (_ffmpegSearched) return _cachedFFmpegPath;
            _ffmpegSearched = true;

            var searchPaths = new System.Collections.Generic.List<string>
            {
                Path.Combine(Application.persistentDataPath, "ffmpeg", "ffmpeg.exe"),
                "ffmpeg",
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(Application.streamingAssetsPath, "ffmpeg.exe"),
                Path.Combine(Application.dataPath, "..", "ffmpeg", "ffmpeg.exe"),
                @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
            };

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                searchPaths.Add(Path.Combine(
                    userProfile, "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe"));
            }

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                string remotePlayDir = Path.Combine(projectRoot, "RemotePlayServer", "bin");
                if (Directory.Exists(remotePlayDir))
                {
                    foreach (string config in new[] { "Debug", "Release" })
                    {
                        string configDir = Path.Combine(remotePlayDir, config);
                        if (!Directory.Exists(configDir)) continue;
                        foreach (string netDir in Directory.GetDirectories(configDir, "net*"))
                        {
                            searchPaths.Add(Path.Combine(netDir, "bin", "ffmpeg.exe"));
                        }
                    }
                }
            }
            catch { /* ignore sibling-project search errors */ }

            foreach (string path in searchPaths)
            {
                try
                {
                    if (path != "ffmpeg" && !File.Exists(path)) continue;

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
                            Debug.Log($"[MediaFFmpegHandler] Found FFmpeg at: {path}");
                            return path;
                        }
                    }
                }
                catch { /* try next */ }
            }

            Debug.LogWarning("[MediaFFmpegHandler] FFmpeg not found in PATH or common locations");
            return null;
        }

        /// <summary>
        /// Reset FFmpeg search cache so the next <see cref="FindFFmpeg"/> call re-searches.
        /// Call after auto-downloading FFmpeg.
        /// </summary>
        public static void ResetFFmpegCache()
        {
            _ffmpegSearched = false;
            _cachedFFmpegPath = null;
        }

        // ------------------------------------------------------------------ //
        //  Codec name helper                                                    //
        // ------------------------------------------------------------------ //

        public static string GetFriendlyCodecName(string fourcc)
        {
            if (string.IsNullOrEmpty(fourcc)) return "Unknown";
            switch (fourcc.ToLowerInvariant())
            {
                case "hev1":
                case "hvc1":  return "HEVC (H.265)";
                case "av01":  return "AV1";
                case "avc1":
                case "avc3":  return "H.264";
                case "vp09":  return "VP9";
                default:      return fourcc.ToUpperInvariant();
            }
        }

        // ------------------------------------------------------------------ //
        //  Remux coroutine                                                      //
        // ------------------------------------------------------------------ //

        private IEnumerator RemuxAndPlayCoroutine(string sourcePath)
        {
            string ffmpegPath = FindFFmpeg();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                Debug.LogError("[MediaFFmpegHandler] FFmpeg not found");
                _ctx.SwitchToLibrary();
                yield break;
            }

            string dir = Path.GetDirectoryName(sourcePath);
            string nameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
            string outputPath = Path.Combine(dir, $"{nameNoExt}.remuxed.mp4");

            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 1024)
            {
                Debug.Log($"[MediaFFmpegHandler] Using existing remuxed file: {outputPath}");
                PlayRemuxedVideo(outputPath);
                yield break;
            }

            Debug.Log($"[MediaFFmpegHandler] Starting FFmpeg remux: {sourcePath} -> {outputPath}");

            _ctx.ErrorDialog?.Show(
                "Converting...",
                "Remuxing video to MP4 format.\nThis should be fast (no re-encoding).",
                showRetry: false, showOpenExternal: false);

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
                        process.WaitForExit(120000);

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

            float remuxStart = Time.time;
            while (!processComplete)
            {
                if (Time.time - remuxStart > 130f)
                {
                    processError = "Remux timeout";
                    break;
                }
                yield return null;
            }

            if (processSuccess)
            {
                Debug.Log($"[MediaFFmpegHandler] Remux complete in {Time.time - remuxStart:F1}s: {outputPath}");
                _ctx.ErrorDialog?.Hide();
                PlayRemuxedVideo(outputPath);
            }
            else
            {
                Debug.LogError($"[MediaFFmpegHandler] Remux failed: {processError}");
                _ctx.ErrorDialog?.Show(
                    "Conversion Failed",
                    $"FFmpeg could not convert this video.\n\n{processError}",
                    showRetry: false, showOpenExternal: true);
            }
        }

        private void PlayRemuxedVideo(string remuxedPath)
        {
            var playerController = _ctx.PlayerController;
            if (playerController == null)
            {
                Debug.LogError("[MediaFFmpegHandler] PlayerController is null, cannot play remuxed video");
                _ctx.SwitchToLibrary();
                return;
            }

            var video = playerController.CurrentVideo;
            if (video.HasValue)
            {
                var remuxedVideo = video.Value;
                remuxedVideo.Path = remuxedPath;
                Debug.Log($"[MediaFFmpegHandler] Playing remuxed video: {remuxedPath}");
                playerController.PlayVideo(remuxedVideo);
            }
            else
            {
                var remuxedVideo = MediaVideoInfo.FromPath(remuxedPath);
                playerController.PlayVideo(remuxedVideo);
            }
        }

        // ------------------------------------------------------------------ //
        //  Windows-only FFmpeg download                                         //
        // ------------------------------------------------------------------ //

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        private IEnumerator DownloadAndConvertCoroutine(string videoPath)
        {
            _ctx.ErrorDialog?.Show(
                "Preparing...",
                "Setting up video converter...",
                showRetry: false, showOpenExternal: false);

            bool downloadSuccess = false;
            yield return _runner.StartCoroutine(DownloadFFmpegCoroutine(success => downloadSuccess = success));

            if (downloadSuccess && !string.IsNullOrEmpty(FindFFmpeg()))
            {
                _ctx.ErrorDialog?.Hide();
                _runner.StartCoroutine(RemuxAndPlayCoroutine(videoPath));
            }
            else
            {
                _ctx.ErrorDialog?.Show(
                    "Download Failed",
                    "Could not download FFmpeg.\nCheck your internet connection and try again.",
                    showRetry: true, showOpenExternal: true);
                _ctx.ErrorDialog?.SetRetryLabel("Retry Download");
            }
        }

        private IEnumerator DownloadFFmpegCoroutine(Action<bool> onComplete)
        {
            if (_isDownloadingFFmpeg)
            {
                onComplete?.Invoke(false);
                yield break;
            }
            _isDownloadingFFmpeg = true;

            string destDir = Path.Combine(Application.persistentDataPath, "ffmpeg");
            string destPath = Path.Combine(destDir, "ffmpeg.exe");

            if (File.Exists(destPath))
            {
                ResetFFmpegCache();
                _isDownloadingFFmpeg = false;
                onComplete?.Invoke(true);
                yield break;
            }

            _ctx.ErrorDialog?.UpdateProgress("Downloading FFmpeg...\nThis is a one-time download (~80 MB).");

            string zipPath = Path.Combine(Application.temporaryCachePath, "ffmpeg-download.zip");

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
                        _ctx.ErrorDialog?.UpdateProgress(
                            $"Downloading FFmpeg... {pct}%\nThis is a one-time download (~80 MB).");
                    }
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[MediaFFmpegHandler] FFmpeg download failed: {request.error}");
                    _isDownloadingFFmpeg = false;
                    onComplete?.Invoke(false);
                    yield break;
                }
            }

            _ctx.ErrorDialog?.UpdateProgress("Extracting FFmpeg...");
            yield return null;

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
                Debug.Log($"[MediaFFmpegHandler] FFmpeg downloaded to: {destPath}");
                ResetFFmpegCache();
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError($"[MediaFFmpegHandler] FFmpeg extraction failed: {extractError}");
                onComplete?.Invoke(false);
            }
        }

#endif // UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    }

    // ---------------------------------------------------------------------- //
    //  Context interface                                                        //
    // ---------------------------------------------------------------------- //

    /// <summary>
    /// State and callbacks that <see cref="MediaFFmpegHandler"/> needs from the owning controller.
    /// </summary>
    public interface IFFmpegHandlerContext
    {
        MediaErrorDialog ErrorDialog { get; }
        VRWorkspace.Media.Core.VRVideoPlayerController PlayerController { get; }
        string LastFailedVideoPath { get; set; }
        string LastFailedContainerFormat { get; set; }
        void SwitchToLibrary();
    }
}
