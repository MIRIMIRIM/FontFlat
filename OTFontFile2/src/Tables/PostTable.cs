namespace OTFontFile2.Tables;

using System.Text;
using OTFontFile2.SourceGen;

[OtTable("post", 32)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("ItalicAngle", OtFieldKind.Fixed1616, 4)]
[OtField("UnderlinePosition", OtFieldKind.Int16, 8)]
[OtField("UnderlineThickness", OtFieldKind.Int16, 10)]
[OtField("IsFixedPitch", OtFieldKind.UInt32, 12)]
[OtField("MinMemType42", OtFieldKind.UInt32, 16)]
[OtField("MaxMemType42", OtFieldKind.UInt32, 20)]
[OtField("MinMemType1", OtFieldKind.UInt32, 24)]
[OtField("MaxMemType1", OtFieldKind.UInt32, 28)]
public readonly partial struct PostTable
{
    public bool IsVersion2 => Version.RawValue == 0x00020000u;

    public bool TryGetNumberOfGlyphs(out ushort numberOfGlyphs)
    {
        numberOfGlyphs = 0;

        if (!IsVersion2)
            return false;

        if (_table.Length < 34)
            return false;

        numberOfGlyphs = BigEndian.ReadUInt16(_table.Span, 32);
        return true;
    }

    public bool TryGetGlyphNameIndex(ushort glyphId, out ushort nameIndex)
    {
        nameIndex = 0;

        if (!TryGetNumberOfGlyphs(out ushort numGlyphs))
            return false;

        if (glyphId >= numGlyphs)
            return false;

        int offset = 34 + (glyphId * 2);
        if ((uint)offset > (uint)_table.Length - 2)
            return false;

        nameIndex = BigEndian.ReadUInt16(_table.Span, offset);
        return true;
    }

    public bool TryGetNameStringBytes(int nameStringIndex, out ReadOnlySpan<byte> bytes)
    {
        bytes = default;

        if (nameStringIndex < 0)
            return false;

        if (!TryGetNumberOfGlyphs(out ushort numGlyphs))
            return false;

        int pos = 34 + (numGlyphs * 2);
        if ((uint)pos > (uint)_table.Length)
            return false;

        var data = _table.Span;
        for (int i = 0; i <= nameStringIndex; i++)
        {
            if ((uint)pos >= (uint)data.Length)
                return false;

            int len = data[pos];
            pos++;

            if ((uint)pos > (uint)data.Length - (uint)len)
                return false;

            if (i == nameStringIndex)
            {
                bytes = data.Slice(pos, len);
                return true;
            }

            pos += len;
        }

        return false;
    }

    public bool TryGetGlyphNameString(ushort glyphId, out string name)
    {
        name = "";

        if (!TryGetGlyphNameIndex(glyphId, out ushort nameIndex))
            return false;

        var standard = PostStandardNames.Values;
        if (nameIndex < standard.Length)
        {
            name = standard[nameIndex];
            return true;
        }

        int stringIndex = nameIndex - standard.Length;
        if (!TryGetNameStringBytes(stringIndex, out var bytes))
            return false;

        name = Encoding.ASCII.GetString(bytes);
        return true;
    }
}
