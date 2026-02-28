using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("Zapf", 8)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("ExtraInfo", OtFieldKind.UInt32, 4)]
public readonly partial struct ZapfTable
{
    public bool TryGetGlyphInfoOffset(int glyphId, int glyphCount, out uint glyphInfoOffset)
    {
        glyphInfoOffset = 0;

        if ((uint)glyphId >= (uint)glyphCount)
            return false;

        int offset = 8 + (glyphId * 4);
        if ((uint)offset > (uint)_table.Length - 4)
            return false;

        glyphInfoOffset = BigEndian.ReadUInt32(_table.Span, offset);
        return true;
    }

    public bool TryGetGlyphInfo(int glyphId, int glyphCount, out GlyphInfo glyphInfo)
    {
        glyphInfo = default;

        if (!TryGetGlyphInfoOffset(glyphId, glyphCount, out uint rel) || rel == 0 || rel > int.MaxValue)
            return false;

        int offset = (int)rel;
        return GlyphInfo.TryCreate(_table, offset, out glyphInfo);
    }

    [OtSubTable(10)]
    [OtField("GroupOffset", OtFieldKind.UInt32, 0)]
    [OtField("FeatureOffset", OtFieldKind.UInt32, 4)]
    [OtField("UnicodeCount", OtFieldKind.UInt16, 8)]
    [OtUInt16Array("UnicodeCodePoint", 10, CountPropertyName = "UnicodeCount")]
    public readonly partial struct GlyphInfo
    {
        public bool TryGetKindNameCount(out ushort nameCount)
        {
            nameCount = 0;

            int offset = _offset + 10 + (UnicodeCount * 2);
            if ((uint)offset > (uint)_table.Length - 2)
                return false;

            nameCount = BigEndian.ReadUInt16(_table.Span, offset);
            return true;
        }

        public bool TryGetKindName(int index, out KindName kindName)
        {
            kindName = default;

            if (!TryGetKindNameCount(out ushort count))
                return false;

            if ((uint)index >= (uint)count)
                return false;

            int pos = _offset + 10 + (UnicodeCount * 2) + 2;
            for (int i = 0; i < index; i++)
            {
                if (!KindName.TryGetByteLength(_table.Span, pos, out int len))
                    return false;
                pos += len;
            }

            return KindName.TryCreate(_table, pos, out kindName);
        }
    }

    [OtSubTable(1, GenerateTryCreate = false)]
    [OtField("Type", OtFieldKind.Byte, 0)]
    public readonly partial struct KindName
    {
        public static bool TryCreate(TableSlice zapf, int offset, out KindName kindName)
        {
            kindName = default;

            if ((uint)offset >= (uint)zapf.Length)
                return false;

            if (!TryGetByteLength(zapf.Span, offset, out int len))
                return false;

            if ((uint)offset > (uint)zapf.Length - (uint)len)
                return false;

            kindName = new KindName(zapf, offset);
            return true;
        }

        public bool TryGetPascalStringBytes(out ReadOnlySpan<byte> bytes)
        {
            bytes = default;

            byte type = Type;
            if (type >= 64)
                return false;

            if ((uint)_offset > (uint)_table.Length - 2)
                return false;

            byte len = _table.Span[_offset + 1];
            int start = _offset + 2;
            if ((uint)start > (uint)_table.Length - len)
                return false;

            bytes = _table.Span.Slice(start, len);
            return true;
        }

        public bool TryGet2ByteValue(out ushort value)
        {
            value = 0;

            byte type = Type;
            if (type < 64 || type >= 128)
                return false;

            if ((uint)_offset > (uint)_table.Length - 3)
                return false;

            value = BigEndian.ReadUInt16(_table.Span, _offset + 1);
            return true;
        }

        public static bool TryGetByteLength(ReadOnlySpan<byte> data, int offset, out int length)
        {
            length = 0;

            if ((uint)offset >= (uint)data.Length)
                return false;

            byte type = data[offset];

            if (type < 64)
            {
                if ((uint)offset > (uint)data.Length - 2)
                    return false;
                int len = 2 + data[offset + 1];
                if ((uint)offset > (uint)data.Length - (uint)len)
                    return false;
                length = len;
                return true;
            }

            if (type < 128)
            {
                if ((uint)offset > (uint)data.Length - 3)
                    return false;
                length = 3;
                return true;
            }

            return false;
        }
    }
}
