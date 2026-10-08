using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class KernLayout
{
    internal const uint Tag = 0x6B65726E;
    // OQ-5: one root, both dialects - the u16 version word at 0 splits them (0 = the
    // OT normative v0, 1 = the Apple fixed32 0x00010000 high half; the low half is the
    // conditional v1 record's own constant-zero semantic check, read-side partial).
    internal const ushort Version0 = 0, VersionApple = 1;
    internal const int HeaderV0 = 4, HeaderV1 = 8,
        SubtableHeaderV0 = 6, SubtableHeaderV1 = 8;
    // The two body shapes are dialect-shared: format 0 adds the count + search triple
    // before the pair cells, format 2 adds rowWidth + three subtable-relative offsets;
    // both fixed bodies start at header + 8 (v0 14, v1 16).
    internal const int SubtableBodyV0 = 14, SubtableBodyV1 = 16, PairStride = 6;
    // v0 coverage (OT 1.9.1 frozen snapshot): low byte flags, high byte format. The
    // reserved flags bits 4-7 are the T0 rejection face; the format byte dispatches.
    internal const ushort Flags0Horizontal = 0x01, Flags0Minimum = 0x02,
        Flags0CrossStream = 0x04, Flags0Override = 0x08,
        Flags0LegalMask = 0x0F, Flags0ReservedMask = 0xF0, Format0Mask = 0xFF00;
    // v1 coverage (Apple TTRM): high byte flags / low byte format - the physical byte
    // order is the opposite of v0 (OQ-8: the dialect dispatch absorbs it). The three
    // semantic flag bits decode as an observation (the card's OQ-2 family); the unused
    // 0x1F00 ridge is refused like the v0 reserved bits.
    internal const ushort AppleVertical = 0x8000, AppleCrossStream = 0x4000,
        AppleVariation = 0x2000, AppleUnusedMask = 0x1F00, AppleFlagsMask = 0xE000,
        AppleFormatMask = 0x00FF;
}

// The kern root (T4-08 section 2, OQ-5): one view over both dialects. The version word
// at 0 selects the v0 u16 count record or the v1 Apple record (u16 low-version half
// plus the u32 count - the BASE 1.1 VariationStoreHeader Offset32 element precedent);
// every subtable is length-driven and format-dispatched, so no reference is declared
// here - the subtable walk is the semantic layer's keep (the cmap root precedent,
// section 7: a per-subtable dynamic stride sequence is not a declarable array).
[BinaryTable(KernLayout.Tag, KernLayout.HeaderV0, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { KernLayout.Version0, KernLayout.VersionApple })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryRecord("LegacyCount", 2, WhenVersions = new uint[] { KernLayout.Version0 })]
[BinaryElement("LegacyCount", "NTablesLegacy", WireType.UInt16, 0, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryRecord("AppleHeader", 6, WhenVersions = new uint[] { KernLayout.VersionApple })]
[BinaryElement("AppleHeader", "VersionHigh", WireType.UInt16, 0, DataField.Version)]
[BinaryElement("AppleHeader", "NTablesApple", WireType.UInt32, 2, DataField.Count,
    Semantic = NumberSemantic.Count)]
// 4 / 8 by declared version: the reference storage floor (GdefSchema's HeaderEnd shape;
// no declared reference consumes it here, the walk bills against it instead).
[BinaryComputed("HeaderEnd", "HeaderEndOf", Inputs = new[] { "Version" }, Maximum = 8ul,
    Semantic = NumberSemantic.Offset, DiagnosticField = DataField.Version,
    DiagnosticSource = "Version")]
public readonly ref partial struct KernView
{
    private static ReadStatus HeaderEndOf(ushort version, DataLocation diagnosticLocation,
        out ulong value, out FontDiagnostic diagnostic)
    {
        value = version == KernLayout.VersionApple ? (ulong)KernLayout.HeaderV1 : (ulong)KernLayout.HeaderV0;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
