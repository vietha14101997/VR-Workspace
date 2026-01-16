using UnityEngine;

namespace VRWorkspace.Core
{
    /// <summary>
    /// Central logging control for the entire application.
    /// Disables verbose logging and stack traces to reduce RAM usage, heat, and FPS drops.
    /// </summary>
    public static class LogManager
    {
        /// <summary>
        /// Enable/disable all Debug.Log output.
        /// Set to false in production builds to maximize performance.
        /// </summary>
        public static bool EnableLogging
        {
            get => Debug.unityLogger.logEnabled;
            set => Debug.unityLogger.logEnabled = value;
        }

        /// <summary>
        /// Set minimum log level (Log, Warning, Error, Exception, Assert).
        /// Default: Warning (skips Debug.Log, keeps warnings and errors).
        /// </summary>
        public static LogType MinLogLevel
        {
            get => Debug.unityLogger.filterLogType;
            set => Debug.unityLogger.filterLogType = value;
        }

        /// <summary>
        /// Initialize logging settings for production.
        /// Automatically called before first scene load.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InitializeProductionLogging()
        {
            // Configure stack trace settings for performance
            // Stack traces are VERY expensive - disable for non-critical logs
            ConfigureStackTraces();

            // ALWAYS disable verbose Debug.Log to reduce RAM/CPU/heat
            // Only show errors in production, warnings+errors in editor
            #if UNITY_EDITOR
            // TEMP: Enable all logs for debugging breadcrumbs
            Debug.unityLogger.filterLogType = LogType.Log;
            // Original: Debug.unityLogger.filterLogType = LogType.Warning;
            #else
            // In builds (Quest, Android, etc), only show errors
            Debug.unityLogger.filterLogType = LogType.Error;
            #endif
        }

        /// <summary>
        /// Configure stack trace logging for optimal performance.
        /// Stack traces are expensive - capture call stack for every log.
        ///
        /// Options:
        /// - None: No stack trace (fastest, recommended for production)
        /// - ScriptOnly: Only C# scripts (medium, good for debugging)
        /// - Full: Full native + managed stack (slowest, for crash analysis)
        /// </summary>
        public static void ConfigureStackTraces()
        {
            #if UNITY_EDITOR
            // Editor: ScriptOnly for Log/Warning, Full for Error/Exception
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.ScriptOnly);
            #else
            // Production builds: Disable ALL stack traces for maximum performance
            // Exceptions still get stack trace for crash reporting
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.None);
            #endif
        }

        /// <summary>
        /// Disable all stack traces (maximum performance mode).
        /// </summary>
        public static void DisableAllStackTraces()
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.None);
        }

        /// <summary>
        /// Enable full stack traces (debugging mode).
        /// </summary>
        public static void EnableFullStackTraces()
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.Full);
        }

        /// <summary>
        /// Force disable all logging (for maximum performance).
        /// </summary>
        public static void DisableAllLogging()
        {
            Debug.unityLogger.logEnabled = false;
        }

        /// <summary>
        /// Force enable all logging (for debugging).
        /// </summary>
        public static void EnableAllLogging()
        {
            Debug.unityLogger.logEnabled = true;
            Debug.unityLogger.filterLogType = LogType.Log;
        }

        /// <summary>
        /// Set to warning-only mode (recommended for production).
        /// </summary>
        public static void SetWarningOnlyMode()
        {
            Debug.unityLogger.logEnabled = true;
            Debug.unityLogger.filterLogType = LogType.Warning;
        }
    }
}
