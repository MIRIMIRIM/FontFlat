using FontWeave.Schema;

using FontWeave.Core;

namespace FontWeave.Tables;

[BinaryTable(0x6E616D65, 6, ContractVersion = 1, VersionField = "Format", SupportedVersions = new uint[] { 0, 1 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("DeclaredCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("StorageOffset", WireType.UInt16, 4, DataField.StorageOffset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryDerived("RecordsEnd", Constant = 6, Count = "DeclaredCount", Stride = 12)]
[BinaryScalar("LanguageTagCount", WireType.UInt16, 0, DataField.Count, Position = "RecordsEnd", Semantic = NumberSemantic.Count,
    WhenVersion = 1, Otherwise = 0)]
[BinaryDerived("MetadataEnd", Base = "RecordsEnd", Constant = 2, Count = "LanguageTagCount", Stride = 4,
    WhenVersion = 1, Otherwise = "RecordsEnd")]
[BinaryArray("Record", "DeclaredCount", 6, 12)]
[BinaryElement("Record", "PlatformId", WireType.UInt16, 0, DataField.None)]
[BinaryElement("Record", "EncodingId", WireType.UInt16, 2, DataField.EncodingId)]
[BinaryElement("Record", "LanguageId", WireType.UInt16, 4, DataField.LanguageId)]
[BinaryElement("Record", "NameId", WireType.UInt16, 6, DataField.None)]
[BinaryElement("Record", "Length", WireType.UInt16, 8, DataField.Length, Semantic = NumberSemantic.Count)]
[BinaryElement("Record", "StorageRelativeOffset", WireType.UInt16, 10, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.NameStorage)]
[BinaryArray("LanguageTag", "LanguageTagCount", 2, 4, Start = "RecordsEnd")]
[BinaryElement("LanguageTag", "Length", WireType.UInt16, 0, DataField.Length, Semantic = NumberSemantic.Count)]
[BinaryElement("LanguageTag", "StorageRelativeOffset", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.NameStorage)]
[BinaryPayload("Record", "StorageRelativeOffset", "Length", "StorageOffset", "MetadataEnd")]
[BinaryPayload("LanguageTag", "StorageRelativeOffset", "Length", "StorageOffset", "MetadataEnd")]
public readonly ref partial struct NameView { }
