using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRWorkspace.Native
{
    /// <summary>
    /// RTP depacketizer for H.265/HEVC video streams.
    /// Extracts NAL units from RTP packets according to RFC 7798.
    ///
    /// Supports:
    /// - Single NAL unit packets (type 0-47)
    /// - Aggregation Packets (AP, type 48)
    /// - Fragmentation Units (FU, type 49)
    /// </summary>
    public class RtpDepacketizer
    {
        private const string TAG = "[RtpDepacketizer]";

        // RTP header size (fixed part)
        private const int RTP_HEADER_SIZE = 12;

        // H.265 NAL unit types
        private const int NAL_TYPE_AP = 48;  // Aggregation Packet
        private const int NAL_TYPE_FU = 49;  // Fragmentation Unit

        // NAL start code
        private static readonly byte[] NAL_START_CODE = { 0x00, 0x00, 0x00, 0x01 };

        // Fragment reassembly state
        private class FragmentState
        {
            public List<byte> Buffer = new List<byte>();
            public ushort SequenceNumber;
            public bool Started;
            public byte NalUnitType;
            public byte NuhLayerId;
            public byte NuhTemporalIdPlus1;
        }

        private FragmentState _fragment = new FragmentState();
        private ushort _lastSequenceNumber;
        private bool _firstPacket = true;

        // Callback for completed NAL units
        public event Action<byte[]> OnNalUnit;

        // Statistics
        public int PacketsReceived { get; private set; }
        public int NalUnitsExtracted { get; private set; }
        public int FragmentsReceived { get; private set; }
        public int SequenceErrors { get; private set; }

        // Packet loss tracking for adaptive bitrate feedback
        private int _packetsLostInWindow;
        private int _packetsReceivedInWindow;
        private DateTime _windowStartTime = DateTime.UtcNow;
        private const float LOSS_WINDOW_SECONDS = 2.0f; // Calculate loss rate every 2 seconds

        /// <summary>
        /// Current packet loss rate in the recent window (0.0 to 1.0).
        /// Updated every LOSS_WINDOW_SECONDS.
        /// </summary>
        public float PacketLossRate { get; private set; }

        /// <summary>
        /// Number of packets lost in the last completed window.
        /// </summary>
        public int PacketsLostInLastWindow { get; private set; }

        /// <summary>
        /// Number of consecutive sequence errors (resets on successful sequence).
        /// High values indicate sustained packet loss.
        /// </summary>
        public int ConsecutiveSequenceErrors { get; private set; }

        /// <summary>
        /// Event fired when packet loss rate is updated (every LOSS_WINDOW_SECONDS).
        /// Parameters: lossRate (0.0-1.0), packetsLost
        /// </summary>
        public event Action<float, int> OnPacketLossUpdated;

        /// <summary>
        /// Process an RTP packet and extract NAL units.
        /// </summary>
        /// <param name="rtpData">Complete RTP packet including header</param>
        public void ProcessRtpPacket(byte[] rtpData)
        {
            if (rtpData == null || rtpData.Length < RTP_HEADER_SIZE + 2)
            {
                Debug.LogWarning($"{TAG} Invalid RTP packet: too short");
                return;
            }

            PacketsReceived++;

            // Parse RTP header
            var header = ParseRtpHeader(rtpData);
            if (!header.Valid)
            {
                Debug.LogWarning($"{TAG} Invalid RTP header");
                return;
            }

            // Check sequence number for gaps
            if (!_firstPacket)
            {
                ushort expectedSeq = (ushort)((_lastSequenceNumber + 1) & 0xFFFF);
                if (header.SequenceNumber != expectedSeq)
                {
                    // Calculate gap size (handle wraparound for 16-bit sequence)
                    int gap = (header.SequenceNumber - expectedSeq) & 0xFFFF;
                    if (gap > 0x8000) gap = 0x10000 - gap; // Handle backward wraparound

                    SequenceErrors++;
                    ConsecutiveSequenceErrors++;
                    _packetsLostInWindow += Math.Max(1, gap - 1); // Count lost packets in gap

                    Debug.LogWarning($"{TAG} Sequence gap: expected {expectedSeq}, got {header.SequenceNumber}, lost ~{gap} packets (consecutive={ConsecutiveSequenceErrors})");

                    // Reset fragment state on sequence error
                    _fragment = new FragmentState();
                }
                else
                {
                    // Successful sequence - reset consecutive error count
                    ConsecutiveSequenceErrors = 0;
                }
            }
            _lastSequenceNumber = header.SequenceNumber;
            _firstPacket = false;

            // Track packets received in current window
            _packetsReceivedInWindow++;

            // Check if window expired - calculate and report loss rate
            float secondsSinceWindowStart = (float)(DateTime.UtcNow - _windowStartTime).TotalSeconds;
            if (secondsSinceWindowStart >= LOSS_WINDOW_SECONDS)
            {
                int totalPackets = _packetsReceivedInWindow + _packetsLostInWindow;
                PacketLossRate = totalPackets > 0 ? (float)_packetsLostInWindow / totalPackets : 0f;
                PacketsLostInLastWindow = _packetsLostInWindow;

                // Fire event for metrics aggregation
                OnPacketLossUpdated?.Invoke(PacketLossRate, PacketsLostInLastWindow);

                // Reset window
                _packetsReceivedInWindow = 0;
                _packetsLostInWindow = 0;
                _windowStartTime = DateTime.UtcNow;
            }

            // Get payload (skip RTP header + CSRC)
            int payloadOffset = RTP_HEADER_SIZE + (header.CsrcCount * 4);

            // Handle extension header if present
            if (header.Extension && rtpData.Length > payloadOffset + 4)
            {
                int extLength = (rtpData[payloadOffset + 2] << 8) | rtpData[payloadOffset + 3];
                payloadOffset += 4 + (extLength * 4);
            }

            if (payloadOffset >= rtpData.Length - 2)
            {
                Debug.LogWarning($"{TAG} No payload in RTP packet");
                return;
            }

            // Process H.265 payload
            ProcessH265Payload(rtpData, payloadOffset, rtpData.Length - payloadOffset, header.Marker);
        }

        /// <summary>
        /// Parse RTP header fields.
        /// </summary>
        private RtpHeader ParseRtpHeader(byte[] data)
        {
            var header = new RtpHeader();

            if (data.Length < RTP_HEADER_SIZE)
            {
                return header;
            }

            // Byte 0: V(2), P(1), X(1), CC(4)
            byte b0 = data[0];
            header.Version = (byte)((b0 >> 6) & 0x03);
            header.Padding = ((b0 >> 5) & 0x01) == 1;
            header.Extension = ((b0 >> 4) & 0x01) == 1;
            header.CsrcCount = (byte)(b0 & 0x0F);

            // Validate version
            if (header.Version != 2)
            {
                return header;
            }

            // Byte 1: M(1), PT(7)
            byte b1 = data[1];
            header.Marker = ((b1 >> 7) & 0x01) == 1;
            header.PayloadType = (byte)(b1 & 0x7F);

            // Bytes 2-3: Sequence number
            header.SequenceNumber = (ushort)((data[2] << 8) | data[3]);

            // Bytes 4-7: Timestamp
            header.Timestamp = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);

            // Bytes 8-11: SSRC
            header.Ssrc = (uint)((data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11]);

            header.Valid = true;
            return header;
        }

        /// <summary>
        /// Process H.265 RTP payload.
        /// </summary>
        private void ProcessH265Payload(byte[] data, int offset, int length, bool marker)
        {
            if (length < 2)
            {
                Debug.LogWarning($"{TAG} H.265 payload too short");
                return;
            }

            // H.265 NAL unit header (2 bytes)
            // Byte 0: F(1), Type(6), LayerId(1 bit)
            // Byte 1: LayerId(5 bits), TID(3 bits)
            byte b0 = data[offset];
            byte b1 = data[offset + 1];

            int nalType = (b0 >> 1) & 0x3F;
            int layerId = ((b0 & 0x01) << 5) | ((b1 >> 3) & 0x1F);
            int tid = b1 & 0x07;

            if (nalType <= 47)
            {
                // Single NAL unit packet
                EmitNalUnit(data, offset, length);
            }
            else if (nalType == NAL_TYPE_AP)
            {
                // Aggregation Packet
                ProcessAggregationPacket(data, offset, length);
            }
            else if (nalType == NAL_TYPE_FU)
            {
                // Fragmentation Unit
                ProcessFragmentationUnit(data, offset, length, marker);
            }
            else
            {
                Debug.LogWarning($"{TAG} Unknown NAL type: {nalType}");
            }
        }

        /// <summary>
        /// Process Aggregation Packet (AP).
        /// Contains multiple NAL units.
        /// </summary>
        private void ProcessAggregationPacket(byte[] data, int offset, int length)
        {
            // Skip AP header (2 bytes)
            int pos = offset + 2;

            while (pos + 2 < offset + length)
            {
                // Each NAL unit is prefixed with 2-byte size
                int nalSize = (data[pos] << 8) | data[pos + 1];
                pos += 2;

                if (pos + nalSize > offset + length)
                {
                    Debug.LogWarning($"{TAG} AP: NAL size exceeds packet boundary");
                    break;
                }

                // Emit NAL unit with start code
                EmitNalUnit(data, pos, nalSize);
                pos += nalSize;
            }
        }

        /// <summary>
        /// Process Fragmentation Unit (FU).
        /// NAL unit split across multiple RTP packets.
        /// </summary>
        private void ProcessFragmentationUnit(byte[] data, int offset, int length, bool marker)
        {
            if (length < 3)
            {
                Debug.LogWarning($"{TAG} FU packet too short");
                return;
            }

            FragmentsReceived++;

            // FU header (after NAL header, 1 byte)
            // S(1), E(1), FuType(6)
            byte fuHeader = data[offset + 2];
            bool start = ((fuHeader >> 7) & 0x01) == 1;
            bool end = ((fuHeader >> 6) & 0x01) == 1;
            byte fuType = (byte)(fuHeader & 0x3F);

            // Original NAL header bytes
            byte b0 = data[offset];
            byte b1 = data[offset + 1];

            // Fragment payload (skip NAL header + FU header = 3 bytes)
            int payloadOffset = offset + 3;
            int payloadLength = length - 3;

            if (start)
            {
                // Start of fragmented NAL
                _fragment = new FragmentState
                {
                    Started = true,
                    NalUnitType = fuType,
                    NuhLayerId = (byte)(((b0 & 0x01) << 5) | ((b1 >> 3) & 0x1F)),
                    NuhTemporalIdPlus1 = (byte)(b1 & 0x07)
                };

                // Reconstruct NAL header
                byte nalHeader0 = (byte)((b0 & 0x81) | (fuType << 1)); // F bit, type, 1 bit of layerId
                byte nalHeader1 = b1; // layerId + TID

                _fragment.Buffer.Add(nalHeader0);
                _fragment.Buffer.Add(nalHeader1);
            }

            if (_fragment.Started)
            {
                // Add payload
                for (int i = 0; i < payloadLength; i++)
                {
                    _fragment.Buffer.Add(data[payloadOffset + i]);
                }

                if (end || marker)
                {
                    // End of fragmented NAL - emit complete NAL unit
                    byte[] nalData = _fragment.Buffer.ToArray();
                    EmitNalUnit(nalData, 0, nalData.Length);
                    _fragment = new FragmentState();
                }
            }
            else
            {
                Debug.LogWarning($"{TAG} FU continuation without start");
            }
        }

        /// <summary>
        /// Emit a complete NAL unit with start code.
        /// </summary>
        private void EmitNalUnit(byte[] data, int offset, int length)
        {
            // Create NAL unit with start code
            byte[] nalUnit = new byte[NAL_START_CODE.Length + length];
            Array.Copy(NAL_START_CODE, 0, nalUnit, 0, NAL_START_CODE.Length);
            Array.Copy(data, offset, nalUnit, NAL_START_CODE.Length, length);

            NalUnitsExtracted++;
            OnNalUnit?.Invoke(nalUnit);
        }

        /// <summary>
        /// Reset depacketizer state.
        /// </summary>
        public void Reset()
        {
            _fragment = new FragmentState();
            _firstPacket = true;
            _lastSequenceNumber = 0;

            // Reset packet loss tracking
            _packetsLostInWindow = 0;
            _packetsReceivedInWindow = 0;
            _windowStartTime = DateTime.UtcNow;
            PacketLossRate = 0f;
            PacketsLostInLastWindow = 0;
            ConsecutiveSequenceErrors = 0;
        }

        /// <summary>
        /// Get statistics string.
        /// </summary>
        public string GetStats()
        {
            return $"Packets: {PacketsReceived}, NALs: {NalUnitsExtracted}, Fragments: {FragmentsReceived}, SeqErrors: {SequenceErrors}";
        }

        /// <summary>
        /// RTP header structure.
        /// </summary>
        private struct RtpHeader
        {
            public bool Valid;
            public byte Version;
            public bool Padding;
            public bool Extension;
            public byte CsrcCount;
            public bool Marker;
            public byte PayloadType;
            public ushort SequenceNumber;
            public uint Timestamp;
            public uint Ssrc;
        }

        /// <summary>
        /// Get NAL unit type name for debugging.
        /// </summary>
        public static string GetNalTypeName(int nalType)
        {
            return nalType switch
            {
                0 => "TRAIL_N",
                1 => "TRAIL_R",
                2 => "TSA_N",
                3 => "TSA_R",
                4 => "STSA_N",
                5 => "STSA_R",
                6 => "RADL_N",
                7 => "RADL_R",
                8 => "RASL_N",
                9 => "RASL_R",
                16 => "BLA_W_LP",
                17 => "BLA_W_RADL",
                18 => "BLA_N_LP",
                19 => "IDR_W_RADL",
                20 => "IDR_N_LP",
                21 => "CRA_NUT",
                32 => "VPS",
                33 => "SPS",
                34 => "PPS",
                35 => "AUD",
                36 => "EOS",
                37 => "EOB",
                38 => "FD",
                39 => "PREFIX_SEI",
                40 => "SUFFIX_SEI",
                48 => "AP",
                49 => "FU",
                _ => $"Unknown({nalType})"
            };
        }

        /// <summary>
        /// Check if NAL type is an IDR/keyframe.
        /// </summary>
        public static bool IsKeyFrame(int nalType)
        {
            return nalType >= 16 && nalType <= 21;
        }

        /// <summary>
        /// Check if NAL type is VPS/SPS/PPS (parameter sets).
        /// </summary>
        public static bool IsParameterSet(int nalType)
        {
            return nalType >= 32 && nalType <= 34;
        }

        /// <summary>
        /// Extract NAL type from NAL unit data (with or without start code).
        /// </summary>
        public static int GetNalType(byte[] nalData)
        {
            if (nalData == null || nalData.Length < 2)
                return -1;

            int offset = 0;

            // Skip start code if present
            if (nalData.Length >= 4 && nalData[0] == 0 && nalData[1] == 0 && nalData[2] == 0 && nalData[3] == 1)
            {
                offset = 4;
            }
            else if (nalData.Length >= 3 && nalData[0] == 0 && nalData[1] == 0 && nalData[2] == 1)
            {
                offset = 3;
            }

            if (offset + 1 >= nalData.Length)
                return -1;

            // NAL type is bits 1-6 of first byte
            return (nalData[offset] >> 1) & 0x3F;
        }
    }
}
