using UnityEngine;
using System;
using System.IO;

/// <summary>
/// Extracts embedded thumbnails from image file metadata (EXIF/JFIF).
/// Many cameras and image editors embed small preview thumbnails in image files.
/// Reading these is much faster than loading and resizing the full image.
/// </summary>
public static class ImageMetadataThumbnailExtractor
{
    /// <summary>
    /// Try to extract embedded thumbnail from image metadata.
    /// </summary>
    /// <param name="imagePath">Path to the image file</param>
    /// <param name="thumbnail">Output texture if found</param>
    /// <returns>True if embedded thumbnail was found and loaded</returns>
    public static bool TryExtractEmbeddedThumbnail(string imagePath, out Texture2D thumbnail)
    {
        thumbnail = null;

        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            return false;

        string extension = Path.GetExtension(imagePath).ToLowerInvariant();

        try
        {
            byte[] thumbnailData = null;

            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    thumbnailData = ExtractJpegExifThumbnail(imagePath);
                    break;
                case ".tiff":
                case ".tif":
                    thumbnailData = ExtractTiffThumbnail(imagePath);
                    break;
                // PNG, GIF, BMP, WebP don't typically have embedded thumbnails
                default:
                    return false;
            }

            if (thumbnailData == null || thumbnailData.Length < 100)
                return false;

            // Create texture from thumbnail data
            thumbnail = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            thumbnail.filterMode = FilterMode.Trilinear;
            thumbnail.wrapMode = TextureWrapMode.Clamp;

            if (thumbnail.LoadImage(thumbnailData))
            {
                // Only use if thumbnail is reasonable size (not too small)
                if (thumbnail.width >= 64 && thumbnail.height >= 64)
                {
                    Debug.Log($"[ImageMetadataThumbnailExtractor] Extracted EXIF thumbnail from {Path.GetFileName(imagePath)} ({thumbnail.width}x{thumbnail.height})");
                    return true;
                }
            }

            // Thumbnail too small or failed to load
            UnityEngine.Object.Destroy(thumbnail);
            thumbnail = null;
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ImageMetadataThumbnailExtractor] Failed to extract thumbnail from {Path.GetFileName(imagePath)}: {ex.Message}");
            if (thumbnail != null)
            {
                UnityEngine.Object.Destroy(thumbnail);
                thumbnail = null;
            }
            return false;
        }
    }

    #region JPEG/EXIF Extraction
    /// <summary>
    /// Extract thumbnail from JPEG EXIF data.
    /// EXIF thumbnails are typically stored as embedded JPEG images.
    /// </summary>
    private static byte[] ExtractJpegExifThumbnail(string filePath)
    {
        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader reader = new BinaryReader(fs))
        {
            // Check JPEG marker
            if (reader.ReadByte() != 0xFF || reader.ReadByte() != 0xD8)
                return null;

            // Find APP1 (EXIF) marker
            while (fs.Position < fs.Length - 2)
            {
                if (reader.ReadByte() != 0xFF)
                    continue;

                byte marker = reader.ReadByte();

                // Skip padding
                while (marker == 0xFF && fs.Position < fs.Length)
                {
                    marker = reader.ReadByte();
                }

                // Check if this is a segment with length
                if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
                    continue;

                // Read segment length
                int length = (reader.ReadByte() << 8) | reader.ReadByte();
                if (length < 2)
                    break;

                // APP1 marker (EXIF)
                if (marker == 0xE1)
                {
                    byte[] segmentData = reader.ReadBytes(length - 2);
                    return ParseExifForThumbnail(segmentData);
                }
                // APP0 marker (JFIF) - skip but continue searching
                else if (marker == 0xE0)
                {
                    fs.Seek(length - 2, SeekOrigin.Current);
                }
                // SOS (Start of Scan) - no more metadata
                else if (marker == 0xDA)
                {
                    break;
                }
                else
                {
                    // Skip other segments
                    fs.Seek(length - 2, SeekOrigin.Current);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parse EXIF data to find embedded thumbnail.
    /// </summary>
    private static byte[] ParseExifForThumbnail(byte[] exifData)
    {
        if (exifData == null || exifData.Length < 14)
            return null;

        // Check "Exif\0\0" header
        if (exifData[0] != 'E' || exifData[1] != 'x' || exifData[2] != 'i' || exifData[3] != 'f')
            return null;

        int tiffStart = 6; // After "Exif\0\0"

        // Check byte order
        bool isLittleEndian = exifData[tiffStart] == 'I' && exifData[tiffStart + 1] == 'I';
        // bool isBigEndian = exifData[tiffStart] == 'M' && exifData[tiffStart + 1] == 'M';

        // Read IFD0 offset
        int ifd0Offset = ReadInt32(exifData, tiffStart + 4, isLittleEndian);
        if (ifd0Offset <= 0 || tiffStart + ifd0Offset >= exifData.Length)
            return null;

        // Parse IFD0 to find IFD1 (thumbnail IFD)
        int ifd0Pos = tiffStart + ifd0Offset;
        int numEntries = ReadInt16(exifData, ifd0Pos, isLittleEndian);
        if (numEntries <= 0 || numEntries > 100)
            return null;

        // Skip to end of IFD0 to find IFD1 offset
        int ifd1OffsetPos = ifd0Pos + 2 + (numEntries * 12);
        if (ifd1OffsetPos + 4 > exifData.Length)
            return null;

        int ifd1Offset = ReadInt32(exifData, ifd1OffsetPos, isLittleEndian);
        if (ifd1Offset <= 0 || tiffStart + ifd1Offset >= exifData.Length)
            return null;

        // Parse IFD1 for thumbnail info
        int ifd1Pos = tiffStart + ifd1Offset;
        int ifd1Entries = ReadInt16(exifData, ifd1Pos, isLittleEndian);
        if (ifd1Entries <= 0 || ifd1Entries > 50)
            return null;

        int thumbOffset = -1;
        int thumbLength = -1;
        int compression = 0;

        for (int i = 0; i < ifd1Entries; i++)
        {
            int entryPos = ifd1Pos + 2 + (i * 12);
            if (entryPos + 12 > exifData.Length)
                break;

            int tag = ReadInt16(exifData, entryPos, isLittleEndian);
            int value = ReadInt32(exifData, entryPos + 8, isLittleEndian);

            switch (tag)
            {
                case 0x0103: // Compression
                    compression = value;
                    break;
                case 0x0201: // JPEGInterchangeFormat (thumbnail offset)
                    thumbOffset = value;
                    break;
                case 0x0202: // JPEGInterchangeFormatLength (thumbnail length)
                    thumbLength = value;
                    break;
            }
        }

        // Check if we have a JPEG thumbnail (compression = 6 means JPEG)
        if (compression == 6 && thumbOffset > 0 && thumbLength > 0)
        {
            int absoluteOffset = tiffStart + thumbOffset;
            if (absoluteOffset + thumbLength <= exifData.Length)
            {
                byte[] thumbnailData = new byte[thumbLength];
                Array.Copy(exifData, absoluteOffset, thumbnailData, 0, thumbLength);
                return thumbnailData;
            }
        }

        return null;
    }

    private static int ReadInt16(byte[] data, int offset, bool littleEndian)
    {
        if (offset + 2 > data.Length) return 0;
        if (littleEndian)
            return data[offset] | (data[offset + 1] << 8);
        else
            return (data[offset] << 8) | data[offset + 1];
    }

    private static int ReadInt32(byte[] data, int offset, bool littleEndian)
    {
        if (offset + 4 > data.Length) return 0;
        if (littleEndian)
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        else
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
    #endregion

    #region TIFF Thumbnail
    /// <summary>
    /// Extract thumbnail from TIFF file.
    /// TIFF files can have multiple IFDs, with the second often being a thumbnail.
    /// </summary>
    private static byte[] ExtractTiffThumbnail(string filePath)
    {
        // TIFF thumbnail extraction is similar to EXIF but with different structure
        // For simplicity, we'll skip this as TIFF thumbnails are less common
        // and the EXIF extractor handles most cases via embedded JPEG thumbnails
        return null;
    }
    #endregion
}
