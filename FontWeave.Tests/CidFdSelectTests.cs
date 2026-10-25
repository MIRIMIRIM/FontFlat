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

    // Format 3's run fd is a one-byte field. The FDArray may legally carry FDs no glyph
    // references, so an edit can name an FD inside the array count but past the byte
    // domain; the plan must refuse it rather than let the writer's byte narrowing
    // silently move every glyph of the run onto a different FD.
    [Fact]
    public void EditedFdIndexPastTheFormat3ByteDomainIsRefused()
    {
        // 300 FDs over 300 glyphs: the builder's own FDSelect writes fds 256.. as
        // truncated bytes, which the reader accepts (every referenced fd stays inside
        // the array), so the model's FD count is the full 300.
        var image = CidFontBuilder.Build(fdCount: 300, numGlyphs: 300);
        _ = Cff1View.TryOpen(image.Bytes, out var view, out _);
        Assert.Equal(ReadStatus.Done, view.TryReadCid(out var model, out _));
        Assert.Equal(300, model!.FdCount);

        var plan = new CidEditPlan { FdRanges = [new CidFdRangeEdit(0, null, 260)] };

        // Prepare flattens every FDSelect refusal to InvalidFormat; the byte-domain
        // bound is told by the diagnostic (expected 256, not the array count).
        Assert.Equal(ReadStatus.InvalidFormat, CidCff1TableCompiler.TryCompile(model!, plan,
            out var bytes, out var diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(260ul, diagnostic.Detail);
        Assert.Null(bytes);
    }

    // A run's FirstGlyph is a uint16 that must stay inside the glyph domain: past the
    // word the writer's narrowing throws in a checked build, and at or past the sentinel
    // the run claims glyphs the font does not have. The reader's own rule, mirrored at
    // the plan instead of only on the way back in.
    [Theory]
    [InlineData(70_000)]
    [InlineData(300)]
    [InlineData(301)]
    public void EditedFirstGlyphOutsideTheGlyphDomainIsRefused(int first)
    {
        var image = CidFontBuilder.Build(fdCount: 3, numGlyphs: 300);
        _ = Cff1View.TryOpen(image.Bytes, out var view, out _);
        Assert.Equal(ReadStatus.Done, view.TryReadCid(out var model, out _));
        var plan = new CidEditPlan { FdRanges = [new CidFdRangeEdit(2, first, null)] };

        Assert.Equal(ReadStatus.InvalidFormat, CidCff1TableCompiler.TryCompile(model!, plan,
            out var bytes, out _));
        Assert.Null(bytes);
    }
}
