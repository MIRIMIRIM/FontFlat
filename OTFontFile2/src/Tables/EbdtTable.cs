using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("EBDT", 4)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
public readonly partial struct EbdtTable
{
    public bool TryGetGlyphSpan(int offset, int length, out ReadOnlySpan<byte> glyphData)
    {
        glyphData = default;

        if (length < 0)
            return false;

        if (length == 0)
        {
            glyphData = ReadOnlySpan<byte>.Empty;
            return true;
        }

        if ((uint)offset > (uint)_table.Length)
            return false;
        if ((uint)length > (uint)(_table.Length - offset))
            return false;

        glyphData = _table.Span.Slice(offset, length);
        return true;
    }

    public static bool TryGetSmallMetricsAndBitmap(ReadOnlySpan<byte> glyphData, out SbitSmallGlyphMetrics metrics, out ReadOnlySpan<byte> bitmapData)
    {
        bitmapData = default;

        if (!SbitSmallGlyphMetrics.TryRead(glyphData, 0, out metrics))
            return false;

        bitmapData = glyphData.Slice(5);
        return true;
    }

    public static bool TryGetBigMetricsAndBitmap(ReadOnlySpan<byte> glyphData, out SbitBigGlyphMetrics metrics, out ReadOnlySpan<byte> bitmapData)
    {
        bitmapData = default;

        if (!SbitBigGlyphMetrics.TryRead(glyphData, 0, out metrics))
            return false;

        bitmapData = glyphData.Slice(8);
        return true;
    }

    public static bool TryGetBitmapOnly(ReadOnlySpan<byte> glyphData, out ReadOnlySpan<byte> bitmapData)
    {
        bitmapData = glyphData;
        return true;
    }

    public static bool TryGetComponentCount(ReadOnlySpan<byte> glyphData, ushort imageFormat, out ushort count)
    {
        count = 0;

        int offset = imageFormat switch
        {
            8 => 6,  // smallMetrics(5) + pad(1)
            9 => 8,  // bigMetrics(8)
            _ => -1
        };

        if (offset < 0)
            return false;

        if ((uint)offset > (uint)glyphData.Length - 2)
            return false;

        count = BigEndian.ReadUInt16(glyphData, offset);
        return true;
    }

    public static bool TryGetComponent(ReadOnlySpan<byte> glyphData, ushort imageFormat, int componentIndex, out SbitComponent component)
    {
        component = default;

        if (componentIndex < 0)
            return false;

        int baseOffset = imageFormat switch
        {
            8 => 8,   // smallMetrics(5) + pad(1) + count(2)
            9 => 10,  // bigMetrics(8) + count(2)
            _ => -1
        };

        if (baseOffset < 0)
            return false;

        if (!TryGetComponentCount(glyphData, imageFormat, out ushort count))
            return false;

        if ((uint)componentIndex >= count)
            return false;

        int offset = checked(baseOffset + (componentIndex * 4));
        if ((uint)offset > (uint)glyphData.Length - 4)
            return false;

        ushort glyphId = BigEndian.ReadUInt16(glyphData, offset);
        sbyte x = unchecked((sbyte)glyphData[offset + 2]);
        sbyte y = unchecked((sbyte)glyphData[offset + 3]);
        component = new SbitComponent(glyphId, x, y);
        return true;
    }
}
