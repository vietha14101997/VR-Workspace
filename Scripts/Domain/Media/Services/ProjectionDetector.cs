using System.IO;
using UnityEngine;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.UI;

namespace VRWorkspace.Media.Utils
{
    /// <summary>
    /// Utility class to auto-detect video projection type from metadata, filename, and resolution.
    /// Detection priority: 1) File metadata (sv3d/st3d/XMP) 2) Filename patterns 3) Resolution heuristics
    /// </summary>
    public static class ProjectionDetector
    {
        /// <summary>
        /// Detect both projection and stereo mode in one pass (single file I/O).
        /// Uses metadata reading first, then falls back to filename/resolution.
        /// </summary>
        public static void DetectProjectionAndStereo(
            string filePath, int width, int height,
            out VideoProjectionType projection, out StereoMode stereo)
        {
            // 1. Try metadata (single file read)
            var metadata = VideoSphericalMetadataReader.ReadMetadata(filePath);

            if (metadata.HasMetadata)
            {
                projection = InterpretProjection(metadata, width, height);
                stereo = InterpretStereo(metadata);

                Debug.Log($"[ProjectionDetector] Metadata detected ({metadata.Source}): " +
                    $"projection={projection}, stereo={stereo}, " +
                    $"projType={metadata.ProjectionType}, fullSphere={metadata.IsFullSphere}, " +
                    $"metaStereo={metadata.StereoMode}, res={width}x{height}");
                return;
            }

            // 2. Fall back to filename + resolution
            projection = DetectProjectionFromFilenameAndResolution(filePath, width, height);
            stereo = DetectStereoModeFromFilename(projection, filePath);
            Debug.Log($"[ProjectionDetector] Filename/resolution fallback: {projection}, {stereo}, res={width}x{height}");
        }

        /// <summary>
        /// Detect projection type from filename patterns and resolution.
        /// Also attempts metadata reading as highest priority.
        /// </summary>
        public static VideoProjectionType DetectProjection(string filename, int width, int height)
        {
            // Try metadata first
            var metadata = VideoSphericalMetadataReader.ReadMetadata(filename);
            if (metadata.HasMetadata)
            {
                return InterpretProjection(metadata, width, height);
            }

            return DetectProjectionFromFilenameAndResolution(filename, width, height);
        }

        /// <summary>
        /// Detect stereo mode from filename and video properties.
        /// Also attempts metadata reading as highest priority.
        /// </summary>
        public static StereoMode DetectStereoMode(VideoProjectionType projection, string filename)
        {
            // Try metadata first (cached from previous call)
            var metadata = VideoSphericalMetadataReader.ReadMetadata(filename);
            if (metadata.HasMetadata && metadata.StereoMode >= 0)
            {
                return InterpretStereo(metadata);
            }

            return DetectStereoModeFromFilename(projection, filename);
        }

        #region Metadata Interpretation

        public static VideoProjectionType InterpretProjection(SphericalVideoMetadata meta, int width, int height)
        {
            if (!meta.HasMetadata) return VideoProjectionType.Flat;

            if (meta.ProjectionType == "equirectangular")
            {
                if (!meta.IsFullSphere)
                    return VideoProjectionType.Dome180;

                // Full sphere equirectangular, but check for 180 SBS cases.
                // Common formats:
                //   180 SBS: 2:1 total (each eye 1:1, e.g. 3840x1920)
                //   180 SBS: 1:1 total (less common, e.g. 1920x1920 per eye stacked)
                //   360 Mono: 2:1 total (e.g. 3840x1920)
                //   360 SBS:  1:1 total (each eye 2:1, combined = 1:1)
                // Key rule: 2:1 + SBS metadata = 180 SBS (not 360 SBS, which would be 1:1)
                if (width > 0 && height > 0 && meta.StereoMode == 2)
                {
                    float ratio = (float)width / height;
                    // 2:1 ratio + SBS = 180 SBS (most common VR180 format)
                    if (ratio >= 1.9f && ratio <= 2.1f)
                        return VideoProjectionType.Dome180;
                    // 1:1 ratio + SBS = also 180 SBS (less common)
                    if (ratio >= 0.9f && ratio <= 1.1f)
                        return VideoProjectionType.Dome180;
                }

                // XMP: check FullPanoWidth vs CroppedAreaImageWidth
                if (meta.FullPanoWidthPixels > 0 && meta.CroppedAreaImageWidthPixels > 0)
                {
                    float cropRatio = (float)meta.CroppedAreaImageWidthPixels / meta.FullPanoWidthPixels;
                    if (cropRatio <= 0.75f) // Cropped area is less than 75% of full pano = 180
                        return VideoProjectionType.Dome180;
                }

                return VideoProjectionType.Sphere360;
            }

            if (meta.ProjectionType == "cubemap")
                return VideoProjectionType.Sphere360;

            return VideoProjectionType.Flat;
        }

        public static StereoMode InterpretStereo(SphericalVideoMetadata meta)
        {
            if (!meta.HasMetadata || meta.StereoMode < 0) return StereoMode.Mono;

            switch (meta.StereoMode)
            {
                case 0: return StereoMode.Mono;
                case 1: return StereoMode.OverUnder;
                case 2: return StereoMode.SideBySide;
                default: return StereoMode.Mono;
            }
        }

        #endregion

        #region Filename & Resolution Detection (existing logic)

        private static VideoProjectionType DetectProjectionFromFilenameAndResolution(string filename, int width, int height)
        {
            string name = StripDownloadPrefixes(Path.GetFileNameWithoutExtension(filename)?.ToLowerInvariant() ?? "");

            // 1. Check filename patterns for immersive projections

            // 360 patterns
            if (ContainsAny(name, "_360", "360x180", "_360_", "360vr", "360degree"))
                return VideoProjectionType.Sphere360;

            // 180 patterns (including VR180)
            if (ContainsAny(name, "_180", "180x180", "_180_", "180vr", "180degree", "vr180", "_vr180"))
                return VideoProjectionType.Dome180;

            // 2. Use resolution heuristics (combined with filename SBS hints)
            if (width > 0 && height > 0)
            {
                float ratio = (float)width / height;
                bool hasSbsHint = ContainsAny(name, "_sbs", "-sbs", ".sbs", "side_by_side", "sidebyside", "_lr", "_leftright");
                bool hasOuHint = ContainsAny(name, "_ou", "-ou", ".ou", "_tb", "-tb", "topbottom", "overunder", "over_under", "top_bottom");

                // 2:1 ratio + SBS hint = 180 SBS (each eye 1:1, total 2:1)
                // Without SBS hint, 2:1 = 360 equirectangular mono
                if (ratio >= 1.9f && ratio <= 2.1f)
                {
                    if (hasSbsHint)
                        return VideoProjectionType.Dome180;
                    if (width >= 1920)
                        return VideoProjectionType.Sphere360;
                }

                // 1:1 ratio is typical for 180 equirectangular mono
                // or 360 SBS (each eye 2:1, combined 1:1) — but without metadata we default to 180
                if (ratio >= 0.9f && ratio <= 1.1f)
                {
                    return VideoProjectionType.Dome180;
                }
            }

            // 3. Default to flat screen
            return VideoProjectionType.Flat;
        }

        private static StereoMode DetectStereoModeFromFilename(VideoProjectionType projection, string filename)
        {
            string name = StripDownloadPrefixes(Path.GetFileNameWithoutExtension(filename)?.ToLowerInvariant() ?? "");

            // VR180 is almost always SBS
            if (projection == VideoProjectionType.Dome180 && (name.Contains("vr180") || name.Contains("_180_sbs") || name.Contains("_180sbs")))
                return StereoMode.SideBySide;

            // Explicit patterns - use delimiter-prefixed patterns to avoid false positives
            // (e.g. "sbs" could match words, "ou" matches "YouTube", "about", etc.)
            if (ContainsAny(name, "_sbs", "-sbs", ".sbs", "side_by_side", "sidebyside", "_lr", "_leftright"))
                return StereoMode.SideBySide;

            if (ContainsAny(name, "_ou", "-ou", ".ou", "_tb", "-tb", "topbottom", "overunder", "over_under", "top_bottom"))
                return StereoMode.OverUnder;

            return StereoMode.Mono;
        }

        #endregion

        #region Display Helpers

        /// <summary>
        /// Get user-friendly name for projection type
        /// </summary>
        public static string GetProjectionDisplayName(VideoProjectionType projection)
        {
            switch (projection)
            {
                case VideoProjectionType.Flat: return "2D Flat";
                case VideoProjectionType.Dome180: return "180° VR";
                case VideoProjectionType.Sphere360: return "360° VR";
                case VideoProjectionType.SideBySide3D: return "3D SBS";
                case VideoProjectionType.OverUnder3D: return "3D OU";
                case VideoProjectionType.VR180Stereo: return "VR180 3D";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Get short badge text for projection type (for display on thumbnails)
        /// </summary>
        public static string GetProjectionBadge(VideoProjectionType projection)
        {
            switch (projection)
            {
                case VideoProjectionType.Sphere360: return "360";
                case VideoProjectionType.Dome180: return "180";
                case VideoProjectionType.SideBySide3D: return "3D";
                case VideoProjectionType.OverUnder3D: return "3D";
                case VideoProjectionType.VR180Stereo: return "VR";
                default: return null;  // No badge for flat videos
            }
        }

        /// <summary>
        /// Get badge color for projection type
        /// </summary>
        public static Color GetProjectionBadgeColor(VideoProjectionType projection)
        {
            switch (projection)
            {
                case VideoProjectionType.Sphere360:
                    return new Color(1f, 0.5f, 0f);         // Orange
                case VideoProjectionType.Dome180:
                    return new Color(0.6f, 0.2f, 1f);       // Purple
                case VideoProjectionType.SideBySide3D:
                case VideoProjectionType.OverUnder3D:
                    return new Color(0f, 0.9f, 1f);         // Cyan
                case VideoProjectionType.VR180Stereo:
                    return new Color(0.3f, 1f, 0.5f);       // Green
                default:
                    return Color.white;
            }
        }

        /// <summary>
        /// Check if projection type requires environment to be hidden
        /// </summary>
        public static bool ShouldHideEnvironment(VideoProjectionType projection)
        {
            switch (projection)
            {
                case VideoProjectionType.Dome180:
                case VideoProjectionType.Sphere360:
                case VideoProjectionType.VR180Stereo:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Check if projection type supports screen distance/size adjustment
        /// </summary>
        public static bool SupportsScreenSettings(VideoProjectionType projection)
        {
            switch (projection)
            {
                case VideoProjectionType.Flat:
                case VideoProjectionType.SideBySide3D:
                case VideoProjectionType.OverUnder3D:
                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region Private Helpers
        private static bool ContainsAny(string text, params string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                if (text.Contains(pattern))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Strip common download site prefixes from filenames to avoid false pattern matches.
        /// e.g. "ytdown.com_youtube_MyVideo" → "MyVideo"
        /// </summary>
        private static string StripDownloadPrefixes(string name)
        {
            string[] prefixes = {
                "ytdown.com_youtube_", "ytdown.com_",
                "fdownloader.net_", "savefrom.net_"
            };
            foreach (var prefix in prefixes)
            {
                if (name.StartsWith(prefix))
                    return name.Substring(prefix.Length);
            }
            return name;
        }
        #endregion
    }

}
