using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class DeltaSetIndexMapLayout
{
    internal const int HeaderLength = 4, FormatAt = 0, EntryFormatAt = 1, MapCountAt = 2,
        EntriesAt = 4;
}

// DeltaSetIndexMap as the HVAR face sees it: format 0 only (OT 1.9.1 hvar.md "In the
// HVAR table, only format 0 is defined"; format 1's UInt32 mapCount is the COLR-era
// shared-structure face, T6-03). The UInt8 format is declared as the table's version
// field, so an unknown map format refuses at the native version gate
// (OQ-T5-01-4: the generator supports UInt8 VersionField directly - no semantic-layer
// substitute). The packed entries are the card's hand-written boundary: entrySize =
// ((entryFormat & 0x30) >> 4) + 1 bytes, innerBits = (entryFormat & 0x0F) + 1, the
// reserved 0xC0 mask refusal, the 0xFFFF/0xFFFF sentinel and the last-entry fallback
// for gid >= mapCount are all semantic-layer work (card 2/3.2).
[BinaryTable(0, DeltaSetIndexMapLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Format", SupportedVersions = new uint[] { 0 })]
[BinaryScalar("Format", WireType.UInt8, DeltaSetIndexMapLayout.FormatAt, DataField.Version)]
[BinaryScalar("EntryFormat", WireType.UInt8, DeltaSetIndexMapLayout.EntryFormatAt,
    DataField.Flags)]
[BinaryScalar("MapCount", WireType.UInt16, DeltaSetIndexMapLayout.MapCountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
public readonly ref partial struct DeltaSetIndexMapView { }
