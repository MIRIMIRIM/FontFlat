using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace OTFontFile2;

public static class OpenTypeChecksum
{
    private static readonly Vector128<byte> Ssse3ByteSwap32Mask = Vector128.Create(
        (byte)3, (byte)2, (byte)1, (byte)0,
        (byte)7, (byte)6, (byte)5, (byte)4,
        (byte)11, (byte)10, (byte)9, (byte)8,
        (byte)15, (byte)14, (byte)13, (byte)12);

    private static readonly Vector256<byte> Avx2ByteSwap32Mask = Vector256.Create(
        (byte)3, (byte)2, (byte)1, (byte)0,
        (byte)7, (byte)6, (byte)5, (byte)4,
        (byte)11, (byte)10, (byte)9, (byte)8,
        (byte)15, (byte)14, (byte)13, (byte)12,
        (byte)3, (byte)2, (byte)1, (byte)0,
        (byte)7, (byte)6, (byte)5, (byte)4,
        (byte)11, (byte)10, (byte)9, (byte)8,
        (byte)15, (byte)14, (byte)13, (byte)12);

    // OpenType checksum is the sum of big-endian uint32 words,
    // with any trailing bytes padded with zeros to a 4-byte boundary.
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            uint sum = 0;
            int length = data.Length;
            int end = length & ~3;

            if (BitConverter.IsLittleEndian)
            {
                ref byte src = ref MemoryMarshal.GetReference(data);
                int offset = 0;

                if (Avx2.IsSupported && end >= 32)
                {
                    Vector256<uint> avxSum = Vector256<uint>.Zero;
                    int simdEnd = end - 31;

                    while (offset < simdEnd)
                    {
                        Vector256<byte> chunk = Unsafe.ReadUnaligned<Vector256<byte>>(ref Unsafe.Add(ref src, offset));
                        Vector256<byte> bigEndianWords = Avx2.Shuffle(chunk, Avx2ByteSwap32Mask);
                        avxSum = Avx2.Add(avxSum, bigEndianWords.AsUInt32());
                        offset += 32;
                    }

                    sum += SumLanes(avxSum);
                }

                if (Ssse3.IsSupported && end - offset >= 16)
                {
                    Vector128<uint> sseSum = Vector128<uint>.Zero;
                    int simdEnd = end - 15;

                    while (offset < simdEnd)
                    {
                        Vector128<byte> chunk = Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref src, offset));
                        Vector128<byte> bigEndianWords = Ssse3.Shuffle(chunk, Ssse3ByteSwap32Mask);
                        sseSum = Sse2.Add(sseSum, bigEndianWords.AsUInt32());
                        offset += 16;
                    }

                    sum += SumLanes(sseSum);
                }

                while (offset < end)
                {
                    uint word = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref src, offset));
                    sum += BinaryPrimitives.ReverseEndianness(word);
                    offset += 4;
                }
            }
            else
            {
                ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(data.Slice(0, end));
                for (int i = 0; i < words.Length; i++)
                    sum += words[i];
            }

            int remaining = length - end;
            if (remaining != 0)
            {
                Span<byte> tail = stackalloc byte[4];
                tail.Clear();
                data.Slice(end, remaining).CopyTo(tail);
                sum += BigEndian.ReadUInt32(tail, 0);
            }

            return sum;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint SumLanes(Vector128<uint> vector)
        => unchecked(vector.GetElement(0) + vector.GetElement(1) + vector.GetElement(2) + vector.GetElement(3));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint SumLanes(Vector256<uint> vector)
        => unchecked(
            vector.GetElement(0) + vector.GetElement(1) + vector.GetElement(2) + vector.GetElement(3) +
            vector.GetElement(4) + vector.GetElement(5) + vector.GetElement(6) + vector.GetElement(7));

    public static uint ComputeHeadDirectoryChecksum(ReadOnlySpan<byte> headTableData)
    {
        unchecked
        {
            uint sum = Compute(headTableData);
            if (headTableData.Length >= 12)
            {
                // checkSumAdjustment field at offset 8 is treated as 0 for checksum purposes.
                sum -= BigEndian.ReadUInt32(headTableData, 8);
            }
            return sum;
        }
    }
}
