using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class MathGlyphAssemblyLayout
{
    internal const int HeaderLength = 6, ItalicsCorrectionValueAt = 0,
        ItalicsCorrectionDeviceOffsetAt = 2, PartCountAt = 4, PartRecordsAt = 6;
}

// GlyphAssembly (T4-10 section 2): the family-named MathGlyphAssemblyView carries one
// MathValueRecord italics correction (the per-element Optional device reference with
// the whole-body floor) over the counted GlyphPart array (stride 10). partCount=0 is
// the OQ-7 legal positive (Partial4's real shape); a non-empty parts list with no
// extender flag is the OQ-8 observation ("no terminating assembly" is the assembly
// algorithm's application semantics, never a structural refusal), and the partFlags
// reserved ridge 0xFFFE is the OQ-5 observation. Part glyph ids are GlyphId-typed
// words whose numGlyphs domain is the semantic layer's hard check.
[BinaryTable(MathLayout.Tag, MathGlyphAssemblyLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("ItalicsCorrectionValue", WireType.Int16, MathGlyphAssemblyLayout.ItalicsCorrectionValueAt, DataField.Payload)]
[BinaryScalar("ItalicsCorrectionDeviceOffset", WireType.UInt16, MathGlyphAssemblyLayout.ItalicsCorrectionDeviceOffsetAt,
    DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]
[BinaryScalar("PartCount", WireType.UInt16, MathGlyphAssemblyLayout.PartCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("PartRecord", "PartCount", MathGlyphAssemblyLayout.PartRecordsAt,
    MathLayout.GlyphPartSize)]
[BinaryElement("PartRecord", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("PartRecord", "StartConnectorLength", WireType.UInt16, 2, DataField.Payload)]
[BinaryElement("PartRecord", "EndConnectorLength", WireType.UInt16, 4, DataField.Payload)]
[BinaryElement("PartRecord", "FullAdvance", WireType.UInt16, 6, DataField.Payload)]
[BinaryElement("PartRecord", "PartFlags", WireType.UInt16, 8, DataField.Flags)]
[BinaryDerived("BodyEnd", Constant = MathGlyphAssemblyLayout.PartRecordsAt,
    Count = "PartCount", Stride = MathLayout.GlyphPartSize)]
[BinaryReference("ItalicsCorrectionDevice", "ItalicsCorrectionDeviceOffset", typeof(StaticDeviceView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional,
    Minimum = "BodyEnd")]
public readonly ref partial struct MathGlyphAssemblyView { }
