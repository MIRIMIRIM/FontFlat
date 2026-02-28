using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Numerics;

namespace CJKCharacterCount.Core;

public readonly struct UnicodeBlock
{
    public required string Name { get; init; }
    public required int StartCode { get; init; } // Min code point for fast check
    public required int EndCode { get; init; }   // Max code point for fast check
    public required ReadOnlyMemory<(int Start, int End)> AssignedRanges { get; init; }

    public bool Contains(int codePoint)
    {
        if (codePoint < StartCode || codePoint > EndCode)
            return false;

        var span = AssignedRanges.Span;
        // Linear scan is fast for small number of ranges (most blocks have 1)
        foreach (var range in span)
        {
            if (codePoint >= range.Start && codePoint <= range.End)
                return true;
        }
        return false;
    }

    public int CountOverlap(ReadOnlySpan<int> sortedCodePoints)
    {
        int count = 0;
        var ranges = AssignedRanges.Span;

        foreach (var (Start, End) in ranges)
        {
            count += CountInRange(sortedCodePoints, Start, End);
        }
        return count;
    }

    private static int CountInRange(ReadOnlySpan<int> codePoints, int start, int end)
    {
        int count = 0;
        int i = 0;

        // Vector optimization: check 8 integers at a time (AVX2)
        if (Vector256.IsHardwareAccelerated && codePoints.Length >= Vector256<int>.Count)
        {
            var vStart = Vector256.Create(start);
            var vEnd = Vector256.Create(end);
            ref var searchSpace = ref MemoryMarshal.GetReference(codePoints);

            for (; i <= codePoints.Length - Vector256<int>.Count; i += Vector256<int>.Count)
            {
                var v = Vector256.LoadUnsafe(ref searchSpace, (nuint)i);

                // Compare: v >= start && v <= end
                var geStart = Vector256.GreaterThanOrEqual(v, vStart);
                var leEnd = Vector256.LessThanOrEqual(v, vEnd);
                var inRange = Vector256.BitwiseAnd(geStart, leEnd);

                if (inRange == Vector256<int>.Zero) continue;

                count += BitOperations.PopCount(inRange.ExtractMostSignificantBits());
            }
        }

        // Scalar fallback
        for (; i < codePoints.Length; i++)
        {
            var val = codePoints[i];
            if (val >= start && val <= end)
                count++;
        }
        return count;
    }
}
