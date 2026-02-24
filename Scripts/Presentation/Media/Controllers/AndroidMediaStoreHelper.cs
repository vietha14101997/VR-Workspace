using UnityEngine;
using System;
using System.Collections.Generic;

namespace VRWorkspace.Media.Core
{
    #if UNITY_ANDROID && !UNITY_EDITOR
    using UnityEngine.Android;
    #endif

    /// <summary>
    /// Helper class to query Android MediaStore for fast media file discovery.
    /// MediaStore is a system-maintained database that indexes all media files,
    /// providing near-instant queries compared to manual directory scanning.
    /// </summary>
    public static class AndroidMediaStoreHelper
    {
        #region Constants
        // MediaStore URIs
        private const string VIDEO_URI = "content://media/external/video/media";
        private const string IMAGE_URI = "content://media/external/images/media";
        private const string AUDIO_URI = "content://media/external/audio/media";

        // Column names
        private const string COL_ID = "_id";
        private const string COL_DATA = "_data";           // File path
        private const string COL_DISPLAY_NAME = "_display_name";
        private const string COL_TITLE = "title";
        private const string COL_SIZE = "_size";
        private const string COL_DATE_ADDED = "date_added";
        private const string COL_DATE_MODIFIED = "date_modified";
        private const string COL_DURATION = "duration";    // Video/Audio only (milliseconds)
        private const string COL_WIDTH = "width";          // Video/Image only
        private const string COL_HEIGHT = "height";        // Video/Image only
        private const string COL_MIME_TYPE = "mime_type";
        #endregion

        #region Data Classes
        /// <summary>
        /// Represents a media item from MediaStore.
        /// </summary>
        public class MediaStoreItem
        {
            public long Id;
            public string Path;
            public string DisplayName;
            public string Title;
            public string MimeType;
            public long SizeBytes;
            public DateTime DateAdded;
            public DateTime DateModified;
            public long DurationMs;      // For video/audio
            public int Width;            // For video/image
            public int Height;           // For video/image
            public MediaType Type;
        }

        public enum MediaType
        {
            Video,
            Image,
            Audio
        }
        #endregion

        #region Public API
        /// <summary>
        /// Query all videos from MediaStore.
        /// Much faster than scanning directories manually.
        /// </summary>
        public static List<MediaStoreItem> QueryVideos()
        {
            return QueryMediaStore(VIDEO_URI, MediaType.Video);
        }

        /// <summary>
        /// Query all images from MediaStore.
        /// </summary>
        public static List<MediaStoreItem> QueryImages()
        {
            return QueryMediaStore(IMAGE_URI, MediaType.Image);
        }

        /// <summary>
        /// Query all audio files from MediaStore.
        /// </summary>
        public static List<MediaStoreItem> QueryAudio()
        {
            return QueryMediaStore(AUDIO_URI, MediaType.Audio);
        }

        /// <summary>
        /// Query all media (videos, images, audio) from MediaStore.
        /// </summary>
        public static List<MediaStoreItem> QueryAllMedia()
        {
            var results = new List<MediaStoreItem>();
            results.AddRange(QueryVideos());
            results.AddRange(QueryImages());
            results.AddRange(QueryAudio());
            return results;
        }

        /// <summary>
        /// Check if we're running on Android and can use MediaStore.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
    #if UNITY_ANDROID && !UNITY_EDITOR
                return true;
    #else
                return false;
    #endif
            }
        }
        #endregion

        #region Private Implementation
        private static List<MediaStoreItem> QueryMediaStore(string uriString, MediaType mediaType)
        {
            var results = new List<MediaStoreItem>();

    #if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Get Android classes
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var contentResolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var uri = new AndroidJavaClass("android.net.Uri").CallStatic<AndroidJavaObject>("parse", uriString))
                {
                    // Define columns to query
                    string[] projection = GetProjection(mediaType);

                    // Sort by date modified descending (newest first)
                    string sortOrder = COL_DATE_MODIFIED + " DESC";

                    // Execute query
                    using (var cursor = contentResolver.Call<AndroidJavaObject>(
                        "query",
                        uri,
                        projection,
                        null,  // selection (WHERE clause)
                        null,  // selectionArgs
                        sortOrder))
                    {
                        if (cursor == null)
                        {
                            Debug.LogWarning($"[AndroidMediaStoreHelper] Query returned null cursor for {mediaType}");
                            return results;
                        }

                        // Get column indices
                        int idIdx = cursor.Call<int>("getColumnIndex", COL_ID);
                        int dataIdx = cursor.Call<int>("getColumnIndex", COL_DATA);
                        int displayNameIdx = cursor.Call<int>("getColumnIndex", COL_DISPLAY_NAME);
                        int titleIdx = cursor.Call<int>("getColumnIndex", COL_TITLE);
                        int sizeIdx = cursor.Call<int>("getColumnIndex", COL_SIZE);
                        int dateAddedIdx = cursor.Call<int>("getColumnIndex", COL_DATE_ADDED);
                        int dateModifiedIdx = cursor.Call<int>("getColumnIndex", COL_DATE_MODIFIED);
                        int mimeTypeIdx = cursor.Call<int>("getColumnIndex", COL_MIME_TYPE);
                        int durationIdx = mediaType != MediaType.Image ? cursor.Call<int>("getColumnIndex", COL_DURATION) : -1;
                        int widthIdx = mediaType != MediaType.Audio ? cursor.Call<int>("getColumnIndex", COL_WIDTH) : -1;
                        int heightIdx = mediaType != MediaType.Audio ? cursor.Call<int>("getColumnIndex", COL_HEIGHT) : -1;

                        int count = cursor.Call<int>("getCount");
                        Debug.Log($"[AndroidMediaStoreHelper] Found {count} {mediaType} items");

                        // Iterate through results
                        while (cursor.Call<bool>("moveToNext"))
                        {
                            try
                            {
                                var item = new MediaStoreItem
                                {
                                    Type = mediaType,
                                    Id = GetLong(cursor, idIdx),
                                    Path = GetString(cursor, dataIdx),
                                    DisplayName = GetString(cursor, displayNameIdx),
                                    Title = GetString(cursor, titleIdx),
                                    MimeType = GetString(cursor, mimeTypeIdx),
                                    SizeBytes = GetLong(cursor, sizeIdx),
                                    DateAdded = UnixTimeToDateTime(GetLong(cursor, dateAddedIdx)),
                                    DateModified = UnixTimeToDateTime(GetLong(cursor, dateModifiedIdx)),
                                };

                                // Duration (video/audio only)
                                if (durationIdx >= 0)
                                {
                                    item.DurationMs = GetLong(cursor, durationIdx);
                                }

                                // Dimensions (video/image only)
                                if (widthIdx >= 0)
                                {
                                    item.Width = GetInt(cursor, widthIdx);
                                    item.Height = GetInt(cursor, heightIdx);
                                }

                                // Only add if path is valid
                                if (!string.IsNullOrEmpty(item.Path))
                                {
                                    results.Add(item);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[AndroidMediaStoreHelper] Error reading row: {ex.Message}");
                            }
                        }

                        cursor.Call("close");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AndroidMediaStoreHelper] Query failed: {ex.Message}");
            }
    #else
            Debug.Log("[AndroidMediaStoreHelper] MediaStore is only available on Android");
    #endif

            return results;
        }

        private static string[] GetProjection(MediaType mediaType)
        {
            var columns = new List<string>
            {
                COL_ID,
                COL_DATA,
                COL_DISPLAY_NAME,
                COL_TITLE,
                COL_SIZE,
                COL_DATE_ADDED,
                COL_DATE_MODIFIED,
                COL_MIME_TYPE
            };

            // Add type-specific columns
            if (mediaType != MediaType.Image)
            {
                columns.Add(COL_DURATION);
            }
            if (mediaType != MediaType.Audio)
            {
                columns.Add(COL_WIDTH);
                columns.Add(COL_HEIGHT);
            }

            return columns.ToArray();
        }

    #if UNITY_ANDROID && !UNITY_EDITOR
        private static string GetString(AndroidJavaObject cursor, int columnIndex)
        {
            if (columnIndex < 0) return null;
            try
            {
                if (cursor.Call<bool>("isNull", columnIndex)) return null;
                return cursor.Call<string>("getString", columnIndex);
            }
            catch
            {
                return null;
            }
        }

        private static long GetLong(AndroidJavaObject cursor, int columnIndex)
        {
            if (columnIndex < 0) return 0;
            try
            {
                if (cursor.Call<bool>("isNull", columnIndex)) return 0;
                return cursor.Call<long>("getLong", columnIndex);
            }
            catch
            {
                return 0;
            }
        }

        private static int GetInt(AndroidJavaObject cursor, int columnIndex)
        {
            if (columnIndex < 0) return 0;
            try
            {
                if (cursor.Call<bool>("isNull", columnIndex)) return 0;
                return cursor.Call<int>("getInt", columnIndex);
            }
            catch
            {
                return 0;
            }
        }
    #endif

        private static DateTime UnixTimeToDateTime(long unixTime)
        {
            if (unixTime <= 0) return DateTime.MinValue;
            return DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime;
        }
        #endregion

        #region Utility Methods
        /// <summary>
        /// Request MediaStore to scan a specific file (useful after creating/modifying files).
        /// </summary>
        public static void ScanFile(string filePath)
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var mediaScannerConnection = new AndroidJavaClass("android.media.MediaScannerConnection"))
                {
                    mediaScannerConnection.CallStatic("scanFile",
                        activity,
                        new string[] { filePath },
                        null,  // MIME types (null = auto-detect)
                        null); // OnScanCompletedListener

                    Debug.Log($"[AndroidMediaStoreHelper] Requested scan for: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AndroidMediaStoreHelper] ScanFile failed: {ex.Message}");
            }
    #endif
        }

        /// <summary>
        /// Get thumbnail URI for a media item (can be used with Android's ThumbnailUtils).
        /// </summary>
        public static string GetThumbnailUri(MediaStoreItem item)
        {
            if (item == null) return null;

            string baseUri = item.Type switch
            {
                MediaType.Video => VIDEO_URI,
                MediaType.Image => IMAGE_URI,
                MediaType.Audio => AUDIO_URI,
                _ => null
            };

            return baseUri != null ? $"{baseUri}/{item.Id}" : null;
        }
        #endregion
    }

}
