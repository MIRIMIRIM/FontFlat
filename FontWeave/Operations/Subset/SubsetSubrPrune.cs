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
/// OQ-S4-5, extended with reachability collection and a per-body stem memo) collects
/// the bodies every retained glyph program's call graph reaches — the Global Subrs
/// INDEX as one face-wide set, every FD's Local Subrs INDEX as its own bucket keyed by
/// the glyph's FDSelect binding (OQ-S4-4 lean) — and parks a prune plan (survivor
/// lists in original relative order, the OQ-S1c-6 shape, plus renumbering maps, the
/// old/new bias pair per pruned INDEX and the stem memo) for the payload rebuilds.
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
    /// renumbering (null = that INDEX rides verbatim), the per-body stem memo the
    /// rewrite walk's mask widths read (the analysis's §2.1 memo), and, per reachable
    /// global body, the FD context its single scan ran under (the rewrite walk's
    /// locals table; multi-context global bodies with local calls refused at plan
    /// time, §4 A3). Identity — nothing pruned anywhere — never parks a plan, so the
    /// payload's verbatim path is the §3.5 byte-identity anchor.</summary>
    internal sealed class SubrPrunePlan
    {
        internal required SubrIndexPlan? Global { get; init; }

        /// <summary>One slot per FD of the face (name-keyed: the single entry of its
        /// one Local Subrs INDEX, FD 0 by §2.2's same-method rule).</summary>
        internal required SubrIndexPlan?[] LocalByFd { get; init; }

        internal required IReadOnlyDictionary<(int Fd, int Index), int> GlobalStems { get; init; }

        internal required IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> LocalStems { get; init; }

        internal required IReadOnlyDictionary<int, int> GlobalContextFd { get; init; }

        internal int PrunedGlobalCount => Global is { } global ? global.OldCount - global.NewCount : 0;

        internal int PrunedLocalCount(int fd) =>
            LocalByFd[fd] is { } local ? local.OldCount - local.NewCount : 0;
    }

    /// <summary>The reachability analysis one compact CFF face accumulates over the
    /// frozen map's output order: the face-wide global body set, every FD's local
    /// body bucket, the per-body stem memo (global bodies keyed by their scanning FD
    /// context — their callsubr targets, and therefore their stem contribution, are
    /// the caller's FD's), and the facts the multi-FD divergence guard reads.</summary>
    private sealed class SubrAnalysis
    {
        internal readonly HashSet<int> GlobalReach = [];
        internal readonly Dictionary<int, HashSet<int>> GlobalBodyFds = [];
        internal readonly Dictionary<(int Fd, int Index), int> GlobalStems = [];
        internal readonly HashSet<int> GlobalLocalCall = [];
        internal readonly Dictionary<int, HashSet<int>> LocalReach = [];
        internal readonly Dictionary<int, Dictionary<int, int>> LocalStems = [];

        internal HashSet<int> LocalSet(int fd)
        {
            if (!LocalReach.TryGetValue(fd, out var set))
            {
                set = [];
                LocalReach[fd] = set;
            }
            return set;
        }

        private Dictionary<int, int> LocalStemMap(int fd)
        {
            if (!LocalStems.TryGetValue(fd, out var map))
            {
                map = [];
                LocalStems[fd] = map;
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

        internal bool TryGetStems(int kind, int fd, int index, out int stems)
        {
            if (kind == SubrWalk.KindGlobal)
                return GlobalStems.TryGetValue((fd, index), out stems);
            if (LocalStems.TryGetValue(fd, out var map) &&
                map.TryGetValue(index, out stems!))
                return true;
            stems = 0;
            return false;
        }

        internal void SetStems(int kind, int fd, int index, int value)
        {
            if (kind == SubrWalk.KindGlobal)
                GlobalStems[(fd, index)] = value;
            else
                LocalStemMap(fd)[index] = value;
        }

        internal SubrPrunePlan ToPlan(SubrIndexPlan? global, SubrIndexPlan?[] localByFd)
        {
            var localStems = new Dictionary<int, IReadOnlyDictionary<int, int>>();
            foreach ((int fd, var map) in LocalStems)
                localStems[fd] = map;
            var contextFd = new Dictionary<int, int>();
            foreach ((int index, var fds) in GlobalBodyFds)
                contextFd[index] = fds.First();
            return new SubrPrunePlan
            {
                Global = global,
                LocalByFd = localByFd,
                GlobalStems = GlobalStems,
                LocalStems = localStems,
                GlobalContextFd = contextFd,
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
    /// stack effects the walker applies in its CFF2 mode and no endchar (TN5171
    /// charstrings end by fall-off). TopDICT carries no charset/encoding, so the
    /// stable identity and this stage never interact.</summary>
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
            var walk = new SubrWalk(budget, model.Location.At(DataField.GlyphId, 0, gid),
                fdLocals[fd], globals, cff2: true, seac: false, analysis, fd,
                rewrite: null);
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
        plan = analysis.ToPlan(globalPlan, localPlans);
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

    // ---- the payload rewriter (§3.3) ---------------------------------------------

    /// <summary>Rewrites the retained programs — one FD context each — under a
    /// parked plan: a token-level walk that copies bytes verbatim except the
    /// callsubr/callgsubr sites, whose operand re-encodes as new index − new bias in
    /// the minimal push form; a site whose value the renumbering leaves unchanged
    /// keeps its original operand bytes (the judgment is by value, not width). Mask
    /// skip widths read the plan's stem memo (§2.1) so the walk tracks the
    /// runtime's hint count across call frames. Sites whose operand does not sit in
    /// the immediately preceding push token refuse (A3): their value's origin is a
    /// stack shape no single-byte rewrite can serve.</summary>
    private static ReadStatus RewritePrograms(byte[][] programs, int[] programFds,
        byte[][] globals, byte[][][] fdLocals, SubrPrunePlan plan, bool cff2,
        DataLocation modelAt, BuildBudget budget, out byte[][] rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = new byte[programs.Length][];
        for (int i = 0; i < programs.Length; i++)
        {
            int fd = programFds[i];
            var rewrite = new RewriteState(plan, fd, plan.LocalByFd[fd], plan.Global);
            var walk = new SubrWalk(budget, modelAt.At(DataField.GlyphId, 0, i),
                fdLocals[fd], globals, cff2, seac: false, analysis: null, fd,
                rewrite: rewrite);
            ReadStatus status = walk.RunRewrite(programs[i], out diagnostic);
            if (status != ReadStatus.Done) return status;
            rewritten[i] = [.. rewrite.Output];
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Rewrites one pruned INDEX's surviving bodies in survivor order, each
    /// under the FD context its mask widths read the memo under (global bodies: their
    /// single scanning context from the plan; local bodies: their own FD).</summary>
    private static ReadStatus RewriteSurvivorBodies(byte[][] bodies, int[] survivorOld,
        int[] bodyFds, byte[][] globals, byte[][][] fdLocals, SubrPrunePlan plan,
        bool cff2, DataLocation modelAt, BuildBudget budget, out byte[][] rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = new byte[survivorOld.Length][];
        for (int s = 0; s < survivorOld.Length; s++)
        {
            int old = survivorOld[s];
            int fd = bodyFds[s];
            var rewrite = new RewriteState(plan, fd, plan.LocalByFd[fd], plan.Global);
            var walk = new SubrWalk(budget, modelAt.At(DataField.GlyphId, 0, old),
                fdLocals[fd], globals, cff2, seac: false, analysis: null, fd,
                rewrite: rewrite);
            ReadStatus status = walk.RunRewrite(bodies[old], out diagnostic);
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
            model.Location, budget, out charStrings, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (globalPlan is { } prunedGlobal)
        {
            status = RewriteSurvivorBodies(globals, prunedGlobal.Survivors,
                new int[prunedGlobal.NewCount], globals, fdLocals, plan, cff2: false,
                model.Location, budget, out globalSubrs, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        else
        {
            globalSubrs = globals;
        }
        if (localPlan is { } prunedLocal)
        {
            status = RewriteSurvivorBodies(locals, prunedLocal.Survivors,
                new int[prunedLocal.NewCount], globals, fdLocals, plan, cff2: false,
                model.Location, budget, out localSubrs, out diagnostic);
            if (status != ReadStatus.Done) return status;
            hasLocalSubrs = prunedLocal.NewCount > 0;
        }
        else
        {
            localSubrs = locals;
            hasLocalSubrs = model.HasLocalSubrs;
        }
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
    /// the call sites read, the plan's stem memo (with the FD context the mask
    /// widths read it under) and the program's output bytes.</summary>
    private sealed class RewriteState
    {
        internal RewriteState(SubrPrunePlan plan, int fd, SubrIndexPlan? localPlan,
            SubrIndexPlan? globalPlan)
        {
            Plan = plan;
            Fd = fd;
            LocalMap = localPlan?.NewIndexOfOld;
            GlobalMap = globalPlan?.NewIndexOfOld;
            NewLocalBias = localPlan?.NewBias ?? 0;
            NewGlobalBias = globalPlan?.NewBias ?? 0;
        }

        internal SubrPrunePlan Plan { get; }

        /// <summary>The FD context the stems memo reads under (the entry glyph's FD,
        /// or a surviving global body's single scanning context).</summary>
        internal int Fd { get; }

        internal int[]? LocalMap { get; }

        internal int[]? GlobalMap { get; }

        internal int NewLocalBias { get; }

        internal int NewGlobalBias { get; }

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
    /// and nesting deeper than TN5177 appendix B's ten frames refuse (A3); the
    /// per-body stem memo feeds re-entries without rescans.
    ///
    /// rewrite (the S4 payload, card §3.3): no descent — every call site re-encodes
    /// its operand from the parked maps, mask widths read the plan's stem memo, and
    /// bytes move verbatim except the re-encoded operands.</summary>
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

        private readonly List<Slot> _stack = [];
        private int _hints;
        private readonly List<(int Kind, int Index)> _path = [];

        /// <summary>The seac form's codes once <see cref="Found"/> turns true (the
        /// shared core reports through fields — the S3 shape's out parameters do not
        /// survive the shared recursion).</summary>
        internal bool Found { get; private set; }

        internal int BcharCode { get; private set; }

        internal int AcharCode { get; private set; }

        /// <summary>The rewrite walk's pending call operand (new index − new bias)
        /// and the called body's memoized stem contribution (the frame's mask
        /// widths read it, the §2.1 memo doing the no-descent walk's accounting).
        /// </summary>
        private int _pendingOperand;
        private int _pendingStems;

        /// <summary>An endchar anywhere in the call graph ends the whole program
        /// (TN5177 §4.2 Note 6): every walker frame stops, so a charstring that
        /// terminates at a call does not read as a fall-off.</summary>
        private bool _programEnded;

        internal SubrWalk(BuildBudget budget, DataLocation at, byte[][] locals,
            byte[][] globals, bool cff2, bool seac, SubrAnalysis? analysis, int fd,
            RewriteState? rewrite)
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
        /// operands into the walk's rewrite state.</summary>
        internal ReadStatus RunRewrite(byte[] program, out FontDiagnostic diagnostic)
        {
            return RewriteBody(program, out diagnostic);
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
            if (_analysis is not null && kind != KindGlyph &&
                _analysis.TryGetStems(kind, _fd, index, out int memoStems))
            {
                // Memo hit (card §2.1): the body completed once under this FD
                // context, so its stem contribution re-applies to this frame —
                // the caller's masks stay correctly sized — without a rescan.
                _hints += memoStems;
                return ReadStatus.Done;
            }
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
                    // (cntr)mask: pop the optional width, skip the hint mask bytes.
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
                    Pop(1); // vsindex: the selector is consumed
                    continue;
                }
                if (_cff2 && b == 16)
                {
                    // blend: the pop count depends on the runtime region count —
                    // the conservative clear poisons every later call operand into
                    // the unknown that refuses (card §2.1's CFF2 addendum).
                    _stack.Clear();
                    continue;
                }
                if (b is 4 or 5 or 6 or 7 or 8 or 21 or 22 or 24 or 25 or 26 or 27
                    or 30 or 34 or 35 or 36 or 37)
                {
                    // Path construction: every value on the stack is this operator's
                    // operand (plus the optional width), well-formed programs included.
                    _stack.Clear();
                    continue;
                }
                // Reserved or not modeled: no stack effect is assumed, the
                // conservative direction (values persist rather than vanish).
            }
            _path.RemoveAt(_path.Count - 1);
            if (_analysis is not null && kind != KindGlyph)
                _analysis.SetStems(kind, _fd, index, _hints - hintsBefore);
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
        /// the re-encoded operand for the loop's emission instead (no descent — its
        /// mask widths come from the plan's stem memo).</summary>
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
                _pendingOperand = newIdx -
                    (local ? _rewrite.NewLocalBias : _rewrite.NewGlobalBias);
                // The called body's stem contribution keeps this frame's hint count
                // runtime-faithful without descending (the analysis's memo; a miss
                // is a pipeline bug the refusal surfaces, never a silent width).
                int stems = local
                    ? (_rewrite.Plan.LocalStems.TryGetValue(_rewrite.Fd,
                            out var localMap) &&
                        localMap.TryGetValue(old, out int localStems)
                            ? localStems : -1)
                    : (_rewrite.Plan.GlobalStems.TryGetValue((_rewrite.Fd, old),
                            out int globalStems)
                            ? globalStems : -1);
                if (stems < 0)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                        DiagnosticCode.UnsupportedOperation, _at, out diagnostic,
                        (uint)old, 0UL);
                _pendingStems = stems;
                return ReadStatus.Done;
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

        /// <summary>The 12 X escape family: arithmetic and logic produce unknown
        /// values, random always does, and the stack tools keep their fixed arities
        /// (TN5176 Type 2 operator table).</summary>
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
                case 5 or 30:            // not / index → unknown
                    Pop(1);
                    _stack.Add(default);
                    break;
                case 18:                 // drop
                    Pop(1);
                    break;
                case 23:                 // random → unknown
                    _stack.Add(default);
                    break;
                case 28:                 // dup: the copy carries the original's state
                    if (_stack.Count > 0) _stack.Add(_stack[^1]);
                    break;
                case 29:                 // exch
                    if (_stack.Count >= 2)
                        (_stack[^1], _stack[^2]) = (_stack[^2], _stack[^1]);
                    break;
                case 31:                 // roll: pops its two control values
                    Pop(2);
                    break;
                default:                 // unmodeled: assume no effect
                    break;
            }
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
                    _hints += _pendingStems; // the called body's stems executed here
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
                    Pop(1);
                }
                else if (_cff2 && b == 16)
                {
                    _stack.Clear();
                }
                else if (b is 4 or 5 or 6 or 7 or 8 or 21 or 22 or 24 or 25 or 26
                    or 27 or 30 or 34 or 35 or 36 or 37)
                {
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
                output.Add((byte)raw);
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
