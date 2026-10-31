using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct Cff1View
{
    /// <summary>Materializes the validated table into the owned Cff1Table model:
    /// name bytes, Top DICT entries (integers plus verbatim real bytes), string INDEX,
    /// Global/Local Subr items, expanded charset SIDs (predefined forms expand from
    /// the embedded standard tables), expanded encoding codes with supplement pairs,
    /// Private DICT entries and per-glyph charstring bytes. Charstrings and subroutines
    /// are preserved byte-for-byte and never interpreted.</summary>
    public ReadStatus TryRead(out Cff1Table? table, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        table = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = TryRead(out table, out var cid, out _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // This entry point is the name-keyed reader. A CID-keyed font is a different shape,
        // so it is reported as such rather than forced into a model that cannot hold it.
        if (cid is not null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation,
                Location.At(DataField.Payload, 0), out diagnostic, 0, 1);
        return ReadStatus.Done;
    }

    /// <summary>Reads whichever CFF1 form the table is, reporting which one it was.
    ///
    /// A CID-keyed font has no top-level Private DICT, no Encoding and no glyph names, so one
    /// result type cannot honestly carry both. Exactly one of the two outputs is non-null on
    /// success; <paramref name="flavor"/> says which, so a caller cannot mistake one form for
    /// the other.</summary>
    public ReadStatus TryRead(out Cff1Table? nameKeyed, out CidCff1Table? cidKeyed,
        out Cff1Flavor flavor, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        nameKeyed = null;
        cidKeyed = null;
        flavor = Cff1Flavor.NameKeyed;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return TryRead(out nameKeyed, out cidKeyed, out flavor, out diagnostic, budget);
    }

    /// <summary>Reads a CID-keyed table specifically. A name-keyed table is refused rather
    /// than silently reinterpreted, so a caller that needs CID data cannot accept a
    /// name-keyed font by accident.</summary>
    public ReadStatus TryReadCid(out CidCff1Table? table, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        table = null;
        ReadStatus status = TryRead(out _, out var cid, out var flavor, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (cid is null || flavor != Cff1Flavor.CidKeyed)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation,
                Location.At(DataField.Payload, 0), out diagnostic, 0, 1);
        table = cid;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus TryRead(out Cff1Table? table, out CidCff1Table? cidKeyed,
        out Cff1Flavor flavor, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        table = null;
        cidKeyed = null;
        flavor = Cff1Flavor.NameKeyed;
        if (!IsBound)
            return Unbound(out diagnostic);
        ReadStatus status = Validate(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;

        byte hdrSize = _data[2];
        status = LocateIndexes(out var names, out var tops, out var strings, out var globals, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int sidBound = Cff1Layout.StandardStringCount + strings.Count;

        status = IndexItemBounds(tops, 0, out int dictStart, out int dictLength, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ParseDict(dictStart, dictLength, Cff1DictScope.Top, out var dictEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // CID-keyed fonts have a different top-level shape, so they materialize through the
        // CID path. The shared validation above already proved the table is well formed.
        if (TryFindEntry(dictEntries, Cff1Layout.OpRos, out _))
        {
            status = TryReadCid(strings, dictEntries, budget, out cidKeyed, out diagnostic);
            if (status != ReadStatus.Done) return status;
            flavor = Cff1Flavor.CidKeyed;
            diagnostic = default;
            return ReadStatus.Done;
        }
        _ = TryFindEntry(dictEntries, Cff1Layout.OpCharset, out var charsetEntry);
        _ = TryFindEntry(dictEntries, Cff1Layout.OpEncoding, out var encodingEntry);
        _ = TryFindEntry(dictEntries, Cff1Layout.OpCharStrings, out var charstringsEntry);
        _ = TryFindEntry(dictEntries, Cff1Layout.OpPrivate, out var privateEntry);
        status = EntryIntegers(charstringsEntry!, out var charstringsOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = EntryIntegers(privateEntry!, out var privateOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int charsetValue = charsetEntry is null ? 0 : charsetEntry.Operands[0]!.Value;
        int encodingValue = encodingEntry is null ? 0 : encodingEntry.Operands[0]!.Value;
        int privateSize = privateOperands[0], privateOffset = privateOperands[1];

        status = LocateIndex(charstringsOperands[0], out var charstrings, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int numGlyphs = charstrings.Count;

        status = ParseDict(privateOffset, privateSize, Cff1DictScope.Private, out var privateEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        bool hasLocalSubrs = TryFindEntry(privateEntries, Cff1Layout.OpSubrs, out var subrsEntry);
        IndexSpan local = default;
        if (hasLocalSubrs)
        {
            // The Subrs operand is relative to the Private DICT start and may point
            // past its declared size (zero alignment fill between the two). Validate
            // already proved the target; TryRead must use the same target or it would
            // parse the padding as an empty INDEX and silently drop the subrs.
            status = EntryIntegers(subrsEntry!, out var subrsOperands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int subrsOffset = subrsOperands[0];
            if (subrsOffset < 0 || (long)privateOffset + subrsOffset > _data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.At(DataField.Offset, (uint)subrsEntry!.OperandStart), out diagnostic,
                    unchecked((ulong)(long)subrsOffset), (ulong)_data.Length);
            status = LocateIndex(privateOffset + subrsOffset, out local, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }

        long items = names.Count + strings.Count + globals.Count + numGlyphs
            + (hasLocalSubrs ? local.Count : 0);
        long itemBytes = (long)dictLength + privateSize
            + ItemBytes(in names) + ItemBytes(in strings) + ItemBytes(in globals)
            + ItemBytes(in charstrings) + (hasLocalSubrs ? ItemBytes(in local) : 0);
        if (!budget.Buffer(itemBytes + 8L * items + 16L * numGlyphs, Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        status = ReadIndexItem(names, 0, out var nameBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var name = new byte[nameBytes.Length];
        nameBytes.CopyTo(name);

        status = MaterializeEntries(dictEntries, out var topDict, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MaterializeItems(in strings, out var stringItems, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MaterializeItems(in globals, out var globalItems, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MaterializeItems(in charstrings, out var charItems, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MaterializeEntries(privateEntries, out var privateDict, out diagnostic);
        if (status != ReadStatus.Done) return status;
        byte[][]? localItems = [];
        if (hasLocalSubrs)
        {
            status = MaterializeItems(in local, out localItems, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }

        status = MaterializeCharset(charsetValue, numGlyphs, out var charsetForm,
            out var charsetSids, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MaterializeEncoding(encodingValue, numGlyphs, out var encodingForm,
            out var encodingCodes, out var supplements, out diagnostic);
        if (status != ReadStatus.Done) return status;

        table = new Cff1Table(major: _data[0], minor: _data[1], hdrSize: hdrSize, name,
            topDict, stringItems, globalItems, localItems!, hasLocalSubrs, privateDict,
            charsetForm, charsetSids, encodingForm, encodingCodes, supplements, charItems,
            Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private long ItemBytes(in IndexSpan index)
    {
        long total = 0;
        for (int i = 0; i < index.Count; i++)
        {
            long startOffset = ReadOffsetAt(index.OffsetsAt + (long)i * index.OffSize, index.OffSize);
            long endOffset = ReadOffsetAt(index.OffsetsAt + (long)(i + 1) * index.OffSize, index.OffSize);
            total += endOffset - startOffset;
        }
        return total;
    }

    private ReadStatus MaterializeItems(in IndexSpan index, out byte[][] items, out FontDiagnostic diagnostic)
    {
        items = new byte[index.Count][];
        for (int i = 0; i < index.Count; i++)
        {
            ReadStatus status = ReadIndexItem(index, i, out var bytes, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var copy = new byte[bytes.Length];
            bytes.CopyTo(copy);
            items[i] = copy;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus MaterializeEntries(List<DictEntryData> entries, out Cff1DictEntry[] result,
        out FontDiagnostic diagnostic)
    {
        result = new Cff1DictEntry[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            var source = entries[i];
            var operands = new Cff1Operand[source.Operands.Length];
            int realOrdinal = 0;
            for (int k = 0; k < source.Operands.Length; k++)
            {
                int? operand = source.Operands[k];
                if (operand is int value)
                {
                    operands[k] = new Cff1Operand(value, null);
                }
                else
                {
                    int offset = source.RealOffsets[realOrdinal];
                    int length = source.RealLengths[realOrdinal];
                    realOrdinal++;
                    var bytes = new byte[length];
                    _data.Slice(offset, length).CopyTo(bytes);
                    operands[k] = new Cff1Operand(0, bytes);
                }
            }
            var raw = new byte[source.EntryEnd - source.EntryStart];
            _data.Slice(source.EntryStart, raw.Length).CopyTo(raw);
            result[i] = new Cff1DictEntry(source.Operator, operands, raw);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus MaterializeCharset(int charsetValue, int numGlyphs,
        out Cff1CharsetForm form, out int[] sids, out FontDiagnostic diagnostic)
    {
        sids = new int[numGlyphs];
        if (charsetValue is 0 or 1 or 2)
        {
            var table = Cff1Predefined.Charset(charsetValue);
            form = new Cff1CharsetForm(Predefined: true, Format: charsetValue);
            for (int gid = 0; gid < numGlyphs; gid++)
                sids[gid] = table[gid];
            diagnostic = default;
            return ReadStatus.Done;
        }
        form = new Cff1CharsetForm(Predefined: false, Format: _data[charsetValue]);
        int format = form.Format;
        sids[0] = 0;
        long pos = charsetValue + 1;
        if (format == 0)
        {
            for (int gid = 1; gid < numGlyphs; gid++)
            {
                sids[gid] = ReadWire.U16(_data, (int)pos);
                pos += 2;
            }
        }
        else
        {
            int stride = format == 1 ? 3 : 4;
            int gid = 1;
            while (gid < numGlyphs)
            {
                int first = ReadWire.U16(_data, (int)pos);
                int left = format == 1 ? _data[(int)pos + 2] : ReadWire.U16(_data, (int)pos + 2);
                for (int j = 0; j <= left; j++)
                    sids[gid++] = first + j;
                pos += stride;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus MaterializeEncoding(int encodingValue, int numGlyphs,
        out Cff1EncodingForm form, out int[] codes, out Cff1EncodingSupplement[] supplements,
        out FontDiagnostic diagnostic)
    {
        codes = new int[numGlyphs];
        supplements = [];
        if (encodingValue is 0 or 1)
        {
            form = new Cff1EncodingForm(Predefined: true, Format: encodingValue, HasSupplement: false);
            diagnostic = default;
            return ReadStatus.Done;
        }
        int format = _data[encodingValue];
        form = new Cff1EncodingForm(Predefined: false, Format: format & 0x7f,
            HasSupplement: (format & 0x80) != 0);
        long pos = encodingValue + 1;
        if (form.Format == 0)
        {
            int nCodes = _data[(int)pos++];
            for (int i = 0; i < nCodes; i++)
            {
                int code = _data[(int)pos + i];
                if (code != 0)
                    codes[i + 1] = code;
            }
            pos += nCodes;
        }
        else
        {
            int nRanges = _data[(int)pos++];
            long glyphId = 1;
            for (int r = 0; r < nRanges; r++)
            {
                int first = _data[(int)pos];
                int nLeft = _data[(int)pos + 1];
                for (int c = 0; c <= nLeft; c++)
                    codes[glyphId++] = first + c;
                pos += 2;
            }
        }
        if (form.HasSupplement)
        {
            int nSups = _data[(int)pos++];
            supplements = new Cff1EncodingSupplement[nSups];
            for (int s = 0; s < nSups; s++)
            {
                supplements[s] = new Cff1EncodingSupplement(_data[(int)pos],
                    ReadWire.U16(_data, (int)pos + 1));
                pos += 3;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
