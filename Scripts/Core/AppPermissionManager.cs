using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Manages all app permissions at startup.
/// Requests all necessary permissions when app launches to avoid interrupting user experience later.
/// </summary>
public class AppPermissionManager : MonoBehaviour
{
    public static AppPermissionManager Instance { get; private set; }

    /// <summary>
    /// Event fired when all permissions have been processed (granted or denied).
    /// </summary>
    public event Action<bool> OnPermissionsComplete;

    /// <summary>
    /// Returns true if all critical permissions are granted.
    /// </summary>
    public bool HasAllPermissions { get; private set; }

    // Permission strings
    private const string READ_EXTERNAL_STORAGE = "android.permission.READ_EXTERNAL_STORAGE";
    private const string WRITE_EXTERNAL_STORAGE = "android.permission.WRITE_EXTERNAL_STORAGE";
    private const string READ_MEDIA_IMAGES = "android.permission.READ_MEDIA_IMAGES";
    private const string READ_MEDIA_VIDEO = "android.permission.READ_MEDIA_VIDEO";
    private const string READ_MEDIA_AUDIO = "android.permission.READ_MEDIA_AUDIO";
    private const string CAMERA = "android.permission.CAMERA";

    // PlayerPrefs key to track if we've already requested permissions
    private const string PREF_PERMISSIONS_REQUESTED = "VRWorkspace_PermissionsRequested";
    private const string PREF_FULL_ACCESS_DECLINED = "VRWorkspace_FullAccessDeclined";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Initialize and request all permissions.
    /// Call this from APBootstrap or main scene initialization.
    /// </summary>
    public void RequestAllPermissions(Action<bool> onComplete = null)
    {
        if (onComplete != null)
            OnPermissionsComplete += onComplete;

#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(RequestAllPermissionsCoroutine());
#else
        HasAllPermissions = true;
        OnPermissionsComplete?.Invoke(true);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator RequestAllPermissionsCoroutine()
    {
        int sdkVersion = GetAndroidSDKVersion();
        Debug.Log($"[PermissionManager] Starting permission requests for Android SDK {sdkVersion}");

        bool allGranted = true;

        // Step 1: Request Camera permission (for QR Scanner)
        yield return StartCoroutine(RequestCameraPermission((granted) => {
            if (!granted) allGranted = false;
        }));

        // Step 2: Request Storage permissions based on SDK version
        if (sdkVersion >= 30)
        {
            // Android 11+: Request MANAGE_EXTERNAL_STORAGE for full file access
            bool hasFullAccess = HasManageExternalStoragePermission();

            if (!hasFullAccess)
            {
                // Check if user previously declined
                bool previouslyDeclined = PlayerPrefs.GetInt(PREF_FULL_ACCESS_DECLINED, 0) == 1;

                if (!previouslyDeclined)
                {
                    Debug.Log("[PermissionManager] Requesting MANAGE_EXTERNAL_STORAGE...");
                    OpenManageAllFilesSettings();

                    // Wait for user to return from Settings
                    yield return new WaitForSeconds(0.5f);

                    // Poll for permission grant (user may take time in Settings)
                    float timeout = 60f; // Wait up to 60 seconds
                    float elapsed = 0f;
                    while (elapsed < timeout)
                    {
                        if (HasManageExternalStoragePermission())
                        {
                            Debug.Log("[PermissionManager] MANAGE_EXTERNAL_STORAGE granted!");
                            break;
                        }

                        // Check if app is in foreground (user returned from Settings)
                        if (Application.isFocused && elapsed > 2f)
                        {
                            // User returned but didn't grant permission
                            Debug.Log("[PermissionManager] User returned without granting full access");
                            PlayerPrefs.SetInt(PREF_FULL_ACCESS_DECLINED, 1);
                            PlayerPrefs.Save();
                            break;
                        }

                        yield return new WaitForSeconds(0.5f);
                        elapsed += 0.5f;
                    }
                }

                // If still no full access, request media permissions as fallback (Android 13+)
                if (!HasManageExternalStoragePermission() && sdkVersion >= 33)
                {
                    yield return StartCoroutine(RequestMediaPermissions((granted) => {
                        if (!granted) allGranted = false;
                    }));
                }
            }
        }
        else
        {
            // Android 10 and below: Request legacy storage permissions
            yield return StartCoroutine(RequestLegacyStoragePermissions((granted) => {
                if (!granted) allGranted = false;
            }));
        }

        // Mark permissions as requested
        PlayerPrefs.SetInt(PREF_PERMISSIONS_REQUESTED, 1);
        PlayerPrefs.Save();

        // Final check
        HasAllPermissions = CheckAllPermissions();
        Debug.Log($"[PermissionManager] Permission request complete. All granted: {HasAllPermissions}");

        OnPermissionsComplete?.Invoke(HasAllPermissions);
    }

    private IEnumerator RequestCameraPermission(Action<bool> onComplete)
    {
        if (Permission.HasUserAuthorizedPermission(CAMERA))
        {
            Debug.Log("[PermissionManager] Camera permission already granted");
            onComplete?.Invoke(true);
            yield break;
        }

        Debug.Log("[PermissionManager] Requesting Camera permission...");

        bool? result = null;
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += (perm) => { result = true; };
        callbacks.PermissionDenied += (perm) => { result = false; };
        callbacks.PermissionDeniedAndDontAskAgain += (perm) => { result = false; };

        Permission.RequestUserPermission(CAMERA, callbacks);

        // Wait for callback
        float timeout = 30f;
        float elapsed = 0f;
        while (!result.HasValue && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        bool granted = result ?? false;
        Debug.Log($"[PermissionManager] Camera permission: {(granted ? "granted" : "denied")}");
        onComplete?.Invoke(granted);
    }

    private IEnumerator RequestMediaPermissions(Action<bool> onComplete)
    {
        var permissions = new string[] { READ_MEDIA_IMAGES, READ_MEDIA_VIDEO, READ_MEDIA_AUDIO };

        // Check if already granted
        bool allGranted = true;
        foreach (var perm in permissions)
        {
            if (!Permission.HasUserAuthorizedPermission(perm))
            {
                allGranted = false;
                break;
            }
        }

        if (allGranted)
        {
            Debug.Log("[PermissionManager] Media permissions already granted");
            onComplete?.Invoke(true);
            yield break;
        }

        Debug.Log("[PermissionManager] Requesting Media permissions...");

        int grantedCount = 0;
        int responseCount = 0;
        int totalPermissions = permissions.Length;

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += (perm) => { grantedCount++; responseCount++; };
        callbacks.PermissionDenied += (perm) => { responseCount++; };
        callbacks.PermissionDeniedAndDontAskAgain += (perm) => { responseCount++; };

        Permission.RequestUserPermissions(permissions, callbacks);

        // Wait for all callbacks
        float timeout = 30f;
        float elapsed = 0f;
        while (responseCount < totalPermissions && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        bool granted = grantedCount > 0; // At least one media permission
        Debug.Log($"[PermissionManager] Media permissions: {grantedCount}/{totalPermissions} granted");
        onComplete?.Invoke(granted);
    }

    private IEnumerator RequestLegacyStoragePermissions(Action<bool> onComplete)
    {
        int sdkVersion = GetAndroidSDKVersion();
        var permissions = sdkVersion <= 29
            ? new string[] { READ_EXTERNAL_STORAGE, WRITE_EXTERNAL_STORAGE }
            : new string[] { READ_EXTERNAL_STORAGE };

        // Check if already granted
        bool allGranted = true;
        foreach (var perm in permissions)
        {
            if (!Permission.HasUserAuthorizedPermission(perm))
            {
                allGranted = false;
                break;
            }
        }

        if (allGranted)
        {
            Debug.Log("[PermissionManager] Legacy storage permissions already granted");
            onComplete?.Invoke(true);
            yield break;
        }

        Debug.Log("[PermissionManager] Requesting legacy storage permissions...");

        int grantedCount = 0;
        int responseCount = 0;
        int totalPermissions = permissions.Length;

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += (perm) => { grantedCount++; responseCount++; };
        callbacks.PermissionDenied += (perm) => { responseCount++; };
        callbacks.PermissionDeniedAndDontAskAgain += (perm) => { responseCount++; };

        Permission.RequestUserPermissions(permissions, callbacks);

        // Wait for all callbacks
        float timeout = 30f;
        float elapsed = 0f;
        while (responseCount < totalPermissions && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        bool granted = grantedCount == totalPermissions;
        Debug.Log($"[PermissionManager] Legacy storage permissions: {grantedCount}/{totalPermissions} granted");
        onComplete?.Invoke(granted);
    }

    /// <summary>
    /// Check if all required permissions are granted.
    /// </summary>
    public bool CheckAllPermissions()
    {
        int sdkVersion = GetAndroidSDKVersion();

        // Check storage
        bool hasStorage = false;
        if (sdkVersion >= 30)
        {
            hasStorage = HasManageExternalStoragePermission();
            if (!hasStorage && sdkVersion >= 33)
            {
                // Fallback: at least media permissions
                hasStorage = Permission.HasUserAuthorizedPermission(READ_MEDIA_IMAGES) ||
                            Permission.HasUserAuthorizedPermission(READ_MEDIA_VIDEO) ||
                            Permission.HasUserAuthorizedPermission(READ_MEDIA_AUDIO);
            }
        }
        else
        {
            hasStorage = Permission.HasUserAuthorizedPermission(READ_EXTERNAL_STORAGE);
        }

        // Check camera (optional but useful for QR)
        bool hasCamera = Permission.HasUserAuthorizedPermission(CAMERA);

        Debug.Log($"[PermissionManager] Permission check: Storage={hasStorage}, Camera={hasCamera}");

        return hasStorage; // Camera is optional
    }

    /// <summary>
    /// Check if full file access is available (can see all file types).
    /// </summary>
    public bool HasFullFileAccess()
    {
        int sdkVersion = GetAndroidSDKVersion();

        if (sdkVersion >= 30)
        {
            return HasManageExternalStoragePermission();
        }

        return Permission.HasUserAuthorizedPermission(READ_EXTERNAL_STORAGE);
    }

    /// <summary>
    /// Request full file access. Opens Settings on Android 11+.
    /// </summary>
    public void RequestFullFileAccess()
    {
        int sdkVersion = GetAndroidSDKVersion();

        if (sdkVersion >= 30 && !HasManageExternalStoragePermission())
        {
            // Clear the declined flag so we try again
            PlayerPrefs.SetInt(PREF_FULL_ACCESS_DECLINED, 0);
            PlayerPrefs.Save();

            OpenManageAllFilesSettings();
        }
    }

    /// <summary>
    /// Reset the "declined" flag to allow requesting full access again.
    /// </summary>
    public void ResetFullAccessDeclined()
    {
        PlayerPrefs.SetInt(PREF_FULL_ACCESS_DECLINED, 0);
        PlayerPrefs.Save();
    }

    private int GetAndroidSDKVersion()
    {
        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                return version.GetStatic<int>("SDK_INT");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PermissionManager] Failed to get SDK version: {ex.Message}");
            return 29;
        }
    }

    private bool HasManageExternalStoragePermission()
    {
        try
        {
            using (var environment = new AndroidJavaClass("android.os.Environment"))
            {
                return environment.CallStatic<bool>("isExternalStorageManager");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PermissionManager] Failed to check MANAGE_EXTERNAL_STORAGE: {ex.Message}");
            return false;
        }
    }

    private void OpenManageAllFilesSettings()
    {
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
            Debug.LogError($"[PermissionManager] Failed to open Settings: {ex.Message}");
            OpenAppSettings();
        }
    }

    private void OpenAppSettings()
    {
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
            Debug.LogError($"[PermissionManager] Failed to open app settings: {ex.Message}");
        }
    }
#endif
}
