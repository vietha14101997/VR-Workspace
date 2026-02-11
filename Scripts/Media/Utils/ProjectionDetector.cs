using System.IO;
using UnityEngine;

/// <summary>
/// Utility class to auto-detect video projection type from filename and resolution
/// </summary>
public static class ProjectionDetector
{
    /// <summary>
    /// Detect projection type from filename patterns and resolution
    /// </summary>
    /// <param name="filename">Video filename (with or without path)</param>
    /// <param name="width">Video width in pixels</param>
    /// <param name="height">Video height in pixels</param>
    /// <returns>Detected projection type</returns>
    public static VideoProjectionType DetectProjection(string filename, int width, int height)
    {
        string name = Path.GetFileNameWithoutExtension(filename)?.ToLowerInvariant() ?? "";

        // 1. Check filename patterns for immersive projections

        // 360 patterns
        if (ContainsAny(name, "_360", "360x180", "_360_", "360vr", "360degree"))
            return VideoProjectionType.Sphere360;

        // 180 patterns (including VR180)
        if (ContainsAny(name, "_180", "180x180", "_180_", "180vr", "180degree", "vr180", "_vr180"))
            return VideoProjectionType.Dome180;

        // 2. Use resolution heuristics
        if (width > 0 && height > 0)
        {
            float ratio = (float)width / height;

            // 2:1 ratio is typical for 360 equirectangular
            if (Mathf.Approximately(ratio, 2f) || (ratio >= 1.9f && ratio <= 2.1f))
            {
                if (width >= 3840) // 4K+ is likely 360
                    return VideoProjectionType.Sphere360;
            }

            // 1:1 ratio is typical for 180 equirectangular (Mono) or 180 Stereo (if 2:1 each eye? No, usually 180 SBS is 2:1 total)
            if (Mathf.Approximately(ratio, 1f) || (ratio >= 0.9f && ratio <= 1.1f))
            {
                return VideoProjectionType.Dome180;
            }
        }

        // 3. Default to flat screen (SBS/OU Flat will also reach here)
        return VideoProjectionType.Flat;
    }

    /// <summary>
    /// Detect stereo mode from filename and video properties
    /// </summary>
    public static StereoMode DetectStereoMode(VideoProjectionType projection, string filename)
    {
        string name = Path.GetFileNameWithoutExtension(filename)?.ToLowerInvariant() ?? "";

        // VR180 is almost always SBS
        if (projection == VideoProjectionType.Dome180 && (name.Contains("vr180") || name.Contains("_180_sbs") || name.Contains("_180sbs")))
            return StereoMode.SideBySide;

        // Explicit patterns
        if (ContainsAny(name, "sbs", "side_by_side", "sidebyside", "_lr", "_leftright"))
            return StereoMode.SideBySide;

        if (ContainsAny(name, "ou", "topbottom", "overunder", "over_under", "_tb"))
            return StereoMode.OverUnder;

        // Logic check: if it's 360 and has 2:1 ratio, it might be mono.
        // If it's 360 and has 1:1 ratio, it's likely OU/TB... 
        // But naming is more reliable in VR.

        return StereoMode.Mono;
    }

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
    #endregion
}
