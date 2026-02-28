using System;
using System.Text;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    public partial class PhaseProtocolClient
    {
        /// <summary>
        /// Handle cursor position from DataChannel (binary format, low-latency).
        /// Binary: [type(1)][monitorIndex(1)][u(4)][v(4)][flags(1)][cursorId(8)] = 19 bytes
        /// </summary>
        private void HandleCursorFromDataChannel(byte[] data)
        {
            if (data == null || data.Length < 19) return;
            if (data[0] != 1) return; // type 1 = cursor_position

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
                Debug.Log($"[PhaseProtocol] HandleCursorImageRaw: rawJson.Length={rawJson.Length}");

                long cursorId = ExtractJsonLong(rawJson, "cursorId");
                int cursorTypeInt = ExtractJsonInt(rawJson, "cursorType");
                int width = ExtractJsonInt(rawJson, "width");
                int height = ExtractJsonInt(rawJson, "height");
                int hotspotX = ExtractJsonInt(rawJson, "hotspotX");
                int hotspotY = ExtractJsonInt(rawJson, "hotspotY");
                string imageBase64 = ExtractJsonString(rawJson, "imageBase64");
                CursorType cursorType = (CursorType)cursorTypeInt;

                Debug.Log($"[PhaseProtocol] RAW Parsed cursor_image: id={cursorId}, type={cursorType}, size={width}x{height}, base64Len={imageBase64?.Length ?? 0}");

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
                        // Log sample of base64 for debugging transmission
                        int b64Len = imageBase64.Length;
                        string b64Start = imageBase64.Substring(0, Math.Min(40, b64Len));
                        string b64End = imageBase64.Substring(Math.Max(0, b64Len - 20));
                        Debug.Log($"[PhaseProtocol] Cursor {cursorId} base64 (RAW): len={b64Len}, start={b64Start}...end={b64End}");

                        byte[] rgbaData = Convert.FromBase64String(imageBase64);
                        int expectedSize = width * height * 4;

                        Debug.Log($"[PhaseProtocol] Cursor {cursorId} decoded: {rgbaData.Length} bytes, expected {expectedSize}");

                        // Handle size mismatch
                        if (rgbaData.Length != expectedSize)
                        {
                            int actualPixels = rgbaData.Length / 4;
                            int actualSide = (int)Mathf.Sqrt(actualPixels);

                            if (actualSide * actualSide * 4 == rgbaData.Length && actualSide > 0 && actualSide <= 256)
                            {
                                Debug.Log($"[PhaseProtocol] Cursor size adjusted: declared {width}x{height} -> actual {actualSide}x{actualSide}");
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
                                Debug.LogWarning($"[PhaseProtocol] RGBA larger than expected ({rgbaData.Length} > {expectedSize}), truncating");
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

                        Debug.Log($"[PhaseProtocol] Cursor {cursorId} texture created (RAW parser): {width}x{height}, hotspot=({hotspotX},{hotspotY})");

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

                Debug.Log($"[PhaseProtocol] Received cursor_image: id={cursorId}, type={cursorType}, size={width}x{height}, base64Len={imageBase64?.Length ?? 0}");

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
                        // Log sample of base64 for debugging transmission
                        int b64Len = imageBase64.Length;
                        string b64Start = imageBase64.Substring(0, Math.Min(40, b64Len));
                        string b64End = imageBase64.Substring(Math.Max(0, b64Len - 20));
                        Debug.Log($"[PhaseProtocol] Cursor {cursorId} base64: len={b64Len}, start={b64Start}...end={b64End}");

                        byte[] rgbaData = Convert.FromBase64String(imageBase64);
                        int expectedSize = width * height * 4;

                        Debug.Log($"[PhaseProtocol] Cursor {cursorId} decoded: {rgbaData.Length} bytes, expected {expectedSize}");

                        // Handle size mismatch intelligently
                        if (rgbaData.Length != expectedSize)
                        {
                            // Try to detect actual cursor size from data length
                            int actualPixels = rgbaData.Length / 4;
                            int actualSide = (int)Mathf.Sqrt(actualPixels);

                            // Check if data represents a square cursor of different size
                            if (actualSide * actualSide * 4 == rgbaData.Length && actualSide > 0 && actualSide <= 256)
                            {
                                Debug.Log($"[PhaseProtocol] Cursor size adjusted: declared {width}x{height} -> actual {actualSide}x{actualSide}");
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
                                Debug.LogWarning($"[PhaseProtocol] RGBA larger than expected ({rgbaData.Length} > {expectedSize}), truncating");
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

                        Debug.Log($"[PhaseProtocol] Cursor texture loaded: {texture.width}x{texture.height}, format={texture.format}");

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
