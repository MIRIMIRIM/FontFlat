// Layout compile overflow faces: every Offset16 word a plan fixes must be preflighted
// at plan time, because the writer's narrowing conversion throws in a checked build
// (and truncates in an unchecked one) instead of answering the documented finite
// refusal. Every witness below is a legal source whose canonical re-emission exceeds
// the word, so the read side accepts it and the compile must refuse.
using FontWeave.Core;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class LayoutCompilerOverflowTests
{
    private static AnchorTable Anchor(int x)
    {
        var bytes = new byte[6];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(2), (short)x);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(4), 0);
        Assert.Equal(ReadStatus.Done, AnchorView.TryOpen(bytes, out var view, out _,
            DataLocation.Anonymous(AnchorLayout.Tag)));
        Assert.Equal(ReadStatus.Done, view.TryRead(out var anchor, out _));
        return anchor!;
    }

    // The mark-base header carries four Offset16 words: two coverage targets, the
    // MarkArray and the BaseArray. A MarkArray that starts past the word refuses at
    // plan time; only the anchor and device deltas used to be preflighted, so the
    // header's own words escaped as an exception.
    [Fact]
    public void MarkBaseHeaderTargetPastTheWordRefusesAtPlanTime()
    {
        const int count = 5000, numGlyphs = 20000;
        var anchor = Anchor(2);
        var marks = Enumerable.Range(0, count)
            .Select(i => new MarkRecord(0, anchor)).ToArray();
        var markGlyphs = Enumerable.Range(0, count).Select(i => (ushort)(2 * i)).ToArray();
        var baseGlyphs = Enumerable.Range(count, count).Select(i => (ushort)(2 * i)).ToArray();
        var baseCells = Enumerable.Repeat<AnchorTable?>(anchor, count).ToArray();
        Assert.Equal(ReadStatus.Done, MarkPosTableCompiler.TryCreate(markGlyphs, marks,
            baseGlyphs, baseCells, 1, numGlyphs, out var model, out _,
            coverageFormat: 2, location: anchor.Location));

        var status = MarkPosTableCompiler.TryCompile(model!, out var bytes, out var diagnostic);

        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Null(bytes);
    }

    // Format 3's coverage offset records are cumulative: checking only the first
    // start let a later body's target exceed the word, and the writer then threw.
    [Fact]
    public void ContextualFormat3LaterCoverageTargetPastTheWordRefusesAtPlanTime()
    {
        const int numGlyphs = 40000;
        Assert.Equal(ReadStatus.Done, CoverageTableCompiler.TryCreate(
            Enumerable.Range(0, 32766).Select(i => (ushort)i).ToArray(), numGlyphs, 1,
            out var large, out _));
        Assert.Equal(ReadStatus.Done, CoverageTableCompiler.TryCreate([39000], numGlyphs,
            1, out var small, out _));
        Assert.Equal(ReadStatus.Done, ContextualSubstTableCompiler.TryCreateFormat3(
            [large!, small!], [], numGlyphs, out var model, out _));

        var status = ContextualSubstTableCompiler.TryCompile(model!, out var bytes,
            out var diagnostic);

        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Null(bytes);
    }

    // The GDEF AttachList owns one Offset16 per covered glyph plus the Coverage target.
    // Neither used to be preflighted, so an over-wide list threw in a checked build and
    // — worse — silently truncated every record in an unchecked one.
    [Fact]
    public void AttachListTargetsPastTheWordRefuseAtPlanTime()
    {
        const int count = 1000, len = 200, numGlyphs = 40000;
        var glyphs = Enumerable.Range(0, count).Select(i => (ushort)(2 * i)).ToArray();
        Assert.Equal(ReadStatus.Done, CoverageTableCompiler.TryCreate(glyphs, numGlyphs, 1,
            out var coverage, out _));
        var points = Enumerable.Range(0, count)
            .Select(i => Enumerable.Range(0, len)
                .Select(k => (ushort)((i + k) % 60000)).ToArray()).ToArray();
        var attach = new AttachListTable(coverage!, points, numGlyphs, coverage!.Location,
            0, []);
        Assert.Equal(ReadStatus.Done, GdefTableCompiler.TryCreate(0x00010000, numGlyphs,
            null, null, null, attach, null, null, out var gdef, out _));

        var status = GdefTableCompiler.TryCompile(gdef!, out var bytes, out var diagnostic);

        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Null(bytes);
    }

    // The GDEF LigCaretList nests two record arrays (LigGlyph and CaretValue), each one
    // Offset16 per entry; neither was preflighted.
    [Fact]
    public void LigCaretListTargetsPastTheWordRefuseAtPlanTime()
    {
        const int count = 1000, carets = 200, numGlyphs = 40000;
        var glyphs = Enumerable.Range(0, count).Select(i => (ushort)(2 * i)).ToArray();
        Assert.Equal(ReadStatus.Done, CoverageTableCompiler.TryCreate(glyphs, numGlyphs, 1,
            out var coverage, out _));
        var ligs = Enumerable.Range(0, count).Select(i => new LigGlyphTable(
            Enumerable.Range(0, carets)
                .Select(k => CaretValueEntry.Create((short)((i + k) % 30000))).ToArray(),
            coverage!.Location, 0)).ToArray();
        var caretsModel = new LigCaretListTable(coverage!, ligs, numGlyphs,
            coverage!.Location, 0, []);
        Assert.Equal(ReadStatus.Done, GdefTableCompiler.TryCreate(0x00010000, numGlyphs,
            null, null, null, null, caretsModel, null, out var gdef, out _));

        var status = GdefTableCompiler.TryCompile(gdef!, out var bytes, out var diagnostic);

        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Null(bytes);
    }
}
