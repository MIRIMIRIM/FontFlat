using FontWeave.Schema;

using FontWeave.Core;

namespace FontWeave.Tables;

[BinaryTable(0x68656164, 4, VersionField = "Version", SupportedVersions = new uint[] { 0x00010000 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("FontRevision", WireType.Int32, 4, DataField.None, Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryScalar("MagicNumber", WireType.UInt32, 12, DataField.MagicNumber)]
[BinaryScalar("UnitsPerEm", WireType.UInt16, 18, DataField.UnitsPerEm)]
public readonly ref partial struct HeadView { }
