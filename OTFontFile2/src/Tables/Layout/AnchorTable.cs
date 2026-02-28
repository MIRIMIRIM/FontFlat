using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GPOS Anchor table (AnchorFormat 1/2/3).
/// </summary>
[OtSubTable(6)]
[OtField("AnchorFormat", OtFieldKind.UInt16, 0)]
[OtField("XCoordinate", OtFieldKind.Int16, 2)]
[OtField("YCoordinate", OtFieldKind.Int16, 4)]
[OtDiscriminant(nameof(AnchorFormat))]
[OtCase(1, typeof(AnchorTable.Format1), Name = "Format1")]
[OtCase(2, typeof(AnchorTable.Format2), Name = "Format2")]
[OtCase(3, typeof(AnchorTable.Format3), Name = "Format3")]
public readonly partial struct AnchorTable
{
    public bool TryGetAnchorPoint(out ushort anchorPoint)
    {
        anchorPoint = 0;

        if (!TryGetFormat2(out var format2))
            return false;

        anchorPoint = format2.AnchorPoint;
        return true;
    }

    public bool TryGetXDeviceTableOffset(out ushort deviceOffset)
    {
        deviceOffset = 0;

        if (!TryGetFormat3(out var format3))
            return false;

        deviceOffset = format3.XDeviceTableOffset;
        return true;
    }

    public bool TryGetYDeviceTableOffset(out ushort deviceOffset)
    {
        deviceOffset = 0;

        if (!TryGetFormat3(out var format3))
            return false;

        deviceOffset = format3.YDeviceTableOffset;
        return true;
    }

    public bool TryGetXDeviceTableAbsoluteOffset(out int absoluteOffset)
    {
        absoluteOffset = 0;
        if (!TryGetXDeviceTableOffset(out ushort rel) || rel == 0)
            return false;

        int abs = checked(_offset + rel);
        if ((uint)abs >= (uint)_table.Length)
            return false;

        absoluteOffset = abs;
        return true;
    }

    public bool TryGetYDeviceTableAbsoluteOffset(out int absoluteOffset)
    {
        absoluteOffset = 0;
        if (!TryGetYDeviceTableOffset(out ushort rel) || rel == 0)
            return false;

        int abs = checked(_offset + rel);
        if ((uint)abs >= (uint)_table.Length)
            return false;

        absoluteOffset = abs;
        return true;
    }

    [OtSubTable(6)]
    [OtField("AnchorFormat", OtFieldKind.UInt16, 0)]
    [OtField("XCoordinate", OtFieldKind.Int16, 2)]
    [OtField("YCoordinate", OtFieldKind.Int16, 4)]
    public readonly partial struct Format1
    {
    }

    [OtSubTable(8)]
    [OtField("AnchorFormat", OtFieldKind.UInt16, 0)]
    [OtField("XCoordinate", OtFieldKind.Int16, 2)]
    [OtField("YCoordinate", OtFieldKind.Int16, 4)]
    [OtField("AnchorPoint", OtFieldKind.UInt16, 6)]
    public readonly partial struct Format2
    {
    }

    [OtSubTable(10)]
    [OtField("AnchorFormat", OtFieldKind.UInt16, 0)]
    [OtField("XCoordinate", OtFieldKind.Int16, 2)]
    [OtField("YCoordinate", OtFieldKind.Int16, 4)]
    [OtField("XDeviceTableOffset", OtFieldKind.UInt16, 6)]
    [OtField("YDeviceTableOffset", OtFieldKind.UInt16, 8)]
    public readonly partial struct Format3
    {
    }
}
