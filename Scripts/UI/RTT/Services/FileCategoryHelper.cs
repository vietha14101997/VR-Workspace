using System.Collections.Generic;

namespace VRWorkspace.UI.RTT.Services
{
    /// <summary>
    /// File category enumeration for categorizing files by type.
    /// Only includes categories that Unity can natively read.
    /// </summary>
    public enum FileCategory
    {
        Folder,
        Image,      // jpg, jpeg, png, gif, bmp - Unity Texture2D.LoadImage()
        Video,      // mp4, webm - Unity VideoPlayer
        Music,      // mp3, wav, ogg, aif - Unity AudioSource
        Text,       // txt, log, json, xml, csv - System.IO
        Archive,    // zip only
        Unknown     // Unsupported file types
    }

    /// <summary>
    /// Static utility class for file categorization and icon mapping.
    /// Determines file categories based on Unity's native file support.
    /// </summary>
    public static class FileCategoryHelper
    {
        // Unity-natively supported image extensions (Texture2D.LoadImage)
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>
        {
            "jpg", "jpeg", "png", "gif", "bmp"
        };

        // Unity-natively supported video extensions (VideoPlayer)
        private static readonly HashSet<string> VideoExtensions = new HashSet<string>
        {
            "mp4", "webm"
        };

        // Unity-natively supported audio extensions (AudioSource)
        // Also includes FLAC and M4A which can have embedded album art
        private static readonly HashSet<string> MusicExtensions = new HashSet<string>
        {
            "mp3", "wav", "ogg", "aif", "flac", "m4a", "aac"
        };

        // Text-based file extensions that can be read with System.IO
        private static readonly HashSet<string> TextExtensions = new HashSet<string>
        {
            "txt", "log", "json", "xml", "md"
        };

        // Archive extensions (only zip is widely supported)
        private static readonly HashSet<string> ArchiveExtensions = new HashSet<string>
        {
            "zip"
        };

        /// <summary>
        /// Get the category of a file based on its extension.
        /// </summary>
        /// <param name="extension">File extension without dot (e.g., "jpg", "mp4")</param>
        /// <returns>FileCategory enum value</returns>
        public static FileCategory GetCategory(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return FileCategory.Unknown;

            string ext = extension.ToLower().TrimStart('.');

            if (ImageExtensions.Contains(ext))
                return FileCategory.Image;

            if (VideoExtensions.Contains(ext))
                return FileCategory.Video;

            if (MusicExtensions.Contains(ext))
                return FileCategory.Music;

            if (TextExtensions.Contains(ext))
                return FileCategory.Text;

            if (ArchiveExtensions.Contains(ext))
                return FileCategory.Archive;

            return FileCategory.Unknown;
        }

        /// <summary>
        /// Check if a file extension is natively supported by Unity.
        /// </summary>
        /// <param name="extension">File extension without dot</param>
        /// <returns>True if Unity can read this file type</returns>
        public static bool IsUnitySupported(string extension)
        {
            return GetCategory(extension) != FileCategory.Unknown;
        }

        /// <summary>
        /// Get the default icon name for a file category.
        /// Used as placeholder while loading or for non-thumbnail categories.
        /// </summary>
        /// <param name="category">File category</param>
        /// <returns>Resource name of the icon sprite</returns>
        public static string GetDefaultIconName(FileCategory category)
        {
            switch (category)
            {
                case FileCategory.Folder:
                    return "icon_folder";
                case FileCategory.Image:
                    return "icon_image";
                case FileCategory.Video:
                    return "icon_video";
                case FileCategory.Music:
                    return "icon_music_file";
                case FileCategory.Text:
                    return "icon_text_file";
                case FileCategory.Archive:
                    return "icon_zip_file";
                case FileCategory.Unknown:
                default:
                    return "icon_file_unknown";
            }
        }

        /// <summary>
        /// Check if a category requires thumbnail generation (async loading).
        /// Image, Video, and Music categories can generate preview thumbnails.
        /// Music files may have embedded album art.
        /// </summary>
        /// <param name="category">File category</param>
        /// <returns>True if thumbnail should be generated for this category</returns>
        public static bool RequiresThumbnailGeneration(FileCategory category)
        {
            return category == FileCategory.Image || category == FileCategory.Video || category == FileCategory.Music;
        }

        /// <summary>
        /// Get the loading placeholder icon for categories that require thumbnail generation.
        /// This icon is displayed while the actual thumbnail is being loaded asynchronously.
        /// </summary>
        /// <param name="category">File category</param>
        /// <returns>Resource name of the loading placeholder icon</returns>
        public static string GetLoadingPlaceholderIcon(FileCategory category)
        {
            // Use unified media icon as placeholder while thumbnail is loading
            if (RequiresThumbnailGeneration(category))
            {
                return "icon_media_file";
            }
            // For non-thumbnail categories, return their default icon
            return GetDefaultIconName(category);
        }

        /// <summary>
        /// Get all supported extensions for a specific category.
        /// </summary>
        /// <param name="category">File category</param>
        /// <returns>HashSet of supported extensions</returns>
        public static HashSet<string> GetExtensionsForCategory(FileCategory category)
        {
            switch (category)
            {
                case FileCategory.Image:
                    return new HashSet<string>(ImageExtensions);
                case FileCategory.Video:
                    return new HashSet<string>(VideoExtensions);
                case FileCategory.Music:
                    return new HashSet<string>(MusicExtensions);
                case FileCategory.Text:
                    return new HashSet<string>(TextExtensions);
                case FileCategory.Archive:
                    return new HashSet<string>(ArchiveExtensions);
                default:
                    return new HashSet<string>();
            }
        }
    }

}
