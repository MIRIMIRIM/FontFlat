using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class MaxpLayout
{
    internal const uint Tag = 0x6D617870;
    internal const uint Version05 = 0x00005000, Version10 = 0x00010000;
    internal const int MinimumHeader = 4, Length05 = 6, Length10 = 32;
    internal const int VersionAt = 0, NumGlyphsAt = 4, MaxPointsAt = 6,
        MaxContoursAt = 8, MaxCompositePointsAt = 10, MaxCompositeContoursAt = 12,
        MaxZonesAt = 14, MaxTwilightPointsAt = 16, MaxStorageAt = 18,
        MaxFunctionDefsAt = 20, MaxInstructionDefsAt = 22,
        MaxStackElementsAt = 24, MaxSizeOfInstructionsAt = 26,
        MaxComponentElementsAt = 28, MaxComponentDepthAt = 30;
}

[BinaryTable(MaxpLayout.Tag, MaxpLayout.MinimumHeader, VersionField = "Version", SupportedVersions = new uint[] { MaxpLayout.Version05, MaxpLayout.Version10 })]
[BinaryScalar("Version", WireType.UInt32, MaxpLayout.VersionAt, DataField.Version)]
[BinaryScalar("NumGlyphs", WireType.UInt16, MaxpLayout.NumGlyphsAt, DataField.NumGlyphs)]
[BinaryScalar("MaxPoints", WireType.UInt16, MaxpLayout.MaxPointsAt, DataField.MaxPoints)]
[BinaryScalar("MaxContours", WireType.UInt16, MaxpLayout.MaxContoursAt, DataField.MaxContours)]
[BinaryScalar("MaxCompositePoints", WireType.UInt16, MaxpLayout.MaxCompositePointsAt, DataField.MaxCompositePoints)]
[BinaryScalar("MaxCompositeContours", WireType.UInt16, MaxpLayout.MaxCompositeContoursAt, DataField.MaxCompositeContours)]
[BinaryScalar("MaxZones", WireType.UInt16, MaxpLayout.MaxZonesAt, DataField.MaxZones)]
[BinaryScalar("MaxTwilightPoints", WireType.UInt16, MaxpLayout.MaxTwilightPointsAt, DataField.MaxTwilightPoints)]
[BinaryScalar("MaxStorage", WireType.UInt16, MaxpLayout.MaxStorageAt, DataField.MaxStorage)]
[BinaryScalar("MaxFunctionDefs", WireType.UInt16, MaxpLayout.MaxFunctionDefsAt, DataField.MaxFunctionDefs)]
[BinaryScalar("MaxInstructionDefs", WireType.UInt16, MaxpLayout.MaxInstructionDefsAt, DataField.MaxInstructionDefs)]
[BinaryScalar("MaxStackElements", WireType.UInt16, MaxpLayout.MaxStackElementsAt, DataField.MaxStackElements)]
[BinaryScalar("MaxSizeOfInstructions", WireType.UInt16, MaxpLayout.MaxSizeOfInstructionsAt, DataField.MaxSizeOfInstructions)]
[BinaryScalar("MaxComponentElements", WireType.UInt16, MaxpLayout.MaxComponentElementsAt, DataField.MaxComponentElements)]
[BinaryScalar("MaxComponentDepth", WireType.UInt16, MaxpLayout.MaxComponentDepthAt, DataField.MaxComponentDepth)]
public readonly ref partial struct MaxpView { }
