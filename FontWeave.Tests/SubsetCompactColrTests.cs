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

    /// <summary>The noto defect's root shape: one BGL row (glyph 5) whose root
    /// paint IS a PaintGlyph — glyph 9's outline, palette solid underneath; no
    /// LayerList, no ClipList. BGL@34, the row's paintOffset 10 lands the
    /// PaintGlyph@44, its child solid@50 (55 bytes).</summary>
    private static byte[] ColrV1RootGlyphPaint() => new ByteBuilder(55)
        .U16(1).U16(0).U32(0).U32(0).U16(0)
        .U32(34).U32(0).U32(0).U32(0).U32(0)   // bgl, no ll/clip/var slots
        .U32(1)                                // BGL count
        .U16(5).U32(10)                        // root 5 → PaintGlyph@44
        .U8(10).U24(6).U16(9)                  // glyph@44 → solid@50, gid 9
        .U8(2).U16(1).U16(0x4000)              // solid@50
        .ToBytes();

    /// <summary>The closure's worklist shape: two BGL rows where row 5's PaintGlyph
    /// targets glyph 9 and row 9's own root PaintGlyph targets glyph 11 — the
    /// re-rooting walk must reach 9's graph through 9's BGL row. BGL@34, rows'
    /// paintOffsets 16 and 27 land the PaintGlyphs@50 and @61, solids@56 and @67
    /// (72 bytes).</summary>
    private static byte[] ColrV1NestedRootGlyphPaints() => new ByteBuilder(72)
        .U16(1).U16(0).U32(0).U32(0).U16(0)
        .U32(34).U32(0).U32(0).U32(0).U32(0)   // bgl, no ll/clip/var slots
        .U32(2)                                // BGL count
        .U16(5).U32(16)                        // root 5 → PaintGlyph@50
        .U16(9).U32(27)                        // root 9 → PaintGlyph@61
        .U8(10).U24(6).U16(9)                  // glyph@50 → solid@56, gid 9
        .U8(2).U16(1).U16(0x4000)              // solid@56
        .U8(10).U24(6).U16(11)                 // glyph@61 → solid@67, gid 11
        .U8(2).U16(2).U16(0x8000)              // solid@67
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

    // The color closure: a PaintGlyph target the request did not select joins the
    // retained set before the freeze (the 2026-10-18 noto defect — a codepoint
    // root's paint graph references outlines no cmap seed carries), and the rebuilt
    // PaintGlyph remaps onto the closed glyph's output slot.
    [Fact]
    public void CompactColrV1PaintGlyphTargetClosesIntoMap()
    {
        var wire = ColrV1();
        BinaryPrimitives.WriteUInt16BigEndian(wire.AsSpan(109 + 4), 9); // glyph → 9
        var face = Spine(52, (ColrTag, wire));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 6],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        // The closure pulled 9 into the frozen order: outputs {0→0, 5→1, 6→2, 9→3}
        // (the TrueType identity order).
        Assert.Equal([0, 5, 6, 9],
            result!.Map!.Entries.Select(e => (int)e.InputGid).ToArray());
        ColrTable model = ReadColr(FontTable(result, ColrTag), 4, 4);
        Assert.Equal(2, model.BaseGlyphPaintCount);
        var rootLayers = Assert.IsType<ColrPaintLayers>(
            model.Paints[model.BaseGlyphPaints[0].PaintNodeId]);
        var glyph = Assert.IsType<ColrPaintGlyph>(
            model.Paints[rootLayers.LayerNodeIds[0]]);
        Assert.Equal((ushort)3, glyph.GlyphId);
        Assert.IsType<ColrPaintSolid>(model.Paints[glyph.PaintNodeId]);
    }

    // The color closure's indirect face: the PaintColrGlyph target joins the
    // retained set with its BaseGlyphList row, so the referenced base's own root
    // graph survives and remaps (the request never selected 6; its solid travels).
    [Fact]
    public void CompactColrV1PaintColrGlyphTargetClosesIntoMap()
    {
        var face = Spine(52, (ColrTag, ColrV1()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 51],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([0, 5, 6, 51],
            result!.Map!.Entries.Select(e => (int)e.InputGid).ToArray());
        ColrTable model = ReadColr(FontTable(result, ColrTag), 4, 4);
        Assert.Equal(2, model.BaseGlyphPaintCount);
        Assert.Equal((ushort)1, model.BaseGlyphPaints[0].GlyphId);
        Assert.Equal((ushort)2, model.BaseGlyphPaints[1].GlyphId);
        var rootLayers = Assert.IsType<ColrPaintLayers>(
            model.Paints[model.BaseGlyphPaints[0].PaintNodeId]);
        var colrGlyph = Assert.IsType<ColrPaintColrGlyph>(
            model.Paints[rootLayers.LayerNodeIds[1]]);
        Assert.Equal((ushort)2, colrGlyph.GlyphId);
        Assert.Equal(model.BaseGlyphPaints[1].PaintNodeId, colrGlyph.TargetNodeId);
        Assert.IsType<ColrPaintSolid>(model.Paints[model.BaseGlyphPaints[1].PaintNodeId]);
    }

    // The noto defect's exact shape: a base glyph whose ROOT paint is itself a
    // PaintGlyph (no LayerList anywhere) — a seed that selects only the base never
    // retains the outlined glyph 9, the closure does, and the root remaps with it.
    [Fact]
    public void CompactColrV1RootPaintGlyphClosesItsTarget()
    {
        var face = Spine(16, (ColrTag, ColrV1RootGlyphPaint()));
        var status = SubsetOperation.TryPrepare(Request(face, [5],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([0, 5, 9],
            result!.Map!.Entries.Select(e => (int)e.InputGid).ToArray());
        ColrTable model = ReadColr(FontTable(result, ColrTag), 3, 4);
        Assert.Equal(1, model.BaseGlyphPaintCount);
        Assert.Equal((ushort)1, model.BaseGlyphPaints[0].GlyphId);
        var glyph = Assert.IsType<ColrPaintGlyph>(
            model.Paints[model.BaseGlyphPaints[0].PaintNodeId]);
        Assert.Equal((ushort)2, glyph.GlyphId);
        Assert.IsType<ColrPaintSolid>(model.Paints[glyph.PaintNodeId]);
    }

    // The closure's worklist face: a PaintGlyph target that carries its own BGL row
    // contributes that root's graph too — PaintGlyph addresses an outline, not a
    // paint node, so only the re-rooting walk reaches 9's own reference (11).
    [Fact]
    public void CompactColrV1ClosedGlyphWithOwnRootClosesTransitively()
    {
        var face = Spine(16, (ColrTag, ColrV1NestedRootGlyphPaints()));
        var status = SubsetOperation.TryPrepare(Request(face, [5],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([0, 5, 9, 11],
            result!.Map!.Entries.Select(e => (int)e.InputGid).ToArray());
        ColrTable model = ReadColr(FontTable(result, ColrTag), 4, 4);
        Assert.Equal(2, model.BaseGlyphPaintCount);
        Assert.Equal((ushort)1, model.BaseGlyphPaints[0].GlyphId);
        Assert.Equal((ushort)2, model.BaseGlyphPaints[1].GlyphId);
        var outer = Assert.IsType<ColrPaintGlyph>(
            model.Paints[model.BaseGlyphPaints[0].PaintNodeId]);
        Assert.Equal((ushort)2, outer.GlyphId);
        var inner = Assert.IsType<ColrPaintGlyph>(
            model.Paints[model.BaseGlyphPaints[1].PaintNodeId]);
        Assert.Equal((ushort)3, inner.GlyphId);
        Assert.IsType<ColrPaintSolid>(model.Paints[inner.PaintNodeId]);
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

    // A TrueType spine over numGlyphs where the requested glyphs carry a real
    // triangle outline - the referenced-outline witness the closure tests measure.
    private static FontFaceRef OutlinedSpine(ushort numGlyphs, int[] outlinedGids,
        params (uint Tag, byte[] Bytes)[] extra)
    {
        var records = new List<byte[]>();
        for (int gid = 0; gid < numGlyphs; gid++)
            records.Add(outlinedGids.Contains(gid)
                ? GlyfImages.ExpandedSimpleGlyph(3)
                : GlyfImages.NoContourGlyphRecord());
        var (loca, glyf, _) = GlyfImages.Assemble(records);
        var tables = new List<(uint, byte[])>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(numGlyphs)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 5)))
        };
        tables.AddRange(extra);
        return SubsetFontFixture.Face([.. tables]);
    }

    // ---- variable COLRv1: the ItemVariationStore <-> fvar signature pair ---------

    // A 34-byte v1 header with big-endian writers, mirroring the read suite's wire
    // builder (kept local to the fixture).
    private sealed class ColrWire
    {
        private readonly List<byte> _data = new(new byte[34]);

        internal ColrWire() => _data[1] = 1; // version 1

        internal int Position => _data.Count;

        internal ColrWire U8(int v) { _data.Add(unchecked((byte)v)); return this; }

        internal ColrWire U16(int v)
        {
            _data.Add(unchecked((byte)(v >> 8)));
            _data.Add(unchecked((byte)v));
            return this;
        }

        internal ColrWire U24(int v)
        {
            _data.Add(unchecked((byte)(v >> 16)));
            _data.Add(unchecked((byte)(v >> 8)));
            _data.Add(unchecked((byte)v));
            return this;
        }

        internal ColrWire U32(uint v) => U16((int)(v >> 16)).U16((int)(v & 0xFFFF));

        internal void Patch16(int at, int v)
        {
            _data[at] = unchecked((byte)(v >> 8));
            _data[at + 1] = unchecked((byte)v);
        }

        internal void Patch32(int at, uint v)
        {
            Patch16(at, (int)(v >> 16));
            Patch16(at + 2, (int)(v & 0xFFFF));
        }

        internal byte[] ToBytes() => [.. _data];
    }

    // Root 5 -> PaintGlyph(9) -> PaintVarSolid, one axis, one VarData row, with the
    // format-0 map and store at their canonical tail positions.
    private static byte[] VariableColrV1RootGlyphPaint()
    {
        var wire = new ColrWire();
        wire.Patch32(14, 34);                         // BaseGlyphList
        wire.U32(1);                                  // one base row
        wire.U16(5).U32(10);                          // root 5 -> PaintGlyph@44
        wire.U8(10).U24(6).U16(9);                    // PaintGlyph@44 -> @50, glyph 9
        wire.U8(3).U16(1).U16(0x4000).U32(0);         // PaintVarSolid@50
        int map = wire.Position;
        wire.U8(0).U8(0).U16(1).U8(0);                // map: format 0, one (0,0) entry
        int store = wire.Position;
        wire.U16(1).U32(12).U16(1).U32(22);           // IVS, one VarData
        wire.U16(1).U16(1).U16(0).U16(0x4000).U16(0x4000); // one axis region
        wire.U16(1).U16(0).U16(1).U16(0).U8(10);      // one row, one int8 delta
        wire.Patch32(26, (uint)map);
        wire.Patch32(30, (uint)store);
        return wire.ToBytes();
    }

    // A v1 table that also keeps the v0 compatibility arrays: root 5's paint graph
    // plus base 7 -> layer 10 (the "fall back to v0" shape a v1 renderer queries).
    private static byte[] ColrV1WithV0Compat()
    {
        var wire = new ColrWire();
        wire.Patch16(2, 1);                           // numBaseGlyphRecords
        wire.Patch32(4, 55);                          // baseGlyphRecordsOffset
        wire.Patch32(8, 61);                          // layerRecordsOffset
        wire.Patch16(12, 1);                          // numLayerRecords
        wire.Patch32(14, 34);                         // BaseGlyphList
        wire.U32(1);
        wire.U16(5).U32(10);                          // root 5 -> PaintGlyph@44
        wire.U8(10).U24(6).U16(9);                    // -> PaintSolid@50, glyph 9
        wire.U8(2).U16(1).U16(0x4000);                // PaintSolid@50
        wire.U16(7).U16(0).U16(1);                    // base record @55
        wire.U16(10).U16(0xFFFF);                     // layer record @61
        return wire.ToBytes();
    }

    // The store must be read against the fvar axis count; a variation-bearing COLRv1
    // was unreadable to the closure, and retain turned that refusal into a success
    // whose PaintGlyph target lost its outline.
    [Fact]
    public void RetainClosesAVariableColrV1GraphWithTheFvarAxisAuthority()
    {
        var face = OutlinedSpine(16, [9],
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar(1)),
            (ColrTag, VariableColrV1RootGlyphPaint()));
        var status = SubsetOperation.TryPrepare(Request(face, [5], SubsetMode.RetainGids),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        var (start, end) = GlyphSpan(result!, 9);
        Assert.True(end > start, "the variable COLRv1 PaintGlyph target lost its outline");
        Assert.Equal(VariableColrV1RootGlyphPaint(), FontTable(result!, ColrTag));
        Assert.Equal(default, diagnostic);
    }

    // The same wire on the compact face: the graph is now readable, the target is
    // retained and remapped, and the variation slots travel with the rebuilt table.
    [Fact]
    public void CompactRemapsAVariableColrV1Graph()
    {
        var face = OutlinedSpine(16, [9],
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar(1)),
            (ColrTag, VariableColrV1RootGlyphPaint()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5], SubsetMode.CompactRemap),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([0, 5, 9],
            result!.Map!.Entries.Select(e => (int)e.InputGid).ToArray());
        byte[] wire = FontTable(result, ColrTag);
        status = ColrView.TryOpen(wire, out var view, out diagnostic,
            DataLocation.Anonymous(ColrLayout.Tag));
        Assert.Equal(ReadStatus.Done, status);
        status = view.TryReadTable(3, null, 1, out var colrOut, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(colrOut!.VarIndexMap);
        Assert.NotNull(colrOut.ItemVariationStore);
        var glyph = Assert.IsType<ColrPaintGlyph>(
            colrOut.Paints[colrOut.BaseGlyphPaints[0].PaintNodeId]);
        Assert.Equal((ushort)2, glyph.GlyphId);
        Assert.IsType<ColrPaintVarSolid>(colrOut.Paints[glyph.PaintNodeId]);
        Assert.Equal(default, diagnostic);
    }

    // A v1 table can also carry the v0 records; a v1 renderer falls back to them, so
    // retain must keep the layer outlines those records paint.
    [Fact]
    public void RetainClosesV0CompatRecordsInsideAV1Table()
    {
        var face = OutlinedSpine(16, [9, 10], (ColrTag, ColrV1WithV0Compat()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 7], SubsetMode.RetainGids),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        foreach (int gid in (int[])[9, 10])
        {
            var (start, end) = GlyphSpan(result!, gid);
            Assert.True(end > start, $"referenced glyph {gid} lost its outline");
        }
        Assert.Equal(ColrV1WithV0Compat(), FontTable(result!, ColrTag));
        Assert.Equal(default, diagnostic);
    }

    // The retained-slot outline span from the output loca (long format on these
    // fixtures); an unretained glyph's slot stays empty (equal offsets).
    private static (uint Start, uint End) GlyphSpan(SubsetResult result, int glyphId)
    {
        byte[] loca = FontTable(result, SubsetFontFixture.LocaTag);
        return (BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(4 * glyphId)),
            BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(4 * (glyphId + 1))));
    }

    // Retain keeps the COLRv1 table raw, so the paint graph's glyph reference survives
    // verbatim; the referenced outline must survive with it, or the raw reference draws
    // an empty glyph (compact remaps and keeps it via the same closure).
    [Fact]
    public void RetainColrV1KeepsThePaintGlyphOutline()
    {
        var records = new List<byte[]>();
        for (int gid = 0; gid < 16; gid++)
            records.Add(gid == 9
                ? GlyfImages.ExpandedSimpleGlyph(3)
                : GlyfImages.NoContourGlyphRecord());
        var (loca, glyf, _) = GlyfImages.Assemble(records);
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(16)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 5))),
            (ColrTag, ColrV1RootGlyphPaint()));
        var status = SubsetOperation.TryPrepare(Request(face, [5], SubsetMode.RetainGids),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        var (start, end) = GlyphSpan(result!, 9);
        Assert.True(end > start, "the retained COLRv1 PaintGlyph target lost its outline");
        var (otherStart, otherEnd) = GlyphSpan(result!, 10);
        Assert.Equal(otherStart, otherEnd);
        Assert.Equal(default, diagnostic);
    }

    // COLR v0 has no paint graph, but its base rows keep their layer slices in retain
    // mode: every layer of a surviving base glyph must keep its outline.
    [Fact]
    public void RetainColrV0KeepsLayerOutlines()
    {
        var records = new List<byte[]>();
        for (int gid = 0; gid < 8; gid++)
            records.Add(gid is 6 or 7
                ? GlyfImages.ExpandedSimpleGlyph(3)
                : GlyfImages.NoContourGlyphRecord());
        var (loca, glyf, _) = GlyfImages.Assemble(records);
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(8)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 5))),
            (ColrTag, ColrV0()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5], SubsetMode.RetainGids),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        foreach (int gid in (int[])[6, 7])
        {
            var (start, end) = GlyphSpan(result!, gid);
            Assert.True(end > start, $"layer glyph {gid} lost its outline");
        }
        // Not a layer of the surviving base row: the retain face still clears it.
        var (otherStart, otherEnd) = GlyphSpan(result!, 2);
        Assert.Equal(otherStart, otherEnd);
        Assert.Equal(default, diagnostic);
    }
}
