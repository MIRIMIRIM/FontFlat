using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>Compact-remap CFF1 rebuilds over the frozen map (S1c card §2/§3).
///
/// The map freeze materialized the CFF table once; these stages transform that model —
/// charset (the stable-identity carrier), name-keyed encoding (codes ∘ map with the
/// supplements pruned to surviving names), CID-keyed FDSelect (per-glyph FD values
/// remapped, wire format preserved, FD numbers never renumbered) and VORG (records ∘
/// map, dropped glyphs' records deleted) — and hand the result to the existing T3
/// compilers unchanged. CharStrings are re-ordered copies — byte-for-byte while no
/// subr prune plan is parked (S1c), rewritten at their callsubr/callgsubr sites with
/// re-encoded operands when one is (S4 card §3.3), with the Subr INDEX supply drawn
/// from the plan's survivors (null plans keep String and both subroutine INDEXes
/// verbatim: counts unchanged, so both subr bias inputs stay frozen, TN5176 §4.7).
/// Every model forms before anything compiles, so a failure still refuses with
/// nothing published.</summary>
public static partial class SubsetOperation
{
    // ---- name-keyed (S1c card §2.1-§2.4) ----------------------------------------

    private static ReadStatus RebuildCff1NameKeyed(Cff1Table model, GlyphRemap map,
        SubrPrunePlan? prune, BuildBudget budget, out byte[]? cffBytes,
        out FontDiagnostic diagnostic)
    {
        cffBytes = null;
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work(8L * outputCount, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        // Charset: the identity carrier. Every output glyph keeps the SID its input
        // glyph had (W1 §3.3 projection); the source wire format is preserved (OQ-S1c-1
        // lean), except a predefined charset loses the marker as soon as the selection
        // is not the full identity order — under a marker the name comes from the
        // standard table position, which a pruned or reordered output breaks. The
        // conversion appends the charset key the input lacks (the marker was implicit);
        // the compiler's fixpoint then rewrites its operand like any custom charset.
        var sids = new int[outputCount];
        bool fullIdentityOrder = outputCount == model.NumGlyphs;
        for (int o = 0; o < outputCount; o++)
        {
            sids[o] = model.CharsetSids[inputOrder[o]];
            fullIdentityOrder &= inputOrder[o] == o;
        }
        var charsetForm = model.CharsetForm;
        var topDict = model.TopDict.ToArray();
        if (model.CharsetForm.Predefined && !fullIdentityOrder)
        {
            charsetForm = new Cff1CharsetForm(Predefined: false, Format: 0);
            if (!topDict.Any(e => e.Operator == Cff1Layout.OpCharset))
                topDict = [.. topDict, new Cff1DictEntry(Cff1Layout.OpCharset,
                    [new Cff1Operand(0, null)], [])];
        }

        // Encoding (name-keyed only; a CID-keyed Top DICT cannot carry one). Predefined
        // markers keep addressing names through the charset and survive untouched
        // (OQ-S1c-2 lean). A custom encoding rebuilds code-per-glyph through the map,
        // keeps the supplements whose SID resolves to a surviving glyph, and refuses
        // (A2) when the result is no longer expressible in the source form rather than
        // silently converting formats.
        var encodingForm = model.EncodingForm;
        var codes = new int[outputCount];
        var supplements = Array.Empty<Cff1EncodingSupplement>();
        if (!model.EncodingForm.Predefined)
        {
            for (int o = 0; o < outputCount; o++)
                codes[o] = model.EncodingCodes[inputOrder[o]];
            if (model.EncodingSupplements.Length > 0)
            {
                var sidToGlyph = new Dictionary<int, int>();
                for (int gid = 1; gid < model.NumGlyphs; gid++)
                    sidToGlyph.TryAdd(model.CharsetSids[gid], gid);
                var kept = new List<Cff1EncodingSupplement>();
                foreach (var supplement in model.EncodingSupplements)
                {
                    if (sidToGlyph.TryGetValue(supplement.Sid, out int gid) &&
                        map.OutputOf(gid) >= 0)
                        kept.Add(supplement);
                }
                supplements = [.. kept];
            }
            encodingForm = new Cff1EncodingForm(Predefined: false,
                Format: model.EncodingForm.Format,
                HasSupplement: model.EncodingForm.HasSupplement && supplements.Length > 0);
            // A2 (OQ-S1c-2 lean): format 0's per-glyph code array is a byte-covered
            // prefix and format 1 carries at most 255 ranges; a remapped column that no
            // longer fits refuses instead of switching formats.
            int last = LastNonzeroCode(codes);
            if (encodingForm.Format == 0)
            {
                if (last > 0xFF)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                        DiagnosticCode.UnsupportedOperation,
                        model.Location.At(DataField.CharCode, 0, last), out diagnostic,
                        (uint)last, 0xFF);
            }
            else if (EncodingRangeCount(codes, last) > 0xFF)
            {
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    model.Location.At(DataField.CharCode, 0), out diagnostic,
                    (uint)EncodingRangeCount(codes, last), 0xFF);
            }
        }

        // CharStrings: reordered copies; String and both subroutine INDEXes stay
        // verbatim on the model (so their counts — and therefore both subr bias
        // inputs — never move, S1c card §2.2) until a parked prune plan replaces
        // the supply with the survivors' rewritten bodies (S4 card §3.1-§3.3); an
        // INDEX pruned to zero survivors loses its Private DICT's Subrs key.
        byte[][] charStrings, globalSubrs, localSubrs;
        bool hasLocalSubrs;
        Cff1DictEntry[] privateDict;
        if (prune is null)
        {
            charStrings = new byte[outputCount][];
            for (int o = 0; o < outputCount; o++)
                charStrings[o] = model.CharStrings[inputOrder[o]];
            globalSubrs = model.GlobalSubrs.ToArray();
            localSubrs = model.LocalSubrs.ToArray();
            hasLocalSubrs = model.HasLocalSubrs;
            privateDict = model.PrivateDict.ToArray();
        }
        else
        {
            ReadStatus pruneStatus = RewriteSubrProgramsNameKeyed(model, map, prune,
                budget, out charStrings, out globalSubrs, out localSubrs,
                out hasLocalSubrs, out diagnostic);
            if (pruneStatus != ReadStatus.Done) return pruneStatus;
            privateDict = hasLocalSubrs || !model.HasLocalSubrs
                ? model.PrivateDict.ToArray()
                : PrivateEntriesWithoutSubrs(model.PrivateDict);
        }

        var rebuilt = new Cff1Table(model.HeaderMajor, model.HeaderMinor,
            model.HeaderHdrSize, model.NameBytes.ToArray(),
            topDict, model.Strings.ToArray(), globalSubrs,
            localSubrs, hasLocalSubrs, privateDict,
            charsetForm, sids, encodingForm, codes, supplements, charStrings,
            model.Location);
        return Cff1TableCompiler.TryCompile(rebuilt, out cffBytes, out diagnostic, budget);
    }

    private static int LastNonzeroCode(ReadOnlySpan<int> codes)
    {
        int last = codes.Length - 1;
        while (last >= 1 && codes[last] == 0) last--;
        return last;
    }

    private static int EncodingRangeCount(ReadOnlySpan<int> codes, int last)
    {
        int ranges = 0;
        for (int gid = 1; gid <= last;)
        {
            ranges++;
            gid++;
            while (gid <= last && codes[gid] == codes[gid - 1] + 1) gid++;
        }
        return ranges;
    }

    // ---- CID-keyed (S1c card §2.5) ----------------------------------------------

    private static ReadStatus RebuildCidCff1(CidCff1Table model, GlyphRemap map,
        SubrPrunePlan? prune, BuildBudget budget, out byte[]? cffBytes,
        out FontDiagnostic diagnostic)
    {
        cffBytes = null;
        var inputOrder = map.OutputToInput;
        int outputCount = map.OutputGlyphCount;
        if (!budget.Work(8L * outputCount + model.FdCount, model.Location,
                out diagnostic))
            return ReadStatus.ResourceLimit;

        // Charset: the CID identity carrier, same rule as the name-keyed SIDs — every
        // output glyph keeps the CID its input glyph had, the source wire format is
        // preserved, and a predefined charset loses its marker as soon as the
        // selection is not the full identity order (the key appends when the input
        // carried only the implicit marker; the CID fixtures carry the key).
        var cids = new int[outputCount];
        bool fullIdentityOrder = outputCount == model.NumGlyphs;
        for (int o = 0; o < outputCount; o++)
        {
            cids[o] = model.CharsetCids[inputOrder[o]];
            fullIdentityOrder &= inputOrder[o] == o;
        }
        var charsetForm = model.CharsetForm;
        var topDict = model.TopDict.ToArray();
        if (model.CharsetForm.Predefined && !fullIdentityOrder)
        {
            charsetForm = new Cff1CharsetForm(Predefined: false, Format: 0);
            if (!topDict.Any(e => e.Operator == Cff1Layout.OpCharset))
                topDict = [.. topDict, new Cff1DictEntry(Cff1Layout.OpCharset,
                    [new Cff1Operand(0, null)], [])];
        }

        // FDSelect: the per-glyph FD values remap through the frozen map and the wire
        // format stays (OQ-S1c-3 lean) — format 0 stays the per-glyph array, format 3
        // re-derives its maximal runs from the remapped column. FD numbers are
        // explicit wire fields, not GIDs: the value domain never renumbers, and the
        // FDArray keeps every FD including unreferenced ones (legal per the reader).
        var newFd = new int[outputCount];
        int maximumFd = 0;
        for (int o = 0; o < outputCount; o++)
        {
            ReadStatus status = model.TryGetFdIndex(inputOrder[o], out newFd[o],
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (newFd[o] > maximumFd) maximumFd = newFd[o];
        }
        var fdSelect = model.FdSelect.Format == CidFdSelectFormat.PerGlyph
            ? CidFdSelect.FromPerGlyph(outputCount, maximumFd + 1, newFd,
                model.FdSelect.Location, model.FdSelect.Offset)
            : CidFdSelect.FromRanges(outputCount, maximumFd + 1,
                FdRangesOf(newFd), model.FdSelect.Location, model.FdSelect.Offset);

        // CharStrings reorder as copies — byte-for-byte on the S1c verbatim path
        // (null plan), rewritten at their call sites when a prune plan is parked
        // (S4 card §3.3). The per-FD supply follows the plan: every FD's Local
        // Subrs renew from its survivor bucket under that FD's own context (an FD
        // no retained glyph references has an empty bucket, so its INDEX prunes
        // away and its Private DICT loses the Subrs key, card §2.2; the FDArray
        // itself keeps every FD), and the Global Subrs renew from the face-wide
        // bucket under each body's scanning context.
        byte[][] charStrings;
        CidFontDict[] fontDicts;
        byte[][] globalSubrs;
        if (prune is null)
        {
            charStrings = new byte[outputCount][];
            for (int o = 0; o < outputCount; o++)
                charStrings[o] = model.CharStrings[inputOrder[o]];
            fontDicts = model.FontDicts.ToArray();
            globalSubrs = model.GlobalSubrs.ToArray();
        }
        else
        {
            byte[][] globals = model.GlobalSubrs.ToArray();
            var fdLocals = new byte[model.FdCount][][];
            for (int fd = 0; fd < model.FdCount; fd++)
                fdLocals[fd] = model[fd].LocalSubrs.ToArray();
            var programFds = new int[outputCount];
            charStrings = new byte[outputCount][];
            var selected = new byte[outputCount][];
            for (int o = 0; o < outputCount; o++)
            {
                ReadStatus fdStatus = model.TryGetFdIndex(inputOrder[o],
                    out programFds[o], out diagnostic);
                if (fdStatus != ReadStatus.Done) return fdStatus;
                selected[o] = model.CharStrings[inputOrder[o]];
            }
            ReadStatus pruneStatus = RewritePrograms(selected, programFds,
                globals, fdLocals, prune, cff2: false, regionCounts: null,
                vsindexByFd: null, model.Location, budget, out charStrings, out diagnostic);
            if (pruneStatus != ReadStatus.Done) return pruneStatus;
            // Every surviving body rides the rewrite once the plan is parked
            // (§3.3): a null slot's INDEX kept every body, but its bodies' calls
            // into a pruned sibling INDEX still renumber — the identity slot
            // keeps its own count, so its same-INDEX operands re-derive to
            // their original values (the value rule).
            SubrIndexPlan effectiveGlobal = prune.Global ??
                IdentityIndexPlan(globals.Length);
            var contextFds = new int[effectiveGlobal.NewCount];
            for (int s = 0; s < effectiveGlobal.NewCount; s++)
                contextFds[s] = prune.GlobalContextFd.TryGetValue(
                    effectiveGlobal.Survivors[s], out int ctx) ? ctx : 0;
            pruneStatus = RewriteSurvivorBodies(globals, effectiveGlobal.Survivors,
                contextFds, globals, fdLocals, prune, cff2: false,
                regionCounts: null, vsindexByFd: null, entryVsindexByOld: null,
                model.Location, budget, out globalSubrs, out diagnostic);
            if (pruneStatus != ReadStatus.Done) return pruneStatus;
            fontDicts = new CidFontDict[model.FdCount];
            for (int fd = 0; fd < model.FdCount; fd++)
            {
                var old = model[fd];
                var localPlan = prune.LocalByFd[fd];
                // Each local body's context is its own FD: a zero-filled column
                // would rewrite FD>0 bodies over FD0's table, plan and bias — the
                // misfire that refused a legal cross-FD caller at its first site.
                SubrIndexPlan effectiveLocal = localPlan ??
                    IdentityIndexPlan(fdLocals[fd].Length);
                var bodyFds = new int[effectiveLocal.NewCount];
                Array.Fill(bodyFds, fd);
                pruneStatus = RewriteSurvivorBodies(fdLocals[fd],
                    effectiveLocal.Survivors, bodyFds, globals,
                    fdLocals, prune, cff2: false, regionCounts: null,
                    vsindexByFd: null, entryVsindexByOld: null, model.Location,
                    budget, out var renewedLocals, out diagnostic);
                if (pruneStatus != ReadStatus.Done) return pruneStatus;
                bool renewedHasLocals = localPlan is null ? old.HasLocalSubrs
                    : effectiveLocal.NewCount > 0;
                var privateEntries = renewedHasLocals || !old.HasLocalSubrs
                    ? old.PrivateEntries.ToArray()
                    : PrivateEntriesWithoutSubrs(old.PrivateEntries);
                fontDicts[fd] = new CidFontDict(old.Ordinal, old.RawBytes.ToArray(),
                    old.Entries.ToArray(), old.PrivateOffset, old.PrivateSize,
                    privateEntries, renewedHasLocals, renewedLocals, old.Location);
            }
        }
        var derived = new CidCff1Table(model.HeaderMajor, model.HeaderMinor,
            model.HeaderHdrSize, model.HeaderOffSize, model.NameBytes.ToArray(),
            topDict, model.Strings.ToArray(), globalSubrs,
            model.Ros, model.CidCount, fdSelect, fontDicts, cids,
            charStrings, charsetForm, model.Location, model.FdArrayOffset,
            model.CharsetOffset, model.FdSelectOffset, model.CharStringsOffset);
        return CidCff1TableCompiler.TryCompile(derived, new CidEditPlan(),
            out cffBytes, out diagnostic, budget);
    }

    private static CidFdRange[] FdRangesOf(ReadOnlySpan<int> fds)
    {
        var ranges = new List<CidFdRange>();
        for (int gid = 0; gid < fds.Length; gid++)
        {
            if (ranges.Count > 0 && ranges[^1].FdIndex == fds[gid]) continue;
            ranges.Add(new CidFdRange(ranges.Count, gid, fds[gid]));
        }
        return [.. ranges];
    }

    // ---- VORG (S1c card §3) ------------------------------------------------------

    private static ReadStatus RebuildVorg(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? vorgBytes,
        out FontDiagnostic diagnostic)
    {
        vorgBytes = null;
        ReadStatus status = ReadTableBytes(directory, Vorg, out var vorgAt,
            out var vorgSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VorgContext.TryCreate(maxp, vorgAt, out var context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VorgView.TryOpen(vorgSource, out var view, out diagnostic, vorgAt);
        if (status != ReadStatus.Done) return status;
        status = view.TryRead(context!, out var vorg, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;

        // Records ride the same remap as the CFF rebuild; a dropped glyph's record
        // goes with it (a dangling record would point outside the output domain).
        // The map's output order is the ascending stable identity, which on a CFF
        // face need not ascend by input GID, so the surviving records re-sort by
        // output GID — strictly ascending, no duplicates by map injectivity.
        if (!budget.Work(vorg!.NumVertOriginYMetrics, vorgAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var records = new List<VorgRecord>();
        foreach (var record in vorg.Records)
        {
            int output = map.OutputOf(record.GlyphIndex);
            if (output < 0) continue;
            records.Add(new VorgRecord((ushort)output, record.VertOriginY));
        }
        records.Sort(static (a, b) => a.GlyphIndex.CompareTo(b.GlyphIndex));
        var rebuilt = new VorgTable(vorg.DefaultVertOriginY, [.. records],
            checked((ushort)map.OutputGlyphCount), vorgAt, maxp.Location);
        return VorgTableCompiler.TryCompile(rebuilt, out vorgBytes, out diagnostic,
            budget);
    }
}
