using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VRWorkspace.Media.Data
{
    /// <summary>
    /// Data model for images in Media Library
    /// </summary>
    [Serializable]
    public struct MediaImageInfo
    {
        #region Identity
        /// <summary>Full path to image file</summary>
        public string Path;

        /// <summary>Display title (filename without extension)</summary>
        public string Title;
        #endregion

        #region Image Properties
        /// <summary>Image width in pixels</summary>
        public int Width;

        /// <summary>Image height in pixels</summary>
        public int Height;

        /// <summary>Image file format</summary>
        public ImageFormat Format;

        /// <summary>File size in bytes</summary>
        public long FileSizeBytes;

        /// <summary>Is this a 360° panoramic image</summary>
        public bool Is360Panorama;

        /// <summary>Is this a 180° panoramic image</summary>
        public bool Is180Panorama;

        /// <summary>Is this a stereoscopic 3D image (SBS or OU)</summary>
        public bool IsStereo;

        /// <summary>Stereo layout type (if IsStereo is true)</summary>
        public StereoLayout StereoType;
        #endregion

        #region EXIF Metadata
        /// <summary>When the photo was taken</summary>
        public DateTime DateTaken;

        /// <summary>Camera make/manufacturer</summary>
        public string CameraMake;

        /// <summary>Camera model</summary>
        public string CameraModel;

        /// <summary>Focal length in mm</summary>
        public float FocalLength;

        /// <summary>Aperture (f-number)</summary>
        public float Aperture;

        /// <summary>ISO sensitivity</summary>
        public int ISO;

        /// <summary>Exposure time in seconds</summary>
        public float ExposureTime;

        /// <summary>GPS latitude</summary>
        public double? Latitude;

        /// <summary>GPS longitude</summary>
        public double? Longitude;
        #endregion

        #region Timestamps
        /// <summary>When file was added to library</summary>
        public DateTime DateAdded;

        /// <summary>File modification date</summary>
        public DateTime DateModified;

        /// <summary>Last time image was viewed</summary>
        public DateTime LastViewed;
        #endregion

        #region User Data
        /// <summary>Is in favorites list</summary>
        public bool IsFavorite;

        /// <summary>Album/Playlist IDs this image belongs to</summary>
        public List<string> AlbumIds;
        #endregion

        #region Cached
        /// <summary>Cached thumbnail texture</summary>
        [NonSerialized]
        public Texture2D Thumbnail;

        /// <summary>Full resolution texture (loaded on demand)</summary>
        [NonSerialized]
        public Texture2D FullTexture;
        #endregion

        #region Computed Properties
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

        /// <summary>Get megapixels</summary>
        public float Megapixels => (Width * Height) / 1000000f;

        /// <summary>Get formatted megapixels</summary>
        public string FormattedMegapixels => $"{Megapixels:F1} MP";

        /// <summary>Get aspect ratio</summary>
        public float AspectRatio => Height > 0 ? (float)Width / Height : 1f;

        /// <summary>Check if image has been viewed before</summary>
        public bool HasBeenViewed => LastViewed != default;

        /// <summary>Check if image has GPS location</summary>
        public bool HasLocation => Latitude.HasValue && Longitude.HasValue;

        /// <summary>Get projection type for VR display</summary>
        public ImageProjectionType ProjectionType
        {
            get
            {
                if (Is360Panorama)
                    return IsStereo ? ImageProjectionType.Sphere360Stereo : ImageProjectionType.Sphere360;
                if (Is180Panorama)
                    return IsStereo ? ImageProjectionType.Dome180Stereo : ImageProjectionType.Dome180;
                if (IsStereo)
                    return StereoType == StereoLayout.SideBySide 
                        ? ImageProjectionType.FlatStereoSBS 
                        : ImageProjectionType.FlatStereoOU;
                return ImageProjectionType.Flat;
            }
        }

        /// <summary>Get camera info string</summary>
        public string CameraInfo
        {
            get
            {
                if (string.IsNullOrEmpty(CameraMake) && string.IsNullOrEmpty(CameraModel))
                    return null;
                if (string.IsNullOrEmpty(CameraMake))
                    return CameraModel;
                if (string.IsNullOrEmpty(CameraModel))
                    return CameraMake;
                return $"{CameraMake} {CameraModel}";
            }
        }

        /// <summary>Get formatted exposure info</summary>
        public string ExposureInfo
        {
            get
            {
                var parts = new List<string>();
                if (Aperture > 0) parts.Add($"f/{Aperture:F1}");
                if (ExposureTime > 0)
                {
                    if (ExposureTime >= 1)
                        parts.Add($"{ExposureTime:F1}s");
                    else
                        parts.Add($"1/{(int)(1 / ExposureTime)}s");
                }
                if (ISO > 0) parts.Add($"ISO {ISO}");
                if (FocalLength > 0) parts.Add($"{FocalLength:F0}mm");
                return parts.Count > 0 ? string.Join("  ", parts) : null;
            }
        }
        #endregion

        #region Factory Methods
        /// <summary>
        /// Create MediaImageInfo from file path
        /// </summary>
        public static MediaImageInfo FromPath(string path)
        {
            var info = new MediaImageInfo
            {
                Path = path,
                Title = System.IO.Path.GetFileNameWithoutExtension(path),
                Format = DetectFormat(path),
                AlbumIds = new List<string>()
            };

            // Detect panorama/stereo from filename
            string fileName = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            info.Is360Panorama = fileName.Contains("360") || fileName.Contains("equirect");
            info.Is180Panorama = fileName.Contains("180") || fileName.Contains("vr180");
            info.IsStereo = fileName.Contains("sbs") || fileName.Contains("ou") || 
                            fileName.Contains("3d") || fileName.Contains("stereo");
            if (info.IsStereo)
            {
                info.StereoType = fileName.Contains("ou") || fileName.Contains("tb") 
                    ? StereoLayout.OverUnder 
                    : StereoLayout.SideBySide;
            }

            // Get file info
            try
            {
                var fileInfo = new FileInfo(path);
                info.FileSizeBytes = fileInfo.Length;
                info.DateModified = fileInfo.LastWriteTime;
                info.DateAdded = fileInfo.CreationTime;
            }
            catch
            {
                info.DateAdded = DateTime.Now;
            }

            return info;
        }

        /// <summary>
        /// Detect image format from file extension
        /// </summary>
        public static ImageFormat DetectFormat(string path)
        {
            string ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => ImageFormat.JPEG,
                ".png" => ImageFormat.PNG,
                ".webp" => ImageFormat.WebP,
                ".heic" or ".heif" => ImageFormat.HEIC,
                ".gif" => ImageFormat.GIF,
                ".bmp" => ImageFormat.BMP,
                ".tiff" or ".tif" => ImageFormat.TIFF,
                ".raw" or ".cr2" or ".nef" or ".arw" or ".dng" => ImageFormat.RAW,
                _ => ImageFormat.Unknown
            };
        }
        #endregion
    }

    /// <summary>
    /// Image file format
    /// </summary>
    public enum ImageFormat
    {
        JPEG,
        PNG,
        WebP,
        HEIC,
        GIF,
        BMP,
        TIFF,
        RAW,
        Unknown
    }

    /// <summary>
    /// Stereo image layout
    /// </summary>
    public enum StereoLayout
    {
        None,
        SideBySide,  // Left-Right
        OverUnder    // Top-Bottom
    }

    /// <summary>
    /// Image projection type for VR display
    /// </summary>
    public enum ImageProjectionType
    {
        Flat,              // Standard 2D image on curved/flat screen
        FlatStereoSBS,     // 2D stereoscopic side-by-side
        FlatStereoOU,      // 2D stereoscopic over-under
        Dome180,           // 180° panorama
        Dome180Stereo,     // 180° panorama stereoscopic
        Sphere360,         // 360° panorama (equirectangular)
        Sphere360Stereo    // 360° panorama stereoscopic
    }

}
