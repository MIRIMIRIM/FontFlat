using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The two kern wire dialects, fixed by the root's version word (section 7:
/// the dialect is a table-level fact, the v1 subtable has no version field of its
/// own).</summary>
public enum KernDialect : byte
{
    /// <summary>OT normative version 0 (u16 version 0 + u16 nTables).</summary>
    Version0 = 0,
    /// <summary>Apple version 1.0 (fixed32 0x00010000 + u32 nTables), read-side only.</summary>
    Apple = 1,
}

/// <summary>One format 0 kerning pair. The (left, right) key is the strict-order
/// domain ((left &lt;&lt; 16) | right strictly increasing, the T0 rejection face) and
/// both glyph ids carry the read-time numGlyphs domain proof (OQ-4's hard half);
/// the value is the full FWORD domain - negative values are real witnesses (R6).</summary>
public readonly record struct KernPair(ushort Left, ushort Right, short Value);

/// <summary>One format 2 class table: firstGlyph plus one opaque class word per
/// covered glyph. The words are premultiplied byte offsets into the owning subtable
/// (left rows include the array base, right columns step by 2 - the jellee byte-level
/// convention), but the model carries them raw; the matrix decode is the body's
/// semantic face. RangeEnd/WithinNumGlyphs keep the read-time numGlyphs observation
/// (OQ-4's observed half, the BaseCoordTable.ReferenceGlyph shape).</summary>
public sealed class KernClassTable
{
    private readonly ushort[] _classes;

    internal KernClassTable(ushort firstGlyph, ushort[] classes, DataLocation location,
        int consumedLength, int rangeEnd, bool withinNumGlyphs)
    {
        FirstGlyph = firstGlyph; _classes = classes; Location = location;
        ConsumedLength = consumedLength; RangeEnd = rangeEnd; WithinNumGlyphs = withinNumGlyphs;
    }

    public ushort FirstGlyph { get; }
    public int GlyphCount => _classes.Length;
    public IReadOnlyList<ushort> Classes => Array.AsReadOnly(_classes);
    /// <summary>firstGlyph + glyphCount (the OQ-4 observation domain edge).</summary>
    public int RangeEnd { get; }
    /// <summary>The OQ-4 observation: the covered glyph range stays under the
    /// numGlyphs context this model was read with.</summary>
    public bool WithinNumGlyphs { get; }

    /// <summary>The raw class word for the covered glyph; false outside
    /// [firstGlyph, rangeEnd).</summary>
    public bool TryGetClass(ushort glyph, out ushort value)
    {
        if (glyph < FirstGlyph || glyph >= RangeEnd)
        {
            value = 0;
            return false;
        }
        value = _classes[glyph - FirstGlyph];
        return true;
    }

    internal ushort ClassAt(int ordinal) => _classes[ordinal];

    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>The typed or raw body of one kern subtable. Format 0/2 bodies are typed
/// (per dialect view); everything else keeps the raw slice: v0 unknown formats are the
/// raw-observe + length-skip face (OQ-3, the table stays valid), Apple formats 1/3 are
/// the registered boundary (state table / compact index array - zero decode, zero
/// execution), Apple 4-255 likewise.</summary>
public abstract class KernSubtableBody
{
    internal KernSubtableBody(DataLocation location, int consumedLength)
    {
        Location = location; ConsumedLength = consumedLength;
    }

    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>Typed format 0 body: the pairs in wire order (never re-sorted on read -
/// strict order was hard-checked) and the OQ-2 search-field observation face. The
/// canonical triple uses the frozen R6b convention (0/0/0 for an empty pair array), so
/// the empty witness's searchRange 6 reads as the registered non-canonical observation;
/// its rangeShift stays identical to the recomputation, the OpenSans mixed-error
/// shape.</summary>
public sealed class KernFormat0Body : KernSubtableBody
{
    private readonly KernPair[] _pairs;

    internal KernFormat0Body(KernPair[] pairs, ushort searchRange, ushort entrySelector,
        ushort rangeShift, int canonicalSearchRange, int canonicalEntrySelector,
        int canonicalRangeShift, DataLocation location, int consumedLength)
        : base(location, consumedLength)
    {
        _pairs = pairs;
        SearchRange = searchRange; EntrySelector = entrySelector; RangeShift = rangeShift;
        CanonicalSearchRange = canonicalSearchRange;
        CanonicalEntrySelector = canonicalEntrySelector;
        CanonicalRangeShift = canonicalRangeShift;
    }

    public int PairCount => _pairs.Length;
    public IReadOnlyList<KernPair> Pairs => Array.AsReadOnly(_pairs);
    internal ReadOnlySpan<KernPair> PairsSpan => _pairs;

    /// <summary>Atomically moves one pair's FWORD value (the card's pair-edit
    /// witness). The full Int16 domain needs no re-proving - negative values are real
    /// witnesses - and the order key is untouched, so the strict order the read face
    /// proved (or the factory sorted) survives every value edit.</summary>
    internal KernFormat0Body WithPairValue(int pairIndex, short value)
    {
        var pairs = (KernPair[])_pairs.Clone();
        pairs[pairIndex] = new(pairs[pairIndex].Left, pairs[pairIndex].Right, value);
        return new(pairs, SearchRange, EntrySelector, RangeShift, CanonicalSearchRange,
            CanonicalEntrySelector, CanonicalRangeShift, Location, ConsumedLength);
    }
    /// <summary>The observed wire search triple (OQ-2: an observation, never a refusal).</summary>
    public ushort SearchRange { get; }
    public ushort EntrySelector { get; }
    public ushort RangeShift { get; }
    /// <summary>The canonical recomputation under the frozen R6b convention; the
    /// searchRange can exceed the u16 field for pair counts over 10922 (the OpenSans
    /// 98304 shape), which is exactly why the field is an observation.</summary>
    public int CanonicalSearchRange { get; }
    public int CanonicalEntrySelector { get; }
    public int CanonicalRangeShift { get; }
    public bool SearchCanonical =>
        SearchRange == CanonicalSearchRange && EntrySelector == CanonicalEntrySelector &&
        RangeShift == CanonicalRangeShift;
}

/// <summary>Typed format 2 body (the class matrix): rowWidth, the three
/// subtable-relative offsets, both owned class tables and the derived kerning array
/// (OQ-7: the extent is data-derived from the premultiplied class values, never
/// declared). The class-word decode is the semantic face: left words decode as
/// (value - arrayOffset) / rowWidth, right words as value / 2, and a body whose words
/// follow the convention is Canonicalizable - the edit and compile faces refuse the
/// others instead of guessing a matrix that the wire never stated.</summary>
public sealed class KernFormat2Body : KernSubtableBody
{
    private readonly ushort[] _array;

    internal KernFormat2Body(ushort rowWidth, int leftClassOffset, int rightClassOffset,
        int kerningArrayOffset, KernClassTable leftClassTable, KernClassTable rightClassTable,
        ushort[] array, bool canonicalizable, DataLocation location, int consumedLength)
        : base(location, consumedLength)
    {
        RowWidth = rowWidth; LeftClassOffset = leftClassOffset;
        RightClassOffset = rightClassOffset; KerningArrayOffset = kerningArrayOffset;
        LeftClassTable = leftClassTable; RightClassTable = rightClassTable;
        _array = array; Canonicalizable = canonicalizable;
    }

    public ushort RowWidth { get; }
    public int LeftClassOffset { get; }
    public int RightClassOffset { get; }
    public int KerningArrayOffset { get; }
    public KernClassTable LeftClassTable { get; }
    public KernClassTable RightClassTable { get; }
    /// <summary>Derived array byte length: maxLeft + maxRight + 2 - arrayOffset
    /// (zero when either class table covers no glyph).</summary>
    public int ArrayLength => _array.Length * 2;
    internal ReadOnlySpan<ushort> ArraySpan => _array;
    /// <summary>The max class-range edge over both tables (the OQ-4 observation).</summary>
    public int MaxClassRangeEnd => Math.Max(LeftClassTable.RangeEnd, RightClassTable.RangeEnd);
    public bool ClassRangeWithinNumGlyphs =>
        LeftClassTable.WithinNumGlyphs && RightClassTable.WithinNumGlyphs;
    /// <summary>Both class words decode under the premultiplication convention
    /// (left rows array-relative and row-aligned, right columns even and under
    /// rowWidth). The edit/compile faces require it; the read face only records it.</summary>
    public bool Canonicalizable { get; }

    /// <summary>The matrix cell for a covered glyph pair: the Int16 at
    /// leftValue + rightValue (the jellee byte-level proof; both class words are
    /// subtable-relative byte offsets, the left rows already include the array base).
    /// Glyphs outside both class ranges report NotFound/GlyphNotMapped - a class
    /// matrix kerns only its covered pairs.</summary>
    public ReadStatus TryGetKernValue(ushort leftGlyph, ushort rightGlyph, out short value,
        out FontDiagnostic diagnostic)
    {
        value = 0;
        if (!Canonicalizable)
        {
            diagnostic = default;
            return ReadStatus.UnsupportedFormat;
        }
        if (!LeftClassTable.TryGetClass(leftGlyph, out ushort left) ||
            !RightClassTable.TryGetClass(rightGlyph, out ushort right))
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Location.AtLocal(DataField.GlyphId, (ulong)KernSubtable2Layout.ArrayOffsetAt),
                out diagnostic, ((ulong)leftGlyph << 16) | rightGlyph, 0ul);
        int cell = (left + right - KerningArrayOffset) / 2;
        value = (short)_array[cell];
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal short ValueAt(int leftOrdinal, int rightOrdinal)
    {
        int left = LeftClassTable.ClassAt(leftOrdinal);
        int right = RightClassTable.ClassAt(rightOrdinal);
        return (short)_array[(left + right - KerningArrayOffset) / 2];
    }

    /// <summary>Atomically moves one matrix cell (the card's class-edit witness, the
    /// row/col ordinals being class-table ordinals). The class words and the array
    /// window are untouched - only the cell's Int16 moves - so the premultiplication
    /// convention the decode proved stays intact.</summary>
    internal KernFormat2Body WithCellValue(int leftOrdinal, int rightOrdinal, short value)
    {
        var array = (ushort[])_array.Clone();
        array[CellIndexOf(leftOrdinal, rightOrdinal)] = (ushort)value;
        return new(RowWidth, LeftClassOffset, RightClassOffset, KerningArrayOffset,
            LeftClassTable, RightClassTable, array, Canonicalizable, Location, ConsumedLength)
        {
            LeftIndices = LeftIndices, RightIndices = RightIndices,
        };
    }

    private int CellIndexOf(int leftOrdinal, int rightOrdinal) =>
        (LeftClassTable.ClassAt(leftOrdinal) +
         RightClassTable.ClassAt(rightOrdinal) - KerningArrayOffset) / 2;

    /// <summary>The decoded matrix row/column indices, or null when the words do not
    /// follow the premultiplication convention (Canonicalizable false).</summary>
    internal ushort[]? LeftIndices { get; init; }
    internal ushort[]? RightIndices { get; init; }
}

/// <summary>The raw-observe body: v0 unknown formats (OQ-3's explicit observation -
/// the table stays valid and the walk skips the declared length), Apple formats 1/3/4-255
/// (the registered boundary: header fields recorded, the raw bytes preserved in order,
/// zero decode and zero execution of the state machine or the compact index array).</summary>
public sealed class KernRawSubtableBody : KernSubtableBody
{
    internal KernRawSubtableBody(byte[] rawBytes, DataLocation location, int consumedLength)
        : base(location, consumedLength) => RawBytes = rawBytes;

    public IReadOnlyList<byte> RawBytes { get; }
}

/// <summary>One subtable of the length-driven sequence: the dialect header fields
/// (raw coverage word kept next to the decoded flags), the format the walk dispatched
/// on, and the typed or raw body. The Apple tupleIndex rides along (0 for v0, which
/// has no such field). Offsets are kern-table-relative (the raw accounting).</summary>
public sealed class KernSubtable
{
    internal KernSubtable(int index, int offset, int length, KernDialect dialect,
        ushort format, ushort coverage, ushort tupleIndex, KernSubtableBody body,
        DataLocation location)
    {
        Index = index; Offset = offset; Length = length; Dialect = dialect;
        Format = format; Coverage = coverage; TupleIndex = tupleIndex;
        Body = body; Location = location;
    }

    public int Index { get; }
    /// <summary>The subtable's start relative to the kern table start.</summary>
    public int Offset { get; }
    /// <summary>The declared length the walk stepped by.</summary>
    public int Length { get; }
    public KernDialect Dialect { get; }
    /// <summary>The dispatch format: the coverage high byte for v0, the low byte for
    /// Apple (OQ-8: the dialect dispatch absorbs the reversed halves).</summary>
    public ushort Format { get; }
    public ushort Coverage { get; }
    public ushort TupleIndex { get; }
    public KernSubtableBody Body { get; }
    public bool IsTyped => Body is not KernRawSubtableBody;
    public DataLocation Location { get; }

    // Decoded coverage flags (the raw word stays alongside). The v0 positions are the
    // OT 1.9.1 frozen snapshot's low-byte flags; the Apple positions are the TTRM
    // high bits - only the owning dialect's positions are meaningful.

    /// <summary>v0 coverage bit 0 (horizontal). False under the Apple dialect.</summary>
    public bool Horizontal => Dialect == KernDialect.Version0 &&
        (Coverage & KernLayout.Flags0Horizontal) != 0;
    /// <summary>v0 coverage bit 1 (minimum). False under the Apple dialect.</summary>
    public bool Minimum => Dialect == KernDialect.Version0 &&
        (Coverage & KernLayout.Flags0Minimum) != 0;
    /// <summary>v0 coverage bit 2, or the Apple 0x4000 bit - both mean cross-stream.</summary>
    public bool CrossStream => Dialect switch
    {
        KernDialect.Version0 => (Coverage & KernLayout.Flags0CrossStream) != 0,
        _ => (Coverage & KernLayout.AppleCrossStream) != 0,
    };
    /// <summary>v0 coverage bit 3 (override). False under the Apple dialect.</summary>
    public bool Override => Dialect == KernDialect.Version0 &&
        (Coverage & KernLayout.Flags0Override) != 0;
    /// <summary>Apple 0x8000 (vertical). False under v0.</summary>
    public bool Vertical => Dialect == KernDialect.Apple &&
        (Coverage & KernLayout.AppleVertical) != 0;
    /// <summary>Apple 0x2000 (variation). False under v0.</summary>
    public bool Variation => Dialect == KernDialect.Apple &&
        (Coverage & KernLayout.AppleVariation) != 0;

    /// <summary>Atomically moves one format 0 pair's FWORD value (the card's pair
    /// edit witness). The pair index is the model's wire-order ordinal; the full
    /// Int16 value domain needs no re-proving. Consumers re-root through
    /// KernTableCompiler.TryCreate. A non-format-0 subtable refuses
    /// (UnsupportedOperation - structural change stays outside the surface, OQ-9).</summary>
    public ReadStatus TryWithPairValue(int pairIndex, short value, out KernSubtable? updated,
        out FontDiagnostic diagnostic)
    {
        updated = null;
        if (Body is not KernFormat0Body format0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Payload, (ulong)KernSubtable0Layout.PairCountAt),
                out diagnostic);
        if ((uint)pairIndex >= (uint)format0.PairCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload,
                    (ulong)(KernSubtable0Layout.PairsAt + pairIndex * KernLayout.PairStride),
                    pairIndex),
                out diagnostic, (ulong)pairIndex, (ulong)format0.PairCount);
        updated = WithBody(format0.WithPairValue(pairIndex, value));
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically moves one format 2 matrix cell (the card's class edit
    /// witness). The row/column are class-table ordinals inside both tables' covered
    /// ranges; a body whose class words do not decode (Canonicalizable false) refuses
    /// instead of guessing the matrix (UnsupportedOperation), as does every
    /// non-format-2 subtable. Consumers re-root through KernTableCompiler.TryCreate.</summary>
    public ReadStatus TryWithKernValue(int row, int col, short value, out KernSubtable? updated,
        out FontDiagnostic diagnostic)
    {
        updated = null;
        if (Body is not KernFormat2Body format2)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Payload, (ulong)KernSubtable2Layout.RowWidthAt),
                out diagnostic);
        if (!format2.Canonicalizable)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Offset, (ulong)KernSubtable2Layout.LeftClassOffsetAt),
                out diagnostic);
        if ((uint)row >= (uint)format2.LeftClassTable.GlyphCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId,
                    (ulong)(format2.LeftClassOffset + KernClassTableLayout.FirstGlyphAt), row),
                out diagnostic, (ulong)row, (ulong)format2.LeftClassTable.GlyphCount);
        if ((uint)col >= (uint)format2.RightClassTable.GlyphCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId,
                    (ulong)(format2.RightClassOffset + KernClassTableLayout.FirstGlyphAt), col),
                out diagnostic, (ulong)col, (ulong)format2.RightClassTable.GlyphCount);
        updated = WithBody(format2.WithCellValue(row, col, value));
        diagnostic = default;
        return ReadStatus.Done;
    }

    private KernSubtable WithBody(KernSubtableBody body) =>
        new(Index, Offset, Length, Dialect, Format, Coverage, TupleIndex, body, Location);
}

/// <summary>Owned whole-kern slice: the dialect, the length-driven subtable sequence
/// (every subtable walked, typed bodies materialized per budget) and the consumed
/// accounting. The walk end is the consumed length; exact tiling (walk end == extent)
/// is the R8 observation - real for eight witnesses, OpenSans's bug excluded at its
/// length contract long before, and not a spec requirement (slack is recorded, never
/// refused). Pair lookup semantics and flag application stay outside this model
/// (T0 ban: no shaping).</summary>
public sealed class KernTable
{
    private readonly KernSubtable[] _subtables;

    internal KernTable(uint version, KernDialect dialect, int numGlyphs,
        KernSubtable[] subtables, DataLocation location, int consumedLength, bool exactTiling)
    {
        Version = version; Dialect = dialect; NumGlyphs = numGlyphs;
        _subtables = subtables; Location = location; ConsumedLength = consumedLength;
        ExactTiling = exactTiling;
    }

    /// <summary>The root version word: 0 = the OT normative v0, 1 = the Apple
    /// fixed32's high half.</summary>
    public uint Version { get; }
    public KernDialect Dialect { get; }
    /// <summary>The explicit numGlyphs input the GID-domain proof was answered with.</summary>
    public int NumGlyphs { get; }
    public int SubtableCount => _subtables.Length;
    public IReadOnlyList<KernSubtable> Subtables => Array.AsReadOnly(_subtables);
    public DataLocation Location { get; }
    /// <summary>The walk end: the declared-length sum over the sequence plus the
    /// header (== the table extent exactly when <see cref="ExactTiling"/> holds).</summary>
    public int ConsumedLength { get; }
    /// <summary>The R8 observation: the sequence tiles the table exactly. Never a
    /// read requirement (the card's walk-slack ruling).</summary>
    public bool ExactTiling { get; }
}
