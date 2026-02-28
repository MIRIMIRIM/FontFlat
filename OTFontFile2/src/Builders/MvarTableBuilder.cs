using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>MVAR</c> table.
/// </summary>
[OtTableBuilder("MVAR")]
public sealed partial class MvarTableBuilder : ISfntTableSource
{
    private const ushort SupportedMajorVersion = 1;
    private const ushort DefaultMinorVersion = 0;
    private const ushort ValueRecordSize = 8;

    private readonly List<ValueRecord> _records = new();

    private ushort _majorVersion = SupportedMajorVersion;
    private ushort _minorVersion = DefaultMinorVersion;
    private ReadOnlyMemory<byte> _itemVariationStore = default;

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

    public int ValueRecordCount => _records.Count;

    public IReadOnlyList<ValueRecord> ValueRecords => _records;

    public ReadOnlyMemory<byte> ItemVariationStoreData => _itemVariationStore;

    public void ClearValueRecords()
    {
        if (_records.Count == 0)
            return;

        _records.Clear();
        MarkDirty();
    }

    public void AddValueRecord(Tag valueTag, VarIdx deltaSetIndex)
    {
        _records.Add(new ValueRecord(valueTag, deltaSetIndex));
        MarkDirty();
    }

    public bool RemoveValueRecordAt(int index)
    {
        if ((uint)index >= (uint)_records.Count)
            return false;

        _records.RemoveAt(index);
        MarkDirty();
        return true;
    }

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

    public static bool TryFrom(MvarTable mvar, out MvarTableBuilder builder)
    {
        builder = new MvarTableBuilder
        {
            MajorVersion = mvar.MajorVersion,
            MinorVersion = mvar.MinorVersion
        };

        int count = mvar.ValueRecordCount;
        for (int i = 0; i < count; i++)
        {
            if (!mvar.TryGetValueRecord(i, out var record))
                return false;

            builder._records.Add(new ValueRecord(record.ValueTag, record.DeltaSetIndex));
        }

        int storeOffset = checked((int)mvar.ItemVariationStoreOffset);
        if ((uint)storeOffset > (uint)mvar.Table.Length)
            return false;

        builder._itemVariationStore = mvar.Table.Span.Slice(storeOffset).ToArray();
        builder.MarkDirty();
        return true;
    }

    private byte[] BuildTable()
    {
        if (MajorVersion != SupportedMajorVersion)
            throw new InvalidOperationException("MVAR major version must be 1.");

        if (_records.Count > ushort.MaxValue)
            throw new InvalidOperationException("MVAR valueRecordCount must fit in uint16.");

        if (_itemVariationStore.IsEmpty)
            throw new InvalidOperationException("MVAR requires an ItemVariationStore. Call SetItemVariationStore() or SetMinimalItemVariationStore().");

        int recordBytes = checked(_records.Count * ValueRecordSize);
        int storeOffset = checked(12 + recordBytes);

        if (_itemVariationStore.Length < 8)
            throw new InvalidOperationException("ItemVariationStore data must be at least 8 bytes.");

        int length = checked(storeOffset + _itemVariationStore.Length);

        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, MajorVersion);
        BigEndian.WriteUInt16(span, 2, MinorVersion);
        BigEndian.WriteUInt32(span, 4, checked((uint)storeOffset));
        BigEndian.WriteUInt16(span, 8, ValueRecordSize);
        BigEndian.WriteUInt16(span, 10, checked((ushort)_records.Count));

        int pos = 12;
        for (int i = 0; i < _records.Count; i++)
        {
            var r = _records[i];
            BigEndian.WriteUInt32(span, pos + 0, r.ValueTag.Value);
            BigEndian.WriteUInt16(span, pos + 4, r.DeltaSetIndex.OuterIndex);
            BigEndian.WriteUInt16(span, pos + 6, r.DeltaSetIndex.InnerIndex);
            pos += ValueRecordSize;
        }

        _itemVariationStore.Span.CopyTo(span.Slice(storeOffset, _itemVariationStore.Length));

        return table;
    }

    public readonly struct ValueRecord
    {
        public Tag ValueTag { get; }
        public VarIdx DeltaSetIndex { get; }

        public ValueRecord(Tag valueTag, VarIdx deltaSetIndex)
        {
            ValueTag = valueTag;
            DeltaSetIndex = deltaSetIndex;
        }
    }
}
