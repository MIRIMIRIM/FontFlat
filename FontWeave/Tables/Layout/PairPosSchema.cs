using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class PairPosLayout
{
    internal const uint Tag = 0x47504F53;
    // The shared fixed prefix (format, coverage, both value formats) is the bound
    // header; the format-specific tails extend it to 10 (fmt1) / 16 (fmt2) bytes.
    internal const int MinimumHeader = 8, CoverageField = 2, Format1Field = 4, Format2Field = 6,
        PairSetCountField = 8, Format2HeaderField = 8,
        Format1Header = 10, Format2Header = 16, OffsetStride = 2, WordStride = 2;
    // One mask contract value, referenced from the SinglePos family constant (the two
    // ValueFormats reuse the T4-01 mask semantics; no second mask contract exists).
    internal const ushort SupportedMask = SinglePosLayout.SupportedMask;
}

// GPOS lookup type 2 (pair adjustment positioning), both formats one view. The two
// formats branch on PairFormat with a per-version tail record (CaretValue format
// precedent): the fmt2 class-matrix header (both ClassDef offsets and both class
// counts) exists only under format 2, while the fmt1 pairSetCount word is the
// unconditional count source its offset array needs. The dynamic rows are the card's
// explicit hand-written boundary - the value/device words are declared as 2-byte
// records on the views themselves (SinglePos SignedValue/DeviceOffset same-owner
// precedent), and the stride/matrix walking stays in the semantic partials (no
// dynamic-mask DSL).
[BinaryTable(PairPosLayout.Tag, PairPosLayout.MinimumHeader, ContractVersion = 1,
    VersionField = "PairFormat", SupportedVersions = new uint[] { 1, 2 })]
[BinaryScalar("PairFormat", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, PairPosLayout.CoverageField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryScalar("ValueFormat1", WireType.UInt16, PairPosLayout.Format1Field, DataField.Flags)]
[BinaryScalar("ValueFormat2", WireType.UInt16, PairPosLayout.Format2Field, DataField.Flags)]
[BinaryScalar("PairSetCount", WireType.UInt16, PairPosLayout.PairSetCountField, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryRecord("Format2Header", 8, WhenVersions = new uint[] { 2 })]
[BinaryElement("Format2Header", "ClassDef1Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryElement("Format2Header", "ClassDef2Offset", WireType.UInt16, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryElement("Format2Header", "Class1Count", WireType.UInt16, 4, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryElement("Format2Header", "Class2Count", WireType.UInt16, 6, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryArray("PairSetOffsetRecord", "PairSetCount", PairPosLayout.Format1Header,
    PairPosLayout.OffsetStride)]
[BinaryElement("PairSetOffsetRecord", "PairSetOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryRecord("PairScalar", PairPosLayout.WordStride)]
[BinaryElement("PairScalar", "Value", WireType.Int16, 0, DataField.Payload)]
[BinaryRecord("PairDeviceOffset", PairPosLayout.WordStride)]
[BinaryElement("PairDeviceOffset", "Offset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
// The declared storage floors: 10 + 2*pairSetCount under format 1, else the 16-byte
// fmt2 header (NameSchema MetadataEnd conditional-with-Otherwise precedent). They are
// the references' static floor only - the fmt2 matrix window check stays semantic
// (W9's declared-rows-ahead-of-coverage refusal needs the count arithmetic).
[BinaryDerived("Format2HeaderEnd", Constant = PairPosLayout.Format2Header)]
[BinaryDerived("Format1HeaderEnd", Constant = PairPosLayout.Format1Header, Count = "PairSetCount",
    Stride = PairPosLayout.OffsetStride, WhenVersion = 1, Otherwise = "Format2HeaderEnd")]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format1HeaderEnd")]
[BinaryReference("PairSet", "PairSetOffset", typeof(PairSetView), Array = "PairSetOffsetRecord",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format1HeaderEnd", WhenVersions = new uint[] { 1 })]
// Both offsets may legally name one shared ClassDef (spec gpos.md:307, W12); identity
// sharing is the semantic cache's property, never assumed from equal addresses.
[BinaryReference("ClassDef1", "ClassDef1Offset", typeof(ClassDefView), Array = "Format2Header",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format2HeaderEnd", WhenVersions = new uint[] { 2 })]
[BinaryReference("ClassDef2", "ClassDef2Offset", typeof(ClassDefView), Array = "Format2Header",
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required,
    Minimum = "Format2HeaderEnd", WhenVersions = new uint[] { 2 })]
public readonly ref partial struct PairPosView { }
