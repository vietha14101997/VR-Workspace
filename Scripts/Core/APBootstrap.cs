using UnityEngine;
#if ADAPTIVE_PERFORMANCE_AVAILABLE
using UnityEngine.AdaptivePerformance;
#endif

public class APBootstrap : MonoBehaviour
{
    void Awake()
    {
        Application.targetFrameRate = 60;      // Cardboard/stream: giữ 60 cho ổn định
        QualitySettings.vSyncCount = 0;        // tránh vSync cản targetFrameRate
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    void Start()
    {
#if ADAPTIVE_PERFORMANCE_AVAILABLE
        SetupAdaptivePerformance();
#endif
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
