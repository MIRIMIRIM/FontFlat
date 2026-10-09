using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Layout;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables;

/// <summary>Canonical CFF2 compiler (card T5-05 §4). One planned layout candidate:
/// header(5) → TopDICT → GlobalSubrINDEX → CharStringINDEX → FDArray → FDSelect
/// (only when the face carries more than one FontDICT) → the PrivateDICTs (each
/// followed by its LocalSubrINDEX) → the variation store (u16 length wrapper plus
/// the T5-01 store re-emission through
/// <see cref="VariationStoreTableCompiler"/>). Charstring, subroutine and
/// blend-interleaved DICT operand regions travel byte-for-byte (never interpreted);
/// DICT integer operands re-encode minimally (BCD reals pass through verbatim, the
/// Cff1TableCompiler real-operand policy - the registered wire-equivalent tier);
/// every INDEX offSize minimizes (spec L293 SHOULD); the FDSelect re-emits in the
/// source's format family (0, 3 or 4) from the expanded per-GID map. Structural
/// changes - FontDICT or charstring set changes, FDSelect changes, TopDICT key
/// add/remove/reorder, FontMatrix rewrites, version upgrades - have no edit surface
/// (the OQ-T5-05-9 finite refusal, the T5-04 family shape); the three supported
/// edits return a new owned model and never mutate the source.</summary>
public static class Cff2TableCompiler
{
    /// <summary>Explicit root-model factory from owned parts (card §4.1): the root
    /// re-answers the structural invariants the read side enforces - the TopDICT
    /// whitelist with required keys and integer offset operands, the FDSelect
    /// presence coupling (one FontDICT never carries FDSelect, more than one always
    /// does, L366/L390), the per-GID map domains, the store-presence coupling with
    /// the TopDICT 0x18 key and the CFF2 degenerate gate (every VarData
    /// itemCount/wordDeltaCount zero, L1444), plus the SameSource guard on a
    /// supplied store. The emitted minor version is always 0 (OQ-T5-05-2). The
    /// lexical charstring aggregates stay the zero record: a factory-built model
    /// carries no scanned source.</summary>
    public static ReadStatus TryCreate(IReadOnlyList<Cff2DictEntry> topDict,
        IReadOnlyList<byte[]> globalSubrs, IReadOnlyList<byte[]> charStrings,
        IReadOnlyList<Cff2FontDict> fontDicts, IReadOnlyList<int> fdIndex, int fdSelectFormat,
        VariationStoreTable? store, out Cff2Table? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(topDict);
        ArgumentNullException.ThrowIfNull(globalSubrs);
        ArgumentNullException.ThrowIfNull(charStrings);
        ArgumentNullException.ThrowIfNull(fontDicts);
        ArgumentNullException.ThrowIfNull(fdIndex);
        var budget = new BuildBudget(limits);
        if (charStrings.Count == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic, 0ul, 1ul);
        if (!budget.Work(charStrings.Count + globalSubrs.Count + 2L * fontDicts.Count,
                location, out diagnostic))
            return ReadStatus.ResourceLimit;
        foreach (var charstring in charStrings)
        {
            ArgumentNullException.ThrowIfNull(charstring);
            if (charstring.Length > Cff2Operators.MaxCharstringLength)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Length, 0), out diagnostic,
                    (uint)charstring.Length, (uint)Cff2Operators.MaxCharstringLength);
        }
        foreach (var subr in globalSubrs)
        {
            ArgumentNullException.ThrowIfNull(subr);
            if (subr.Length > Cff2Operators.MaxCharstringLength)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Length, 0), out diagnostic,
                    (uint)subr.Length, (uint)Cff2Operators.MaxCharstringLength);
        }

        // The TopDICT face: whitelist, single definition per key, the two required
        // keys, integer operands on the four offset keys and the FontMatrix arity.
        bool hasCharStringsKey = false, hasFdArrayKey = false, hasStoreKey = false;
        for (int i = 0; i < topDict.Count; i++)
        {
            var entry = topDict[i];
            if (!Cff2Operators.IsTopOperator(entry.Operator))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, (uint)entry.EntryStart), out diagnostic,
                    (uint)entry.Operator);
            for (int prior = 0; prior < i; prior++)
            {
                if (topDict[prior].Operator == entry.Operator)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                        location.AtLocal(DataField.Payload, (uint)entry.EntryStart), out diagnostic,
                        (uint)entry.Operator);
            }
            switch (entry.Operator)
            {
                case Cff2Operators.TopFontMatrix:
                    if (entry.Operands.Count != 6)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            location.AtLocal(DataField.Payload, (uint)entry.EntryStart), out diagnostic,
                            (uint)entry.Operands.Count, 6ul);
                    break;
                case Cff2Operators.TopCharStrings:
                    hasCharStringsKey = true;
                    if (entry.Operands.Count != 1 || entry.Operands[0].IsReal)
                        return OffsetOperandRefusal(location, entry, out diagnostic);
                    break;
                case Cff2Operators.TopFdArray:
                    hasFdArrayKey = true;
                    if (entry.Operands.Count != 1 || entry.Operands[0].IsReal)
                        return OffsetOperandRefusal(location, entry, out diagnostic);
                    break;
                case Cff2Operators.TopVariationStore:
                    hasStoreKey = true;
                    if (entry.Operands.Count != 1 || entry.Operands[0].IsReal)
                        return OffsetOperandRefusal(location, entry, out diagnostic);
                    break;
                default: // TopFdSelect
                    if (entry.Operands.Count != 1 || entry.Operands[0].IsReal)
                        return OffsetOperandRefusal(location, entry, out diagnostic);
                    break;
            }
        }
        if (!hasCharStringsKey || !hasFdArrayKey)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.TableNotFound,
                location.AtLocal(DataField.Payload, 0), out diagnostic,
                (uint)(hasCharStringsKey ? Cff2Operators.TopFdArray : Cff2Operators.TopCharStrings));
        // The store<->TopDICT coupling, both directions (OQ-T5-05-7 at the factory).
        if (hasStoreKey != (store is not null))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Signature, 0), out diagnostic,
                hasStoreKey ? 0ul : 1ul, hasStoreKey ? 1ul : 0ul);

        // The FontDICT face: the single 0x12 key and the L461 `0 0` coupling.
        if (fontDicts.Count == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic, 0ul, 1ul);
        foreach (var fontDict in fontDicts)
        {
            ArgumentNullException.ThrowIfNull(fontDict);
            if (fontDict.Entry.Operator != Cff2Operators.FontPrivateDict
                || fontDict.Entry.Operands.Count != 2
                || fontDict.Entry.Operands[0].IsReal || fontDict.Entry.Operands[1].IsReal)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, (uint)fontDict.Entry.EntryStart),
                    out diagnostic, (uint)fontDict.Entry.Operator);
            bool declaredEmpty = fontDict.PrivateSize == 0;
            if (declaredEmpty != (fontDict.Private is null) || (declaredEmpty && fontDict.PrivateOffset != 0))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, (uint)fontDict.Entry.EntryStart),
                    out diagnostic, (uint)fontDict.PrivateOffset, 0ul);
        }

        // The FDSelect face (OQ-T5-05-8 domains at the factory).
        bool hasFdSelect = fdSelectFormat != -1;
        if (fontDicts.Count == 1)
        {
            if (hasFdSelect)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Version, 0), out diagnostic,
                    (uint)fdSelectFormat, 1ul);
            foreach (int fd in fdIndex)
            {
                if (fd != 0)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        location.AtLocal(DataField.FdIndex, 0), out diagnostic, (uint)fd, 1ul);
            }
        }
        else
        {
            if (fdSelectFormat is not (0 or 3 or 4))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Version, 0), out diagnostic,
                    (uint)fdSelectFormat, 4ul);
            if (fdIndex.Count != charStrings.Count)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, 0), out diagnostic,
                    (uint)fdIndex.Count, (uint)charStrings.Count);
            if ((fdSelectFormat is 0 or 3) && fontDicts.Count > 0x100)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Version, 0), out diagnostic,
                    (uint)fontDicts.Count, 0x100ul);
            if (fdSelectFormat == 3 && charStrings.Count > 0xFFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Version, 0), out diagnostic,
                    (uint)charStrings.Count, 0xFFFFul);
            for (int gid = 0; gid < fdIndex.Count; gid++)
            {
                if (fdIndex[gid] is < 0 || fdIndex[gid] >= fontDicts.Count)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        location.AtLocal(DataField.FdIndex, (uint)gid, gid), out diagnostic,
                        (uint)fdIndex[gid], (uint)fontDicts.Count);
            }
        }

        // The degenerate CFF2 store gate (L1444) and the SameSource guard.
        if (store is not null)
        {
            if (!SameSource(location, store))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    location.AtLocal(DataField.None, 0), out diagnostic);
            foreach (var subtable in store.Data)
            {
                if (subtable is null) continue;
                if (subtable.ItemCount != 0)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        subtable.Location.AtLocal(DataField.Count,
                            ItemVariationDataLayout.ItemCountAt), out diagnostic,
                        subtable.ItemCount, 0ul);
                if (subtable.WordDeltaCount != 0)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        subtable.Location.AtLocal(DataField.Flags,
                            ItemVariationDataLayout.WordDeltaCountAt), out diagnostic,
                        subtable.WordDeltaCount, 0ul);
            }
        }

        model = new Cff2Table(0, Cff2Layout.HeaderLength, [.. topDict], store is not null,
            [.. globalSubrs], [.. charStrings], [.. fontDicts], fdSelectFormat, [.. fdIndex],
            store, new Cff2CharstringStats(0, 0, null, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(Cff2Table model, out byte[]? bytes,
        out FontDiagnostic diagnostic, int? outputBudget = null)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        if (outputBudget is < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                unchecked((ulong)(long)outputBudget.Value), 0ul);
        var limits = outputBudget is int budget
            ? new BuildLimits { MaxOutputBytes = budget }
            : new BuildLimits();
        return Compile(model, out bytes, out diagnostic, new BuildBudget(limits));
    }

    /// <summary>Deterministic write through the same planned emission; every plan
    /// admission completes before the destination capacity check, and a short
    /// destination leaves the span untouched (the T5-04 atomicity discipline).</summary>
    public static ReadStatus TryWrite(Cff2Table model, Span<byte> destination,
        out int written, out FontDiagnostic diagnostic, int? outputBudget = null)
    {
        written = 0;
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = TryCompile(model, out byte[]? encoded, out diagnostic, outputBudget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < encoded!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (uint)encoded.Length);
        encoded.AsSpan().CopyTo(destination);
        written = encoded.Length;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus Compile(Cff2Table model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        bytes = null;
        int fontDictCount = model.FontDictCount;
        bool hasFdSelect = model.FdSelectFormat != -1;
        // Defensive shape re-answers (unreachable for TryRead/TryCreate models):
        // the compiled TopDICT must keep referencing every emitted block.
        if (fontDictCount == 0 || model.NumGlyphs == 0
            || Cff2Table.FindEntry(model.TopDictInternal, Cff2Operators.TopCharStrings) is null
            || Cff2Table.FindEntry(model.TopDictInternal, Cff2Operators.TopFdArray) is null
            || (Cff2Table.FindEntry(model.TopDictInternal, Cff2Operators.TopVariationStore) is not null)
                != (model.HasVariationStore && model.Store is not null)
            || (Cff2Table.FindEntry(model.TopDictInternal, Cff2Operators.TopFdSelect) is not null)
                != hasFdSelect
            || (fontDictCount == 1 && hasFdSelect) || (fontDictCount > 1 && !hasFdSelect))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Payload, 0), out diagnostic,
                (uint)fontDictCount, 1ul);

        // Per-PrivateDICT body fixpoint: the 0x13 operand is the LocalSubrINDEX
        // position relative to the PrivateDICT start, and the canonical layout
        // puts that INDEX right behind the DICT body, so the operand encodes the
        // body's own length (the Cff1 op-19 fixpoint shape). A blend-interleaved
        // 0x13 operand region cannot be re-based and refuses (the byte stream
        // would have to be interpreted - the OQ-T5-05-9 boundary).
        var privateBodyLengths = new int[fontDictCount];
        var localSubrsLengths = new long[fontDictCount];
        for (int fd = 0; fd < fontDictCount; fd++)
        {
            var privateDict = model.FontDictsInternal[fd].Private;
            if (privateDict is null) continue;
            int length = 0;
            for (int iteration = 0; iteration < 5; iteration++)
            {
                var replacements = privateDict.HasLocalSubrs
                    ? new Dictionary<int, int[]> { [Cff2Operators.PrivateLocalSubrs] = [length] }
                    : null;
                int next = DictLength(privateDict.Entries, replacements);
                if (next == length) break;
                length = next;
            }
            privateBodyLengths[fd] = length;
            localSubrsLengths[fd] = privateDict.HasLocalSubrs
                ? IndexLength(privateDict.LocalSubrs) : 0;
        }

        long globalSubrsLength = IndexLength(model.GlobalSubrsInternal);
        long charStringsLength = IndexLength(model.CharStringsInternal);
        int fdSelectLength = hasFdSelect
            ? FdSelectLength(model.FdSelectFormat, model.FdIndexInternal) : 0;
        long storeLength = 0;
        if (model.HasVariationStore)
        {
            storeLength = VariationStoreTableCompiler.Measure(model.Store!);
            if (storeLength + 2 > 0xFFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.Length,
                        ItemVariationStoreLayout.DataCountAt), out diagnostic,
                    (ulong)storeLength + 2, 0xFFFFul);
        }

        // The whole-layout fixpoint from the minimal operand widths upward: the
        // TopDICT and FontDICT offset operands encode block positions, the block
        // lengths depend on those operand widths, and the widths grow monotonically
        // (the Cff1TopDICT fixpoint shape; 24 iterations cap a non-converging plan).
        long globalSubrsOffset = 0, charStringsOffset = 0, fdArrayOffset = 0;
        long fdSelectOffset = 0, storeOffset = 0, total = 0;
        var privateOffsets = new int[fontDictCount];
        var topReplacements = new Dictionary<int, int[]>();
        for (int iteration = 0; iteration < 24; iteration++)
        {
            long fdArrayData = 0;
            for (int fd = 0; fd < fontDictCount; fd++)
            {
                fdArrayData += IntOperandLength(privateBodyLengths[fd])
                    + IntOperandLength(privateOffsets[fd]) + 1;
            }
            long fdArrayLength = 5 + (long)(fontDictCount + 1)
                * OffSizeFor(fdArrayData + 1) + fdArrayData;
            topReplacements.Clear();
            topReplacements[Cff2Operators.TopCharStrings] = [(int)charStringsOffset];
            topReplacements[Cff2Operators.TopFdArray] = [(int)fdArrayOffset];
            if (model.HasVariationStore)
                topReplacements[Cff2Operators.TopVariationStore] = [(int)storeOffset];
            if (hasFdSelect)
                topReplacements[Cff2Operators.TopFdSelect] = [(int)fdSelectOffset];
            int topDictLength = DictLength(model.TopDictInternal, topReplacements);
            if (topDictLength > 0xFFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.Length, Cff2Layout.TopDictSizeAt),
                    out diagnostic, (uint)topDictLength, 0xFFFFul);
            long cursor = Cff2Layout.HeaderLength + topDictLength;
            long nextGlobalSubrs = cursor;
            cursor += globalSubrsLength;
            long nextCharStrings = cursor;
            cursor += charStringsLength;
            long nextFdArray = cursor;
            cursor += fdArrayLength;
            long nextFdSelect = cursor;
            cursor += fdSelectLength;
            var nextPrivates = new int[fontDictCount];
            for (int fd = 0; fd < fontDictCount; fd++)
            {
                if (model.FontDictsInternal[fd].Private is null) continue;
                nextPrivates[fd] = (int)cursor;
                cursor += privateBodyLengths[fd] + localSubrsLengths[fd];
            }
            long nextStore = cursor;
            long nextTotal = cursor + (model.HasVariationStore ? storeLength + 2 : 0);
            if (nextGlobalSubrs == globalSubrsOffset && nextCharStrings == charStringsOffset
                && nextFdArray == fdArrayOffset && nextFdSelect == fdSelectOffset
                && nextStore == storeOffset && nextTotal == total
                && nextPrivates.AsSpan().SequenceEqual(privateOffsets))
                break;
            if (iteration == 23)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.Length, 0), out diagnostic,
                    (ulong)nextTotal, (ulong)total);
            globalSubrsOffset = nextGlobalSubrs;
            charStringsOffset = nextCharStrings;
            fdArrayOffset = nextFdArray;
            fdSelectOffset = nextFdSelect;
            storeOffset = nextStore;
            total = nextTotal;
            nextPrivates.CopyTo(privateOffsets, 0);
        }

        if (total > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.AtLocal(DataField.Length, 0), out diagnostic,
                (ulong)total, (ulong)int.MaxValue);
        long indexItems = model.GlobalSubrsInternal.LongLength + model.NumGlyphs
            + fontDictCount;
        foreach (var fontDict in model.FontDictsInternal)
            if (fontDict.Private is not null)
                indexItems += fontDict.Private.LocalSubrs.Length;
        if (!budget.Work(2L * total + 16L * indexItems, model.Location, out diagnostic) ||
            !budget.Buffer(total + 4L * indexItems, model.Location, out diagnostic) ||
            !budget.Output(total, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        // ---- the single planned emission ----
        var output = new byte[(int)total];
        int at = 0;
        output[at++] = Cff2Layout.MajorVersionValue;
        output[at++] = 0; // the write side always emits minor 0 (OQ-T5-05-2)
        output[at++] = Cff2Layout.HeaderLength; // the write side always emits 5 (OQ-T5-05-3)
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at), (ushort)DictLength(
            model.TopDictInternal, topReplacements));
        at += 2;
        EmitDict(output, ref at, model.TopDictInternal, topReplacements);
        EmitIndex(output, ref at, model.GlobalSubrsInternal);
        EmitIndex(output, ref at, model.CharStringsInternal);
        var fontDictItems = new byte[fontDictCount][];
        for (int fd = 0; fd < fontDictCount; fd++)
        {
            int size = privateBodyLengths[fd];
            int offset = model.FontDictsInternal[fd].Private is null ? 0 : privateOffsets[fd];
            var item = new byte[IntOperandLength(size) + IntOperandLength(offset) + 1];
            int itemAt = 0;
            WriteIntOperand(item, ref itemAt, size);
            WriteIntOperand(item, ref itemAt, offset);
            item[itemAt] = Cff2Operators.FontPrivateDict;
            fontDictItems[fd] = item;
        }
        EmitIndex(output, ref at, fontDictItems);
        if (hasFdSelect)
            EmitFdSelect(output, ref at, model.FdSelectFormat, model.FdIndexInternal);
        for (int fd = 0; fd < fontDictCount; fd++)
        {
            var privateDict = model.FontDictsInternal[fd].Private;
            if (privateDict is null) continue;
            var replacements = privateDict.HasLocalSubrs
                ? new Dictionary<int, int[]>
                {
                    [Cff2Operators.PrivateLocalSubrs] = [privateBodyLengths[fd]]
                }
                : null;
            EmitDict(output, ref at, privateDict.Entries, replacements);
            if (privateDict.HasLocalSubrs)
                EmitIndex(output, ref at, privateDict.LocalSubrs);
        }
        if (model.HasVariationStore)
        {
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at), (ushort)storeLength);
            at += 2;
            VariationStoreTableCompiler.WritePlanned(model.Store!, output.AsSpan(at));
            at += (int)storeLength;
        }
        if (at != output.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Length, (uint)at), out diagnostic,
                (uint)at, (uint)output.Length);
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation location, VariationStoreTable store)
    {
        DataLocation child = store.Location;
        return location.SourceId == child.SourceId && location.FaceIndex == child.FaceIndex &&
            (location.SourceId == 0 || location.TableTag == child.TableTag);
    }

    private static ReadStatus OffsetOperandRefusal(DataLocation location, Cff2DictEntry entry,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            location.AtLocal(DataField.Payload, (uint)entry.EntryStart), out diagnostic,
            (uint)entry.Operator);

    private static ReadStatus Unsupported(DataLocation location, out FontDiagnostic diagnostic,
        ulong detail = 0) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            location.AtLocal(DataField.Payload, detail), out diagnostic, detail);

    // ---- CFF2 wire primitives (write side; the read-side codecs live in the View) ----

    internal static int OffSizeFor(long maxOffset) => maxOffset switch
    {
        <= 0xFF => 1,
        <= 0xFFFF => 2,
        <= 0xFFFFFF => 3,
        _ => 4
    };

    internal static int IntOperandLength(int value) => value switch
    {
        >= -107 and <= 107 => 1,
        >= 108 and <= 1131 or >= -1131 and <= -108 => 2,
        >= short.MinValue and <= short.MaxValue => 3,
        _ => 5
    };

    internal static void WriteIntOperand(byte[] output, ref int at, int value)
    {
        if (value is >= -107 and <= 107)
        {
            output[at++] = unchecked((byte)((value + 139)));
        }
        else if (value is >= 108 and <= 1131)
        {
            int v = value - 108;
            output[at++] = unchecked((byte)((247 + (v >> 8))));
            output[at++] = unchecked((byte)((v & 0xFF)));
        }
        else if (value is >= -1131 and <= -108)
        {
            int v = -value - 108;
            output[at++] = unchecked((byte)((251 + (v >> 8))));
            output[at++] = unchecked((byte)((v & 0xFF)));
        }
        else if (value is >= short.MinValue and <= short.MaxValue)
        {
            output[at++] = 28;
            BinaryPrimitives.WriteInt16BigEndian(output.AsSpan(at), (short)value);
            at += 2;
        }
        else
        {
            output[at++] = 29;
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(at), value);
            at += 4;
        }
    }

    /// <summary>Wire length of one DICT under the replacements: replaced keys emit
    /// the replacement integers and every other entry a minimal re-encode of its
    /// operands (reals verbatim; blend-region integers are plain wire integers and
    /// re-encode like any operand - the entry model of the frozen wire oracle).</summary>
    internal static int DictLength(IReadOnlyList<Cff2DictEntry> entries,
        Dictionary<int, int[]>? replacements)
    {
        long length = 0;
        foreach (var entry in entries)
        {
            if (replacements is not null
                && replacements.TryGetValue(entry.Operator, out var values))
            {
                foreach (int value in values)
                    length += IntOperandLength(value);
            }
            else
            {
                foreach (var operand in entry.Operands)
                    length += operand.IsReal
                        ? operand.RealBytes!.Length + 1 // + the 0x1E real-number marker
                        : IntOperandLength(operand.Value);
            }
            length += entry.Operator > 0xFF ? 2 : 1;
        }
        return (int)length;
    }

    internal static void EmitDict(byte[] output, ref int at, IReadOnlyList<Cff2DictEntry> entries,
        Dictionary<int, int[]>? replacements)
    {
        foreach (var entry in entries)
        {
            if (replacements is not null
                && replacements.TryGetValue(entry.Operator, out var values))
            {
                foreach (int value in values)
                    WriteIntOperand(output, ref at, value);
            }
            else
            {
                foreach (var operand in entry.Operands)
                {
                    if (operand.IsReal)
                    {
                        // RealNumber operands are held without their 0x1E marker.
                        output[at++] = 30;
                        operand.RealBytes!.CopyTo(output.AsSpan(at));
                        at += operand.RealBytes!.Length;
                    }
                    else
                    {
                        WriteIntOperand(output, ref at, operand.Value);
                    }
                }
            }
            if (entry.Operator > 0xFF)
                output[at++] = 12;
            output[at++] = unchecked((byte)((entry.Operator & 0xFF)));
        }
    }

    /// <summary>Raw bytes of one re-encoded entry (the edit methods' rebuilt
    /// entries); real operands carry their marker back.</summary>
    internal static byte[] EncodeEntryRaw(int op, IReadOnlyList<Cff2Operand> operands)
    {
        int length = op > 0xFF ? 2 : 1;
        foreach (var operand in operands)
            length += operand.IsReal
                ? operand.RealBytes!.Length + 1
                : IntOperandLength(operand.Value);
        var raw = new byte[length];
        int at = 0;
        foreach (var operand in operands)
        {
            if (operand.IsReal)
            {
                raw[at++] = 30;
                operand.RealBytes!.CopyTo(raw.AsSpan(at));
                at += operand.RealBytes!.Length;
            }
            else
            {
                WriteIntOperand(raw, ref at, operand.Value);
            }
        }
        if (op > 0xFF) raw[at++] = 12;
        raw[at] = (byte)(op & 0xFF);
        return raw;
    }

    /// <summary>CFF2 INDEX wire length: u32 count, empty = exactly 4 bytes (spec
    /// L285), otherwise count + offSize + (count+1) minimized offsets + data.</summary>
    internal static long IndexLength(IReadOnlyList<byte[]> items)
    {
        if (items.Count == 0)
            return 4;
        long data = 0;
        foreach (var item in items)
            data += item.Length;
        return 5 + (long)(items.Count + 1) * OffSizeFor(data + 1) + data;
    }

    private static void EmitIndex(byte[] output, ref int at, IReadOnlyList<byte[]> items)
    {
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at), (uint)items.Count);
        at += 4;
        if (items.Count == 0)
            return;
        long data = 0;
        foreach (var item in items)
            data += item.Length;
        int offSize = OffSizeFor(data + 1);
        output[at++] = unchecked((byte)(offSize));
        long offset = 1;
        WriteIndexOffset(output, ref at, offset, offSize);
        foreach (var item in items)
        {
            offset += item.Length;
            WriteIndexOffset(output, ref at, offset, offSize);
        }
        foreach (var item in items)
        {
            item.CopyTo(output.AsSpan(at));
            at += item.Length;
        }
    }

    private static void WriteIndexOffset(byte[] output, ref int at, long value, int offSize)
    {
        // Big-endian offSize-byte offset; the shifts land inside 8*offSize <= 32
        // bits, but the long shift expression is checked, so narrow through int.
        for (int i = 0; i < offSize; i++)
            output[at + i] = unchecked((byte)((ulong)value >> (8 * (offSize - 1 - i))));
        at += offSize;
    }

    /// <summary>FDSelect wire length in the model's format family; the format 3/4
    /// range count comes from the merged adjacent-equal runs of the per-GID map
    /// (the canonical minimal partition; the sentinel is numGlyphs).</summary>
    private static int FdSelectLength(int format, int[] fdIndex)
    {
        if (format == 0)
            return 1 + fdIndex.Length;
        return format == 3
            ? 3 + 3 * FdSelectRangeCount(fdIndex) + 2
            : 5 + 6 * FdSelectRangeCount(fdIndex) + 4;
    }

    private static int FdSelectRangeCount(int[] fdIndex)
    {
        int ranges = 1;
        for (int gid = 1; gid < fdIndex.Length; gid++)
            if (fdIndex[gid] != fdIndex[gid - 1])
                ranges++;
        return ranges;
    }

    private static void EmitFdSelect(byte[] output, ref int at, int format, int[] fdIndex)
    {
        output[at++] = unchecked((byte)format);
        if (format == 0)
        {
            foreach (int fd in fdIndex)
                output[at++] = unchecked((byte)fd);
            return;
        }
        if (format == 3)
        {
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at),
                (ushort)FdSelectRangeCount(fdIndex));
            at += 2;
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at),
                (uint)FdSelectRangeCount(fdIndex));
            at += 4;
        }
        int stride = format == 3 ? 3 : 6;
        int previous = -1;
        for (int gid = 0; gid < fdIndex.Length; gid++)
        {
            if (fdIndex[gid] == previous) continue;
            previous = fdIndex[gid];
            if (format == 3)
            {
                BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at), (ushort)gid);
                output[at + 2] = unchecked((byte)previous);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at), (uint)gid);
                BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at + 4), (ushort)previous);
            }
            at += stride;
        }
        if (format == 3)
        {
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at), (ushort)fdIndex.Length);
            at += 2;
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at), (uint)fdIndex.Length);
            at += 4;
        }
    }
}
