using System.Runtime.InteropServices;
using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Operations.Subset;

public static partial class SubsetOperation
{
    private sealed class CompactLayoutModels
    {
        internal GdefTable? Gdef;
        internal LayoutListsTable? Gsub;
        internal LayoutListsTable? Gpos;
        internal JstfTable? Jstf;
    }

    private static ReadStatus PruneLayoutGraph(PrepareState state,
        ref byte[]? gsubBytes, ref byte[]? gposBytes, ref byte[]? jstfBytes,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (state.Mode != SubsetMode.CompactRemap || state.RewrittenLayoutModels is not { } models)
            return ReadStatus.Done;
        var map = state.Map!;
        var prune = new LayoutGraphPruner(state.Budget,
            LayoutOutputMap.Identity(map.SourceFace, map.OutputGlyphCount), state.GdefContext);
        LayoutListsTable? gsub = models.Gsub is { } gs ? prune.Layout(gs) : null;
        LayoutListsTable? gpos = models.Gpos is { } gp ? prune.Layout(gp) : null;
        JstfTable? jstf = models.Jstf is { } js ? prune.Jstf(js) : null;
        if (prune.Status != ReadStatus.Done)
        {
            diagnostic = prune.Diagnostic;
            return prune.Status;
        }
        byte[]? nextGsub = gsubBytes, nextGpos = gposBytes, nextJstf = jstfBytes;
        ReadStatus status;
        if (gsub is not null && !ReferenceEquals(gsub, models.Gsub))
        {
            status = GsubTableCompiler.TryCompile(gsub, state.GdefContext,
                out nextGsub, out diagnostic, state.Budget);
            if (status != ReadStatus.Done) return status;
        }
        if (gpos is not null && !ReferenceEquals(gpos, models.Gpos))
        {
            status = GposTableCompiler.TryCompile(gpos, state.GdefContext,
                out nextGpos, out diagnostic, state.Budget);
            if (status != ReadStatus.Done) return status;
        }
        if (jstf is not null && !ReferenceEquals(jstf, models.Jstf))
        {
            status = JstfTableCompiler.TryCompile(jstf, out nextJstf,
                out diagnostic, state.Budget);
            if (status != ReadStatus.Done) return status;
        }
        models.Gsub = gsub;
        models.Gpos = gpos;
        models.Jstf = jstf;
        gsubBytes = nextGsub;
        gposBytes = nextGpos;
        jstfBytes = nextJstf;
        if (state.LayoutRewriteCensus is { } census)
            for (int i = 0; i < census.Count; i++)
            {
                var row = census[i];
                var (bytes, count) = row.Tag switch
                {
                    Gsub when gsub is not null => (nextGsub, LookupSubtables(gsub.Lookups)),
                    Gpos when gpos is not null => (nextGpos, LookupSubtables(gpos.Lookups)),
                    Jstf when jstf is not null => (nextJstf, JstfPriorityCount(jstf)),
                    _ => (null, 0)
                };
                if (bytes is not null)
                    census[i] = row with
                    {
                        OutputSubtableCount = count,
                        OutputBytes = bytes.Length,
                        CountsInvarianceHeld = row.InputSubtableCount == count
                    };
            }
        return ReadStatus.Done;
    }

    private sealed class LayoutGraphPruner(BuildBudget budget, LayoutOutputMap glyphMap,
        GdefLookupContext? context)
    {
        private readonly Dictionary<object, object> _cache = new(ReferenceEqualityComparer.Instance);
        private LayoutLookupRemap? _gsubMap;
        private LayoutLookupRemap? _gposMap;
        internal ReadStatus Status = ReadStatus.Done;
        internal FontDiagnostic Diagnostic;

        private bool Reserve(int count, int bytesPerElement, DataLocation at)
        {
            if (Status != ReadStatus.Done) return false;
            if (budget.Work(count, at, out Diagnostic) &&
                budget.Buffer((long)count * bytesPerElement, at, out Diagnostic)) return true;
            Status = ReadStatus.ResourceLimit;
            return false;
        }

        internal LayoutListsTable Layout(LayoutListsTable source)
        {
            if (!Reserve(source.Lookups.Length, 20, source.Location)) return source;
            var map = new int[source.Lookups.Length];
            var lookups = new List<LookupTable>(map.Length);
            bool changed = false;
            for (int i = 0; i < map.Length; i++)
            {
                var original = source.Lookups[i];
                var lookup = FilterSubtables(original);
                if (Status != ReadStatus.Done) return source;
                changed |= !ReferenceEquals(lookup, original);
                if (lookup.SubtableCount == 0)
                {
                    map[i] = -1;
                    changed = true;
                }
                else
                {
                    map[i] = lookups.Count;
                    lookups.Add(lookup);
                }
            }
            var remap = new LayoutLookupRemap
            {
                OldToNew = map, NewCount = lookups.Count, DropUnmatchableTargets = true
            };
            if (source.Kind == LayoutLookupKind.Gsub) _gsubMap = remap;
            else _gposMap = remap;
            if (!changed) return source;
            if (lookups.Count != map.Length)
                for (int i = 0; i < lookups.Count; i++)
                {
                    var lookup = lookups[i];
                    bool hasRecords = source.Kind == LayoutLookupKind.Gsub
                        ? lookup.LookupType is 5 or 6 or 7
                        : lookup.LookupType is 7 or 8 or 9;
                    if (!hasRecords) continue;
                    Status = RewriteLookup(lookup, glyphMap, source.Kind, context, budget,
                        out var rewritten, out Diagnostic, remap);
                    if (Status != ReadStatus.Done) return source;
                    lookups[i] = rewritten!;
                }
            if (!Reserve(source.Features.Length, 8, source.Location)) return source;
            var features = new LayoutFeature[source.Features.Length];
            for (int i = 0; i < features.Length; i++) features[i] = Feature(source.Features[i], remap, false);
            var variations = source.FeatureVariations is { } fv ? Variations(fv, remap, features.Length) : null;
            if (Status != ReadStatus.Done) return source;
            Status = LayoutListsTableCompiler.TryCreate(source.Version, source.FeatureVariationsOffsetRaw,
                source.Scripts, features, CollectionsMarshal.AsSpan(lookups), source.NumGlyphs,
                source.Kind, out var rebuilt, out Diagnostic, budget, source.Location, variations);
            return Status == ReadStatus.Done ? rebuilt! : source;
        }

        private LookupTable FilterSubtables(LookupTable source)
        {
            if (Status != ReadStatus.Done) return source;
            if (source.Kind == LayoutLookupKind.Gsub)
                return source.LookupType switch
                {
                    1 => Keep(source, source.SingleSubstitutions!, static leaf => leaf.Coverage.Glyphs.Length != 0, source.WithDecodedSubtables),
                    2 => Keep(source, source.Multiples!, static leaf => leaf.Coverage.Glyphs.Length != 0, source.WithDecodedMultiples),
                    3 => Keep(source, source.Alternates!, static leaf => leaf.Coverage.Glyphs.Length != 0, source.WithDecodedAlternates),
                    4 => Keep(source, source.Ligatures!, MayMatch, source.WithDecodedLigatures),
                    5 => Keep(source, source.Contextuals!, MayMatch, source.WithDecodedContextuals),
                    6 => Keep(source, source.Chainings!, MayMatch, source.WithDecodedChainings),
                    7 => Keep(source, source.Extensions!, MayMatch, source.WithDecodedExtensions),
                    8 => Keep(source, source.ReverseChains!, MayMatch, source.WithDecodedReverseChains),
                    _ => Unsupported(source)
                };
            return source.LookupType switch
            {
                1 => Keep(source, source.SinglePositions!, static leaf => leaf.Coverage.Glyphs.Length != 0, source.WithDecodedPositions),
                2 => Keep(source, source.PairPositions!, MayMatch, source.WithDecodedPairPositions),
                3 => Keep(source, source.CursiveAttachments!, static leaf => leaf.Coverage.Glyphs.Length != 0, source.WithDecodedCursive),
                4 => Keep(source, source.MarkBases!, static leaf => leaf.MarkCoverage.Glyphs.Length != 0 && leaf.BaseCoverage.Glyphs.Length != 0, source.WithDecodedMarkBases),
                5 => Keep(source, source.MarkLigs!, static leaf => leaf.MarkCoverage.Glyphs.Length != 0 && leaf.LigatureCoverage.Glyphs.Length != 0, source.WithDecodedMarkLigs),
                6 => Keep(source, source.MarkMarks!, static leaf => leaf.MarkCoverage.Glyphs.Length != 0 && leaf.Mark2Coverage.Glyphs.Length != 0, source.WithDecodedMarkMarks),
                7 => Keep(source, source.Contextuals!, MayMatch, source.WithDecodedContextuals),
                8 => Keep(source, source.Chainings!, MayMatch, source.WithDecodedChainings),
                9 => Keep(source, source.ExtensionPositions!, MayMatch, source.WithDecodedExtensionPositions),
                _ => Unsupported(source)
            };
        }

        private LookupTable Unsupported(LookupTable source)
        {
            Status = UnsupportedLookupType(source, out Diagnostic);
            return source;
        }

        private LookupTable Keep<T>(LookupTable source, IReadOnlyList<T> leaves,
            Func<T, bool> mayMatch, Func<T[], LookupTable> rebuild)
        {
            if (!Reserve(leaves.Count, 16, source.Location)) return source;
            var kept = new List<T>(leaves.Count);
            foreach (var leaf in leaves)
            {
                if (mayMatch(leaf)) kept.Add(leaf);
                if (Status != ReadStatus.Done) return source;
            }
            return kept.Count == leaves.Count ? source : rebuild(kept.ToArray());
        }

        private bool MayMatch(LigatureSubstTable source)
        {
            if (source.Coverage.Glyphs.Length == 0 || !Reserve(source.SetCount, 0, source.Location)) return false;
            for (int i = 0; i < source.SetCount; i++) if (source.GetSet(i).LigatureCount != 0) return true;
            return false;
        }

        private bool MayMatch(PairPosTable source)
        {
            if (source.Coverage.Glyphs.Length == 0) return false;
            if (source.Format == 2) return true;
            if (!Reserve(source.PairSets!.Count, 0, source.Location)) return false;
            foreach (var set in source.PairSets) if (set.Records.Length != 0) return true;
            return false;
        }

        private bool MayMatch(ContextualSubstTable source)
        {
            if (source.Format == 3) return RequiredCoverages(source.Coverages!, source.Location);
            if (source.Coverage.Glyphs.Length == 0 || !Reserve(source.RuleSets!.Count, 0, source.Location)) return false;
            foreach (var set in source.RuleSets) if (set is { RuleCount: > 0 }) return true;
            return false;
        }

        private bool MayMatch(ChainingSubstTable source)
        {
            if (source.Format == 3)
                return RequiredCoverages(source.InputCoverages!, source.Location) &&
                    RequiredCoverages(source.BacktrackCoverages!, source.Location) &&
                    RequiredCoverages(source.LookaheadCoverages!, source.Location);
            if (source.Coverage!.Glyphs.Length == 0 || !Reserve(source.RuleSets!.Count, 0, source.Location)) return false;
            foreach (var set in source.RuleSets) if (set is { RuleCount: > 0 }) return true;
            return false;
        }

        private bool MayMatch(ReverseChainSubstTable source) => source.Coverage.Glyphs.Length != 0 &&
            RequiredCoverages(source.BacktrackCoverages, source.Location) &&
            RequiredCoverages(source.LookaheadCoverages, source.Location);

        private bool RequiredCoverages(IReadOnlyList<CoverageTable> coverages, DataLocation at)
        {
            if (!Reserve(coverages.Count, 0, at)) return false;
            foreach (var coverage in coverages) if (coverage.Glyphs.Length == 0) return false;
            return true;
        }

        private bool MayMatch(ExtensionSubstTable source) => source.ExtensionLookupType switch
        {
            1 => source.Single!.Coverage.Glyphs.Length != 0,
            2 => source.Multiple!.Coverage.Glyphs.Length != 0,
            3 => source.Alternate!.Coverage.Glyphs.Length != 0,
            4 => MayMatch(source.Ligature!),
            5 => MayMatch(source.Contextual!),
            6 => MayMatch(source.Chaining!),
            8 => MayMatch(source.ReverseChain!),
            _ => throw new InvalidOperationException("Unsupported decoded GSUB extension.")
        };

        private bool MayMatch(ExtensionPosTable source) => source.ExtensionLookupType switch
        {
            1 => source.Single!.Coverage.Glyphs.Length != 0,
            2 => MayMatch(source.Pair!),
            3 => source.Cursive!.Coverage.Glyphs.Length != 0,
            4 => source.MarkBase!.MarkCoverage.Glyphs.Length != 0 && source.MarkBase.BaseCoverage.Glyphs.Length != 0,
            5 => source.MarkLig!.MarkCoverage.Glyphs.Length != 0 && source.MarkLig.LigatureCoverage.Glyphs.Length != 0,
            6 => source.MarkMark!.MarkCoverage.Glyphs.Length != 0 && source.MarkMark.Mark2Coverage.Glyphs.Length != 0,
            7 => MayMatch(source.Contextual!),
            8 => MayMatch(source.Chaining!),
            _ => throw new InvalidOperationException("Unsupported decoded GPOS extension.")
        };

        private ushort[] MapIndices(ReadOnlySpan<ushort> source, LayoutLookupRemap map, DataLocation at)
        {
            if (!Reserve(source.Length, 4, at)) return [];
            var indices = new ushort[source.Length];
            int count = 0;
            foreach (ushort old in source)
            {
                if (old >= map.OldToNew.Length)
                {
                    Status = ScopedRecordMiss(at, old, out Diagnostic);
                    return [];
                }
                int target = map.OldToNew[old];
                if (target == -1) continue;
                if (target < 0 || target >= map.NewCount)
                {
                    Status = ScopedRecordMiss(at, old, out Diagnostic);
                    return [];
                }
                indices[count++] = (ushort)target;
            }
            if (count != indices.Length) Array.Resize(ref indices, count);
            return indices;
        }

        private LayoutFeature Feature(LayoutFeature source, LayoutLookupRemap map, bool alternate)
        {
            var indices = MapIndices(source.LookupIndices, map, source.Location);
            if (Status != ReadStatus.Done || source.LookupIndices.SequenceEqual(indices)) return source;
            LayoutFeature? rebuilt;
            Status = alternate
                ? FeatureVariationsTableCompiler.TryCreateAlternateFeature(source.ParamsOffsetRaw, indices,
                    map.NewCount, out rebuilt, out Diagnostic, budget, source.Location)
                : LayoutListsTableCompiler.TryCreateFeature(source.Tag, source.ParamsOffsetRaw, indices,
                    map.NewCount, out rebuilt, out Diagnostic, budget, source.Location, source.ParamsPayload);
            return Status == ReadStatus.Done ? rebuilt! : source;
        }

        private FeatureVariationsTable Variations(FeatureVariationsTable source, LayoutLookupRemap map, int featureCount)
        {
            if (!Reserve(source.RecordCount, 16, source.Location)) return source;
            var records = new FeatureVariationRecord[source.RecordCount];
            bool changed = false;
            for (int i = 0; i < records.Length; i++)
            {
                var record = source.Records[i];
                records[i] = record;
                if (record.Substitution is not { } substitution) continue;
                if (!Reserve(substitution.Records.Count, 16, substitution.Location)) return source;
                var entries = new (ushort FeatureIndex, LayoutFeature Alternate)[substitution.Records.Count];
                bool entryChanged = false;
                for (int j = 0; j < entries.Length; j++)
                {
                    var entry = substitution.Records[j];
                    var alternate = Feature(entry.Alternate, map, true);
                    entries[j] = (entry.FeatureIndex, alternate);
                    entryChanged |= !ReferenceEquals(entry.Alternate, alternate);
                }
                if (Status != ReadStatus.Done) return source;
                if (!entryChanged) continue;
                Status = FeatureVariationsTableCompiler.TryCreateFeatureTableSubst(entries, featureCount,
                    out var rebuilt, out Diagnostic, budget, substitution.Location);
                if (Status != ReadStatus.Done) return source;
                records[i] = new(record.ConditionSet, rebuilt);
                changed = true;
            }
            if (!changed) return source;
            Status = FeatureVariationsTableCompiler.TryCreate(records, out var variations,
                out Diagnostic, budget, source.Location);
            return Status == ReadStatus.Done ? variations! : source;
        }

        private T Cached<T>(T source, Func<T, T> visit, DataLocation at) where T : class
        {
            if (Status != ReadStatus.Done) return source;
            if (_cache.TryGetValue(source, out var cached)) return (T)cached;
            if (!Reserve(1, 32, at)) return source;
            T rebuilt = visit(source);
            _cache.Add(source, rebuilt);
            return rebuilt;
        }

        internal JstfTable Jstf(JstfTable source)
        {
            if (!Reserve(source.ScriptCount, 16, source.Location)) return source;
            var scripts = new JstfScriptRecord[source.ScriptCount];
            bool changed = false;
            for (int i = 0; i < scripts.Length; i++)
            {
                var record = source.Scripts[i];
                var script = Cached(record.Script, Script, record.Script.Location);
                scripts[i] = new(record.Tag, script);
                changed |= !ReferenceEquals(record.Script, script);
            }
            int gsubCount = _gsubMap?.NewCount ?? source.GsubLookupCount;
            int gposCount = _gposMap?.NewCount ?? source.GposLookupCount;
            if (Status != ReadStatus.Done || !changed &&
                gsubCount == source.GsubLookupCount && gposCount == source.GposLookupCount) return source;
            return new(source.Version, source.NumGlyphs, gsubCount, gposCount, source.Context,
                scripts, source.Location, 0);
        }

        private JstfScriptTable Script(JstfScriptTable source)
        {
            if (!Reserve(source.LangSysCount, 16, source.Location)) return source;
            var defaultLanguage = source.DefaultLangSys is { } lang ? Cached(lang, Language, lang.Location) : null;
            var languages = new JstfLangSysRecord[source.LangSysCount];
            bool changed = !ReferenceEquals(defaultLanguage, source.DefaultLangSys);
            for (int i = 0; i < languages.Length; i++)
            {
                var record = source.LangSys[i];
                var language = Cached(record.LangSys, Language, record.LangSys.Location);
                languages[i] = new(record.Tag, language);
                changed |= !ReferenceEquals(record.LangSys, language);
            }
            return changed ? new(source.ExtenderGlyphs, defaultLanguage, languages, source.Location, 0) : source;
        }

        private JstfLangSysTable Language(JstfLangSysTable source)
        {
            if (!Reserve(source.PriorityCount, 8, source.Location)) return source;
            var priorities = new JstfPriorityTable[source.PriorityCount];
            bool changed = false;
            for (int i = 0; i < priorities.Length; i++)
            {
                var priority = source.Priorities[i];
                priorities[i] = Cached(priority, Priority, priority.Location);
                changed |= !ReferenceEquals(priority, priorities[i]);
            }
            return changed ? new(priorities, source.Location, 0) : source;
        }

        private JstfPriorityTable Priority(JstfPriorityTable source)
        {
            var subShrinkEnable = Mod(source.GsubShrinkageEnable);
            var subShrinkDisable = Mod(source.GsubShrinkageDisable);
            var posShrinkEnable = Mod(source.GposShrinkageEnable);
            var posShrinkDisable = Mod(source.GposShrinkageDisable);
            var shrink = source.ShrinkageJstfMax is { } s ? Cached(s, Max, s.Location) : null;
            var subExtendEnable = Mod(source.GsubExtensionEnable);
            var subExtendDisable = Mod(source.GsubExtensionDisable);
            var posExtendEnable = Mod(source.GposExtensionEnable);
            var posExtendDisable = Mod(source.GposExtensionDisable);
            var extend = source.ExtensionJstfMax is { } e ? Cached(e, Max, e.Location) : null;
            if (ReferenceEquals(subShrinkEnable, source.GsubShrinkageEnable) &&
                ReferenceEquals(subShrinkDisable, source.GsubShrinkageDisable) &&
                ReferenceEquals(posShrinkEnable, source.GposShrinkageEnable) &&
                ReferenceEquals(posShrinkDisable, source.GposShrinkageDisable) &&
                ReferenceEquals(shrink, source.ShrinkageJstfMax) &&
                ReferenceEquals(subExtendEnable, source.GsubExtensionEnable) &&
                ReferenceEquals(subExtendDisable, source.GsubExtensionDisable) &&
                ReferenceEquals(posExtendEnable, source.GposExtensionEnable) &&
                ReferenceEquals(posExtendDisable, source.GposExtensionDisable) &&
                ReferenceEquals(extend, source.ExtensionJstfMax)) return source;
            return new(subShrinkEnable, subShrinkDisable, posShrinkEnable, posShrinkDisable,
                shrink, subExtendEnable, subExtendDisable, posExtendEnable, posExtendDisable,
                extend, source.Location, 0);
        }

        private JstfModListTable? Mod(JstfModListTable? source) =>
            source is null ? null : Cached(source, ModCore, source.Location);

        private JstfModListTable ModCore(JstfModListTable source)
        {
            var map = source.Domain == JstfLookupDomain.Gsub ? _gsubMap : _gposMap;
            if (map is null || !Reserve(source.LookupCount, 2, source.Location)) return source;
            var original = source.LookupIndices.ToArray();
            var indices = MapIndices(original, map, source.Location);
            return Status != ReadStatus.Done || original.AsSpan().SequenceEqual(indices)
                ? source : new(source.Domain, indices, source.Location, 0);
        }

        private JstfMaxTable Max(JstfMaxTable source)
        {
            if (!Reserve(source.LookupCount, 16, source.Location)) return source;
            var lookups = new List<LookupTable>(source.LookupCount);
            bool changed = false;
            foreach (var original in source.Lookups)
            {
                var lookup = FilterSubtables(original);
                if (Status != ReadStatus.Done) return source;
                changed |= !ReferenceEquals(original, lookup);
                if (lookup.SubtableCount == 0) changed = true;
                else lookups.Add(lookup);
            }
            return changed ? new(lookups.ToArray(), source.Location, 0) : source;
        }
    }
}
