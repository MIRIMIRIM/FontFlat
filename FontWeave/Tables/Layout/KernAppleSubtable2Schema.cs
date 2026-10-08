using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class KernAppleSubtable2Layout
{
    internal const int HeaderLength = KernLayout.SubtableHeaderV1, RowWidthAt = 8,
        LeftClassOffsetAt = 10, RightClassOffsetAt = 12, ArrayOffsetAt = 14,
        BodyAt = KernLayout.SubtableBodyV1;
}

// The Apple v1.0 format 2 subtable (read-side typed depth, OQ-6): the v1 8-byte header
// (u32 length, coverage, tupleIndex) followed by the dialect-shared class-matrix body.
// The three offsets are subtable-relative including this header - the jellee witness's
// byte-level proof (kerningArrayOffset 16 = the body start under the 8-byte header).
// KerningArrayOffset is declared as an offset scalar without a reference: the array
// extent is derived from the premultiplied class values (OQ-7). jellee's bodies use
// the array-in-first legal non-canonical order (array 16, class tables later) - the
// W canonical re-layout is the compiler's outcome, never a read requirement.
[BinaryTable(KernLayout.Tag, KernAppleSubtable2Layout.BodyAt, ContractVersion = 1)]
[BinaryScalar("Length", WireType.UInt32, 0, DataField.Length)]
[BinaryScalar("Coverage", WireType.UInt16, 4, DataField.Flags)]
[BinaryScalar("TupleIndex", WireType.UInt16, 6, DataField.Payload)]
[BinaryScalar("RowWidth", WireType.UInt16, KernAppleSubtable2Layout.RowWidthAt, DataField.Payload)]
[BinaryScalar("LeftClassOffset", WireType.UInt16, KernAppleSubtable2Layout.LeftClassOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("RightClassOffset", WireType.UInt16, KernAppleSubtable2Layout.RightClassOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryScalar("KerningArrayOffset", WireType.UInt16, KernAppleSubtable2Layout.ArrayOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table,
    ZeroAllowed = false)]
[BinaryDerived("BodyEnd", Constant = KernAppleSubtable2Layout.BodyAt)]
[BinaryReference("LeftClassTable", "LeftClassOffset", typeof(KernClassTableView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
[BinaryReference("RightClassTable", "RightClassOffset", typeof(KernClassTableView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "BodyEnd")]
public readonly ref partial struct KernAppleSubtable2View
{
    // The Apple format 2 body read: the v0 face minus the version field, over the
    // u32 declared length. The three offsets stay subtable-relative including this
    // 8-byte header (the jellee byte-level proof), so the derivation and decode are
    // the shared rules - only the dialect head differs (OQ-6's typed depth; the
    // array-in-first witness layout is the legal non-canonical read shape).
    internal ReadStatus ReadBody(int numGlyphs, bool materialize, out KernFormat2Body? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetLength(out uint declared, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work((long)declared, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryGetRowWidth(out ushort rowWidth, out diagnostic);
        if (status != ReadStatus.Done) return status;
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
                left!, right!, array!, canonicalizable, Location, checked((int)declared))
            {
                LeftIndices = leftIndices, RightIndices = rightIndices,
            };
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The shared derivation/decode rules, answered from the class views so validate
    // and read run the identical pass (the v0 partial states the rule commentary; this
    // mirror exists because the dialect head split is the one-layout rule's correct
    // shape - the registered duplication cost, commit 1).
    private ReadStatus DeriveArray(KernClassTableView leftView, KernClassTableView rightView,
        ushort rowWidth, uint declared, ushort arrayOffset, bool materialize,
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
        if (arrayOffset < KernAppleSubtable2Layout.BodyAt)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, KernAppleSubtable2Layout.ArrayOffsetAt),
                out diagnostic, arrayOffset, (ulong)KernAppleSubtable2Layout.BodyAt);
        if (arrayOffset >= declared)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, KernAppleSubtable2Layout.ArrayOffsetAt),
                out diagnostic, arrayOffset, (ulong)declared);
        int maxLeft = MaxWord(leftView, leftCount);
        int maxRight = MaxWord(rightView, rightCount);
        int derivedEnd = leftCount > 0 && rightCount > 0 ? maxLeft + maxRight + 2 : arrayOffset;
        if (derivedEnd < arrayOffset)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, KernAppleSubtable2Layout.ArrayOffsetAt),
                out diagnostic, (ulong)derivedEnd, (ulong)arrayOffset);
        if (derivedEnd > declared)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, KernAppleSubtable2Layout.ArrayOffsetAt),
                out diagnostic, (ulong)derivedEnd, (ulong)declared);
        int arrayBytes = derivedEnd - arrayOffset;
        if ((arrayBytes & 1) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, KernAppleSubtable2Layout.ArrayOffsetAt),
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
