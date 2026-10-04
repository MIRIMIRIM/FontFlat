using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class LayoutTagListLayout
{
    internal const int HeaderLength = 2, RecordStride = 6, RecordOffsetField = 4;
}

internal static class LayoutScriptLayout
{
    internal const int HeaderLength = 4, DefaultOffsetField = 0, CountOffset = 2;
    internal const uint DefaultTag = 0x44464C54;
}

internal static class LayoutLangSysLayout
{
    internal const int HeaderLength = 6, RequiredOffset = 2, CountOffset = 4, IndexStride = 2;
}

internal static class LayoutFeatureLayout
{
    internal const int HeaderLength = 4, CountOffset = 2, IndexStride = 2;
}

internal static class LayoutLookupListLayout
{
    internal const int HeaderLength = 2, OffsetStride = 2;
}

internal static class LayoutListsLayout
{
    internal const uint Version10 = 0x00010000, Version11 = 0x00010001;
    internal const int Header10 = 10, Header11 = 14;
    internal const int ScriptOffsetField = 4, FeatureOffsetField = 6, LookupOffsetField = 8;
    internal const int VariationsOffsetField = 10, VariationsStride = 4;
}

// ScriptList and FeatureList have one shared structural definition. Their tag
// ordering and child contexts belong to the semantic partial, not another reader.
// Contract version 1 makes the counted record arrays generated contracts: the
// generated ordinal/extent accessors and the fixed-start counted writer replace
// the handwritten row arithmetic in the semantic partials.
[BinaryTable(0, LayoutTagListLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("TagRecord", "Count", LayoutTagListLayout.HeaderLength, LayoutTagListLayout.RecordStride)]
[BinaryElement("TagRecord", "Tag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("TagRecord", "Offset", WireType.UInt16, LayoutTagListLayout.RecordOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
public readonly ref partial struct LayoutTagListView { }

[BinaryTable(0, LayoutScriptLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("DefaultLanguageOffset", WireType.UInt16, LayoutScriptLayout.DefaultOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("LanguageCount", WireType.UInt16, LayoutScriptLayout.CountOffset, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("LanguageRecord", "LanguageCount", LayoutScriptLayout.HeaderLength, LayoutTagListLayout.RecordStride)]
[BinaryElement("LanguageRecord", "Tag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("LanguageRecord", "Offset", WireType.UInt16, LayoutTagListLayout.RecordOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
public readonly ref partial struct LayoutScriptView { }

[BinaryTable(0, LayoutLangSysLayout.HeaderLength)]
[BinaryScalar("LookupOrderOffset", WireType.UInt16, 0, DataField.Offset)]
[BinaryScalar("RequiredFeatureIndex", WireType.UInt16, LayoutLangSysLayout.RequiredOffset, DataField.Payload)]
[BinaryScalar("FeatureCount", WireType.UInt16, LayoutLangSysLayout.CountOffset, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryRecord("FeatureIndex", LayoutLangSysLayout.IndexStride)]
[BinaryElement("FeatureIndex", "Index", WireType.UInt16, 0, DataField.Payload)]
public readonly ref partial struct LayoutLangSysView { }

[BinaryTable(0, LayoutFeatureLayout.HeaderLength)]
[BinaryScalar("ParamsOffsetRaw", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("LookupCount", WireType.UInt16, LayoutFeatureLayout.CountOffset, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryRecord("LookupIndex", LayoutFeatureLayout.IndexStride)]
[BinaryElement("LookupIndex", "Index", WireType.UInt16, 0, DataField.Payload)]
public readonly ref partial struct LayoutFeatureView { }

[BinaryTable(0, LayoutLookupListLayout.HeaderLength)]
[BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryRecord("LookupOffset", LayoutLookupListLayout.OffsetStride)]
[BinaryElement("LookupOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
public readonly ref partial struct LayoutLookupListView { }

[BinaryTable(0, LayoutListsLayout.Header10, VersionField = "Version",
    SupportedVersions = new uint[] { LayoutListsLayout.Version10, LayoutListsLayout.Version11 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("ScriptListOffset", WireType.UInt16, LayoutListsLayout.ScriptOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("FeatureListOffset", WireType.UInt16, LayoutListsLayout.FeatureOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("LookupListOffset", WireType.UInt16, LayoutListsLayout.LookupOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryRecord("VariationsHeader", LayoutListsLayout.VariationsStride)]
[BinaryElement("VariationsHeader", "OffsetRaw", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
public readonly ref partial struct LayoutListsView { }
