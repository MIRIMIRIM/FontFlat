using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct LigatureView
{
    // The component array count is the declarative rawCount-1 (CountSubtract): the
    // generated extent/count accessors refuse componentCount=0 with the GC7 underflow
    // row (InvalidFormat/InvalidValue at the raw count field, Detail=0/Expected=1 -
    // W6) before any element is touched, and componentCount=1 is the legal
    // zero-component ligature (OQ-T4-03-2). No hand-written interception exists.
    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryGetComponentRecordExtent(out _, out _, out ulong end, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work((long)end, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (LigatureGlyph >= numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, LigatureLayout.LigatureGlyphAt),
                out diagnostic, LigatureGlyph, (ulong)numGlyphs);
        status = TryGetComponentRecordCount(out ulong componentCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int i = 0; i < (int)componentCount; i++)
        {
            status = TryGetComponentRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (record.ComponentGlyphId >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId,
                        LigatureLayout.HeaderLength + (ulong)i * LigatureLayout.ComponentStride, i),
                    out diagnostic, record.ComponentGlyphId, (ulong)numGlyphs);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus TryRead(int numGlyphs, out LigatureTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = Validate(numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        _ = TryGetComponentRecordExtent(out _, out _, out ulong end, out _);
        _ = TryGetComponentRecordCount(out ulong componentCount, out _);
        if (!budget.Buffer((long)componentCount * 2L, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var components = new ushort[(int)componentCount];
        for (int i = 0; i < (int)componentCount; i++)
        {
            _ = TryGetComponentRecord(i, out var record, out _);
            components[i] = record.ComponentGlyphId;
        }
        model = new(LigatureGlyph, components, Location, (int)end);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
