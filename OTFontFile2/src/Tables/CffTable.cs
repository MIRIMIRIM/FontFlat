using OTFontFile2.SourceGen;
using System.Text;

namespace OTFontFile2.Tables;

[OtTable("CFF ", 4, GenerateTryCreate = false)]
[OtField("Major", OtFieldKind.Byte, 0)]
[OtField("Minor", OtFieldKind.Byte, 1)]
[OtField("HeaderSize", OtFieldKind.Byte, 2)]
[OtField("OffSize", OtFieldKind.Byte, 3)]
public readonly partial struct CffTable
{
    public static bool TryCreate(TableSlice table, out CffTable cff)
    {
        // major(1) + minor(1) + hdrSize(1) + offSize(1)
        if (table.Length < 4)
        {
            cff = default;
            return false;
        }

        byte hdrSize = table.Span[2];
        if (hdrSize < 4 || hdrSize > table.Length)
        {
            cff = default;
            return false;
        }

        cff = new CffTable(table);
        return true;
    }
    public bool TryGetNameIndex(out CffIndex index)
        => CffIndex.TryCreate(_table, HeaderSize, out index);

    public bool TryGetTopDictIndex(out CffIndex index)
    {
        index = default;

        if (!TryGetNameIndex(out var name))
            return false;

        int offset = checked(HeaderSize + name.ByteLength);
        return CffIndex.TryCreate(_table, offset, out index);
    }

    public bool TryGetStringIndex(out CffIndex index)
    {
        index = default;

        if (!TryGetTopDictIndex(out var topDict))
            return false;

        int offset = checked(topDict.Offset + topDict.ByteLength);
        return CffIndex.TryCreate(_table, offset, out index);
    }

    public bool TryGetGlobalSubrIndex(out CffIndex index)
    {
        index = default;

        if (!TryGetStringIndex(out var strings))
            return false;

        int offset = checked(strings.Offset + strings.ByteLength);
        return CffIndex.TryCreate(_table, offset, out index);
    }

    public bool TryGetTopDict(out CffTopDict topDict)
    {
        topDict = default;

        if (!TryGetTopDictIndex(out var topIndex))
            return false;

        if (topIndex.Count == 0)
            return false;

        if (!topIndex.TryGetObjectBounds(0, out int dictOffset, out int dictLength))
            return false;

        return CffTopDict.TryCreate(_table, dictOffset, dictLength, out topDict);
    }

    public bool TryGetSidString(int sid, out string value, bool allowUtf8 = false)
    {
        value = "";

        if (sid < 0)
            return false;

        var std = CffStandardStrings.Values;
        if ((uint)sid < (uint)std.Length)
        {
            value = std[sid];
            return true;
        }

        int stringIndex = sid - std.Length;
        if (!TryGetStringIndex(out var strings))
            return false;

        if (!strings.TryGetObjectSpan(stringIndex, out var bytes))
            return false;

        value = allowUtf8 ? Encoding.UTF8.GetString(bytes) : Encoding.ASCII.GetString(bytes);
        return true;
    }
}
