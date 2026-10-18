using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Operations.Subset;

/// <summary>The S2c layout-scope resolution (S2c card §2): the caller's declared
/// script/language/feature lists resolve once, over the step 3.5 materialized models
/// (zero second parse, the AGENTS freeze), into one resident verdict per layout list
/// table — the surviving script/langsys/feature/lookup sets, the old→new remaps, and
/// the no-op verdict. The resolution feeds two consumers without re-derivation: the
/// step 4 decide (the matrix's fifth alternate pulls a truly-cut table to
/// RebuildTyped; a no-op verdict short-circuits back to the undeclared decision,
/// byte-identical per §4.2) and the step 4.5 closure, whose reference union
/// enumerates only surviving lookups (a cut lookup no longer pulls glyphs; the
/// conservative direction is unchanged — every reference of every surviving rule
/// still merges, the cut only shrinks the rule set, §4.3).
///
/// The lookup seed is the union of the surviving feature entries' lookup indices, the
/// required features of every surviving langsys (implicitly kept, never cut by the
/// feature list — OQ-S2c-5), the alternate features of every surviving
/// FeatureVariations record (OQ-S2c-6: a record survives iff its replaced
/// featureIndex survives, and its replacement's lookups must survive with it), and —
/// via the worklist — every contextual/chaining SequenceLookupRecord target of a
/// surviving lookup. The nested-reference worklist is 05 §6's "rule index +
/// change-worklist" obligation: on every feature-rich face orphan lookups (reached
/// only through nested records, never through a feature) exist, and a scope cut
/// without the transitive closure would leave the surviving rules pointing at
/// deleted lookup indices.</summary>
public static partial class SubsetOperation
{
    /// <summary>One layout list table's scope verdict. The surviving sets keep the
    /// original wire order (record/index order invariants — the script tags' strict
    /// ascent and the feature tags' non-descending order both survive a subsequence),
    /// and the remaps are total on the surviving sets and -1 on the cut ones.</summary>
    internal sealed class LayoutScopeResolution
    {
        internal required uint Tag { get; init; }
        internal int InputScriptCount { get; init; }
        internal int InputFeatureCount { get; init; }
        internal int InputLookupCount { get; init; }
        internal List<int> SurvivingScripts { get; } = [];
        internal List<int> SurvivingFeatures { get; } = [];
        internal List<int> SurvivingLookups { get; } = [];
        /// <summary>Per surviving script (parallel to <see cref="SurvivingScripts"/>):
        /// the surviving tagged LangSys record ordinals, ascending. A script pruned
        /// for an empty langsys tree (§5.1 step 5) never enters SurvivingScripts.</summary>
        internal List<int>[] SurvivingLanguages { get; set; } = [];
        /// <summary>Per input script ordinal: the tagged LangSys record count the
        /// resolution answered against — the language dimension's equality baseline
        /// for the no-op verdict (S2c card §2.1/§4.2).</summary>
        internal int[] InputLanguageCounts { get; set; } = [];
        internal int[] LookupNewIndex { get; private set; } = [];
        internal int[] FeatureNewIndex { get; private set; } = [];
        // OQ-S2c-7/10: per-declared-tag match counts over the input face's whole
        // table — a declared tag matching nothing (the typo case) reads zero here,
        // visible in the scope census, never refused.
        internal int DeclaredScriptMatches { get; set; }
        internal int DeclaredLanguageMatches { get; set; }
        internal int DeclaredFeatureMatches { get; set; }

        /// <summary>The no-op verdict (S2c card §2.1 step 4): the three surviving
        /// sets equal the input sets (count and membership — the lists are built as
        /// input-ordinals-only, so count equality is membership equality) and every
        /// surviving script's tagged LangSys count equals its input count — a
        /// language-only declaration (§2.1's "only the listed tagged LangSys
        /// records survive" MUST) is a true cut, never the short circuit. A no-op
        /// table short-circuits to the undeclared decision: retain copies raw,
        /// compact rides the S2b whole-table rewrite, and the output stays
        /// byte-identical to the same request without a declaration (§4.2 — the
        /// guarantee binds the declared-full-set form only).</summary>
        internal bool ListsNoOp => SurvivingScripts.Count == InputScriptCount &&
            SurvivingFeatures.Count == InputFeatureCount &&
            SurvivingLookups.Count == InputLookupCount && LanguagesNoOp;

        private bool LanguagesNoOp
        {
            get
            {
                for (int s = 0; s < SurvivingScripts.Count; s++)
                    if (SurvivingLanguages[s].Count !=
                        InputLanguageCounts[SurvivingScripts[s]])
                        return false;
                return true;
            }
        }

        internal void BuildRemaps()
        {
            LookupNewIndex = new int[InputLookupCount];
            Array.Fill(LookupNewIndex, -1);
            for (int i = 0; i < SurvivingLookups.Count; i++)
                LookupNewIndex[SurvivingLookups[i]] = i;
            FeatureNewIndex = new int[InputFeatureCount];
            Array.Fill(FeatureNewIndex, -1);
            for (int i = 0; i < SurvivingFeatures.Count; i++)
                FeatureNewIndex[SurvivingFeatures[i]] = i;
        }
    }

    /// <summary>The JSTF scope verdict (S2c card §5.2, OQ-S2c-8): the script/lang
    /// tree prunes by the same tag lists, and the mod-list entries re-answer
    /// against the GSUB/GPOS surviving-lookup remaps — a target the scope cut
    /// deletes its entry (enable/disable over a deleted lookup is a no-op, so the
    /// deletion is the honest form; the increasing-order observation survives the
    /// subsequence). The extender lists never cut (no script domain — the hard
    /// reference face) and the JstfMax inline lookups ride their script.</summary>
    internal sealed class JstfScopeResolution
    {
        internal int InputScriptCount { get; init; }
        internal List<int> SurvivingScripts { get; } = [];
        internal List<int>[] SurvivingLanguages { get; set; } = [];
        /// <summary>Per input script ordinal: the tagged LangSys record count the
        /// resolution answered against (the no-op verdict's language baseline).</summary>
        internal int[] InputLanguageCounts { get; set; } = [];
        internal int DanglingModEntries { get; set; }
        internal int DeclaredScriptMatches { get; set; }
        internal int DeclaredLanguageMatches { get; set; }
        /// <summary>Whether both neighbor tables' surviving-lookup remaps ride
        /// identity (a table absent, unscoped, or no-op). The mod entries
        /// address the neighbor lookup record ordinals (S2b OQ-S2b-7), so a
        /// neighbor that truly cut shifts every later index — the JSTF short
        /// circuit must not fire while the mod domains move, or a raw-preserved
        /// tree keeps mod entries pointing at the wrong lookups.</summary>
        internal bool NeighborRemapsIdentity { get; set; }

        /// <summary>The no-op verdict: the script trees are whole, no mod entry
        /// dangles, the neighbor remaps are identity, and every surviving script's
        /// tagged LangSys count equals its input count (a language-only cut is a
        /// true cut — S2c card §2.1/§5.2) — then (and only then) the short-circuit
        /// keeps the undeclared decision byte for byte.</summary>
        internal bool ListsNoOp => SurvivingScripts.Count == InputScriptCount &&
            DanglingModEntries == 0 && NeighborRemapsIdentity && LanguagesNoOp;

        private bool LanguagesNoOp
        {
            get
            {
                for (int s = 0; s < SurvivingScripts.Count; s++)
                    if (SurvivingLanguages[s].Count !=
                        InputLanguageCounts[SurvivingScripts[s]])
                        return false;
                return true;
            }
        }
    }

    /// <summary>The JSTF resolution (after the two list tables — the mod remaps
    /// borrow their old→new maps): script/lang matching over the JSTF tree, the
    /// dangling census over every mod list, and the per-dimension match counts.</summary>
    private static void ResolveJstfScope(JstfTable jstf, SubsetLayoutScope scope,
        PrepareState state, out JstfScopeResolution resolution,
        out FontDiagnostic diagnostic)
    {
        var verdict = new JstfScopeResolution
        {
            InputScriptCount = jstf.ScriptCount
        };
        if (scope.ScriptTags is { } scriptTags)
        {
            var present = new HashSet<uint>();
            foreach (var (tag, _) in jstf.Scripts) present.Add(tag);
            verdict.DeclaredScriptMatches = CountListMatches(scriptTags, present);
        }
        if (scope.LanguageTags is { } languageTags)
        {
            var present = new HashSet<uint>();
            foreach (var (_, script) in jstf.Scripts)
                foreach (var (langTag, _) in script.LangSys)
                    present.Add(langTag);
            verdict.DeclaredLanguageMatches = CountListMatches(languageTags, present);
        }
        var languages = new List<List<int>>();
        var inputLanguageCounts = new int[jstf.ScriptCount];
        for (int s = 0; s < jstf.ScriptCount; s++)
        {
            (uint tag, JstfScriptTable script) = jstf.Scripts[s];
            inputLanguageCounts[s] = script.LangSysCount;
            if (scope.ScriptTags is { } tags && !ContainsTag(tags, tag)) continue;
            verdict.SurvivingScripts.Add(s);
            var rows = new List<int>();
            for (int l = 0; l < script.LangSysCount; l++)
                if (scope.LanguageTags is null ||
                    ContainsTag(scope.LanguageTags, script.LangSys[l].Tag))
                    rows.Add(l);
            languages.Add(rows);
        }
        verdict.SurvivingLanguages = [.. languages];
        verdict.InputLanguageCounts = inputLanguageCounts;
        // The dangling census: every mod entry whose target lookup the GSUB/GPOS
        // scope cut. A table without a resolution (absent or no-op) contributes
        // its full count as the domain — identity remaps, nothing dangles.
        verdict.DanglingModEntries = CountDanglingMods(jstf, state);
        // The neighbor remaps: a truly-cut neighbor shifts the record ordinals
        // the surviving mod entries address, so the tree re-emits over the remap
        // even with zero dangles (§5.2 — the remap is unconditional under a
        // neighbor cut; the raw form would address the wrong lookups).
        verdict.NeighborRemapsIdentity =
            (state.GsubScope is null || state.GsubScope.ListsNoOp) &&
            (state.GposScope is null || state.GposScope.ListsNoOp);
        resolution = verdict;
        diagnostic = default;
    }

    private static int CountDanglingMods(JstfTable jstf, PrepareState state)
    {
        int dangling = 0;
        foreach (var (_, script) in jstf.Scripts)
        {
            CollectDangling(script.DefaultLangSys, state, ref dangling);
            foreach (var (_, langSys) in script.LangSys)
                CollectDangling(langSys, state, ref dangling);
        }
        return dangling;
    }

    private static void CollectDangling(JstfLangSysTable? langSys, PrepareState state,
        ref int dangling)
    {
        if (langSys is null) return;
        foreach (var priority in langSys.Priorities)
        {
            CountSlots(priority.GsubShrinkageEnable, priority.GsubShrinkageDisable,
                priority.GposShrinkageEnable, priority.GposShrinkageDisable,
                priority.GsubExtensionEnable, priority.GsubExtensionDisable,
                priority.GposExtensionEnable, priority.GposExtensionDisable,
                state, ref dangling);
        }
    }

    private static void CountSlots(JstfModListTable? gsubShrinkageEnable,
        JstfModListTable? gsubShrinkageDisable, JstfModListTable? gposShrinkageEnable,
        JstfModListTable? gposShrinkageDisable, JstfModListTable? gsubExtensionEnable,
        JstfModListTable? gsubExtensionDisable, JstfModListTable? gposExtensionEnable,
        JstfModListTable? gposExtensionDisable, PrepareState state, ref int dangling)
    {
        foreach (var mod in new[] { gsubShrinkageEnable, gsubShrinkageDisable,
            gposShrinkageEnable, gposShrinkageDisable, gsubExtensionEnable,
            gsubExtensionDisable, gposExtensionEnable, gposExtensionDisable })
        {
            if (mod is null) continue;
            int[]? remap = mod.Domain == JstfLookupDomain.Gsub
                ? state.GsubScope?.LookupNewIndex
                : state.GposScope?.LookupNewIndex;
            if (remap is null) continue;
            foreach (ushort index in mod.LookupIndices)
                if (index >= remap.Length || remap[index] < 0) dangling++;
        }
    }

    /// <summary>The declaration's "set" test (S2c card §1.3): any member non-null.
    /// A scope record with every member null is indistinguishable from an absent
    /// declaration — there is nothing to match.</summary>
    internal static bool ScopeDeclared(SubsetOptions options) =>
        options.LayoutScope is { } scope &&
        (scope.ScriptTags is not null || scope.LanguageTags is not null ||
            scope.FeatureTags is not null);

    /// <summary>Step 3.6 (S2c card §3): resolve the declared scope over the admission
    /// models, once, before decide. GSUB and GPOS resolve independently (a GSUB cut
    /// with GPOS wholly kept is a legal state, S2c card §2.1 step 4); JSTF's script
    /// tree joins with its rewrite face.</summary>
    private static ReadStatus ResolveLayoutScopes(PrepareState state,
        SubsetLayoutScope scope, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        var census = state.LayoutScopeCensus ??= [];
        if (state.Gsub is { } gsub)
        {
            ReadStatus status = ResolveLayoutListsScope(gsub, Gsub, scope, state.Budget,
                out state.GsubScope, out diagnostic);
            if (status != ReadStatus.Done) return status;
            census.Add(ScopeCensusRow(state.GsubScope!));
        }
        if (state.Gpos is { } gpos)
        {
            ReadStatus status = ResolveLayoutListsScope(gpos, Gpos, scope, state.Budget,
                out state.GposScope, out diagnostic);
            if (status != ReadStatus.Done) return status;
            census.Add(ScopeCensusRow(state.GposScope!));
        }
        if (state.Jstf is { } jstf)
        {
            ResolveJstfScope(jstf, scope, state, out state.JstfScope,
                out diagnostic);
            if (diagnostic.Code != 0) return ReadStatus.InvalidFormat;
            census.Add(new LayoutScopeCensusRow(Jstf, jstf.ScriptCount,
                state.JstfScope.SurvivingScripts.Count, 0, 0, 0, 0,
                state.JstfScope.DeclaredScriptMatches,
                state.JstfScope.DeclaredLanguageMatches, 0, 0));
        }
        // The GDEF transparency row (OQ-S2c-9): the mark sets are never renumbered
        // (the MFS ordinal domain is the all-sets policy), so a mark set every
        // surviving lookup stopped referencing stays — the orphan count registers
        // the staleness honestly (the MERG raw / OQ-S2a-7 stance).
        if (state.Gdef is { } gdef)
            census.Add(new LayoutScopeCensusRow(Gdef, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                CountOrphanMarkSets(gdef, state)));
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The orphan mark-set count: the sets no surviving lookup's
    /// MarkFilteringSet names (the conservative keep's registered face).</summary>
    private static int CountOrphanMarkSets(GdefTable gdef, PrepareState state)
    {
        if (gdef.MarkGlyphSets is not { } sets) return 0;
        var used = new HashSet<int>();
        foreach (var table in new[] { state.Gsub, state.Gpos })
        {
            if (table is null) continue;
            var resolution = table == state.Gsub ? state.GsubScope : state.GposScope;
            IEnumerable<int> surviving = resolution is null
                ? Enumerable.Range(0, table.Lookups.Length)
                : resolution.SurvivingLookups;
            foreach (int i in surviving)
                if (table.Lookups[i].MarkFilteringSet is { } markSet)
                    used.Add(markSet);
        }
        int orphans = 0;
        for (int s = 0; s < sets.Sets.Count; s++)
            if (!used.Contains(s)) orphans++;
        return orphans;
    }

    /// <summary>The scope census row (OQ-S2c-10): the surviving element counts and
    /// the per-dimension declared-tag match counts, count level only.</summary>
    private static LayoutScopeCensusRow ScopeCensusRow(LayoutScopeResolution verdict) =>
        new(verdict.Tag, verdict.InputScriptCount, verdict.SurvivingScripts.Count,
            verdict.InputFeatureCount, verdict.SurvivingFeatures.Count,
            verdict.InputLookupCount, verdict.SurvivingLookups.Count,
            verdict.DeclaredScriptMatches, verdict.DeclaredLanguageMatches,
            verdict.DeclaredFeatureMatches, 0);

    /// <summary>One table's resolution (S2c card §2.1): record-level tag matching,
    /// the required-feature implicit keep, the FV alternate merge, and the nested-
    /// record worklist to its fixed point.</summary>
    private static ReadStatus ResolveLayoutListsScope(LayoutListsTable table, uint tag,
        SubsetLayoutScope scope, BuildBudget budget, out LayoutScopeResolution? resolution,
        out FontDiagnostic diagnostic)
    {
        resolution = null;
        var verdict = new LayoutScopeResolution
        {
            Tag = tag,
            InputScriptCount = table.Scripts.Length,
            InputFeatureCount = table.Features.Length,
            InputLookupCount = table.Lookups.Length,
        };
        // Per-dimension declared-tag match counts over the input face's whole table
        // (the element tags the declaration was answered against, pre-cut).
        if (scope.ScriptTags is { } scriptTags)
        {
            var scriptTagSet = new HashSet<uint>();
            for (int i = 0; i < table.Scripts.Length; i++)
                scriptTagSet.Add(table.Scripts[i].Tag);
            verdict.DeclaredScriptMatches = CountListMatches(scriptTags, scriptTagSet);
        }
        if (scope.LanguageTags is { } languageTags)
            verdict.DeclaredLanguageMatches = CountListMatches(languageTags,
                TaggedLanguageTags(table));
        if (scope.FeatureTags is { } featureTags)
        {
            var featureTagSet = new HashSet<uint>();
            for (int i = 0; i < table.Features.Length; i++)
                featureTagSet.Add(table.Features[i].Tag);
            verdict.DeclaredFeatureMatches = CountListMatches(featureTags, featureTagSet);
        }

        // Step 1 — scripts and their langsys trees: a null list keeps the dimension;
        // a declared list keeps exactly its members. The DefaultLanguage follows the
        // script (it carries no record tag, S2c card §1.1). A script whose surviving
        // langsys tree is empty — no default and every tagged row cut — is pruned
        // (§5.1 step 5: it contributes nothing; keeping it would emit a script shell
        // whose langsys records all point at deleted feature indices).
        var langsysSurvives = new List<(int Script, List<int> Languages)>();
        var inputLanguageCounts = new int[table.Scripts.Length];
        for (int s = 0; s < table.Scripts.Length; s++)
        {
            LayoutScript script = table.Scripts[s];
            inputLanguageCounts[s] = script.Languages.Length;
            if (scope.ScriptTags is { } tags && !ContainsTag(tags, script.Tag)) continue;
            var languages = new List<int>();
            for (int l = 0; l < script.Languages.Length; l++)
                if (scope.LanguageTags is null ||
                    ContainsTag(scope.LanguageTags, script.Languages[l].Tag))
                    languages.Add(l);
            if (script.DefaultLanguage is null && languages.Count == 0) continue;
            langsysSurvives.Add((s, languages));
        }
        verdict.SurvivingScripts.AddRange(langsysSurvives.Select(row => row.Script));
        verdict.SurvivingLanguages = [.. langsysSurvives.Select(row => row.Languages)];
        verdict.InputLanguageCounts = inputLanguageCounts;

        // Step 2 — feature entries: the declared tags keep all of a tag's entries
        // (the global list's per-(script,langsys) eligibility copies are not carved
        // per entry — the eligibility dimension belongs to the script/language
        // lists), plus every surviving langsys's required feature index (implicitly
        // kept — a shaper applies it unconditionally, OQ-S2c-5; the read chain
        // bounded every index at admission).
        var featureSurvives = new bool[table.Features.Length];
        for (int f = 0; f < table.Features.Length; f++)
            featureSurvives[f] = scope.FeatureTags is null ||
                ContainsTag(scope.FeatureTags, table.Features[f].Tag);
        for (int s = 0; s < verdict.SurvivingScripts.Count; s++)
        {
            LayoutScript script = table.Scripts[verdict.SurvivingScripts[s]];
            if (script.DefaultLanguage is { } defaultLangSys &&
                defaultLangSys.RequiredFeatureIndex != ushort.MaxValue)
                featureSurvives[defaultLangSys.RequiredFeatureIndex] = true;
            foreach (int languageOrdinal in verdict.SurvivingLanguages[s])
            {
                ushort required =
                    script.Languages[languageOrdinal].Language.RequiredFeatureIndex;
                if (required != ushort.MaxValue) featureSurvives[required] = true;
            }
        }
        for (int f = 0; f < featureSurvives.Length; f++)
            if (featureSurvives[f]) verdict.SurvivingFeatures.Add(f);

        // Step 3 — the lookup seeds: surviving features' indices, every surviving FV
        // record's alternate feature indices (a record survives iff its replaced
        // featureIndex survives, OQ-S2c-6), then the nested-record worklist to its
        // fixed point. One element = one Work unit (the S2a card §2.6 shape).
        var lookupSurvives = new bool[table.Lookups.Length];
        var pending = new Queue<int>();
        foreach (int featureIndex in verdict.SurvivingFeatures)
        {
            if (!budget.Work(1, table.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            foreach (ushort lookupIndex in table.Features[featureIndex].LookupIndices)
                SeedLookup(lookupIndex, lookupSurvives, pending);
        }
        if (table.FeatureVariations is { } variations)
        {
            foreach (var record in variations.Records)
            {
                if (record.Substitution is not { } substitution) continue;
                bool recordSurvives = false;
                foreach (var entry in substitution.Records)
                {
                    if (!featureSurvives[entry.FeatureIndex]) continue;
                    recordSurvives = true;
                    if (!budget.Work(1, table.Location, out diagnostic))
                        return ReadStatus.ResourceLimit;
                    foreach (ushort lookupIndex in entry.Alternate.LookupIndices)
                        SeedLookup(lookupIndex, lookupSurvives, pending);
                }
                _ = recordSurvives; // the rewrite face prunes dead records (S2c card §2.3)
            }
        }
        // The worklist (05 §6): every surviving contextual/chaining rule's
        // SequenceLookupRecord target joins the set; a newly reached lookup is
        // processed once. Extension wrappers unwrap one level (the shared
        // Contextuals/Chainings models re-enter the same walk).
        while (pending.Count > 0)
        {
            int lookupIndex = pending.Dequeue();
            if (!budget.Work(1, table.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            CollectNestedLookupIndices(table.Lookups[lookupIndex], lookupSurvives,
                pending);
        }
        for (int i = 0; i < lookupSurvives.Length; i++)
            if (lookupSurvives[i]) verdict.SurvivingLookups.Add(i);
        verdict.BuildRemaps();
        resolution = verdict;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Test bridge for the per-table resolution (the S2a card §3 layer-1
    /// registration shape): the parser's unit tests drive it directly over compiled
    /// models, since the pipeline keeps the resolution resident and the no-op
    /// short-circuit never publishes the verdict itself.</summary>
    internal static ReadStatus ResolveLayoutListsScopeForTests(LayoutListsTable table,
        SubsetLayoutScope scope, out LayoutScopeResolution? resolution,
        out FontDiagnostic diagnostic) =>
        ResolveLayoutListsScope(table, Gsub, scope,
            new BuildBudget(new BuildLimits()), out resolution, out diagnostic);

    private static void SeedLookup(ushort lookupIndex, bool[] lookupSurvives,
        Queue<int> pending)
    {
        if (lookupIndex >= lookupSurvives.Length || lookupSurvives[lookupIndex]) return;
        lookupSurvives[lookupIndex] = true;
        pending.Enqueue(lookupIndex);
    }

    /// <summary>One lookup's nested SequenceLookupRecord targets (S2c card §2.1 step
    /// 3): the contextual and chaining subtables' rules and format-3 record lists,
    /// with the extension wrappers unwrapped one level. The admission's typed read
    /// materialized every record list on the models, so this is a model walk, never
    /// a re-read.</summary>
    private static void CollectNestedLookupIndices(LookupTable lookup,
        bool[] lookupSurvives, Queue<int> pending)
    {
        if (lookup.Contextuals is { } contextuals)
            foreach (var sub in contextuals)
                CollectContextualRecords(sub, lookupSurvives, pending);
        if (lookup.Chainings is { } chainings)
            foreach (var sub in chainings)
                CollectChainingRecords(sub, lookupSurvives, pending);
        if (lookup.Extensions is { } extensions)
            foreach (var extension in extensions)
            {
                if (extension.Contextual is { } extensionContextual)
                    CollectContextualRecords(extensionContextual, lookupSurvives, pending);
                else if (extension.Chaining is { } extensionChaining)
                    CollectChainingRecords(extensionChaining, lookupSurvives, pending);
            }
        if (lookup.ExtensionPositions is { } extensionPositions)
            foreach (var extension in extensionPositions)
            {
                if (extension.Contextual is { } extensionContextual)
                    CollectContextualRecords(extensionContextual, lookupSurvives, pending);
                else if (extension.Chaining is { } extensionChaining)
                    CollectChainingRecords(extensionChaining, lookupSurvives, pending);
            }
    }

    private static void CollectContextualRecords(ContextualSubstTable sub,
        bool[] lookupSurvives, Queue<int> pending)
    {
        if (sub.Format == 3)
        {
            if (sub.LookupRecords is { } records)
                foreach (var record in records)
                    SeedLookup(record.LookupListIndex, lookupSurvives, pending);
            return;
        }
        if (sub.RuleSets is { } ruleSets)
            foreach (var ruleSet in ruleSets)
            {
                if (ruleSet is null) continue;
                foreach (var rule in ruleSet.Rules)
                    foreach (var record in rule.LookupRecords)
                        SeedLookup(record.LookupListIndex, lookupSurvives, pending);
            }
    }

    private static void CollectChainingRecords(ChainingSubstTable sub,
        bool[] lookupSurvives, Queue<int> pending)
    {
        if (sub.Format == 3)
        {
            if (sub.LookupRecords is { } records)
                foreach (var record in records)
                    SeedLookup(record.LookupListIndex, lookupSurvives, pending);
            return;
        }
        if (sub.RuleSets is { } ruleSets)
            foreach (var ruleSet in ruleSets)
            {
                if (ruleSet is null) continue;
                foreach (var rule in ruleSet.Rules)
                    foreach (var record in rule.LookupRecords)
                        SeedLookup(record.LookupListIndex, lookupSurvives, pending);
            }
    }

    /// <summary>The tagged LangSys record tags over the whole table (a plain loop —
    /// the Scripts/Languages spans cannot cross an iterator boundary).</summary>
    private static HashSet<uint> TaggedLanguageTags(LayoutListsTable table)
    {
        var tags = new HashSet<uint>();
        foreach (var script in table.Scripts)
            foreach (var language in script.Languages)
                tags.Add(language.Tag);
        return tags;
    }

    /// <summary>The declared-vs-present match count: how many declared tags equal at
    /// least one element tag (the OQ-S2c-7 registration — a declaration's dead tags
    /// are visible as a zero, never a refusal).</summary>
    private static int CountListMatches(IReadOnlyList<uint> declared,
        HashSet<uint> present)
    {
        int matches = 0;
        for (int i = 0; i < declared.Count; i++)
            if (present.Contains(declared[i])) matches++;
        return matches;
    }

    private static bool ContainsTag(IReadOnlyList<uint> tags, uint tag)
    {
        for (int i = 0; i < tags.Count; i++)
            if (tags[i] == tag) return true;
        return false;
    }
}
