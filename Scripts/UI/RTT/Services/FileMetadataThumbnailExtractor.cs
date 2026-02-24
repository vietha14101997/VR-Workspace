using UnityEngine;
using System;
using System.IO;

namespace VRWorkspace.UI.RTT.Services
{
    /// <summary>
    /// Unified metadata thumbnail extractor for all file types.
    /// Attempts to extract embedded thumbnails from file metadata before falling back to generation.
    ///
    /// Supported formats:
    /// - Images: JPEG/TIFF EXIF thumbnails
    /// - Videos: MP4/MOV covr atom, MKV attachments
    /// - Audio: MP3 ID3v2 APIC, FLAC PICTURE, M4A covr, Ogg Vorbis
    ///
    /// Benefits:
    /// - Much faster than loading full file and resizing
    /// - No need to cache metadata thumbnails (extraction is fast)
    /// - Consistent with Windows Explorer behavior
    /// </summary>
    public static class FileMetadataThumbnailExtractor
    {
        /// <summary>
        /// Result of metadata thumbnail extraction.
        /// </summary>
        public struct ExtractionResult
        {
            public bool Success;
            public Texture2D Thumbnail;
            public bool FromMetadata;  // True if thumbnail came from metadata, false if generated
        }

        /// <summary>
        /// Try to extract embedded thumbnail from file metadata.
        /// This is the primary entry point - checks file type and delegates to appropriate extractor.
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <param name="category">File category (Image, Video, Music, etc.)</param>
        /// <param name="thumbnail">Output texture if found</param>
        /// <returns>True if embedded thumbnail was successfully extracted</returns>
        public static bool TryExtractMetadataThumbnail(string filePath, FileCategory category, out Texture2D thumbnail)
        {
            thumbnail = null;

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                switch (category)
                {
                    case FileCategory.Image:
                        return ImageMetadataThumbnailExtractor.TryExtractEmbeddedThumbnail(filePath, out thumbnail);

                    case FileCategory.Video:
                        return VideoMetadataThumbnailExtractor.TryExtractEmbeddedThumbnail(filePath, out thumbnail);

                    case FileCategory.Music:
                        return AudioMetadataExtractor.TryExtractAlbumArt(filePath, out thumbnail);

                    default:
                        // Other file types don't have metadata thumbnails
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileMetadataThumbnailExtractor] Failed to extract metadata thumbnail from {Path.GetFileName(filePath)}: {ex.Message}");
                if (thumbnail != null)
                {
                    UnityEngine.Object.Destroy(thumbnail);
                    thumbnail = null;
                }
                return false;
            }
        }

        /// <summary>
        /// Try to extract thumbnail from file metadata (thread-safe version that returns raw bytes).
        /// Use this for background thread extraction.
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <param name="category">File category</param>
        /// <returns>Raw image bytes (JPEG/PNG) or null if not found</returns>
        public static byte[] TryExtractMetadataThumbnailData(string filePath, FileCategory category)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                switch (category)
                {
                    case FileCategory.Music:
                        return AudioMetadataExtractor.TryExtractAlbumArtData(filePath);

                    // Image and Video extractors work with Texture2D, so we can't extract raw bytes easily
                    // For these, use TryExtractMetadataThumbnail on main thread instead
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if a file type potentially has metadata thumbnails.
        /// Use this to decide whether to attempt extraction.
        /// </summary>
        public static bool MayHaveMetadataThumbnail(FileCategory category)
        {
            switch (category)
            {
                case FileCategory.Image:
                case FileCategory.Video:
                case FileCategory.Music:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Check if a specific file extension potentially has metadata thumbnails.
        /// </summary>
        public static bool MayHaveMetadataThumbnail(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            switch (ext)
            {
                // Images with EXIF
                case ".jpg":
                case ".jpeg":
                case ".tiff":
                case ".tif":
                // Videos with embedded thumbnails
                case ".mp4":
                case ".m4v":
                case ".mov":
                case ".mkv":
                case ".webm":
                // Audio with album art
                case ".mp3":
                case ".flac":
                case ".m4a":
                case ".aac":
                case ".ogg":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Resize a texture to fit within maxSize while maintaining aspect ratio.
        /// Use this to standardize metadata thumbnail sizes.
        /// </summary>
        public static Texture2D ResizeIfNeeded(Texture2D source, int maxSize)
        {
            if (source == null)
                return null;

            if (source.width <= maxSize && source.height <= maxSize)
                return source;

            int targetWidth, targetHeight;
            if (source.width > source.height)
            {
                targetWidth = maxSize;
                targetHeight = Mathf.RoundToInt((float)source.height / source.width * maxSize);
            }
            else
            {
                targetHeight = maxSize;
                targetWidth = Mathf.RoundToInt((float)source.width / source.height * maxSize);
            }

            // Use RenderTexture for GPU-accelerated resize
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Graphics.Blit(source, rt);

            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, true);
            result.filterMode = FilterMode.Trilinear;
            result.anisoLevel = 16;
            result.wrapMode = TextureWrapMode.Clamp;
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply(true);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            // Destroy original if we created a new one
            if (result != source)
            {
                UnityEngine.Object.Destroy(source);
            }

            return result;
        }
    }

}
