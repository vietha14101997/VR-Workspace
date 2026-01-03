using UnityEngine;
using VRWorkspace.Streaming;
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
    }

    void Start()
    {
#if ADAPTIVE_PERFORMANCE_AVAILABLE
        SetupAdaptivePerformance();
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        // Request battery optimization exemption for stable streaming
        // This will prompt user once to allow the app to run without restrictions
        AndroidStreamingHelper.Instance?.RequestDisableBatteryOptimization();
#endif
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
