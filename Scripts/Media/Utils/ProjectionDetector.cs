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

        // 1. Check filename patterns first (most reliable)

        // VR180 patterns (must check before 180)
        if (ContainsAny(name, "_vr180", "_180_3d", "_180_sbs", "_180sbs", "vr180"))
            return VideoProjectionType.VR180Stereo;

        // 360 patterns
        if (ContainsAny(name, "_360", "360x180", "_360_", "360vr", "360degree"))
            return VideoProjectionType.Sphere360;

        // 180 patterns
        if (ContainsAny(name, "_180", "180x180", "_180_", "180vr", "180degree"))
            return VideoProjectionType.Dome180;

        // SBS 3D patterns
        if (ContainsAny(name, "_sbs", "_3d_sbs", "_3dsbs", "sbs3d", "side_by_side", "sidebyside", "_lr", "_leftright"))
            return VideoProjectionType.SideBySide3D;

        // Over-Under 3D patterns
        if (ContainsAny(name, "_tb", "_ou", "_3d_ou", "_3dou", "_topbottom", "_overunder", "over_under"))
            return VideoProjectionType.OverUnder3D;

        // 2. Use resolution heuristics if filename doesn't match

        if (width > 0 && height > 0)
        {
            float ratio = (float)width / height;

            // 2:1 ratio is typical for 360 equirectangular
            if (Mathf.Approximately(ratio, 2f) || (ratio >= 1.9f && ratio <= 2.1f))
            {
                // Could be 360 or SBS flat - check for common 360 resolutions
                if (width >= 3840) // 4K+ is likely 360
                    return VideoProjectionType.Sphere360;
            }

            // 1:1 ratio could be 180 equirectangular
            if (Mathf.Approximately(ratio, 1f) || (ratio >= 0.9f && ratio <= 1.1f))
            {
                return VideoProjectionType.Dome180;
            }

            // 4:1 ratio is SBS 360
            if (ratio >= 3.8f && ratio <= 4.2f)
            {
                return VideoProjectionType.VR180Stereo;
            }
        }

        // 3. Default to flat screen
        return VideoProjectionType.Flat;
    }

    /// <summary>
    /// Detect stereo mode from projection type and filename
    /// </summary>
    public static StereoMode DetectStereoMode(VideoProjectionType projection, string filename)
    {
        switch (projection)
        {
            case VideoProjectionType.SideBySide3D:
            case VideoProjectionType.OverUnder3D:
            case VideoProjectionType.VR180Stereo:
                return StereoMode.Stereo;

            default:
                return StereoMode.Mono;
        }
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
