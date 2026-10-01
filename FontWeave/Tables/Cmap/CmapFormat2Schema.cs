using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat2Layout
{
    internal const int HeaderLength = 6, KeyStride = 2, SubHeaderStride = 8;
    internal const int KeyCount = 256, KeysEnd = 518;
}

[BinaryTable(CmapLayout.Tag, CmapFormat2Layout.HeaderLength, VersionField = "Format", SupportedVersions = new uint[] { 2 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Length", WireType.UInt16, 2, DataField.Length)]
[BinaryScalar("Language", WireType.UInt16, 4, DataField.LanguageId)]
[BinaryRecord("SubHeaderKey", CmapFormat2Layout.KeyStride)]
[BinaryElement("SubHeaderKey", "Value", WireType.UInt16, 0, DataField.Offset)]
[BinaryRecord("SubHeader", CmapFormat2Layout.SubHeaderStride)]
[BinaryElement("SubHeader", "FirstCode", WireType.UInt16, 0, DataField.CharCode)]
[BinaryElement("SubHeader", "EntryCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryElement("SubHeader", "IdDelta", WireType.Int16, 4, DataField.Payload)]
[BinaryElement("SubHeader", "IdRangeOffset", WireType.UInt16, 6, DataField.Offset)]
public readonly ref partial struct CmapFormat2View { }
