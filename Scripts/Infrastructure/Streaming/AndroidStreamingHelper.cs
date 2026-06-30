using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Android-specific optimizations for streaming stability.
    /// Manages Wi-Fi Lock, Wake Lock, and power management settings.
    /// </summary>
    public class AndroidStreamingHelper : MonoBehaviour
    {
        private static AndroidStreamingHelper _instance;
        public static AndroidStreamingHelper Instance => _instance;

        /// <summary>
        /// Reset static singleton at the start of each Play session.
        /// Without this, _instance keeps a ghost reference to a destroyed object
        /// on the 2nd Play onwards (Unity doesn't reset static fields on Play exit).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticInstance()
        {
            _instance = null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _wifiLock;
        private AndroidJavaObject _wakeLock;
        private AndroidJavaObject _activity;
        private bool _isAcquired;
#endif

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Acquire Wi-Fi Lock and Wake Lock to prevent Android from sleeping during streaming.
        /// Call this when streaming starts.
        /// </summary>
        public void AcquireLocks()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_isAcquired) return;

            try
            {
                // Get activity
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }

                // Acquire Wi-Fi Lock (HIGH_PERF mode keeps Wi-Fi active)
                using (var context = _activity.Call<AndroidJavaObject>("getApplicationContext"))
                using (var wifiManager = context.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    // WIFI_MODE_FULL_HIGH_PERF = 3
                    _wifiLock = wifiManager.Call<AndroidJavaObject>("createWifiLock", 3, "VRWorkspace_WifiLock");
                    _wifiLock.Call("setReferenceCounted", false);
                    _wifiLock.Call("acquire");
                    AppLog.Log("[AndroidHelper] Wi-Fi Lock acquired (HIGH_PERF mode)");
                }

                // Acquire Wake Lock (PARTIAL_WAKE_LOCK keeps CPU running)
                using (var context = _activity.Call<AndroidJavaObject>("getApplicationContext"))
                using (var powerManager = context.Call<AndroidJavaObject>("getSystemService", "power"))
                {
                    // PARTIAL_WAKE_LOCK = 1
                    _wakeLock = powerManager.Call<AndroidJavaObject>("newWakeLock", 1, "VRWorkspace:StreamingWakeLock");
                    _wakeLock.Call("setReferenceCounted", false);
                    _wakeLock.Call("acquire");
                    AppLog.Log("[AndroidHelper] Wake Lock acquired (PARTIAL mode)");
                }

                _isAcquired = true;
                AppLog.Log("[AndroidHelper] All power locks acquired for stable streaming");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AndroidHelper] Failed to acquire locks: {ex.Message}");
            }
#else
            AppLog.Log("[AndroidHelper] Power locks only available on Android device");
#endif
        }

        /// <summary>
        /// Release all locks. Call this when streaming stops.
        /// </summary>
        public void ReleaseLocks()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_isAcquired) return;

            try
            {
                if (_wifiLock != null)
                {
                    if (_wifiLock.Call<bool>("isHeld"))
                    {
                        _wifiLock.Call("release");
                        AppLog.Log("[AndroidHelper] Wi-Fi Lock released");
                    }
                    _wifiLock.Dispose();
                    _wifiLock = null;
                }

                if (_wakeLock != null)
                {
                    if (_wakeLock.Call<bool>("isHeld"))
                    {
                        _wakeLock.Call("release");
                        AppLog.Log("[AndroidHelper] Wake Lock released");
                    }
                    _wakeLock.Dispose();
                    _wakeLock = null;
                }

                _isAcquired = false;
                AppLog.Log("[AndroidHelper] All power locks released");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AndroidHelper] Failed to release locks: {ex.Message}");
            }
#endif
        }

        /// <summary>
        /// Check if battery optimization is disabled for this app.
        /// If not, prompt user to disable it for better streaming stability.
        /// Uses PlayerPrefs cache to skip JNI calls on subsequent launches.
        /// </summary>
        public void RequestDisableBatteryOptimization()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Fast path: if already exempted on a previous launch, skip JNI calls entirely
            if (PlayerPrefs.GetInt("VRWorkspace_BatteryOptExempt", 0) == 1)
            {
                AppLog.Log("[AndroidHelper] Battery optimization already handled (cached)");
                return;
            }

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                using (var powerManager = context.Call<AndroidJavaObject>("getSystemService", "power"))
                {
                    string packageName = context.Call<string>("getPackageName");
                    bool isIgnoring = powerManager.Call<bool>("isIgnoringBatteryOptimizations", packageName);

                    if (!isIgnoring)
                    {
                        AppLog.Log("[AndroidHelper] Requesting battery optimization exemption...");

                        using (var intent = new AndroidJavaObject("android.content.Intent"))
                        using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                        {
                            intent.Call<AndroidJavaObject>("setAction", "android.settings.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS");
                            var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + packageName);
                            intent.Call<AndroidJavaObject>("setData", uri);
                            activity.Call("startActivity", intent);
                        }
                    }
                    else
                    {
                        AppLog.Log("[AndroidHelper] Battery optimization already disabled for this app");
                        // Cache result so we skip JNI on next launch
                        PlayerPrefs.SetInt("VRWorkspace_BatteryOptExempt", 1);
                        PlayerPrefs.Save();
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AndroidHelper] Failed to request battery optimization: {ex.Message}");
            }
#endif
        }

        /// <summary>
        /// Keep screen on during streaming (prevents display from turning off).
        /// </summary>
        public void SetKeepScreenOn(bool keepOn)
        {
            Screen.sleepTimeout = keepOn ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            AppLog.Log($"[AndroidHelper] Screen sleep: {(keepOn ? "disabled" : "system default")}");
        }

        private void OnDestroy()
        {
            ReleaseLocks();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            // When app is paused (backgrounded), we keep the locks
            // to maintain streaming connection
            if (pauseStatus)
            {
                AppLog.Log("[AndroidHelper] App paused - keeping locks active");
            }
            else
            {
                AppLog.Log("[AndroidHelper] App resumed");
            }
        }

        private void OnApplicationQuit()
        {
            ReleaseLocks();
        }
    }
}
