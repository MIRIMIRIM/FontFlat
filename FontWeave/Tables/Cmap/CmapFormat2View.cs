using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct CmapFormat2View
{
    /// <summary>Derived subHeader count: max(subHeaderKeys)/8 + 1, 1 when all keys are 0.
    /// Best effort before Validate: keys beyond the bound bytes are ignored, so the exact
    /// structure is only guaranteed once Validate passed. Unsupported formats have no
    /// readable keys and return 1.</summary>
    public int SubHeaderCount
    {
        get
        {
            uint maxKey = 0;
            for (int i = 0; i < CmapFormat2Layout.KeyCount; i++)
            {
                if (TryGetSubHeaderKey(i, out ushort key, out _) != ReadStatus.Done) break;
                if (key > maxKey) maxKey = key;
            }
            return (int)(maxKey / 8) + 1;
        }
    }

    /// <summary>Bounded local key read; positions are subtable-relative (6 + 2*byteIndex).</summary>
    public ReadStatus TryGetSubHeaderKey(int byteIndex, out ushort value, out FontDiagnostic diagnostic)
    {
        value = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (byteIndex < 0 || byteIndex >= CmapFormat2Layout.KeyCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Offset, CmapFormat2Layout.HeaderLength, byteIndex), out diagnostic,
                unchecked((ulong)(long)byteIndex), CmapFormat2Layout.KeyCount);
        ReadStatus status = TryReadSubHeaderKeyAt(
            CmapFormat2Layout.HeaderLength + 2UL * (uint)byteIndex, byteIndex,
            out var key, out diagnostic);
        if (status != ReadStatus.Done) return status;
        value = key.Value;
        return ReadStatus.Done;
    }

    /// <summary>Bounded local subHeader read; positions are subtable-relative (518 + 8*ordinal).
    /// The count is derived from the keys, so a truncated key array bounds it down.</summary>
    public ReadStatus TryGetSubHeader(int ordinal, out SubHeaderData header, out FontDiagnostic diagnostic)
    {
        header = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        int count = SubHeaderCount;
        if (ordinal < 0 || ordinal >= count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.None, CmapFormat2Layout.KeysEnd, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), (uint)count);
        return TryReadSubHeaderAt(CmapFormat2Layout.KeysEnd + 8UL * (uint)ordinal, ordinal,
            out header, out diagnostic);
    }

    /// <summary>Raw uint16 read inside the subtable; semantic addressing rules are enforced by Validate.</summary>
    public ReadStatus TryReadGlyphAt(ulong position, out ushort value, out FontDiagnostic diagnostic)
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

    /// <summary>Strict format 2 validation: exact tiled length over the derived subHeader
    /// structure, 8-aligned keys, per-subHeader window placement rules, the language rule and
    /// every resolvable GID of referenced subHeaders against numGlyphs. Unreferenced subHeaders
    /// get structural checks only. The public form applies the strict non-Mac language rule;
    /// the table dispatch passes the Mac-only exemption.</summary>
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(requireZeroLanguage: true, numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(bool requireZeroLanguage, int numGlyphs, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (_data.Length < CmapFormat2Layout.KeysEnd)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Length, 2), out diagnostic, (ulong)_data.Length,
                CmapFormat2Layout.KeysEnd);
        uint maxKey = 0;
        for (int i = 0; i < CmapFormat2Layout.KeyCount; i++)
        {
            status = TryGetSubHeaderKey(i, out ushort key, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if ((key & 0x7) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, CmapFormat2Layout.HeaderLength + 2UL * (uint)i, i),
                    out diagnostic, key, (ulong)key & ~7UL);
            if (key > maxKey) maxKey = key;
        }
        int count = (int)(maxKey / 8) + 1;
        long glyphArrayStart = CmapFormat2Layout.KeysEnd + 8L * count;
        if (_data.Length < glyphArrayStart)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.AtLocal(DataField.Length, 2), out diagnostic, (ulong)_data.Length, (ulong)glyphArrayStart);
        if (((_data.Length - glyphArrayStart) & 1) == 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, 2), out diagnostic, (uint)_data.Length,
                (uint)(_data.Length - 1));
        if (requireZeroLanguage && Language != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.LanguageId, 4), out diagnostic, Language, 0);
        if (!budget.Work(_data.Length + 8L * count + 2L * CmapFormat2Layout.KeyCount, Location, out diagnostic) ||
            !budget.Buffer(count, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var referenced = new bool[count];
        for (int b = 0; b < CmapFormat2Layout.KeyCount; b++)
        {
            _ = TryGetSubHeaderKey(b, out ushort key, out _);
            if (key == 0)
                referenced[0] = true; // any zero key opens the single-byte path
            else
                referenced[key / 8] = true;
        }
        long span = 0;
        for (int k = 0; k < count; k++)
        {
            status = TryGetSubHeader(k, out var header, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong fieldAddress = (ulong)(CmapFormat2Layout.KeysEnd + 8L * k);
            if (header.FirstCode + (uint)header.EntryCount > 256)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.CharCode, fieldAddress, k), out diagnostic,
                    (uint)header.FirstCode + header.EntryCount, 256);
            if (header.EntryCount == 0)
            {
                if (header.IdRangeOffset != 0)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Offset, fieldAddress + 6, k), out diagnostic,
                        header.IdRangeOffset, 0);
                continue;
            }
            if (header.IdRangeOffset == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, fieldAddress + 6, k), out diagnostic, 0, 1);
            if ((header.IdRangeOffset & 1) == 1)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, fieldAddress + 6, k), out diagnostic,
                    header.IdRangeOffset, (uint)(header.IdRangeOffset - 1));
            ulong windowStart = fieldAddress + 6 + header.IdRangeOffset;
            ulong windowEnd = windowStart + 2UL * (uint)(header.EntryCount - 1);
            if (windowStart < (ulong)glyphArrayStart || windowEnd > (ulong)_data.Length - 2)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.AtLocal(DataField.Offset, fieldAddress + 6, k), out diagnostic,
                    windowStart, (ulong)_data.Length - 2);
            if (!referenced[k]) continue;
            span += header.EntryCount;
            for (int i = 0; i < header.EntryCount; i++)
            {
                status = TryReadGlyphAt(windowStart + 2UL * (uint)i, out ushort raw, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (raw == 0) continue;
                ushort mapped = unchecked((ushort)(raw + header.IdDelta));
                if (mapped >= (uint)numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.GlyphId, windowStart + 2UL * (uint)i, k),
                        out diagnostic, mapped, (uint)numGlyphs);
            }
        }
        if (!budget.Work(span, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
