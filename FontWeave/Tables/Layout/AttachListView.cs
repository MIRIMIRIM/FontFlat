using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct AttachListView
{
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        ReadChain(numGlyphs, false, out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out AttachListTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out AttachListTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        ReadChain(numGlyphs, true, out model, out diagnostic, budget);

    // All wire fields are read by the sole generated View. The Coverage index order is
    // the declared glyph order; identical AttachPoint offsets legally share one body,
    // so reading dedupes by source offset exactly like the MarkGlyphSets Coverage cache.
    private ReadStatus ReadChain(int numGlyphs, bool materialize, out AttachListTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int prefix = AttachListLayout.HeaderLength + AttachListLayout.PointStride * GlyphCount;
        if (!budget.Work(prefix, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (_data.Length < prefix)
        {
            int firstIncomplete = (_data.Length - AttachListLayout.HeaderLength) / AttachListLayout.PointStride;
            status = TryGetAttachPointOffsetRecord(firstIncomplete, out _, out diagnostic);
            // Preserve the mechanical row coordinate and classify the sole field.
            if (status == ReadStatus.Truncated)
                diagnostic = diagnostic with { Location = diagnostic.Location with { Field = DataField.Offset } };
            return status;
        }
        status = TryGetCoverageOffset(out ushort coverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The declared opener owns the zero policy, the PointsEnd floor, the source
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
        // Relation check: one AttachPoint offset per covered glyph.
        if (GlyphCount != coverageView.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, AttachListLayout.GlyphCountAt), out diagnostic,
                (ulong)GlyphCount, (ulong)coverageView.Count);
        if (materialize && !budget.Buffer(12L * GlyphCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ushort[][] points = materialize && GlyphCount != 0 ? new ushort[GlyphCount][] : [];
        uint[] offsets = materialize && GlyphCount != 0 ? new uint[GlyphCount] : [];
        var lengths = new Dictionary<uint, int>();
        var models = materialize ? new Dictionary<uint, ushort[]>() : null;
        int consumed = Math.Max(prefix, coverageOffset + coverageLength);
        for (int i = 0; i < GlyphCount; i++)
        {
            status = TryGetAttachPointOffsetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            uint offset = record.AttachPointOffset;
            ushort[]? cached = null;
            int length = 0;
            bool seen = materialize ? models!.TryGetValue(offset, out cached) : lengths.TryGetValue(offset, out length);
            if (seen && materialize) length = ByteLength(cached!);
            if (!seen)
            {
                status = TryOpenAttachPoint(i, out var pointView, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (materialize)
                {
                    status = pointView.TryRead(out cached, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    length = ByteLength(cached!);
                    if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    models!.Add(offset, cached!);
                }
                else
                {
                    status = pointView.Inspect(out length, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(8, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                }
                if (!materialize) lengths.Add(offset, length);
            }
            consumed = Math.Max(consumed, (int)offset + length);
            if (materialize)
            {
                points[i] = models![offset];
                offsets[i] = offset;
            }
        }
        if (materialize) model = new(coverage!, points, numGlyphs, Location, consumed, offsets);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static int ByteLength(ushort[] points) => AttachPointLayout.HeaderLength +
        AttachPointLayout.PointStride * points.Length;
}
