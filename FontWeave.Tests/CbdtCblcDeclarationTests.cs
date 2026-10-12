using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-05 commit 1's declared faces: the CBLC 8-byte header (the
/// major/minor u16 pair and the u32 numSizes, OQ-T6-05-1's strict {3} version
/// field) over the counted 48-byte BitmapSize composite array (the NameRecord
/// generated-composite line's second exercise, with the two SbitLineMetrics
/// blocks riding the generated 12×1-byte element runs), and the CBDT 4-byte
/// version head over the unstructured pool (zero declared pool structure, the
/// card §2 line). Every assertion is against the public generated accessors,
/// so the OQ-T6-05-15 precheck (exactly the two new generated views, zero
/// generator gaps) is exercised here, not just declared.</summary>
public sealed class CbdtCblcDeclarationTests
{
    private const uint CbdtTag = 0x43424454;
    private const uint CblcTag = 0x43424C43;
    private static readonly DataLocation CbdtSource = new(9, 0, CbdtTag, -1,
        DataField.None, OffsetBasis.Table, 0);
    private static readonly DataLocation CblcSource = new(9, 0, CblcTag, -1,
        DataField.None, OffsetBasis.Table, 0);

    private static byte[] CbdtHeader(ushort major, ushort minor)
    {
        var wire = new byte[4];
        WriteU16(wire, 0, major);
        WriteU16(wire, 2, minor);
        return wire;
    }

    private static byte[] CblcHeader(ushort major, ushort minor, uint numSizes)
    {
        var wire = new byte[8];
        WriteU16(wire, 0, major);
        WriteU16(wire, 2, minor);
        WriteU32(wire, 4, numSizes);
        return wire;
    }

    /// <summary>One BitmapSize row's wire: the offsets/sizes/count/colorRef
    /// words, two 12-byte raw metrics blocks, the glyph range, ppem/depth and
    /// the int8 flags word.</summary>
    private static byte[] BitmapSizeRow(uint listOffset, uint listSize, uint numSubtables,
        uint colorRef, ushort startGlyph, ushort endGlyph, byte ppemX, byte ppemY,
        byte bitDepth, sbyte flags)
    {
        var row = new byte[48];
        WriteU32(row, 0, listOffset);
        WriteU32(row, 4, listSize);
        WriteU32(row, 8, numSubtables);
        WriteU32(row, 12, colorRef);
        // The hori block at 16: ascender -12, descender -3, widthMax 18, the
        // caret words 1/2/0, the sidebearing words -2/-1/1/-1, pads 0.
        row[16] = unchecked((byte)-12); row[17] = unchecked((byte)-3);
        row[18] = 18; row[19] = 1; row[20] = 2; row[21] = 0;
        row[22] = unchecked((byte)-2); row[23] = unchecked((byte)-1);
        row[24] = 1; row[25] = unchecked((byte)-1); row[26] = 0; row[27] = 0;
        // The vert block at 28: the same raw shape.
        row[28] = unchecked((byte)-12); row[29] = unchecked((byte)-3);
        row[30] = 18; row[31] = 1; row[32] = 2; row[33] = 0;
        row[34] = unchecked((byte)-2); row[35] = unchecked((byte)-1);
        row[36] = 1; row[37] = unchecked((byte)-1); row[38] = 0; row[39] = 0;
        WriteU16(row, 40, startGlyph);
        WriteU16(row, 42, endGlyph);
        row[44] = ppemX; row[45] = ppemY; row[46] = bitDepth;
        row[47] = unchecked((byte)flags);
        return row;
    }

    private static void WriteU16(byte[] wire, int at, ushort value)
    {
        wire[at] = (byte)(value >> 8);
        wire[at + 1] = (byte)value;
    }

    private static void WriteU32(byte[] wire, int at, uint value)
    {
        WriteU16(wire, at, (ushort)(value >> 16));
        WriteU16(wire, at + 2, (ushort)value);
    }

    // ---- CBDT declaration face ----

    [Fact]
    public void CbdtHeaderScalarsReadAtTheirFixedPositions()
    {
        ReadStatus status = CbdtView.TryOpen(CbdtHeader(3, 0), out var view,
            out var diagnostic, CbdtSource);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetMajorVersion(out ushort major, out diagnostic));
        Assert.Equal((ushort)3, major);
        Assert.Equal(ReadStatus.Done, view.TryGetMinorVersion(out ushort minor, out diagnostic));
        Assert.Equal((ushort)0, minor);
    }

    [Fact]
    public void CbdtShortHeaderRefusesAtTheFirstIncompleteSlot()
    {
        ReadStatus status = CbdtView.TryOpen(CbdtHeader(3, 0)[..3], out _,
            out var diagnostic, CbdtSource);
        Assert.Equal(ReadStatus.Truncated, status);
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(2uL, diagnostic.Location.Offset);
        Assert.Equal(4uL, diagnostic.Expected);
    }

    // ---- CBLC declaration face ----

    [Fact]
    public void CblcHeaderScalarsReadAtTheirFixedPositions()
    {
        var wire = CblcHeader(3, 0, 0);
        ReadStatus status = CblcView.TryOpen(wire, out var view, out var diagnostic,
            CblcSource);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetMajorVersion(out ushort major, out diagnostic));
        Assert.Equal((ushort)3, major);
        Assert.Equal(ReadStatus.Done, view.TryGetMinorVersion(out ushort minor, out diagnostic));
        Assert.Equal((ushort)0, minor);
        Assert.Equal(ReadStatus.Done, view.TryGetNumSizes(out uint numSizes, out diagnostic));
        Assert.Equal(0u, numSizes);
    }

    [Fact]
    public void CblcShortHeaderRefusesAtTheFirstIncompleteSlot()
    {
        // The 8-byte header is the open minimum; a 7-byte wire names the
        // numSizes word at 4..8 as the first incomplete value.
        ReadStatus status = CblcView.TryOpen(CblcHeader(3, 0, 0)[..7], out _,
            out var diagnostic, CblcSource);
        Assert.Equal(ReadStatus.Truncated, status);
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Count, diagnostic.Location.Field);
        Assert.Equal(4uL, diagnostic.Location.Offset);
        Assert.Equal(8uL, diagnostic.Expected);
    }

    [Fact]
    public void CblcBitmapSizeRowProjectsEveryDeclaredField()
    {
        var wire = CblcHeader(3, 0, 1);
        Array.Resize(ref wire, 8 + 48);
        BitmapSizeRow(56, 36, 2, 0, 0, 3, 16, 16, 32, 1).CopyTo(wire, 8);
        ReadStatus status = CblcView.TryOpen(wire, out var view, out var diagnostic,
            CblcSource);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetBitmapSize(0, out var row, out diagnostic));
        Assert.Equal(0, row.Ordinal);
        Assert.Equal(56u, row.IndexSubtableListOffset);
        Assert.Equal(36u, row.IndexSubtableListSize);
        Assert.Equal(2u, row.NumIndexSubtables);
        Assert.Equal(0u, row.ColorRef);
        Assert.Equal((sbyte)-12, row.HoriAscender);
        Assert.Equal((sbyte)-3, row.HoriDescender);
        Assert.Equal((byte)18, row.HoriWidthMax);
        Assert.Equal((sbyte)1, row.HoriCaretSlopeNumerator);
        Assert.Equal((sbyte)2, row.HoriCaretSlopeDenominator);
        Assert.Equal((sbyte)0, row.HoriCaretOffset);
        Assert.Equal((sbyte)-2, row.HoriMinOriginSB);
        Assert.Equal((sbyte)-1, row.HoriMinAdvanceSB);
        Assert.Equal((sbyte)1, row.HoriMaxBeforeBL);
        Assert.Equal((sbyte)-1, row.HoriMinAfterBL);
        Assert.Equal((sbyte)0, row.HoriPad1);
        Assert.Equal((sbyte)0, row.HoriPad2);
        Assert.Equal((sbyte)-12, row.VertAscender);
        Assert.Equal((sbyte)-3, row.VertDescender);
        Assert.Equal((byte)18, row.VertWidthMax);
        Assert.Equal((sbyte)1, row.VertCaretSlopeNumerator);
        Assert.Equal((sbyte)2, row.VertCaretSlopeDenominator);
        Assert.Equal((sbyte)0, row.VertCaretOffset);
        Assert.Equal((sbyte)-2, row.VertMinOriginSB);
        Assert.Equal((sbyte)-1, row.VertMinAdvanceSB);
        Assert.Equal((sbyte)1, row.VertMaxBeforeBL);
        Assert.Equal((sbyte)-1, row.VertMinAfterBL);
        Assert.Equal((sbyte)0, row.VertPad1);
        Assert.Equal((sbyte)0, row.VertPad2);
        Assert.Equal((ushort)0, row.StartGlyphIndex);
        Assert.Equal((ushort)3, row.EndGlyphIndex);
        Assert.Equal((byte)16, row.PpemX);
        Assert.Equal((byte)16, row.PpemY);
        Assert.Equal((byte)32, row.BitDepth);
        Assert.Equal((sbyte)1, row.Flags);
    }

    [Fact]
    public void CblcBitmapSizeArrayWalksOrdinalAndRefusesOutside()
    {
        var wire = CblcHeader(3, 0, 2);
        Array.Resize(ref wire, 8 + 96);
        BitmapSizeRow(104, 36, 1, 0, 0, 3, 16, 16, 32, 1).CopyTo(wire, 8);
        BitmapSizeRow(140, 36, 1, 0, 0, 3, 64, 64, 32, 1).CopyTo(wire, 56);
        ReadStatus status = CblcView.TryOpen(wire, out var view, out var diagnostic,
            CblcSource);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetBitmapSize(1, out var row, out diagnostic));
        Assert.Equal(1, row.Ordinal);
        Assert.Equal((byte)64, row.PpemX);
        Assert.Equal(ReadStatus.InvalidIndex, view.TryGetBitmapSize(2, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
    }

    [Fact]
    public void CblcBitmapSizeTruncatedRowNamesTheFirstIncompleteField()
    {
        // The row accessor names the first incomplete field: a 47-byte tail
        // leaves the int8 flags word at 47 unreadable (the T4-10 family line
        // through the generated truncation chain).
        var wire = CblcHeader(3, 0, 1);
        Array.Resize(ref wire, 8 + 47);
        BitmapSizeRow(55, 36, 1, 0, 0, 3, 16, 16, 32, 1).AsSpan()[..47].CopyTo(wire.AsSpan(8));
        ReadStatus status = CblcView.TryOpen(wire, out var view, out var _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Truncated,
            view.TryGetBitmapSize(0, out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Flags, diagnostic.Location.Field);
        Assert.Equal(55uL, diagnostic.Location.Offset); // row at 8 + flags at 47.
    }

    [Fact]
    public void CblcNumSizesZeroIsALegalBareHeaderShellAtDeclarationLevel()
    {
        // The generated face carries no non-zero clause for numSizes: the shell
        // binds and every row ordinal is simply outside the zero-length array
        // (the OQ-T6-05-10 observation ruling is the semantic layer's business,
        // not the declaration's).
        ReadStatus status = CblcView.TryOpen(CblcHeader(3, 0, 0), out var view,
            out var diagnostic, CblcSource);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidIndex,
            view.TryGetBitmapSize(0, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
    }
}
