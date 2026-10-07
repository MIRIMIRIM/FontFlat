using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// The reverse-chaining view's semantic partial (T4-04 section 3): the declared
// backtrack chain plus the hand-walked computed-start tail - lookaheadGlyphCount at
// 6+2*backtrack, its coverage offsets, then glyphCount and the substitutes (OQ-5's
// hand-walked boundary). The identity check is the SingleSubst format-2 shape: one
// substitute per coverage individual, every substitute inside the glyph domain.
public readonly ref partial struct ReverseChainSubstView
{
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Inspect(numGlyphs, out _, out diagnostic, budget, cache: null, 0, default);
    }

    public ReadStatus TryRead(int numGlyphs, out ReverseChainSubstTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ReadOwned(numGlyphs, out model, out diagnostic, budget, null, 0, default);
    }

    internal ReadStatus TryRead(int numGlyphs, out ReverseChainSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, Dictionary<int, CoverageTable>? coverageCache,
        int subtableStart, DataLocation lookupLocation) =>
        ReadOwned(numGlyphs, out model, out diagnostic, budget, coverageCache,
            subtableStart, lookupLocation);

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Inspect(numGlyphs, out _, out diagnostic, budget, cache: null, 0, default);

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic,
        BuildBudget budget, Dictionary<int, CoverageTable>? coverageCache,
        int subtableStart, DataLocation lookupLocation) =>
        Inspect(numGlyphs, out _, out diagnostic, budget, coverageCache, subtableStart, lookupLocation);

    private ReadStatus Inspect(int numGlyphs, out int consumed,
        out FontDiagnostic diagnostic, BuildBudget budget,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation)
    {
        consumed = 0;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenCoverage(out var coverage, out bool present, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!present)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, ReverseChainSubstLayout.CoverageOffsetField),
                out diagnostic, 0UL, 1UL);
        status = coverage!.Inspect(numGlyphs, out int coverageCount, out int coverageLength,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetBacktrackGlyphCount(out ushort backtrackCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int i = 0; i < backtrackCount; i++)
        {
            status = OpenBacktrackCoverage(i, numGlyphs, cache, subtableStart, lookupLocation,
                budget, validateOnly: true, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        int cursor = ReverseChainSubstLayout.BacktrackArrayAt + 2 * backtrackCount;
        status = ReadCountAt(cursor, out int lookaheadCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2;
        status = WalkChain(cursor, lookaheadCount, numGlyphs, cache, subtableStart,
            lookupLocation, budget, validateOnly: true, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2 * lookaheadCount;
        status = ReadCountAt(cursor, out int glyphCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2;
        consumed = (int)(cursor + 2L * glyphCount);
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (cursor + 2L * glyphCount > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.GlyphId, (ulong)cursor), out diagnostic,
                (ulong)(cursor + 2L * glyphCount), (ulong)_data.Length);
        // The substitutes/coverage identity (SingleSubst format-2 shape) and the
        // substitute glyph domain.
        if (glyphCount != coverageCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, (ulong)(cursor - 2)), out diagnostic,
                (ulong)glyphCount, (ulong)coverageCount);
        for (int i = 0; i < glyphCount; i++)
        {
            ushort substitute = ReadWire.U16(_data, cursor + 2 * i);
            if (substitute >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId, (ulong)(cursor + 2 * i), i), out diagnostic,
                    substitute, (ulong)numGlyphs);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadCountAt(int at, out int count, out FontDiagnostic diagnostic)
    {
        count = 0;
        if (at < 0 || at + 2 > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Count, (ulong)at), out diagnostic,
                (ulong)at, (ulong)_data.Length);
        count = ReadWire.U16(_data, at);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus OpenBacktrackCoverage(int ordinal, int numGlyphs,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation,
        BuildBudget budget, bool validateOnly, out CoverageTable? model, out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = TryGetBacktrackCoverageOffsetRecord(ordinal, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (record.CoverageOffset >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset,
                    ReverseChainSubstLayout.BacktrackArrayAt +
                    (ulong)ordinal * ReverseChainSubstLayout.OffsetStride, ordinal),
                out diagnostic, record.CoverageOffset, (ulong)_data.Length);
        int target = ReverseChainSubstLayout.BacktrackArrayAt + record.CoverageOffset;
        if (cache is not null && cache.TryGetValue(subtableStart + target, out var cached))
        {
            model = cached;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = CoverageView.TryOpen(_data.Slice(record.CoverageOffset), out var coverage,
            out diagnostic, Location);
        if (status != ReadStatus.Done) return status;
        if (validateOnly)
        {
            status = coverage!.Validate(numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = coverage!.TryRead(numGlyphs, out model, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (cache is not null && subtableStart >= 0)
        {
            if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            cache.TryAdd(subtableStart + target, model!);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One hand-walked coverage-offset chain (the lookahead chain's form).
    private ReadStatus WalkChain(int offsetsAt, int count, int numGlyphs,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation,
        BuildBudget budget, bool validateOnly, out CoverageTable[]? models, out FontDiagnostic diagnostic)
    {
        models = null;
        if (offsetsAt + 2L * count > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, (ulong)offsetsAt), out diagnostic,
                (ulong)(offsetsAt + 2L * count), (ulong)_data.Length);
        if (!budget.Work(count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        CoverageTable[]? result = validateOnly ? null : new CoverageTable[count];
        for (int i = 0; i < count; i++)
        {
            // Coverage offsets address from the subtable start, not the offsets row.
            int offset = ReadWire.U16(_data, offsetsAt + 2 * i);
            int target = offset;
            if (offset == 0 || target >= _data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.AtLocal(DataField.Offset, (ulong)(offsetsAt + 2 * i), i), out diagnostic,
                    (ulong)offset, (ulong)_data.Length);
            if (cache is not null && cache.TryGetValue(subtableStart + target, out var cached))
            {
                if (result is not null) result[i] = cached;
                continue;
            }
            ReadStatus status = CoverageView.TryOpen(_data.Slice(offset), out var coverage,
                out diagnostic, Location);
            if (status != ReadStatus.Done) return status;
            if (validateOnly)
            {
                status = coverage!.Validate(numGlyphs, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                continue;
            }
            status = coverage!.TryRead(numGlyphs, out var model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (cache is not null && subtableStart >= 0)
            {
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                cache.TryAdd(subtableStart + target, model!);
            }
            if (result is not null) result[i] = model!;
        }
        models = result;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadOwned(int numGlyphs, out ReverseChainSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation)
    {
        model = null;
        ReadStatus status = Inspect(numGlyphs, out int consumed, out diagnostic, budget,
            cache, subtableStart, lookupLocation);
        if (status != ReadStatus.Done) return status;
        // Reopen the declared surfaces for materialization (the Inspect facts stay
        // valid; spec 6.2 keeps the re-reads from re-charging semantics).
        status = TryOpenCoverage(out var coverage, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = coverage!.TryRead(numGlyphs, out var coverageModel, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetBacktrackGlyphCount(out ushort backtrackCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var backtrackCoverages = new CoverageTable[backtrackCount];
        for (int i = 0; i < backtrackCount; i++)
        {
            status = OpenBacktrackCoverage(i, numGlyphs, cache, subtableStart, lookupLocation,
                budget, validateOnly: false, out var bt, out diagnostic);
            if (status != ReadStatus.Done) return status;
            backtrackCoverages[i] = bt!;
        }
        int cursor = ReverseChainSubstLayout.BacktrackArrayAt + 2 * backtrackCount;
        status = ReadCountAt(cursor, out int lookaheadCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2;
        status = WalkChain(cursor, lookaheadCount, numGlyphs, cache, subtableStart,
            lookupLocation, budget, validateOnly: false, out var lookaheadCoverages, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2 * lookaheadCount;
        status = ReadCountAt(cursor, out int glyphCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2;
        var substitutes = new ushort[glyphCount];
        for (int i = 0; i < glyphCount; i++)
            substitutes[i] = ReadWire.U16(_data, cursor + 2 * i);
        if (!budget.Buffer(16 + 2L * glyphCount + 8L * backtrackCount + 8L * lookaheadCount,
            Location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(coverageModel!, backtrackCoverages,
            (CoverageTable[])lookaheadCoverages!, substitutes, numGlyphs, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
