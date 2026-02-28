using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(8, GenerateTryCreate = false)]
[OtField("Format", OtFieldKind.UInt16, 0)]
[OtField("VariationRegionListOffset", OtFieldKind.UInt32, 2)]
[OtField("ItemVariationDataCount", OtFieldKind.UInt16, 6)]
public readonly partial struct ItemVariationStore
{
    public static bool TryCreate(TableSlice table, int offset, out ItemVariationStore store)
    {
        store = default;

        // format(2) + variationRegionListOffset(4) + itemVariationDataCount(2)
        if ((uint)offset > (uint)table.Length - 8)
            return false;

        var data = table.Span;
        ushort format = BigEndian.ReadUInt16(data, offset + 0);
        if (format != 1)
            return false;

        uint regionListOffsetU = BigEndian.ReadUInt32(data, offset + 2);
        if (regionListOffsetU > int.MaxValue)
            return false;

        int regionListOffset = (int)regionListOffsetU;
        ushort dataCount = BigEndian.ReadUInt16(data, offset + 6);

        long offsetsBytesLong = 8L + (dataCount * 4L);
        if (offsetsBytesLong > int.MaxValue)
            return false;

        int offsetsBytes = (int)offsetsBytesLong;
        if ((uint)offset > (uint)table.Length - (uint)offsetsBytes)
            return false;

        int regionAbs = checked(offset + regionListOffset);
        if ((uint)regionAbs > (uint)table.Length - 4)
            return false;

        store = new ItemVariationStore(table, offset);
        return true;
    }

    public bool TryGetByteLength(out int byteLength)
    {
        byteLength = 0;

        int start = _offset;
        var data = _table.Span;
        if ((uint)start > (uint)data.Length - 8)
            return false;

        ushort itemVariationDataCount = ItemVariationDataCount;
        int maxEnd = start + 8 + (itemVariationDataCount * 4);

        int regionAbs = checked(start + (int)VariationRegionListOffset);
        if (!VariationRegionList.TryCreate(_table, regionAbs, out var regionList))
            return false;

        long regionBytesLong = 4L + (long)regionList.RegionCount * regionList.AxisCount * 6;
        if (regionBytesLong > int.MaxValue)
            return false;

        int regionEnd = checked(regionAbs + (int)regionBytesLong);
        if (regionEnd > maxEnd)
            maxEnd = regionEnd;

        for (int i = 0; i < itemVariationDataCount; i++)
        {
            int offsetsArrayOffset = start + 8;
            int entryOffset = checked(offsetsArrayOffset + (i * 4));
            if ((uint)entryOffset > (uint)data.Length - 4)
                return false;

            uint relU = BigEndian.ReadUInt32(data, entryOffset);
            if (relU > int.MaxValue)
                return false;

            int abs = checked(start + (int)relU);
            if ((uint)abs > (uint)data.Length - 6)
                return false;

            ushort itemCount = BigEndian.ReadUInt16(data, abs + 0);
            ushort shortDeltaCount = BigEndian.ReadUInt16(data, abs + 2);
            ushort regionIndexCount = BigEndian.ReadUInt16(data, abs + 4);

            if (shortDeltaCount > regionIndexCount)
                return false;

            int deltaSetRecordSize = checked((shortDeltaCount * 2) + (regionIndexCount - shortDeltaCount));

            long itemVarDataLenLong = 6L + (regionIndexCount * 2L) + (itemCount * (long)deltaSetRecordSize);
            if (itemVarDataLenLong > int.MaxValue)
                return false;

            int end = checked(abs + (int)itemVarDataLenLong);
            if (end > data.Length)
                return false;

            if (end > maxEnd)
                maxEnd = end;
        }

        byteLength = maxEnd - start;
        return byteLength >= 0;
    }

    public bool TryGetVariationRegionList(out VariationRegionList regionList)
        => VariationRegionList.TryCreate(_table, checked(_offset + (int)VariationRegionListOffset), out regionList);

    public bool TryGetItemVariationData(int index, out ItemVariationData itemVariationData)
    {
        itemVariationData = default;

        ushort itemVariationDataCount = ItemVariationDataCount;
        if ((uint)index >= itemVariationDataCount)
            return false;

        int offsetsArrayOffset = _offset + 8;
        int entryOffset = checked(offsetsArrayOffset + (index * 4));
        if ((uint)entryOffset > (uint)_table.Length - 4)
            return false;

        uint relU = BigEndian.ReadUInt32(_table.Span, entryOffset);
        if (relU > int.MaxValue)
            return false;

        int abs = checked(_offset + (int)relU);
        return ItemVariationData.TryCreate(_table, abs, out itemVariationData);
    }

    [OtSubTable(4, GenerateTryCreate = false)]
    [OtField("AxisCount", OtFieldKind.UInt16, 0)]
    [OtField("RegionCount", OtFieldKind.UInt16, 2)]
    public readonly partial struct VariationRegionList
    {
        public static bool TryCreate(TableSlice table, int offset, out VariationRegionList regionList)
        {
            regionList = default;

            // axisCount(2) + regionCount(2)
            if ((uint)offset > (uint)table.Length - 4)
                return false;

            var data = table.Span;
            ushort axisCount = BigEndian.ReadUInt16(data, offset + 0);
            ushort regionCount = BigEndian.ReadUInt16(data, offset + 2);

            long regionBytesLong = (long)regionCount * axisCount * 6;
            if (regionBytesLong > int.MaxValue)
                return false;

            int regionBytes = (int)regionBytesLong;
            int regionsOffset = offset + 4;
            if ((uint)regionsOffset > (uint)table.Length - (uint)regionBytes)
                return false;

            regionList = new VariationRegionList(table, offset);
            return true;
        }

        public bool TryGetRegion(int regionIndex, out VariationRegion region)
        {
            region = default;

            ushort regionCount = RegionCount;
            if ((uint)regionIndex >= regionCount)
                return false;

            ushort axisCount = AxisCount;
            int regionSize = axisCount * 6;
            int offset = checked(_offset + 4 + (regionIndex * regionSize));
            if ((uint)offset > (uint)_table.Length - (uint)regionSize)
                return false;

            region = new VariationRegion(_table, offset, axisCount);
            return true;
        }
    }

    public readonly struct VariationRegion
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly ushort _axisCount;

        internal VariationRegion(TableSlice table, int offset, ushort axisCount)
        {
            _table = table;
            _offset = offset;
            _axisCount = axisCount;
        }

        public ushort AxisCount => _axisCount;

        public bool TryGetAxisCoordinates(int axisIndex, out RegionAxisCoordinates coords)
        {
            coords = default;

            if ((uint)axisIndex >= _axisCount)
                return false;

            int offset = checked(_offset + (axisIndex * 6));
            if ((uint)offset > (uint)_table.Length - 6)
                return false;

            var data = _table.Span;
            coords = new RegionAxisCoordinates(
                startCoord: new F2Dot14(BigEndian.ReadInt16(data, offset + 0)),
                peakCoord: new F2Dot14(BigEndian.ReadInt16(data, offset + 2)),
                endCoord: new F2Dot14(BigEndian.ReadInt16(data, offset + 4)));
            return true;
        }
    }

    public readonly struct RegionAxisCoordinates
    {
        public F2Dot14 StartCoord { get; }
        public F2Dot14 PeakCoord { get; }
        public F2Dot14 EndCoord { get; }

        public RegionAxisCoordinates(F2Dot14 startCoord, F2Dot14 peakCoord, F2Dot14 endCoord)
        {
            StartCoord = startCoord;
            PeakCoord = peakCoord;
            EndCoord = endCoord;
        }
    }

    [OtSubTable(6, GenerateTryCreate = false, GenerateStorage = false)]
    [OtField("ItemCount", OtFieldKind.UInt16, 0)]
    [OtField("ShortDeltaCount", OtFieldKind.UInt16, 2)]
    [OtField("RegionIndexCount", OtFieldKind.UInt16, 4)]
    public readonly partial struct ItemVariationData
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly int _regionIndicesOffset;
        private readonly int _deltaSetsOffset;
        private readonly int _deltaSetRecordSize;

        private ItemVariationData(
            TableSlice table,
            int offset,
            int regionIndicesOffset,
            int deltaSetsOffset,
            int deltaSetRecordSize)
        {
            _table = table;
            _offset = offset;
            _regionIndicesOffset = regionIndicesOffset;
            _deltaSetsOffset = deltaSetsOffset;
            _deltaSetRecordSize = deltaSetRecordSize;
        }

        public static bool TryCreate(TableSlice table, int offset, out ItemVariationData itemVariationData)
        {
            itemVariationData = default;

            // itemCount(2) + shortDeltaCount(2) + regionIndexCount(2)
            if ((uint)offset > (uint)table.Length - 6)
                return false;

            var data = table.Span;
            ushort itemCount = BigEndian.ReadUInt16(data, offset + 0);
            ushort shortDeltaCount = BigEndian.ReadUInt16(data, offset + 2);
            ushort regionIndexCount = BigEndian.ReadUInt16(data, offset + 4);

            if (shortDeltaCount > regionIndexCount)
                return false;

            long regionIndicesBytesLong = (long)regionIndexCount * 2;
            if (regionIndicesBytesLong > int.MaxValue)
                return false;

            int regionIndicesOffset = checked(offset + 6);
            int regionIndicesBytes = (int)regionIndicesBytesLong;
            if ((uint)regionIndicesOffset > (uint)table.Length - (uint)regionIndicesBytes)
                return false;

            int deltaSetsOffset = checked(regionIndicesOffset + regionIndicesBytes);
            int deltaSetRecordSize = checked((shortDeltaCount * 2) + (regionIndexCount - shortDeltaCount));

            long deltaSetsBytesLong = (long)itemCount * deltaSetRecordSize;
            if (deltaSetsBytesLong > int.MaxValue)
                return false;

            int deltaSetsBytes = (int)deltaSetsBytesLong;
            if ((uint)deltaSetsOffset > (uint)table.Length - (uint)deltaSetsBytes)
                return false;

            itemVariationData = new ItemVariationData(
                table,
                offset,
                regionIndicesOffset,
                deltaSetsOffset,
                deltaSetRecordSize);
            return true;
        }

        public int DeltaSetRecordSize => _deltaSetRecordSize;

        public bool TryGetRegionIndex(int index, out ushort regionIndex)
        {
            regionIndex = 0;

            ushort regionIndexCount = RegionIndexCount;
            if ((uint)index >= regionIndexCount)
                return false;

            int offset = checked(_regionIndicesOffset + (index * 2));
            if ((uint)offset > (uint)_table.Length - 2)
                return false;

            regionIndex = BigEndian.ReadUInt16(_table.Span, offset);
            return true;
        }

        public bool TryGetDelta(int itemIndex, int regionDeltaIndex, out int delta)
        {
            delta = 0;

            ushort itemCount = ItemCount;
            if ((uint)itemIndex >= itemCount)
                return false;

            ushort regionIndexCount = RegionIndexCount;
            if ((uint)regionDeltaIndex >= regionIndexCount)
                return false;

            int recordOffset = checked(_deltaSetsOffset + (itemIndex * _deltaSetRecordSize));

            ushort shortDeltaCount = ShortDeltaCount;
            if (regionDeltaIndex < shortDeltaCount)
            {
                int deltaOffset = checked(recordOffset + (regionDeltaIndex * 2));
                if ((uint)deltaOffset > (uint)_table.Length - 2)
                    return false;

                delta = BigEndian.ReadInt16(_table.Span, deltaOffset);
                return true;
            }

            int byteIndex = regionDeltaIndex - shortDeltaCount;
            int deltaByteOffset = checked(recordOffset + (shortDeltaCount * 2) + byteIndex);
            if ((uint)deltaByteOffset >= (uint)_table.Length)
                return false;

            delta = unchecked((sbyte)_table.Span[deltaByteOffset]);
            return true;
        }
    }
}
