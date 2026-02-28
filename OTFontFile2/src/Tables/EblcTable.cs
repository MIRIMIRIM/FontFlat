using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("EBLC", 8)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("BitmapSizeTableCount", OtFieldKind.UInt32, 4)]
public readonly partial struct EblcTable
{
    [OtSubTable(48)]
    [OtField("IndexSubTableArrayOffset", OtFieldKind.UInt32, 0)]
    [OtField("IndexTablesSize", OtFieldKind.UInt32, 4)]
    [OtField("NumberOfIndexSubTables", OtFieldKind.UInt32, 8)]
    [OtField("ColorRef", OtFieldKind.UInt32, 12)]
    [OtField("StartGlyphIndex", OtFieldKind.UInt16, 40)]
    [OtField("EndGlyphIndex", OtFieldKind.UInt16, 42)]
    [OtField("PpemX", OtFieldKind.Byte, 44)]
    [OtField("PpemY", OtFieldKind.Byte, 45)]
    [OtField("BitDepth", OtFieldKind.Byte, 46)]
    [OtField("Flags", OtFieldKind.SByte, 47)]
    public readonly partial struct BitmapSizeTable
    {
        public SbitLineMetrics Hori => SbitLineMetrics.CreateUnchecked(_table, _offset + 16);
        public SbitLineMetrics Vert => SbitLineMetrics.CreateUnchecked(_table, _offset + 28);

        [OtSubTable(8)]
        [OtField("FirstGlyphIndex", OtFieldKind.UInt16, 0)]
        [OtField("LastGlyphIndex", OtFieldKind.UInt16, 2)]
        [OtField("AdditionalOffsetToIndexSubTable", OtFieldKind.UInt32, 4)]
        public readonly partial struct IndexSubTableArray
        {
        }

        [OtSubTable(8)]
        [OtField("IndexFormat", OtFieldKind.UInt16, 0)]
        [OtField("ImageFormat", OtFieldKind.UInt16, 2)]
        [OtField("ImageDataOffset", OtFieldKind.UInt32, 4)]
        public readonly partial struct IndexSubTableHeader
        {
        }

        [OtSubTable(8, GenerateTryCreate = false, GenerateStorage = false)]
        [OtField("IndexFormat", OtFieldKind.UInt16, 0)]
        [OtField("ImageFormat", OtFieldKind.UInt16, 2)]
        [OtField("ImageDataOffset", OtFieldKind.UInt32, 4)]
        public readonly partial struct IndexSubTable
        {
            private readonly TableSlice _table;
            private readonly int _offset;
            private readonly ushort _firstGlyphIndex;
            private readonly ushort _lastGlyphIndex;

            private IndexSubTable(TableSlice eblc, int offset, ushort firstGlyphIndex, ushort lastGlyphIndex)
            {
                _table = eblc;
                _offset = offset;
                _firstGlyphIndex = firstGlyphIndex;
                _lastGlyphIndex = lastGlyphIndex;
            }

            public static bool TryCreate(TableSlice eblc, int offset, ushort firstGlyphIndex, ushort lastGlyphIndex, out IndexSubTable value)
            {
                value = default;

                // header(8)
                if ((uint)offset > (uint)eblc.Length - 8)
                    return false;

                value = new IndexSubTable(eblc, offset, firstGlyphIndex, lastGlyphIndex);
                return true;
            }

            public ushort FirstGlyphIndex => _firstGlyphIndex;
            public ushort LastGlyphIndex => _lastGlyphIndex;

            public IndexSubTableHeader Header
            {
                get
                {
                    _ = IndexSubTableHeader.TryCreate(_table, _offset, out var header);
                    return header;
                }
            }

            public bool TryGetImageSize(out uint imageSize)
            {
                imageSize = 0;

                ushort indexFormat = IndexFormat;
                if (indexFormat is not (2 or 5))
                    return false;

                if ((uint)_offset > (uint)_table.Length - 12)
                    return false;

                imageSize = BigEndian.ReadUInt32(_table.Span, _offset + 8);
                return true;
            }

            public bool TryGetBigGlyphMetrics(out SbitBigGlyphMetrics metrics)
            {
                metrics = default;

                ushort indexFormat = IndexFormat;
                if (indexFormat is not (2 or 5))
                    return false;

                // header(8) + imageSize(4) + bigMetrics(8)
                if ((uint)_offset > (uint)_table.Length - 20)
                    return false;

                return SbitBigGlyphMetrics.TryRead(_table.Span, _offset + 12, out metrics);
            }

            public bool TryGetGlyphImageBounds(ushort glyphId, out int ebdtOffset, out int length)
            {
                ebdtOffset = 0;
                length = 0;

                ushort first = _firstGlyphIndex;
                ushort last = _lastGlyphIndex;
                if (last < first)
                    return false;
                if (glyphId < first || glyphId > last)
                    return false;

                if ((uint)_offset > (uint)_table.Length - 8)
                    return false;

                ushort indexFormat = IndexFormat;
                uint imageDataOffset = ImageDataOffset;
                if (imageDataOffset > int.MaxValue)
                    return false;

                int glyphIndex = glyphId - first;

                var data = _table.Span;
                switch (indexFormat)
                {
                    case 1:
                        return TryGetBoundsFormat1(data, first, last, glyphIndex, (int)imageDataOffset, _offset, out ebdtOffset, out length);

                    case 2:
                        return TryGetBoundsFormat2(data, glyphIndex, (int)imageDataOffset, _offset, out ebdtOffset, out length);

                    case 3:
                        return TryGetBoundsFormat3(data, first, last, glyphIndex, (int)imageDataOffset, _offset, out ebdtOffset, out length);

                    case 4:
                        return TryGetBoundsFormat4(data, glyphId, (int)imageDataOffset, _offset, out ebdtOffset, out length);

                    case 5:
                        return TryGetBoundsFormat5(data, glyphId, (int)imageDataOffset, _offset, out ebdtOffset, out length);

                    default:
                        return false;
    }
}

            private static bool TryGetBoundsFormat1(
                ReadOnlySpan<byte> data,
                ushort first,
                ushort last,
                int glyphIndex,
                int imageDataOffset,
                int subTableOffset,
                out int ebdtOffset,
                out int length)
            {
                ebdtOffset = 0;
                length = 0;

                int glyphCount = last - first + 1;
                int offsetsOffset = subTableOffset + 8;
                long needed = (glyphCount + 1L) * 4;
                if (needed > int.MaxValue)
                    return false;
                if ((uint)offsetsOffset > (uint)data.Length - (uint)needed)
                    return false;

                uint startRel = BigEndian.ReadUInt32(data, offsetsOffset + (glyphIndex * 4));
                uint endRel = BigEndian.ReadUInt32(data, offsetsOffset + ((glyphIndex + 1) * 4));
                if (endRel < startRel)
                    return false;

                long startAbs = (long)imageDataOffset + startRel;
                long len = endRel - startRel;
                if (startAbs > int.MaxValue || len > int.MaxValue)
                    return false;

                ebdtOffset = (int)startAbs;
                length = (int)len;
                return true;
            }

            private static bool TryGetBoundsFormat2(
                ReadOnlySpan<byte> data,
                int glyphIndex,
                int imageDataOffset,
                int subTableOffset,
                out int ebdtOffset,
                out int length)
            {
                ebdtOffset = 0;
                length = 0;

                if ((uint)subTableOffset > (uint)data.Length - 12)
                    return false;

                uint imageSize = BigEndian.ReadUInt32(data, subTableOffset + 8);
                if (imageSize > int.MaxValue)
                    return false;

                long startAbs = (long)imageDataOffset + (imageSize * (long)glyphIndex);
                if (startAbs > int.MaxValue)
                    return false;

                ebdtOffset = (int)startAbs;
                length = (int)imageSize;
                return true;
            }

            private static bool TryGetBoundsFormat3(
                ReadOnlySpan<byte> data,
                ushort first,
                ushort last,
                int glyphIndex,
                int imageDataOffset,
                int subTableOffset,
                out int ebdtOffset,
                out int length)
            {
                ebdtOffset = 0;
                length = 0;

                int glyphCount = last - first + 1;
                int offsetsOffset = subTableOffset + 8;
                long needed = (glyphCount + 1L) * 2;
                if (needed > int.MaxValue)
                    return false;
                if ((uint)offsetsOffset > (uint)data.Length - (uint)needed)
                    return false;

                uint startRel = BigEndian.ReadUInt16(data, offsetsOffset + (glyphIndex * 2));
                uint endRel = BigEndian.ReadUInt16(data, offsetsOffset + ((glyphIndex + 1) * 2));
                if (endRel < startRel)
                    return false;

                long startAbs = (long)imageDataOffset + startRel;
                long len = endRel - startRel;
                if (startAbs > int.MaxValue || len > int.MaxValue)
                    return false;

                ebdtOffset = (int)startAbs;
                length = (int)len;
                return true;
            }

            private static bool TryGetBoundsFormat4(
                ReadOnlySpan<byte> data,
                ushort glyphId,
                int imageDataOffset,
                int subTableOffset,
                out int ebdtOffset,
                out int length)
            {
                ebdtOffset = 0;
                length = 0;

                if ((uint)subTableOffset > (uint)data.Length - 12)
                    return false;

                uint numGlyphsU = BigEndian.ReadUInt32(data, subTableOffset + 8);
                if (numGlyphsU == 0 || numGlyphsU > int.MaxValue)
                    return false;

                int numGlyphs = (int)numGlyphsU;
                int pairsOffset = subTableOffset + 12;

                long needed = (numGlyphs + 1L) * 4;
                if (needed > int.MaxValue)
                    return false;
                if ((uint)pairsOffset > (uint)data.Length - (uint)needed)
                    return false;

                for (int i = 0; i < numGlyphs; i++)
                {
                    int off = pairsOffset + (i * 4);
                    ushort code = BigEndian.ReadUInt16(data, off);
                    if (code != glyphId)
                        continue;

                    uint startRel = BigEndian.ReadUInt16(data, off + 2);
                    uint endRel = BigEndian.ReadUInt16(data, off + 6); // next pair offset
                    if (endRel < startRel)
                        return false;

                    long startAbs = (long)imageDataOffset + startRel;
                    long len = endRel - startRel;
                    if (startAbs > int.MaxValue || len > int.MaxValue)
                        return false;

                    ebdtOffset = (int)startAbs;
                    length = (int)len;
                    return true;
                }

                return false;
            }

            private static bool TryGetBoundsFormat5(
                ReadOnlySpan<byte> data,
                ushort glyphId,
                int imageDataOffset,
                int subTableOffset,
                out int ebdtOffset,
                out int length)
            {
                ebdtOffset = 0;
                length = 0;

                // header(8) + imageSize(4) + bigMetrics(8) + numGlyphs(4)
                if ((uint)subTableOffset > (uint)data.Length - 24)
                    return false;

                uint imageSize = BigEndian.ReadUInt32(data, subTableOffset + 8);
                if (imageSize > int.MaxValue)
                    return false;

                uint numGlyphsU = BigEndian.ReadUInt32(data, subTableOffset + 20);
                if (numGlyphsU == 0 || numGlyphsU > int.MaxValue)
                    return false;

                int numGlyphs = (int)numGlyphsU;
                int glyphCodeArrayOffset = subTableOffset + 24;

                long needed = numGlyphsU * 2L;
                if (needed > int.MaxValue)
                    return false;
                if ((uint)glyphCodeArrayOffset > (uint)data.Length - (uint)needed)
                    return false;

                for (int i = 0; i < numGlyphs; i++)
                {
                    ushort code = BigEndian.ReadUInt16(data, glyphCodeArrayOffset + (i * 2));
                    if (code != glyphId)
                        continue;

                    long startAbs = (long)imageDataOffset + (imageSize * (long)i);
                    if (startAbs > int.MaxValue)
                        return false;

                    ebdtOffset = (int)startAbs;
                    length = (int)imageSize;
                    return true;
                }

                return false;
            }
        }

        public bool TryGetIndexSubTableArray(int index, out IndexSubTableArray array)
        {
            array = default;

            uint count = NumberOfIndexSubTables;
            if (count > int.MaxValue)
                return false;
            if ((uint)index >= count)
                return false;

            uint rel = IndexSubTableArrayOffset;
            if (rel > int.MaxValue)
                return false;

            int offset = checked((int)rel + (index * 8));
            return IndexSubTableArray.TryCreate(_table, offset, out array);
        }

        public bool TryFindIndexSubTableArray(ushort glyphId, out IndexSubTableArray array)
        {
            array = default;

            uint count = NumberOfIndexSubTables;
            if (count > int.MaxValue)
                return false;

            for (int i = 0; i < (int)count; i++)
            {
                if (!TryGetIndexSubTableArray(i, out var item))
                    return false;

                if (glyphId >= item.FirstGlyphIndex && glyphId <= item.LastGlyphIndex)
                {
                    array = item;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetIndexSubTableHeader(IndexSubTableArray array, out IndexSubTableHeader header)
        {
            header = default;

            uint rel = IndexSubTableArrayOffset;
            uint add = array.AdditionalOffsetToIndexSubTable;

            if (rel > int.MaxValue || add > int.MaxValue)
                return false;

            int offset = checked((int)rel + (int)add);
            return IndexSubTableHeader.TryCreate(_table, offset, out header);
        }

        public bool TryGetIndexSubTable(IndexSubTableArray array, out IndexSubTable subTable)
        {
            subTable = default;

            uint rel = IndexSubTableArrayOffset;
            uint add = array.AdditionalOffsetToIndexSubTable;

            if (rel > int.MaxValue || add > int.MaxValue)
                return false;

            int offset = checked((int)rel + (int)add);
            return IndexSubTable.TryCreate(_table, offset, array.FirstGlyphIndex, array.LastGlyphIndex, out subTable);
        }

        public bool TryGetIndexSubTableForGlyph(ushort glyphId, out IndexSubTable subTable)
        {
            subTable = default;

            if (!TryFindIndexSubTableArray(glyphId, out var array))
                return false;

            return TryGetIndexSubTable(array, out subTable);
        }

        public bool TryGetGlyphImageBounds(ushort glyphId, out ushort imageFormat, out int ebdtOffset, out int length)
        {
            imageFormat = 0;
            ebdtOffset = 0;
            length = 0;

            if (!TryGetIndexSubTableForGlyph(glyphId, out var subTable))
                return false;

            imageFormat = subTable.ImageFormat;
            return subTable.TryGetGlyphImageBounds(glyphId, out ebdtOffset, out length);
        }
    }

    public bool TryGetBitmapSizeTable(int index, out BitmapSizeTable table)
    {
        table = default;

        uint count = BitmapSizeTableCount;
        if (count > int.MaxValue)
            return false;
        if ((uint)index >= count)
            return false;

        int offset = 8 + (index * 48);
        return BitmapSizeTable.TryCreate(_table, offset, out table);
    }
}
