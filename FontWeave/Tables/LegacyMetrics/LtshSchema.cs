using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class LtshLayout
{
    internal const uint Tag = 0x4C545348;
    internal const int HeaderLength = 4;
}

[BinaryTable(LtshLayout.Tag, LtshLayout.HeaderLength, VersionField = "Version", SupportedVersions = new uint[] { 0 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("NumGlyphs", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("Threshold", "NumGlyphs", LtshLayout.HeaderLength, 1)]
[BinaryElement("Threshold", "YPixels", WireType.UInt8, 0, DataField.Payload)]
public readonly ref partial struct LtshView { }
