// Regression test for the CID charset offset guard.
//
// A CID-keyed table validates its charset through a path that does not require the block to
// tile, and that path previously used the offset without checking its sign.
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class CidCharsetOffsetTests
{
    /// <summary>A negative charset offset names no byte in the table. The reader must report
    /// that as a diagnostic; before the fix it indexed the image with the negative value and
    /// faulted instead, so a malformed font could not be rejected gracefully.
    /// </summary>
    [Fact]
    public void NegativeCharsetOffsetIsDiagnosedNotFaulted()
    {
        var image = CidFontBuilder.Build(fdCount: 3, numGlyphs: 3);
        byte[] cff = (byte[])image.Bytes.Clone();
        var (operandAt, marker) = CffWire.FindTopEntry(cff, 0x0F);
        CffWire.PatchOperand(cff, operandAt, marker, -1);

        _ = Cff1View.TryOpen(cff, out var view, out _);
        var status = view.TryReadCid(out var model, out var diagnostic);

        Assert.Equal(ReadStatus.InvalidOffset, status);
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
        Assert.Null(model);
    }

    /// <summary>The same image without the patch must read cleanly, so the guard is the only
    /// difference between the two outcomes.
    /// </summary>
    [Fact]
    public void UnpatchedImageReadsCleanly()
    {
        var image = CidFontBuilder.Build(fdCount: 3, numGlyphs: 3);
        _ = Cff1View.TryOpen(image.Bytes, out var view, out _);
        Assert.Equal(ReadStatus.Done, view.TryReadCid(out var model, out _));
        Assert.Equal(3, model!.FdCount);
        Assert.Equal(3, model.NumGlyphs);
    }

    /// <summary>An offset past the end of the table is refused too, so the guard is a range
    /// check rather than a negativity check alone.
    /// </summary>
    [Fact]
    public void CharsetOffsetPastTheTableEndIsDiagnosed()
    {
        var image = CidFontBuilder.Build(fdCount: 3, numGlyphs: 3);
        byte[] cff = (byte[])image.Bytes.Clone();
        var (operandAt, marker) = CffWire.FindTopEntry(cff, 0x0F);
        CffWire.PatchOperand(cff, operandAt, marker, image.Length + 64);

        _ = Cff1View.TryOpen(cff, out var view, out _);
        var status = view.TryReadCid(out var model, out var diagnostic);

        Assert.NotEqual(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
        Assert.Null(model);
    }
}
