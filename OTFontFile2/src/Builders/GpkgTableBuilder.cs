using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the <c>GPKG</c> table.
/// </summary>
[OtTableBuilder("GPKG")]
public sealed partial class GpkgTableBuilder : ISfntTableSource
{
    private readonly List<ReadOnlyMemory<byte>> _gmaps = new();
    private readonly List<ReadOnlyMemory<byte>> _glyphlets = new();

    private ushort _version;
    private ushort _flags;

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

    public ushort Flags
    {
        get => _flags;
        set
        {
            if (value == _flags)
                return;
            _flags = value;
            MarkDirty();
        }
    }

    public int GmapCount => _gmaps.Count;
    public int GlyphletCount => _glyphlets.Count;

    public IReadOnlyList<ReadOnlyMemory<byte>> Gmaps => _gmaps;
    public IReadOnlyList<ReadOnlyMemory<byte>> Glyphlets => _glyphlets;

    public void Clear()
    {
        _gmaps.Clear();
        _glyphlets.Clear();
        MarkDirty();
    }

    public void AddGmap(ReadOnlyMemory<byte> data)
    {
        _gmaps.Add(data);
        MarkDirty();
    }

    public void AddGlyphlet(ReadOnlyMemory<byte> data)
    {
        _glyphlets.Add(data);
        MarkDirty();
    }

    public static bool TryFrom(GpkgTable gpkg, out GpkgTableBuilder builder)
    {
        builder = null!;

        var b = new GpkgTableBuilder
        {
            Version = gpkg.Version,
            Flags = gpkg.Flags
        };

        int gmapCount = gpkg.GmapCount;
        for (int i = 0; i < gmapCount; i++)
        {
            if (!gpkg.TryGetGmapData(i, out var data))
                continue;
            b._gmaps.Add(data.ToArray());
        }

        int glyphletCount = gpkg.GlyphletCount;
        for (int i = 0; i < glyphletCount; i++)
        {
            if (!gpkg.TryGetGlyphletData(i, out var data))
                continue;
            b._glyphlets.Add(data.ToArray());
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_gmaps.Count > ushort.MaxValue)
            throw new InvalidOperationException("GPKG numGMAPs must fit in uint16.");
        if (_glyphlets.Count > ushort.MaxValue)
            throw new InvalidOperationException("GPKG numGlyplets must fit in uint16.");

        int gmapCount = _gmaps.Count;
        int glyphletCount = _glyphlets.Count;

        int headerSize = checked(8 + ((gmapCount + 1) * 4) + ((glyphletCount + 1) * 4));

        var gmapOffsets = new uint[gmapCount + 1];
        var glyphletOffsets = new uint[glyphletCount + 1];

        int pos = headerSize;
        gmapOffsets[0] = checked((uint)pos);
        for (int i = 0; i < gmapCount; i++)
        {
            pos = checked(pos + _gmaps[i].Length);
            gmapOffsets[i + 1] = checked((uint)pos);
        }

        glyphletOffsets[0] = checked((uint)pos);
        for (int i = 0; i < glyphletCount; i++)
        {
            pos = checked(pos + _glyphlets[i].Length);
            glyphletOffsets[i + 1] = checked((uint)pos);
        }

        byte[] table = new byte[pos];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, Version);
        BigEndian.WriteUInt16(span, 2, Flags);
        BigEndian.WriteUInt16(span, 4, checked((ushort)gmapCount));
        BigEndian.WriteUInt16(span, 6, checked((ushort)glyphletCount));

        int offsetPos = 8;
        for (int i = 0; i < gmapOffsets.Length; i++)
        {
            BigEndian.WriteUInt32(span, offsetPos, gmapOffsets[i]);
            offsetPos += 4;
        }

        for (int i = 0; i < glyphletOffsets.Length; i++)
        {
            BigEndian.WriteUInt32(span, offsetPos, glyphletOffsets[i]);
            offsetPos += 4;
        }

        int dataPos = headerSize;
        for (int i = 0; i < gmapCount; i++)
        {
            var data = _gmaps[i];
            if (data.Length != 0)
                data.Span.CopyTo(span.Slice(dataPos, data.Length));
            dataPos += data.Length;
        }

        for (int i = 0; i < glyphletCount; i++)
        {
            var data = _glyphlets[i];
            if (data.Length != 0)
                data.Span.CopyTo(span.Slice(dataPos, data.Length));
            dataPos += data.Length;
        }

        return table;
    }
}

