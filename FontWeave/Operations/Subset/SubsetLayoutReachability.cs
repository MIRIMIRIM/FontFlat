using FontWeave.Core;
using FontWeave.Tables;
using FontWeave.Tables.Layout;

namespace FontWeave.Operations.Subset;

public static partial class SubsetOperation
{
    private static ReadStatus CloseReachableLayout(PrepareState state,
        out FontDiagnostic diagnostic)
    {
        var budget = state.Budget;
        bool[] retained = state.Retained;
        DataLocation at = state.Gsub?.Location ?? state.Gpos?.Location ??
            state.Gdef?.Location ?? default;
        if (!budget.Buffer(4L * retained.Length, at, out diagnostic))
            return ReadStatus.ResourceLimit;
        var added = state.LayoutClosureAdded ?? [];
        var references = new LayoutReferenceSet(retained.Length);
        if (state.Jstf is { } jstf)
            CollectJstfReferences(jstf, references, state.JstfScope?.SurvivingScripts);
        if (state.Kern is { } kern) CollectKernReferences(kern, references);
        if (state.Math is { } math) CollectMathReferences(math, references);
        if (state.Base is { } baseTable) CollectBaseReferences(baseTable, references);
        MergeLayoutReferences(at, references, retained, budget, added, out diagnostic);
        if (diagnostic.Code != 0) return ReadStatus.ResourceLimit;

        if (state.LayoutReachability is null)
        {
            if (!budget.Buffer((2L * IntPtr.Size + 5) * retained.Length,
                    at, out diagnostic)) return ReadStatus.ResourceLimit;
            var graph = new LayoutReachabilityGraph(retained.Length);
            if (state.Gsub is { } gsub)
            {
                if (!budget.Work(gsub.Lookups.Length + 1, gsub.Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                if (state.GsubScope is { } scope)
                {
                    foreach (int index in scope.SurvivingLookups)
                        if (!IndexSubstitutionOutputs(gsub.Lookups[index], graph,
                                budget, out diagnostic))
                            return ReadStatus.ResourceLimit;
                }
                else
                {
                    foreach (var lookup in gsub.Lookups)
                        if (!IndexSubstitutionOutputs(lookup, graph, budget, out diagnostic))
                            return ReadStatus.ResourceLimit;
                }
            }
            state.LayoutReachability = graph;
        }
        if (!state.LayoutReachability.Drain(retained, added, state.LayoutCompositeModel,
                state.ClosureAdded ??= [], budget, at, out diagnostic)) return ReadStatus.ResourceLimit;
        added.Sort();
        state.ClosureAdded.Sort();
        state.LayoutClosureAdded = added;
        var census = new List<LayoutTableCensus>();
        if (state.Gdef is { } gdef)
        {
            references.Clear();
            CollectGdefReferences(gdef, references);
            CountSurvivingLayoutReferences(Gdef, references, retained, census);
        }
        if (state.Gsub is { } substitutions)
        {
            references.Clear();
            CollectLayoutListsReferences(substitutions, references, state.GsubScope?.SurvivingLookups);
            CountSurvivingLayoutReferences(Gsub, references, retained, census);
        }
        if (state.Gpos is { } positioning)
        {
            references.Clear();
            CollectLayoutListsReferences(positioning, references, state.GposScope?.SurvivingLookups);
            CountSurvivingLayoutReferences(Gpos, references, retained, census);
        }
        if (state.Jstf is { } justification)
        {
            references.Clear();
            CollectJstfReferences(justification, references, state.JstfScope?.SurvivingScripts);
            CountSurvivingLayoutReferences(Jstf, references, retained, census);
        }
        if (state.Kern is { } kerning)
        {
            references.Clear();
            CollectKernReferences(kerning, references);
            CountSurvivingLayoutReferences(Kern, references, retained, census);
        }
        if (state.Math is { } mathematics)
        {
            references.Clear();
            CollectMathReferences(mathematics, references);
            CountSurvivingLayoutReferences(Math, references, retained, census);
        }
        if (state.Base is { } baseline)
        {
            references.Clear();
            CollectBaseReferences(baseline, references);
            CountSurvivingLayoutReferences(Base, references, retained, census);
        }
        state.LayoutReferenceCensus = census;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void CountSurvivingLayoutReferences(uint tag,
        LayoutReferenceSet references, bool[] retained, List<LayoutTableCensus> census) =>
        census.Add(new(tag, references.Enumerate().Count(gid => retained[gid])));

    private static bool IndexSubstitutionOutputs(LookupTable lookup,
        LayoutReachabilityGraph graph, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        IEnumerable<object>? leaves = (IEnumerable<object>?)lookup.SingleSubstitutions
            ?? lookup.Multiples ?? (IEnumerable<object>?)lookup.Alternates
            ?? lookup.Ligatures ?? (IEnumerable<object>?)lookup.ReverseChains
            ?? lookup.Extensions;
        diagnostic = default;
        if (leaves is null) return true;
        foreach (var leaf in leaves)
            if (!IndexSubstitutionLeaf(leaf, graph, budget, out diagnostic))
                return false;
        return true;
    }

    private static bool IndexSubstitutionLeaf(object leaf, LayoutReachabilityGraph graph,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        switch (leaf)
        {
            case SingleSubstTable single:
                return IndexCorrespondingOutputs(single.Coverage, single.Substitutes,
                    graph, budget, out diagnostic);
            case ReverseChainSubstTable reverse:
                return IndexCorrespondingOutputs(reverse.Coverage, reverse.Substitutes,
                    graph, budget, out diagnostic);
            case MultipleSubstTable multiple:
                if (!budget.Work(multiple.SequenceCount, multiple.Location, out diagnostic)) return false;
                for (int i = 0; i < multiple.SequenceCount; i++)
                {
                    foreach (ushort output in multiple.GetSequence(i))
                        if (!graph.AddEdge(multiple.Coverage.Glyphs[i], output,
                                budget, multiple.Location, out diagnostic)) return false;
                }
                return true;
            case AlternateSubstTable alternate:
                if (!budget.Work(alternate.AlternateSetCount, alternate.Location, out diagnostic)) return false;
                for (int i = 0; i < alternate.AlternateSetCount; i++)
                {
                    foreach (ushort output in alternate.GetAlternateSet(i))
                        if (!graph.AddEdge(alternate.Coverage.Glyphs[i], output,
                                budget, alternate.Location, out diagnostic)) return false;
                }
                return true;
            case LigatureSubstTable ligatures:
                if (!budget.Work(ligatures.SetCount, ligatures.Location, out diagnostic)) return false;
                for (int i = 0; i < ligatures.SetCount; i++)
                {
                    foreach (var rule in ligatures.GetSet(i).Ligatures)
                    {
                        if (!graph.AddLigature(ligatures.Coverage.Glyphs[i], rule.Components,
                                rule.LigatureGlyph, budget, ligatures.Location, out diagnostic)) return false;
                    }
                }
                return true;
            case ExtensionSubstTable extension:
                object? body = extension.Single ?? (object?)extension.Multiple
                    ?? extension.Alternate ?? (object?)extension.Ligature
                    ?? extension.ReverseChain ?? (object?)extension.Contextual
                    ?? extension.Chaining;
                return IndexSubstitutionLeaf(body!, graph, budget, out diagnostic);
            case ContextualSubstTable:
            case ChainingSubstTable:
                return true;
            default:
                throw new InvalidOperationException("An admitted GSUB lookup has no typed substitution body.");
        }
    }

    private static bool IndexCorrespondingOutputs(CoverageTable coverage,
        ReadOnlySpan<ushort> outputs, LayoutReachabilityGraph graph, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        for (int i = 0; i < outputs.Length; i++)
            if (!graph.AddEdge(coverage.Glyphs[i], outputs[i], budget,
                    coverage.Location, out diagnostic)) return false;
        return true;
    }

    private sealed class LayoutReachabilityGraph(int glyphCount)
    {
        private readonly List<ushort>?[] _outputs = new List<ushort>?[glyphCount];
        private readonly List<PendingLigature>?[] _ligatures = new List<PendingLigature>?[glyphCount];
        private readonly bool[] _seen = new bool[glyphCount];
        private readonly Queue<int> _pending = new(glyphCount);

        private sealed class PendingLigature(ushort output, int remaining)
        {
            internal ushort Output { get; } = output;
            internal int Remaining = remaining;
        }

        internal bool AddEdge(ushort input, ushort output, BuildBudget budget,
            DataLocation at, out FontDiagnostic diagnostic)
        {
            if (!budget.Work(1, at, out diagnostic) || !budget.Buffer(2, at, out diagnostic))
                return false;
            (_outputs[input] ??= []).Add(output);
            return true;
        }

        internal bool AddLigature(ushort first, ReadOnlySpan<ushort> components,
            ushort output, BuildBudget budget, DataLocation at, out FontDiagnostic diagnostic)
        {
            int count = components.Length + 1;
            if (!budget.Work(count, at, out diagnostic) ||
                !budget.Buffer(16L + (long)IntPtr.Size * count, at, out diagnostic)) return false;
            var rule = new PendingLigature(output, count);
            (_ligatures[first] ??= []).Add(rule);
            foreach (ushort component in components)
                (_ligatures[component] ??= []).Add(rule);
            return true;
        }

        internal bool Drain(bool[] retained, List<ushort> added, GlyfTable? glyf,
            List<ushort> componentAdditions, BuildBudget budget, DataLocation at,
            out FontDiagnostic diagnostic)
        {
            if (!budget.Work(retained.Length, at, out diagnostic)) return false;
            for (int gid = 0; gid < retained.Length; gid++)
                if (retained[gid] && !_seen[gid])
                {
                    _seen[gid] = true;
                    _pending.Enqueue(gid);
                }
            while (_pending.Count != 0)
            {
                int gid = _pending.Dequeue();
                if (_outputs[gid] is { } outputs)
                {
                    if (!budget.Work(outputs.Count, at, out diagnostic)) return false;
                    foreach (ushort output in outputs) Retain(output, retained, added);
                }
                if (_ligatures[gid] is { } ligatures)
                {
                    if (!budget.Work(ligatures.Count, at, out diagnostic)) return false;
                    foreach (var rule in ligatures)
                        if (--rule.Remaining == 0) Retain(rule.Output, retained, added);
                }
                if (glyf?.CompositeOf(gid) is { } composite)
                {
                    if (!budget.Work(composite.ComponentCount, glyf.Location, out diagnostic)) return false;
                    for (int i = 0; i < composite.ComponentCount; i++)
                        Retain(composite[i].GlyphIndex, retained, componentAdditions);
                }
            }
            diagnostic = default;
            return true;
        }

        private void Retain(ushort output, bool[] retained, List<ushort> added)
        {
            if (retained[output]) return;
            retained[output] = true;
            added.Add(output);
            _seen[output] = true;
            _pending.Enqueue(output);
        }
    }
}
