using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the Apple AAT <c>cidg</c> table (format 0).
/// </summary>
[OtTableBuilder("cidg")]
public sealed partial class CidgTableBuilder : ISfntTableSource
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

    private readonly List<ushort> _glyphIds = new();

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

    public int CidCount => _glyphIds.Count;
    public IReadOnlyList<ushort> GlyphIds => _glyphIds;

    public void Clear()
    {
        _format = Format0;
        _dataFormat = 0;
        _registry = 0;
        _order = 0;
        _supplementVersion = 0;
        _registryName.AsSpan().Clear();
        _orderName.AsSpan().Clear();
        _glyphIds.Clear();
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

    public void SetGlyphId(int cid, ushort glyphId)
    {
        if (cid < 0)
            throw new ArgumentOutOfRangeException(nameof(cid));

        if (cid >= ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(cid), "CID must be < 65535.");

        if (cid >= _glyphIds.Count)
        {
            int add = (cid + 1) - _glyphIds.Count;
            _glyphIds.Capacity = Math.Max(_glyphIds.Capacity, cid + 1);
            for (int i = 0; i < add; i++)
                _glyphIds.Add(0xFFFF);
        }

        if (_glyphIds[cid] == glyphId)
            return;

        _glyphIds[cid] = glyphId;
        MarkDirty();
    }

    public static bool TryFrom(CidgTable cidg, out CidgTableBuilder builder)
    {
        builder = null!;

        if (!cidg.IsFormat0)
            return false;

        var b = new CidgTableBuilder
        {
            Format = cidg.Format,
            DataFormat = cidg.DataFormat,
            Registry = cidg.Registry,
            Order = cidg.Order,
            SupplementVersion = cidg.SupplementVersion
        };

        cidg.RegistryNameBytes.CopyTo(b._registryName);
        cidg.OrderNameBytes.CopyTo(b._orderName);

        if (!cidg.TryGetCidCount(out ushort count))
            return false;

        b._glyphIds.Clear();
        b._glyphIds.Capacity = Math.Max(b._glyphIds.Capacity, count);

        int offset = MappingOffset + 2;
        var data = cidg.Table.Span;
        for (int i = 0; i < count; i++)
        {
            if ((uint)offset > (uint)data.Length - 2)
                return false;

            b._glyphIds.Add(BigEndian.ReadUInt16(data, offset));
            offset += 2;
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_glyphIds.Count > ushort.MaxValue)
            throw new InvalidOperationException("cidg mapping count must fit in uint16.");

        int count = _glyphIds.Count;
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
            BigEndian.WriteUInt16(span, offset, _glyphIds[i]);
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

