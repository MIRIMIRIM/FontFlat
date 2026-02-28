using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// CBDT (Color Bitmap Data) table.
/// Layout-compatible with EBDT at the header level, but uses different glyph image formats (17–19).
/// </summary>
[OtTable("CBDT", 4)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
public readonly partial struct CbdtTable
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

    public static bool TryGetFormat17SmallMetricsAndData(ReadOnlySpan<byte> glyphData, out SbitSmallGlyphMetrics metrics, out ReadOnlySpan<byte> data)
    {
        data = default;
        if (!SbitSmallGlyphMetrics.TryRead(glyphData, 0, out metrics))
            return false;

        if (glyphData.Length < 9)
            return false;

        uint len = BigEndian.ReadUInt32(glyphData, 5);
        if (len > int.MaxValue)
            return false;

        int n = (int)len;
        if (glyphData.Length - 9 < n)
            return false;

        data = glyphData.Slice(9, n);
        return true;
    }

    public static bool TryGetFormat18BigMetricsAndData(ReadOnlySpan<byte> glyphData, out SbitBigGlyphMetrics metrics, out ReadOnlySpan<byte> data)
    {
        data = default;
        if (!SbitBigGlyphMetrics.TryRead(glyphData, 0, out metrics))
            return false;

        if (glyphData.Length < 12)
            return false;

        uint len = BigEndian.ReadUInt32(glyphData, 8);
        if (len > int.MaxValue)
            return false;

        int n = (int)len;
        if (glyphData.Length - 12 < n)
            return false;

        data = glyphData.Slice(12, n);
        return true;
    }

    public static bool TryGetFormat19Data(ReadOnlySpan<byte> glyphData, out ReadOnlySpan<byte> data)
    {
        data = default;

        if (glyphData.Length < 4)
            return false;

        uint len = BigEndian.ReadUInt32(glyphData, 0);
        if (len > int.MaxValue)
            return false;

        int n = (int)len;
        if (glyphData.Length - 4 < n)
            return false;

        data = glyphData.Slice(4, n);
        return true;
    }
}
