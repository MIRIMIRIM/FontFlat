using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct CmapFormat14View
{
    /// <summary>Bounded record read: the u24 varSelector plus the two declared u32 table offsets.</summary>
    public ReadStatus TryGetVarSelector(int ordinal, out uint varSelector, out uint defaultUvsOffset,
        out uint nonDefaultUvsOffset, out FontDiagnostic diagnostic)
    {
        varSelector = 0; defaultUvsOffset = 0; nonDefaultUvsOffset = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (ordinal < 0 || (uint)ordinal >= NumVarSelectorRecords)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, 6, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), NumVarSelectorRecords);
        ulong position = CmapFormat14Layout.HeaderLength + CmapFormat14Layout.RecordStride * (ulong)(uint)ordinal;
        ReadStatus status = TryReadU24At(position, out varSelector, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadVarSelectorAt(position, ordinal, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        defaultUvsOffset = record.DefaultUVSOffset;
        nonDefaultUvsOffset = record.NonDefaultUVSOffset;
        return ReadStatus.Done;
    }

    /// <summary>Bounded row count of the default-UVS table referenced by one record;
    /// ReadStatus.NotFound reports a record without a default table.</summary>
    public ReadStatus TryGetDefaultRangeCount(int recordOrdinal, out int count, out FontDiagnostic diagnostic)
    {
        count = 0;
        ReadStatus status = TryGetUvsTablePosition(recordOrdinal, true, out ulong tablePosition, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadU32At(tablePosition, out uint raw, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (raw > int.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, tablePosition, recordOrdinal), out diagnostic, raw, int.MaxValue);
        count = (int)raw;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Bounded row read of one {startUnicodeValue u24, additionalCount u8} range.</summary>
    public ReadStatus TryGetDefaultRange(int recordOrdinal, int rangeOrdinal,
        out uint startUnicodeValue, out byte additionalCount, out FontDiagnostic diagnostic)
    {
        startUnicodeValue = 0; additionalCount = 0;
        ReadStatus status = TryGetUvsTablePosition(recordOrdinal, true, out ulong tablePosition, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadU32At(tablePosition, out uint count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (rangeOrdinal < 0 || (ulong)rangeOrdinal >= count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, tablePosition, recordOrdinal), out diagnostic,
                unchecked((ulong)(long)rangeOrdinal), count);
        ulong position = tablePosition + 4 + CmapFormat14Layout.DefaultRangeStride * (ulong)(uint)rangeOrdinal;
        status = TryReadU24At(position, out startUnicodeValue, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Preserve the local field diagnostic's unspecified record ordinal.
        status = TryReadDefaultRangeAt(position, -1, out var range, out diagnostic);
        if (status != ReadStatus.Done) return status;
        additionalCount = range.AdditionalCount;
        return ReadStatus.Done;
    }

    /// <summary>Bounded row count of the non-default-UVS table referenced by one record;
    /// ReadStatus.NotFound reports a record without a non-default table.</summary>
    public ReadStatus TryGetUvsMappingCount(int recordOrdinal, out int count, out FontDiagnostic diagnostic)
    {
        count = 0;
        ReadStatus status = TryGetUvsTablePosition(recordOrdinal, false, out ulong tablePosition, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadU32At(tablePosition, out uint raw, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (raw > int.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, tablePosition, recordOrdinal), out diagnostic, raw, int.MaxValue);
        count = (int)raw;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Bounded row read of one {unicodeValue u24, glyphID u16} mapping.</summary>
    public ReadStatus TryGetUvsMapping(int recordOrdinal, int mappingOrdinal,
        out uint unicodeValue, out ushort glyphId, out FontDiagnostic diagnostic)
    {
        unicodeValue = 0; glyphId = 0;
        ReadStatus status = TryGetUvsTablePosition(recordOrdinal, false, out ulong tablePosition, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadU32At(tablePosition, out uint count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (mappingOrdinal < 0 || (ulong)mappingOrdinal >= count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, tablePosition, recordOrdinal), out diagnostic,
                unchecked((ulong)(long)mappingOrdinal), count);
        ulong position = tablePosition + 4 + CmapFormat14Layout.UvsMappingStride * (ulong)(uint)mappingOrdinal;
        status = TryReadU24At(position, out unicodeValue, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Preserve the local field diagnostic's unspecified record ordinal.
        status = TryReadUvsMappingAt(position, -1, out var mapping, out diagnostic);
        if (status != ReadStatus.Done) return status;
        glyphId = mapping.GlyphId;
        return ReadStatus.Done;
    }

    /// <summary>Strict format 14 validation: exact length, offset bounds, selector ordering and
    /// VS-block membership, range/mapping ordering with scalar and GID bounds, and exact tail
    /// tiling of the record array plus every present UVS table (equal offsets share legally).</summary>
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((ulong)_data.Length != Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, 2), out diagnostic, (ulong)_data.Length, Length);
        uint recordCount = NumVarSelectorRecords;
        ulong recordsEnd = CmapFormat14Layout.HeaderLength + CmapFormat14Layout.RecordStride * (ulong)recordCount;
        if ((ulong)_data.Length < recordsEnd)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, 6), out diagnostic, (ulong)_data.Length, recordsEnd);
        if (!budget.Work(_data.Length + 96L * recordCount, Location, out diagnostic) ||
            !budget.Buffer(48L * recordCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var tiles = new List<UvsTile>((int)Math.Min(2L * recordCount, 1 << 16));
        ulong previousSelector = 0;
        for (int i = 0; (uint)i < recordCount; i++)
        {
            status = TryGetVarSelector(i, out uint varSelector, out uint defaultOffset,
                out uint nonDefaultOffset, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong recordPosition = CmapFormat14Layout.HeaderLength + CmapFormat14Layout.RecordStride * (ulong)(uint)i;
            if (i > 0 && varSelector <= previousSelector)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.CharCode, recordPosition, i), out diagnostic,
                    varSelector, previousSelector);
            if (!IsVariationSelector(varSelector))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.CharCode, recordPosition, i), out diagnostic, varSelector, 0);
            status = BoundUvsOffset(defaultOffset, recordsEnd, recordPosition + 3, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = BoundUvsOffset(nonDefaultOffset, recordsEnd, recordPosition + 7, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
            previousSelector = varSelector;
            if (defaultOffset != 0)
            {
                status = TryReadU32At(defaultOffset, out uint rangeCount, out diagnostic);
                if (status != ReadStatus.Done) return status;
                ulong size = 4 + CmapFormat14Layout.DefaultRangeStride * (ulong)rangeCount;
                if (defaultOffset + size > (ulong)_data.Length)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                        Location.At(DataField.Count, defaultOffset, i), out diagnostic,
                        defaultOffset + size, (ulong)_data.Length);
                tiles.Add(new(defaultOffset, size, i));
                status = ValidateDefaultTable(defaultOffset, rangeCount, i, budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            if (nonDefaultOffset != 0)
            {
                status = TryReadU32At(nonDefaultOffset, out uint mappingCount, out diagnostic);
                if (status != ReadStatus.Done) return status;
                ulong size = 4 + CmapFormat14Layout.UvsMappingStride * (ulong)mappingCount;
                if (nonDefaultOffset + size > (ulong)_data.Length)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                        Location.At(DataField.Count, nonDefaultOffset, i), out diagnostic,
                        nonDefaultOffset + size, (ulong)_data.Length);
                tiles.Add(new(nonDefaultOffset, size, i));
                status = ValidateNonDefaultTable(nonDefaultOffset, mappingCount, i, numGlyphs, budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        tiles.Sort(static (a, b) => a.Offset.CompareTo(b.Offset));
        ulong cursor = recordsEnd;
        for (int j = 0; j < tiles.Count; j++)
        {
            var tile = tiles[j];
            if (j > 0 && tiles[j - 1].Offset == tile.Offset)
            {
                // Exact offset equality is legal sharing; anything else overlaps.
                if (tiles[j - 1].Size != tile.Size)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.OverlappingRange,
                        Location.At(DataField.Offset, tile.Offset, tile.Ordinal), out diagnostic,
                        tile.Size, tiles[j - 1].Size);
                continue;
            }
            if (tile.Offset != cursor)
            {
                if (tile.Offset < cursor)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.OverlappingRange,
                        Location.At(DataField.Offset, tile.Offset, tile.Ordinal), out diagnostic,
                        tile.Offset, cursor);
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                    Location.At(DataField.Offset, cursor, tile.Ordinal), out diagnostic,
                    tile.Offset, cursor);
            }
            cursor = tile.Offset + tile.Size;
        }
        if (cursor != (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, cursor), out diagnostic, (ulong)_data.Length, cursor);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Variation selector code points: FE00-FE0F, E0100-E01EF and the Mongolian
    /// FVS block 180B-180D.</summary>
    internal static bool IsVariationSelector(uint value) =>
        value is (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF) or (>= 0x180B and <= 0x180D);

    private ReadStatus ValidateDefaultTable(ulong tableOffset, uint rangeCount, int recordOrdinal,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!budget.Work(16L + CmapFormat14Layout.DefaultRangeStride * (long)rangeCount,
            Location, out diagnostic)) return ReadStatus.ResourceLimit;
        uint previousEnd = 0;
        for (int r = 0; (uint)r < rangeCount; r++)
        {
            ReadStatus status = TryGetDefaultRange(recordOrdinal, r, out uint start,
                out byte additionalCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong position = tableOffset + 4 + CmapFormat14Layout.DefaultRangeStride * (ulong)(uint)r;
            if (additionalCount > 239)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Count, position + 3, recordOrdinal), out diagnostic,
                    additionalCount, 239);
            if (start is >= 0xD800 and <= 0xDFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.CharCode, position, recordOrdinal), out diagnostic,
                    start, 0x10FFFF);
            uint end = start + additionalCount;
            if (end > 0x10FFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    Location.At(DataField.CharCode, position, recordOrdinal), out diagnostic,
                    end, 0x10FFFF);
            if (end is >= 0xD800 and <= 0xDFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.CharCode, position, recordOrdinal), out diagnostic,
                    end, 0x10FFFF);
            if ((uint)r > 0 && start <= previousEnd)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    Location.At(DataField.CharCode, position, recordOrdinal), out diagnostic,
                    start, previousEnd);
            previousEnd = end;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ValidateNonDefaultTable(ulong tableOffset, uint mappingCount, int recordOrdinal,
        int numGlyphs, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!budget.Work(16L + CmapFormat14Layout.UvsMappingStride * (long)mappingCount,
            Location, out diagnostic)) return ReadStatus.ResourceLimit;
        uint previousValue = 0;
        for (int r = 0; (uint)r < mappingCount; r++)
        {
            ReadStatus status = TryGetUvsMapping(recordOrdinal, r, out uint unicodeValue,
                out ushort glyphId, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong position = tableOffset + 4 + CmapFormat14Layout.UvsMappingStride * (ulong)(uint)r;
            if (unicodeValue > 0x10FFFF || unicodeValue is >= 0xD800 and <= 0xDFFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.CharCode, position, recordOrdinal), out diagnostic,
                    unicodeValue, 0x10FFFF);
            if ((uint)r > 0 && unicodeValue <= previousValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.CharCode, position, recordOrdinal), out diagnostic,
                    unicodeValue, previousValue);
            if (glyphId >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.At(DataField.GlyphId, position + 3, recordOrdinal), out diagnostic,
                    glyphId, (uint)numGlyphs);
            previousValue = unicodeValue;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus TryGetUvsTablePosition(int recordOrdinal, bool isDefault,
        out ulong tablePosition, out FontDiagnostic diagnostic)
    {
        tablePosition = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (recordOrdinal < 0 || (uint)recordOrdinal >= NumVarSelectorRecords)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, 6, recordOrdinal), out diagnostic,
                unchecked((ulong)(long)recordOrdinal), NumVarSelectorRecords);
        ulong recordPosition = CmapFormat14Layout.HeaderLength + CmapFormat14Layout.RecordStride * (ulong)(uint)recordOrdinal;
        ReadStatus status = TryReadVarSelectorAt(recordPosition, recordOrdinal, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        uint offset = isDefault ? record.DefaultUVSOffset : record.NonDefaultUVSOffset;
        if (offset == 0)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.TableNotFound,
                Location.At(DataField.Offset, recordPosition + (isDefault ? 3UL : 7UL), recordOrdinal),
                out diagnostic, 0);
        status = BoundUvsOffset(offset, 0, recordPosition + (isDefault ? 3UL : 7UL), recordOrdinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        tablePosition = offset;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus BoundUvsOffset(uint offset, ulong recordsEnd, ulong fieldPosition,
        int ordinal, out FontDiagnostic diagnostic)
    {
        if (offset == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (offset < recordsEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.At(DataField.Offset, fieldPosition, ordinal), out diagnostic, offset, recordsEnd);
        if (offset > (ulong)_data.Length - 4)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, fieldPosition, ordinal), out diagnostic,
                offset + 4, (ulong)_data.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus TryReadU32At(ulong position, out uint value, out FontDiagnostic diagnostic)
    {
        value = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (!TrySlice(_data, position, 4, out _))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, position), out diagnostic, (ulong)_data.Length, position + 4);
        value = ReadWire.U32(_data, (int)position);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus TryReadU24At(ulong position, out uint value, out FontDiagnostic diagnostic)
    {
        value = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (!TrySlice(_data, position, 3, out var bytes))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.CharCode, position), out diagnostic, (ulong)_data.Length, position + 3);
        value = ((uint)bytes[0] << 16) | ((uint)bytes[1] << 8) | bytes[2];
        diagnostic = default;
        return ReadStatus.Done;
    }

    private readonly record struct UvsTile(ulong Offset, ulong Size, int Ordinal);
}
