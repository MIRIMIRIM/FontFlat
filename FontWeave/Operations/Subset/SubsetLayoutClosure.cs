using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;
using FontWeave.Tables.Layout;

namespace FontWeave.Operations.Subset;

/// <summary>Layout admission and the conservative layout-reference closure (S2a card
/// §2).
///
/// Under the caller's explicit PreserveLayout selection (retain-GIDs mode only), every
/// present layout-family table is admitted through its existing whole-table typed read
/// chain in the fixed GDEF→GSUB→GPOS (→JSTF→kern→MATH→BASE) dependency order, and the
/// one materialized model per table parks on the PrepareState — the closure and the
/// post-closure verification consume the same objects, never a second parse (the S1c/SV
/// model-residency precedent). The closure input is the whole table (all scripts,
/// languages, features, lookups — OQ-S2a-2 lean), which reduces the multi-input
/// hypergraph of 05 §6 to a single-pass reference union: with every rule retained, the
/// referenced-glyph function no longer depends on the retained set, so the fixed point
/// is "seed ∪ {0} ∪ L" and the existing composite closure finishes the job. The
/// conservative direction is stated once here: every referenced glyph joins the
/// retained set — retaining extra glyphs is the defining feature of retain-GIDs mode,
/// never a defect of this slice (S2a card §2.2).
///
/// Refusal faces (zero new codes; code 23 carries every unenumerable form, OQ-S2a-5):
/// a whole-table read-chain failure propagates its own diagnostic (RF-A), a lookup
/// that stays metadata-only after the deep read refuses with the FontInspection
/// posture's formal typed refusal re-keyed under code 23 (RF-B), and the post-closure
/// verification re-walks the same enumerators against the merged bitmap, refusing a
/// reference that escaped the merge (an orchestration-error guard — it reduces
/// "closure correct" to "enumerator correct", S2a card §3 layer 1).</summary>
public static partial class SubsetOperation
{
    // ---- step 3.5: admission (fixed dependency order, one model per table) ----

    private static ReadStatus AdmitLayoutFamily(SfntDirectoryView directory,
        ProfileScan profile, MaxpTable maxp, PrepareState state,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        var budget = state.Budget;
        int axisCount = state.Fvar?.AxisCount ?? 0;
        // GDEF first: the GSUB/GPOS (and JSTF) typed reads consume its lookup context,
        // the FontInspection dependency-order precedent (FontInspection.cs:682-693).
        if (profile.HasGdef)
        {
            ReadStatus status = directory.TryGetTableBytes(profile.GdefOrdinal,
                out var gdefBytes, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = GdefView.TryOpen(gdefBytes, out var gdefView, out diagnostic,
                directory.TableLocation(profile.GdefOrdinal));
            if (status != ReadStatus.Done) return status;
            status = gdefView.TryReadTable(maxp.NumGlyphs, axisCount, out var gdef,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            // The lookup context mirrors FontInspection's GDEF builder: the mark-set
            // slot offset is the actual child's table-coordinate position, not a
            // re-read of the generated header (FontInspection.cs:661-679).
            ushort markSetsOffset = 0;
            if (gdef!.MarkGlyphSets is not null)
            {
                DataLocation child = gdef.MarkGlyphSets.Location;
                if (child.Offset < gdef.Location.Offset)
                    return ReadWire.Fail(ReadStatus.InvalidFormat,
                        DiagnosticCode.InvalidValue, child, out diagnostic,
                        child.Offset, gdef.Location.Offset);
                ulong relative = child.Offset - gdef.Location.Offset;
                if (relative > ushort.MaxValue)
                    return ReadWire.Fail(ReadStatus.InvalidFormat,
                        DiagnosticCode.InvalidValue, child, out diagnostic,
                        relative, (ulong)ushort.MaxValue);
                markSetsOffset = checked((ushort)relative);
            }
            var classes = new GdefClassDefinitions(gdef.Version, gdef.NumGlyphs,
                gdef.GlyphClasses, gdef.MarkAttachmentClasses, gdefView.AttachListOffset,
                gdefView.LigCaretListOffset, markSetsOffset,
                gdef.ItemVariationStoreOffset, gdef.Location);
            status = GdefLookupContext.TryCreate(classes, gdef.MarkGlyphSets,
                gdef.ItemVariationStore, out var context, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            state.Gdef = gdef;
            state.GdefContext = context;
        }
        if (profile.HasGsub)
        {
            ReadStatus status = AdmitLayoutLists(directory, profile.GsubOrdinal,
                maxp.NumGlyphs, LayoutLookupKind.Gsub, state.GdefContext, state, budget,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (profile.HasGpos)
        {
            ReadStatus status = AdmitLayoutLists(directory, profile.GposOrdinal,
                maxp.NumGlyphs, LayoutLookupKind.Gpos, state.GdefContext, state, budget,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus AdmitLayoutLists(SfntDirectoryView directory, int ordinal,
        int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        PrepareState state, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = directory.TryGetTableBytes(ordinal, out var bytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutListsView.TryOpen(bytes, out var view, out diagnostic,
            directory.TableLocation(ordinal));
        if (status != ReadStatus.Done) return status;
        status = view.TryReadTable(numGlyphs, kind, context, out var model,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // RF-B (S2a card §5): a lookup that stayed metadata-only after the deep read
        // has no enumerable reference set, so the table cannot be closed. The refusal
        // re-runs the formal typed refusal for the exact bounded provenance (the
        // FontInspection metadata-only posture, FontInspection.cs:701-725) and then
        // fails the operation under the code-23 contract with the lookup ordinal.
        for (int i = 0; i < model!.Lookups.Length; i++)
        {
            if (HasTypedLayoutBody(model.Lookups[i])) continue;
            status = view.TryOpenLookupList(kind, out var list, out _, out diagnostic);
            if (status == ReadStatus.Done)
            {
                status = list.TryOpenLookup(kind, i, out var leaf, out _, out diagnostic);
                if (status == ReadStatus.Done)
                    status = leaf.Validate(numGlyphs, kind, context,
                        model.Lookups.Length, out diagnostic, budget);
            }
            state.Diagnostic = new(DiagnosticCode.LayoutFamilyUnsupported,
                model.Lookups[i].Location, (ulong)i);
            diagnostic = state.Diagnostic;
            return status == ReadStatus.Done ? ReadStatus.UnsupportedFormat : status;
        }
        if (kind == LayoutLookupKind.Gsub) state.Gsub = model;
        else state.Gpos = model;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool HasTypedLayoutBody(LookupTable lookup) =>
        lookup.SingleSubstitutions is not null || lookup.SinglePositions is not null ||
        lookup.Multiples is not null || lookup.Alternates is not null ||
        lookup.Ligatures is not null || lookup.Extensions is not null ||
        lookup.PairPositions is not null || lookup.CursiveAttachments is not null ||
        lookup.Contextuals is not null || lookup.Chainings is not null ||
        lookup.ReverseChains is not null || lookup.MarkBases is not null ||
        lookup.MarkLigs is not null || lookup.MarkMarks is not null ||
        lookup.ExtensionPositions is not null;

    /// <summary>Test bridge for the per-lookup enumerator (the one collection shape
    /// the merge and the verification share).</summary>
    internal static void CollectLookupReferencesForTests(LookupTable lookup,
        int numGlyphs, HashSet<int> into) =>
        CollectLookupReferences(lookup, numGlyphs, into);

    // ---- step 4.5: the single-pass reference union and its verification ----

    private static ReadStatus CloseLayoutReferences(PrepareState state,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        var budget = state.Budget;
        bool[] retained = state.Retained;
        DataLocation at = state.Gsub?.Location ?? state.Gpos?.Location ??
            state.Gdef?.Location ?? default;
        if (!budget.Buffer(4L * retained.Length, at, out diagnostic) ||
            !budget.Work(1, at, out diagnostic))
            return ReadStatus.ResourceLimit;
        List<ushort> added = [];
        List<LayoutTableCensus> census = [];
        if (state.Gdef is { } gdef)
        {
            var refs = new HashSet<int>();
            CollectGdefReferences(gdef, refs);
            census.Add(new(Gdef, refs.Count));
            MergeLayoutReferences(gdef.Location, refs, retained, budget, added,
                out diagnostic);
            if (diagnostic.Code != 0) return ReadStatus.ResourceLimit;
        }
        if (state.Gsub is { } gsub)
        {
            var refs = new HashSet<int>();
            CollectLayoutListsReferences(gsub, refs);
            census.Add(new(Gsub, refs.Count));
            MergeLayoutReferences(gsub.Location, refs, retained, budget, added,
                out diagnostic);
            if (diagnostic.Code != 0) return ReadStatus.ResourceLimit;
        }
        if (state.Gpos is { } gpos)
        {
            var refs = new HashSet<int>();
            CollectLayoutListsReferences(gpos, refs);
            census.Add(new(Gpos, refs.Count));
            MergeLayoutReferences(gpos.Location, refs, retained, budget, added,
                out diagnostic);
            if (diagnostic.Code != 0) return ReadStatus.ResourceLimit;
        }
        // The product-side post-closure assertion (S2a card §3 layer 1): every
        // retained layout table's reference set must sit inside the merged bitmap.
        // It shares the enumerators with the merge, so it defends against
        // orchestration errors, not enumerator omissions.
        if (!VerifyLayoutClosureRetained(state.Gdef, state.Gsub, state.Gpos,
                retained, out diagnostic))
            return ReadStatus.UnsupportedFormat;
        added.Sort();
        state.LayoutClosureAdded = added;
        state.LayoutReferenceCensus = census;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The verification entry shared by the pipeline and the tests: every
    /// reference the retained layout tables enumerate must sit inside the merged
    /// bitmap; the first escape names its GID under the code-23 contract.</summary>
    internal static bool VerifyLayoutClosureRetained(GdefTable? gdef,
        LayoutListsTable? gsub, LayoutListsTable? gpos, bool[] retained,
        out FontDiagnostic diagnostic)
    {
        if (gdef is not null)
        {
            var refs = new HashSet<int>();
            CollectGdefReferences(gdef, refs);
            if (!AllRetained(refs, retained, gdef.Location, out diagnostic))
                return false;
        }
        if (gsub is not null)
        {
            var refs = new HashSet<int>();
            CollectLayoutListsReferences(gsub, refs);
            if (!AllRetained(refs, retained, gsub.Location, out diagnostic))
                return false;
        }
        if (gpos is not null)
        {
            var refs = new HashSet<int>();
            CollectLayoutListsReferences(gpos, refs);
            if (!AllRetained(refs, retained, gpos.Location, out diagnostic))
                return false;
        }
        diagnostic = default;
        return true;
    }

    private static void MergeLayoutReferences(DataLocation at, HashSet<int> refs,
        bool[] retained, BuildBudget budget, List<ushort> added,
        out FontDiagnostic diagnostic)
    {
        foreach (int gid in refs)
        {
            if (retained[gid]) continue;
            if (!budget.Work(1, at.At(DataField.GlyphId, 0, gid), out diagnostic))
                return;
            retained[gid] = true;
            added.Add((ushort)gid);
        }
        diagnostic = default;
    }

    private static bool AllRetained(HashSet<int> refs, bool[] retained,
        DataLocation at, out FontDiagnostic diagnostic)
    {
        foreach (int gid in refs)
        {
            if (retained[gid]) continue;
            diagnostic = new(DiagnosticCode.LayoutFamilyUnsupported,
                at.At(DataField.GlyphId, 0, gid), unchecked((uint)gid));
            return false;
        }
        diagnostic = default;
        return true;
    }

    // ---- the reference enumerators (S2a card §2.3/§2.4; the one enumeration shape
    // shared by the merge and the verification) ----

    private static void CollectGdefReferences(GdefTable gdef, HashSet<int> into)
    {
        // OQ-S2a-7: the two ClassDefs are classification metadata, not semantic
        // references — the shaping positions of retained text are fully covered by
        // the lookups' coverage/context references, and merging class domains would
        // degenerate to the full glyph domain through class 0. The three coverage
        // faces (attach list, lig caret list, every mark set — the all-sets policy,
        // since any set may back some lookup's UseMarkFilteringSet) are real GID
        // references and merge.
        AddCoverage(gdef.AttachList?.Coverage, into);
        AddCoverage(gdef.LigCaretList?.Coverage, into);
        if (gdef.MarkGlyphSets is { } sets)
            foreach (var set in sets.Sets)
                AddCoverage(set, into);
    }

    private static void CollectLayoutListsReferences(LayoutListsTable table,
        HashSet<int> into)
    {
        foreach (var lookup in table.Lookups)
            CollectLookupReferences(lookup, table.NumGlyphs, into);
    }

    private static void CollectLookupReferences(LookupTable lookup, int numGlyphs,
        HashSet<int> into)
    {
        if (lookup.SingleSubstitutions is { } singles)
        {
            foreach (var sub in singles)
            {
                AddCoverage(sub.Coverage, into);
                AddGlyphs(sub.Substitutes, into);
            }
        }
        else if (lookup.Multiples is { } multiples)
        {
            foreach (var sub in multiples)
            {
                AddCoverage(sub.Coverage, into);
                for (int i = 0; i < sub.SequenceCount; i++)
                    AddGlyphs(sub.GetSequence(i), into);
            }
        }
        else if (lookup.Alternates is { } alternates)
        {
            foreach (var sub in alternates)
            {
                AddCoverage(sub.Coverage, into);
                for (int i = 0; i < sub.AlternateSetCount; i++)
                    AddGlyphs(sub.GetAlternateSet(i), into);
            }
        }
        else if (lookup.Ligatures is { } ligatures)
        {
            foreach (var sub in ligatures)
            {
                AddCoverage(sub.Coverage, into);
                for (int i = 0; i < sub.SetCount; i++)
                {
                    foreach (var ligature in sub.GetSet(i).Ligatures)
                    {
                        into.Add(ligature.LigatureGlyph);
                        AddGlyphs(ligature.Components, into);
                    }
                }
            }
        }
        else if (lookup.Contextuals is { } contextuals)
        {
            foreach (var sub in contextuals)
                CollectContextualReferences(sub, numGlyphs, into);
        }
        else if (lookup.Chainings is { } chainings)
        {
            foreach (var sub in chainings)
                CollectChainingReferences(sub, numGlyphs, into);
        }
        else if (lookup.ReverseChains is { } reverseChains)
        {
            foreach (var sub in reverseChains)
            {
                AddCoverage(sub.Coverage, into);
                foreach (var coverage in sub.BacktrackCoverages)
                    AddCoverage(coverage, into);
                foreach (var coverage in sub.LookaheadCoverages)
                    AddCoverage(coverage, into);
                AddGlyphs(sub.Substitutes, into);
            }
        }
        else if (lookup.Extensions is { } extensions)
        {
            foreach (var extension in extensions)
                CollectExtensionReferences(extension, numGlyphs, into);
        }
        else if (lookup.SinglePositions is { } singlePositions)
        {
            foreach (var sub in singlePositions)
                AddCoverage(sub.Coverage, into);
        }
        else if (lookup.PairPositions is { } pairPositions)
        {
            foreach (var sub in pairPositions)
            {
                AddCoverage(sub.Coverage, into);
                if (sub.PairSets is { } pairSets)
                {
                    foreach (var pairSet in pairSets)
                        foreach (var record in pairSet.Records)
                            into.Add(record.SecondGlyph);
                }
                else
                {
                    // Format 2: the fully materialized class1×class2 matrix
                    // references every cell, and class 0 is the unassigned domain —
                    // the OQ-S2a-6 lean names the merge's upper bound as the full
                    // glyph domain, reported honestly through the census.
                    AddClassDomain(sub.ClassDef1, numGlyphs, into);
                    AddClassDomain(sub.ClassDef2, numGlyphs, into);
                }
            }
        }
        else if (lookup.CursiveAttachments is { } cursive)
        {
            foreach (var sub in cursive)
                AddCoverage(sub.Coverage, into);
        }
        else if (lookup.MarkBases is { } markBases)
        {
            foreach (var sub in markBases)
            {
                AddCoverage(sub.MarkCoverage, into);
                AddCoverage(sub.BaseCoverage, into);
            }
        }
        else if (lookup.MarkLigs is { } markLigs)
        {
            foreach (var sub in markLigs)
            {
                AddCoverage(sub.MarkCoverage, into);
                AddCoverage(sub.LigatureCoverage, into);
            }
        }
        else if (lookup.MarkMarks is { } markMarks)
        {
            foreach (var sub in markMarks)
            {
                AddCoverage(sub.MarkCoverage, into);
                AddCoverage(sub.Mark2Coverage, into);
            }
        }
        else if (lookup.ExtensionPositions is { } extensionPositions)
        {
            foreach (var extension in extensionPositions)
                CollectExtensionPosReferences(extension, numGlyphs, into);
        }
    }

    /// <summary>One-level GPOS type-9 extension unwrapping; GPOS 7/8 wrap the shared
    /// Contextuals/Chainings models (the OQ-T4-06-3 reuse), which re-enter their
    /// GSUB-shaped collectors.</summary>
    private static void CollectExtensionPosReferences(ExtensionPosTable extension,
        int numGlyphs, HashSet<int> into)
    {
        if (extension.Single is { } single)
            AddCoverage(single.Coverage, into);
        else if (extension.Pair is { } pair)
        {
            AddCoverage(pair.Coverage, into);
            if (pair.PairSets is { } pairSets)
                foreach (var pairSet in pairSets)
                    foreach (var record in pairSet.Records)
                        into.Add(record.SecondGlyph);
            else
            {
                AddClassDomain(pair.ClassDef1, numGlyphs, into);
                AddClassDomain(pair.ClassDef2, numGlyphs, into);
            }
        }
        else if (extension.Cursive is { } cursive)
            AddCoverage(cursive.Coverage, into);
        else if (extension.MarkBase is { } markBase)
        {
            AddCoverage(markBase.MarkCoverage, into);
            AddCoverage(markBase.BaseCoverage, into);
        }
        else if (extension.MarkLig is { } markLig)
        {
            AddCoverage(markLig.MarkCoverage, into);
            AddCoverage(markLig.LigatureCoverage, into);
        }
        else if (extension.MarkMark is { } markMark)
        {
            AddCoverage(markMark.MarkCoverage, into);
            AddCoverage(markMark.Mark2Coverage, into);
        }
        else if (extension.Contextual is { } contextual)
            CollectContextualReferences(contextual, numGlyphs, into);
        else if (extension.Chaining is { } chaining)
            CollectChainingReferences(chaining, numGlyphs, into);
    }

    /// <summary>One-level extension unwrapping (GSUB 7): the wrapped leaf re-enters the
    /// same per-type collection; a metadata-only wrapped body contributes nothing and
    /// has already refused at the admission segment (RF-B).</summary>
    private static void CollectExtensionReferences(ExtensionSubstTable extension,
        int numGlyphs, HashSet<int> into)
    {
        if (extension.Single is { } single)
        {
            AddCoverage(single.Coverage, into);
            AddGlyphs(single.Substitutes, into);
        }
        else if (extension.Multiple is { } multiple)
        {
            AddCoverage(multiple.Coverage, into);
            for (int i = 0; i < multiple.SequenceCount; i++)
                AddGlyphs(multiple.GetSequence(i), into);
        }
        else if (extension.Alternate is { } alternate)
        {
            AddCoverage(alternate.Coverage, into);
            for (int i = 0; i < alternate.AlternateSetCount; i++)
                AddGlyphs(alternate.GetAlternateSet(i), into);
        }
        else if (extension.Ligature is { } ligature)
        {
            AddCoverage(ligature.Coverage, into);
            for (int i = 0; i < ligature.SetCount; i++)
                foreach (var lig in ligature.GetSet(i).Ligatures)
                {
                    into.Add(lig.LigatureGlyph);
                    AddGlyphs(lig.Components, into);
                }
        }
        else if (extension.Contextual is { } contextual)
            CollectContextualReferences(contextual, numGlyphs, into);
        else if (extension.Chaining is { } chaining)
            CollectChainingReferences(chaining, numGlyphs, into);
        else if (extension.ReverseChain is { } reverseChain)
        {
            AddCoverage(reverseChain.Coverage, into);
            foreach (var coverage in reverseChain.BacktrackCoverages)
                AddCoverage(coverage, into);
            foreach (var coverage in reverseChain.LookaheadCoverages)
                AddCoverage(coverage, into);
            AddGlyphs(reverseChain.Substitutes, into);
        }
    }

    private static void CollectContextualReferences(ContextualSubstTable sub,
        int numGlyphs, HashSet<int> into)
    {
        if (sub.Format == 3)
        {
            if (sub.Coverages is { } coverages)
                foreach (var coverage in coverages)
                    AddCoverage(coverage, into);
            return;
        }
        AddCoverage(sub.Coverage, into);
        if (sub.ClassDef is { } classDef)
        {
            // Format 2: the rules' class values expand through the class definition;
            // a referenced 0-class cell covers the unassigned domain, so its merge
            // upper bound is the full glyph domain (OQ-S2a-6).
            if (sub.RuleSets is { } ruleSets)
                foreach (var ruleSet in ruleSets)
                {
                    if (ruleSet is null) continue;
                    foreach (var rule in ruleSet.Rules)
                        foreach (ushort classValue in rule.InputSequence)
                            AddClassValues(classDef, classValue, numGlyphs, into);
                }
        }
        else if (sub.RuleSets is { } ruleSets)
        {
            foreach (var ruleSet in ruleSets)
            {
                if (ruleSet is null) continue;
                foreach (var rule in ruleSet.Rules)
                    AddGlyphs(rule.InputSequence, into);
            }
        }
    }

    private static void CollectChainingReferences(ChainingSubstTable sub,
        int numGlyphs, HashSet<int> into)
    {
        if (sub.Format == 3)
        {
            if (sub.BacktrackCoverages is { } backtrack)
                foreach (var coverage in backtrack)
                    AddCoverage(coverage, into);
            if (sub.InputCoverages is { } input)
                foreach (var coverage in input)
                    AddCoverage(coverage, into);
            if (sub.LookaheadCoverages is { } lookahead)
                foreach (var coverage in lookahead)
                    AddCoverage(coverage, into);
            return;
        }
        AddCoverage(sub.Coverage, into);
        if (sub.RuleSets is not { } ruleSets) return;
        foreach (var ruleSet in ruleSets)
        {
            if (ruleSet is null) continue;
            foreach (var rule in ruleSet.Rules)
            {
                if (sub.BacktrackClassDef is { } backtrackDef)
                    foreach (ushort classValue in rule.BacktrackSequence)
                        AddClassValues(backtrackDef, classValue, numGlyphs, into);
                else
                    AddGlyphs(rule.BacktrackSequence, into);
                if (sub.InputClassDef is { } inputDef)
                    foreach (ushort classValue in rule.InputSequence)
                        AddClassValues(inputDef, classValue, numGlyphs, into);
                else
                    AddGlyphs(rule.InputSequence, into);
                if (sub.LookaheadClassDef is { } lookaheadDef)
                    foreach (ushort classValue in rule.LookaheadSequence)
                        AddClassValues(lookaheadDef, classValue, numGlyphs, into);
                else
                    AddGlyphs(rule.LookaheadSequence, into);
            }
        }
    }

    /// <summary>Expands one referenced class value through its class definition. The
    /// 0-class cell is the class definition's unassigned domain: the OQ-S2a-6 lean
    /// merges its upper bound (the full glyph domain) and the census reports the
    /// resulting breadth honestly.</summary>
    private static void AddClassValues(ClassDefTable classDef, ushort classValue,
        int numGlyphs, HashSet<int> into)
    {
        if (classValue == 0)
        {
            for (int gid = 0; gid < numGlyphs; gid++) into.Add(gid);
            return;
        }
        foreach (var range in classDef.Ranges)
        {
            if (range.ClassValue != classValue) continue;
            for (int gid = range.StartGlyph; gid <= range.EndGlyph; gid++)
                into.Add(gid);
        }
    }

    /// <summary>The format-2 pair-position class matrix references every class1×class2
    /// cell, so both class domains merge wholesale: class 0 drags the bound to the
    /// full glyph domain (OQ-S2a-6).</summary>
    private static void AddClassDomain(ClassDefTable? classDef, int numGlyphs,
        HashSet<int> into)
    {
        if (classDef is null) return;
        for (int gid = 0; gid < numGlyphs; gid++) into.Add(gid);
    }

    private static void AddCoverage(CoverageTable? coverage, HashSet<int> into)
    {
        if (coverage is null) return;
        foreach (ushort glyph in coverage.Glyphs) into.Add(glyph);
    }

    private static void AddGlyphs(ReadOnlySpan<ushort> glyphs, HashSet<int> into)
    {
        foreach (ushort glyph in glyphs) into.Add(glyph);
    }
}
