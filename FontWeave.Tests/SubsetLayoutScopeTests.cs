// S2c layout-scope unit tests (S2c card §2/§8 commit ① acceptance).
//
// Two faces drive the slice: compiled typed models answer the parser directly
// through the test bridge (matching, the nested-orphan worklist closure, the
// required-feature implicit keep, the dangling declaration's zero match count,
// the no-op verdict), and a synthetic face drives the pipeline — the two
// RF-S2c-1 contradiction refusals, the no-op short-circuit's byte identity with
// the undeclared request, and the truly-cut retain request's fifth-alternate
// decision whose scoped closure stops pulling the cut lookups' glyphs (the
// scoped rewrite producer lands with the slice's next commit; the window refuses
// with the decision's own tag until then).
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetLayoutScopeTests
{
    private const int NumGlyphs = 16;
    private const uint TagA = 0x41424344; // 'ABCD'
    private const uint TagB = 0x42434445; // 'BCDE'
    private const uint TagDflt = 0x44464C54; // 'DFLT'
    private const uint TagLatn = 0x6C61746E; // 'latn'

    private static void Ok(ReadStatus status)
    {
        Assert.Equal(ReadStatus.Done, status);
    }

    private static void NoDiagnostic(FontDiagnostic diagnostic)
    {
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
    }

    // lookup0: contextual fmt1 over coverage {4} with input glyph 5 and one record
    // targeting lookup1 — the nested edge. lookup1: the orphan single subst
    // {6}→{7} (no feature references it). lookup2: an independent single
    // subst {1}→{2} behind feature TagB.
    private static LayoutListsTable Model(int requiredFeatureIndex = ushort.MaxValue)
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 4 }, NumGlyphs, 1,
            out var contextualCoverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        var record = new SequenceLookupRecord(0, 1);
        Ok(ContextualSubstTableCompiler.TryCreateRule(stackalloc ushort[] { 5 },
            [record], 3, out var rule, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreateRuleSet([rule!], out var ruleSet,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreate(contextualCoverage!,
            [ruleSet!], NumGlyphs, out var contextual, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateContextualSubst(NumGlyphs, [contextual!],
            0, null, null, out var contextualLookup, out diagnostic));
        NoDiagnostic(diagnostic);

        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 6 },
            stackalloc ushort[] { 7 }, NumGlyphs, 1, out var orphanSingle, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [orphanSingle!],
            0, null, null, out var orphanLookup, out diagnostic));
        NoDiagnostic(diagnostic);

        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            stackalloc ushort[] { 2 }, NumGlyphs, 1, out var plainSingle, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [plainSingle!],
            0, null, null, out var plainLookup, out diagnostic));
        NoDiagnostic(diagnostic);

        Ok(LayoutListsTableCompiler.TryCreateLangSys((ushort)requiredFeatureIndex,
            stackalloc ushort[] { 0, 1 }, 2, out var langSys, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateScript(TagDflt, langSys!, [], 2,
            out var script, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateFeature(TagA, 0, stackalloc ushort[] { 0 },
            3, out var featureA, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateFeature(TagB, 0, stackalloc ushort[] { 2 },
            3, out var featureB, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreate(LayoutListsLayout.Version10, 0,
            [script!], [featureA!, featureB!],
            [contextualLookup!, orphanLookup!, plainLookup!], NumGlyphs,
            LayoutLookupKind.Gsub, out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        return model!;
    }

    private static byte[] GsubBytes(LayoutListsTable model)
    {
        Ok(GsubTableCompiler.TryCompile(model, null, out var bytes, out var diagnostic));
        NoDiagnostic(diagnostic);
        return bytes!;
    }

    private static FontFaceRef Face(byte[] gsub)
    {
        var glyphs = new List<byte>();
        var loca = new List<uint> { 0 };
        for (int i = 0; i < NumGlyphs; i++)
        {
            glyphs.AddRange(GlyfImages.NoContourGlyphRecord());
            loca.Add((uint)(12 * (i + 1)));
        }
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
            (SubsetFontFixture.GsubTag, gsub),
            (SubsetFontFixture.GlyfTag, [.. glyphs]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca(loca)),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))));
    }

    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        SubsetOptions options) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = SubsetMode.RetainGids,
        Options = options
    };

    private static byte[] OutputBytes(SubsetResult result)
    {
        byte[] output = new byte[result.Font.OutputLength];
        Assert.Equal(ReadStatus.Done,
            result.Font.TryCopyTo(output, out int written, out _));
        Assert.Equal(output.Length, written);
        return output;
    }

    private static byte[] TableBytes(byte[] sfnt, uint tag)
    {
        int numTables = (sfnt[4] << 8) | sfnt[5];
        for (int i = 0; i < numTables; i++)
        {
            int at = 12 + 16 * i;
            uint found = (uint)(sfnt[at] << 24 | sfnt[at + 1] << 16 |
                sfnt[at + 2] << 8 | sfnt[at + 3]);
            if (found != tag) continue;
            int offset = (sfnt[at + 8] << 24) | (sfnt[at + 9] << 16) |
                (sfnt[at + 10] << 8) | sfnt[at + 11];
            int length = (sfnt[at + 12] << 24) | (sfnt[at + 13] << 16) |
                (sfnt[at + 14] << 8) | sfnt[at + 15];
            return sfnt[offset..(offset + length)];
        }
        throw new Xunit.Sdk.XunitException($"tag {tag} missing from output");
    }

    // ---- the parser, over compiled models (the test bridge) ------------------

    [Fact]
    public void NestedOrphanJoinsOnlyThroughTheWorklist()
    {
        // Feature TagB's list: the plain lookup survives; the contextual lookup and
        // its orphan target stay out — a cut without the transitive closure would
        // leave nothing dangling here, but the opposite direction proves the pull.
        Ok(SubsetOperation.ResolveLayoutListsScopeForTests(Model(),
            new SubsetLayoutScope { FeatureTags = [TagB] },
            out var resolution, out var diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(resolution);
        Assert.Equal([2], resolution!.SurvivingLookups);
        Assert.Equal([0], resolution.SurvivingScripts);
        Assert.Equal([1], resolution.SurvivingFeatures);
        Assert.Equal(new[] { -1, -1, 0 }, resolution.LookupNewIndex);
        Assert.False(resolution.ListsNoOp);
    }

    [Fact]
    public void ContextualFeaturePullsItsOrphanTarget()
    {
        // Feature TagA's list: the contextual lookup 0 joins, and its record's
        // target — the orphan lookup 1 no feature names — joins through the
        // worklist (the corpus's orphan-chain shape, S2c card §2.1 step 3).
        Ok(SubsetOperation.ResolveLayoutListsScopeForTests(Model(),
            new SubsetLayoutScope { FeatureTags = [TagA] },
            out var resolution, out var diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(resolution);
        Assert.Equal([0, 1], resolution!.SurvivingLookups);
        Assert.Equal(new[] { 0, 1, -1 }, resolution.LookupNewIndex);
        Assert.Equal(new[] { 0, -1 }, resolution.FeatureNewIndex);
    }

    [Fact]
    public void FullDeclarationIsTheNoOpVerdict()
    {
        Ok(SubsetOperation.ResolveLayoutListsScopeForTests(Model(),
            new SubsetLayoutScope { FeatureTags = [TagA, TagB] },
            out var resolution, out var diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(resolution);
        Assert.True(resolution!.ListsNoOp);
        Assert.Equal([0, 1, 2], resolution.SurvivingLookups);
        // The remaps are the identity (declared members, original wire order).
        Assert.Equal(new[] { 0, 1, 2 }, resolution.LookupNewIndex);
        Assert.Equal(2, resolution.DeclaredFeatureMatches);
    }

    [Fact]
    public void DanglingDeclarationMatchesZeroAndIsIgnored()
    {
        const uint absent = 0x5A5A5A5A; // 'ZZZZ' — not in the face
        Ok(SubsetOperation.ResolveLayoutListsScopeForTests(Model(),
            new SubsetLayoutScope { FeatureTags = [TagA, TagB, absent] },
            out var resolution, out var diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(resolution);
        // The absent tag is ignored (OQ-S2c-7) and its zero match is visible; the
        // verdict equals the full declaration's — the no-op form.
        Assert.True(resolution!.ListsNoOp);
        Assert.Equal(2, resolution.DeclaredFeatureMatches);
        var full = Model();
        Ok(SubsetOperation.ResolveLayoutListsScopeForTests(full,
            new SubsetLayoutScope { FeatureTags = [TagA, TagB] },
            out var fullResolution, out diagnostic));
        Assert.Equal(fullResolution!.SurvivingLookups, resolution.SurvivingLookups);
    }

    [Fact]
    public void RequiredFeatureSurvivesAnUndeclaredTag()
    {
        // The langsys's required feature (index 0, tag TagA) is implicitly kept even
        // when only TagB is declared (OQ-S2c-5): the required entry joins the
        // surviving set and its lookups ride the seeds (the contextual + the orphan).
        Ok(SubsetOperation.ResolveLayoutListsScopeForTests(Model(requiredFeatureIndex: 0),
            new SubsetLayoutScope { FeatureTags = [TagB] },
            out var resolution, out var diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(resolution);
        Assert.Equal([0, 1], resolution!.SurvivingFeatures);
        Assert.Equal([0, 1, 2], resolution.SurvivingLookups);
    }

    [Fact]
    public void ScriptListCutPrunesScriptsAndKeepsDefaultLangSys()
    {
        // Two scripts: the second carries no feature-reachable difference, but a
        // script-level cut must prune it and keep the surviving script's default
        // langsys (the no-tag form follows the script, §1.1).
        var model = Model();
        Ok(LayoutListsTableCompiler.TryCreateLangSys(ushort.MaxValue,
            stackalloc ushort[] { 0, 1 }, 2, out var langSys, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateScript(TagLatn, langSys!, [], 2,
            out var latn, out diagnostic));
        NoDiagnostic(diagnostic);
        var scripts = new List<LayoutScript>(model.Scripts.ToArray()) { latn! };
        var features = new List<LayoutFeature>(model.Features.ToArray());
        var lookups = new List<LookupTable>(model.Lookups.ToArray());
        Ok(LayoutListsTableCompiler.TryCreate(LayoutListsLayout.Version10, 0,
            [.. scripts], [.. features], [.. lookups], NumGlyphs,
            LayoutLookupKind.Gsub, out var twoScripts, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(SubsetOperation.ResolveLayoutListsScopeForTests(twoScripts!,
            new SubsetLayoutScope { ScriptTags = [TagDflt] },
            out var resolution, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(resolution);
        Assert.Equal([0], resolution!.SurvivingScripts);
        Assert.Equal(1, resolution.DeclaredScriptMatches);
        // A language declaration on a no-tag default langsys table matches nothing.
        Ok(SubsetOperation.ResolveLayoutListsScopeForTests(twoScripts!,
            new SubsetLayoutScope { LanguageTags = [TagLatn] },
            out var languageResolution, out diagnostic));
        Assert.True(languageResolution!.ListsNoOp);
        Assert.Equal(0, languageResolution.DeclaredLanguageMatches);
    }

    // ---- the pipeline: contradictions, the no-op short-circuit, the cut --------

    [Fact]
    public void ScopeContradictsNoLayoutAndRefuses()
    {
        // RF-S2c-1, form 1: the declared scope and NoLayout have no common meaning
        // (code 23, Detail 1; the refusal keeps the formed LayoutScope records).
        var options = new SubsetOptions
        {
            NoLayout = true,
            LayoutScope = new SubsetLayoutScope { FeatureTags = [TagB] }
        };
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(Model())), [1], options), out var result,
            out var report, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Equal(1UL, diagnostic.Detail);
        Assert.Null(result);
        Assert.NotNull(report);
        Assert.Contains(report!.OptionRecords,
            r => r.Id == SubsetOptionId.LayoutScope && r.Tag == TagB);
    }

    [Fact]
    public void ScopeWithoutPreserveLayoutRefuses()
    {
        // RF-S2c-1, form 2: the scope has no retention path to scope (code 23,
        // Detail 0) — a silent ignore would be a caller foot-gun (S2c card §1.3).
        var options = new SubsetOptions
        {
            LayoutScope = new SubsetLayoutScope { ScriptTags = [TagDflt] }
        };
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(Model())), [1], options), out var result,
            out var report, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Equal(0UL, diagnostic.Detail);
        Assert.Null(result);
        Assert.NotNull(report);
    }

    [Fact]
    public void NoOpScopeRetainIsByteIdenticalToUndeclared()
    {
        // §4.2's consequence-equivalence verdict, retain form: the full declaration
        // (plus a dangling tag) short-circuits to the undeclared decision — the two
        // products are byte-identical, fonts and reports.
        byte[] gsub = GsubBytes(Model());
        var face = Face(gsub);
        var undeclared = new SubsetOptions { PreserveLayout = true };
        var declared = new SubsetOptions
        {
            PreserveLayout = true,
            LayoutScope = new SubsetLayoutScope
            {
                ScriptTags = [TagDflt, 0x5A5A5A5A],
                FeatureTags = [TagA, TagB]
            }
        };
        var first = SubsetOperation.TryPrepare(Request(face, [1], undeclared),
            out var plain, out var plainReport, out var diagnostic);
        Assert.Equal(ReadStatus.Done, first);
        NoDiagnostic(diagnostic);
        var second = SubsetOperation.TryPrepare(Request(face, [1], declared),
            out var scoped, out var scopedReport, out diagnostic);
        Assert.Equal(ReadStatus.Done, second);
        NoDiagnostic(diagnostic);
        Assert.NotNull(plain);
        Assert.NotNull(scoped);
        Assert.Equal(OutputBytes(plain!), OutputBytes(scoped!));
        // The scoped report is the undeclared report: decisions, census, additions.
        Assert.Equal(plainReport!.Decisions, scopedReport!.Decisions);
        Assert.Equal(plainReport.LayoutClosureAddedGids, scopedReport.LayoutClosureAddedGids);
        Assert.Equal(plainReport.LayoutReferenceCensus, scopedReport.LayoutReferenceCensus);
        // The LayoutScope records are the declaration's only report trace.
        Assert.Contains(scopedReport.OptionRecords,
            r => r.Id == SubsetOptionId.LayoutScope && r.Tag == TagA);
        byte[] outGsub = TableBytes(OutputBytes(scoped!), SubsetFontFixture.GsubTag);
        Assert.Equal(gsub, outGsub);
    }

    [Fact]
    public void TrueCutStopsPullingCutLookupsAndReEmitsTheSurvivingSubset()
    {
        // The fifth alternate (retain form): a truly-cut GSUB flips to
        // RebuildTyped, the scoped closure enumerates only surviving lookups (the
        // cut lookups' references — GIDs 4..7 — never join), and the scoped
        // cascade re-emits the surviving subset over the identity seam: the
        // output carries exactly one lookup (the plain single) behind one
        // feature, and the report keeps the scoped closure facts.
        var options = new SubsetOptions
        {
            PreserveLayout = true,
            LayoutScope = new SubsetLayoutScope { FeatureTags = [TagB] }
        };
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(Model())), [1], options), out var result,
            out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        var census = Assert.Single(report!.LayoutReferenceCensus);
        Assert.Equal(SubsetFontFixture.GsubTag, census.Tag);
        Assert.Equal(2, census.ReferencedGidCount);
        Assert.Equal(new ushort[] { 2 }, report.LayoutClosureAddedGids);
        // The decision carries the fifth alternate's RebuildTyped over GSUB.
        bool gsubRebuilt = false;
        foreach (var decision in report!.Decisions)
            if (decision.Tag == SubsetFontFixture.GsubTag)
                gsubRebuilt = decision.Policy == SubsetPolicy.RebuildTyped &&
                    decision.Action == TableAction.Rebuild;
        Assert.True(gsubRebuilt);
        // The output GSUB: the surviving subset only — one feature (TagB), one
        // lookup (the plain single over {1}→{2}), one script whose default
        // langsys re-indexed its feature list to the surviving entry.
        byte[] output = OutputBytes(result!);
        Ok(LayoutListsView.TryOpen(TableBytes(output, SubsetFontFixture.GsubTag),
            out var view, out diagnostic, DataLocation.Anonymous(
                SubsetFontFixture.GsubTag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryReadTable(NumGlyphs, LayoutLookupKind.Gsub, null, out var model,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.Equal(1, model!.Scripts.Length);
        Assert.Equal(1, model.Features.Length);
        Assert.Equal(TagB, model.Features[0].Tag);
        Assert.Equal(1, model.Lookups.Length);
        // The scoped rewrite census row reads in≠out (3 subtables → 1) legally
        // (OQ-S2c-10) beside the scope census row.
        var rewriteRow = Assert.Single(report.LayoutRewriteCensus,
            r => r.Tag == SubsetFontFixture.GsubTag);
        Assert.Equal(3, rewriteRow.InputSubtableCount);
        Assert.Equal(1, rewriteRow.OutputSubtableCount);
        Assert.False(rewriteRow.CountsInvarianceHeld);
        var scopeRow = Assert.Single(report.LayoutScopeCensus);
        Assert.Equal(1, scopeRow.SurvivingLookupCount);
        Assert.Equal(1, scopeRow.FeatureTagMatches);
    }

    [Fact]
    public void TrueCutCompactReEmitsTheSurvivingSubsetOverTheFrozenMap()
    {
        // The compact form: the scoped cascade rides the frozen map and the map
        // artifact publishes as usual; the cut lookups' GIDs stayed out of the
        // closure, so the frozen domain carries only the surviving references.
        byte[] gsub = GsubBytes(Model());
        var options = new SubsetOptions
        {
            PreserveLayout = true,
            LayoutScope = new SubsetLayoutScope { FeatureTags = [TagB] }
        };
        var status = SubsetOperation.TryPrepare(new SubsetRequest
        {
            Face = Face(gsub),
            Root = new SubsetGlyphRoot
            {
                Kind = SubsetRootKind.ExplicitGlyphSet,
                Gids = [1]
            },
            Mode = SubsetMode.CompactRemap,
            Options = options
        }, out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        Assert.NotNull(result!.Map);
        Assert.Equal(new ushort[] { 2 }, report!.LayoutClosureAddedGids);
        Ok(LayoutListsView.TryOpen(
            TableBytes(OutputBytes(result), SubsetFontFixture.GsubTag),
            out var view, out diagnostic, DataLocation.Anonymous(
                SubsetFontFixture.GsubTag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryReadTable(3, LayoutLookupKind.Gsub, null, out var model,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.Equal(1, model!.Lookups.Length);
        Assert.Equal(1, model.Features.Length);
    }
}
