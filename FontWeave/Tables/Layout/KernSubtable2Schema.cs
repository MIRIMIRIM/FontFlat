using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class KernSubtable2Layout
{
    internal const int HeaderLength = KernLayout.SubtableHeaderV0, RowWidthAt = 6,
        LeftClassOffsetAt = 8, RightClassOffsetAt = 10, ArrayOffsetAt = 12,
        BodyAt = KernLayout.SubtableBodyV0;
}

// One OT v0 format 2 subtable (class matrix): the shared three-word header, rowWidth
// and the three subtable-relative offsets (OT: "beginning of this subtable" - the
// jellee Apple witness proves byte-level that the same rule holds under the v1 8-byte
// header). The class tables are declared Required references (no NULL note); the
// kerning array has no declared reference at all - its extent is data-derived from the
// premultiplied class values (the card's OQ-7 semantic derivation, the cmap keep
// family), so the array window and the rowWidth/class-value relation stay in the
// semantic partial. The numGlyphs domain of firstGlyph+nGlyphs is the OQ-4 observation.
[BinaryTable(KernLayout.Tag, KernSubtable2Layout.BodyAt, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { KernLayout.Version0 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Length", WireType.UInt16, 2, DataField.Length)]
[BinaryScalar("Coverage", WireType.UInt16, 4, DataField.Flags)]
[BinaryScalar("RowWidth", WireType.UInt16, KernSubtable2Layout.RowWidthAt, DataField.Payload)]
[BinaryScalar("LeftClassOffset", WireType.UInt16, KernSubtable2Layout.LeftClassOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("RightClassOffset", WireType.UInt16, KernSubtable2Layout.RightClassOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("KerningArrayOffset", WireType.UInt16, KernSubtable2Layout.ArrayOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryDerived("BodyEnd", Constant = KernSubtable2Layout.BodyAt)]
[BinaryReference("LeftClassTable", "LeftClassOffset", typeof(KernClassTableView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("RightClassTable", "RightClassOffset", typeof(KernClassTableView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
public readonly ref partial struct KernSubtable2View
{
    // The typed format 2 body read. The declared references own the class-table
    // offsets (zero refusals, the BodyEnd floor, the subtable-end ceiling - the T0
    // "array/class offset out of range" face); this partial adds the kerning array's
    // semantic derivation (OQ-7): the extent comes from the premultiplied class words
    // (max left + max right + 2, both subtable-relative byte offsets, the left rows
    // already carrying the array base - the jellee byte-level proof), never from a
    // declared count. The class words' own convention decode is an observation
    // (Canonicalizable) and the numGlyphs class range is the OQ-4 observed half.
    internal ReadStatus ReadBody(int numGlyphs, bool materialize, out KernFormat2Body? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetLength(out ushort declared, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(declared, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryGetRowWidth(out ushort rowWidth, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Required references (no spec NULL note): the openers refuse zero, the
        // BodyEnd floor and the subtable-end ceiling natively.
        status = TryOpenLeftClassTable(out var leftView, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = leftView.ReadClassTable(numGlyphs, materialize, out var left, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryOpenRightClassTable(out var rightView, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = rightView.ReadClassTable(numGlyphs, materialize, out var right, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetKerningArrayOffset(out ushort arrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = DeriveArray(leftView, rightView, rowWidth, declared, arrayOffset, materialize,
            out var array, out bool canonicalizable, out ushort[]? leftIndices,
            out ushort[]? rightIndices, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (materialize)
        {
            if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(rowWidth, LeftClassOffset, RightClassOffset, arrayOffset,
                left!, right!, array!, canonicalizable, Location, declared)
            {
                LeftIndices = leftIndices, RightIndices = rightIndices,
            };
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The OQ-7 derivation plus the premultiplication decode, answered from the class
    // views so validate and read run the identical pass. Hard faces: the array offset
    // must land in the body, and the derived extent must sit inside the declared
    // subtable (the card's array-overflow face); a misaligned extent cannot be a u16
    // cell array and refuses with it. Decode: left words must be array-relative row
    // starts ((value - arrayOffset) aligned to rowWidth), right words even column
    // steps under rowWidth - a body that fails only loses the edit and compile faces
    // (Canonicalizable false), never the read.
    private ReadStatus DeriveArray(KernClassTableView leftView, KernClassTableView rightView,
        ushort rowWidth, ushort declared, ushort arrayOffset, bool materialize,
        out ushort[]? array, out bool canonicalizable, out ushort[]? leftIndices,
        out ushort[]? rightIndices, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        array = null;
        leftIndices = null;
        rightIndices = null;
        canonicalizable = false;
        ReadStatus status = leftView.TryGetGlyphCount(out ushort leftCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = rightView.TryGetGlyphCount(out ushort rightCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (arrayOffset < KernSubtable2Layout.BodyAt)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, KernSubtable2Layout.ArrayOffsetAt),
                out diagnostic, arrayOffset, (ulong)KernSubtable2Layout.BodyAt);
        if (arrayOffset >= declared)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, KernSubtable2Layout.ArrayOffsetAt),
                out diagnostic, arrayOffset, (ulong)declared);
        // The derived extent: cells span up to maxLeft + maxRight + 2; either class
        // table covering no glyph means no referenced cell at all.
        int maxLeft = MaxWord(leftView, leftCount);
        int maxRight = MaxWord(rightView, rightCount);
        int derivedEnd = leftCount > 0 && rightCount > 0 ? maxLeft + maxRight + 2 : arrayOffset;
        if (derivedEnd < arrayOffset)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, KernSubtable2Layout.ArrayOffsetAt),
                out diagnostic, (ulong)derivedEnd, (ulong)arrayOffset);
        if (derivedEnd > declared)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, KernSubtable2Layout.ArrayOffsetAt),
                out diagnostic, (ulong)derivedEnd, (ulong)declared);
        int arrayBytes = derivedEnd - arrayOffset;
        if ((arrayBytes & 1) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, KernSubtable2Layout.ArrayOffsetAt),
                out diagnostic, (ulong)derivedEnd, (ulong)(derivedEnd - 1));
        canonicalizable = Decodes(leftView, leftCount, rightView, rightCount, arrayOffset,
            rowWidth, materialize, out leftIndices, out rightIndices);
        if (!materialize)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        int cellCount = arrayBytes / 2;
        if (!budget.Buffer(2L * cellCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        array = new ushort[cellCount];
        for (int i = 0; i < cellCount; i++)
            array[i] = ReadWire.U16(RawBytes, arrayOffset + 2 * i);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The largest class word of a table whose extent the generated gate already
    // proved, so the per-entry read cannot fail here.
    private static int MaxWord(KernClassTableView view, ushort count)
    {
        int max = 0;
        for (int i = 0; i < count; i++)
        {
            _ = view.TryGetClass(i, out var entry, out _);
            if (entry.Value > max) max = entry.Value;
        }
        return max;
    }

    // The canonical decode: every left word is the array base plus a row-aligned
    // offset (index = (value - arrayOffset) / rowWidth) and every right word an even
    // column step under rowWidth (index = value / 2). Jellee's bodies decode exactly
    // this way; a body that does not stays readable and un-editable (the finite
    // "cannot state the matrix" refusal lives on the edit/compile faces).
    private static bool Decodes(KernClassTableView leftView, ushort leftCount,
        KernClassTableView rightView, ushort rightCount, int arrayOffset, int rowWidth,
        bool materialize, out ushort[]? leftIndices, out ushort[]? rightIndices)
    {
        leftIndices = null;
        rightIndices = null;
        if (rowWidth == 0)
            return false;
        var lefts = materialize ? new ushort[leftCount] : null;
        for (int i = 0; i < leftCount; i++)
        {
            _ = leftView.TryGetClass(i, out var entry, out _);
            int value = entry.Value - arrayOffset;
            if (value < 0 || value % rowWidth != 0 || value / rowWidth > ushort.MaxValue)
                return false;
            if (lefts is not null)
                lefts[i] = (ushort)(value / rowWidth);
        }
        var rights = materialize ? new ushort[rightCount] : null;
        for (int i = 0; i < rightCount; i++)
        {
            _ = rightView.TryGetClass(i, out var entry, out _);
            int value = entry.Value;
            if ((value & 1) != 0 || value >= rowWidth)
                return false;
            if (rights is not null)
                rights[i] = (ushort)(value / 2);
        }
        leftIndices = lefts;
        rightIndices = rights;
        return true;
    }
}
