using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One mark record (GPOS lookup types 4/5/6): the mark class and its
/// Required anchor - the spec marks no NULL note for the mark-record anchor, the
/// explicit contrast to the Base/Component/Mark2 rows (T4-06 section 2), so a zero
/// offset refuses at the generated opener and the model carries a non-null anchor.</summary>
public readonly record struct MarkRecord(ushort Class, AnchorTable Anchor);

/// <summary>A static GPOS type-4 fragment: one mark record per mark Coverage index,
/// one Optional-anchor row (classCount entries) per base Coverage index. Shared
/// anchor bodies materialize as one shared AnchorTable instance, so recompilation
/// reproduces the wire's body sharing (the cursive precedent).</summary>
public sealed class MarkBasePosTable
{
    private readonly MarkRecord[] _marks;
    private readonly AnchorTable?[][] _baseRows;

    internal MarkBasePosTable(CoverageTable markCoverage, MarkRecord[] marks,
        CoverageTable baseCoverage, ushort classCount, AnchorTable?[][] baseRows,
        int numGlyphs, DataLocation location, int consumedLength)
    {
        MarkCoverage = markCoverage; _marks = marks; BaseCoverage = baseCoverage;
        ClassCount = classCount; _baseRows = baseRows; NumGlyphs = numGlyphs;
        Location = location; ConsumedLength = consumedLength;
    }

    public CoverageTable MarkCoverage { get; }
    public int MarkCount => _marks.Length;
    public ReadOnlySpan<MarkRecord> Marks => _marks;
    public CoverageTable BaseCoverage { get; }
    public ushort ClassCount { get; }
    /// <summary>One Optional-anchor row per base Coverage index, in Coverage order.</summary>
    public int BaseCount => _baseRows.Length;
    public ReadOnlySpan<AnchorTable?> BaseRow(int baseOrdinal) => _baseRows[baseOrdinal];
    public int NumGlyphs { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One ligature attach body (GPOS lookup type 5): the component count plus
/// one Optional-anchor row per component. componentCount 0 is the lean legal empty
/// attach (OQ-T4-06-5: spec has no ban, zero real witnesses, the no-op row family).</summary>
public sealed class LigatureAttachTable
{
    private readonly AnchorTable?[][] _componentRows;

    internal LigatureAttachTable(ushort componentCount, AnchorTable?[][] componentRows,
        DataLocation location, int consumedLength)
    {
        ComponentCount = componentCount; _componentRows = componentRows;
        Location = location; ConsumedLength = consumedLength;
    }

    public ushort ComponentCount { get; }
    public int Components => _componentRows.Length;
    public ReadOnlySpan<AnchorTable?> ComponentRow(int componentOrdinal) => _componentRows[componentOrdinal];
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>A static GPOS type-5 fragment: the mark side plus the LigatureArray of
/// attach bodies, one per ligature Coverage index.</summary>
public sealed class MarkLigPosTable
{
    private readonly MarkRecord[] _marks;
    private readonly LigatureAttachTable[] _ligatures;

    internal MarkLigPosTable(CoverageTable markCoverage, MarkRecord[] marks,
        CoverageTable ligatureCoverage, LigatureAttachTable[] ligatures,
        int numGlyphs, DataLocation location, int consumedLength)
    {
        MarkCoverage = markCoverage; _marks = marks; LigatureCoverage = ligatureCoverage;
        _ligatures = ligatures; NumGlyphs = numGlyphs; Location = location;
        ConsumedLength = consumedLength;
    }

    public CoverageTable MarkCoverage { get; }
    public int MarkCount => _marks.Length;
    public ReadOnlySpan<MarkRecord> Marks => _marks;
    public CoverageTable LigatureCoverage { get; }
    /// <summary>One attach per ligature Coverage index, in Coverage order.</summary>
    public IReadOnlyList<LigatureAttachTable> Ligatures => _ligatures;
    public int NumGlyphs { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>A static GPOS type-6 fragment: the mark-1 side plus one Optional-anchor
/// row (classCount entries) per mark-2 Coverage index.</summary>
public sealed class MarkMarkPosTable
{
    private readonly MarkRecord[] _marks;
    private readonly AnchorTable?[][] _mark2Rows;

    internal MarkMarkPosTable(CoverageTable markCoverage, MarkRecord[] marks,
        CoverageTable mark2Coverage, ushort classCount, AnchorTable?[][] mark2Rows,
        int numGlyphs, DataLocation location, int consumedLength)
    {
        MarkCoverage = markCoverage; _marks = marks; Mark2Coverage = mark2Coverage;
        ClassCount = classCount; _mark2Rows = mark2Rows; NumGlyphs = numGlyphs;
        Location = location; ConsumedLength = consumedLength;
    }

    public CoverageTable MarkCoverage { get; }
    public int MarkCount => _marks.Length;
    public ReadOnlySpan<MarkRecord> Marks => _marks;
    public CoverageTable Mark2Coverage { get; }
    public ushort ClassCount { get; }
    public int Mark2Count => _mark2Rows.Length;
    public ReadOnlySpan<AnchorTable?> Mark2Row(int mark2Ordinal) => _mark2Rows[mark2Ordinal];
    public int NumGlyphs { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>The one hand-written boundary OQ-T4-06-1 registered: the matrix rows of
/// BaseArray/Mark2Array/LigatureAttach all carry the parent-driven classCount x 2
/// stride the generator cannot declare, so one walker serves all three (the PairSet
/// value-row precedent). Row cells hold Optional array-relative anchor offsets:
/// zero is the legal no-op NULL, a nonzero target refuses below the whole rows area
/// as storage-before-records and past the window as outside-source, both at the
/// offending cell with its ordinal provenance. Anchors dedupe through the shared
/// per-Lookup state (the cursive chain's facts caches, reused verbatim).</summary>
internal static class MarkAnchorMatrix
{
    internal const int ArrayHeaderLength = 2;

    /// <summary>Reads one row of <paramref name="rowCount"/> x classCount Optional
    /// anchors at <paramref name="arrayOffset"/> (subtable-relative). materialize=
    /// false still validates every cell and every present anchor body.</summary>
    internal static ReadStatus ReadRow(ReadOnlySpan<byte> data, int arrayOffset, int rowCount,
        int rowOrdinal, ushort classCount, int position, CursivePosReadState state,
        BuildBudget budget, ref int consumed, out AnchorTable?[] row, out FontDiagnostic diagnostic)
    {
        row = new AnchorTable?[classCount];
        int rowsEnd = ArrayHeaderLength + rowCount * 2 * classCount;
        long rowStart = ArrayHeaderLength + (long)rowOrdinal * 2 * classCount;
        for (int c = 0; c < classCount; c++)
        {
            long cell = rowStart + 2L * c;
            if (arrayOffset + cell + 2 > data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    state.At((int)(position + arrayOffset + cell)), out diagnostic,
                    (ulong)data.Length, (ulong)(arrayOffset + cell + 2));
            ushort anchorOffset = ReadWire.U16(data, (int)(arrayOffset + cell));
            if (anchorOffset == 0) continue;
            if (anchorOffset < rowsEnd)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                    state.At((int)(position + arrayOffset + cell)), out diagnostic,
                    anchorOffset, (ulong)rowsEnd);
            if (arrayOffset + anchorOffset + 2 > data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    state.At((int)(position + arrayOffset + cell)), out diagnostic,
                    (ulong)(arrayOffset + anchorOffset), (ulong)data.Length);
            int local = arrayOffset + anchorOffset;
            int target = position + local;
            if (!budget.Work(1, state.At(target), out diagnostic)) return ReadStatus.ResourceLimit;
            if (state.AnchorFacts.TryGetValue(target, out var fact))
            {
                AnchorTable? cached = null;
                if (state.Materialize) state.Anchors.TryGetValue(target, out cached);
                row[c] = cached;
                if (local + fact.Length > consumed) consumed = local + fact.Length;
                continue;
            }
            ReadStatus status = AnchorView.TryOpen(data.Slice(local), out var anchorView,
                out diagnostic, state.At(target));
            if (status != ReadStatus.Done) return status;
            status = anchorView.TryRead(out var anchor, out int length, out diagnostic, budget,
                state, target);
            if (status != ReadStatus.Done) return status;
            if (!budget.Buffer(state.Materialize ? 32 : 16, state.At(target), out diagnostic))
                return ReadStatus.ResourceLimit;
            state.AnchorFacts.Add(target, new(1, length));
            if (state.Materialize) state.Anchors.Add(target, anchor!);
            row[c] = anchor;
            if (local + length > consumed) consumed = local + length;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The hand-opened array header count (BaseArray/Mark2Array): the count
    /// word mirrors the generated counted-array guards at the array's own window.</summary>
    internal static ReadStatus ReadCount(ReadOnlySpan<byte> data, int arrayOffset, int position,
        CursivePosReadState state, out ushort count, out FontDiagnostic diagnostic)
    {
        count = 0;
        if (arrayOffset + ArrayHeaderLength > data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                state.At((int)(position + arrayOffset)), out diagnostic,
                (ulong)data.Length, (ulong)(arrayOffset + ArrayHeaderLength));
        count = ReadWire.U16(data, arrayOffset);
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>The mark-array side walk shared by types 4/5/6 (wire-identical): the
/// caller opens the declared MarkArray hop through its generated opener, this walker
/// proves the record extent, the coverage-count correspondence, every mark class's
/// array-index domain (OQ-T4-06-2's hard check; classCount-to-peak stays an
/// observation) and reads each Required anchor through the per-Lookup caches.</summary>
internal static class MarkSideReader
{
    internal static ReadStatus Read(MarkArrayView markArray, ushort classCount,
        int coverageCount, int position, ushort markArrayOffset, ReadOnlySpan<byte> data,
        DataLocation viewLocation, CursivePosReadState state, BuildBudget budget,
        ref int consumed, out MarkRecord[]? marks, out FontDiagnostic diagnostic)
    {
        marks = null;
        ReadStatus status = markArray.TryRequireMarkRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        int markCount = markArray.MarkCount;
        if (!budget.Work(MarkArrayLayout.MinimumHeader +
                (long)markCount * MarkArrayLayout.RecordStride, viewLocation, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (markCount != coverageCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                viewLocation.AtLocal(DataField.Count, markArrayOffset), out diagnostic,
                (ulong)markCount, (ulong)coverageCount);
        var recordArray = state.Materialize ? new MarkRecord[markCount] : null;
        if (state.Materialize && !budget.Buffer(16L * markCount, viewLocation, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; i < markCount; i++)
        {
            status = markArray.TryGetMarkRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (record.Class >= classCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    viewLocation.AtLocal(DataField.Payload,
                        (ulong)(markArrayOffset + MarkArrayLayout.MinimumHeader +
                            (long)i * MarkArrayLayout.RecordStride), i),
                    out diagnostic, record.Class, (ulong)classCount);
            status = markArray.TryOpenMarkAnchor(i, out var anchorView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int local = markArrayOffset + record.MarkAnchorOffset;
            int target = position + local;
            if (!budget.Work(1, state.At(target), out diagnostic)) return ReadStatus.ResourceLimit;
            AnchorTable? anchor;
            if (state.AnchorFacts.TryGetValue(target, out var fact))
            {
                anchor = null;
                if (state.Materialize) state.Anchors.TryGetValue(target, out anchor);
                if (local + fact.Length > consumed) consumed = local + fact.Length;
            }
            else
            {
                status = anchorView.TryRead(out anchor, out int length, out diagnostic, budget,
                    state, target);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(state.Materialize ? 32 : 16, state.At(target), out diagnostic))
                    return ReadStatus.ResourceLimit;
                state.AnchorFacts.Add(target, new(1, length));
                if (state.Materialize) state.Anchors.Add(target, anchor!);
                if (local + length > consumed) consumed = local + length;
            }
            if (recordArray is not null) recordArray[i] = new(record.Class, anchor!);
        }
        marks = recordArray;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>The Coverage read shared by the mark family and the cursive/anchor
/// chains: facts dedupe by absolute Lookup-local position (the cursive read state's
/// caches); Inspect proves, Materialize expands the same facts.</summary>
internal static class MarkCoverageReader
{
    internal static ReadStatus Read(int numGlyphs, int target, ReadOnlySpan<byte> data,
        CursivePosReadState state, out CoverageTable? model,
        out CursivePosReadState.Leaf fact, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null; fact = default;
        if (!budget.Work(1, state.At(target), out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.CoverageFacts.TryGetValue(target, out fact))
        {
            if (state.Materialize) state.Coverages.TryGetValue(target, out model);
            diagnostic = default;
            return ReadStatus.Done;
        }
        int local = target - (state.Length - data.Length);
        ReadStatus status = CoverageView.TryOpen(data.Slice(local), out var view, out diagnostic,
            state.At(target));
        if (status != ReadStatus.Done) return status;
        status = view.Inspect(numGlyphs, out int count, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (state.Materialize)
        {
            // Expansion consumes the facts Inspect just proved; no second inspection.
            status = view.Materialize(numGlyphs, count, length, out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (!budget.Buffer(state.Materialize ? 32 : 16, state.At(target), out diagnostic))
            return ReadStatus.ResourceLimit;
        fact = new(count, length);
        state.CoverageFacts.Add(target, fact);
        if (state.Materialize) state.Coverages.Add(target, model!);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
