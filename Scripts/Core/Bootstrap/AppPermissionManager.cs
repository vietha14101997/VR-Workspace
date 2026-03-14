using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace VRWorkspace.Core
{
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
        // Cached SDK version to avoid repeated JNI calls
        private int _cachedSdkVersion = -1;

        private IEnumerator RequestAllPermissionsCoroutine()
        {
            // === FAST PATH: Skip heavy JNI calls if permissions were already granted ===
            // PlayerPrefs check is instant (no JNI), avoids 500ms+ of blocking on every startup
            bool previouslyRequested = PlayerPrefs.GetInt(PREF_PERMISSIONS_REQUESTED, 0) == 1;
            if (previouslyRequested)
            {
                // Permissions were processed before — do a lightweight check
                // Spread JNI calls across frames to avoid single-frame freeze
                yield return null; // 1 frame gap
                _cachedSdkVersion = GetAndroidSDKVersion();

                yield return null; // 1 frame gap
                HasAllPermissions = CheckAllPermissions();

                if (HasAllPermissions)
                {
                    Debug.Log("[PermissionManager] Permissions already granted (fast path)");
                    OnPermissionsComplete?.Invoke(true);
                    yield break;
                }
                // Permissions were revoked — fall through to full request flow
                Debug.Log("[PermissionManager] Permissions previously requested but not all granted — re-requesting");
            }

            // === FULL PATH: First launch or permissions revoked ===
            // Spread JNI calls across frames
            yield return null;
            int sdkVersion = _cachedSdkVersion > 0 ? _cachedSdkVersion : GetAndroidSDKVersion();
            _cachedSdkVersion = sdkVersion;
            Debug.Log($"[PermissionManager] Starting permission requests for Android SDK {sdkVersion}");

            yield return null; // Let a frame render before camera permission dialog

            // Step 1: Request Camera permission (for QR Scanner)
            yield return StartCoroutine(RequestCameraPermission((granted) => {
                // Camera is optional
            }));

            // Step 2: Request Storage permissions based on SDK version
            if (sdkVersion >= 30)
            {
                yield return null; // Frame gap before JNI call
                bool hasFullAccess = HasManageExternalStoragePermission();

                if (!hasFullAccess)
                {
                    bool previouslyDeclined = PlayerPrefs.GetInt(PREF_FULL_ACCESS_DECLINED, 0) == 1;

                    if (!previouslyDeclined)
                    {
                        Debug.Log("[PermissionManager] Requesting MANAGE_EXTERNAL_STORAGE...");
                        OpenManageAllFilesSettings();

                        yield return new WaitForSeconds(0.5f);

                        float timeout = 60f;
                        float elapsed = 0f;
                        while (elapsed < timeout)
                        {
                            if (HasManageExternalStoragePermission())
                            {
                                Debug.Log("[PermissionManager] MANAGE_EXTERNAL_STORAGE granted!");
                                break;
                            }

                            if (Application.isFocused && elapsed > 2f)
                            {
                                Debug.Log("[PermissionManager] User returned without granting full access");
                                PlayerPrefs.SetInt(PREF_FULL_ACCESS_DECLINED, 1);
                                PlayerPrefs.Save();
                                break;
                            }

                            yield return new WaitForSeconds(0.5f);
                            elapsed += 0.5f;
                        }
                    }

                    if (!HasManageExternalStoragePermission() && sdkVersion >= 33)
                    {
                        yield return StartCoroutine(RequestMediaPermissions((granted) => {
                            // Media access fallback
                        }));
                    }
                }
            }
            else
            {
                yield return StartCoroutine(RequestLegacyStoragePermissions((granted) => {
                    // Legacy storage access
                }));
            }

            // Mark permissions as requested
            PlayerPrefs.SetInt(PREF_PERMISSIONS_REQUESTED, 1);
            PlayerPrefs.Save();

            // Final check
            yield return null; // Frame gap before JNI
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
#pragma warning disable 0618
            callbacks.PermissionDeniedAndDontAskAgain += (perm) => { result = false; };
#pragma warning restore 0618

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
#pragma warning disable 0618
            callbacks.PermissionDeniedAndDontAskAgain += (perm) => { responseCount++; };
#pragma warning restore 0618

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
#pragma warning disable 0618
            callbacks.PermissionDeniedAndDontAskAgain += (perm) => { responseCount++; };
#pragma warning restore 0618

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

}
