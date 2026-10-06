using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct MultipleSubstView
{
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Inspect(numGlyphs, out _, out _, out _, out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out MultipleSubstTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out MultipleSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
        => ReadOwned(numGlyphs, out model, out diagnostic, budget, null, 0, default);

    // Cache scope is one Lookup span/source/glyph context; keys are physical
    // Lookup-local positions, never diagnostic coordinates (SingleSubstView precedent).
    internal ReadStatus TryRead(int numGlyphs, out MultipleSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, Dictionary<int, CoverageTable>? coverageCache,
        int subtableStart, DataLocation lookupLocation)
        => ReadOwned(numGlyphs, out model, out diagnostic, budget, coverageCache, subtableStart, lookupLocation);

    private ReadStatus ReadOwned(int numGlyphs, out MultipleSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, Dictionary<int, CoverageTable>? coverageCache,
        int subtableStart, DataLocation lookupLocation)
    {
        model = null;
        int coverageTarget = 0;
        CoverageTable? coverageModel = null;
        DataLocation? coverageOrigin = null;
        if (coverageCache is not null)
        {
            ReadStatus offsetStatus = TryGetCoverageOffset(out ushort offset, out diagnostic);
            if (offsetStatus != ReadStatus.Done) return offsetStatus;
            if (subtableStart < 0 || subtableStart > int.MaxValue - _data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.AtLocal(DataField.Offset, MultipleSubstLayout.CoverageOffsetField), out diagnostic,
                    unchecked((ulong)(long)subtableStart), (ulong)(int.MaxValue - _data.Length));
            // Invalid offsets retain Inspect's normal field/format diagnostic precedence.
            if (offset < _data.Length)
            {
                coverageTarget = subtableStart + offset;
                coverageOrigin = lookupLocation.AtLocal(DataField.None, (ulong)coverageTarget);
                if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                coverageCache.TryGetValue(coverageTarget, out coverageModel);
                if (coverageModel is not null &&
                    (coverageModel.NumGlyphs != numGlyphs || coverageModel.Location != coverageOrigin.Value))
                    return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                        coverageOrigin.Value, out diagnostic);
            }
        }
        ReadStatus status = Inspect(numGlyphs, out var coverage, out int sequenceCount,
            out int coverageCount, out int coverageLength, out diagnostic, budget,
            coverageModel, coverageOrigin);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(sequenceCount, Location, out diagnostic) ||
            !budget.Buffer(8L * sequenceCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (coverageModel is null)
        {
            // Expansion consumes the facts Inspect just proved (spec 6.2).
            status = coverage.Materialize(numGlyphs, coverageCount, coverageLength,
                out coverageModel, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (coverageCache is not null)
            {
                if (!budget.Buffer(16, coverageModel!.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                coverageCache.Add(coverageTarget, coverageModel!);
            }
        }
        var sequences = new ushort[sequenceCount][];
        int consumed = Math.Max(MultipleSubstLayout.MinimumHeader +
            MultipleSubstLayout.OffsetStride * sequenceCount, CoverageOffset + coverageLength);
        for (int i = 0; i < sequenceCount; i++)
        {
            status = TryOpenSequence(i, out var child, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = child.TryRead(numGlyphs, out sequences[i], out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            status = TryGetSequenceOffsetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            consumed = Math.Max(consumed, record.SequenceOffset + sequences[i].Length * 2 + 2);
        }
        model = new(coverageModel!, sequences, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Proves version, glyph context, the offset-record extent, the Coverage facts and
    // the index i ↔ sequence i correspondence (W10's negative direction); the per
    // -sequence GID domains belong to the child walk. Mirrors SingleSubstView.Inspect.
    private ReadStatus Inspect(int numGlyphs, out CoverageView coverage, out int sequenceCount,
        out int coverageCount, out int coverageLength, out FontDiagnostic diagnostic, BuildBudget budget,
        CoverageTable? cachedCoverage = null, DataLocation? coverageOrigin = null)
    {
        coverage = default;
        sequenceCount = coverageCount = coverageLength = 0;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetSequenceCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        sequenceCount = count;
        int bodyEnd = MultipleSubstLayout.MinimumHeader + MultipleSubstLayout.OffsetStride * count;
        if (!budget.Work(bodyEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryGetSequenceOffsetRecordExtent(out _, out _, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (cachedCoverage is null)
        {
            status = TryOpenCoverage(out coverage, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = coverage.Inspect(numGlyphs, out coverageCount, out coverageLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        else
        {
            coverageCount = cachedCoverage.Glyphs.Length;
            coverageLength = cachedCoverage.ConsumedLength;
        }
        if (sequenceCount != coverageCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, MultipleSubstLayout.CountAt),
                out diagnostic, (ulong)sequenceCount, (ulong)coverageCount);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
