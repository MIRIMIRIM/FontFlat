// S2c scoped layout rewrite unit tests (S2c card §4/§5, commit ② acceptance).
//
// The scoped cascade executes at the unit level over synthetic GSUB faces in both
// modes: the retain+scope re-emission rides the identity seam (no map exists — the
// unchanged GID domain, only the rule/feature/lookup lists shrink), the compact+scope
// re-emission rides the frozen map, and the record re-answer re-validates against the
// real new lookup count (the nested-target remap witness). The re-read assertions go
// through the typed views — the same read chain the pipeline validates against. The
// JSTF interim face pins the pending-producer refusal the slice's third commit
// replaces with the script/lang pruning and mod remap.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetLayoutScopeRewriteTests
{
    private const int NumGlyphs = 16;
    private const uint TagA = 0x41424344; // 'ABCD' — the contextual feature
    private const uint TagB = 0x42434445; // 'BCDE' — the plain feature
    private const uint TagDflt = 0x44464C54; // 'DFLT'
    private const uint JstfTag = 0x4A535446; // 'JSTF'

    private static void Ok(ReadStatus status)
    {
        Assert.Equal(ReadStatus.Done, status);
    }

    private static void NoDiagnostic(FontDiagnostic diagnostic)
    {
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
    }

    // lookup0: the caller's contextual/chaining witness (the shared three-lookup
    // tail rides the same shape for every contextual format). lookup1: the plain
    // single {1}→{2} behind feature TagB (the cut witness). lookup2: the orphan
    // single {6}→{7} no feature names (reachable only through the record).
    // Declaring FeatureTags=[TagA] keeps {0, 2}: lookup2's new index is 1, so the
    // surviving record must re-answer 2→1 against the real new count.
    private static LayoutListsTable RemapModel()
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
        return ThreeLookupModel(contextualLookup!);
    }

    // The shared three-lookup tail: `first` rides lookup index 0 behind feature
    // TagA, the plain and orphan singles fill indices 1/2, one DFLT script and
    // the two features close the lists.
    private static LayoutListsTable ThreeLookupModel(LookupTable first)
    {
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            stackalloc ushort[] { 2 }, NumGlyphs, 1, out var plainSingle, out var diagnostic));
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
        Ok(LayoutListsTableCompiler.TryCreateLangSys(ushort.MaxValue,
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
        Ok(LayoutListsTableCompiler.TryCreate(LayoutListsLayout.Version10, 0,
            [script!], [featureA!, featureB!],
            [first, plainLookup!, orphanLookup!], NumGlyphs,
            LayoutLookupKind.Gsub, out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        return model!;
    }

    // The format-2 witness lookup: contextual (GSUB 5 fmt 2) over coverage {4} —
    // glyph 4 carries class 0, the one class-0 rule set's rule (stored input class
    // sequence [0]) targets lookup2. Class values never renumber, so only the
    // record's target moves under the scope cut (card §5.1 step 2).
    private static LookupTable ContextualClassLookup()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 4 }, NumGlyphs, 1,
            out var classCoverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate([new ClassDefRange(4, 4, 0)], NumGlyphs, 2,
            out var classDef, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreateRule(stackalloc ushort[] { 0 },
            [new SequenceLookupRecord(0, 2)], 3, out var rule, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreateRuleSet([rule!], out var ruleSet,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreate(classCoverage!, classDef!,
            new ContextualRuleSet?[] { ruleSet }, NumGlyphs, out var contextual,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateContextualSubst(NumGlyphs, [contextual!],
            0, null, null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        return lookup!;
    }

    // The chaining analog: GSUB 6 fmt 2 over coverage {4} with the three class
    // definitions sharing one body (glyph 4 → class 0), the rule carrying empty
    // backtrack/lookahead and the lookup2-targeting record.
    private static LookupTable ChainingClassLookup()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 4 }, NumGlyphs, 1,
            out var classCoverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate([new ClassDefRange(4, 4, 0)], NumGlyphs, 2,
            out var classDef, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ChainingSubstTableCompiler.TryCreateRule(stackalloc ushort[] { },
            stackalloc ushort[] { 0 }, stackalloc ushort[] { },
            [new SequenceLookupRecord(0, 2)], 3, out var rule, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ChainingSubstTableCompiler.TryCreateRuleSet([rule!], out var ruleSet,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ChainingSubstTableCompiler.TryCreate(classCoverage!, classDef!, classDef!,
            classDef!, new ChainingRuleSet?[] { ruleSet }, NumGlyphs, out var chaining,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateChainingSubst(NumGlyphs, [chaining!],
            0, null, null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        return lookup!;
    }

    /// <summary>A valid empty JSTF 1.0 (zero scripts) — enough to put the table on
    /// the face so the profile admission admits it and the decide row exists.</summary>
    private static byte[] JstfBytes()
    {
        Ok(JstfTableCompiler.TryCreate(JstfLayout.Version10, NumGlyphs, 3, 0, null,
            [], out var model, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(JstfTableCompiler.TryCompile(model!, out var bytes, out diagnostic));
        NoDiagnostic(diagnostic);
        return bytes!;
    }

    private static byte[] GsubBytes(LayoutListsTable model)
    {
        Ok(GsubTableCompiler.TryCompile(model, null, out var bytes, out var diagnostic));
        NoDiagnostic(diagnostic);
        return bytes!;
    }

    private static FontFaceRef Face(byte[] gsub, bool withJstf = false,
        params (uint Tag, byte[] Bytes)[] extra)
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
        if (withJstf) tables.Add((JstfTag, JstfBytes()));
        tables.AddRange(extra);
        return SubsetFontFixture.Face([.. tables]);
    }

    private const uint BaseTag = 0x42415345;

    // One DFLT script carrying one fmt2 coord over referenceGlyph — the scope
    // combo's BASE witness (the scope-transparent row, OQ-S2c-9).
    private static byte[] BaseBytes(ushort referenceGlyph)
    {
        Ok(BaseAxisTableCompiler.TryCreate(new[] { 0x64736372u }, out var tagList,
            out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(BaseCoordTableCompiler.TryCreate(2, -100, referenceGlyph, 0, null,
            NumGlyphs, out var coord, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(BaseAxisTableCompiler.TryCreate(0, new[] { coord! }, out var values,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(BaseAxisTableCompiler.TryCreate(values, null,
            Array.Empty<BaseLangSysRecord>(), out var script, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(BaseAxisTableCompiler.TryCreate(tagList,
            new[] { new BaseScriptRecord(TagDflt, script!) }, out var axis,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(BaseTableCompiler.TryCreate(BaseLayout.Version10, NumGlyphs, axis, null,
            null, out var baseModel, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(BaseTableCompiler.TryCompile(baseModel!, out var bytes, out diagnostic));
        NoDiagnostic(diagnostic);
        return bytes!;
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

    private static LayoutListsTable ReadBack(byte[] sfnt, int numGlyphs = NumGlyphs)
    {
        byte[] gsub = TableBytes(sfnt, SubsetFontFixture.GsubTag);
        Ok(LayoutListsView.TryOpen(gsub, out var view, out var diagnostic,
            DataLocation.Anonymous(SubsetFontFixture.GsubTag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryReadTable(numGlyphs, LayoutLookupKind.Gsub, null, out var model,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(model);
        return model!;
    }

    private static SubsetOptions Scoped(SubsetLayoutScope scope) => new()
    {
        PreserveLayout = true,
        LayoutScope = scope
    };

    // The retain execution face (card §4.1): the fifth alternate re-emits the
    // surviving subset over the identity seam — the output carries exactly the
    // surviving script/feature/lookup sets, the nested target re-indexed, and the
    // report keeps the scoped closure facts plus the census rows.
    [Fact]
    public void ScopedRetainReEmitsSurvivingSubsetOverIdentitySeam()
    {
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel())), [1], SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope { FeatureTags = [TagA] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        // The scoped closure pulled only the surviving lookups' references beyond
        // the seed: the contextual's coverage/input {4, 5} and the orphan's
        // {6, 7} — the full surviving-rule union (card §4.3); the cut plain
        // lookup's substitute {2} never joins.
        Assert.Equal(new ushort[] { 4, 5, 6, 7 }, report!.LayoutClosureAddedGids);
        // The output GSUB: one script, one feature (TagA), two lookups — the
        // contextual at 0 and the orphan single at 1 (the record's re-answer).
        var model = ReadBack(OutputBytes(result!));
        Assert.Equal(1, model.Scripts.Length);
        Assert.Equal(TagDflt, model.Scripts[0].Tag);
        Assert.Equal(1, model.Features.Length);
        Assert.Equal(TagA, model.Features[0].Tag);
        Assert.Equal(2, model.Lookups.Length);
        // The remap witness: the contextual rule's record targets new index 1 —
        // the re-answered orphan — and the domain check ran against the real
        // new count (a stale 2 would be out of domain and refused).
        var contextual = model.Lookups[0];
        Assert.NotNull(contextual.Contextuals);
        var rule = contextual.Contextuals![0].RuleSets![0]!.Rules[0];
        var record = Assert.Single(rule.LookupRecords.ToArray());
        Assert.Equal(1, record.LookupListIndex);
        // The orphan single rode the identity seam: its coverage glyph is 6.
        var orphan = model.Lookups[1];
        Assert.NotNull(orphan.SingleSubstitutions);
        Assert.Equal(6, orphan.SingleSubstitutions![0].Coverage.Glyphs[0]);
        // The census: the scoped rewrite row reads in≠out legally, the scope
        // census keeps the resolution counts and the match counts.
        var rewriteRow = Assert.Single(report.LayoutRewriteCensus,
            r => r.Tag == SubsetFontFixture.GsubTag);
        Assert.Equal(3, rewriteRow.InputSubtableCount);
        Assert.Equal(2, rewriteRow.OutputSubtableCount);
        Assert.False(rewriteRow.CountsInvarianceHeld);
        var scopeRow = Assert.Single(report.LayoutScopeCensus);
        Assert.Equal(SubsetFontFixture.GsubTag, scopeRow.Tag);
        Assert.Equal(1, scopeRow.SurvivingScriptCount);
        Assert.Equal(1, scopeRow.SurvivingFeatureCount);
        Assert.Equal(2, scopeRow.SurvivingLookupCount);
        Assert.Equal(1, scopeRow.FeatureTagMatches);
    }

    // The compact execution face: the same cut over the frozen map — the scoped
    // cascade re-emits the surviving subset over the map's output domain and the
    // map artifact publishes as usual.
    [Fact]
    public void ScopedCompactReEmitsSurvivingSubsetOverFrozenMap()
    {
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel())), [1, 4, 5, 6], SubsetMode.CompactRemap,
                Scoped(new SubsetLayoutScope { FeatureTags = [TagA] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        Assert.NotNull(result!.Map);
        Assert.Equal(6, result.Map.OutputGlyphCount);
        var model = ReadBack(OutputBytes(result), 6);
        Assert.Equal(2, model.Lookups.Length);
        Assert.Equal(1, model.Features.Length);
        var orphan = model.Lookups[1];
        Assert.Equal(4, orphan.SingleSubstitutions![0].Coverage.Glyphs[0]);
        var scopeRow = Assert.Single(report!.LayoutScopeCensus);
        Assert.Equal(2, scopeRow.SurvivingLookupCount);
    }

    // The scope-transparent row's compact face (OQ-S2c-9 with the OQ-S2b-6
    // upgrade): a declared feature scope truly cuts the GSUB lists while the
    // fmt2-bearing BASE rebuilds its whole axis tree — the scope never prunes
    // the BASE script tree, and the fmt2 reference rides the same frozen map
    // the scoped GSUB saw (the closure merged it beyond the seed).
    [Fact]
    public void ScopedCompactRebuildsTheWholeBaseTreeAndRemapsFormat2()
    {
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel()),
                extra: [(BaseTag, BaseBytes(9))]), [1, 4, 5, 6], SubsetMode.CompactRemap,
                Scoped(new SubsetLayoutScope { FeatureTags = [TagA] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        Assert.Equal(new ushort[] { 7, 9 }, report!.LayoutClosureAddedGids);
        Assert.Equal(7, result.Map!.OutputGlyphCount);
        var decision = Assert.Single(report.Decisions, d => d.Tag == BaseTag);
        Assert.Equal(TableAction.Rebuild, decision.Action);
        Assert.Equal(SubsetPolicy.RebuildTyped, decision.Policy);
        // The scoped GSUB: two lookups over the new count; the BASE: the whole
        // tree (one script — unpruned), the reference at its mapped image.
        var model = ReadBack(OutputBytes(result!), 7);
        Assert.Equal(2, model.Lookups.Length);
        Assert.Equal(1, model.Features.Length);
        byte[] basePayload = TableBytes(OutputBytes(result!), BaseTag);
        Ok(BaseView.TryOpen(basePayload, out var baseView, out diagnostic,
            DataLocation.Anonymous(BaseTag)));
        NoDiagnostic(diagnostic);
        Ok(baseView!.TryReadTable(7, 0, out var baseModel, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.Equal(1, baseModel!.HorizAxis!.ScriptCount);
        var coord = baseModel.HorizAxis.Scripts[0].Script.BaseValues!.Coords[0];
        Assert.Equal(2, coord.Format);
        Assert.Equal(6, coord.ReferenceGlyph);
        Assert.Equal(-100, coord.Coordinate);
        // The rewrite census: the scoped GSUB row reads in≠out legally, the
        // BASE row keeps its zero-slot-loss in==out fact.
        var baseRow = Assert.Single(report.LayoutRewriteCensus, r => r.Tag == BaseTag);
        Assert.Equal(1, baseRow.InputSubtableCount);
        Assert.Equal(1, baseRow.OutputSubtableCount);
        Assert.True(baseRow.CountsInvarianceHeld);
    }

    // The format-2 record remap (card §5.1 step 2): the class-valued rule bodies
    // re-answer their records too — the surviving record's target re-indexes to
    // the cut subset (2→1), the class values and the rule-set array ride.
    [Fact]
    public void ScopedRetainRemapsContextualFormat2Records()
    {
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(ThreeLookupModel(ContextualClassLookup()))), [1],
                SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope { FeatureTags = [TagA] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        var model = ReadBack(OutputBytes(result!));
        Assert.Equal(2, model.Lookups.Length);
        Assert.Equal(1, model.Features.Length);
        var contextual = model.Lookups[0];
        Assert.NotNull(contextual.Contextuals);
        Assert.Equal(2, contextual.Contextuals![0].Format);
        // The class domain kept glyph 4's class-0 entry and the rule-set array
        // (class-value keyed) kept its one row; the record re-answered 2→1 —
        // a stale 2 would refuse the read chain against the new count.
        var ranges = contextual.Contextuals![0].ClassDef!.Ranges.ToArray();
        Assert.Equal(new ClassDefRange(4, 4, 0), Assert.Single(ranges));
        var rule = contextual.Contextuals![0].RuleSets![0]!.Rules[0];
        var record = Assert.Single(rule.LookupRecords.ToArray());
        Assert.Equal(1, record.LookupListIndex);
        var scopeRow = Assert.Single(report!.LayoutScopeCensus);
        Assert.Equal(2, scopeRow.SurvivingLookupCount);
    }

    // The chaining format-2 analog: the class-valued chaining rule's record
    // re-answers identically (the class sequences never move — class values are
    // the untouched dimension).
    [Fact]
    public void ScopedRetainRemapsChainingFormat2Records()
    {
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(ThreeLookupModel(ChainingClassLookup()))), [1],
                SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope { FeatureTags = [TagA] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        var model = ReadBack(OutputBytes(result!));
        Assert.Equal(2, model.Lookups.Length);
        var chaining = model.Lookups[0];
        Assert.NotNull(chaining.Chainings);
        Assert.Equal(2, chaining.Chainings![0].Format);
        var rule = chaining.Chainings![0].RuleSets![0]!.Rules[0];
        Assert.Equal(new ushort[] { 0 }, rule.InputSequence.ToArray());
        var record = Assert.Single(rule.LookupRecords.ToArray());
        Assert.Equal(1, record.LookupListIndex);
    }

    // The script tier: cutting every script prunes the tree and leaves the legal
    // zero-count ScriptList root; the feature/lookup dimensions are untouched by
    // a script-only declaration (the conservative face of §5.1 step 3).
    [Fact]
    public void ScopedScriptCutLeavesTheLegalEmptyRoot()
    {
        var status = SubsetOperation.TryPrepare(
            Request(Face(GsubBytes(RemapModel())), [1], SubsetMode.RetainGids,
                Scoped(new SubsetLayoutScope { ScriptTags = [] })),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        var model = ReadBack(OutputBytes(result!));
        Assert.Equal(0, model.Scripts.Length);
        Assert.Equal(2, model.Features.Length);
        Assert.Equal(3, model.Lookups.Length);
        var scopeRow = Assert.Single(report!.LayoutScopeCensus);
        Assert.Equal(0, scopeRow.SurvivingScriptCount);
        // An empty declaration matches nothing — the count stays zero.
        Assert.Equal(0, scopeRow.ScriptTagMatches);
    }

    // The JSTF no-op face: an empty JSTF tree under a declared scope resolves to
    // the no-op verdict — the undeclared decision (raw Preserve) rides and the
    // output is byte-identical; the true-cut forms live in the families tests.
    [Fact]
    public void DeclaredScopeOnAnEmptyJstfFaceIsTheNoOpShortCircuit()
    {
        byte[] gsub = GsubBytes(RemapModel());
        byte[] jstf = JstfBytes();
        var options = Scoped(new SubsetLayoutScope { ScriptTags = [TagDflt] });
        var status = SubsetOperation.TryPrepare(
            Request(Face(gsub, withJstf: true), [1], SubsetMode.RetainGids, options),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        // The raw copy: the JSTF payload moves byte-identically.
        Assert.Equal(jstf, TableBytes(OutputBytes(result!), JstfTag));
        // The scope census carries the JSTF row (the zero-script verdict).
        Assert.Contains(report!.LayoutScopeCensus, r => r.Tag == JstfTag);
    }
}
