using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct Cff1View
{
    private readonly record struct SidSpan(long First, long Last, long Position);

    private readonly record struct CodeUse(int Code, long Position);

    /// <summary>Strict table validation (card section 1): header version rules, every
    /// INDEX's internal integrity, the strict Top DICT with name-keyed arity reversal
    /// and CID-key rejection, charset/Encoding expansion with exact glyph coverage and
    /// non-overlapping name spans, and the frozen top-level layout order Header, Name
    /// INDEX, Top DICT INDEX, String INDEX, Global Subr INDEX, then the Top DICT offset
    /// targets charset, Encoding, CharStrings, Private with its Local Subrs tiling the
    /// table exactly. Budget is charged as a structural lump sum plus expansion work.</summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return Validate(out diagnostic, budget);
    }

    internal ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (!IsBound)
            return Unbound(out diagnostic);
        byte major = _data[0], minor = _data[1], hdrSize = _data[2], offSize = _data[3];
        if (major != Cff1Layout.MajorVersion)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, 0), out diagnostic, major, Cff1Layout.MajorVersion);
        // minor 0 is the strict 1.0 form; minor > 0 is accepted as a wide read and the
        // fact is preserved on the materialized model (HeaderMinor).
        _ = minor;
        if (offSize is < 1 or > 4)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Length, 3), out diagnostic, offSize, 4);
        if (hdrSize < Cff1Layout.MinimumHeaderLength)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Length, 2), out diagnostic, hdrSize, 4);
        if (!budget.Work(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        ReadStatus status = LocateIndex(hdrSize, out var names, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (names.Count != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)names.Start), out diagnostic,
                (uint)names.Count, 1);
        status = LocateIndex(names.End, out var tops, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (tops.Count != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)tops.Start), out diagnostic,
                (uint)tops.Count, 1);
        status = LocateIndex(tops.End, out var strings, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocateIndex(strings.End, out var globals, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int sidBound = Cff1Layout.StandardStringCount + strings.Count;

        status = IndexItemBounds(tops, 0, out int dictStart, out int dictLength, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ParseDict(dictStart, dictLength, Cff1DictScope.Top, out var entries, out diagnostic);
        if (status != ReadStatus.Done) return status;

        // The ROS key is what makes a CFF1 font CID-keyed. The two forms are structurally
        // different (CID has a per-FD Private DICT and no Encoding), so the branch is taken
        // once here and the rest of this method handles the name-keyed form only.
        bool isCid = TryFindEntry(entries, Cff1Layout.OpRos, out _);
        if (isCid)
            return ValidateCid(entries, dictStart, strings, globals, budget, out diagnostic);
        // A CID-only key without ROS leaves the font in neither form: the CID structures
        // would be present but nothing would declare the character collection.
        foreach (var candidate in entries)
        {
            if (!Cff1Layout.IsCidOnlyOperator(candidate.Operator)) continue;
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Payload, (uint)candidate.EntryStart), out diagnostic,
                (uint)candidate.Operator, (uint)Cff1Layout.OpRos);
        }

        int charsetValue = 0, encodingValue = 0, charstringsAt = -1, privateSize = -1, privateOffset = -1;
        if (TryFindEntry(entries, Cff1Layout.OpCharset, out var entry))
        {
            status = EntryIntegers(entry!, out var operands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            charsetValue = operands[0];
        }
        if (TryFindEntry(entries, Cff1Layout.OpEncoding, out entry))
        {
            status = EntryIntegers(entry!, out var operands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            encodingValue = operands[0];
        }
        if (!TryFindEntry(entries, Cff1Layout.OpCharStrings, out entry))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Payload, (uint)dictStart), out diagnostic,
                (uint)Cff1Layout.OpCharStrings);
        status = EntryIntegers(entry!, out var charstringsOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        charstringsAt = charstringsOperands[0];
        if (!TryFindEntry(entries, Cff1Layout.OpPrivate, out entry))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Payload, (uint)dictStart), out diagnostic,
                (uint)Cff1Layout.OpPrivate);
        status = EntryIntegers(entry!, out var privateOperands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (privateOperands.Length != 2)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)entry!.EntryStart), out diagnostic,
                (uint)privateOperands.Length, 2);
        privateSize = privateOperands[0];
        privateOffset = privateOperands[1];
        if (charstringsAt < 0 || charstringsAt > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)(uint)Math.Max(0, charstringsAt)), out diagnostic,
                (uint)Math.Max(0, charstringsAt), (ulong)_data.Length);
        if (privateSize < 0 || privateOffset < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Offset, (uint)entry!.EntryStart), out diagnostic,
                (ulong)Math.Min(privateSize, privateOffset), 0);

        foreach (var candidate in entries)
        {
            if (!Cff1Layout.IsSidOperator(candidate.Operator))
                continue;
            status = EntryIntegers(candidate, out var operands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int sid = operands[0];
            if (sid < 0 || sid >= sidBound)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.At(DataField.Cid, (uint)candidate.OperandStart), out diagnostic,
                    (uint)sid, (uint)sidBound);
        }
        if (TryFindEntry(entries, Cff1Layout.OpCharstringType, out entry))
        {
            status = EntryIntegers(entry!, out var operands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (operands[0] != 2)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, (uint)entry!.EntryStart), out diagnostic,
                    (uint)operands[0], 2);
        }

        status = LocateIndex(charstringsAt, out var charstrings, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int numGlyphs = charstrings.Count;
        if (numGlyphs < 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)charstringsAt), out diagnostic, 0, 1);

        int charsetEnd = globals.End;
        if (charsetValue is 0 or 1 or 2)
        {
            if (!budget.Work(2L * numGlyphs, Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            if (numGlyphs > Cff1Predefined.CharsetLength(charsetValue))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Count, (uint)globals.End, 0), out diagnostic,
                    (uint)numGlyphs, (uint)(Cff1Predefined.CharsetLength(charsetValue) + 1));
        }
        else
        {
            if (charsetValue < globals.End)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                    Location.At(DataField.Offset, (uint)charsetValue), out diagnostic,
                    (uint)charsetValue, (uint)globals.End);
            status = ValidateCustomCharset(charsetValue, globals.End, numGlyphs, sidBound,
                budget, out charsetEnd, out diagnostic, requireTiling: false);
            if (status != ReadStatus.Done) return status;
            if (charsetValue > globals.End && !IsZeroPadding(globals.End, charsetValue))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                    Location.At(DataField.Offset, (uint)globals.End), out diagnostic,
                    (uint)charsetValue, (uint)globals.End);
        }

        int encodingEnd = charsetEnd;
        if (encodingValue is not (0 or 1))
        {
            status = ValidateCustomEncoding(encodingValue, charsetEnd, numGlyphs, sidBound,
                budget, out encodingEnd, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (charstringsAt != encodingEnd)
        {
            if (charstringsAt < encodingEnd)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                    Location.At(DataField.Offset, (uint)charstringsAt), out diagnostic,
                    (uint)charstringsAt, (uint)encodingEnd);
            if (!IsZeroPadding(encodingEnd, charstringsAt))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                    Location.At(DataField.Offset, (uint)encodingEnd), out diagnostic,
                    (uint)charstringsAt, (uint)encodingEnd);
        }

        if ((long)privateOffset + privateSize > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Length, (ulong)(uint)(privateOffset + privateSize)), out diagnostic,
                (ulong)((long)privateOffset + privateSize), (ulong)_data.Length);
        if (privateOffset != charstrings.End)
        {
            if (privateOffset < charstrings.End)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                    Location.At(DataField.Offset, (uint)privateOffset), out diagnostic,
                    (uint)privateOffset, (uint)charstrings.End);
            if (!IsZeroPadding(charstrings.End, privateOffset))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                    Location.At(DataField.Offset, (uint)charstrings.End), out diagnostic,
                    (uint)privateOffset, (uint)charstrings.End);
        }
        status = ParseDict(privateOffset, privateSize, Cff1DictScope.Private, out var privateEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;

        int blockEnd = privateOffset + privateSize;
        if (TryFindEntry(privateEntries, Cff1Layout.OpSubrs, out entry))
        {
            status = EntryIntegers(entry!, out var operands, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int subrsOffset = operands[0];
            if (subrsOffset < 0)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.At(DataField.Offset, (uint)entry!.OperandStart), out diagnostic,
                    unchecked((ulong)(long)subrsOffset), (ulong)_data.Length);
            if (subrsOffset != privateSize)
            {
                if (subrsOffset < privateSize)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.OverlappingRange,
                        Location.At(DataField.Offset, (uint)(privateOffset + subrsOffset)), out diagnostic,
                        (uint)subrsOffset, (uint)privateSize);
                if ((long)privateOffset + subrsOffset > _data.Length)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                        Location.At(DataField.Offset, (uint)Math.Max(0, subrsOffset)), out diagnostic,
                        (ulong)((long)privateOffset + subrsOffset), (ulong)_data.Length);
                if (!IsZeroPadding(blockEnd, privateOffset + subrsOffset))
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                        Location.At(DataField.Offset, (uint)blockEnd), out diagnostic,
                        (uint)Math.Max(0, subrsOffset), (uint)privateSize);
                blockEnd = privateOffset + subrsOffset;
            }
            status = LocateIndex(blockEnd, out var local, out diagnostic);
            if (status != ReadStatus.Done) return status;
            blockEnd = local.End;
        }
        if (blockEnd > _data.Length ||
            (blockEnd < _data.Length && !IsZeroPadding(blockEnd, _data.Length)))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, (uint)blockEnd), out diagnostic,
                (ulong)_data.Length, (uint)blockEnd);

        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Zero alignment padding is legal between CFF structures and at the table
    /// tail; the tiling rules forbid overlap and non-zero fill, not zero fill.</summary>
    private bool IsZeroPadding(int start, int end) =>
        start >= end || !_data.Slice(start, end - start).ContainsAnyExcept((byte)0);

    /// <summary>Custom charset strict rules: tiling position, format byte, exact
    /// nGlyphs-1 coverage, SID bounds and non-overlapping (unique) name spans. Range
    /// SID order is deliberately not required; disjointness is checked by sorting spans.</summary>
    private ReadStatus ValidateCustomCharset(int offset, int expectedStart, int numGlyphs,
        int sidBound, BuildBudget budget, out int blockEnd, out FontDiagnostic diagnostic,
        bool requireTiling = true)
    {
        blockEnd = 0;
        // A negative offset is refused before it can index the table. The tiling branch below
        // compares against expectedStart, but the non-tiling caller (the CID charset) has no
        // such comparison, so the guard has to live here.
        if (offset < 0)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, 0), out diagnostic,
                unchecked((ulong)(long)offset), (ulong)_data.Length);
        if (offset > _data.Length - 1)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (uint)offset), out diagnostic,
                (uint)offset, (ulong)_data.Length);
        if (requireTiling && offset != expectedStart)
        {
            if (offset < expectedStart)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                    Location.At(DataField.Offset, (uint)offset), out diagnostic,
                    (uint)offset, (uint)expectedStart);
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Offset, (uint)expectedStart), out diagnostic,
                (uint)offset, (uint)expectedStart);
        }
        int format = _data[offset];
        if (format > 2)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, (uint)offset), out diagnostic, (uint)format, 2);
        int count = numGlyphs - 1;
        if (!budget.Work(2L * numGlyphs + 8L * count, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (!budget.Buffer(24L * Math.Max(count, 1) + 16, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long pos = offset + 1;
        var spans = new SidSpan[Math.Max(count, 1)];
        int spanCount = 0;
        if (format == 0)
        {
            if (pos + 2L * count > _data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.Count, (uint)offset), out diagnostic,
                    (ulong)_data.Length, (ulong)(pos + 2L * count));
            for (int i = 0; i < count; i++)
            {
                int sid = ReadWire.U16(_data, (int)pos + 2 * i);
                if (sid >= sidBound)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.At(DataField.Cid, (ulong)(uint)(pos + 2 * i)), out diagnostic,
                        (uint)sid, (uint)sidBound);
                spans[spanCount++] = new SidSpan(sid, sid, pos + 2 * i);
            }
            pos += 2L * count;
        }
        else
        {
            int stride = format == 1 ? 3 : 4;
            int covered = 0;
            while (covered < count)
            {
                if (pos + stride > _data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.At(DataField.Count, (uint)offset), out diagnostic,
                        (ulong)_data.Length, (ulong)(pos + stride));
                int first = ReadWire.U16(_data, (int)pos);
                int left = format == 1 ? _data[(int)pos + 2] : ReadWire.U16(_data, (int)pos + 2);
                int remaining = count - covered;
                if (left >= remaining)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Count, (uint)pos), out diagnostic,
                        (uint)left, (uint)(remaining - 1));
                if (first + left > 0xFFFF)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Cid, (uint)pos), out diagnostic,
                        (uint)(first + left), 0xFFFF);
                if (first + left >= sidBound)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.At(DataField.Cid, (uint)pos), out diagnostic,
                        (uint)(first + left), (uint)sidBound);
                spans[spanCount++] = new SidSpan(first, first + left, pos);
                covered += left + 1;
                pos += stride;
            }
        }
        if (spanCount > 1)
        {
            Array.Sort(spans, 0, spanCount, SidSpanOrder.Instance);
            for (int i = 1; i < spanCount; i++)
            {
                if (spans[i].First <= spans[i - 1].Last)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                        Location.At(DataField.Cid, (uint)spans[i].Position), out diagnostic,
                        (uint)spans[i].First, (uint)(spans[i - 1].Last + 1));
            }
        }
        blockEnd = (int)pos;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Custom Encoding strict rules: tiling position, format 0/1 with u8
    /// counts, positional format 0 codes with 0 as the unmapped marker, consecutive
    /// format 1 glyph assignment, unique nonzero code positions, glyph bounds, and
    /// strictly read supplements whose u16 field is a SID within the string space.</summary>
    private ReadStatus ValidateCustomEncoding(int offset, int expectedStart, int numGlyphs,
        int sidBound, BuildBudget budget, out int blockEnd, out FontDiagnostic diagnostic)
    {
        blockEnd = 0;
        if (offset > _data.Length - 1)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (uint)offset), out diagnostic,
                (uint)offset, (ulong)_data.Length);
        if (offset != expectedStart)
        {
            if (offset < expectedStart)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                    Location.At(DataField.Offset, (uint)offset), out diagnostic,
                    (uint)offset, (uint)expectedStart);
            if (!IsZeroPadding(expectedStart, offset))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                    Location.At(DataField.Offset, (uint)expectedStart), out diagnostic,
                    (uint)offset, (uint)expectedStart);
        }
        int format = _data[offset];
        int low = format & 0x7f;
        if (low > 1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, (uint)offset), out diagnostic, (uint)format, 1);
        if (!budget.Work(8L * 768, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (!budget.Buffer(24L * 768, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long pos = offset + 1;
        if (pos >= _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, (uint)offset), out diagnostic,
                (ulong)_data.Length, (ulong)(pos + 1));
        var uses = new CodeUse[512];
        int useCount = 0;
        SidSpan[] rangeSpans;
        int rangeCount = 0;
        if (low == 0)
        {
            int nCodes = _data[(int)pos++];
            if (nCodes >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.At(DataField.GlyphId, (ulong)(uint)(pos - 1)), out diagnostic,
                    (uint)nCodes, (uint)numGlyphs);
            if (pos + nCodes > _data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.CharCode, (uint)offset), out diagnostic,
                    (ulong)_data.Length, (ulong)(pos + nCodes));
            for (int i = 0; i < nCodes; i++)
            {
                int code = _data[(int)pos + i];
                if (code != 0)
                    uses[useCount++] = new CodeUse(code, pos + i);
            }
            pos += nCodes;
            rangeSpans = [];
        }
        else
        {
            int nRanges = _data[(int)pos++];
            rangeSpans = new SidSpan[nRanges];
            if (pos + 2L * nRanges > _data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.CharCode, (uint)offset), out diagnostic,
                    (ulong)_data.Length, (ulong)(pos + 2L * nRanges));
            long glyphId = 1;
            for (int r = 0; r < nRanges; r++)
            {
                int first = _data[(int)pos];
                int nLeft = _data[(int)pos + 1];
                if (first == 0)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.CharCode, (ulong)(uint)pos), out diagnostic, 0, 1);
                if (first + nLeft > 0xFF)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.CharCode, (ulong)(uint)pos), out diagnostic,
                        (uint)(first + nLeft), 0xFF);
                if (glyphId + nLeft >= numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.At(DataField.GlyphId, (ulong)(uint)pos), out diagnostic,
                        (uint)(glyphId + nLeft), (uint)numGlyphs);
                rangeSpans[rangeCount++] = new SidSpan(first, first + nLeft, pos);
                glyphId += nLeft + 1;
                pos += 2;
            }
            if (rangeCount > 1)
            {
                Array.Sort(rangeSpans, 0, rangeCount, SidSpanOrder.Instance);
                for (int i = 1; i < rangeCount; i++)
                {
                    if (rangeSpans[i].First <= rangeSpans[i - 1].Last)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                            Location.At(DataField.CharCode, (uint)rangeSpans[i].Position), out diagnostic,
                            (uint)rangeSpans[i].First, (uint)(rangeSpans[i - 1].Last + 1));
                }
            }
        }
        if ((format & 0x80) != 0)
        {
            if (pos >= _data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.Count, (uint)offset), out diagnostic,
                    (ulong)_data.Length, (ulong)(pos + 1));
            int nSups = _data[(int)pos++];
            if (pos + 3L * nSups > _data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.CharCode, (uint)offset), out diagnostic,
                    (ulong)_data.Length, (ulong)(pos + 3L * nSups));
            for (int s = 0; s < nSups; s++)
            {
                int code = _data[(int)pos + 3 * s];
                int sid = ReadWire.U16(_data, (int)pos + 3 * s + 1);
                if (code == 0)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.CharCode, (ulong)(uint)(pos + 3 * s)), out diagnostic, 0, 1);
                if (sid >= sidBound)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.At(DataField.Cid, (ulong)(uint)(pos + 3 * s + 1)), out diagnostic,
                        (uint)sid, (uint)sidBound);
                uses[useCount++] = new CodeUse(code, pos + 3 * s);
            }
            pos += 3L * nSups;
        }
        if (useCount > 1)
        {
            Array.Sort(uses, 0, useCount, CodeUseOrder.Instance);
            for (int i = 1; i < useCount; i++)
            {
                if (uses[i].Code == uses[i - 1].Code)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                        Location.At(DataField.CharCode, (uint)uses[i].Position), out diagnostic,
                        (uint)uses[i].Code, (uint)uses[i].Code);
            }
        }
        if (rangeCount > 0 && useCount > 0)
        {
            for (int i = 0; i < useCount; i++)
            {
                int index = Array.BinarySearch(rangeSpans, 0, rangeCount,
                    new SidSpan(uses[i].Code, 0, 0), SidSpanOrder.Instance);
                long hit = index >= 0 ? index : ~index - 1;
                if (hit >= 0 && uses[i].Code >= rangeSpans[hit].First
                    && uses[i].Code <= rangeSpans[hit].Last)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                        Location.At(DataField.CharCode, (uint)uses[i].Position), out diagnostic,
                        (uint)uses[i].Code, (uint)rangeSpans[hit].First);
            }
        }
        blockEnd = (int)pos;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private sealed class SidSpanOrder : IComparer<SidSpan>
    {
        internal static readonly SidSpanOrder Instance = new();
        public int Compare(SidSpan x, SidSpan y) => x.First.CompareTo(y.First);
    }

    private sealed class CodeUseOrder : IComparer<CodeUse>
    {
        internal static readonly CodeUseOrder Instance = new();
        public int Compare(CodeUse x, CodeUse y) => x.Code.CompareTo(y.Code);
    }
}
