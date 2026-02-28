using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>LTSH</c> table.
/// </summary>
[OtTableBuilder("LTSH")]
public sealed partial class LtshTableBuilder : ISfntTableSource
{
    private ushort _version;
    private byte[] _yPels = Array.Empty<byte>();

    public ushort Version
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

    public ushort NumGlyphs => checked((ushort)_yPels.Length);

    public void Clear()
    {
        _yPels = Array.Empty<byte>();
        MarkDirty();
    }

    public void Resize(ushort numGlyphs)
    {
        _yPels = new byte[numGlyphs];
        MarkDirty();
    }

    public bool TryGetYPel(int glyphId, out byte yPel)
    {
        yPel = 0;

        if ((uint)glyphId >= (uint)_yPels.Length)
            return false;

        yPel = _yPels[glyphId];
        return true;
    }

    public void SetYPel(int glyphId, byte yPel)
    {
        if ((uint)glyphId >= (uint)_yPels.Length)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        if (_yPels[glyphId] == yPel)
            return;

        _yPels[glyphId] = yPel;
        MarkDirty();
    }

    public void SetYPels(ReadOnlySpan<byte> yPels)
    {
        _yPels = yPels.ToArray();
        MarkDirty();
    }

    public static bool TryFrom(LtshTable ltsh, out LtshTableBuilder builder)
    {
        builder = null!;

        if (!ltsh.TryGetYPelSpan(out var yPels))
            return false;

        var b = new LtshTableBuilder
        {
            Version = ltsh.Version
        };

        b._yPels = yPels.ToArray();
        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        int count = _yPels.Length;
        if (count > ushort.MaxValue)
            throw new InvalidOperationException("LTSH numGlyphs must fit in uint16.");

        byte[] table = new byte[checked(4 + count)];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, Version);
        BigEndian.WriteUInt16(span, 2, (ushort)count);
        _yPels.AsSpan().CopyTo(span.Slice(4, count));

        return table;
    }
}
