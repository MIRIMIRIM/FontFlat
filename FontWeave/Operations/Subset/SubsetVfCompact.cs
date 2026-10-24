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
/// DeltaSetIndexMap domains and prune unreachable variation data;
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
        out FontDiagnostic diagnostic) =>
        SpliceGvarBlocks(model, Array.ConvertAll(map.OutputToInput.ToArray(),
            static input => (int)input), budget, out gvarBytes, out diagnostic);

    /// <summary>Retain-GIDs keeps every GID but clears each unretained glyph's
    /// outline (the glyf rebuild's empty-slot form). A gvar block describes its
    /// glyph's point set — the outlines' points plus the four phantoms — so a
    /// cleared glyph's block would describe points that no longer exist, and a
    /// whole-table gvar decode or a variable instantiation would fail on the
    /// emitted font. Those slots are emptied (the absent-glyph form); a retained
    /// slot keeps its block verbatim. The table re-emits through the same splice,
    /// so retain's gvar is no longer a raw pass-through once any slot is cleared.</summary>
    private static ReadStatus RebuildRetainedGvar(Tables.Variation.GvarTable model,
        bool[] retained, BuildBudget budget, out byte[]? gvarBytes,
        out FontDiagnostic diagnostic)
    {
        var inputOrder = new int[retained.Length];
        for (int gid = 0; gid < retained.Length; gid++)
            inputOrder[gid] = retained[gid] ? gid : -1;
        return SpliceGvarBlocks(model, inputOrder, budget, out gvarBytes, out diagnostic);
    }

    /// <summary>The one block splice: output slot o carries input slot
    /// <c>inputOrder[o]</c>'s block verbatim, or an empty block when the entry is
    /// -1 (the absent-glyph form).</summary>
    private static ReadStatus SpliceGvarBlocks(Tables.Variation.GvarTable model,
        int[] inputOrder, BuildBudget budget, out byte[]? gvarBytes,
        out FontDiagnostic diagnostic)
    {
        gvarBytes = null;
        int outputCount = inputOrder.Length;
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
            if (input >= 0) total += inputOffsets[input + 1] - inputOffsets[input];
            offsets[o + 1] = checked((int)total);
        }
        if (!budget.Buffer(total, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var blocks = new byte[total];
        for (int o = 0; o < outputCount; o++)
        {
            int input = inputOrder[o];
            if (input < 0) continue;
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
        var store = model.Store;
        if (map.OutputGlyphCount != map.InputGlyphCount)
        {
            DeltaSetIndexMapTable?[] maps = [advanceMap, lsbMap, rsbMap];
            status = PruneMetricVariationStore(store, maps, budget,
                out var pruned, out diagnostic);
            if (status != ReadStatus.Done) return status;
            store = pruned!;
            (advanceMap, lsbMap, rsbMap) = (maps[0], maps[1], maps[2]);
        }
        status = HvarTableCompiler.TryCreate(store, advanceMap, lsbMap,
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
        var store = model.Store;
        if (map.OutputGlyphCount != map.InputGlyphCount)
        {
            DeltaSetIndexMapTable?[] maps = [advanceMap, tsbMap, bsbMap, vOrgMap];
            status = PruneMetricVariationStore(store, maps, budget,
                out var pruned, out diagnostic);
            if (status != ReadStatus.Done) return status;
            store = pruned!;
            (advanceMap, tsbMap, bsbMap, vOrgMap) = (maps[0], maps[1], maps[2], maps[3]);
        }
        status = VvarTableCompiler.TryCreate(store, advanceMap, tsbMap,
            bsbMap, vOrgMap, model.Store.AxisCount, map.OutputGlyphCount,
            out var rebuilt, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        return VvarTableCompiler.TryCompile(rebuilt!, out vvarBytes,
            out diagnostic, budget);
    }

    // ---- CFF2 (SV card §2.5) ------------------------------------------------------

    /// <summary>Re-slots the parked CFF2 model over the frozen map (OQ-SV-4
    ///attached lean): output item o of the CharString INDEX is the input item
    /// <c>outputToInput[o]</c> as a byte[] reference move while no subr prune plan
    /// is parked (zero copies, zero charstring semantics — the blend/vsindex
    /// operand domains are never touched), or a plan-rewritten program whose
    /// callsubr/callgsubr sites re-encode over the pruned INDEXes (S4 card §3.3,
    /// OQ-S4-1 lean); the per-glyph FD index re-slots the same way (FD numbers are
    /// explicit wire attribute fields whose value domain never renumbers), and
    /// every other member — TopDICT, FDArray with its Private DICTs, the FDSelect
    /// wire format, the variation store — carries over verbatim on the derived
    /// model, whose compile re-runs the layout fixpoint over the subset sizes and
    /// re-answers the FDSelect runs in the source's format family. Under a plan the
    /// Global Subrs renew from the plan's survivors and every FontDICT's Private
    /// DICT renews from its FD's survivor bucket (a bucket pruned to zero loses
    /// the Private DICT's 0x13 key with its INDEX). The re-slot deliberately
    /// bypasses the TryCreate factory: the read model is already fully guarded,
    /// and staying on it keeps the semantic boundary explicit.</summary>
    private static ReadStatus RebuildCff2(Tables.Cff2Table model, GlyphRemap map,
        SubrPrunePlan? prune, BuildBudget budget, out byte[]? cff2Bytes,
        out FontDiagnostic diagnostic)
    {
        cff2Bytes = null;
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work(outputCount, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[][] charStrings;
        byte[][] globalSubrs;
        Tables.Cff2FontDict[] fontDicts;
        if (prune is null)
        {
            charStrings = new byte[outputCount][];
            var fdIndex = new int[outputCount];
            for (int o = 0; o < outputCount; o++)
            {
                int input = inputOrder[o];
                charStrings[o] = model.CharStrings[input];
                fdIndex[o] = model.FdIndex[input];
            }
            globalSubrs = model.GlobalSubrsInternal;
            fontDicts = model.FontDictsInternal;
            var rebuilt = new Tables.Cff2Table(model.MinorVersion, model.HeaderSize,
                model.TopDictInternal, model.HasVariationStore, globalSubrs,
                charStrings, fontDicts, model.FdSelectFormat, fdIndex,
                model.Store, model.CharstringStats, model.Location,
                model.ConsumedLength);
            return Tables.Cff2TableCompiler.TryCompile(rebuilt, out cff2Bytes,
                out diagnostic, budget);
        }

        byte[][] globals = model.GlobalSubrsInternal;
        int fdCount = model.FontDictCount;
        var fdLocals = new byte[fdCount][][];
        for (int fd = 0; fd < fdCount; fd++)
            fdLocals[fd] = model.FontDictsInternal[fd].Private?.LocalSubrs ?? [];
        // The per-FD Private vsindex column (TN5171 §5): every rewrite walk seeds
        // its blend selector from the owning DICT, the same default the reach
        // walk read — a fresh body frame inherits its FD's DICT, not slot 0.
        var vsindexByFd = new int[fdCount];
        for (int fd = 0; fd < fdCount; fd++)
            vsindexByFd[fd] = model.FontDictsInternal[fd].Private?.VsindexValue ?? 0;
        var programFds = new int[outputCount];
        charStrings = new byte[outputCount][];
        var selected = new byte[outputCount][];
        for (int o = 0; o < outputCount; o++)
        {
            int input = inputOrder[o];
            programFds[o] = model.FdIndex[input];
            selected[o] = model.CharStrings[input];
        }
        ReadStatus pruneStatus = RewritePrograms(selected, programFds,
            globals, fdLocals, prune, cff2: true,
            RegionCountsByVsindex(model.Store), vsindexByFd, model.Location, budget,
            out charStrings, out diagnostic);
        if (pruneStatus != ReadStatus.Done) return pruneStatus;
        var fdIndexPruned = new int[outputCount];
        for (int o = 0; o < outputCount; o++)
            fdIndexPruned[o] = model.FdIndex[inputOrder[o]];
        // Every surviving body rides the rewrite once the plan is parked
        // (§3.3): a null slot's INDEX kept every body, but its bodies' calls
        // into a pruned sibling INDEX still renumber — the identity slot
        // keeps its own count, so its same-INDEX operands re-derive to
        // their original values (the value rule).
        // Every surviving body's rewrite frame seeds its blend selector from the
        // plan's per-body entry memo — the selector its callers armed at the call
        // site (TN5171 §5), which an explicit vsindex before the call diverges
        // from the FD default the glyph frames keep. A multi-valued entry set
        // never parks: the plan-time guard refused the face.
        SubrIndexPlan effectiveGlobal = prune.Global ??
            IdentityIndexPlan(globals.Length);
        var contextFds = new int[effectiveGlobal.NewCount];
        for (int s = 0; s < effectiveGlobal.NewCount; s++)
            contextFds[s] = prune.GlobalContextFd.TryGetValue(
                effectiveGlobal.Survivors[s], out int ctx) ? ctx : 0;
        pruneStatus = RewriteSurvivorBodies(globals, effectiveGlobal.Survivors,
            SubrWalk.KindGlobal, contextFds, globals, fdLocals, prune,
            cff2: true,
            RegionCountsByVsindex(model.Store), vsindexByFd,
            prune.GlobalEntryVsindex, model.Location,
            budget, out globalSubrs, out diagnostic);
        if (pruneStatus != ReadStatus.Done) return pruneStatus;
        fontDicts = new Tables.Cff2FontDict[fdCount];
        for (int fd = 0; fd < fdCount; fd++)
        {
            var old = model.FontDictsInternal[fd];
            var localPlan = prune.LocalByFd[fd];
            if (localPlan is null && old.Private is null)
            {
                // An FD without a Private DICT owns no local INDEX (its slot
                // rides null over an empty body set): the DICT-less FontDICT
                // carries over whole.
                fontDicts[fd] = old;
                continue;
            }
            var privateDict = old.Private;
            if (privateDict is null)
            {
                // Unreachable by construction: an FD without a Private DICT owns
                // no local INDEX, so its plan slot stays null and the branch above
                // carried the FontDICT — refuse defensively instead of inventing
                // the DICT.
                diagnostic = new(DiagnosticCode.UnsupportedOperation,
                    model.Location, (uint)fd, 0UL);
                return ReadStatus.UnsupportedFormat;
            }
            // Each local body's context is its own FD: a zero-filled column
            // would rewrite FD>0 bodies over FD0's table, plan and bias — the
            // misfire that refused a legal cross-FD caller at its first site.
            SubrIndexPlan effectiveLocal = localPlan ??
                IdentityIndexPlan(fdLocals[fd].Length);
            var bodyFds = new int[effectiveLocal.NewCount];
            Array.Fill(bodyFds, fd);
            // This FD's entry-selector column; an FD no call site ever entered
            // has no survivors to seed, so the null slot stays unconsulted.
            prune.LocalEntryVsindex.TryGetValue(fd, out var entryByOld);
            pruneStatus = RewriteSurvivorBodies(fdLocals[fd], effectiveLocal.Survivors,
                SubrWalk.KindLocal, bodyFds, globals, fdLocals, prune, cff2: true,
                RegionCountsByVsindex(model.Store), vsindexByFd, entryByOld,
                model.Location, budget, out var renewedLocals, out diagnostic);
            if (pruneStatus != ReadStatus.Done) return pruneStatus;
            bool renewedHasLocals = localPlan is null ? privateDict.HasLocalSubrs
                : effectiveLocal.NewCount > 0;
            var entries = renewedHasLocals || !privateDict.HasLocalSubrs
                ? privateDict.Entries
                : [.. privateDict.Entries.Where(entry =>
                    entry.Operator != Tables.Cff2Operators.PrivateLocalSubrs)];
            fontDicts[fd] = new Tables.Cff2FontDict(old.Entry,
                new Tables.Cff2PrivateDict(entries, renewedLocals,
                    entries.Sum(static entry => entry.BlendCount),
                    privateDict.VsindexValue));
        }
        var rebuiltPruned = new Tables.Cff2Table(model.MinorVersion, model.HeaderSize,
            model.TopDictInternal, model.HasVariationStore, globalSubrs,
            charStrings, fontDicts, model.FdSelectFormat, fdIndexPruned,
            model.Store, model.CharstringStats, model.Location,
            model.ConsumedLength);
        return Tables.Cff2TableCompiler.TryCompile(rebuiltPruned, out cff2Bytes,
            out diagnostic, budget);
    }
}
