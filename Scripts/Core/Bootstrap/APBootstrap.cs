using UnityEngine;
using System.Collections;
using VRWorkspace.Streaming;

namespace VRWorkspace.Core
{
    #if ADAPTIVE_PERFORMANCE_AVAILABLE
    using UnityEngine.AdaptivePerformance;
    #endif

    public class APBootstrap : MonoBehaviour
    {
        void Awake()
        {
            Application.targetFrameRate = 60;      // Cardboard/stream: giữ 60 cho ổn định
            QualitySettings.vSyncCount = 1;        // BẬT vSync để tránh tearing (sọc chéo)
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // Initialize Android streaming helper for Wi-Fi Lock, Wake Lock, etc.
            InitializeAndroidStreamingHelper();

            // Initialize Permission Manager
            InitializePermissionManager();
        }

        void Start()
        {
    #if ADAPTIVE_PERFORMANCE_AVAILABLE
            SetupAdaptivePerformance();
    #endif

    #if UNITY_ANDROID && !UNITY_EDITOR
            // Defer permission requests to avoid blocking startup rendering.
            // JNI calls (GetAndroidSDKVersion, HasManageExternalStorage, etc.) block main thread.
            // Wait a few frames so UI renders first, then request permissions in background.
            StartCoroutine(DeferredPermissionRequest());
    #endif
        }

    #if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator DeferredPermissionRequest()
        {
            // Wait 3 frames to let Cardboard XR + RTT UI fully initialize and render
            yield return null;
            yield return null;
            yield return null;

            AppPermissionManager.Instance?.RequestAllPermissions((allGranted) =>
            {
                Debug.Log($"[APBootstrap] Permissions complete. All granted: {allGranted}");
                AndroidStreamingHelper.Instance?.RequestDisableBatteryOptimization();
            });
        }
    #endif

        private void InitializePermissionManager()
        {
            if (AppPermissionManager.Instance == null)
            {
                var go = new GameObject("AppPermissionManager");
                go.AddComponent<AppPermissionManager>();
                DontDestroyOnLoad(go);
                Debug.Log("[APBootstrap] AppPermissionManager initialized");
            }
        }

        private void InitializeAndroidStreamingHelper()
        {
            // Create AndroidStreamingHelper if not exists (singleton pattern)
            if (AndroidStreamingHelper.Instance == null)
            {
                var go = new GameObject("AndroidStreamingHelper");
                go.AddComponent<AndroidStreamingHelper>();
                DontDestroyOnLoad(go);
                Debug.Log("[APBootstrap] AndroidStreamingHelper initialized");
            }
        }

    #if ADAPTIVE_PERFORMANCE_AVAILABLE
        void SetupAdaptivePerformance()
        {
            var ap = Holder.Instance?.AdaptivePerformance;
            if (ap == null) return;

            // Giới hạn mức boost để tránh máy nóng quá
            ap.Indexer.Settings.maxCpuPerformanceLevel = 2;
            ap.Indexer.Settings.maxGpuPerformanceLevel = 2;

            // Chỉ cho phép giảm render scale nhẹ
            var res = ap.Indexer.AdaptivePerformanceScalers.AdaptiveResolution;
            if (res != null) { res.MinScale = 0.85f; res.MaxScale = 1.0f; }

            // Không cho phép Adaptive Framerate kéo FPS xuống
            var fps = ap.Indexer.AdaptivePerformanceScalers.AdaptiveFramerate;
            if (fps != null) fps.Enabled = false;
        }
    #endif
    }

}
