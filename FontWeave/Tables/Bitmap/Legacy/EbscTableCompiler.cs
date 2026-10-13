using System.Runtime.CompilerServices;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Canonical EBSC 2.0 emission. Every metrics and ppem byte is preserved.</summary>
public static class EbscTableCompiler
{
    // One bit for each UInt8 ppem pair. This bounds both duplicate detection
    // and explicit source-existence validation without unbounded containers.
    internal const int PpemMapLength = 8192;
    internal static int MapLength(int count) => count == 0 ? 0 : PpemMapLength;
    internal static int PpemPair(byte x, byte y) => (x << 8) | y;
    internal static bool HasPair(byte[] map, int pair) =>
        (map[pair >> 3] & (1 << (pair & 7))) != 0;
    internal static void AddPair(byte[] map, int pair) =>
        map[pair >> 3] |= (byte)(1 << (pair & 7));

    public static ReadStatus TryCreate(ushort majorVersion, ushort minorVersion,
        IReadOnlyList<EbscScaleRecord> records, out EbscTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(records);
        location = location == default ? DataLocation.Anonymous(EbscLayout.Tag) : location;
        var budget = new BuildBudget(limits);
        ReadStatus status = RequireVersion(majorVersion, minorVersion, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = records.Count;
        status = CheckRecords(records, count, location, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(28L * count, location, out diagnostic) ||
            !budget.Buffer(count * (long)Unsafe.SizeOf<EbscScaleRecord>(),
                location, out diagnostic)) return ReadStatus.ResourceLimit;
        var owned = new EbscScaleRecord[count];
        for (int i = 0; i < owned.Length; i++)
            owned[i] = records[i] with { Location = location.AtLocal(DataField.Payload,
                EbscLayout.RecordPosition(i), i) };
        model = new(majorVersion, minorVersion, owned, location, length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(EbscTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = TryCompile(model, out var bytes, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < bytes!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)bytes.Length);
        bytes.AsSpan().CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(EbscTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = RequireVersion(model.MajorVersion, model.MinorVersion,
            model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = model.SizeCount;
        status = CheckRecords(model.Records, count, model.Location, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var output = new byte[length];
        RequireWritten(EbscView.TryWriteMajorVersion(output, model.MajorVersion, out _, model.Location));
        RequireWritten(EbscView.TryWriteMinorVersion(output, model.MinorVersion, out _, model.Location));
        RequireWritten(EbscView.TryWriteNumSizes(output, (uint)count, out _, model.Location));
        for (int i = 0; i < count; i++)
        {
            var row = model.Records[i].ToWire(i);
            RequireWritten(EbscView.TryWriteBitmapScale(output, (ulong)count,
                row, out _, model.Location));
        }
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus CheckRecords(IReadOnlyList<EbscScaleRecord> records, int count,
        DataLocation location, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = 0;
        if (count < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 4), out diagnostic,
                unchecked((ulong)(long)count), 0UL);
        long required = EbscLayout.HeaderLength + (long)EbscLayout.BitmapScaleStride * count;
        if (required > int.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                location.AtLocal(DataField.Length, 4), out diagnostic, (ulong)required, int.MaxValue);
        int mapLength = MapLength(count);
        if (!budget.Work(29L * count + mapLength, location, out diagnostic) ||
            !budget.Buffer(mapLength, location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] targets = mapLength == 0 ? [] : new byte[mapLength];
        for (int i = 0; i < count; i++)
        {
            var record = records[i];
            int pair = PpemPair(record.PpemX, record.PpemY);
            if (HasPair(targets, pair))
            {
                for (int previous = 0; previous < i; previous++)
                {
                    var earlier = records[previous];
                    if (pair == PpemPair(earlier.PpemX, earlier.PpemY))
                        return DuplicateTarget(location, i, pair, previous, out diagnostic);
                }
            }
            AddPair(targets, pair);
        }
        length = (int)required;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus DuplicateTarget(DataLocation location, int recordIndex,
        int pair, int previous, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            location.AtLocal(DataField.Payload,
                EbscLayout.RecordPosition(recordIndex) + EbscLayout.PpemXAt, recordIndex),
            out diagnostic, (uint)pair, (uint)previous);

    internal static ReadStatus RequireVersion(ushort majorVersion, ushort minorVersion,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        if (majorVersion != EbscLayout.MajorVersion2)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, majorVersion, EbscLayout.MajorVersion2);
        if (minorVersion != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 2), out diagnostic, minorVersion, 0UL);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted EBSC write failed.");
    }
}
