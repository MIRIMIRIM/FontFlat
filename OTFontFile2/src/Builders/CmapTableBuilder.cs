using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>cmap</c> table.
/// Currently supports writing Unicode BMP (format 4) and full Unicode (format 12) subtables.
/// Import supports format 4/12/13 (and format 14 UVS when present).
/// </summary>
[OtTableBuilder("cmap")]
public sealed partial class CmapTableBuilder : ISfntTableSource
{
    private readonly Dictionary<uint, ushort> _mappings = new();
    private readonly Dictionary<uint, VariationSelectorEntry> _uvs = new();

    public int MappingCount => _mappings.Count;

    public IEnumerable<KeyValuePair<uint, ushort>> Mappings => _mappings;

    public int VariationSelectorCount => _uvs.Count;

    public void Clear()
    {
        _mappings.Clear();
        _uvs.Clear();
        MarkDirty();
    }

    public void AddOrReplaceMapping(uint codePoint, ushort glyphId)
    {
        ValidateUnicodeScalarValue(codePoint);

        // Treat glyphId=0 as "missing" mapping (same as removal in format 4/12).
        if (glyphId == 0)
        {
            if (_mappings.Remove(codePoint))
                MarkDirty();
            return;
        }

        _mappings[codePoint] = glyphId;
        MarkDirty();
    }

    public bool RemoveMapping(uint codePoint)
    {
        bool removed = _mappings.Remove(codePoint);
        if (removed)
            MarkDirty();
        return removed;
    }

    public bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        => _mappings.TryGetValue(codePoint, out glyphId);

    public void AddOrReplaceNonDefaultUvsMapping(uint variationSelector, uint unicodeValue, ushort glyphId)
    {
        ValidateVariationSelector(variationSelector);
        ValidateUnicodeScalarValue(unicodeValue);

        if (glyphId == 0)
        {
            RemoveNonDefaultUvsMapping(variationSelector, unicodeValue);
            return;
        }

        if (!_uvs.TryGetValue(variationSelector, out var entry))
        {
            entry = new VariationSelectorEntry();
            _uvs.Add(variationSelector, entry);
        }

        entry.NonDefaultMappings[unicodeValue] = glyphId;
        MarkDirty();
    }

    public bool RemoveNonDefaultUvsMapping(uint variationSelector, uint unicodeValue)
    {
        if (!_uvs.TryGetValue(variationSelector, out var entry))
            return false;

        bool removed = entry.NonDefaultMappings.Remove(unicodeValue);
        if (removed)
            MarkDirty();

        if (entry.IsEmpty)
            _uvs.Remove(variationSelector);

        return removed;
    }

    public void AddOrReplaceDefaultUvsRange(uint variationSelector, uint startUnicodeValue, byte additionalCount)
    {
        ValidateVariationSelector(variationSelector);
        ValidateUnicodeScalarValue(startUnicodeValue);

        uint end = startUnicodeValue + additionalCount;
        if (end > 0x10FFFFu)
            throw new ArgumentOutOfRangeException(nameof(additionalCount), "Default UVS range end must be <= 0x10FFFF.");

        // Reject ranges that cross surrogate block.
        if (startUnicodeValue <= 0xD7FFu && end >= 0xD800u)
            throw new ArgumentOutOfRangeException(nameof(additionalCount), "Default UVS ranges must not include surrogate code points.");

        // Avoid emitting U+FFFF (reserved by cmap format 4 sentinel).
        if (startUnicodeValue <= 0xFFFFu && end >= 0xFFFFu)
            throw new ArgumentOutOfRangeException(nameof(additionalCount), "Default UVS ranges must not include U+FFFF.");

        if (!_uvs.TryGetValue(variationSelector, out var entry))
        {
            entry = new VariationSelectorEntry();
            _uvs.Add(variationSelector, entry);
        }

        entry.DefaultRanges[startUnicodeValue] = additionalCount;
        MarkDirty();
    }

    public bool RemoveDefaultUvsRange(uint variationSelector, uint startUnicodeValue)
    {
        if (!_uvs.TryGetValue(variationSelector, out var entry))
            return false;

        bool removed = entry.DefaultRanges.Remove(startUnicodeValue);
        if (removed)
            MarkDirty();

        if (entry.IsEmpty)
            _uvs.Remove(variationSelector);

        return removed;
    }

    internal bool TryFindGlyphIdAtOrAbove(ushort numGlyphs, out uint codePoint, out ushort glyphId, out uint variationSelector)
    {
        foreach (var kv in _mappings)
        {
            if (kv.Value >= numGlyphs)
            {
                codePoint = kv.Key;
                glyphId = kv.Value;
                variationSelector = 0;
                return true;
            }
        }

        foreach (var vs in _uvs)
        {
            foreach (var kv in vs.Value.NonDefaultMappings)
            {
                if (kv.Value >= numGlyphs)
                {
                    codePoint = kv.Key;
                    glyphId = kv.Value;
                    variationSelector = vs.Key;
                    return true;
                }
            }
        }

        codePoint = 0;
        glyphId = 0;
        variationSelector = 0;
        return false;
    }

    public static bool TryFrom(CmapTable cmap, out CmapTableBuilder builder)
    {
        builder = null!;

        var table = cmap.Table;
        var data = table.Span;

        if (table.Length < 4)
            return false;

        int recordCount = cmap.EncodingRecordCount;

        CmapTableBuilder? b = null;

        // Prefer a full Unicode format 12 subtable when present.
        if (TryFindSubtableOffset(cmap, platformId: 3, encodingId: 10, out int offset) ||
            TryFindSubtableOffset(cmap, platformId: 0, encodingId: 4, out offset))
        {
            if (TryReadFormat12(data, offset, table.Length, out var from12))
                b = from12;
            else if (TryReadFormat13(data, offset, table.Length, out var from13))
                b = from13;
        }

        // Fallback to BMP format 4.
        if (b is null && (TryFindSubtableOffset(cmap, platformId: 3, encodingId: 1, out offset) ||
                          TryFindSubtableOffset(cmap, platformId: 0, encodingId: 3, out offset)))
        {
            if (TryReadFormat4(data, offset, table.Length, out var from4))
                b = from4;
        }

        // Last resort: scan all encoding records for any supported format.
        if (b is null)
        {
            for (int i = 0; i < recordCount; i++)
            {
                if (!cmap.TryGetEncodingRecord(i, out var r))
                    continue;

                if (r.Offset > int.MaxValue)
                    continue;

                offset = (int)r.Offset;
                if ((uint)offset > (uint)table.Length - 2)
                    continue;

                ushort format = BigEndian.ReadUInt16(data, offset);
                if (format == 12)
                {
                    if (TryReadFormat12(data, offset, table.Length, out var tmp))
                    {
                        b = tmp;
                        break;
                    }
                }
                else if (format == 13)
                {
                    if (TryReadFormat13(data, offset, table.Length, out var tmp))
                    {
                        b = tmp;
                        break;
                    }
                }
                else if (format == 4)
                {
                    if (TryReadFormat4(data, offset, table.Length, out var tmp))
                    {
                        b = tmp;
                        break;
                    }
                }
            }
        }

        if (b is null)
            return false;

        // Best-effort: read format 14 UVS if present.
        _ = TryReadFormat14(cmap, b);

        b.MarkDirty();
        builder = b;
        return true;
    }

    private static bool TryFindSubtableOffset(CmapTable cmap, ushort platformId, ushort encodingId, out int offset)
    {
        offset = 0;

        if (!cmap.TryFindEncodingRecord(platformId, encodingId, out var record))
            return false;

        if (record.Offset > int.MaxValue)
            return false;

        offset = (int)record.Offset;
        return true;
    }

    private static bool TryReadFormat12(ReadOnlySpan<byte> cmap, int offset, int cmapLength, out CmapTableBuilder builder)
    {
        builder = null!;

        if ((uint)offset > (uint)cmapLength - 16)
            return false;

        if (BigEndian.ReadUInt16(cmap, offset) != 12)
            return false;

        // reserved at +2
        uint lengthU32 = BigEndian.ReadUInt32(cmap, offset + 4);
        if (lengthU32 > int.MaxValue)
            return false;

        int length = (int)lengthU32;
        if (length < 16)
            return false;

        if ((uint)offset > (uint)cmapLength - (uint)length)
            return false;

        uint nGroups = BigEndian.ReadUInt32(cmap, offset + 12);
        long groupsBytesLong = (long)nGroups * 12;
        if (groupsBytesLong > int.MaxValue)
            return false;

        if (16L + groupsBytesLong > length)
            return false;

        int groupsOffset = offset + 16;

        var b = new CmapTableBuilder();
        for (int i = 0; i < (int)nGroups; i++)
        {
            int o = groupsOffset + (i * 12);
            if ((uint)o > (uint)cmapLength - 12)
                return false;

            uint startCharCode = BigEndian.ReadUInt32(cmap, o);
            uint endCharCode = BigEndian.ReadUInt32(cmap, o + 4);
            uint startGlyphId = BigEndian.ReadUInt32(cmap, o + 8);

            if (endCharCode < startCharCode)
                return false;

            if (endCharCode > 0x10FFFFu)
                return false;

            if (startGlyphId > ushort.MaxValue)
                return false;

            uint glyph = startGlyphId;
            for (uint cp = startCharCode; cp <= endCharCode; cp++)
            {
                // Be tolerant of invalid fonts: skip surrogate code points.
                if (cp is >= 0xD800u and <= 0xDFFFu)
                {
                    glyph++;
                    continue;
                }

                if (glyph > ushort.MaxValue)
                    return false;

                ushort gid = (ushort)glyph;
                if (gid != 0)
                {
                    if (cp != 0xFFFFu)
                        b._mappings[cp] = gid;
                }

                glyph++;
            }
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private static bool TryReadFormat13(ReadOnlySpan<byte> cmap, int offset, int cmapLength, out CmapTableBuilder builder)
    {
        builder = null!;

        if ((uint)offset > (uint)cmapLength - 16)
            return false;

        if (BigEndian.ReadUInt16(cmap, offset) != 13)
            return false;

        // reserved at +2
        uint lengthU32 = BigEndian.ReadUInt32(cmap, offset + 4);
        if (lengthU32 > int.MaxValue)
            return false;

        int length = (int)lengthU32;
        if (length < 16)
            return false;

        if ((uint)offset > (uint)cmapLength - (uint)length)
            return false;

        uint nGroups = BigEndian.ReadUInt32(cmap, offset + 12);
        long groupsBytesLong = (long)nGroups * 12;
        if (groupsBytesLong > int.MaxValue)
            return false;

        if (16L + groupsBytesLong > length)
            return false;

        int groupsOffset = offset + 16;

        var b = new CmapTableBuilder();
        for (int i = 0; i < (int)nGroups; i++)
        {
            int o = groupsOffset + (i * 12);
            if ((uint)o > (uint)cmapLength - 12)
                return false;

            uint startCharCode = BigEndian.ReadUInt32(cmap, o);
            uint endCharCode = BigEndian.ReadUInt32(cmap, o + 4);
            uint glyphIdU32 = BigEndian.ReadUInt32(cmap, o + 8);

            if (endCharCode < startCharCode)
                return false;

            if (endCharCode > 0x10FFFFu)
                return false;

            if (glyphIdU32 > ushort.MaxValue)
                return false;

            ushort gid = (ushort)glyphIdU32;
            if (gid == 0)
                continue;

            for (uint cp = startCharCode; cp <= endCharCode; cp++)
            {
                // Be tolerant of invalid fonts: skip surrogate code points.
                if (cp is >= 0xD800u and <= 0xDFFFu)
                    continue;

                // Avoid emitting U+FFFF (reserved by cmap format 4 sentinel).
                if (cp == 0xFFFFu)
                    continue;

                b._mappings[cp] = gid;
            }
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private static bool TryReadFormat4(ReadOnlySpan<byte> cmap, int offset, int cmapLength, out CmapTableBuilder builder)
    {
        builder = null!;

        if ((uint)offset > (uint)cmapLength - 14)
            return false;

        if (BigEndian.ReadUInt16(cmap, offset) != 4)
            return false;

        ushort lengthU16 = BigEndian.ReadUInt16(cmap, offset + 2);
        int length = lengthU16;
        if (length < 16)
            return false;

        if ((uint)offset > (uint)cmapLength - (uint)length)
            return false;

        ushort segCountX2 = BigEndian.ReadUInt16(cmap, offset + 6);
        if ((segCountX2 & 1) != 0)
            return false;

        int segCount = segCountX2 / 2;
        if (segCount <= 0)
            return false;

        int endCodeOffset = offset + 14;
        int endCodesBytes = checked(segCount * 2);
        int startCodeOffset = checked(endCodeOffset + endCodesBytes + 2); // reservedPad
        int idDeltaOffset = checked(startCodeOffset + endCodesBytes);
        int idRangeOffsetOffset = checked(idDeltaOffset + endCodesBytes);
        int glyphIdArrayOffset = checked(idRangeOffsetOffset + endCodesBytes);

        int subtableEnd = offset + length;
        if (glyphIdArrayOffset > subtableEnd)
            return false;

        var b = new CmapTableBuilder();

        for (int i = 0; i < segCount; i++)
        {
            ushort endCode = BigEndian.ReadUInt16(cmap, endCodeOffset + (i * 2));
            ushort startCode = BigEndian.ReadUInt16(cmap, startCodeOffset + (i * 2));
            short idDelta = BigEndian.ReadInt16(cmap, idDeltaOffset + (i * 2));
            ushort idRangeOffset = BigEndian.ReadUInt16(cmap, idRangeOffsetOffset + (i * 2));

            if (startCode == 0xFFFF && endCode == 0xFFFF)
                break;

            if (endCode < startCode)
                return false;

            for (uint cp = startCode; cp <= endCode; cp++)
            {
                ushort glyphIndex;

                if (idRangeOffset == 0)
                {
                    glyphIndex = unchecked((ushort)(cp + (uint)idDelta));
                }
                else
                {
                    int idRangeOffsetWord = idRangeOffsetOffset + (i * 2);
                    int glyphIndexOffset = checked(idRangeOffsetWord + idRangeOffset + (int)((cp - startCode) * 2));
                    if ((uint)glyphIndexOffset > (uint)cmapLength - 2)
                        return false;

                    if (glyphIndexOffset >= subtableEnd)
                        return false;

                    glyphIndex = BigEndian.ReadUInt16(cmap, glyphIndexOffset);
                    if (glyphIndex != 0)
                        glyphIndex = unchecked((ushort)(glyphIndex + idDelta));
                }

                if (glyphIndex != 0)
                {
                    if (cp is >= 0xD800u and <= 0xDFFFu)
                        continue;

                    if (cp == 0xFFFFu)
                        continue;

                    b._mappings[cp] = glyphIndex;
                }
            }
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private static bool TryReadFormat14(CmapTable cmap, CmapTableBuilder builder)
    {
        // Encoding record for variation sequences is platform 0, encoding 5.
        if (!cmap.TryGetSubtable(platformId: 0, encodingId: 5, out var subtable))
            return false;

        if (!subtable.TryGetFormat14(out var format14))
            return false;

        uint count = format14.VarSelectorRecordCount;
        for (int i = 0; i < (int)count; i++)
        {
            if (!format14.TryGetVarSelectorRecord(i, out var rec))
                continue;

            uint vs = rec.VarSelector;
            if (!IsValidVariationSelector(vs))
                continue;

            if (rec.TryGetDefaultUvsTable(out var def))
            {
                uint rangeCount = def.UnicodeValueRangeCount;
                for (int r = 0; r < (int)rangeCount; r++)
                {
                    if (!def.TryGetRange(r, out var range))
                        continue;

                    uint start = range.StartUnicodeValue;
                    uint end = start + range.AdditionalCount;
                    if (!IsValidUnicodeScalarValue(start) || !IsValidUnicodeScalarValue(end))
                        continue;

                    if (start <= 0xD7FFu && end >= 0xD800u)
                        continue;

                    if (start <= 0xFFFFu && end >= 0xFFFFu)
                        continue;

                    if (!builder._uvs.TryGetValue(vs, out var entry))
                    {
                        entry = new VariationSelectorEntry();
                        builder._uvs.Add(vs, entry);
                    }

                    entry.DefaultRanges[start] = range.AdditionalCount;
                }
            }

            if (rec.TryGetNonDefaultUvsTable(out var nd))
            {
                uint mappingCount = nd.UvsMappingCount;
                for (int m = 0; m < (int)mappingCount; m++)
                {
                    if (!nd.TryGetMapping(m, out var mapping))
                        continue;

                    uint unicode = mapping.UnicodeValue;
                    if (!IsValidUnicodeScalarValue(unicode))
                        continue;

                    if (unicode == 0xFFFFu)
                        continue;

                    ushort glyphId = mapping.GlyphId;
                    if (glyphId == 0)
                        continue;

                    if (!builder._uvs.TryGetValue(vs, out var entry))
                    {
                        entry = new VariationSelectorEntry();
                        builder._uvs.Add(vs, entry);
                    }

                    entry.NonDefaultMappings[unicode] = glyphId;
                }
            }

            if (builder._uvs.TryGetValue(vs, out var e) && e.IsEmpty)
                builder._uvs.Remove(vs);
        }

        return true;
    }

    private byte[] BuildTable()
    {
        if (_mappings.Count == 0 && _uvs.Count == 0)
        {
            // Minimal cmap with no subtables is invalid in practice; still build a well-formed header.
            byte[] empty = new byte[4];
            BigEndian.WriteUInt16(empty, 0, 0); // version
            BigEndian.WriteUInt16(empty, 2, 0); // numTables
            return empty;
        }

        byte[]? format4 = null;
        int format4Length = 0;
        byte[]? format12 = null;
        byte[]? format14 = null;

        if (_mappings.Count != 0)
        {
            BuildMappingArrays(out var allCodePoints, out var allGlyphIds, out bool hasNonBmp, out var bmpCodes, out var bmpGlyphIds);

            format4 = TryBuildFormat4(bmpCodes, bmpGlyphIds, out format4Length);

            if (hasNonBmp || format4 is null)
            {
                format12 = BuildFormat12(allCodePoints, allGlyphIds);
            }
        }

        if (_uvs.Count != 0)
        {
            format14 = BuildFormat14();
            if (format14.Length == 0)
                format14 = null;
        }

        // Decide encoding records (shared offsets when possible).
        var records = new List<EncodingRecordEntry>(capacity: 5);

        if (format4 is not null)
        {
            records.Add(new EncodingRecordEntry(platformId: 0, encodingId: 3, subtable: SubtableKind.Format4));
            records.Add(new EncodingRecordEntry(platformId: 3, encodingId: 1, subtable: SubtableKind.Format4));
        }

        if (format12 is not null)
        {
            records.Add(new EncodingRecordEntry(platformId: 0, encodingId: 4, subtable: SubtableKind.Format12));
            records.Add(new EncodingRecordEntry(platformId: 3, encodingId: 10, subtable: SubtableKind.Format12));
        }

        if (format14 is not null)
        {
            records.Add(new EncodingRecordEntry(platformId: 0, encodingId: 5, subtable: SubtableKind.Format14));
        }

        int recordCount = records.Count;
        int headerSize = checked(4 + (recordCount * 8));

        int pos = headerSize;
        int format4Offset = 0;
        int format12Offset = 0;
        int format14Offset = 0;

        if (format4 is not null)
        {
            format4Offset = pos;
            pos = checked(pos + Pad4(format4Length));
        }

        if (format12 is not null)
        {
            format12Offset = pos;
            pos = checked(pos + Pad4(format12.Length));
        }

        if (format14 is not null)
        {
            format14Offset = pos;
            pos = checked(pos + Pad4(format14.Length));
        }

        byte[] table = new byte[pos];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, 0); // version
        BigEndian.WriteUInt16(span, 2, (ushort)recordCount);

        for (int i = 0; i < recordCount; i++)
        {
            int o = 4 + (i * 8);
            var r = records[i];

            int subtableOffset = r.Subtable switch
            {
                SubtableKind.Format4 => format4Offset,
                SubtableKind.Format12 => format12Offset,
                SubtableKind.Format14 => format14Offset,
                _ => 0
            };

            BigEndian.WriteUInt16(span, o + 0, r.PlatformId);
            BigEndian.WriteUInt16(span, o + 2, r.EncodingId);
            BigEndian.WriteUInt32(span, o + 4, checked((uint)subtableOffset));
        }

        if (format4 is not null)
        {
            format4.AsSpan().CopyTo(span.Slice(format4Offset, format4Length));
        }

        if (format12 is not null)
        {
            format12.AsSpan().CopyTo(span.Slice(format12Offset, format12.Length));
        }

        if (format14 is not null)
        {
            format14.AsSpan().CopyTo(span.Slice(format14Offset, format14.Length));
        }

        return table;
    }

    private void BuildMappingArrays(out uint[] allCodePoints, out ushort[] allGlyphIds, out bool hasNonBmp, out ushort[] bmpCodes, out ushort[] bmpGlyphIds)
    {
        int count = _mappings.Count;
        var codes = new uint[count];
        var glyphs = new ushort[count];

        int i = 0;
        foreach (var kv in _mappings)
        {
            codes[i] = kv.Key;
            glyphs[i] = kv.Value;
            i++;
        }

        Array.Sort(codes, glyphs);
        allCodePoints = codes;
        allGlyphIds = glyphs;

        hasNonBmp = false;
        int bmpCount = 0;
        for (i = 0; i < codes.Length; i++)
        {
            if (codes[i] <= 0xFFFFu)
            {
                bmpCount++;
            }
            else
            {
                hasNonBmp = true;
            }
        }

        bmpCodes = new ushort[bmpCount];
        bmpGlyphIds = new ushort[bmpCount];

        int b = 0;
        for (i = 0; i < codes.Length; i++)
        {
            uint cp = codes[i];
            if (cp <= 0xFFFFu)
            {
                bmpCodes[b] = (ushort)cp;
                bmpGlyphIds[b] = glyphs[i];
                b++;
            }
        }
    }

    private static byte[] BuildFormat12(uint[] codePoints, ushort[] glyphIds)
    {
        if (codePoints.Length != glyphIds.Length)
            throw new ArgumentException("Input arrays must have the same length.");

        if (codePoints.Length == 0)
            throw new InvalidOperationException("Cannot build format 12 with no mappings.");

        // Build groups where both code point and glyph id are sequential.
        var groups = new List<Group>();
        uint groupStartCp = codePoints[0];
        uint groupEndCp = codePoints[0];
        uint groupStartGid = glyphIds[0];

        uint prevCp = codePoints[0];
        uint prevGid = glyphIds[0];

        for (int i = 1; i < codePoints.Length; i++)
        {
            uint cp = codePoints[i];
            uint gid = glyphIds[i];

            if (cp == prevCp + 1 && gid == prevGid + 1)
            {
                groupEndCp = cp;
            }
            else
            {
                groups.Add(new Group(groupStartCp, groupEndCp, groupStartGid));
                groupStartCp = cp;
                groupEndCp = cp;
                groupStartGid = gid;
            }

            prevCp = cp;
            prevGid = gid;
        }

        groups.Add(new Group(groupStartCp, groupEndCp, groupStartGid));

        int length = checked(16 + (groups.Count * 12));
        byte[] subtable = new byte[length];
        var span = subtable.AsSpan();

        BigEndian.WriteUInt16(span, 0, 12);
        BigEndian.WriteUInt16(span, 2, 0); // reserved
        BigEndian.WriteUInt32(span, 4, checked((uint)length));
        BigEndian.WriteUInt32(span, 8, 0); // language
        BigEndian.WriteUInt32(span, 12, checked((uint)groups.Count));

        int offset = 16;
        for (int i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            BigEndian.WriteUInt32(span, offset + 0, g.StartCharCode);
            BigEndian.WriteUInt32(span, offset + 4, g.EndCharCode);
            BigEndian.WriteUInt32(span, offset + 8, g.StartGlyphId);
            offset += 12;
        }

        return subtable;
    }

    private static byte[]? TryBuildFormat4(ushort[] codes, ushort[] glyphIds, out int subtableLength)
    {
        subtableLength = 0;

        if (codes.Length != glyphIds.Length)
            throw new ArgumentException("Input arrays must have the same length.");

        if (codes.Length == 0)
            return null;

        // Build segments from consecutive code points.
        var segments = new List<Segment>();
        var glyphIdArray = new List<ushort>();

        int i = 0;
        while (i < codes.Length)
        {
            int startIndex = i;
            ushort startCode = codes[i];
            if (startCode == 0xFFFF)
                return null;

            ushort prevCode = startCode;
            i++;

            while (i < codes.Length)
            {
                ushort c = codes[i];
                if (c != prevCode + 1)
                    break;

                prevCode = c;
                i++;
            }

            int endIndex = i - 1;
            ushort endCode = codes[endIndex];
            if (endCode == 0xFFFF)
                return null;

            // Can we represent the whole segment using idDelta (gid = code + delta)?
            short delta = unchecked((short)(glyphIds[startIndex] - startCode));
            bool isDelta = true;

            for (int j = startIndex; j <= endIndex; j++)
            {
                ushort expected = unchecked((ushort)(codes[j] + (uint)delta));
                if (expected != glyphIds[j])
                {
                    isDelta = false;
                    break;
                }
            }

            if (isDelta)
            {
                segments.Add(new Segment(startCode, endCode, delta, glyphArrayStart: 0, glyphArrayCount: 0));
            }
            else
            {
                int glyphArrayStart = glyphIdArray.Count;
                int glyphArrayCount = checked(endIndex - startIndex + 1);
                glyphIdArray.Capacity = Math.Max(glyphIdArray.Capacity, glyphArrayStart + glyphArrayCount);

                for (int j = startIndex; j <= endIndex; j++)
                    glyphIdArray.Add(glyphIds[j]);

                segments.Add(new Segment(startCode, endCode, idDelta: 0, glyphArrayStart, glyphArrayCount));
            }
        }

        // Add the required sentinel segment.
        segments.Add(new Segment(startCode: 0xFFFF, endCode: 0xFFFF, idDelta: 1, glyphArrayStart: 0, glyphArrayCount: 0));

        int segCount = segments.Count;
        if (segCount > ushort.MaxValue)
            return null;

        int segCountX2 = segCount * 2;

        // Size check for format 4 (u16 length).
        int glyphCount = glyphIdArray.Count;
        int length = checked(16 + (segCount * 8) + (glyphCount * 2));
        if (length > ushort.MaxValue)
            return null;

        byte[] subtable = new byte[length];
        var span = subtable.AsSpan();

        BigEndian.WriteUInt16(span, 0, 4);
        BigEndian.WriteUInt16(span, 2, (ushort)length);
        BigEndian.WriteUInt16(span, 4, 0); // language
        BigEndian.WriteUInt16(span, 6, (ushort)segCountX2);

        ComputeFormat4SearchFields((ushort)segCount, out ushort searchRange, out ushort entrySelector, out ushort rangeShift);
        BigEndian.WriteUInt16(span, 8, searchRange);
        BigEndian.WriteUInt16(span, 10, entrySelector);
        BigEndian.WriteUInt16(span, 12, rangeShift);

        int endCodeOffset = 14;
        for (int s = 0; s < segCount; s++)
        {
            BigEndian.WriteUInt16(span, endCodeOffset + (s * 2), segments[s].EndCode);
        }

        int startCodeOffset = checked(endCodeOffset + (segCount * 2) + 2); // reservedPad
        for (int s = 0; s < segCount; s++)
        {
            BigEndian.WriteUInt16(span, startCodeOffset + (s * 2), segments[s].StartCode);
        }

        int idDeltaOffset = checked(startCodeOffset + (segCount * 2));
        for (int s = 0; s < segCount; s++)
        {
            BigEndian.WriteInt16(span, idDeltaOffset + (s * 2), segments[s].IdDelta);
        }

        int idRangeOffsetOffset = checked(idDeltaOffset + (segCount * 2));
        int glyphArrayOffset = checked(idRangeOffsetOffset + (segCount * 2));

        // Write idRangeOffset values (pointing into glyphIdArray for non-delta segments).
        int glyphIndex = 0;
        for (int s = 0; s < segCount; s++)
        {
            var seg = segments[s];

            ushort ro = 0;
            if (seg.GlyphArrayCount != 0)
            {
                int idRangeWordOffset = idRangeOffsetOffset + (s * 2);
                int segmentGlyphStart = glyphArrayOffset + (glyphIndex * 2);
                int diff = segmentGlyphStart - idRangeWordOffset;

                if (diff < 0 || diff > ushort.MaxValue)
                    return null;

                ro = (ushort)diff;
                glyphIndex = checked(glyphIndex + seg.GlyphArrayCount);
            }

            BigEndian.WriteUInt16(span, idRangeOffsetOffset + (s * 2), ro);
        }

        // Write glyphIdArray.
        int glyphOut = glyphArrayOffset;
        for (int g = 0; g < glyphIdArray.Count; g++)
        {
            BigEndian.WriteUInt16(span, glyphOut, glyphIdArray[g]);
            glyphOut += 2;
        }

        subtableLength = length;
        return subtable;
    }

    private byte[] BuildFormat14()
    {
        // Filter out empty selectors and sort.
        var selectors = new List<uint>(_uvs.Count);
        foreach (var kv in _uvs)
        {
            if (!kv.Value.IsEmpty)
                selectors.Add(kv.Key);
        }

        if (selectors.Count == 0)
            return Array.Empty<byte>();

        selectors.Sort();

        int recordCount = selectors.Count;
        int headerSize = checked(10 + (recordCount * 11));

        // Precompute table sizes and offsets.
        var defaultOffsets = new uint[recordCount];
        var nonDefaultOffsets = new uint[recordCount];

        int pos = headerSize;

        for (int i = 0; i < recordCount; i++)
        {
            uint selector = selectors[i];
            var entry = _uvs[selector];

            if (entry.DefaultRanges.Count != 0)
            {
                defaultOffsets[i] = checked((uint)pos);
                pos = checked(pos + checked(4 + (entry.DefaultRanges.Count * 4)));
            }

            if (entry.NonDefaultMappings.Count != 0)
            {
                nonDefaultOffsets[i] = checked((uint)pos);
                pos = checked(pos + checked(4 + (entry.NonDefaultMappings.Count * 5)));
            }
        }

        byte[] subtable = new byte[pos];
        var span = subtable.AsSpan();

        BigEndian.WriteUInt16(span, 0, 14);
        BigEndian.WriteUInt32(span, 2, checked((uint)pos));
        BigEndian.WriteUInt32(span, 6, checked((uint)recordCount));

        int recordOffset = 10;
        for (int i = 0; i < recordCount; i++)
        {
            uint selector = selectors[i];
            WriteUInt24(span, recordOffset, selector);
            BigEndian.WriteUInt32(span, recordOffset + 3, defaultOffsets[i]);
            BigEndian.WriteUInt32(span, recordOffset + 7, nonDefaultOffsets[i]);
            recordOffset += 11;
        }

        for (int i = 0; i < recordCount; i++)
        {
            uint selector = selectors[i];
            var entry = _uvs[selector];

            if (defaultOffsets[i] != 0)
            {
                uint[] starts = new uint[entry.DefaultRanges.Count];
                byte[] additionals = new byte[starts.Length];

                int idx = 0;
                foreach (var kv in entry.DefaultRanges)
                {
                    starts[idx] = kv.Key;
                    additionals[idx] = kv.Value;
                    idx++;
                }

                Array.Sort(starts, additionals);

                // Validate non-overlapping and no surrogate/FFFF inclusion.
                uint prevEnd = 0;
                for (int r = 0; r < starts.Length; r++)
                {
                    uint start = starts[r];
                    uint end = start + additionals[r];

                    if (r != 0 && start <= prevEnd)
                        throw new InvalidOperationException("cmap format 14 default UVS ranges must be sorted and non-overlapping.");

                    if (start <= 0xD7FFu && end >= 0xD800u)
                        throw new InvalidOperationException("cmap format 14 default UVS ranges must not include surrogate code points.");

                    if (start <= 0xFFFFu && end >= 0xFFFFu)
                        throw new InvalidOperationException("cmap format 14 default UVS ranges must not include U+FFFF.");

                    prevEnd = end;
                }

                int o = (int)defaultOffsets[i];
                BigEndian.WriteUInt32(span, o, checked((uint)starts.Length));
                o += 4;

                for (int r = 0; r < starts.Length; r++)
                {
                    WriteUInt24(span, o, starts[r]);
                    span[o + 3] = additionals[r];
                    o += 4;
                }
            }

            if (nonDefaultOffsets[i] != 0)
            {
                uint[] codes = new uint[entry.NonDefaultMappings.Count];
                ushort[] glyphs = new ushort[codes.Length];

                int idx = 0;
                foreach (var kv in entry.NonDefaultMappings)
                {
                    codes[idx] = kv.Key;
                    glyphs[idx] = kv.Value;
                    idx++;
                }

                Array.Sort(codes, glyphs);

                int o = (int)nonDefaultOffsets[i];
                BigEndian.WriteUInt32(span, o, checked((uint)codes.Length));
                o += 4;

                for (int m = 0; m < codes.Length; m++)
                {
                    WriteUInt24(span, o, codes[m]);
                    BigEndian.WriteUInt16(span, o + 3, glyphs[m]);
                    o += 5;
                }
            }
        }

        return subtable;
    }

    private static void ComputeFormat4SearchFields(ushort segCount, out ushort searchRange, out ushort entrySelector, out ushort rangeShift)
    {
        // Per OpenType spec (values are in bytes; each entry is uint16).
        ushort maxPower2 = 1;
        while ((ushort)(maxPower2 << 1) != 0 && (ushort)(maxPower2 << 1) <= segCount)
            maxPower2 <<= 1;

        ushort log2 = 0;
        ushort tmp = maxPower2;
        while (tmp > 1)
        {
            tmp >>= 1;
            log2++;
        }

        searchRange = (ushort)(maxPower2 * 2);
        entrySelector = log2;
        rangeShift = (ushort)((segCount * 2) - searchRange);
    }

    private static int Pad4(int length) => (length + 3) & ~3;

    private static void WriteUInt24(Span<byte> data, int offset, uint value)
    {
        if (value > 0xFFFFFFu)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must fit in uint24.");

        data[offset + 0] = (byte)(value >> 16);
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)value;
    }

    private static void ValidateUnicodeScalarValue(uint codePoint)
    {
        if (codePoint > 0x10FFFFu)
            throw new ArgumentOutOfRangeException(nameof(codePoint), "codePoint must be <= 0x10FFFF.");

        // Surrogates are not valid Unicode scalar values.
        if (codePoint is >= 0xD800u and <= 0xDFFFu)
            throw new ArgumentOutOfRangeException(nameof(codePoint), "Surrogate code points are not valid in cmap.");

        // Format 4 reserves a sentinel segment for 0xFFFF; mapping it is not supported.
        if (codePoint == 0xFFFFu)
            throw new ArgumentOutOfRangeException(nameof(codePoint), "U+FFFF is reserved and cannot be mapped in cmap.");
    }

    private static void ValidateVariationSelector(uint variationSelector)
    {
        if (!IsValidVariationSelector(variationSelector))
            throw new ArgumentOutOfRangeException(nameof(variationSelector), "Invalid Unicode variation selector.");
    }

    private static bool IsValidVariationSelector(uint variationSelector)
        => variationSelector is >= 0xFE00u and <= 0xFE0Fu
            || variationSelector is >= 0xE0100u and <= 0xE01EFu;

    private static bool IsValidUnicodeScalarValue(uint codePoint)
        => codePoint <= 0x10FFFFu && codePoint is not (>= 0xD800u and <= 0xDFFFu);

    private enum SubtableKind
    {
        Format4 = 4,
        Format12 = 12,
        Format14 = 14
    }

    private readonly struct EncodingRecordEntry
    {
        public readonly ushort PlatformId;
        public readonly ushort EncodingId;
        public readonly SubtableKind Subtable;

        public EncodingRecordEntry(ushort platformId, ushort encodingId, SubtableKind subtable)
        {
            PlatformId = platformId;
            EncodingId = encodingId;
            Subtable = subtable;
        }
    }

    private readonly struct Group
    {
        public readonly uint StartCharCode;
        public readonly uint EndCharCode;
        public readonly uint StartGlyphId;

        public Group(uint startCharCode, uint endCharCode, uint startGlyphId)
        {
            StartCharCode = startCharCode;
            EndCharCode = endCharCode;
            StartGlyphId = startGlyphId;
        }
    }

    private readonly struct Segment
    {
        public readonly ushort StartCode;
        public readonly ushort EndCode;
        public readonly short IdDelta;
        public readonly int GlyphArrayStart;
        public readonly int GlyphArrayCount;

        public Segment(ushort startCode, ushort endCode, short idDelta, int glyphArrayStart, int glyphArrayCount)
        {
            StartCode = startCode;
            EndCode = endCode;
            IdDelta = idDelta;
            GlyphArrayStart = glyphArrayStart;
            GlyphArrayCount = glyphArrayCount;
        }
    }

    private sealed class VariationSelectorEntry
    {
        public Dictionary<uint, byte> DefaultRanges { get; } = new();
        public Dictionary<uint, ushort> NonDefaultMappings { get; } = new();

        public bool IsEmpty => DefaultRanges.Count == 0 && NonDefaultMappings.Count == 0;
    }
}
