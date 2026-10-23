using System.Runtime.InteropServices;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Operations.Subset;

public static partial class SubsetOperation
{
    internal abstract class LayoutOutputMap
    {
        private sealed class FrozenMap : LayoutOutputMap
        {
            private readonly GlyphRemap _map;
            internal FrozenMap(GlyphRemap map) => _map = map;
            internal override int OutputOf(int inputGlyphId) =>
                _map.OutputOf(inputGlyphId);
            internal override int OutputGlyphCount => _map.OutputGlyphCount;
            internal override bool HasDroppedGlyphs =>
                _map.OutputGlyphCount != _map.InputGlyphCount;
            internal override FontFaceRef SourceFace => _map.SourceFace;
        }

        private sealed class IdentityMap : LayoutOutputMap
        {
            private readonly FontFaceRef _face;
            private readonly int _glyphCount;
            internal IdentityMap(FontFaceRef face, int glyphCount) =>
                (_face, _glyphCount) = (face, glyphCount);
            internal override int OutputOf(int inputGlyphId) => inputGlyphId;
            internal override int OutputGlyphCount => _glyphCount;
            internal override bool HasDroppedGlyphs => false;
            internal override FontFaceRef SourceFace => _face;
        }

        internal static LayoutOutputMap Frozen(GlyphRemap map) => new FrozenMap(map);
        internal static LayoutOutputMap Identity(FontFaceRef face, int numGlyphs) =>
            new IdentityMap(face, numGlyphs);

        internal abstract int OutputOf(int inputGlyphId);
        internal abstract int OutputGlyphCount { get; }
        internal abstract bool HasDroppedGlyphs { get; }
        internal abstract FontFaceRef SourceFace { get; }
    }

    /// <summary>The S2c record-remap face (card §5.1 step 2): the surviving lookup
    /// subset's old→new index map and the new lookup count, so a rewritten
    /// contextual/chaining rule re-answers its SequenceLookupRecords against the
    /// real new count (the TryCreateRule domain check's first fully-effective
    /// pass — the S2b form passed -1 = records unchanged). Null = the whole-table
    /// form: the record indices never move (the record-order invariant OQ-S2b-7
    /// rides).</summary>
    internal sealed class LayoutLookupRemap
    {
        internal required int[] OldToNew { get; init; }
        internal int NewCount { get; init; }
        internal bool DropUnmatchableTargets { get; init; }

        internal static LayoutLookupRemap Of(LayoutScopeResolution scope) => new()
        {
            OldToNew = scope.LookupNewIndex,
            NewCount = scope.SurvivingLookups.Count
        };
    }

    /// <summary>The rewrite entry: GDEF first — the GSUB/GPOS lookup flags validate
    /// against its rebuilt lookup context — then the two layout lists (S2b card §2.2
    /// emission order). A tag with a Rebuild decision whose admission model is absent
    /// refuses at the producer boundary (the gvar/CFF2 producer shape): decide and
    /// admission disagree only on a pipeline bug. Each rewritten tag's count-level
    /// in/out facts land in the census (OQ-S2b-10), in rewrite order.</summary>
    private static ReadStatus RewriteCompactLayout(PrepareState state, GlyphRemap map,
        HashSet<uint> rebuildTags, SfntDirectoryView directory,
        out byte[]? gdef, out byte[]? gsub, out byte[]? gpos,
        out FontDiagnostic diagnostic)
    {
        gdef = gsub = gpos = null;
        GdefLookupContext? context = null;
        ReadStatus status;
        // The seam (OQ-S2c-4): this compact window always rides the frozen map, so
        // the engine below sees the FrozenMap view — semantically identical to the
        // pre-S2c direct parameter (the S2b1/S2b2 gate re-certification).
        var outputMap = LayoutOutputMap.Frozen(map);
        // The shared census list (OQ-S2b-10, the merged emission order): the
        // GDEF/GSUB/GPOS rows append here and the kern/JSTF/MATH producers append
        // to the same list after it — one report face in rewrite order.
        var census = state.LayoutRewriteCensus ??= new List<LayoutRewriteCensusRow>(3);
        if (rebuildTags.Contains(Gdef))
        {
            if (state.Gdef is not { } gdefModel)
                return RewriteProducerMissing(map.SourceFace.Location, Gdef,
                    out diagnostic);
            status = RewriteCompactGdef(gdefModel, outputMap, state.Budget, out gdef,
                out context, out int gdefSubtables, out diagnostic, state.RewrittenLayoutModels);
            if (status != ReadStatus.Done) return status;
            // The merged-tree JSTF seam (S2b-2 card §2.5): the JSTF rebuild
            // validates and constructs against the POST-rewrite context, so the
            // rebuilt context re-parks on the state. The GSUB/GPOS stages below
            // consume the same local either way; the S2a retain-GIDs admission
            // context is untouched because this entry runs in the compact window
            // only.
            state.GdefContext = context;
            census.Add(RewriteCensusRow(directory, Gdef, ChildBlocks(gdefModel),
                gdefSubtables, gdef!.Length));
        }
        if (rebuildTags.Contains(Gsub))
        {
            if (state.Gsub is not { } gsubModel)
                return RewriteProducerMissing(map.SourceFace.Location, Gsub,
                    out diagnostic);
            // S2c (card §5.1): a resolution that truly cut the table re-emits the
            // surviving subset through the scoped cascade; a no-op verdict rides the
            // S2b whole-table rewrite unchanged (§4.2 — map-driven, no short circuit).
            status = state.GsubScope is { } gsubScope && !gsubScope.ListsNoOp
                ? RewriteScopedLayoutLists(gsubModel, outputMap, gsubScope,
                    LayoutLookupKind.Gsub, context, state.Budget, out gsub,
                    out int gsubSubtables, out diagnostic, state.RewrittenLayoutModels)
                : RewriteCompactLayoutLists(gsubModel, outputMap, LayoutLookupKind.Gsub,
                    context, state.Budget, out gsub, out gsubSubtables,
                    out diagnostic, state.RewrittenLayoutModels);
            if (status != ReadStatus.Done) return status;
            census.Add(RewriteCensusRow(directory, Gsub, LookupSubtables(gsubModel.Lookups),
                gsubSubtables, gsub!.Length));
        }
        if (rebuildTags.Contains(Gpos))
        {
            if (state.Gpos is not { } gposModel)
                return RewriteProducerMissing(map.SourceFace.Location, Gpos,
                    out diagnostic);
            status = state.GposScope is { } gposScope && !gposScope.ListsNoOp
                ? RewriteScopedLayoutLists(gposModel, outputMap, gposScope,
                    LayoutLookupKind.Gpos, context, state.Budget, out gpos,
                    out int gposSubtables, out diagnostic, state.RewrittenLayoutModels)
                : RewriteCompactLayoutLists(gposModel, outputMap, LayoutLookupKind.Gpos,
                    context, state.Budget, out gpos, out gposSubtables,
                    out diagnostic, state.RewrittenLayoutModels);
            if (status != ReadStatus.Done) return status;
            census.Add(RewriteCensusRow(directory, Gpos, LookupSubtables(gposModel.Lookups),
                gposSubtables, gpos!.Length));
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The S2c retain+scope entry (card 4.1/5.1): the truly-cut layout
    /// lists re-emit over the identity seam - retain-GIDs never constructs a map
    /// (W1 3.2), so the rewrite engine's seam views the unchanged GID domain and
    /// the scoped cascade shrinks only the rule/feature/lookup lists. GDEF is
    /// scope-transparent (OQ-S2c-9) and never enters this entry; the admission
    /// context feeds the flags validation unchanged. The census rows land in the
    /// same rewrite order as the compact window (OQ-S2b-10/OQ-S2c-10: a scoped
    /// row's in!=out is legal, the per-lookup subtable identity still asserts).</summary>
    private static ReadStatus RewriteScopedRetainLayout(FontFaceRef face,
        PrepareState state, HashSet<uint> rebuildTags, SfntDirectoryView directory,
        out byte[]? gsub, out byte[]? gpos, out byte[]? jstf,
        out FontDiagnostic diagnostic)
    {
        gsub = gpos = jstf = null;
        var outputMap = LayoutOutputMap.Identity(face, state.Retained.Length);
        var census = state.LayoutRewriteCensus ??= [];
        if (rebuildTags.Contains(Gsub))
        {
            if (state.Gsub is not { } gsubModel ||
                state.GsubScope is not { } gsubScope)
                return RewriteProducerMissing(face.Location, Gsub, out diagnostic);
            ReadStatus status = RewriteScopedLayoutLists(gsubModel, outputMap,
                gsubScope, LayoutLookupKind.Gsub, state.GdefContext, state.Budget,
                out gsub, out int gsubSubtables, out diagnostic);
            if (status != ReadStatus.Done) return status;
            census.Add(RewriteCensusRow(directory, Gsub,
                LookupSubtables(gsubModel.Lookups), gsubSubtables, gsub!.Length));
        }
        if (rebuildTags.Contains(Gpos))
        {
            if (state.Gpos is not { } gposModel ||
                state.GposScope is not { } gposScope)
                return RewriteProducerMissing(face.Location, Gpos, out diagnostic);
            ReadStatus status = RewriteScopedLayoutLists(gposModel, outputMap,
                gposScope, LayoutLookupKind.Gpos, state.GdefContext, state.Budget,
                out gpos, out int gposSubtables, out diagnostic);
            if (status != ReadStatus.Done) return status;
            census.Add(RewriteCensusRow(directory, Gpos,
                LookupSubtables(gposModel.Lookups), gposSubtables, gpos!.Length));
        }
        if (rebuildTags.Contains(Jstf))
        {
            if (state.Jstf is not { } jstfModel ||
                state.JstfScope is not { } jstfScope)
                return RewriteProducerMissing(face.Location, Jstf, out diagnostic);
            ReadStatus jstfStatus = RebuildScopedJstfCore(jstfModel, state,
                jstfScope, outputMap, outputMap.OutputGlyphCount, state.Budget,
                census, out jstf, out diagnostic);
            if (jstfStatus != ReadStatus.Done) return jstfStatus;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The S2c scoped JSTF producer (card 5.2, OQ-S2c-8): the surviving
    /// script tree re-emits through the leaf factories - the extender lists remap
    /// (never cut: no script domain), the JstfMax inline lookups ride the seam,
    /// and every mod list re-answers its entries against the neighbor tables'
    /// surviving-lookup remaps, a dangling entry deleting (enable/disable over a
    /// deleted lookup is a no-op; the emptied list drops to the absent slot form)
    /// and the increasing-order observation riding the subsequence. The root
    /// re-emits with the neighbor tables' new lookup counts - the AnswerScript
    /// re-answer is the defensive face (RF-S2c-4; the dangling deletion means it
    /// cannot fire on well-formed input). The census row reads in!=out legally
    /// (OQ-S2c-10).</summary>
    private static ReadStatus RebuildScopedJstf(JstfTable jstf, PrepareState state,
        JstfScopeResolution scope, GlyphRemap map, BuildBudget budget,
        List<LayoutRewriteCensusRow> rewrites, out byte[]? jstfBytes,
        out FontDiagnostic diagnostic) =>
        RebuildScopedJstfCore(jstf, state, scope, LayoutOutputMap.Frozen(map),
            map.OutputGlyphCount, budget, rewrites, out jstfBytes, out diagnostic);

    private static ReadStatus RebuildScopedJstfCore(JstfTable jstf,
        PrepareState state, JstfScopeResolution scope, LayoutOutputMap outputMap,
        int outputGlyphs, BuildBudget budget, List<LayoutRewriteCensusRow> rewrites,
        out byte[]? jstfBytes, out FontDiagnostic diagnostic)
    {
        jstfBytes = null;
        int inputPriorities = JstfPriorityCount(jstf);
        // The neighbor tables' new lookup counts: a scoped table answers with its
        // surviving count, an unscoped or absent one with the count the JSTF read
        // was answered with (the mods ride identity there).
        int gsubNewCount = state.GsubScope?.SurvivingLookups.Count ??
            state.Gsub?.Lookups.Length ?? jstf.GsubLookupCount;
        int gposNewCount = state.GposScope?.SurvivingLookups.Count ??
            state.Gpos?.Lookups.Length ?? jstf.GposLookupCount;
        DataLocation at = jstfModelLocation(jstf.Location);
        var scripts = new List<JstfScriptRecord>(scope.SurvivingScripts.Count);
        for (int s = 0; s < scope.SurvivingScripts.Count; s++)
        {
            (uint tag, JstfScriptTable script) = jstf.Scripts[scope.SurvivingScripts[s]];
            JstfExtenderGlyphTable? extenders = null;
            if (script.ExtenderGlyphs is { } extenderModel)
            {
                var glyphs = new ushort[extenderModel.GlyphCount];
                for (int i = 0; i < glyphs.Length; i++)
                {
                    ReadStatus status = RemapGlyphId(at, extenderModel.Glyphs[i],
                        outputMap, out glyphs[i], out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                ReadStatus extenderStatus = JstfTableCompiler.TryCreate(glyphs,
                    outputGlyphs, out JstfExtenderGlyphTable? rebuiltExtenders,
                    out diagnostic, location: at);
                if (extenderStatus != ReadStatus.Done) return extenderStatus;
                extenders = rebuiltExtenders;
            }
            ReadStatus langSysStatus = RebuildScopedJstfLangSys(script.DefaultLangSys,
                state, at, outputMap, outputGlyphs, state.GdefContext, budget,
                out var defaultLangSys, out diagnostic);
            if (langSysStatus != ReadStatus.Done) return langSysStatus;
            var langSysRows = new List<JstfLangSysRecord>(
                scope.SurvivingLanguages[s].Count);
            foreach (int languageOrdinal in scope.SurvivingLanguages[s])
            {
                (uint langTag, JstfLangSysTable langSys) =
                    script.LangSys[languageOrdinal];
                langSysStatus = RebuildScopedJstfLangSys(langSys, state, at,
                    outputMap, outputGlyphs, state.GdefContext, budget,
                    out var rebuiltLangSys, out diagnostic);
                if (langSysStatus != ReadStatus.Done) return langSysStatus;
                langSysRows.Add(new(langTag, rebuiltLangSys!));
            }
            ReadStatus scriptStatus = JstfTableCompiler.TryCreate(extenders,
                defaultLangSys, langSysRows, out JstfScriptTable? rebuiltScript,
                out diagnostic, location: at);
            if (scriptStatus != ReadStatus.Done) return scriptStatus;
            scripts.Add(new(tag, rebuiltScript!));
        }
        ReadStatus rootStatus = JstfTableCompiler.TryCreate(jstf.Version,
            outputGlyphs, gsubNewCount, gposNewCount, state.GdefContext, scripts,
            out JstfTable? model, out diagnostic, location: at);
        if (rootStatus != ReadStatus.Done) return rootStatus;
        if (state.RewrittenLayoutModels is { } layoutModels) layoutModels.Jstf = model;
        rootStatus = JstfTableCompiler.TryCompile(model!, out var bytes,
            out diagnostic);
        if (rootStatus != ReadStatus.Done) return rootStatus;
        rewrites.Add(RewriteCensusRow(JstfLayout.Tag, inputPriorities,
            JstfPriorityCount(model!), jstf.ConsumedLength, bytes!.Length));
        return ChargeCompiled(budget, jstf.Location, bytes, out jstfBytes,
            out diagnostic);
    }

    /// <summary>One JSTF langsys's scoped rebuild: every priority re-forms with the
    /// remapped mod lists and the seam-rewritten JstfMax inline lookups; a zero-
    /// priority langsys is the OQ-5 legal empty form and keeps its row.</summary>
    private static ReadStatus RebuildScopedJstfLangSys(JstfLangSysTable? langSys,
        PrepareState state, DataLocation at, LayoutOutputMap outputMap,
        int outputGlyphs, GdefLookupContext? context, BuildBudget budget,
        out JstfLangSysTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        if (langSys is null)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        var priorities = new JstfPriorityTable[langSys.PriorityCount];
        for (int p = 0; p < langSys.PriorityCount; p++)
        {
            JstfPriorityTable priority = langSys.Priorities[p];
            ReadStatus status = RebuildJstfMax(at, priority.ShrinkageJstfMax,
                outputMap, outputGlyphs, context, out var shrinkageMax,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = RebuildJstfMax(at, priority.ExtensionJstfMax, outputMap,
                outputGlyphs, context, out var extensionMax, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // The eight mod slots rebuild before the priority composes, so a
            // remap failure refuses before anything constructs.
            var slots = new[]
            {
                priority.GsubShrinkageEnable, priority.GsubShrinkageDisable,
                priority.GposShrinkageEnable, priority.GposShrinkageDisable,
                priority.GsubExtensionEnable, priority.GsubExtensionDisable,
                priority.GposExtensionEnable, priority.GposExtensionDisable,
            };
            var mods = new JstfModListTable?[slots.Length];
            for (int m = 0; m < slots.Length; m++)
            {
                status = RebuildScopedModList(slots[m], state, out mods[m],
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            status = JstfTableCompiler.TryCreate(mods[0], mods[1], mods[2], mods[3],
                shrinkageMax, mods[4], mods[5], mods[6], mods[7], extensionMax,
                out JstfPriorityTable? rebuiltPriority, out diagnostic, at);
            if (status != ReadStatus.Done) return status;
            priorities[p] = rebuiltPriority!;
        }
        return JstfTableCompiler.TryCreate(priorities, out rebuilt, out diagnostic,
            location: at);
    }

    /// <summary>One mod list's scoped re-answer (OQ-S2c-8): the entries whose
    /// target lookup survived ride the old-to-new remap in wire order (the
    /// increasing-order observation rides the subsequence); a dangling entry
    /// deletes, and an emptied list drops to the absent slot form. No neighbor
    /// resolution (the table absent or unscoped) rides identity. A factory
    /// failure propagates its own status (the budget face rides ResourceLimit;
    /// the domain face is the RF-S2c-4 defense and cannot fire on well-formed
    /// input — every remapped target is a surviving ordinal by construction).</summary>
    private static ReadStatus RebuildScopedModList(JstfModListTable? mod,
        PrepareState state, out JstfModListTable? rebuilt,
        out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        if (mod is null)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        int[]? remap = mod.Domain == JstfLookupDomain.Gsub
            ? state.GsubScope?.LookupNewIndex
            : state.GposScope?.LookupNewIndex;
        if (remap is null)
        {
            diagnostic = default;
            rebuilt = mod;
            return ReadStatus.Done;
        }
        var indices = new List<ushort>(mod.LookupCount);
        foreach (ushort old in mod.LookupIndices)
        {
            int target = old < remap.Length ? remap[old] : -1;
            if (target < 0) continue; // the dangling entry deletes
            indices.Add((ushort)target);
        }
        if (indices.Count == 0)
        {
            diagnostic = default;
            return ReadStatus.Done; // the emptied list drops to the absent slot form
        }
        int domainCount = mod.Domain == JstfLookupDomain.Gsub
            ? state.GsubScope!.SurvivingLookups.Count
            : state.GposScope!.SurvivingLookups.Count;
        return JstfTableCompiler.TryCreate(mod.Domain,
            CollectionsMarshal.AsSpan(indices), domainCount,
            out rebuilt, out diagnostic, location: mod.Location);
    }

    /// <summary>The S2c record-remap refusal (RF-S2c-3): a rule record whose target
    /// lookup the scope cut is the rewrite-invariant break under the code-23
    /// contract ("layout / rewrite / lookup / tag", the old index in Detail) —
    /// unreachable while the transitive closure feeds the resolution, so the face
    /// defends orchestration drift only.</summary>
    private static ReadStatus ScopedRecordMiss(DataLocation at, int oldIndex,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.LayoutFamilyUnsupported,
            at with
            {
                Field = DataField.Payload,
                RecordOrdinal = oldIndex
            }, out diagnostic, unchecked((uint)oldIndex));

    /// <summary>One record list's S2c re-answer (card §5.1 step 2): with a remap the
    /// targets move to their new indices and the caller's TryCreateRule re-validates
    /// against the real new count (the domain check's first fully-effective pass);
    /// without one the list travels unchanged and lookupCount stays -1 — the S2b
    /// records-unchanged form (the record-order invariant OQ-S2b-7 rides).</summary>
    private static ReadStatus RemapLookupRecords(
        ReadOnlySpan<SequenceLookupRecord> source, LayoutLookupRemap? remap,
        DataLocation at, out SequenceLookupRecord[] records, out int lookupCount,
        out FontDiagnostic diagnostic)
    {
        records = source.ToArray();
        lookupCount = -1;
        diagnostic = default;
        if (remap is null) return ReadStatus.Done;
        int count = 0;
        for (int i = 0; i < records.Length; i++)
        {
            int old = records[i].LookupListIndex;
            if (old >= remap.OldToNew.Length) return ScopedRecordMiss(at, old, out diagnostic);
            int target = remap.OldToNew[old];
            if (target == -1 && remap.DropUnmatchableTargets) continue;
            if (target < 0 || target >= remap.NewCount)
                return ScopedRecordMiss(at, old, out diagnostic);
            records[count++] = records[i] with { LookupListIndex = (ushort)target };
        }
        if (count != records.Length) Array.Resize(ref records, count);
        lookupCount = remap.NewCount;
        return ReadStatus.Done;
    }

    /// <summary>One census row's measures (OQ-S2b-10): the input payload length comes
    /// from the source directory, the counts from the input and rebuilt models, and
    /// the count-identity result is recorded here — the per-lookup defense already
    /// refused on a count break (RF-S2b-2), so a false cell can only ride a GDEF
    /// child block the compiler dropped.</summary>
    private static LayoutRewriteCensusRow RewriteCensusRow(SfntDirectoryView directory,
        uint tag, int inputSubtables, int outputSubtables, int outputBytes)
    {
        int inputBytes = 0;
        if (ReadTableBytes(directory, tag, out _, out var payload, out _) ==
            ReadStatus.Done)
            inputBytes = payload.Length;
        return RewriteCensusRow(tag, inputSubtables, outputSubtables, inputBytes,
            outputBytes);
    }

    /// <summary>The census row's core (the merged S2b-2 producers' form): the
    /// model-side overload for the kern/MATH/JSTF families, whose input length is
    /// the admission model's own ConsumedLength — the same source table the
    /// directory read admitted, so the two overloads state identical facts. The
    /// count-identity verdict computes here once: every producer has already
    /// refused its count break before appending, so a false cell never
    /// publishes.</summary>
    private static LayoutRewriteCensusRow RewriteCensusRow(uint tag,
        int inputSubtables, int outputSubtables, int inputBytes, int outputBytes) =>
        new(tag, inputSubtables, outputSubtables, inputBytes, outputBytes,
            inputSubtables == outputSubtables);

    /// <summary>GDEF's subtable analog for the census: the present top-level child
    /// blocks (the two class domains, the mark sets, the attach and lig-caret lists,
    /// the item variation store).</summary>
    private static int ChildBlocks(GdefTable gdef) =>
        (gdef.GlyphClasses is not null ? 1 : 0) +
        (gdef.MarkAttachmentClasses is not null ? 1 : 0) +
        (gdef.MarkGlyphSets is not null ? 1 : 0) +
        (gdef.AttachList is not null ? 1 : 0) +
        (gdef.LigCaretList is not null ? 1 : 0) +
        (gdef.ItemVariationStore is not null ? 1 : 0);

    /// <summary>The layout lists' subtable analog for the census: the lookup list's
    /// total subtable count.</summary>
    private static int LookupSubtables(ReadOnlySpan<LookupTable> lookups)
    {
        int total = 0;
        foreach (var lookup in lookups) total += lookup.SubtableCount;
        return total;
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
    private static ReadStatus RewriteMiss(LayoutOutputMap map, uint tag, int inputGlyph,
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
    private static ReadStatus RewriteCoverage(CoverageTable coverage, LayoutOutputMap map,
        BuildBudget budget, out CoverageTable? rewritten, out int[] ordinalOf,
        out FontDiagnostic diagnostic, Func<int, bool>? keepOrdinal = null)
    {
        rewritten = null;
        ordinalOf = new int[coverage.Glyphs.Length];
        var keys = new ushort[coverage.Glyphs.Length];
        int count = 0;
        for (int i = 0; i < coverage.Glyphs.Length; i++)
        {
            int output = map.OutputOf(coverage.Glyphs[i]);
            if (output < 0 || keepOrdinal is not null && !keepOrdinal(i)) continue;
            keys[count] = (ushort)output;
            ordinalOf[count++] = i;
        }
        Array.Resize(ref keys, count);
        Array.Resize(ref ordinalOf, count);
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
    private static ReadStatus RewriteCompactGdef(GdefTable gdef, LayoutOutputMap map,
        BuildBudget budget, out byte[]? bytes, out GdefLookupContext? context,
        out int subtableCount, out FontDiagnostic diagnostic, CompactLayoutModels? layoutModels = null)
    {
        bytes = null;
        context = null;
        subtableCount = 0;
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
            var points = new ushort[ordinalOf.Length][];
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
            var ligGlyphs = new LigGlyphTable[ordinalOf.Length];
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
        if (layoutModels is not null) layoutModels.Gdef = rewritten;
        status = GdefTableCompiler.TryCompile(rewritten!, out bytes, out diagnostic,
            budget);
        if (status != ReadStatus.Done) return status;
        subtableCount = ChildBlocks(rewritten!);
        var classes = new GdefClassDefinitions(gdef.Version, numGlyphs, glyphClasses,
            markAttachmentClasses, 0, 0, 0, 0, gdef.Location);
        status = GdefLookupContext.TryCreate(classes, markSets,
            gdef.ItemVariationStore, out context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RewriteClassDefRanges(ClassDefTable? classDef,
        LayoutOutputMap map, out ClassDefRange[] ranges, out ushort format,
        out FontDiagnostic diagnostic, ReadOnlySpan<int> classRemap = default)
    {
        ranges = [];
        format = 2;
        diagnostic = default;
        if (classDef is null) return ReadStatus.Done;
        var keys = new List<ushort>();
        var values = new List<ushort>();
        foreach (var range in classDef.Ranges)
        {
            if (!classRemap.IsEmpty && range.ClassValue >= classRemap.Length) continue;
            int classValue = classRemap.IsEmpty
                ? range.ClassValue : classRemap[range.ClassValue];
            if (classValue < 0) continue;
            for (int gid = range.StartGlyph; gid <= range.EndGlyph; gid++)
            {
                int output = map.OutputOf(gid);
                if (output < 0) continue; // the definitional entry drop
                keys.Add((ushort)output);
                values.Add((ushort)classValue);
            }
        }
        var keyArray = keys.ToArray();
        var valueArray = values.ToArray();
        Array.Sort(keyArray, valueArray); // distinct keys: a total sort
        var grouped = new List<ClassDefRange>(keyArray.Length);
        int at = 0;
        while (at < keyArray.Length)
        {
            int end = at;
            while (end + 1 < keyArray.Length && keyArray[end + 1] == keyArray[end] + 1 &&
                valueArray[end + 1] == valueArray[at])
                end++;
            grouped.Add(new ClassDefRange(keyArray[at], keyArray[end], valueArray[at]));
            at = end + 1;
        }
        // The emission format is the canonical smaller measure: format 1 only when
        // the runs tile a dense window no wider than the sparse encoding (the
        // reader's expanded-membership semantics make the two formats' class
        // answers identical).
        bool tiles = true;
        for (int i = 1; i < grouped.Count && tiles; i++)
            tiles = grouped[i].StartGlyph == grouped[i - 1].EndGlyph + 1;
        int denseCount = grouped.Count == 0
            ? 0
            : grouped[^1].EndGlyph - grouped[0].StartGlyph + 1;
        int denseLength = ClassDefLayout.DenseValuesOffset +
            ClassDefLayout.ValueStride * denseCount;
        int sparseLength = ClassDefLayout.SparseRangesOffset +
            ClassDefLayout.RangeStride * grouped.Count;
        ranges = grouped.ToArray();
        format = tiles && denseLength <= sparseLength ? (ushort)1 : (ushort)2;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RewriteClassDef(ClassDefTable? classDef, LayoutOutputMap map,
        BuildBudget budget, out ClassDefTable? rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteClassDefRanges(classDef, map, out var ranges,
            out ushort format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (classDef is null) return ReadStatus.Done;
        return ClassDefTableCompiler.TryCreate(ranges, map.OutputGlyphCount,
            format, out rewritten, out diagnostic, budget, classDef.Location);
    }

    // ---- the layout lists (GSUB/GPOS; S2b card §2.1) ------------------------------

    private static ReadStatus RewriteCompactLayoutLists(LayoutListsTable table,
        LayoutOutputMap map, LayoutLookupKind kind, GdefLookupContext? context,
        BuildBudget budget, out byte[]? bytes, out int subtableCount,
        out FontDiagnostic diagnostic, CompactLayoutModels? layoutModels = null)
    {
        bytes = null;
        subtableCount = 0;
        var lookups = new LookupTable[table.Lookups.Length];
        for (int i = 0; i < lookups.Length; i++)
        {
            ReadStatus status = RewriteLookup(table.Lookups[i], map, kind, context,
                budget, out var lookup, out diagnostic);
            lookups[i] = lookup!;
            if (status != ReadStatus.Done) return status;
            if (lookups[i] is null ||
                lookups[i]!.SubtableCount != table.Lookups[i].SubtableCount)
                return RewriteMiss(map, table.Location.TableTag, -1, out diagnostic);
        }
        subtableCount = LookupSubtables(lookups);
        LayoutListsTable? root;
        ReadStatus rootStatus;
        if (kind == LayoutLookupKind.Gsub)
        {
            rootStatus = GsubTableCompiler.TryCreate(table.Version,
                table.FeatureVariationsOffsetRaw, table.Scripts, table.Features,
                lookups, map.OutputGlyphCount, out root, out diagnostic, budget.Limits,
                table.Location, table.FeatureVariations);
        }
        else
        {
            rootStatus = GposTableCompiler.TryCreate(table.Version,
                table.FeatureVariationsOffsetRaw, table.Scripts, table.Features,
                lookups, map.OutputGlyphCount, out root, out diagnostic, budget.Limits,
                table.Location, table.FeatureVariations);
        }
        if (rootStatus != ReadStatus.Done) return rootStatus;
        if (layoutModels is not null)
        {
            if (kind == LayoutLookupKind.Gsub) layoutModels.Gsub = root;
            else layoutModels.Gpos = root;
        }
        ReadStatus compileStatus = kind == LayoutLookupKind.Gsub
            ? GsubTableCompiler.TryCompile(root!, context, out bytes, out diagnostic,
                budget)
            : GposTableCompiler.TryCompile(root!, context, out bytes, out diagnostic,
                budget);
        if (compileStatus != ReadStatus.Done) return compileStatus;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The S2c scoped cascade (card 5.1, one layout list over a true cut):
    /// (1) the surviving lookups re-emit in original wire order through the same
    /// 17-form rewrite on the caller's output-map seam (compact = frozen map,
    /// retain+scope = identity); (2) every surviving contextual/chaining rule
    /// re-answers its records against the real new lookup count; (3) surviving
    /// feature entries rebuild via TryWithLookups over the remapped indices; (4)
    /// surviving langsys rows rebuild via TryWithFeatures - cut feature indices
    /// drop, a required index remaps (its entry is an implicit survivor), and an
    /// empty required-less row drops; (5) scripts rebuild via TryWithLanguages (the
    /// parser already pruned empty trees; the empty ScriptList root is the legal
    /// zero-count form); (6) the FV records re-root over the surviving feature
    /// indices with rebuilt alternate features; (7) the
    /// root re-emits through the family compiler with the surviving lists. The
    /// per-lookup subtable-count defense stays exact (RF-S2b-2); the whole-table
    /// census row reads in!=out legally for a scoped rewrite (OQ-S2c-10).</summary>
    private static ReadStatus RewriteScopedLayoutLists(LayoutListsTable table,
        LayoutOutputMap map, LayoutScopeResolution scope, LayoutLookupKind kind,
        GdefLookupContext? context, BuildBudget budget, out byte[]? bytes,
        out int subtableCount, out FontDiagnostic diagnostic, CompactLayoutModels? layoutModels = null)
    {
        bytes = null;
        subtableCount = 0;
        var lookupRemap = LayoutLookupRemap.Of(scope);
        var lookups = new List<LookupTable>(scope.SurvivingLookups.Count);
        foreach (int lookupIndex in scope.SurvivingLookups)
        {
            ReadStatus status = RewriteLookup(table.Lookups[lookupIndex], map, kind,
                context, budget, out var lookup, out diagnostic, lookupRemap);
            if (status != ReadStatus.Done) return status;
            if (lookup is null ||
                lookup.SubtableCount != table.Lookups[lookupIndex].SubtableCount)
                return RewriteMiss(map, table.Location.TableTag, -1, out diagnostic);
            lookups.Add(lookup!);
        }
        foreach (var lookup in lookups) subtableCount += lookup.SubtableCount;

        // Step 3: the surviving feature entries (their lookups were seeds, so the
        // remap is total; a -1 cell is the RF-S2c-3 drift refusal).
        var features = new List<LayoutFeature>(scope.SurvivingFeatures.Count);
        foreach (int featureIndex in scope.SurvivingFeatures)
        {
            LayoutFeature feature = table.Features[featureIndex];
            var indices = new ushort[feature.LookupIndices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                int target = lookupRemap.OldToNew[feature.LookupIndices[i]];
                if (target < 0)
                    return ScopedRecordMiss(feature.Location,
                        feature.LookupIndices[i], out diagnostic);
                indices[i] = (ushort)target;
            }
            ReadStatus status = feature.TryWithLookups(indices,
                scope.SurvivingLookups.Count, out var rebuilt, out diagnostic,
                budget.Limits);
            if (status != ReadStatus.Done) return status;
            features.Add(rebuilt!);
        }

        // Steps 4+5: the langsys rows, then the scripts.
        var scripts = new List<LayoutScript>(scope.SurvivingScripts.Count);
        for (int s = 0; s < scope.SurvivingScripts.Count; s++)
        {
            LayoutScript script = table.Scripts[scope.SurvivingScripts[s]];
            ReadStatus status = RewriteScopedLangSys(script.DefaultLanguage, scope,
                budget, out var defaultLangSys, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var languages = new List<LayoutLanguage>(scope.SurvivingLanguages[s].Count);
            foreach (int languageOrdinal in scope.SurvivingLanguages[s])
            {
                status = RewriteScopedLangSys(
                    script.Languages[languageOrdinal].Language, scope, budget,
                    out var rebuiltLangSys, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (rebuiltLangSys is null) continue; // the empty row drops (step 4)
                languages.Add(new LayoutLanguage(script.Languages[languageOrdinal].Tag,
                    rebuiltLangSys));
            }
            if (defaultLangSys is null && languages.Count == 0)
                continue; // the empty script drops (step 5; the parser prunes these)
            status = script.TryWithLanguages(defaultLangSys,
                CollectionsMarshal.AsSpan(languages), features.Count,
                out var rebuiltScript, out diagnostic, budget.Limits);
            if (status != ReadStatus.Done) return status;
            scripts.Add(rebuiltScript!);
        }

        // Step 6: the FV records re-root (OQ-S2c-6).
        FeatureVariationsTable? variations = null;
        if (table.FeatureVariations is { } sourceVariations)
        {
            ReadStatus variationsStatus = RewriteScopedVariations(sourceVariations,
                scope, budget, out variations, out diagnostic);
            if (variationsStatus != ReadStatus.Done) return variationsStatus;
        }

        // Step 7: the root re-emits with the surviving lists.
        LayoutListsTable? root;
        ReadStatus rootStatus = kind == LayoutLookupKind.Gsub
            ? GsubTableCompiler.TryCreate(table.Version,
                table.FeatureVariationsOffsetRaw, CollectionsMarshal.AsSpan(scripts),
                CollectionsMarshal.AsSpan(features), CollectionsMarshal.AsSpan(lookups),
                map.OutputGlyphCount, out root, out diagnostic, budget.Limits,
                table.Location, variations)
            : GposTableCompiler.TryCreate(table.Version,
                table.FeatureVariationsOffsetRaw, CollectionsMarshal.AsSpan(scripts),
                CollectionsMarshal.AsSpan(features), CollectionsMarshal.AsSpan(lookups),
                map.OutputGlyphCount, out root, out diagnostic, budget.Limits,
                table.Location, variations);
        if (rootStatus != ReadStatus.Done) return rootStatus;
        if (layoutModels is not null)
        {
            if (kind == LayoutLookupKind.Gsub) layoutModels.Gsub = root;
            else layoutModels.Gpos = root;
        }
        ReadStatus compileStatus = kind == LayoutLookupKind.Gsub
            ? GsubTableCompiler.TryCompile(root!, context, out bytes, out diagnostic,
                budget)
            : GposTableCompiler.TryCompile(root!, context, out bytes, out diagnostic,
                budget);
        if (compileStatus != ReadStatus.Done) return compileStatus;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One langsys row's scoped rebuild (card 5.1 step 4): the feature
    /// indices ride the feature remap (a cut index drops), the required index
    /// remaps (its entry is an implicit survivor - a -1 cell is the RF-S2c-3
    /// drift refusal), and an empty required-less row drops (null).</summary>
    private static ReadStatus RewriteScopedLangSys(LayoutLangSys? langSys,
        LayoutScopeResolution scope, BuildBudget budget, out LayoutLangSys? rebuilt,
        out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        if (langSys is null)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        var indices = new List<ushort>(langSys.FeatureIndices.Length);
        foreach (ushort old in langSys.FeatureIndices)
        {
            int target = scope.FeatureNewIndex[old];
            if (target < 0) continue; // the cut feature's index drops from the row
            indices.Add((ushort)target);
        }
        ushort required = langSys.RequiredFeatureIndex;
        if (required != ushort.MaxValue)
        {
            int target = scope.FeatureNewIndex[required];
            if (target < 0)
                return ScopedRecordMiss(langSys.Location, required, out diagnostic);
            required = (ushort)target;
        }
        else if (indices.Count == 0)
        {
            diagnostic = default;
            return ReadStatus.Done; // the empty required-less row drops
        }
        return langSys.TryWithFeatures(required, CollectionsMarshal.AsSpan(indices),
            scope.SurvivingFeatures.Count, out rebuilt, out diagnostic,
            budget.Limits);
    }

    /// <summary>The FV block's scoped re-root (card 2.3, OQ-S2c-6): a substitution
    /// entry survives iff its replaced featureIndex survives, its alternate feature
    /// rebuilds over the remapped lookup indices (the alternate's lookups were
    /// seeds, so the remap is total), and the
    /// records re-form through the FV compiler.</summary>
    private static ReadStatus RewriteScopedVariations(FeatureVariationsTable source,
        LayoutScopeResolution scope, BuildBudget budget,
        out FeatureVariationsTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        var records = new List<FeatureVariationRecord>(source.RecordCount);
        foreach (var record in source.Records)
        {
            if (record.Substitution is not { } substitution)
            {
                records.Add(record); // the no-op record form keeps its condition set
                continue;
            }
            var entries = new List<(ushort FeatureIndex, LayoutFeature Alternate)>(
                substitution.Records.Count);
            foreach (var entry in substitution.Records)
            {
                int featureTarget = scope.FeatureNewIndex[entry.FeatureIndex];
                if (featureTarget < 0) continue; // the replaced feature died
                LayoutFeature alternate = entry.Alternate;
                var indices = new ushort[alternate.LookupIndices.Length];
                for (int i = 0; i < indices.Length; i++)
                {
                    int target = scope.LookupNewIndex[alternate.LookupIndices[i]];
                    if (target < 0)
                        return ScopedRecordMiss(alternate.Location,
                            alternate.LookupIndices[i], out diagnostic);
                    indices[i] = (ushort)target;
                }
                ReadStatus status = FeatureVariationsTableCompiler.TryCreateAlternateFeature(
                    alternate.ParamsOffsetRaw, indices, scope.SurvivingLookups.Count,
                    out var rebuiltAlternate, out diagnostic, budget, alternate.Location);
                if (status != ReadStatus.Done) return status;
                entries.Add(((ushort)featureTarget, rebuiltAlternate!));
            }
            ReadStatus substStatus = FeatureVariationsTableCompiler.
                TryCreateFeatureTableSubst(entries, scope.SurvivingFeatures.Count,
                    out var subst, out diagnostic, budget, substitution.Location);
            if (substStatus != ReadStatus.Done) return substStatus;
            records.Add(new FeatureVariationRecord(record.ConditionSet, subst));
        }
        ReadStatus rootStatus = FeatureVariationsTableCompiler.TryCreate(records,
            out rebuilt, out diagnostic, budget.Limits, source.Location);
        if (rootStatus != ReadStatus.Done) return rootStatus;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RewriteLookup(LookupTable lookup, LayoutOutputMap map,
        LayoutLookupKind kind, GdefLookupContext? context, BuildBudget budget,
        out LookupTable? rewritten, out FontDiagnostic diagnostic,
        LayoutLookupRemap? lookupRemap = null)
    {
        rewritten = null;
        return kind == LayoutLookupKind.Gsub
            ? lookup.LookupType switch
            {
                1 => RewriteSingleSubstLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic),
                2 => RewriteDirectLookup(lookup, static l => l.Multiples!, RewriteMultiple,
                    LookupTableCompiler.TryCreateMultipleSubst, map, context, budget,
                    out rewritten, out diagnostic),
                3 => RewriteDirectLookup(lookup, static l => l.Alternates!, RewriteAlternate,
                    LookupTableCompiler.TryCreateAlternateSubst, map, context, budget,
                    out rewritten, out diagnostic),
                4 => RewriteDirectLookup(lookup, static l => l.Ligatures!, RewriteLigature,
                    LookupTableCompiler.TryCreateLigatureSubst, map, context, budget,
                    out rewritten, out diagnostic),
                5 => RewriteContextualLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic, lookupRemap),
                6 => RewriteChainingLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic, lookupRemap),
                7 => RewriteGsubExtensionLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic, lookupRemap),
                8 => RewriteDirectLookup(lookup, static l => l.ReverseChains!,
                    RewriteReverseChain, LookupTableCompiler.TryCreateReverseChainSubst,
                    map, context, budget, out rewritten, out diagnostic),
                _ => UnsupportedLookupType(lookup, out diagnostic),
            }
            : lookup.LookupType switch
            {
                1 => RewriteSinglePosLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic),
                2 => RewritePairPosLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic),
                3 => RewriteDirectLookup(lookup, static l => l.CursiveAttachments!,
                    RewriteCursive, LookupTableCompiler.TryCreateCursivePos, map, context,
                    budget, out rewritten, out diagnostic),
                4 => RewriteMarkLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic),
                5 => RewriteMarkLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic),
                6 => RewriteMarkLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic),
                7 => RewriteContextualLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic, lookupRemap),
                8 => RewriteChainingLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic, lookupRemap),
                9 => RewriteGposExtensionLookup(lookup, map, context, budget,
                    out rewritten, out diagnostic, lookupRemap),
                _ => UnsupportedLookupType(lookup, out diagnostic),
            };
    }

    private static ReadStatus UnsupportedLookupType(LookupTable lookup,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            lookup.Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic,
            lookup.LookupType);

    /// <summary>The direct-leaf lookup shape (GSUB 2/3/4/8, GPOS 3): one leaf
    /// rewriter per type plus the type's LookupTableCompiler factory, shared by the
    /// whole family (the factory's per-child identity checks re-fire on the rewritten
    /// leaves).</summary>
    private delegate ReadStatus LeafRewriter<TLeaf>(TLeaf sub, LayoutOutputMap map,
        BuildBudget budget, out TLeaf? rewritten, out FontDiagnostic diagnostic)
        where TLeaf : class;

    private delegate ReadStatus LookupFactory<TLeaf>(int numGlyphs,
        IReadOnlyList<TLeaf> subtables, ushort flags, ushort? markFilteringSet,
        GdefLookupContext? context, out LookupTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits, DataLocation location)
        where TLeaf : class;

    private static ReadStatus RewriteDirectLookup<TLeaf>(LookupTable lookup,
        Func<LookupTable, IReadOnlyList<TLeaf>> leaves, LeafRewriter<TLeaf> rewriter,
        LookupFactory<TLeaf> factory, LayoutOutputMap map, GdefLookupContext? context,
        BuildBudget budget, out LookupTable? rewritten, out FontDiagnostic diagnostic)
        where TLeaf : class
    {
        rewritten = null;
        var source = leaves(lookup);
        var subtables = new TLeaf[source.Count];
        for (int i = 0; i < subtables.Length; i++)
        {
            ReadStatus status = rewriter(source[i], map, budget, out var leaf,
                out diagnostic);
            subtables[i] = leaf!;
            if (status != ReadStatus.Done) return status;
        }
        ReadStatus factoryStatus = factory(map.OutputGlyphCount, subtables,
            lookup.Flags, lookup.MarkFilteringSet, context, out rewritten,
            out diagnostic, budget.Limits, lookup.Location);
        if (factoryStatus != ReadStatus.Done) return factoryStatus;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- GSUB 1 / GPOS 1 ----------------------------------------------------------

    /// <summary>The GSUB type-1 form answer (OQ-S2b-4): the input format re-answers
    /// in the output domain — a format-1 subtable whose uniform delta breaks under
    /// the remap re-selects format 2 (definitional, never a drift); format 2 stays.</summary>
    private static ReadStatus RewriteSingleSubstSubtable(SingleSubstTable sub,
        LayoutOutputMap map, BuildBudget budget, out SingleSubstTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteCoverage(sub.Coverage, map, budget, out var coverage,
            out var ordinalOf, out diagnostic,
            i => map.OutputOf(sub.Substitutes[i]) >= 0);
        if (status != ReadStatus.Done) return status;
        var ordered = new ushort[ordinalOf.Length];
        for (int i = 0; i < ordered.Length; i++)
            ordered[i] = (ushort)map.OutputOf(sub.Substitutes[ordinalOf[i]]);
        ushort format = sub.Format;
        if (format == 1 && !HasUniformDelta(coverage!.Glyphs, ordered)) format = 2;
        return SingleSubstTableCompiler.TryCreate(coverage!.Glyphs, ordered,
            map.OutputGlyphCount, format, out rewritten, out diagnostic, budget,
            sub.Location, coverage.Format);
    }

    private static bool HasUniformDelta(ReadOnlySpan<ushort> glyphs,
        ReadOnlySpan<ushort> substitutes)
    {
        if (glyphs.IsEmpty) return true;
        short delta = unchecked((short)(substitutes[0] - glyphs[0]));
        for (int i = 1; i < glyphs.Length; i++)
            if (unchecked((ushort)(glyphs[i] + delta)) != substitutes[i]) return false;
        return true;
    }

    private static ReadStatus RewriteSingleSubstLookup(LookupTable lookup, LayoutOutputMap map,
        GdefLookupContext? context, BuildBudget budget, out LookupTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        var source = lookup.SingleSubstitutions!;
        var subtables = new SingleSubstTable[source.Count];
        for (int i = 0; i < subtables.Length; i++)
        {
            ReadStatus status = RewriteSingleSubstSubtable(source[i], map, budget,
                out var leaf, out diagnostic);
            subtables[i] = leaf!;
            if (status != ReadStatus.Done) return status;
        }
        ReadStatus factoryStatus = LookupTableCompiler.TryCreateSingleSubst(
            map.OutputGlyphCount, subtables, lookup.Flags, lookup.MarkFilteringSet,
            context, out rewritten, out diagnostic, budget, lookup.Location);
        if (factoryStatus != ReadStatus.Done) return factoryStatus;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RewriteSinglePosSubtable(SinglePosTable sub,
        LayoutOutputMap map, BuildBudget budget, out SinglePosTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteCoverage(sub.Coverage, map, budget, out var coverage,
            out var ordinalOf, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The value rows ride their coverage ordinals (format 2's per-key rows);
        // format 1 keeps the single shared physical row — the model stores one row
        // regardless of the coverage size (SinglePosTable.ValueAt's convention), so
        // the ordinal domain never indexes it under a non-monotone map.
        StaticValueRecord[] ordered;
        if (sub.Format == 1)
        {
            ordered = new StaticValueRecord[1];
            ordered[0] = sub.Values[0];
        }
        else
        {
            ordered = new StaticValueRecord[ordinalOf.Length];
            for (int v = 0; v < ordered.Length; v++) ordered[v] = sub.Values[ordinalOf[v]];
        }
        return SinglePosTableCompiler.TryCreate(coverage!.Glyphs, ordered,
            map.OutputGlyphCount, sub.Format, sub.ValueFormat, out rewritten,
            out diagnostic, budget, sub.Location, coverage.Format);
    }

    private static ReadStatus RewriteSinglePosLookup(LookupTable lookup, LayoutOutputMap map,
        GdefLookupContext? context, BuildBudget budget, out LookupTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        var source = lookup.SinglePositions!;
        var subtables = new SinglePosTable[source.Count];
        for (int i = 0; i < subtables.Length; i++)
        {
            ReadStatus status = RewriteSinglePosSubtable(source[i], map, budget,
                out var leaf, out diagnostic);
            subtables[i] = leaf!;
            if (status != ReadStatus.Done) return status;
        }
        ReadStatus factoryStatus = LookupTableCompiler.TryCreateSinglePos(
            map.OutputGlyphCount, subtables, lookup.Flags, lookup.MarkFilteringSet,
            context, out rewritten, out diagnostic, budget, lookup.Location);
        if (factoryStatus != ReadStatus.Done) return factoryStatus;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- GSUB 2 / 3 ---------------------------------------------------------------

    private static ReadStatus RewriteMultiple(MultipleSubstTable sub, LayoutOutputMap map,
        BuildBudget budget, out MultipleSubstTable? rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteCoverage(sub.Coverage, map, budget, out var coverage,
            out var ordinalOf, out diagnostic,
            i => GlyphSequenceSurvives(sub.GetSequence(i), map));
        if (status != ReadStatus.Done) return status;
        var ordered = new ushort[ordinalOf.Length][];
        for (int i = 0; i < ordered.Length; i++)
        {
            status = RewriteGlyphSequence(sub.GetSequence(ordinalOf[i]), map,
                sub.Coverage.Location.TableTag, out ordered[i], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        return MultipleSubstTableCompiler.TryCreate(coverage!.Glyphs, ordered,
            map.OutputGlyphCount, out rewritten, out diagnostic, budget, sub.Location,
            coverage.Format);
    }

    private static ReadStatus RewriteAlternate(AlternateSubstTable sub, LayoutOutputMap map,
        BuildBudget budget, out AlternateSubstTable? rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteCoverage(sub.Coverage, map, budget, out var coverage,
            out var ordinalOf, out diagnostic,
            i => GlyphSequenceSurvives(sub.GetAlternateSet(i), map));
        if (status != ReadStatus.Done) return status;
        var ordered = new ushort[ordinalOf.Length][];
        for (int i = 0; i < ordered.Length; i++)
        {
            status = RewriteGlyphSequence(sub.GetAlternateSet(ordinalOf[i]), map,
                sub.Coverage.Location.TableTag, out ordered[i], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        return AlternateSubstTableCompiler.TryCreate(coverage!.Glyphs, ordered,
            map.OutputGlyphCount, out rewritten, out diagnostic, budget, sub.Location,
            coverage.Format);
    }

    // ---- GSUB 4 -------------------------------------------------------------------

    private static ReadStatus RewriteLigature(LigatureSubstTable sub, LayoutOutputMap map,
        BuildBudget budget, out LigatureSubstTable? rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteCoverage(sub.Coverage, map, budget, out var coverage,
            out var ordinalOf, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var sets = new LigatureSetTable[ordinalOf.Length];
        for (int i = 0; i < sets.Length; i++)
        {
            LigatureSetTable source = sub.GetSet(ordinalOf[i]);
            var ligatures = new List<LigatureTable>(source.LigatureCount);
            for (int l = 0; l < source.LigatureCount; l++)
            {
                LigatureTable rule = source.Ligatures[l];
                int ligature = map.OutputOf(rule.LigatureGlyph);
                if (ligature < 0 || !GlyphSequenceSurvives(rule.Components, map)) continue;
                status = RewriteGlyphSequence(rule.Components, map,
                    sub.Coverage.Location.TableTag, out var components, out diagnostic);
                if (status != ReadStatus.Done) return status;
                // The ligature rules keep their wire preference order — semantic, so
                // never re-ordered; only the GIDs move.
                ligatures.Add(new((ushort)ligature, components, rule.Location, 0));
            }
            sets[i] = new(ligatures.ToArray(), source.Location, 0);
        }
        return LigatureSubstTableCompiler.TryCreate(coverage!.Glyphs, sets,
            map.OutputGlyphCount, out rewritten, out diagnostic, budget, sub.Location,
            coverage.Format);
    }

    // ---- GSUB 8 -------------------------------------------------------------------

    private static ReadStatus RewriteReverseChain(ReverseChainSubstTable sub,
        LayoutOutputMap map, BuildBudget budget, out ReverseChainSubstTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteCoverage(sub.Coverage, map, budget, out var coverage,
            out var ordinalOf, out diagnostic,
            i => map.OutputOf(sub.Substitutes[i]) >= 0);
        if (status != ReadStatus.Done) return status;
        var ordered = new ushort[ordinalOf.Length];
        for (int i = 0; i < ordered.Length; i++)
            ordered[i] = (ushort)map.OutputOf(sub.Substitutes[ordinalOf[i]]);
        status = RewriteCoverageList(sub.BacktrackCoverages, map, budget,
            out var backtrack, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RewriteCoverageList(sub.LookaheadCoverages, map, budget,
            out var lookahead, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ReverseChainSubstTableCompiler.TryCreate(coverage!, backtrack, lookahead,
            ordered, map.OutputGlyphCount, out rewritten, out diagnostic, budget,
            sub.Location);
    }

    /// <summary>A declared coverage chain remaps in its declared order — the chain
    /// slots are positional (backtrack/lookahead and format-3 input semantics), so
    /// no permutation across slots.</summary>
    private static ReadStatus RewriteCoverageList(IReadOnlyList<CoverageTable> coverages,
        LayoutOutputMap map, BuildBudget budget, out CoverageTable[] rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = new CoverageTable[coverages.Count];
        for (int i = 0; i < rewritten.Length; i++)
        {
            ReadStatus status = RewriteCoverage(coverages[i], map, budget,
                out var coverage, out _, out diagnostic);
            rewritten[i] = coverage!;
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- GSUB 5 / GPOS 7 and GSUB 6 / GPOS 8 (the shared rule families) -----------

    private static ReadStatus RewriteContextualSubtable(ContextualSubstTable sub,
        LayoutOutputMap map, BuildBudget budget, out ContextualSubstTable? rewritten,
        out FontDiagnostic diagnostic, LayoutLookupRemap? lookupRemap = null)
    {
        rewritten = null;
        if (sub.Format == 3)
        {
            ReadStatus format3Status = RewriteCoverageList(sub.Coverages!, map, budget,
                out var coverages, out diagnostic);
            if (format3Status != ReadStatus.Done) return format3Status;
            format3Status = RemapLookupRecords(sub.LookupRecords!.ToArray(),
                lookupRemap, sub.Location, out var records, out _, out diagnostic);
            if (format3Status != ReadStatus.Done) return format3Status;
            return ContextualSubstTableCompiler.TryCreateFormat3(coverages,
                records, map.OutputGlyphCount, out rewritten,
                out diagnostic, budget, sub.Location);
        }
        ReadStatus status = RewriteCoverage(sub.Coverage, map, budget, out var coverage,
            out var ordinalOf, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (sub.Format == 2)
        {
            status = RewriteClassDef(sub.ClassDef, map, budget, out var classDef,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            IReadOnlyList<ContextualRuleSet?> ruleSets = sub.RuleSets!;
            if (lookupRemap is not null || map.HasDroppedGlyphs)
            {
                status = RemapContextualClassRuleSets(ruleSets, lookupRemap, budget,
                    out var remappedSets, out diagnostic,
                    map.HasDroppedGlyphs ? classDef : null, coverage);
                if (status != ReadStatus.Done) return status;
                ruleSets = remappedSets;
            }
            return ContextualSubstTableCompiler.TryCreate(2, coverage!, classDef!,
                ruleSets, map.OutputGlyphCount, out rewritten, out diagnostic,
                budget, sub.Location);
        }
        status = RewriteContextualRuleSets(sub.RuleSets!, ordinalOf, map, budget,
            out var orderedSets, out diagnostic, lookupRemap);
        if (status != ReadStatus.Done) return status;
        return ContextualSubstTableCompiler.TryCreate(1, coverage!, null, orderedSets,
            map.OutputGlyphCount, out rewritten, out diagnostic, budget, sub.Location);
    }

    private static ReadStatus RewriteContextualRuleSets(
        IReadOnlyList<ContextualRuleSet?> ruleSets, int[] ordinalOf, LayoutOutputMap map,
        BuildBudget budget, out ContextualRuleSet?[] ordered, out FontDiagnostic diagnostic,
        LayoutLookupRemap? lookupRemap = null)
    {
        ordered = new ContextualRuleSet?[ordinalOf.Length];
        for (int newIndex = 0; newIndex < ordered.Length; newIndex++)
        {
            ContextualRuleSet? source = ruleSets[ordinalOf[newIndex]];
            if (source is null) continue; // the spec-NULL slot permutes with its coverage ordinal
            var rules = new List<ContextualRule>(source.RuleCount);
            for (int r = 0; r < source.RuleCount; r++)
            {
                ContextualRule rule = source.Rules[r];
                if (!GlyphSequenceSurvives(rule.InputSequence, map)) continue;
                ReadStatus status = RewriteGlyphSequence(rule.InputSequence, map,
                    rule.Location.TableTag, out var input, out diagnostic);
                if (status != ReadStatus.Done) return status;
                // The S2b form keeps the record indices byte-stable (the record
                // list is the rule's wire layout); the S2c remap form re-answers
                // each target against the surviving subset and re-validates against
                // the real new count (card 5.1 step 2).
                status = RemapLookupRecords(rule.LookupRecords, lookupRemap,
                    rule.Location, out var records, out int lookupCount,
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ContextualSubstTableCompiler.TryCreateRule(input,
                    records, lookupCount, out var rewrittenRule,
                    out diagnostic, budget, rule.Location);
                if (status != ReadStatus.Done) return status;
                rules.Add(rewrittenRule!);
            }
            ordered[newIndex] = AssembleContextualRuleSet(rules.ToArray(), source.Location);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ContextualRuleSet AssembleContextualRuleSet(ContextualRule[] rules,
        DataLocation location)
    {
        long consumed = ContextualRuleSetLayout.HeaderLength +
            ContextualRuleSetLayout.OffsetStride * rules.Length;
        foreach (var rule in rules) consumed += rule.ConsumedLength;
        return new(rules, location, (int)consumed);
    }

    private static ReadStatus RemapContextualClassRuleSets(
        IReadOnlyList<ContextualRuleSet?> ruleSets, LayoutLookupRemap? remap,
        BuildBudget budget, out ContextualRuleSet?[] ordered, out FontDiagnostic diagnostic,
        ClassDefTable? classDef = null, CoverageTable? coverage = null)
    {
        ordered = new ContextualRuleSet?[ruleSets.Count];
        HashSet<ushort>? inputClasses = null, firstClasses = null;
        if (classDef is not null)
        {
            if (!budget.Work(classDef.NumGlyphs + coverage!.Glyphs.Length,
                classDef.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            inputClasses = ClassesInDomain(classDef);
            firstClasses = [];
            foreach (ushort glyph in coverage.Glyphs)
                firstClasses.Add(classDef.ClassOf(glyph));
        }
        for (int i = 0; i < ordered.Length; i++)
        {
            if (ruleSets[i] is not { } source) continue; // the spec-NULL slot keeps its slot
            if (firstClasses is not null && !firstClasses.Contains((ushort)i)) continue;
            var rules = new List<ContextualRule>(source.RuleCount);
            for (int r = 0; r < source.RuleCount; r++)
            {
                ContextualRule rule = source.Rules[r];
                if (inputClasses is not null &&
                    !ClassSequenceSurvives(rule.InputSequence, inputClasses)) continue;
                ReadStatus status = RemapLookupRecords(rule.LookupRecords, remap,
                    rule.Location, out var records, out int lookupCount, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ContextualSubstTableCompiler.TryCreateRule(rule.InputSequence,
                    records, lookupCount, out var rebuilt, out diagnostic, budget,
                    rule.Location);
                if (status != ReadStatus.Done) return status;
                rules.Add(rebuilt!);
            }
            ordered[i] = AssembleContextualRuleSet(rules.ToArray(), source.Location);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RewriteChainingSubtable(ChainingSubstTable sub,
        LayoutOutputMap map, BuildBudget budget, out ChainingSubstTable? rewritten,
        out FontDiagnostic diagnostic, LayoutLookupRemap? lookupRemap = null)
    {
        rewritten = null;
        if (sub.Format == 3)
        {
            ReadStatus format3Status = RewriteCoverageList(sub.BacktrackCoverages!, map,
                budget, out var backtrack, out diagnostic);
            if (format3Status != ReadStatus.Done) return format3Status;
            format3Status = RewriteCoverageList(sub.InputCoverages!, map, budget,
                out var input, out diagnostic);
            if (format3Status != ReadStatus.Done) return format3Status;
            format3Status = RewriteCoverageList(sub.LookaheadCoverages!, map, budget,
                out var lookahead, out diagnostic);
            if (format3Status != ReadStatus.Done) return format3Status;
            format3Status = RemapLookupRecords(sub.LookupRecords!.ToArray(),
                lookupRemap, sub.Location, out var records, out _, out diagnostic);
            if (format3Status != ReadStatus.Done) return format3Status;
            return ChainingSubstTableCompiler.TryCreateFormat3(backtrack, input,
                lookahead, records, map.OutputGlyphCount,
                out rewritten, out diagnostic, budget, sub.Location);
        }
        ReadStatus status = RewriteCoverage(sub.Coverage!, map, budget,
            out var coverage, out var ordinalOf, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (sub.Format == 2)
        {
            status = RewriteClassDef(sub.BacktrackClassDef, map, budget,
                out var backtrackDef, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = RewriteClassDef(sub.InputClassDef, map, budget,
                out var inputDef, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = RewriteClassDef(sub.LookaheadClassDef, map, budget,
                out var lookaheadDef, out diagnostic);
            if (status != ReadStatus.Done) return status;
            IReadOnlyList<ChainingRuleSet?> ruleSets = sub.RuleSets!;
            if (lookupRemap is not null || map.HasDroppedGlyphs)
            {
                status = RemapChainingClassRuleSets(ruleSets, lookupRemap, budget,
                    out var remappedSets, out diagnostic,
                    map.HasDroppedGlyphs ? inputDef : null, backtrackDef, lookaheadDef,
                    coverage);
                if (status != ReadStatus.Done) return status;
                ruleSets = remappedSets;
            }
            return ChainingSubstTableCompiler.TryCreate(2, coverage!, backtrackDef!,
                inputDef!, lookaheadDef!, ruleSets, map.OutputGlyphCount,
                out rewritten, out diagnostic, budget, sub.Location);
        }
        status = RewriteChainingRuleSets(sub.RuleSets!, ordinalOf, map, budget,
            out var orderedSets, out diagnostic, lookupRemap);
        if (status != ReadStatus.Done) return status;
        return ChainingSubstTableCompiler.TryCreate(1, coverage!, null, null, null,
            orderedSets, map.OutputGlyphCount, out rewritten, out diagnostic, budget,
            sub.Location);
    }

    private static ReadStatus RewriteChainingRuleSets(
        IReadOnlyList<ChainingRuleSet?> ruleSets, int[] ordinalOf, LayoutOutputMap map,
        BuildBudget budget, out ChainingRuleSet?[] ordered, out FontDiagnostic diagnostic,
        LayoutLookupRemap? lookupRemap = null)
    {
        ordered = new ChainingRuleSet?[ordinalOf.Length];
        for (int newIndex = 0; newIndex < ordered.Length; newIndex++)
        {
            ChainingRuleSet? source = ruleSets[ordinalOf[newIndex]];
            if (source is null) continue; // the spec-NULL slot permutes with its coverage ordinal
            var rules = new List<ChainingRule>(source.RuleCount);
            for (int r = 0; r < source.RuleCount; r++)
            {
                ChainingRule rule = source.Rules[r];
                if (!GlyphSequenceSurvives(rule.BacktrackSequence, map) ||
                    !GlyphSequenceSurvives(rule.InputSequence, map) ||
                    !GlyphSequenceSurvives(rule.LookaheadSequence, map)) continue;
                uint tag = rule.Location.TableTag;
                ReadStatus status = RewriteGlyphSequence(rule.BacktrackSequence, map,
                    tag, out var backtrack, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = RewriteGlyphSequence(rule.InputSequence, map, tag,
                    out var input, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = RewriteGlyphSequence(rule.LookaheadSequence, map, tag,
                    out var lookahead, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = RemapLookupRecords(rule.LookupRecords, lookupRemap,
                    rule.Location, out var records, out int lookupCount,
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ChainingSubstTableCompiler.TryCreateRule(backtrack, input,
                    lookahead, records, lookupCount, out var rewrittenRule,
                    out diagnostic, budget, rule.Location);
                if (status != ReadStatus.Done) return status;
                rules.Add(rewrittenRule!);
            }
            long consumed = ContextualRuleSetLayout.HeaderLength +
                ContextualRuleSetLayout.OffsetStride * rules.Count;
            foreach (var rule in rules) consumed += rule.ConsumedLength;
            ordered[newIndex] = new(rules.ToArray(), source.Location, (int)consumed);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RemapChainingClassRuleSets(
        IReadOnlyList<ChainingRuleSet?> ruleSets, LayoutLookupRemap? remap,
        BuildBudget budget, out ChainingRuleSet?[] ordered, out FontDiagnostic diagnostic,
        ClassDefTable? inputDef = null, ClassDefTable? backtrackDef = null,
        ClassDefTable? lookaheadDef = null, CoverageTable? coverage = null)
    {
        ordered = new ChainingRuleSet?[ruleSets.Count];
        HashSet<ushort>? inputClasses = null, firstClasses = null;
        HashSet<ushort>? backtrackClasses = null, lookaheadClasses = null;
        if (inputDef is not null)
        {
            if (!budget.Work(3L * inputDef.NumGlyphs + coverage!.Glyphs.Length,
                inputDef.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            inputClasses = ClassesInDomain(inputDef);
            backtrackClasses = ClassesInDomain(backtrackDef!);
            lookaheadClasses = ClassesInDomain(lookaheadDef!);
            firstClasses = [];
            foreach (ushort glyph in coverage.Glyphs)
                firstClasses.Add(inputDef.ClassOf(glyph));
        }
        for (int i = 0; i < ordered.Length; i++)
        {
            if (ruleSets[i] is not { } source) continue; // the spec-NULL slot keeps its slot
            if (firstClasses is not null && !firstClasses.Contains((ushort)i)) continue;
            var rules = new List<ChainingRule>(source.RuleCount);
            for (int r = 0; r < source.RuleCount; r++)
            {
                ChainingRule rule = source.Rules[r];
                if (inputClasses is not null &&
                    (!ClassSequenceSurvives(rule.InputSequence, inputClasses) ||
                     !ClassSequenceSurvives(rule.BacktrackSequence, backtrackClasses!) ||
                     !ClassSequenceSurvives(rule.LookaheadSequence, lookaheadClasses!))) continue;
                ReadStatus status = RemapLookupRecords(rule.LookupRecords, remap,
                    rule.Location, out var records, out int lookupCount, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ChainingSubstTableCompiler.TryCreateRule(
                    rule.BacktrackSequence, rule.InputSequence,
                    rule.LookaheadSequence, records, lookupCount, out var rebuilt,
                    out diagnostic, budget, rule.Location);
                if (status != ReadStatus.Done) return status;
                rules.Add(rebuilt!);
            }
            long consumed = ContextualRuleSetLayout.HeaderLength +
                ContextualRuleSetLayout.OffsetStride * rules.Count;
            foreach (var rule in rules) consumed += rule.ConsumedLength;
            ordered[i] = new(rules.ToArray(), source.Location, (int)consumed);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static HashSet<ushort> ClassesInDomain(ClassDefTable classDef)
    {
        var classes = new HashSet<ushort>();
        for (int glyph = 0; glyph < classDef.NumGlyphs; glyph++)
            classes.Add(classDef.ClassOf(glyph));
        return classes;
    }

    private static bool ClassSequenceSurvives(ReadOnlySpan<ushort> sequence,
        HashSet<ushort> classes)
    {
        foreach (ushort value in sequence)
            if (!classes.Contains(value)) return false;
        return true;
    }

    private static bool GlyphSequenceSurvives(ReadOnlySpan<ushort> sequence,
        LayoutOutputMap map)
    {
        foreach (ushort glyph in sequence)
            if (map.OutputOf(glyph) < 0) return false;
        return true;
    }

    private static ReadStatus RewriteGlyphSequence(ReadOnlySpan<ushort> sequence,
        LayoutOutputMap map, uint tag, out ushort[] rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = new ushort[sequence.Length];
        for (int i = 0; i < rewritten.Length; i++)
        {
            int output = map.OutputOf(sequence[i]);
            if (output < 0) return RewriteMiss(map, tag, sequence[i], out diagnostic);
            rewritten[i] = (ushort)output;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RewriteContextualLookup(LookupTable lookup, LayoutOutputMap map,
        GdefLookupContext? context, BuildBudget budget, out LookupTable? rewritten,
        out FontDiagnostic diagnostic, LayoutLookupRemap? lookupRemap = null)
    {
        rewritten = null;
        var source = lookup.Contextuals!;
        var subtables = new ContextualSubstTable[source.Count];
        for (int i = 0; i < subtables.Length; i++)
        {
            ReadStatus status = RewriteContextualSubtable(source[i], map, budget,
out var rewrittenLeaf, out diagnostic, lookupRemap);
                subtables[i] = rewrittenLeaf!;
            if (status != ReadStatus.Done) return status;
        }
        if (lookup.Kind == LayoutLookupKind.Gsub)
        {
            ReadStatus status = LookupTableCompiler.TryCreateContextualSubst(map.OutputGlyphCount,
                subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                out rewritten, out diagnostic, budget.Limits, lookup.Location);
            if (status != ReadStatus.Done) return status;
            diagnostic = default;
            return ReadStatus.Done;
        }
        // GPOS type 7: no kind-parameterized public factory exists (the T4-04 family
        // factory pins kind Gsub), so the GPOS form builds the same model shape
        // in-assembly — the exact TryCreateRuleFamily body, kind Gpos, including the
        // per-child source checks and the flags re-validation against the rebuilt
        // context — consuming the existing public surface only.
        return FinishGposRuleFamilyLookup(lookup, map.OutputGlyphCount, 7, subtables,
            null, context, budget, out rewritten, out diagnostic);
    }

    private static ReadStatus RewriteChainingLookup(LookupTable lookup, LayoutOutputMap map,
        GdefLookupContext? context, BuildBudget budget, out LookupTable? rewritten,
        out FontDiagnostic diagnostic, LayoutLookupRemap? lookupRemap = null)
    {
        rewritten = null;
        var source = lookup.Chainings!;
        var subtables = new ChainingSubstTable[source.Count];
        for (int i = 0; i < subtables.Length; i++)
        {
            ReadStatus status = RewriteChainingSubtable(source[i], map, budget,
out var rewrittenLeaf, out diagnostic, lookupRemap);
                subtables[i] = rewrittenLeaf!;
            if (status != ReadStatus.Done) return status;
        }
        if (lookup.Kind == LayoutLookupKind.Gsub)
        {
            ReadStatus status = LookupTableCompiler.TryCreateChainingSubst(map.OutputGlyphCount,
                subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                out rewritten, out diagnostic, budget.Limits, lookup.Location);
            if (status != ReadStatus.Done) return status;
            diagnostic = default;
            return ReadStatus.Done;
        }
        return FinishGposRuleFamilyLookup(lookup, map.OutputGlyphCount, 8, null,
            subtables, context, budget, out rewritten, out diagnostic);
    }

    /// <summary>The GPOS 7/8 lookup model over rewritten shared-model leaves: the
    /// TryCreateRuleFamily contract mirrored kind-for-kind (count cap, budget, per-
    /// child NumGlyphs/source identity, GPOS source tag, flags validation against the
    /// rebuilt context) — no Tables/Layout surface changes.</summary>
    private static ReadStatus FinishGposRuleFamilyLookup(LookupTable lookup, int numGlyphs,
        ushort lookupType, ContextualSubstTable[]? contextuales, ChainingSubstTable[]? chainings,
        GdefLookupContext? context, BuildBudget budget, out LookupTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = LookupTable.ValidateGposSource(lookup.Location,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (contextuales is { } contextualChildren)
        {
            foreach (var child in contextualChildren)
            {
                if (child.NumGlyphs != numGlyphs ||
                    child.Location.SourceId != lookup.Location.SourceId ||
                    child.Location.FaceIndex != lookup.Location.FaceIndex)
                    return ReadWire.Fail(ReadStatus.InvalidSource,
                        DiagnosticCode.WrongSourceFace,
                        lookup.Location.AtLocal(DataField.None, 0), out diagnostic);
                status = LookupTable.ValidateGposSource(child.Location, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        else
        {
            foreach (var child in chainings!)
            {
                if (child.NumGlyphs != numGlyphs ||
                    child.Location.SourceId != lookup.Location.SourceId ||
                    child.Location.FaceIndex != lookup.Location.FaceIndex)
                    return ReadWire.Fail(ReadStatus.InvalidSource,
                        DiagnosticCode.WrongSourceFace,
                        lookup.Location.AtLocal(DataField.None, 0), out diagnostic);
                status = LookupTable.ValidateGposSource(child.Location, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        var candidate = new LookupTable(LayoutLookupKind.Gpos, numGlyphs, lookupType,
            lookup.Flags, lookup.MarkFilteringSet, [], null, lookup.Location,
            contextuales: contextuales, chainings: chainings);
        status = candidate.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        rewritten = candidate;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- GPOS 2 -------------------------------------------------------------------

    private static ReadStatus RewritePairPosSubtable(PairPosTable sub, LayoutOutputMap map,
        BuildBudget budget, out PairPosTable? rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteCoverage(sub.Coverage, map, budget, out var coverage,
            out var ordinalOf, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (sub.Format == 2)
        {
            bool compactClasses = map.HasDroppedGlyphs &&
                sub.Class1Count > 0 && sub.Class2Count > 0;
            if (!budget.Work(compactClasses ? sub.NumGlyphs + ordinalOf.Length : 0,
                sub.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            var used1 = new bool[sub.Class1Count];
            var used2 = new bool[sub.Class2Count];
            if (compactClasses)
            {
                used1[0] = ordinalOf.Length == 0;
                used2[0] = true;
                foreach (int ordinal in ordinalOf)
                {
                    ushort glyph = sub.Coverage.Glyphs[ordinal];
                    ushort value = sub.ClassDef1!.ClassOf(glyph);
                    if (value >= used1.Length)
                        return ReadWire.Fail(ReadStatus.InvalidIndex,
                            DiagnosticCode.IndexOutOfRange,
                            sub.ClassDef1.Location with
                            { Field = DataField.Payload, RecordOrdinal = glyph },
                            out diagnostic, value, (ulong)used1.Length);
                    used1[value] = true;
                }
                if (ordinalOf.Length != 0)
                    for (int glyph = 0; glyph < sub.NumGlyphs; glyph++)
                    {
                        if (map.OutputOf(glyph) < 0) continue;
                        ushort value = sub.ClassDef2!.ClassOf(glyph);
                        if (value >= used2.Length)
                            return ReadWire.Fail(ReadStatus.InvalidIndex,
                                DiagnosticCode.IndexOutOfRange,
                                sub.ClassDef2.Location with
                                { Field = DataField.Payload, RecordOrdinal = glyph },
                                out diagnostic, value, (ulong)used2.Length);
                        used2[value] = true;
                    }
            }
            else
            {
                Array.Fill(used1, true);
                Array.Fill(used2, true);
            }
            int[] class1Remap = CompactClassIndices(used1, out var sourceClasses1);
            int[] class2Remap = CompactClassIndices(used2, out var sourceClasses2);
            status = RewriteClassDefRanges(sub.ClassDef1, map, out var class1Ranges,
                out ushort class1Format, out diagnostic,
                compactClasses ? class1Remap : default);
            if (status != ReadStatus.Done) return status;
            status = RewriteClassDefRanges(sub.ClassDef2, map, out var class2Ranges,
                out ushort class2Format, out diagnostic,
                compactClasses ? class2Remap : default);
            if (status != ReadStatus.Done) return status;
            ushort classDefFormat = class1Format == 1 && class2Format == 1
                ? (ushort)1 : (ushort)2;
            int class1Count = sourceClasses1.Length, class2Count = sourceClasses2.Length;
            var values1 = new StaticValueRecord[class1Count * class2Count];
            var values2 = new StaticValueRecord[values1.Length];
            for (int class1 = 0; class1 < class1Count; class1++)
            {
                for (int class2 = 0; class2 < class2Count; class2++)
                {
                    status = sub.TryGetMatrixValue(sourceClasses1[class1],
                        sourceClasses2[class2], out values1[class1 * class2Count + class2],
                        out values2[class1 * class2Count + class2], out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
            }
            return PairPosTableCompiler.TryCreate(coverage!.Glyphs, class1Ranges,
                class2Ranges, (ushort)class1Count, (ushort)class2Count,
                values1, values2, map.OutputGlyphCount, sub.ValueFormat1,
                sub.ValueFormat2, out rewritten, out diagnostic, budget, sub.Location,
                coverage.Format, classDefFormat);
        }
        var pairSets = new PairValueRecord[ordinalOf.Length][];
        for (int set = 0; set < pairSets.Length; set++)
        {
            PairSetTable source = sub.PairSets![ordinalOf[set]];
            var records = new List<PairValueRecord>(source.PairValueCount);
            for (int r = 0; r < source.PairValueCount; r++)
            {
                PairValueRecord record = source.Records[r];
                int second = map.OutputOf(record.SecondGlyph);
                if (second < 0) continue;
                records.Add(new PairValueRecord((ushort)second, record.Value1,
                    record.Value2));
            }
            // The strict second-glyph wire order re-answers over the output keys
            // (the kern fmt0 canonical-sort precedent).
            records.Sort(static (a, b) => a.SecondGlyph.CompareTo(b.SecondGlyph));
            pairSets[set] = records.ToArray();
        }
        return PairPosTableCompiler.TryCreate(coverage!.Glyphs, pairSets,
            map.OutputGlyphCount, sub.ValueFormat1, sub.ValueFormat2, out rewritten,
            out diagnostic, budget.Limits, sub.Location, coverage.Format);
    }

    private static int[] CompactClassIndices(bool[] used, out ushort[] sourceClasses)
    {
        var remap = new int[used.Length];
        Array.Fill(remap, -1);
        var kept = new List<ushort>();
        for (int i = 0; i < used.Length; i++)
        {
            if (!used[i]) continue;
            remap[i] = kept.Count;
            kept.Add((ushort)i);
        }
        sourceClasses = kept.ToArray();
        return remap;
    }

    private static ReadStatus RewritePairPosLookup(LookupTable lookup, LayoutOutputMap map,
        GdefLookupContext? context, BuildBudget budget, out LookupTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        var source = lookup.PairPositions!;
        var subtables = new PairPosTable[source.Count];
        for (int i = 0; i < subtables.Length; i++)
        {
            ReadStatus status = RewritePairPosSubtable(source[i], map, budget,
                out var leaf, out diagnostic);
            subtables[i] = leaf!;
            if (status != ReadStatus.Done) return status;
        }
        ReadStatus factoryStatus = LookupTableCompiler.TryCreatePairPos(
            map.OutputGlyphCount, subtables, lookup.Flags, lookup.MarkFilteringSet,
            context, out rewritten, out diagnostic, budget.Limits, lookup.Location);
        if (factoryStatus != ReadStatus.Done) return factoryStatus;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- GPOS 3 -------------------------------------------------------------------

    private static ReadStatus RewriteCursive(CursivePosTable sub, LayoutOutputMap map,
        BuildBudget budget, out CursivePosTable? rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteCoverage(sub.Coverage, map, budget, out var coverage,
            out var ordinalOf, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var ordered = new EntryExitTable[ordinalOf.Length];
        for (int r = 0; r < ordered.Length; r++) ordered[r] = sub.Records[ordinalOf[r]];
        return CursivePosTableCompiler.TryCreate(coverage!.Glyphs, ordered,
            map.OutputGlyphCount, out rewritten, out diagnostic, budget, sub.Location,
            coverage.Format);
    }

    // ---- GPOS 4 / 5 / 6 -----------------------------------------------------------

    private static ReadStatus RewriteMarkBaseSubtable(MarkBasePosTable sub,
        LayoutOutputMap map, BuildBudget budget, out MarkBasePosTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteMarkSide(sub.MarkCoverage, sub.Marks.ToArray(), map,
            budget, out var markGlyphs, out var marks, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RewriteCoverage(sub.BaseCoverage, map, budget, out var baseCoverage,
            out var ordinalOf, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var cells = new AnchorTable?[baseCoverage!.Glyphs.Length * sub.ClassCount];
        for (int b = 0; b < ordinalOf.Length; b++)
        {
            var row = sub.BaseRow(ordinalOf[b]);
            for (int c = 0; c < sub.ClassCount; c++)
                cells[b * sub.ClassCount + c] = row[c];
        }
        return MarkPosTableCompiler.TryCreate(markGlyphs, marks, baseCoverage.Glyphs,
            cells, sub.ClassCount, map.OutputGlyphCount, out rewritten, out diagnostic,
            budget.Limits, sub.Location, baseCoverage.Format);
    }

    private static ReadStatus RewriteMarkLigSubtable(MarkLigPosTable sub,
        LayoutOutputMap map, BuildBudget budget, out MarkLigPosTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteMarkSide(sub.MarkCoverage, sub.Marks.ToArray(), map,
            budget, out var markGlyphs, out var marks, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RewriteCoverage(sub.LigatureCoverage, map, budget, out var ligCoverage,
            out var ordinalOf, out diagnostic,
            i => !map.HasDroppedGlyphs || sub.Ligatures[i].Components > 0);
        if (status != ReadStatus.Done) return status;
        var ligatures = new LigatureAttachTable[ligCoverage!.Glyphs.Length];
        for (int l = 0; l < ligatures.Length; l++)
            ligatures[l] = sub.Ligatures[ordinalOf[l]];
        if (map.HasDroppedGlyphs && ligatures.Length == 0)
        {
            markGlyphs = [];
            marks = [];
        }
        return MarkPosTableCompiler.TryCreateLig(markGlyphs, marks,
            ligCoverage.Glyphs, ligatures, map.OutputGlyphCount, out rewritten,
            out diagnostic, budget.Limits, sub.Location, ligCoverage.Format);
    }

    private static ReadStatus RewriteMarkMarkSubtable(MarkMarkPosTable sub,
        LayoutOutputMap map, BuildBudget budget, out MarkMarkPosTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status = RewriteMarkSide(sub.MarkCoverage, sub.Marks.ToArray(), map,
            budget, out var markGlyphs, out var marks, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RewriteCoverage(sub.Mark2Coverage, map, budget, out var mark2Coverage,
            out var ordinalOf, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var cells = new AnchorTable?[mark2Coverage!.Glyphs.Length * sub.ClassCount];
        for (int m = 0; m < ordinalOf.Length; m++)
        {
            var row = sub.Mark2Row(ordinalOf[m]);
            for (int c = 0; c < sub.ClassCount; c++)
                cells[m * sub.ClassCount + c] = row[c];
        }
        return MarkPosTableCompiler.TryCreateMark(markGlyphs, marks,
            mark2Coverage.Glyphs, cells, sub.ClassCount, map.OutputGlyphCount,
            out rewritten, out diagnostic, budget.Limits, sub.Location,
            mark2Coverage.Format);
    }

    /// <summary>The mark side permutes its records with the mark coverage sort;
    /// anchors carry no GID and ride along.</summary>
    private static ReadStatus RewriteMarkSide(CoverageTable markCoverage,
        MarkRecord[] marks, LayoutOutputMap map, BuildBudget budget, out ushort[] markGlyphs,
        out MarkRecord[] orderedMarks, out FontDiagnostic diagnostic)
    {
        ReadStatus status = RewriteCoverage(markCoverage, map, budget, out var coverage,
            out var ordinalOf, out diagnostic);
        if (status != ReadStatus.Done)
        {
            markGlyphs = [];
            orderedMarks = marks;
            return status;
        }
        markGlyphs = coverage!.Glyphs.ToArray();
        orderedMarks = new MarkRecord[ordinalOf.Length];
        for (int i = 0; i < orderedMarks.Length; i++)
            orderedMarks[i] = marks[ordinalOf[i]];
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RewriteMarkLookup(LookupTable lookup, LayoutOutputMap map,
        GdefLookupContext? context, BuildBudget budget, out LookupTable? rewritten,
        out FontDiagnostic diagnostic)
    {
        rewritten = null;
        ReadStatus status;
        if (lookup.MarkBases is { } markBases)
        {
            var subtables = new MarkBasePosTable[markBases.Count];
            for (int i = 0; i < subtables.Length; i++)
            {
                status = RewriteMarkBaseSubtable(markBases[i], map, budget,
out var rewrittenLeaf, out diagnostic);
                    subtables[i] = rewrittenLeaf!;
                if (status != ReadStatus.Done) return status;
            }
            status = LookupTableCompiler.TryCreateMarkBase(map.OutputGlyphCount,
                subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                out rewritten, out diagnostic, budget.Limits, lookup.Location);
        }
        else if (lookup.MarkLigs is { } markLigs)
        {
            var subtables = new MarkLigPosTable[markLigs.Count];
            for (int i = 0; i < subtables.Length; i++)
            {
                status = RewriteMarkLigSubtable(markLigs[i], map, budget,
out var rewrittenLeaf, out diagnostic);
                    subtables[i] = rewrittenLeaf!;
                if (status != ReadStatus.Done) return status;
            }
            status = LookupTableCompiler.TryCreateMarkLig(map.OutputGlyphCount,
                subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                out rewritten, out diagnostic, budget.Limits, lookup.Location);
        }
        else
        {
            var markMarks = lookup.MarkMarks!;
            var subtables = new MarkMarkPosTable[markMarks.Count];
            for (int i = 0; i < subtables.Length; i++)
            {
                status = RewriteMarkMarkSubtable(markMarks[i], map, budget,
out var rewrittenLeaf, out diagnostic);
                    subtables[i] = rewrittenLeaf!;
                if (status != ReadStatus.Done) return status;
            }
            status = LookupTableCompiler.TryCreateMarkMark(map.OutputGlyphCount,
                subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                out rewritten, out diagnostic, budget.Limits, lookup.Location);
        }
        if (status != ReadStatus.Done) return status;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- GSUB 7 / GPOS 9 (the extension wrappers) ---------------------------------

    /// <summary>The GSUB type-7 wrapper: every wrapped leaf re-runs its own rewriter
    /// and the wrapper re-forms through ExtensionSubstTable.TryCreate — the wrapper
    /// itself carries no GID reference.</summary>
    private static ReadStatus RewriteGsubExtensionLookup(LookupTable lookup,
        LayoutOutputMap map, GdefLookupContext? context, BuildBudget budget,
        out LookupTable? rewritten, out FontDiagnostic diagnostic,
        LayoutLookupRemap? lookupRemap = null)
    {
        rewritten = null;
        var extensions = lookup.Extensions!;
        var subtables = new ExtensionSubstTable[extensions.Count];
        for (int i = 0; i < subtables.Length; i++)
        {
            ExtensionSubstTable ext = extensions[i];
            ushort type = ext.ExtensionLookupType;
            ReadStatus status;
            switch (type)
            {
                case 1:
                    status = RewriteSingleSubstSubtable(ext.Single!, map, budget,
                        out var single, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionSubstTable.TryCreate(type, single, null, null,
                        null, out var rewritten1, out diagnostic, ext.Location);
                    subtables[i] = rewritten1!;
                    break;
                case 2:
                    status = RewriteMultiple(ext.Multiple!, map, budget,
                        out var multiple, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionSubstTable.TryCreate(type, null, multiple, null,
                        null, out var rewritten2, out diagnostic, ext.Location);
                    subtables[i] = rewritten2!;
                    break;
                case 3:
                    status = RewriteAlternate(ext.Alternate!, map, budget,
                        out var alternate, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionSubstTable.TryCreate(type, null, null, alternate,
                        null, out var rewritten3, out diagnostic, ext.Location);
                    subtables[i] = rewritten3!;
                    break;
                case 4:
                    status = RewriteLigature(ext.Ligature!, map, budget,
                        out var ligature, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionSubstTable.TryCreate(type, null, null, null,
                        ligature, out var rewritten4, out diagnostic, ext.Location);
                    subtables[i] = rewritten4!;
                    break;
                case 5:
                    status = RewriteContextualSubtable(ext.Contextual!, map, budget,
                        out var contextual, out diagnostic, lookupRemap);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionSubstTable.TryCreate(type, null, null, null,
                        null, out var rewritten5, out diagnostic, ext.Location,
                        contextual: contextual);
                    subtables[i] = rewritten5!;
                    break;
                case 6:
                    status = RewriteChainingSubtable(ext.Chaining!, map, budget,
                        out var chaining, out diagnostic, lookupRemap);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionSubstTable.TryCreate(type, null, null, null,
                        null, out var rewritten6, out diagnostic, ext.Location,
                        chaining: chaining);
                    subtables[i] = rewritten6!;
                    break;
                case 8:
                    status = RewriteReverseChain(ext.ReverseChain!, map, budget,
                        out var reverseChain, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionSubstTable.TryCreate(type, null, null, null,
                        null, out var rewritten8, out diagnostic, ext.Location,
                        reverseChain: reverseChain);
                    subtables[i] = rewritten8!;
                    break;
                default:
                    status = UnsupportedLookupType(lookup, out diagnostic);
                    break;
            }
            if (status != ReadStatus.Done) return status;
        }
        ReadStatus factoryStatus = LookupTableCompiler.TryCreateExtensionSubst(
            map.OutputGlyphCount, subtables, lookup.Flags, lookup.MarkFilteringSet,
            context, out rewritten, out diagnostic, budget, lookup.Location);
        if (factoryStatus != ReadStatus.Done) return factoryStatus;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The GPOS type-9 wrapper: the wrapped leaf re-runs its rewriter and
    /// the wrapper re-forms through ExtensionPosTable.TryCreate; the lookup model
    /// then takes the in-assembly GPOS-kind construction (the same mirror the GPOS
    /// 7/8 family uses — no kind-parameterized public factory exists for GPOS 9),
    /// with the flags re-validated against the rebuilt context.</summary>
    private static ReadStatus RewriteGposExtensionLookup(LookupTable lookup,
        LayoutOutputMap map, GdefLookupContext? context, BuildBudget budget,
        out LookupTable? rewritten, out FontDiagnostic diagnostic,
        LayoutLookupRemap? lookupRemap = null)
    {
        rewritten = null;
        var extensions = lookup.ExtensionPositions!;
        var subtables = new ExtensionPosTable[extensions.Count];
        for (int i = 0; i < subtables.Length; i++)
        {
            ExtensionPosTable ext = extensions[i];
            ushort type = ext.ExtensionLookupType;
            ReadStatus status;
            switch (type)
            {
                case 1:
                    status = RewriteSinglePosSubtable(ext.Single!, map, budget,
                        out var single, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionPosTable.TryCreate(type, single, null, null,
                        out var rewrittenPos1, out diagnostic, ext.Location);
                    subtables[i] = rewrittenPos1!;
                    break;
                case 2:
                    status = RewritePairPosSubtable(ext.Pair!, map, budget,
                        out var pair, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionPosTable.TryCreate(type, null, pair, null,
                        out var rewrittenPos2, out diagnostic, ext.Location);
                    subtables[i] = rewrittenPos2!;
                    break;
                case 3:
                    status = RewriteCursive(ext.Cursive!, map, budget,
                        out var cursive, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionPosTable.TryCreate(type, null, null, cursive,
                        out var rewrittenPos3, out diagnostic, ext.Location);
                    subtables[i] = rewrittenPos3!;
                    break;
                case 4:
                    status = RewriteMarkBaseSubtable(ext.MarkBase!, map, budget,
                        out var markBase, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionPosTable.TryCreate(type, null, null, null,
                        out var rewrittenPos4, out diagnostic, ext.Location,
                        markBase: markBase);
                    subtables[i] = rewrittenPos4!;
                    break;
                case 5:
                    status = RewriteMarkLigSubtable(ext.MarkLig!, map, budget,
                        out var markLig, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionPosTable.TryCreate(type, null, null, null,
                        out var rewrittenPos5, out diagnostic, ext.Location,
                        markLig: markLig);
                    subtables[i] = rewrittenPos5!;
                    break;
                case 6:
                    status = RewriteMarkMarkSubtable(ext.MarkMark!, map, budget,
                        out var markMark, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionPosTable.TryCreate(type, null, null, null,
                        out var rewrittenPos6, out diagnostic, ext.Location,
                        markMark: markMark);
                    subtables[i] = rewrittenPos6!;
                    break;
                case 7:
                    status = RewriteContextualSubtable(ext.Contextual!, map, budget,
                        out var contextual, out diagnostic, lookupRemap);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionPosTable.TryCreate(type, null, null, null,
                        out var rewrittenPos7, out diagnostic, ext.Location,
                        contextual: contextual);
                    subtables[i] = rewrittenPos7!;
                    break;
                case 8:
                    status = RewriteChainingSubtable(ext.Chaining!, map, budget,
                        out var chaining, out diagnostic, lookupRemap);
                    if (status != ReadStatus.Done) return status;
                    status = ExtensionPosTable.TryCreate(type, null, null, null,
                        out var rewrittenPos8, out diagnostic, ext.Location,
                        chaining: chaining);
                    subtables[i] = rewrittenPos8!;
                    break;
                default:
                    status = UnsupportedLookupType(lookup, out diagnostic);
                    break;
            }
            if (status != ReadStatus.Done) return status;
        }
        ReadStatus sourceStatus = LookupTable.ValidateGposSource(lookup.Location,
            out diagnostic);
        if (sourceStatus != ReadStatus.Done) return sourceStatus;
        var candidate = new LookupTable(LayoutLookupKind.Gpos, map.OutputGlyphCount, 9,
            lookup.Flags, lookup.MarkFilteringSet, [], null, lookup.Location,
            extensionPositions: subtables);
        ReadStatus flagsStatus = candidate.ValidateFlags(context, out diagnostic, budget);
        if (flagsStatus != ReadStatus.Done) return flagsStatus;
        rewritten = candidate;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---------------------------------------------------------------------
    // kern (S2b-2 card §2.3, merged commit a3bccb6): the v0 pair/class rewrite
    // over the same frozen map and RF-S2b-2 refusal contract as the layout lists
    // above; its defensive refusals carry the input GID in Detail
    // ("layout / rewrite / kern glyph <id>"), keyed by the kern family's own
    // RewriteInvariantRefusal — the layout lists keep their coverage-keyed
    // RewriteMiss. The MATH/JSTF producers join in their commits.
    // ---------------------------------------------------------------------
    /// <summary>Compact kern rewrite (S2b card §2.3): the v0 dialect's format-0 pair
    /// graphs and format-2 class matrices re-emit over the map's output domain. The
    /// Apple dialect never reaches here — the decide stage refuses it (RF-S2b-3) —
    /// and raw subtables never reach admission (RF-C), so every subtable below is a
    /// typed v0 body. Subtable order (the declaration order) and pair/class semantics
    /// are identity: the factories' canonical forms absorb the renumbering (format 0
    /// re-sorts into the strict pair order; format 2 re-premultiplies its class words
    /// for the canonical layout), so an identity map re-emits a canonical source
    /// byte-identically and a remapped one wire-equivalently.</summary>
    private static ReadStatus RebuildKern(KernTable kern, GlyphRemap map,
        BuildBudget budget, List<LayoutRewriteCensusRow> rewrites,
        out byte[]? kernBytes, out FontDiagnostic diagnostic)
    {
        kernBytes = null;
        int inputSubtables = kern.SubtableCount;
        int outputGlyphs = map.OutputGlyphCount;
        var outputMap = LayoutOutputMap.Frozen(map);
        var subtables = new KernSubtable[kern.SubtableCount];
        for (int i = 0; i < kern.SubtableCount; i++)
        {
            KernSubtable subtable = kern.Subtables[i];
            ReadStatus status;
            KernSubtable? rebuilt;
            switch (subtable.Body)
            {
                case KernFormat0Body format0:
                    status = RebuildKernFormat0(kern.Location, subtable, format0,
                        outputMap, outputGlyphs, out rebuilt, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    subtables[i] = rebuilt!;
                    break;
                case KernFormat2Body format2:
                    status = RebuildKernFormat2(kern.Location, subtable, format2,
                        outputMap, outputGlyphs, out rebuilt, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    subtables[i] = rebuilt!;
                    break;
                default:
                    // Unreachable: admission refused raw bodies (RF-C) and decide
                    // refused the Apple dialect (RF-S2b-3); the switch stays total
                    // so an orchestration drift cannot pass a raw body silently.
                    return RewriteInvariantRefusal(
                        kern.Location.At(DataField.GlyphId, (ulong)i, i),
                        (ulong)subtable.Format, out diagnostic);
            }
        }
        // The root factory re-answers the pair GID domain against THIS root's
        // explicit output numGlyphs (the read face's hard half, re-answered) and
        // SameSource over every subtable; the count-identity invariant (zero rule
        // loss, S2b card §3) is checked on the rebuilt model below.
        ReadStatus rootStatus = KernTableCompiler.TryCreate(KernDialect.Version0,
            outputGlyphs, subtables, out KernTable? model, out diagnostic,
            location: kern.Location);
        if (rootStatus != ReadStatus.Done) return rootStatus;
        if (model!.SubtableCount != kern.SubtableCount)
            return RewriteInvariantRefusal(
                kern.Location.At(DataField.Count, (ulong)kern.SubtableCount),
                (ulong)model.SubtableCount, out diagnostic);
        rootStatus = KernTableCompiler.TryCompile(model, out var bytes,
            out diagnostic);
        if (rootStatus != ReadStatus.Done) return rootStatus;
        rewrites.Add(RewriteCensusRow(KernLayout.Tag, inputSubtables,
            model.SubtableCount, kern.ConsumedLength, bytes!.Length));
        return ChargeCompiled(budget, kern.Location, bytes, out kernBytes,
            out diagnostic);
    }

    private static ReadStatus RebuildKernFormat0(DataLocation at, KernSubtable subtable,
        KernFormat0Body format0, LayoutOutputMap map, int outputGlyphs,
        out KernSubtable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        var pairs = new KernPair[format0.PairCount];
        for (int p = 0; p < format0.PairCount; p++)
        {
            KernPair pair = format0.Pairs[p];
            ReadStatus status = RemapGlyphId(at, pair.Left, map, out ushort left,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = RemapGlyphId(at, pair.Right, map, out ushort right,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            pairs[p] = new(left, right, pair.Value);
        }
        // The factory sorts into the canonical strict order (duplicates refuse —
        // unreachable under the injective map) and re-answers both GID domains
        // against the output context; the flags byte rides verbatim (the read face
        // already refused the reserved bits).
        ReadStatus create = KernSubtableCompiler.TryCreateFormat0(outputGlyphs,
            pairs, KernFlagsByte(subtable), out rebuilt, out diagnostic,
            location: at);
        if (create != ReadStatus.Done) return create;
        if (((KernFormat0Body)rebuilt!.Body).PairCount != format0.PairCount)
            return RewriteInvariantRefusal(
                at.At(DataField.Count, (ulong)format0.PairCount),
                (ulong)((KernFormat0Body)rebuilt.Body).PairCount, out diagnostic);
        return ReadStatus.Done;
    }

    private static ReadStatus RebuildKernFormat2(DataLocation at, KernSubtable subtable,
        KernFormat2Body format2, LayoutOutputMap map, int outputGlyphs,
        out KernSubtable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        // A class matrix whose words do not follow the premultiplication convention
        // has no decodable matrix — the write face refuses it, and this rewrite
        // refuses it first under the same code-23 contract (the matrix is not
        // expressible for re-emission; retain keeps such bodies raw).
        if (!format2.Canonicalizable || format2.LeftIndices is null ||
            format2.RightIndices is null)
            return RewriteInvariantRefusal(
                at.AtLocal(DataField.Offset,
                    (ulong)KernSubtable2Layout.LeftClassOffsetAt),
                (ulong)subtable.Format, out diagnostic);
        // The domain-continuity invariant (S2b card §2.3): the closure merged the
        // whole covered range [First, First+GlyphCount) on both axes
        // (AddClassRange), and the frozen map is monotone over the retained set, so
        // the image is the contiguous range [First', First'+GlyphCount) — verified
        // here cell by cell, never assumed (a broken domain cannot be stated by a
        // class matrix).
        ReadStatus status = RemapContiguousClassDomain(at,
            format2.LeftClassTable, map, out int leftFirst, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RemapContiguousClassDomain(at, format2.RightClassTable, map,
            out int rightFirst, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int leftCount = format2.LeftClassTable.GlyphCount;
        int rightCount = format2.RightClassTable.GlyphCount;
        // The decoded matrix travels verbatim: the premultiplied class words decode
        // to per-glyph class indices (the read face's own decode) and the cells to
        // a row-major value matrix; only the two firstGlyph words remap.
        var values = new short[leftCount * rightCount];
        for (int i = 0; i < leftCount; i++)
            for (int j = 0; j < rightCount; j++)
                values[i * rightCount + j] = format2.ValueAt(i, j);
        status = KernSubtableCompiler.TryCreateFormat2(outputGlyphs,
            (ushort)leftFirst, format2.LeftIndices, (ushort)rightFirst,
            format2.RightIndices, values, KernFlagsByte(subtable), out rebuilt,
            out diagnostic, location: at);
        if (status != ReadStatus.Done) return status;
        var body = (KernFormat2Body)rebuilt!.Body;
        if (body.LeftClassTable.GlyphCount != leftCount ||
            body.RightClassTable.GlyphCount != rightCount)
            return RewriteInvariantRefusal(
                at.At(DataField.Count, (ulong)leftCount),
                (ulong)((body.LeftClassTable.GlyphCount << 16) |
                        body.RightClassTable.GlyphCount), out diagnostic);
        return ReadStatus.Done;
    }

    /// <summary>The subtable's v0 flags byte: the coverage word's low half (the
    /// format rides the high byte), masked to the read face's legal flag set —
    /// the narrowing must never pass the format bits.</summary>
    private static byte KernFlagsByte(KernSubtable subtable) =>
        (byte)(subtable.Coverage & KernLayout.Flags0LegalMask);

    /// <summary>One covered kern class range maps to a contiguous output range: the
    /// first glyph names the image start and every ordinal must follow +1 (the
    /// monotone frozen map over the closure-merged range). A miss or a gap is the
    /// domain-broken refusal (RF-S2b-2).</summary>
    private static ReadStatus RemapContiguousClassDomain(DataLocation at,
        KernClassTable classTable, LayoutOutputMap map, out int remappedFirst,
        out FontDiagnostic diagnostic)
    {
        remappedFirst = 0;
        int first = classTable.FirstGlyph;
        ReadStatus status = RemapGlyphId(at, (ushort)first, map, out ushort firstOut,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        remappedFirst = firstOut;
        for (int k = 1; k < classTable.GlyphCount; k++)
        {
            int mapped = map.OutputOf(first + k);
            if (mapped != firstOut + k)
                return RewriteInvariantRefusal(
                    at.At(DataField.GlyphId, (ulong)(first + k)), (ulong)mapped,
                    out diagnostic);
        }
        return ReadStatus.Done;
    }

    /// <summary>Same-assembly remap read: the caller's GID comes from a model read
    /// under the same face's numGlyphs domain, so the array access is in-bounds by
    /// construction and only the membership half can fail (the closure keeps every
    /// retained reference mapped; a miss is the RF-S2b-2 refusal).</summary>
    private static ReadStatus RemapGlyphId(DataLocation at, int inputGlyph,
        LayoutOutputMap map, out ushort outputGlyph, out FontDiagnostic diagnostic)
    {
        outputGlyph = 0;
        int mapped = map.OutputOf(inputGlyph);
        if (mapped < 0)
            return RewriteInvariantRefusal(at.At(DataField.GlyphId,
                (ulong)inputGlyph), (ulong)inputGlyph, out diagnostic);
        outputGlyph = (ushort)mapped;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The defensive-invariant refusal (RF-S2b-2, S2b card §5): the
    /// code-23 contract with the input GID (or the drifted count) in Detail —
    /// "layout / rewrite / kern glyph <id>".</summary>
    private static ReadStatus RewriteInvariantRefusal(DataLocation at,
        ulong detail, out FontDiagnostic diagnostic)
    {
        diagnostic = new(DiagnosticCode.LayoutFamilyUnsupported, at, detail);
        return ReadStatus.UnsupportedFormat;
    }

    /// <summary>Test bridge for the kern rewrite (the remap-miss invariant's
    /// non-vacuity proof: the closure keeps the invariant unreachable through the
    /// pipeline, so the unit test drives the producer directly — the S2a card §3
    /// layer-1 registration shape).</summary>
    internal static ReadStatus RebuildKernForTests(KernTable kern, GlyphRemap map,
        out byte[]? kernBytes, out FontDiagnostic diagnostic,
        List<LayoutRewriteCensusRow>? rewrites = null) =>
        RebuildKern(kern, map, new BuildBudget(new BuildLimits()),
            rewrites ?? [], out kernBytes, out diagnostic);

    internal static ReadStatus RebuildMathForTests(MathTable math, GlyphRemap map,
        out byte[]? mathBytes, out FontDiagnostic diagnostic,
        List<LayoutRewriteCensusRow>? rewrites = null) =>
        RebuildMath(math, map, new BuildBudget(new BuildLimits()),
            rewrites ?? [], out mathBytes, out diagnostic);

    internal static ReadStatus RebuildJstfForTests(JstfTable jstf, GlyphRemap map,
        out byte[]? jstfBytes, out FontDiagnostic diagnostic,
        List<LayoutRewriteCensusRow>? rewrites = null) =>
        RebuildJstf(jstf, new PrepareState(SubsetMode.CompactRemap,
            new BuildBudget(new BuildLimits())), map,
            new BuildBudget(new BuildLimits()), rewrites ?? [], out jstfBytes,
            out diagnostic);

    internal static ReadStatus RebuildBaseForTests(BaseTable baseModel, GlyphRemap map,
        out byte[]? baseBytes, out FontDiagnostic diagnostic,
        List<LayoutRewriteCensusRow>? rewrites = null) =>
        RebuildBase(baseModel, map, new BuildBudget(new BuildLimits()),
            rewrites ?? [], out baseBytes, out diagnostic);

    // ---------------------------------------------------------------------
    // MATH (S2b card §2.5, commit ⑥): the six coverage faces and the hard
    // per-glyph references remap; the constants body, the kern values and every
    // device slot carry no GID and travel as the admission model's own
    // instances (the shared-identity rule — reusing the instances keeps the
    // dedupe facts the read materialized, and their MATH-tagged locations
    // satisfy the root factory's SameSource walk).
    // ---------------------------------------------------------------------

    private static ReadStatus RebuildMath(MathTable math, GlyphRemap map,
        BuildBudget budget, List<LayoutRewriteCensusRow> rewrites,
        out byte[]? mathBytes, out FontDiagnostic diagnostic)
    {
        mathBytes = null;
        int inputConstructions = (math.Variants?.VertGlyphCount ?? 0) +
            (math.Variants?.HorizGlyphCount ?? 0);
        int outputGlyphs = map.OutputGlyphCount;
        var outputMap = LayoutOutputMap.Frozen(map);
        MathGlyphInfoTable? glyphInfo = null;
        if (math.GlyphInfo is { } info)
        {
            MathItalicsCorrectionInfoTable? italics = null;
            if (info.ItalicsCorrectionInfo is { } italicsModel)
            {
                ReadStatus status = RebuildCoverage(math.Location,
                    italicsModel.Coverage, outputMap, outputGlyphs, out var coverage,
                    out var italicsOrdinal, out diagnostic);
                if (status != ReadStatus.Done) return status;
                // The "Should" family keeps its records and device slots verbatim,
                // riding their coverage's sort.
                status = MathTableCompiler.TryCreate(coverage,
                    Permute(italicsModel.Records, italicsOrdinal),
                    italicsModel.Devices is null
                        ? null
                        : Permute(italicsModel.Devices, italicsOrdinal),
                    out italics, out diagnostic, location: math.Location);
                if (status != ReadStatus.Done) return status;
            }
            MathTopAccentAttachmentTable? topAccent = null;
            if (info.TopAccentAttachment is { } topAccentModel)
            {
                ReadStatus status = RebuildCoverage(math.Location,
                    topAccentModel.Coverage, outputMap, outputGlyphs, out var coverage,
                    out var accentOrdinal, out diagnostic);
                if (status != ReadStatus.Done) return status;
                // The "Must" coincidence re-derives at the factory; the injective
                // map keeps the remapped coverage's length equal to the records'.
                status = MathTableCompiler.TryCreate(coverage,
                    Permute(topAccentModel.Records, accentOrdinal),
                    topAccentModel.Devices is null
                        ? null
                        : Permute(topAccentModel.Devices, accentOrdinal),
                    out topAccent, out diagnostic, location: math.Location);
                if (status != ReadStatus.Done) return status;
            }
            ReadStatus extendedStatus = RebuildCoverage(math.Location,
                info.ExtendedShapeCoverage, outputMap, outputGlyphs, out var extended,
                out _, out diagnostic);
            if (extendedStatus != ReadStatus.Done) return extendedStatus;
            MathKernInfoTable? kernInfo = null;
            if (info.KernInfo is { } kernModel)
            {
                ReadStatus status = RebuildCoverage(math.Location,
                    kernModel.Coverage, outputMap, outputGlyphs, out var coverage,
                    out var kernOrdinal, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = MathTableCompiler.TryCreate(coverage,
                    Permute(kernModel.Records, kernOrdinal), out kernInfo,
                    out diagnostic, location: math.Location);
                if (status != ReadStatus.Done) return status;
            }
            glyphInfo = new(italics, topAccent, extended, kernInfo, math.Location,
                0);
        }
        MathVariantsTable? variants = null;
        if (math.Variants is { } variantsModel)
        {
            ReadStatus status = RebuildCoverage(math.Location,
                variantsModel.VertGlyphCoverage, outputMap, outputGlyphs,
                out var vertCoverage, out var vertOrdinal, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = RebuildCoverage(math.Location,
                variantsModel.HorizGlyphCoverage, outputMap, outputGlyphs,
                out var horizCoverage, out var horizOrdinal, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // The construction rows are the coverages' parallel payloads and ride
            // their sort; each row's inner variant/part references remap in place.
            status = RebuildConstructions(
                Permute(variantsModel.VertConstructions, vertOrdinal), outputMap,
                outputGlyphs, out var vertConstructions, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = RebuildConstructions(
                Permute(variantsModel.HorizConstructions, horizOrdinal), outputMap,
                outputGlyphs, out var horizConstructions, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = MathTableCompiler.TryCreate(
                variantsModel.MinConnectorOverlap, vertCoverage, horizCoverage,
                vertConstructions, horizConstructions, out variants,
                out diagnostic, location: math.Location);
            if (status != ReadStatus.Done) return status;
        }
        ReadStatus rootStatus = MathTableCompiler.TryCreate(math.Version,
            outputGlyphs, math.Constants, glyphInfo, variants,
            out MathTable? model, out diagnostic, location: math.Location);
        if (rootStatus != ReadStatus.Done) return rootStatus;
        // Count identity (S2b card §3): the construction arrays are the coverage
        // arrays' image (both factories re-derive the MUST relations), so the
        // variant counts close the zero-rule-loss assertion.
        if (model!.Variants is { } newVariants && math.Variants is { } oldVariants
            && (newVariants.VertGlyphCount != oldVariants.VertGlyphCount ||
                newVariants.HorizGlyphCount != oldVariants.HorizGlyphCount))
            return RewriteInvariantRefusal(
                math.Location.At(DataField.Count,
                    (ulong)MathVariantsLayout.VertGlyphCountAt),
                (ulong)((newVariants.VertGlyphCount << 16) |
                        newVariants.HorizGlyphCount), out diagnostic);
        rootStatus = MathTableCompiler.TryCompile(model, out var bytes,
            out diagnostic);
        if (rootStatus != ReadStatus.Done) return rootStatus;
        int outputConstructions = (model!.Variants?.VertGlyphCount ?? 0) +
            (model.Variants?.HorizGlyphCount ?? 0);
        rewrites.Add(RewriteCensusRow(MathLayout.Tag, inputConstructions,
            outputConstructions, math.ConsumedLength, bytes!.Length));
        return ChargeCompiled(budget, math.Location, bytes, out mathBytes,
            out diagnostic);
    }

    private static ReadStatus RebuildConstructions(
        IReadOnlyList<MathGlyphConstructionTable> constructions, LayoutOutputMap map,
        int outputGlyphs, out MathGlyphConstructionTable[] rebuilt,
        out FontDiagnostic diagnostic)
    {
        rebuilt = new MathGlyphConstructionTable[constructions.Count];
        diagnostic = default;
        for (int i = 0; i < constructions.Count; i++)
        {
            MathGlyphConstructionTable construction = constructions[i];
            var variantRecords = new MathGlyphVariantRecord[construction.VariantCount];
            for (int v = 0; v < construction.VariantCount; v++)
            {
                MathGlyphVariantRecord variant = construction.Variants[v];
                ReadStatus status = RemapGlyphId(construction.Location,
                    variant.VariantGlyph, map, out ushort variantGlyph,
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
                variantRecords[v] = new(variantGlyph, variant.AdvanceMeasurement);
            }
            MathGlyphAssemblyTable? assembly = null;
            if (construction.Assembly is { } assemblyModel)
            {
                var parts = new GlyphPartRecord[assemblyModel.PartCount];
                for (int p = 0; p < assemblyModel.PartCount; p++)
                {
                    GlyphPartRecord part = assemblyModel.Parts[p];
                    ReadStatus status = RemapGlyphId(assemblyModel.Location,
                        part.GlyphId, map, out ushort partGlyph, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    parts[p] = part with { GlyphId = partGlyph };
                }
                // The italics correction record and its device slot carry no GID.
                ReadStatus create = MathTableCompiler.TryCreate(
                    assemblyModel.ItalicsCorrection,
                    assemblyModel.ItalicsCorrectionDevice, parts, outputGlyphs,
                    out assembly, out diagnostic,
                    location: assemblyModel.Location);
                if (create != ReadStatus.Done) return create;
            }
            ReadStatus built = MathTableCompiler.TryCreate(assembly,
                variantRecords, outputGlyphs,
                out MathGlyphConstructionTable? rebuiltConstruction,
                out diagnostic, location: construction.Location);
            if (built != ReadStatus.Done) return built;
            rebuilt[i] = rebuiltConstruction!;
            if (rebuilt[i]!.VariantCount != construction.VariantCount)
                return RewriteInvariantRefusal(
                    construction.Location.At(DataField.Count,
                        (ulong)construction.VariantCount),
                    (ulong)rebuilt[i]!.VariantCount, out diagnostic);
        }
        return ReadStatus.Done;
    }

    /// <summary>One coverage's image: the keys remap into the output domain and sort
    /// ascending (the wire's strict order) — the compact map's output order can
    /// permute the input order on a CFF1 face, so the in-place image is not the
    /// sorted one — and <paramref name="ordinalOf"/> carries the permutation —
    /// ordinalOf[newIndex] = source ordinal — so the caller can ride the parallel
    /// payloads (an identity permutation on the TrueType monotone order). A miss is
    /// the RF-S2b-2 refusal.</summary>
    private static ReadStatus RebuildCoverage(DataLocation at, CoverageTable? coverage,
        LayoutOutputMap map, int outputGlyphs, out CoverageTable? rebuilt,
        out int[] ordinalOf, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        ordinalOf = [];
        if (coverage is null)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        var glyphs = new ushort[coverage.Glyphs.Length];
        ordinalOf = new int[coverage.Glyphs.Length];
        for (int i = 0; i < glyphs.Length; i++)
        {
            ReadStatus status = RemapGlyphId(at, coverage.Glyphs[i], map,
                out glyphs[i], out diagnostic);
            if (status != ReadStatus.Done) return status;
            ordinalOf[i] = i;
        }
        // The frozen map is injective, so the keys are distinct and the sort is total.
        Array.Sort(glyphs, ordinalOf);
        ReadStatus create = CoverageTableCompiler.TryCreate(glyphs, outputGlyphs,
            coverage.Format, out rebuilt, out diagnostic, location: at);
        if (create != ReadStatus.Done) return create;
        if (rebuilt!.Glyphs.Length != glyphs.Length)
            return RewriteInvariantRefusal(at.At(DataField.Count,
                (ulong)glyphs.Length), (ulong)rebuilt.Glyphs.Length,
                out diagnostic);
        return ReadStatus.Done;
    }

    /// <summary>The parallel payloads ride their coverage's sort: the element at
    /// source ordinal <c>ordinalOf[i]</c> lands at sorted position i (the
    /// RewriteCoverage contract the MATH and JSTF-inline producers share).</summary>
    private static T[] Permute<T>(IReadOnlyList<T> source, int[] ordinalOf)
    {
        var permuted = new T[ordinalOf.Length];
        for (int i = 0; i < permuted.Length; i++) permuted[i] = source[ordinalOf[i]];
        return permuted;
    }


    // ---------------------------------------------------------------------
    // BASE (the S2b-2 registered upgrade delivered, OQ-S2b-6): the fmt2 coord's
    // referenceGlyph is the table's one GID-reference face and the closure already
    // merges every one of them in both modes (CollectBaseReferences), so the
    // compact rewrite only substitutes those leaves and re-emits the surviving
    // axis trees through the existing BaseTableCompiler. The fmt1/fmt3 leaves,
    // the tag lists and the v1.1 item variation store carry no GID and ride as
    // the admission model's own instances (the shared-identity rule — reusing
    // them keeps the dedupe facts the read materialized, and SameBody re-derives
    // the source wire's shared placements). An aggregate node whose every
    // descendant kept its instance keeps its own, so the rebuild touches only
    // the chains a fmt2 leaf roots. S2c's script cut never reaches here: BASE is
    // a scope-transparent row (OQ-S2c-9) and the decide stage sends the whole
    // tree, both axes, unpruned.
    // ---------------------------------------------------------------------

    /// <summary>Compact BASE rewrite: every BaseCoordFormat2's referenceGlyph
    /// remaps over the frozen map (the closure merged each one before the map
    /// froze, so a miss is the RF-S2b-2 refusal, never a silent strand), the
    /// coordinate/point words and every non-fmt2 leaf travel verbatim, and the
    /// coord factory re-answers each substituted reference against the output
    /// numGlyphs. The census row counts the table's GID-bearing leaves — the
    /// fmt2 coord slots — which are structure-identical in and out: the rewrite
    /// moves reference words, never slots (the zero-slot-loss assertion).</summary>
    private static ReadStatus RebuildBase(BaseTable baseModel, GlyphRemap map,
        BuildBudget budget, List<LayoutRewriteCensusRow> rewrites,
        out byte[]? baseBytes, out FontDiagnostic diagnostic)
    {
        baseBytes = null;
        int inputCoords = BaseFormat2CoordCount(baseModel);
        var outputMap = LayoutOutputMap.Frozen(map);
        int outputGlyphs = map.OutputGlyphCount;
        var coordMemo = new Dictionary<BaseCoordTable, BaseCoordTable>();
        ReadStatus status = RebuildBaseAxis(baseModel.HorizAxis, outputMap,
            outputGlyphs, coordMemo, out var horiz, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RebuildBaseAxis(baseModel.VertAxis, outputMap, outputGlyphs,
            coordMemo, out var vert, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = BaseTableCompiler.TryCreate(baseModel.Version, outputGlyphs,
            horiz, vert, baseModel.ItemVariationStore, out BaseTable? model,
            out diagnostic, location: baseModel.Location);
        if (status != ReadStatus.Done) return status;
        int outputCoords = BaseFormat2CoordCount(model!);
        if (outputCoords != inputCoords)
            return RewriteInvariantRefusal(
                baseModel.Location.At(DataField.Count, (ulong)inputCoords),
                (ulong)outputCoords, out diagnostic);
        status = BaseTableCompiler.TryCompile(model!, out var bytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        rewrites.Add(RewriteCensusRow(BaseLayout.Tag, inputCoords, outputCoords,
            baseModel.ConsumedLength, bytes!.Length));
        return ChargeCompiled(budget, baseModel.Location, bytes, out baseBytes,
            out diagnostic);
    }

    // Bottom-up subtree rebuild over one memo: a node whose every descendant
    // kept its instance keeps its own (the R7 shared-body facts ride reference
    // identity into the root's SameSource walk), and one remapped fmt2 leaf
    // re-roots exactly its own chain. The memo is shared across both axes, so a
    // coord shared horiz→vert stays one instance after the substitution.
    private static ReadStatus RebuildBaseAxis(BaseAxisTable? axis, LayoutOutputMap map,
        int outputGlyphs, Dictionary<BaseCoordTable, BaseCoordTable> coordMemo,
        out BaseAxisTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = axis;
        if (axis is null)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        var scripts = new BaseScriptRecord[axis.ScriptCount];
        bool changed = false;
        for (int i = 0; i < axis.ScriptCount; i++)
        {
            (uint tag, BaseScriptTable script) = axis.Scripts[i];
            ReadStatus status = RebuildBaseScript(script, map, outputGlyphs,
                coordMemo, out var rebuiltScript, out diagnostic);
            if (status != ReadStatus.Done) return status;
            scripts[i] = new(tag, rebuiltScript!);
            changed |= !ReferenceEquals(script, rebuiltScript);
        }
        if (!changed)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        rebuilt = new(axis.TagList, scripts, axis.Location, axis.ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RebuildBaseScript(BaseScriptTable script,
        LayoutOutputMap map, int outputGlyphs,
        Dictionary<BaseCoordTable, BaseCoordTable> coordMemo,
        out BaseScriptTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        ReadStatus status = RebuildBaseValues(script.BaseValues, map, outputGlyphs,
            coordMemo, out var values, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RebuildBaseMinMax(script.DefaultMinMax, map, outputGlyphs,
            coordMemo, out var defaultMinMax, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var langSys = new BaseLangSysRecord[script.BaseLangSysCount];
        bool changed = !ReferenceEquals(values, script.BaseValues) ||
            !ReferenceEquals(defaultMinMax, script.DefaultMinMax);
        for (int i = 0; i < script.BaseLangSysCount; i++)
        {
            (uint tag, BaseMinMaxTable minMax) = script.LangSys[i];
            status = RebuildBaseMinMax(minMax, map, outputGlyphs, coordMemo,
                out var rebuiltMinMax, out diagnostic);
            if (status != ReadStatus.Done) return status;
            langSys[i] = new(tag, rebuiltMinMax!);
            changed |= !ReferenceEquals(minMax, rebuiltMinMax);
        }
        if (!changed)
        {
            rebuilt = script;
            diagnostic = default;
            return ReadStatus.Done;
        }
        rebuilt = new(values, defaultMinMax, langSys, script.Location,
            script.ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RebuildBaseMinMax(BaseMinMaxTable? minMax,
        LayoutOutputMap map, int outputGlyphs,
        Dictionary<BaseCoordTable, BaseCoordTable> coordMemo,
        out BaseMinMaxTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = minMax;
        if (minMax is null)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = RebuildBaseCoord(minMax.MinCoord, map, outputGlyphs,
            coordMemo, out var minCoord, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RebuildBaseCoord(minMax.MaxCoord, map, outputGlyphs, coordMemo,
            out var maxCoord, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var featMinMax = new FeatMinMaxRecord[minMax.FeatMinMaxCount];
        bool changed = !ReferenceEquals(minCoord, minMax.MinCoord) ||
            !ReferenceEquals(maxCoord, minMax.MaxCoord);
        for (int i = 0; i < minMax.FeatMinMaxCount; i++)
        {
            FeatMinMaxRecord record = minMax.FeatMinMax[i];
            status = RebuildBaseCoord(record.MinCoord, map, outputGlyphs,
                coordMemo, out var recordMin, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = RebuildBaseCoord(record.MaxCoord, map, outputGlyphs,
                coordMemo, out var recordMax, out diagnostic);
            if (status != ReadStatus.Done) return status;
            featMinMax[i] = new(record.Tag, recordMin, recordMax);
            changed |= !ReferenceEquals(record.MinCoord, recordMin) ||
                !ReferenceEquals(record.MaxCoord, recordMax);
        }
        if (!changed)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        rebuilt = new(minCoord, maxCoord, featMinMax, minMax.Location,
            minMax.ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RebuildBaseValues(BaseValuesTable? values,
        LayoutOutputMap map, int outputGlyphs,
        Dictionary<BaseCoordTable, BaseCoordTable> coordMemo,
        out BaseValuesTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = values;
        if (values is null)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        var coords = new BaseCoordTable[values.CoordCount];
        bool changed = false;
        for (int i = 0; i < values.CoordCount; i++)
        {
            ReadStatus status = RebuildBaseCoord(values.Coords[i], map,
                outputGlyphs, coordMemo, out var coord, out diagnostic);
            if (status != ReadStatus.Done) return status;
            coords[i] = coord!;
            changed |= !ReferenceEquals(values.Coords[i], coord);
        }
        if (!changed)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        rebuilt = new(values.DefaultBaselineIndex, coords, values.Location,
            values.ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One coord slot: a fmt2 leaf substitutes its referenceGlyph and
    /// re-answers the output domain at the coord factory (the slot's other words
    /// and its Device ride verbatim); the fmt1/fmt3 leaves carry no GID and keep
    /// the shared instance. The memo absorbs the R7 shapes — one source coord
    /// referenced from several slots substitutes once.</summary>
    private static ReadStatus RebuildBaseCoord(BaseCoordTable? coord,
        LayoutOutputMap map, int outputGlyphs,
        Dictionary<BaseCoordTable, BaseCoordTable> coordMemo,
        out BaseCoordTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = coord;
        if (coord is null || coord.Format != 2)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (coordMemo.TryGetValue(coord, out var hit))
        {
            rebuilt = hit;
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = RemapGlyphId(coord.Location, coord.ReferenceGlyph, map,
            out ushort glyph, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = BaseCoordTableCompiler.TryCreate(coord.Format, coord.Coordinate,
            glyph, coord.BaseCoordPoint, coord.Device, outputGlyphs, out var model,
            out diagnostic, location: coord.Location);
        if (status != ReadStatus.Done) return status;
        coordMemo.Add(coord, model!);
        rebuilt = model;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The fmt2 coord slot count over both axis trees (wire order) —
    /// the census row's in/out pair and the decide stage's fmt2 census agree on
    /// this walk (CollectBaseCoordReferences).</summary>
    private static int BaseFormat2CoordCount(BaseTable baseModel)
    {
        int count = 0;
        foreach (ushort _ in CollectBaseCoordReferences(baseModel)) count++;
        return count;
    }

    // ---------------------------------------------------------------------
    // JSTF (S2b card §2.5, commit ⑥): the extender lists and the JstfMax inline
    // lookups remap; the mod-list lookup indices stay identity (OQ-S2b-7 — the
    // indices address the neighbor LookupList record order, and the record order
    // never changes: the LayoutLookupListCompiler invariant), so the neighbor
    // counts are identity too and the count-identity assertion below is the
    // zero-rule-loss face for the cross-table index domain.
    //
    // The rebuilt subtree presents the GPOS family tag on source-bound
    // locations — the write-side mirror of the read's registered OQ-T4-09-10
    // lean: the GPOS-shaped wire genuinely lives inside this JSTF window, and
    // presenting the tag is what lets the frozen typed-chain identity contracts
    // (LookupTable.ValidateGposSource, the flags' context binding and the root
    // factory's SameSource walk) keep their exact semantics. The script tree is
    // rebuilt through the existing leaf factories; the JstfMax wrapper composes
    // the rebuilt lookups (its re-answer runs at the root factory's AnswerMax).
    // ---------------------------------------------------------------------

    private static ReadStatus RebuildJstf(JstfTable jstf, PrepareState state,
        GlyphRemap map, BuildBudget budget, List<LayoutRewriteCensusRow> rewrites,
        out byte[]? jstfBytes, out FontDiagnostic diagnostic)
    {
        jstfBytes = null;
        int inputPriorities = JstfPriorityCount(jstf);
        int outputGlyphs = map.OutputGlyphCount;
        var outputMap = LayoutOutputMap.Frozen(map);
        // OQ-S2b-7's count identity: the neighbor lists' record order never
        // changes, so the rebuilt counts equal the read counts; a drift would be
        // an S2b-1 rewrite bug this assertion pins at the JSTF seam.
        int gsubLookupCount = state.Gsub?.Lookups.Length ?? jstf.GsubLookupCount;
        int gposLookupCount = state.Gpos?.Lookups.Length ?? jstf.GposLookupCount;
        if (gsubLookupCount != jstf.GsubLookupCount ||
            gposLookupCount != jstf.GposLookupCount)
            return RewriteInvariantRefusal(
                jstf.Location.At(DataField.Count, (ulong)jstf.GsubLookupCount),
                (ulong)(((uint)(gsubLookupCount & 0xFFFF) << 16) |
                        (uint)(gposLookupCount & 0xFFFF)), out diagnostic);
        DataLocation at = jstfModelLocation(jstf.Location);
        var scripts = new JstfScriptRecord[jstf.ScriptCount];
        for (int s = 0; s < jstf.ScriptCount; s++)
        {
            (uint tag, JstfScriptTable script) = jstf.Scripts[s];
            ReadStatus status = RebuildJstfScript(at, script, outputMap,
                outputGlyphs, state.GdefContext, out JstfScriptTable? rebuiltScript,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            scripts[s] = new(tag, rebuiltScript!);
        }
        ReadStatus rootStatus = JstfTableCompiler.TryCreate(jstf.Version,
            outputGlyphs, gsubLookupCount, gposLookupCount, state.GdefContext,
            scripts, out JstfTable? model, out diagnostic, location: at);
        if (rootStatus != ReadStatus.Done) return rootStatus;
        if (model!.ScriptCount != jstf.ScriptCount)
            return RewriteInvariantRefusal(
                at.At(DataField.Count, (ulong)jstf.ScriptCount),
                (ulong)model.ScriptCount, out diagnostic);
        if (state.RewrittenLayoutModels is { } layoutModels) layoutModels.Jstf = model;
        rootStatus = JstfTableCompiler.TryCompile(model, out var bytes,
            out diagnostic);
        if (rootStatus != ReadStatus.Done) return rootStatus;
        rewrites.Add(RewriteCensusRow(JstfLayout.Tag, inputPriorities,
            JstfPriorityCount(model), jstf.ConsumedLength, bytes!.Length));
        return ChargeCompiled(budget, jstf.Location, bytes, out jstfBytes,
            out diagnostic);
    }

    /// <summary>The whole script tree's priority count (the rewrite's structural
    /// unit for the OQ-S2b-10 census row; the shared-identity caches make the
    /// walk cheap but the count is the wire count).</summary>
    private static int JstfPriorityCount(JstfTable jstf)
    {
        int count = 0;
        foreach (var (_, script) in jstf.Scripts)
        {
            count += script.DefaultLangSys?.PriorityCount ?? 0;
            foreach (var (_, langSys) in script.LangSys)
                count += langSys.PriorityCount;
        }
        return count;
    }

    /// <summary>The JSTF rewrite's source-bound presentation tag (the OQ-T4-09-10
    /// mirror; see the section comment).</summary>
    private static DataLocation jstfModelLocation(DataLocation readLocation) =>
        readLocation with { TableTag = SinglePosLayout.Tag };

    private static ReadStatus RebuildJstfScript(DataLocation at,
        JstfScriptTable script, LayoutOutputMap map, int outputGlyphs,
        GdefLookupContext? context, out JstfScriptTable? rebuilt,
        out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        JstfExtenderGlyphTable? extenders = null;
        if (script.ExtenderGlyphs is { } extenderModel)
        {
            var glyphs = new ushort[extenderModel.GlyphCount];
            for (int i = 0; i < glyphs.Length; i++)
            {
                ReadStatus status = RemapGlyphId(at, extenderModel.Glyphs[i], map,
                    out glyphs[i], out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            ReadStatus create = JstfTableCompiler.TryCreate(glyphs, outputGlyphs,
                out JstfExtenderGlyphTable? rebuiltExtenders,
                out diagnostic, location: at);
            if (create != ReadStatus.Done) return create;
            extenders = rebuiltExtenders;
        }
        ReadStatus status2 = RebuildJstfLangSys(at, script.DefaultLangSys, map,
            outputGlyphs, context, out var defaultLangSys, out diagnostic);
        if (status2 != ReadStatus.Done) return status2;
        var langSysRows = new JstfLangSysRecord[script.LangSysCount];
        for (int i = 0; i < script.LangSysCount; i++)
        {
            (uint tag, JstfLangSysTable langSys) = script.LangSys[i];
            status2 = RebuildJstfLangSys(at, langSys, map, outputGlyphs,
                context, out var rebuiltLangSys, out diagnostic);
            if (status2 != ReadStatus.Done) return status2;
            langSysRows[i] = new(tag, rebuiltLangSys!);
        }
        return JstfTableCompiler.TryCreate(extenders, defaultLangSys, langSysRows,
            out rebuilt, out diagnostic, location: at);
    }

    private static ReadStatus RebuildJstfLangSys(DataLocation at,
        JstfLangSysTable? langSys, LayoutOutputMap map, int outputGlyphs,
        GdefLookupContext? context, out JstfLangSysTable? rebuilt,
        out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        if (langSys is null)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        var priorities = new JstfPriorityTable[langSys.PriorityCount];
        for (int p = 0; p < langSys.PriorityCount; p++)
        {
            JstfPriorityTable priority = langSys.Priorities[p];
            ReadStatus status = RebuildJstfMax(at, priority.ShrinkageJstfMax,
                map, outputGlyphs, context, out var shrinkageMax,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = RebuildJstfMax(at, priority.ExtensionJstfMax, map,
                outputGlyphs, context, out var extensionMax, out diagnostic);
            if (status != ReadStatus.Done) return status;
            // The eight mod-list slots travel as the read model's own instances:
            // their lookup indices are record-order references (OQ-S2b-7, zero
            // remap) and the root factory re-answers every index against the
            // (identity) neighbor counts.
            status = JstfTableCompiler.TryCreate(priority.GsubShrinkageEnable,
                priority.GsubShrinkageDisable, priority.GposShrinkageEnable,
                priority.GposShrinkageDisable, shrinkageMax,
                priority.GsubExtensionEnable, priority.GsubExtensionDisable,
                priority.GposExtensionEnable, priority.GposExtensionDisable,
                extensionMax, out JstfPriorityTable? rebuiltPriority,
                out diagnostic, at);
            if (status != ReadStatus.Done) return status;
            priorities[p] = rebuiltPriority!;
        }
        return JstfTableCompiler.TryCreate(priorities, out rebuilt,
            out diagnostic, location: at);
    }

    private static ReadStatus RebuildJstfMax(DataLocation at, JstfMaxTable? max,
        LayoutOutputMap map, int outputGlyphs, GdefLookupContext? context,
        out JstfMaxTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        if (max is null)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        var lookups = new LookupTable[max.LookupCount];
        for (int i = 0; i < max.LookupCount; i++)
        {
            ReadStatus status = RewriteInlineGposLookup(at, max.Lookups[i], map,
                outputGlyphs, context, out var rebuiltLookup,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            lookups[i] = rebuiltLookup!;
        }
        rebuilt = new(lookups, at, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One inline JstfMax lookup over the output domain: the GPOS leaf
    /// family rewrites through the existing leaf factories (types 1-6; the
    /// contextual pair is banned at the read face) and type 9 unwraps exactly one
    /// level with the same ban. LookupType/Flags/MarkFilteringSet are verbatim —
    /// the markset policy is set-identity, so the markFilteringSet index is
    /// identity too (the S2b card §2.1 lookup-level contract). The subtable
    /// sequence is identity and asserted.</summary>
    private static ReadStatus RewriteInlineGposLookup(DataLocation at,
        LookupTable lookup, LayoutOutputMap map, int outputGlyphs,
        GdefLookupContext? context, out LookupTable? rebuilt,
        out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        ReadStatus status;
        switch (lookup.LookupType)
        {
            case 1:
                {
                    var subtables = new SinglePosTable[lookup.SinglePositions!.Count];
                    for (int i = 0; i < subtables.Length; i++)
                    {
                        status = RebuildSinglePosLeaf(at, lookup.SinglePositions[i],
                            map, outputGlyphs, out var rebuiltLeaf, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        subtables[i] = rebuiltLeaf!;
                    }
                    status = LookupTableCompiler.TryCreateSinglePos(outputGlyphs,
                        subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                        out rebuilt, out diagnostic, location: at);
                    break;
                }
            case 2:
                {
                    var subtables = new PairPosTable[lookup.PairPositions!.Count];
                    for (int i = 0; i < subtables.Length; i++)
                    {
                        status = RebuildPairPosLeaf(at, lookup.PairPositions[i],
                            map, outputGlyphs, out var rebuiltLeaf, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        subtables[i] = rebuiltLeaf!;
                    }
                    status = LookupTableCompiler.TryCreatePairPos(outputGlyphs,
                        subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                        out rebuilt, out diagnostic, location: at);
                    break;
                }
            case 3:
                {
                    var subtables = new CursivePosTable[
                        lookup.CursiveAttachments!.Count];
                    for (int i = 0; i < subtables.Length; i++)
                    {
                        status = RebuildCursiveLeaf(at, lookup.CursiveAttachments[i],
                            map, outputGlyphs, out var rebuiltLeaf, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        subtables[i] = rebuiltLeaf!;
                    }
                    status = LookupTableCompiler.TryCreateCursivePos(outputGlyphs,
                        subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                        out rebuilt, out diagnostic, location: at);
                    break;
                }
            case 4:
                {
                    var subtables = new MarkBasePosTable[lookup.MarkBases!.Count];
                    for (int i = 0; i < subtables.Length; i++)
                    {
                        status = RebuildMarkBaseLeaf(at, lookup.MarkBases[i], map,
                            outputGlyphs, out var rebuiltLeaf, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        subtables[i] = rebuiltLeaf!;
                    }
                    status = LookupTableCompiler.TryCreateMarkBase(outputGlyphs,
                        subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                        out rebuilt, out diagnostic, location: at);
                    break;
                }
            case 5:
                {
                    var subtables = new MarkLigPosTable[lookup.MarkLigs!.Count];
                    for (int i = 0; i < subtables.Length; i++)
                    {
                        status = RebuildMarkLigLeaf(at, lookup.MarkLigs[i], map,
                            outputGlyphs, out var rebuiltLeaf, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        subtables[i] = rebuiltLeaf!;
                    }
                    status = LookupTableCompiler.TryCreateMarkLig(outputGlyphs,
                        subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                        out rebuilt, out diagnostic, location: at);
                    break;
                }
            case 6:
                {
                    var subtables = new MarkMarkPosTable[lookup.MarkMarks!.Count];
                    for (int i = 0; i < subtables.Length; i++)
                    {
                        status = RebuildMarkMarkLeaf(at, lookup.MarkMarks[i], map,
                            outputGlyphs, out var rebuiltLeaf, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        subtables[i] = rebuiltLeaf!;
                    }
                    status = LookupTableCompiler.TryCreateMarkMark(outputGlyphs,
                        subtables, lookup.Flags, lookup.MarkFilteringSet, context,
                        out rebuilt, out diagnostic, location: at);
                    break;
                }
            case 9:
                {
                    var subtables = new ExtensionPosTable[
                        lookup.ExtensionPositions!.Count];
                    for (int i = 0; i < subtables.Length; i++)
                    {
                        status = RebuildExtensionLeaf(at,
                            lookup.ExtensionPositions[i], map, outputGlyphs,
                            out var rebuiltExtension, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        subtables[i] = rebuiltExtension!;
                    }
                    // The type-9 wrapper composes directly: the read face is the
                    // only producer of these models today (the write plans wrap
                    // at emission time), so the flags contract re-answers here —
                    // the same checks the type factories run for their types.
                    var candidate = new LookupTable(LayoutLookupKind.Gpos,
                        outputGlyphs, 9, lookup.Flags, lookup.MarkFilteringSet,
                        [], null, at, extensionPositions: subtables);
                    status = candidate.ValidateFlags(context, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    rebuilt = candidate;
                    break;
                }
            default:
                // Unreachable: the read face admits types 1-9 only and bans 7/8
                // outright; the total switch keeps an orchestration drift from
                // passing an unknown type silently.
                return RewriteInvariantRefusal(
                    lookup.Location.AtLocal(DataField.Tag, LookupLayout.TypeAt),
                    lookup.LookupType, out diagnostic);
        }
        if (status != ReadStatus.Done) return status;
        if (rebuilt!.SubtableCount != lookup.SubtableCount)
            return RewriteInvariantRefusal(
                at.At(DataField.Count, (ulong)lookup.SubtableCount),
                (ulong)rebuilt.SubtableCount, out diagnostic);
        return ReadStatus.Done;
    }

    private static ReadStatus RebuildSinglePosLeaf(DataLocation at,
        SinglePosTable sub, LayoutOutputMap map, int outputGlyphs,
        out SinglePosTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        ReadStatus status = RebuildCoverage(at, sub.Coverage, map, outputGlyphs,
            out var coverage, out var coverageOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
        {
            rebuilt = null;
            return status;
        }
        // The physical value rows travel verbatim (one for format 1, one per
        // coverage key for format 2) and the per-key rows ride the coverage's sort.
        StaticValueRecord[] values = sub.Format == 1
            ? sub.Values.ToArray()
            : Permute(sub.Values.ToArray(), coverageOrdinal);
        return SinglePosTableCompiler.TryCreate(coverage!.Glyphs, values,
            outputGlyphs, sub.Format, sub.ValueFormat, out rebuilt,
            out diagnostic, location: at, coverageFormat: sub.Coverage.Format);
    }

    private static ReadStatus RebuildPairPosLeaf(DataLocation at, PairPosTable sub,
        LayoutOutputMap map, int outputGlyphs, out PairPosTable? rebuilt,
        out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        ReadStatus status = RebuildCoverage(at, sub.Coverage, map, outputGlyphs,
            out var coverage, out var coverageOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
        {
            rebuilt = null;
            return status;
        }
        if (sub.Format == 1)
        {
            // The pair sets are the coverage's parallel payloads: position s of the
            // rebuilt array remaps the source set the sorted key rode.
            var pairSets = new PairValueRecord[sub.PairSetCount][];
            for (int s = 0; s < sub.PairSetCount; s++)
            {
                PairSetTable pairSet = sub.PairSets![coverageOrdinal[s]];
                pairSets[s] = new PairValueRecord[pairSet.PairValueCount];
                for (int r = 0; r < pairSet.PairValueCount; r++)
                {
                    PairValueRecord record = pairSet.Records[r];
                    status = RemapGlyphId(at, record.SecondGlyph, map,
                        out ushort second, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    pairSets[s][r] = record with { SecondGlyph = second };
                }
            }
            return PairPosTableCompiler.TryCreate(coverage!.Glyphs, pairSets,
                outputGlyphs, sub.ValueFormat1, sub.ValueFormat2, out rebuilt,
                out diagnostic, location: at,
                coverageFormat: sub.Coverage.Format);
        }
        // Format 2: the two class domains contract entry-wise (OQ-S2b-3's lean, the
        // JSTF window's form) — a mapped glyph keeps its class value (class values
        // never renumber), an unmapped glyph's entry drops, and the declared
        // class1xclass2 matrix travels verbatim class1-major. Classes that lose
        // every member stay as dead rows (the MERG precedent).
        status = RebuildClassDef(at, sub.ClassDef1!, map, outputGlyphs,
            out var classDef1, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RebuildClassDef(at, sub.ClassDef2!, map, outputGlyphs,
            out var classDef2, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var values1 = new StaticValueRecord[sub.Class1Count * sub.Class2Count];
        var values2 = new StaticValueRecord[sub.Class1Count * sub.Class2Count];
        for (int c1 = 0; c1 < sub.Class1Count; c1++)
            for (int c2 = 0; c2 < sub.Class2Count; c2++)
            {
                values1[c1 * sub.Class2Count + c2] = sub.GetMatrixValue1(c1, c2);
                values2[c1 * sub.Class2Count + c2] = sub.GetMatrixValue2(c1, c2);
            }
        return PairPosTableCompiler.TryCreate(coverage!.Glyphs,
            classDef1!.Ranges.ToArray(), classDef2!.Ranges.ToArray(),
            (ushort)sub.Class1Count, (ushort)sub.Class2Count, values1, values2,
            outputGlyphs, sub.ValueFormat1, sub.ValueFormat2, out rebuilt,
            out diagnostic, location: at, coverageFormat: sub.Coverage.Format,
            classDefFormat: sub.ClassDef1!.Format);
    }

    private static ReadStatus RebuildCursiveLeaf(DataLocation at,
        CursivePosTable sub, LayoutOutputMap map, int outputGlyphs,
        out CursivePosTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        ReadStatus status = RebuildCoverage(at, sub.Coverage, map, outputGlyphs,
            out var coverage, out var coverageOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
        {
            rebuilt = null;
            return status;
        }
        // The EntryExit records and their anchors carry no GID beyond the coverage
        // and ride its sort.
        return CursivePosTableCompiler.TryCreate(coverage!.Glyphs,
            Permute(sub.Records.ToArray(), coverageOrdinal), outputGlyphs,
            out rebuilt, out diagnostic, location: at,
            coverageFormat: sub.Coverage.Format);
    }

    private static ReadStatus RebuildMarkBaseLeaf(DataLocation at,
        MarkBasePosTable sub, LayoutOutputMap map, int outputGlyphs,
        out MarkBasePosTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        ReadStatus status = RebuildCoverage(at, sub.MarkCoverage, map,
            outputGlyphs, out var markCoverage, out var markOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
        {
            rebuilt = null;
            return status;
        }
        status = RebuildCoverage(at, sub.BaseCoverage, map, outputGlyphs,
            out var baseCoverage, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var baseCells = new AnchorTable?[sub.BaseCount * sub.ClassCount];
        for (int b = 0; b < sub.BaseCount; b++)
        {
            ReadOnlySpan<AnchorTable?> row = sub.BaseRow(b);
            for (int c = 0; c < sub.ClassCount; c++)
                baseCells[b * sub.ClassCount + c] = row[c];
        }
        // The mark array rides its coverage's sort; the anchor matrix carries no
        // GID beyond the coverages.
        return MarkPosTableCompiler.TryCreate(markCoverage!.Glyphs,
            Permute(sub.Marks.ToArray(), markOrdinal), baseCoverage!.Glyphs,
            baseCells, sub.ClassCount, outputGlyphs, out rebuilt, out diagnostic,
            location: at, coverageFormat: sub.MarkCoverage.Format);
    }

    private static ReadStatus RebuildMarkLigLeaf(DataLocation at,
        MarkLigPosTable sub, LayoutOutputMap map, int outputGlyphs,
        out MarkLigPosTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        ReadStatus status = RebuildCoverage(at, sub.MarkCoverage, map,
            outputGlyphs, out var markCoverage, out var markOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
        {
            rebuilt = null;
            return status;
        }
        status = RebuildCoverage(at, sub.LigatureCoverage, map, outputGlyphs,
            out var ligCoverage, out var ligOrdinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Both parallel arrays ride their own coverage's sort.
        return MarkPosTableCompiler.TryCreateLig(markCoverage!.Glyphs,
            Permute(sub.Marks.ToArray(), markOrdinal), ligCoverage!.Glyphs,
            Permute(sub.Ligatures, ligOrdinal), outputGlyphs, out rebuilt,
            out diagnostic, location: at, coverageFormat: sub.MarkCoverage.Format);
    }

    private static ReadStatus RebuildMarkMarkLeaf(DataLocation at,
        MarkMarkPosTable sub, LayoutOutputMap map, int outputGlyphs,
        out MarkMarkPosTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        ReadStatus status = RebuildCoverage(at, sub.MarkCoverage, map,
            outputGlyphs, out var markCoverage, out var markOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
        {
            rebuilt = null;
            return status;
        }
        status = RebuildCoverage(at, sub.Mark2Coverage, map, outputGlyphs,
            out var mark2Coverage, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var mark2Cells = new AnchorTable?[sub.Mark2Count * sub.ClassCount];
        for (int m = 0; m < sub.Mark2Count; m++)
        {
            ReadOnlySpan<AnchorTable?> row = sub.Mark2Row(m);
            for (int c = 0; c < sub.ClassCount; c++)
                mark2Cells[m * sub.ClassCount + c] = row[c];
        }
        return MarkPosTableCompiler.TryCreateMark(markCoverage!.Glyphs,
            Permute(sub.Marks.ToArray(), markOrdinal), mark2Coverage!.Glyphs,
            mark2Cells, sub.ClassCount, outputGlyphs, out rebuilt, out diagnostic,
            location: at, coverageFormat: sub.MarkCoverage.Format);
    }

    private static ReadStatus RebuildExtensionLeaf(DataLocation at,
        ExtensionPosTable extension, LayoutOutputMap map, int outputGlyphs,
        out ExtensionPosTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        // The contextual pair is banned at the read face (OQ-3), so the wrapped
        // type is one of the six direct leaves; the total switch keeps an
        // orchestration drift from passing the banned forms silently.
        switch (extension.ExtensionLookupType)
        {
            case 1:
            {
                ReadStatus status = RebuildSinglePosLeaf(at, extension.Single!,
                    map, outputGlyphs, out var single, out diagnostic);
                if (status != ReadStatus.Done) return status;
                return ExtensionPosTable.TryCreate(1, single, null, null,
                    out rebuilt, out diagnostic, at);
            }
            case 2:
            {
                ReadStatus status = RebuildPairPosLeaf(at, extension.Pair!, map,
                    outputGlyphs, out var pair, out diagnostic);
                if (status != ReadStatus.Done) return status;
                return ExtensionPosTable.TryCreate(2, null, pair, null,
                    out rebuilt, out diagnostic, at);
            }
            case 3:
            {
                ReadStatus status = RebuildCursiveLeaf(at, extension.Cursive!,
                    map, outputGlyphs, out var cursive, out diagnostic);
                if (status != ReadStatus.Done) return status;
                return ExtensionPosTable.TryCreate(3, null, null, cursive,
                    out rebuilt, out diagnostic, at);
            }
            case 4:
            {
                ReadStatus status = RebuildMarkBaseLeaf(at, extension.MarkBase!,
                    map, outputGlyphs, out var markBase, out diagnostic);
                if (status != ReadStatus.Done) return status;
                return ExtensionPosTable.TryCreate(4, null, null, null,
                    out rebuilt, out diagnostic, at, markBase: markBase);
            }
            case 5:
            {
                ReadStatus status = RebuildMarkLigLeaf(at, extension.MarkLig!,
                    map, outputGlyphs, out var markLig, out diagnostic);
                if (status != ReadStatus.Done) return status;
                return ExtensionPosTable.TryCreate(5, null, null, null,
                    out rebuilt, out diagnostic, at, markLig: markLig);
            }
            case 6:
            {
                ReadStatus status = RebuildMarkMarkLeaf(at, extension.MarkMark!,
                    map, outputGlyphs, out var markMark, out diagnostic);
                if (status != ReadStatus.Done) return status;
                return ExtensionPosTable.TryCreate(6, null, null, null,
                    out rebuilt, out diagnostic, at, markMark: markMark);
            }
            default:
                return RewriteInvariantRefusal(
                    extension.Location.AtLocal(DataField.Tag,
                        (ulong)ExtensionPosLayout.TypeAt),
                    extension.ExtensionLookupType, out diagnostic);
        }
    }

    /// <summary>One ClassDef's entry-wise image: per range, the mapped members keep
    /// the range's class value and recompose into maximal same-class runs (the
    /// monotone map keeps the image ascending); an unmapped member is the RF-S2b-2
    /// refusal (the closure merged the class domain).</summary>
    private static ReadStatus RebuildClassDef(DataLocation at,
        ClassDefTable classDef, LayoutOutputMap map, int outputGlyphs,
        out ClassDefTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        var ranges = new List<ClassDefRange>();
        foreach (ClassDefRange range in classDef.Ranges)
        {
            int runStart = -1, runEnd = -1;
            for (int gid = range.StartGlyph; gid <= range.EndGlyph; gid++)
            {
                int mapped = map.OutputOf(gid);
                if (mapped < 0)
                    return RewriteInvariantRefusal(
                        at.At(DataField.GlyphId, (ulong)gid), (ulong)gid,
                        out diagnostic);
                if (runStart >= 0 && mapped == runEnd + 1)
                {
                    runEnd = mapped;
                    continue;
                }
                if (runStart >= 0)
                    ranges.Add(new((ushort)runStart, (ushort)runEnd,
                        range.ClassValue));
                runStart = runEnd = mapped;
            }
            if (runStart >= 0)
                ranges.Add(new((ushort)runStart, (ushort)runEnd,
                    range.ClassValue));
        }
        diagnostic = default;
        return ClassDefTableCompiler.TryCreate(ranges.ToArray(), outputGlyphs,
            classDef.Format, out rebuilt, out diagnostic, location: at);
    }
}
