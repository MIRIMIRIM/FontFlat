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
            refs.UnionWith(SubsetOperation.CollectLookupReferencesForTests(
                l, model.NumGlyphs));
        Assert.Equal(new HashSet<int> { 1, 3 }, refs);
    }

    // The kern reader used to be handed the operation's BuildLimits, which opened a
    // second BuildBudget: the per-operation ceiling then compared the main budget's
    // usage alone, so a request whose main budget sat under the ceiling and whose kern
    // reader charged 65,584 bytes of its own still reported Done. The reader now
    // charges the running budget, so the same request refuses.
    [Fact]
    public void KernReadChargesTheOperationBudget()
    {
        const ushort glyphCount = 128;
        var pairs = Enumerable.Range(0, 8192)
            .Select(i => new KernPair((ushort)(i / 128), (ushort)(i % 128), -1)).ToArray();
        Ok(KernSubtableCompiler.TryCreateFormat0(glyphCount, pairs, 1, out var sub,
            out var diagnostic));
        Ok(KernTableCompiler.TryCreate(KernDialect.Version0, glyphCount, [sub!],
            out var table, out diagnostic));
        Ok(KernTableCompiler.TryCompile(table!, out var kern, out diagnostic));
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(glyphCount)),
            (SubsetFontFixture.KernTag, kern!),
            (SubsetFontFixture.GlyfTag, []),
            (SubsetFontFixture.LocaTag, new byte[4 * (glyphCount + 1)]),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))));
        var limits = new BuildLimits { MaxTemporaryBytes = 70_000 };
        var status = SubsetOperation.TryPrepare(
            Request(face, [1], new SubsetOptions { PreserveLayout = true }), out var result,
            out _, out var failure, limits);
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, failure.Code);
        Assert.Equal(DataField.TemporaryBytes, failure.Location.Field);
        Assert.Null(result);
    }

    // The MATH admission shares the kern contract: the reader charges the running
    // operation budget. The same ceiling passes the bare face and refuses once the
    // MATH read lands in the same accounting domain (a fresh BuildLimits would have
    // kept the table's charge in a second budget and returned Done).
    [Fact]
    public void MathReadChargesTheOperationBudget()
    {
        const int numGlyphs = 4100, count = 2000, variantsPerGlyph = 20;
        FontDiagnostic diagnostic = default;
        var constructions = new MathGlyphConstructionTable[count];
        for (int i = 0; i < count; i++)
        {
            var variants = Enumerable.Range(0, variantsPerGlyph)
                .Select(k => new MathGlyphVariantRecord((ushort)((i * variantsPerGlyph + k) % 4000),
                    (ushort)k)).ToArray();
            Ok(MathTableCompiler.TryCreate(null, variants, numGlyphs,
                out var construction, out diagnostic));
            constructions[i] = construction!;
        }
        var coverageGlyphs = Enumerable.Range(0, count).Select(i => (ushort)(2 * i)).ToArray();
        Ok(CoverageTableCompiler.TryCreate(coverageGlyphs, numGlyphs, 1, out var coverage,
            out diagnostic));
        Ok(MathTableCompiler.TryCreate(0, coverage!, coverage!, constructions, constructions,
            out var variantsTable, out diagnostic));
        Ok(MathTableCompiler.TryCreate(0x00010000, numGlyphs, null, null, variantsTable,
            out var math, out diagnostic));
        Ok(MathTableCompiler.TryCompile(math!, out var mathBytes, out diagnostic));
        var extras = new (uint Tag, byte[] Bytes)[]
        {
            (0x4D415448, mathBytes!),
        };
        ReaderChargesTheOperationBudget(numGlyphs, extras, mathBytes!.Length);
    }

    // The BASE admission answers the fmt2 census through the same shared budget as
    // kern and MATH; the ceiling that passes the bare face must refuse the face
    // carrying the table instead of letting the read charge a second budget.
    [Fact]
    public void BaseReadChargesTheOperationBudget()
    {
        const int numGlyphs = 64, scriptCount = 100, coordCount = 100;
        FontDiagnostic diagnostic = default;
        var scripts = new BaseScriptRecord[scriptCount];
        for (int i = 0; i < scriptCount; i++)
        {
            var coords = Enumerable.Range(0, coordCount)
                .Select(k => BaseCoord(i * coordCount + k)).ToArray();
            Ok(BaseAxisTableCompiler.TryCreate(0, coords, out var values, out diagnostic));
            Ok(BaseAxisTableCompiler.TryCreate(values, null,
                ReadOnlySpan<BaseLangSysRecord>.Empty, out var script, out diagnostic));
            scripts[i] = new BaseScriptRecord(0x61726162u + (uint)i, script!);
        }
        Ok(BaseAxisTableCompiler.TryCreate(null, scripts, out var axis, out diagnostic));
        Ok(BaseTableCompiler.TryCreate(0x00010000, numGlyphs, axis, null, null,
            out var model, out diagnostic));
        Ok(BaseTableCompiler.TryCompile(model!, out var baseBytes, out diagnostic));
        var extras = new (uint Tag, byte[] Bytes)[]
        {
            (0x42415345, baseBytes!),
        };
        ReaderChargesTheOperationBudget(numGlyphs, extras, baseBytes!.Length);
    }

    // The shared kern/MATH/BASE witness shape: run the bare face and the face
    // carrying the table under a generous ceiling, then split the measured usages
    // down the middle. The bare face must still pass at the split (it finished
    // below it), while the carrying face must refuse there — its read charge lands
    // in the one operation budget, so nothing may cross the ceiling unaccounted.
    private static void ReaderChargesTheOperationBudget(int numGlyphs,
        (uint Tag, byte[] Bytes)[] extras, long tableLength)
    {
        FontFaceRef BareFace()
        {
            var tables = new List<(uint, byte[])>
            {
                (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
                (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp((ushort)numGlyphs)),
                (SubsetFontFixture.GlyfTag, Array.Empty<byte>()),
                (SubsetFontFixture.LocaTag, new byte[4 * (numGlyphs + 1)]),
                (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))),
            };
            tables.AddRange(extras);
            return SubsetFontFixture.Face(tables.ToArray());
        }
        var bareFace = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp((ushort)numGlyphs)),
            (SubsetFontFixture.GlyfTag, Array.Empty<byte>()),
            (SubsetFontFixture.LocaTag, new byte[4 * (numGlyphs + 1)]),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))));
        var face = BareFace();
        var options = new SubsetOptions { PreserveLayout = true };
        const long generous = 64_000_000;
        Ok(SubsetOperation.TryPrepare(Request(bareFace, [1], options), out var bare,
            out _, out _, new BuildLimits { MaxTemporaryBytes = generous }));
        Ok(SubsetOperation.TryPrepare(Request(face, [1], options), out var full,
            out _, out _, new BuildLimits { MaxTemporaryBytes = generous }));
        Assert.NotNull(bare);
        Assert.NotNull(full);
        long bareUsage = bare.Font.Usage.TemporaryBytes;
        long fullUsage = full.Font.Usage.TemporaryBytes;
        Assert.True(fullUsage > bareUsage + tableLength,
            $"carrying usage {fullUsage} must exceed bare {bareUsage} plus the raw table {tableLength}");
        long ceiling = (bareUsage + tableLength + fullUsage) / 2;
        Assert.Equal(ReadStatus.Done, SubsetOperation.TryPrepare(Request(bareFace, [1], options),
            out var under, out _, out _, new BuildLimits { MaxTemporaryBytes = ceiling }));
        Assert.NotNull(under);
        var status = SubsetOperation.TryPrepare(Request(face, [1], options), out var result,
            out _, out var failure, new BuildLimits { MaxTemporaryBytes = ceiling });
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, failure.Code);
        Assert.Equal(DataField.TemporaryBytes, failure.Location.Field);
        Assert.Null(result);
    }

    private static BaseCoordTable BaseCoord(int value) =>
        BaseCoordTableCompiler.TryCreate(1, (short)(value % 30000), 0, 0, null, 64,
            out var model, out _) == ReadStatus.Done
            ? model! : throw new Xunit.Sdk.XunitException("base coord factory refused");

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
