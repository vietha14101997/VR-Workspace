using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// RFC 7798 compliant H265 RTP packet assembler.
    /// Reassembles RTP payloads into complete H265 NAL units with Annex-B start codes.
    ///
    /// Handles:
    ///   - Single NAL Unit packets (nal_unit_type 1-47)
    ///   - Aggregation Packets / AP (nal_unit_type 48)
    ///   - Fragmentation Units / FU (nal_unit_type 49)
    ///
    /// Output: complete NAL units prefixed with Annex-B start code {0x00,0x00,0x00,0x01}
    /// </summary>
    public class H265RtpAssembler
    {
        private const string TAG = "[H265RtpAssembler]";

        // Annex-B 4-byte start code
        private static readonly byte[] StartCode = { 0x00, 0x00, 0x00, 0x01 };

        // H265 NAL unit types (RFC 7798)
        private const int NAL_TYPE_AP = 48;   // Aggregation Packet
        private const int NAL_TYPE_FU = 49;   // Fragmentation Unit

        // FU fragmentation state
        private List<byte[]> _fuFragments = new List<byte[]>();
        private bool _inFuSession;
        private uint _fuSequenceBase;
        private byte _fuNalType;    // reconstructed NAL type inside FU
        private byte _fuNalHeader1; // first byte of original NAL header (for type reconstruction)
        private byte _fuNalHeader2; // second byte
        private bool _fuKeyFrame;

        // Callback: fires when a complete NAL is ready
        // Parameters: nalData (Annex-B), isKeyFrame, timestamp
        public event Action<byte[], bool, long> OnNalUnitReady;

        /// <summary>
        /// Reset assembler state (call on reconnect / stream discontinuity).
        /// </summary>
        public void Reset()
        {
            _fuFragments.Clear();
            _inFuSession = false;
            _fuKeyFrame = false;
        }

        /// <summary>
        /// Process one RTP payload (already decrypted SRTP payload, no RTP header).
        /// </summary>
        /// <param name="payload">Raw RTP payload bytes</param>
        /// <param name="isKeyFrame">Whether this is a keyframe (from RTP extension or signaling)</param>
        /// <param name="timestamp">RTP timestamp of the frame</param>
        public void ProcessRtpPayload(byte[] payload, bool isKeyFrame = false, long timestamp = 0)
        {
            if (payload == null || payload.Length < 2) return;

            // H265 RTP payload header is 2 bytes (RFC 7798 §4.4.1)
            // Byte 0: F(1) | nal_unit_type(6) | layer_id high(1)
            // Byte 1: layer_id low(5) | TID(3)
            int nalUnitType = (payload[0] >> 1) & 0x3F;

            if (nalUnitType == NAL_TYPE_FU)
            {
                ProcessFuPacket(payload, isKeyFrame, timestamp);
            }
            else if (nalUnitType == NAL_TYPE_AP)
            {
                ProcessApPacket(payload, isKeyFrame, timestamp);
            }
            else
            {
                // Single NAL unit packet — emit directly with Annex-B header
                EmitNal(payload, isKeyFrame, timestamp);
            }
        }

        // ─────────────────────────────────────────────────────
        // FU (Fragmentation Unit) processing
        // ─────────────────────────────────────────────────────

        private void ProcessFuPacket(byte[] payload, bool isKeyFrame, long timestamp)
        {
            if (payload.Length < 3) return;

            // FU header (3rd byte after 2-byte RTP NAL header):
            // S(1) | E(1) | nal_unit_type(6)
            byte fuHeader = payload[2];
            bool isStart = (fuHeader & 0x80) != 0;
            bool isEnd   = (fuHeader & 0x40) != 0;
            byte innerNalType = (byte)(fuHeader & 0x3F);

            if (isStart)
            {
                // Begin new FU session — drop any incomplete previous session
                _fuFragments.Clear();
                _inFuSession = true;
                _fuNalType = innerNalType;
                _fuNalHeader1 = payload[0];
                _fuNalHeader2 = payload[1];
                _fuKeyFrame = isKeyFrame;
            }
            else if (!_inFuSession)
            {
                // Middle/end fragment arrived before start — discard
                Debug.LogWarning($"{TAG} FU fragment arrived before start, discarding");
                return;
            }

            // Collect fragment data (skip 2-byte RTP NAL header + 1-byte FU header = 3 bytes)
            if (payload.Length > 3)
            {
                byte[] fragment = new byte[payload.Length - 3];
                Buffer.BlockCopy(payload, 3, fragment, 0, fragment.Length);
                _fuFragments.Add(fragment);
            }

            if (isEnd && _inFuSession)
            {
                // Reconstruct full NAL unit:
                // 2-byte NAL header (modified to inner NAL type) + all fragments
                byte nalHeader0 = (byte)((_fuNalHeader1 & 0x81) | ((_fuNalType & 0x3F) << 1));
                byte nalHeader1 = _fuNalHeader2;

                // Calculate total size
                int totalSize = 2; // reconstructed NAL header
                foreach (var frag in _fuFragments) totalSize += frag.Length;

                byte[] nal = new byte[totalSize];
                nal[0] = nalHeader0;
                nal[1] = nalHeader1;
                int offset = 2;
                foreach (var frag in _fuFragments)
                {
                    Buffer.BlockCopy(frag, 0, nal, offset, frag.Length);
                    offset += frag.Length;
                }

                EmitNal(nal, _fuKeyFrame, timestamp);

                // Reset state
                _fuFragments.Clear();
                _inFuSession = false;
            }
        }

        // ─────────────────────────────────────────────────────
        // AP (Aggregation Packet) processing
        // ─────────────────────────────────────────────────────

        private void ProcessApPacket(byte[] payload, bool isKeyFrame, long timestamp)
        {
            // AP format: 2-byte RTP NAL header, then one or more:
            //   [2-byte size][nal unit data]
            int offset = 2; // skip 2-byte RTP NAL header
            while (offset + 2 < payload.Length)
            {
                int nalSize = (payload[offset] << 8) | payload[offset + 1];
                offset += 2;

                if (nalSize <= 0 || offset + nalSize > payload.Length) break;

                byte[] nal = new byte[nalSize];
                Buffer.BlockCopy(payload, offset, nal, 0, nalSize);
                offset += nalSize;

                EmitNal(nal, isKeyFrame, timestamp);
            }
        }

        // ─────────────────────────────────────────────────────
        // Emit: prefix with Annex-B start code and fire callback
        // ─────────────────────────────────────────────────────
        private void EmitNal(byte[] nal, bool isKeyFrame, long timestamp)
        {
            // Strip existing Annex-B start code if present
            int dataStart = 0;
            if (nal.Length >= 4 && nal[0] == 0 && nal[1] == 0 && nal[2] == 0 && nal[3] == 1)
                dataStart = 4;
            else if (nal.Length >= 3 && nal[0] == 0 && nal[1] == 0 && nal[2] == 1)
                dataStart = 3;

            // Log NAL unit type for debugging
            int nalHeader = nal[dataStart];
            int nalType = (nalHeader >> 1) & 0x3F;
            
            // Only log important types or throttle
            if (isKeyFrame || nalType == 32 || nalType == 33 || nalType == 34) // VPS, SPS, PPS, or Key
            {
                Debug.Log($"{TAG} Emitting NAL: type={nalType}, size={nal.Length}, isKey={isKeyFrame}, ts={timestamp}");
            }

            byte[] output = new byte[4 + (nal.Length - dataStart)];
            output[0] = 0x00; output[1] = 0x00; output[2] = 0x00; output[3] = 0x01;
            Buffer.BlockCopy(nal, dataStart, output, 4, nal.Length - dataStart);

            OnNalUnitReady?.Invoke(output, isKeyFrame, timestamp);
        }
    }
}
