using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public static class CoverageTableCompiler
{
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, int numGlyphs, ushort format,
        out CoverageTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default) =>
        TryCreate(glyphs, numGlyphs, format, out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, int numGlyphs, ushort format,
        out CoverageTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        ReadStatus status = CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format is not (1 or 2))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, format);
        if (glyphs.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, CoverageLayout.CountOffset), out diagnostic,
                (ulong)glyphs.Length, ushort.MaxValue);
        if (!budget.Work(2L * glyphs.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        int previous = -1, rangeCount = 0;
        for (int i = 0; i < glyphs.Length; i++)
        {
            ushort gid = glyphs[i];
            var at = location.AtLocal(DataField.GlyphId, CoverageLayout.HeaderLength +
                (ulong)i * CoverageLayout.GlyphStride, i);
            if (gid >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    at, out diagnostic, gid, (ulong)numGlyphs);
            if (gid <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    at, out diagnostic, gid, unchecked((ulong)(long)previous));
            if (i == 0 || gid != previous + 1) rangeCount++;
            previous = gid;
        }
        if (!budget.Buffer(2L * glyphs.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        int consumed = CoverageLayout.HeaderLength + (format == 1
            ? CoverageLayout.GlyphStride * glyphs.Length : CoverageLayout.RangeStride * rangeCount);
        model = new(glyphs.ToArray(), numGlyphs, format, location, consumed, rangeCount);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(CoverageTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(CoverageTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = Measure(model);
        if (!budget.Work(length + ExpansionWriteWork(model), model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(CoverageTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = Measure(model);
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        var budget = new BuildBudget(limits);
        if (!budget.Work(length + ExpansionWriteWork(model), model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static int Measure(CoverageTable model) => CoverageLayout.HeaderLength +
        (model.Format == 1 ? CoverageLayout.GlyphStride * model.Glyphs.Length : CoverageLayout.RangeStride * model.RangeCount);

    // Parent writers already charge encoded bytes, but compressed ranges still group every key.
    internal static long ExpansionWriteWork(CoverageTable model) => model.Format == 2 ? model.Glyphs.Length : 0;

    // Only called with immutable, validated models after every destination/offset has been preflighted.
    internal static void WritePlanned(CoverageTable model, Span<byte> destination)
        => WritePlanned(model.Glyphs, model.Format, model.RangeCount, destination, model.Location);

    internal static void WritePlanned(ReadOnlySpan<ushort> glyphs, ushort format, int rangeCount,
        Span<byte> destination, DataLocation location)
    {
        RequireWritten(CoverageView.TryWriteFormat(destination, format, out _, location));
        RequireWritten(CoverageView.TryWriteCount(destination,
            (ushort)(format == 1 ? glyphs.Length : rangeCount), out _, location));
        if (format == 1)
        {
            for (int i = 0; i < glyphs.Length; i++)
                RequireWritten(CoverageView.TryWriteGlyphRecord(destination, (ulong)glyphs.Length,
                    new CoverageView.GlyphRecordData(i, glyphs[i]), out _, location));
        }
        else
        {
            int ordinal = 0;
            for (int start = 0; start < glyphs.Length;)
            {
                int end = start;
                while (end + 1 < glyphs.Length && glyphs[end + 1] == glyphs[end] + 1) end++;
                RequireWritten(CoverageView.TryWriteRangeRecord(destination, (ulong)rangeCount,
                    new CoverageView.RangeRecordData(ordinal, glyphs[start], glyphs[end], (ushort)start),
                    out _, location));
                ordinal++;
                start = end + 1;
            }
        }
    }

    internal static int CountRanges(ReadOnlySpan<ushort> glyphs)
    {
        int count = glyphs.IsEmpty ? 0 : 1;
        for (int i = 1; i < glyphs.Length; i++) if (glyphs[i] != glyphs[i - 1] + 1) count++;
        return count;
    }

    internal static ReadStatus CheckNumGlyphs(int numGlyphs, DataLocation location, out FontDiagnostic diagnostic)
    {
        if (numGlyphs is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.NumGlyphs, 0), out diagnostic,
                unchecked((ulong)(long)numGlyphs), ushort.MaxValue);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted layout write failed.");
    }
}
