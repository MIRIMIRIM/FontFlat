// P1-5 boundary witness: a GSUB coverage entry naming GID 0xFFFF is outside the
// maxp glyph domain. The corpus scan did not reproduce the historical 17-case
// bucket in the current 1031 sample, so the refusal is kept as a declared boundary
// rather than relaxed without a proven-safe rewrite. This pins the exact refusal
// point (CoverageView's numGlyphs domain check) so a future relaxation must
// consciously change it.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class GsubSentinelBoundaryTests
{
    [Fact]
    public void SingleSubstTrailingSentinelPairIsDropped()
    {
        // format 2: coverage offset 12, substitute count 3, substitutes 3,4,0xFFFF;
        // coverage format 1: glyphs 1,2,0xFFFF. The trailing pair is the sentinel.
        byte[] bytes =
        [
            0x00, 0x02, 0x00, 0x0C, 0x00, 0x03,
            0x00, 0x03, 0x00, 0x04, 0xFF, 0xFF,
            0x00, 0x01, 0x00, 0x03,
            0x00, 0x01, 0x00, 0x02, 0xFF, 0xFF,
        ];
        Assert.Equal(ReadStatus.Done, SingleSubstView.TryOpen(bytes, out var view,
            out var diagnostic, DataLocation.Anonymous(0x47535542)));
        Assert.Equal(ReadStatus.Done, view.TryRead(12, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal([1, 2], model!.Glyphs.ToArray());
        Assert.Equal([3, 4], model.Substitutes.ToArray());
    }

    [Fact]
    public void CoverageGlyph0xFFFFRefusesAsIndexOutOfRange()
    {
        Assert.Equal(ReadStatus.Done, CoverageTableCompiler.TryCreate(
            [1], numGlyphs: 12, format: 1, out var model, out var diagnostic));
        Assert.Equal(ReadStatus.Done, CoverageTableCompiler.TryCompile(
            model!, out byte[]? bytes, out diagnostic));
        Assert.NotNull(bytes);
        // Format 1: format, count, then one u16 glyph at offset 4.
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 0xFFFF);
        Assert.Equal(ReadStatus.Done, CoverageView.TryOpen(bytes, out var view,
            out diagnostic, DataLocation.Anonymous(0x47535542)));
        var status = view.Validate(12, out diagnostic);
        Assert.Equal(ReadStatus.InvalidIndex, status);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(0xFFFFUL, diagnostic.Detail);
        Assert.Equal(12UL, diagnostic.Expected);
        Assert.Equal(DataField.GlyphId, diagnostic.Location.Field);
    }
}
