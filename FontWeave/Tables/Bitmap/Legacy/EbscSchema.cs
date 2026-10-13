using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

/// <summary>EBSC 2.0 fixed wire layout, from the OpenType EBSC chapter.</summary>
internal static class EbscLayout
{
    internal const uint Tag = 0x45425343; // 'EBSC'
    internal const ushort MajorVersion2 = 2;
    internal const int HeaderLength = 8;
    internal const int BitmapScaleAt = 8, BitmapScaleStride = 28;
    internal const int SbitLineMetricsLength = 12;
    internal const int HoriMetricsAt = 0, VertMetricsAt = 12;
    internal const int PpemXAt = 24, SubstitutePpemXAt = 26;

    internal static ulong RecordPosition(int ordinal) =>
        BitmapScaleAt + (ulong)(uint)ordinal * BitmapScaleStride;
}

// The schema owns the header and every field of the fixed BitmapScale array.
// The partial adds strict table semantics and explicit EBLC source resolution.
[BinaryTable(EbscLayout.Tag, EbscLayout.HeaderLength, ContractVersion = 1,
    VersionField = "MajorVersion", SupportedVersions = new uint[] { EbscLayout.MajorVersion2 })]
[BinaryScalar("MajorVersion", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("MinorVersion", WireType.UInt16, 2, DataField.Payload)]
[BinaryScalar("NumSizes", WireType.UInt32, 4, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("BitmapScale", "NumSizes", EbscLayout.BitmapScaleAt, EbscLayout.BitmapScaleStride)]
[BinaryElement("BitmapScale", "HoriAscender", WireType.Int8, 0, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriDescender", WireType.Int8, 1, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriWidthMax", WireType.UInt8, 2, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriCaretSlopeNumerator", WireType.Int8, 3, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriCaretSlopeDenominator", WireType.Int8, 4, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriCaretOffset", WireType.Int8, 5, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriMinOriginSB", WireType.Int8, 6, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriMinAdvanceSB", WireType.Int8, 7, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriMaxBeforeBL", WireType.Int8, 8, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriMinAfterBL", WireType.Int8, 9, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriPad1", WireType.Int8, 10, DataField.Payload)]
[BinaryElement("BitmapScale", "HoriPad2", WireType.Int8, 11, DataField.Payload)]
[BinaryElement("BitmapScale", "VertAscender", WireType.Int8, 12, DataField.Payload)]
[BinaryElement("BitmapScale", "VertDescender", WireType.Int8, 13, DataField.Payload)]
[BinaryElement("BitmapScale", "VertWidthMax", WireType.UInt8, 14, DataField.Payload)]
[BinaryElement("BitmapScale", "VertCaretSlopeNumerator", WireType.Int8, 15, DataField.Payload)]
[BinaryElement("BitmapScale", "VertCaretSlopeDenominator", WireType.Int8, 16, DataField.Payload)]
[BinaryElement("BitmapScale", "VertCaretOffset", WireType.Int8, 17, DataField.Payload)]
[BinaryElement("BitmapScale", "VertMinOriginSB", WireType.Int8, 18, DataField.Payload)]
[BinaryElement("BitmapScale", "VertMinAdvanceSB", WireType.Int8, 19, DataField.Payload)]
[BinaryElement("BitmapScale", "VertMaxBeforeBL", WireType.Int8, 20, DataField.Payload)]
[BinaryElement("BitmapScale", "VertMinAfterBL", WireType.Int8, 21, DataField.Payload)]
[BinaryElement("BitmapScale", "VertPad1", WireType.Int8, 22, DataField.Payload)]
[BinaryElement("BitmapScale", "VertPad2", WireType.Int8, 23, DataField.Payload)]
[BinaryElement("BitmapScale", "PpemX", WireType.UInt8, 24, DataField.Payload)]
[BinaryElement("BitmapScale", "PpemY", WireType.UInt8, 25, DataField.Payload)]
[BinaryElement("BitmapScale", "SubstitutePpemX", WireType.UInt8, 26, DataField.Payload)]
[BinaryElement("BitmapScale", "SubstitutePpemY", WireType.UInt8, 27, DataField.Payload)]
public readonly ref partial struct EbscView { }
