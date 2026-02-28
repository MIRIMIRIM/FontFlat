using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("LTSH", 4)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("NumGlyphs", OtFieldKind.UInt16, 2)]
public readonly partial struct LtshTable
{
    public bool TryGetYPel(int glyphId, out byte yPel)
    {
        yPel = 0;

        ushort count = NumGlyphs;
        if ((uint)glyphId >= (uint)count)
            return false;

        int offset = 4 + glyphId;
        if ((uint)offset >= (uint)_table.Length)
            return false;

        yPel = _table.Span[offset];
        return true;
    }

    public bool TryGetYPelSpan(out ReadOnlySpan<byte> yPels)
    {
        yPels = default;

        int count = NumGlyphs;
        if ((uint)count > (uint)_table.Length - 4)
            return false;

        yPels = _table.Span.Slice(4, count);
        return true;
    }
}
