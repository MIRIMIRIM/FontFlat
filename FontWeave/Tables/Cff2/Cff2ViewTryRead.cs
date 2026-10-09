using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables;

/// <summary>Whole-table materialization for CFF2 2.0 (card T5-05 §2 owned model,
/// full-materialization strategy): every INDEX item's bytes, every FontDICT and
/// PrivateDICT entry with its subroutines, the expanded per-GID FontDICT map and the
/// reused T5-01 store are owned. The bytes are preserved verbatim - zero-length
/// charstrings stay zero-length and BCD operands stay byte-identical (the Cff1
/// real-operand policy); nothing is interpreted.</summary>
public readonly ref partial struct Cff2View
{
    public ReadStatus TryReadTable(int numGlyphs, int? fvarAxisCount, out Cff2Table? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(numGlyphs, fvarAxisCount, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int numGlyphs, int? fvarAxisCount, out Cff2Table? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(numGlyphs, fvarAxisCount, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetMinorVersion(out byte minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetHeaderSize(out byte headerSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ParseTopDict(out var topEntries, out int topDictStart, out int topDictLength,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Validation has admitted the table; these three keys are invariant now.
        Cff2DictEntry charStringsEntry = RequireEntry(topEntries, Cff2Operators.TopCharStrings);
        Cff2DictEntry fdArrayEntry = RequireEntry(topEntries, Cff2Operators.TopFdArray);
        TryFindEntry(topEntries, Cff2Operators.TopVariationStore, out var storeEntry);
        TryFindEntry(topEntries, Cff2Operators.TopFdSelect, out var fdSelectEntry);

        // GlobalSubrINDEX and CharStringINDEX materialization (validation bounded both).
        status = TryGetGlobalSubrsAt(out ulong globalSubrsAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocateIndex((int)globalSubrsAt, out var globalSubrs, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var globalSubrBytes = new byte[globalSubrs.Count][];
        long bufferBytes = 0;
        for (int i = 0; i < globalSubrs.Count; i++)
        {
            status = ReadIndexItem(globalSubrs, i, out var subr, out diagnostic);
            if (status != ReadStatus.Done) return status;
            globalSubrBytes[i] = subr.ToArray();
            bufferBytes += subr.Length + 16;
        }
        status = TopDictOffset(charStringsEntry, out int charStringsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocateIndex(charStringsOffset, out var charStrings, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var charStringBytes = new byte[charStrings.Count][];
        for (int gid = 0; gid < charStrings.Count; gid++)
        {
            status = ReadIndexItem(charStrings, gid, out var charstring, out diagnostic);
            if (status != ReadStatus.Done) return status;
            charStringBytes[gid] = charstring.ToArray();
            bufferBytes += charstring.Length + 16;
        }
        status = ScanCharStrings(charStrings, out var stats, out diagnostic);
        if (status != ReadStatus.Done) return status;

        // FDArray: FontDICT entries with their resolved PrivateDICTs and subroutines.
        status = TopDictOffset(fdArrayEntry, out int fdArrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocateIndex(fdArrayOffset, out var fdArray, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var fontDicts = new Cff2FontDict[fdArray.Count];
        int localSubrsEnd = 0;
        for (int fd = 0; fd < fdArray.Count; fd++)
        {
            status = IndexItemBounds(fdArray, fd, out int fontDictStart, out int fontDictLength,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ParseDict(fontDictStart, fontDictLength, Cff2DictScope.FontDict,
                out var fontEntries, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = EntryIntegers(fontEntries[0], out int[] operands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int privateSize = operands[0];
            int privateOffset = operands[1];
            Cff2PrivateDict? privateDict = null;
            long fontDictBuffer = fontDictLength + 16;
            if (privateSize > 0)
            {
                status = ParseDict(privateOffset, privateSize, Cff2DictScope.Private,
                    out var privateEntries, out diagnostic);
                if (status != ReadStatus.Done) return status;
                byte[][] localSubrs = [];
                if (TryFindEntry(privateEntries, Cff2Operators.PrivateLocalSubrs,
                        out var localSubrsEntry))
                {
                    status = EntryIntegers(localSubrsEntry!, out int[] subrOperands,
                        out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = LocateIndex(privateOffset + subrOperands[0], out var localIndex,
                        out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    localSubrs = new byte[localIndex.Count][];
                    for (int i = 0; i < localIndex.Count; i++)
                    {
                        status = ReadIndexItem(localIndex, i, out var subr, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        localSubrs[i] = subr.ToArray();
                        bufferBytes += subr.Length + 16;
                    }
                    localSubrsEnd = Math.Max(localSubrsEnd, localIndex.End);
                }
                int? vsindexValue = null;
                if (TryFindEntry(privateEntries, Cff2Operators.PrivateVsindex, out var vsindexEntry))
                {
                    status = EntryIntegers(vsindexEntry!, out int[] vsOperands, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    vsindexValue = vsOperands[0];
                }
                privateDict = new Cff2PrivateDict(privateEntries, localSubrs,
                    privateEntries.Sum(static key => key.BlendCount), vsindexValue);
                fontDictBuffer += privateSize + 16L * privateEntries.Count;
            }
            fontDicts[fd] = new Cff2FontDict(fontEntries[0], privateDict);
            bufferBytes += fontDictBuffer;
        }

        // Per-GID FontDICT map: the validated FDSelect expansion, or all-zero for the
        // single-FD face without FDSelect (spec L361-366).
        int[] fdIndex = new int[numGlyphs];
        int fdSelectEnd = 0;
        if (fdSelectEntry is not null)
        {
            status = TopDictOffset(fdSelectEntry, out int fdSelectOffset, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ValidateFdSelect(fdSelectOffset, fdArray.Count, numGlyphs, out fdIndex,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            fdSelectEnd = FdSelectEnd(fdSelectOffset, numGlyphs);
        }

        // VariationStore: the same single +2 slice, typed through the T5-01 view.
        VariationStoreTable? store = null;
        int storeEnd = 0;
        if (storeEntry is not null)
        {
            status = TopDictOffset(storeEntry, out int storeOffset, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int storeLength = ReadWire.U16(_data, storeOffset);
            status = ItemVariationStoreView.TryOpen(
                _data.Slice(storeOffset + 2, storeLength), out var storeView, out diagnostic,
                Location.At(DataField.Offset, (uint)storeOffset));
            if (status != ReadStatus.Done) return status;
            status = storeView.TryRead(fvarAxisCount!.Value, out store, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            storeEnd = storeOffset + 2 + storeLength;
        }

        if (!budget.Buffer(bufferBytes + 4L * numGlyphs, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int consumed = topDictStart + topDictLength;
        consumed = Math.Max(consumed, globalSubrs.End);
        consumed = Math.Max(consumed, charStrings.End);
        consumed = Math.Max(consumed, fdArray.End);
        consumed = Math.Max(consumed, fdSelectEnd);
        consumed = Math.Max(consumed, localSubrsEnd);
        consumed = Math.Max(consumed, storeEnd);
        foreach (var fontDict in fontDicts)
            if (fontDict.Private is not null)
                consumed = Math.Max(consumed, fontDict.PrivateOffset + fontDict.PrivateSize);
        model = new Cff2Table(minor, headerSize, [.. topEntries],
            storeEntry is not null, globalSubrBytes, charStringBytes, fontDicts, fdIndex,
            store, stats, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static Cff2DictEntry RequireEntry(IReadOnlyList<Cff2DictEntry> entries, int op)
    {
        foreach (var entry in entries)
            if (entry.Operator == op)
                return entry;
        throw new InvalidOperationException("A validated CFF2 table lost a required TopDICT key.");
    }

    /// <summary>Wire end of the FDSelect block in its own format; validation has
    /// already bounded every byte.</summary>
    private int FdSelectEnd(int fdSelectOffset, int numGlyphs) => _data[fdSelectOffset] switch
    {
        0 => fdSelectOffset + 1 + numGlyphs,
        3 => fdSelectOffset + 3 + 3 * ReadWire.U16(_data, fdSelectOffset + 1) + 2,
        _ => fdSelectOffset + 5 + (int)ReadWire.U32(_data, fdSelectOffset + 1) * 6 + 4
    };
}
