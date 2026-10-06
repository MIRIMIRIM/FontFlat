using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct AlternateSetView
{
    // Proves the alternate GID domain. Order is arbitrary by spec, so unlike attach
    // points no ordering relation is checked; the empty set is the legal no-op
    // (OQ-T4-03-2).
    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryGetAlternateRecordExtent(out _, out _, out ulong end, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work((long)end, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < GlyphCount; i++)
        {
            status = TryGetAlternateRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (record.AlternateGlyphId >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId,
                        AlternateSetLayout.HeaderLength + (ulong)i * AlternateSetLayout.AlternateStride, i),
                    out diagnostic, record.AlternateGlyphId, (ulong)numGlyphs);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus TryRead(int numGlyphs, out ushort[] alternates,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        alternates = [];
        ReadStatus status = Validate(numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int count = GlyphCount;
        if (!budget.Buffer(2L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        alternates = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            _ = TryGetAlternateRecord(i, out var record, out _);
            alternates[i] = record.AlternateGlyphId;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
