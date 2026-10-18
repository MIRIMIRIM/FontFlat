// S2c commit-3 family tests (S2c card §5.2/§5.3/§9.3/§9.6/§7): the JSTF scoped
// producer (script/lang pruning, the mod-list dangling deletion and remap, the
// AnswerScript re-answer), the transparent face close-outs (GDEF raw under
// retain+scope with the orphan mark-set census), the FV conditional-replacement
// family (the alternate-seed merge, the record survival and remap, the emptied
// record dropping), the no-op short-circuit's compact identity form, the required
// feature's pipeline-level implicit keep, and the S2c-BF-001 budget exhaustion.
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetLayoutScopeFamiliesTests
{
    private const int NumGlyphs = 16;
    private const uint TagA = 0x41424344; // 'ABCD' — the contextual feature
    private const uint TagB = 0x42434445; // 'BCDE' — the plain feature
    private const uint TagDflt = 0x44464C54; // 'DFLT'
    private const uint TagEng = 0x454E4720; // 'ENG '
    private const uint TagFra = 0x46524120; // 'FRA '
    private const uint JstfTag = 0x4A535446; // 'JSTF'
    private static readonly uint[] LayoutTags =
        { 0x6B65726E, 0x47535542, 0x47504F53, 0x47444546, 0x42415345, 0x4A535446,
          0x4D415448 };

    private static void Ok(ReadStatus status)
    {
        Assert.Equal(ReadStatus.Done, status);
    }

    private static void NoDiagnostic(FontDiagnostic diagnostic)
    {
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
    }

    // The ② remap model: lookup0 contextual (record → lookup2), lookup1 the plain
    // single {1}→{2} behind TagB, lookup2 the orphan single {6}→{7}.
    private static LayoutListsTable RemapModel(
        ushort requiredFeatureIndex = ushort.MaxValue,
        FeatureVariationsTable? variations = null)
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 4 }, NumGlyphs, 1,
            out var contextualCoverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        var record = new SequenceLookupRecord(0, 2);
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
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            stackalloc ushort[] { 2 }, NumGlyphs, 1, out var plainSingle, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [plainSingle!],
            0, null, null, out var plainLookup, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 6 },
            stackalloc ushort[] { 7 }, NumGlyphs, 1, out var orphanSingle, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [orphanSingle!],
            0, null, null, out var orphanLookup, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateLangSys(requiredFeatureIndex,
            stackalloc ushort[] { 0, 1 }, 2, out var langSys, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateScript(TagDflt, langSys!, [], 2,
            out var script, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateFeature(TagA, 0, stackalloc ushort[] { 0 },
            3, out var featureA, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateFeature(TagB, 0, stackalloc ushort[] { 1 },
            3, out var featureB, out diagnostic));
        NoDiagnostic(diagnostic);
        uint version = variations is null
            ? LayoutListsLayout.Version10
            : LayoutListsLayout.Version11;
        Ok(LayoutListsTableCompiler.TryCreate(version, variations is null ? 0u : 1u,
            [script!], [featureA!, featureB!],
            [contextualLookup!, plainLookup!, orphanLookup!], NumGlyphs,
            LayoutLookupKind.Gsub, out var model, out diagnostic,
            featureVariations: variations));
        NoDiagnostic(diagnostic);
        return model!;
    }

    /// <summary>The FV 1.1 fixture (OQ-S2c-6): one record with a universal
    /// condition set replacing feature 0 by an alternate feature whose lookups are
    /// the orphan {2} — the seed-merge witness — and, for the drop form, the same
    /// block with the replaced index retargeted by the caller.</summary>
    private static FeatureVariationsTable Variations(ushort replacedIndex)
    {
        Ok(LayoutListsTableCompiler.TryCreateFeature(TagB, 0, stackalloc ushort[] { 2 },
            3, out var alternate, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(FeatureVariationsTableCompiler.TryCreateFeatureTableSubst(
            [(replacedIndex, alternate!)], 2, out var substitution, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(FeatureVariationsTableCompiler.TryCreate(
            [new FeatureVariationRecord(null, substitution!)], out var variations,
            out diagnostic));
        NoDiagnostic(diagnostic);
        return variations!;
    }

    private static byte[] GsubBytes(LayoutListsTable model)
    {
        Ok(GsubTableCompiler.TryCompile(model, null, out var bytes, out var diagnostic));
        NoDiagnostic(diagnostic);
        return bytes!;
    }

    /// <summary>The JSTF fixture (the S2b2 fixture family's extension): one script
    /// whose default langsys carries one priority with a single GSUB shrinkage
    /// enable mod list — the indices address the GSUB lookup record order.</summary>
    private static byte[] JstfBytes(params ushort[] modIndices)
    {
        Ok(JstfTableCompiler.TryCreate(JstfLookupDomain.Gsub,
            modIndices, 3, out var mod, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCreate(mod!, null, null, null, null, null, null,
            null, null, null, out var priority, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCreate([priority!], out var langSys,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCreate(null, langSys!, [], out var script,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCreate(JstfLayout.Version10, NumGlyphs, 3, 0, null,
            [new JstfScriptRecord(TagDflt, script!)], out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCompile(model!, out var bytes, out diagnostic));
        NoDiagnostic(diagnostic);
        return bytes!;
    }

    /// <summary>The tagged-language JSTF fixture: the JstfBytes script with two
    /// tagged LangSys rows — 'ENG ' then 'FRA ' — over the same mod-carrying
    /// priority shape, the language-only cut's witness tree.</summary>
    private static byte[] JstfLanguageBytes()
    {
        Ok(JstfTableCompiler.TryCreate(JstfLookupDomain.Gsub,
            stackalloc ushort[] { 1 }, 3, out var mod, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCreate(mod!, null, null, null, null, null, null,
            null, null, null, out var priority, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCreate([priority!], out var engLangSys,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCreate([priority!], out var fraLangSys,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCreate(null, engLangSys!,
            [new JstfLangSysRecord(TagEng, engLangSys!), new JstfLangSysRecord(TagFra, fraLangSys!)],
            out var script, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCreate(JstfLayout.Version10, NumGlyphs, 3, 0, null,
            [new JstfScriptRecord(TagDflt, script!)], out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCompile(model!, out var bytes, out diagnostic));
        NoDiagnostic(diagnostic);
        return bytes!;
    }

    private static byte[] GdefBytes()
    {
        // Two mark sets over disjoint glyphs; no lookup uses either — both read as
        // orphans under the scope census (the conservative keep's registered face).
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 6 }, NumGlyphs, 1,
            out var set1, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 7 }, NumGlyphs, 1,
            out var set2, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(MarkGlyphSetsTableCompiler.TryCreate([set1!, set2!], NumGlyphs,
            out var markSets, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(GdefTableCompiler.TryCreate(0x00010002, NumGlyphs, null, null, markSets,
            null, null, null, out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(GdefTableCompiler.TryCompile(model!, out var bytes, out diagnostic));
        NoDiagnostic(diagnostic);
        return bytes!;
    }

    private static FontFaceRef Face(byte[] gsub, bool withJstf = false,
        byte[]? jstf = null, bool withGdef = false)
    {
        var glyphs = new List<byte>();
        var loca = new List<uint> { 0 };
        for (int i = 0; i < NumGlyphs; i++)
        {
            glyphs.AddRange(GlyfImages.NoContourGlyphRecord());
            loca.Add((uint)(12 * (i + 1)));
        }
        var tables = new List<(uint Tag, byte[] Bytes)>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
            (SubsetFontFixture.GsubTag, gsub),
            (SubsetFontFixture.GlyfTag, [.. glyphs]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca(loca)),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1)))
        };
        if (withJstf) tables.Add((JstfTag, jstf ?? JstfBytes(1)));
        if (withGdef) tables.Add((SubsetFontFixture.GdefTag, GdefBytes()));
        return SubsetFontFixture.Face([.. tables]);
    }

    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        SubsetMode mode, SubsetOptions options) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = mode,
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

    private static SubsetOptions Scoped(SubsetLayoutScope scope) => new()
    {
        PreserveLayout = true,
        LayoutScope = scope
    };

    // ---- the JSTF scoped producer (card §5.2) ---------------------------------

    [Fact]
    public void ScopedJstfDeletesDanglingModsAndRemapsTheRest()
    {
        // The mod list [2, 1]: target 2 survives via the nested closure (its new
        // index is 1); target 1 is the cut plain lookup — the dangling entry
        // deletes and the surviving entry re-answers to 1 (the order rides the
        // subsequence). The root re-answers against the new GSUB count (2).
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel()), withJstf: true,
                jstf: JstfBytes(2, 1)), [1], SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope { FeatureTags = [TagA] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        // The JSTF decision carried the fifth alternate.
        bool jstfRebuilt = false;
        foreach (var decision in report!.Decisions)
            if (decision.Tag == JstfTag)
                jstfRebuilt = decision.Action == TableAction.Rebuild &&
                    decision.Policy == SubsetPolicy.RebuildTyped;
        Assert.True(jstfRebuilt);
        // The output JSTF re-reads against the new counts with the remapped mods.
        byte[] jstf = TableBytes(OutputBytes(result!), JstfTag);
        Ok(JstfView.TryOpen(jstf, out var view, out diagnostic,
            DataLocation.Anonymous(JstfTag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryReadTable(NumGlyphs, 2, 0, null, out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(model);
        Assert.Equal(1, model!.ScriptCount);
        var priority = model.Scripts[0].Script.DefaultLangSys!.Priorities[0];
        var mod = priority.GsubShrinkageEnable;
        Assert.NotNull(mod);
        Assert.Equal(new ushort[] { 1 }, mod!.LookupIndices);
        // The census keeps the JSTF row (one priority in, one out — the cut rode
        // the mod entries, not the priority list).
        Assert.Contains(report.LayoutRewriteCensus,
            r => r.Tag == JstfTag && r.InputSubtableCount == 1 &&
                r.OutputSubtableCount == 1);
    }

    [Fact]
    public void ScopedJstfScriptCutPrunesTheTreeAndReanswersTheCounts()
    {
        // Declaring only a script tag the face does not have in JSTF (the GSUB
        // carries DFLT too, so the GSUB stays whole)... the JSTF script cut comes
        // from an empty script list: every JSTF script prunes, the root becomes
        // the legal zero-script form, and the neighbor counts still re-answer.
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel()), withJstf: true,
                jstf: JstfBytes(1)), [1], SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope
                {
                    ScriptTags = [0x6C61746E], // 'latn' — absent from the JSTF tree
                    FeatureTags = [TagA, TagB]
                })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        byte[] jstf = TableBytes(OutputBytes(result!), JstfTag);
        Ok(JstfView.TryOpen(jstf, out var view, out diagnostic,
            DataLocation.Anonymous(JstfTag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryReadTable(NumGlyphs, 3, 0, null, out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(model);
        Assert.Equal(0, model!.ScriptCount);
    }

    // The JSTF language-only cut: the script trees and the mod domains are whole,
    // so the language dimension alone moves — the verdict is a true cut (S2c card
    // §5.2 with §2.1's language MUST) and the scoped producer drops the excluded
    // 'FRA ' row while the GSUB neighbor (no tagged languages) rides the no-op.
    [Fact]
    public void JstfLanguageOnlyCutPrunesTheExcludedLangSysRow()
    {
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel()), withJstf: true,
                jstf: JstfLanguageBytes()), [1], SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope { LanguageTags = [TagEng] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        bool jstfRebuilt = false;
        foreach (var decision in report!.Decisions)
            if (decision.Tag == JstfTag)
                jstfRebuilt = decision.Action == TableAction.Rebuild &&
                    decision.Policy == SubsetPolicy.RebuildTyped;
        Assert.True(jstfRebuilt);
        byte[] jstf = TableBytes(OutputBytes(result!), JstfTag);
        Ok(JstfView.TryOpen(jstf, out var view, out diagnostic,
            DataLocation.Anonymous(JstfTag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryReadTable(NumGlyphs, 3, 0, null, out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(model);
        Assert.Equal(1, model!.ScriptCount);
        var script = model.Scripts[0].Script;
        // The default langsys rode the script; 'FRA ' dropped, 'ENG ' stayed with
        // its mod list riding the identity neighbor remap (GSUB no-op → [1]).
        Assert.NotNull(script!.DefaultLangSys);
        Assert.Equal(1, script.LangSysCount);
        Assert.Equal(TagEng, script.LangSys[0].Tag);
        Assert.Equal(new ushort[] { 1 }, script.LangSys[0].LangSys.Priorities[0]
            .GsubShrinkageEnable!.LookupIndices);
    }

    // ---- the transparent face close-outs (card §5.3) --------------------------

    [Fact]
    public void GdefStaysRawUnderRetainScopeAndRegistersOrphanMarkSets()
    {
        byte[] gdef = GdefBytes();
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel()), withGdef: true), [1],
                SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope { FeatureTags = [TagA] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        // GDEF is scope-transparent in retain: the decision keeps RETAIN_RAW and
        // the payload moves byte-identically (the identity seam never touches it).
        bool gdefRaw = false;
        foreach (var decision in report!.Decisions)
            if (decision.Tag == SubsetFontFixture.GdefTag)
                gdefRaw = decision.Action == TableAction.Preserve &&
                    decision.Policy == SubsetPolicy.RetainRaw;
        Assert.True(gdefRaw);
        Assert.Equal(gdef, TableBytes(OutputBytes(result!), SubsetFontFixture.GdefTag));
        // The GDEF census row registers both orphan mark sets (no surviving
        // lookup names a set — the flags ride 0).
        var gdefRow = Assert.Single(report.LayoutScopeCensus,
            r => r.Tag == SubsetFontFixture.GdefTag);
        Assert.Equal(2, gdefRow.OrphanMarkSets);
    }

    // ---- the FV conditional-replacement family (OQ-S2c-6) ---------------------

    [Fact]
    public void FvAlternateLookupsJoinTheTransitiveClosureSeeds()
    {
        // The record replaces feature 0 (TagA) with an alternate whose lookups are
        // the orphan {2}: declaring [TagB] keeps feature 1 by tag and feature 0 by
        // nothing — the record dies. But declaring [TagA] keeps the record, and
        // the alternate's lookups were already seeds... the seed-merge witness is
        // the reverse cut: keep only TagB — the record dies with feature 0, and
        // the orphan {2} must NOT join (the record is dead).
        Ok(SubsetOperation.ResolveLayoutListsScopeForTests(
            RemapModel(variations: Variations(0)),
            new SubsetLayoutScope { FeatureTags = [TagB] },
            out var resolution, out var diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(resolution);
        Assert.Equal([1], resolution!.SurvivingFeatures);
        Assert.Equal([1], resolution.SurvivingLookups);
        // Keeping TagA: the record survives and the alternate's lookups ride the
        // seeds — {0} from the feature plus {2} from the nested record and the
        // alternate alike; the cut feature's lookup {1} never joins. The seed
        // merge is the conservative face: an alternate lookup reachable no other
        // way joins exactly here (the non-vacuity witness needs the alternate to
        // name a lookup outside the feature-reachable set, which the pipeline
        // families cover through the seeded-orphan form).
        Ok(SubsetOperation.ResolveLayoutListsScopeForTests(
            RemapModel(variations: Variations(0)),
            new SubsetLayoutScope { FeatureTags = [TagA] },
            out var keep, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.Equal([0, 2], keep!.SurvivingLookups);
    }

    [Fact]
    public void FvRecordDropsWhenItsReplacedFeatureIsCut()
    {
        // Declaring [TagB] cuts feature 0 (TagA): the FV record dies, and the v1.1
        // output carries the variations block with zero records (the legal emptied
        // root) while the surviving subset re-emits.
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel(variations: Variations(0)))), [1],
                SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope { FeatureTags = [TagB] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        byte[] gsub = TableBytes(OutputBytes(result!), SubsetFontFixture.GsubTag);
        Ok(LayoutListsView.TryOpen(gsub, out var view, out diagnostic,
            DataLocation.Anonymous(SubsetFontFixture.GsubTag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryReadTable(NumGlyphs, LayoutLookupKind.Gsub, null, out var model,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(model);
        Assert.Equal(LayoutListsLayout.Version11, model!.Version);
        Assert.NotNull(model.FeatureVariations);
        Assert.Equal(0, model.FeatureVariations!.RecordCount);
        Assert.Equal(1, model.Features.Length);
        Assert.Equal(TagB, model.Features[0].Tag);
    }

    // ---- the short-circuit identity, compact form (card §4.2/§9.3) ------------

    [Fact]
    public void NoOpScopeCompactIsByteIdenticalToUndeclared()
    {
        byte[] gsub = GsubBytes(RemapModel());
        var face = Face(gsub);
        var undeclared = new SubsetOptions { PreserveLayout = true };
        var declared = Scoped(new SubsetLayoutScope
        {
            ScriptTags = [TagDflt],
            FeatureTags = [TagA, TagB]
        });
        var first = SubsetOperation.TryPrepare(
            Request(face, [1], SubsetMode.CompactRemap, undeclared),
            out var plain, out var plainReport, out var diagnostic);
        Assert.Equal(ReadStatus.Done, first);
        NoDiagnostic(diagnostic);
        var second = SubsetOperation.TryPrepare(
            Request(face, [1], SubsetMode.CompactRemap, declared),
            out var scoped, out var scopedReport, out diagnostic);
        Assert.Equal(ReadStatus.Done, second);
        NoDiagnostic(diagnostic);
        Assert.NotNull(plain);
        Assert.NotNull(scoped);
        // The S2b rewrite is map-driven (no short circuit), so the two products
        // ride the same engine over the same retained set — byte-identical fonts,
        // and the scope census rows are the declaration's only extra trace.
        Assert.Equal(OutputBytes(plain!), OutputBytes(scoped!));
        Assert.Equal(plainReport!.LayoutRewriteCensus,
            scopedReport!.LayoutRewriteCensus);
        Assert.Single(scopedReport.LayoutScopeCensus);
    }

    // ---- the short-circuit identity, retain form (card §4.2/§9.3) -------------

    [Fact]
    public void NoOpScopeRetainIsByteIdenticalToUndeclared()
    {
        byte[] gsub = GsubBytes(RemapModel());
        var face = Face(gsub);
        var undeclared = new SubsetOptions { PreserveLayout = true };
        // The declared-full-set form: every script tag and every feature tag the
        // face carries, listed explicitly (§9.3's construction).
        var declared = Scoped(new SubsetLayoutScope
        {
            ScriptTags = [TagDflt],
            FeatureTags = [TagA, TagB]
        });
        var first = SubsetOperation.TryPrepare(
            Request(face, [1], SubsetMode.RetainGids, undeclared),
            out var plain, out var plainReport, out var diagnostic);
        Assert.Equal(ReadStatus.Done, first);
        NoDiagnostic(diagnostic);
        var second = SubsetOperation.TryPrepare(
            Request(face, [1], SubsetMode.RetainGids, declared),
            out var scoped, out var scopedReport, out diagnostic);
        Assert.Equal(ReadStatus.Done, second);
        NoDiagnostic(diagnostic);
        Assert.NotNull(plain);
        Assert.NotNull(scoped);
        // The no-op verdict short-circuits to the undeclared decision: the GSUB
        // rides RETAIN_RAW and the whole font is byte-identical (§4.2's
        // consequence-equivalence judgment — the S2a freeze extends to the
        // "declared but nothing cut" form).
        Assert.Equal(OutputBytes(plain!), OutputBytes(scoped!));
        bool gsubRaw = false;
        foreach (var decision in scopedReport!.Decisions)
            if (decision.Tag == SubsetFontFixture.GsubTag)
                gsubRaw = decision.Action == TableAction.Preserve &&
                    decision.Policy == SubsetPolicy.RetainRaw;
        Assert.True(gsubRaw);
        Assert.Empty(plainReport!.LayoutRewriteCensus);
        Assert.Empty(scopedReport.LayoutRewriteCensus);
    }

    [Fact]
    public void DanglingSupersetDeclarationStaysTheNoOpIdentity()
    {
        byte[] gsub = GsubBytes(RemapModel());
        var face = Face(gsub);
        var undeclared = new SubsetOptions { PreserveLayout = true };
        // The superset form (§9.3's twemoji face): the declaration names every
        // real tag plus tags the face does not carry — the dangling tags resolve
        // to zero matches (OQ-S2c-7, never refused), the verdict stays no-op, and
        // the product is byte-identical to the undeclared request.
        var declared = Scoped(new SubsetLayoutScope
        {
            ScriptTags = [TagDflt, 0x6C61746E], // 'DFLT' + 'latn' (absent)
            LanguageTags = [0x41505048], // 'APPH' (absent)
            FeatureTags = [TagA, TagB, 0x7A7A7A7A] // real + 'zzzz' (absent)
        });
        var first = SubsetOperation.TryPrepare(
            Request(face, [1], SubsetMode.RetainGids, undeclared),
            out var plain, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, first);
        NoDiagnostic(diagnostic);
        var second = SubsetOperation.TryPrepare(
            Request(face, [1], SubsetMode.RetainGids, declared),
            out var scoped, out var scopedReport, out diagnostic);
        Assert.Equal(ReadStatus.Done, second);
        NoDiagnostic(diagnostic);
        Assert.NotNull(plain);
        Assert.NotNull(scoped);
        Assert.Equal(OutputBytes(plain!), OutputBytes(scoped!));
        // The dangling faces are visible in the census: the latn/APPH/zzzz
        // declarations read zero matches, the real tags read one each.
        var scopeRow = Assert.Single(scopedReport!.LayoutScopeCensus);
        Assert.Equal(1, scopeRow.ScriptTagMatches);
        Assert.Equal(0, scopeRow.LanguageTagMatches);
        Assert.Equal(2, scopeRow.FeatureTagMatches);
    }

    // ---- the required feature's pipeline-level keep (OQ-S2c-5) ----------------

    [Fact]
    public void RequiredFeatureRidesTheScopedRewriteUndeclared()
    {
        // The langsys's required feature (TagA, index 0) is implicitly kept while
        // only TagB is declared: the output keeps both features (the required one
        // re-indexed) and all three lookups, and the langsys's required index
        // remaps to the surviving position.
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel(requiredFeatureIndex: 0))), [1],
                SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope { FeatureTags = [TagB] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        byte[] gsub = TableBytes(OutputBytes(result!), SubsetFontFixture.GsubTag);
        Ok(LayoutListsView.TryOpen(gsub, out var view, out diagnostic,
            DataLocation.Anonymous(SubsetFontFixture.GsubTag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryReadTable(NumGlyphs, LayoutLookupKind.Gsub, null, out var model,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(model);
        Assert.Equal(2, model!.Features.Length);
        // The wire order rides: feature 0 (TagA, the required entry) survives in
        // place, feature 1 (TagB, the declared tag) follows.
        Assert.Equal(TagA, model.Features[0].Tag);
        Assert.Equal(TagB, model.Features[1].Tag);
        Assert.Equal(3, model.Lookups.Length);
        var defaultLangSys = model.Scripts[0].DefaultLanguage;
        Assert.NotNull(defaultLangSys);
        Assert.Equal(0, defaultLangSys!.RequiredFeatureIndex);
        Assert.Equal(new ushort[] { 0, 1 }, defaultLangSys.FeatureIndices.ToArray());
    }

    // ---- S2c-BF-001: the resolution/rewrite budget exhaustion -----------------

    [Fact]
    public void BudgetExhaustionRefusesWithNoPartialProduct()
    {
        // A work-unit budget the scope resolution alone exhausts: the operation
        // refuses ResourceLimit with no font bytes and the report keeps the
        // diagnostic (the S0-BF-002 posture, the S2c card §7 RF-S2c-5 row).
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel())), [1], SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope { FeatureTags = [TagA] })),
            out var result, out var report, out var diagnostic,
            new BuildLimits { MaxWorkUnits = 8 });
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Null(result);
        Assert.NotNull(report);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
    }
}
