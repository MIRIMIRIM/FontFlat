using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct CoverageView
{
    /// <summary>Local format-1 access; does not assert sorting or enclosing glyph validity.</summary>
    public ReadStatus TryGetGlyph(int ordinal, out ushort glyphId, out FontDiagnostic diagnostic)
    {
        glyphId = 0;
        ReadStatus status = TryGetGlyphRecord(ordinal, out var record, out diagnostic);
        if (status == ReadStatus.Done) glyphId = record.GlyphId;
        return status;
    }

    /// <summary>Local format-2 access; coverage-index and range semantics are checked by Validate.</summary>
    public ReadStatus TryGetRange(int ordinal, out RangeRecordData record, out FontDiagnostic diagnostic) =>
        TryGetRangeRecord(ordinal, out record, out diagnostic);

    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Inspect(numGlyphs, out _, out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out CoverageTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out CoverageTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = Inspect(numGlyphs, out int expandedCount, out int consumed,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        return Materialize(numGlyphs, expandedCount, consumed, out model, out diagnostic, budget);
    }

    // Expansion over already inspected facts (spec 6.2): Inspect owns version, count,
    // format and every per-row semantic check, so this step re-walks the same verified
    // rows only to copy glyph ids and must not re-inspect the same semantics.
    internal ReadStatus Materialize(int numGlyphs, int expandedCount, int consumed,
        out CoverageTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Work(2L * expandedCount, Location, out diagnostic) ||
            !budget.Buffer(2L * expandedCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ushort[] glyphs = new ushort[expandedCount];
        int at = 0, rangeCount = 0;
        for (int i = 0; i < Count; i++)
        {
            if (Format == 1)
            {
                _ = TryGetGlyph(i, out ushort gid, out _);
                if (at == 0 || gid != glyphs[at - 1] + 1) rangeCount++;
                glyphs[at++] = gid;
            }
            else
            {
                _ = TryGetRange(i, out var range, out _);
                if (at == 0 || range.StartGlyph != glyphs[at - 1] + 1) rangeCount++;
                for (int gid = range.StartGlyph; gid <= range.EndGlyph; gid++) glyphs[at++] = (ushort)gid;
            }
        }
        model = new(glyphs, numGlyphs, Format, Location, consumed, rangeCount);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // No length is encoded in Coverage. A valid leaf may be followed by other parent payloads.
    internal ReadStatus Inspect(int numGlyphs, out int expandedCount, out int consumed,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        expandedCount = consumed = 0;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int stride = Format == 1 ? CoverageLayout.GlyphStride : CoverageLayout.RangeStride;
        // The active format's extent owns the consumed arithmetic; the extent getter
        // deliberately does not prove the trailing rows lie inside the source.
        status = Format == 1
            ? TryGetGlyphRecordExtent(out _, out _, out ulong end, out diagnostic)
            : TryGetRangeRecordExtent(out _, out _, out end, out diagnostic);
        if (status != ReadStatus.Done) return status;
        consumed = (int)end;
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ulong start = CoverageLayout.HeaderLength;
        int previous = -1;
        for (int i = 0; i < Count; i++)
        {
            // Row provenance for semantic refusals; the record access itself is the
            // generated accessor's wide-arithmetic start + ordinal * stride.
            ulong row = start + (ulong)i * (uint)stride;
            if (Format == 1)
            {
                status = TryGetGlyph(i, out ushort gid, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (gid >= numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.GlyphId, row, i), out diagnostic, gid, (ulong)numGlyphs);
                if (gid <= previous)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.GlyphId, row, i), out diagnostic, gid, unchecked((ulong)(long)previous));
                previous = gid;
                expandedCount++;
            }
            else
            {
                status = TryGetRange(i, out var range, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (range.EndGlyph < range.StartGlyph)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.GlyphId, row + CoverageLayout.RangeEndOffset, i),
                        out diagnostic, range.EndGlyph, range.StartGlyph);
                if (range.StartGlyph <= previous)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.GlyphId, row, i), out diagnostic,
                        range.StartGlyph, unchecked((ulong)(long)previous));
                if (range.EndGlyph >= numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.GlyphId, row + CoverageLayout.RangeEndOffset, i),
                        out diagnostic, range.EndGlyph, (ulong)numGlyphs);
                if (range.StartCoverageIndex != expandedCount)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Payload, row + CoverageLayout.RangeIndexOffset, i),
                        out diagnostic, range.StartCoverageIndex, (ulong)expandedCount);
                expandedCount += range.EndGlyph - range.StartGlyph + 1;
                previous = range.EndGlyph;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
