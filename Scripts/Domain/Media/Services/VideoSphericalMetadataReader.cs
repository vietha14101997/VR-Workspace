using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using VRWorkspace.Media.UI;
using VRWorkspace.UI.RTT.Services;

namespace VRWorkspace.Media.Utils
{
    /// <summary>
    /// Result of reading spherical video metadata from file.
    /// </summary>
    public struct SphericalVideoMetadata
    {
        /// <summary>True if any spherical metadata was found</summary>
        public bool HasMetadata;

        /// <summary>Projection type: "equirectangular", "cubemap", or null</summary>
        public string ProjectionType;

        /// <summary>Stereo mode: 0=mono, 1=top-bottom, 2=left-right, -1=not found</summary>
        public int StereoMode;

        /// <summary>Whether this is a full sphere (360) or half sphere (180)</summary>
        public bool IsFullSphere;

        /// <summary>Source of metadata: "sv3d", "xmp", "none"</summary>
        public string Source;

        /// <summary>Full panorama width pixels from XMP (for 180 vs 360 disambiguation)</summary>
        public int FullPanoWidthPixels;

        /// <summary>Cropped area image width pixels from XMP</summary>
        public int CroppedAreaImageWidthPixels;

        public static SphericalVideoMetadata Empty => new SphericalVideoMetadata
        {
            HasMetadata = false,
            ProjectionType = null,
            StereoMode = -1,
            IsFullSphere = false,
            Source = "none",
            FullPanoWidthPixels = 0,
            CroppedAreaImageWidthPixels = 0
        };
    }

    /// <summary>
    /// Reads spherical video metadata from MP4/MOV files.
    /// Supports Google Spatial Media V2 (sv3d/st3d ISO BMFF boxes)
    /// and V1 (XMP GSpherical).
    /// </summary>
    public static class VideoSphericalMetadataReader
    {
        #region Cache
        private static string _cachedFilePath;
        private static SphericalVideoMetadata _cachedResult;
        #endregion

        #region Supported Extensions
        private static readonly string[] SupportedExtensions = { ".mp4", ".m4v", ".mov", ".m4a" };
        #endregion

        /// <summary>
        /// Read spherical video metadata from file. Results are cached per file path.
        /// </summary>
        public static SphericalVideoMetadata ReadMetadata(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return SphericalVideoMetadata.Empty;

            // Cache check
            if (filePath == _cachedFilePath)
                return _cachedResult;

            _cachedFilePath = filePath;
            _cachedResult = ReadMetadataInternal(filePath);
            return _cachedResult;
        }

        /// <summary>
        /// Clear the metadata cache.
        /// </summary>
        public static void ClearCache()
        {
            _cachedFilePath = null;
            _cachedResult = SphericalVideoMetadata.Empty;
        }

        private static SphericalVideoMetadata ReadMetadataInternal(string filePath)
        {
            if (!File.Exists(filePath))
                return SphericalVideoMetadata.Empty;

            // Check supported extension
            string ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            bool supported = false;
            for (int i = 0; i < SupportedExtensions.Length; i++)
            {
                if (ext == SupportedExtensions[i]) { supported = true; break; }
            }
            if (!supported)
                return SphericalVideoMetadata.Empty;

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {
                    var result = SphericalVideoMetadata.Empty;

                    // Try V2 (sv3d/st3d) first - more precise
                    if (TryReadV2Metadata(reader, fs.Length, ref result))
                    {
                        result.Source = "sv3d";
                        result.HasMetadata = true;
                        return result;
                    }

                    // Try V1 (XMP GSpherical)
                    fs.Position = 0;
                    if (TryReadV1XmpMetadata(reader, fs.Length, ref result))
                    {
                        result.Source = "xmp";
                        result.HasMetadata = true;
                        return result;
                    }

                    return SphericalVideoMetadata.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VideoSphericalMetadataReader] Failed to read metadata from {Path.GetFileName(filePath)}: {ex.Message}");
                return SphericalVideoMetadata.Empty;
            }
        }

        #region V2: sv3d/st3d (ISO BMFF)

        private static bool TryReadV2Metadata(BinaryReader reader, long fileLength, ref SphericalVideoMetadata result)
        {
            var fs = reader.BaseStream;
            fs.Position = 0;

            // Find moov atom
            long moovPos = Mp4AtomParser.FindAtom(reader, fileLength, "moov");
            if (moovPos < 0) return false;

            fs.Position = moovPos;
            long moovSize = Mp4AtomParser.ReadAtomSize(reader, out _);
            long moovEnd = moovPos + moovSize;
            fs.Position = moovPos + 8;

            // Find video track
            if (!TryFindVideoTrackSampleEntry(reader, moovEnd, out long sampleEntryPos, out long sampleEntryEnd))
                return false;

            // Navigate past sample entry fixed fields (78 bytes from start of sample entry)
            // Sample entry: 8 (header) + 70 (fixed fields) = 78
            long childBoxesStart = sampleEntryPos + 78;
            if (childBoxesStart >= sampleEntryEnd)
                return false;

            bool foundSomething = false;

            // Search for sv3d
            fs.Position = childBoxesStart;
            long sv3dPos = Mp4AtomParser.FindAtomWithin(reader, sampleEntryEnd, "sv3d");
            if (sv3dPos >= 0)
            {
                fs.Position = sv3dPos;
                long sv3dSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                long sv3dEnd = sv3dPos + sv3dSize;
                fs.Position = sv3dPos + 8;

                TryParseSv3d(reader, sv3dEnd, ref result);
                foundSomething = true;
            }

            // Search for st3d
            fs.Position = childBoxesStart;
            long st3dPos = Mp4AtomParser.FindAtomWithin(reader, sampleEntryEnd, "st3d");
            if (st3dPos >= 0)
            {
                fs.Position = st3dPos;
                long st3dSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                fs.Position = st3dPos + 8;

                TryParseSt3d(reader, ref result);
                foundSomething = true;
            }

            return foundSomething;
        }

        /// <summary>
        /// Find the video track and navigate to the first sample entry inside stsd.
        /// Returns the position and end of the first sample entry.
        /// </summary>
        private static bool TryFindVideoTrackSampleEntry(BinaryReader reader, long moovEnd, out long sampleEntryPos, out long sampleEntryEnd)
        {
            sampleEntryPos = -1;
            sampleEntryEnd = -1;

            var fs = reader.BaseStream;

            // Iterate trak atoms within moov
            while (fs.Position < moovEnd - 8)
            {
                long trakPos = fs.Position;
                long trakAtomPos = Mp4AtomParser.FindAtomWithin(reader, moovEnd, "trak");
                if (trakAtomPos < 0) break;

                fs.Position = trakAtomPos;
                long trakSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                long trakEnd = trakAtomPos + trakSize;
                fs.Position = trakAtomPos + 8;

                // Check if this is a video track by finding mdia/hdlr
                if (IsVideoTrack(reader, trakEnd))
                {
                    // Navigate to stsd: trak -> mdia -> minf -> stbl -> stsd
                    fs.Position = trakAtomPos + 8;
                    long mdiaPos = Mp4AtomParser.FindAtomWithin(reader, trakEnd, "mdia");
                    if (mdiaPos < 0) { fs.Position = trakEnd; continue; }

                    fs.Position = mdiaPos;
                    long mdiaSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                    long mdiaEnd = mdiaPos + mdiaSize;
                    fs.Position = mdiaPos + 8;

                    long minfPos = Mp4AtomParser.FindAtomWithin(reader, mdiaEnd, "minf");
                    if (minfPos < 0) { fs.Position = trakEnd; continue; }

                    fs.Position = minfPos;
                    long minfSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                    long minfEnd = minfPos + minfSize;
                    fs.Position = minfPos + 8;

                    long stblPos = Mp4AtomParser.FindAtomWithin(reader, minfEnd, "stbl");
                    if (stblPos < 0) { fs.Position = trakEnd; continue; }

                    fs.Position = stblPos;
                    long stblSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                    long stblEnd = stblPos + stblSize;
                    fs.Position = stblPos + 8;

                    long stsdPos = Mp4AtomParser.FindAtomWithin(reader, stblEnd, "stsd");
                    if (stsdPos < 0) { fs.Position = trakEnd; continue; }

                    fs.Position = stsdPos;
                    long stsdSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                    long stsdEnd = stsdPos + stsdSize;

                    // stsd is a FullBox: 8 (header) + 4 (version/flags) + 4 (entry_count)
                    fs.Position = stsdPos + 16;

                    if (fs.Position >= stsdEnd) { fs.Position = trakEnd; continue; }

                    // Read first sample entry
                    sampleEntryPos = fs.Position;
                    long entrySize = Mp4AtomParser.ReadAtomSize(reader, out _);
                    sampleEntryEnd = sampleEntryPos + entrySize;

                    return true;
                }

                fs.Position = trakEnd;
            }

            return false;
        }

        /// <summary>
        /// Check if a trak atom is a video track by reading its mdia/hdlr handler_type.
        /// </summary>
        private static bool IsVideoTrack(BinaryReader reader, long trakEnd)
        {
            var fs = reader.BaseStream;
            long savedPos = fs.Position;

            try
            {
                // Find mdia within trak
                long mdiaPos = Mp4AtomParser.FindAtomWithin(reader, trakEnd, "mdia");
                if (mdiaPos < 0) return false;

                fs.Position = mdiaPos;
                long mdiaSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                long mdiaEnd = mdiaPos + mdiaSize;
                fs.Position = mdiaPos + 8;

                // Find hdlr within mdia
                long hdlrPos = Mp4AtomParser.FindAtomWithin(reader, mdiaEnd, "hdlr");
                if (hdlrPos < 0) return false;

                // hdlr structure: 8 (header) + 4 (version/flags) + 4 (pre_defined) + 4 (handler_type)
                fs.Position = hdlrPos + 16;

                if (fs.Position + 4 > trakEnd) return false;

                byte[] handlerType = reader.ReadBytes(4);
                string handler = Encoding.ASCII.GetString(handlerType);

                return handler == "vide";
            }
            catch
            {
                return false;
            }
            finally
            {
                fs.Position = savedPos;
            }
        }

        private static void TryParseSv3d(BinaryReader reader, long sv3dEnd, ref SphericalVideoMetadata result)
        {
            var fs = reader.BaseStream;

            // Find proj box within sv3d
            long projPos = Mp4AtomParser.FindAtomWithin(reader, sv3dEnd, "proj");
            if (projPos < 0) return;

            fs.Position = projPos;
            long projSize = Mp4AtomParser.ReadAtomSize(reader, out _);
            long projEnd = projPos + projSize;
            fs.Position = projPos + 8;

            // Look for equi (equirectangular) or cbmp (cubemap) within proj
            long searchStart = fs.Position;

            // Check for equi
            fs.Position = searchStart;
            long equiPos = Mp4AtomParser.FindAtomWithin(reader, projEnd, "equi");
            if (equiPos >= 0)
            {
                result.ProjectionType = "equirectangular";

                // equi is a FullBox: 8 (header) + 4 (version/flags) + 4*4 (bounds)
                fs.Position = equiPos + 12; // skip header + version/flags

                if (fs.Position + 16 <= projEnd)
                {
                    uint boundsTop = Mp4AtomParser.ReadUInt32BE(reader);
                    uint boundsBottom = Mp4AtomParser.ReadUInt32BE(reader);
                    uint boundsLeft = Mp4AtomParser.ReadUInt32BE(reader);
                    uint boundsRight = Mp4AtomParser.ReadUInt32BE(reader);

                    // If all bounds are 0, it's a full sphere (360)
                    // Non-zero bounds indicate partial sphere (could be 180)
                    result.IsFullSphere = (boundsTop == 0 && boundsBottom == 0 && boundsLeft == 0 && boundsRight == 0);
                }
                else
                {
                    result.IsFullSphere = true; // Default to full sphere if can't read bounds
                }
                return;
            }

            // Check for cbmp
            fs.Position = searchStart;
            long cbmpPos = Mp4AtomParser.FindAtomWithin(reader, projEnd, "cbmp");
            if (cbmpPos >= 0)
            {
                result.ProjectionType = "cubemap";
                result.IsFullSphere = true;
            }
        }

        private static void TryParseSt3d(BinaryReader reader, ref SphericalVideoMetadata result)
        {
            var fs = reader.BaseStream;

            // st3d is a FullBox: after header comes 4 bytes version/flags
            if (fs.Position + 4 > fs.Length) return;
            reader.ReadBytes(4); // version + flags

            if (fs.Position + 1 > fs.Length) return;
            byte stereoMode = reader.ReadByte();

            // 0 = mono, 1 = top-bottom, 2 = left-right
            result.StereoMode = stereoMode;
        }

        #endregion

        #region V1: XMP GSpherical

        private static bool TryReadV1XmpMetadata(BinaryReader reader, long fileLength, ref SphericalVideoMetadata result)
        {
            var fs = reader.BaseStream;
            fs.Position = 0;

            // Try 1: Find moov/udta/XMP_ atom
            long moovPos = Mp4AtomParser.FindAtom(reader, fileLength, "moov");
            if (moovPos >= 0)
            {
                fs.Position = moovPos;
                long moovSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                long moovEnd = moovPos + moovSize;
                fs.Position = moovPos + 8;

                long udtaPos = Mp4AtomParser.FindAtomWithin(reader, moovEnd, "udta");
                if (udtaPos >= 0)
                {
                    fs.Position = udtaPos;
                    long udtaSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                    long udtaEnd = udtaPos + udtaSize;
                    fs.Position = udtaPos + 8;

                    long xmpPos = Mp4AtomParser.FindAtomWithin(reader, udtaEnd, "XMP_");
                    if (xmpPos >= 0)
                    {
                        fs.Position = xmpPos;
                        long xmpSize = Mp4AtomParser.ReadAtomSize(reader, out _);
                        fs.Position = xmpPos + 8;

                        int dataSize = (int)(xmpSize - 8);
                        if (dataSize > 0 && dataSize < 1024 * 1024) // Max 1MB XMP
                        {
                            byte[] xmpData = reader.ReadBytes(dataSize);
                            string xmpText = Encoding.UTF8.GetString(xmpData);
                            if (ParseGSphericalXmp(xmpText, ref result))
                                return true;
                        }
                    }
                }
            }

            // Try 2: Find uuid atom with XMP UUID at top level
            fs.Position = 0;
            byte[] xmpUuid = {
                0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8,
                0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC
            };

            while (fs.Position < fileLength - 8)
            {
                long atomPos = fs.Position;
                long atomSize = Mp4AtomParser.ReadAtomSize(reader, out _);

                if (atomSize < 8) break;

                // Read atom type
                byte[] atomType = reader.ReadBytes(4);
                if (Encoding.ASCII.GetString(atomType) == "uuid")
                {
                    if (fs.Position + 16 <= atomPos + atomSize)
                    {
                        byte[] uuid = reader.ReadBytes(16);
                        if (MatchesUuid(uuid, xmpUuid))
                        {
                            int dataSize = (int)(atomSize - 28); // 8 header + 4 type + 16 uuid
                            if (dataSize > 0 && dataSize < 1024 * 1024)
                            {
                                byte[] xmpData = reader.ReadBytes(dataSize);
                                string xmpText = Encoding.UTF8.GetString(xmpData);
                                if (ParseGSphericalXmp(xmpText, ref result))
                                    return true;
                            }
                        }
                    }
                }

                fs.Position = atomPos + atomSize;
            }

            return false;
        }

        private static bool MatchesUuid(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        // Regex patterns for XMP GSpherical parsing
        private static readonly Regex SphericalRegex = new Regex(
            @"GSpherical:Spherical[>""=\s]+(true|false)", RegexOptions.IgnoreCase);
        private static readonly Regex ProjectionTypeRegex = new Regex(
            @"GSpherical:ProjectionType[>""=\s]+(\w+)", RegexOptions.IgnoreCase);
        private static readonly Regex StereoModeRegex = new Regex(
            @"GSpherical:StereoMode[>""=\s]+([\w-]+)", RegexOptions.IgnoreCase);
        private static readonly Regex FullPanoWidthRegex = new Regex(
            @"GSpherical:FullPanoWidthPixels[>""=\s]+(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex CroppedWidthRegex = new Regex(
            @"GSpherical:CroppedAreaImageWidthPixels[>""=\s]+(\d+)", RegexOptions.IgnoreCase);

        private static bool ParseGSphericalXmp(string xmpText, ref SphericalVideoMetadata result)
        {
            // Check if spherical metadata is present
            var sphericalMatch = SphericalRegex.Match(xmpText);
            if (!sphericalMatch.Success || sphericalMatch.Groups[1].Value.ToLowerInvariant() != "true")
                return false;

            // Projection type
            var projMatch = ProjectionTypeRegex.Match(xmpText);
            if (projMatch.Success)
            {
                result.ProjectionType = projMatch.Groups[1].Value.ToLowerInvariant();
            }
            else
            {
                result.ProjectionType = "equirectangular"; // Default for V1
            }

            // Stereo mode
            var stereoMatch = StereoModeRegex.Match(xmpText);
            if (stereoMatch.Success)
            {
                string stereoValue = stereoMatch.Groups[1].Value.ToLowerInvariant();
                switch (stereoValue)
                {
                    case "mono":
                        result.StereoMode = 0;
                        break;
                    case "top-bottom":
                        result.StereoMode = 1;
                        break;
                    case "left-right":
                        result.StereoMode = 2;
                        break;
                    default:
                        result.StereoMode = 0;
                        break;
                }
            }

            // Full pano width (for 180 vs 360 disambiguation)
            var fullPanoMatch = FullPanoWidthRegex.Match(xmpText);
            if (fullPanoMatch.Success && int.TryParse(fullPanoMatch.Groups[1].Value, out int fullPanoWidth))
            {
                result.FullPanoWidthPixels = fullPanoWidth;
            }

            var croppedMatch = CroppedWidthRegex.Match(xmpText);
            if (croppedMatch.Success && int.TryParse(croppedMatch.Groups[1].Value, out int croppedWidth))
            {
                result.CroppedAreaImageWidthPixels = croppedWidth;
            }

            // Determine if full sphere
            // If cropped area is roughly half of full pano, it's 180
            if (result.FullPanoWidthPixels > 0 && result.CroppedAreaImageWidthPixels > 0)
            {
                float ratio = (float)result.CroppedAreaImageWidthPixels / result.FullPanoWidthPixels;
                result.IsFullSphere = ratio > 0.75f; // If cropped is > 75% of full, treat as 360
            }
            else
            {
                result.IsFullSphere = true; // Default to 360 for V1 without crop info
            }

            return true;
        }

        #endregion

    }

}
