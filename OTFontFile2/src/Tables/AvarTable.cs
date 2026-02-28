using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("avar", 8, GenerateTryCreate = false)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("AxisCount", OtFieldKind.UInt16, 6)]
public readonly partial struct AvarTable
{
    public static bool TryCreate(TableSlice table, out AvarTable avar)
    {
        avar = default;

        // version(4) + reserved(2) + axisCount(2)
        if (table.Length < 8)
            return false;

        var data = table.Span;
        uint version = BigEndian.ReadUInt32(data, 0);

        // Currently only avar 1.0 is supported.
        if (version != 0x00010000u)
            return false;

        ushort axisCount = BigEndian.ReadUInt16(data, 6);

        // Validate segment maps are in-bounds by scanning once.
        int pos = 8;
        for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
        {
            if ((uint)pos > (uint)table.Length - 2)
                return false;

            ushort mapCount = BigEndian.ReadUInt16(data, pos);
            pos += 2;

            long mapsBytesLong = (long)mapCount * 4;
            if (mapsBytesLong > int.MaxValue)
                return false;

            int mapsBytes = (int)mapsBytesLong;
            if ((uint)pos > (uint)table.Length - (uint)mapsBytes)
                return false;

            pos += mapsBytes;
        }

        avar = new AvarTable(table);
        return true;
    }

    public bool TryGetSegmentMap(int axisIndex, out SegmentMap segmentMap)
    {
        segmentMap = default;

        if ((uint)axisIndex >= AxisCount)
            return false;

        var data = _table.Span;
        int pos = 8;

        for (int i = 0; i < axisIndex; i++)
        {
            if ((uint)pos > (uint)_table.Length - 2)
                return false;

            ushort mapCount = BigEndian.ReadUInt16(data, pos);
            pos += 2;

            long mapsBytesLong = (long)mapCount * 4;
            if (mapsBytesLong > int.MaxValue)
                return false;

            int mapsBytes = (int)mapsBytesLong;
            if ((uint)pos > (uint)_table.Length - (uint)mapsBytes)
                return false;

            pos += mapsBytes;
        }

        if ((uint)pos > (uint)_table.Length - 2)
            return false;

        return SegmentMap.TryCreate(_table, pos, out segmentMap);
    }

    [OtSubTable(2)]
    [OtField("PositionMapCount", OtFieldKind.UInt16, 0)]
    [OtSequentialRecordArray("AxisValueMap", 2, 4, CountPropertyName = "PositionMapCount")]
    public readonly partial struct SegmentMap
    {
    }

    public readonly struct AxisValueMap
    {
        public F2Dot14 FromCoordinate { get; }
        public F2Dot14 ToCoordinate { get; }

        public AxisValueMap(F2Dot14 fromCoordinate, F2Dot14 toCoordinate)
        {
            FromCoordinate = fromCoordinate;
            ToCoordinate = toCoordinate;
        }
    }
}
