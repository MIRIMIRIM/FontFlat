using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct SingleSubstView
{
    public ReadStatus TryGetDelta(out short deltaGlyphId, out FontDiagnostic diagnostic)
    {
        deltaGlyphId = 0;
        ReadStatus status = RequireFormat(1, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadDeltaRecordAt(SingleSubstLayout.BodyOffset, -1, out var record, out diagnostic);
        if (status == ReadStatus.Done) deltaGlyphId = record.DeltaGlyphId;
        return status;
    }

    public ReadStatus TryGetSubstituteCount(out ushort count, out FontDiagnostic diagnostic)
    {
        count = 0;
        ReadStatus status = RequireFormat(2, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadSubstituteHeaderAt(SingleSubstLayout.BodyOffset, -1, out var record, out diagnostic);
        if (status == ReadStatus.Done) count = record.GlyphCount;
        return status;
    }

    public ReadStatus TryGetSubstitute(int ordinal, out ushort glyphId, out FontDiagnostic diagnostic)
    {
        glyphId = 0;
        ReadStatus status = TryGetSubstituteCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (ordinal < 0 || ordinal >= count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, SingleSubstLayout.BodyOffset, ordinal),
                out diagnostic, unchecked((ulong)(long)ordinal), count);
        status = TryReadSubstituteRecordAt(SingleSubstLayout.HeaderLength +
            (ulong)ordinal * SingleSubstLayout.SubstituteStride, ordinal, out var record, out diagnostic);
        if (status == ReadStatus.Done) glyphId = record.GlyphId;
        return status;
    }

    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Inspect(numGlyphs, out _, out _, out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out SingleSubstTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out SingleSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = Inspect(numGlyphs, out var coverage, out int count, out int consumed,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(count, Location, out diagnostic) ||
            !budget.Buffer(2L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = coverage.TryRead(numGlyphs, out var coverageModel, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var substitutes = new ushort[count];
        if (Format == 1)
        {
            _ = TryGetDelta(out short delta, out _);
            for (int i = 0; i < count; i++) substitutes[i] = unchecked((ushort)(coverageModel!.Glyphs[i] + delta));
        }
        else
        {
            for (int i = 0; i < count; i++) _ = TryGetSubstitute(i, out substitutes[i], out _);
        }
        model = new(coverageModel!, substitutes, Format, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus Inspect(int numGlyphs, out CoverageView coverage, out int count, out int consumed,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        coverage = default;
        count = consumed = 0;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int bodyEnd = SingleSubstLayout.HeaderLength;
        short delta = 0;
        if (Format == 1)
        {
            status = TryGetDelta(out delta, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        else
        {
            status = TryGetSubstituteCount(out ushort substituteCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            count = substituteCount;
            bodyEnd += SingleSubstLayout.SubstituteStride * count;
        }
        if (!budget.Work(bodyEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (Format == 2)
        {
            for (int i = 0; i < count; i++)
            {
                status = TryGetSubstitute(i, out ushort gid, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (gid >= numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.GlyphId, SingleSubstLayout.HeaderLength +
                            (ulong)i * SingleSubstLayout.SubstituteStride, i),
                        out diagnostic, gid, (ulong)numGlyphs);
            }
        }
        if (CoverageOffset == 0 || CoverageOffset >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, SingleSubstLayout.CoverageOffsetField),
                out diagnostic, CoverageOffset, (ulong)_data.Length);
        if (CoverageOffset < bodyEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, SingleSubstLayout.CoverageOffsetField),
                out diagnostic, CoverageOffset, (ulong)bodyEnd);
        status = CoverageView.TryOpen(_data.Slice(CoverageOffset), out coverage, out diagnostic,
            Location.AtLocal(DataField.None, CoverageOffset));
        if (status != ReadStatus.Done) return status;
        status = coverage.Inspect(numGlyphs, out int coverageCount, out int coverageLength, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (Format == 2 && coverageCount != count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, SingleSubstLayout.BodyOffset),
                out diagnostic, (ulong)count, (ulong)coverageCount);
        count = coverageCount;
        if (Format == 1)
        {
            if (!budget.Work(count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            for (int i = 0; i < coverage.Count; i++)
            {
                if (coverage.Format == 1)
                {
                    _ = coverage.TryGetGlyph(i, out ushort gid, out _);
                    ushort replacement = unchecked((ushort)(gid + delta));
                    if (replacement >= numGlyphs) return InvalidDeltaResult(replacement, numGlyphs, out diagnostic);
                }
                else
                {
                    _ = coverage.TryGetRange(i, out var range, out _);
                    for (int gid = range.StartGlyph; gid <= range.EndGlyph; gid++)
                    {
                        ushort replacement = unchecked((ushort)(gid + delta));
                        if (replacement >= numGlyphs) return InvalidDeltaResult(replacement, numGlyphs, out diagnostic);
                    }
                }
            }
        }
        consumed = Math.Max(bodyEnd, CoverageOffset + coverageLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus InvalidDeltaResult(ushort replacement, int numGlyphs, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            Location.AtLocal(DataField.Payload, SingleSubstLayout.BodyOffset),
            out diagnostic, replacement, (ulong)numGlyphs);

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
}
