using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class PcltLayout
{
    internal const uint Tag = 0x50434C54;
    internal const uint Version = 0x00010000;
    internal const int Length = 54;
}

[BinaryTable(PcltLayout.Tag, PcltLayout.Length, VersionField = "VersionRaw", SupportedVersions = new uint[] { PcltLayout.Version })]
[BinaryScalar("VersionRaw", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("FontNumber", WireType.UInt32, 4, DataField.Payload)]
[BinaryScalar("Pitch", WireType.UInt16, 8, DataField.Payload)]
[BinaryScalar("XHeight", WireType.UInt16, 10, DataField.Payload)]
[BinaryScalar("Style", WireType.UInt16, 12, DataField.Flags)]
[BinaryScalar("TypeFamily", WireType.UInt16, 14, DataField.Payload)]
[BinaryScalar("CapHeight", WireType.UInt16, 16, DataField.Payload)]
[BinaryScalar("SymbolSet", WireType.UInt16, 18, DataField.Payload)]
[BinaryScalar("Typeface0", WireType.UInt8, 20, DataField.Payload)]
[BinaryScalar("Typeface1", WireType.UInt8, 21, DataField.Payload)]
[BinaryScalar("Typeface2", WireType.UInt8, 22, DataField.Payload)]
[BinaryScalar("Typeface3", WireType.UInt8, 23, DataField.Payload)]
[BinaryScalar("Typeface4", WireType.UInt8, 24, DataField.Payload)]
[BinaryScalar("Typeface5", WireType.UInt8, 25, DataField.Payload)]
[BinaryScalar("Typeface6", WireType.UInt8, 26, DataField.Payload)]
[BinaryScalar("Typeface7", WireType.UInt8, 27, DataField.Payload)]
[BinaryScalar("Typeface8", WireType.UInt8, 28, DataField.Payload)]
[BinaryScalar("Typeface9", WireType.UInt8, 29, DataField.Payload)]
[BinaryScalar("Typeface10", WireType.UInt8, 30, DataField.Payload)]
[BinaryScalar("Typeface11", WireType.UInt8, 31, DataField.Payload)]
[BinaryScalar("Typeface12", WireType.UInt8, 32, DataField.Payload)]
[BinaryScalar("Typeface13", WireType.UInt8, 33, DataField.Payload)]
[BinaryScalar("Typeface14", WireType.UInt8, 34, DataField.Payload)]
[BinaryScalar("Typeface15", WireType.UInt8, 35, DataField.Payload)]
[BinaryScalar("CharacterComplementHigh", WireType.UInt32, 36, DataField.Payload)]
[BinaryScalar("CharacterComplementLow", WireType.UInt32, 40, DataField.Payload)]
[BinaryScalar("FileName0", WireType.UInt8, 44, DataField.Payload)]
[BinaryScalar("FileName1", WireType.UInt8, 45, DataField.Payload)]
[BinaryScalar("FileName2", WireType.UInt8, 46, DataField.Payload)]
[BinaryScalar("FileName3", WireType.UInt8, 47, DataField.Payload)]
[BinaryScalar("FileName4", WireType.UInt8, 48, DataField.Payload)]
[BinaryScalar("FileName5", WireType.UInt8, 49, DataField.Payload)]
[BinaryScalar("StrokeWeight", WireType.Int8, 50, DataField.Payload)]
[BinaryScalar("WidthType", WireType.Int8, 51, DataField.Payload)]
[BinaryScalar("SerifStyle", WireType.UInt8, 52, DataField.Payload)]
[BinaryScalar("Reserved", WireType.UInt8, 53, DataField.Payload)]
public readonly ref partial struct PcltView { }
