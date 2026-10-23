using FontWeave.Core;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetReachableClosureTests
{
    private const int GlyphCount = 8;

    [Fact]
    public void CompactSharesTheWorklistAcrossSubstitutionsAndComposites()
    {
        const int pairs = 512, count = pairs * 2 + 2;
        ushort[] inputs = Enumerable.Range(0, pairs).Select(i => (ushort)(2 * i + 1)).ToArray();
        ushort[] outputs = inputs.Select(i => (ushort)(i + 1)).ToArray();
        Assert.Equal(ReadStatus.Done, SingleSubstTableCompiler.TryCreate(inputs, outputs,
            count, 1, out var single, out _));
        Assert.Equal(ReadStatus.Done, LookupTableCompiler.TryCreateSingleSubst(count,
            [single!], 0, null, null, out var lookup, out _));
        Assert.Equal(ReadStatus.Done, LayoutListsTableCompiler.TryCreate(
            LayoutListsLayout.Version10, 0, [], [], [lookup!], count,
            LayoutLookupKind.Gsub, out var table, out _));
        Assert.Equal(ReadStatus.Done, GsubTableCompiler.TryCompile(table!, null, out var gsub, out _));
        var records = new List<byte>();
        var offsets = new List<uint> { 0 };
        for (int gid = 0; gid < count; gid++)
        {
            records.AddRange(gid != 0 && gid % 2 == 0
                ? GlyfImages.CompositeRecord((ushort)(gid + 1)) : GlyfImages.NoContourGlyphRecord());
            offsets.Add((uint)records.Count);
        }
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(count)),
            (SubsetFontFixture.GlyfTag, records.ToArray()),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca(offsets.ToArray())),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))),
            (SubsetFontFixture.GsubTag, gsub!));
        var request = new SubsetRequest
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = [1] },
            Mode = SubsetMode.CompactRemap,
            Options = new SubsetOptions { PreserveLayout = true }
        };
        Assert.Equal(ReadStatus.Done, SubsetOperation.TryPrepare(request, out var result,
            out var report, out _, new BuildLimits { MaxTemporaryBytes = 2 * 1024 * 1024 }));
        Assert.Equal(Enumerable.Range(0, count).Select(i => (ushort)i),
            result!.Map!.Entries.Select(e => e.InputGid));
        Assert.Equal(pairs, report!.ClosureAddedGids.Count);
        Assert.Equal(pairs, report.LayoutClosureAddedGids.Count);
    }

    [Fact]
    public void CompactClosesOnlyReachableSubstitutionsToFixedPoint()
    {
        var result = Subset(Face(false), 1);
        Assert.Equal(new ushort[] { 0, 1, 3, 4 }, result.Map!.Entries.Select(e => e.InputGid));
    }

    [Fact]
    public void CompactRevisitsLayoutAfterCompositeDependencies()
    {
        var result = Subset(Face(true), 7);
        Assert.Equal(new ushort[] { 0, 1, 3, 4, 7 }, result.Map!.Entries.Select(e => e.InputGid));
    }

    [Fact]
    public void CompactClosesLongSubstitutionChainWithinLinearWorkBudget()
    {
        const int count = 4096;
        ushort[] inputs = Enumerable.Range(1, count - 2).Select(i => (ushort)i).ToArray();
        ushort[] outputs = inputs.Select(i => (ushort)(i + 1)).ToArray();
        Assert.Equal(ReadStatus.Done, SingleSubstTableCompiler.TryCreate(inputs, outputs,
            count, 1, out var single, out _));
        Assert.Equal(ReadStatus.Done, LookupTableCompiler.TryCreateSingleSubst(count,
            [single!], 0, null, null, out var lookup, out _));
        Assert.Equal(ReadStatus.Done, LayoutListsTableCompiler.TryCreate(
            LayoutListsLayout.Version10, 0, [], [], [lookup!], count,
            LayoutLookupKind.Gsub, out var table, out _));
        Assert.Equal(ReadStatus.Done, GsubTableCompiler.TryCompile(table!, null, out var gsub, out _));
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(count)),
            (SubsetFontFixture.GlyfTag, []),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca(new uint[count + 1])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))),
            (SubsetFontFixture.GsubTag, gsub!));
        var request = new SubsetRequest
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = [1] },
            Mode = SubsetMode.CompactRemap,
            Options = new SubsetOptions { PreserveLayout = true }
        };
        Assert.Equal(ReadStatus.Done, SubsetOperation.TryPrepare(request, out var result, out _, out _,
            new BuildLimits { MaxWorkUnits = 1_000_000 }));
        Assert.Equal(Enumerable.Range(0, count).Select(i => (ushort)i),
            result!.Map!.Entries.Select(e => e.InputGid));
    }

    private static SubsetResult Subset(FontFaceRef face, ushort seed)
    {
        var request = new SubsetRequest
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = [seed] },
            Mode = SubsetMode.CompactRemap,
            Options = new SubsetOptions { PreserveLayout = true }
        };
        Assert.Equal(ReadStatus.Done, SubsetOperation.TryPrepare(request, out var result,
            out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        return Assert.IsType<SubsetResult>(result);
    }

    private static FontFaceRef Face(bool composite)
    {
        Assert.Equal(ReadStatus.Done, SingleSubstTableCompiler.TryCreate([3], [4],
            GlyphCount, 2, out var first, out _));
        Assert.Equal(ReadStatus.Done, SingleSubstTableCompiler.TryCreate([1, 2], [3, 5],
            GlyphCount, 2, out var second, out _));
        Assert.Equal(ReadStatus.Done, LookupTableCompiler.TryCreateSingleSubst(GlyphCount,
            [first!], 0, null, null, out var lookup1, out _));
        Assert.Equal(ReadStatus.Done, LookupTableCompiler.TryCreateSingleSubst(GlyphCount,
            [second!], 0, null, null, out var lookup2, out _));
        Assert.Equal(ReadStatus.Done, LayoutListsTableCompiler.TryCreate(
            LayoutListsLayout.Version10, 0, [], [], [lookup1!, lookup2!], GlyphCount,
            LayoutLookupKind.Gsub, out var table, out _));
        Assert.Equal(ReadStatus.Done, GsubTableCompiler.TryCompile(table!, null,
            out var gsub, out _));
        var glyf = new List<byte>();
        var offsets = new List<uint> { 0 };
        for (int gid = 0; gid < GlyphCount; gid++)
        {
            glyf.AddRange(composite && gid == 7
                ? GlyfImages.CompositeRecord(1) : GlyfImages.NoContourGlyphRecord());
            offsets.Add((uint)glyf.Count);
        }
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(GlyphCount)),
            (SubsetFontFixture.GlyfTag, glyf.ToArray()),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca(offsets.ToArray())),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))),
            (SubsetFontFixture.GsubTag, gsub!));
    }
}
