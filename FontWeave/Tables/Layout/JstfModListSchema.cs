using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class JstfModListLayout
{
    internal const int HeaderLength = 2, CountAt = 0, IndicesAt = 2, IndexStride = 2;
}

// One JstfModList (T4-09 section 2, OQ-1): the single view over both the GSUB and the
// GPOS mod list. The two wire layouts are word-identical, so fontTools' separate
// JstfGSUBModList/JstfGPOSModList types would be parallel readers of one layout - the
// AGENTS one-layout rule refuses them. The array carries raw u16 indices (Payload,
// not references): the domain is not this table's, it is the caller-supplied GSUB or
// GPOS LookupList count selected by the referencing slot's kind (section 3.2's
// cross-table authority), and the increasing order is the OQ-2 observation.
[BinaryTable(JstfLayout.Tag, JstfModListLayout.HeaderLength, ContractVersion = 1)]
[BinaryScalar("LookupCount", WireType.UInt16, JstfModListLayout.CountAt,
    DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("LookupIndex", "LookupCount", JstfModListLayout.IndicesAt,
    JstfModListLayout.IndexStride)]
[BinaryElement("LookupIndex", "Index", WireType.UInt16, 0, DataField.Payload)]
public readonly ref partial struct JstfModListView { }
