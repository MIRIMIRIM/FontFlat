using FontWeave.Core;
using FontWeave.Tables.Layout;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables;

/// <summary>Strict whole-table validation for CFF2 2.0 (card T5-05 §3). numGlyphs and
/// fvarAxisCount are both explicit with no defaults (the T5-04 dual-input discipline):
/// numGlyphs is the maxp-forward context the CharStrings count must match (spec L808
/// "must match"), and the store-presence/fvarAxisCount signature coupling (OQ-T5-05-7:
/// a VariationStore requires 'fvar' and vice versa, spec L348/L1606) makes a present
/// store with a null axis count - or a missing store with a value - an
/// InvalidValue@Signature refusal before any block is walked. Error priority follows
/// the T4-01 line: version, then presence/signature, then bounds, then relations,
/// then value domains. Pure validation materializes nothing.</summary>
public readonly ref partial struct Cff2View
{
    public ReadStatus ValidateTable(int numGlyphs, int? fvarAxisCount,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(numGlyphs, fvarAxisCount, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int numGlyphs, int? fvarAxisCount,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work((long)_data.Length + 2L * numGlyphs, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        // minorVersion is "set to 0" (spec L304) with no 2.1 defined in the frozen
        // spec; a nonzero minor is the hard OQ-T5-05-2 face (the MVAR/MERG
        // reserved-value precedent), not a lenient observation.
        status = TryGetMinorVersion(out byte minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (minor != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Version, Cff2Layout.MinorVersionAt), out diagnostic,
                minor, 0);
        status = ParseTopDict(out var topEntries, out int topDictStart, out diagnostic);
        if (status != ReadStatus.Done) return status;
        bool hasStore = TryFindEntry(topEntries, Cff2Operators.TopVariationStore, out var storeEntry);
        // The store<->fvar signature coupling, both directions (OQ-T5-05-7 lean).
        if (hasStore != fvarAxisCount.HasValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Signature, 0), out diagnostic,
                hasStore ? 0ul : unchecked((ulong)(long)fvarAxisCount!.Value),
                hasStore ? 1ul : 0ul);
        // Required TopDICT keys (spec L339/L357 "Required"): 0x11 and 0x0c24.
        if (!TryFindEntry(topEntries, Cff2Operators.TopCharStrings, out var charStringsEntry))
            return MissingRequired(topDictStart, Cff2Operators.TopCharStrings, out diagnostic);
        if (!TryFindEntry(topEntries, Cff2Operators.TopFdArray, out var fdArrayEntry))
            return MissingRequired(topDictStart, Cff2Operators.TopFdArray, out diagnostic);

        // GlobalSubrINDEX at headerSize + topDictSize (the declared runtime derivation).
        status = TryGetGlobalSubrsAt(out ulong globalSubrsAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (globalSubrsAt > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, Cff2Layout.HeaderSizeAt), out diagnostic,
                globalSubrsAt, (ulong)_data.Length);
        status = LocateIndex((int)globalSubrsAt, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;

        // CharStringINDEX: bounds, count == numGlyphs (spec L808/L1578), per-item
        // 65535-byte bound (L795) and the single-pass lexical scan (OQ-T5-05-11).
        status = TopDictOffset(charStringsEntry!, out int charStringsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (charStringsOffset < 0 || (long)charStringsOffset + 4 > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (uint)Math.Max(0, charStringsOffset)),
                out diagnostic, (uint)Math.Max(0, charStringsOffset), (ulong)_data.Length);
        status = LocateIndex(charStringsOffset, out var charStrings, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (charStrings.Count != numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)charStringsOffset), out diagnostic,
                (uint)charStrings.Count, (uint)numGlyphs);
        status = ScanCharStrings(charStrings, out var stats, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // No store -> no blend/vsindex in CharStrings (spec L1428, OQ-T5-05-6 lean).
        if (!hasStore && (stats.BlendCount > 0 || stats.VsindexCount > 0))
            return NoStoreBlendRefusal(stats.BlendCount + stats.VsindexCount, out diagnostic);

        // FDArray (FontDICTINDEX): INDEX validity, at least one FontDICT (L368),
        // then the single-0x12 walks with the `0 0` empty-private coupling (L461).
        status = TopDictOffset(fdArrayEntry!, out int fdArrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (fdArrayOffset < 0 || (long)fdArrayOffset + 4 > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (uint)Math.Max(0, fdArrayOffset)),
                out diagnostic, (uint)Math.Max(0, fdArrayOffset), (ulong)_data.Length);
        status = LocateIndex(fdArrayOffset, out var fdArray, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (fdArray.Count == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)fdArrayOffset), out diagnostic, 0ul, 1ul);
        var privates = new Cff2PrivateDict?[fdArray.Count];
        int privateBlendTotal = 0;
        bool privateVsindexPresent = false;
        for (int fd = 0; fd < fdArray.Count; fd++)
        {
            status = ValidateFontDict(fdArray, fd, privates, ref privateBlendTotal,
                ref privateVsindexPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        // No store -> no blend/vsindex in the PrivateDICTs either (OQ-T5-05-6).
        if (!hasStore && (privateBlendTotal > 0 || privateVsindexPresent))
            return NoStoreBlendRefusal(privateBlendTotal + (privateVsindexPresent ? 1 : 0),
                out diagnostic);

        // FDSelect: the L366/L390 presence coupling and the format 0/3/4 domains
        // (OQ-T5-05-8 lean; format 4 has zero corpus hosts and is pinned synthetic).
        bool hasFdSelect = TryFindEntry(topEntries, Cff2Operators.TopFdSelect, out var fdSelectEntry);
        if (fdArray.Count == 1 && hasFdSelect)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Payload, (uint)fdSelectEntry!.EntryStart), out diagnostic,
                (uint)fdArray.Count, 1ul);
        if (fdArray.Count > 1 && !hasFdSelect)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Payload, (uint)fdArrayEntry!.EntryStart), out diagnostic,
                (uint)fdArray.Count, 1ul);
        if (hasFdSelect)
        {
            status = TopDictOffset(fdSelectEntry!, out int fdSelectOffset, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ValidateFdSelect(fdSelectOffset, fdArray.Count, numGlyphs, out _,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }

        // VariationStore: the +2 length-prefixed wrapper around the reused T5-01
        // ItemVariationStoreView, its full axis-count validation, and the CFF2
        // degenerate gate (itemCount/wordDeltaCount must be 0, spec L1444).
        if (hasStore)
        {
            status = TopDictOffset(storeEntry!, out int storeOffset, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ValidateVariationStore(storeOffset, fvarAxisCount!.Value, out diagnostic,
                budget);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus MissingRequired(int topDictStart, int op, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.TableNotFound,
            Location.At(DataField.Payload, (uint)topDictStart), out diagnostic, (uint)op);

    private ReadStatus NoStoreBlendRefusal(int count, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            Location.AtLocal(DataField.Signature, 0), out diagnostic, (uint)count, 0ul);

    /// <summary>One FontDICT walk: only key 0x12 exists in the scope, its two operands
    /// are the exact [size offset] pair from the table start, and an empty PrivateDICT
    /// must be encoded `0 0` (spec L461) - the size==0/offset!=0 half is the card-pinned
    /// refusal. The PrivateDICT itself is walked under the 16-operator whitelist with
    /// its ordering restrictions and LocalSubrINDEX bounds.</summary>
    private ReadStatus ValidateFontDict(IndexSpan fdArray, int fd, Cff2PrivateDict?[] privates,
        ref int privateBlendTotal, ref bool privateVsindexPresent, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        ReadStatus status = IndexItemBounds(fdArray, fd, out int fontDictStart, out int fontDictLength,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ParseDict(fontDictStart, fontDictLength, Cff2DictScope.FontDict,
            out var fontEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var entry = fontEntries[0];
        status = EntryIntegers(entry, out int[] operands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int privateSize = operands[0];
        int privateOffset = operands[1];
        if (privateSize == 0 && privateOffset != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Payload, (uint)entry.EntryStart), out diagnostic,
                unchecked((ulong)(long)privateOffset), 0ul);
        if (privateSize < 0 || (long)privateOffset + privateSize > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (uint)Math.Max(0, privateOffset)), out diagnostic,
                (ulong)Math.Max(0, (long)privateOffset + privateSize), (ulong)_data.Length);
        if (privateSize == 0)
        {
            privates[fd] = null;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = ParseDict(privateOffset, privateSize, Cff2DictScope.Private,
            out var privateEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        bool blueValuesSeen = false, familyBluesSeen = false;
        byte[][]? localSubrs = null;
        int? vsindexValue = null;
        foreach (var key in privateEntries)
        {
            if (Cff2Operators.ViolatesPrivateOrdering(key.Operator, blueValuesSeen, familyBluesSeen,
                    out int required))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, (uint)key.EntryStart), out diagnostic,
                    (uint)key.Operator, (uint)required);
            if (key.Operator == Cff2Operators.BlueValues) blueValuesSeen = true;
            if (key.Operator == Cff2Operators.FamilyBlues) familyBluesSeen = true;
            if (key.Operator == Cff2Operators.PrivateVsindex)
            {
                privateVsindexPresent = true;
                status = EntryIntegers(key, out var vsOperands, out diagnostic);
                if (status != ReadStatus.Done) return status;
                vsindexValue = vsOperands[0];
            }
            if (key.Operator == Cff2Operators.PrivateLocalSubrs)
            {
                status = EntryIntegers(key, out var subrOperands, out diagnostic);
                if (status != ReadStatus.Done) return status;
                long relative = subrOperands[0];
                // The LocalSubrINDEX offset is from the PrivateDICT start (L516).
                if (relative < 0)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                        Location.At(DataField.Offset, (uint)key.EntryStart), out diagnostic,
                        unchecked((ulong)relative), (ulong)(_data.Length - privateOffset));
                status = LocateIndex(privateOffset + (int)relative, out var localIndex,
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
                localSubrs = new byte[localIndex.Count][];
                for (int i = 0; i < localIndex.Count; i++)
                {
                    status = ReadIndexItem(localIndex, i, out var subr, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    localSubrs[i] = subr.ToArray();
                }
            }
            privateBlendTotal += key.BlendCount;
        }
        privates[fd] = new Cff2PrivateDict(privateEntries, localSubrs ?? [],
            privateEntries.Sum(static key => key.BlendCount), vsindexValue);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>FDSelect format 0/3/4 domains (OQ-T5-05-8): the full per-glyph map is
    /// expanded so validation and materialization agree on one decoder. Format 3's
    /// sentinel is the u16 face of "must equal numGlyphs" (L425); format 4 is its
    /// u32 twin for >65,534-glyph fonts.</summary>
    private ReadStatus ValidateFdSelect(int fdSelectOffset, int fdCount, int numGlyphs,
        out int[] fdIndex, out FontDiagnostic diagnostic)
    {
        fdIndex = [];
        if (fdSelectOffset < 0 || fdSelectOffset >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (uint)Math.Max(0, fdSelectOffset)),
                out diagnostic, (uint)Math.Max(0, fdSelectOffset), (ulong)_data.Length);
        int format = _data[fdSelectOffset];
        switch (format)
        {
            case 0:
            {
                long end = (long)fdSelectOffset + 1 + numGlyphs;
                if (end > _data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.At(DataField.Payload, (uint)fdSelectOffset), out diagnostic,
                        (ulong)_data.Length, (ulong)end);
                var map = new int[numGlyphs];
                for (int gid = 0; gid < numGlyphs; gid++)
                {
                    int id = _data[fdSelectOffset + 1 + gid];
                    if (id >= fdCount)
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            Location.At(DataField.FdIndex, (uint)(fdSelectOffset + 1 + gid), gid),
                            out diagnostic, (uint)id, (uint)fdCount);
                    map[gid] = id;
                }
                fdIndex = map;
                diagnostic = default;
                return ReadStatus.Done;
            }
            case 3:
            {
                if ((long)fdSelectOffset + 3 > _data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.At(DataField.Payload, (uint)fdSelectOffset), out diagnostic,
                        (ulong)_data.Length, (ulong)fdSelectOffset + 3);
                int numRanges = ReadWire.U16(_data, fdSelectOffset + 1);
                if (numRanges < 1)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Count, (uint)(fdSelectOffset + 1)), out diagnostic,
                        (uint)numRanges, 1ul);
                long end = (long)fdSelectOffset + 3 + 3L * numRanges + 2;
                if (end > _data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.At(DataField.Payload, (uint)fdSelectOffset), out diagnostic,
                        (ulong)_data.Length, (ulong)end);
                var map = new int[numGlyphs];
                int previousFirst = -1;
                for (int r = 0; r < numRanges; r++)
                {
                    int at = fdSelectOffset + 3 + 3 * r;
                    int first = ReadWire.U16(_data, at);
                    int id = _data[at + 2];
                    if (r == 0)
                    {
                        if (first != 0)
                            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                                Location.At(DataField.GlyphId, (uint)at, 0), out diagnostic,
                                (uint)first, 0ul);
                    }
                    else if (first <= previousFirst)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.At(DataField.GlyphId, (uint)at, r), out diagnostic,
                            (uint)first, (uint)previousFirst);
                    if (id >= fdCount)
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            Location.At(DataField.FdIndex, (uint)(at + 2), r), out diagnostic,
                            (uint)id, (uint)fdCount);
                    previousFirst = first;
                    int limit = r + 1 < numRanges
                        ? ReadWire.U16(_data, at + 3)
                        : numGlyphs;
                    for (int gid = first; gid < limit; gid++)
                        map[gid] = id;
                }
                int sentinel = ReadWire.U16(_data, fdSelectOffset + 3 + 3 * numRanges);
                if (sentinel != numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.GlyphId, (uint)(fdSelectOffset + 3 + 3 * numRanges)),
                        out diagnostic, (uint)sentinel, (uint)numGlyphs);
                fdIndex = map;
                diagnostic = default;
                return ReadStatus.Done;
            }
            case 4:
            {
                if ((long)fdSelectOffset + 5 > _data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.At(DataField.Payload, (uint)fdSelectOffset), out diagnostic,
                        (ulong)_data.Length, (ulong)fdSelectOffset + 5);
                long numRanges = ReadWire.U32(_data, fdSelectOffset + 1);
                if (numRanges < 1)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Count, (uint)(fdSelectOffset + 1)), out diagnostic,
                        (ulong)numRanges, 1ul);
                long end = (long)fdSelectOffset + 5 + 6L * numRanges + 4;
                if (end > _data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.At(DataField.Payload, (uint)fdSelectOffset), out diagnostic,
                        (ulong)_data.Length, (ulong)end);
                var map = new int[numGlyphs];
                long previousFirst = -1;
                for (int r = 0; r < numRanges; r++)
                {
                    int at = fdSelectOffset + 5 + 6 * r;
                    long first = ReadWire.U32(_data, at);
                    int id = ReadWire.U16(_data, at + 4);
                    if (r == 0)
                    {
                        if (first != 0)
                            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                                Location.At(DataField.GlyphId, (uint)at, 0), out diagnostic,
                                (ulong)first, 0ul);
                    }
                    else if (first <= previousFirst)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.At(DataField.GlyphId, (uint)at, r), out diagnostic,
                            (ulong)first, (ulong)previousFirst);
                    if (id >= fdCount)
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            Location.At(DataField.FdIndex, (uint)(at + 4), r), out diagnostic,
                            (uint)id, (uint)fdCount);
                    previousFirst = first;
                    long limit = r + 1 < numRanges
                        ? ReadWire.U32(_data, at + 6)
                        : numGlyphs;
                    for (long gid = first; gid < limit && gid < numGlyphs; gid++)
                        map[(int)gid] = id;
                }
                long sentinel = ReadWire.U32(_data,
                    (int)(fdSelectOffset + 5 + 6L * numRanges));
                if (sentinel != numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.GlyphId,
                            (uint)(fdSelectOffset + 5 + 6L * numRanges)),
                        out diagnostic, (ulong)sentinel, (uint)numGlyphs);
                fdIndex = map;
                diagnostic = default;
                return ReadStatus.Done;
            }
            default:
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Version, (uint)fdSelectOffset), out diagnostic,
                    (uint)format, 4ul);
        }
    }

    /// <summary>The variation store wrapper: u16 length, then the ItemVariationStore
    /// (spec L1420-1426). The +2 interior slice is opened once through the public
    /// T5-01 opener and validated under the caller's fvar axis count; the CFF2
    /// degenerate gate then refuses any VarData with itemCount or wordDeltaCount
    /// nonzero (L1444, OQ-T5-05-7 item 2). NULL outer slots stay the no-variation
    /// face (k=0), never an error (OQ-T5-05-7 item 3).</summary>
    private ReadStatus ValidateVariationStore(int storeOffset, int axisCount,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (storeOffset < 0 || (long)storeOffset + 2 > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Offset, (uint)Math.Max(0, storeOffset)), out diagnostic,
                (ulong)_data.Length, (ulong)Math.Max(0, storeOffset) + 2);
        int length = ReadWire.U16(_data, storeOffset);
        if ((long)storeOffset + 2 + length > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Offset, (uint)storeOffset), out diagnostic,
                (ulong)_data.Length, (ulong)storeOffset + 2 + (uint)length);
        ReadStatus status = ItemVariationStoreView.TryOpen(
            _data.Slice(storeOffset + 2, length), out var storeView, out diagnostic,
            Location.At(DataField.Offset, (uint)storeOffset));
        if (status != ReadStatus.Done) return status;
        status = storeView.Validate(axisCount, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = storeView.TryGetItemVariationDataCount(out ushort dataCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int outer = 0; outer < dataCount; outer++)
        {
            status = storeView.TryOpenItemVariationData(outer, out var varDataView,
                out bool present, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!present) continue;
            status = varDataView.TryGetItemCount(out ushort itemCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (itemCount != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    varDataView.Location.AtLocal(DataField.Count,
                        ItemVariationDataLayout.ItemCountAt), out diagnostic, itemCount, 0ul);
            status = varDataView.TryGetWordDeltaCount(out ushort wordDeltaCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (wordDeltaCount != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    varDataView.Location.AtLocal(DataField.Flags,
                        ItemVariationDataLayout.WordDeltaCountAt), out diagnostic,
                    wordDeltaCount, 0ul);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The single-pass lexical charstring scan (OQ-T5-05-11): token walk only
    /// - blend/vsindex/callsubr/callgsubr/mask counts, the stem count for mask byte
    /// stepping and the operand-push peak. No call-graph following, no bias-domain
    /// checks (the ambiguity is unresolvable lexically, the T3-02 precedent), no
    /// blend operand accounting (the documented scanner artifact). The only hard
    /// faces are the 65535-byte item bound (spec L795) and, at the caller, the
    /// no-store blend gate.</summary>
    private ReadStatus ScanCharStrings(IndexSpan index, out Cff2CharstringStats stats,
        out FontDiagnostic diagnostic)
    {
        stats = new Cff2CharstringStats(0, 0, null, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        int blend = 0, vsindex = 0, callSubr = 0, callGsubr = 0;
        int hintmask = 0, cntrmask = 0, maskWithoutStems = 0, maskBytesTruncated = 0;
        int truncatedTokens = 0, maxPeak = 0, maxItem = 0;
        int? lastVsindexValue = null;
        for (int gid = 0; gid < index.Count; gid++)
        {
            ReadStatus status = IndexItemBounds(index, gid, out int start, out int length,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (length > Cff2Operators.MaxCharstringLength)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Length, (uint)start, gid), out diagnostic,
                    (uint)length, (uint)Cff2Operators.MaxCharstringLength);
            if (length > maxItem) maxItem = length;
            int at = start;
            int end = start + length;
            int pushed = 0;
            int stems = 0;
            int? lastValue = null;
            while (at < end)
            {
                byte b = _data[at++];
                if (b is >= 32 and <= 246)
                {
                    lastValue = b - 139;
                    pushed++;
                }
                else if (b is >= 247 and <= 250)
                {
                    if (at >= end) { truncatedTokens++; break; }
                    lastValue = (b - 247) * 256 + _data[at++] + 108;
                    pushed++;
                }
                else if (b is >= 251 and <= 254)
                {
                    if (at >= end) { truncatedTokens++; break; }
                    lastValue = -(b - 251) * 256 - _data[at++] - 108;
                    pushed++;
                }
                else if (b == 28)
                {
                    if (at + 2 > end) { truncatedTokens++; break; }
                    lastValue = System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(
                        _data.Slice(at, 2));
                    at += 2;
                    pushed++;
                }
                else if (b == 255)
                {
                    // CharString-only Fixed 16.16; the value is never decoded.
                    if (at + 4 > end) { truncatedTokens++; break; }
                    at += 4;
                    lastValue = null;
                    pushed++;
                }
                else
                {
                    switch (b)
                    {
                        case 0x0a: // callsubr: pops the biased index, does not clear.
                            callSubr++;
                            pushed = Math.Max(0, pushed - 1);
                            break;
                        case 0x1d: // callgsubr
                            callGsubr++;
                            pushed = Math.Max(0, pushed - 1);
                            break;
                        case 0x0f: // vsindex: single operand, resets the lexical context.
                            vsindex++;
                            lastVsindexValue = lastValue;
                            pushed = 0;
                            break;
                        case 0x10: // blend: operand accounting is the documented artifact.
                            blend++;
                            break;
                        case 0x01 or 0x03 or 0x12 or 0x17: // hstem/vstem/hstemhm/vstemhm
                            stems += pushed / 2;
                            pushed = 0;
                            break;
                        case 0x13 or 0x14: // hintmask/cntrmask consume mask bytes.
                            if (b == 0x13) hintmask++; else cntrmask++;
                            if (stems == 0)
                            {
                                // "must not be used unless the CharString also defines
                                // stems" (L1278): observation only (OQ-T5-05-6).
                                maskWithoutStems++;
                            }
                            else
                            {
                                int maskBytes = 1 + (stems - 1) / 8;
                                if (at + maskBytes > end)
                                {
                                    maskBytesTruncated++;
                                    at = end;
                                    break;
                                }
                                at += maskBytes;
                            }
                            pushed = 0;
                            break;
                        case 0x0c: // escape prefix: one more operator byte.
                            if (at >= end) { truncatedTokens++; break; }
                            at++;
                            pushed = 0;
                            break;
                        default: // every other operator clears the operand stack.
                            pushed = 0;
                            break;
                    }
                }
                if (pushed > maxPeak) maxPeak = pushed;
            }
        }
        stats = new Cff2CharstringStats(blend, vsindex, lastVsindexValue, callSubr, callGsubr,
            hintmask, cntrmask, maskWithoutStems, maskBytesTruncated, truncatedTokens,
            maxPeak, maxItem);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
