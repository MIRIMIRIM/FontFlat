using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Adobe SING Glyphlets table (<c>SING</c>).
/// </summary>
[OtTable("SING", 61)]
[OtField("TableVersionMajor", OtFieldKind.UInt16, 0)]
[OtField("TableVersionMinor", OtFieldKind.UInt16, 2)]
[OtField("GlyphletVersion", OtFieldKind.UInt16, 4)]
[OtField("Permissions", OtFieldKind.Int16, 6)]
[OtField("MainGid", OtFieldKind.UInt16, 8)]
[OtField("UnitsPerEm", OtFieldKind.UInt16, 10)]
[OtField("VertAdvance", OtFieldKind.Int16, 12)]
[OtField("VertOrigin", OtFieldKind.Int16, 14)]
[OtField("UniqueNameBytes", OtFieldKind.Bytes, 16, Length = 28)]
[OtField("MetaMd5Bytes", OtFieldKind.Bytes, 44, Length = 16)]
[OtField("NameLength", OtFieldKind.Byte, 60)]
public readonly partial struct SingTable
{
    public bool TryGetBaseGlyphNameBytes(out ReadOnlySpan<byte> bytes)
    {
        bytes = default;

        int length = NameLength;
        const int start = 61;
        if ((uint)start > (uint)_table.Length)
            return false;
        if ((uint)length > (uint)(_table.Length - start))
            return false;

        bytes = _table.Span.Slice(start, length);
        return true;
    }

    public bool TryGetBaseGlyphNameString(out string name)
    {
        name = "";

        if (!TryGetBaseGlyphNameBytes(out var bytes))
            return false;

        name = Encoding.ASCII.GetString(bytes);
        return true;
    }
}

