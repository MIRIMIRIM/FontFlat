using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct SequenceView
{
    // Proves the substitution GID domain. The empty sequence is the legal deletion
    // no-op (OQ-T4-03-2, W9) and needs no special case: zero iterations validate it.
    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryGetSubstituteRecordExtent(out _, out _, out ulong end, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work((long)end, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < GlyphCount; i++)
        {
            status = TryGetSubstituteRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (record.SubstituteGlyphId >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId,
                        SequenceLayout.HeaderLength + (ulong)i * SequenceLayout.SubstituteStride, i),
                    out diagnostic, record.SubstituteGlyphId, (ulong)numGlyphs);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Expansion over the facts Validate proved (spec 6.2): the re-read rows are proven,
    // so no second Work admission is paid for them.
    internal ReadStatus TryRead(int numGlyphs, out ushort[] substitutes,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        substitutes = [];
        ReadStatus status = Validate(numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int count = GlyphCount;
        if (!budget.Buffer(2L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        substitutes = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            _ = TryGetSubstituteRecord(i, out var record, out _);
            substitutes[i] = record.SubstituteGlyphId;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
