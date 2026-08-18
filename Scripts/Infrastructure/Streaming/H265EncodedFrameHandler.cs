using System;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Intercepts encoded H265 frames from WebRTC RTCRtpReceiver via Encoded Transform API.
    ///
    /// OPTIMIZED: ALL H265 frames (IDR + P) now come via reliable DataChannel to bypass
    /// Unity WebRTC's broken H265 RTP assembly and synchronization issues.
    /// </summary>
    public class H265EncodedFrameHandler : IDisposable
    {
        private const string TAG = "[H265EncodedHandler]";
        private readonly H265StreamReceiver _receiver;
        private bool _isFirstFrame = true;
        
        public RTCRtpScriptTransform Transform { get; private set; }

        private int _frameCount = 0;

        // ── Gating strategy ──
        private bool _decoderBootstrapped = false;
        private volatile byte[] _codecConfig;

        // ── Stopwatch for presentation timestamps ──
        private static readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

        public H265EncodedFrameHandler(H265StreamReceiver receiver)
        {
            _receiver = receiver;

            // Note: Transform is kept for API compatibility but the callback is a no-op for H265.
            Transform = new RTCRtpScriptTransform(TrackKind.Video, OnTransformedFrame);
            AppLog.Log($"{TAG} PC{_receiver.MonitorIndex} handler created (DataChannel-only mode)");
        }

        public void SetCodecConfig(byte[] annexBParamSets)
        {
            _codecConfig = annexBParamSets;
            AppLog.Log($"{TAG} PC{_receiver?.MonitorIndex} Codec config updated ({annexBParamSets.Length} bytes)");
        }

        public void FeedIdrFromDataChannel(byte[] idrAnnexBData)
        {
            if (_receiver == null || idrAnnexBData == null || idrAnnexBData.Length == 0) return;

            byte[] outputData = idrAnnexBData;
            var config = _codecConfig;

            // Ensure IDR always has VPS/SPS/PPS for first bootstrap or resync
            if (config != null && config.Length > 0 && !ContainsVps(idrAnnexBData))
            {
                outputData = new byte[config.Length + idrAnnexBData.Length];
                Buffer.BlockCopy(config, 0, outputData, 0, config.Length);
                Buffer.BlockCopy(idrAnnexBData, 0, outputData, config.Length, idrAnnexBData.Length);
            }

            _decoderBootstrapped = true;
            _frameCount++;

            if (_isFirstFrame) {
                AppLog.Log($"{TAG} PC{_receiver.MonitorIndex} FIRST IDR received: {outputData.Length} bytes");
                _isFirstFrame = false;
            }

            _receiver.OnEncodedFrameReceived(outputData, true, GetTimestampUs());
        }

        public void FeedPFrameFromDataChannel(byte[] pframeAnnexBData)
        {
            if (_receiver == null || pframeAnnexBData == null || pframeAnnexBData.Length == 0) return;
            if (!_decoderBootstrapped) return; // Drop P-frames until first IDR

            _frameCount++;
            _receiver.OnEncodedFrameReceived(pframeAnnexBData, false, GetTimestampUs());
        }

        private void OnTransformedFrame(RTCTransformEvent e)
        {
            // RTP path disabled for H265
        }

        private static bool ContainsVps(byte[] data)
        {
            for (int i = 0; i < data.Length - 4; i++)
            {
                int startSize = 0;
                if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1)
                    startSize = 4;
                else if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
                    startSize = 3;

                if (startSize > 0 && i + startSize < data.Length)
                {
                    int type = (data[i + startSize] >> 1) & 0x3F;
                    if (type == 32) return true;
                    i += startSize;
                }
            }
            return false;
        }

        private static long GetTimestampUs()
            => _stopwatch.ElapsedTicks * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

        public void ResetBootstrapGate()
        {
            if (_decoderBootstrapped)
            {
                _decoderBootstrapped = false;
                AppLog.Log($"{TAG} PC{_receiver?.MonitorIndex} Bootstrap gate RE-CLOSED.");
            }
        }

        public void ResetState()
        {
            _decoderBootstrapped = false;
            _isFirstFrame = true;
            _frameCount = 0;
            AppLog.Log($"{TAG} PC{_receiver?.MonitorIndex} State reset.");
        }

        public void Dispose()
        {
            _codecConfig = null;
            Transform?.Dispose();
            Transform = null;
        }
    }
}
