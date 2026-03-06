using System;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;
using Unity.Collections;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Intercepts encoded H265 frames from WebRTC RTCRtpReceiver via Encoded Transform API.
    /// Forwards the encoded payload to H265StreamReceiver for decoding.
    ///
    /// KEY INSIGHT: Large IDR keyframes (100-200KB) are fragmented into 30-150+ RTP FU
    /// packets. The Encoded Transform API does NOT reassemble them — it delivers each
    /// small RTP payload individually as type=TRAIL(0). Real IDR keyframes NEVER arrive
    /// through this API.
    ///
    /// SOLUTION: Server sends VPS/SPS/PPS + IDR keyframe data via reliable DataChannel.
    /// This handler gates on codec config arrival, then accepts P-frames from the
    /// Encoded Transform once the decoder has been bootstrapped with IDR from DataChannel.
    ///
    /// Available in unity-webrtc 3.0.0+.
    /// </summary>
    public class H265EncodedFrameHandler : IDisposable
    {
        private const string TAG = "[H265EncodedHandler]";
        private readonly H265StreamReceiver _receiver;
        private bool _isFirstFrame = true;
        
        public RTCRtpScriptTransform Transform { get; private set; }

        private int _packetCount = 0;
        private int _frameCount = 0;
        private int _droppedBeforeConfig = 0;
        private static readonly byte[] AnnexBPrefix = { 0x00, 0x00, 0x00, 0x01 };

        // ── Gating strategy ──
        // IDR keyframes NEVER arrive via Encoded Transform (too large, fragmented into
        // many RTP packets that get lost). Instead:
        // 1. Wait for codec config (VPS/SPS/PPS) from DataChannel
        // 2. Wait for IDR keyframe data from DataChannel (fed directly to decoder)
        // 3. Then accept P-frames from Encoded Transform
        private bool _waitingForCodecConfig = true;  // Gate: need VPS/SPS/PPS
        private bool _decoderBootstrapped = false;    // True after IDR fed via DataChannel

        // ── Codec config (VPS/SPS/PPS) received via DataChannel side-channel ──
        private volatile byte[] _codecConfig;  // Annex-B VPS+SPS+PPS bytes
        private bool _codecConfigApplied;       // True after first successful prepend

        // ── FU reassembly state (Mode B only) ──
        private List<byte> _fuBuffer;
        private int _fuOriginalNalType = -1;
        private int _fuLayerId;
        private int _fuTid;

        // ── Frame assembly state: group NALUs by RTP timestamp (Mode B only) ──
        private uint _currentTimestamp;
        private List<byte[]> _currentFrameNals;
        private bool _currentFrameHasKeyNal;

        // ── Stopwatch for presentation timestamps ──
        private static readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

        public H265EncodedFrameHandler(H265StreamReceiver receiver)
        {
            _receiver = receiver;
            _currentFrameNals = new List<byte[]>();
            _fuBuffer = new List<byte>();
            
            Transform = new RTCRtpScriptTransform(TrackKind.Video, OnTransformedFrame);
            Debug.Log($"{TAG} PC{_receiver.MonitorIndex} handler created (gates on DataChannel codec config + IDR)");
        }

        /// <summary>
        /// Receive VPS/SPS/PPS from DataChannel side-channel.
        /// Called by PhaseProtocolClient when type=0x02 message arrives.
        /// </summary>
        public void SetCodecConfig(byte[] annexBParamSets)
        {
            _codecConfig = annexBParamSets;
            _codecConfigApplied = false;
            
            // Open the config gate
            if (_waitingForCodecConfig)
            {
                _waitingForCodecConfig = false;
                Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} ★ Codec config gate OPENED ({annexBParamSets.Length} bytes)");
            }
            
            // Log NAL types in the config
            var sb = new System.Text.StringBuilder();
            sb.Append($"{TAG} PC{_receiver?.MonitorIndex} Codec config ({annexBParamSets.Length} bytes): ");
            for (int i = 0; i < annexBParamSets.Length - 4; i++)
            {
                int sc = 0;
                if (annexBParamSets[i] == 0 && annexBParamSets[i+1] == 0 && annexBParamSets[i+2] == 0 && annexBParamSets[i+3] == 1) sc = 4;
                else if (i + 2 < annexBParamSets.Length && annexBParamSets[i] == 0 && annexBParamSets[i+1] == 0 && annexBParamSets[i+2] == 1) sc = 3;
                if (sc > 0 && i + sc < annexBParamSets.Length)
                {
                    int t = (annexBParamSets[i + sc] >> 1) & 0x3F;
                    string desc = t == 32 ? "VPS" : t == 33 ? "SPS" : t == 34 ? "PPS" : $"NAL({t})";
                    sb.Append($"{desc} ");
                    i += sc;
                }
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Feed an IDR keyframe received via DataChannel directly to the decoder.
        /// This bypasses the Encoded Transform entirely — the DataChannel delivers
        /// the full IDR reliably (SCTP), unlike RTP which fragments and loses it.
        /// </summary>
        public void FeedIdrFromDataChannel(byte[] idrAnnexBData)
        {
            if (_receiver == null || idrAnnexBData == null || idrAnnexBData.Length == 0) return;

            // Prepend codec config if we have it and the IDR doesn't already contain VPS/SPS/PPS
            byte[] outputData = idrAnnexBData;
            var config = _codecConfig;
            if (config != null && config.Length > 0 && !ContainsVps(idrAnnexBData))
            {
                outputData = new byte[config.Length + idrAnnexBData.Length];
                Buffer.BlockCopy(config, 0, outputData, 0, config.Length);
                Buffer.BlockCopy(idrAnnexBData, 0, outputData, config.Length, idrAnnexBData.Length);
            }

            Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} ★ IDR from DataChannel: {outputData.Length} bytes (raw IDR={idrAnnexBData.Length})");

            _decoderBootstrapped = true;
            _frameCount++;
            _receiver.OnEncodedFrameReceived(outputData, true, GetTimestampUs());
        }

        /// <summary>
        /// Feed a P-frame received via DataChannel directly to the decoder.
        /// DEPRECATED: In hybrid mode, P-frames arrive via RTP Encoded Transform.
        /// Kept as fallback if server still sends type 0x04 messages.
        /// </summary>
        public void FeedPFrameFromDataChannel(byte[] pframeAnnexBData)
        {
            if (_receiver == null || pframeAnnexBData == null || pframeAnnexBData.Length == 0) return;

            _frameCount++;
            if (_frameCount <= 10 || _frameCount % 300 == 0)
                Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} P-frame from DataChannel: {pframeAnnexBData.Length} bytes (frame #{_frameCount})");

            _decoderBootstrapped = true;
            _receiver.OnEncodedFrameReceived(pframeAnnexBData, false, GetTimestampUs());
        }

        /// <summary>
        /// Callback for transformed video frames.
        /// </summary>
        private void OnTransformedFrame(RTCTransformEvent e)
        {
            _packetCount++;
            if (e.Frame == null || _receiver == null) return;

            try
            {
                if (e.Frame is RTCEncodedVideoFrame videoFrame)
                {
                    ProcessFrame(videoFrame);
                }
                else
                {
                    if (_packetCount % 300 == 0)
                        Debug.LogWarning($"{TAG} Received non-video frame type: {e.Frame.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{TAG} Error processing frame: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Process a single callback from the Encoded Transform.
        /// Auto-detects whether data is a complete Annex-B frame, raw NAL, or FU packet.
        /// </summary>
        private void ProcessFrame(RTCEncodedVideoFrame frame)
        {
            var nativeData = frame.GetData();
            if (nativeData.Length < 2) return;

            byte[] data = new byte[nativeData.Length];
            nativeData.CopyTo(data);

            uint rtpTimestamp = frame.Timestamp;

            // ── HEX DUMP for first 10 packets to understand data format ──
            if (_packetCount <= 10)
            {
                int dumpLen = Math.Min(data.Length, 32);
                var hex = new System.Text.StringBuilder();
                for (int i = 0; i < dumpLen; i++)
                    hex.Append(data[i].ToString("X2")).Append(" ");
                Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} RAW pkt#{_packetCount}: len={data.Length}, ts={rtpTimestamp}, WebRTC_Type={frame.Type}, hex=[{hex}]");
            }

            // ── HYBRID MODE: IDR comes via DataChannel, P-frames come via RTP here. ──
            // Gate: Wait for decoder bootstrap (IDR fed via DataChannel) before accepting
            // RTP P-frames. Without IDR reference, P-frames are useless to the decoder.
            if (!_decoderBootstrapped)
            {
                _droppedBeforeConfig++;
                if (_droppedBeforeConfig <= 5 || _droppedBeforeConfig % 500 == 0)
                    Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} Dropped (waiting for IDR bootstrap): #{_droppedBeforeConfig}, len={data.Length}");
                return;
            }

            // ── MODE A CHECK: Does data already contain Annex-B start codes? ──
            if (HasAnnexBStartCode(data))
            {
                FlushCurrentFrame();

                bool hasRealIdr = ContainsIdrNal(data);
                bool isKeyFrame = hasRealIdr;

                byte[] outputData = PrependCodecConfigIfNeeded(data, hasRealIdr, ref isKeyFrame);

                _frameCount++;
                if (_isFirstFrame)
                {
                    Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} ★ FIRST FRAME (Annex-B passthrough): len={outputData.Length}, key={isKeyFrame}");
                    LogNalBreakdown(outputData);
                    _isFirstFrame = false;
                }
                if (_frameCount % 300 == 0 || _frameCount < 10 || isKeyFrame)
                    Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} Frame #{_frameCount}: len={outputData.Length}, key={isKeyFrame}, type=AnnexB");

                _receiver.OnEncodedFrameReceived(outputData, isKeyFrame, GetTimestampUs());
                return;
            }

            // ── MODE B: Raw NAL unit (no Annex-B start codes) ──
            // Server sends each H265 NAL as a single RTP packet with a dummy prefix byte
            // that SIPSorcery strips. The Encoded Transform delivers the complete NAL with
            // its 2-byte HEVC header intact: [nalType<<1|flags, layerId<<3|tid, payload...]
            int nalType = (data[0] >> 1) & 0x3F;

            if (_packetCount <= 5 || _packetCount % 500 == 0)
            {
                string typeDesc = nalType == 49 ? "FU" : nalType == 48 ? "AP" :
                                  nalType == 32 ? "VPS" : nalType == 33 ? "SPS" : nalType == 34 ? "PPS" :
                                  (nalType >= 19 && nalType <= 21) ? "IDR" :
                                  nalType <= 9 ? "TRAIL" : $"NAL({nalType})";
                Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} pkt#{_packetCount}: type={nalType}({typeDesc}), len={data.Length}, ts={rtpTimestamp}");
            }

            // Check if we moved to a new frame (different RTP timestamp)
            if (_currentFrameNals.Count > 0 && rtpTimestamp != _currentTimestamp)
                FlushCurrentFrame();
            _currentTimestamp = rtpTimestamp;

            if (nalType == 49)
            {
                ProcessFuPacket(data);
            }
            else if (nalType == 48)
            {
                ProcessApPacket(data);
            }
            else if (nalType >= 0 && nalType <= 47)
            {
                // ── Single NAL Unit — wrap with Annex-B prefix ──
                byte[] nalWithPrefix = new byte[4 + data.Length];
                Buffer.BlockCopy(AnnexBPrefix, 0, nalWithPrefix, 0, 4);
                Buffer.BlockCopy(data, 0, nalWithPrefix, 4, data.Length);
                _currentFrameNals.Add(nalWithPrefix);

                if (nalType == 19 || nalType == 20 || nalType == 32 || nalType == 33 || nalType == 34)
                    _currentFrameHasKeyNal = true;
            }
        }

        /// <summary>
        /// Process RFC 7798 Fragmentation Unit packet.
        /// </summary>
        private void ProcessFuPacket(byte[] data)
        {
            if (data.Length < 3) return;

            int layerId = ((data[0] & 0x01) << 5) | ((data[1] >> 3) & 0x1F);
            int tid = data[1] & 0x07;

            byte fuHeader = data[2];
            bool isStart = (fuHeader & 0x80) != 0;
            bool isEnd = (fuHeader & 0x40) != 0;
            int fuType = fuHeader & 0x3F;

            if (isStart)
            {
                _fuBuffer.Clear();
                _fuOriginalNalType = fuType;
                _fuLayerId = layerId;
                _fuTid = tid;
            }

            if (_fuOriginalNalType < 0) return;

            for (int i = 3; i < data.Length; i++)
                _fuBuffer.Add(data[i]);

            if (isEnd)
            {
                byte nalHdr0 = (byte)((_fuOriginalNalType << 1) | (_fuLayerId >> 5));
                byte nalHdr1 = (byte)((_fuLayerId << 3) | _fuTid);

                byte[] completeNal = new byte[4 + 2 + _fuBuffer.Count];
                Buffer.BlockCopy(AnnexBPrefix, 0, completeNal, 0, 4);
                completeNal[4] = nalHdr0;
                completeNal[5] = nalHdr1;
                _fuBuffer.CopyTo(completeNal, 6);

                _currentFrameNals.Add(completeNal);

                if (_fuOriginalNalType == 19 || _fuOriginalNalType == 20 || 
                    _fuOriginalNalType == 32 || _fuOriginalNalType == 33 || _fuOriginalNalType == 34)
                    _currentFrameHasKeyNal = true;

                if (_packetCount <= 20 || _currentFrameHasKeyNal)
                {
                    string nalDesc = _fuOriginalNalType == 32 ? "VPS" : _fuOriginalNalType == 33 ? "SPS" : 
                                    _fuOriginalNalType == 34 ? "PPS" :
                                    (_fuOriginalNalType >= 19 && _fuOriginalNalType <= 20) ? "IDR" : 
                                    _fuOriginalNalType <= 9 ? "TRAIL" : $"NAL({_fuOriginalNalType})";
                    Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} FU reassembled: type={_fuOriginalNalType}({nalDesc}), size={completeNal.Length}");
                }

                _fuBuffer.Clear();
                _fuOriginalNalType = -1;
            }
        }

        /// <summary>
        /// Process RFC 7798 Aggregation Packet (type 48).
        /// </summary>
        private void ProcessApPacket(byte[] data)
        {
            if (data.Length < 4) return;
            int offset = 2;
            while (offset + 2 <= data.Length)
            {
                int naluSize = (data[offset] << 8) | data[offset + 1];
                offset += 2;
                if (offset + naluSize > data.Length) break;

                byte[] nalWithPrefix = new byte[4 + naluSize];
                Buffer.BlockCopy(AnnexBPrefix, 0, nalWithPrefix, 0, 4);
                Buffer.BlockCopy(data, offset, nalWithPrefix, 4, naluSize);
                _currentFrameNals.Add(nalWithPrefix);

                if (naluSize >= 2)
                {
                    int t = (data[offset] >> 1) & 0x3F;
                    if (t == 19 || t == 20 || t == 32 || t == 33 || t == 34)
                        _currentFrameHasKeyNal = true;
                }
                offset += naluSize;
            }
        }

        /// <summary>
        /// Flush accumulated NAL units as one complete Annex-B frame to the decoder.
        /// </summary>
        private void FlushCurrentFrame()
        {
            if (_currentFrameNals.Count == 0) return;

            int totalSize = 0;
            foreach (var nal in _currentFrameNals) totalSize += nal.Length;

            byte[] frameData = new byte[totalSize];
            int pos = 0;
            foreach (var nal in _currentFrameNals)
            {
                Buffer.BlockCopy(nal, 0, frameData, pos, nal.Length);
                pos += nal.Length;
            }

            bool hasRealIdr = _currentFrameHasKeyNal && ContainsIdrNal(frameData);
            bool isKeyFrame = hasRealIdr;

            // For P-frames: just pass through (decoder already has IDR reference from DataChannel)
            // For IDR frames: prepend codec config
            byte[] outputData = PrependCodecConfigIfNeeded(frameData, hasRealIdr, ref isKeyFrame);

            _frameCount++;

            if (_isFirstFrame)
            {
                Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} ★ FIRST FRAME (reassembled): nalCount={_currentFrameNals.Count}, totalLen={outputData.Length}, isKey={isKeyFrame}");
                LogNalBreakdown(outputData);
                _isFirstFrame = false;
            }
            if (_frameCount % 300 == 0 || _frameCount < 20)
                Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} Frame #{_frameCount}: {_currentFrameNals.Count} NALs, {outputData.Length} bytes, key={isKeyFrame}");

            _receiver.OnEncodedFrameReceived(outputData, isKeyFrame, GetTimestampUs());

            _currentFrameNals.Clear();
            _currentFrameHasKeyNal = false;
        }

        // ── Helper methods ──

        private static bool HasAnnexBStartCode(byte[] data)
        {
            if (data.Length < 4) return false;
            return (data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 1) ||
                   (data[0] == 0 && data[1] == 0 && data[2] == 1);
        }

        /// <summary>
        /// Check if an Annex-B stream contains a real IDR NAL unit (type 19 or 20).
        /// </summary>
        private static bool ContainsIdrNal(byte[] data)
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
                    if (type == 19 || type == 20)
                        return true;
                    i += startSize;
                }
            }
            return false;
        }

        /// <summary>
        /// Check if data contains VPS NAL (type 32).
        /// </summary>
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
                    if (type == 32)
                        return true;
                    i += startSize;
                }
            }
            return false;
        }

        private void LogNalBreakdown(byte[] data)
        {
            int nalIndex = 0;
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
                    string desc = type == 32 ? "VPS" : type == 33 ? "SPS" : type == 34 ? "PPS" :
                                  type == 19 ? "IDR_W_RADL" : type == 20 ? "IDR_N_LP" :
                                  type <= 9 ? $"TRAIL({type})" : $"OTHER({type})";
                    Debug.Log($"{TAG}   NAL[{nalIndex}] @ offset {i}: type={type} ({desc})");
                    nalIndex++;
                    i += startSize;
                }
            }
        }

        /// <summary>
        /// Prepend VPS/SPS/PPS codec config to frame data.
        /// Only prepends to real IDR frames.
        /// </summary>
        private byte[] PrependCodecConfigIfNeeded(byte[] frameData, bool hasRealIdr, ref bool isKeyFrame)
        {
            var config = _codecConfig;
            if (config == null || config.Length == 0) return frameData;
            if (!hasRealIdr) return frameData;
            if (_codecConfigApplied && isKeyFrame) return frameData;

            byte[] combined = new byte[config.Length + frameData.Length];
            Buffer.BlockCopy(config, 0, combined, 0, config.Length);
            Buffer.BlockCopy(frameData, 0, combined, config.Length, frameData.Length);

            if (!_codecConfigApplied)
            {
                Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} ★ Prepended codec config ({config.Length} bytes) to IDR ({frameData.Length} bytes) → {combined.Length} bytes");
                _codecConfigApplied = true;
            }

            isKeyFrame = true;
            return combined;
        }

        private static long GetTimestampUs()
            => _stopwatch.ElapsedTicks * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>
        /// Reset internal state for reconnection.
        /// </summary>
        public void ResetState()
        {
            _waitingForCodecConfig = true;
            _decoderBootstrapped = false;
            _codecConfigApplied = false;
            _droppedBeforeConfig = 0;
            _isFirstFrame = true;
            _frameCount = 0;
            _packetCount = 0;
            _currentFrameNals.Clear();
            _currentFrameHasKeyNal = false;
            _fuBuffer.Clear();
            _fuOriginalNalType = -1;
            Debug.Log($"{TAG} PC{_receiver?.MonitorIndex} State reset (waiting for codec config + IDR from DataChannel)");
        }

        public void Dispose()
        {
            FlushCurrentFrame();
            _codecConfig = null;
            Transform?.Dispose();
            Transform = null;
        }
    }
}
