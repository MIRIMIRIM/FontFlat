using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Operations.Subset;

/// <summary>Compact+PreserveLayout layout rewrite (S2b card §2/§3).
///
/// Under the caller's explicit PreserveLayout selection in compact-remap mode, the
/// admitted prefix (GDEF→GSUB→GPOS) rewrites in place over the frozen map: read =
/// the step 3.5 materialized models (never a second parse), rewrite = every GID-
/// bearing field re-answered through <see cref="GlyphRemap.OutputOf"/>, construct =
/// the existing leaf/root compilers over the output domain, emit = the existing
/// TryCompile faces. The closure (step 4.5) merged every wire reference into the
/// retained set before the map froze, so every remap here is total: a miss is an
/// orchestration bug and refuses under the code-23 contract (RF-S2b-2), never a
/// silent rule loss. The GDEF class domains shrink definitionally — a glyph the map
/// dropped drops its class entry, while surviving entries keep their class values
/// (the lookup flags' markAttachmentType contract reads class values, so no
/// renumbering, OQ-S2b-3). The output order on a TrueType face is the ascending
/// input GID (monotone, so remapped keys stay sorted); on a CFF1 face the ascending
/// SID/CID identity can permute, so every key-order wire constraint (coverage
/// entries, pair-set second glyphs, class-def ranges) re-groups over the sorted
/// output keys with the parallel payloads riding the same permutation — the kern
/// fmt0 canonical-sort precedent, a wire-equivalence tier member. Counts are
/// invariant by construction and the root compilers re-answer every flags/context
/// contract against the rebuilt GDEF context (the emission-side second defense).</summary>
public static partial class SubsetOperation
{
    /// <summary>The rewrite entry: GDEF first — the GSUB/GPOS lookup flags validate
    /// against its rebuilt lookup context — then the two layout lists (S2b card §2.2
    /// emission order). A tag with a Rebuild decision whose admission model is absent
    /// refuses at the producer boundary (the gvar/CFF2 producer shape): decide and
    /// admission disagree only on a pipeline bug.</summary>
    private static ReadStatus RewriteCompactLayout(PrepareState state, GlyphRemap map,
        HashSet<uint> rebuildTags, out byte[]? gdef, out byte[]? gsub, out byte[]? gpos,
        out FontDiagnostic diagnostic)
    {
        gdef = gsub = gpos = null;
        GdefLookupContext? context = null;
        ReadStatus status;
        if (rebuildTags.Contains(Gdef))
        {
            if (state.Gdef is not { } gdefModel)
                return RewriteProducerMissing(map.SourceFace.Location, Gdef,
                    out diagnostic);
            status = RewriteCompactGdef(gdefModel, map, state.Budget, out gdef,
                out context, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RewriteProducerMissing(DataLocation faceAt, uint tag,
        out FontDiagnostic diagnostic)
    {
        diagnostic = new(DiagnosticCode.UnsupportedOperation, faceAt with
        {
            TableTag = tag
        }, tag);
        return ReadStatus.UnsupportedFormat;
    }

    /// <summary>RF-S2b-2: a remap miss refuses under the code-23 contract with the
    /// offending input GID at the glyph field — the closure guarantees totality, so
    /// this face defends against enumerator/orchestration drift only.</summary>
    private static ReadStatus RewriteMiss(GlyphRemap map, uint tag, int inputGlyph,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.LayoutFamilyUnsupported,
            map.SourceFace.Location with
            {
                TableTag = tag,
                Field = DataField.GlyphId,
                RecordOrdinal = inputGlyph
            }, out diagnostic, unchecked((uint)inputGlyph));

    /// <summary>One coverage's keys remap into the output domain; the output keys sort
    /// ascending (the wire's strict order) and <paramref name="ordinalOf"/> carries the
    /// permutation — ordinalOf[newIndex] = source ordinal — so the caller can ride the
    /// parallel payloads (an identity permutation on the TrueType monotone order).</summary>
    private static ReadStatus RewriteCoverage(CoverageTable coverage, GlyphRemap map,
        BuildBudget budget, out CoverageTable? rewritten, out int[] ordinalOf,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ordinalOf = new int[coverage.Glyphs.Length];
        var keys = new ushort[coverage.Glyphs.Length];
        for (int i = 0; i < coverage.Glyphs.Length; i++)
        {
            int output = map.OutputOf(coverage.Glyphs[i]);
            if (output < 0)
                return RewriteMiss(map, coverage.Location.TableTag,
                    coverage.Glyphs[i], out diagnostic);
            keys[i] = (ushort)output;
            ordinalOf[i] = i;
        }
        // The frozen map is injective, so the keys are distinct and the sort is total.
        Array.Sort(keys, ordinalOf);
        return CoverageTableCompiler.TryCreate(keys, map.OutputGlyphCount,
            coverage.Format, out rewritten, out diagnostic, budget, coverage.Location);
    }

    /// <summary>GDEF (S2b card §2.2): the two class domains shrink per entry with
    /// their class values identical, the three coverage faces remap (attach list,
    /// lig caret list, every mark set), the item variation store re-emits its model
    /// unchanged (region/axis keyed, no GID domain), and the root re-emits through
    /// GdefTableCompiler with the version and head-shape differences preserved. The
    /// rebuilt lookup context re-roots over the rewritten children for the GSUB/GPOS
    /// flags validation (the observed header-slot offsets stay zero — the emission
    /// face reads them lazily, GdefLookupContext.TryCreate checks identity only).</summary>
    private static ReadStatus RewriteCompactGdef(GdefTable gdef, GlyphRemap map,
        BuildBudget budget, out byte[]? bytes, out GdefLookupContext? context,
        out FontDiagnostic diagnostic)
    {
        bytes = null;
        context = null;
        int numGlyphs = map.OutputGlyphCount;
        ReadStatus status = RewriteClassDef(gdef.GlyphClasses, map, budget,
            out var glyphClasses, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RewriteClassDef(gdef.MarkAttachmentClasses, map, budget,
            out var markAttachmentClasses, out diagnostic);
        if (status != ReadStatus.Done) return status;
        MarkGlyphSetsTable? markSets = null;
        if (gdef.MarkGlyphSets is { } originalSets)
        {
            // The sets' order is the markFilteringSet index domain — preserved
            // identically (the all-sets policy keeps set ordinals stable); each set's
            // members remap with their own sort.
            var sets = new List<CoverageTable>(originalSets.Sets.Count);
            foreach (var set in originalSets.Sets)
            {
                status = RewriteCoverage(set, map, budget, out var coverage,
                    out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                sets.Add(coverage!);
            }
            status = MarkGlyphSetsTableCompiler.TryCreate(sets, numGlyphs,
                out markSets, out diagnostic, budget, originalSets.Location);
            if (status != ReadStatus.Done) return status;
        }
        AttachListTable? attachList = null;
        if (gdef.AttachList is { } originalAttach)
        {
            status = RewriteCoverage(originalAttach.Coverage, map, budget,
                out var attachCoverage, out var ordinalOf, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // The remap is total on a coverage's members (the closure merged them),
            // so the contour-point lists ride their coverage ordinals through the
            // permutation unchanged.
            var points = new ushort[originalAttach.Count][];
            for (int i = 0; i < points.Length; i++)
                points[i] = originalAttach.Points(ordinalOf[i]).ToArray();
            attachList = new(attachCoverage!, points, numGlyphs,
                originalAttach.Location, 0, []);
        }
        LigCaretListTable? ligCaretList = null;
        if (gdef.LigCaretList is { } originalCaret)
        {
            status = RewriteCoverage(originalCaret.Coverage, map, budget,
                out var caretCoverage, out var ordinalOf, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var ligGlyphs = new LigGlyphTable[originalCaret.LigGlyphs.Count];
            for (int i = 0; i < ligGlyphs.Length; i++)
            {
                LigGlyphTable source = originalCaret.LigGlyphs[ordinalOf[i]];
                var carets = new CaretValueEntry[source.Count];
                for (int c = 0; c < carets.Length; c++) carets[c] = source[c];
                ligGlyphs[i] = new(carets, source.Location, 0);
            }
            ligCaretList = new(caretCoverage!, ligGlyphs, numGlyphs,
                originalCaret.Location, 0, []);
        }
        status = GdefTableCompiler.TryCreate(gdef.Version, numGlyphs, glyphClasses,
            markAttachmentClasses, markSets, attachList, ligCaretList,
            gdef.ItemVariationStore, out var rewritten, out diagnostic,
            budget.Limits, gdef.Location);
        if (status != ReadStatus.Done) return status;
        status = GdefTableCompiler.TryCompile(rewritten!, out bytes, out diagnostic,
            budget);
        if (status != ReadStatus.Done) return status;
        var classes = new GdefClassDefinitions(gdef.Version, numGlyphs, glyphClasses,
            markAttachmentClasses, 0, 0, 0, 0, gdef.Location);
        status = GdefLookupContext.TryCreate(classes, markSets,
            gdef.ItemVariationStore, out context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One class domain's per-entry remap (OQ-S2b-3): every mapped glyph
    /// keeps its class value, an unmapped glyph's entry drops (the definitional
    /// shrink — dropped glyphs were never closure members), and the survivors
    /// re-group into maximal runs over the sorted output keys. The emission format
    /// is the canonical smaller measure: format 1 only when the runs tile a dense
    /// window no wider than the sparse encoding (the reader's expanded-membership
    /// semantics make the two formats' class answers identical).</summary>
    private static ReadStatus RewriteClassDef(ClassDefTable? classDef, GlyphRemap map,
        BuildBudget budget, out ClassDefTable? rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = null;
        diagnostic = default;
        if (classDef is null) return ReadStatus.Done;
        var keys = new List<ushort>();
        var values = new List<ushort>();
        foreach (var range in classDef.Ranges)
        {
            for (int gid = range.StartGlyph; gid <= range.EndGlyph; gid++)
            {
                int output = map.OutputOf(gid);
                if (output < 0) continue; // the definitional entry drop
                keys.Add((ushort)output);
                values.Add(range.ClassValue);
            }
        }
        var keyArray = keys.ToArray();
        var valueArray = values.ToArray();
        Array.Sort(keyArray, valueArray); // distinct keys: a total sort
        var ranges = new List<ClassDefRange>(keyArray.Length);
        int at = 0;
        while (at < keyArray.Length)
        {
            int end = at;
            while (end + 1 < keyArray.Length && keyArray[end + 1] == keyArray[end] + 1 &&
                valueArray[end + 1] == valueArray[at])
                end++;
            ranges.Add(new ClassDefRange(keyArray[at], keyArray[end], valueArray[at]));
            at = end + 1;
        }
        bool tiles = true;
        for (int i = 1; i < ranges.Count && tiles; i++)
            tiles = ranges[i].StartGlyph == ranges[i - 1].EndGlyph + 1;
        int denseCount = ranges.Count == 0
            ? 0
            : ranges[^1].EndGlyph - ranges[0].StartGlyph + 1;
        int denseLength = ClassDefLayout.DenseValuesOffset +
            ClassDefLayout.ValueStride * denseCount;
        int sparseLength = ClassDefLayout.SparseRangesOffset +
            ClassDefLayout.RangeStride * ranges.Count;
        ushort format = tiles && denseLength <= sparseLength ? (ushort)1 : (ushort)2;
        return ClassDefTableCompiler.TryCreate(ranges.ToArray(), map.OutputGlyphCount,
            format, out rewritten, out diagnostic, budget, classDef.Location);
    }
}
