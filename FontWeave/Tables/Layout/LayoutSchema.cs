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
[BinaryTable(0, LayoutTagListLayout.HeaderLength, ContractVersion = 1,
    RoleType = typeof(LayoutTagListRole),
    SupportedRoles = new long[] { (long)LayoutTagListRole.ScriptList, (long)LayoutTagListRole.FeatureList })]
[BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("TagRecord", "Count", LayoutTagListLayout.HeaderLength, LayoutTagListLayout.RecordStride)]
[BinaryElement("TagRecord", "Tag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("TagRecord", "Offset", WireType.UInt16, LayoutTagListLayout.RecordOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = 2, Count = "Count", Stride = LayoutTagListLayout.RecordStride)]
[BinaryReference("Script", "Offset", typeof(LayoutScriptView), Array = "TagRecord",
    WhenRoles = new long[] { (long)LayoutTagListRole.ScriptList },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
[BinaryReference("Feature", "Offset", typeof(LayoutFeatureView), Array = "TagRecord",
    WhenRoles = new long[] { (long)LayoutTagListRole.FeatureList },
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
public readonly ref partial struct LayoutTagListView { }

[BinaryTable(0, LayoutScriptLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("DefaultLanguageOffset", WireType.UInt16, LayoutScriptLayout.DefaultOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("LanguageCount", WireType.UInt16, LayoutScriptLayout.CountOffset, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("LanguageRecord", "LanguageCount", LayoutScriptLayout.HeaderLength, LayoutTagListLayout.RecordStride)]
[BinaryElement("LanguageRecord", "Tag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("LanguageRecord", "Offset", WireType.UInt16, LayoutTagListLayout.RecordOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = 4, Count = "LanguageCount", Stride = LayoutTagListLayout.RecordStride)]
[BinaryReference("DefaultLanguage", "DefaultLanguageOffset", typeof(LayoutLangSysView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
[BinaryReference("Language", "Offset", typeof(LayoutLangSysView), Array = "LanguageRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd")]
public readonly ref partial struct LayoutScriptView { }

[BinaryTable(0, LayoutLangSysLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("LookupOrderOffset", WireType.UInt16, 0, DataField.Offset)]
[BinaryScalar("RequiredFeatureIndex", WireType.UInt16, LayoutLangSysLayout.RequiredOffset, DataField.Payload)]
[BinaryScalar("FeatureCount", WireType.UInt16, LayoutLangSysLayout.CountOffset, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("FeatureIndex", "FeatureCount", LayoutLangSysLayout.HeaderLength, LayoutLangSysLayout.IndexStride)]
[BinaryElement("FeatureIndex", "Index", WireType.UInt16, 0, DataField.Payload)]
public readonly ref partial struct LayoutLangSysView { }

[BinaryTable(0, LayoutFeatureLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("ParamsOffsetRaw", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("LookupCount", WireType.UInt16, LayoutFeatureLayout.CountOffset, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("LookupIndex", "LookupCount", LayoutFeatureLayout.HeaderLength, LayoutFeatureLayout.IndexStride)]
[BinaryElement("LookupIndex", "Index", WireType.UInt16, 0, DataField.Payload)]
public readonly ref partial struct LayoutFeatureView { }

[BinaryTable(0, LayoutLookupListLayout.HeaderLength, ContractVersion = 1,
    RoleType = typeof(LayoutLookupKind),
    SupportedRoles = new long[] { (long)LayoutLookupKind.Gsub, (long)LayoutLookupKind.Gpos })]
[BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("LookupOffset", "Count", LayoutLookupListLayout.HeaderLength, LayoutLookupListLayout.OffsetStride)]
[BinaryElement("LookupOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryDerived("RecordsEnd", Constant = 2, Count = "Count", Stride = 2)]
[BinaryReference("Lookup", "Offset", typeof(LookupView), Array = "LookupOffset",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "RecordsEnd",
    ForwardRole = true)]
public readonly ref partial struct LayoutLookupListView { }

[BinaryTable(0, LayoutListsLayout.Header10, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { LayoutListsLayout.Version10, LayoutListsLayout.Version11 },
    RoleType = typeof(LayoutLookupKind),
    SupportedRoles = new long[] { (long)LayoutLookupKind.Gsub, (long)LayoutLookupKind.Gpos })]
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
[BinaryComputed("ChildBase", "ChildBaseOf",
    Inputs = new[] { "Version" }, Maximum = 14ul,
    Semantic = NumberSemantic.Offset, DiagnosticField = DataField.Offset)]
[BinaryReference("ScriptList", "ScriptListOffset", typeof(LayoutTagListView),
    TargetRole = (long)LayoutTagListRole.ScriptList, Minimum = "ChildBase")]
[BinaryReference("FeatureList", "FeatureListOffset", typeof(LayoutTagListView),
    TargetRole = (long)LayoutTagListRole.FeatureList, Minimum = "ChildBase")]
[BinaryReference("LookupList", "LookupListOffset", typeof(LayoutLookupListView),
    ForwardRole = true, Minimum = "ChildBase")]
public readonly ref partial struct LayoutListsView { }
