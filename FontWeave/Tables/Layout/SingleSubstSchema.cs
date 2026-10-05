using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class SingleSubstLayout
{
    internal const uint Tag = 0x47535542;
    internal const int MinimumHeader = 4, CoverageOffsetField = 2, BodyOffset = 4;
    internal const int HeaderLength = 6, BodyRecordStride = 2, SubstituteStride = 2;
    internal const int MaximumFormat2Rows = (ushort.MaxValue - HeaderLength) / SubstituteStride;
    // A two-subtable direct Lookup can have a MarkFilteringSet in its 12-byte header.
    internal const int MaximumFirstSplitLength = ushort.MaxValue - 12;
}

[BinaryTable(SingleSubstLayout.Tag, SingleSubstLayout.MinimumHeader, ContractVersion = 1, VersionField = "Format", SupportedVersions = new uint[] { 1, 2 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("CoverageOffset", WireType.UInt16, SingleSubstLayout.CoverageOffsetField, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryReference("Coverage", "CoverageOffset", typeof(CoverageView),
    Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "BodyEnd")]
[BinaryRecord("DeltaRecord", SingleSubstLayout.BodyRecordStride)]
[BinaryElement("DeltaRecord", "DeltaGlyphId", WireType.Int16, 0, DataField.Payload)]
// M1c a' (OQ-M1c-4): the former SubstituteHeader record is one unconditional scalar.
// Under format 1 the same bytes are the delta; observing them as a raw count is
// harmless and always readable, which keeps the computed BodyEnd on an unconditional
// activation path.
[BinaryScalar("SubstituteGlyphCount", WireType.UInt16, SingleSubstLayout.BodyOffset, DataField.Count)]
[BinaryRecord("SubstituteRecord", SingleSubstLayout.SubstituteStride)]
[BinaryElement("SubstituteRecord", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
// 6 + 2*count: the SubtablePrefixOf shape (mask test plus conditional selection is one
// of the explicitly allowed bounded computations, spec 2.3). The worst case
// 6 + 2*65535 = 131076 matches the declared static Maximum. BodyEnd is the declared
// storage floor of the Coverage reference, replacing the former handwritten pre-check
// (OQ-1's boundary marker; the Minimum row reproduces its diagnostics byte for byte).
[BinaryComputed("BodyEnd", "BodyEndOf",
    Inputs = new[] { "Format", "SubstituteGlyphCount" }, Maximum = 131076ul,
    Semantic = NumberSemantic.Offset, DiagnosticField = DataField.Offset,
    DiagnosticSource = "Format")]
public readonly ref partial struct SingleSubstView
{
    private static ReadStatus BodyEndOf(ushort format, ushort substituteGlyphCount,
        DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
    {
        value = format == 1 ? 6UL : 6UL + 2UL * substituteGlyphCount;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
