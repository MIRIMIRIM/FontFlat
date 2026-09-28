using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Containers;

/// <summary>Container-only emitter: inputs are already selected and compiled tables.</summary>
internal static class SfntEmitter
{
    internal const int MaxOutputBytes = 64 * 1024 * 1024;

    internal static ReadStatus TryEmit(uint version, DataLocation location,
        SortedDictionary<uint, byte[]> tables, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        diagnostic = default;
        if (version is not (0x00010000 or 0x4F54544F))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.At(DataField.Version, 0), out diagnostic, version);
        if (tables.Count is < 1 or > 4095)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                location.At(DataField.Count, 0), out diagnostic, (ulong)tables.Count, 4095);
        if (!tables.TryGetValue(0x68656164, out byte[]? head) || head.Length < 54)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location with { TableTag = 0x68656164 }, out diagnostic);
        if (!budget.Work(tables.Count, location.At(DataField.Count, 0), out diagnostic))
            return ReadStatus.ResourceLimit;
        long length = 12 + 16L * tables.Count;
        foreach (byte[] table in tables.Values)
            length += (table.Length + 3L) & ~3L;
        if (length > MaxOutputBytes)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                location.At(DataField.Length, 0), out diagnostic, (ulong)length, MaxOutputBytes);
        DataLocation lengthLocation = location.At(DataField.Length, 0);
        if (!budget.Output(length, lengthLocation, out diagnostic) ||
            !budget.Work(3 * length, lengthLocation, out diagnostic)) return ReadStatus.ResourceLimit;

        byte[] result = new byte[(int)length];
        W32(result, 0, version);
        W16(result, 4, (ushort)tables.Count);
        int searchRange = 16, selector = 0;
        while (searchRange <= tables.Count * 8) { searchRange *= 2; selector++; }
        W16(result, 6, (ushort)searchRange);
        W16(result, 8, (ushort)selector);
        W16(result, 10, (ushort)(16 * tables.Count - searchRange));
        int cursor = 12 + 16 * tables.Count, ordinal = 0, headOffset = -1;
        foreach ((uint tag, byte[] table) in tables)
        {
            table.CopyTo(result.AsSpan(cursor));
            if (tag == 0x68656164)
            {
                headOffset = cursor;
                result.AsSpan(cursor + 8, 4).Clear();
            }
            int record = 12 + 16 * ordinal++;
            W32(result, record, tag);
            W32(result, record + 4, Checksum(result.AsSpan(cursor, table.Length)));
            W32(result, record + 8, (uint)cursor);
            W32(result, record + 12, (uint)table.Length);
            cursor += (table.Length + 3) & ~3;
        }
        W32(result, headOffset + 8, unchecked(0xB1B0AFBAu - Checksum(result)));
        output = result;
        return ReadStatus.Done;
    }

    private static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        int at = 0;
        for (; at + 4 <= bytes.Length; at += 4)
            sum = unchecked(sum + ReadWire.U32(bytes, at));
        uint last = 0;
        for (int i = 0; at + i < bytes.Length; i++)
            last |= (uint)bytes[at + i] << (24 - 8 * i);
        return unchecked(sum + last);
    }

    internal static void W16(Span<byte> bytes, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(at, 2), value);
    internal static void W32(Span<byte> bytes, int at, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(at, 4), value);
}
