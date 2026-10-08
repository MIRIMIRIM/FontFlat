using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class JstfPriorityLayout
{
    internal const int HeaderLength = 20;
    // The ten Offset16 slots in wire order. The slot position IS the kind: the wire
    // carries no kind field and the declaration has no selector to spend, so the
    // GSUB/GPOS attribution maps from these constants in the semantic layer (OQ-1;
    // the generator sees ten plain Optional slots and nothing else).
    internal const int GsubShrinkageEnableAt = 0, GsubShrinkageDisableAt = 2,
        GposShrinkageEnableAt = 4, GposShrinkageDisableAt = 6, ShrinkageJstfMaxAt = 8,
        GsubExtensionEnableAt = 10, GsubExtensionDisableAt = 12,
        GposExtensionEnableAt = 14, GposExtensionDisableAt = 16, ExtensionJstfMaxAt = 18;
}

// One JstfPriority (T4-09 section 2): ten Offset16 slots, every one the spec's
// "may be NULL" (Optional/ZeroAllowed; the all-NULL priority is the OQ-5 legal
// positive). The GSUB/GPOS attribution is slot-position semantics - wire-identical
// JstfModList targets on both sides mean one shared JstfModListView (OQ-1), the two
// JstfMax slots target the shared JstfMaxView, and no per-item references exist
// beyond these ten (JstfMax's lookup bodies open through the semantic layer's
// LookupView reuse, OQ-6). The constant BodyEnd floor (the 20-byte priority body)
// keeps every edge strictly forward, the declaration-level DAG guarantee.
[BinaryTable(JstfLayout.Tag, JstfPriorityLayout.HeaderLength, ContractVersion = 1)]
[BinaryDerived("BodyEnd", Constant = JstfPriorityLayout.HeaderLength)]
[BinaryScalar("GsubShrinkageEnableOffset", WireType.UInt16, JstfPriorityLayout.GsubShrinkageEnableAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("GsubShrinkageDisableOffset", WireType.UInt16, JstfPriorityLayout.GsubShrinkageDisableAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("GposShrinkageEnableOffset", WireType.UInt16, JstfPriorityLayout.GposShrinkageEnableAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("GposShrinkageDisableOffset", WireType.UInt16, JstfPriorityLayout.GposShrinkageDisableAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("ShrinkageJstfMaxOffset", WireType.UInt16, JstfPriorityLayout.ShrinkageJstfMaxAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("GsubExtensionEnableOffset", WireType.UInt16, JstfPriorityLayout.GsubExtensionEnableAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("GsubExtensionDisableOffset", WireType.UInt16, JstfPriorityLayout.GsubExtensionDisableAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("GposExtensionEnableOffset", WireType.UInt16, JstfPriorityLayout.GposExtensionEnableAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("GposExtensionDisableOffset", WireType.UInt16, JstfPriorityLayout.GposExtensionDisableAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("ExtensionJstfMaxOffset", WireType.UInt16, JstfPriorityLayout.ExtensionJstfMaxAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryReference("GsubShrinkageEnable", "GsubShrinkageEnableOffset", typeof(JstfModListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
[BinaryReference("GsubShrinkageDisable", "GsubShrinkageDisableOffset", typeof(JstfModListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
[BinaryReference("GposShrinkageEnable", "GposShrinkageEnableOffset", typeof(JstfModListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
[BinaryReference("GposShrinkageDisable", "GposShrinkageDisableOffset", typeof(JstfModListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
[BinaryReference("ShrinkageJstfMax", "ShrinkageJstfMaxOffset", typeof(JstfMaxView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
[BinaryReference("GsubExtensionEnable", "GsubExtensionEnableOffset", typeof(JstfModListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
[BinaryReference("GsubExtensionDisable", "GsubExtensionDisableOffset", typeof(JstfModListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
[BinaryReference("GposExtensionEnable", "GposExtensionEnableOffset", typeof(JstfModListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
[BinaryReference("GposExtensionDisable", "GposExtensionDisableOffset", typeof(JstfModListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
[BinaryReference("ExtensionJstfMax", "ExtensionJstfMaxOffset", typeof(JstfMaxView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
public readonly ref partial struct JstfPriorityView { }
