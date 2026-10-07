using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class FvarLayout
{
    internal const uint Tag = 0x66766172;
    internal const uint Version10 = 0x00010000;
    internal const int HeaderLength = 16, AxesArrayOffsetAt = 4, ReservedAt = 6,
        AxisCountAt = 8, AxisSizeAt = 10, InstanceCountAt = 12, InstanceSizeAt = 14;
    internal const int AxisStride = 20;
    // "This field is permanently reserved. Set to 2." (spec fvar.md, 1.9.1 snapshot).
    internal const ushort ReservedValue = 2;
    // HIDDEN_AXIS = 0x0001 is the only defined flag bit; every other bit is reserved.
    internal const ushort AxisFlagReservedMask = 0xFFFE;
}

// fvar 1.0 (the variable-line axis authority, T5-02): a 16-byte header whose axes
// array is declared as the production first use of a counted BinaryArray positioned
// by a raw scalar (Start = "AxesArrayOffset", OQ-T5-02-7 pre-registered; the parser
// resolves raw-scalar bounds, but every earlier counted array used a BinaryDerived
// start). The stride-20 VariationAxisRecord carries the three Fixed coordinates as
// FixedPoint Scale=65536 array elements - HeadSchema declares the scalar precedent,
// the element form is the second first use pre-registered by the card. The semantic
// pin stays deliberately raw: axis values are user-coordinate Fixed raw values, no
// normalized conversion exists at this level (HeadSchema FontRevision philosophy).
//
// The InstanceRecord block is the card's hand-written boundary and is not declared:
// instanceSize is dynamic (4*axisCount+4 or +6), the instances directly follow the
// axes array with no offset field of their own, and their semantics (flags/nameID
// value domains, coordinate range observations) are partial faces (card 2). The
// version gate is 1.0 only - 1.9.1 has no published minor extension, and future
// 1.x compatible extensions arrive through a later slice (card 1).
[BinaryTable(FvarLayout.Tag, FvarLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { FvarLayout.Version10 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("AxesArrayOffset", WireType.UInt16, FvarLayout.AxesArrayOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("Reserved", WireType.UInt16, FvarLayout.ReservedAt, DataField.Payload)]
[BinaryScalar("AxisCount", WireType.UInt16, FvarLayout.AxisCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryScalar("AxisSize", WireType.UInt16, FvarLayout.AxisSizeAt, DataField.Length)]
[BinaryScalar("InstanceCount", WireType.UInt16, FvarLayout.InstanceCountAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryScalar("InstanceSize", WireType.UInt16, FvarLayout.InstanceSizeAt, DataField.Length)]
[BinaryArray("Axis", "AxisCount", 0, FvarLayout.AxisStride, Start = "AxesArrayOffset")]
[BinaryElement("Axis", "AxisTag", WireType.UInt32, 0, DataField.Tag)]
[BinaryElement("Axis", "MinValue", WireType.Int32, 4, DataField.Payload,
    Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryElement("Axis", "DefaultValue", WireType.Int32, 8, DataField.Payload,
    Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryElement("Axis", "MaxValue", WireType.Int32, 12, DataField.Payload,
    Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryElement("Axis", "Flags", WireType.UInt16, 16, DataField.Flags)]
[BinaryElement("Axis", "AxisNameID", WireType.UInt16, 18, DataField.Payload)]
public readonly ref partial struct FvarView { }
