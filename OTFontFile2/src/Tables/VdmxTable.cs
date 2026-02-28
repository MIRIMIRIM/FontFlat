using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("VDMX", 6)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("GroupCount", OtFieldKind.UInt16, 2)]
[OtField("RatioCount", OtFieldKind.UInt16, 4)]
[OtSequentialRecordArray("Ratio", 6, 4, RecordTypeName = "RatioRecord")]
public readonly partial struct VdmxTable
{
    public readonly struct RatioRecord
    {
        public byte CharSet { get; }
        public byte XRatio { get; }
        public byte YStartRatio { get; }
        public byte YEndRatio { get; }

        public RatioRecord(byte charSet, byte xRatio, byte yStartRatio, byte yEndRatio)
        {
            CharSet = charSet;
            XRatio = xRatio;
            YStartRatio = yStartRatio;
            YEndRatio = yEndRatio;
        }
    }

    public bool TryGetGroupOffsetForRatio(int ratioIndex, out ushort offset)
    {
        offset = 0;

        ushort ratioCount = RatioCount;
        if ((uint)ratioIndex >= (uint)ratioCount)
            return false;

        long offsetsBase = 6 + ((long)ratioCount * 4);
        long o = offsetsBase + ((long)ratioIndex * 2);
        if (o < 0 || o > _table.Length - 2)
            return false;

        offset = BigEndian.ReadUInt16(_table.Span, (int)o);
        return true;
    }

    [OtSubTable(4)]
    [OtField("EntryCount", OtFieldKind.UInt16, 0)]
    [OtField("StartSize", OtFieldKind.Byte, 2)]
    [OtField("EndSize", OtFieldKind.Byte, 3)]
    [OtSequentialRecordArray("Entry", 4, 6)]
    public readonly partial struct Group
    {
        public readonly struct Entry
        {
            public ushort YPelHeight { get; }
            public short YMax { get; }
            public short YMin { get; }

            public Entry(ushort yPelHeight, short yMax, short yMin)
            {
                YPelHeight = yPelHeight;
                YMax = yMax;
                YMin = yMin;
            }
        }
    }

    public bool TryGetGroupForRatio(int ratioIndex, out Group group)
    {
        group = default;

        if (!TryGetGroupOffsetForRatio(ratioIndex, out ushort offsetU16))
            return false;

        int offset = offsetU16;
        return Group.TryCreate(_table, offset, out group);
    }
}
