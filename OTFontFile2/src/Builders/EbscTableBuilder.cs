using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>EBSC</c> table.
/// </summary>
[OtTableBuilder("EBSC")]
public sealed partial class EbscTableBuilder : ISfntTableSource
{
    private Fixed1616 _version = new(0x00020000u);
    private readonly List<BitmapScaleRecord> _scales = new();

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

    public int SizeCount => _scales.Count;

    public IReadOnlyList<BitmapScaleRecord> Scales => _scales;

    public void Clear()
    {
        _scales.Clear();
        MarkDirty();
    }

    public void AddScale(
        SbitLineMetricsData hori,
        SbitLineMetricsData vert,
        byte ppemX,
        byte ppemY,
        byte substitutePpemX,
        byte substitutePpemY)
    {
        _scales.Add(new BitmapScaleRecord(hori, vert, ppemX, ppemY, substitutePpemX, substitutePpemY));
        MarkDirty();
    }

    public bool RemoveAt(int index)
    {
        if ((uint)index >= (uint)_scales.Count)
            return false;

        _scales.RemoveAt(index);
        MarkDirty();
        return true;
    }

    public static bool TryFrom(EbscTable ebsc, out EbscTableBuilder builder)
    {
        builder = null!;

        uint countU = ebsc.SizeCount;
        if (countU > int.MaxValue)
            return false;

        int count = (int)countU;

        var b = new EbscTableBuilder
        {
            Version = ebsc.Version
        };

        for (int i = 0; i < count; i++)
        {
            if (!ebsc.TryGetBitmapScale(i, out var scale))
                return false;

            b._scales.Add(new BitmapScaleRecord(
                SbitLineMetricsData.From(scale.Hori),
                SbitLineMetricsData.From(scale.Vert),
                scale.PpemX,
                scale.PpemY,
                scale.SubstitutePpemX,
                scale.SubstitutePpemY));
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        int count = _scales.Count;
        int tableSize = checked(8 + (count * 28));

        byte[] table = new byte[tableSize];
        var span = table.AsSpan();

        BigEndian.WriteUInt32(span, 0, Version.RawValue);
        BigEndian.WriteUInt32(span, 4, checked((uint)count));

        int pos = 8;
        for (int i = 0; i < count; i++)
        {
            var s = _scales[i];
            s.Hori.WriteTo(span, pos + 0);
            s.Vert.WriteTo(span, pos + 12);

            span[pos + 24] = s.PpemX;
            span[pos + 25] = s.PpemY;
            span[pos + 26] = s.SubstitutePpemX;
            span[pos + 27] = s.SubstitutePpemY;
            pos += 28;
        }

        return table;
    }

    public readonly struct BitmapScaleRecord
    {
        public SbitLineMetricsData Hori { get; }
        public SbitLineMetricsData Vert { get; }
        public byte PpemX { get; }
        public byte PpemY { get; }
        public byte SubstitutePpemX { get; }
        public byte SubstitutePpemY { get; }

        public BitmapScaleRecord(
            SbitLineMetricsData hori,
            SbitLineMetricsData vert,
            byte ppemX,
            byte ppemY,
            byte substitutePpemX,
            byte substitutePpemY)
        {
            Hori = hori;
            Vert = vert;
            PpemX = ppemX;
            PpemY = ppemY;
            SubstitutePpemX = substitutePpemX;
            SubstitutePpemY = substitutePpemY;
        }
    }
}
