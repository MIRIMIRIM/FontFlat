using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-05 commit 2's read faces against synthetic wires: the
/// strict version gates (the 2.0 witness pool's expected refusal, the minor
/// face), the row value domains, the record ordering must + overlap refusal,
/// the format faces (index closed set, the image format three-way face, the
/// pairing matrix), the window derivations over all five index formats, the
/// CBDT data windows against the explicit cbdtDataLength, the numSizes==0
/// shell and the optional numGlyphs domain (null skip vs supplied refusal),
/// then the joint glyph-image read with both bounded views and the lying
/// dataLen refusal.</summary>
public sealed class CbdtCblcReadTests
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

    /// <summary>One format-17 image: small metrics + dataLen + payload.</summary>
    private static byte[] Image17(byte height, byte width, ReadOnlySpan<byte> payload)
    {
        var image = new byte[9 + payload.Length];
        image[0] = height; image[1] = width;
        image[2] = unchecked((byte)-1); image[3] = 2; image[4] = 3;
        WriteU32(image, 5, (uint)payload.Length);
        payload.CopyTo(image.AsSpan(9));
        return image;
    }

    /// <summary>One pool image of the given format over the same four opaque
    /// payload bytes: the format's metrics head plus the counted length.</summary>
    private static byte[] PoolImage(ushort imageFormat)
    {
        var payload = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        int head = imageFormat switch
        {
            17 => 9, // small metrics + dataLen
            18 => 12, // big metrics + dataLen
            _ => 4, // 19: dataLen only
        };
        var image = new byte[head + payload.Length];
        if (imageFormat == 17)
        {
            image[0] = 16; image[1] = 16;
            image[2] = 0xFF; image[3] = 2; image[4] = 3;
        }
        else if (imageFormat == 18)
        {
            for (int i = 0; i < 8; i++) image[i] = (byte)(10 + i);
        }
        WriteU32(image, head - 4, (uint)payload.Length);
        payload.CopyTo(image.AsSpan(head));
        return image;
    }

    /// <summary>A minimal pair: one strike over [firstGlyph..lastGlyph] with a
    /// single index-format-1 subtable whose offsets walk a pool of consecutive
    /// format-17 images (one per populated slot; a zero slot length emits the
    /// equal-consecutive absent form). The pool base is 4, so the first
    /// sbitOffset is 0.</summary>
    private static (byte[] cblc, byte[] cbdt) BuildPair(ushort major, ushort minor,
        ushort firstGlyph, ushort lastGlyph, int[] slotLengths, uint colorRef = 0,
        sbyte flags = 1, byte bitDepth = 32)
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
        WriteU16(cbdt, 0, major);
        WriteU16(cbdt, 2, minor);
        images.CopyTo(cbdt, 4);
        // The CBLC: header + row + record + F1 body.
        int listAt = 8 + 48;
        var cblc = new byte[listAt + 8 + 8 + 4 * (span + 1)];
        WriteU16(cblc, 0, major);
        WriteU16(cblc, 2, minor);
        WriteU32(cblc, 4, 1);
        var row = new byte[48];
        WriteU32(row, 0, (uint)listAt);
        WriteU32(row, 4, (uint)(cblc.Length - listAt));
        WriteU32(row, 8, 1);
        WriteU32(row, 12, colorRef);
        row[16] = unchecked((byte)-12); row[18] = 18; // a couple of raw metrics bytes
        WriteU16(row, 40, firstGlyph);
        WriteU16(row, 42, lastGlyph);
        row[44] = 16; row[45] = 16; row[46] = bitDepth;
        row[47] = unchecked((byte)flags);
        row.CopyTo(cblc, 8);
        WriteU16(cblc, listAt, firstGlyph);
        WriteU16(cblc, listAt + 2, lastGlyph);
        WriteU32(cblc, listAt + 4, 8); // the subtable header right after the record
        WriteU16(cblc, listAt + 8, 1); // index format 1
        WriteU16(cblc, listAt + 10, 17); // image format 17
        WriteU32(cblc, listAt + 12, 4); // imageDataOffset: the pool base
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

    /// <summary>A one-glyph pair whose single subtable carries the given
    /// (indexFormat, imageFormat) cell with a body of that format's own shape
    /// over one slot sized to the pool image (the matrix theories' base).</summary>
    private static (byte[] cblc, byte[] cbdt) BuildMatrixPair(ushort indexFormat,
        ushort imageFormat)
    {
        var image = PoolImage(imageFormat);
        var cbdt = new byte[4 + image.Length];
        WriteU16(cbdt, 0, 3); WriteU16(cbdt, 2, 0);
        image.CopyTo(cbdt, 4);
        int listAt = 8 + 48;
        int bodyLength = indexFormat switch
        {
            1 => 8 + 8, // header + offsets[2] u32
            2 => 8 + 4 + 8, // header + imageSize + big metrics
            3 => 8 + 4, // header + offsets[2] u16
            4 => 8 + 4 + 8, // header + numGlyphs + two pairs
            _ => 8 + 4 + 8 + 4 + 2, // 5: header + imageSize + metrics + count + gid
        };
        var cblc = new byte[listAt + 8 + bodyLength];
        WriteU16(cblc, 0, 3); WriteU16(cblc, 2, 0); WriteU32(cblc, 4, 1);
        var row = new byte[48];
        WriteU32(row, 0, (uint)listAt);
        WriteU32(row, 4, (uint)(cblc.Length - listAt));
        WriteU32(row, 8, 1);
        WriteU16(row, 40, 0); WriteU16(row, 42, 0);
        row[44] = 16; row[45] = 16; row[46] = 32; row[47] = 1;
        row.CopyTo(cblc, 8);
        int headerAt = listAt + 8;
        WriteU16(cblc, listAt, 0); WriteU16(cblc, listAt + 2, 0);
        WriteU32(cblc, listAt + 4, 8);
        WriteU16(cblc, headerAt, indexFormat);
        WriteU16(cblc, headerAt + 2, imageFormat);
        WriteU32(cblc, headerAt + 4, 4); // the pool base
        int slot = image.Length;
        switch (indexFormat)
        {
            case 1:
                WriteU32(cblc, headerAt + 8, 0);
                WriteU32(cblc, headerAt + 12, (uint)slot);
                break;
            case 2:
                WriteU32(cblc, headerAt + 8, (uint)slot);
                for (int i = 0; i < 8; i++) cblc[headerAt + 12 + i] = (byte)(10 + i);
                break;
            case 3:
                WriteU16(cblc, headerAt + 8, 0);
                WriteU16(cblc, headerAt + 10, (ushort)slot);
                break;
            case 4:
                WriteU32(cblc, headerAt + 8, 1);
                WriteU16(cblc, headerAt + 12, 0); WriteU16(cblc, headerAt + 14, 0);
                WriteU16(cblc, headerAt + 16, 0xFFFF); WriteU16(cblc, headerAt + 18, (ushort)slot);
                break;
            default: // 5
                WriteU32(cblc, headerAt + 8, (uint)slot);
                for (int i = 0; i < 8; i++) cblc[headerAt + 12 + i] = (byte)(10 + i);
                WriteU32(cblc, headerAt + 20, 1);
                WriteU16(cblc, headerAt + 24, 0);
                break;
        }
        return (cblc, cbdt);
    }

    // ---- version gates ----

    [Fact]
    public void Major2RefusesAsUnsupportedVersion()
    {
        var (cblc, cbdt) = BuildPair(2, 0, 0, 1, [26, 26]);
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        // The 2.0 corpus hosts are the expected typed-refusal witness pool
        // (OQ-T6-05-1's strict {3}).
        Assert.Equal(ReadStatus.UnsupportedFormat,
            cbdtView.ValidateTable(out var diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal((ulong)2, diagnostic.Detail);
        Assert.Equal((ulong)3, diagnostic.Expected);
        Assert.Equal(ReadStatus.UnsupportedFormat,
            cblcView.ValidateTable(cbdt.Length, null, out diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal((ulong)3, diagnostic.Expected);
    }

    [Fact]
    public void MinorNonZeroRefusesOnBothTables()
    {
        var (cblc, cbdt) = BuildPair(3, 1, 0, 1, [26, 26]);
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat, cbdtView.ValidateTable(out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal((ulong)1, diagnostic.Detail);
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(cbdt.Length, null, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
    }

    // ---- the happy 3.0 path and the joint read ----

    [Fact]
    public void MinimalPairValidatesAndResolvesGlyphImages()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26]);
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done, cbdtView.ValidateTable(out _));
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 0, out var image0, out diagnostic));
        Assert.Equal((ushort)17, image0.ImageFormat);
        Assert.Equal(5, image0.Metrics!.Length);
        Assert.Equal(17, image0.Payload.Length); // 26 - 9
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 1, out var image1, out _));
        Assert.Equal(17, image1.Payload.Length);
        // Glyph 2 is outside the strike's range: data absence, not corruption.
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 2, out var absent, out _));
        Assert.Empty(absent.Payload);
    }

    [Fact]
    public void EqualConsecutiveOffsetsAreTheAbsentForm()
    {
        // Slot 0 absent (zero length => equal pair), slot 1 populated.
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [0, 26]);
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out _));
        Assert.Equal(ReadStatus.Done,
            cblcView.TryGetGlyphLocation(0, 0, out _, out bool present0, out _));
        Assert.False(present0);
        Assert.Equal(ReadStatus.Done,
            cblcView.TryGetGlyphLocation(0, 1, out var location, out bool present1, out _));
        Assert.True(present1);
        // The one populated slot rides at the pool base (4) and ends at the
        // single image's end (30).
        Assert.Equal(30UL, location.PositionInCbdt + location.ByteLength);
    }

    [Fact]
    public void FirstElementSbitOffsetZeroIsLegal()
    {
        // The first entry of every corpus subtable is sbitOffset 0: the pool
        // base (imageDataOffset 4 here) plus offset 0 - a populated slot's
        // start value, never a NULL mark (OQ-11's observation ruling).
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26]);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out _));
        Assert.Equal(ReadStatus.Done,
            cblcView.TryGetGlyphLocation(0, 0, out var location, out bool present, out _));
        Assert.True(present);
        Assert.Equal(4UL, location.PositionInCbdt);
        Assert.Equal(0u, CblcOffsetSubtable0(cblc));
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 0, out var image, out _));
        Assert.Equal(17, image.Payload.Length);
    }

    /// <summary>Reads the wire's first sbitOffset entry back (the F1 body's
    /// first u32 after the subtable header at 64).</summary>
    private static uint CblcOffsetSubtable0(byte[] cblc) =>
        (uint)((cblc[72] << 24) | (cblc[73] << 16) | (cblc[74] << 8) | cblc[75]);

    // ---- the row value domains ----

    [Fact]
    public void ColorRefNonZeroRefuses()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26], colorRef: 7);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal((ulong)7, diagnostic.Detail);
    }

    [Fact]
    public void FlagsReservedBitsRefuseAndNegativeByteIsTheSameFace()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26], flags: unchecked((sbyte)0x84));
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Flags, diagnostic.Location.Field);
    }

    [Fact]
    public void BitDepthOutsideTheSetRefuses()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26], bitDepth: 24);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal((ulong)24, diagnostic.Detail);
    }

    [Fact]
    public void StartBeyondEndRefuses()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 2, 1, [26, 26]);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.GlyphId, diagnostic.Location.Field);
    }

    // ---- the numGlyphs domain (OQ-3's candidate B) ----

    [Fact]
    public void NumGlyphsNullSkipsAndSuppliedValueRefusesTheCorpusWitness()
    {
        // endGlyphIndex 5 with maxp numGlyphs 4 - the subset-tool witness
        // (harfbuzz NotoColorEmoji.cmap.38,AE,2049's shape).
        var (cblc, cbdt) = BuildPair(3, 0, 4, 5, [26, 26]);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out _));
        Assert.Equal(ReadStatus.InvalidIndex,
            cblcView.ValidateTable(cbdt.Length, 4, out var diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal((ulong)5, diagnostic.Detail);
        Assert.Equal((ulong)4, diagnostic.Expected);
    }

    [Fact]
    public void SparseGlyphIdAtNumGlyphsRefusesWhenSupplied()
    {
        // A format-4 subtable listing glyph 3 with numGlyphs 2 supplied.
        var image = Image17(16, 16, [1, 2, 3, 4, 5, 6, 7, 8]);
        var cbdt = new byte[4 + image.Length];
        WriteU16(cbdt, 0, 3); WriteU16(cbdt, 2, 0);
        image.CopyTo(cbdt, 4);
        var cblc = new byte[8 + 48 + 8 + 20];
        WriteU16(cblc, 0, 3); WriteU16(cblc, 2, 0); WriteU32(cblc, 4, 1);
        WriteU32(cblc, 8, 56); WriteU32(cblc, 12, (uint)(cblc.Length - 56));
        WriteU32(cblc, 16, 1);
        cblc[52] = 16; cblc[53] = 16; cblc[54] = 32; cblc[55] = 1;
        WriteU16(cblc, 8 + 40, 3); WriteU16(cblc, 8 + 42, 3); // record range 3..3
        WriteU16(cblc, 56, 3); WriteU16(cblc, 58, 3); WriteU32(cblc, 60, 8);
        WriteU16(cblc, 64, 4); WriteU16(cblc, 66, 17); WriteU32(cblc, 68, 4);
        WriteU32(cblc, 72, 1); // numGlyphs: one pair + sentinel
        WriteU16(cblc, 76, 3); WriteU16(cblc, 78, 0); // glyph 3 at pool offset 0
        WriteU16(cblc, 80, 0xFFFF); WriteU16(cblc, 82, (ushort)image.Length);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done, cblcView.ValidateTable(cbdt.Length, null, out _));
        Assert.Equal(ReadStatus.InvalidIndex,
            cblcView.ValidateTable(cbdt.Length, 2, out var diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal((ulong)3, diagnostic.Detail);
    }

    // ---- the format faces ----

    [Fact]
    public void UnknownIndexFormatRefuses()
    {
        var (cblc, cbdt) = BuildMatrixPair(6, 17);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.UnsupportedFormat,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal((ulong)6, diagnostic.Detail);
    }

    [Theory]
    [InlineData(1)] // inherited: T6-08's known formats
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(3)] // never supported (EBDT obsolete)
    [InlineData(4)] // never supported (Apple-only)
    [InlineData(42)] // unknown
    public void NonTypedImageFormatsRefuseUnderTheSameFace(ushort imageFormat)
    {
        var (cblc, cbdt) = BuildMatrixPair(1, imageFormat);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.UnsupportedFormat,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(imageFormat, (ushort)diagnostic.Detail);
    }

    [Theory]
    [InlineData(1, 19)] // 19 over a variable-metrics format: metrics unreadable
    [InlineData(3, 19)]
    [InlineData(4, 19)]
    [InlineData(2, 17)] // 17 over a constant-imageSize format: metrics double-set
    [InlineData(2, 18)]
    [InlineData(5, 17)]
    [InlineData(5, 18)]
    public void PairingMatrixRefusesTheIllegalCells(ushort indexFormat, ushort imageFormat)
    {
        var (cblc, cbdt) = BuildMatrixPair(indexFormat, imageFormat);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(imageFormat, (ushort)diagnostic.Detail);
        Assert.Equal(0UL, diagnostic.Expected);
    }

    [Theory]
    [InlineData(1, 17)]
    [InlineData(3, 17)]
    [InlineData(4, 17)]
    [InlineData(1, 18)]
    [InlineData(3, 18)]
    [InlineData(4, 18)]
    [InlineData(2, 19)]
    [InlineData(5, 19)]
    public void PairingMatrixAcceptsTheLegalCells(ushort indexFormat, ushort imageFormat)
    {
        var (cblc, cbdt) = BuildMatrixPair(indexFormat, imageFormat);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        // The legal pairing validates clean: no face refuses.
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 0, out var image0, out diagnostic));
        Assert.Equal(imageFormat, image0.ImageFormat);
        Assert.Equal(4, image0.Payload.Length);
    }

    // ---- the records layer ----

    /// <summary>A two-record pair: two F1 subtables over the given ranges with
    /// a pool of four format-17 images.</summary>
    private static (byte[] cblc, byte[] cbdt) BuildTwoRecordPair(ushort first0, ushort last0,
        ushort first1, ushort last1)
    {
        var images = new List<byte>();
        for (int k = 0; k < 4; k++) images.AddRange(Image17(16, 16, [0x89, 0x50, 0x4E, 0x47]));
        var cbdt = new byte[4 + images.Count];
        WriteU16(cbdt, 0, 3); WriteU16(cbdt, 2, 0);
        images.CopyTo(cbdt, 4);
        int listAt = 8 + 48;
        int subtablesAt = listAt + 16; // two records
        var cblc = new byte[subtablesAt + 2 * (8 + 4 * 5)];
        WriteU16(cblc, 0, 3); WriteU16(cblc, 2, 0); WriteU32(cblc, 4, 1);
        var row = new byte[48];
        WriteU32(row, 0, (uint)listAt);
        WriteU32(row, 4, (uint)(cblc.Length - listAt));
        WriteU32(row, 8, 2);
        WriteU16(row, 40, 0); WriteU16(row, 42, 3);
        row[44] = 16; row[45] = 16; row[46] = 32; row[47] = 1;
        row.CopyTo(cblc, 8);
        WriteU16(cblc, listAt, first0);
        WriteU16(cblc, listAt + 2, last0);
        WriteU32(cblc, listAt + 4, 16); // subtable 0 right after the records
        WriteU16(cblc, listAt + 8, first1);
        WriteU16(cblc, listAt + 10, last1);
        WriteU32(cblc, listAt + 12,
            (uint)((subtablesAt - listAt) + (8 + 4 * (last0 - first0 + 2))));
        for (int r = 0; r < 2; r++)
        {
            ushort first = r == 0 ? first0 : first1;
            ushort last = r == 0 ? last0 : last1;
            int at = subtablesAt + r * (8 + 4 * (last0 - first0 + 2));
            WriteU16(cblc, at, 1); WriteU16(cblc, at + 2, 17);
            WriteU32(cblc, at + 4, 4);
            uint running = 0;
            int entries = last - first + 2;
            for (int k = 0; k < entries - 1; k++)
            {
                WriteU32(cblc, at + 8 + 4 * k, running);
                running += 13; // one 13-byte format-17 image per slot
            }
            WriteU32(cblc, at + 8 + 4 * (entries - 1), running);
        }
        return (cblc, cbdt);
    }

    [Fact]
    public void UnsortedRecordsRefuseAtTheLaterRecord()
    {
        // Two records over 0..1 and 2..3; raise record 0's range to 4..5 so
        // the later first (2) lands below it (the E L183 must face).
        var (cblc, cbdt) = BuildTwoRecordPair(0, 1, 2, 3);
        WriteU16(cblc, 56, 4);
        WriteU16(cblc, 58, 5);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal((ulong)2, diagnostic.Detail);
        Assert.Equal((ulong)4, diagnostic.Expected);
    }

    [Fact]
    public void OverlappingRecordsRefuse()
    {
        // Record 0 covers 0..2, record 1 starts at 2: the OQ-4 lean's overlap face.
        var (cblc, cbdt) = BuildTwoRecordPair(0, 2, 2, 3);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.OverlappingRange, diagnostic.Code);
    }

    // ---- the shell and windows ----

    [Fact]
    public void NumSizesZeroShellValidatesAndReadsAsEmpty()
    {
        var cblc = new byte[8];
        WriteU16(cblc, 0, 3); WriteU16(cblc, 2, 0); WriteU32(cblc, 4, 0);
        var cbdt = new byte[4];
        WriteU16(cbdt, 0, 3); WriteU16(cbdt, 2, 0);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out _));
        Assert.Equal(ReadStatus.Done, cbdtView.ValidateTable(out _));
        Assert.Equal(ReadStatus.Done,
            cblcView.TryReadTable(cbdt.Length, null, out var cblcModel, out _));
        Assert.Equal(0, cblcModel!.SizeCount);
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryReadTable(cblcView, null, out var cbdtModel, out _));
        Assert.Equal(0, cbdtModel!.ImageCount);
    }

    [Fact]
    public void ListRegionBeyondTheTableRefuses()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26]);
        WriteU32(cblc, 8 + 4, (uint)cblc.Length + 40); // listSize past the end
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Truncated,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
    }

    [Fact]
    public void DataWindowBeyondCbdtDataLengthRefuses()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26]);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidOffset,
            cblcView.ValidateTable(cbdt.Length - 1, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out _));
    }

    [Fact]
    public void SubtableBodyBeyondTheListRegionRefuses()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26]);
        // Shrink the declared list region below the subtable body.
        WriteU32(cblc, 8 + 4, 8); // listSize = the record only
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidOffset,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
    }

    // ---- the index format bodies ----

    [Fact]
    public void Format2SubtablePlacesConstantSlotsAndPairsWith19()
    {
        var (cblc, cbdt) = BuildMatrixPair(2, 19);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(ReadStatus.Done,
            cblcView.TryGetIndexSubtable(0, 0, out var subtable, out _));
        var f2 = Assert.IsType<CblcImageSizeSubtable>(subtable);
        Assert.Equal(8u, f2.ImageSize); // 4 dataLen + 4 payload
        Assert.Equal(8, f2.BigMetrics.Length);
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 0, out var image, out _));
        Assert.Equal((ushort)19, image.ImageFormat);
        Assert.Null(image.Metrics);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, image.Payload);
    }

    [Fact]
    public void Format5SparseGlyphArrayOrdersAndResolves()
    {
        // F5 over gids {1, 3}: ascending; gid 2 absent.
        var image = PoolImage(19);
        var cbdt = new byte[4 + 2 * image.Length];
        WriteU16(cbdt, 0, 3); WriteU16(cbdt, 2, 0);
        image.CopyTo(cbdt, 4);
        image.CopyTo(cbdt, 4 + image.Length);
        var cblc = new byte[8 + 48 + 8 + 28];
        WriteU16(cblc, 0, 3); WriteU16(cblc, 2, 0); WriteU32(cblc, 4, 1);
        WriteU32(cblc, 8, 56); WriteU32(cblc, 12, (uint)(cblc.Length - 56));
        WriteU32(cblc, 16, 1);
        cblc[52] = 16; cblc[53] = 16; cblc[54] = 32; cblc[55] = 1;
        WriteU16(cblc, 8 + 40, 1); WriteU16(cblc, 8 + 42, 3);
        WriteU16(cblc, 56, 1); WriteU16(cblc, 58, 3); WriteU32(cblc, 60, 8);
        WriteU16(cblc, 64, 5); WriteU16(cblc, 66, 19); WriteU32(cblc, 68, 4);
        WriteU32(cblc, 72, (uint)image.Length);
        for (int i = 0; i < 8; i++) cblc[76 + i] = (byte)(10 + i);
        WriteU32(cblc, 84, 2); // numGlyphs
        WriteU16(cblc, 88, 1); WriteU16(cblc, 90, 3);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 3, out var image1, out _));
        Assert.Equal((ushort)19, image1.ImageFormat);
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 2, out var absent, out _));
        Assert.Empty(absent.Payload);
        // Descending the array refuses (E L258).
        WriteU16(cblc, 88, 3); WriteU16(cblc, 90, 1);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(cbdt.Length, null, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
    }

    [Fact]
    public void Format4SparsePairsOrderAndResolve()
    {
        // F4 over gids {0, 2}: the sentinel sizes the second pair.
        var image = PoolImage(17);
        var cbdt = new byte[4 + 2 * image.Length];
        WriteU16(cbdt, 0, 3); WriteU16(cbdt, 2, 0);
        image.CopyTo(cbdt, 4);
        image.CopyTo(cbdt, 4 + image.Length);
        var cblc = new byte[8 + 48 + 8 + 12 + 12];
        WriteU16(cblc, 0, 3); WriteU16(cblc, 2, 0); WriteU32(cblc, 4, 1);
        WriteU32(cblc, 8, 56); WriteU32(cblc, 12, (uint)(cblc.Length - 56));
        WriteU32(cblc, 16, 1);
        cblc[52] = 16; cblc[53] = 16; cblc[54] = 32; cblc[55] = 1;
        WriteU16(cblc, 8 + 40, 0); WriteU16(cblc, 8 + 42, 2);
        WriteU16(cblc, 56, 0); WriteU16(cblc, 58, 2); WriteU32(cblc, 60, 8);
        WriteU16(cblc, 64, 4); WriteU16(cblc, 66, 17); WriteU32(cblc, 68, 4);
        WriteU32(cblc, 72, 2); // numGlyphs pairs + sentinel
        WriteU16(cblc, 76, 0); WriteU16(cblc, 78, 0);
        WriteU16(cblc, 80, 2); WriteU16(cblc, 82, (ushort)image.Length);
        WriteU16(cblc, 84, 0xFFFF); WriteU16(cblc, 86, (ushort)(2 * image.Length));
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 0, out var image0, out _));
        Assert.Equal(4, image0.Payload.Length);
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 1, out var absent, out _));
        Assert.Empty(absent.Payload);
        // Descending gids refuse (the E L262 order premise).
        WriteU16(cblc, 76, 2); WriteU16(cblc, 80, 0);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(cbdt.Length, null, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
    }

    [Fact]
    public void Format3Offset16ArrayWalksLikeFormat1()
    {
        // The F3 body: the same walk with u16 elements.
        var image = Image17(16, 16, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            1, 2, 3, 4, 5, 6, 7, 8, 9]);
        var cbdt = new byte[4 + 2 * image.Length];
        WriteU16(cbdt, 0, 3); WriteU16(cbdt, 2, 0);
        image.CopyTo(cbdt, 4);
        image.CopyTo(cbdt, 4 + image.Length);
        var cblc = new byte[8 + 48 + 8 + 8 + 6];
        WriteU16(cblc, 0, 3); WriteU16(cblc, 2, 0); WriteU32(cblc, 4, 1);
        WriteU32(cblc, 8, 56); WriteU32(cblc, 12, (uint)(cblc.Length - 56));
        WriteU32(cblc, 16, 1);
        cblc[52] = 16; cblc[53] = 16; cblc[54] = 32; cblc[55] = 1;
        WriteU16(cblc, 8 + 40, 0); WriteU16(cblc, 8 + 42, 1);
        WriteU16(cblc, 56, 0); WriteU16(cblc, 58, 1); WriteU32(cblc, 60, 8);
        WriteU16(cblc, 64, 3); WriteU16(cblc, 66, 17); WriteU32(cblc, 68, 4);
        WriteU16(cblc, 72, 0); WriteU16(cblc, 74, (ushort)image.Length);
        WriteU16(cblc, 76, (ushort)(2 * image.Length));
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done,
            cblcView.ValidateTable(cbdt.Length, null, out var diagnostic));
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryGetGlyphImage(cblcView, 0, 1, out var image1, out _));
        Assert.Equal(image.AsSpan(9).ToArray(), image1.Payload);
    }

    // ---- materialization ----

    [Fact]
    public void TryReadTableMaterializesRowsAndSubtables()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26]);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done,
            cblcView.TryReadTable(cbdt.Length, null, out var model, out _));
        Assert.NotNull(model);
        Assert.Equal((ushort)3, model!.MajorVersion);
        Assert.Equal(1, model.SizeCount);
        var strike = model.Sizes[0];
        Assert.Equal(12, strike.HoriMetrics.Length);
        Assert.Equal((sbyte)-12, unchecked((sbyte)strike.HoriMetrics[0]));
        Assert.True(strike.IsColorStrike);
        var f1 = Assert.IsType<CblcOffsetSubtable>(strike.Subtables[0]);
        Assert.Equal(3, f1.SbitOffsets.Length); // span 2 + sentinel
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done,
            cbdtView.TryReadTable(cblcView, null, out var cbdtModel, out _));
        Assert.NotNull(cbdtModel);
        Assert.Equal(2, cbdtModel!.ImageCount);
        Assert.Equal((ushort)17, cbdtModel.Images[0].ImageFormat);
        Assert.Equal(5, cbdtModel.Images[0].Metrics!.Length);
        Assert.Equal(17, cbdtModel.Images[0].Payload.Length);
    }

    [Fact]
    public void JointReadRefusesALyingDataLength()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26]);
        // The first image's dataLen claims more than its CBLC-derived slot
        // carries (the pool still holds the second image, so the slot face is
        // the refuser).
        WriteU32(cbdt, 4 + 5, 30);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(cbdt, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Done, cblcView.ValidateTable(cbdt.Length, null, out _));
        Assert.Equal(ReadStatus.InvalidOffset,
            cbdtView.TryGetGlyphImage(cblcView, 0, 0, out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
    }

    [Fact]
    public void JointReadRefusesAPoolTruncation()
    {
        var (cblc, cbdt) = BuildPair(3, 0, 0, 1, [26, 26]);
        var truncated = cbdt[..^10]; // the last image loses its tail
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.Done, CbdtView.TryOpen(truncated, out var cbdtView, out _, CbdtSource));
        Assert.Equal(ReadStatus.Truncated,
            cbdtView.TryGetGlyphImage(cblcView, 0, 1, out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
    }

    [Fact]
    public void NegativeCbdtDataLengthRefusesAtTheInputContract()
    {
        var (cblc, _) = BuildPair(3, 0, 0, 0, [26]);
        Assert.Equal(ReadStatus.Done, CblcView.TryOpen(cblc, out var cblcView, out _, CblcSource));
        Assert.Equal(ReadStatus.InvalidFormat,
            cblcView.ValidateTable(-1, null, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.InputBytes, diagnostic.Location.Field);
    }
}
