using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class StatLayout
{
    internal const uint Tag = 0x53544154;
    internal const uint Version10 = 0x00010000, Version11 = 0x00010001, Version12 = 0x00010002;
    // Card erratum, registered at implementation: the card's section 2 text drafts
    // "HeaderLength=12 / HeaderEnd 12/14", which contradicts the card's own field list
    // (OffsetToAxisValueOffsets@14 is an Offset32, the elided record trails it), the
    // spec snapshot ("In version 1.0 ... the elidedFallbackNameId field was not
    // included"; version 1.1 adds it) and the frozen section 5 fact "STAT 1.0
    // (18-byte header)". The declared layout follows the spec: 18 bytes for 1.0,
    // 20 bytes for 1.1/1.2 (GDEF HeaderEndOf precedent).
    internal const int Header10 = 18, Header11 = 20;
    internal const int DesignAxisSizeAt = 4, DesignAxisCountAt = 6, DesignAxesOffsetAt = 8,
        AxisValueCountAt = 12, OffsetToAxisValueOffsetsAt = 14, ElidedFallbackNameIDAt = 18;
    internal const int DesignAxisStride = 8, AxisValueOffsetStride = 2;
    // "The size in bytes of each axis record" - 8 for every 1.x version; a different
    // value belongs to a future minor version this contract does not declare.
    internal const ushort DesignAxisSize = 8;
}

// STAT 1.0/1.1/1.2 (the style-attributes table, T5-02): a version-branched header
// (18/20 bytes, the elidedFallbackNameID record is physically absent under 1.0 -
// the A4 OQ-6 conditional-record discipline), the designAxes counted array positioned
// by the raw Offset32 scalar (the OQ-T5-02-7 Start=<raw scalar> first use, same form
// as fvar) and the axisValueOffsets counted array whose Offset16 entries are relative
// to the start of the offsets array itself - not the table start.
//
// That offsets-relative addressing is the production first use of the NamedBase
// reference frame (OQ-T5-02-4, generator-supported with 0/45 prior uses): the
// AxisValue reference resolves against the OffsetToAxisValueOffsets raw value and is
// floored at the derived end of the offsets array, so a target at or inside the
// offsets array refuses exactly like a backward reference. The Minimum NamedBase
// frame came out clean (rel >= 2*count through the BinaryDerived), so the Cmap
// format 4 partial-bind degradation path was not needed (card 2, OQ-T5-02-4).
//
// Each AxisValue target is a StatAxisValueView; format 1-3 bodies are fixed-length
// records, while format 4's trailing axisIndex/value rows stay that view's
// hand-written boundary. Static fonts are first-class: every fvar-related rule is
// conditional on a non-null FvarTable input (card 3.3 - AnekBangla carries a real
// static STAT).
[BinaryTable(StatLayout.Tag, StatLayout.Header10, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { StatLayout.Version10, StatLayout.Version11, StatLayout.Version12 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("DesignAxisSize", WireType.UInt16, StatLayout.DesignAxisSizeAt, DataField.Length)]
[BinaryScalar("DesignAxisCount", WireType.UInt16, StatLayout.DesignAxisCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryScalar("DesignAxesOffset", WireType.UInt32, StatLayout.DesignAxesOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("AxisValueCount", WireType.UInt16, StatLayout.AxisValueCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryScalar("OffsetToAxisValueOffsets", WireType.UInt32, StatLayout.OffsetToAxisValueOffsetsAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryRecord("ElidedFallbackName", 2, WhenVersions = new uint[] { StatLayout.Version11, StatLayout.Version12 })]
[BinaryElement("ElidedFallbackName", "NameID", WireType.UInt16, 0, DataField.Payload)]
// 18 / 20 by declared version: the two arrays' storage floor.
[BinaryComputed("HeaderEnd", "HeaderEndOf", Inputs = new[] { "Version" }, Maximum = 20ul,
    Semantic = NumberSemantic.Offset, DiagnosticField = DataField.Offset,
    DiagnosticSource = "Version")]
[BinaryArray("DesignAxis", "DesignAxisCount", 0, StatLayout.DesignAxisStride, Start = "DesignAxesOffset")]
[BinaryElement("DesignAxis", "AxisTag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("DesignAxis", "AxisNameID", WireType.UInt16, 4, DataField.Payload)]
[BinaryElement("DesignAxis", "AxisOrdering", WireType.UInt16, 6, DataField.Payload)]
[BinaryArray("AxisValueOffsets", "AxisValueCount", 0, StatLayout.AxisValueOffsetStride,
    Start = "OffsetToAxisValueOffsets")]
// The NameStorage origin is the declared frame that pairs with a NamedBase reference
// (the parser's origin-consistency rule: "NameStorage only pairs with an explicit
// NamedBase Base") - the value addresses through its named base, not the table
// window. ZeroAllowed=false matches the Required/InvalidValue reference policy
// (the CaretValue Device precedent).
[BinaryElement("AxisValueOffsets", "Value", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.NameStorage, ZeroAllowed = false)]
[BinaryDerived("AxisValueOffsetsEnd", Base = "OffsetToAxisValueOffsets",
    Count = "AxisValueCount", Stride = StatLayout.AxisValueOffsetStride)]
// The offsets-array-relative frame: the raw header Offset32 is the base, each
// Offset16 element scales by one, and the floor keeps targets behind the offsets
// array out of the window. Zero entries refuse as InvalidValue (a zero offset would
// address the offsets array itself, the CaretValue Device precedent).
[BinaryReference("AxisValue", "Value", typeof(StatAxisValueView), Array = "AxisValueOffsets",
    Origin = ReferenceOrigin.NamedBase, Base = "OffsetToAxisValueOffsets",
    NullPolicy = ReferenceNullPolicy.Required, ZeroFailure = ReferenceZeroFailure.InvalidValue,
    Minimum = "AxisValueOffsetsEnd")]
public readonly ref partial struct StatView
{
    private static ReadStatus HeaderEndOf(uint version, DataLocation diagnosticLocation,
        out ulong value, out FontDiagnostic diagnostic)
    {
        value = version >= StatLayout.Version11 ? (ulong)StatLayout.Header11 : (ulong)StatLayout.Header10;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
