using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Operations.Subset;

/// <summary>Compact-remap variable-family rebuilds over the frozen map (SV card §2).
///
/// The admission chain materialized each variable model once (SV card §1); these
/// stages re-slot that model over the map's ascending stable identity and hand the
/// result to the T5 compilers unchanged. gvar re-splices its per-GlyphVariationData
/// blocks byte-for-byte (OQ-SV-1 lean: the block interiors — shared peak tuple
/// references, private point numbers, intermediate regions — are block-internal
/// data with no GID domain, so they move verbatim); HVAR/VVAR re-map their
/// DeltaSetIndexMap domains (§2.3: the store's rows carry no GID domain and
/// re-emit byte-identical, explicit maps re-resolve per output gid in the input
/// domain, and the NULL advance map's implicit reading materializes, OQ-SV-6);
/// CFF2 re-slots CharStrings/FDSelect (§2.5, commit ④). Every model forms before anything compiles, so a failure
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

    // ---- HVAR/VVAR (SV card §2.3) -------------------------------------------------

    /// <summary>Re-roots one explicit DeltaSetIndexMap over the frozen map: entry
    /// o resolves the input gid <c>outputToInput[o]</c> through the source map —
    /// the last-entry fallback resolves in the input domain first, then the row
    /// lands in the output map, so the output map covers the whole output domain
    /// with no fallback of its own. The source entryFormat is preserved ("no
    /// entryFormat widening"); a resolved pair outside it refuses through the
    /// compiler's CheckRepresentable (unreachable in practice — remapping never
    /// changes the (outer, inner) values — but the defense stays).</summary>
    private static ReadStatus RemapDeltaSetIndexMap(DeltaSetIndexMapTable sourceMap,
        GlyphRemap map, BuildBudget budget, out DeltaSetIndexMapTable? remapped,
        out FontDiagnostic diagnostic)
    {
        remapped = null;
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work(outputCount, sourceMap.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var entries = new DeltaSetIndex[outputCount];
        for (int o = 0; o < outputCount; o++)
        {
            if (!sourceMap.TryGetMapping(inputOrder[o], out entries[o]))
                return ReadWire.Fail(ReadStatus.InvalidFormat,
                    DiagnosticCode.IndexOutOfRange,
                    sourceMap.Location.AtLocal(DataField.Count,
                        DeltaSetIndexMapLayout.MapCountAt),
                    out diagnostic, (ulong)inputOrder[o], 0UL);
        }
        return DeltaSetIndexMapTableCompiler.TryCreate(sourceMap.EntryFormat,
            entries, out remapped, out diagnostic, budget, sourceMap.Location);
    }

    /// <summary>Materializes the NULL advance map's implicit reading (OQ-SV-6
    /// lean): the implicit index is (0, gid) in the model's own domain, so output
    /// gid o would dereference input row o — the wrong row whenever the selection
    /// prunes or reorders. The materialized map pins entries[o] = (0,
    /// outputToInput[o]) with the minimal representable entryFormat (outer level
    /// 0, inner the input gid; innerBits cover the largest input gid and the
    /// entry size covers the packed pair — a mechanical derivation,
    /// CheckRepresentable is the backstop).</summary>
    private static ReadStatus MaterializeAdvanceMap(GlyphRemap map,
        DataLocation location, BuildBudget budget,
        out DeltaSetIndexMapTable? materialized, out FontDiagnostic diagnostic)
    {
        materialized = null;
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work(outputCount, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var entries = new DeltaSetIndex[outputCount];
        int maxInner = 0;
        for (int o = 0; o < outputCount; o++)
        {
            ushort input = inputOrder[o];
            entries[o] = new(0, input);
            if (input > maxInner) maxInner = input;
        }
        int innerBits = 1;
        while (maxInner > (1 << innerBits) - 1) innerBits++;
        int entrySize = 1;
        while (maxInner > ((1 << (8 * entrySize)) - 1)) entrySize++;
        byte entryFormat = unchecked((byte)(((entrySize - 1) << 4) | (innerBits - 1)));
        return DeltaSetIndexMapTableCompiler.TryCreate(entryFormat, entries,
            out materialized, out diagnostic, budget, location);
    }

    private static ReadStatus RebuildHvar(HvarTable model, GlyphRemap map,
        BuildBudget budget, out byte[]? hvarBytes, out FontDiagnostic diagnostic)
    {
        hvarBytes = null;
        // The store's rows are (outer, inner)-keyed with no GID domain: the same
        // store model re-emits byte-identical through the T5-01 compiler. The
        // advance map re-roots (explicit remap, or the NULL implicit reading
        // materialized, OQ-SV-6); lsb/rsb stay NULL when the source is NULL —
        // their advance-index fallback is a read-side rule over the now
        // output-correct advance map, not stored data.
        ReadStatus status = model.AdvanceWidthMapping is { } advanceSource
            ? RemapDeltaSetIndexMap(advanceSource, map, budget, out var advanceMap,
                out diagnostic)
            : MaterializeAdvanceMap(map, model.Location, budget, out advanceMap,
                out diagnostic);
        if (status != ReadStatus.Done) return status;
        DeltaSetIndexMapTable? lsbMap = null, rsbMap = null;
        if (model.LsbMapping is { } lsbSource)
        {
            status = RemapDeltaSetIndexMap(lsbSource, map, budget, out lsbMap,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (model.RsbMapping is { } rsbSource)
        {
            status = RemapDeltaSetIndexMap(rsbSource, map, budget, out rsbMap,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        status = HvarTableCompiler.TryCreate(model.Store, advanceMap, lsbMap,
            rsbMap, model.Store.AxisCount, map.OutputGlyphCount,
            out var rebuilt, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        return HvarTableCompiler.TryCompile(rebuilt!, out hvarBytes,
            out diagnostic, budget);
    }

    private static ReadStatus RebuildVvar(VvarTable model, GlyphRemap map,
        BuildBudget budget, out byte[]? vvarBytes, out FontDiagnostic diagnostic)
    {
        vvarBytes = null;
        // OQ-T5-04-4 on the SV slice's map face: only advanceHeight carries the
        // implicit (0, gid) reading, so only it materializes when NULL; tsb/
        // bsb/vOrg NULL is the no-variation answer and materializing would
        // invent data — they keep their NULL slots. Non-NULL side maps re-map
        // like their HVAR counterparts. The store re-emits byte-identical.
        ReadStatus status = model.AdvanceHeightMapping is { } advanceSource
            ? RemapDeltaSetIndexMap(advanceSource, map, budget, out var advanceMap,
                out diagnostic)
            : MaterializeAdvanceMap(map, model.Location, budget, out advanceMap,
                out diagnostic);
        if (status != ReadStatus.Done) return status;
        DeltaSetIndexMapTable? tsbMap = null, bsbMap = null, vOrgMap = null;
        if (model.TsbMapping is { } tsbSource)
        {
            status = RemapDeltaSetIndexMap(tsbSource, map, budget, out tsbMap,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (model.BsbMapping is { } bsbSource)
        {
            status = RemapDeltaSetIndexMap(bsbSource, map, budget, out bsbMap,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (model.VOrgMapping is { } vOrgSource)
        {
            status = RemapDeltaSetIndexMap(vOrgSource, map, budget, out vOrgMap,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        status = VvarTableCompiler.TryCreate(model.Store, advanceMap, tsbMap,
            bsbMap, vOrgMap, model.Store.AxisCount, map.OutputGlyphCount,
            out var rebuilt, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        return VvarTableCompiler.TryCompile(rebuilt!, out vvarBytes,
            out diagnostic, budget);
    }
}
