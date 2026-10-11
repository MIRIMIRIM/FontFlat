using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class DeltaSetIndexMapLayout
{
    internal const int HeaderLength = 4, FormatAt = 0, EntryFormatAt = 1, MapCountAt = 2,
        EntriesAt = 4;
    // Format 1's u32 count pushes the packed entries two bytes later (the declared
    // 4-byte floor stays the format-0 face; the wide arm's floor is semantic work).
    internal const int Format1EntriesAt = 6;
}

// DeltaSetIndexMap as the shared COLR-era structure face (OT 1.9.1 colr.md L643:
// "Within the COLR table, either format 0 or format 1 of the DeltaSetIndexMap can
// be used"). Format 0 is the HVAR face ("In the HVAR table, only format 0 is
// defined" - T5-01); format 1 is the identical structure with a UInt32 mapCount
// (the T5-01 card's declared T6-03 reservation), declared here as the same-offset
// double-width WhenVersion pair. The UInt8 format is declared as the table's version
// field, so an unknown map format (3+) refuses at the native version gate
// (OQ-T5-01-4). With the version domain widened, the HVAR/VVAR consumers re-anchor
// their format-0-only face explicitly at the map open sites (OQ-T6-03-4: the
// diagnostic shape stays byte-identical to the old version-gate refusal). The
// packed entries are the card's hand-written boundary: entrySize =
// ((entryFormat & 0x30) >> 4) + 1 bytes, innerBits = (entryFormat & 0x0F) + 1, the
// reserved 0xC0 mask refusal, the 0xFFFF/0xFFFF sentinel and the last-entry fallback
// for gid >= mapCount are all semantic-layer work (cards T5-01 2/3.2, T6-03 §2).
[BinaryTable(0, DeltaSetIndexMapLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Format", SupportedVersions = new uint[] { 0, 1 })]
[BinaryScalar("Format", WireType.UInt8, DeltaSetIndexMapLayout.FormatAt, DataField.Version)]
[BinaryScalar("EntryFormat", WireType.UInt8, DeltaSetIndexMapLayout.EntryFormatAt,
    DataField.Flags)]
[BinaryScalar("MapCount", WireType.UInt16, DeltaSetIndexMapLayout.MapCountAt,
    DataField.Count, Semantic = NumberSemantic.Count, WhenVersion = 0)]
[BinaryScalar("MapCountWide", WireType.UInt32, DeltaSetIndexMapLayout.MapCountAt,
    DataField.Count, Semantic = NumberSemantic.Count, WhenVersion = 1)]
public readonly ref partial struct DeltaSetIndexMapView { }
