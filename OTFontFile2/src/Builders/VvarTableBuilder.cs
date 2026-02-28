using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>VVAR</c> table.
/// </summary>
[OtTableBuilder("VVAR")]
public sealed partial class VvarTableBuilder : ISfntTableSource
{
    private const ushort SupportedMajorVersion = 1;
    private const ushort DefaultMinorVersion = 0;

    private ushort _majorVersion = SupportedMajorVersion;
    private ushort _minorVersion = DefaultMinorVersion;

    private ReadOnlyMemory<byte> _itemVariationStore = default;
    private ReadOnlyMemory<byte> _advanceHeightMapping = default;
    private ReadOnlyMemory<byte> _tsbMapping = default;
    private ReadOnlyMemory<byte> _bsbMapping = default;
    private ReadOnlyMemory<byte> _vorgMapping = default;

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

    public bool HasAdvanceHeightMapping => !_advanceHeightMapping.IsEmpty;
    public bool HasTsbMapping => !_tsbMapping.IsEmpty;
    public bool HasBsbMapping => !_bsbMapping.IsEmpty;
    public bool HasVorgMapping => !_vorgMapping.IsEmpty;

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

    public void ClearAdvanceHeightMapping()
    {
        if (_advanceHeightMapping.IsEmpty)
            return;

        _advanceHeightMapping = default;
        MarkDirty();
    }

    public void ClearTsbMapping()
    {
        if (_tsbMapping.IsEmpty)
            return;

        _tsbMapping = default;
        MarkDirty();
    }

    public void ClearBsbMapping()
    {
        if (_bsbMapping.IsEmpty)
            return;

        _bsbMapping = default;
        MarkDirty();
    }

    public void ClearVorgMapping()
    {
        if (_vorgMapping.IsEmpty)
            return;

        _vorgMapping = default;
        MarkDirty();
    }

    public void SetAdvanceHeightMapping(ReadOnlyMemory<byte> mapData)
    {
        _advanceHeightMapping = mapData;
        MarkDirty();
    }

    public void SetTsbMapping(ReadOnlyMemory<byte> mapData)
    {
        _tsbMapping = mapData;
        MarkDirty();
    }

    public void SetBsbMapping(ReadOnlyMemory<byte> mapData)
    {
        _bsbMapping = mapData;
        MarkDirty();
    }

    public void SetVorgMapping(ReadOnlyMemory<byte> mapData)
    {
        _vorgMapping = mapData;
        MarkDirty();
    }

    public static bool TryFrom(VvarTable vvar, out VvarTableBuilder builder)
    {
        builder = new VvarTableBuilder
        {
            MajorVersion = vvar.MajorVersion,
            MinorVersion = vvar.MinorVersion
        };

        var table = vvar.Table;
        int length = table.Length;

        int storeOffset = checked((int)vvar.ItemVariationStoreOffset);
        int advOffset = checked((int)vvar.AdvanceHeightMappingOffset);
        int tsbOffset = checked((int)vvar.TsbMappingOffset);
        int bsbOffset = checked((int)vvar.BsbMappingOffset);
        int vorgOffset = checked((int)vvar.VorgMappingOffset);

        if ((uint)storeOffset > (uint)length)
            return false;

        Span<(int offset, int kind)> sections = stackalloc (int, int)[5];
        int count = 0;
        sections[count++] = (storeOffset, 0);
        if (advOffset != 0) sections[count++] = (advOffset, 1);
        if (tsbOffset != 0) sections[count++] = (tsbOffset, 2);
        if (bsbOffset != 0) sections[count++] = (bsbOffset, 3);
        if (vorgOffset != 0) sections[count++] = (vorgOffset, 4);

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
                    builder._advanceHeightMapping = copied;
                    break;
                case 2:
                    builder._tsbMapping = copied;
                    break;
                case 3:
                    builder._bsbMapping = copied;
                    break;
                case 4:
                    builder._vorgMapping = copied;
                    break;
            }
        }

        builder.MarkDirty();
        return true;
    }

    private byte[] BuildTable()
    {
        if (MajorVersion != SupportedMajorVersion)
            throw new InvalidOperationException("VVAR major version must be 1.");

        if (_itemVariationStore.IsEmpty)
            throw new InvalidOperationException("VVAR requires an ItemVariationStore. Call SetItemVariationStore() or SetMinimalItemVariationStore().");

        if (_itemVariationStore.Length < 8)
            throw new InvalidOperationException("ItemVariationStore data must be at least 8 bytes.");

        int storeOffset = 24;
        int pos = checked(storeOffset + _itemVariationStore.Length);

        int advOffset = 0;
        if (!_advanceHeightMapping.IsEmpty)
        {
            advOffset = pos;
            pos = checked(pos + _advanceHeightMapping.Length);
        }

        int tsbOffset = 0;
        if (!_tsbMapping.IsEmpty)
        {
            tsbOffset = pos;
            pos = checked(pos + _tsbMapping.Length);
        }

        int bsbOffset = 0;
        if (!_bsbMapping.IsEmpty)
        {
            bsbOffset = pos;
            pos = checked(pos + _bsbMapping.Length);
        }

        int vorgOffset = 0;
        if (!_vorgMapping.IsEmpty)
        {
            vorgOffset = pos;
            pos = checked(pos + _vorgMapping.Length);
        }

        byte[] table = new byte[pos];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, MajorVersion);
        BigEndian.WriteUInt16(span, 2, MinorVersion);
        BigEndian.WriteUInt32(span, 4, checked((uint)storeOffset));
        BigEndian.WriteUInt32(span, 8, (uint)advOffset);
        BigEndian.WriteUInt32(span, 12, (uint)tsbOffset);
        BigEndian.WriteUInt32(span, 16, (uint)bsbOffset);
        BigEndian.WriteUInt32(span, 20, (uint)vorgOffset);

        _itemVariationStore.Span.CopyTo(span.Slice(storeOffset, _itemVariationStore.Length));
        if (advOffset != 0)
            _advanceHeightMapping.Span.CopyTo(span.Slice(advOffset, _advanceHeightMapping.Length));
        if (tsbOffset != 0)
            _tsbMapping.Span.CopyTo(span.Slice(tsbOffset, _tsbMapping.Length));
        if (bsbOffset != 0)
            _bsbMapping.Span.CopyTo(span.Slice(bsbOffset, _bsbMapping.Length));
        if (vorgOffset != 0)
            _vorgMapping.Span.CopyTo(span.Slice(vorgOffset, _vorgMapping.Length));

        return table;
    }
}
