using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct ClassDefView
{
    public ReadStatus TryGetDenseHeader(out DenseHeaderData header, out FontDiagnostic diagnostic)
    {
        header = default;
        ReadStatus status = RequireFormat(1, out diagnostic);
        return status == ReadStatus.Done
            ? TryReadDenseHeaderAt(ClassDefLayout.HeaderRecordOffset, -1, out header, out diagnostic) : status;
    }

    public ReadStatus TryGetSparseHeader(out SparseHeaderData header, out FontDiagnostic diagnostic)
    {
        header = default;
        ReadStatus status = RequireFormat(2, out diagnostic);
        return status == ReadStatus.Done
            ? TryReadSparseHeaderAt(ClassDefLayout.HeaderRecordOffset, -1, out header, out diagnostic) : status;
    }

    public ReadStatus TryGetClassValue(int ordinal, out ushort value, out FontDiagnostic diagnostic)
    {
        value = 0;
        ReadStatus status = TryGetDenseHeader(out var header, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (ordinal < 0 || ordinal >= header.GlyphCount)
            return InvalidOrdinal(ordinal, header.GlyphCount, ClassDefLayout.DenseCountOffset, out diagnostic);
        status = TryReadClassValueAt(ClassDefLayout.DenseValuesOffset + (ulong)ordinal * ClassDefLayout.ValueStride,
            ordinal, out var record, out diagnostic);
        if (status == ReadStatus.Done) value = record.Value;
        return status;
    }

    public ReadStatus TryGetRange(int ordinal, out ClassRangeData range, out FontDiagnostic diagnostic)
    {
        range = default;
        ReadStatus status = TryGetSparseHeader(out var header, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (ordinal < 0 || ordinal >= header.RangeCount)
            return InvalidOrdinal(ordinal, header.RangeCount, ClassDefLayout.HeaderRecordOffset, out diagnostic);
        return TryReadClassRangeAt(ClassDefLayout.SparseRangesOffset + (ulong)ordinal * ClassDefLayout.RangeStride,
            ordinal, out range, out diagnostic);
    }

    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Inspect(numGlyphs, out _, out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out ClassDefTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out ClassDefTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = Inspect(numGlyphs, out int rangeCount, out int consumed, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(consumed, Location, out diagnostic) ||
            !budget.Buffer(6L * rangeCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var ranges = new ClassDefRange[rangeCount];
        if (Format == 1)
        {
            _ = TryGetDenseHeader(out var header, out _);
            int at = 0, start = 0;
            ushort previous = 0;
            for (int i = 0; i < header.GlyphCount; i++)
            {
                _ = TryGetClassValue(i, out ushort value, out _);
                if (i > 0 && value != previous)
                {
                    ranges[at++] = new((ushort)(header.StartGlyph + start), (ushort)(header.StartGlyph + i - 1), previous);
                    start = i;
                }
                previous = value;
            }
            if (header.GlyphCount > 0)
                ranges[at] = new((ushort)(header.StartGlyph + start),
                    (ushort)(header.StartGlyph + header.GlyphCount - 1), previous);
        }
        else
        {
            for (int i = 0; i < ranges.Length; i++)
            {
                _ = TryGetRange(i, out var range, out _);
                ranges[i] = new(range.StartGlyph, range.EndGlyph, range.ClassValue);
            }
        }
        model = new(ranges, numGlyphs, Format, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The GDEF GlyphClassDef consumer has a narrower class domain than generic ClassDef.
    internal ReadStatus ValidateClassLimit(ushort maxClassValue, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (Format == 1)
        {
            status = TryGetDenseHeader(out var header, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!budget.Work(header.GlyphCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            for (int i = 0; i < header.GlyphCount; i++)
            {
                status = TryGetClassValue(i, out ushort value, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (value > maxClassValue)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Payload, ClassDefLayout.DenseValuesOffset +
                            (ulong)i * ClassDefLayout.ValueStride, i), out diagnostic, value, maxClassValue);
            }
        }
        else
        {
            status = TryGetSparseHeader(out var header, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!budget.Work(header.RangeCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            for (int i = 0; i < header.RangeCount; i++)
            {
                status = TryGetRange(i, out var range, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (range.ClassValue > maxClassValue)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Payload, ClassDefLayout.SparseRangesOffset +
                            (ulong)i * ClassDefLayout.RangeStride + ClassDefLayout.RangeClassOffset, i),
                        out diagnostic, range.ClassValue, maxClassValue);
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus Inspect(int numGlyphs, out int rangeCount, out int consumed,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        rangeCount = consumed = 0;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (Format == 1)
        {
            status = TryGetDenseHeader(out var header, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if ((uint)header.StartGlyph + header.GlyphCount > numGlyphs && header.GlyphCount != 0)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.Count, ClassDefLayout.DenseCountOffset),
                    out diagnostic, (uint)header.StartGlyph + header.GlyphCount, (ulong)numGlyphs);
            consumed = ClassDefLayout.DenseValuesOffset + ClassDefLayout.ValueStride * header.GlyphCount;
            if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            ushort previous = 0;
            for (int i = 0; i < header.GlyphCount; i++)
            {
                status = TryGetClassValue(i, out ushort value, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (i == 0 || value != previous) rangeCount++;
                previous = value;
            }
        }
        else
        {
            status = TryGetSparseHeader(out var header, out diagnostic);
            if (status != ReadStatus.Done) return status;
            rangeCount = header.RangeCount;
            consumed = ClassDefLayout.SparseRangesOffset + ClassDefLayout.RangeStride * rangeCount;
            if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            int previous = -1;
            for (int i = 0; i < rangeCount; i++)
            {
                status = TryGetRange(i, out var range, out diagnostic);
                if (status != ReadStatus.Done) return status;
                ulong row = ClassDefLayout.SparseRangesOffset + (ulong)i * ClassDefLayout.RangeStride;
                if (range.EndGlyph < range.StartGlyph)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.GlyphId, row + ClassDefLayout.RangeEndOffset, i),
                        out diagnostic, range.EndGlyph, range.StartGlyph);
                if (range.StartGlyph <= previous)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.GlyphId, row, i), out diagnostic,
                        range.StartGlyph, unchecked((ulong)(long)previous));
                if (range.EndGlyph >= numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.GlyphId, row + ClassDefLayout.RangeEndOffset, i),
                        out diagnostic, range.EndGlyph, (ulong)numGlyphs);
                previous = range.EndGlyph;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus RequireFormat(ushort format, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (Format != format)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Version, 0), out diagnostic, Format, format);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus InvalidOrdinal(int ordinal, int count, ulong offset, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            Location.AtLocal(DataField.Count, offset, ordinal), out diagnostic,
            unchecked((ulong)(long)ordinal), (ulong)count);
}
