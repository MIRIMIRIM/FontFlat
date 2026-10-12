using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-04 commit 1's declared faces: the SVG 10-byte header (three
/// scalars, OQ-T6-04-1's version 0 domain and OQ-T6-04-2's reserved word) and
/// the sbix 8-byte header plus the counted strikeOffsets array (OQ-T6-04-9's
/// u32 count, the CPAL generated-array line). Every assertion is against the
/// public generated accessors, so the OQ-T6-04-15 precheck (exactly the two new
/// generated views, zero generator gaps) is exercised here, not just declared.
/// The internal SvgLayout/SbixLayout constants are pinned through the wire
/// literals this file builds.</summary>
public sealed class SvgSbixDeclarationTests
{
    private const uint SvgTag = 0x53564720;
    private const uint SbixTag = 0x73626978;
    private static readonly DataLocation Source = new(9, 0, SvgTag, -1,
        DataField.None, OffsetBasis.Table, 0);
    private static readonly DataLocation SbixSource = new(9, 0, SbixTag, -1,
        DataField.None, OffsetBasis.Table, 0);

    // The SVG header words pack at 0/2/6: u16 version + Offset32
    // svgDocumentListOffset + u32 reserved = the 10-byte face (card T6-04 §0;
    // the §2 prose "@4/@8" slip is registered in the schema comment).
    private static byte[] SvgHeader(ushort version, uint documentListOffset, uint reserved)
    {
        var wire = new byte[10];
        WriteU16(wire, 0, version);
        WriteU32(wire, 2, documentListOffset);
        WriteU32(wire, 6, reserved);
        return wire;
    }

    private static byte[] SbixHeader(ushort version, ushort flags, uint numStrikes)
    {
        var wire = new byte[8 + 4 * (int)numStrikes];
        WriteU16(wire, 0, version);
        WriteU16(wire, 2, flags);
        WriteU32(wire, 4, numStrikes);
        for (int i = 0; i < (int)numStrikes; i++)
            WriteU32(wire, 8 + 4 * i, (uint)(8 + 4 * (int)numStrikes + 4 * i));
        return wire;
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

    // ---- SVG declaration face ----

    [Fact]
    public void SvgHeaderScalarsReadAtTheirFixedPositions()
    {
        ReadStatus status = SvgView.TryOpen(SvgHeader(0, 10, 0), out var view, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetVersion(out ushort version, out diagnostic));
        Assert.Equal((ushort)0, version);
        Assert.Equal(ReadStatus.Done, view.TryGetDocumentListOffset(out uint listOffset, out diagnostic));
        Assert.Equal(10u, listOffset);
        Assert.Equal(ReadStatus.Done, view.TryGetReserved(out uint reserved, out diagnostic));
        Assert.Equal(0u, reserved);
    }

    [Fact]
    public void SvgShortHeaderRefusesAtTheFirstIncompleteSlot()
    {
        // The 10-byte header is also the open minimum, so a 9-byte wire refuses
        // at TryOpen and names the first incomplete slot: the reserved word at
        // 6..10 (version and documentListOffset slice cleanly).
        byte[] wire = SvgHeader(0, 10, 0)[..9];
        ReadStatus status = SvgView.TryOpen(wire, out _, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Truncated, status);
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(6uL, diagnostic.Location.Offset);
        Assert.Equal(10uL, diagnostic.Expected);
    }

    [Fact]
    public void SvgOpenRefusesBelowTheTenByteHeader()
    {
        ReadStatus status = SvgView.TryOpen(SvgHeader(0, 10, 0)[..8], out _, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Truncated, status);
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(6uL, diagnostic.Location.Offset);
        Assert.Equal(10uL, diagnostic.Expected);
    }

    [Fact]
    public void SvgViewRawBytesExposeTheBoundWindow()
    {
        var wire = SvgHeader(0, 10, 0);
        ReadStatus status = SvgView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.True(view.IsBound);
        Assert.Equal(wire.Length, view.RawBytes.Length);
    }

    // ---- sbix declaration face ----

    [Fact]
    public void SbixHeaderScalarsAndStrikeOffsetsReadAtTheirFixedPositions()
    {
        var wire = SbixHeader(1, 1, 3);
        ReadStatus status = SbixView.TryOpen(wire, out var view, out var diagnostic, SbixSource);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetVersion(out ushort version, out diagnostic));
        Assert.Equal((ushort)1, version);
        Assert.Equal(ReadStatus.Done, view.TryGetFlags(out ushort flags, out diagnostic));
        Assert.Equal((ushort)1, flags);
        Assert.Equal(ReadStatus.Done, view.TryGetNumStrikes(out uint numStrikes, out diagnostic));
        Assert.Equal(3u, numStrikes);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(ReadStatus.Done, view.TryGetStrikeOffsets(i, out var row, out diagnostic));
            Assert.Equal(i, row.Ordinal);
            Assert.Equal(20u + 4u * (uint)i, row.Value); // 8 + 4*3 = 20: strikes follow the array.
        }
    }

    [Fact]
    public void SbixZeroStrikesIsALegalPureHeaderShell()
    {
        // numStrikes==0 has no non-zero clause (OQ-T6-04-9's lean): the array is
        // empty, any ordinal refuses as an index face, and the header reads Done.
        ReadStatus status = SbixView.TryOpen(SbixHeader(1, 1, 0), out var view, out var diagnostic, SbixSource);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetNumStrikes(out uint numStrikes, out diagnostic));
        Assert.Equal(0u, numStrikes);
        Assert.Equal(ReadStatus.InvalidIndex, view.TryGetStrikeOffsets(0, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(DataField.Count, diagnostic.Location.Field);
    }

    [Fact]
    public void SbixShortStrikeOffsetsArrayTruncatesAtTheFirstIncompleteRow()
    {
        // Three declared offsets, wire carries two and a half rows: the third
        // row's own slot is the truncated one (the generated row provenance,
        // observed through the public row accessor).
        byte[] wire = SbixHeader(1, 1, 3)[..^1];
        ReadStatus status = SbixView.TryOpen(wire, out var view, out _, SbixSource);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetStrikeOffsets(1, out _, out _));
        Assert.Equal(ReadStatus.Truncated, view.TryGetStrikeOffsets(2, out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(16uL, diagnostic.Location.Offset);
        Assert.Equal(20uL, diagnostic.Expected);
    }

    [Fact]
    public void SbixShortHeaderTruncatesAtTheFirstIncompleteSlot()
    {
        // 7 bytes: version and flags slice cleanly, numStrikes at 4..8 is short.
        byte[] wire = SbixHeader(1, 1, 0)[..7];
        ReadStatus status = SbixView.TryOpen(wire, out _, out var diagnostic, SbixSource);
        Assert.Equal(ReadStatus.Truncated, status);
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Count, diagnostic.Location.Field);
        Assert.Equal(4uL, diagnostic.Location.Offset);
        Assert.Equal(8uL, diagnostic.Expected);
    }

    [Fact]
    public void SbixViewRawBytesExposeTheBoundWindow()
    {
        var wire = SbixHeader(1, 1, 2);
        ReadStatus status = SbixView.TryOpen(wire, out var view, out _, SbixSource);
        Assert.Equal(ReadStatus.Done, status);
        Assert.True(view.IsBound);
        Assert.Equal(wire.Length, view.RawBytes.Length);
    }
}
