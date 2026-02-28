using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Apple AAT CID-to-glyph mapping table (<c>cidg</c>), format 0.
/// </summary>
[OtTable("cidg", 144)]
[OtField("Format", OtFieldKind.UInt16, 0)]
[OtField("DataFormat", OtFieldKind.UInt16, 2)]
[OtField("StructLength", OtFieldKind.UInt32, 4)]
[OtField("Registry", OtFieldKind.UInt16, 8)]
[OtField("RegistryNameBytes", OtFieldKind.Bytes, 10, Length = 64)]
[OtField("Order", OtFieldKind.UInt16, 74)]
[OtField("OrderNameBytes", OtFieldKind.Bytes, 76, Length = 64)]
[OtField("SupplementVersion", OtFieldKind.UInt16, 140)]
public readonly partial struct CidgTable
{
    private const int MappingOffset = 142;

    public bool IsFormat0 => Format == 0;

    public bool TryGetCidCount(out ushort count)
    {
        count = 0;
        if ((uint)MappingOffset > (uint)_table.Length - 2)
            return false;
        count = BigEndian.ReadUInt16(_table.Span, MappingOffset);
        return true;
    }

    public bool TryGetGlyphIdForCid(int cid, out ushort glyphId)
    {
        glyphId = 0;

        if (cid < 0)
            return false;

        if (!TryGetCidCount(out ushort count))
            return false;

        if ((uint)cid >= (uint)count)
            return false;

        int offset = MappingOffset + 2 + (cid * 2);
        if ((uint)offset > (uint)_table.Length - 2)
            return false;

        glyphId = BigEndian.ReadUInt16(_table.Span, offset);
        return true;
    }

    public bool TryGetMappedGlyphIdForCid(int cid, out ushort glyphId)
    {
        glyphId = 0;
        if (!TryGetGlyphIdForCid(cid, out glyphId))
            return false;
        return glyphId != 0xFFFF;
    }

    public string GetRegistryNameString() => DecodeChar64(RegistryNameBytes);
    public string GetOrderNameString() => DecodeChar64(OrderNameBytes);

    private static string DecodeChar64(ReadOnlySpan<byte> bytes)
    {
        int len = bytes.IndexOf((byte)0);
        if (len < 0)
            len = bytes.Length;
        return Encoding.ASCII.GetString(bytes.Slice(0, len));
    }
}

