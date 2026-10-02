// Regression tests for temporary-space accounting in the CID compiler.
//
// Recompiling materializes working copies of the charset, FDSelect, CharStrings, FDArray, the
// per-FD Private DICTs and the Local Subrs. Before the fix the compiler charged work and output
// bytes but no temporary space at all, so a zero temporary budget was accepted.
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class CidCompileBudgetTests
{
    /// <summary>Reads a model and compiles it under the supplied limits.</summary>
    private static (ReadStatus Status, DiagnosticCode Code, byte[]? Bytes) Compile(
        CidFont font, BuildLimits? limits)
    {
        var image = CidFontBuilder.Build(font);
        _ = Cff1View.TryOpen(image.Bytes, out var view, out _);
        Assert.Equal(ReadStatus.Done, view.TryReadCid(out var model, out _));
        var status = CidCff1TableCompiler.TryCompile(model!, out var bytes, out var diagnostic,
            limits);
        return (status, diagnostic.Code, bytes);
    }

    /// <summary>A zero temporary budget cannot cover the working copies, so the compile must
    /// be refused before those arrays exist.
    /// </summary>
    [Fact]
    public void ZeroTemporaryBudgetRefusesCompile()
    {
        var (status, code, bytes) = Compile(new CidFont { FdCount = 3, NumGlyphs = 3 },
            new BuildLimits { MaxTemporaryBytes = 0 });
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, code);
        Assert.Null(bytes);
    }

    /// <summary>A zero work budget is likewise refused, so the two charges are independent.
    /// </summary>
    [Fact]
    public void ZeroWorkBudgetRefusesCompile()
    {
        var (status, code, bytes) = Compile(new CidFont { FdCount = 3, NumGlyphs = 3 },
            new BuildLimits { MaxWorkUnits = 0 });
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, code);
        Assert.Null(bytes);
    }

    /// <summary>The charge must scale with the table, not be a constant that happens to exceed
    /// a zero budget. A budget that fits a small font must not fit a much larger one.
    /// </summary>
    [Fact]
    public void ChargeScalesWithTheTableSize()
    {
        // Calibrate the smallest temporary budget a 64-glyph font still accepts.
        long low = 0, high = 64L << 20;
        while (low < high)
        {
            long mid = low + ((high - low) / 2);
            var (probe, _, _) = Compile(new CidFont { FdCount = 4, NumGlyphs = 64 },
                new BuildLimits { MaxTemporaryBytes = mid });
            if (probe == ReadStatus.Done) high = mid;
            else low = mid + 1;
        }
        long smallCharge = low;

        // A 512-glyph font has eight times the CharStrings data, so it cannot fit that budget.
        var (big, _, _) = Compile(new CidFont { FdCount = 4, NumGlyphs = 512 },
            new BuildLimits { MaxTemporaryBytes = smallCharge });
        Assert.Equal(ReadStatus.ResourceLimit, big);

        // With room to spare it compiles, so the refusal above is about size and not about the
        // larger font being unsupported.
        var (ok, _, bytes) = Compile(new CidFont { FdCount = 4, NumGlyphs = 512 },
            new BuildLimits { MaxTemporaryBytes = 64L << 20 });
        Assert.Equal(ReadStatus.Done, ok);
        Assert.NotNull(bytes);
    }

    /// <summary>A compile within budget must still produce an image the reader accepts, with
    /// the same glyph and FD counts, so the charge did not change the output.
    /// </summary>
    [Fact]
    public void CompiledImageRoundTrips()
    {
        var (status, _, bytes) = Compile(new CidFont { FdCount = 3, NumGlyphs = 5 }, null);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(bytes);

        _ = Cff1View.TryOpen(bytes!, out var reopened, out _);
        Assert.Equal(ReadStatus.Done, reopened.TryReadCid(out var again, out _));
        Assert.Equal(3, again!.FdCount);
        Assert.Equal(5, again.NumGlyphs);
        for (int fd = 0; fd < again.FdCount; fd++) Assert.True(again[fd].HasPrivate);
    }
}
