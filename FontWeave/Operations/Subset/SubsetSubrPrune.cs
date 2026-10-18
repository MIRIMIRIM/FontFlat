using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>CFF subr pruning: compact-mode Subr INDEX subsetting with biased call
/// operand re-encoding (S4 card §1-§4).
///
/// The compact rebuild had kept both subroutine INDEXes verbatim with their counts —
/// and therefore both bias inputs — frozen (S1c card §2.2), which made any real
/// subset's charstrings keep calling bodies the output no longer needs and made the
/// bias tier itself a function of a count a future slice would have to move. This
/// stage closes that obligation (S1c card §2.2's reopening clause): after the map
/// freeze (the final retained set — composite, layout and seac additions included —
/// is frozen there), a token-level reachability walk (the S3 closure's shared walker,
/// OQ-S4-5, extended with reachability collection) collects
/// the bodies every retained glyph program's call graph reaches — the Global Subrs
/// INDEX as one face-wide set, every FD's Local Subrs INDEX as its own bucket keyed by
/// the glyph's FDSelect binding (OQ-S4-4 lean) — and parks a prune plan (survivor
/// lists in original relative order, the OQ-S1c-6 shape, plus renumbering maps and
/// the old/new bias pair per pruned INDEX) for the payload rebuilds.
/// The bias semantics are the TN5177 §4.7 corrected form this slice first puts into
/// production: bias tiers {107, 1131, 32768} over the CALLED INDEX's count, and
/// index = operand + bias (the encoded operand = index − bias) — the same INDEX
/// semantics the seac closure's descent now shares (S3 card §5's preview, realized by
/// the shared walker; the S1c/S3 card draft's "<1240 → bias 0" form was an erratum,
/// S4 card §8 finding 1). Survivors renumber and every callsubr/callgsubr site inside
/// a retained program or surviving body re-encodes its operand as new index − new
/// bias in the minimal push form (§3.3); an INDEX pruned to zero survivors loses its
/// Private DICT's Subrs key entirely (the compiler's empty emission and fixpoint
/// absorb the layout). A plan is null exactly when nothing was pruned anywhere, and
/// null plans take the S1c verbatim path byte for byte — the §3.5 identity anchor.
/// Token-undecidable shapes refuse (OQ-S4-3 lean, card §4): an unknown call operand
/// (A1), an operand whose biased index falls outside the called INDEX (A2) and parse
/// derailment — a truncated token, a mask skip past the program, a body that neither
/// returns nor ends, nesting deeper than TN5177 appendix B's ten frames, and the
/// multi-FD global body whose local calls no single operand re-encoding can serve
/// (A3) — all with UnsupportedOperation(17), finite, before any byte exists. This is
/// reference enumeration and operand re-encoding, not a Type 2 interpreter: no
/// coordinates, no outlines, no desubroutinizing, no inlining (card §9).</summary>
public static partial class SubsetOperation
{
    /// <summary>TN5177 §4.7: the call bias as a function of the CALLED INDEX's count,
    /// added to the encoded operand to find the entry (index = operand + bias). The
    /// low tier is 107, the spec value the S1c gate's census mislabeled as 0 and this
    /// slice first consumes in production (S4 card §0.1/§8 finding 1).</summary>
    internal static int SubrBias(int count) =>
        count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    // ---- the prune plan (§2.3/§3.1-§3.2) ----------------------------------------

    /// <summary>One pruned INDEX's renumbering: the survivors in their original
    /// relative order (OQ-S1c-6's surviving-reference shape), the old→new index map
    /// (−1 = pruned) and the bias pair (old over the pre-prune count, new over the
    /// survivor count). A null plan slot means the INDEX kept every body.</summary>
    internal sealed class SubrIndexPlan
    {
        internal required int[] Survivors { get; init; }

        internal required int[] NewIndexOfOld { get; init; }

        internal required int OldBias { get; init; }

        internal required int NewBias { get; init; }

        internal int OldCount => NewIndexOfOld.Length;

        internal int NewCount => Survivors.Length;
    }

    /// <summary>The parked plan one compact CFF/CFF2 rebuild consumes: per-INDEX
    /// renumbering (null = that INDEX kept every body — its bodies still ride the
    /// rewrite walk under the identity map, because their calls into a pruned
    /// sibling INDEX renumber) and, per reachable global body, the FD context its
    /// single scan ran under (the rewrite walk's locals table; multi-context
    /// global bodies with local calls refused at plan time, §4 A3). Identity —
    /// nothing pruned anywhere — never parks a plan, so the payload's verbatim
    /// path is the §3.5 byte-identity anchor.</summary>
    internal sealed class SubrPrunePlan
    {
        internal required SubrIndexPlan? Global { get; init; }

        /// <summary>One slot per FD of the face (name-keyed: the single entry of its
        /// one Local Subrs INDEX, FD 0 by §2.2's same-method rule).</summary>
        internal required SubrIndexPlan?[] LocalByFd { get; init; }

        internal required IReadOnlyDictionary<int, int> GlobalContextFd { get; init; }

        /// <summary>Each reached body's single call-site entry selector — the
        /// rewrite frame's blend seed (TN5171 §5: a body's runtime selector is the
        /// one its callers armed at the call site, not the FD default). Global
        /// bodies by index, local bodies nested by FD then index. A multi-valued
        /// entry set refuses for a selector-sensitive body and resolves to one
        /// recorded entry otherwise, whose reading the body's parse never
        /// consults.</summary>
        internal required IReadOnlyDictionary<int, int> GlobalEntryVsindex { get; init; }

        internal required IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> LocalEntryVsindex { get; init; }

        internal int PrunedGlobalCount => Global is { } global ? global.OldCount - global.NewCount : 0;

        internal int PrunedLocalCount(int fd) =>
            LocalByFd[fd] is { } local ? local.OldCount - local.NewCount : 0;
    }

    /// <summary>The reachability analysis one compact CFF face accumulates over the
    /// frozen map's output order: the face-wide global body set, every FD's local
    /// body bucket, the per-body call-site vsindex entry sets (the CFF2 rewrite
    /// frames' selector seeds; on the CFF1 forms the walks carry the constant 0,
    /// so the sets stay singletons and the map is dead weight), and the facts the
    /// multi-FD divergence guard reads. No per-body scan memo rides here: the scan
    /// walks execute every reached body fresh — a shared body's stem contribution
    /// and stack effect depend on the caller's stack (hint-helper subrs consume
    /// the caller's passed arguments), so no body-keyed memo can replay them
    /// faithfully, and the rewrite keeps its frames faithful the same way, by
    /// running its callees' tokens instead of a parked summary.</summary>
    private sealed class SubrAnalysis
    {
        internal readonly HashSet<int> GlobalReach = [];
        internal readonly Dictionary<int, HashSet<int>> GlobalBodyFds = [];
        internal readonly HashSet<int> GlobalLocalCall = [];
        internal readonly Dictionary<int, HashSet<int>> LocalReach = [];
        internal readonly Dictionary<int, HashSet<int>> GlobalEntryVsindex = [];
        internal readonly Dictionary<int, Dictionary<int, HashSet<int>>> LocalEntryVsindex = [];

        /// <summary>The bodies whose blend arithmetic reads the ENTRY selector: a
        /// blend of the body's own tokens ran before the body's own vsindex
        /// operator armed one (TN5171 section 5 — the blend's k is the armed
        /// selector's region count). A body without such a blend parses
        /// identically under every entry; only these carry an ambiguity the
        /// single-rewrite guard can refuse on.</summary>
        internal readonly HashSet<(int Kind, int Fd, int Index)> SelectorSensitive = [];

        internal HashSet<int> LocalSet(int fd)
        {
            if (!LocalReach.TryGetValue(fd, out var set))
            {
                set = [];
                LocalReach[fd] = set;
            }
            return set;
        }

        private Dictionary<int, HashSet<int>> LocalEntryMap(int fd)
        {
            if (!LocalEntryVsindex.TryGetValue(fd, out var map))
            {
                map = [];
                LocalEntryVsindex[fd] = map;
            }
            return map;
        }

        internal void Mark(int kind, int fd, int index)
        {
            if (kind == SubrWalk.KindGlobal)
            {
                GlobalReach.Add(index);
                if (!GlobalBodyFds.TryGetValue(index, out var fds))
                {
                    fds = [];
                    GlobalBodyFds[index] = fds;
                }
                fds.Add(fd);
            }
            else
            {
                LocalSet(fd).Add(index);
            }
        }

        /// <summary>Records the walk's current vsindex as one call-site entry context
        /// of the called body (TN5171 §5: a blend without its own vsindex operator
        /// reads the selector the caller armed). Sets only grow; the plan-time
        /// guard refuses a multi-valued set only for a selector-sensitive body —
        /// an insensitive body's parse never consults the entry value, so any one
        /// of them seeds a faithful rewrite.</summary>
        internal void MarkEntryVsindex(int kind, int fd, int index, int vsindex)
        {
            if (kind == SubrWalk.KindGlobal)
            {
                if (!GlobalEntryVsindex.TryGetValue(index, out var set))
                {
                    set = [];
                    GlobalEntryVsindex[index] = set;
                }
                set.Add(vsindex);
            }
            else
            {
                var map = LocalEntryMap(fd);
                if (!map.TryGetValue(index, out var set))
                {
                    set = [];
                    map[index] = set;
                }
                set.Add(vsindex);
            }
        }

        /// <summary>Marks the body selector-sensitive: one of its own blends ran
        /// before the body's own vsindex operator, so its blend arithmetic reads
        /// the selector the caller armed at the entry. A global body's fact is
        /// FD-free — its tokens and the face-wide variation store fix it — so the
        /// key normalizes the scanning FD away, matching the guard's lookup.</summary>
        internal void MarkSelectorSensitive(int kind, int fd, int index) =>
            SelectorSensitive.Add((kind, kind == SubrWalk.KindGlobal ? 0 : fd, index));

        internal bool IsSelectorSensitive(int kind, int fd, int index) =>
            SelectorSensitive.Contains((kind, kind == SubrWalk.KindGlobal ? 0 : fd,
                index));

        internal SubrPrunePlan ToPlan(SubrIndexPlan? global, SubrIndexPlan?[] localByFd)
        {
            var contextFd = new Dictionary<int, int>();
            foreach ((int index, var fds) in GlobalBodyFds)
                contextFd[index] = fds.First();
            // Every set resolves to its single entry value: the CFF2 caller runs
            // RefuseAmbiguousEntryVsindex before parking, and the CFF1 walks only
            // ever record the constant 0.
            var globalEntries = new Dictionary<int, int>();
            foreach ((int index, var set) in GlobalEntryVsindex)
                globalEntries[index] = set.First();
            var localEntries = new Dictionary<int, IReadOnlyDictionary<int, int>>();
            foreach ((int fd, var map) in LocalEntryVsindex)
            {
                var resolved = new Dictionary<int, int>();
                foreach ((int index, var set) in map)
                    resolved[index] = set.First();
                localEntries[fd] = resolved;
            }
            return new SubrPrunePlan
            {
                Global = global,
                LocalByFd = localByFd,
                GlobalContextFd = contextFd,
                GlobalEntryVsindex = globalEntries,
                LocalEntryVsindex = localEntries,
            };
        }
    }

    // ---- the pipeline stage (§2.3: freeze 后、payload 前) -------------------------

    /// <summary>Computes the compact subr prune plan over the frozen map and parks it
    /// on the state (null = identity). Runs once, after step 5.5's freeze and before
    /// any payload stage; a failure refuses atomically.</summary>
    private static ReadStatus PruneSubrs(Cff1Table? nameKeyed, CidCff1Table? cidKeyed,
        GlyphRemap map, BuildBudget budget, out SubrPrunePlan? plan,
        out FontDiagnostic diagnostic)
    {
        return cidKeyed is not null
            ? PruneSubrsCid(cidKeyed, map, budget, out plan, out diagnostic)
            : PruneSubrsNameKeyed(nameKeyed!, map, budget, out plan, out diagnostic);
    }

    /// <summary>The CID-keyed analysis (card §2.2, OQ-S4-4 lean): the global set is
    /// face-wide while every FD's local set buckets separately — a glyph's callsubr
    /// resolves only against its FDSelect-bound FD's INDEX and that FD's count's
    /// bias — and each retained glyph's program walks under its own FD context. An
    /// FD no retained glyph references has an empty bucket, so its INDEX prunes to
    /// zero survivors and its Private DICT loses the Subrs key (FDArray never
    /// shrinks). A global body carrying local calls that more than one FD context
    /// scans refuses (A3): its call operand would need a per-context re-encoding no
    /// single byte can serve.</summary>
    private static ReadStatus PruneSubrsCid(CidCff1Table model, GlyphRemap map,
        BuildBudget budget, out SubrPrunePlan? plan, out FontDiagnostic diagnostic)
    {
        plan = null;
        byte[][] globals = model.GlobalSubrs.ToArray();
        var fdLocals = new byte[model.FdCount][][];
        for (int fd = 0; fd < model.FdCount; fd++)
            fdLocals[fd] = model[fd].LocalSubrs.ToArray();
        var analysis = new SubrAnalysis();
        var inputOrder = map.OutputToInput;
        for (int o = 0; o < inputOrder.Length; o++)
        {
            int gid = inputOrder[o];
            ReadStatus status = model.TryGetFdIndex(gid, out int fd, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var walk = new SubrWalk(budget, model.Location.At(DataField.GlyphId, 0, gid),
                fdLocals[fd], globals, cff2: false, seac: false, analysis, fd,
                rewrite: null);
            status = walk.RunReach(model.CharStrings[gid], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        ReadStatus guard = RefuseDivergentGlobalLocals(analysis, model.Location,
            out diagnostic);
        if (guard != ReadStatus.Done) return guard;
        var globalPlan = BuildIndexPlan(analysis.GlobalReach, globals.Length);
        var localPlans = new SubrIndexPlan?[model.FdCount];
        bool any = globalPlan is not null;
        for (int fd = 0; fd < model.FdCount; fd++)
        {
            localPlans[fd] = BuildIndexPlan(analysis.LocalSet(fd), fdLocals[fd].Length);
            any |= localPlans[fd] is not null;
        }
        if (!any)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        plan = analysis.ToPlan(globalPlan, localPlans);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The CFF2 analysis (OQ-S4-1 lean, card §2.1's CFF2 addendum): the
    /// same per-FD bucketing over the FontDICT binding, with the vsindex/blend
    /// stack effects the walker applies in its CFF2 mode — blend carries its
    /// result count as a stack operand and keeps its defaults (TN5171 §5), so the
    /// stems declared over blend results still total correctly and the mask skip
    /// widths stay in sync — and no endchar (TN5171 charstrings end by fall-off).
    /// TopDICT carries no charset/encoding, so the stable identity and this stage
    /// never interact.</summary>
    private static ReadStatus PruneSubrsCff2(Tables.Cff2Table model, GlyphRemap map,
        BuildBudget budget, out SubrPrunePlan? plan, out FontDiagnostic diagnostic)
    {
        plan = null;
        byte[][] globals = model.GlobalSubrsInternal;
        int fdCount = model.FontDictCount;
        var fdLocals = new byte[fdCount][][];
        for (int fd = 0; fd < fdCount; fd++)
            fdLocals[fd] = model.FontDictsInternal[fd].Private?.LocalSubrs ?? [];
        var analysis = new SubrAnalysis();
        var inputOrder = map.OutputToInput;
        for (int o = 0; o < inputOrder.Length; o++)
        {
            int gid = inputOrder[o];
            int fd = model.FdIndexInternal[gid];
            if ((uint)fd >= (uint)fdCount)
            {
                // The read model guarantees the FD binding's domain; a violation is
                // a pipeline bug, refused at the model's own location (A2's face
                // form — the FDSelect drift the strict validator pins).
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    model.Location.At(DataField.GlyphId, 0, gid), out diagnostic,
                    (uint)fd, (uint)fdCount);
            }
            // The walk's blend arithmetic inherits the FD's Private vsindex (TN5171
            // §5: the DICT key is the default selector every blend reads until an
            // explicit vsindex operator overrides it) — seeding slot 0 instead
            // mis-sized every blend of a nonzero-selector face.
            var walk = new SubrWalk(budget, model.Location.At(DataField.GlyphId, 0, gid),
                fdLocals[fd], globals, cff2: true, seac: false, analysis, fd,
                rewrite: null, RegionCountsByVsindex(model.Store),
                model.FontDictsInternal[fd].Private?.VsindexValue ?? 0);
            ReadStatus status = walk.RunReach(model.CharStringsInternal[gid],
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        ReadStatus guard = RefuseDivergentGlobalLocals(analysis, model.Location,
            out diagnostic);
        if (guard != ReadStatus.Done) return guard;
        var globalPlan = BuildIndexPlan(analysis.GlobalReach, globals.Length);
        var localPlans = new SubrIndexPlan?[fdCount];
        bool any = globalPlan is not null;
        for (int fd = 0; fd < fdCount; fd++)
        {
            localPlans[fd] = BuildIndexPlan(analysis.LocalSet(fd), fdLocals[fd].Length);
            any |= localPlans[fd] is not null;
        }
        if (!any)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus ambiguous = RefuseAmbiguousEntryVsindex(analysis, model.Location,
            out diagnostic);
        if (ambiguous != ReadStatus.Done) return ambiguous;
        plan = analysis.ToPlan(globalPlan, localPlans);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The per-context vsindex guard (§4 A3, the rewrite's scope): a body
    /// whose recorded call-site entries hold more than one distinct selector has a
    /// path-dependent runtime interpretation only where its own blends read the
    /// entry selector — a blend of the body's tokens ran before the body's own
    /// vsindex operator (TN5171 §5; elsewhere the k the mask widths read would
    /// change per entry). Such a sensitive body refuses: no single rewritten
    /// product is semantically faithful to both entries. An insensitive body's
    /// parse never consults the entry, so its multi-valued set is innocent and
    /// any one value seeds a byte-faithful rewrite frame. It runs only where a
    /// plan will park: the identity face's verbatim path keeps such a face
    /// byte-faithful, the ambiguity is the rewrite's to refuse, not the copy's.
    /// Detail names the body (a global index, or the local index within its FD —
    /// the plan's single-number convention), Expected the distinct entry count
    /// the single rewrite cannot serve.</summary>
    private static ReadStatus RefuseAmbiguousEntryVsindex(SubrAnalysis analysis,
        DataLocation modelAt, out FontDiagnostic diagnostic)
    {
        foreach ((int index, var set) in analysis.GlobalEntryVsindex)
        {
            if (set.Count <= 1 || !analysis.IsSelectorSensitive(SubrWalk.KindGlobal,
                    0, index)) continue;
            diagnostic = new(DiagnosticCode.UnsupportedOperation,
                modelAt, (uint)index, (uint)set.Count);
            return ReadStatus.UnsupportedFormat;
        }
        foreach ((int fd, var map) in analysis.LocalEntryVsindex)
        {
            foreach ((int index, var set) in map)
            {
                if (set.Count <= 1 || !analysis.IsSelectorSensitive(SubrWalk.KindLocal,
                        fd, index)) continue;
                diagnostic = new(DiagnosticCode.UnsupportedOperation,
                    modelAt, (uint)index, (uint)set.Count);
                return ReadStatus.UnsupportedFormat;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The multi-FD divergence guard (§4 A3): a global body whose program
    /// calls locals and which more than one FD context reaches would need one call
    /// operand to renumber identically for every calling context — a fact no
    /// single-context scan proves, so the shape refuses. Detail names the body,
    /// Expected the single context the rewrite could serve.</summary>
    private static ReadStatus RefuseDivergentGlobalLocals(SubrAnalysis analysis,
        DataLocation modelAt, out FontDiagnostic diagnostic)
    {
        foreach (int body in analysis.GlobalReach)
        {
            if (analysis.GlobalLocalCall.Contains(body) &&
                analysis.GlobalBodyFds[body].Count > 1)
            {
                diagnostic = new(DiagnosticCode.UnsupportedOperation,
                    modelAt, (uint)body, 1UL);
                return ReadStatus.UnsupportedFormat;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Per-vsindex region counts over the parked store (TN5171 §5): slot v
    /// is the RegionIndexCount of the ItemVariationData selector v names, and a
    /// NULL data slot — the spec's "no variation data" outer — reads as zero
    /// regions (blend keeps its defaults alone). A face without a store yields the
    /// empty table, so any blend's selector is out of domain and refuses. The CFF2
    /// walk's blend arithmetic reads this table; without it the stem totals behind
    /// every mask width would silently desync.</summary>
    private static int[] RegionCountsByVsindex(Tables.Variation.VariationStoreTable? store)
    {
        if (store is null) return [];
        var counts = new int[store.DataCount];
        for (int v = 0; v < store.DataCount; v++)
            counts[v] = store.Data[v]?.RegionIndexCount ?? 0;
        return counts;
    }

    /// <summary>The name-keyed analysis (§2.2's FD-0 form): one local INDEX, one
    /// global INDEX, one bias each; the walk domain is the frozen map's output
    /// order, so every seac/layout addition's program is analyzed too.</summary>
    private static ReadStatus PruneSubrsNameKeyed(Cff1Table model, GlyphRemap map,
        BuildBudget budget, out SubrPrunePlan? plan, out FontDiagnostic diagnostic)
    {
        plan = null;
        byte[][] globals = model.GlobalSubrs.ToArray();
        byte[][] locals = model.LocalSubrs.ToArray();
        var analysis = new SubrAnalysis();
        var inputOrder = map.OutputToInput;
        for (int o = 0; o < inputOrder.Length; o++)
        {
            int gid = inputOrder[o];
            var walk = new SubrWalk(budget, model.Location.At(DataField.GlyphId, 0, gid),
                locals, globals, cff2: false, seac: false, analysis, fd: 0, rewrite: null);
            ReadStatus status = walk.RunReach(model.CharStrings[gid], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        var globalPlan = BuildIndexPlan(analysis.GlobalReach, globals.Length);
        var localPlan = BuildIndexPlan(analysis.LocalSet(0), locals.Length);
        if (globalPlan is null && localPlan is null)
        {
            // Identity: nothing pruned anywhere — the payload must ride the S1c
            // verbatim path byte for byte (§3.5's regression anchor).
            diagnostic = default;
            return ReadStatus.Done;
        }
        plan = analysis.ToPlan(globalPlan, [localPlan]);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Survivor columns from one reach set: ascending original indices (the
    /// original relative order), the old→new map, and the bias pair. A full set —
    /// or an empty INDEX — plans nothing (null).</summary>
    private static SubrIndexPlan? BuildIndexPlan(HashSet<int> reach, int count)
    {
        if (reach.Count == count) return null;
        var survivors = new int[reach.Count];
        int at = 0;
        for (int index = 0; index < count; index++)
            if (reach.Contains(index))
                survivors[at++] = index;
        var newIndexOfOld = new int[count];
        Array.Fill(newIndexOfOld, -1);
        for (int s = 0; s < survivors.Length; s++)
            newIndexOfOld[survivors[s]] = s;
        return new SubrIndexPlan
        {
            Survivors = survivors,
            NewIndexOfOld = newIndexOfOld,
            OldBias = SubrBias(count),
            NewBias = SubrBias(survivors.Length),
        };
    }

    /// <summary>The no-prune slot's rewrite form: every body survives under the
    /// identity map and the unchanged count's own bias pair, so a same-INDEX call
    /// operand re-derives to its original value and keeps its bytes (the value
    /// rule) while the cross-INDEX sites a sibling plan pruned re-encode — §3.3's
    /// rewrite obligation runs over every surviving body, not only the slots with
    /// prunings of their own. One array serves both columns: the identity map is
    /// its own survivor list.</summary>
    private static SubrIndexPlan IdentityIndexPlan(int count)
    {
        var identity = new int[count];
        for (int i = 0; i < count; i++) identity[i] = i;
        return new SubrIndexPlan
        {
            Survivors = identity,
            NewIndexOfOld = identity,
            OldBias = SubrBias(count),
            NewBias = SubrBias(count),
        };
    }

    // ---- the payload rewriter (§3.3) ---------------------------------------------

    /// <summary>Rewrites the retained programs — one FD context each — under a
    /// parked plan: a token-level walk that copies bytes verbatim except the
    /// callsubr/callgsubr sites, whose operand re-encodes as new index − new bias in
    /// the minimal push form; a site whose value the renumbering leaves unchanged
    /// keeps its original operand bytes (the judgment is by value, not width).
    /// Each call's body also executes its tokens on the shared stack at the site,
    /// so the hint count and stack depth the caller's mask widths and later
    /// operands read stay the runtime's own (a body-keyed summary cannot: a
    /// hint-helper subr's stems are the caller's passed arguments). The CFF2 form's
    /// <paramref name="vsindexByFd"/> column seeds each program's blend selector
    /// from its FD's Private DICT (TN5171 §5; null on the CFF1 forms, where no
    /// blend exists). Sites whose operand does not sit in
    /// the immediately preceding push token refuse (A3): their value's origin is a
    /// stack shape no single-byte rewrite can serve.</summary>
    private static ReadStatus RewritePrograms(byte[][] programs, int[] programFds,
        byte[][] globals, byte[][][] fdLocals, SubrPrunePlan plan, bool cff2,
        int[]? regionCounts, int[]? vsindexByFd, DataLocation modelAt, BuildBudget budget,
        out byte[][] rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = new byte[programs.Length][];
        for (int i = 0; i < programs.Length; i++)
        {
            int fd = programFds[i];
            var rewrite = new RewriteState(plan.LocalByFd[fd], plan.Global);
            var walk = new SubrWalk(budget, modelAt.At(DataField.GlyphId, 0, i),
                fdLocals[fd], globals, cff2, seac: false, analysis: null, fd,
                rewrite: rewrite, regionCounts, vsindexByFd?[fd] ?? 0);
            ReadStatus status = walk.RunRewrite(programs[i], SubrWalk.KindGlyph, i,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            rewritten[i] = [.. rewrite.Output];
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Rewrites one pruned INDEX's surviving bodies in survivor order, each
    /// under its FD context (global bodies: their single scanning context from the
    /// plan; local bodies: their own FD). The CFF2
    /// form's <paramref name="entryVsindexByOld"/> column seeds each body's blend
    /// selector from the plan's per-body entry column — the context its callers armed
    /// at the call site, not the FD default (null on the CFF1 forms, where the
    /// FD-default channel stands and no blend exists).</summary>
    private static ReadStatus RewriteSurvivorBodies(byte[][] bodies, int[] survivorOld,
        int frameKind, int[] bodyFds, byte[][] globals, byte[][][] fdLocals,
        SubrPrunePlan plan, bool cff2, int[]? regionCounts, int[]? vsindexByFd,
        IReadOnlyDictionary<int, int>? entryVsindexByOld, DataLocation modelAt,
        BuildBudget budget, out byte[][] rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = new byte[survivorOld.Length][];
        for (int s = 0; s < survivorOld.Length; s++)
        {
            int old = survivorOld[s];
            int fd = bodyFds[s];
            int initialVsindex;
            if (entryVsindexByOld is null)
                initialVsindex = vsindexByFd?[fd] ?? 0;
            else if (entryVsindexByOld.TryGetValue(old, out int entry))
                initialVsindex = entry;
            else
            {
                // A survivor is a reached body and every reached body recorded
                // its call-site entry selector; a miss is a pipeline bug the
                // refusal surfaces, never a silent FD-default seed.
                diagnostic = new(DiagnosticCode.UnsupportedOperation,
                    modelAt.At(DataField.GlyphId, 0, old), (uint)old, 0UL);
                return ReadStatus.UnsupportedFormat;
            }
            var rewrite = new RewriteState(plan.LocalByFd[fd], plan.Global);
            var walk = new SubrWalk(budget, modelAt.At(DataField.GlyphId, 0, old),
                fdLocals[fd], globals, cff2, seac: false, analysis: null, fd,
                rewrite: rewrite, regionCounts, initialVsindex);
            ReadStatus status = walk.RunRewrite(bodies[old], frameKind, old,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            rewritten[s] = [.. rewrite.Output];
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The name-keyed form (FD 0 everywhere): the retained programs, the
    /// surviving global bodies and the surviving local bodies of the one INDEX.
    /// Also decides the Private DICT's Subrs key: an INDEX pruned to zero survivors
    /// loses the key (card §1.1's key-delete face) — a stale operand would point
    /// into the body it no longer owns.</summary>
    private static ReadStatus RewriteSubrProgramsNameKeyed(Cff1Table model, GlyphRemap map,
        SubrPrunePlan plan, BuildBudget budget, out byte[][] charStrings,
        out byte[][] globalSubrs, out byte[][] localSubrs, out bool hasLocalSubrs,
        out FontDiagnostic diagnostic)
    {
        var localPlan = plan.LocalByFd[0];
        var globalPlan = plan.Global;
        byte[][] globals = model.GlobalSubrs.ToArray();
        byte[][] locals = model.LocalSubrs.ToArray();
        byte[][][] fdLocals = [locals];
        var inputOrder = map.OutputToInput;
        charStrings = [];
        globalSubrs = [];
        localSubrs = [];
        hasLocalSubrs = false;
        var selected = new byte[inputOrder.Length][];
        for (int o = 0; o < inputOrder.Length; o++)
            selected[o] = model.CharStrings[inputOrder[o]];
        ReadStatus status = RewritePrograms(selected,
            new int[inputOrder.Length], globals, fdLocals, plan, cff2: false,
            regionCounts: null, vsindexByFd: null, model.Location, budget,
            out charStrings, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Every surviving body rides the rewrite walk once the plan is parked
        // (§3.3): a null slot's INDEX kept every body, but those bodies' calls
        // into a pruned sibling INDEX still renumber — the identity slot keeps
        // its own count, so its same-INDEX operands re-derive to their original
        // values and hold their bytes. The zero-filled context column is this
        // face's one FD (§2.2's FD-0 form); the multi-FD rebuilds fill each
        // body's owner instead.
        SubrIndexPlan effectiveGlobal = globalPlan ?? IdentityIndexPlan(globals.Length);
        status = RewriteSurvivorBodies(globals, effectiveGlobal.Survivors,
            SubrWalk.KindGlobal, new int[effectiveGlobal.NewCount], globals,
            fdLocals, plan, cff2: false,
            regionCounts: null, vsindexByFd: null, entryVsindexByOld: null,
            model.Location, budget, out globalSubrs, out diagnostic);
        if (status != ReadStatus.Done) return status;
        SubrIndexPlan effectiveLocal = localPlan ?? IdentityIndexPlan(locals.Length);
        status = RewriteSurvivorBodies(locals, effectiveLocal.Survivors,
            SubrWalk.KindLocal, new int[effectiveLocal.NewCount], globals,
            fdLocals, plan, cff2: false,
            regionCounts: null, vsindexByFd: null, entryVsindexByOld: null,
            model.Location, budget, out localSubrs, out diagnostic);
        if (status != ReadStatus.Done) return status;
        hasLocalSubrs = localPlan is null ? model.HasLocalSubrs
            : effectiveLocal.NewCount > 0;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The Private DICT entries of an INDEX pruned to zero survivors: the
    /// Subrs key goes with the INDEX (card §1.1's key-delete face) — a stale operand
    /// would point into the body it no longer owns.</summary>
    private static Cff1DictEntry[] PrivateEntriesWithoutSubrs(
        ReadOnlySpan<Cff1DictEntry> entries)
    {
        int subrs = -1;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].Operator == Cff1Layout.OpSubrs)
            {
                subrs = i;
                break;
            }
        if (subrs < 0) return entries.ToArray();
        var kept = new List<Cff1DictEntry>(entries.Length - 1);
        for (int i = 0; i < entries.Length; i++)
            if (i != subrs)
                kept.Add(entries[i]);
        return [.. kept];
    }

    // ---- the shared token walker (OQ-S4-5) ---------------------------------------

    /// <summary>Rewrite-walk per-program state: the renumbering maps and new biases
    /// the call sites read, and the program's output bytes. A null plan slot keeps
    /// its INDEX's own bias — the walk falls back to the called table's current
    /// bias, since an unpruned INDEX renumbers to itself (the nullable biases).</summary>
    private sealed class RewriteState
    {
        internal RewriteState(SubrIndexPlan? localPlan, SubrIndexPlan? globalPlan)
        {
            LocalMap = localPlan?.NewIndexOfOld;
            GlobalMap = globalPlan?.NewIndexOfOld;
            NewLocalBias = localPlan?.NewBias;
            NewGlobalBias = globalPlan?.NewBias;
        }

        internal int[]? LocalMap { get; }

        internal int[]? GlobalMap { get; }

        /// <summary>The new bias of the pruned INDEX; null when that INDEX kept every
        /// body (the walk then reads the unchanged bias off the called table, so an
        /// unpruned INDEX's sites keep their operand values — the value rule).</summary>
        internal int? NewLocalBias { get; }

        internal int? NewGlobalBias { get; }

        internal readonly List<byte> Output = [];

        /// <summary>Sites whose operand value the renumbering changed — the budget's
        /// recode-site units (card §2.1).</summary>
        internal int RecodedSites { get; private set; }

        internal void CountRecodedSite() => RecodedSites++;
    }

    /// <summary>The shared token-level walker over Type 2 charstrings (TN5177): one
    /// per-operator stack-effect table over two-state values (known literal / the
    /// unknown), the value stack and the hint count persisting across subr call
    /// frames, the TN5177 §4.7 corrected bias resolving a known operand's body
    /// (index = operand + bias over the CALLED INDEX's count) and a call-path cycle
    /// guard that terminates loop branches. Reference enumeration, not an
    /// interpreter: no coordinates, no outline, no byte ever rewritten by the scan
    /// modes (S3 card OQ-S3-2's domain, now shared). Three policies hang off the
    /// same token core:
    ///
    /// seac (the S3 closure, card §2): endchar's 4-5 operand form resolves through
    /// the Standard encoding; an unknown call operand descends every body of the
    /// called INDEX with an all-unknown stack; a wild operand leaves nothing to
    /// scan; parse derailment stays as lenient as the merged S3 scanner's.
    ///
    /// reach (the S4 analysis, card §2.1): reachable bodies collect into the
    /// analysis; an unknown or wild call operand refuses (A1/A2); a truncated
    /// token, a mask skip past the program, a body that neither returns nor ends
    /// and nesting deeper than TN5177 appendix B's ten frames refuse (A3); every
    /// reached body executes fresh — a body's stems and stack effect ride the
    /// caller's arguments, so no per-body summary replays them. The CFF2 mode tracks
    /// the blend result count and the vsindex region counts (TN5171 §5), so the
    /// stems declared over blend results still total and the mask widths stay in
    /// sync — a blend whose count, selector or depth the walk cannot serve
    /// refuses (A3) instead of desyncing.
    ///
    /// rewrite (the S4 payload, card §3.3): every call site re-encodes its operand
    /// from the parked maps and executes the called body's tokens for the shared
    /// stack and hint count, and bytes move verbatim except the re-encoded
    /// operands.</summary>
    private sealed class SubrWalk
    {
        internal const int KindGlyph = 0, KindLocal = 1, KindGlobal = 2;

        /// <summary>TN5177 appendix B: subr nesting is bounded at ten frames; deeper
        /// call stacks refuse rather than model an unbounded runtime (card §4 A3).</summary>
        private const int MaxSubrNesting = 10;

        /// <summary>One value stack slot: a known literal or the unknown value.
        /// Unknown is the default; a known zero is a real literal and must stay
        /// distinct from it.</summary>
        private readonly record struct Slot(bool Known, int Value)
        {
            public static Slot Of(int value) => new(true, value);
        }

        private readonly byte[][] _locals;
        private readonly byte[][] _globals;
        private readonly bool _cff2;
        private readonly bool _seac;
        private readonly BuildBudget _budget;
        private readonly DataLocation _at;
        private readonly SubrAnalysis? _analysis;
        private readonly int _fd;
        private readonly RewriteState? _rewrite;

        /// <summary>Per-vsindex region counts (TN5171 §5, CFF2 mode only): the
        /// RegionIndexCount of the ItemVariationData each selector names, a NULL
        /// data slot read as zero regions. Null outside CFF2 mode.</summary>
        private readonly int[]? _regionCounts;

        /// <summary>The current vsindex selector (TN5171 §5): the walk seeds it
        /// from the entry program's Private DICT — the spec's default while no
        /// explicit vsindex operator has run — or −1 once poisoned by an unknown
        /// or out-of-domain value (the next blend refuses).</summary>
        private int _vsindex;

        private readonly List<Slot> _stack = [];
        private int _hints;
        private readonly List<(int Kind, int Index)> _path = [];

        /// <summary>The seac form's codes once <see cref="Found"/> turns true (the
        /// shared core reports through fields — the S3 shape's out parameters do not
        /// survive the shared recursion).</summary>
        internal bool Found { get; private set; }

        internal int BcharCode { get; private set; }

        internal int AcharCode { get; private set; }

        /// <summary>The rewrite walk's pending call operand (new index − new bias);
        /// the called body's stems and stack effect ride the effect descent, not a
        /// parked summary.</summary>
        private int _pendingOperand;

        /// <summary>An endchar anywhere in the call graph ends the whole program
        /// (TN5177 §4.2 Note 6): every walker frame stops, so a charstring that
        /// terminates at a call does not read as a fall-off.</summary>
        private bool _programEnded;

        internal SubrWalk(BuildBudget budget, DataLocation at, byte[][] locals,
            byte[][] globals, bool cff2, bool seac, SubrAnalysis? analysis, int fd,
            RewriteState? rewrite, int[]? regionCounts = null, int initialVsindex = 0)
        {
            _budget = budget;
            _at = at;
            _locals = locals;
            _globals = globals;
            _cff2 = cff2;
            _seac = seac;
            _analysis = analysis;
            _fd = fd;
            _rewrite = rewrite;
            _regionCounts = regionCounts;
            _vsindex = initialVsindex;
        }

        // ---- entries ---------------------------------------------------------

        /// <summary>The S3 closure's entry: walks one glyph program for the endchar
        /// seac form; <see cref="Found"/> and the two codes report the detection.</summary>
        internal ReadStatus RunSeac(byte[] program, out FontDiagnostic diagnostic)
        {
            Found = false;
            BcharCode = AcharCode = 0;
            _programEnded = false;
            return ScanBody(KindGlyph, 0, program, out diagnostic);
        }

        /// <summary>The S4 analysis entry: collects the program's reachable bodies
        /// into the walk's analysis and vetoes every undecidable shape.</summary>
        internal ReadStatus RunReach(byte[] program, out FontDiagnostic diagnostic)
        {
            Found = false;
            _programEnded = false;
            return ScanBody(KindGlyph, 0, program, out diagnostic);
        }

        /// <summary>The S4 payload entry: copies the program with re-encoded call
        /// operands into the walk's rewrite state. The frame's key rides the call
        /// path for the walk's duration — an effect descent that re-enters the body
        /// being rewritten must stop at the same cycle branch the reach's scan
        /// did, not recurse to the nesting refusal.</summary>
        internal ReadStatus RunRewrite(byte[] program, int frameKind, int frameIndex,
            out FontDiagnostic diagnostic)
        {
            _path.Add((frameKind, frameIndex));
            ReadStatus status = RewriteBody(program, out diagnostic);
            _path.RemoveAt(_path.Count - 1);
            return status;
        }

        // ---- the shared scan loop (seac + reach) ------------------------------

        private ReadStatus ScanBody(int kind, int index, byte[] program,
            out FontDiagnostic diagnostic)
        {
            diagnostic = default;
            var key = (kind, index);
            if (_path.Contains(key)) return ReadStatus.Done; // cycle branch: stop here
            if (_analysis is not null && kind != KindGlyph)
                _analysis.Mark(kind, _fd, index);
            if (!_budget.Work(1, _at, out diagnostic)) return ReadStatus.ResourceLimit;
            _path.Add(key);
            if (!_seac && kind != KindGlyph && _path.Count - 1 > MaxSubrNesting)
            {
                // A3: TN5177 appendix B bounds subr nesting at ten frames; a deeper
                // call stack refuses instead of modeling the unbounded runtime.
                _path.RemoveAt(_path.Count - 1);
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                    (uint)(_path.Count + 1), (uint)MaxSubrNesting);
            }
            int hintsBefore = _hints;
            bool ended = false;

            // The body's own vsindex operator (TN5171 §5): once its token ran in
            // this frame, later blends read the body's armed selector and no
            // longer depend on the entry's. A blend met before that token is
            // what makes the body selector-sensitive.
            bool ownVsindexArmed = false;
            int i = 0, n = program.Length;
            while (i < n)
            {
                int start = i;
                int b = program[i++];
                if (b == 28 || b >= 32)
                {
                    int width = ReadPush(b, program, start, n, out Slot value);
                    if (width < 0)
                    {
                        if (_seac) continue; // the merged S3 scanner's single step
                        return FailDerail(start, n, out diagnostic);
                    }
                    _stack.Add(value);
                    i = start + width;
                    continue;
                }
                if (b is 10 or 29)
                {
                    Slot top = _stack.Count > 0 ? _stack[^1] : default;
                    if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
                    ReadStatus status = Call(b == 10, top, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (Found || _programEnded)
                    {
                        // The seac form was read, or a called body's endchar ended
                        // the program: every caller frame stops with it.
                        ended = true;
                        break;
                    }
                    continue;
                }
                if (b == 12)
                {
                    if (i >= n)
                    {
                        if (_seac) continue; // single step over the bare prefix
                        return FailDerail(start, n, out diagnostic);
                    }
                    if (_cff2) _stack.Clear(); // the CFF2 escape: no fixed arity
                    else ApplyEscape(program[i]);
                    i++;
                    continue;
                }
                if (b is 1 or 3 or 18 or 23)
                {
                    // Stem declarations: the width form leaves one extra value, so
                    // pairs advance the hint count that sizes every later mask.
                    _hints += _stack.Count / 2;
                    _stack.Clear();
                    continue;
                }
                if (b is 19 or 20)
                {
                    // (cntr)mask: the pairs still on the stack are the implicit
                    // vstems the hstemhm form may leave undeclared (TN5177 §4.3;
                    // the Microsoft CFF2 spec's hintmask section — the mask covers
                    // h and v stems together), the floor absorbing the odd
                    // optional width. Then the mask's byte count skips.
                    _hints += _stack.Count / 2;
                    _stack.Clear();
                    int skip = (_hints + 7) >> 3;
                    if (i + skip > n)
                    {
                        if (_seac)
                        {
                            i += skip; // the merged scanner's overshoot exits the loop
                            break;
                        }
                        return FailDerail(i + skip, n, out diagnostic);
                    }
                    i += skip;
                    continue;
                }
                if (!_cff2 && b == 11)
                {
                    ended = true; // return: leave this body, the caller frame continues
                    break;
                }
                if (!_cff2 && b == 14)
                {
                    // endchar: 4-5 operands = the deprecated seac form (TN5176 Type 2
                    // appendix), only read in the seac policy; the operator ends the
                    // program in both policies (TN5177 §4.2 Note 6: a called body
                    // may end it — every frame stops).
                    if (_seac && _stack.Count >= 4)
                    {
                        Slot bchar = _stack[^2], achar = _stack[^1];
                        if (bchar.Known && achar.Known)
                        {
                            Found = true;
                            BcharCode = bchar.Value;
                            AcharCode = achar.Value;
                        }
                        else
                        {
                            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                                DiagnosticCode.UnsupportedOperation, _at,
                                out diagnostic, (uint)_stack.Count, 4UL);
                        }
                    }
                    _programEnded = true;
                    ended = true;
                    break;
                }
                if (_cff2 && b == 15)
                {
                    ownVsindexArmed = true;
                    ReadStatus status = ApplyVsindex(out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    continue;
                }
                if (_cff2 && b == 16)
                {
                    if (_analysis is not null && kind != KindGlyph && !ownVsindexArmed)
                    {
                        // The blend's region count k is the entry selector's until
                        // this body arms its own (TN5171 §5): the ambiguity the
                        // plan-time guard refuses is exactly this shape's.
                        _analysis.MarkSelectorSensitive(kind, _fd, index);
                    }
                    ReadStatus status = ApplyBlend(out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    continue;
                }
                if (b is 4 or 5 or 6 or 7 or 8 or 21 or 22 or 24 or 25 or 26 or 27
                    or 30 or 31 or 34 or 35 or 36 or 37)
                {
                    // Path construction: every value on the stack is this operator's
                    // operand (plus the optional width), well-formed programs included.
                    // hvcurveto (31, TN5177 appendix A) rides this list with vhcurveto;
                    // missing it let the operands persist into a later endchar, whose
                    // 4-operand shape then read them as a seac form that never existed.
                    _stack.Clear();
                    continue;
                }
                // Reserved or not modeled: no stack effect is assumed, the
                // conservative direction (values persist rather than vanish).
            }
            _path.RemoveAt(_path.Count - 1);
            if (!_seac && !_cff2 && !ended && n > 0)
            {
                // TN5177 §4.2 Note 6: a CFF1 program ends at return or at an
                // endchar (its own or a called body's). Falling off the end is
                // derailment the scan refuses; CFF2 has no terminator and ends by
                // fall-off (TN5171).
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                    (uint)n, 0UL);
            }
            return ReadStatus.Done;
        }

        /// <summary>callsubr/callgsubr. Scan policies resolve the popped operand
        /// through the CALLED INDEX's bias and descend; the rewrite policy computes
        /// the re-encoded operand for the loop's emission and descends as well, so
        /// the shared stack and hint count stay the runtime's own.</summary>
        private ReadStatus Call(bool local, Slot top, out FontDiagnostic diagnostic)
        {
            diagnostic = default;
            byte[][] table = local ? _locals : _globals;
            int kind = local ? KindLocal : KindGlobal;
            if (_rewrite is not null)
            {
                // Defensive only: the reach pass vetted every site of every program
                // the rewriter sees, so unknown/wild/pruned never fire here.
                if (!top.Known)
                    return FailUnknownOperand(out diagnostic);
                int old = top.Value + SubrBias(table.Length);
                if (old < 0 || old >= table.Length)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                        DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                        unchecked((ulong)(long)top.Value), (ulong)table.Length);
                int[]? map = local ? _rewrite.LocalMap : _rewrite.GlobalMap;
                int newIdx = map is null ? old : map[old];
                if (newIdx < 0)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                        DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                        unchecked((ulong)(long)top.Value), (ulong)table.Length);
                // A null plan slot means the INDEX kept every body: the new bias is
                // the one the unchanged count still yields, so the site's operand
                // value re-derives to its original and the bytes stay (the value
                // rule, card §3.3's re-encoding judgment).
                int newBias = (local ? _rewrite.NewLocalBias : _rewrite.NewGlobalBias)
                    ?? SubrBias(table.Length);
                _pendingOperand = newIdx - newBias;
                // The called body's tokens execute here — on this shared stack, so
                // the caller's mask widths and later call operands stay
                // runtime-faithful: a body-keyed memo cannot (a hint-helper subr's
                // stem contribution is the caller's passed arguments, no summary
                // replays it). The reach pass vetted the body; the rescan's
                // verdicts are the same execution's. The descent's own sites run
                // through this same branch and would clobber the pending value —
                // the frame's site reclaims it before the loop's emission reads it.
                int siteOperand = _pendingOperand;
                ReadStatus status = ScanBody(kind, old, table[old], out diagnostic);
                _pendingOperand = siteOperand;
                return status;
            }
            if (local && _analysis is { } analysis && _path.Count > 0 &&
                _path[^1].Kind == KindGlobal)
            {
                // A local call inside a global body: the divergence guard's marker
                // (§4 A3 — the body's operand must renumber identically for every
                // calling FD context, which more than one context never proves).
                analysis.GlobalLocalCall.Add(_path[^1].Index);
            }
            if (top.Known)
            {
                int idx = top.Value + SubrBias(table.Length);
                if (idx < 0 || idx >= table.Length)
                {
                    if (!_seac)
                    {
                        // A2: the operand resolves outside the called INDEX — the
                        // program would fault at run time; the face's own defect.
                        return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                            DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                            unchecked((ulong)(long)top.Value), (ulong)table.Length);
                    }
                    return ReadStatus.Done; // the S3 scanner: no body to scan
                }
                _analysis?.Mark(kind, _fd, idx);
                // The call site's selector context (TN5171 §5): the body's blends
                // read the walk's current vsindex at this entry — recorded per
                // body so the rewrite frames seed the callers' actual context,
                // not the FD default a caller may have switched away from (≥2
                // distinct selector-sensitive entries refuse at plan time; the
                // cycle branch still sees the record, the runtime does execute it).
                _analysis?.MarkEntryVsindex(kind, _fd, idx, _vsindex);
                return ScanBody(kind, idx, table[idx], out diagnostic);
            }
            if (!_seac)
                return FailUnknownOperand(out diagnostic); // A1: not statically decidable
            // The S3 scanner's conservative tier: every body of the INDEX descends
            // with an all-unknown stack (the runtime selection is not decidable, so
            // the scan keeps every branch instead of guessing one).
            Slot[] saved = [.. _stack];
            for (int s = 0; s < table.Length; s++)
            {
                _stack.Clear();
                for (int v = 0; v < saved.Length; v++) _stack.Add(default);
                ReadStatus status = ScanBody(kind, s, table[s], out diagnostic);
                if (status != ReadStatus.Done || Found) return status;
            }
            _stack.Clear();
            foreach (var _ in saved) _stack.Add(default); // the caller resumes unknowable
            return ReadStatus.Done;
        }

        /// <summary>vsindex (TN5171 §5): the selector names the ItemVariationData
        /// whose region count every later blend reads. A known in-domain selector
        /// arms the count; an unknown or out-of-domain one poisons it — the next
        /// blend refuses, because a poisoned count would silently desync the stem
        /// totals every later mask width reads (the §2.1 mask-width lesson).</summary>
        private ReadStatus ApplyVsindex(out FontDiagnostic diagnostic)
        {
            Slot top = _stack.Count > 0 ? _stack[^1] : default;
            Pop(1);
            _vsindex = top.Known && _regionCounts is not null &&
                (uint)top.Value < (uint)_regionCounts.Length
                ? top.Value : -1;
            diagnostic = default;
            return ReadStatus.Done;
        }

        /// <summary>blend (TN5171 §5): the top operand is the result count n; the
        /// n defaults stay on the stack and the n·k deltas go, so the frame keeps
        /// depth − 1 − n·k values — the top n of them (the defaults) demoted to
        /// the unknown, since the blend math is not modeled. k is the region count
        /// of the ItemVariationData the current vsindex selects. The count operand
        /// is what keeps the walk runtime-faithful: CFF2 declares its stems over
        /// blend results, so a stack wipe here would zero the stem totals and every
        /// later mask skip width would read the wrong bytes (the desync this
        /// walker's first draft died of). An unknown count, a poisoned region
        /// count or a stack that dips below the block refuses (A3): finite, before
        /// any byte exists, per OQ-S4-3.</summary>
        private ReadStatus ApplyBlend(out FontDiagnostic diagnostic)
        {
            Slot top = _stack.Count > 0 ? _stack[^1] : default;
            if (!top.Known)
                return FailUnknownOperand(out diagnostic);
            int count = top.Value;
            int? regions = _vsindex >= 0 && _regionCounts is not null &&
                (uint)_vsindex < (uint)_regionCounts.Length
                ? _regionCounts[_vsindex] : null;
            if (count < 0 || regions is not { } k)
            {
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                    unchecked((ulong)(long)count),
                    (uint)(_vsindex < 0 || _regionCounts is null
                        ? 0 : _regionCounts.Length));
            }
            Pop(1);
            int keep = _stack.Count - count * k;
            if (keep < 0)
            {
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                    (uint)(_stack.Count + 1), (uint)(count * k + 1));
            }
            _stack.RemoveRange(keep, _stack.Count - keep);
            for (int v = keep - count > 0 ? keep - count : 0; v < keep; v++)
                _stack[v] = default;
            diagnostic = default;
            return ReadStatus.Done;
        }

        private ReadStatus FailUnknownOperand(out FontDiagnostic diagnostic) =>
            ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                (uint)_stack.Count, 1UL);

        /// <summary>A3's derail tier: a truncated token or a mask skip past the
        /// program — Detail is the byte position the parse demanded, Expected the
        /// program length.</summary>
        private ReadStatus FailDerail(int at, int length, out FontDiagnostic diagnostic) =>
            ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                (uint)at, (uint)length);

        /// <summary>The 12 X escape family over the TN5177 appendix A table: arithmetic
        /// and logic produce unknown values, random always does, put/get round-trip the
        /// transient array as unknown slots (TN5177 §4.5), the stack tools keep their
        /// fixed arities, and the flex family is path construction — it consumes every
        /// stacked value. The reserved codes take the loop's unmodeled default (no
        /// assumed effect, values persist): 12 0 is the appendix C dotsection no-op,
        /// and 12 8/12 13 — the CFF1-era store/load — lost their operands to the
        /// appendix D 2000 revision, so no arity remains to model them by.</summary>
        private void ApplyEscape(int op)
        {
            switch (op)
            {
                case 3 or 4 or 15:       // and / or / eq → one unknown result
                    Pop(2);
                    _stack.Add(default);
                    break;
                case 22:                 // ifelse pops its four operands
                    Pop(4);
                    _stack.Add(default);
                    break;
                case 9 or 14 or 26:      // abs / neg / sqrt → unknown
                    Pop(1);
                    _stack.Add(default);
                    break;
                case 10 or 11 or 12 or 24: // add / sub / div / mul → unknown
                    Pop(2);
                    _stack.Add(default);
                    break;
                case 5:                  // not → unknown
                    Pop(1);
                    _stack.Add(default);
                    break;
                case 18:                 // drop
                    Pop(1);
                    break;
                case 20:                 // put: the transient array absorbs val, i
                    Pop(2);
                    break;
                case 21:                 // get: a stored or never-stored slot reads unknown
                    Pop(1);
                    _stack.Add(default);
                    break;
                case 23:                 // random → unknown
                    _stack.Add(default);
                    break;
                case 27:                 // dup: the copy carries the original's state
                    if (_stack.Count > 0) _stack.Add(_stack[^1]);
                    break;
                case 28:                 // exch
                    if (_stack.Count >= 2)
                        (_stack[^1], _stack[^2]) = (_stack[^2], _stack[^1]);
                    break;
                case 29:                 // index
                    IndexStack();
                    break;
                case 30:                 // roll: rotate the top n by j (TN5177 §4.4)
                    RollStack();
                    break;
                case 34 or 35 or 36 or 37: // hflex / flex / hflex1 / flex1: path
                    _stack.Clear();        // construction — every stacked value consumed
                    break;
                default:                 // unmodeled: assume no effect
                    break;
            }
        }

        /// <summary>roll (12 30): pops the control pair n, j and rotates the top n
        /// values by j — TN5177 §4.4's worked form "4 2 roll" turns
        /// [65, 194, 66, 193] into [66, 193, 65, 194] (the top j of the n values move
        /// below the block's remainder). A control pair the remaining stack cannot serve
        /// (unknown, n < 0, n past the depth, the degenerate zero-count-nonzero-j)
        /// poisons the frame's values instead: a guessed roll would let the later
        /// call operands decide from a stack shape the program never had.</summary>
        private void RollStack()
        {
            Slot nControl = _stack.Count > 0 ? _stack[^2] : default;
            Slot jControl = _stack.Count > 0 ? _stack[^1] : default;
            Pop(2);
            if (nControl.Known && jControl.Known &&
                (uint)nControl.Value <= (uint)_stack.Count)
            {
                int count = nControl.Value;
                int by = count > 0 ? jControl.Value % count : jControl.Value;
                if (count > 1 && by != 0)
                {
                    int from = _stack.Count - count;
                    var rolled = new Slot[count];
                    for (int k = 0; k < count; k++)
                        rolled[k] = _stack[from + ((k - by + count) % count)];
                    for (int k = 0; k < count; k++)
                        _stack[from + k] = rolled[k];
                    return;
                }
                if (count > 0 || by == 0) return; // the defined no-op forms
            }
            for (int k = 0; k < _stack.Count; k++) _stack[k] = default;
        }

        /// <summary>index (12 29): pops the position i and pushes a copy of the
        /// element i deep, the copy carrying the source slot's state (TN5177 §4.4;
        /// a negative i copies the top). A position the remaining stack cannot answer —
        /// an unknown control value, or a depth past the block, the spec's undefined
        /// domain — poisons the frame's values instead: the runtime's copy source is a
        /// shape the scan cannot name, and a guessed copy would let the later call
        /// operands decide from a value the program never had.</summary>
        private void IndexStack()
        {
            Slot i = _stack.Count > 0 ? _stack[^1] : default;
            Pop(1);
            if (i.Known)
            {
                int depth = i.Value < 0 ? 0 : i.Value;
                if (depth < _stack.Count)
                {
                    Slot copy = _stack[_stack.Count - 1 - depth];
                    _stack.Add(copy);
                    return;
                }
            }
            for (int k = 0; k < _stack.Count; k++) _stack[k] = default;
        }

        private void Pop(int count)
        {
            for (int k = 0; k < count && _stack.Count > 0; k++)
                _stack.RemoveAt(_stack.Count - 1);
        }

        // ---- the rewrite loop (§3.3) ------------------------------------------

        /// <summary>Copies the program with re-encoded call operands. Bytes move
        /// verbatim except the call sites' operand tokens; a return or endchar ends
        /// the reachable program, and whatever trails it copies verbatim (non-call
        /// tokens by definition — the call graph never reaches them).</summary>
        private ReadStatus RewriteBody(byte[] program, out FontDiagnostic diagnostic)
        {
            var rewrite = _rewrite!;
            var output = rewrite.Output;
            int copied = 0;   // output covers program[0..copied)
            int freshAt = -1; // the latest push token's start; -1 once anything else runs
            int i = 0, n = program.Length;
            while (i < n)
            {
                int start = i;
                int b = program[i++];
                if (b == 28 || b >= 32)
                {
                    int width = ReadPush(b, program, start, n, out Slot value);
                    if (width < 0) return FailDerail(start, n, out diagnostic);
                    _stack.Add(value);
                    freshAt = start;
                    i = start + width;
                    continue; // the operand token stays deferred for a possible call
                }
                if (b is 10 or 29)
                {
                    Slot top = _stack.Count > 0 ? _stack[^1] : default;
                    if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
                    ReadStatus status = Call(b == 10, top, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (freshAt < 0)
                    {
                        // The call's operand did not come from an immediately
                        // preceding push: no single-byte rewrite serves a value
                        // whose origin is a stack shape (§4 A3's rewrite tier).
                        return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                            DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                            (uint)start, (uint)n);
                    }
                    if (_pendingOperand != top.Value)
                    {
                        // The value changed: emit everything before the operand
                        // token verbatim, then the re-encoded operand and the
                        // operator byte, and charge the recode unit (card §2.1).
                        if (!_budget.Work(1, _at, out diagnostic))
                            return ReadStatus.ResourceLimit;
                        Flush(program, ref copied, freshAt, output);
                        EmitOperand(output, _pendingOperand);
                        output.Add((byte)b);
                        rewrite.CountRecodedSite();
                    }
                    else
                    {
                        // Same value — the original operand bytes stay (the
                        // re-encoding judgment is by value, not width).
                        Flush(program, ref copied, i, output);
                    }
                    copied = i;
                    freshAt = -1;
                    continue;
                }
                freshAt = -1;
                if (b == 12)
                {
                    if (i >= n) return FailDerail(start, n, out diagnostic);
                    if (_cff2) _stack.Clear();
                    else ApplyEscape(program[i]);
                    i++;
                }
                else if (b is 1 or 3 or 18 or 23)
                {
                    _hints += _stack.Count / 2;
                    _stack.Clear();
                }
                else if (b is 19 or 20)
                {
                    // Implicit vstems again (TN5177 §4.3; the Microsoft CFF2
                    // hintmask section): the rewrite must count the stack's
                    // pairs exactly as the scan did or the copied mask bytes
                    // span a different program region.
                    _hints += _stack.Count / 2;
                    _stack.Clear();
                    int skip = (_hints + 7) >> 3;
                    if (i + skip > n) return FailDerail(i + skip, n, out diagnostic);
                    i += skip;
                }
                else if (b == 11 || b == 14)
                {
                    if (!_cff2)
                    {
                        // The program's reachable end: copy through the terminator
                        // and preserve the unreachable tail verbatim.
                        Flush(program, ref copied, n, output);
                        diagnostic = default;
                        return ReadStatus.Done;
                    }
                    // CFF2 has no return/endchar: the byte rides the default case.
                }
                else if (_cff2 && b == 15)
                {
                    ReadStatus status = ApplyVsindex(out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                else if (_cff2 && b == 16)
                {
                    ReadStatus status = ApplyBlend(out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                else if (b is 4 or 5 or 6 or 7 or 8 or 21 or 22 or 24 or 25 or 26
                    or 27 or 30 or 31 or 34 or 35 or 36 or 37)
                {
                    // The scan's path-construction list (hvcurveto included): the
                    // rewrite must consume the stack exactly as the reach walk did or
                    // the copied mask bytes span a different program region.
                    _stack.Clear();
                }
                Flush(program, ref copied, i, output);
            }
            Flush(program, ref copied, n, output);
            diagnostic = default;
            return ReadStatus.Done;
        }

        private static void Flush(byte[] program, ref int copied, int upto,
            List<byte> output)
        {
            for (int at = copied; at < upto; at++)
                output.Add(program[at]);
            copied = upto;
        }

        /// <summary>The minimal push encoding (TN5177 §3.1): one byte over
        /// [-107, 107], the two-byte positive/negative ranges, the 28 shortint —
        /// which covers every expressible new index − new bias — and the 255 fixed
        /// form as the unreachable total.</summary>
        private static void EmitOperand(List<byte> output, int value)
        {
            if (value is >= -107 and <= 107)
            {
                output.Add((byte)(value + 139));
            }
            else if (value is >= 108 and <= 1131)
            {
                int remainder = value - 108;
                output.Add((byte)(247 + (remainder >> 8)));
                output.Add((byte)(remainder & 0xFF));
            }
            else if (value is >= -1131 and <= -108)
            {
                int remainder = -value - 108;
                output.Add((byte)(251 + (remainder >> 8)));
                output.Add((byte)(remainder & 0xFF));
            }
            else if (value is >= -32768 and <= 32767)
            {
                int raw = value & 0xFFFF;
                output.Add((byte)28);
                output.Add((byte)(raw >> 8));
                // The low byte rides the mask too: raw spans the full 16 bits and
                // the checked builds throw on the bare cast at 1132 and beyond.
                output.Add((byte)(raw & 0xFF));
            }
            else
            {
                output.Add((byte)255);
                output.Add((byte)((value >> 24) & 0xFF));
                output.Add((byte)((value >> 16) & 0xFF));
                output.Add((byte)((value >> 8) & 0xFF));
                output.Add((byte)(value & 0xFF));
            }
        }

        // ---- the shared token decode ------------------------------------------

        /// <summary>Decodes the push token whose leading byte <paramref name="lead"/>
        /// sits at <paramref name="start"/>. Returns the token width, or −1 when the
        /// token truncates (each loop applies its own policy: the seac walk's single
        /// step, the reach/rewrite walk's A3 refusal).</summary>
        private static int ReadPush(int lead, byte[] program, int start, int n,
            out Slot value)
        {
            value = default;
            if (lead == 28)
            {
                if (start + 3 > n) return -1;
                int raw = (program[start + 1] << 8) | program[start + 2];
                // The sign extension stays in int space so the checked builds never
                // narrow through short.
                value = Slot.Of(raw >= 0x8000 ? raw - 0x10000 : raw);
                return 3;
            }
            if (lead is >= 32 and <= 246)
            {
                value = Slot.Of(lead - 139);
                return 1;
            }
            if (lead is >= 247 and <= 250)
            {
                if (start + 2 > n) return -1;
                value = Slot.Of((lead - 247) * 256 + program[start + 1] + 108);
                return 2;
            }
            if (lead is >= 251 and <= 254)
            {
                if (start + 2 > n) return -1;
                value = Slot.Of(-(lead - 251) * 256 - program[start + 1] - 108);
                return 2;
            }
            // 255: the 16.16 fixed form — a value the scan never decodes.
            if (start + 5 > n) return -1;
            return 5;
        }
    }
}
