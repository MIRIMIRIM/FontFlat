using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class StatAxisValueLayout
{
    // Axis value tables live inside STAT; the view carries STAT's tag for provenance
    // (the CaretValue/GDEF precedent for owned sub-structures).
    internal const int PrefixLength = 2;
    // Fixed-length bodies behind the shared UInt16 format field (the CaretValue
    // format-activation shape): format 1 = axisIndex/flags/valueNameID/value(Fixed),
    // format 2 adds nominal/min/max, format 3 carries value+linkedValue, and format 4
    // declares only the 6-byte header (combinationAxisCount/flags/valueNameID).
    internal const int Format1BodyStride = 10, Format2BodyStride = 18,
        Format3BodyStride = 14, Format4HeaderStride = 6;
    // Format 4's trailing AxisValue rows (axisIndex + Fixed value) are the view's
    // hand-written boundary - an axisCount-sized dynamic tail no counted array with a
    // fixed stride can declare (card 2).
    internal const int Format4EntryStride = 6;
}

// One STAT axis value table (T5-02): the format is declared directly as the version
// field, so a format outside {1,2,3,4} refuses at the native UnsupportedVersion gate
// (the card's lean reading of the spec's "may be ignored" - typed reads refuse, raw
// observation stays legal; OQ-T5-02-3). Format 4's combination rows are walked by the
// partial with the STAT-level rules (axisIndex domain, pairwise-distinct axes and the
// version 1.2 activation gate) owned by StatView, which holds both contexts (card 3.1).
[BinaryTable(StatLayout.Tag, StatAxisValueLayout.PrefixLength, ContractVersion = 1,
    VersionField = "Format", SupportedVersions = new uint[] { 1, 2, 3, 4 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryRecord("Format1Body", StatAxisValueLayout.Format1BodyStride, WhenVersions = new uint[] { 1 })]
[BinaryElement("Format1Body", "AxisIndex", WireType.UInt16, 0, DataField.Payload)]
[BinaryElement("Format1Body", "Flags", WireType.UInt16, 2, DataField.Flags)]
[BinaryElement("Format1Body", "ValueNameID", WireType.UInt16, 4, DataField.Payload)]
[BinaryElement("Format1Body", "Value", WireType.Int32, 6, DataField.Payload,
    Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryRecord("Format2Body", StatAxisValueLayout.Format2BodyStride, WhenVersions = new uint[] { 2 })]
[BinaryElement("Format2Body", "AxisIndex", WireType.UInt16, 0, DataField.Payload)]
[BinaryElement("Format2Body", "Flags", WireType.UInt16, 2, DataField.Flags)]
[BinaryElement("Format2Body", "ValueNameID", WireType.UInt16, 4, DataField.Payload)]
[BinaryElement("Format2Body", "NominalValue", WireType.Int32, 6, DataField.Payload,
    Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryElement("Format2Body", "RangeMinValue", WireType.Int32, 10, DataField.Payload,
    Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryElement("Format2Body", "RangeMaxValue", WireType.Int32, 14, DataField.Payload,
    Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryRecord("Format3Body", StatAxisValueLayout.Format3BodyStride, WhenVersions = new uint[] { 3 })]
[BinaryElement("Format3Body", "AxisIndex", WireType.UInt16, 0, DataField.Payload)]
[BinaryElement("Format3Body", "Flags", WireType.UInt16, 2, DataField.Flags)]
[BinaryElement("Format3Body", "ValueNameID", WireType.UInt16, 4, DataField.Payload)]
[BinaryElement("Format3Body", "Value", WireType.Int32, 6, DataField.Payload,
    Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryElement("Format3Body", "LinkedValue", WireType.Int32, 10, DataField.Payload,
    Semantic = NumberSemantic.FixedPoint, Scale = 65536)]
[BinaryRecord("Format4Header", StatAxisValueLayout.Format4HeaderStride, WhenVersions = new uint[] { 4 })]
[BinaryElement("Format4Header", "CombinationAxisCount", WireType.UInt16, 0, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryElement("Format4Header", "Flags", WireType.UInt16, 2, DataField.Flags)]
[BinaryElement("Format4Header", "ValueNameID", WireType.UInt16, 4, DataField.Payload)]
public readonly ref partial struct StatAxisValueView { }
