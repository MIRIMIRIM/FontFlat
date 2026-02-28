using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("DSIG", 8)]
[OtField("Version", OtFieldKind.UInt32, 0)]
[OtField("SignatureCount", OtFieldKind.UInt16, 4)]
[OtField("Flags", OtFieldKind.UInt16, 6)]
[OtSequentialRecordArray("SignatureRecord", 8, 12, CountPropertyName = "SignatureCount")]
public readonly partial struct DsigTable
{
    public readonly struct SignatureRecord
    {
        public uint Format { get; }
        public uint Length { get; }
        public uint Offset { get; }

        public SignatureRecord(uint format, uint length, uint offset)
        {
            Format = format;
            Length = length;
            Offset = offset;
        }
    }

    [OtSubTable(8)]
    [OtField("Reserved1", OtFieldKind.UInt16, 0)]
    [OtField("Reserved2", OtFieldKind.UInt16, 2)]
    [OtField("SignatureLength", OtFieldKind.UInt32, 4)]
    public readonly partial struct SignatureBlock
    {
        public bool TryGetSignatureSpan(out ReadOnlySpan<byte> signature)
        {
            signature = default;

            uint len = SignatureLength;
            if (len > int.MaxValue)
                return false;

            int length = (int)len;
            int start = _offset + 8;
            if ((uint)start > (uint)_table.Length)
                return false;
            if ((uint)length > (uint)(_table.Length - start))
                return false;

            signature = _table.Span.Slice(start, length);
            return true;
        }
    }

    public bool TryGetSignatureBlock(int index, out SignatureBlock block)
    {
        block = default;

        if (!TryGetSignatureRecord(index, out var record))
            return false;

        if (record.Offset > int.MaxValue)
            return false;

        int offset = (int)record.Offset;
        return SignatureBlock.TryCreate(_table, offset, out block);
    }
}
