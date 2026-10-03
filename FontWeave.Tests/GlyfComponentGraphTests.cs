// Regression tests for glyf decoding budgets and component-graph verification.
//
// These cover failures where the reader either under-charged temporary space or made a graph
// verdict depend on something other than the graph itself.
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class GlyfComponentGraphTests
{
    private const int DepthBudget = 64;

    /// <summary>The depth limit must be a property of the graph, not of the order the glyphs
    /// happen to be visited in. An 80-level chain is over budget in either numbering, and
    /// before the fix the ascending numbering was accepted because the DFS path counter was
    /// reset by the memoized early return.
    /// </summary>
    [Theory]
    [InlineData(80, true)]
    [InlineData(80, false)]
    [InlineData(30000, true)]
    [InlineData(30000, false)]
    public void ChainDeeperThanTheBudgetIsRefusedInEitherNumbering(int length, bool ascending)
    {
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.DepthChain(length, ascending);
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);

        var status = view.TryReadComposites(loca, out var model, out var diagnostic);
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Null(model);
    }

    /// <summary>A long cycle must produce a structural diagnostic, never exhaust the
    /// process stack. Unlike an acyclic chain, it has no resolved depth.</summary>
    [Fact]
    public void LongCycleIsDiagnosedWithoutRecursion()
    {
        const int count = 30000;
        var records = new List<byte[]>(count);
        for (int gid = 0; gid < count; gid++)
            records.Add(GlyfImages.CompositeRecord((ushort)((gid + 1) % count)));
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.Assemble(records);
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);

        var status = view.TryReadComposites(loca, out var model, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(GlyfImages.GlyfLocation.SourceId, diagnostic.Location.SourceId);
        Assert.Null(model);
    }

    /// <summary>A chain that fits the budget must be accepted, so the limit is not simply
    /// rejecting every deep chain in either numbering.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChainWithinTheBudgetIsAcceptedInEitherNumbering(bool ascending)
    {
        const int length = DepthBudget;
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.DepthChain(length, ascending);
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);

        var status = view.TryReadComposites(loca, out var model, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(model);
        Assert.Equal(ReadStatus.Done, view.ValidateComponentGraph(model, out var graph, out _));
        Assert.Equal(length, graph!.MaxDepth);
    }

    /// <summary>A composite referencing itself is a cycle and must be refused with the glyph
    /// named, in both the reader and the standalone graph check.
    /// </summary>
    [Fact]
    public void SelfReferenceIsRefused()
    {
        var (locaBytes, glyfBytes, numGlyphs) =
            GlyfImages.Assemble([GlyfImages.NoContourGlyphRecord(), GlyfImages.CompositeRecord(1)]);
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);

        Assert.Equal(ReadStatus.InvalidFormat, view.TryReadComposites(loca, out var model, out _));
        Assert.Null(model);
    }

    /// <summary>Two composites referencing each other form a cycle that reaches no leaf. Three
    /// glyphs are needed so the references can stay inside the glyph domain: with two, the
    /// cycle edge would be an out-of-range index instead, which is a different failure.
    /// </summary>
    [Fact]
    public void MutualReferenceIsRefused()
    {
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.Assemble(
        [
            GlyfImages.CompositeRecord(1),
            GlyfImages.CompositeRecord(2),
            GlyfImages.CompositeRecord(0)
        ]);
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);

        var status = view.TryReadComposites(loca, out var model, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Null(model);
    }

    /// <summary>A component index past the glyph count cannot be resolved and must be an
    /// index failure rather than a graph verdict.</summary>
    [Fact]
    public void ComponentPastTheGlyphCountIsAnIndexFailure()
    {
        var (locaBytes, glyfBytes, numGlyphs) =
            GlyfImages.Assemble([GlyfImages.NoContourGlyphRecord(), GlyfImages.CompositeRecord(9)]);
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);

        var status = view.TryReadComposites(loca, out var model, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidIndex, status);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Null(model);
    }
}
