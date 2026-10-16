// S2a layout admission + closure unit tests (S2a card §2/§3, commit ② acceptance).
//
// A synthetic GSUB (one type-1 single-substitution lookup) drives the whole
// PreserveLayout path at the unit level: admission through the whole-table read
// chain, the single-pass reference union merging into the retained bitmap, the
// report facts, the byte identity of the retained layout payload, and the
// code-23 refusal faces. The post-closure verification's non-vacuity is proven
// directly: a stale bitmap that misses one referenced glyph refuses.
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetLayoutClosureTests
{
    private const int NumGlyphs = 8;

    private static void Ok(ReadStatus status)
    {
        Assert.Equal(ReadStatus.Done, status);
    }

    private static byte[] GsubBytes()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1, 2 },
            NumGlyphs, 1, out var coverage, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1, 2 },
            stackalloc ushort[] { 3, 4 }, NumGlyphs, 1, out var single, out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [single!],
            0, null, null, out var lookup, out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(LayoutListsTableCompiler.TryCreate(LayoutListsLayout.Version10, 0,
            [], [], [lookup!], NumGlyphs, LayoutLookupKind.Gsub, out var model,
            out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(GsubTableCompiler.TryCompile(model!, null, out var bytes,
            out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        return bytes!;
    }

    private static FontFaceRef Face(byte[] gsub) => SubsetFontFixture.Face(
        (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
        (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
        (SubsetFontFixture.GsubTag, gsub),
        (SubsetFontFixture.GlyfTag,
            [.. GlyfImages.NoContourGlyphRecord(), .. GlyfImages.NoContourGlyphRecord(),
             .. GlyfImages.NoContourGlyphRecord(), .. GlyfImages.NoContourGlyphRecord(),
             .. GlyfImages.NoContourGlyphRecord(), .. GlyfImages.NoContourGlyphRecord(),
             .. GlyfImages.NoContourGlyphRecord(), .. GlyfImages.NoContourGlyphRecord()]),
        (SubsetFontFixture.LocaTag,
            GlyfImages.LongLoca([0u, 12, 24, 36, 48, 60, 72, 84, 96])),
        (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical(
            (0x41, 1), (0x42, 2))));

    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        SubsetOptions options) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = SubsetMode.RetainGids,
        Options = options
    };

    // The whole PreserveLayout chain on a real typed GSUB: the coverage glyphs {1, 2}
    // and the substitution outputs {3, 4} merge into the retained set beyond the
    // {0, 1} seed, the layout payload moves byte-identically, and the report keeps
    // the three facts (layout additions, reference census, disjoint composite list).
    [Fact]
    public void PreserveLayoutExecutesAndClosesReferences()
    {
        byte[] gsub = GsubBytes();
        var options = new SubsetOptions { PreserveLayout = true };
        var status = SubsetOperation.TryPrepare(
            Request(Face(gsub), [1], options), out var result, out var report,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        Assert.Equal(new ushort[] { 2, 3, 4 }, report!.LayoutClosureAddedGids);
        // The composite closure added nothing (no composites): the two addition
        // lists stay disjoint by construction.
        Assert.Empty(report.ClosureAddedGids);
        var census = Assert.Single(report.LayoutReferenceCensus);
        Assert.Equal(SubsetFontFixture.GsubTag, census.Tag);
        Assert.Equal(4, census.ReferencedGidCount);
        // The layout family retained raw: the GSUB payload is byte-identical.
        var gsubAction = TableAction.Drop;
        foreach (var decision in result!.Font.Decisions)
            if (decision.Tag == SubsetFontFixture.GsubTag)
                gsubAction = decision.Action;
        Assert.Equal(TableAction.Preserve, gsubAction);
        byte[] output = OutputBytes(result);
        byte[] outGsub = TableBytes(output, SubsetFontFixture.GsubTag);
        Assert.Equal(gsub, outGsub);
    }

    // RF-A (S2a card §5): a layout body the whole-table read chain rejects refuses
    // with the reader's own diagnostic, never the decide-stage code-23 refusal.
    [Fact]
    public void PreserveLayoutMalformedLayoutRefusesThroughReadChain()
    {
        var options = new SubsetOptions { PreserveLayout = true };
        var status = SubsetOperation.TryPrepare(
            Request(Face(SubsetFontFixture.Dummy()), [1], options), out var result,
            out var report, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.NotEqual(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.GsubTag, diagnostic.Location.TableTag);
        Assert.Null(result);
        Assert.NotNull(report);
    }

    // S2a card §3 layer 1: the post-closure assertion is not vacuously true — a
    // bitmap that misses one referenced glyph fails it under the code-23 contract.
    [Fact]
    public void ClosureVerificationCatchesAMissingGlyph()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1, 2 },
            NumGlyphs, 1, out var coverage, out var diagnostic));
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1, 2 },
            stackalloc ushort[] { 3, 4 }, NumGlyphs, 1, out var single, out diagnostic));
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [single!],
            0, null, null, out var lookup, out diagnostic));
        Ok(LayoutListsTableCompiler.TryCreate(LayoutListsLayout.Version10, 0,
            [], [], [lookup!], NumGlyphs, LayoutLookupKind.Gsub, out var model,
            out diagnostic));
        var retained = new bool[NumGlyphs];
        retained[0] = retained[1] = retained[2] = retained[4] = true;
        Assert.False(SubsetOperation.VerifyLayoutClosureRetained(
            new SubsetOperation.LayoutAdmission(null, model!, null, null, null, null),
            retained, out var violation));
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, violation.Code);
        Assert.Equal(3UL, violation.Detail);
        retained[3] = true;
        Assert.True(SubsetOperation.VerifyLayoutClosureRetained(
            new SubsetOperation.LayoutAdmission(null, model!, null, null, null, null),
            retained, out violation));
        Assert.Equal(DiagnosticCode.None, violation.Code);
    }

    // The class-domain forms of the OQ-S2a-6 lean: a format-2 pair-position matrix
    // and a 0-class rule cell both merge their upper bound — the full glyph domain.
    [Fact]
    public void ClassZeroExpandsToTheFullGlyphDomain()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            NumGlyphs, 1, out var coverage, out var diagnostic));
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            stackalloc ushort[] { 3 }, NumGlyphs, 1, out var single, out diagnostic));
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [single!],
            0, null, null, out var lookup, out diagnostic));
        Ok(LayoutListsTableCompiler.TryCreate(LayoutListsLayout.Version10, 0,
            [], [], [lookup!], NumGlyphs, LayoutLookupKind.Gsub, out var model,
            out diagnostic));
        var refs = new HashSet<int>();
        foreach (var l in model!.Lookups)
            SubsetOperation.CollectLookupReferencesForTests(l, model.NumGlyphs, refs);
        Assert.Equal(new HashSet<int> { 1, 3 }, refs);
    }

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
}
