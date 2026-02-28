using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// AAT <c>ltag</c> table.
/// Maps numeric language codes used by the <c>name</c> table to IETF language tags.
/// </summary>
[OtTable("ltag", 12, GenerateTryCreate = false)]
[OtField("Version", OtFieldKind.UInt32, 0)]
[OtField("Flags", OtFieldKind.UInt32, 4)]
[OtField("TagCount", OtFieldKind.UInt32, 8)]
[OtSequentialRecordArray("TagRecord", 12, 4, CountPropertyName = "TagCount")]
public readonly partial struct LtagTable
{
    public static bool TryCreate(TableSlice table, out LtagTable ltag)
    {
        ltag = default;

        if (table.Length < 12)
            return false;

        var data = table.Span;
        uint version = BigEndian.ReadUInt32(data, 0);
        if (version != 1)
            return false;

        uint tagCount = BigEndian.ReadUInt32(data, 8);
        long recordsBytesLong = 12L + ((long)tagCount * 4);
        if (recordsBytesLong > table.Length)
            return false;

        ltag = new LtagTable(table);
        return true;
    }

    public readonly struct TagRecord
    {
        public ushort Offset { get; }
        public ushort Length { get; }

        public TagRecord(ushort offset, ushort length)
        {
            Offset = offset;
            Length = length;
        }
    }

    public bool TryGetLanguageTagSpan(int index, out ReadOnlySpan<byte> tag)
    {
        tag = default;

        if (!TryGetTagRecord(index, out var record))
            return false;

        int offset = record.Offset;
        int length = record.Length;
        if ((uint)offset > (uint)_table.Length)
            return false;
        if ((uint)length > (uint)(_table.Length - offset))
            return false;

        tag = _table.Span.Slice(offset, length);
        return true;
    }

    public string? GetLanguageTagString(int index)
    {
        if (!TryGetLanguageTagSpan(index, out var tag))
            return null;

        for (int i = 0; i < tag.Length; i++)
        {
            if (tag[i] > 0x7Fu)
                return null;
        }

        return Encoding.ASCII.GetString(tag);
    }
}

