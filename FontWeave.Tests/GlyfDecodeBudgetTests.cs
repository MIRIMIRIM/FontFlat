// Regression tests for glyf decoding budgets and component-graph verification.
//
// These cover failures where the reader either under-charged temporary space or made a graph
// verdict depend on something other than the graph itself.
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class GlyfDecodeBudgetTests
{
    /// <summary>A compressed flag run lets a few hundred bytes describe thousands of points.
    /// The temporary charge must follow the point count, not the wire length: charging by
    /// input length made MaxTemporaryBytes unable to bound what decoding actually allocates.
    /// </summary>
    [Fact]
    public void ExpandedPointsAreChargedByPointCountNotInputLength()
    {
        const int glyphs = 200;
        const int pointsPerGlyph = 2048;
        var (locaBytes, glyfBytes, numGlyphs) =
            GlyfImages.RepeatedExpandedGlyph(glyphs, pointsPerGlyph);
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);

        // Calibrate the charge the decoder actually applies: the smallest temporary limit it
        // still accepts. Bisection is used because the assertion is about the real charge, not
        // about a constant copied out of the implementation.
        long low = 0, high = 64L << 20;
        while (low < high)
        {
            long mid = low + ((high - low) / 2);
            var trial = new BuildLimits { MaxTemporaryBytes = mid };
            if (view.TryRead(loca, out _, out _, trial) == ReadStatus.Done) high = mid;
            else low = mid + 1;
        }
        long charged = low;

        // A limit equal to the input length is far below what the decoded arrays need, so it
        // must be refused. Before the fix this limit was accepted.
        var byInputLength = new BuildLimits { MaxTemporaryBytes = 2L * glyfBytes.Length };
        Assert.Equal(ReadStatus.ResourceLimit, view.TryRead(loca, out _, out _, byInputLength));

        // And the calibrated charge must be proportional to the point count, so a limit just
        // under it is refused while one at or above it succeeds.
        long points = (long)pointsPerGlyph * glyphs;
        Assert.True(charged >= points,
            $"the decoder charged {charged} bytes for {points} points");
        var justUnder = new BuildLimits { MaxTemporaryBytes = charged - 1 };
        Assert.Equal(ReadStatus.ResourceLimit, view.TryRead(loca, out _, out _, justUnder));
        var exact = new BuildLimits { MaxTemporaryBytes = charged };
        Assert.Equal(ReadStatus.Done, view.TryRead(loca, out var model, out _, exact));

        // The decoded model really does hold that many points, so the charge is not simply an
        // arbitrary constant that happens to exceed the point count.
        Assert.NotNull(model);
        Assert.Equal(pointsPerGlyph, model.MaxSimplePointCount);
        Assert.Equal(numGlyphs, model.NumGlyphs);
    }

    /// <summary>A zero temporary budget must be refused whenever decoding would need memory.
    /// </summary>
    [Fact]
    public void ZeroTemporaryBudgetIsRefused()
    {
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.SingleSimple(64);
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);
        var zero = new BuildLimits { MaxTemporaryBytes = 0 };
        Assert.Equal(ReadStatus.ResourceLimit, view.TryRead(loca, out _, out _, zero));
    }

    /// <summary>A glyph that materializes no points must not be charged the per-point
    /// temporary cost. The observable difference is that an image of point-less glyphs fits a
    /// budget far below what the same number of expanded glyphs needs.
    /// </summary>
    [Fact]
    public void PointlessGlyphsAreNotChargedPerPoint()
    {
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.Assemble(
            [GlyfImages.NoContourGlyphRecord(), GlyfImages.NoContourGlyphRecord()]);
        var smallLoca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var smallView, out _, GlyfImages.GlyfLocation);

        // Enough for the glyph slot array but far below the per-point cost of two 2048-point
        // glyphs (2 x 2048 x 10 bytes).
        var small = new BuildLimits { MaxTemporaryBytes = 1 << 14 };
        Assert.Equal(ReadStatus.Done, smallView.TryRead(smallLoca, out var model, out _, small));
        Assert.Equal(0, model!.MaxSimplePointCount);
        Assert.Equal(0, model.EmptyGlyphCount);

        // The same budget cannot cover two expanded glyphs, so the charge really does track
        // the points rather than the glyph count.
        var (locaBytes2, glyfBytes2, numGlyphs2) = GlyfImages.RepeatedExpandedGlyph(2, 2048);
        var bigLoca = GlyfImages.LocaOrThrow(locaBytes2, numGlyphs2);
        _ = GlyfView.TryOpen(glyfBytes2, out var bigView, out _, GlyfImages.GlyfLocation);
        Assert.Equal(ReadStatus.ResourceLimit, bigView.TryRead(bigLoca, out _, out _, small));
    }

    /// <summary>A zero-length loca slot is a genuinely empty glyph: no outline bytes at all.
    /// It must stay readable under a budget that only has to cover the glyph slot array.
    /// </summary>
    [Fact]
    public void EmptyLocaSlotsNeedNoTemporarySpace()
    {
        // Two zero-length slots: loca offsets 0, 0, 0 over an image that holds no records.
        byte[] locaBytes = GlyfImages.LongLoca([0, 0, 0]);
        byte[] glyfBytes = [];
        var loca = GlyfImages.LocaOrThrow(locaBytes, 2);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);
        var slotOnly = new BuildLimits { MaxTemporaryBytes = 8L * 2 };
        Assert.Equal(ReadStatus.Done, view.TryRead(loca, out var model, out _, slotOnly));
        Assert.Equal(2, model!.EmptyGlyphCount);
        Assert.Equal(0, model.MaxSimplePointCount);
    }
}

