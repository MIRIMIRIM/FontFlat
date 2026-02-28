using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("EBSC", 8)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("SizeCount", OtFieldKind.UInt32, 4)]
public readonly partial struct EbscTable
{
    [OtSubTable(28)]
    [OtField("PpemX", OtFieldKind.Byte, 24)]
    [OtField("PpemY", OtFieldKind.Byte, 25)]
    [OtField("SubstitutePpemX", OtFieldKind.Byte, 26)]
    [OtField("SubstitutePpemY", OtFieldKind.Byte, 27)]
    public readonly partial struct BitmapScale
    {
        public SbitLineMetrics Hori => SbitLineMetrics.CreateUnchecked(_table, _offset + 0);
        public SbitLineMetrics Vert => SbitLineMetrics.CreateUnchecked(_table, _offset + 12);
    }

    public bool TryGetBitmapScale(int index, out BitmapScale scale)
    {
        scale = default;

        uint count = SizeCount;
        if (count > int.MaxValue)
            return false;
        if ((uint)index >= count)
            return false;

        long offsetLong = 8L + ((long)index * 28);
        if (offsetLong > int.MaxValue)
            return false;

        return BitmapScale.TryCreate(_table, (int)offsetLong, out scale);
    }
}
