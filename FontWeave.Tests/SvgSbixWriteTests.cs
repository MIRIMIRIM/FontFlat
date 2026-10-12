using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-04 commit 3's compilers and edit API: the canonical plans
/// (header → records/strikeOffsets → documents/strikes, single forward
/// emissions), the identity/wire-equivalent double track (same-shape sources
/// recompile byte-identical; other placements re-root onto the canonical
/// layout with content preserved), the four TryWith* value edits with their
/// refusal wings, the short-destination atomicity and the deterministic
/// repeat. Compiled wires are asserted against raw byte layouts, never against
/// a View self-comparison alone; the re-read projections cross-check the
/// content.</summary>
public sealed class SvgSbixWriteTests
{
    private static readonly DataLocation Source = new(9, 0, 0x53564720, -1,
        DataField.None, OffsetBasis.Table, 0);
    private static readonly DataLocation SbixSource = new(9, 0, 0x73626978, -1,
        DataField.None, OffsetBasis.Table, 0);

    private static void WriteU16(byte[] wire, int at, ushort value)
    {
        wire[at] = (byte)(value >> 8);
        wire[at + 1] = unchecked((byte)value);
    }

    private static void WriteU32(byte[] wire, int at, uint value)
    {
        WriteU16(wire, at, (ushort)(value >> 16));
        WriteU16(wire, at + 2, unchecked((ushort)value));
    }

    private static uint ReadU32(byte[] wire, int at) =>
        (uint)(wire[at] << 24 | wire[at + 1] << 16 | wire[at + 2] << 8 | wire[at + 3]);

    // ---- SVG compiler ------------------------------------------------------

    [Fact]
    public void SvgReadModelRecompilesByteIdentical()
    {
        var wire = SvgSbixReadTests.SvgWire([(2, 2, [0x61, 0x62]), (5, 9, [0x63])]);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(ReadStatus.Done, SvgTableCompiler.TryCompile(model!, out var compiled, out diagnostic));
        Assert.NotNull(compiled);
        Assert.Equal(wire, compiled);
    }

    [Fact]
    public void SvgNonCanonicalPlacementRecompilesWireEquivalent()
    {
        // docList after 4 padding bytes: the canonical plan re-roots the list
        // at 10 with the same records and documents (content preserved).
        var wire = SvgSbixReadTests.SvgWire([(2, 2, [0x61, 0x62]), (5, 9, [0x63])],
            immediateList: false, padding: 4);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(ReadStatus.Done, SvgTableCompiler.TryCompile(model!, out var compiled, out diagnostic));
        Assert.NotNull(compiled);
        Assert.NotEqual(wire, compiled);
        // The canonical coordinates: list at 10, records at 12, docs after.
        Assert.Equal(10u, ReadU32(compiled!, 2));
        Assert.Equal(0x61, compiled![36]); // first document byte at 10+2+24
        // Re-read equals the original model's content.
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(compiled, out var reRead, out _, Source));
        Assert.Equal(ReadStatus.Done, reRead.TryReadTable(out var reModel, out diagnostic));
        Assert.NotNull(reModel);
        Assert.Equal(model!.RecordCount, reModel!.RecordCount);
        Assert.Equal(model.DocumentCount, reModel.DocumentCount);
        for (int i = 0; i < model.RecordCount; i++)
        {
            Assert.Equal(model.Records[i], reModel.Records[i]);
            Assert.True(model.Documents[model.Records[i].PayloadIndex].AsSpan()
                .SequenceEqual(reModel.Documents[reModel.Records[i].PayloadIndex]));
        }
        // The write-read-write fixed point.
        Assert.Equal(ReadStatus.Done, SvgTableCompiler.TryCompile(reModel, out var again, out diagnostic));
        Assert.Equal(compiled, again);
    }

    [Fact]
    public void SvgFactoryProducesTheCanonicalLayout()
    {
        FontDiagnostic diagnostic;

        var documents = new byte[][] { [0x61, 0x62], [0x63] };
        var records = new SvgDocumentRecord[] { new(2, 2, 0), new(5, 9, 1) };
        Assert.Equal(ReadStatus.Done, SvgTableCompiler.TryCreate(0, 0, records, documents,
            out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(ReadStatus.Done, SvgTableCompiler.TryCompile(model!, out var compiled, out diagnostic));
        Assert.NotNull(compiled);
        // header(10) + count(2) + records(24) + docs(3): documents at 36.
        Assert.Equal(39, compiled!.Length);
        Assert.Equal(10u, ReadU32(compiled, 2));
        Assert.Equal((ushort)2, (ushort)(compiled[10] << 8 | compiled[11]));
        Assert.Equal((ushort)2, (ushort)(compiled[12] << 8 | compiled[13]));
        Assert.Equal((ushort)2, (ushort)(compiled[14] << 8 | compiled[15]));
        Assert.Equal(26u, ReadU32(compiled, 16));  // doc0 at 36, list-relative 26
        Assert.Equal(2u, ReadU32(compiled, 20));
        Assert.Equal((ushort)5, (ushort)(compiled[24] << 8 | compiled[25]));
        Assert.Equal((ushort)9, (ushort)(compiled[26] << 8 | compiled[27]));
        Assert.Equal(28u, ReadU32(compiled, 28));  // doc1 at 38, list-relative 28
        Assert.Equal(1u, ReadU32(compiled, 32));
        Assert.Equal(0x63, compiled[38]);
    }

    [Fact]
    public void SvgDocumentPayloadEditFollowsTheSharedGroup()
    {
        byte[] shared = [0x61, 0x62];
        var wire = SvgSbixReadTests.SvgWire([(2, 2, shared), (13, 14, shared)]);
        // Point record 1 at document 0's key so the two records share.
        WriteU32(wire, 28, ReadU32(wire, 16));
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(1, model!.DocumentCount);
        Assert.Equal(ReadStatus.Done, model.TryWithDocumentPayload(0,
            [0x01, 0x02, 0x03], out var updated, out diagnostic));
        Assert.NotNull(updated);
        Assert.Equal(ReadStatus.Done, SvgTableCompiler.TryCompile(updated!, out var compiled, out diagnostic));
        Assert.NotNull(compiled);
        // The compiled wire carries the new document once; both records name it.
        Assert.Equal(39, compiled!.Length);
        Assert.Equal(0x01, compiled[36]); // document at 10+2+24
        Assert.Equal(ReadU32(compiled, 16), ReadU32(compiled, 28));
        // The source model never mutated.
        Assert.Equal(0x61, model.Documents[0][0]);
        // An empty replacement refuses (svgDocLength's non-zero face).
        Assert.Equal(ReadStatus.InvalidFormat, model.TryWithDocumentPayload(0,
            ReadOnlySpan<byte>.Empty, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
    }

    [Fact]
    public void SvgRangeEditReAnswersTheOrderingClauses()
    {
        var wire = SvgSbixReadTests.SvgWire([(2, 5, [0x61]), (8, 12, [0x62])]);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(out var model, out diagnostic));
        Assert.NotNull(model);
        // A legal rewrite keeps the strict ascent.
        Assert.Equal(ReadStatus.Done, model!.TryWithRange(0, 0, 1, out var updated, out diagnostic));
        Assert.NotNull(updated);
        Assert.Equal((ushort)0, updated!.Records[0].StartGlyphID);
        Assert.Equal((ushort)1, updated.Records[0].EndGlyphID);
        // start > end refuses with the read-side face.
        Assert.Equal(ReadStatus.InvalidFormat, model.TryWithRange(0, 9, 3, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        // Overlapping the next record refuses at the next record's face.
        Assert.Equal(ReadStatus.InvalidFormat, model.TryWithRange(0, 0, 8, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(8UL, diagnostic.Detail);
        // Overlapping the previous record refuses.
        Assert.Equal(ReadStatus.InvalidFormat, model.TryWithRange(1, 5, 20, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        // An out-of-domain record index refuses.
        Assert.Equal(ReadStatus.InvalidIndex, model.TryWithRange(2, 0, 1, out _, out diagnostic));
    }

    [Fact]
    public void SvgFactoryAndCompileRefusalWings()
    {
        var documents = new byte[][] { [0x61] };
        Assert.Equal(ReadStatus.UnsupportedFormat, SvgTableCompiler.TryCreate(1, 0,
            [new SvgDocumentRecord(2, 2, 0)], documents, out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal(ReadStatus.InvalidFormat, SvgTableCompiler.TryCreate(0, 7,
            [new SvgDocumentRecord(2, 2, 0)], documents, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ReadStatus.InvalidFormat, SvgTableCompiler.TryCreate(0, 0,
            [], documents, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ReadStatus.InvalidIndex, SvgTableCompiler.TryCreate(0, 0,
            [new SvgDocumentRecord(2, 2, 3)], documents, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(ReadStatus.InvalidFormat, SvgTableCompiler.TryCreate(0, 0,
            [new SvgDocumentRecord(9, 3, 0)], documents, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ReadStatus.InvalidFormat, SvgTableCompiler.TryCreate(0, 0,
            [new SvgDocumentRecord(2, 2, 0), new SvgDocumentRecord(2, 4, 0)], documents,
            out _, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ReadStatus.InvalidFormat, SvgTableCompiler.TryCreate(0, 0,
            [new SvgDocumentRecord(2, 2, 0)], [[]], out _, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
    }

    [Fact]
    public void SvgWriteIsAtomicAndDeterministic()
    {
        FontDiagnostic diagnostic;

        var wire = SvgSbixReadTests.SvgMinimal();
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out _, Source));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(out var model, out diagnostic));
        Assert.NotNull(model);
        var destination = new byte[wire.Length - 1];
        Assert.Equal(ReadStatus.DestinationTooSmall, SvgTableCompiler.TryWrite(model!,
            destination, out diagnostic));
        Assert.Equal(DiagnosticCode.DestinationTooSmall, diagnostic.Code);
        Assert.True(destination.AsSpan().SequenceEqual(new byte[destination.Length]));
        var exact = new byte[wire.Length];
        Assert.Equal(ReadStatus.Done, SvgTableCompiler.TryWrite(model!, exact, out diagnostic));
        Assert.Equal(ReadStatus.Done, SvgTableCompiler.TryCompile(model!, out var compiled, out diagnostic));
        Assert.Equal(compiled, exact);
    }

    // ---- sbix compiler -----------------------------------------------------

    [Fact]
    public void SbixReadModelRecompilesByteIdentical()
    {
        var wire = SvgSbixReadTests.SbixWire(1,
        [
            (20, 72, [SvgSbixReadTests.GlyphSlot("png ", 1, 2), null,
                SvgSbixReadTests.GlyphSlot("png ")]),
            (96, 72, [SvgSbixReadTests.GlyphSlot("png "), null, null]),
        ], 3);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out var diagnostic, SbixSource));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(3, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(3, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(ReadStatus.Done, SbixTableCompiler.TryCompile(model!, out var compiled, out diagnostic));
        Assert.NotNull(compiled);
        Assert.Equal(wire, compiled);
    }

    [Fact]
    public void SbixNullFormSourceRecompilesWireEquivalent()
    {
        // The NULL-anchor empty form is legal on the wire; the canonical plan
        // re-emits it as the equal form (OQ-T6-04-11's decided tier).
        var wire = SvgSbixReadTests.SbixWire(1,
            [(109, 72, [SvgSbixReadTests.NullSlot, SvgSbixReadTests.GlyphSlot("png ")])], 2);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out var diagnostic, SbixSource));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(2, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(2, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(ReadStatus.Done, SbixTableCompiler.TryCompile(model!, out var compiled, out diagnostic));
        Assert.NotNull(compiled);
        Assert.NotEqual(wire, compiled!);
        // The canonical offsets: glyph 0's anchor equals glyph 1's (equal form).
        uint anchor0 = ReadU32(compiled, 16);
        uint anchor1 = ReadU32(compiled, 20);
        Assert.NotEqual(0u, anchor0);
        Assert.Equal(anchor0, anchor1);
        // Content preserved: the re-read model equals the original.
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(compiled, out var reRead, out _, SbixSource));
        Assert.Equal(ReadStatus.Done, reRead.ValidateTable(2, out diagnostic));
        Assert.Equal(ReadStatus.Done, reRead.TryReadTable(2, out var reModel, out diagnostic));
        Assert.NotNull(reModel);
        Assert.Equal(model!.Strikes[0].Ppem, reModel!.Strikes[0].Ppem);
        Assert.Null(reModel.Strikes[0].Glyphs[0]);
        Assert.True(model.Strikes[0].Glyphs[1]!.Value.Data.AsSpan()
            .SequenceEqual(reModel.Strikes[0].Glyphs[1]!.Value.Data));
        // The write-read-write fixed point.
        Assert.Equal(ReadStatus.Done, SbixTableCompiler.TryCompile(reModel, out var again, out diagnostic));
        Assert.Equal(compiled, again);
    }

    [Fact]
    public void SbixFactoryProducesTheCanonicalLayout()
    {
        FontDiagnostic diagnostic;

        var glyphs = new SbixGlyph?[2]
        {
            new(3, -4, 0x706E6720, [0x01, 0x02]),
            null,
        };
        var strikes = new SbixStrike[] { new(109, 72, glyphs) };
        Assert.Equal(ReadStatus.Done, SbixTableCompiler.TryCreate(1, 1, 2, strikes,
            out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(ReadStatus.Done, SbixTableCompiler.TryCompile(model!, out var compiled, out diagnostic));
        Assert.NotNull(compiled);
        // header(8) + strikeOffsets(4) + strike head(4) + offsets(12) + slot(10) = 38.
        Assert.Equal(38, compiled!.Length);
        Assert.Equal(12u, ReadU32(compiled, 8));   // strike base after the array
        Assert.Equal((ushort)109, (ushort)(compiled[12] << 8 | compiled[13]));
        Assert.Equal((ushort)72, (ushort)(compiled[14] << 8 | compiled[15]));
        Assert.Equal(16u, ReadU32(compiled, 16));  // offsets[0]: head(4) + array(12)
        Assert.Equal(26u, ReadU32(compiled, 20));  // offsets[1]: equal form
        Assert.Equal(26u, ReadU32(compiled, 24));  // sentinel
        Assert.Equal(0, compiled[28]);             // originOffsetX = 3 (high byte)
        Assert.Equal(3, compiled[29]);
        Assert.Equal(0xFF, compiled[30]);          // originOffsetY = -4
        Assert.Equal(0xFC, compiled[31]);
        Assert.Equal(0x70, compiled[32]);          // 'p' of 'png '
    }

    [Fact]
    public void SbixGlyphEditsAndRefusalWings()
    {
        var wire = SvgSbixReadTests.SbixWire(1,
            [(109, 72, [SvgSbixReadTests.GlyphSlot("png ", 9, 9), SvgSbixReadTests.GlyphSlot("png ")])],
            2);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out var diagnostic, SbixSource));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(2, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(2, out var model, out diagnostic));
        Assert.NotNull(model);

        // Origin rewrite: the i16 pair travels raw.
        Assert.Equal(ReadStatus.Done, model!.TryWithGlyphOrigin(0, 0, -5, 7, out var moved,
            out diagnostic));
        Assert.NotNull(moved);
        Assert.Equal(-5, moved!.Strikes[0].Glyphs[0]!.Value.OriginOffsetX);
        Assert.Equal(7, moved.Strikes[0].Glyphs[0]!.Value.OriginOffsetY);
        // A non-empty slot's origin rewrites freely.
        Assert.Equal(ReadStatus.Done, moved.TryWithGlyphOrigin(0, 1, 1, 1, out var other, out diagnostic));
        Assert.NotNull(other);
        var emptyStrikes = new SbixStrike[] { new(109, 72, new SbixGlyph?[2]) };
        Assert.Equal(ReadStatus.Done, SbixTableCompiler.TryCreate(1, 1, 2, emptyStrikes,
            out var emptyModel, out diagnostic));
        Assert.NotNull(emptyModel);
        Assert.Equal(ReadStatus.InvalidIndex, emptyModel!.TryWithGlyphOrigin(0, 0, 1, 1,
            out _, out diagnostic));

        // Payload replacement: the closed set re-answers.
        Assert.Equal(ReadStatus.Done, model.TryWithGlyphPayload(0, 0, 0x6A706720,
            [0x01], out var replaced, out diagnostic)); // 'jpg '
        Assert.NotNull(replaced);
        Assert.Equal(0x6A706720u, replaced!.Strikes[0].Glyphs[0]!.Value.GraphicType);
        Assert.Equal(ReadStatus.UnsupportedFormat, model.TryWithGlyphPayload(0, 0, 0x70646620,
            [0x01], out _, out diagnostic)); // 'pdf '
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);

        // A dupe replacement re-answers the target layers: a legal target
        // lands; an out-of-domain or empty target refuses; a cycle refuses.
        Assert.Equal(ReadStatus.Done, model.TryWithGlyphPayload(0, 1, 0x64757065,
            [0x00, 0x00], out var duped, out diagnostic)); // dupe -> glyph 0
        Assert.NotNull(duped);
        Assert.Equal(ReadStatus.InvalidIndex, model.TryWithGlyphPayload(0, 1, 0x64757065,
            [0x00, 0x09], out _, out diagnostic));
        Assert.Equal(ReadStatus.InvalidFormat, model.TryWithGlyphPayload(0, 1, 0x64757065,
            [0x00, 0x01], out _, out diagnostic)); // target 1 is the slot itself -> cycle face
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ReadStatus.InvalidFormat, model.TryWithGlyphPayload(0, 0, 0x64757065,
            [0x00, 0x00], out _, out diagnostic)); // self-dupe -> cycle face
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ReadStatus.Truncated, model.TryWithGlyphPayload(0, 1, 0x64757065,
            [0x00], out _, out diagnostic));
    }

    [Fact]
    public void SbixFactoryAndCompileRefusalWings()
    {
        var glyphs = new SbixGlyph?[1] { new(0, 0, 0x706E6720, [0x01]) };
        Assert.Equal(ReadStatus.InvalidFormat, SbixTableCompiler.TryCreate(1, 1, -1,
            [], out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ReadStatus.UnsupportedFormat, SbixTableCompiler.TryCreate(0, 1, 1,
            [new SbixStrike(72, 72, glyphs)], out _, out diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal(1UL, diagnostic.Expected);
        Assert.Equal(ReadStatus.InvalidFormat, SbixTableCompiler.TryCreate(1, 0, 1,
            [new SbixStrike(72, 72, glyphs)], out _, out diagnostic));
        Assert.Equal(ReadStatus.InvalidFormat, SbixTableCompiler.TryCreate(1, 0x0004, 1,
            [new SbixStrike(72, 72, glyphs)], out _, out diagnostic));
        Assert.Equal(ReadStatus.InvalidFormat, SbixTableCompiler.TryCreate(1, 1, 2,
            [new SbixStrike(72, 72, glyphs)], out _, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ReadStatus.Done, SbixTableCompiler.TryCreate(1, 1, 0, [], out var shell,
            out diagnostic));
        Assert.NotNull(shell);
        Assert.Equal(ReadStatus.Done, SbixTableCompiler.TryCompile(shell!, out var compiled, out diagnostic));
        Assert.Equal(8, compiled!.Length);
    }

    [Fact]
    public void SbixWriteIsAtomicAndDeterministic()
    {
        FontDiagnostic diagnostic;

        var wire = SvgSbixReadTests.SbixWire(1, [(109, 72, [SvgSbixReadTests.GlyphSlot("png ")])], 1);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out _, SbixSource));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(1, out var model, out diagnostic));
        Assert.NotNull(model);
        var destination = new byte[wire.Length - 1];
        Assert.Equal(ReadStatus.DestinationTooSmall, SbixTableCompiler.TryWrite(model!,
            destination, out diagnostic));
        Assert.True(destination.AsSpan().SequenceEqual(new byte[destination.Length]));
        var exact = new byte[wire.Length];
        Assert.Equal(ReadStatus.Done, SbixTableCompiler.TryWrite(model!, exact, out diagnostic));
        Assert.Equal(ReadStatus.Done, SbixTableCompiler.TryCompile(model!, out var compiled, out diagnostic));
        Assert.Equal(wire, exact);
        Assert.Equal(compiled, exact);
    }
}
