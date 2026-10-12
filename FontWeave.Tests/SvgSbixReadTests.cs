using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-04 commit 2's read chains: the SVG whole-table walk (the
/// four non-zero gates, the three range clauses, the list-relative document
/// windows, the reserved word, the gzip magic observation and the shared-group
/// projection) and the sbix walk (the flags three-segment contract, the
/// strikeOffset gate, the explicit numGlyphs authority, the ascending contract
/// over the OQ-T6-04-11 empty dual form, the graphicType closed set and the
/// dupe three-layer semantics), with the owned models' materializations. The
/// synthetic family mirrors the frozen case set's faces six-tuple by six-tuple;
/// the wire builders are the shared fixtures the write tests reuse.</summary>
public sealed class SvgSbixReadTests
{
    private static readonly DataLocation Source = new(9, 0, 0x53564720, -1,
        DataField.None, OffsetBasis.Table, 0);
    private static readonly DataLocation SbixSource = new(9, 0, 0x73626978, -1,
        DataField.None, OffsetBasis.Table, 0);

    // ---- SVG wire builders -------------------------------------------------

    /// <summary>A minimal legal SVG v0 wire: header, one record over one
    /// document, docList immediately after the header.</summary>
    internal static byte[] SvgMinimal(ushort start = 2, ushort end = 2,
        bool immediateList = true, bool gzip = false)
    {
        byte[] doc = gzip
            ? [0x1F, 0x8B, 0x08, 0x00, 0x7B]
            : [0x3C, 0x73, 0x76, 0x67, 0x3E]; // "<svg>"
        return SvgWire([(start, end, doc)], immediateList);
    }

    /// <summary>Assembles an SVG wire: header(10) + [padding] + numEntries +
    /// records + documents laid out sequentially (list-relative offsets computed
    /// here; tests patch offsets afterwards for the overlap shapes).</summary>
    internal static byte[] SvgWire((ushort start, ushort end, byte[] doc)[] entries,
        bool immediateList = true, int padding = 0)
    {
        int recordsAt = 12 + padding;
        int docsAt = recordsAt + 12 * entries.Length;
        int total = docsAt + entries.Sum(entry => entry.doc.Length);
        var wire = new byte[total];
        WriteU16(wire, 0, 0);
        WriteU32(wire, 2, (uint)(immediateList ? 10 : 10 + padding));
        WriteU32(wire, 6, 0);
        uint listOffset = (uint)(immediateList ? 10 : 10 + padding);
        WriteU16(wire, (int)listOffset, (ushort)entries.Length);
        int cursor = docsAt;
        for (int i = 0; i < entries.Length; i++)
        {
            int at = (int)listOffset + 2 + 12 * i;
            WriteU16(wire, at, entries[i].start);
            WriteU16(wire, at + 2, entries[i].end);
            WriteU32(wire, at + 4, (uint)(cursor - listOffset));
            WriteU32(wire, at + 8, (uint)entries[i].doc.Length);
            entries[i].doc.CopyTo(wire, cursor);
            cursor += entries[i].doc.Length;
        }
        return wire;
    }

    // ---- sbix wire builders ------------------------------------------------

    /// <summary>The NULL-anchor form marker for one glyph slot (OQ-T6-04-11's
    /// offset-0 shape; a plain null builds the canonical equal-consecutive
    /// form).</summary>
    internal static readonly byte[] NullSlot = [0];

    /// <summary>Assembles a sbix wire: header(8) + strikeOffsets + strikes
    /// packed in strike order. Glyph entries are the raw GlyphData slot bytes
    /// (the 8-byte head - origin zeros plus the graphicType - over the opaque
    /// payload), so a slot's span is exactly its byte length; NullSlot builds
    /// the NULL-anchor form and a plain null the canonical equal form.</summary>
    internal static byte[] SbixWire(ushort flags,
        (ushort ppem, ushort ppi, byte[]?[] glyphs)[] strikes,
        int numGlyphs, ushort version = 1)
    {
        int offsetsAt = 8 + 4 * strikes.Length;
        int total = offsetsAt;
        var plan = new uint[strikes.Length];
        for (int s = 0; s < strikes.Length; s++)
        {
            plan[s] = (uint)total;
            total += 4 + 4 * (numGlyphs + 1);
            foreach (var glyph in strikes[s].glyphs)
                if (glyph is not null && glyph != NullSlot)
                    total += glyph.Length;
        }
        var wire = new byte[total];
        WriteU16(wire, 0, version);
        WriteU16(wire, 2, flags);
        WriteU32(wire, 4, (uint)strikes.Length);
        for (int s = 0; s < strikes.Length; s++)
            WriteU32(wire, 8 + 4 * s, plan[s]);
        for (int s = 0; s < strikes.Length; s++)
        {
            int at = (int)plan[s];
            WriteU16(wire, at, strikes[s].ppem);
            WriteU16(wire, at + 2, strikes[s].ppi);
            int arrayAt = at + 4;
            var offsets = new uint[numGlyphs + 1];
            uint running = (uint)(4 + 4 * (numGlyphs + 1));
            int cursor = arrayAt + 4 * (numGlyphs + 1);
            for (int g = 0; g < numGlyphs; g++)
            {
                var glyph = strikes[s].glyphs[g];
                offsets[g] = glyph == NullSlot ? 0 : running;
                if (glyph is null || glyph == NullSlot)
                    continue;
                glyph.CopyTo(wire, cursor);
                running += (uint)glyph.Length;
                cursor += glyph.Length;
            }
            offsets[numGlyphs] = running;
            for (int k = 0; k <= numGlyphs; k++)
                WriteU32(wire, arrayAt + 4 * k, offsets[k]);
        }
        return wire;
    }

    /// <summary>One raw GlyphData slot: the 8-byte head (origin pair zero, the
    /// graphicType tag) over the given payload bytes.</summary>
    internal static byte[] GlyphSlot(string tag, params byte[] payload)
    {
        var slot = new byte[8 + payload.Length];
        System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(slot, 4);
        payload.CopyTo(slot, 8);
        return slot;
    }

    /// <summary>A 'dupe' slot carrying its big-endian target glyph ID.</summary>
    internal static byte[] DupeSlot(ushort target) =>
        GlyphSlot("dupe", (byte)(target >> 8), (byte)target);

    private static uint ReadU32(byte[] wire, int at) =>
        (uint)(wire[at] << 24 | wire[at + 1] << 16 | wire[at + 2] << 8 | wire[at + 3]);

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

    // ---- SVG read chain ----------------------------------------------------

    [Fact]
    public void SvgMinimalReadsAndMaterializes()
    {
        var wire = SvgMinimal();
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal((ushort)0, model!.Version);
        Assert.Equal(0u, model.Reserved);
        Assert.Equal(1, model.RecordCount);
        Assert.Equal(1, model.DocumentCount);
        Assert.Equal(2, model.Records[0].StartGlyphID);
        Assert.Equal(2, model.Records[0].EndGlyphID);
        Assert.Equal(0, model.Records[0].PayloadIndex);
        Assert.True(model.Documents[0].AsSpan().SequenceEqual(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x3E }));
        Assert.Equal(ReadStatus.Done, view.IsGzipPayload(0, out bool isGzip, out _));
        Assert.False(isGzip);
    }

    [Fact]
    public void SvgGzipMagicIsObservedNeverDecoded()
    {
        var wire = SvgWire([(2, 2, [0x1F, 0x8B, 0x08, 0x00, 0x7B])]);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(out diagnostic));
        Assert.Equal(ReadStatus.Done, view.IsGzipPayload(0, out bool isGzip, out diagnostic));
        Assert.True(isGzip);
        // The payload travels verbatim: the structural layer never decompresses.
        Assert.Equal(ReadStatus.Done, view.TryGetDocumentPayload(0, out var payload, out diagnostic));
        Assert.Equal(0x7B, payload[4]);
    }

    [Fact]
    public void SvgSharedDocumentsDeduplicateByWireIdentity()
    {
        // Two records sharing one document over discontinuous ranges (V L86),
        // plus a distinct third record.
        byte[] shared = [0x61, 0x62, 0x63, 0x64];
        byte[] lone = [0x7A];
        var wire = SvgWire([(2, 2, shared), (13, 14, shared), (20, 20, lone)]);
        // The builder lays documents sequentially; pointing record 1 at
        // record 0's (offset, length) key is what makes the two records share
        // one document on the wire (V L86's discontinuous ranges).
        WriteU32(wire, 28, 38); // record 1's svgDocOffset -> document 0's
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(3, model!.RecordCount);
        Assert.Equal(2, model.DocumentCount);
        Assert.Equal(0, model.Records[0].PayloadIndex);
        Assert.Equal(0, model.Records[1].PayloadIndex);
        Assert.Equal(1, model.Records[2].PayloadIndex);
        // The shared-group projection: group of 2 for record 0, members 0 and 1.
        Assert.Equal(ReadStatus.Done, view.TryGetDocumentGroupSize(0, out int size, out diagnostic));
        Assert.Equal(2, size);
        Assert.Equal(ReadStatus.Done, view.TryGetDocumentGroupMember(0, 1, out int member, out diagnostic));
        Assert.Equal(1, member);
        Assert.Equal(ReadStatus.InvalidIndex, view.TryGetDocumentGroupMember(0, 2, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        // The identical-content document at a distinct offset stays distinct
        // (the wire's own identity), so record 2's group is a singleton.
        Assert.Equal(ReadStatus.Done, view.TryGetDocumentGroupSize(2, out size, out diagnostic));
        Assert.Equal(1, size);
    }

    [Fact]
    public void SvgPartialOverlapAndNonImmediateDocListAreObservations()
    {
        // Two documents whose byte ranges partially overlap: no spec ban, a
        // read-side observation; the docList also sits after 4 padding bytes.
        var wire = SvgWire(
        [
            (2, 2, [0x01, 0x02, 0x03, 0x04]),
            (5, 5, [0x03, 0x04, 0x05]),
        ], immediateList: false, padding: 4);
        // The partial overlap is the observation (no spec ban): record 1's
        // document re-anchors two bytes into document 0's span, and the wire
        // stays fully inside the table window. Record rows sit at 16/28, so
        // record 1's svgDocOffset word is at 32.
        WriteU32(wire, 32, 28);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryGetDocumentSpan(0, out ulong start0, out ulong length0, out diagnostic));
        Assert.Equal(40UL, start0); // list at 14, document 0 at list offset 26
        Assert.Equal(4UL, length0);
        Assert.Equal(ReadStatus.Done, view.TryGetDocumentSpan(1, out ulong start, out ulong length, out diagnostic));
        Assert.Equal(42UL, start);
        Assert.Equal(3UL, length);
    }

    [Fact]
    public void SvgHeaderTruncatesAtTryOpen()
    {
        byte[] wire = SvgMinimal()[..9];
        Assert.Equal(ReadStatus.Truncated, SvgView.TryOpen(wire, out _, out var diagnostic, Source));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(6UL, diagnostic.Location.Offset);
        Assert.Equal(10UL, diagnostic.Expected);
    }

    [Fact]
    public void SvgVersionOneRefusesAtTheGeneratedGate()
    {
        byte[] wire = SvgMinimal();
        WriteU16(wire, 0, 1);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.UnsupportedFormat, view.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal(DataField.Version, diagnostic.Location.Field);
        Assert.Equal(0UL, diagnostic.Location.Offset);
        Assert.Equal(1UL, diagnostic.Detail);
        Assert.Equal(0UL, diagnostic.Expected);
    }

    [Fact]
    public void SvgReservedNonZeroRefuses()
    {
        byte[] wire = SvgMinimal();
        WriteU32(wire, 6, 7);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(6UL, diagnostic.Location.Offset);
        Assert.Equal(7UL, diagnostic.Detail);
        Assert.Equal(0UL, diagnostic.Expected);
    }

    [Fact]
    public void SvgDocumentListOffsetZeroRefuses()
    {
        byte[] wire = SvgMinimal();
        WriteU32(wire, 2, 0);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(2UL, diagnostic.Location.Offset);
        Assert.Equal(0UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);
    }

    [Fact]
    public void SvgDocumentListOutsideRefusesAsRange()
    {
        byte[] wire = SvgMinimal();
        WriteU32(wire, 2, (uint)wire.Length);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.InvalidOffset, view.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(2UL, diagnostic.Location.Offset);
        Assert.Equal((uint)wire.Length, diagnostic.Detail);
        Assert.Equal((uint)wire.Length, diagnostic.Expected);
    }

    [Fact]
    public void SvgNumEntriesZeroRefuses()
    {
        var wire = SvgMinimal();
        wire[10] = 0;
        wire[11] = 0;
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Count, diagnostic.Location.Field);
        Assert.Equal(10UL, diagnostic.Location.Offset);
        Assert.Equal(0UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);
    }

    [Fact]
    public void SvgRecordsTruncatedNamesTheFirstIncompleteField()
    {
        // One declared record, the wire ends inside svgDocOffset (start and end
        // present, the offset word short).
        var full = SvgMinimal();
        var wire = new byte[16];
        full.AsSpan()[..16].CopyTo(wire);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out _, Source));
        Assert.Equal(ReadStatus.Truncated, view.ValidateTable(out var diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(16UL, diagnostic.Location.Offset);
        Assert.Equal(16UL, diagnostic.Detail);
        Assert.Equal(20UL, diagnostic.Expected);
    }

    [Fact]
    public void SvgDocOffsetAndLengthZeroRefuseInRecordOrder()
    {
        var wire = SvgWire([(2, 2, [0x61]), (5, 5, [0x62])]);
        WriteU32(wire, 12 + 4, 0); // record 0's svgDocOffset = 0
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(16UL, diagnostic.Location.Offset); // record 0 at 12, offset word at +4
        Assert.Equal(0UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);

        var wire2 = SvgWire([(2, 2, [0x61])]);
        WriteU32(wire2, 12 + 8, 0); // svgDocLength = 0
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire2, out var view2, out _, Source));
        Assert.Equal(ReadStatus.InvalidFormat, view2.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Length, diagnostic.Location.Field);
        Assert.Equal(20UL, diagnostic.Location.Offset); // record 0 at 12, length word at +8
        Assert.Equal(0UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);
    }

    [Fact]
    public void SvgStartBeyondEndRefuses()
    {
        var wire = SvgWire([(7, 3, [0x61])]);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(wire, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.GlyphId, diagnostic.Location.Field);
        Assert.Equal(12UL, diagnostic.Location.Offset);
        Assert.Equal(7UL, diagnostic.Detail);
        Assert.Equal(3UL, diagnostic.Expected);
    }

    [Fact]
    public void SvgOverlappingAndReorderedRecordsRefuse()
    {
        // Overlap: record 1 starts at the previous record's end.
        var overlap = SvgWire([(2, 5, [0x61]), (5, 9, [0x62])]);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(overlap, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.GlyphId, diagnostic.Location.Field);
        Assert.Equal(24UL, diagnostic.Location.Offset); // record 1's start word
        Assert.Equal(5UL, diagnostic.Detail);
        Assert.Equal(5UL, diagnostic.Expected);

        // Reordered without touching ranges: strictly decreasing starts are
        // overlaps under the same clause.
        var reordered = SvgWire([(20, 25, [0x61]), (4, 9, [0x62])]);
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(reordered, out var view2, out _, Source));
        Assert.Equal(ReadStatus.InvalidFormat, view2.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(4UL, diagnostic.Detail);
        Assert.Equal(25UL, diagnostic.Expected);
    }

    [Fact]
    public void SvgDocumentWindowsRefuseAtListRelativeFaces()
    {
        // A document whose list-relative start lands at/after the table end.
        var outside = SvgWire([(2, 2, [0x61])]);
        WriteU32(outside, 16, (uint)(outside.Length + 4)); // svgDocOffset beyond end
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(outside, out var view, out var diagnostic, Source));
        Assert.Equal(ReadStatus.InvalidOffset, view.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(16UL, diagnostic.Location.Offset);
        Assert.Equal((uint)(outside.Length + 4 + 10), diagnostic.Detail); // the absolute start
        Assert.Equal((uint)outside.Length, diagnostic.Expected);

        // A document whose body runs past the table end: available/required.
        var truncated = SvgWire([(2, 2, [0x61, 0x62, 0x63, 0x64, 0x65, 0x66])]);
        WriteU32(truncated, 20, 9); // svgDocLength larger than the body
        Assert.Equal(ReadStatus.Done, SvgView.TryOpen(truncated, out var view2, out _, Source));
        Assert.Equal(ReadStatus.Truncated, view2.ValidateTable(out diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(16UL, diagnostic.Location.Offset);
        Assert.Equal(6UL, diagnostic.Detail);
        Assert.Equal(9UL, diagnostic.Expected);
    }

    // ---- sbix read chain ---------------------------------------------------

    [Fact]
    public void SbixMinimalReadsAndMaterializes()
    {
        var wire = SbixWire(1, [(109, 72, [GlyphSlot("png ")])], 1);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out var diagnostic, SbixSource));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(1, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(1, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal((ushort)1, model!.Version);
        Assert.Equal((ushort)1, model.Flags);
        Assert.Equal(1, model.NumGlyphs);
        Assert.Equal(1, model.StrikeCount);
        Assert.Equal((ushort)109, model.Strikes[0].Ppem);
        Assert.Equal((ushort)72, model.Strikes[0].Ppi);
        SbixGlyph? slot = model.Strikes[0].Glyphs[0];
        Assert.NotNull(slot);
        var glyph = slot!.Value;
        Assert.Equal(0x706E6720u, glyph.GraphicType); // 'png '
        Assert.Equal(0, glyph.OriginOffsetX);
        Assert.Equal(0, glyph.OriginOffsetY);
        Assert.Empty(glyph.Data);
    }

    [Fact]
    public void SbixMultiStrikePackedAndUnsortedOrderAreObservations()
    {
        var wire = SbixWire(1,
        [
            (20, 72, [GlyphSlot("png "), null, null]),
            (96, 72, [GlyphSlot("png "), GlyphSlot("png "), GlyphSlot("png ")]),
        ], 3);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out var diagnostic, SbixSource));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(3, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(3, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(2, model!.StrikeCount);
        Assert.Null(model.Strikes[0].Glyphs[1]);
        Assert.Null(model.Strikes[0].Glyphs[2]);

        // The strikes in decreasing-offset order are the spec-silent shape:
        // an observation, never a refusal (OQ-T6-04-10's observation wing).
        var unsorted = SbixWire(1,
        [
            (96, 72, [GlyphSlot("png ")]),
            (20, 72, [GlyphSlot("png ")]),
        ], 1);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(unsorted, out var view2, out _, SbixSource));
        Assert.Equal(ReadStatus.Done, view2.ValidateTable(1, out diagnostic));
    }

    [Fact]
    public void SbixNullSlotFormIsLegalAndNormalizesEmpty()
    {
        // A NULL anchor slot (offset 0): the Apple form, legal next to the
        // canonical equal form.
        var wire = SbixWire(1, [(109, 72, [GlyphSlot("png "), NullSlot])], 2);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out var diagnostic, SbixSource));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(2, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(2, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Null(model!.Strikes[0].Glyphs[1]);
        // The normalized span answers Done with a zero-length slice.
        Assert.Equal(ReadStatus.Done, view.TryGetGlyphSpan(0, 1, out var span, out diagnostic));
        Assert.Equal(0, span.Length);
        Assert.Equal(ReadStatus.Done, view.TryGetGlyphHeader(0, 1, out _, out _, out _, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryGetGlyphOffsets(0, 1, out uint anchor, out uint next, out diagnostic));
        Assert.Equal(0u, anchor); // the NULL form's raw anchor
    }

    [Fact]
    public void SbixSamePpemStrikesAndNumStrikesZeroAreLegal()
    {
        var samePpem = SbixWire(1,
        [
            (109, 72, [GlyphSlot("png ")]),
            (109, 144, [GlyphSlot("png ")]),
        ], 1);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(samePpem, out var view, out var diagnostic, SbixSource));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(1, out diagnostic));

        var shell = SbixWire(1, [], 0);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(shell, out var view2, out _, SbixSource));
        Assert.Equal(ReadStatus.Done, view2.ValidateTable(0, out diagnostic));
        Assert.Equal(ReadStatus.Done, view2.TryReadTable(0, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Empty(model!.Strikes);
    }

    [Fact]
    public void SbixFlagsBitOneIsObservedNeverChecked()
    {
        // flags = 0x0003: bit0 set (required), bit1 set (draw outlines - the
        // application instruction), reserved zero.
        var wire = SbixWire(3, [(109, 72, [GlyphSlot("png ")])], 1);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out var diagnostic, SbixSource));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(1, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryGetFlags(out ushort flags, out diagnostic));
        Assert.Equal((ushort)3, flags);
    }

    [Fact]
    public void SbixDupeChainTerminatesLegally()
    {
        // gid0 dupe -> gid1 dupe -> gid2 'png ': chains terminate at the first
        // non-dupe graphic (B L101), no cycle face.
        var wire = SbixWire(1,
            [(109, 72, [DupeSlot(1), DupeSlot(2), GlyphSlot("png ")])],
            3);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out var diagnostic, SbixSource));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(3, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryGetDupeTarget(0, 0, out ushort target, out diagnostic));
        Assert.Equal((ushort)1, target);
    }

    [Fact]
    public void SbixHeaderAndStrikeOffsetFaces()
    {
        byte[] wire = SbixWire(1, [(109, 72, [GlyphSlot("png ")])], 1)[..7];
        Assert.Equal(ReadStatus.Truncated, SbixView.TryOpen(wire, out _, out var diagnostic, SbixSource));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Count, diagnostic.Location.Field);
        Assert.Equal(4UL, diagnostic.Location.Offset);
        Assert.Equal(8UL, diagnostic.Expected);

        var versionWire = SbixWire(1, [(109, 72, [GlyphSlot("png ")])], 1, version: 0);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(versionWire, out var view, out _, SbixSource));
        Assert.Equal(ReadStatus.UnsupportedFormat, view.ValidateTable(1, out diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal(DataField.Version, diagnostic.Location.Field);
        Assert.Equal(0UL, diagnostic.Location.Offset);
        Assert.Equal(0UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);

        var zeroStrike = SbixWire(1, [(109, 72, [GlyphSlot("png ")])], 1);
        WriteU32(zeroStrike, 8, 0);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(zeroStrike, out var view2, out _, SbixSource));
        Assert.Equal(ReadStatus.InvalidFormat, view2.ValidateTable(1, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(8UL, diagnostic.Location.Offset);
        Assert.Equal(0UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);

        var outsideStrike = SbixWire(1, [(109, 72, [GlyphSlot("png ")])], 1);
        WriteU32(outsideStrike, 8, (uint)outsideStrike.Length);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(outsideStrike, out var view3, out _, SbixSource));
        Assert.Equal(ReadStatus.InvalidOffset, view3.ValidateTable(1, out diagnostic));
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal((uint)outsideStrike.Length, diagnostic.Detail);
        Assert.Equal((uint)outsideStrike.Length, diagnostic.Expected);
    }

    [Fact]
    public void SbixFlagsFacesPinTheWord()
    {
        var bit0 = SbixWire(0, [(109, 72, [GlyphSlot("png ")])], 1);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(bit0, out var view, out var diagnostic, SbixSource));
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(1, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Flags, diagnostic.Location.Field);
        Assert.Equal(2UL, diagnostic.Location.Offset);
        Assert.Equal(0UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);

        var reserved = SbixWire(0x0005, [(109, 72, [GlyphSlot("png ")])], 1);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(reserved, out var view2, out _, SbixSource));
        Assert.Equal(ReadStatus.InvalidFormat, view2.ValidateTable(1, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Flags, diagnostic.Location.Field);
        Assert.Equal(0x0004UL, diagnostic.Detail);
        Assert.Equal(0UL, diagnostic.Expected);
    }

    [Fact]
    public void SbixArrayAndStrikeWindowsRefuse()
    {
        // The glyphDataOffsets array short by one slot: the sentinel row of an
        // all-empty strike (wire = 8 + 4 + 4 + 16 = 32... exactly 28 bytes;
        // the sentinel slot sits at 24..28 and the cut removes it).
        var arrayTruncated = SbixWire(1, [(109, 72, [NullSlot, NullSlot])], 2)[..24];
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(arrayTruncated, out var view, out _, SbixSource));
        Assert.Equal(ReadStatus.Truncated, view.ValidateTable(2, out var diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(24UL, diagnostic.Location.Offset);
        Assert.Equal((uint)arrayTruncated.Length, diagnostic.Detail);
        Assert.Equal(28UL, diagnostic.Expected);

        // The strike head short: ppem names its slot first.
        var headWire = SbixWire(1, [(109, 72, [GlyphSlot("png ")])], 1);
        var headTruncated = headWire[..13];
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(headTruncated, out var view2, out _, SbixSource));
        Assert.Equal(ReadStatus.Truncated, view2.ValidateTable(1, out diagnostic));
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(12UL, diagnostic.Location.Offset);
        Assert.Equal(13UL, diagnostic.Detail);
        Assert.Equal(14UL, diagnostic.Expected);
    }

    [Fact]
    public void SbixDescendingOffsetsRefuseAtTheLaterAnchor()
    {
        // gid1's anchor below gid0's: the dual-non-zero descent face.
        var wire = SbixWire(1, [(109, 72, [GlyphSlot("png "), GlyphSlot("png ")])], 2);
        uint first = ReadU32(wire, 16); // offsets[0] at the array start (12+4)
        WriteU32(wire, 20, first - 4);  // offsets[1] below it
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out _, SbixSource));
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(2, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(20UL, diagnostic.Location.Offset);
        Assert.Equal(first - 4UL, diagnostic.Detail);
        Assert.Equal(first, diagnostic.Expected);
    }

    [Fact]
    public void SbixDataWindowFaces()
    {
        // A glyph body running past the table end: the sentinel claims four
        // more bytes than the wire carries; available/required accounting.
        var wire = SbixWire(1, [(109, 72, [GlyphSlot("png ")])], 1);
        uint bodyEnd = ReadU32(wire, 20); // offsets[1]
        WriteU32(wire, 20, bodyEnd + 4);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out _, SbixSource));
        Assert.Equal(ReadStatus.Truncated, view.ValidateTable(1, out var diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(24UL, diagnostic.Location.Offset); // glyph data at base 12 + offsets[0] 12
        Assert.Equal((uint)(wire.Length - 24), diagnostic.Detail);
        Assert.Equal(bodyEnd - 12 + 4, diagnostic.Expected); // the claimed span
    }

    [Fact]
    public void SbixGraphicTypeClosedSetFaces()
    {
        // 'jpg ' and 'tiff' are in-set positives (corpus zero coverage, the
        // synthetic acceptance wing); 'pdf ', 'mask' and an unknown tag refuse.
        foreach (var (tag, legal) in new[]
                 { ("jpg ", true), ("tiff", true), ("pdf ", false), ("mask", false), ("webp", false) })
        {
            var wire = SbixWire(1, [(109, 72, [GlyphSlot(tag)])], 1);
            Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out _, SbixSource));
            ReadStatus status = view.ValidateTable(1, out var diagnostic);
            if (legal)
            {
                Assert.Equal(ReadStatus.Done, status);
            }
            else
            {
                Assert.Equal(ReadStatus.UnsupportedFormat, status);
                Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
                Assert.Equal(DataField.Tag, diagnostic.Location.Field);
                Assert.Equal(28UL, diagnostic.Location.Offset); // glyph data 24 + tag 4
                Assert.Equal(ReadU32(GlyphSlot(tag), 4), diagnostic.Detail);
                Assert.Equal(0UL, diagnostic.Expected);
            }
        }
    }

    [Fact]
    public void SbixDupeLayersRefuse()
    {
        // Truncated dupe: the slot span covers the 'dupe' tag but only one
        // payload byte (the frozen construction's shape).
        var truncated = SbixWire(1, [(109, 72, [NullSlot, GlyphSlot("dupe", 0xAA)])], 2);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(truncated, out var view, out _, SbixSource));
        Assert.Equal(ReadStatus.Truncated, view.ValidateTable(2, out var diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(36UL, diagnostic.Location.Offset); // glyph data 28 + head 8
        Assert.Equal(1UL, diagnostic.Detail); // the one-byte payload
        Assert.Equal(2UL, diagnostic.Expected);

        // Out-of-domain target.
        var oob = SbixWire(1, [(109, 72, [DupeSlot(5), GlyphSlot("png ")])], 2);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(oob, out var view2, out _, SbixSource));
        Assert.Equal(ReadStatus.InvalidIndex, view2.ValidateTable(2, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(DataField.GlyphId, diagnostic.Location.Field);
        Assert.Equal(5UL, diagnostic.Detail);
        Assert.Equal(2UL, diagnostic.Expected);

        // Dangling target: the dupe's target slot is NULL-empty (offset 0).
        var dangling = SbixWire(1,
            [(109, 72, [NullSlot, DupeSlot(0)])], 2);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(dangling, out var view3, out _, SbixSource));
        Assert.Equal(ReadStatus.InvalidIndex, view3.ValidateTable(2, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(0UL, diagnostic.Detail); // the target glyph ID
        Assert.Equal(0UL, diagnostic.Expected); // the empty anchor

        // Cycle: gid0 -> gid1 -> gid0; the revisit refuses at the hop's target
        // field with the repeated glyph as the detail.
        var cycle = SbixWire(1,
            [(109, 72, [DupeSlot(1), DupeSlot(0)])], 2);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(cycle, out var view4, out _, SbixSource));
        Assert.Equal(ReadStatus.InvalidFormat, view4.ValidateTable(2, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.GlyphId, diagnostic.Location.Field);
        Assert.Equal(0UL, diagnostic.Detail); // the repeated glyph
        Assert.Equal(0UL, diagnostic.Expected);
    }

    [Fact]
    public void SbixNumGlyphsInputContractRefusesFirst()
    {
        var wire = SbixWire(1, [(109, 72, [GlyphSlot("png ")])], 1);
        Assert.Equal(ReadStatus.Done, SbixView.TryOpen(wire, out var view, out _, SbixSource));
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(-1, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.NumGlyphs, diagnostic.Location.Field);
        Assert.Equal(0UL, diagnostic.Location.Offset);
        Assert.Equal(65535UL, diagnostic.Expected);
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(65536, out diagnostic));
        Assert.Equal(65535UL, diagnostic.Expected);
    }
}
