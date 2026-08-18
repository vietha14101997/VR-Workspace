using UnityEngine;
using System;

namespace VRWorkspace.UI.RTT.Services
{
    #if UNITY_ANDROID
    using UnityEngine.Android;
    #endif

    /// <summary>
    /// Helper class to handle Android storage permissions for File Manager.
    /// Handles different permission models across Android versions:
    /// - Android 9 and below: READ/WRITE_EXTERNAL_STORAGE
    /// - Android 10: requestLegacyExternalStorage + READ/WRITE_EXTERNAL_STORAGE
    /// - Android 11-12: MANAGE_EXTERNAL_STORAGE (requires Settings intent)
    /// - Android 13+: READ_MEDIA_IMAGES/VIDEO/AUDIO
    /// </summary>
    public static class StoragePermissionHelper
    {
        private const string READ_EXTERNAL_STORAGE = "android.permission.READ_EXTERNAL_STORAGE";
        private const string WRITE_EXTERNAL_STORAGE = "android.permission.WRITE_EXTERNAL_STORAGE";
        private const string READ_MEDIA_IMAGES = "android.permission.READ_MEDIA_IMAGES";
        private const string READ_MEDIA_VIDEO = "android.permission.READ_MEDIA_VIDEO";
        private const string READ_MEDIA_AUDIO = "android.permission.READ_MEDIA_AUDIO";

        /// <summary>
        /// Check if app has storage read permission.
        /// For media-only access on Android 13+, use HasMediaPermission().
        /// For full file access (all file types), use HasFullFileAccess().
        /// </summary>
        public static bool HasStoragePermission()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            int sdkVersion = GetAndroidSDKVersion();
            Debug.Log($"[StoragePermission] Android SDK version: {sdkVersion}");

            // Android 11+ (API 30+): Prefer MANAGE_EXTERNAL_STORAGE for full file access
            if (sdkVersion >= 30)
            {
                bool hasManageStorage = HasManageExternalStoragePermission();
                Debug.Log($"[StoragePermission] API 30+: MANAGE_EXTERNAL_STORAGE={hasManageStorage}");

                // If has full access, return true
                if (hasManageStorage) return true;

                // Android 13+: Fall back to media permissions (limited access)
                if (sdkVersion >= 33)
                {
                    bool hasMedia = HasMediaPermission();
                    Debug.Log($"[StoragePermission] API 33+ fallback to media: {hasMedia}");
                    return hasMedia;
                }

                return false;
            }

            // Android 10 and below: Check READ_EXTERNAL_STORAGE
            bool hasReadStorage = Permission.HasUserAuthorizedPermission(READ_EXTERNAL_STORAGE);
            Debug.Log($"[StoragePermission] API <30: READ_EXTERNAL_STORAGE={hasReadStorage}");
            return hasReadStorage;
    #else
            return true; // Always true in Editor or non-Android
    #endif
        }

        /// <summary>
        /// Check if app has FULL file access (all file types including documents, text, etc.)
        /// On Android 11+, this requires MANAGE_EXTERNAL_STORAGE.
        /// On Android 10 and below, READ_EXTERNAL_STORAGE is sufficient.
        /// </summary>
        public static bool HasFullFileAccess()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            int sdkVersion = GetAndroidSDKVersion();

            // Android 11+ requires MANAGE_EXTERNAL_STORAGE for full access
            if (sdkVersion >= 30)
            {
                return HasManageExternalStoragePermission();
            }

            // Android 10 and below: READ_EXTERNAL_STORAGE gives full access
            return Permission.HasUserAuthorizedPermission(READ_EXTERNAL_STORAGE);
    #else
            return true;
    #endif
        }

        /// <summary>
        /// Check if app has media permissions (images, video, audio) on Android 13+.
        /// </summary>
        public static bool HasMediaPermission()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            bool hasImages = Permission.HasUserAuthorizedPermission(READ_MEDIA_IMAGES);
            bool hasVideo = Permission.HasUserAuthorizedPermission(READ_MEDIA_VIDEO);
            bool hasAudio = Permission.HasUserAuthorizedPermission(READ_MEDIA_AUDIO);
            return hasImages || hasVideo || hasAudio;
    #else
            return true;
    #endif
        }

        /// <summary>
        /// Request storage permissions based on Android version.
        /// On Android 11+, this will request MANAGE_EXTERNAL_STORAGE for full file access.
        /// </summary>
        public static void RequestStoragePermission(Action<bool> onComplete = null)
        {
            RequestFullFileAccess(onComplete);
        }

        #if UNITY_ANDROID && !UNITY_EDITOR
        // Session-level dedupe for opening the All Files Settings page.
        // Multiple startup paths (AppPermissionManager, file manager init, etc.) can
        // race to call OpenManageAllFilesSettings within milliseconds of each other,
        // stacking two Settings activities on the activity back-stack so the user has
        // to press Back twice to return to the app. Cooldown windows multiple callers
        // down to a single Settings open per session window.
        private static float _lastAllFilesOpenTime = -100f;
        private const float ALL_FILES_OPEN_COOLDOWN = 30f;
#endif

        /// <summary>
        /// Request FULL file access (all file types).
        /// On Android 11+, opens Settings for MANAGE_EXTERNAL_STORAGE.
        /// On Android 10 and below, requests READ/WRITE_EXTERNAL_STORAGE.
        /// </summary>
        public static void RequestFullFileAccess(Action<bool> onComplete = null)
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            int sdkVersion = GetAndroidSDKVersion();
            Debug.Log($"[StoragePermission] Requesting FULL file access for SDK {sdkVersion}");

            // Android 11+ (API 30+): Need MANAGE_EXTERNAL_STORAGE for full access
            if (sdkVersion >= 30)
            {
                if (!HasManageExternalStoragePermission())
                {
                    // Dedupe: skip if another caller already opened Settings recently.
                    // The other caller (AppPermissionManager) will handle the user response.
                    if (Time.unscaledTime - _lastAllFilesOpenTime < ALL_FILES_OPEN_COOLDOWN)
                    {
                        Debug.Log("[StoragePermission] Skipping duplicate All Files Settings open (within cooldown)");
                        onComplete?.Invoke(false);
                        return;
                    }

                    Debug.Log("[StoragePermission] Opening Settings for MANAGE_EXTERNAL_STORAGE");
                    _lastAllFilesOpenTime = Time.unscaledTime;
                    OpenManageAllFilesSettings();
                    onComplete?.Invoke(false); // User needs to manually enable
                    return;
                }
                onComplete?.Invoke(true);
                return;
            }

            // Android 10 and below: Request READ/WRITE_EXTERNAL_STORAGE
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += (perm) => {
                Debug.Log($"[StoragePermission] Granted: {perm}");
                onComplete?.Invoke(true);
            };
            callbacks.PermissionDenied += (perm) => {
                Debug.Log($"[StoragePermission] Denied: {perm}");
                onComplete?.Invoke(false);
            };
#pragma warning disable 0618
            callbacks.PermissionDeniedAndDontAskAgain += (perm) => {
#pragma warning restore 0618
                Debug.Log($"[StoragePermission] Denied (Don't ask again): {perm}");
                onComplete?.Invoke(false);
            };

            if (sdkVersion <= 29)
            {
                Permission.RequestUserPermissions(new string[] { READ_EXTERNAL_STORAGE, WRITE_EXTERNAL_STORAGE }, callbacks);
            }
            else
            {
                Permission.RequestUserPermission(READ_EXTERNAL_STORAGE, callbacks);
            }
    #else
            onComplete?.Invoke(true);
    #endif
        }

        /// <summary>
        /// Request media-only permissions (images, video, audio) on Android 13+.
        /// This is a fallback when user doesn't want to grant full file access.
        /// </summary>
        public static void RequestMediaPermission(Action<bool> onComplete = null)
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            int sdkVersion = GetAndroidSDKVersion();

            // Android 13+: Request granular media permissions
            if (sdkVersion >= 33)
            {
                var permissions = new string[] { READ_MEDIA_IMAGES, READ_MEDIA_VIDEO, READ_MEDIA_AUDIO };
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += (perm) => {
                    Debug.Log($"[StoragePermission] Media granted: {perm}");
                    onComplete?.Invoke(true);
                };
                callbacks.PermissionDenied += (perm) => {
                    Debug.Log($"[StoragePermission] Media denied: {perm}");
                    onComplete?.Invoke(false);
                };
#pragma warning disable 0618
                callbacks.PermissionDeniedAndDontAskAgain += (perm) => {
#pragma warning restore 0618
                    Debug.Log($"[StoragePermission] Media denied (Don't ask again): {perm}");
                    onComplete?.Invoke(false);
                };
                Permission.RequestUserPermissions(permissions, callbacks);
                return;
            }

            // For older versions, full access = media access
            RequestFullFileAccess(onComplete);
    #else
            onComplete?.Invoke(true);
    #endif
        }

        /// <summary>
        /// Get Android SDK version (API level).
        /// </summary>
        public static int GetAndroidSDKVersion()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    return version.GetStatic<int>("SDK_INT");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StoragePermission] Failed to get SDK version: {ex.Message}");
                return 29; // Fallback to Android 10
            }
    #else
            return 33; // Assume latest in Editor
    #endif
        }

        /// <summary>
        /// Check if app has MANAGE_EXTERNAL_STORAGE permission (Android 11+).
        /// </summary>
        public static bool HasManageExternalStoragePermission()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var environment = new AndroidJavaClass("android.os.Environment"))
                {
                    return environment.CallStatic<bool>("isExternalStorageManager");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StoragePermission] Failed to check MANAGE_EXTERNAL_STORAGE: {ex.Message}");
                return false;
            }
    #else
            return true;
    #endif
        }

        /// <summary>
        /// Open system Settings to grant MANAGE_EXTERNAL_STORAGE permission (Android 11+).
        /// Cooldown-deduped: callers within the cooldown window are skipped silently.
        /// </summary>
        public static void OpenManageAllFilesSettings()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            // Single-source dedupe: every path (AppPermissionManager startup,
            // file manager init, user-clicked "grant access") funnels through
            // here. The 30-second cooldown collapses races that would otherwise
            // stack two Settings activities on the Android back-stack.
            if (Time.unscaledTime - _lastAllFilesOpenTime < ALL_FILES_OPEN_COOLDOWN)
            {
                Debug.Log("[StoragePermission] Skipping duplicate All Files Settings open (within cooldown)");
                return;
            }
            _lastAllFilesOpenTime = Time.unscaledTime;

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent",
                    "android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION"))
                {
                    using (var uri = new AndroidJavaClass("android.net.Uri"))
                    using (var uriObj = uri.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier))
                    {
                        intent.Call<AndroidJavaObject>("setData", uriObj);
                        activity.Call("startActivity", intent);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StoragePermission] Failed to open Settings: {ex.Message}");
                // Fallback: Open general app settings
                OpenAppSettings();
            }
    #endif
        }

        /// <summary>
        /// Open app settings page.
        /// </summary>
        public static void OpenAppSettings()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent",
                    "android.settings.APPLICATION_DETAILS_SETTINGS"))
                {
                    using (var uri = new AndroidJavaClass("android.net.Uri"))
                    using (var uriObj = uri.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier))
                    {
                        intent.Call<AndroidJavaObject>("setData", uriObj);
                        activity.Call("startActivity", intent);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StoragePermission] Failed to open app settings: {ex.Message}");
            }
    #endif
        }
    }

}
