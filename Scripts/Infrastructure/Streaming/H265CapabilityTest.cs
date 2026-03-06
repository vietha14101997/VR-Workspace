using System;
using UnityEngine;
using VRWorkspace.Native;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Quick H265 decode capability test.
    /// Checks if the device can actually initialize an HEVC hardware decoder,
    /// not just report support via MediaCodecList.
    ///
    /// Usage:
    ///   bool capable = H265CapabilityTest.IsDeviceCapable();  // cached after first call
    /// </summary>
    public static class H265CapabilityTest
    {
        private static bool _tested;
        private static bool _capable;

        /// <summary>
        /// Test if this device can decode H265.
        /// Result is cached — safe to call multiple times.
        /// Must be called on Unity main thread (JNI requirement).
        /// </summary>
        public static bool IsDeviceCapable()
        {
            if (_tested) return _capable;
            _tested = true;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Step 1: Check if MediaCodec reports HEVC support
                if (!HevcDecoderPlugin.IsAvailable())
                {
                    Debug.Log("[H265Test] MediaCodec reports no HEVC decoder available");
                    _capable = false;
                    return false;
                }

                // Step 2: Try initializing decoder with small resolution
                // Some devices report support but fail to create the codec instance
                var testDecoder = new HevcDecoderPlugin();
                bool initOk = testDecoder.Initialize(320, 240);
                testDecoder.Dispose();

                if (!initOk)
                {
                    Debug.LogWarning("[H265Test] HEVC decoder init failed (320x240 test)");
                    _capable = false;
                    return false;
                }

                Debug.Log("[H265Test] HEVC decoder init OK — device is H265 capable");
                _capable = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[H265Test] Capability test exception: {ex.Message}");
                _capable = false;
                return false;
            }
#else
            Debug.Log("[H265Test] Not Android — H265 hardware decode not available");
            _capable = false;
            return false;
#endif
        }

        /// <summary>
        /// Reset cached result (e.g., for retry after app resume).
        /// </summary>
        public static void Reset()
        {
            _tested = false;
            _capable = false;
        }
    }
}
