using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class GlyfCompileBudgetTests
{
    private sealed class ObservedPointEdits : IReadOnlyList<GlyphPointEdit>
    {
        public int Visited { get; private set; }
        public int Count => 1;
        public GlyphPointEdit this[int index] => index == 0
            ? new GlyphPointEdit(0, 10, -10)
            : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<GlyphPointEdit> GetEnumerator()
        {
            Visited++;
            yield return this[0];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private static GlyfTable ReadExpanded(int glyphs, int points)
    {
        var (locaBytes, glyfBytes, count) = GlyfImages.RepeatedExpandedGlyph(glyphs, points);
        var loca = GlyfImages.LocaOrThrow(locaBytes, count);
        Assert.Equal(ReadStatus.Done,
            GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation));
        Assert.Equal(ReadStatus.Done, view.TryRead(loca, out var model, out _));
        return model!;
    }

    private static GlyfEditPlan EditAll(int glyphs) => new()
    {
        Glyphs = Enumerable.Range(0, glyphs)
            .Select(gid => new GlyphEdit { GlyphId = gid }).ToArray()
    };

    [Fact]
    public void ZeroWorkBudgetRefusesBeforeApplyingPointEdits()
    {
        var model = ReadExpanded(1, 2048);
        var points = new ObservedPointEdits();
        var plan = new GlyfEditPlan
        {
            Glyphs = [new GlyphEdit { GlyphId = 0, Points = points }]
        };

        var status = GlyfTableCompiler.TryApply(model, plan, out var bytes,
            out var offsets, out var diagnostic, new BuildLimits { MaxWorkUnits = 0 });

        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DataField.WorkUnits, diagnostic.Location.Field);
        Assert.Equal(0, points.Visited);
        Assert.Null(bytes);
        Assert.Null(offsets);
    }

    [Fact]
    public void ExpandedPointWorkingArraysCannotFitAWireSizedBudget()
    {
        // The source repeats one flag for 2048 coincident points. Re-encoding writes only
        // one byte per point, but editing still needs expanded flags, points and deltas;
        // an admission limit sized for the wire bytes cannot cover those working arrays.
        var model = ReadExpanded(1, 2048);
        byte[] source = model.OriginalBytes.ToArray();
        var limits = new BuildLimits { MaxTemporaryBytes = 16 * 1024 };

        var status = GlyfTableCompiler.TryApply(model, EditAll(1), out var bytes,
            out var offsets, out var diagnostic, limits);

        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(DataField.TemporaryBytes, diagnostic.Location.Field);
        Assert.Null(bytes);
        Assert.Null(offsets);
        Assert.Equal(source, model.OriginalBytes.ToArray());
    }

    [Fact]
    public void RefusedExpandedEditLeavesCallerDestinationUntouched()
    {
        var model = ReadExpanded(3, 2048);
        byte[] source = model.OriginalBytes.ToArray();
        byte[] destination = Enumerable.Repeat((byte)0xA5, 8192).ToArray();
        var limits = new BuildLimits { MaxTemporaryBytes = 32 * 1024 };

        var status = GlyfTableCompiler.TryApplyTo(model, EditAll(3), destination,
            out int written, out var diagnostic, limits);

        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(0, written);
        Assert.All(destination, b => Assert.Equal(0xA5, b));
        Assert.Equal(source, model.OriginalBytes.ToArray());
    }

    [Fact]
    public void TemporaryBoundaryIsCumulativeAndAcceptedImageMatchesIndependentWire()
    {
        const int glyphs = 3, points = 257;
        var model = ReadExpanded(glyphs, points);
        var edit = EditAll(glyphs);
        long low = 0, high = 1 << 20;
        while (low < high)
        {
            long mid = low + (high - low) / 2;
            var status = GlyfTableCompiler.TryApply(model, edit, out _, out _, out _,
                new BuildLimits { MaxTemporaryBytes = mid });
            if (status == ReadStatus.Done) high = mid;
            else
            {
                Assert.Equal(ReadStatus.ResourceLimit, status);
                low = mid + 1;
            }
        }

        // Flags and the two int delta arrays alone require nine bytes per point. This
        // lower bound follows their logical payload, not an implementation charge constant.
        Assert.True(low >= 9L * glyphs * points);
        Assert.Equal(ReadStatus.ResourceLimit,
            GlyfTableCompiler.TryApply(model, edit, out var refused, out var refusedOffsets,
                out var failure, new BuildLimits { MaxTemporaryBytes = low - 1 }));
        Assert.Equal(DataField.TemporaryBytes, failure.Location.Field);
        Assert.Null(refused);
        Assert.Null(refusedOffsets);
        Assert.Equal(ReadStatus.Done,
            GlyfTableCompiler.TryApply(model, edit, out var bytes, out var offsets, out _,
                new BuildLimits { MaxTemporaryBytes = low }));

        // Independent canonical records: one contour, endpoint 256, no instructions,
        // then 257 ON_CURVE|X_SAME|Y_SAME flags and no coordinate bytes or padding.
        byte[] record = new byte[14 + points];
        record[1] = 1;
        record[10] = 1;
        record.AsSpan(14).Fill(0x31);
        Assert.Equal(Enumerable.Range(0, glyphs).SelectMany(_ => record).ToArray(), bytes);
        Assert.Equal(Enumerable.Range(0, glyphs + 1)
            .Select(gid => (uint)(gid * record.Length)).ToArray(), offsets);
    }
}
