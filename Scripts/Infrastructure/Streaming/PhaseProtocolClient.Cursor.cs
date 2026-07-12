using System;
using System.Text;
using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    public partial class PhaseProtocolClient
    {
        // ── Video frame counters per track for device-build diagnostics ──
        // AppLog is stripped from builds, so these use Debug.Log with throttling
        private readonly System.Collections.Generic.Dictionary<int, int> _h265FrameCountPerTrack
            = new System.Collections.Generic.Dictionary<int, int>();
        private float _lastH265DiagTime;

        /// <summary>
        /// Handle H.265 video frames from dedicated unreliable DataChannel.
        /// Separated from cursor DC to avoid SCTP head-of-line blocking on audio.
        /// </summary>
        private void HandleH265VideoFromDataChannel(byte[] data)
        {
            if (data == null || data.Length < 2) return;

            byte msgType = data[0];

            // Track per-monitor frame count for diagnostics (visible on device builds)
            if (msgType >= 0x02 && msgType <= 0x04 && data.Length >= 2)
            {
                int trackIdx = data[1];
                if (!_h265FrameCountPerTrack.ContainsKey(trackIdx))
                    _h265FrameCountPerTrack[trackIdx] = 0;
                _h265FrameCountPerTrack[trackIdx]++;

                // Throttled diagnostic log: every 5 seconds, log frame count per track
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now - _lastH265DiagTime > 5f)
                {
                    _lastH265DiagTime = now;
                    var sb = new StringBuilder("[H265-DIAG] Frames per track:");
                    foreach (var kv in _h265FrameCountPerTrack)
                        sb.Append($" T{kv.Key}={kv.Value}");
                    Debug.Log(sb.ToString());
                }
            }

            if (msgType == 0x02)
            {
                HandleH265CodecConfig(data);
            }
            else if (msgType == 0x03)
            {
                HandleH265IdrData(data);
            }
            else if (msgType == 0x04)
            {
                HandleH265PFrameData(data);
            }
        }

        /// <summary>
        /// Handle cursor position from DataChannel (binary format, low-latency).
        /// Binary: [type(1)][monitorIndex(1)][u(4)][v(4)][flags(1)][cursorId(8)] = 19 bytes
        /// H.265 video frames now go through dedicated h265video DC (unreliable, unordered).
        /// This DC only handles cursor position (type=0x01) and legacy H.265 fallback.
        /// </summary>
        private void HandleCursorFromDataChannel(byte[] data)
        {
            if (data == null || data.Length < 2) return;

            byte msgType = data[0];

            // Legacy fallback: handle H.265 frames on cursor DC if h265video DC not available
            if (msgType == 0x02 || msgType == 0x03 || msgType == 0x04)
            {
                HandleH265VideoFromDataChannel(data);
                return;
            }

            if (msgType != 1 || data.Length < 19) return; // type 1 = cursor_position

            int monitorIndex = data[1];
            float u = BitConverter.ToSingle(data, 2);
            float v = BitConverter.ToSingle(data, 6);
            byte flags = data[10];
            bool visible = (flags & 1) != 0;
            int cursorTypeInt = (flags >> 1) & 0x0F;
            long cursorId = BitConverter.ToInt64(data, 11);
            CursorType cursorType = (CursorType)cursorTypeInt;

            // Dispatch to main thread (DataChannel callback is on WebRTC thread)
            VRWorkspace.Core.MainThreadDispatcher.Enqueue(() =>
            {
                OnCursorPosition?.Invoke(monitorIndex, u, v, visible, cursorType, cursorId);
            });
        }

        /// <summary>
        /// Handle H265 codec config (VPS/SPS/PPS) received via DataChannel side-channel.
        /// Format: [type=0x02][trackIndex(1)][Annex-B VPS+SPS+PPS bytes...]
        /// These are sent reliably because keyframe NALs are lost during RTP FU reassembly.
        /// </summary>
        private void HandleH265CodecConfig(byte[] data)
        {
            if (data.Length < 10) return; // At minimum: type + trackIndex + some NAL data

            int trackIndex = data[1];
            int paramLen = data.Length - 2;
            byte[] paramSets = new byte[paramLen];
            Buffer.BlockCopy(data, 2, paramSets, 0, paramLen);

            // Forward to the H265 handler for this track
            if (_h265Handlers.TryGetValue(trackIndex, out var handler))
            {
                handler.SetCodecConfig(paramSets);
            }
            else
            {
                // Per-track mode: no Encoded Transform handler — store config for IDR prepend
                _perTrackCodecConfig[trackIndex] = paramSets;
            }
        }

        // ── Per-track codec config storage (used when H265EncodedFrameHandler is absent) ──
        // In per-track mode, there's no Encoded Transform handler, so codec config
        // (VPS/SPS/PPS sent as type 0x02) must be stored here and prepended to IDR data.
        private readonly System.Collections.Generic.Dictionary<int, byte[]> _perTrackCodecConfig
            = new System.Collections.Generic.Dictionary<int, byte[]>();

        // ── IDR chunk reassembly state ──
        private readonly System.Collections.Generic.Dictionary<int, byte[][]> _idrChunks
            = new System.Collections.Generic.Dictionary<int, byte[][]>();

        // Chunk timeout tracking for corruption detection
        private readonly System.Collections.Generic.Dictionary<int, DateTime> _idrChunkStartTimes
            = new System.Collections.Generic.Dictionary<int, DateTime>();
        private readonly System.Collections.Generic.Dictionary<int, DateTime> _pframeChunkStartTimes
            = new System.Collections.Generic.Dictionary<int, DateTime>();
        private int _consecutiveDroppedPframes = 0;
        private DateTime _lastCorruptionKeyframeRequest = DateTime.MinValue;

        // ── P-frame gating: stop feeding P-frames when reference chain is broken ──
        // When ANY chunk is lost, the decoder's reference chain is corrupted.
        // ALL subsequent P-frames will produce garbage until a new IDR arrives.
        // Tainted tracks drop P-frames at the source to prevent decoder corruption.
        private readonly System.Collections.Generic.HashSet<int> _taintedTracks
            = new System.Collections.Generic.HashSet<int>();

        // ── Per-track reassembly status ──
        private readonly System.Collections.Generic.HashSet<int> _reassemblingIdrTracks
            = new System.Collections.Generic.HashSet<int>();

        // ── P-frame chunk reassembly state ──
        private readonly System.Collections.Generic.Dictionary<int, byte[][]> _pframeChunks
            = new System.Collections.Generic.Dictionary<int, byte[][]>();
        private int _pframeCount = 0;

        /// <summary>
        /// Handle H265 IDR keyframe data received via DataChannel side-channel.
        /// Robust to out-of-order chunks on unordered SCTP.
        /// </summary>
        private void HandleH265IdrData(byte[] data)
        {
            if (data.Length < 6) return;

            int trackIndex = data[1];
            int chunkIndex = data[2];
            int totalChunks = data[3];
            int dataLen = (data[4] << 8) | data[5];

            if (dataLen > data.Length - 6) dataLen = data.Length - 6;

            if (totalChunks == 1)
            {
                byte[] idrData = new byte[dataLen];
                Buffer.BlockCopy(data, 6, idrData, 0, dataLen);

                _pframeChunks.Remove(trackIndex);
                _idrChunks.Remove(trackIndex);
                _consecutiveDroppedPframes = 0;
                UntaintTrack(trackIndex);
                _reassemblingIdrTracks.Remove(trackIndex);

                FeedIdrToReceiver(trackIndex, idrData);
                return;
            }

            // Multi-chunk reassembly
            if (!_idrChunks.TryGetValue(trackIndex, out var chunks) || chunks.Length != totalChunks)
            {
                // New reassembly window
                chunks = new byte[totalChunks][];
                _idrChunks[trackIndex] = chunks;
                _idrChunkStartTimes[trackIndex] = DateTime.UtcNow;
                _reassemblingIdrTracks.Add(trackIndex); // BLOCK P-frames until complete
            }
            else if ((DateTime.UtcNow - _idrChunkStartTimes[trackIndex]).TotalMilliseconds > 500)
            {
                // Timeout - reset reassembly
                AppLog.LogWarning($"[PhaseProtocol] IDR reassembly timeout for track {trackIndex}, restarting");
                chunks = new byte[totalChunks][];
                _idrChunks[trackIndex] = chunks;
                _idrChunkStartTimes[trackIndex] = DateTime.UtcNow;
                _reassemblingIdrTracks.Add(trackIndex);
            }

            // Store chunk (allow out-of-order arrival)
            byte[] chunkData = new byte[dataLen];
            Buffer.BlockCopy(data, 6, chunkData, 0, dataLen);
            chunks[chunkIndex] = chunkData;

            // Check if complete
            bool complete = true;
            int totalSize = 0;
            for (int i = 0; i < totalChunks; i++)
            {
                if (chunks[i] == null) { complete = false; break; }
                totalSize += chunks[i].Length;
            }

            if (complete)
            {
                byte[] idrData = new byte[totalSize];
                int pos = 0;
                for (int i = 0; i < totalChunks; i++)
                {
                    Buffer.BlockCopy(chunks[i], 0, idrData, pos, chunks[i].Length);
                    pos += chunks[i].Length;
                }

                _idrChunks.Remove(trackIndex);
                _idrChunkStartTimes.Remove(trackIndex);
                _reassemblingIdrTracks.Remove(trackIndex); // UNBLOCK P-frames

                _consecutiveDroppedPframes = 0;
                _pframeChunks.Remove(trackIndex);
                UntaintTrack(trackIndex);

                FeedIdrToReceiver(trackIndex, idrData);
            }
        }

        private void FeedIdrToReceiver(int trackIndex, byte[] idrData)
        {
            if (_h265Handlers.TryGetValue(trackIndex, out var handler))
            {
                handler.FeedIdrFromDataChannel(idrData);
            }
            else if (_h265Receivers.TryGetValue(trackIndex, out var receiver))
            {
                byte[] feedData = idrData;
                if (_perTrackCodecConfig.TryGetValue(trackIndex, out var config) && config.Length > 0)
                {
                    feedData = new byte[config.Length + idrData.Length];
                    Buffer.BlockCopy(config, 0, feedData, 0, config.Length);
                    Buffer.BlockCopy(idrData, 0, feedData, config.Length, idrData.Length);
                }
                receiver.OnEncodedFrameReceived(feedData, true, GetTimestampUs());
            }
        }

        private void HandleH265PFrameData(byte[] data)
        {
            if (data.Length < 6) return;

            int trackIndex = data[1];
            int chunkIndex = data[2];
            int totalChunks = data[3];
            int dataLen = (data[4] << 8) | data[5];

            if (dataLen > data.Length - 6) dataLen = data.Length - 6;

            if (totalChunks == 1)
            {
                byte[] pframeData = new byte[dataLen];
                Buffer.BlockCopy(data, 6, pframeData, 0, dataLen);
                FeedPFrameToReceiver(trackIndex, pframeData);
                return;
            }

            if (!_pframeChunks.TryGetValue(trackIndex, out var chunks) || chunks.Length != totalChunks)
            {
                chunks = new byte[totalChunks][];
                _pframeChunks[trackIndex] = chunks;
                _pframeChunkStartTimes[trackIndex] = DateTime.UtcNow;
            }
            else if ((DateTime.UtcNow - _pframeChunkStartTimes[trackIndex]).TotalMilliseconds > 200)
            {
                chunks = new byte[totalChunks][];
                _pframeChunks[trackIndex] = chunks;
                _pframeChunkStartTimes[trackIndex] = DateTime.UtcNow;
            }

            byte[] chunkData = new byte[dataLen];
            Buffer.BlockCopy(data, 6, chunkData, 0, dataLen);
            chunks[chunkIndex] = chunkData;

            bool complete = true;
            int totalSize = 0;
            for (int i = 0; i < totalChunks; i++)
            {
                if (chunks[i] == null) { complete = false; break; }
                totalSize += chunks[i].Length;
            }

            if (complete)
            {
                byte[] pframeData = new byte[totalSize];
                int pos = 0;
                for (int i = 0; i < totalChunks; i++)
                {
                    Buffer.BlockCopy(chunks[i], 0, pframeData, pos, chunks[i].Length);
                    pos += chunks[i].Length;
                }
                _pframeChunks.Remove(trackIndex);
                _pframeChunkStartTimes.Remove(trackIndex);
                if (_consecutiveDroppedPframes > 0) _consecutiveDroppedPframes = 0;
                FeedPFrameToReceiver(trackIndex, pframeData);
            }
        }

        private void FeedPFrameToReceiver(int trackIndex, byte[] pframeData)
        {
            // Gate 1: drop P-frames for tainted tracks (reference chain broken)
            if (_taintedTracks.Contains(trackIndex))
                return;

            // Gate 2: CRITICAL - drop P-frames while an IDR reassembly is in progress
            // Since SCTP for video is 'unordered', P-frames can arrive while we're
            // still waiting for retransmission of an IDR chunk. Feeding them to
            // the decoder now would cause massive artifacts.
            if (_reassemblingIdrTracks.Contains(trackIndex))
            {
                if (_pframeCount % 100 == 0)
                    AppLog.LogWarning($"[PhaseProtocol] Dropping P-frame for track {trackIndex} (IDR reassembly in progress)");
                return;
            }

            _pframeCount++;

            if (_h265Handlers.TryGetValue(trackIndex, out var handler))
            {
                handler.FeedPFrameFromDataChannel(pframeData);
            }
            else if (_h265Receivers.TryGetValue(trackIndex, out var receiver))
            {
                // Direct feed to receiver if handler not available
                receiver.OnEncodedFrameReceived(pframeData, false, GetTimestampUs());
            }
        }

        private static long GetTimestampUs()
            => System.Diagnostics.Stopwatch.GetTimestamp() * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>
        /// Handle cursor position update from server (WebSocket fallback).
        /// </summary>
        private void HandleCursorPosition(SimpleJson json)
        {
            int monitorIndex = json.GetInt("monitorIndex");
            float u = json.GetFloat("u");
            float v = json.GetFloat("v");
            bool visible = json.GetBool("visible");
            int cursorTypeInt = json.GetInt("cursorType", 1); // Default: Arrow
            long cursorId = json.GetLong("cursorId", 0);
            CursorType cursorType = (CursorType)cursorTypeInt;

            // Invoke event for ConnectionViewModel to handle
            OnCursorPosition?.Invoke(monitorIndex, u, v, visible, cursorType, cursorId);
        }

        /// <summary>
        /// Extract integer value from raw JSON string using simple string parsing.
        /// </summary>
        private int ExtractJsonInt(string json, string key, int defaultValue = 0)
        {
            string pattern = $"\"{key}\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return defaultValue;
            idx += pattern.Length;

            // Skip whitespace
            while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;

            // Read digits (and possible minus sign)
            var sb = new StringBuilder();
            if (idx < json.Length && json[idx] == '-') { sb.Append('-'); idx++; }
            while (idx < json.Length && char.IsDigit(json[idx]))
            {
                sb.Append(json[idx++]);
            }

            return int.TryParse(sb.ToString(), out int result) ? result : defaultValue;
        }

        /// <summary>
        /// Extract long value from raw JSON string using simple string parsing.
        /// </summary>
        private long ExtractJsonLong(string json, string key, long defaultValue = 0)
        {
            string pattern = $"\"{key}\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return defaultValue;
            idx += pattern.Length;

            // Skip whitespace
            while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;

            // Read digits (and possible minus sign)
            var sb = new StringBuilder();
            if (idx < json.Length && json[idx] == '-') { sb.Append('-'); idx++; }
            while (idx < json.Length && char.IsDigit(json[idx]))
            {
                sb.Append(json[idx++]);
            }

            return long.TryParse(sb.ToString(), out long result) ? result : defaultValue;
        }

        /// <summary>
        /// Extract string value from raw JSON string using simple string parsing.
        /// This avoids any escape sequence processing that could corrupt base64 data.
        /// </summary>
        private string ExtractJsonString(string json, string key)
        {
            string pattern = $"\"{key}\":\"";
            int start = json.IndexOf(pattern);
            if (start < 0) return null;
            start += pattern.Length;

            // Find the closing quote - but be careful about escaped quotes
            int end = start;
            while (end < json.Length)
            {
                if (json[end] == '"')
                {
                    // Check if this quote is escaped
                    int backslashCount = 0;
                    int checkIdx = end - 1;
                    while (checkIdx >= start && json[checkIdx] == '\\')
                    {
                        backslashCount++;
                        checkIdx--;
                    }
                    // If even number of backslashes (including 0), this quote ends the string
                    if (backslashCount % 2 == 0)
                        break;
                }
                end++;
            }

            if (end <= start || end >= json.Length) return null;
            return json.Substring(start, end - start);
        }

        /// <summary>
        /// Handle cursor_image message directly from raw JSON string.
        /// Bypasses SimpleJson to avoid any base64 data corruption during parsing.
        /// </summary>
        private void HandleCursorImageRaw(string rawJson)
        {
            try
            {
                long cursorId = ExtractJsonLong(rawJson, "cursorId");
                int cursorTypeInt = ExtractJsonInt(rawJson, "cursorType");
                int width = ExtractJsonInt(rawJson, "width");
                int height = ExtractJsonInt(rawJson, "height");
                int hotspotX = ExtractJsonInt(rawJson, "hotspotX");
                int hotspotY = ExtractJsonInt(rawJson, "hotspotY");
                string imageBase64 = ExtractJsonString(rawJson, "imageBase64");
                CursorType cursorType = (CursorType)cursorTypeInt;

                if (string.IsNullOrEmpty(imageBase64))
                {
                    Debug.LogError("[PhaseProtocol] cursor_image has empty imageBase64 (RAW parser)!");
                    return;
                }

                // Decode raw RGBA on main thread
                VRWorkspace.Core.MainThreadDispatcher.Enqueue(() =>
                {
                    try
                    {
                        byte[] rgbaData = Convert.FromBase64String(imageBase64);
                        int expectedSize = width * height * 4;

                        // Handle size mismatch
                        if (rgbaData.Length != expectedSize)
                        {
                            int actualPixels = rgbaData.Length / 4;
                            int actualSide = (int)Mathf.Sqrt(actualPixels);

                            if (actualSide * actualSide * 4 == rgbaData.Length && actualSide > 0 && actualSide <= 256)
                            {
                                width = actualSide;
                                height = actualSide;
                                expectedSize = rgbaData.Length;
                            }
                            else if (rgbaData.Length < expectedSize)
                            {
                                Debug.LogError($"[PhaseProtocol] RGBA too small: got {rgbaData.Length}, need {expectedSize} for {width}x{height}");
                                return;
                            }
                            else
                            {
                                AppLog.LogWarning($"[PhaseProtocol] RGBA larger than expected ({rgbaData.Length} > {expectedSize}), truncating");
                                var truncated = new byte[expectedSize];
                                Array.Copy(rgbaData, truncated, expectedSize);
                                rgbaData = truncated;
                            }
                        }

                        // Create texture with vertical flip for Unity
                        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                        int rowSize = width * 4;
                        byte[] flippedData = new byte[rgbaData.Length];
                        for (int y = 0; y < height; y++)
                        {
                            int srcRow = y * rowSize;
                            int dstRow = (height - 1 - y) * rowSize;
                            Array.Copy(rgbaData, srcRow, flippedData, dstRow, rowSize);
                        }

                        // Zero out RGB for fully transparent pixels to prevent edge artifacts
                        for (int i = 0; i < flippedData.Length; i += 4)
                        {
                            if (flippedData[i + 3] == 0) // Alpha = 0
                            {
                                flippedData[i] = 0;     // R
                                flippedData[i + 1] = 0; // G
                                flippedData[i + 2] = 0; // B
                            }
                        }

                        texture.LoadRawTextureData(flippedData);
                        texture.Apply();
                        texture.filterMode = FilterMode.Point;
                        texture.wrapMode = TextureWrapMode.Clamp; // Prevent edge bleeding

                        // Invoke event to notify renderers
                        OnCursorImageReceived?.Invoke(cursorId, cursorType, texture, hotspotX, hotspotY);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PhaseProtocol] Failed to decode cursor image (RAW): {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] Failed to parse cursor_image (RAW): {ex.Message}");
            }
        }

        /// <summary>
        /// Mark a track as tainted (reference chain broken). All P-frames for this
        /// track are dropped until a clean IDR arrives. Also flushes decoder and
        /// requests keyframe burst.
        /// </summary>
        private void TaintTrack(int trackIndex, string reason)
        {
            bool wasAlreadyTainted = _taintedTracks.Contains(trackIndex);
            _taintedTracks.Add(trackIndex);

            if (!wasAlreadyTainted)
                AppLog.LogWarning($"[PhaseProtocol] Track {trackIndex} TAINTED: {reason}. P-frames will be dropped until next IDR.");

            // Flush the decoder to clear corrupted reference frames
            if (_h265Receivers.TryGetValue(trackIndex, out var receiver))
                receiver.Flush();

            // Also reset the handler's bootstrap gate so RTP P-frames are dropped too
            if (_h265Handlers.TryGetValue(trackIndex, out var handler))
                handler.ResetBootstrapGate();

            // Request keyframe with cooldown
            if ((DateTime.UtcNow - _lastCorruptionKeyframeRequest).TotalSeconds < 0.5)
                return;
            _lastCorruptionKeyframeRequest = DateTime.UtcNow;
            _ = SendTextAsync($"{{\"type\":\"request_keyframe_burst\",\"monitorIndex\":{trackIndex},\"count\":2,\"reason\":\"tainted_track\"}}");
        }

        /// <summary>
        /// Clear tainted status after a complete IDR is received and fed to decoder.
        /// </summary>
        private void UntaintTrack(int trackIndex)
        {
            if (_taintedTracks.Remove(trackIndex))
                AppLog.Log($"[PhaseProtocol] Track {trackIndex} UNTAINTED: clean IDR received, P-frames resumed.");
        }

        /// <summary>
        /// Handle cursor image from server.
        /// Decodes PNG data and creates texture for cursor rendering.
        /// </summary>
        private void HandleCursorImage(SimpleJson json)
        {
            try
            {
                long cursorId = json.GetLong("cursorId");
                int cursorTypeInt = json.GetInt("cursorType");
                int width = json.GetInt("width");
                int height = json.GetInt("height");
                int hotspotX = json.GetInt("hotspotX");
                int hotspotY = json.GetInt("hotspotY");
                string imageBase64 = json.GetString("imageBase64");
                CursorType cursorType = (CursorType)cursorTypeInt;

                if (string.IsNullOrEmpty(imageBase64))
                {
                    Debug.LogError("[PhaseProtocol] cursor_image has empty imageBase64!");
                    return;
                }

                // Decode raw RGBA on main thread
                VRWorkspace.Core.MainThreadDispatcher.Enqueue(() =>
                {
                    try
                    {
                        byte[] rgbaData = Convert.FromBase64String(imageBase64);
                        int expectedSize = width * height * 4;

                        // Handle size mismatch intelligently
                        if (rgbaData.Length != expectedSize)
                        {
                            // Try to detect actual cursor size from data length
                            int actualPixels = rgbaData.Length / 4;
                            int actualSide = (int)Mathf.Sqrt(actualPixels);

                            // Check if data represents a square cursor of different size
                            if (actualSide * actualSide * 4 == rgbaData.Length && actualSide > 0 && actualSide <= 256)
                            {
                                width = actualSide;
                                height = actualSide;
                                expectedSize = rgbaData.Length;
                            }
                            else if (rgbaData.Length < expectedSize)
                            {
                                Debug.LogError($"[PhaseProtocol] RGBA too small: got {rgbaData.Length}, need {expectedSize} for {width}x{height}");
                                return;
                            }
                            else
                            {
                                // Truncate extra bytes
                                AppLog.LogWarning($"[PhaseProtocol] RGBA larger than expected ({rgbaData.Length} > {expectedSize}), truncating");
                                var truncated = new byte[expectedSize];
                                Array.Copy(rgbaData, truncated, expectedSize);
                                rgbaData = truncated;
                            }
                        }

                        // Validate data size before creating texture
                        int requiredSize = width * height * 4;
                        if (rgbaData.Length != requiredSize)
                        {
                            Debug.LogError($"[PhaseProtocol] CRITICAL: Data size mismatch! rgbaData={rgbaData.Length}, required={requiredSize} for {width}x{height}");
                            // Try to fix by padding or truncating
                            var fixedData = new byte[requiredSize];
                            Array.Copy(rgbaData, fixedData, Math.Min(rgbaData.Length, requiredSize));
                            rgbaData = fixedData;
                        }

                        // Create texture with correct dimensions
                        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                        texture.filterMode = FilterMode.Bilinear; // Smooth cursor edges
                        texture.wrapMode = TextureWrapMode.Clamp; // Prevent edge artifacts

                        // Flip rows vertically: Server sends top-to-bottom (Windows order),
                        // but Unity Texture2D expects bottom-to-top for correct display
                        int rowSize = width * 4; // 4 bytes per pixel (RGBA)
                        byte[] flippedData = new byte[requiredSize]; // Use exact required size
                        for (int y = 0; y < height; y++)
                        {
                            int srcRow = y * rowSize;
                            int dstRow = (height - 1 - y) * rowSize;
                            Array.Copy(rgbaData, srcRow, flippedData, dstRow, rowSize);
                        }

                        texture.LoadRawTextureData(flippedData);
                        texture.Apply();

                        OnCursorImageReceived?.Invoke(cursorId, cursorType, texture, hotspotX, hotspotY);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[PhaseProtocol] Failed to decode cursor image: {ex.Message}\n{ex.StackTrace}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhaseProtocol] Failed to parse cursor_image: {ex.Message}");
            }
        }
    }
}
