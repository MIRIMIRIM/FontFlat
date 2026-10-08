using System.Numerics;
using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The kern subtable writers (the CursivePos/BaseAxis leaf family shape):
/// the public explicit factories whose record inputs carry every row, and the
/// emission the root plan drives. Format 0's canonical form is the strict pair order
/// plus the recomputed search triple - the factory sorts (duplicates refuse), and
/// emission recomputes the search fields from the pair count, so an unedited
/// canonical witness re-emits byte-identically and a wrong-searchRange wire
/// re-emits wire-equivalent with canonical fields (the card's wire-equivalent
/// expectation; never identity for it). The emission search formula keeps the
/// largest power of two at or under max(pairCount, 1), which reproduces every real
/// canonical wire including the empty table's 6/0/0 - the R6b observation's 0/0/0
/// convention for the empty shape stays the read side's, and the two conventions
/// meet exactly at the seven byte-identity files.
///
/// Format 2's canonical layout is fixed (header, left class table, right class
/// table, kerning array last) with the class words re-premultiplied for the final
/// array position: left rows carry arrayOffset + index * rowWidth, right columns
/// step by 2 (the jellee convention). rowWidth derives as 2 * columnCount; a
/// factory input that does not state its matrix (row index over the value rows,
/// column index over the value columns, or a value count off rows * columns)
/// refuses instead of guessing.</summary>
public static class KernSubtableCompiler
{
    /// <summary>Explicit format 0 factory: every pair is caller-supplied, any input
    /// order (the factory sorts into the canonical strict order), duplicate keys
    /// refuse, and both glyph ids carry the explicit numGlyphs domain (the read
    /// face's hard check mirrored at admission). flags is the v0 flags byte; the
    /// reserved bits 4-7 refuse exactly like the read face.</summary>
    public static ReadStatus TryCreateFormat0(int numGlyphs, IReadOnlyList<KernPair> pairs,
        byte flags, out KernSubtable? subtable, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        subtable = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(14 + KernLayout.PairStride * (pairs?.Count ?? 0), location,
            out diagnostic)) return ReadStatus.ResourceLimit;
        if ((flags & KernLayout.Flags0ReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, 4), out diagnostic,
                flags, (ulong)KernLayout.Flags0LegalMask);
        if (pairs is null || pairs.Count == 0)
        {
            // The empty format 0 subtable is a real shape (the testgvareight witness).
            return Finish(Array.Empty<KernPair>(), flags, location, out subtable, out diagnostic);
        }
        ReadStatus numGlyphsStatus = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location,
            out diagnostic);
        if (numGlyphsStatus != ReadStatus.Done) return numGlyphsStatus;
        var sorted = pairs.ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            if (sorted[i].Left >= (ushort)numGlyphs || sorted[i].Right >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId,
                        (ulong)(KernSubtable0Layout.PairsAt + i * KernLayout.PairStride), i),
                    out diagnostic,
                    Math.Max(sorted[i].Left, sorted[i].Right), (ulong)numGlyphs);
        }
        Array.Sort(sorted, static (a, b) =>
            (((uint)a.Left << 16) | a.Right).CompareTo(((uint)b.Left << 16) | b.Right));
        for (int i = 1; i < sorted.Length; i++)
        {
            if ((((uint)sorted[i].Left << 16) | sorted[i].Right) <=
                (((uint)sorted[i - 1].Left << 16) | sorted[i - 1].Right))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.GlyphId,
                        (ulong)(KernSubtable0Layout.PairsAt + i * KernLayout.PairStride), i),
                    out diagnostic,
                    ((uint)sorted[i].Left << 16) | sorted[i].Right,
                    ((uint)sorted[i - 1].Left << 16) | sorted[i - 1].Right);
        }
        return Finish(sorted, flags, location, out subtable, out diagnostic);
    }

    /// <summary>Explicit format 2 factory: the two class tables as (firstGlyph,
    /// per-glyph class index) rows and the matrix values row-major. The canonical
    /// layout is fixed - left table, right table, array last - and the factory
    /// pre-multiplies the class words for that final array position.</summary>
    public static ReadStatus TryCreateFormat2(int numGlyphs,
        ushort leftFirstGlyph, IReadOnlyList<ushort> leftRows,
        ushort rightFirstGlyph, IReadOnlyList<ushort> rightColumns,
        IReadOnlyList<short> values, byte flags, out KernSubtable? subtable,
        out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        subtable = null;
        var left = (leftRows ?? Array.Empty<ushort>()).ToArray();
        var right = (rightColumns ?? Array.Empty<ushort>()).ToArray();
        var cells = (values ?? Array.Empty<short>()).ToArray();
        int leftCount = left.Length, rightCount = right.Length;
        var budget = new BuildBudget(limits);
        if (!budget.Input(18 + 2 * (leftCount + rightCount) + 2 * cells.Length,
            location, out diagnostic)) return ReadStatus.ResourceLimit;
        if ((flags & KernLayout.Flags0ReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, 4), out diagnostic,
                flags, (ulong)KernLayout.Flags0LegalMask);
        ReadStatus numGlyphsStatus = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location,
            out diagnostic);
        if (numGlyphsStatus != ReadStatus.Done) return numGlyphsStatus;
        int rows = leftCount > 0 ? left.Max() + 1 : 0;
        int columns = rightCount > 0 ? right.Max() + 1 : 0;
        if (rows > ushort.MaxValue / 2 || columns > ushort.MaxValue / 2 ||
            (long)rows * columns * 2 > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Length, 2), out diagnostic,
                (ulong)((long)rows * columns * 2), (ulong)ushort.MaxValue);
        if (cells.Length != rows * columns)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, (ulong)KernSubtable2Layout.ArrayOffsetAt),
                out diagnostic, (ulong)cells.Length, (ulong)(rows * columns));
        // The canonical placement: header, left table, right table, array last.
        int leftAt = KernSubtable2Layout.BodyAt;
        int rightAt = leftAt + KernClassTableLayout.HeaderLength + 2 * leftCount;
        int arrayAt = rightAt + KernClassTableLayout.HeaderLength + 2 * rightCount;
        ushort rowWidth = (ushort)(2 * columns);
        int length = arrayAt + rows * rowWidth;
        if (length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Length, 2), out diagnostic,
                (ulong)length, (ulong)ushort.MaxValue);
        var leftClasses = new ushort[leftCount];
        for (int i = 0; i < leftCount; i++)
        {
            if (left[i] >= rows)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId,
                        (ulong)(leftAt + KernClassTableLayout.ClassesAt + 2 * i), i),
                    out diagnostic, left[i], (ulong)rows);
            leftClasses[i] = (ushort)(arrayAt + left[i] * rowWidth);
        }
        var rightClasses = new ushort[rightCount];
        for (int j = 0; j < rightCount; j++)
        {
            if (right[j] >= columns)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId,
                        (ulong)(rightAt + KernClassTableLayout.ClassesAt + 2 * j), j),
                    out diagnostic, right[j], (ulong)columns);
            rightClasses[j] = (ushort)(2 * right[j]);
        }
        var array = new ushort[rows * columns];
        for (int k = 0; k < array.Length; k++)
            array[k] = (ushort)cells[k];
        var leftModel = new KernClassTable(leftFirstGlyph, leftClasses, location,
            KernClassTableLayout.HeaderLength + KernClassTableLayout.ClassStride * leftCount,
            leftFirstGlyph + leftCount, leftFirstGlyph + leftCount <= numGlyphs);
        var rightModel = new KernClassTable(rightFirstGlyph, rightClasses, location,
            KernClassTableLayout.HeaderLength + KernClassTableLayout.ClassStride * rightCount,
            rightFirstGlyph + rightCount, rightFirstGlyph + rightCount <= numGlyphs);
        var body = new KernFormat2Body(rowWidth, leftAt, rightAt, arrayAt, leftModel,
            rightModel, array, canonicalizable: true, location, length)
        {
            LeftIndices = left.ToArray(), RightIndices = right.ToArray(),
        };
        subtable = new KernSubtable(0, 0, length, KernDialect.Version0, 2,
            (ushort)((0x02 << 8) | flags), 0, body, location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The subtable's canonical byte size: the measure the root plan lays
    /// out with. Raw bodies have no canonical emission and never reach this; the
    /// format 2 measure is the exact canonical placement (header, left, right,
    /// array), never the source wire's declared length.</summary>
    internal static int Measure(KernSubtable subtable) => subtable.Body switch
    {
        KernFormat0Body format0 => KernSubtable0Layout.PairsAt +
            KernLayout.PairStride * format0.PairCount,
        KernFormat2Body format2 => KernSubtable2Layout.BodyAt
            + KernClassTableLayout.HeaderLength + 2 * format2.LeftClassTable.GlyphCount
            + KernClassTableLayout.HeaderLength + 2 * format2.RightClassTable.GlyphCount
            + format2.ArrayLength,
        _ => subtable.Length,
    };

    /// <summary>The emission search triple: the largest power of two at or under
    /// max(pairCount, 1) drives the range, so the empty table emits 6/0/0 (the seven
    /// byte-identity files) while the read-side observation recomputes 0/0/0 for the
    /// same shape (the frozen R6b convention). Inside the u16 length domain the
    /// range always fits.</summary>
    internal static void EmissionSearchFields(int pairCount, out ushort searchRange,
        out ushort entrySelector, out ushort rangeShift)
    {
        uint largest = 1;
        while ((long)largest * 2 <= Math.Max(pairCount, 1)) largest *= 2;
        searchRange = (ushort)(largest * KernLayout.PairStride);
        entrySelector = (ushort)BitOperations.Log2(largest);
        rangeShift = (ushort)(Math.Max(pairCount - (int)largest, 0) * KernLayout.PairStride);
    }

    /// <summary>Writes one subtable's canonical bytes at the destination start (the
    /// root plan positions it). Format 0 re-sorts defensively (an invariant breach is
    /// impossible from the factory or the read face, and sort is stable for keys),
    /// recomputes the search triple and fixes the length; format 2 re-premultiplies
    /// the class words for the fixed canonical placement and re-emits the cells
    /// through the decoded matrix.</summary>
    internal static ReadStatus Write(KernSubtable subtable, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        switch (subtable.Body)
        {
            case KernFormat0Body format0:
            {
                int length = Measure(subtable);
                var pairs = format0.PairsSpan.ToArray();
                ReadStatus status = KernSubtable0View.TryWriteVersion(destination, 0,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                status = KernSubtable0View.TryWriteLength(destination, (ushort)length,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                status = KernSubtable0View.TryWriteCoverage(destination, subtable.Coverage,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                status = KernSubtable0View.TryWritePairCount(destination, (ushort)pairs.Length,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                EmissionSearchFields(pairs.Length, out ushort searchRange,
                    out ushort entrySelector, out ushort rangeShift);
                status = KernSubtable0View.TryWriteSearchRange(destination, searchRange,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                status = KernSubtable0View.TryWriteEntrySelector(destination, entrySelector,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                status = KernSubtable0View.TryWriteRangeShift(destination, rangeShift,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                for (int i = 0; i < pairs.Length; i++)
                {
                    status = KernSubtable0View.TryWriteKernPairAt(destination,
                        (ulong)(KernSubtable0Layout.PairsAt + i * KernLayout.PairStride),
                        new KernSubtable0View.KernPairData(i, pairs[i].Left, pairs[i].Right,
                            pairs[i].Value),
                        out diagnostic, subtable.Location);
                    if (status != ReadStatus.Done) return status;
                }
                diagnostic = default;
                return ReadStatus.Done;
            }
            case KernFormat2Body format2:
            {
                if (!format2.Canonicalizable || format2.LeftIndices is null ||
                    format2.RightIndices is null)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                        DiagnosticCode.UnsupportedOperation,
                        subtable.Location.AtLocal(DataField.Offset,
                            (ulong)KernSubtable2Layout.LeftClassOffsetAt), out diagnostic);
                int leftCount = format2.LeftClassTable.GlyphCount;
                int rightCount = format2.RightClassTable.GlyphCount;
                int leftAt = KernSubtable2Layout.BodyAt;
                int rightAt = leftAt + KernClassTableLayout.HeaderLength + 2 * leftCount;
                int arrayAt = rightAt + KernClassTableLayout.HeaderLength + 2 * rightCount;
                int length = arrayAt + format2.ArrayLength;
                var coverage = subtable.Coverage;
                ReadStatus status = KernSubtable2View.TryWriteVersion(destination, 0,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                status = KernSubtable2View.TryWriteLength(destination, (ushort)length,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                status = KernSubtable2View.TryWriteCoverage(destination, coverage,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                status = KernSubtable2View.TryWriteRowWidth(destination, format2.RowWidth,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                // The three offsets re-premultiply for THIS placement (canonical:
                // left, right, array last); the preflight refuses anything the u16
                // cannot state.
                status = KernSubtable2View.TryWriteLeftClassOffset(destination, (ushort)leftAt,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                status = KernSubtable2View.TryWriteRightClassOffset(destination, (ushort)rightAt,
                    out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                status = KernSubtable2View.TryWriteKerningArrayOffset(destination,
                    (ushort)arrayAt, out diagnostic, subtable.Location);
                if (status != ReadStatus.Done) return status;
                for (int i = 0; i < leftCount; i++)
                {
                    int word = arrayAt + format2.LeftIndices[i] * format2.RowWidth;
                    status = KernClassTableView.TryWriteFirstGlyph(destination.Slice(leftAt),
                        format2.LeftClassTable.FirstGlyph, out diagnostic,
                        subtable.Location.AtLocal(DataField.None, (ulong)leftAt));
                    if (status != ReadStatus.Done) return status;
                    status = KernClassTableView.TryWriteGlyphCount(destination.Slice(leftAt),
                        (ushort)leftCount, out diagnostic,
                        subtable.Location.AtLocal(DataField.None, (ulong)leftAt));
                    if (status != ReadStatus.Done) return status;
                    status = KernClassTableView.TryWriteClassAt(destination.Slice(leftAt),
                        (ulong)(KernClassTableLayout.ClassesAt + 2 * i),
                        new KernClassTableView.ClassData(i, (ushort)word), out diagnostic,
                        subtable.Location.AtLocal(DataField.None, (ulong)leftAt));
                    if (status != ReadStatus.Done) return status;
                }
                for (int j = 0; j < rightCount; j++)
                {
                    status = KernClassTableView.TryWriteFirstGlyph(destination.Slice(rightAt),
                        format2.RightClassTable.FirstGlyph, out diagnostic,
                        subtable.Location.AtLocal(DataField.None, (ulong)rightAt));
                    if (status != ReadStatus.Done) return status;
                    status = KernClassTableView.TryWriteGlyphCount(destination.Slice(rightAt),
                        (ushort)rightCount, out diagnostic,
                        subtable.Location.AtLocal(DataField.None, (ulong)rightAt));
                    if (status != ReadStatus.Done) return status;
                    int word = 2 * format2.RightIndices[j];
                    status = KernClassTableView.TryWriteClassAt(destination.Slice(rightAt),
                        (ulong)(KernClassTableLayout.ClassesAt + 2 * j),
                        new KernClassTableView.ClassData(j, (ushort)word), out diagnostic,
                        subtable.Location.AtLocal(DataField.None, (ulong)rightAt));
                    if (status != ReadStatus.Done) return status;
                }
                // The cells re-emit through the decoded matrix: row-major at the new
                // array position, values read from the source window.
                int columns = format2.RowWidth / 2;
                for (int i = 0; i < leftCount; i++)
                    for (int j = 0; j < rightCount; j++)
                    {
                        int at = arrayAt + (i * columns + j) * 2;
                        if (at + 2 > length) continue; // beyond the last full row cell
                        destination[at] = (byte)((ushort)format2.ValueAt(i, j) >> 8);
                        destination[at + 1] = (byte)(ushort)format2.ValueAt(i, j);
                    }
                diagnostic = default;
                return ReadStatus.Done;
            }
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    subtable.Location.AtLocal(DataField.None, 0), out diagnostic);
        }
    }

    private static ReadStatus Finish(KernPair[] pairs, byte flags, DataLocation location,
        out KernSubtable? subtable, out FontDiagnostic diagnostic)
    {
        int length = KernSubtable0Layout.PairsAt + KernLayout.PairStride * pairs.Length;
        var body = new KernFormat0Body(pairs, 0, 0, 0, 0, 0, 0, location, length);
        subtable = new KernSubtable(0, 0, length, KernDialect.Version0, 0,
            (ushort)((0x00 << 8) | flags), 0, body, location);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
