using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

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
            Debug.unityLogger.filterLogType = LogType.Log; // Show all logs in editor
            #else
            Debug.unityLogger.filterLogType = LogType.Log; // Show only errors in production
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

    /// <summary>
    /// Editor-only logging utility. All methods are completely stripped from builds.
    /// Use this instead of Debug.Log to ensure zero overhead in production.
    ///
    /// Usage: Replace Debug.Log("message") with AppLog.Log("message")
    /// </summary>
    public static class AppLog
    {
        /// <summary>
        /// Log a message (Editor only - stripped from builds).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void Log(object message)
        {
            Debug.Log(message);
        }

        /// <summary>
        /// Log a message with context (Editor only - stripped from builds).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void Log(object message, Object context)
        {
            Debug.Log(message, context);
        }

        /// <summary>
        /// Log a formatted message (Editor only - stripped from builds).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void LogFormat(string format, params object[] args)
        {
            Debug.LogFormat(format, args);
        }

        /// <summary>
        /// Log a warning (Editor only - stripped from builds).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void LogWarning(object message)
        {
            Debug.LogWarning(message);
        }

        /// <summary>
        /// Log a warning with context (Editor only - stripped from builds).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void LogWarning(object message, Object context)
        {
            Debug.LogWarning(message, context);
        }

        /// <summary>
        /// Log a formatted warning (Editor only - stripped from builds).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void LogWarningFormat(string format, params object[] args)
        {
            Debug.LogWarningFormat(format, args);
        }

        /// <summary>
        /// Log an error (ALWAYS included - errors should be visible in builds).
        /// </summary>
        public static void LogError(object message)
        {
            Debug.LogError(message);
        }

        /// <summary>
        /// Log an error with context (ALWAYS included).
        /// </summary>
        public static void LogError(object message, Object context)
        {
            Debug.LogError(message, context);
        }

        /// <summary>
        /// Log a formatted error (ALWAYS included).
        /// </summary>
        public static void LogErrorFormat(string format, params object[] args)
        {
            Debug.LogErrorFormat(format, args);
        }

        /// <summary>
        /// Log an exception (ALWAYS included).
        /// </summary>
        public static void LogException(System.Exception exception)
        {
            Debug.LogException(exception);
        }

        /// <summary>
        /// Log an exception with context (ALWAYS included).
        /// </summary>
        public static void LogException(System.Exception exception, Object context)
        {
            Debug.LogException(exception, context);
        }

        // === Verbose logging (completely stripped from builds) ===

        /// <summary>
        /// Verbose log for detailed debugging (Editor only).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void Verbose(object message)
        {
            Debug.Log($"[VERBOSE] {message}");
        }

        /// <summary>
        /// Log with custom tag (Editor only).
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void Tag(string tag, object message)
        {
            Debug.Log($"[{tag}] {message}");
        }
    }
}
