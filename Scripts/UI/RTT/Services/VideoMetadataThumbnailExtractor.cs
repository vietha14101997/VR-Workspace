using UnityEngine;
using System;
using System.IO;
using System.Text;

namespace VRWorkspace.UI.RTT.Services
{
    /// <summary>
    /// Extracts embedded thumbnail/cover art from video file metadata.
    /// Supports MP4/M4V (iTunes-style covr atom) and basic MKV attachments.
    /// Falls back gracefully if no embedded thumbnail is found.
    /// </summary>
    public static class VideoMetadataThumbnailExtractor
    {
        /// <summary>
        /// Try to extract embedded thumbnail from video file metadata.
        /// </summary>
        /// <param name="videoPath">Path to the video file</param>
        /// <param name="thumbnail">Output texture if found</param>
        /// <returns>True if embedded thumbnail was found and loaded</returns>
        public static bool TryExtractEmbeddedThumbnail(string videoPath, out Texture2D thumbnail)
        {
            thumbnail = null;

            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            {
                return false;
            }

            string extension = Path.GetExtension(videoPath).ToLowerInvariant();

            try
            {
                switch (extension)
                {
                    case ".mp4":
                    case ".m4v":
                    case ".m4a":
                    case ".mov":
                        return TryExtractFromMP4(videoPath, out thumbnail);

                    case ".mkv":
                    case ".webm":
                        return TryExtractFromMKV(videoPath, out thumbnail);

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VideoMetadataThumbnailExtractor] Failed to extract thumbnail from {Path.GetFileName(videoPath)}: {ex.Message}");
                return false;
            }
        }

        #region MP4/MOV Extraction
        /// <summary>
        /// Extract embedded artwork from MP4/MOV container (iTunes-style metadata).
        /// Looks for moov/udta/meta/ilst/covr atom.
        /// </summary>
        private static bool TryExtractFromMP4(string filePath, out Texture2D thumbnail)
        {
            thumbnail = null;

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                // Find moov atom
                long moovPos = FindAtom(reader, fs.Length, "moov");
                if (moovPos < 0) return false;

                // Read moov atom size
                fs.Position = moovPos;
                uint moovSize = ReadUInt32BE(reader);
                long moovEnd = moovPos + moovSize;

                // Skip moov header (size + type)
                fs.Position = moovPos + 8;

                // Find udta atom within moov
                long udtaPos = FindAtomWithin(reader, moovEnd, "udta");
                if (udtaPos < 0) return false;

                fs.Position = udtaPos;
                uint udtaSize = ReadUInt32BE(reader);
                long udtaEnd = udtaPos + udtaSize;
                fs.Position = udtaPos + 8;

                // Find meta atom within udta
                long metaPos = FindAtomWithin(reader, udtaEnd, "meta");
                if (metaPos < 0) return false;

                fs.Position = metaPos;
                uint metaSize = ReadUInt32BE(reader);
                long metaEnd = metaPos + metaSize;
                // meta atom has 4 extra bytes (version/flags) after header
                fs.Position = metaPos + 12;

                // Find ilst atom within meta
                long ilstPos = FindAtomWithin(reader, metaEnd, "ilst");
                if (ilstPos < 0) return false;

                fs.Position = ilstPos;
                uint ilstSize = ReadUInt32BE(reader);
                long ilstEnd = ilstPos + ilstSize;
                fs.Position = ilstPos + 8;

                // Find covr atom within ilst
                long covrPos = FindAtomWithin(reader, ilstEnd, "covr");
                if (covrPos < 0) return false;

                fs.Position = covrPos;
                uint covrSize = ReadUInt32BE(reader);
                long covrEnd = covrPos + covrSize;
                fs.Position = covrPos + 8;

                // Find data atom within covr
                long dataPos = FindAtomWithin(reader, covrEnd, "data");
                if (dataPos < 0) return false;

                fs.Position = dataPos;
                uint dataSize = ReadUInt32BE(reader);

                // Skip data atom header (8 bytes) + type indicator (4 bytes) + null bytes (4 bytes)
                fs.Position = dataPos + 16;

                // Read image data
                int imageDataSize = (int)(dataSize - 16);
                if (imageDataSize <= 0 || imageDataSize > 10 * 1024 * 1024) // Max 10MB
                {
                    return false;
                }

                byte[] imageData = reader.ReadBytes(imageDataSize);

                // Create texture from image data
                thumbnail = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                thumbnail.filterMode = FilterMode.Trilinear;
                thumbnail.wrapMode = TextureWrapMode.Clamp;

                if (thumbnail.LoadImage(imageData))
                {
                    Debug.Log($"[VideoMetadataThumbnailExtractor] Extracted embedded thumbnail from MP4: {Path.GetFileName(filePath)} ({thumbnail.width}x{thumbnail.height})");
                    return true;
                }
                else
                {
                    UnityEngine.Object.Destroy(thumbnail);
                    thumbnail = null;
                    return false;
                }
            }
        }

        private static long FindAtom(BinaryReader reader, long endPos, string atomType)
        {
            long startPos = reader.BaseStream.Position;
            return FindAtomWithin(reader, endPos, atomType, startPos);
        }

        private static long FindAtomWithin(BinaryReader reader, long endPos, string atomType, long startPos = -1)
        {
            FileStream fs = (FileStream)reader.BaseStream;
            if (startPos >= 0) fs.Position = startPos;

            byte[] targetType = Encoding.ASCII.GetBytes(atomType);

            while (fs.Position < endPos - 8)
            {
                long atomPos = fs.Position;

                // Read atom size (4 bytes, big-endian)
                uint size = ReadUInt32BE(reader);
                if (size < 8) break; // Invalid atom

                // Read atom type (4 bytes)
                byte[] type = reader.ReadBytes(4);

                // Check if this is the atom we're looking for
                if (type[0] == targetType[0] && type[1] == targetType[1] &&
                    type[2] == targetType[2] && type[3] == targetType[3])
                {
                    return atomPos;
                }

                // Skip to next atom
                fs.Position = atomPos + size;
            }

            return -1;
        }

        private static uint ReadUInt32BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt32(bytes, 0);
        }
        #endregion

        #region MKV Extraction
        /// <summary>
        /// Extract embedded thumbnail from MKV container (attachments).
        /// Looks for attachments with image MIME types.
        /// </summary>
        private static bool TryExtractFromMKV(string filePath, out Texture2D thumbnail)
        {
            thumbnail = null;

            // MKV uses EBML format which is more complex
            // For simplicity, we'll search for common image signatures within attachments

            try
            {
                byte[] fileBytes;

                // Read limited portion of file to find attachments (usually near the end)
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // MKV attachments are typically within the first few MB or at the end
                    // Read first 5MB for a quick scan
                    int readSize = (int)Math.Min(fs.Length, 5 * 1024 * 1024);
                    fileBytes = new byte[readSize];
                    fs.Read(fileBytes, 0, readSize);
                }

                // Look for JPEG signature (FFD8FF)
                int jpegStart = FindSignature(fileBytes, new byte[] { 0xFF, 0xD8, 0xFF });
                if (jpegStart >= 0)
                {
                    int jpegEnd = FindSignature(fileBytes, new byte[] { 0xFF, 0xD9 }, jpegStart + 3);
                    if (jpegEnd > jpegStart)
                    {
                        int imageSize = jpegEnd - jpegStart + 2;
                        if (imageSize > 1000 && imageSize < 2 * 1024 * 1024) // Between 1KB and 2MB
                        {
                            byte[] imageData = new byte[imageSize];
                            Array.Copy(fileBytes, jpegStart, imageData, 0, imageSize);

                            thumbnail = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                            thumbnail.filterMode = FilterMode.Trilinear;
                            thumbnail.wrapMode = TextureWrapMode.Clamp;

                            if (thumbnail.LoadImage(imageData))
                            {
                                Debug.Log($"[VideoMetadataThumbnailExtractor] Extracted embedded JPEG from MKV: {Path.GetFileName(filePath)} ({thumbnail.width}x{thumbnail.height})");
                                return true;
                            }
                            else
                            {
                                UnityEngine.Object.Destroy(thumbnail);
                                thumbnail = null;
                            }
                        }
                    }
                }

                // Look for PNG signature (89504E47)
                int pngStart = FindSignature(fileBytes, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                if (pngStart >= 0)
                {
                    // Find PNG end marker (IEND chunk)
                    int pngEnd = FindSignature(fileBytes, new byte[] { 0x49, 0x45, 0x4E, 0x44 }, pngStart + 8);
                    if (pngEnd > pngStart)
                    {
                        int imageSize = pngEnd - pngStart + 8; // +8 for IEND chunk CRC
                        if (imageSize > 1000 && imageSize < 2 * 1024 * 1024)
                        {
                            byte[] imageData = new byte[imageSize];
                            Array.Copy(fileBytes, pngStart, imageData, 0, imageSize);

                            thumbnail = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                            thumbnail.filterMode = FilterMode.Trilinear;
                            thumbnail.wrapMode = TextureWrapMode.Clamp;

                            if (thumbnail.LoadImage(imageData))
                            {
                                Debug.Log($"[VideoMetadataThumbnailExtractor] Extracted embedded PNG from MKV: {Path.GetFileName(filePath)} ({thumbnail.width}x{thumbnail.height})");
                                return true;
                            }
                            else
                            {
                                UnityEngine.Object.Destroy(thumbnail);
                                thumbnail = null;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VideoMetadataThumbnailExtractor] MKV extraction failed: {ex.Message}");
                return false;
            }
        }

        private static int FindSignature(byte[] data, byte[] signature, int startIndex = 0)
        {
            for (int i = startIndex; i <= data.Length - signature.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < signature.Length; j++)
                {
                    if (data[i + j] != signature[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
        #endregion
    }

}
