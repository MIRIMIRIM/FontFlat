using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("cmap", 4)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("EncodingRecordCount", OtFieldKind.UInt16, 2)]
[OtSequentialRecordArray("EncodingRecord", 4, 8)]
public readonly partial struct CmapTable
{
    public readonly struct EncodingRecord
    {
        public ushort PlatformId { get; }
        public ushort EncodingId { get; }
        public uint Offset { get; }

        public EncodingRecord(ushort platformId, ushort encodingId, uint offset)
        {
            PlatformId = platformId;
            EncodingId = encodingId;
            Offset = offset;
        }
    }

    public bool TryFindEncodingRecord(ushort platformId, ushort encodingId, out EncodingRecord record)
    {
        int count = EncodingRecordCount;
        for (int i = 0; i < count; i++)
        {
            if (!TryGetEncodingRecord(i, out var r))
                continue;

            if (r.PlatformId == platformId && r.EncodingId == encodingId)
            {
                record = r;
                return true;
            }
        }

        record = default;
        return false;
    }

    public bool TryGetSubtable(EncodingRecord record, out CmapSubtable subtable)
    {
        subtable = default;

        if (record.Offset > int.MaxValue)
            return false;

        int offset = (int)record.Offset;
        if ((uint)offset > (uint)_table.Length - 2)
            return false;

        subtable = new CmapSubtable(_table, record.PlatformId, record.EncodingId, offset);
        return true;
    }

    public bool TryGetSubtable(ushort platformId, ushort encodingId, out CmapSubtable subtable)
    {
        subtable = default;
        return TryFindEncodingRecord(platformId, encodingId, out var record) && TryGetSubtable(record, out subtable);
    }

    public readonly partial struct CmapSubtable
    {
        private readonly TableSlice _cmap;
        private readonly int _offset;

        internal CmapSubtable(TableSlice cmap, ushort platformId, ushort encodingId, int offset)
        {
            _cmap = cmap;
            PlatformId = platformId;
            EncodingId = encodingId;
            _offset = offset;
        }

        public ushort PlatformId { get; }
        public ushort EncodingId { get; }

        public ushort Format => BigEndian.ReadUInt16(_cmap.Span, _offset);

        public bool TryGetFormat14(out Format14Subtable format14)
        {
            format14 = default;

            if (Format != 14)
                return false;

            return Format14Subtable.TryCreate(_cmap, _offset, out format14);
        }

        /// <summary>
        /// Maps a Unicode code point to a glyph id for the current cmap subtable.
        /// Returns <see langword="false"/> only when the subtable data is invalid/unsupported.
        /// Returns <see langword="true"/> with <c>glyphId == 0</c> both for "not mapped" and for valid mappings to glyph 0 (.notdef).
        /// </summary>
        public bool TryMapCodePoint(uint codePoint, out uint glyphId)
        {
            glyphId = 0;

            var data = _cmap.Span;

            switch (Format)
            {
                case 0:
                    return TryMapFormat0(data, _offset, codePoint, out glyphId);
                case 2:
                    if (codePoint > 0xFFFF) return false;
                    return TryMapFormat2(data, _offset, (ushort)codePoint, out glyphId);
                case 4:
                    if (codePoint > 0xFFFF) return false;
                    return TryMapFormat4(data, _offset, (ushort)codePoint, out glyphId);
                case 6:
                    if (codePoint > 0xFFFF) return false;
                    return TryMapFormat6(data, _offset, (ushort)codePoint, out glyphId);
                case 10:
                    return TryMapFormat10(data, _offset, codePoint, out glyphId);
                case 12:
                    return TryMapFormat12(data, _offset, codePoint, out glyphId);
                case 13:
                    return TryMapFormat13(data, _offset, codePoint, out glyphId);
                case 8:
                    return TryMapFormat8(data, _offset, codePoint, out glyphId);
                default:
                    return false;
            }
        }

        private static bool TryMapFormat0(ReadOnlySpan<byte> cmap, int subtableOffset, uint codePoint, out uint glyphId)
        {
            glyphId = 0;
            if (codePoint > 0xFF)
                return false;

            // format(2) length(2) language(2) glyphIdArray[256]
            int glyphArrayOffset = subtableOffset + 6;
            int index = glyphArrayOffset + (int)codePoint;
            if ((uint)index >= (uint)cmap.Length)
                return false;

            glyphId = cmap[index];
            return true;
        }

        private static bool TryMapFormat6(ReadOnlySpan<byte> cmap, int subtableOffset, ushort codePoint, out uint glyphId)
        {
            glyphId = 0;

            // format(2) length(2) language(2) firstCode(2) entryCount(2) glyphIdArray[entryCount]
            if ((uint)subtableOffset > (uint)cmap.Length - 10)
                return false;

            ushort firstCode = BigEndian.ReadUInt16(cmap, subtableOffset + 6);
            ushort entryCount = BigEndian.ReadUInt16(cmap, subtableOffset + 8);

            if (codePoint < firstCode)
                return true; // not mapped

            uint index = (uint)(codePoint - firstCode);
            if (index >= entryCount)
                return true; // not mapped

            int glyphArrayOffset = subtableOffset + 10;
            int o = glyphArrayOffset + (int)index * 2;
            if ((uint)o > (uint)cmap.Length - 2)
                return false;

            glyphId = BigEndian.ReadUInt16(cmap, o);
            return true;
        }

        private static bool TryMapFormat2(ReadOnlySpan<byte> cmap, int subtableOffset, ushort codePoint, out uint glyphId)
        {
            glyphId = 0;

            // format(2) length(2) language(2) subHeaderKeys[256] subHeaders[n] glyphIndexArray[...]
            // Note: legacy OTFontFile interprets the input bytes as Intel byte order for multi-byte chars.
            if ((uint)subtableOffset > (uint)cmap.Length)
                return false;

            int remaining = cmap.Length - subtableOffset;
            if (remaining < 526)
                return false;

            byte byte1 = unchecked((byte)codePoint);
            byte byte2 = unchecked((byte)(codePoint >> 8));

            int subHeaderKeysOffset = subtableOffset + 6;
            ushort subHeaderKey = BigEndian.ReadUInt16(cmap, subHeaderKeysOffset + (byte1 * 2));
            ushort subHeaderIndex = (ushort)(subHeaderKey / 8);

            int subHeadersOffset = subtableOffset + 518;
            int subHeaderOffset = checked(subHeadersOffset + (subHeaderIndex * 8));
            if ((uint)subHeaderOffset > (uint)cmap.Length - 8)
                return false;

            ushort firstCode = BigEndian.ReadUInt16(cmap, subHeaderOffset + 0);
            ushort entryCount = BigEndian.ReadUInt16(cmap, subHeaderOffset + 2);
            short idDelta = BigEndian.ReadInt16(cmap, subHeaderOffset + 4);
            ushort idRangeOffset = BigEndian.ReadUInt16(cmap, subHeaderOffset + 6);

            byte c = subHeaderIndex == 0 ? byte1 : byte2;

            int end = firstCode + entryCount;
            if (c < firstCode || c >= end)
                return true; // not mapped

            int glyphIndexOffset = checked(subHeaderOffset + 6 + idRangeOffset + ((c - firstCode) * 2));
            if ((uint)glyphIndexOffset > (uint)cmap.Length - 2)
                return false;

            ushort raw = BigEndian.ReadUInt16(cmap, glyphIndexOffset);

            if (subHeaderIndex != 0 && raw != 0)
                raw = unchecked((ushort)(raw + idDelta));

            glyphId = raw;
            return true;
        }

        private static bool TryMapFormat10(ReadOnlySpan<byte> cmap, int subtableOffset, uint codePoint, out uint glyphId)
        {
            glyphId = 0;

            // format(2) reserved(2) length(4) language(4) startCharCode(4) numChars(4) glyphIdArray[numChars]
            if ((uint)subtableOffset > (uint)cmap.Length - 20)
                return false;

            uint startCharCode = BigEndian.ReadUInt32(cmap, subtableOffset + 12);
            uint numChars = BigEndian.ReadUInt32(cmap, subtableOffset + 16);

            if (codePoint < startCharCode)
                return true;

            uint index = codePoint - startCharCode;
            if (index >= numChars)
                return true;

            int glyphArrayOffset = subtableOffset + 20;
            long o = (long)glyphArrayOffset + (long)index * 2;
            if (o < 0 || o > cmap.Length - 2)
                return false;

            glyphId = BigEndian.ReadUInt16(cmap, (int)o);
            return true;
        }

        private static bool TryMapFormat12(ReadOnlySpan<byte> cmap, int subtableOffset, uint codePoint, out uint glyphId)
        {
            glyphId = 0;

            // format(2) reserved(2) length(4) language(4) nGroups(4) groups[n]
            if ((uint)subtableOffset > (uint)cmap.Length - 16)
                return false;

            uint nGroups = BigEndian.ReadUInt32(cmap, subtableOffset + 12);
            int groupsOffset = subtableOffset + 16;

            long required = (long)groupsOffset + (long)nGroups * 12;
            if (required > cmap.Length)
                return false;

            // Binary search groups by endCharCode.
            int lo = 0;
            int hi = (int)nGroups - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int g = groupsOffset + (mid * 12);

                uint start = BigEndian.ReadUInt32(cmap, g);
                uint end = BigEndian.ReadUInt32(cmap, g + 4);

                if (codePoint < start)
                {
                    hi = mid - 1;
                    continue;
                }

                if (codePoint > end)
                {
                    lo = mid + 1;
                    continue;
                }

                uint startGlyphId = BigEndian.ReadUInt32(cmap, g + 8);
                glyphId = startGlyphId + (codePoint - start);
                return true;
            }

            return true; // not mapped
        }

        private static bool TryMapFormat13(ReadOnlySpan<byte> cmap, int subtableOffset, uint codePoint, out uint glyphId)
        {
            glyphId = 0;

            // format(2) reserved(2) length(4) language(4) nGroups(4) groups[n]
            if ((uint)subtableOffset > (uint)cmap.Length - 16)
                return false;

            uint nGroups = BigEndian.ReadUInt32(cmap, subtableOffset + 12);
            int groupsOffset = subtableOffset + 16;

            long required = (long)groupsOffset + (long)nGroups * 12;
            if (required > cmap.Length)
                return false;

            // Binary search groups by endCharCode (same as format 12).
            int lo = 0;
            int hi = (int)nGroups - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int g = groupsOffset + (mid * 12);

                uint start = BigEndian.ReadUInt32(cmap, g);
                uint end = BigEndian.ReadUInt32(cmap, g + 4);

                if (codePoint < start)
                {
                    hi = mid - 1;
                    continue;
                }

                if (codePoint > end)
                {
                    lo = mid + 1;
                    continue;
                }

                glyphId = BigEndian.ReadUInt32(cmap, g + 8);
                return true;
            }

            return true; // not mapped
        }

        private static bool TryMapFormat8(ReadOnlySpan<byte> cmap, int subtableOffset, uint codePoint, out uint glyphId)
        {
            glyphId = 0;

            // format(2) reserved(2) length(4) language(4) is32[8192] nGroups(4) groups[n]
            if ((uint)subtableOffset > (uint)cmap.Length)
                return false;

            int remaining = cmap.Length - subtableOffset;
            if (remaining < 8208)
                return false;

            uint nGroups = BigEndian.ReadUInt32(cmap, subtableOffset + 8204);
            int groupsOffset = subtableOffset + 8208;

            long required = (long)groupsOffset + (long)nGroups * 12;
            if (required > cmap.Length)
                return false;

            // Binary search groups by endCharCode (same as format 12).
            int lo = 0;
            int hi = (int)nGroups - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int g = groupsOffset + (mid * 12);

                uint start = BigEndian.ReadUInt32(cmap, g);
                uint end = BigEndian.ReadUInt32(cmap, g + 4);

                if (codePoint < start)
                {
                    hi = mid - 1;
                    continue;
                }

                if (codePoint > end)
                {
                    lo = mid + 1;
                    continue;
                }

                uint startGlyphId = BigEndian.ReadUInt32(cmap, g + 8);
                glyphId = startGlyphId + (codePoint - start);
                return true;
            }

            return true; // not mapped
        }

        private static bool TryMapFormat4(ReadOnlySpan<byte> cmap, int subtableOffset, ushort codePoint, out uint glyphId)
        {
            glyphId = 0;

            // Need up to segCountX2 at offset 6 and range fields.
            if ((uint)subtableOffset > (uint)cmap.Length - 14)
                return false;

            ushort segCountX2 = BigEndian.ReadUInt16(cmap, subtableOffset + 6);
            if ((segCountX2 & 1) != 0)
                return false;

            int segCount = segCountX2 / 2;
            if (segCount == 0)
                return false;

            int endCodeOffset = subtableOffset + 14;
            int startCodeOffset = endCodeOffset + (segCount * 2) + 2;
            int idDeltaOffset = startCodeOffset + (segCount * 2);
            int idRangeOffsetOffset = idDeltaOffset + (segCount * 2);

            // Need arrays: endCode, startCode, idDelta, idRangeOffset at least.
            int minBytes = idRangeOffsetOffset + (segCount * 2) - subtableOffset;
            if (subtableOffset + minBytes > cmap.Length)
                return false;

            // Binary search on endCode.
            int lo = 0;
            int hi = segCount - 1;
            int found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ushort endCode = BigEndian.ReadUInt16(cmap, endCodeOffset + (mid * 2));

                if (codePoint > endCode)
                {
                    lo = mid + 1;
                    continue;
                }

                found = mid;
                hi = mid - 1;
            }

            if (found < 0)
                return true; // not mapped

            ushort startCode = BigEndian.ReadUInt16(cmap, startCodeOffset + (found * 2));
            if (codePoint < startCode)
                return true;

            short idDelta = BigEndian.ReadInt16(cmap, idDeltaOffset + (found * 2));
            ushort idRangeOffset = BigEndian.ReadUInt16(cmap, idRangeOffsetOffset + (found * 2));

            if (idRangeOffset == 0)
            {
                glyphId = (ushort)(codePoint + idDelta);
                return true;
            }

            int idRangeEntryOffset = idRangeOffsetOffset + (found * 2);
            int glyphIndexOffset = idRangeEntryOffset + idRangeOffset + ((codePoint - startCode) * 2);
            if ((uint)glyphIndexOffset > (uint)cmap.Length - 2)
                return false;

            ushort rawGlyph = BigEndian.ReadUInt16(cmap, glyphIndexOffset);
            if (rawGlyph == 0)
            {
                glyphId = 0;
                return true;
            }

            glyphId = (ushort)(rawGlyph + idDelta);
            return true;
        }

        [OtSubTable(10, GenerateTryCreate = false, GenerateStorage = false)]
        [OtSequentialRecordArray("VarSelectorRecord", 10, 11, CountPropertyName = "VarSelectorRecordCount", RecordTypeName = "VarSelectorRecord", OutParameterName = "record")]
        public readonly partial struct Format14Subtable
        {
            private readonly TableSlice _table;
            private readonly int _offset;
            private readonly int _length;
            private readonly uint _numVarSelectorRecords;

            private Format14Subtable(TableSlice cmap, int offset, int length, uint numVarSelectorRecords)
            {
                _table = cmap;
                _offset = offset;
                _length = length;
                _numVarSelectorRecords = numVarSelectorRecords;
            }

            public static bool TryCreate(TableSlice cmap, int offset, out Format14Subtable format14)
            {
                format14 = default;

                // format(2) + length(4) + numVarSelectorRecords(4)
                if ((uint)offset > (uint)cmap.Length - 10)
                    return false;

                var data = cmap.Span;
                if (BigEndian.ReadUInt16(data, offset) != 14)
                    return false;

                uint lengthU = BigEndian.ReadUInt32(data, offset + 2);
                uint numVarSelectorRecords = BigEndian.ReadUInt32(data, offset + 6);

                if (lengthU > int.MaxValue)
                    return false;

                int length = (int)lengthU;
                if (length < 10)
                    return false;

                if ((uint)offset > (uint)cmap.Length - (uint)length)
                    return false;

                long selectorBytesLong = (long)numVarSelectorRecords * 11;
                if (selectorBytesLong > int.MaxValue)
                    return false;

                if (10L + selectorBytesLong > length)
                    return false;

                format14 = new Format14Subtable(cmap, offset, length, numVarSelectorRecords);
                return true;
            }

            public int Length => _length;

            public uint VarSelectorRecordCount => _numVarSelectorRecords;

            public bool TryFindVarSelectorRecord(uint variationSelector, out VarSelectorRecord record)
            {
                record = default;

                if (variationSelector > 0xFFFFFFu)
                    return false;

                ReadOnlySpan<byte> data = _table.Span;
                int count = (int)_numVarSelectorRecords;
                int lo = 0;
                int hi = count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    int recOffset = _offset + 10 + (mid * 11);
                    if ((uint)recOffset > (uint)_table.Length - 3)
                        return false;

                    uint midSelector = BigEndian.ReadUInt24(data, recOffset);
                    if (midSelector == variationSelector)
                        return TryGetVarSelectorRecord(mid, out record);

                    if (midSelector < variationSelector)
                        lo = mid + 1;
                    else
                        hi = mid - 1;
                }

                return false;
            }

            public bool TryGetNonDefaultGlyphId(uint unicodeValue, uint variationSelector, out ushort glyphId)
            {
                glyphId = 0;

                if (!TryFindVarSelectorRecord(variationSelector, out var rec))
                    return false;

                if (!rec.TryGetNonDefaultUvsTable(out var nd))
                    return false;

                return nd.TryFindGlyphId(unicodeValue, out glyphId);
            }

            public bool IsDefaultVariationSequence(uint unicodeValue, uint variationSelector)
            {
                if (!TryFindVarSelectorRecord(variationSelector, out var rec))
                    return false;

                if (!rec.TryGetDefaultUvsTable(out var def))
                    return false;

                return def.ContainsUnicodeValue(unicodeValue);
            }
        }

        public readonly struct VarSelectorRecord
        {
            private readonly TableSlice _cmap;
            private readonly int _subtableOffset;
            private readonly int _subtableLength;

            public uint VarSelector { get; }
            public uint DefaultUvsOffset { get; }
            public uint NonDefaultUvsOffset { get; }

            internal VarSelectorRecord(
                [OtRecordContext("_table")] TableSlice cmap,
                [OtRecordContext("_offset")] int subtableOffset,
                [OtRecordContext("_length")] int subtableLength,
                [OtRecordField(OtFieldKind.UInt24)] uint varSelector,
                uint defaultUvsOffset,
                uint nonDefaultUvsOffset)
            {
                _cmap = cmap;
                _subtableOffset = subtableOffset;
                _subtableLength = subtableLength;
                VarSelector = varSelector;
                DefaultUvsOffset = defaultUvsOffset;
                NonDefaultUvsOffset = nonDefaultUvsOffset;
            }

            public bool HasDefaultUvs => DefaultUvsOffset != 0;
            public bool HasNonDefaultUvs => NonDefaultUvsOffset != 0;

            public bool TryGetDefaultUvsTable(out DefaultUvsTable table)
            {
                table = default;
                return HasDefaultUvs && DefaultUvsTable.TryCreate(_cmap, _subtableOffset, _subtableLength, DefaultUvsOffset, out table);
            }

            public bool TryGetNonDefaultUvsTable(out NonDefaultUvsTable table)
            {
                table = default;
                return HasNonDefaultUvs && NonDefaultUvsTable.TryCreate(_cmap, _subtableOffset, _subtableLength, NonDefaultUvsOffset, out table);
            }
        }

        [OtSubTable(4, GenerateTryCreate = false)]
        [OtField("UnicodeValueRangeCount", OtFieldKind.UInt32, 0)]
        [OtSequentialRecordArray("Range", 4, 4, CountPropertyName = "UnicodeValueRangeCount", RecordTypeName = "UnicodeValueRange")]
        public readonly partial struct DefaultUvsTable
        {
            public static bool TryCreate(TableSlice cmap, int subtableOffset, int subtableLength, uint relOffset, out DefaultUvsTable table)
            {
                table = default;

                if (relOffset > int.MaxValue)
                    return false;

                int offset = checked(subtableOffset + (int)relOffset);
                if ((uint)offset > (uint)cmap.Length - 4)
                    return false;

                if ((int)relOffset > subtableLength - 4)
                    return false;

                var data = cmap.Span;
                uint numRanges = BigEndian.ReadUInt32(data, offset);

                long rangesBytesLong = (long)numRanges * 4;
                if (rangesBytesLong > int.MaxValue)
                    return false;

                if (4L + rangesBytesLong > subtableLength - (int)relOffset)
                    return false;

                table = new DefaultUvsTable(cmap, offset);
                return true;
            }

            public bool ContainsUnicodeValue(uint unicodeValue)
            {
                uint count = UnicodeValueRangeCount;
                if (count == 0)
                    return false;

                if (unicodeValue > 0x10FFFFu)
                    return false;

                int rangesOffset = checked(_offset + 4);
                ReadOnlySpan<byte> data = _table.Span;

                int lo = 0;
                int hi = (int)count - 1;
                int found = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    int o = rangesOffset + (mid * 4);
                    if ((uint)o > (uint)_table.Length - 4)
                        return false;

                    uint start = BigEndian.ReadUInt24(data, o);
                    if (unicodeValue < start)
                    {
                        hi = mid - 1;
                        continue;
                    }

                    found = mid;
                    lo = mid + 1;
                }

                if (found < 0)
                    return false;

                int fo = rangesOffset + (found * 4);
                uint foundStart = BigEndian.ReadUInt24(data, fo);
                byte additional = data[fo + 3];
                uint end = foundStart + additional;
                return unicodeValue >= foundStart && unicodeValue <= end;
            }
        }

        [OtSubTable(4, GenerateTryCreate = false)]
        [OtField("UvsMappingCount", OtFieldKind.UInt32, 0)]
        [OtSequentialRecordArray("Mapping", 4, 5, CountPropertyName = "UvsMappingCount", RecordTypeName = "UvsMapping")]
        public readonly partial struct NonDefaultUvsTable
        {
            public static bool TryCreate(TableSlice cmap, int subtableOffset, int subtableLength, uint relOffset, out NonDefaultUvsTable table)
            {
                table = default;

                if (relOffset > int.MaxValue)
                    return false;

                int offset = checked(subtableOffset + (int)relOffset);
                if ((uint)offset > (uint)cmap.Length - 4)
                    return false;

                if ((int)relOffset > subtableLength - 4)
                    return false;

                var data = cmap.Span;
                uint numMappings = BigEndian.ReadUInt32(data, offset);

                long mappingsBytesLong = (long)numMappings * 5;
                if (mappingsBytesLong > int.MaxValue)
                    return false;

                if (4L + mappingsBytesLong > subtableLength - (int)relOffset)
                    return false;

                table = new NonDefaultUvsTable(cmap, offset);
                return true;
            }

            public bool TryFindGlyphId(uint unicodeValue, out ushort glyphId)
            {
                glyphId = 0;

                uint count = UvsMappingCount;
                if (count == 0)
                    return false;

                if (unicodeValue > 0x10FFFFu)
                    return false;

                int mappingsOffset = checked(_offset + 4);
                ReadOnlySpan<byte> data = _table.Span;

                int lo = 0;
                int hi = (int)count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    int o = mappingsOffset + (mid * 5);
                    if ((uint)o > (uint)_table.Length - 5)
                        return false;

                    uint midUnicode = BigEndian.ReadUInt24(data, o);
                    if (midUnicode == unicodeValue)
                    {
                        glyphId = BigEndian.ReadUInt16(data, o + 3);
                        return true;
                    }

                    if (midUnicode < unicodeValue)
                        lo = mid + 1;
                    else
                        hi = mid - 1;
                }

                return false;
            }
        }

        public readonly struct UnicodeValueRange
        {
            public uint StartUnicodeValue { get; }
            public byte AdditionalCount { get; }

            public UnicodeValueRange([OtRecordField(OtFieldKind.UInt24)] uint startUnicodeValue, byte additionalCount)
            {
                StartUnicodeValue = startUnicodeValue;
                AdditionalCount = additionalCount;
            }
        }

        public readonly struct UvsMapping
        {
            public uint UnicodeValue { get; }
            public ushort GlyphId { get; }

            public UvsMapping([OtRecordField(OtFieldKind.UInt24)] uint unicodeValue, ushort glyphId)
            {
                UnicodeValue = unicodeValue;
                GlyphId = glyphId;
            }
        }
    }
}
