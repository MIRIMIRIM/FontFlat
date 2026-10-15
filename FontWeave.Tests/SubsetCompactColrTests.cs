// Compact-remap COLR rebuild tests (SE card §4 commit ② acceptance).
//
// The input COLR wires are hand-assembled canonical bytes (never product-emitted),
// so the rebuild's expectation is independent of the compiler under test; the v0
// expectation arrays are hand-computed canonical emissions. The CPAL retain face
// and the palette-domain cross wiring ride the same faces.
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCompactColrTests
{
    private const uint ColrTag = 0x434F4C52, CpalTag = 0x4350414C;

    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        SubsetMode mode) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = mode,
    };

    internal static FontFaceRef ProbeFace()
    {
        var cpal = CpalV0();
        return Spine(8, (ColrTag, ColrV0()), (CpalTag, cpal));
    }

    internal static byte[] ColrV1WireForProbe() => ColrV1();

    internal static FontFaceRef SpineForProbe(ushort numGlyphs,
        params (uint Tag, byte[] Bytes)[] extra) => Spine(numGlyphs, extra);

    private static FontFaceRef Spine(ushort numGlyphs, params (uint Tag, byte[] Bytes)[] extra)
    {
        var records = new List<byte[]>();
        for (int i = 0; i < numGlyphs; i++) records.Add(GlyfImages.NoContourGlyphRecord());
        var (loca, glyf, _) = GlyfImages.Assemble(records);
        var tables = new List<(uint, byte[])>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(numGlyphs)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 1))),
        };
        tables.AddRange(extra);
        return SubsetFontFixture.Face([.. tables]);
    }

    /// <summary>A valid minimal CPAL v0: one palette of two entries, two BGRA records
    /// (version is the UInt16 head; the records offset rides at byte 8).</summary>
    private static byte[] CpalV0()
    {
        var bytes = new byte[22];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 0);   // version
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 2);   // numPaletteEntries
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 1);   // numPalettes
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 2);   // numColorRecords
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 14);  // records offset
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), 0);  // index[0]
        bytes[14] = 0x10; bytes[15] = 0x20; bytes[16] = 0x30; bytes[17] = 0xFF;
        bytes[18] = 0x40; bytes[19] = 0x50; bytes[20] = 0x60; bytes[21] = 0xFF;
        return bytes;
    }

    /// <summary>Canonical COLR v0 (version is the UInt16 head): base 5 covers
    /// layers (5,6,7), base 7 shares layers (6,7); palette indices sit inside the
    /// CPAL entry domain.</summary>
    private static byte[] ColrV0(ushort paletteIndex = 1) => new ByteBuilder(38)
        .U16(0).U16(2).U32(26).U32(14).U16(3)          // header: base@26, layers@14
        .U16(5).U16(0).U16(6).U16(paletteIndex).U16(7).U16(0)  // layers
        .U16(5).U16(0).U16(3).U16(7).U16(1).U16(2)     // bases
        .ToBytes();

    /// <summary>Canonical COLR v1 graph: root 5 → layers slice over entries
    /// [glyph(51)→solid, colrGlyph(6)→root 6's solid, solid]; root 6 → solid;
    /// clips over [5,6] and [10,20]. Offsets follow the canonical plan: BGL@34,
    /// LL@50, ClipList@66, paints from 103 (layers 6B, glyph 6B, solids 5B,
    /// colrGlyph 3B → total 133).</summary>
    private static byte[] ColrV1() => new ByteBuilder(133)
        .U16(1).U16(0).U32(0).U32(0).U16(0)
        .U32(34).U32(50).U32(66).U32(0).U32(0)         // bgl, ll, clipList, var slots
        .U32(2)                                        // BGL count
        .U16(5).U32(69)                                // root 5 → layers@103
        .U16(6).U32(89)                                // root 6 → solid@123
        .U32(3).U32(59).U32(70).U32(78)                // LL entries → glyph/colrGlyph/solid
        .U8(1).U32(2)                                  // clipList fmt1, 2 rows
        .U16(5).U16(6).U24(19)                         // clip [5,6] → box@85
        .U16(10).U16(20).U24(28)                       // clip [10,20] → box@94
        .U8(1).S16(-10).S16(-10).S16(10).S16(10)       // box0 (fmt1)
        .U8(1).S16(0).S16(0).S16(4).S16(4)             // box1
        .U8(1).U8(3).U32(0)                            // layers@103: slice 0..3
        .U8(10).U24(6).U16(51)                         // glyph@109 → solid@115, gid 51
        .U8(2).U16(3).U16(0x4000)                      // solid@115
        .U8(11).U16(6)                                 // colrGlyph@120 → root 6
        .U8(2).U16(1).U16(0x8000)                      // solid@123 (root 6)
        .U8(2).U16(2).U16(0xC000)                      // solid@128
        .ToBytes();

    private sealed class ByteBuilder
    {
        private readonly byte[] _bytes;
        private int _at;

        internal ByteBuilder(int length) => _bytes = new byte[length];

        internal ByteBuilder U8(byte v) { _bytes[_at++] = v; return this; }
        internal ByteBuilder U8(int v) => U8((byte)v);
        internal ByteBuilder U16(ushort v)
        {
            BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan(_at), v);
            _at += 2;
            return this;
        }
        internal ByteBuilder U16(int v) => U16((ushort)v);
        internal ByteBuilder U24(uint v)
        {
            _bytes[_at++] = (byte)(v >> 16);
            _bytes[_at++] = (byte)(v >> 8);
            _bytes[_at++] = (byte)v;
            return this;
        }
        internal ByteBuilder U24(int v) => U24((uint)v);
        internal ByteBuilder U32(uint v)
        {
            BinaryPrimitives.WriteUInt32BigEndian(_bytes.AsSpan(_at), v);
            _at += 4;
            return this;
        }
        internal ByteBuilder U32(int v) => U32((uint)v);
        internal ByteBuilder S16(short v) => U16(unchecked((ushort)v));
        internal ByteBuilder S16(int v) => S16((short)v);
        internal byte[] ToBytes()
        {
            Assert.Equal(_bytes.Length, _at);
            return _bytes;
        }
    }

    private static byte[] FontTable(SubsetResult result, uint tag)
    {
        byte[] bytes = new byte[result.Font.OutputLength];
        var copy = result.Font.TryCopyTo(bytes, out int written, out _);
        Assert.Equal(ReadStatus.Done, copy);
        Assert.Equal(bytes.Length, written);
        _ = FontSource.TryCopy(bytes, out var source, out _);
        _ = source!.TryGetFace(0, out var face, out _);
        _ = face.TryOpenDirectory(out var directory, out _);
        var status = directory.TryFindUniqueTable(tag, out int ordinal, out _);
        Assert.Equal(ReadStatus.Done, status);
        status = directory.TryGetTableBytes(ordinal, out var payload, out _);
        Assert.Equal(ReadStatus.Done, status);
        return payload.ToArray();
    }

    private static ColrTable ReadColr(byte[] wire, int numGlyphs, int? cpalEntries)
    {
        var status = ColrView.TryOpen(wire, out var view, out var diagnostic,
            DataLocation.Anonymous(ColrLayout.Tag));
        Assert.Equal(ReadStatus.Done, status);
        status = view.TryReadTable(numGlyphs, cpalEntries, out var model, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        return model!;
    }

    // v0: base 5 survives with its surviving layers (5,6), base 7 and layer 7 prune;
    // the expectation is the hand-computed canonical emission over the map
    // {0→0, 5→1, 6→2}; CPAL stays byte-identical (RETAIN_RAW).
    [Fact]
    public void CompactColrV0PrunesSlicesAndKeepsCpalRaw()
    {
        var cpal = CpalV0();
        var face = Spine(8, (ColrTag, ColrV0()), (CpalTag, cpal));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 6],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);

        byte[] expectedColr = new ByteBuilder(28)
            .U16(0).U16(1).U32(22).U32(14).U16(2)
            .U16(1).U16(0).U16(2).U16(1)
            .U16(1).U16(0).U16(2)
            .ToBytes();
        Assert.Equal(expectedColr, FontTable(result, ColrTag));
        Assert.Equal(cpal, FontTable(result, CpalTag));
    }

    // v0 with every color glyph unselected drops the table through the payload-time
    // decision revision (OQ-SE-5): no COLR in the output directory, the decision set
    // revises to Drop, DroppedTables carries the tag.
    [Fact]
    public void CompactColrV0AllPrunedDropsTable()
    {
        var face = Spine(8, (ColrTag, ColrV0()), (CpalTag, CpalV0()));
        var status = SubsetOperation.TryPrepare(Request(face, [0],
            SubsetMode.CompactRemap), out var result, out var report,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.NotNull(report);
        var byTag = report!.Decisions.ToDictionary(d => d.Tag);
        Assert.Equal(TableAction.Drop, byTag[ColrTag].Action);
        Assert.Contains(ColrTag, report.DroppedTables);
        byte[] bytes = new byte[result!.Font.OutputLength];
        result.Font.TryCopyTo(bytes, out _, out _);
        _ = FontSource.TryCopy(bytes, out var source, out _);
        _ = source!.TryGetFace(0, out var faceOut, out _);
        _ = faceOut.TryOpenDirectory(out var directory, out _);
        Assert.Equal(ReadStatus.MissingTable,
            directory.TryFindUniqueTable(ColrTag, out _, out _));
    }

    // The CPAL palette-entry authority rides the COLR read: a layer palette index
    // outside the validated CPAL domain refuses through the read chain.
    [Fact]
    public void CompactColrPaletteDomainRefusesThroughCpalAuthority()
    {
        var face = Spine(8, (ColrTag, ColrV0(paletteIndex: 3)), (CpalTag, CpalV0()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 6],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidIndex, status);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(ColrTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // v1 identity: the request over the whole domain maps every reference to itself
    // (the SE-ID tier's COLR half at unit scale). The rebuilt graph is the factory's
    // canonical emission of the reachable subgraph - the read model's Kahn order
    // (depth, first visit), not the input's wire order - so byte identity holds only
    // when the input already sits at that fixed point; this fixture does not, so the
    // tier asserts the fixed point itself: a second full-selection rebuild of the
    // output re-emits byte-for-byte, and every reference lands on itself.
    [Fact]
    public void CompactColrV1IdentityRecompilesByteIdentical()
    {
        var face = Spine(52, (ColrTag, ColrV1()));
        var all = new List<ushort>();
        for (ushort g = 0; g < 52; g++) all.Add(g);
        var status = SubsetOperation.TryPrepare(Request(face, all,
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        // The semantic tier: every reference survives onto itself (BGL roots, the
        // layer slice's glyph and colrGlyph, both clip rows' spans).
        ColrTable model = ReadColr(FontTable(result!, ColrTag), 52, 4);
        Assert.Equal(2, model.BaseGlyphPaintCount);
        Assert.Equal((ushort)5, model.BaseGlyphPaints[0].GlyphId);
        Assert.Equal((ushort)6, model.BaseGlyphPaints[1].GlyphId);
        var rootLayers = Assert.IsType<ColrPaintLayers>(
            model.Paints[model.BaseGlyphPaints[0].PaintNodeId]);
        Assert.Equal(3, rootLayers.LayerNodeIds.Count);
        var glyph = Assert.IsType<ColrPaintGlyph>(
            model.Paints[rootLayers.LayerNodeIds[0]]);
        Assert.Equal((ushort)51, glyph.GlyphId);
        var colrGlyph = Assert.IsType<ColrPaintColrGlyph>(
            model.Paints[rootLayers.LayerNodeIds[1]]);
        Assert.Equal((ushort)6, colrGlyph.GlyphId);
        Assert.Equal(model.BaseGlyphPaints[1].PaintNodeId, colrGlyph.TargetNodeId);
        Assert.Equal(2, model.ClipCount);
        Assert.Equal(((ushort)5, (ushort)6),
            (model.Clips[0].StartGlyphId, model.Clips[0].EndGlyphId));
        Assert.Equal(((ushort)10, (ushort)20),
            (model.Clips[1].StartGlyphId, model.Clips[1].EndGlyphId));
        // The fixed-point tier: the output's own full-selection rebuild is
        // byte-identical (the input's wire order is not the canonical emission's
        // Kahn order, so the first pass may differ from the input - the identity
        // contract is against the fixed point, not the wire).
        byte[] first = FontTable(result!, ColrTag);
        var again = SubsetOperation.TryPrepare(Request(
            Spine(52, (ColrTag, first)), all, SubsetMode.CompactRemap),
            out var result2, out _, out _);
        Assert.Equal(ReadStatus.Done, again);
        Assert.NotNull(result2);
        Assert.Equal(first, FontTable(result2!, ColrTag));
    }

    // v1 pruning: root 5 survives, its graph renumbers through the reachable set,
    // PaintGlyph/PaintColrGlyph remap, the dead clip row drops and the surviving row
    // re-spans over the map; the assertions ride the output model's re-read.
    [Fact]
    public void CompactColrV1PrunesGraphAndClips()
    {
        var face = Spine(52, (ColrTag, ColrV1()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 6, 51],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        ColrTable model = ReadColr(FontTable(result!, ColrTag), 4, 4);
        Assert.Equal(1, model.Version);
        Assert.Equal(2, model.BaseGlyphPaintCount);
        Assert.Equal((ushort)1, model.BaseGlyphPaints[0].GlyphId);
        Assert.Equal((ushort)2, model.BaseGlyphPaints[1].GlyphId);
        // The rebuilt graph: root 1's slice covers three entries whose nodes are
        // [glyph(out 51), colrGlyph(out 6) → root 2's solid, solid]; assertions ride
        // the re-read's own canonical numbering.
        var rootLayers = Assert.IsType<ColrPaintLayers>(model.Paints[model.BaseGlyphPaints[0].PaintNodeId]);
        Assert.Equal(0u, rootLayers.FirstLayerIndex);
        Assert.Equal(3, rootLayers.LayerNodeIds.Count);
        var glyph = Assert.IsType<ColrPaintGlyph>(model.Paints[rootLayers.LayerNodeIds[0]]);
        Assert.Equal((ushort)3, glyph.GlyphId);
        Assert.IsType<ColrPaintSolid>(model.Paints[glyph.PaintNodeId]);
        var colrGlyph = Assert.IsType<ColrPaintColrGlyph>(model.Paints[rootLayers.LayerNodeIds[1]]);
        Assert.Equal((ushort)2, colrGlyph.GlyphId);
        Assert.Equal(model.BaseGlyphPaints[1].PaintNodeId, colrGlyph.TargetNodeId);
        Assert.IsType<ColrPaintSolid>(model.Paints[model.BaseGlyphPaints[1].PaintNodeId]);
        Assert.IsType<ColrPaintSolid>(model.Paints[rootLayers.LayerNodeIds[2]]);
        Assert.Equal(1, model.ClipCount);
        Assert.Equal((ushort)1, model.Clips[0].StartGlyphId);
        Assert.Equal((ushort)2, model.Clips[0].EndGlyphId);
    }

    // RE3: a PaintGlyph target the request did not select refuses with InvalidValue
    // at the referencing paint, detail = the old GID (the SE pre-check, finding 4).
    [Fact]
    public void CompactColrV1DanglingPaintGlyphRefuses()
    {
        var wire = ColrV1();
        BinaryPrimitives.WriteUInt16BigEndian(wire.AsSpan(109 + 4), 9); // glyph → 9
        var face = Spine(52, (ColrTag, wire));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 6],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ColrTag, diagnostic.Location.TableTag);
        Assert.Equal(9UL, diagnostic.Detail);
        Assert.Null(result);
    }

    // RE3: the PaintColrGlyph indirect reference answers the same face — the
    // fixture's colrGlyph targets root 6, which the request leaves unselected (the
    // BGL record exists, so the read walk passes and the SE pre-check refuses).
    [Fact]
    public void CompactColrV1DanglingPaintColrGlyphRefuses()
    {
        var face = Spine(52, (ColrTag, ColrV1()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 51],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(ColrTag, diagnostic.Location.TableTag);
        Assert.Equal(6UL, diagnostic.Detail);
        Assert.Null(result);
    }

    // v1 with no surviving color graph drops the table (OQ-SE-5).
    [Fact]
    public void CompactColrV1AllPrunedDropsTable()
    {
        var face = Spine(52, (ColrTag, ColrV1()));
        var status = SubsetOperation.TryPrepare(Request(face, [0],
            SubsetMode.CompactRemap), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.NotNull(report);
        var byTag = report!.Decisions.ToDictionary(d => d.Tag);
        Assert.Equal(TableAction.Drop, byTag[ColrTag].Action);
        Assert.Contains(ColrTag, report.DroppedTables);
    }

    // Retain-GIDs zero drift: the same faces keep COLR/CPAL raw and byte-identical.
    [Fact]
    public void RetainKeepsColrAndCpalRaw()
    {
        var cpal = CpalV0();
        var face = Spine(8, (ColrTag, ColrV0()), (CpalTag, cpal));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 6],
            SubsetMode.RetainGids), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var byTag = report!.Decisions.ToDictionary(d => d.Tag);
        Assert.Equal(TableAction.Preserve, byTag[ColrTag].Action);
        Assert.Equal(SubsetPolicy.RetainRaw, byTag[ColrTag].Policy);
        Assert.Equal(TableAction.Preserve, byTag[CpalTag].Action);
        Assert.Equal(SubsetPolicy.RetainRaw, byTag[CpalTag].Policy);
        Assert.Equal(ColrV0(), FontTable(result!, ColrTag));
        Assert.Equal(cpal, FontTable(result!, CpalTag));
    }
}
