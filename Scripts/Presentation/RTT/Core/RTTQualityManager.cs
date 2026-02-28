using UnityEngine;
using System.Collections.Generic;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Manages RTT quality levels and memory monitoring.
    /// Extracted from RTTManager to separate concerns.
    /// </summary>
    public class RTTQualityManager
    {
        #region Fields

        private RTTQualityLevel _currentQualityLevel = RTTQualityLevel.High;
        private readonly RTTConfig _config;
        private readonly RTTPanelManager _panelManager;
        private readonly bool _enableLogging;

        private float _totalMemoryMB;
        private float _lastMemoryCheck;
        private const float MEMORY_CHECK_INTERVAL = 1.0f;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new quality manager.
        /// </summary>
        /// <param name="config">RTT configuration asset</param>
        /// <param name="panelManager">Panel manager for accessing registered panels</param>
        /// <param name="enableLogging">Enable performance logging</param>
        public RTTQualityManager(RTTConfig config, RTTPanelManager panelManager, bool enableLogging = false)
        {
            _config = config;
            _panelManager = panelManager;
            _enableLogging = enableLogging;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Current quality level
        /// </summary>
        public RTTQualityLevel CurrentQualityLevel => _currentQualityLevel;

        /// <summary>
        /// Current total texture memory in MB
        /// </summary>
        public float TotalMemoryMB => _totalMemoryMB;

        /// <summary>
        /// Maximum allowed texture memory from config
        /// </summary>
        public float MaxTextureMemoryMB => _config?.maxTextureMemoryMB ?? 150f;

        /// <summary>
        /// Whether auto-scaling is enabled
        /// </summary>
        public bool AutoScalingEnabled => _config?.enableAutoScaling ?? false;

        #endregion

        #region Quality Level Management

        /// <summary>
        /// Set the quality level and apply to all registered panels.
        /// </summary>
        public void SetQualityLevel(RTTQualityLevel level)
        {
            if (_currentQualityLevel == level) return;

            _currentQualityLevel = level;
            var preset = GetQualityPreset(level);

            if (_enableLogging)
                Debug.Log($"[RTTQualityManager] Quality changed to {level}: {preset.width}x{preset.height} AA={preset.antiAliasing}");

            ApplyQualityToAllPanels(preset);
        }

        /// <summary>
        /// Get quality preset for a level.
        /// </summary>
        public RTTQualityPreset GetQualityPreset(RTTQualityLevel level)
        {
            if (_config != null)
                return _config.GetPreset(level);

            // Fallback presets
            switch (level)
            {
                case RTTQualityLevel.Low:
                    return new RTTQualityPreset
                    {
                        name = "Low",
                        width = 1280,
                        height = 720,
                        antiAliasing = 2,
                        renderScale = 0.75f
                    };
                case RTTQualityLevel.Medium:
                    return new RTTQualityPreset
                    {
                        name = "Medium",
                        width = 1600,
                        height = 900,
                        antiAliasing = 4,
                        renderScale = 0.875f
                    };
                default: // High
                    return new RTTQualityPreset
                    {
                        name = "High",
                        width = 1920,
                        height = 1080,
                        antiAliasing = 4,
                        renderScale = 1.0f
                    };
            }
        }

        /// <summary>
        /// Apply quality preset to all registered panels.
        /// </summary>
        private void ApplyQualityToAllPanels(RTTQualityPreset preset)
        {
            if (_panelManager == null) return;

            foreach (var panel in _panelManager.RegisteredPanels)
            {
                if (panel == null) continue;
                var resolution = panel.CurrentResolution;
                int newWidth = Mathf.RoundToInt(resolution.x * preset.renderScale);
                int newHeight = Mathf.RoundToInt(resolution.y * preset.renderScale);
                panel.ResizeRenderTexture(newWidth, newHeight);
            }
        }

        #endregion

        #region Auto-Scaling

        /// <summary>
        /// Try to scale up quality if memory usage is low.
        /// </summary>
        public void TryScaleUp()
        {
            if (_currentQualityLevel == RTTQualityLevel.High) return;

            float threshold = MaxTextureMemoryMB * 0.6f;
            if (_totalMemoryMB < threshold)
            {
                var newLevel = _currentQualityLevel == RTTQualityLevel.Low
                    ? RTTQualityLevel.Medium
                    : RTTQualityLevel.High;
                SetQualityLevel(newLevel);
            }
        }

        /// <summary>
        /// Try to scale down quality if memory usage is high.
        /// </summary>
        public void TryScaleDown()
        {
            if (_currentQualityLevel == RTTQualityLevel.Low) return;

            var newLevel = _currentQualityLevel == RTTQualityLevel.High
                ? RTTQualityLevel.Medium
                : RTTQualityLevel.Low;
            SetQualityLevel(newLevel);
        }

        #endregion

        #region Memory Management

        /// <summary>
        /// Update memory statistics. Call periodically (e.g., every second).
        /// </summary>
        public void UpdateMemoryStats()
        {
            if (_panelManager != null)
                _totalMemoryMB = _panelManager.CalculateTotalTextureMemoryMB();
        }

        /// <summary>
        /// Check memory thresholds and auto-scale if needed.
        /// </summary>
        public void CheckMemoryThresholds()
        {
            if (!AutoScalingEnabled) return;

            if (_totalMemoryMB > MaxTextureMemoryMB)
                TryScaleDown();
            else if (_totalMemoryMB < MaxTextureMemoryMB * 0.5f)
                TryScaleUp();
        }

        /// <summary>
        /// Periodic update - call from MonoBehaviour.Update().
        /// Handles memory checking on interval.
        /// </summary>
        public void PeriodicUpdate()
        {
            if (Time.unscaledTime - _lastMemoryCheck >= MEMORY_CHECK_INTERVAL)
            {
                _lastMemoryCheck = Time.unscaledTime;
                UpdateMemoryStats();
                CheckMemoryThresholds();
            }
        }

        #endregion

        #region Performance Stats

        /// <summary>
        /// Get current performance statistics.
        /// </summary>
        public RTTPerformanceStats GetPerformanceStats()
        {
            return new RTTPerformanceStats
            {
                totalPanels = _panelManager?.RegisteredPanelCount ?? 0,
                visiblePanels = _panelManager?.VisiblePanelCount ?? 0,
                totalTextureMemoryMB = _totalMemoryMB,
                averageRenderTimeMs = 0f, // Would need timing data
                currentQuality = _currentQualityLevel
            };
        }

        /// <summary>
        /// Log performance stats to console.
        /// </summary>
        public void LogPerformanceStats()
        {
            var stats = GetPerformanceStats();
            Debug.Log($"[RTTQualityManager] {stats}");
        }

        #endregion
    }
}
