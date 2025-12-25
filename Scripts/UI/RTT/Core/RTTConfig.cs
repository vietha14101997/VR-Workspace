using UnityEngine;

/// <summary>
/// ScriptableObject configuration cho RTT (Render-to-Texture) system.
/// Cho phép tuning các thông số RTT mà không cần recompile.
/// </summary>
[CreateAssetMenu(fileName = "RTTConfig", menuName = "VR-Workspace/RTT Config")]
public class RTTConfig : ScriptableObject
{
    [Header("Render Texture Settings")]
    [Tooltip("Default width of render texture")]
    public int defaultWidth = 1920;

    [Tooltip("Default height of render texture")]
    public int defaultHeight = 1080;

    [Range(1, 8)]
    [Tooltip("Anti-aliasing samples (1, 2, 4, 8)")]
    public int antiAliasing = 4;

    [Tooltip("Render texture format")]
    public RenderTextureFormat format = RenderTextureFormat.ARGB32;

    [Tooltip("Texture filter mode")]
    public FilterMode filterMode = FilterMode.Bilinear;

    [Header("Quality Presets")]
    public RTTQualityPreset lowQuality = new RTTQualityPreset
    {
        name = "Low",
        width = 1280,
        height = 720,
        antiAliasing = 2,
        renderScale = 0.75f
    };

    public RTTQualityPreset mediumQuality = new RTTQualityPreset
    {
        name = "Medium",
        width = 1600,
        height = 900,
        antiAliasing = 4,
        renderScale = 0.875f
    };

    public RTTQualityPreset highQuality = new RTTQualityPreset
    {
        name = "High",
        width = 1920,
        height = 1080,
        antiAliasing = 4,
        renderScale = 1.0f
    };

    [Header("Performance")]
    [Tooltip("Only re-render when UI changes (dirty flag optimization)")]
    public bool useDirtyFlag = true;

    [Tooltip("Maximum frames to skip between renders (0 = always render)")]
    [Range(0, 10)]
    public int maxFrameSkip = 2;

    [Tooltip("Delay before applying dirty flag (for animations)")]
    [Range(0f, 1f)]
    public float dirtyFlagDelay = 0.1f;

    [Header("Memory Management")]
    [Tooltip("Maximum texture memory in MB before auto-scaling")]
    public float maxTextureMemoryMB = 128f;

    [Tooltip("Warning threshold in MB")]
    public float warningThresholdMB = 100f;

    [Tooltip("Enable automatic quality scaling based on memory")]
    public bool enableAutoScaling = true;

    [Header("Debug")]
    [Tooltip("Show debug gizmos in editor")]
    public bool showDebugGizmos = false;

    [Tooltip("Log performance metrics to console")]
    public bool logPerformanceMetrics = false;

    [Tooltip("Show debug overlay in game")]
    public bool showDebugOverlay = false;

    /// <summary>
    /// Get quality preset by level
    /// </summary>
    public RTTQualityPreset GetPreset(RTTQualityLevel level)
    {
        switch (level)
        {
            case RTTQualityLevel.Low:
                return lowQuality;
            case RTTQualityLevel.Medium:
                return mediumQuality;
            case RTTQualityLevel.High:
            default:
                return highQuality;
        }
    }
}

/// <summary>
/// Quality preset for RTT rendering
/// </summary>
[System.Serializable]
public class RTTQualityPreset
{
    public string name;
    public int width;
    public int height;
    public int antiAliasing;
    public float renderScale = 1.0f;

    public Vector2Int Resolution => new Vector2Int(
        Mathf.RoundToInt(width * renderScale),
        Mathf.RoundToInt(height * renderScale)
    );
}

/// <summary>
/// Quality levels for RTT system
/// </summary>
public enum RTTQualityLevel
{
    Low,
    Medium,
    High
}

/// <summary>
/// Performance statistics for RTT system
/// </summary>
[System.Serializable]
public struct RTTPerformanceStats
{
    public int totalPanels;
    public int visiblePanels;
    public float totalTextureMemoryMB;
    public float averageRenderTimeMs;
    public RTTQualityLevel currentQuality;

    public override string ToString()
    {
        return $"Panels: {visiblePanels}/{totalPanels}, Memory: {totalTextureMemoryMB:F1}MB, Quality: {currentQuality}";
    }
}
