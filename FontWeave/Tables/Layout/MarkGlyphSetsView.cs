using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct MarkGlyphSetsView
{
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        ReadSets(numGlyphs, false, out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out MarkGlyphSetsTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out MarkGlyphSetsTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        ReadSets(numGlyphs, true, out model, out diagnostic, budget);

    // All wire fields are read by the sole generated View. This combines parent offsets,
    // leaf semantics and a cache scoped to this span, glyph context and Coverage kind.
    private ReadStatus ReadSets(int numGlyphs, bool materialize, out MarkGlyphSetsTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int prefix = MarkGlyphSetsLayout.HeaderLength + MarkGlyphSetsLayout.OffsetStride * SetCount;
        if (!budget.Work(prefix, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (_data.Length < prefix)
        {
            int firstIncomplete = (_data.Length - MarkGlyphSetsLayout.HeaderLength) / MarkGlyphSetsLayout.OffsetStride;
            status = TryGetCoverageOffsetRecord(firstIncomplete, out _, out diagnostic);
            // Preserve the mechanical row coordinate and classify the sole field.
            if (status == ReadStatus.Truncated)
                diagnostic = diagnostic with { Location = diagnostic.Location with { Field = DataField.Offset } };
            return status;
        }
        if (materialize && !budget.Buffer(12L * SetCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        CoverageTable[] sets = materialize && SetCount != 0 ? new CoverageTable[SetCount] : [];
        uint[] offsets = materialize && SetCount != 0 ? new uint[SetCount] : [];
        var lengths = new Dictionary<uint, int>();
        var models = materialize ? new Dictionary<uint, CoverageTable>() : null;
        int consumed = prefix;
        for (int i = 0; i < SetCount; i++)
        {
            status = TryGetCoverageOffsetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            uint offset = record.CoverageOffset;
            CoverageTable? cached = null;
            int length = 0;
            bool seen = materialize ? models!.TryGetValue(offset, out cached) : lengths.TryGetValue(offset, out length);
            if (seen && materialize) length = cached!.ConsumedLength;
            if (!seen)
            {
                // The generated opener owns the whole declared envelope: zero policy,
                // Minimum=SetsEnd storage floor, source bounds and the child TryOpen with
                // the location translated exactly once. It re-reads the same 4-byte offset
                // through the record accessor, unbilled and observationally identical.
                status = TryOpenCoverageSet(i, out var child, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (materialize)
                {
                    status = child.TryRead(numGlyphs, out var coverage, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    CoverageTable owned = coverage!;
                    length = owned.ConsumedLength;
                    if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    models!.Add(offset, owned);
                }
                else
                {
                    status = child.Inspect(numGlyphs, out _, out length, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(8, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                }
                if (!materialize) lengths.Add(offset, length);
            }
            consumed = Math.Max(consumed, (int)offset + length);
            if (materialize)
            {
                sets[i] = models![offset];
                offsets[i] = offset;
            }
        }
        if (materialize) model = new(sets, numGlyphs, Location, consumed, offsets);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
