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
        /// input-ordinals-only, so count equality is membership equality). A no-op
        /// table short-circuits to the undeclared decision: retain copies raw,
        /// compact rides the S2b whole-table rewrite, and the output stays
        /// byte-identical to the same request without a declaration (§4.2).</summary>
        internal bool ListsNoOp => SurvivingScripts.Count == InputScriptCount &&
            SurvivingFeatures.Count == InputFeatureCount &&
            SurvivingLookups.Count == InputLookupCount;

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
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The scope census row (OQ-S2c-10): the surviving element counts and
    /// the per-dimension declared-tag match counts, count level only.</summary>
    private static LayoutScopeCensusRow ScopeCensusRow(LayoutScopeResolution verdict) =>
        new(verdict.Tag, verdict.InputScriptCount, verdict.SurvivingScripts.Count,
            verdict.InputFeatureCount, verdict.SurvivingFeatures.Count,
            verdict.InputLookupCount, verdict.SurvivingLookups.Count,
            verdict.DeclaredScriptMatches, verdict.DeclaredLanguageMatches,
            verdict.DeclaredFeatureMatches);

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
        for (int s = 0; s < table.Scripts.Length; s++)
        {
            LayoutScript script = table.Scripts[s];
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
