using UnityEngine;
using System;
using System.IO;
using System.Text;

/// <summary>
/// Extracts embedded album art (cover) from audio files.
/// Supports MP3 (ID3v2) and FLAC metadata formats.
/// </summary>
public static class AudioMetadataExtractor
{
    /// <summary>
    /// Try to extract album art from an audio file.
    /// </summary>
    /// <param name="filePath">Path to the audio file</param>
    /// <param name="texture">Output texture if album art found</param>
    /// <returns>True if album art was successfully extracted</returns>
    public static bool TryExtractAlbumArt(string filePath, out Texture2D texture)
    {
        texture = null;

        byte[] imageData = TryExtractAlbumArtData(filePath);
        if (imageData == null || imageData.Length == 0)
            return false;

        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 16;
            texture.wrapMode = TextureWrapMode.Clamp;

            if (texture.LoadImage(imageData))
            {
                return true;
            }
            else
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AudioMetadataExtractor] Failed to create texture from album art: {ex.Message}");
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
        }

        return false;
    }

    /// <summary>
    /// Try to extract album art raw image data from an audio file.
    /// This method is thread-safe and can be called from background threads.
    /// </summary>
    /// <param name="filePath">Path to the audio file</param>
    /// <returns>Raw image bytes (JPEG/PNG) or null if not found</returns>
    public static byte[] TryExtractAlbumArtData(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        string extension = Path.GetExtension(filePath).ToLower().TrimStart('.');

        try
        {
            switch (extension)
            {
                case "mp3":
                    return ExtractFromMp3(filePath);
                case "flac":
                    return ExtractFromFlac(filePath);
                case "ogg":
                    return ExtractFromOgg(filePath);
                case "m4a":
                case "aac":
                    return ExtractFromM4a(filePath);
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AudioMetadataExtractor] Failed to extract album art from {filePath}: {ex.Message}");
            return null;
        }
    }

    #region MP3 (ID3v2)
    /// <summary>
    /// Extract album art from MP3 file (ID3v2 APIC frame).
    /// </summary>
    private static byte[] ExtractFromMp3(string filePath)
    {
        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader reader = new BinaryReader(fs))
        {
            // Check for ID3v2 header
            byte[] header = reader.ReadBytes(3);
            if (Encoding.ASCII.GetString(header) != "ID3")
                return null;

            // Read version and flags
            byte versionMajor = reader.ReadByte();
            byte versionMinor = reader.ReadByte();
            byte flags = reader.ReadByte();

            // Read tag size (syncsafe integer)
            byte[] sizeBytes = reader.ReadBytes(4);
            int tagSize = (sizeBytes[0] << 21) | (sizeBytes[1] << 14) | (sizeBytes[2] << 7) | sizeBytes[3];

            long tagEnd = fs.Position + tagSize;

            // Parse frames
            while (fs.Position < tagEnd - 10)
            {
                // Read frame header
                byte[] frameId = reader.ReadBytes(4);
                string frameIdStr = Encoding.ASCII.GetString(frameId);

                if (frameIdStr[0] == '\0')
                    break; // Padding reached

                // Read frame size
                byte[] frameSizeBytes = reader.ReadBytes(4);
                int frameSize;

                if (versionMajor >= 4)
                {
                    // ID3v2.4 uses syncsafe integers
                    frameSize = (frameSizeBytes[0] << 21) | (frameSizeBytes[1] << 14) | (frameSizeBytes[2] << 7) | frameSizeBytes[3];
                }
                else
                {
                    // ID3v2.3 uses regular big-endian integers
                    frameSize = (frameSizeBytes[0] << 24) | (frameSizeBytes[1] << 16) | (frameSizeBytes[2] << 8) | frameSizeBytes[3];
                }

                // Read frame flags (2 bytes)
                reader.ReadBytes(2);

                if (frameSize <= 0 || fs.Position + frameSize > tagEnd)
                    break;

                // Check for APIC frame (Attached Picture)
                if (frameIdStr == "APIC")
                {
                    byte[] frameData = reader.ReadBytes(frameSize);
                    return ParseApicFrame(frameData);
                }
                else
                {
                    // Skip frame
                    fs.Seek(frameSize, SeekOrigin.Current);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parse APIC frame data to extract image bytes.
    /// </summary>
    private static byte[] ParseApicFrame(byte[] data)
    {
        if (data == null || data.Length < 10)
            return null;

        int pos = 0;

        // Text encoding (1 byte)
        byte encoding = data[pos++];

        // MIME type (null-terminated string)
        int mimeStart = pos;
        while (pos < data.Length && data[pos] != 0) pos++;
        pos++; // Skip null terminator

        // Picture type (1 byte)
        if (pos >= data.Length) return null;
        pos++;

        // Description (null-terminated, encoding-dependent)
        if (encoding == 0 || encoding == 3) // ISO-8859-1 or UTF-8
        {
            while (pos < data.Length && data[pos] != 0) pos++;
            pos++; // Skip null terminator
        }
        else if (encoding == 1 || encoding == 2) // UTF-16 with BOM or UTF-16BE
        {
            // Skip BOM if present
            while (pos < data.Length - 1)
            {
                if (data[pos] == 0 && data[pos + 1] == 0)
                {
                    pos += 2;
                    break;
                }
                pos += 2;
            }
        }

        if (pos >= data.Length)
            return null;

        // Remaining bytes are the image data
        int imageLength = data.Length - pos;
        byte[] imageData = new byte[imageLength];
        Array.Copy(data, pos, imageData, 0, imageLength);

        return imageData;
    }
    #endregion

    #region FLAC
    /// <summary>
    /// Extract album art from FLAC file (METADATA_BLOCK_PICTURE).
    /// </summary>
    private static byte[] ExtractFromFlac(string filePath)
    {
        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader reader = new BinaryReader(fs))
        {
            // Check for fLaC marker
            byte[] marker = reader.ReadBytes(4);
            if (Encoding.ASCII.GetString(marker) != "fLaC")
                return null;

            // Parse metadata blocks
            bool isLast = false;
            while (!isLast && fs.Position < fs.Length)
            {
                // Read block header (4 bytes)
                byte blockHeader = reader.ReadByte();
                isLast = (blockHeader & 0x80) != 0;
                byte blockType = (byte)(blockHeader & 0x7F);

                // Read block size (3 bytes big-endian)
                byte[] sizeBytes = reader.ReadBytes(3);
                int blockSize = (sizeBytes[0] << 16) | (sizeBytes[1] << 8) | sizeBytes[2];

                if (blockSize <= 0 || fs.Position + blockSize > fs.Length)
                    break;

                // Block type 6 = PICTURE
                if (blockType == 6)
                {
                    byte[] blockData = reader.ReadBytes(blockSize);
                    return ParseFlacPictureBlock(blockData);
                }
                else
                {
                    // Skip block
                    fs.Seek(blockSize, SeekOrigin.Current);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parse FLAC PICTURE block to extract image bytes.
    /// </summary>
    private static byte[] ParseFlacPictureBlock(byte[] data)
    {
        if (data == null || data.Length < 32)
            return null;

        int pos = 0;

        // Picture type (4 bytes big-endian)
        pos += 4;

        // MIME type length (4 bytes big-endian)
        int mimeLength = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
        pos += 4;

        // Skip MIME type
        pos += mimeLength;

        // Description length (4 bytes big-endian)
        if (pos + 4 > data.Length) return null;
        int descLength = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
        pos += 4;

        // Skip description
        pos += descLength;

        // Skip width, height, color depth, indexed colors (16 bytes)
        pos += 16;

        // Image data length (4 bytes big-endian)
        if (pos + 4 > data.Length) return null;
        int imageLength = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
        pos += 4;

        if (pos + imageLength > data.Length)
            return null;

        // Extract image data
        byte[] imageData = new byte[imageLength];
        Array.Copy(data, pos, imageData, 0, imageLength);

        return imageData;
    }
    #endregion

    #region Ogg Vorbis
    /// <summary>
    /// Extract album art from Ogg Vorbis file (Vorbis Comment METADATA_BLOCK_PICTURE).
    /// </summary>
    private static byte[] ExtractFromOgg(string filePath)
    {
        // Ogg Vorbis stores cover art in Vorbis Comments as base64-encoded METADATA_BLOCK_PICTURE
        // This is a simplified implementation - full Ogg parsing is complex
        try
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            string searchPattern = "METADATA_BLOCK_PICTURE=";
            byte[] patternBytes = Encoding.ASCII.GetBytes(searchPattern);

            int patternIndex = FindPattern(fileData, patternBytes);
            if (patternIndex < 0)
                return null;

            // Find the end of the base64 data (next null or field separator)
            int dataStart = patternIndex + patternBytes.Length;
            int dataEnd = dataStart;
            while (dataEnd < fileData.Length && fileData[dataEnd] != 0 && fileData[dataEnd] != 1)
            {
                dataEnd++;
            }

            if (dataEnd <= dataStart)
                return null;

            // Extract and decode base64
            string base64Data = Encoding.ASCII.GetString(fileData, dataStart, dataEnd - dataStart);
            byte[] pictureBlock = Convert.FromBase64String(base64Data);

            // Parse as FLAC PICTURE block
            return ParseFlacPictureBlock(pictureBlock);
        }
        catch
        {
            return null;
        }
    }

    private static int FindPattern(byte[] data, byte[] pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }
            if (found)
                return i;
        }
        return -1;
    }
    #endregion

    #region M4A/AAC (MP4 container)
    /// <summary>
    /// Extract album art from M4A/AAC file (MP4 container, covr atom).
    /// </summary>
    private static byte[] ExtractFromM4a(string filePath)
    {
        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader reader = new BinaryReader(fs))
        {
            return FindCovrAtom(reader, fs.Length);
        }
    }

    private static byte[] FindCovrAtom(BinaryReader reader, long endPos)
    {
        while (reader.BaseStream.Position < endPos - 8)
        {
            // Read atom size (4 bytes big-endian)
            byte[] sizeBytes = reader.ReadBytes(4);
            uint atomSize = (uint)((sizeBytes[0] << 24) | (sizeBytes[1] << 16) | (sizeBytes[2] << 8) | sizeBytes[3]);

            // Read atom type (4 bytes)
            byte[] typeBytes = reader.ReadBytes(4);
            string atomType = Encoding.ASCII.GetString(typeBytes);

            if (atomSize == 0)
                break;

            if (atomSize < 8)
            {
                // Invalid atom, skip 1 byte and try again
                reader.BaseStream.Seek(-7, SeekOrigin.Current);
                continue;
            }

            long atomEnd = reader.BaseStream.Position - 8 + atomSize;

            // Container atoms that may contain covr
            if (atomType == "moov" || atomType == "udta" || atomType == "meta" || atomType == "ilst")
            {
                // Skip version/flags for meta atom
                if (atomType == "meta")
                {
                    reader.ReadBytes(4);
                }

                // Recurse into container
                byte[] result = FindCovrAtom(reader, atomEnd);
                if (result != null)
                    return result;
            }
            else if (atomType == "covr")
            {
                // Found cover art atom
                // Read data atom inside covr
                byte[] dataSizeBytes = reader.ReadBytes(4);
                uint dataSize = (uint)((dataSizeBytes[0] << 24) | (dataSizeBytes[1] << 16) | (dataSizeBytes[2] << 8) | dataSizeBytes[3]);

                byte[] dataType = reader.ReadBytes(4);
                if (Encoding.ASCII.GetString(dataType) != "data")
                {
                    reader.BaseStream.Seek(atomEnd, SeekOrigin.Begin);
                    continue;
                }

                // Skip type indicator and locale (8 bytes)
                reader.ReadBytes(8);

                // Read image data
                int imageLength = (int)(dataSize - 16);
                if (imageLength > 0 && reader.BaseStream.Position + imageLength <= reader.BaseStream.Length)
                {
                    return reader.ReadBytes(imageLength);
                }
            }
            else
            {
                // Skip unknown atom
                reader.BaseStream.Seek(atomEnd, SeekOrigin.Begin);
            }

            if (reader.BaseStream.Position >= endPos)
                break;
        }

        return null;
    }
    #endregion
}
