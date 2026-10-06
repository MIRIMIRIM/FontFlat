using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct AlternateSubstView
{
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Inspect(numGlyphs, out _, out _, out _, out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out AlternateSubstTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out AlternateSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
        => ReadOwned(numGlyphs, out model, out diagnostic, budget, null, 0, default);

    internal ReadStatus TryRead(int numGlyphs, out AlternateSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, Dictionary<int, CoverageTable>? coverageCache,
        int subtableStart, DataLocation lookupLocation)
        => ReadOwned(numGlyphs, out model, out diagnostic, budget, coverageCache, subtableStart, lookupLocation);

    private ReadStatus ReadOwned(int numGlyphs, out AlternateSubstTable? model,
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
                    Location.AtLocal(DataField.Offset, AlternateSubstLayout.CoverageOffsetField), out diagnostic,
                    unchecked((ulong)(long)subtableStart), (ulong)(int.MaxValue - _data.Length));
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
        ReadStatus status = Inspect(numGlyphs, out var coverage, out int setCount,
            out int coverageCount, out int coverageLength, out diagnostic, budget,
            coverageModel, coverageOrigin);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(setCount, Location, out diagnostic) ||
            !budget.Buffer(8L * setCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (coverageModel is null)
        {
            status = coverage.Materialize(numGlyphs, coverageCount, coverageLength,
                out coverageModel, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (coverageCache is not null)
            {
                if (!budget.Buffer(16, coverageModel!.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                coverageCache.Add(coverageTarget, coverageModel!);
            }
        }
        var alternateSets = new ushort[setCount][];
        int consumed = Math.Max(AlternateSubstLayout.MinimumHeader +
            AlternateSubstLayout.OffsetStride * setCount, CoverageOffset + coverageLength);
        for (int i = 0; i < setCount; i++)
        {
            status = TryOpenAlternateSet(i, out var child, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = child.TryRead(numGlyphs, out alternateSets[i], out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            status = TryGetAlternateSetOffsetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            consumed = Math.Max(consumed, record.AlternateSetOffset + alternateSets[i].Length * 2 + 2);
        }
        model = new(coverageModel!, alternateSets, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus Inspect(int numGlyphs, out CoverageView coverage, out int setCount,
        out int coverageCount, out int coverageLength, out FontDiagnostic diagnostic, BuildBudget budget,
        CoverageTable? cachedCoverage = null, DataLocation? coverageOrigin = null)
    {
        coverage = default;
        setCount = coverageCount = coverageLength = 0;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetAlternateSetCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        setCount = count;
        int bodyEnd = AlternateSubstLayout.MinimumHeader + AlternateSubstLayout.OffsetStride * count;
        if (!budget.Work(bodyEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryGetAlternateSetOffsetRecordExtent(out _, out _, out _, out diagnostic);
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
        if (setCount != coverageCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, AlternateSubstLayout.CountAt),
                out diagnostic, (ulong)setCount, (ulong)coverageCount);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
