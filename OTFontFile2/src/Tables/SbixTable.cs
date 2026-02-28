using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("sbix", 8, GenerateTryCreate = false)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("Flags", OtFieldKind.UInt16, 2)]
[OtField("StrikeCount", OtFieldKind.UInt32, 4)]
public readonly partial struct SbixTable
{
    public static bool TryCreate(TableSlice table, out SbixTable sbix)
    {
        sbix = default;

        // version(2) + flags(2) + numStrikes(4)
        if (table.Length < 8)
            return false;

        var data = table.Span;
        ushort version = BigEndian.ReadUInt16(data, 0);
        ushort flags = BigEndian.ReadUInt16(data, 2);
        uint numStrikes = BigEndian.ReadUInt32(data, 4);

        long strikeOffsetsLenLong = 8L + (numStrikes * 4L);
        if (strikeOffsetsLenLong > int.MaxValue)
            return false;
        if (strikeOffsetsLenLong > table.Length)
            return false;

        sbix = new SbixTable(table);
        return true;
    }

    public bool TryGetStrikeOffset(int strikeIndex, out uint offset)
    {
        offset = 0;

        uint count = StrikeCount;
        if ((uint)strikeIndex >= count)
            return false;

        int o = 8 + (strikeIndex * 4);
        offset = BigEndian.ReadUInt32(_table.Span, o);
        return true;
    }

    public bool TryGetStrike(int strikeIndex, ushort numGlyphs, out Strike strike)
    {
        strike = default;

        if (!TryGetStrikeOffset(strikeIndex, out uint strikeOffsetU))
            return false;
        if (strikeOffsetU > int.MaxValue)
            return false;

        int strikeOffset = (int)strikeOffsetU;
        if ((uint)strikeOffset > (uint)_table.Length)
            return false;

        int strikeEnd;
        if ((uint)(strikeIndex + 1) < StrikeCount)
        {
            if (!TryGetStrikeOffset(strikeIndex + 1, out uint nextU))
                return false;
            if (nextU > int.MaxValue)
                return false;

            strikeEnd = (int)nextU;
        }
        else
        {
            strikeEnd = _table.Length;
        }

        if (strikeEnd < strikeOffset || strikeEnd > _table.Length)
            return false;

        int strikeLength = strikeEnd - strikeOffset;
        if (!Strike.TryCreate(_table, strikeOffset, strikeLength, numGlyphs, out strike))
            return false;

        return true;
    }

    public readonly struct GlyphHeader
    {
        public short OriginOffsetX { get; }
        public short OriginOffsetY { get; }
        public Tag GraphicType { get; }

        public GlyphHeader(short originOffsetX, short originOffsetY, Tag graphicType)
        {
            OriginOffsetX = originOffsetX;
            OriginOffsetY = originOffsetY;
            GraphicType = graphicType;
        }

        public bool IsReferenceType => GraphicType.Value is 0x64757065 or 0x666C6970; // 'dupe' or 'flip'
    }

    public static bool TryReadGlyphHeader(ReadOnlySpan<byte> glyphData, out GlyphHeader header, out ReadOnlySpan<byte> payload)
    {
        header = default;
        payload = default;

        // originOffsetX(2) + originOffsetY(2) + graphicType(4)
        if (glyphData.Length < 8)
            return false;

        header = new GlyphHeader(
            originOffsetX: BigEndian.ReadInt16(glyphData, 0),
            originOffsetY: BigEndian.ReadInt16(glyphData, 2),
            graphicType: new Tag(BigEndian.ReadUInt32(glyphData, 4)));

        payload = glyphData.Slice(8);
        return true;
    }

    public readonly struct Strike
    {
        private readonly TableSlice _sbix;
        private readonly int _offset;
        private readonly int _length;
        private readonly ushort _numGlyphs;

        private Strike(TableSlice sbix, int offset, int length, ushort numGlyphs)
        {
            _sbix = sbix;
            _offset = offset;
            _length = length;
            _numGlyphs = numGlyphs;
        }

        public static bool TryCreate(TableSlice sbix, int offset, int length, ushort numGlyphs, out Strike strike)
        {
            strike = default;

            if (length < 0)
                return false;
            if ((uint)offset > (uint)sbix.Length - (uint)length)
                return false;

            // strikeHeader(4) + glyphDataOffsets[(numGlyphs+1)] (4 each)
            long offsetsLenLong = 4L + ((long)numGlyphs + 1) * 4L;
            if (offsetsLenLong > int.MaxValue)
                return false;
            if (offsetsLenLong > length)
                return false;

            strike = new Strike(sbix, offset, length, numGlyphs);
            return true;
        }

        public ushort Ppem => BigEndian.ReadUInt16(_sbix.Span, _offset + 0);
        public ushort Resolution => BigEndian.ReadUInt16(_sbix.Span, _offset + 2);
        public ushort GlyphCount => _numGlyphs;

        public bool TryGetGlyphDataBounds(ushort glyphId, out int offset, out int length)
        {
            offset = 0;
            length = 0;

            if (glyphId >= _numGlyphs)
                return false;

            int offsetsBase = _offset + 4;
            int entryOffset = offsetsBase + ((int)glyphId * 4);

            if ((uint)entryOffset > (uint)_sbix.Length - 8)
                return false;

            uint startRel = BigEndian.ReadUInt32(_sbix.Span, entryOffset);
            uint endRel = BigEndian.ReadUInt32(_sbix.Span, entryOffset + 4);
            if (endRel < startRel)
                return false;

            if (startRel > int.MaxValue || endRel > int.MaxValue)
                return false;

            int start = (int)startRel;
            int end = (int)endRel;

            if (end > _length)
                return false;

            offset = _offset + start;
            length = end - start;
            return true;
        }

        public bool TryGetGlyphDataSpan(ushort glyphId, out ReadOnlySpan<byte> glyphData)
        {
            glyphData = default;

            if (!TryGetGlyphDataBounds(glyphId, out int offset, out int length))
                return false;

            if (length == 0)
            {
                glyphData = ReadOnlySpan<byte>.Empty;
                return true;
            }

            if ((uint)offset > (uint)_sbix.Length)
                return false;
            if ((uint)length > (uint)(_sbix.Length - offset))
                return false;

            glyphData = _sbix.Span.Slice(offset, length);
            return true;
        }
    }
}
