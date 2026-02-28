using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the Apple AAT <c>gcid</c> table (format 0).
/// </summary>
[OtTableBuilder("gcid")]
public sealed partial class GcidTableBuilder : ISfntTableSource
{
    private const ushort Format0 = 0;
    private const int HeaderSize = 142;
    private const int MappingOffset = 142;

    private ushort _format = Format0;
    private ushort _dataFormat;
    private ushort _registry;
    private ushort _order;
    private ushort _supplementVersion;

    private readonly byte[] _registryName = new byte[64];
    private readonly byte[] _orderName = new byte[64];

    private readonly List<ushort> _cids = new();

    public ushort Format
    {
        get => _format;
        set
        {
            if (value == _format)
                return;
            _format = value;
            MarkDirty();
        }
    }

    public ushort DataFormat
    {
        get => _dataFormat;
        set
        {
            if (value == _dataFormat)
                return;
            _dataFormat = value;
            MarkDirty();
        }
    }

    public ushort Registry
    {
        get => _registry;
        set
        {
            if (value == _registry)
                return;
            _registry = value;
            MarkDirty();
        }
    }

    public ushort Order
    {
        get => _order;
        set
        {
            if (value == _order)
                return;
            _order = value;
            MarkDirty();
        }
    }

    public ushort SupplementVersion
    {
        get => _supplementVersion;
        set
        {
            if (value == _supplementVersion)
                return;
            _supplementVersion = value;
            MarkDirty();
        }
    }

    public ReadOnlySpan<byte> RegistryNameBytes => _registryName;
    public ReadOnlySpan<byte> OrderNameBytes => _orderName;

    public int GlyphCount => _cids.Count;
    public IReadOnlyList<ushort> Cids => _cids;

    public void Clear()
    {
        _format = Format0;
        _dataFormat = 0;
        _registry = 0;
        _order = 0;
        _supplementVersion = 0;
        _registryName.AsSpan().Clear();
        _orderName.AsSpan().Clear();
        _cids.Clear();
        MarkDirty();
    }

    public void SetRegistryNameString(string ascii)
    {
        if (ascii is null) throw new ArgumentNullException(nameof(ascii));
        SetChar64(ascii, _registryName);
        MarkDirty();
    }

    public void SetOrderNameString(string ascii)
    {
        if (ascii is null) throw new ArgumentNullException(nameof(ascii));
        SetChar64(ascii, _orderName);
        MarkDirty();
    }

    public void SetCid(int glyphId, ushort cid)
    {
        if (glyphId < 0)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        if (glyphId >= ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(glyphId), "Glyph ID must be < 65535.");

        if (glyphId >= _cids.Count)
        {
            int add = (glyphId + 1) - _cids.Count;
            _cids.Capacity = Math.Max(_cids.Capacity, glyphId + 1);
            for (int i = 0; i < add; i++)
                _cids.Add(0xFFFF);
        }

        if (_cids[glyphId] == cid)
            return;

        _cids[glyphId] = cid;
        MarkDirty();
    }

    public static bool TryFrom(GcidTable gcid, out GcidTableBuilder builder)
    {
        builder = null!;

        if (!gcid.IsFormat0)
            return false;

        var b = new GcidTableBuilder
        {
            Format = gcid.Format,
            DataFormat = gcid.DataFormat,
            Registry = gcid.Registry,
            Order = gcid.Order,
            SupplementVersion = gcid.SupplementVersion
        };

        gcid.RegistryNameBytes.CopyTo(b._registryName);
        gcid.OrderNameBytes.CopyTo(b._orderName);

        if (!gcid.TryGetGlyphCount(out ushort count))
            return false;

        b._cids.Clear();
        b._cids.Capacity = Math.Max(b._cids.Capacity, count);

        int offset = MappingOffset + 2;
        var data = gcid.Table.Span;
        for (int i = 0; i < count; i++)
        {
            if ((uint)offset > (uint)data.Length - 2)
                return false;

            b._cids.Add(BigEndian.ReadUInt16(data, offset));
            offset += 2;
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_cids.Count > ushort.MaxValue)
            throw new InvalidOperationException("gcid mapping count must fit in uint16.");

        int count = _cids.Count;
        int length = checked(HeaderSize + 2 + (count * 2));

        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, Format);
        BigEndian.WriteUInt16(span, 2, DataFormat);
        BigEndian.WriteUInt32(span, 4, checked((uint)length));
        BigEndian.WriteUInt16(span, 8, Registry);
        _registryName.AsSpan().CopyTo(span.Slice(10, 64));
        BigEndian.WriteUInt16(span, 74, Order);
        _orderName.AsSpan().CopyTo(span.Slice(76, 64));
        BigEndian.WriteUInt16(span, 140, SupplementVersion);

        BigEndian.WriteUInt16(span, MappingOffset, checked((ushort)count));

        int offset = MappingOffset + 2;
        for (int i = 0; i < count; i++)
        {
            BigEndian.WriteUInt16(span, offset, _cids[i]);
            offset += 2;
        }

        return table;
    }

    private static void SetChar64(string ascii, byte[] dest)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(ascii);
        int len = Math.Min(bytes.Length, dest.Length);

        dest.AsSpan().Clear();
        bytes.AsSpan(0, len).CopyTo(dest);
    }
}

