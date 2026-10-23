using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>cmap rebuild for the retain-GIDs slice (S1a card §5).
///
/// Every Unicode-semantic input record (platform 0 except the variation-sequence
/// record (0,5), or (3,0)/(3,1)/(3,10)) is enumerated through the existing typed
/// models and its mappings pruned to the retained set. The (0,5) record and the
/// non-Unicode records are read as evidence but not merged, and land in the report's
/// consumed-not-emitted list rather than being refused — refusing would misfire on
/// mainstream real corpora (the (0,5) drop is OQ-F14: subsetting serves no UVS
/// semantics, and the canonical pair never carries a format 14 subtable). The pruned
/// union feeds the canonical {((3,1), format 4), ((3,10), format 12)} factory;
/// record-level mapping conflicts are not arbitrated (first record in wire order wins
/// a duplicate code point).</summary>
public static partial class SubsetOperation
{
    private static ReadStatus RebuildCmap(PrepareState state, SfntDirectoryView directory,
        int cmapOrdinal, MaxpTable maxp, bool[] retained, BuildBudget budget,
        out byte[]? cmapBytes, out List<CmapConsumedRecord> consumedNotEmitted,
        out FontDiagnostic diagnostic, GlyphRemap? map = null)
    {
        cmapBytes = null;
        consumedNotEmitted = [];
        DataLocation cmapAt = directory.TableLocation(cmapOrdinal);
        // Single parse (SR card §2.4, OQ-SR-5): a resolved-root request parks the one
        // materialized model on the state and the rebuild consumes it — no second read.
        // An explicit-GID request leaves the park null and the read chain runs exactly
        // as before, byte for byte (the zero-drift mechanical guarantee).
        CmapTable? model = state.CmapModel;
        ReadStatus status;
        if (model is null)
        {
            status = directory.TryGetTableBytes(cmapOrdinal, out var cmapSource,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = CmapContext.TryCreate(maxp, cmapAt, out var context, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = CmapView.TryOpen(cmapSource, out var view, out diagnostic, cmapAt);
            if (status != ReadStatus.Done) return status;
            status = view.TryRead(context!, out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }

        // One union keyed by code point; the first record in wire order supplies the
        // value for a duplicate, and only mappings into the retained set survive. The
        // compact map transforms each surviving value to its output ordinal, so the
        // rebuilt mapping set is the input mapping set composed with the map.
        if (!budget.Work(model!.Records.Length, cmapAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var union = new SortedDictionary<uint, ushort>();
        var records = model.Records;
        for (int i = 0; i < records.Length; i++)
        {
            if (!IsUnicodeSemantic(records[i].PlatformId, records[i].EncodingId))
            {
                consumedNotEmitted.Add(new(records[i].PlatformId, records[i].EncodingId));
                continue;
            }
            status = model.TryGetSubtable(i, out var subtable, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = model.TryGetMappings(subtable!, out var mappings, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            foreach (var mapping in mappings!)
            {
                if (!retained[mapping.GlyphId]) continue;
                union.TryAdd(mapping.CharacterCode, map is { } remap
                    ? checked((ushort)remap.OutputOf(mapping.GlyphId))
                    : mapping.GlyphId);
            }
        }
        var pruned = new CmapMapping[union.Count];
        int at = 0;
        foreach ((uint code, ushort gid) in union) pruned[at++] = new(code, gid);
        status = CmapTableCompiler.TryBuildCanonicalPair(model, pruned, out var rebuilt,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = CmapTableCompiler.TryCompile(rebuilt!, out cmapBytes, out diagnostic,
            budget);
        return status;
    }

    /// <summary>Records whose scalar-mapping semantics this slice treats as Unicode:
    /// platform 0 except the variation-sequence record (0,5), plus the Windows Unicode
    /// encodings (3,0)/(3,1)/(3,10). (0,5) maps variation sequences, not scalars, so it
    /// takes the consumed-not-emitted routing (OQ-F14); a format 14 subtable hung off
    /// any other record stays inside the queried set and refuses through the subtable
    /// read chain, which has no scalar reader for that format.</summary>
    private static bool IsUnicodeSemantic(ushort platformId, ushort encodingId) =>
        platformId == 0 && encodingId != 5 ||
        (platformId == 3 && encodingId is 0 or 1 or 10);
}
