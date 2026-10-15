using FontWeave.Core;

namespace FontWeave.Operations.Subset;

/// <summary>Compact-remap variable-family rebuilds over the frozen map (SV card §2).
///
/// The admission chain materialized each variable model once (SV card §1); these
/// stages re-slot that model over the map's ascending stable identity and hand the
/// result to the T5 compilers unchanged. gvar re-splices its per-GlyphVariationData
/// blocks byte-for-byte (OQ-SV-1 lean: the block interiors — shared peak tuple
/// references, private point numbers, intermediate regions — are block-internal
/// data with no GID domain, so they move verbatim); HVAR/VVAR re-map their
/// DeltaSetIndexMap domains (§2.3, commit ③); CFF2 re-slots CharStrings/FDSelect
/// (§2.5, commit ④). Every model forms before anything compiles, so a failure
/// still refuses with nothing published.</summary>
public static partial class SubsetOperation
{
    // ---- gvar (SV card §2.1) ------------------------------------------------------

    /// <summary>Re-slots the parked gvar model over the frozen map: output slot
    /// o's GlyphVariationData block is the input slot <c>outputToInput[o]</c>'s
    /// block copied byte-for-byte into a fresh contiguous buffer, and the new
    /// model (same axisCount/flags/shared tuples, re-derived output-domain
    /// offsets, the block buffer as its table bytes with the array base at zero)
    /// compiles through <see cref="Tables.Variation.GvarTableCompiler.TryCompile"/>
    /// — the compiler re-writes the header, the offsets array and the shared
    /// tuples (F2DOT14 re-encode is byte-identical) and emits the blocks verbatim
    /// with the short-form even pad. The offsets form (flags bit0) is preserved;
    /// an output total that overflows the source form refuses at the compile
    /// plan (the T5-01 width-class precedent).</summary>
    private static ReadStatus RebuildGvar(Tables.Variation.GvarTable model,
        GlyphRemap map, BuildBudget budget, out byte[]? gvarBytes,
        out FontDiagnostic diagnostic)
    {
        gvarBytes = null;
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        var inputOffsets = model.GlyphVariationDataOffsets;
        var source = model.RawTableBytes;
        int sourceBase = model.GvdArrayOffset;

        // Relay plan: the output block sizes first (the whole plan completes
        // before the buffer exists), then the byte-for-byte block copies.
        if (!budget.Work(outputCount + 1, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var offsets = new int[outputCount + 1];
        long total = 0;
        for (int o = 0; o < outputCount; o++)
        {
            int input = inputOrder[o];
            total += inputOffsets[input + 1] - inputOffsets[input];
            offsets[o + 1] = checked((int)total);
        }
        if (!budget.Buffer(total, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var blocks = new byte[total];
        for (int o = 0; o < outputCount; o++)
        {
            int input = inputOrder[o];
            int start = sourceBase + inputOffsets[input];
            int size = inputOffsets[input + 1] - inputOffsets[input];
            source.AsSpan(start, size).CopyTo(blocks.AsSpan(offsets[o], size));
        }

        var rebuilt = new Tables.Variation.GvarTable(model.AxisCount, outputCount,
            model.Flags, model.SharedTuples, offsets, 0, blocks, model.Location,
            blocks.Length);
        return Tables.Variation.GvarTableCompiler.TryCompile(rebuilt,
            out gvarBytes, out diagnostic, budget);
    }
}
