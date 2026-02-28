using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>bdat</c> table.
/// Layout-compatible with <c>EBDT</c>.
/// </summary>
[OtTableBuilder("bdat", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class BdatTableBuilder : ISfntTableSource
{
    private Fixed1616 _version = new(0x00020000u);
    private ReadOnlyMemory<byte> _payload = ReadOnlyMemory<byte>.Empty;

    public Fixed1616 Version
    {
        get => _version;
        set
        {
            if (value == _version)
                return;

            _version = value;
            MarkDirty();
        }
    }

    public ReadOnlyMemory<byte> PayloadBytes => _payload;

    public void ClearPayload()
    {
        if (_payload.IsEmpty)
            return;

        _payload = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetPayload(ReadOnlyMemory<byte> payloadBytes)
    {
        _payload = payloadBytes;
        MarkDirty();
    }

    public static bool TryFrom(BdatTable bdat, out BdatTableBuilder builder)
    {
        var b = new BdatTableBuilder
        {
            Version = bdat.Version
        };

        var span = bdat.Table.Span;
        b._payload = span.Length == 4 ? ReadOnlyMemory<byte>.Empty : span.Slice(4).ToArray();
        builder = b;
        return true;
    }

    private int ComputeLength() => checked(4 + _payload.Length);

    private uint ComputeDirectoryChecksum()
    {
        unchecked
        {
            return _version.RawValue + OpenTypeChecksum.Compute(_payload.Span);
        }
    }

    private void WriteTable(Stream destination, uint headCheckSumAdjustment)
    {
        Span<byte> header = stackalloc byte[4];
        BigEndian.WriteUInt32(header, 0, _version.RawValue);
        destination.Write(header);
        destination.Write(_payload.Span);
    }
}
