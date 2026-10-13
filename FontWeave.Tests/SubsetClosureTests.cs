// Composite dependency closure tests (S1a card §3, commit ② acceptance).
//
// The closure walks the already-verified component graph of the existing read chain, so
// the negative graph cases below surface with the reader's own diagnostics. Budget
// exhaustion pins the finite failure shape; the frozen case's exact limitRecipe ("cumula-
// tive work before the closure stage plus one") is evaluated at gate-build time by the
// validation harness, which observes the real accounting.
using FontWeave.Core;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetClosureTests
{
    /// <summary>Glyph 0/1 are simple leaves; glyph 2 composites over 1; glyph 3 over 2.</summary>
    private static FontFaceRef ChainFace()
    {
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.Assemble(
        [
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.CompositeRecord(1),
            GlyfImages.CompositeRecord(2)
        ]);
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp((ushort)numGlyphs)),
            (SubsetFontFixture.GlyfTag, glyfBytes),
            (SubsetFontFixture.LocaTag, locaBytes),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapShell(4)));
    }

    private static SubsetOptions NoLayoutOptions() => new() { NoLayout = true };

    private static SubsetRequest Request(FontFaceRef face, ushort[] gids) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = SubsetMode.RetainGids,
        Options = NoLayoutOptions()
    };

    [Fact]
    public void ClosurePullsDirectComponents()
    {
        var face = ChainFace();
        var status = SubsetOperation.TryPrepare(Request(face, [2]), out var result,
            out var report, out var diagnostic);
        // The rebuild stage is wired by a later commit; the closure report is retained
        // by the finite refusal that ends the pipeline today.
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Null(result);
        Assert.NotNull(report);
        Assert.Equal([1], report!.ClosureAddedGids);
        Assert.Equal(1, report.UnselectedSlotCount);
    }

    [Fact]
    public void ClosureIsTransitive()
    {
        var face = ChainFace();
        var status = SubsetOperation.TryPrepare(Request(face, [3]), out _,
            out var report, out _);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal([1, 2], report!.ClosureAddedGids);
        Assert.Equal(0, report.UnselectedSlotCount);
    }

    [Fact]
    public void ClosureAddsNothingWithoutComposites()
    {
        var face = ChainFace();
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out _,
            out var report, out _);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Empty(report!.ClosureAddedGids);
        Assert.Equal(2, report.UnselectedSlotCount);
    }

    [Fact]
    public void CycleRefusesWithReadChainDiagnostic()
    {
        // Glyph 0 references itself; the graph builder hard-refuses cycles before any
        // model is published (GlyfViewComposite.cs:370-372).
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.Assemble(
        [
            GlyfImages.CompositeRecord(0),
            GlyfImages.NoContourGlyphRecord()
        ]);
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp((ushort)numGlyphs)),
            (SubsetFontFixture.GlyfTag, glyfBytes),
            (SubsetFontFixture.LocaTag, locaBytes),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapShell(4)));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.GlyphId, diagnostic.Location.Field);
        Assert.Null(result);
    }

    [Fact]
    public void OverDeepChainRefusesWithReadChainDiagnostic()
    {
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.DepthChain(80, true);
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp((ushort)numGlyphs)),
            (SubsetFontFixture.GlyfTag, glyfBytes),
            (SubsetFontFixture.LocaTag, locaBytes),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapShell(4)));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(64UL, diagnostic.Expected);
        Assert.Null(result);
    }

    // S0-BF-001 shape: reducing only MaxWorkUnits below what admits the pipeline turns
    // the outcome into ResourceLimit/BudgetExceeded on the WorkUnits field with no
    // partial artifact and a retained report.
    [Fact]
    public void WorkBudgetExhaustionRefuses()
    {
        var face = ChainFace();
        var request = Request(face, [3]);

        // Find the smallest work budget (by bisection) that still reaches the pipeline's
        // end: the rebuild stage's finite refusal.
        long low = 1, high = 1L << 40;
        Assert.Equal(ReadStatus.UnsupportedFormat,
            PrepareStatus(request, new BuildLimits { MaxWorkUnits = high }));
        while (high - low > 1)
        {
            long mid = low + (high - low) / 2;
            if (PrepareStatus(request, new BuildLimits { MaxWorkUnits = mid }) ==
                ReadStatus.UnsupportedFormat) high = mid;
            else low = mid;
        }

        // One unit below admission exhausts some work charge; every other limit stays at
        // its default, so the failure must be the work-units field.
        var limits = new BuildLimits { MaxWorkUnits = high - 1 };
        var status = SubsetOperation.TryPrepare(request, out var result, out var report,
            out var diagnostic, limits);
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(DataField.WorkUnits, diagnostic.Location.Field);
        Assert.Null(result);
        Assert.NotNull(report);
        Assert.NotEmpty(report!.Diagnostics);
    }

    private static ReadStatus PrepareStatus(SubsetRequest request, BuildLimits limits) =>
        SubsetOperation.TryPrepare(request, out _, out _, out _, limits);
}
