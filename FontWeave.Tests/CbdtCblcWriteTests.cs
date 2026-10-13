using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-05 commit 3's write faces: the canonical compilers'
/// write-read-write fixed point over synthetic wires, the identity tier (a
/// canonical source re-emits byte-for-byte), the wire-equivalent tier (a
/// gapped or unaligned source re-emits canonical and reads equal), the
/// cross-table fixup coordination (imageDataOffset/sbitOffset recomputed
/// against the packed pool, the SameSource count guard), the four TryWith*
/// edit faces with their value-domain refusals, and the short-destination
/// atomicity.</summary>
public sealed class CbdtCblcWriteTests
{
    private static readonly DataLocation CbdtSource = new(9, 0, 0x43424454, -1,
        DataField.None, OffsetBasis.Table, 0);
    private static readonly DataLocation CblcSource = new(9, 0, 0x43424C43, -1,
        DataField.None, OffsetBasis.Table, 0);

    private static void WriteU16(Span<byte> wire, int at, ushort value)
    {
        wire[at] = (byte)(value >> 8);
        wire[at + 1] = unchecked((byte)value);
    }

    private static void WriteU32(Span<byte> wire, int at, uint value)
    {
        WriteU16(wire, at, (ushort)(value >> 16));
        WriteU16(wire, at + 2, (ushort)value);
    }

    private static byte[] Image17(byte height, byte width, ReadOnlySpan<byte> payload)
    {
        var image = new byte[9 + payload.Length];
        image[0] = height; image[1] = width;
        image[2] = unchecked((byte)-1); image[3] = 2; image[4] = 3;
        WriteU32(image, 5, (uint)payload.Length);
        payload.CopyTo(image.AsSpan(9));
        return image;
    }

    /// <summary>A canonical-shaped pair: one strike over
    /// [firstGlyph..lastGlyph], one index-format-1 subtable whose offsets walk
    /// a pool of consecutive format-17 images packed from the pool base - the
    /// layout the compilers re-emit byte-for-byte.</summary>
    private static (byte[] cblc, byte[] cbdt) BuildPair(ushort firstGlyph,
        ushort lastGlyph, int[] slotLengths)
    {
        int span = lastGlyph - firstGlyph + 1;
        var images = new List<byte>();
        for (int k = 0; k < span; k++)
        {
            int length = slotLengths[k % slotLengths.Length];
            if (length == 0) continue;
            var filler = new byte[Math.Max(0, length - 9)];
            for (int b = 0; b < filler.Length; b++) filler[b] = 0x89;
            images.AddRange(Image17((byte)(16 + k), (byte)(16 + k), filler));
        }
        var cbdt = new byte[4 + images.Count];
        WriteU16(cbdt, 0, 3);
        WriteU16(cbdt, 2, 0);
        images.CopyTo(cbdt, 4);
        int listAt = 8 + 48;
        var cblc = new byte[listAt + 8 + 8 + 4 * (span + 1)];
        WriteU16(cblc, 0, 3);
        WriteU16(cblc, 2, 0);
        WriteU32(cblc, 4, 1);
        var row = new byte[48];
        WriteU32(row, 0, (uint)listAt);
        WriteU32(row, 4, (uint)(cblc.Length - listAt));
        WriteU32(row, 8, 1);
        WriteU16(row, 40, firstGlyph);
        WriteU16(row, 42, lastGlyph);
        row[44] = 16; row[45] = 16; row[46] = 32; row[47] = 1;
        row.CopyTo(cblc, 8);
        WriteU16(cblc, listAt, firstGlyph);
        WriteU16(cblc, listAt + 2, lastGlyph);
        WriteU32(cblc, listAt + 4, 8);
        WriteU16(cblc, listAt + 8, 1);
        WriteU16(cblc, listAt + 10, 17);
        WriteU32(cblc, listAt + 12, 4);
        uint running = 0;
        for (int k = 0; k < span; k++)
        {
            WriteU32(cblc, listAt + 16 + 4 * k, running);
            int length = slotLengths[k % slotLengths.Length];
            if (length != 0) running += (uint)length;
        }
        WriteU32(cblc, listAt + 16 + 4 * span, running);
        return (cblc, cbdt);
    }

    private static (byte[]? cblc, byte[]? cbdt) Recompile(byte[] cblc, byte[] cbdt,
        out ReadStatus cblcStatus, out ReadStatus cbdtStatus)
    {
        CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource);
        CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource);
        cblcStatus = cblcView.TryReadTable(cbdt.Length, null, out var cblcModel, out _);
        cbdtStatus = cbdtView.TryReadTable(cblcView, null, out var cbdtModel, out _);
        if (cblcStatus != ReadStatus.Done || cbdtStatus != ReadStatus.Done)
            return (null, null);
        var createCblc = CblcTableCompiler.TryCreate(cblcModel!.MajorVersion,
            cblcModel.MinorVersion, cblcModel.Sizes, cbdtModel!, out var cblcCanon,
            out _);
        var createCbdt = CbdtTableCompiler.TryCreate(cbdtModel!.MajorVersion,
            cbdtModel.MinorVersion, cbdtModel.Images, out var cbdtCanon, out _);
        if (createCblc != ReadStatus.Done || createCbdt != ReadStatus.Done)
        {
            cblcStatus = createCblc == ReadStatus.Done ? cbdtStatus : createCblc;
            return (null, null);
        }
        CblcTableCompiler.TryCompile(cblcCanon!, out var cblcBytes, out _);
        CbdtTableCompiler.TryCompile(cbdtCanon!, out var cbdtBytes, out _);
        return (cblcBytes, cbdtBytes);
    }

    [Fact]
    public void CanonicalSourceRecompilesByteForByte()
    {
        var (cblc, cbdt) = BuildPair(0, 1, [26, 26]);
        var (cblc2, cbdt2) = Recompile(cblc, cbdt, out var s1, out var s2);
        Assert.Equal(ReadStatus.Done, s1);
        Assert.Equal(ReadStatus.Done, s2);
        Assert.Equal(cblc, cblc2);
        Assert.Equal(cbdt, cbdt2);
    }

    [Fact]
    public void NumSizesZeroShellRoundTrips()
    {
        var cblc = new byte[8];
        WriteU16(cblc, 0, 3); WriteU16(cblc, 2, 0); WriteU32(cblc, 4, 0);
        var cbdt = new byte[4];
        WriteU16(cbdt, 0, 3); WriteU16(cbdt, 2, 0);
        var (cblc2, cbdt2) = Recompile(cblc, cbdt, out var s1, out var s2);
        Assert.Equal(ReadStatus.Done, s1);
        Assert.Equal(ReadStatus.Done, s2);
        Assert.Equal(cblc, cblc2);
        Assert.Equal(cbdt, cbdt2);
    }

    [Fact]
    public void WriteReadWriteReachesAFixedPoint()
    {
        var (cblc, cbdt) = BuildPair(0, 1, [26, 26]);
        var (cblc2, cbdt2) = Recompile(cblc, cbdt, out var s1, out var s2);
        Assert.Equal(ReadStatus.Done, s1);
        Assert.Equal(ReadStatus.Done, s2);
        var (cblc3, cbdt3) = Recompile(cblc2!, cbdt2!, out _, out _);
        Assert.Equal(cblc2, cblc3);
        Assert.Equal(cbdt2, cbdt3);
    }

    [Fact]
    public void GappedListRegionReemitsCanonicalAndReadsEqual()
    {
        // A list region pushed 40 bytes past the canonical position: the
        // recompile closes the gap (the wire-equivalent tier) and the read
        // answers identical values.
        var (cblc, cbdt) = BuildPair(0, 1, [26, 26]);
        var shifted = new byte[cblc.Length + 40];
        cblc.AsSpan()[..56].CopyTo(shifted);
        cblc.AsSpan()[56..].CopyTo(shifted.AsSpan(96));
        WriteU32(shifted, 8, 96); // the row's listOffset: 56 + 40
        var (cblc2, cbdt2) = Recompile(shifted, cbdt, out var s1, out var s2);
        Assert.Equal(ReadStatus.Done, s1);
        Assert.Equal(ReadStatus.Done, s2);
        Assert.NotEqual(shifted, cblc2);
        Assert.Equal(cblc, cblc2); // the canonical shape is the unshifted one
        Assert.Equal(cbdt, cbdt2);
    }

    [Fact]
    public void UnalignedFormat3SourceReemitsAlignedWithPad()
    {
        // An F3 subtable with an odd entry count placed unaligned and flush
        // (the corpus's index_format3 violation shape): the canonical
        // emission aligns the body and grows the pad element - the tables
        // differ, the reads agree.
        var payload = new byte[17];
        for (int i = 0; i < 17; i++) payload[i] = (byte)(i + 1);
        var image = Image17(16, 16, payload);
        var cbdt = new byte[4 + 2 * image.Length];
        WriteU16(cbdt, 0, 3); WriteU16(cbdt, 2, 0);
        image.CopyTo(cbdt, 4);
        image.CopyTo(cbdt, 4 + image.Length);
        // F3 over [0..1]: n' = 3 (odd), the body flush to the table end and
        // the window walk reaches into the second image.
        var cblc = new byte[8 + 48 + 8 + 8 + 6]; // the 14-byte body flush at 64
        WriteU16(cblc, 0, 3); WriteU16(cblc, 2, 0); WriteU32(cblc, 4, 1);
        WriteU32(cblc, 8, 56); WriteU32(cblc, 12, (uint)(cblc.Length - 56));
        WriteU32(cblc, 16, 1);
        cblc[52] = 16; cblc[53] = 16; cblc[54] = 32; cblc[55] = 1;
        WriteU16(cblc, 56, 0); WriteU16(cblc, 58, 1); WriteU32(cblc, 60, 8);
        WriteU16(cblc, 64, 3); WriteU16(cblc, 66, 17); WriteU32(cblc, 68, 4);
        // F3 entries [0, 26, 52] (n' = 3, odd), body flush to the table end.
        WriteU16(cblc, 72, 0);
        WriteU16(cblc, 74, (ushort)image.Length);
        WriteU16(cblc, 76, (ushort)(2 * image.Length));
        var (cblc2, cbdt2) = Recompile(cblc, cbdt, out var s1, out var s2);
        Assert.Equal(ReadStatus.Done, s1);
        Assert.Equal(ReadStatus.Done, s2);
        Assert.NotEqual(cblc, cblc2);
        Assert.Equal(cbdt, cbdt2);
        // The canonical body aligns to 4 and grows the odd pad element.
        Assert.True(cblc2!.Length >= cblc.Length);
        CblcView.TryOpen(cblc2, out var view, out _, CblcSource);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(cbdt.Length, null, out _));
        Assert.Equal(ReadStatus.Done, view.TryGetGlyphLocation(0, 1, out var location,
            out bool present, out _));
        Assert.True(present);
        Assert.Equal(4UL + (ulong)image.Length, location.PositionInCbdt);
    }

    [Fact]
    public void SameSourceGuardRefusesACountMismatch()
    {
        var (cblc, cbdt) = BuildPair(0, 1, [26, 26]);
        CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource);
        CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource);
        Assert.Equal(ReadStatus.Done,
            cblcView.TryReadTable(cbdt.Length, null, out var cblcModel, out _));
        // A one-image pool: the chain walks two populated slots.
        var lonely = CbdtTableCompiler.TryCreate(3, 0,
            [new CbdtImage(17, new byte[5], new byte[17])], out var cbdtModel, out _);
        Assert.Equal(ReadStatus.Done, lonely);
        Assert.Equal(ReadStatus.InvalidSource,
            CblcTableCompiler.TryCreate(cblcModel!.MajorVersion, cblcModel.MinorVersion,
                cblcModel.Sizes, cbdtModel!, out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.WrongSourceFace, diagnostic.Code);
        Assert.Equal((ulong)2, diagnostic.Detail);
        Assert.Equal((ulong)1, diagnostic.Expected);
    }

    [Fact]
    public void TryCreateRefusesVersionAndInheritedWrites()
    {
        var (cblc, cbdt) = BuildPair(0, 0, [26]);
        CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource);
        CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource);
        Assert.Equal(ReadStatus.Done,
            cblcView.TryReadTable(cbdt.Length, null, out var cblcModel, out _));
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryReadTable(cblcView, null, out var cbdtModel, out _));
        // The version write has no surface: the strict {3} gate re-answers.
        Assert.Equal(ReadStatus.UnsupportedFormat,
            CblcTableCompiler.TryCreate(2, 0, cblcModel!.Sizes, cbdtModel!,
                out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal(ReadStatus.UnsupportedFormat,
            CbdtTableCompiler.TryCreate(2, 0, cbdtModel!.Images, out _, out _));
        // The inherited image format write has no surface: the support face
        // re-answers (T6-08's deferral stays).
        var inherited = new CblcStrike(56, 28, 0, new byte[12], new byte[12], 0, 0,
            16, 16, 32, 1,
            [new CblcOffsetSubtable(0, 1, 1, 4, 0, 0, new uint[] { 0, 13 })]);
        Assert.Equal(ReadStatus.UnsupportedFormat,
            CblcTableCompiler.TryCreate(3, 0, [inherited], cbdtModel!,
                out _, out diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal((ulong)1, diagnostic.Detail);
    }

    [Fact]
    public void TryWithImagePayloadReplacesAndRecomputesFixups()
    {
        var (cblc, cbdt) = BuildPair(0, 1, [26, 26]);
        CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource);
        CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource);
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryReadTable(cblcView, null, out var cbdtModel, out _));
        Assert.Equal(ReadStatus.Done,
            cblcView.TryReadTable(cbdt.Length, null, out var cblcModel, out _));
        // A longer replacement: the pool grows and the second slot's fixups
        // shift; the untouched payload travels verbatim.
        var replacement = new byte[40];
        for (int i = 0; i < 40; i++) replacement[i] = (byte)(0xF0 + (i & 7));
        Assert.Equal(ReadStatus.Done,
            cbdtModel!.TryWithImagePayload(0, replacement, out var edited, out _));
        Assert.Equal(ReadStatus.Done, CbdtTableCompiler.TryCompile(edited!,
            out var cbdtBytes, out _));
        Assert.Equal(4 + 49 + 26, cbdtBytes!.Length);
        // The frame carries the metrics head, the word and the payload: the
        // fresh payload rides at 13, the untouched second image at 53.
        Assert.Equal(replacement, cbdtBytes.AsSpan(13, 40).ToArray());
        Assert.Equal(cbdt.AsSpan(39, 17).ToArray(), cbdtBytes.AsSpan(62, 17).ToArray());
        // The index re-roots against the new frames: the SameSource pairing
        // holds and the windows re-derive.
        Assert.Equal(ReadStatus.Done,
            CblcTableCompiler.TryCreate(cblcModel!.MajorVersion, cblcModel.MinorVersion,
                cblcModel.Sizes, edited!, out var cblcCanon, out _));
        Assert.Equal(ReadStatus.Done,
            CblcTableCompiler.TryCompile(cblcCanon!, out var cblcBytes, out _));
        CblcView.TryOpen(cblcBytes!, out var recompiled, out _, CblcSource);
        Assert.Equal(ReadStatus.Done,
            recompiled.ValidateTable(cbdtBytes!.Length, null, out _));
        CbdtView.TryOpen(cbdtBytes, out var pool, out _, CbdtSource);
        Assert.Equal(ReadStatus.Done,
            pool.TryGetGlyphImage(recompiled, 0, 1, out var image1, out _));
        Assert.Equal(cbdt.AsSpan(30 + 9, 17).ToArray(), image1.Payload);
    }

    [Fact]
    public void TryWithGlyphMetricsRewritesTheHeadAndRefusesTheRest()
    {
        var (cblc, cbdt) = BuildPair(0, 0, [26]);
        CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource);
        CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource);
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryReadTable(cblcView, null, out var cbdtModel, out _));
        var fresh = new byte[] { 20, 20, 3, 4, 5 };
        Assert.Equal(ReadStatus.Done,
            cbdtModel!.TryWithGlyphMetrics(0, fresh, out var edited, out _));
        Assert.Equal(ReadStatus.Done, CbdtTableCompiler.TryCompile(edited!,
            out var cbdtBytes, out _));
        Assert.Equal(fresh, cbdtBytes!.AsSpan(4, 5).ToArray());
        Assert.Equal(cbdt.AsSpan(13, 17).ToArray(), cbdtBytes.AsSpan(13, 17).ToArray());
        // A wrong length and a format without a head refuse.
        Assert.Equal(ReadStatus.InvalidFormat,
            cbdtModel.TryWithGlyphMetrics(0, new byte[8], out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
    }

    [Fact]
    public void TryWithStrikeMetricsRewritesTheBlocks()
    {
        var (cblc, cbdt) = BuildPair(0, 1, [26, 26]);
        CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource);
        Assert.Equal(ReadStatus.Done,
            cblcView.TryReadTable(cbdt.Length, null, out var cblcModel, out _));
        var hori = new byte[12];
        var vert = new byte[12];
        for (int i = 0; i < 12; i++) { hori[i] = (byte)(10 + i); vert[i] = (byte)(20 + i); }
        Assert.Equal(ReadStatus.Done,
            cblcModel!.TryWithStrikeMetrics(0, hori, vert, out var edited, out _));
        Assert.Equal(ReadStatus.Done, CblcTableCompiler.TryCompile(edited!,
            out var cblcBytes, out _));
        Assert.Equal(hori, cblcBytes!.AsSpan(8 + 16, 12).ToArray());
        Assert.Equal(vert, cblcBytes.AsSpan(8 + 28, 12).ToArray());
        // A wrong block length refuses.
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcModel.TryWithStrikeMetrics(0, new byte[11], vert, out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
    }

    [Fact]
    public void TryWithBitmapSizeReanswersTheValueDomains()
    {
        var (cblc, cbdt) = BuildPair(0, 1, [26, 26]);
        CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource);
        Assert.Equal(ReadStatus.Done,
            cblcView.TryReadTable(cbdt.Length, null, out var cblcModel, out _));
        Assert.Equal(ReadStatus.Done,
            cblcModel!.TryWithBitmapSize(0, 32, 32, 8, 1, out var edited, out _));
        Assert.Equal(ReadStatus.Done, CblcTableCompiler.TryCompile(edited!,
            out var cblcBytes, out _));
        Assert.Equal((byte)32, cblcBytes![8 + 44]);
        Assert.Equal((byte)8, cblcBytes[8 + 46]);
        // The domains refuse the read-side shapes.
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcModel.TryWithBitmapSize(0, 16, 16, 24, 1, out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcModel.TryWithBitmapSize(0, 16, 16, 32, unchecked((sbyte)0x08), out _,
                out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Flags, diagnostic.Location.Field);
    }

    [Fact]
    public void ShortDestinationLeavesTheSpanUntouched()
    {
        var (cblc, cbdt) = BuildPair(0, 1, [26, 26]);
        CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource);
        CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource);
        Assert.Equal(ReadStatus.Done,
            cblcView.TryReadTable(cbdt.Length, null, out var cblcModel, out _));
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryReadTable(cblcView, null, out var cbdtModel, out _));
        Assert.Equal(ReadStatus.Done,
            CblcTableCompiler.TryCreate(cblcModel!.MajorVersion, cblcModel.MinorVersion,
                cblcModel.Sizes, cbdtModel!, out var cblcCanon, out _));
        var destination = new byte[cblc.Length - 1];
        Assert.Equal(ReadStatus.DestinationTooSmall,
            CblcTableCompiler.TryWrite(cblcCanon!, destination, out _));
        Assert.All(destination, b => Assert.Equal(0, b));
    }
}
