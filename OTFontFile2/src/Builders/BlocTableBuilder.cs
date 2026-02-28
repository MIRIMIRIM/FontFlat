using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>bloc</c> table.
/// Layout-compatible with <c>EBLC</c>.
/// </summary>
[OtTableBuilder("bloc", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class BlocTableBuilder : ISfntTableSource
{
    private Fixed1616 _version = new(0x00020000u);
    private uint _bitmapSizeTableCount;
    private ReadOnlyMemory<byte> _body = ReadOnlyMemory<byte>.Empty;

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

    public uint BitmapSizeTableCount
    {
        get => _bitmapSizeTableCount;
        set
        {
            if (value == _bitmapSizeTableCount)
                return;

            _bitmapSizeTableCount = value;
            MarkDirty();
        }
    }

    public ReadOnlyMemory<byte> BodyBytes => _body;

    public void ClearBody()
    {
        if (_body.IsEmpty)
            return;

        _body = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetBody(uint bitmapSizeTableCount, ReadOnlyMemory<byte> bodyBytes)
    {
        _bitmapSizeTableCount = bitmapSizeTableCount;
        _body = bodyBytes;
        MarkDirty();
    }

    public static bool TryFrom(BlocTable bloc, out BlocTableBuilder builder)
    {
        var b = new BlocTableBuilder
        {
            Version = bloc.Version,
            BitmapSizeTableCount = bloc.BitmapSizeTableCount
        };

        var span = bloc.Table.Span;
        b._body = span.Length == 8 ? ReadOnlyMemory<byte>.Empty : span.Slice(8).ToArray();
        builder = b;
        return true;
    }

    private int ComputeLength() => checked(8 + _body.Length);

    private uint ComputeDirectoryChecksum()
    {
        unchecked
        {
            return _version.RawValue + _bitmapSizeTableCount + OpenTypeChecksum.Compute(_body.Span);
        }
    }

    private void WriteTable(Stream destination, uint headCheckSumAdjustment)
    {
        Span<byte> header = stackalloc byte[8];
        BigEndian.WriteUInt32(header, 0, _version.RawValue);
        BigEndian.WriteUInt32(header, 4, _bitmapSizeTableCount);
        destination.Write(header);
        destination.Write(_body.Span);
    }
}
