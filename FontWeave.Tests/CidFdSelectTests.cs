// Regression test for the FDSelect range-domain check.
//
// Format 3 FDSelect is a table of runs, each naming the first glyph of the run and the FD for
// it. The sentinel closes the last run. A run whose start lies at or beyond the sentinel claims
// glyphs that do not exist, which silently moves real glyphs onto a later FD.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class CidFdSelectTests
{
    /// <summary>Position of the last run's FirstGlyph field in a format 3 FDSelect.</summary>
    private static int LastRunFirstGlyphAt(byte[] cff, int fdSelectAt)
    {
        ushort rangeCount = BinaryPrimitives.ReadUInt16BigEndian(cff.AsSpan(fdSelectAt + 1));
        Assert.True(rangeCount >= 1);
        return fdSelectAt + 3 + (rangeCount - 1) * 3;
    }

    /// <summary>A final run that starts at the sentinel describes no glyphs at all, and one
    /// that starts past it describes glyphs that do not exist. Both must be refused rather
    /// than accepted, which would leave the run table claiming a glyph space the font does not
    /// have.
    /// </summary>
    [Theory]
    [InlineData(0)]  // starts exactly at the sentinel
    [InlineData(1)]  // starts one past the sentinel
    public void FinalRunStartingAtOrPastTheSentinelIsRefused(int overshoot)
    {
        var image = CidFontBuilder.Build(fdCount: 3, numGlyphs: 3);
        byte[] cff = (byte[])image.Bytes.Clone();
        int at = LastRunFirstGlyphAt(cff, image.FdSelectOffset);
        // The sentinel equals the glyph count for a well-formed run table.
        int sentinel = image.NumGlyphs;
        BinaryPrimitives.WriteUInt16BigEndian(cff.AsSpan(at), (ushort)(sentinel + overshoot));

        _ = Cff1View.TryOpen(cff, out var view, out _);
        var status = view.TryReadCid(out var model, out var diagnostic);

        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Null(model);
    }

    /// <summary>Every run start inside the glyph domain must still be accepted, so the check
    /// is a domain bound and not a rejection of the last run in general.
    /// </summary>
    [Fact]
    public void RunsInsideTheGlyphDomainAreAccepted()
    {
        var image = CidFontBuilder.Build(fdCount: 3, numGlyphs: 3);
        _ = Cff1View.TryOpen(image.Bytes, out var view, out _);
        Assert.Equal(ReadStatus.Done, view.TryReadCid(out var model, out _));
        // Each of the three glyphs is mapped to its own FD by the builder.
        for (int gid = 0; gid < model!.NumGlyphs; gid++)
        {
            Assert.Equal(ReadStatus.Done, model.TryGetFdIndex(gid, out int fd, out _));
            Assert.Equal(gid, fd);
        }
    }

    /// <summary>The sentinel must equal the glyph count; a mismatch is refused before the
    /// per-run domain check, so the two diagnostics stay distinguishable.
    /// </summary>
    [Fact]
    public void SentinelDisagreeingWithTheGlyphCountIsRefused()
    {
        var image = CidFontBuilder.Build(fdCount: 3, numGlyphs: 3);
        byte[] cff = (byte[])image.Bytes.Clone();
        int at = LastRunFirstGlyphAt(cff, image.FdSelectOffset) + 3; // the sentinel field
        BinaryPrimitives.WriteUInt16BigEndian(cff.AsSpan(at), (ushort)(image.NumGlyphs + 1));

        _ = Cff1View.TryOpen(cff, out var view, out _);
        var status = view.TryReadCid(out var model, out var diagnostic);

        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Null(model);
    }
}
