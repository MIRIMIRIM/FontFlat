using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>Zapf</c> table.
/// </summary>
[OtTableBuilder("Zapf", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class ZapfTableBuilder : ISfntTableSource
{
    private readonly ushort _glyphCount;

    private Fixed1616 _version = new(0x00010000u);
    private uint _extraInfo;
    private byte[] _body;

    public ZapfTableBuilder(ushort glyphCount)
    {
        _glyphCount = glyphCount;
        _body = new byte[checked(glyphCount * 4)];
    }

    public ushort GlyphCount => _glyphCount;

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

    public uint ExtraInfo
    {
        get => _extraInfo;
        set
        {
            if (value == _extraInfo)
                return;

            _extraInfo = value;
            MarkDirty();
        }
    }

    public ReadOnlyMemory<byte> BodyBytes => _body;

    public void ResetBody()
    {
        _body = new byte[checked(_glyphCount * 4)];
        MarkDirty();
    }

    public void SetBody(ReadOnlySpan<byte> bodyBytes)
    {
        int minLength = checked(_glyphCount * 4);
        if (bodyBytes.Length < minLength)
            throw new ArgumentException("Zapf body must contain the glyphInfoOffsets array.", nameof(bodyBytes));

        _body = bodyBytes.ToArray();
        MarkDirty();
    }

    public bool TryGetGlyphInfoOffset(int glyphId, out uint glyphInfoOffset)
    {
        glyphInfoOffset = 0;

        if ((uint)glyphId >= _glyphCount)
            return false;

        int offset = glyphId * 4;
        if ((uint)offset > (uint)_body.Length - 4)
            return false;

        glyphInfoOffset = BigEndian.ReadUInt32(_body, offset);
        return true;
    }

    public void SetGlyphInfoOffset(int glyphId, uint glyphInfoOffset)
    {
        if ((uint)glyphId >= _glyphCount)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        int offset = glyphId * 4;
        if ((uint)offset > (uint)_body.Length - 4)
            throw new InvalidOperationException("Zapf body is too short for the glyphInfoOffsets array.");

        BigEndian.WriteUInt32(_body, offset, glyphInfoOffset);
        MarkDirty();
    }

    public static bool TryFrom(ZapfTable zapf, ushort glyphCount, out ZapfTableBuilder builder)
    {
        builder = null!;

        int minBodyLength = checked(glyphCount * 4);

        var span = zapf.Table.Span;
        if (span.Length < 8 + minBodyLength)
            return false;

        var b = new ZapfTableBuilder(glyphCount)
        {
            Version = zapf.Version,
            ExtraInfo = zapf.ExtraInfo
        };

        b._body = span.Slice(8).ToArray();
        if (b._body.Length < minBodyLength)
            return false;

        builder = b;
        return true;
    }

    private int ComputeLength() => checked(8 + _body.Length);

    private uint ComputeDirectoryChecksum()
    {
        unchecked
        {
            return _version.RawValue + _extraInfo + OpenTypeChecksum.Compute(_body);
        }
    }

    private void WriteTable(Stream destination, uint headCheckSumAdjustment)
    {
        Span<byte> header = stackalloc byte[8];
        BigEndian.WriteUInt32(header, 0, _version.RawValue);
        BigEndian.WriteUInt32(header, 4, _extraInfo);
        destination.Write(header);
        destination.Write(_body);
    }
}
