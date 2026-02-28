using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>HVAR</c> table.
/// </summary>
[OtTableBuilder("HVAR")]
public sealed partial class HvarTableBuilder : ISfntTableSource
{
    private const ushort SupportedMajorVersion = 1;
    private const ushort DefaultMinorVersion = 0;

    private ushort _majorVersion = SupportedMajorVersion;
    private ushort _minorVersion = DefaultMinorVersion;

    private ReadOnlyMemory<byte> _itemVariationStore = default;
    private ReadOnlyMemory<byte> _advanceWidthMapping = default;
    private ReadOnlyMemory<byte> _lsbMapping = default;
    private ReadOnlyMemory<byte> _rsbMapping = default;

    public ushort MajorVersion
    {
        get => _majorVersion;
        set
        {
            if (value == _majorVersion)
                return;

            _majorVersion = value;
            MarkDirty();
        }
    }

    public ushort MinorVersion
    {
        get => _minorVersion;
        set
        {
            if (value == _minorVersion)
                return;

            _minorVersion = value;
            MarkDirty();
        }
    }

    public ReadOnlyMemory<byte> ItemVariationStoreData => _itemVariationStore;

    public bool HasAdvanceWidthMapping => !_advanceWidthMapping.IsEmpty;
    public bool HasLsbMapping => !_lsbMapping.IsEmpty;
    public bool HasRsbMapping => !_rsbMapping.IsEmpty;

    public void SetItemVariationStore(ReadOnlyMemory<byte> storeData)
    {
        _itemVariationStore = storeData;
        MarkDirty();
    }

    public void SetMinimalItemVariationStore(ushort axisCount)
    {
        // Minimal ItemVariationStore:
        // format=1, variationRegionListOffset=8, itemVariationDataCount=0
        // VariationRegionList: axisCount, regionCount=0
        byte[] store = new byte[12];
        var span = store.AsSpan();
        BigEndian.WriteUInt16(span, 0, 1);
        BigEndian.WriteUInt32(span, 2, 8);
        BigEndian.WriteUInt16(span, 6, 0);
        BigEndian.WriteUInt16(span, 8, axisCount);
        BigEndian.WriteUInt16(span, 10, 0);
        SetItemVariationStore(store);
    }

    public void ClearAdvanceWidthMapping()
    {
        if (_advanceWidthMapping.IsEmpty)
            return;

        _advanceWidthMapping = default;
        MarkDirty();
    }

    public void ClearLsbMapping()
    {
        if (_lsbMapping.IsEmpty)
            return;

        _lsbMapping = default;
        MarkDirty();
    }

    public void ClearRsbMapping()
    {
        if (_rsbMapping.IsEmpty)
            return;

        _rsbMapping = default;
        MarkDirty();
    }

    public void SetAdvanceWidthMapping(ReadOnlyMemory<byte> mapData)
    {
        _advanceWidthMapping = mapData;
        MarkDirty();
    }

    public void SetLsbMapping(ReadOnlyMemory<byte> mapData)
    {
        _lsbMapping = mapData;
        MarkDirty();
    }

    public void SetRsbMapping(ReadOnlyMemory<byte> mapData)
    {
        _rsbMapping = mapData;
        MarkDirty();
    }

    public static bool TryFrom(HvarTable hvar, out HvarTableBuilder builder)
    {
        builder = new HvarTableBuilder
        {
            MajorVersion = hvar.MajorVersion,
            MinorVersion = hvar.MinorVersion
        };

        var table = hvar.Table;
        int length = table.Length;

        int storeOffset = checked((int)hvar.ItemVariationStoreOffset);
        int advOffset = checked((int)hvar.AdvanceWidthMappingOffset);
        int lsbOffset = checked((int)hvar.LsbMappingOffset);
        int rsbOffset = checked((int)hvar.RsbMappingOffset);

        if ((uint)storeOffset > (uint)length)
            return false;

        Span<(int offset, int kind)> sections = stackalloc (int, int)[4];
        int count = 0;
        sections[count++] = (storeOffset, 0);
        if (advOffset != 0) sections[count++] = (advOffset, 1);
        if (lsbOffset != 0) sections[count++] = (lsbOffset, 2);
        if (rsbOffset != 0) sections[count++] = (rsbOffset, 3);

        sections.Slice(0, count).Sort(static (a, b) => a.offset.CompareTo(b.offset));

        for (int i = 0; i < count; i++)
        {
            int start = sections[i].offset;
            int end = i + 1 < count ? sections[i + 1].offset : length;
            if (end < start || (uint)end > (uint)length)
                return false;

            ReadOnlySpan<byte> bytes = table.Span.Slice(start, end - start);
            byte[] copied = bytes.ToArray();

            switch (sections[i].kind)
            {
                case 0:
                    builder._itemVariationStore = copied;
                    break;
                case 1:
                    builder._advanceWidthMapping = copied;
                    break;
                case 2:
                    builder._lsbMapping = copied;
                    break;
                case 3:
                    builder._rsbMapping = copied;
                    break;
            }
        }

        builder.MarkDirty();
        return true;
    }

    private byte[] BuildTable()
    {
        if (MajorVersion != SupportedMajorVersion)
            throw new InvalidOperationException("HVAR major version must be 1.");

        if (_itemVariationStore.IsEmpty)
            throw new InvalidOperationException("HVAR requires an ItemVariationStore. Call SetItemVariationStore() or SetMinimalItemVariationStore().");

        if (_itemVariationStore.Length < 8)
            throw new InvalidOperationException("ItemVariationStore data must be at least 8 bytes.");

        int storeOffset = 20;
        int pos = checked(storeOffset + _itemVariationStore.Length);

        int advOffset = 0;
        if (!_advanceWidthMapping.IsEmpty)
        {
            advOffset = pos;
            pos = checked(pos + _advanceWidthMapping.Length);
        }

        int lsbOffset = 0;
        if (!_lsbMapping.IsEmpty)
        {
            lsbOffset = pos;
            pos = checked(pos + _lsbMapping.Length);
        }

        int rsbOffset = 0;
        if (!_rsbMapping.IsEmpty)
        {
            rsbOffset = pos;
            pos = checked(pos + _rsbMapping.Length);
        }

        byte[] table = new byte[pos];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, MajorVersion);
        BigEndian.WriteUInt16(span, 2, MinorVersion);
        BigEndian.WriteUInt32(span, 4, checked((uint)storeOffset));
        BigEndian.WriteUInt32(span, 8, (uint)advOffset);
        BigEndian.WriteUInt32(span, 12, (uint)lsbOffset);
        BigEndian.WriteUInt32(span, 16, (uint)rsbOffset);

        _itemVariationStore.Span.CopyTo(span.Slice(storeOffset, _itemVariationStore.Length));
        if (advOffset != 0)
            _advanceWidthMapping.Span.CopyTo(span.Slice(advOffset, _advanceWidthMapping.Length));
        if (lsbOffset != 0)
            _lsbMapping.Span.CopyTo(span.Slice(lsbOffset, _lsbMapping.Length));
        if (rsbOffset != 0)
            _rsbMapping.Span.CopyTo(span.Slice(rsbOffset, _rsbMapping.Length));

        return table;
    }
}
