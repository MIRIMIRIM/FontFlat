using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Variation;

internal static class CvarLayout
{
    internal const uint Tag = 0x63766172;
    // The whole cvar table is one versioned tuple variation store: an 8-byte
    // header (spec cvar.md) with no offsets array, no shared peak tuples and no
    // glyph indirection (card 0, common formats L110-114).
    internal const int HeaderLength = 8;
    internal const int TupleVariationCountAt = 4, DataOffsetAt = 6;
    internal const uint Version10 = 0x00010000;
}

// cvar 1.0 (CVT variations): three header scalars are the entire declarable
// skeleton. The TupleVariationHeader array (embedded-peak dynamic stride) and
// the serialized runs are the shared hand-written boundary with gvar - one
// serialization core serves both partials, the正面 form of the single-reader
// rule (AGENTS; card 2/7). No child reference is declared, so there is no
// structure scan (the Variation-domain precedent).
[BinaryTable(CvarLayout.Tag, CvarLayout.HeaderLength, ContractVersion = 1,
    VersionField = "Version", SupportedVersions = new uint[] { CvarLayout.Version10 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("TupleVariationCount", WireType.UInt16, CvarLayout.TupleVariationCountAt,
    DataField.Flags)]
[BinaryScalar("DataOffset", WireType.UInt16, CvarLayout.DataOffsetAt, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
public readonly ref partial struct CvarView { }
