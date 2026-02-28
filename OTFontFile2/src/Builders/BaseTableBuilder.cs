using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>BASE</c> table.
/// </summary>
[OtTableBuilder("BASE", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class BaseTableBuilder : ISfntTableSource
{
    private Fixed1616 _version = new(0x00010000u);
    private ushort _horizAxisOffset;
    private ushort _vertAxisOffset;
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

    public ushort HorizAxisOffset
    {
        get => _horizAxisOffset;
        set
        {
            if (value == _horizAxisOffset)
                return;

            _horizAxisOffset = value;
            MarkDirty();
        }
    }

    public ushort VertAxisOffset
    {
        get => _vertAxisOffset;
        set
        {
            if (value == _vertAxisOffset)
                return;

            _vertAxisOffset = value;
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

    public void SetBody(ReadOnlyMemory<byte> bodyBytes)
    {
        _body = bodyBytes;
        MarkDirty();
    }

    public static bool TryFrom(BaseTable @base, out BaseTableBuilder builder)
    {
        var b = new BaseTableBuilder
        {
            Version = @base.Version,
            HorizAxisOffset = @base.HorizAxisOffset,
            VertAxisOffset = @base.VertAxisOffset
        };

        var span = @base.Table.Span;
        b._body = span.Length == 8 ? ReadOnlyMemory<byte>.Empty : span.Slice(8).ToArray();
        builder = b;
        return true;
    }

    private int ComputeLength() => checked(8 + _body.Length);

    private uint ComputeDirectoryChecksum()
    {
        unchecked
        {
            uint word1 = ((uint)_horizAxisOffset << 16) | _vertAxisOffset;
            return _version.RawValue + word1 + OpenTypeChecksum.Compute(_body.Span);
        }
    }

    private void WriteTable(Stream destination, uint headCheckSumAdjustment)
    {
        Span<byte> header = stackalloc byte[8];
        BigEndian.WriteUInt32(header, 0, _version.RawValue);
        BigEndian.WriteUInt16(header, 4, _horizAxisOffset);
        BigEndian.WriteUInt16(header, 6, _vertAxisOffset);
        destination.Write(header);
        destination.Write(_body.Span);
    }
}
