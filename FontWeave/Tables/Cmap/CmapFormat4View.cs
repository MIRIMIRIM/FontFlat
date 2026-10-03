using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct CmapFormat4View
{
    /// <summary>Derived count; only meaningful after SegCountX2 passed the even/non-zero checks.</summary>
    public int SegmentCount => SegCountX2 / 2;

    internal long GlyphArrayStart => 16 + 8L * SegmentCount;

    /// <summary>Bounded local field reads; positions are subtable-relative and derived from SegCountX2.</summary>
    public ReadStatus TryGetEndCode(int ordinal, out ushort value, out FontDiagnostic diagnostic)
    {
        value = 0;
        ReadStatus status = BoundSegmentField(14, ordinal, out ulong position, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadEndCodeAt(position, ordinal, out var row, out diagnostic);
        if (status != ReadStatus.Done) return status;
        value = row.Value;
        return ReadStatus.Done;
    }

    public ReadStatus TryGetStartCode(int ordinal, out ushort value, out FontDiagnostic diagnostic)
    {
        value = 0;
        ReadStatus status = BoundSegmentField(16 + 2L * SegmentCount, ordinal, out ulong position, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadStartCodeAt(position, ordinal, out var row, out diagnostic);
        if (status != ReadStatus.Done) return status;
        value = row.Value;
        return ReadStatus.Done;
    }

    public ReadStatus TryGetIdDelta(int ordinal, out short value, out FontDiagnostic diagnostic)
    {
        value = 0;
        ReadStatus status = BoundSegmentField(16 + 4L * SegmentCount, ordinal, out ulong position, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadIdDeltaAt(position, ordinal, out var row, out diagnostic);
        if (status != ReadStatus.Done) return status;
        value = row.Value;
        return ReadStatus.Done;
    }

    public ReadStatus TryGetIdRangeOffset(int ordinal, out ushort value, out FontDiagnostic diagnostic)
    {
        value = 0;
        ReadStatus status = BoundSegmentField(16 + 6L * SegmentCount, ordinal, out ulong position, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadIdRangeOffsetAt(position, ordinal, out var row, out diagnostic);
        if (status != ReadStatus.Done) return status;
        value = row.Value;
        return ReadStatus.Done;
    }

    private ReadStatus BoundSegmentField(long start, int ordinal, out ulong position, out FontDiagnostic diagnostic)
    {
        position = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        int count = SegmentCount;
        if (ordinal < 0 || ordinal >= count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 6, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), (uint)count);
        position = (ulong)(start + 2L * ordinal);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Raw uint16 read inside the subtable; semantic addressing rules are enforced by Validate.</summary>
    internal ReadStatus TryReadGlyphIdAt(ulong position, out ushort value, out FontDiagnostic diagnostic)
    {
        value = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (position > (ulong)_data.Length - 2)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.GlyphId, position), out diagnostic,
                (ulong)_data.Length, position > ulong.MaxValue - 2 ? ulong.MaxValue : position + 2);
        value = ReadWire.U16(_data, (int)position);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Strict format 4 validation: structure, search fields, sentinel, segment ordering,
    /// self-relative glyph windows and every resolvable GID against numGlyphs.</summary>
    public ReadStatus Validate(bool requireZeroLanguage, int numGlyphs,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(requireZeroLanguage, numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(bool requireZeroLanguage, int numGlyphs,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (SegCountX2 == 0 || (SegCountX2 & 1) == 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 6), out diagnostic, SegCountX2, 2);
        int count = SegmentCount;
        if (count == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 6), out diagnostic, 0, 1);
        long glyphArrayStart = 16 + 8L * count;
        if (_data.Length < glyphArrayStart)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Length, 2), out diagnostic, (ulong)_data.Length, (ulong)glyphArrayStart);
        if ((_data.Length & 1) == 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, 2), out diagnostic, (uint)_data.Length,
                (uint)(_data.Length - 1));
        status = TryGetStartCode(0, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ushort reservedPad = ReadWire.U16(_data, (int)(14 + 2L * count));
        if (reservedPad != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, (ulong)(14 + 2L * count)), out diagnostic, reservedPad, 0);
        if (requireZeroLanguage && Language != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.LanguageId, 4), out diagnostic, Language, 0);
        if (!budget.Work(_data.Length + 32L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int power = 1, selector = 0;
        while (power <= count / 2) { power *= 2; selector++; }
        int expectedSearch = power * 2, expectedShift = count * 2 - expectedSearch;
        if (SearchRange != expectedSearch || EntrySelector != selector || RangeShift != expectedShift)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 8), out diagnostic,
                (uint)(SearchRange | (EntrySelector << 16)), (uint)(expectedSearch | (selector << 16)));
        int previousEnd = -1;
        long totalSpan = 0;
        for (int i = 0; i < count; i++)
        {
            status = TryGetEndCode(i, out ushort end, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = TryGetStartCode(i, out ushort start, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = TryGetIdDelta(i, out short delta, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = TryGetIdRangeOffset(i, out ushort rangeOffset, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var location = Location.AtLocal(DataField.CharCode, (ulong)(14 + 2L * count) + 2UL * (uint)i, i);
            if (start > end || (i > 0 && start <= previousEnd))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    location, out diagnostic, start, unchecked((uint)previousEnd));
            if (i == count - 1 && end != 0xFFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location, out diagnostic, end, 0xFFFF);
            totalSpan += end - start + 1;
            if (rangeOffset == 0)
            {
                status = ValidateDeltaArc(start, end, delta, numGlyphs, location, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            else
            {
                if ((rangeOffset & 1) == 1)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Offset, (ulong)(16 + 6L * count) + 2UL * (uint)i, i),
                        out diagnostic, rangeOffset, (uint)(rangeOffset - 1));
                ulong fieldPosition = (ulong)(16 + 6L * count + 2L * i);
                ulong windowStart = fieldPosition + rangeOffset;
                ulong windowEnd = windowStart + 2UL * (uint)(end - start);
                if (windowStart < (ulong)glyphArrayStart || windowEnd > (ulong)_data.Length - 2)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                        Location.AtLocal(DataField.Offset, fieldPosition, i), out diagnostic,
                        windowStart, (ulong)_data.Length - 2);
                for (uint c = start; ; c++)
                {
                    status = TryReadGlyphIdAt(windowStart + 2UL * (c - start), out ushort raw, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (raw != 0)
                    {
                        ushort mapped = unchecked((ushort)(raw + delta));
                        if (mapped >= numGlyphs)
                            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                                Location.AtLocal(DataField.GlyphId, windowStart + 2UL * (c - start), i),
                                out diagnostic, mapped, (uint)numGlyphs);
                    }
                    if (c == end) break;
                }
            }
            previousEnd = end;
        }
        if (!budget.Work(totalSpan, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus ValidateDeltaArc(ushort start, ushort end, short delta, int numGlyphs,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        // The image of c -> (c + delta) mod 65536 over [start,end]: monotone when the arc
        // stays inside [0,0xFFFF], shifted as a whole when it leaves on one side only,
        // and always containing GID 0xFFFF when it straddles a boundary (only numGlyphs
        // 65536 could admit that). GID 0 itself means "unmapped" and is harmless.
        int low = start + delta, high = end + delta;
        int max = low >= 0 && high <= 0xFFFF ? high
            : low > 0xFFFF ? high - 0x10000
            : high < 0 ? high + 0x10000
            : 0xFFFF;
        if (max >= numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location, out diagnostic, (uint)max, (uint)numGlyphs);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
