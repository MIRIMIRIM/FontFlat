using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct LigCaretListView
{
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        ReadChain(numGlyphs, false, out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out LigCaretListTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out LigCaretListTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        ReadChain(numGlyphs, true, out model, out diagnostic, budget);

    // All wire fields are read by the sole generated View. An empty list (zero count
    // over an empty format-1 Coverage) is legal and stays legal here (OQ-T4-02-6);
    // shared LigGlyph bodies dedupe by source offset like the MarkGlyphSets cache.
    private ReadStatus ReadChain(int numGlyphs, bool materialize, out LigCaretListTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int prefix = LigCaretListLayout.HeaderLength + LigCaretListLayout.OffsetStride * LigGlyphCount;
        if (!budget.Work(prefix, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (_data.Length < prefix)
        {
            int firstIncomplete = (_data.Length - LigCaretListLayout.HeaderLength) / LigCaretListLayout.OffsetStride;
            status = TryGetLigGlyphOffsetRecord(firstIncomplete, out _, out diagnostic);
            // Preserve the mechanical row coordinate and classify the sole field.
            if (status == ReadStatus.Truncated)
                diagnostic = diagnostic with { Location = diagnostic.Location with { Field = DataField.Offset } };
            return status;
        }
        status = TryGetCoverageOffset(out ushort coverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The declared opener owns the zero policy, the LigGlyphsEnd floor, the source
        // bounds and the child TryOpen with the location translated exactly once.
        status = TryOpenCoverage(out var coverageView, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        CoverageTable? coverage = null;
        int coverageLength;
        if (materialize)
        {
            status = coverageView.TryRead(numGlyphs, out coverage, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            coverageLength = coverage!.ConsumedLength;
        }
        else
        {
            status = coverageView.Inspect(numGlyphs, out _, out coverageLength, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        // Relation check: one LigGlyph offset per covered ligated glyph.
        if (LigGlyphCount != coverageView.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, LigCaretListLayout.LigGlyphCountAt), out diagnostic,
                (ulong)LigGlyphCount, (ulong)coverageView.Count);
        if (materialize && !budget.Buffer(16L * LigGlyphCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var ligGlyphs = materialize && LigGlyphCount != 0 ? new LigGlyphTable[LigGlyphCount] : [];
        uint[] offsets = materialize && LigGlyphCount != 0 ? new uint[LigGlyphCount] : [];
        var lengths = new Dictionary<uint, int>();
        var models = materialize ? new Dictionary<uint, LigGlyphTable>() : null;
        int consumed = Math.Max(prefix, coverageOffset + coverageLength);
        for (int i = 0; i < LigGlyphCount; i++)
        {
            status = TryGetLigGlyphOffsetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            uint offset = record.LigGlyphOffset;
            LigGlyphTable? cached = null;
            int length = 0;
            bool seen = materialize ? models!.TryGetValue(offset, out cached) : lengths.TryGetValue(offset, out length);
            if (seen && materialize) length = cached!.ConsumedLength;
            if (!seen)
            {
                status = TryOpenLigGlyph(i, out var ligView, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (materialize)
                {
                    status = ligView.TryRead(out cached, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    length = cached!.ConsumedLength;
                    models!.Add(offset, cached!);
                }
                else
                {
                    status = ligView.Validate(out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(8, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    length = 0;
                }
                if (!materialize) lengths.Add(offset, length);
            }
            consumed = Math.Max(consumed, (int)offset + length);
            if (materialize)
            {
                ligGlyphs[i] = models![offset];
                offsets[i] = offset;
            }
        }
        if (materialize) model = new(coverage!, ligGlyphs, numGlyphs, Location, consumed, offsets);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
