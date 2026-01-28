using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Data model cho video trong Media Library
/// </summary>
[Serializable]
public struct MediaVideoInfo
{
    #region Identity
    /// <summary>Full path to video file</summary>
    public string Path;

    /// <summary>Display title (filename without extension)</summary>
    public string Title;
    #endregion

    #region Video Properties
    /// <summary>Video duration</summary>
    public TimeSpan Duration;

    /// <summary>Detected projection type</summary>
    public VideoProjectionType Projection;

    /// <summary>Video file format</summary>
    public VideoFormat Format;

    /// <summary>Video width in pixels</summary>
    public int Width;

    /// <summary>Video height in pixels</summary>
    public int Height;

    /// <summary>Frame rate (fps)</summary>
    public float FrameRate;

    /// <summary>File size in bytes</summary>
    public long FileSizeBytes;
    #endregion

    #region Timestamps
    /// <summary>When file was added to library</summary>
    public DateTime DateAdded;

    /// <summary>File modification date</summary>
    public DateTime DateModified;

    /// <summary>Last time video was played</summary>
    public DateTime LastPlayed;
    #endregion

    #region User Data
    /// <summary>Is in favorites list</summary>
    public bool IsFavorite;

    /// <summary>Playlist IDs this video belongs to</summary>
    public List<string> PlaylistIds;

    /// <summary>Last playback position for resume</summary>
    public TimeSpan LastPosition;
    #endregion

    #region Cached
    /// <summary>Cached thumbnail texture</summary>
    [NonSerialized]
    public Texture2D Thumbnail;
    #endregion

    #region Computed Properties
    /// <summary>Get formatted duration string (HH:MM:SS or MM:SS)</summary>
    public string FormattedDuration
    {
        get
        {
            if (Duration.TotalHours >= 1)
                return $"{(int)Duration.TotalHours}:{Duration.Minutes:D2}:{Duration.Seconds:D2}";
            return $"{Duration.Minutes}:{Duration.Seconds:D2}";
        }
    }

    /// <summary>Get formatted file size (KB, MB, GB)</summary>
    public string FormattedSize
    {
        get
        {
            if (FileSizeBytes >= 1024L * 1024 * 1024)
                return $"{FileSizeBytes / (1024.0 * 1024 * 1024):F1} GB";
            if (FileSizeBytes >= 1024L * 1024)
                return $"{FileSizeBytes / (1024.0 * 1024):F1} MB";
            if (FileSizeBytes >= 1024)
                return $"{FileSizeBytes / 1024.0:F1} KB";
            return $"{FileSizeBytes} B";
        }
    }

    /// <summary>Get formatted resolution (Width x Height)</summary>
    public string FormattedResolution => $"{Width} x {Height}";

    /// <summary>Get aspect ratio</summary>
    public float AspectRatio => Height > 0 ? (float)Width / Height : 16f / 9f;

    /// <summary>Check if video has been played before</summary>
    public bool HasBeenPlayed => LastPlayed != default;

    /// <summary>Check if video can resume from last position</summary>
    public bool CanResume => LastPosition.TotalSeconds > 10 &&
                              LastPosition < Duration - TimeSpan.FromSeconds(30);

    /// <summary>Get projection badge text</summary>
    public string ProjectionBadge
    {
        get
        {
            switch (Projection)
            {
                case VideoProjectionType.Sphere360: return "360";
                case VideoProjectionType.Dome180: return "180";
                case VideoProjectionType.SideBySide3D:
                case VideoProjectionType.OverUnder3D: return "3D";
                case VideoProjectionType.VR180Stereo: return "VR";
                default: return null;
            }
        }
    }

    /// <summary>Get duration range category</summary>
    public DurationRange DurationCategory
    {
        get
        {
            if (Duration.TotalMinutes < 5) return DurationRange.Short;
            if (Duration.TotalMinutes <= 30) return DurationRange.Medium;
            return DurationRange.Long;
        }
    }
    #endregion

    #region Factory Methods
    /// <summary>
    /// Create MediaVideoInfo from file path
    /// </summary>
    public static MediaVideoInfo FromPath(string path)
    {
        var info = new MediaVideoInfo
        {
            Path = path,
            Title = System.IO.Path.GetFileNameWithoutExtension(path),
            Format = DetectFormat(path),
            PlaylistIds = new List<string>()
        };

        // Get file info from metadata
        try
        {
            var fileInfo = new FileInfo(path);
            info.FileSizeBytes = fileInfo.Length;
            info.DateModified = fileInfo.LastWriteTime;
            // Use file creation time as DateAdded (when file was added to system)
            info.DateAdded = fileInfo.CreationTime;
        }
        catch
        {
            info.DateAdded = DateTime.Now;
        }

        return info;
    }

    /// <summary>
    /// Detect video format from file extension
    /// </summary>
    public static VideoFormat DetectFormat(string path)
    {
        string ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
        switch (ext)
        {
            case ".mp4": return VideoFormat.MP4;
            case ".mkv": return VideoFormat.MKV;
            case ".avi": return VideoFormat.AVI;
            case ".webm": return VideoFormat.WebM;
            case ".mov": return VideoFormat.MOV;
            case ".wmv": return VideoFormat.WMV;
            default: return VideoFormat.Unknown;
        }
    }
    #endregion
}
