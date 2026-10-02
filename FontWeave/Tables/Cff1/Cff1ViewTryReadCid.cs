using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>CID-keyed materialization, sharing the Cff1View reader.
///
/// Validation has already proved the table is well formed by the time this runs, so the work
/// here is copying the structures into owned models. Nothing is interpreted: charstrings and
/// subroutines stay verbatim byte programs.
/// </summary>
public readonly ref partial struct Cff1View
{
    private ReadStatus TryReadCid(IndexSpan strings, List<DictEntryData> entries,
        BuildBudget budget, out CidCff1Table? table, out FontDiagnostic diagnostic)
    {
        table = null;
        // Validation has already proved these keys are present, so the lookups cannot fail
        // here; the null-forgiving form keeps that invariant explicit.
        ReadStatus status = EntryIntegers(FindRequired(entries, Cff1Layout.OpRos), out var ros,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = EntryIntegers(FindRequired(entries, Cff1Layout.OpCharStrings),
            out var charStringsOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = EntryIntegers(FindRequired(entries, Cff1Layout.OpFdArray),
            out var fdArrayOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = EntryIntegers(FindRequired(entries, Cff1Layout.OpFdSelect),
            out var fdSelectOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;

        int cidCount = -1;
        if (TryFindEntry(entries, Cff1Layout.OpCidCount, out var cidCountEntry))
            cidCount = cidCountEntry!.Operands[0]!.Value;
        int charsetValue = 0;
        if (TryFindEntry(entries, Cff1Layout.OpCharset, out var charsetEntry))
            charsetValue = charsetEntry!.Operands[0]!.Value;

        // --- charstrings, verbatim ---
        status = LocateIndex(charStringsOperands[0], out var charstrings, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int numGlyphs = charstrings.Count;
        var charItems = new byte[numGlyphs][];
        for (int gid = 0; gid < numGlyphs; gid++)
        {
            status = ReadIndexItem(charstrings, gid, out var program, out diagnostic);
            if (status != ReadStatus.Done) return status;
            charItems[gid] = program.ToArray();
        }

        // --- charset: gid -> CID ---
        // A CID charset holds CIDs, so the string INDEX does not bound it; the CID space does.
        int cidBound = cidCount >= 0 ? cidCount : CidLayout.MaxCidCount;
        var charsetSids = new int[numGlyphs];
        // Predefined forms (0/1/2) are marker values in the DICT, not table offsets, so the
        // recorded format is the marker itself rather than a byte read from the table.
        var charsetForm = new Cff1CharsetForm(charsetValue is 0 or 1 or 2,
            charsetValue is 0 or 1 or 2 ? charsetValue : _data[charsetValue]);
        if (charsetValue is 0 or 1 or 2)
        {
            // Predefined charsets are name-keyed constructs. Absent a CID mapping the CIDs
            // are taken as the identity, which is what the predefined forms denote.
            for (int gid = 0; gid < numGlyphs; gid++) charsetSids[gid] = gid;
        }
        else
        {
            status = ReadCustomCharset(charsetValue, numGlyphs, cidBound, budget,
                charsetSids, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }

        // --- FDSelect ---
        status = ReadFdSelect(fdSelectOperands[0], numGlyphs, budget, out var fdSelect,
            out diagnostic);
        if (status != ReadStatus.Done) return status;

        // --- FDArray ---
        status = LocateIndex(fdArrayOperands[0], out var fdArray, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var fontDicts = new CidFontDict[fdArray.Count];
        for (int fd = 0; fd < fdArray.Count; fd++)
        {
            status = IndexItemBounds(fdArray, fd, out int fontStart, out int fontLength,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ParseDict(fontStart, fontLength, Cff1DictScope.FontDict, out var fontEntries,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            var raw = new byte[fontLength];
            _data.Slice(fontStart, fontLength).CopyTo(raw);
            status = MaterializeEntries(fontEntries, out var fontDictEntryModels, out diagnostic);
            if (status != ReadStatus.Done) return status;

            int privateOffset = -1, privateSize = -1;
            Cff1DictEntry[] privateModels = [];
            bool hasLocalSubrs = false;
            byte[][] localSubrs = [];
            if (TryFindEntry(fontEntries, Cff1Layout.OpPrivate, out var privateEntry))
            {
                privateSize = privateEntry!.Operands[0]!.Value;
                privateOffset = privateEntry.Operands[1]!.Value;
                status = ParseDict(privateOffset, privateSize, Cff1DictScope.Private,
                    out var privateEntries, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = MaterializeEntries(privateEntries, out privateModels, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (TryFindEntry(privateEntries, Cff1Layout.OpSubrs, out var subrsEntry))
                {
                    hasLocalSubrs = true;
                    // Relative to the Private DICT start, and not necessarily equal to its
                    // size: the largest witness points each FD's Subrs at a later region.
                    int subrsRelative = subrsEntry!.Operands[0]!.Value;
                    status = LocateIndex(privateOffset + subrsRelative, out var local,
                        out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    localSubrs = new byte[local.Count][];
                    for (int i = 0; i < local.Count; i++)
                    {
                        status = ReadIndexItem(local, i, out var program, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        localSubrs[i] = program.ToArray();
                    }
                }
            }
            fontDicts[fd] = new CidFontDict(fd, raw, fontDictEntryModels, privateOffset,
                privateSize, privateModels, hasLocalSubrs, localSubrs,
                Location.At(DataField.Payload, (ulong)(uint)fontStart, fd));
        }

        long byteBudget = (long)charstrings.Length + fdArray.Length;
        foreach (var fontDict in fontDicts)
        {
            byteBudget += fontDict.PrivateSize > 0 ? fontDict.PrivateSize : 0;
            foreach (var program in fontDict.LocalSubrs) byteBudget += program.Length;
        }
        if (!budget.Buffer(byteBudget, Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        // ReadFdSelect either produced a model or failed, so it is never null on this path.
        // The shared parts (header, name, Top DICT, String and Global Subrs INDEXes) are
        // carried on the CID model too, because recompilation has to re-emit them.
        status = MaterializeItems(strings, out var stringItems, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MaterializeEntries(entries, out var topDictModels, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocateIndexes(out var nameIndex, out _, out _, out var globalIndex,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MaterializeItems(globalIndex, out var globalItems, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadIndexItem(nameIndex, 0, out var nameItem, out diagnostic);
        if (status != ReadStatus.Done) return status;

        table = new CidCff1Table(_data[0], _data[1], _data[2], _data[3], nameItem.ToArray(),
            topDictModels, stringItems, globalItems,
            new CidRos(ros[0], ros[1], ros[2]), cidCount, fdSelect!,
            fontDicts, charsetSids, charItems, charsetForm, Location,
            (ulong)(uint)fdArrayOperands[0], (ulong)(uint)charsetValue,
            (ulong)(uint)fdSelectOperands[0], (ulong)(uint)charStringsOperands[0]);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Looks up a DICT key the validator has already required. Reaching this with a
    /// missing key would mean validation and materialization disagree, so it throws rather
    /// than returning null and deferring the failure.</summary>
    private static DictEntryData FindRequired(List<DictEntryData> entries, int op) =>
        TryFindEntry(entries, op, out var entry) && entry is not null
            ? entry
            : throw new InvalidOperationException(
                $"CID-keyed validation required operator 0x{op:X4} but it is absent.");

    /// <summary>Reads FDSelect into an owned model, keeping the wire format.</summary>
    private ReadStatus ReadFdSelect(int offset, int numGlyphs, BuildBudget budget,
        out CidFdSelect? fdSelect, out FontDiagnostic diagnostic)
    {
        fdSelect = null;
        if (!budget.Buffer(1L + numGlyphs + 8L * numGlyphs, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int format = _data[offset];
        if (format == CidLayout.FdSelectFormat0)
        {
            var perGlyph = new int[numGlyphs];
            int max = 0;
            for (int gid = 0; gid < numGlyphs; gid++)
            {
                perGlyph[gid] = _data[offset + 1 + gid];
                if (perGlyph[gid] > max) max = perGlyph[gid];
            }
            fdSelect = CidFdSelect.FromPerGlyph(numGlyphs, max + 1, perGlyph, Location,
                (ulong)(uint)offset);
            diagnostic = default;
            return ReadStatus.Done;
        }
        int nRanges = ReadWire.U16(_data, offset + 1);
        var ranges = new CidFdRange[nRanges];
        int maxFd = 0;
        for (int i = 0; i < nRanges; i++)
        {
            int at = offset + 3 + i * CidLayout.FdSelectRangeStride;
            int first = ReadWire.U16(_data, at);
            int fd = _data[at + 2];
            if (fd > maxFd) maxFd = fd;
            ranges[i] = new CidFdRange(i, first, fd);
        }
        fdSelect = CidFdSelect.FromRanges(numGlyphs, maxFd + 1, ranges, Location,
            (ulong)(uint)offset);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Expands a custom charset into gid -&gt; SID. Validation already proved the
    /// coverage and bounds, so this only copies the values.</summary>
    private ReadStatus ReadCustomCharset(int offset, int numGlyphs, int sidBound,
        BuildBudget budget, int[] sids, out FontDiagnostic diagnostic)
    {
        int count = numGlyphs - 1;
        long pos = offset + 1;
        int format = _data[offset];
        sids[0] = 0;
        if (format == 0)
        {
            for (int i = 0; i < count; i++)
                sids[i + 1] = ReadWire.U16(_data, (int)pos + 2 * i);
            diagnostic = default;
            return ReadStatus.Done;
        }
        int stride = format == 1 ? 3 : 4;
        int glyph = 1;
        while (glyph < numGlyphs)
        {
            int first = ReadWire.U16(_data, (int)pos);
            int left = format == 1 ? _data[(int)pos + 2] : ReadWire.U16(_data, (int)pos + 2);
            for (int i = 0; i <= left && glyph < numGlyphs; i++, glyph++)
                sids[glyph] = first + i;
            pos += stride;
        }
        _ = sidBound;
        diagnostic = default;
        return ReadStatus.Done;
    }

    }
