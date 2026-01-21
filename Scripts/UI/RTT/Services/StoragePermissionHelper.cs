using UnityEngine;
using System;
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
    /// </summary>
    public static bool HasStoragePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        int sdkVersion = GetAndroidSDKVersion();
        Debug.Log($"[StoragePermission] Android SDK version: {sdkVersion}");

        // Android 13+ (API 33): Check granular media permissions
        if (sdkVersion >= 33)
        {
            bool hasImages = Permission.HasUserAuthorizedPermission(READ_MEDIA_IMAGES);
            bool hasVideo = Permission.HasUserAuthorizedPermission(READ_MEDIA_VIDEO);
            bool hasAudio = Permission.HasUserAuthorizedPermission(READ_MEDIA_AUDIO);
            Debug.Log($"[StoragePermission] API 33+: Images={hasImages}, Video={hasVideo}, Audio={hasAudio}");
            return hasImages || hasVideo || hasAudio;
        }

        // Android 11-12 (API 30-32): Check MANAGE_EXTERNAL_STORAGE
        if (sdkVersion >= 30)
        {
            bool hasManageStorage = HasManageExternalStoragePermission();
            Debug.Log($"[StoragePermission] API 30-32: MANAGE_EXTERNAL_STORAGE={hasManageStorage}");
            return hasManageStorage;
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
    /// Request storage permissions based on Android version.
    /// </summary>
    public static void RequestStoragePermission(Action<bool> onComplete = null)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        int sdkVersion = GetAndroidSDKVersion();
        Debug.Log($"[StoragePermission] Requesting permissions for SDK {sdkVersion}");

        // Android 13+ (API 33): Request granular media permissions
        if (sdkVersion >= 33)
        {
            var permissions = new string[] { READ_MEDIA_IMAGES, READ_MEDIA_VIDEO, READ_MEDIA_AUDIO };
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += (perm) => {
                Debug.Log($"[StoragePermission] Granted: {perm}");
                onComplete?.Invoke(true);
            };
            callbacks.PermissionDenied += (perm) => {
                Debug.Log($"[StoragePermission] Denied: {perm}");
                onComplete?.Invoke(false);
            };
            callbacks.PermissionDeniedAndDontAskAgain += (perm) => {
                Debug.Log($"[StoragePermission] Denied (Don't ask again): {perm}");
                onComplete?.Invoke(false);
            };
            Permission.RequestUserPermissions(permissions, callbacks);
            return;
        }

        // Android 11-12 (API 30-32): Need to open Settings for MANAGE_EXTERNAL_STORAGE
        if (sdkVersion >= 30)
        {
            if (!HasManageExternalStoragePermission())
            {
                Debug.Log("[StoragePermission] Opening Settings for MANAGE_EXTERNAL_STORAGE");
                OpenManageAllFilesSettings();
                onComplete?.Invoke(false); // User needs to manually enable
                return;
            }
            onComplete?.Invoke(true);
            return;
        }

        // Android 10 and below: Request READ/WRITE_EXTERNAL_STORAGE
        var callbacks2 = new PermissionCallbacks();
        callbacks2.PermissionGranted += (perm) => {
            Debug.Log($"[StoragePermission] Granted: {perm}");
            onComplete?.Invoke(true);
        };
        callbacks2.PermissionDenied += (perm) => {
            Debug.Log($"[StoragePermission] Denied: {perm}");
            onComplete?.Invoke(false);
        };
        callbacks2.PermissionDeniedAndDontAskAgain += (perm) => {
            Debug.Log($"[StoragePermission] Denied (Don't ask again): {perm}");
            onComplete?.Invoke(false);
        };

        if (sdkVersion <= 29)
        {
            Permission.RequestUserPermissions(new string[] { READ_EXTERNAL_STORAGE, WRITE_EXTERNAL_STORAGE }, callbacks2);
        }
        else
        {
            Permission.RequestUserPermission(READ_EXTERNAL_STORAGE, callbacks2);
        }
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
    /// </summary>
    public static void OpenManageAllFilesSettings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
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
