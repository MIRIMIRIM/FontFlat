using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct LigatureSetView
{
    // Proves the offset-record extent and walks every ligature's declarative
    // component rule; the ligature offset order is preference order, so no ordering
    // relation is declared (the owned model retains the wire order).
    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetLigatureCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int bodyEnd = LigatureSetLayout.HeaderLength + LigatureSetLayout.OffsetStride * count;
        if (!budget.Work(bodyEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryGetLigatureOffsetRecordExtent(out _, out _, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int i = 0; i < count; i++)
        {
            status = TryOpenLigature(i, out var child, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = child.Validate(numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus TryRead(int numGlyphs, out LigatureSetTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = Validate(numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        _ = TryGetLigatureCount(out ushort count, out _);
        if (!budget.Buffer(8L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var ligatures = new LigatureTable[count];
        int consumed = LigatureSetLayout.HeaderLength + LigatureSetLayout.OffsetStride * count;
        for (int i = 0; i < count; i++)
        {
            status = TryOpenLigature(i, out var child, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = child.TryRead(numGlyphs, out var ligature, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            status = TryGetLigatureOffsetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ligatures[i] = ligature!;
            consumed = Math.Max(consumed, record.LigatureOffset + ligature!.ConsumedLength);
        }
        model = new(ligatures, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
