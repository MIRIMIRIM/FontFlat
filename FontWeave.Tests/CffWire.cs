// Minimal CFF1 wire walkers used to build malformed fixtures.
//
// The tests need to submit images that a well-formed writer would never produce (a negative
// charset offset, an FDSelect range starting outside the glyph domain). Those are built by
// patching a valid image at the byte level, and the patching is derived from the CFF structure
// read here rather than from anything the product reports.
using System.Buffers.Binary;

namespace FontWeave.Tests;

internal static class CffWire
{
    /// <summary>Bounds of the single Top DICT's payload inside the CFF image.</summary>
    internal static (int PayloadAt, int Length) TopDictPayload(byte[] cff)
    {
        // header, then the Name INDEX, then the Top DICT INDEX.
        int at = cff[2];
        (at, _) = IndexEnd(cff, at);
        int topAt = at;
        int count = BinaryPrimitives.ReadUInt16BigEndian(cff.AsSpan(topAt));
        if (count != 1) throw new InvalidOperationException("Top DICT INDEX is not single-item");
        int offSize = cff[topAt + 2];
        int first = topAt + 3;
        // INDEX offsets are 1-based counts from the byte before the data area.
        int dataAreaBase = first + (count + 1) * offSize - 1;
        long start = ReadOffset(cff, first, offSize);
        long end = ReadOffset(cff, first + offSize, offSize);
        return (dataAreaBase + (int)start, (int)(end - start));
    }

    /// <summary>Finds a Top DICT entry and returns the position and marker byte of its first
    /// operand, which is where a patch has to be written.</summary>
    internal static (int OperandAt, byte Marker) FindTopEntry(byte[] cff, int op)
    {
        var (payloadAt, length) = TopDictPayload(cff);
        var stack = new List<(int At, byte Marker)>();
        int at = 0;
        while (at < length)
        {
            byte b0 = cff[payloadAt + at];
            if (b0 <= 21)
            {
                int width = b0 == 12 ? 2 : 1;
                int current = b0 == 12 ? 0x0C00 | cff[payloadAt + at + 1] : b0;
                if (current == op)
                {
                    if (stack.Count == 0)
                        throw new InvalidOperationException($"operator 0x{op:x4} has no operands");
                    return (payloadAt + stack[0].At, stack[0].Marker);
                }
                stack.Clear();
                at += width;
            }
            else if (b0 is >= 32 and <= 246) { stack.Add((at, b0)); at += 1; }
            else if (b0 is >= 247 and <= 254) { stack.Add((at, b0)); at += 2; }
            else if (b0 == 28) { stack.Add((at, b0)); at += 3; }
            else if (b0 == 29) { stack.Add((at, b0)); at += 5; }
            else if (b0 == 30)
            {
                stack.Add((at, b0));
                at += 1;
                while (at < length && (cff[payloadAt + at] >> 4) != 0xF &&
                    (cff[payloadAt + at] & 0xF) != 0xF) at += 1;
                at += 1;
            }
            else throw new InvalidOperationException("reserved DICT byte");
        }
        throw new InvalidOperationException($"operator 0x{op:x4} not present");
    }

    /// <summary>Reads the integer at an operand position.</summary>
    internal static int ReadOperand(byte[] cff, int at) => cff[at] switch
    {
        28 => (short)((cff[at + 1] << 8) | cff[at + 2]),
        29 => (cff[at + 1] << 24) | (cff[at + 2] << 16) | (cff[at + 3] << 8) | cff[at + 4],
        >= 32 and <= 246 => cff[at] - 139,
        >= 247 and <= 250 => ((cff[at] - 247) << 8) + cff[at + 1] + 108,
        _ => -((cff[at] - 251) << 8) - cff[at + 1] - 108
    };

    internal static int MarkerLength(byte marker) => marker switch
    {
        29 => 5,
        28 => 3,
        >= 247 and <= 254 => 2,
        >= 32 and <= 246 => 1,
        _ => throw new InvalidOperationException("not an integer operand")
    };

    /// <summary>Overwrites an operand in place. The width is taken from the marker already
    /// there, so the surrounding layout never moves; the caller must choose a value that fits
    /// the existing width.</summary>
    internal static void PatchOperand(byte[] cff, int at, byte marker, int value)
    {
        int length = MarkerLength(marker);
        byte[] encoded = length switch
        {
            1 => value is >= -107 and <= 107
                ? [(byte)(value + 139)]
                : throw new InvalidOperationException("does not fit one byte"),
            2 => value is >= 108 and <= 1131
                ? [(byte)(247 + ((value - 108) >> 8)), (byte)((value - 108) & 0xFF)]
                : value is >= -1131 and <= -108
                    ? [(byte)(251 + ((-value - 108) >> 8)), (byte)((-value - 108) & 0xFF)]
                    : throw new InvalidOperationException("does not fit two bytes"),
            3 => value is >= short.MinValue and <= short.MaxValue
                ? [28, (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)]
                : throw new InvalidOperationException("does not fit a shortint"),
            _ =>
            [
                29, (byte)((value >> 24) & 0xFF), (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF)
            ]
        };
        Array.Copy(encoded, 0, cff, at, length);
    }

    /// <summary>Finds the FDSelect operator (0x0c25) and returns the absolute offset of the
    /// FDSelect structure, plus the operand's position and marker so a caller can relocate it.
    /// </summary>
    internal static int FdSelectAt(byte[] cff, out int operandAt, out byte marker)
    {
        (operandAt, marker) = FindTopEntry(cff, 0x0C25);
        return ReadOperand(cff, operandAt);
    }

    internal static (int End, int Count) IndexEnd(byte[] data, int at)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));
        if (count == 0) return (at + 2, 0);
        int offSize = data[at + 2];
        int last = at + 3 + count * offSize;
        int end = at + 3 + (count + 1) * offSize - 1 + (int)ReadOffset(data, last, offSize);
        return (end, count);
    }

    internal static long ReadOffset(byte[] data, int at, int size)
    {
        long value = 0;
        for (int i = 0; i < size; i++) value = (value << 8) | data[at + i];
        return value;
    }
}
