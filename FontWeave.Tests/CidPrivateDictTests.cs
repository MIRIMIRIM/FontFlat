// Regression tests for editing a Private DICT value on an FD that has no Private DICT.
//
// A Font DICT without the Private operator is legal and simply has no local hints, so an edit
// naming that FD has nowhere to go. Accepting it and dropping it would report success for a
// change that never reaches the output.
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class CidPrivateDictTests
{
    /// <summary>The operator the builder writes into every Private DICT (DefaultWidthX).</summary>
    private const int DefaultWidthX = 20;

    /// <summary>Builds a two-FD font whose first FD has no Private DICT and second one does,
    /// and returns the read model.</summary>
    private static CidCff1Table ReadMixedFont()
    {
        var image = CidFontBuilder.Build(new CidFont
        {
            FdCount = 2,
            NumGlyphs = 2,
            HasPrivate = [false, true]
        });
        _ = Cff1View.TryOpen(image.Bytes, out var view, out _);
        Assert.Equal(ReadStatus.Done, view.TryReadCid(out var model, out _));
        return model!;
    }

    /// <summary>Editing a value in an FD that declares no Private DICT must be refused as
    /// unsupported, not silently dropped.
    /// </summary>
    [Fact]
    public void EditOnFdWithoutPrivateDictIsRefused()
    {
        var model = ReadMixedFont();
        Assert.False(model[0].HasPrivate);
        Assert.True(model[1].HasPrivate);

        var plan = new CidEditPlan
        {
            PrivateValues = [new CidPrivateValueEdit(0, DefaultWidthX, 700)]
        };
        var status = CidCff1TableCompiler.TryApply(model, plan, out var updated,
            out var diagnostic);

        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Null(updated);
    }

    /// <summary>The same edit addressed to an FD that does have a Private DICT must still
    /// succeed, so the guard is specific to the missing case rather than refusing all Private
    /// edits.
    /// </summary>
    [Fact]
    public void EditOnFdWithPrivateDictIsApplied()
    {
        var model = ReadMixedFont();
        var plan = new CidEditPlan
        {
            PrivateValues = [new CidPrivateValueEdit(1, DefaultWidthX, 700)]
        };
        var status = CidCff1TableCompiler.TryApply(model, plan, out var updated,
            out var diagnostic);

        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(updated);

        // The edit reaches the reread model.
        int observed = PrivateValue(updated![1], DefaultWidthX);
        Assert.Equal(700, observed);

        // And it survives a compile, so it was not merely recorded in the in-memory model.
        Assert.Equal(ReadStatus.Done,
            CidCff1TableCompiler.TryCompile(updated, out var bytes, out _));
        _ = Cff1View.TryOpen(bytes!, out var reopened, out _);
        Assert.Equal(ReadStatus.Done, reopened.TryReadCid(out var again, out _));
        Assert.Equal(700, PrivateValue(again![1], DefaultWidthX));
    }

    /// <summary>A round-trip of a font with a private-less FD must keep that FD private-less,
    /// so compiling does not invent a Private DICT for it.
    /// </summary>
    [Fact]
    public void RoundTripPreservesThePrivateLessFd()
    {
        var model = ReadMixedFont();
        Assert.Equal(ReadStatus.Done,
            CidCff1TableCompiler.TryCompile(model, out var bytes, out _));

        _ = Cff1View.TryOpen(bytes!, out var reopened, out _);
        Assert.Equal(ReadStatus.Done, reopened.TryReadCid(out var again, out _));
        Assert.Equal(2, again!.FdCount);
        Assert.False(again[0].HasPrivate);
        Assert.True(again[1].HasPrivate);
    }

    /// <summary>An FD index outside the FDArray is an index failure, which is a different
    /// diagnostic from the unsupported-edit case above.</summary>
    [Fact]
    public void EditOnFdIndexPastTheArrayIsAnIndexFailure()
    {
        var model = ReadMixedFont();
        var plan = new CidEditPlan
        {
            PrivateValues = [new CidPrivateValueEdit(9, DefaultWidthX, 700)]
        };
        var status = CidCff1TableCompiler.TryApply(model, plan, out var updated,
            out var diagnostic);

        Assert.Equal(ReadStatus.InvalidIndex, status);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Null(updated);
    }

    private static int PrivateValue(CidFontDict fontDict, int op)
    {
        foreach (var entry in fontDict.PrivateEntries)
            if (entry.Operator == op && entry.Operands.Count > 0)
                return entry.Operands[0].Value;
        return int.MinValue;
    }
}
