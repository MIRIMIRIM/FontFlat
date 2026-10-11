using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-02 commit 1's declared v1 face: the five WhenVersion Offset32
/// slots and the two v1 arrays (BGL/LayerList), gated by the counts per the
/// generator's FWG005 shape, plus the read-only gate that keeps whole-table v1
/// wires refusing until the read chain lands in commit 2. Every assertion is
/// against the public generated accessors, so the OQ-T6-02-2/OQ-T6-02-15
/// prechecks (array generation shape, no new .g.cs surface) are exercised here,
/// not just declared. The internal ColrLayout constants are pinned through the
/// wire literals this file builds.</summary>
public sealed class ColrV1DeclarationTests
{
    private const uint ColrTag = 0x434F4C52;
    private const int V1HeaderLength = 34;
    private static readonly DataLocation Source = new(9, 0, ColrTag, -1,
        DataField.None, OffsetBasis.Table, 0);

    // A 34-byte v1 header with the five offsets, no arrays.
    private static byte[] V1Header(uint baseGlyphList = 34, uint layerList = 0,
        uint clipList = 0, uint varIndexMap = 0, uint ivs = 0)
    {
        var wire = new byte[V1HeaderLength];
        WriteU16(wire, 0, 1);
        WriteU32(wire, 14, baseGlyphList);
        WriteU32(wire, 18, layerList);
        WriteU32(wire, 22, clipList);
        WriteU32(wire, 26, varIndexMap);
        WriteU32(wire, 30, ivs);
        return wire;
    }

    // A minimal legal v0 wire: header + one layer record + one base record,
    // layer-records-first (the T6-01 canonical order).
    private static byte[] V0Wire()
    {
        var wire = new byte[14 + 4 + 6];
        WriteU16(wire, 0, 0);
        WriteU16(wire, 2, 1);              // numBaseGlyphRecords
        WriteU32(wire, 4, 18);             // baseGlyphRecordsOffset
        WriteU32(wire, 8, 14);             // layerRecordsOffset
        WriteU16(wire, 12, 1);             // numLayerRecords
        WriteU16(wire, 14, 9);             // layer: glyphId
        WriteU16(wire, 16, 3);             // layer: paletteIndex
        WriteU16(wire, 18, 9);             // base: glyphId
        WriteU16(wire, 20, 0);             // base: firstLayerIndex
        WriteU16(wire, 22, 1);             // base: numLayers
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

    [Fact]
    public void V1HeaderSlotsReadAtTheirFixedPositions()
    {
        ReadStatus status = ColrView.TryOpen(V1Header(baseGlyphList: 40, layerList: 46,
            clipList: 58, varIndexMap: 70, ivs: 74), out var view, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetBaseGlyphListOffset(out uint bgl, out diagnostic));
        Assert.Equal(40u, bgl);
        Assert.Equal(ReadStatus.Done, view.TryGetLayerListOffset(out uint ll, out diagnostic));
        Assert.Equal(46u, ll);
        Assert.Equal(ReadStatus.Done, view.TryGetClipListOffset(out uint cl, out diagnostic));
        Assert.Equal(58u, cl);
        Assert.Equal(ReadStatus.Done, view.TryGetVarIndexMapOffset(out uint vim, out diagnostic));
        Assert.Equal(70u, vim);
        Assert.Equal(ReadStatus.Done, view.TryGetItemVariationStoreOffset(out uint ivs, out diagnostic));
        Assert.Equal(74u, ivs);
    }

    [Fact]
    public void V0WireReadsOtherwiseZerosForEveryV1SlotWithoutTruncation()
    {
        // A 14-byte v0 wire physically ends where the v1 slots would sit; the
        // WhenVersion accessors must return the Otherwise zeros as Done, never a
        // truncation face (the GDEF physically-absent-field line).
        byte[] wire = V0Wire()[..14];
        ReadStatus status = ColrView.TryOpen(wire, out var view, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetBaseGlyphListOffset(out uint bgl, out diagnostic));
        Assert.Equal(0u, bgl);
        Assert.Equal(ReadStatus.Done, view.TryGetLayerListOffset(out uint ll, out diagnostic));
        Assert.Equal(0u, ll);
        Assert.Equal(ReadStatus.Done, view.TryGetClipListOffset(out uint clip, out diagnostic));
        Assert.Equal(0u, clip);
        Assert.Equal(ReadStatus.Done, view.TryGetVarIndexMapOffset(out uint map, out diagnostic));
        Assert.Equal(0u, map);
        Assert.Equal(ReadStatus.Done, view.TryGetItemVariationStoreOffset(out uint store, out diagnostic));
        Assert.Equal(0u, store);
        Assert.Equal(ReadStatus.Done, view.TryGetNumBaseGlyphPaintRecords(out uint bglCount, out diagnostic));
        Assert.Equal(0uL, bglCount);
        Assert.Equal(ReadStatus.Done, view.TryGetNumLayerPaints(out uint llCount, out diagnostic));
        Assert.Equal(0uL, llCount);
    }

    [Fact]
    public void ShortV1HeaderTruncatesAtTheFirstIncompleteSlot()
    {
        // 30 bytes: the four first v1 slots slice cleanly, the fifth (IVS at
        // 30..34) is the truncated one and names its position.
        byte[] wire = V1Header()[..30];
        ReadStatus status = ColrView.TryOpen(wire, out var view, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.TryGetVarIndexMapOffset(out uint map, out diagnostic));
        Assert.Equal(0u, map);
        Assert.Equal(ReadStatus.Truncated, view.TryGetItemVariationStoreOffset(out _, out diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(30uL, diagnostic.Location.Offset);
        Assert.Equal(34uL, diagnostic.Expected);
    }

    [Fact]
    public void WholeTableV1WireRefusesWithThePinnedVersionFaceUntilCommit2()
    {
        ReadStatus status = ColrView.TryOpen(V1Header(), out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.UnsupportedFormat, view.ValidateTable(16, 4,
            out var diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal(DataField.Version, diagnostic.Location.Field);
        Assert.Equal(1uL, diagnostic.Detail);
        Assert.Equal(0uL, diagnostic.Expected);
        Assert.Equal(ReadStatus.UnsupportedFormat, view.TryFindBaseGlyph(9, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
    }

    [Fact]
    public void V0WholeTableWalkIsUntouchedByTheDeclaration()
    {
        ReadStatus status = ColrView.TryOpen(V0Wire(), out var view, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(16, 8, out diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryFindBaseGlyph(9, out int ordinal, out diagnostic));
        Assert.Equal(0, ordinal);
        Assert.Equal(ReadStatus.NotFound, view.TryFindBaseGlyph(10, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.GlyphNotMapped, diagnostic.Code);
    }

    [Fact]
    public void VersionGatedCountsReadZeroOnV0AndTheArraysReadRowsOnV1()
    {
        ReadStatus status = ColrView.TryOpen(V0Wire(), out var v0View, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        // On v0 both counts answer the Otherwise zero before any position is
        // evaluated, so the arrays are empty: any ordinal refuses as InvalidIndex
        // (the generator's FWG005 shape - the version gate lives on the counts,
        // the arrays stay unconditioned).
        Assert.Equal(ReadStatus.Done, v0View.TryGetNumBaseGlyphPaintRecords(out uint v0Bgl, out diagnostic));
        Assert.Equal(0uL, v0Bgl);
        Assert.Equal(ReadStatus.InvalidIndex, v0View.TryGetBaseGlyphPaintRecord(0,
            out _, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(ReadStatus.InvalidIndex, v0View.TryGetLayerPaintOffset(0,
            out _, out diagnostic));

        // A v1 wire with a one-record BGL at 34 and a two-entry LayerList at 46.
        var wire = new byte[64];
        V1Header(baseGlyphList: 34, layerList: 46).AsSpan().CopyTo(wire);
        WriteU32(wire, 34, 1);             // BGL count
        WriteU16(wire, 38, 7);             // record glyphID
        WriteU32(wire, 40, 12);            // record paintOffset (from BGL start)
        WriteU32(wire, 46, 2);             // LayerList count
        WriteU32(wire, 50, 6);             // entry 0 offset (from LL start)
        WriteU32(wire, 54, 18);            // entry 1 offset
        status = ColrView.TryOpen(wire, out var v1View, out diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, v1View.TryGetNumBaseGlyphPaintRecords(out uint bglCount, out diagnostic));
        Assert.Equal(1uL, bglCount);
        Assert.Equal(ReadStatus.Done, v1View.TryGetBaseGlyphPaintRecord(0,
            out var record, out diagnostic));
        Assert.Equal((ushort)7, record.GlyphId);
        Assert.Equal(12u, record.PaintOffset);
        Assert.Equal(ReadStatus.InvalidIndex, v1View.TryGetBaseGlyphPaintRecord(1,
            out _, out diagnostic));
        Assert.Equal(ReadStatus.Done, v1View.TryGetNumLayerPaints(out uint llCount, out diagnostic));
        Assert.Equal(2uL, llCount);
        Assert.Equal(ReadStatus.Done, v1View.TryGetLayerPaintOffset(1,
            out var entry, out diagnostic));
        Assert.Equal(18u, entry.PaintOffset);
        Assert.Equal(ReadStatus.InvalidIndex, v1View.TryGetLayerPaintOffset(2,
            out _, out diagnostic));
    }

    [Fact]
    public void BglWindowTruncationNamesTheFirstIncompleteRecordField()
    {
        // BGL at 34 declares one record but the wire ends inside it (42 bytes:
        // glyphID present, paintOffset short by two bytes).
        var wire = new byte[42];
        V1Header(baseGlyphList: 34).AsSpan().CopyTo(wire);
        WriteU32(wire, 34, 1);
        WriteU16(wire, 38, 7);
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Truncated, view.TryGetBaseGlyphPaintRecord(0,
            out _, out var diagnostic));
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(44uL, diagnostic.Expected);
    }
}
