using System;
using System.Buffers.Binary;
using System.Diagnostics;

namespace OTFontFile
{
    /// <summary>
    /// Performance-optimized extensions for MBOBuffer using Span<T> and BinaryPrimitives
    /// Provides zero-copy buffer operations with improved CPU efficiency
    /// </summary>
    public static class MBOBufferEx
    {
        /// <summary>
        /// Get a zero-copy span of the buffer for performance operations
        /// </summary>
        public static ReadOnlySpan<byte> GetSpan(this MBOBuffer buffer)
        {
            return new ReadOnlySpan<byte>(buffer.GetBuffer());
        }

        /// <summary>
        /// Get a mutable span of the buffer for write operations
        /// </summary>
        public static Span<byte> GetMutableSpan(this MBOBuffer buffer)
        {
            return new Span<byte>(buffer.GetBuffer());
        }

        /// <summary>
        /// Read a short (Big-Endian) from MBOBuffer using BinaryPrimitives - more efficient than manual bit shifts
        /// </summary>
        public static short GetShortEx(this MBOBuffer buffer, uint offset)
        {
            var span = buffer.GetSpan().Slice((int)offset);
            return BinaryPrimitives.ReadInt16BigEndian(span);
        }

        /// <summary>
        /// Read an unsigned short (Big-Endian) using BinaryPrimitives
        /// </summary>
        public static ushort GetUshortEx(this MBOBuffer buffer, uint offset)
        {
            var span = buffer.GetSpan().Slice((int)offset);
            return BinaryPrimitives.ReadUInt16BigEndian(span);
        }

        /// <summary>
        /// Read an int (Big-Endian) using BinaryPrimitives
        /// </summary>
        public static int GetIntEx(this MBOBuffer buffer, uint offset)
        {
            var span = buffer.GetSpan().Slice((int)offset);
            return BinaryPrimitives.ReadInt32BigEndian(span);
        }

        /// <summary>
        /// Read an unsigned int (Big-Endian) using BinaryPrimitives
        /// </summary>
        public static uint GetUintEx(this MBOBuffer buffer, uint offset)
        {
            var span = buffer.GetSpan().Slice((int)offset);
            return BinaryPrimitives.ReadUInt32BigEndian(span);
        }

        /// <summary>
        /// Write a short (Big-Endian) using BinaryPrimitives
        /// </summary>
        public static void SetShortEx(this MBOBuffer buffer, short value, uint offset)
        {
            var span = buffer.GetMutableSpan().Slice((int)offset);
            BinaryPrimitives.WriteInt16BigEndian(span, value);
        }

        /// <summary>
        /// Write an unsigned short (Big-Endian) using BinaryPrimitives
        /// </summary>
        public static void SetUshortEx(this MBOBuffer buffer, ushort value, uint offset)
        {
            var span = buffer.GetMutableSpan().Slice((int)offset);
            BinaryPrimitives.WriteUInt16BigEndian(span, value);
        }

        /// <summary>
        /// Write an int (Big-Endian) using BinaryPrimitives
        /// </summary>
        public static void SetIntEx(this MBOBuffer buffer, int value, uint offset)
        {
            var span = buffer.GetMutableSpan().Slice((int)offset);
            BinaryPrimitives.WriteInt32BigEndian(span, value);
        }

        /// <summary>
        /// Write an unsigned int (Big-Endian) using BinaryPrimitives
        /// </summary>
        public static void SetUintEx(this MBOBuffer buffer, uint value, uint offset)
        {
            var span = buffer.GetMutableSpan().Slice((int)offset);
            BinaryPrimitives.WriteUInt32BigEndian(span, value);
        }
    }
}

