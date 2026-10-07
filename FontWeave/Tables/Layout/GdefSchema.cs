using FontWeave.Core;
using FontWeave.Schema;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables.Layout;

internal static class GdefLayout
{
    internal const uint Tag = 0x47444546, Version10 = 0x00010000,
        Version12 = 0x00010002, Version13 = 0x00010003;
    internal const int Header10 = 12, Header12 = 14, Header13 = 18,
        GlyphClassesAt = 4, AttachListAt = 6, LigCaretListAt = 8,
        MarkAttachmentClassesAt = 10, MarkSetsAt = 12, VariationStoreAt = 14;
}

[BinaryTable(GdefLayout.Tag, GdefLayout.Header10, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { GdefLayout.Version10, GdefLayout.Version12, GdefLayout.Version13 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("GlyphClassDefOffset", WireType.UInt16, GdefLayout.GlyphClassesAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("AttachListOffset", WireType.UInt16, GdefLayout.AttachListAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("LigCaretListOffset", WireType.UInt16, GdefLayout.LigCaretListAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("MarkAttachClassDefOffset", WireType.UInt16, GdefLayout.MarkAttachmentClassesAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryRecord("MarkSetsHeader", 2, WhenVersions = new uint[] { GdefLayout.Version12, GdefLayout.Version13 })]
[BinaryElement("MarkSetsHeader", "MarkGlyphSetsDefOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
// OQ-M1c-1 ⑥ closes here (T4-02b): the raw-only registration ends and the header
// record keeps its shape while the offset gains the shared offset semantics. The typed
// target is the T5-01 ItemVariationStoreView, wired in as the first Layout->Variation
// cross-directory reference (the emitter qualifies it with global::); the store's
// VariationIndex/0x8000 device semantics stay outside this declaration (T4-06).
[BinaryRecord("VariationStoreHeader", 4, WhenVersions = new uint[] { GdefLayout.Version13 })]
[BinaryElement("VariationStoreHeader", "ItemVariationStoreOffset", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
// 12 / 14 / 18 by declared version: the classdef references' storage floor.
[BinaryComputed("HeaderEnd", "HeaderEndOf", Inputs = new[] { "Version" }, Maximum = 18ul,
    Semantic = NumberSemantic.Offset, DiagnosticField = DataField.Offset,
    DiagnosticSource = "Version")]
[BinaryReference("GlyphClassDef", "GlyphClassDefOffset", typeof(ClassDefView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
[BinaryReference("MarkAttachClassDef", "MarkAttachClassDefOffset", typeof(ClassDefView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
// The first version-conditional reference (A4 OQ-6 unlocked): the v1.0 header has no
// MarkSets bytes at all, so the reference is inactive exactly where its source record
// is physically absent.
[BinaryReference("MarkGlyphSets", "MarkGlyphSetsDefOffset", typeof(MarkGlyphSetsView),
    Array = "MarkSetsHeader", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Optional, Minimum = "HeaderEnd",
    WhenVersions = new uint[] { GdefLayout.Version12, GdefLayout.Version13 })]
[BinaryReference("AttachList", "AttachListOffset", typeof(AttachListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
[BinaryReference("LigCaretList", "LigCaretListOffset", typeof(LigCaretListView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "HeaderEnd")]
// The store reference trails the five static references (OQ-T4-02b-4): the declared
// existence domain is the record's own v1.3 slot, NULL is the spec's "may be NULL"
// store, and the floor is the shared HeaderEnd computed field.
[BinaryReference("ItemVariationStore", "ItemVariationStoreOffset", typeof(ItemVariationStoreView),
    Array = "VariationStoreHeader", Origin = ReferenceOrigin.ViewStart,
    NullPolicy = ReferenceNullPolicy.Optional, Minimum = "HeaderEnd",
    WhenVersions = new uint[] { GdefLayout.Version13 })]
public readonly ref partial struct GdefView
{
    private static ReadStatus HeaderEndOf(uint version, DataLocation diagnosticLocation,
        out ulong value, out FontDiagnostic diagnostic)
    {
        value = version == GdefLayout.Version13 ? (ulong)GdefLayout.Header13
            : version == GdefLayout.Version12 ? (ulong)GdefLayout.Header12 : (ulong)GdefLayout.Header10;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
