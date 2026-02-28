using System;
using System.IO;
using System.Text;

namespace VRWorkspace.UI.RTT.Services
{
    /// <summary>
    /// Shared utility for parsing MP4/MOV ISO BMFF atom (box) structures.
    /// Handles standard 32-bit atom sizes and extended 64-bit sizes (size field == 1).
    /// All methods operate on the provided BinaryReader's BaseStream for positioning.
    /// </summary>
    public static class Mp4AtomParser
    {
        /// <summary>
        /// Find an atom of the given type starting from the reader's current stream position,
        /// searching up to (but not including) endPos.
        /// Returns the byte offset of the atom start, or -1 if not found.
        /// </summary>
        public static long FindAtom(BinaryReader reader, long endPos, string atomType)
        {
            long startPos = reader.BaseStream.Position;
            return FindAtomWithin(reader, endPos, atomType, startPos);
        }

        /// <summary>
        /// Find an atom of the given type within the byte range [startPos, endPos).
        /// If startPos is -1, searching begins from the reader's current stream position.
        /// Returns the byte offset of the atom start, or -1 if not found.
        /// Handles extended 64-bit atom sizes (size field == 1) and overflow protection.
        /// </summary>
        public static long FindAtomWithin(BinaryReader reader, long endPos, string atomType, long startPos = -1)
        {
            Stream fs = reader.BaseStream;
            if (startPos >= 0) fs.Position = startPos;

            byte[] targetType = Encoding.ASCII.GetBytes(atomType);

            while (fs.Position < endPos - 8)
            {
                long atomPos = fs.Position;

                // Read atom size (4 bytes, big-endian)
                uint size32 = ReadUInt32BE(reader);

                // size 0 is not handled here (means "rest of file") — treat as invalid
                if (size32 < 8 && size32 != 1) break;

                // Read atom type (4 bytes)
                byte[] type = reader.ReadBytes(4);
                if (type.Length < 4) break;

                long atomSize;
                if (size32 == 1)
                {
                    // Extended 64-bit size follows immediately after the type field
                    if (fs.Position + 8 > endPos) break;
                    atomSize = ReadInt64BE(reader);
                }
                else
                {
                    atomSize = size32;
                }

                if (atomSize < 8) break;

                // Check if this is the atom we're looking for
                if (type[0] == targetType[0] && type[1] == targetType[1] &&
                    type[2] == targetType[2] && type[3] == targetType[3])
                {
                    return atomPos;
                }

                // Skip to next atom; guard against overflow/infinite loop
                long nextPos = atomPos + atomSize;
                if (nextPos <= atomPos) break;
                fs.Position = nextPos;
            }

            return -1;
        }

        /// <summary>
        /// Read the size field of the current atom, handling extended 64-bit sizes.
        /// Advances the stream past the size field (and extended-size field when present).
        /// Returns the total atom size including the header. Sets isExtended when a 64-bit
        /// size was used.
        /// </summary>
        public static long ReadAtomSize(BinaryReader reader, out bool isExtended)
        {
            isExtended = false;
            uint size32 = ReadUInt32BE(reader);

            if (size32 == 1)
            {
                isExtended = true;
                return ReadInt64BE(reader);
            }

            return size32;
        }

        /// <summary>
        /// Read a 4-byte big-endian unsigned integer from the reader.
        /// </summary>
        public static uint ReadUInt32BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length < 4) return 0;
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        /// <summary>
        /// Read an 8-byte big-endian unsigned integer from the reader.
        /// </summary>
        public static ulong ReadUInt64BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(8);
            if (bytes.Length < 8) return 0;
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        /// <summary>
        /// Read a 2-byte big-endian unsigned integer from the reader.
        /// </summary>
        public static ushort ReadUInt16BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(2);
            if (bytes.Length < 2) return 0;
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToUInt16(bytes, 0);
        }

        /// <summary>
        /// Read an 8-byte big-endian signed integer from the reader.
        /// </summary>
        public static long ReadInt64BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(8);
            if (bytes.Length < 8) return 0;
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToInt64(bytes, 0);
        }
    }
}
