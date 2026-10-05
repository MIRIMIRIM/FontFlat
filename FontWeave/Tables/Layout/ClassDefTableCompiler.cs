using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public static class ClassDefTableCompiler
{
    /// <summary>Format 1 requires contiguous explicit membership. Use an explicit class-zero
    /// range to retain intervening slots, or format 2 to preserve membership gaps.</summary>
    public static ReadStatus TryCreate(ReadOnlySpan<ClassDefRange> ranges, int numGlyphs, ushort format,
        out ClassDefTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default) =>
        TryCreate(ranges, numGlyphs, format, out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(ReadOnlySpan<ClassDefRange> ranges, int numGlyphs, ushort format,
        out ClassDefTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format is not (1 or 2))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, format);
        if (ranges.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, ClassDefLayout.HeaderRecordOffset), out diagnostic,
                (ulong)ranges.Length, ushort.MaxValue);
        if (!budget.Work(ranges.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        int previous = -1;
        for (int i = 0; i < ranges.Length; i++)
        {
            var range = ranges[i];
            ulong row = ClassDefLayout.SparseRangesOffset + (ulong)i * ClassDefLayout.RangeStride;
            if (range.EndGlyph < range.StartGlyph)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.GlyphId, row + ClassDefLayout.RangeEndOffset, i),
                    out diagnostic, range.EndGlyph, range.StartGlyph);
            if (range.StartGlyph <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.GlyphId, row, i), out diagnostic,
                    range.StartGlyph, unchecked((ulong)(long)previous));
            if (range.EndGlyph >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId, row + ClassDefLayout.RangeEndOffset, i),
                    out diagnostic, range.EndGlyph, (ulong)numGlyphs);
            if (format == 1 && i > 0 && range.StartGlyph != previous + 1)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.GlyphId, row, i), out diagnostic,
                    range.StartGlyph, (ulong)(previous + 1));
            previous = range.EndGlyph;
        }
        int denseCount = ranges.IsEmpty ? 0 : ranges[^1].EndGlyph - ranges[0].StartGlyph + 1;
        if (format == 1 && denseCount > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Count, ClassDefLayout.DenseCountOffset),
                out diagnostic, (ulong)denseCount, ushort.MaxValue);
        if (!budget.Buffer(6L * ranges.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        int consumed = format == 1
            ? ClassDefLayout.DenseValuesOffset + ClassDefLayout.ValueStride * denseCount
            : ClassDefLayout.SparseRangesOffset + ClassDefLayout.RangeStride * ranges.Length;
        model = new(ranges.ToArray(), numGlyphs, format, location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(ClassDefTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(ClassDefTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = Measure(model);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(ClassDefTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = Measure(model);
        // Orderings coexist by registration: this GDEF-chain leaf gains the spec 7.3 budget-first order with M2/T4-02 (OQ-M1e-2).
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        var budget = new BuildBudget(limits);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static int Measure(ClassDefTable model) => model.Format == 1
        ? ClassDefLayout.DenseValuesOffset + ClassDefLayout.ValueStride * DenseCount(model)
        : ClassDefLayout.SparseRangesOffset + ClassDefLayout.RangeStride * model.Ranges.Length;

    internal static void WritePlanned(ClassDefTable model, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(ClassDefView.TryWriteFormat(destination, model.Format, out _, model.Location));
        if (model.Format == 1)
        {
            int count = DenseCount(model);
            ushort start = model.Ranges.IsEmpty ? (ushort)0 : model.Ranges[0].StartGlyph;
            CoverageTableCompiler.RequireWritten(ClassDefView.TryWriteDenseHeaderAt(destination,
                ClassDefLayout.HeaderRecordOffset, new ClassDefView.DenseHeaderData(-1, start, (ushort)count),
                out _, model.Location));
            int rangeAt = 0;
            for (int i = 0; i < count; i++)
            {
                int gid = start + i;
                while (rangeAt < model.Ranges.Length && model.Ranges[rangeAt].EndGlyph < gid) rangeAt++;
                // Format-1 models retain every slot; creation rejects unrepresented gaps.
                ushort value = model.Ranges[rangeAt].ClassValue;
                CoverageTableCompiler.RequireWritten(ClassDefView.TryWriteClassValueAt(destination,
                    ClassDefLayout.DenseValuesOffset + (ulong)i * ClassDefLayout.ValueStride,
                    new ClassDefView.ClassValueData(i, value), out _, model.Location));
            }
        }
        else
        {
            CoverageTableCompiler.RequireWritten(ClassDefView.TryWriteSparseHeaderAt(destination,
                ClassDefLayout.HeaderRecordOffset, new ClassDefView.SparseHeaderData(-1, (ushort)model.Ranges.Length),
                out _, model.Location));
            for (int i = 0; i < model.Ranges.Length; i++)
            {
                var range = model.Ranges[i];
                CoverageTableCompiler.RequireWritten(ClassDefView.TryWriteClassRangeAt(destination,
                    ClassDefLayout.SparseRangesOffset + (ulong)i * ClassDefLayout.RangeStride,
                    new ClassDefView.ClassRangeData(i, range.StartGlyph, range.EndGlyph, range.ClassValue),
                    out _, model.Location));
            }
        }
    }

    private static int DenseCount(ClassDefTable model) => model.Ranges.IsEmpty
        ? 0 : model.Ranges[^1].EndGlyph - model.Ranges[0].StartGlyph + 1;
}
