using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-02 commit 3's write side: the canonical parents-first
/// emission, the source-faithful identity on already-canonical wires, the six
/// value-level edit families with their refusal wings, and the factory's
/// re-answered invariants. Every compiled wire re-reads through the View (the
/// write-read-write fixed point); no compiled byte is trusted without a
/// re-read.</summary>
public sealed class ColrV1WriteTests
{
    private const uint ColrTag = 0x434F4C52;
    private static readonly DataLocation Source = new(13, 0, ColrTag, -1,
        DataField.None, OffsetBasis.Table, 0);

    private static ColrTable Read(byte[] wire)
    {
        ReadStatus status = ColrView.TryOpen(wire, out var view, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        status = view.TryReadTable(256, 4, out var model, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(model);
        return model!;
    }

    private static byte[] Compile(ColrTable model)
    {
        ReadStatus status = ColrTableCompiler.TryCompile(model, out var bytes,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(bytes);
        return bytes!;
    }

    [Fact]
    public void AlreadyCanonicalWiresRecompileByteIdentical()
    {
        // The minimal solid fixture: header | BGL | the paint region - already
        // the canonical layout, so the identity tier holds byte for byte.
        var w = NewWire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(9); w.U32(10);
        w.U8(2); w.U16(3); w.U16(0x4000);
        byte[] wire = w.ToBytes();
        ColrTable model = Read(wire);
        byte[] compiled = Compile(model);
        Assert.Equal(wire, compiled);
        // The fixed point: re-read the compiled wire, compile again - identical.
        Assert.Equal(compiled, Compile(Read(compiled)));
    }

    [Fact]
    public void CompositeGraphRecompilesByteIdenticalWithAuxBehindNodes()
    {
        // header | BGL | composite@44 | source@52 | backdrop@57 - the source
        // already places parents before children, so identity holds; the affine
        // fixture additionally exercises the transform's aux riding behind its
        // node.
        var w = NewWire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(9); w.U32(10);
        w.U8(32); w.U24(8); w.U8(3); w.U24(13);
        w.U8(2); w.U16(1); w.U16(0x4000);
        w.U8(2); w.U16(2); w.U16(0x0000);
        byte[] wire = w.ToBytes();
        ColrTable model = Read(wire);
        Assert.Equal(wire, Compile(model));

        var w2 = NewWire();
        w2.Patch32(14, 34);
        w2.U32(1);
        w2.U16(9); w2.U32(10);
        w2.U8(12); w2.U24(7); w2.U24(20);            // transform -> skew at 51, affine at 64
        w2.U8(28); w2.U24(8); w2.U16(0x2000); w2.U16(0x4000);
        w2.U8(2); w2.U16(3); w2.U16(0x4000);
        w2.U32(0x00010000); w2.U32(0); w2.U32(0);
        w2.U32(0x00010000); w2.U32(0xFFF00000); w2.U32(0x00040000);
        // The transform's affine rides immediately behind its node, so this
        // source (affine last) is wire-equivalent, not canonical: the plan
        // repositions it, and the fixed point still holds.
        byte[] wire2 = w2.ToBytes();
        ColrTable model2 = Read(wire2);
        byte[] once = Compile(model2);
        Assert.Equal(once, Compile(Read(once)));
        ColrTable rePlanned = Read(once);
        var plannedTransform = rePlanned.Paints.OfType<ColrPaintTransform>().Single();
        Assert.Equal(0, plannedTransform.Id);    // the root emits first
        // The aux-offset constant: the affine rides right behind the node.
        Assert.True(once.Length == 34 + 10 + 7 + 24 + 8 + 5,
            $"unexpected plan length {once.Length}");
    }

    [Fact]
    public void ClipListAndLayerListRoundTrip()
    {
        // header | BGL | LL@44 (2 entries) | layers@56 | leafA@62 | X@68.
        var w = NewWire();
        w.Patch32(14, 34);
        w.Patch32(18, 44);
        w.U32(1);
        w.U16(7); w.U32(22);                          // root -> 56 (the layers node)
        w.U32(2);                                     // LL count
        w.U32(18);                                    // entry 0 -> 62 (leafA)
        w.U32(24);                                    // entry 1 -> 68 (X)
        w.U8(1); w.U8(2); w.U32(0);                   // slice entries 0..1
        w.U8(10); w.U24(6); w.U16(51);                // leafA -> X
        w.U8(2); w.U16(1); w.U16(0x4000);             // X
        byte[] wire = w.ToBytes();
        ColrTable model = Read(wire);
        byte[] compiled = Compile(model);
        Assert.Equal(wire, compiled);
        // The clips ride the same discipline.
        int clipList = 60;
        var w2 = NewWire();
        w2.Patch32(14, 34);
        w2.Patch32(22, (uint)clipList);
        w2.U32(1);
        w2.U16(7); w2.U32(10);
        w2.U8(2); w2.U16(1); w2.U16(0x4000);
        while (w2.Position < clipList) w2.U8(0);
        w2.U8(1); w2.U32(2);
        w2.U16(10); w2.U16(19); w2.U24(5 + 14);
        w2.U16(20); w2.U16(29); w2.U24(5 + 14 + 9);
        w2.U8(1); w2.U16(0xFFF6); w2.U16(0xFFF6); w2.U16(0x0064); w2.U16(0x0064);
        w2.U8(1); w2.U16(0x0000); w2.U16(0x0000); w2.U16(0x0032); w2.U16(0x0032);
        // The clip fixture places the ClipList after the paints - the
        // canonical plan moves it before them, so this source is
        // wire-equivalent: the fixed point holds instead of identity.
        byte[] wire2 = w2.ToBytes();
        ColrTable model2 = Read(wire2);
        byte[] once2 = Compile(model2);
        Assert.Equal(once2, Compile(Read(once2)));
        Assert.Equal(44u, ReadU32(once2, 22));   // the plan's own clipList slot
    }

    [Fact]
    public void SixEditFamiliesChangeExactlyTheirOwnWords()
    {
        // A model carrying one of each family: composite root at 90, its
        // source a linear gradient at 106, its backdrop a transform at 131
        // (child translate at 139, leaf solid at 147, affine at 170), plus one
        // clip over glyph 10..19.
        var w = NewWire();
        w.Patch32(14, 34);
        w.Patch32(22, 200);
        w.U32(1);
        w.U16(9); w.U32(56);                          // composite root at 90
        while (w.Position < 90) w.U8(0);
        w.U8(32); w.U24(16); w.U8(5); w.U24(41);      // source 106, backdrop 131
        while (w.Position < 106) w.U8(0);
        w.U8(4); w.U24(16);                           // gradient at 106
        for (int i = 0; i < 6; i++) w.U16(0);
        w.U8(1); w.U16(1);                            // colorLine at 122: PAD, 1 stop
        w.U16(0); w.U16(2); w.U16(0x4000);
        while (w.Position < 131) w.U8(0);
        w.U8(12); w.U24(8); w.U24(39);                // transform at 131 -> child 139, affine 170
        while (w.Position < 139) w.U8(0);
        w.U8(14); w.U24(8); w.U16(0); w.U16(5);       // translate at 139 -> child 147
        w.U8(2); w.U16(3); w.U16(0x2000);             // solid at 147
        while (w.Position < 170) w.U8(0);
        w.U32(0x00010000); w.U32(0); w.U32(0);
        w.U32(0x00010000); w.U32(0xFFF00000); w.U32(0x00040000);
        while (w.Position < 200) w.U8(0);
        w.U8(1); w.U32(1);                            // ClipList at 200: 1 clip
        w.U16(10); w.U16(19); w.U24(5 + 7);           // the box rides right behind the one row
        w.U8(1); w.U16(0xFFF6); w.U16(0xFFF6); w.U16(0x0064); w.U16(0x0064);
        byte[] wire = w.ToBytes();
        ColrTable model = Read(wire);
        Assert.Equal(5, model.PaintCount);
        Assert.Equal(1, model.ClipCount);

        byte[] before = Compile(model);

        // (1) solid alpha: only the 2-byte alpha word moves.
        var solid = model.Paints.OfType<ColrPaintSolid>().Single();
        Assert.Equal(ReadStatus.Done, model.TryWithPaintSolidAlpha(solid.Id, 0x1234,
            out var edited, out var diagnostic));
        Assert.NotNull(edited);
        byte[] after = Compile(edited!);
        Assert.True(DiffCount(before, after) <= 2, "alpha edit moved too much");

        // (2) color stop: the stop's three words move inside the color line;
        // the sentinel passes the domain, an over-domain entry refuses.
        var linear = model.Paints.OfType<ColrPaintLinearGradient>().Single();
        Assert.Equal(ReadStatus.InvalidIndex, model.TryWithColorStop(linear.Id, 0,
            new ColrColorStop(0, 4, 0x4000), 4, out _, out diagnostic));
        Assert.Equal(4uL, diagnostic.Expected);
        Assert.Equal(ReadStatus.Done, model.TryWithColorStop(linear.Id, 0,
            new ColrColorStop(0x4000, 0xFFFF, 0x1111), 4, out edited, out diagnostic));
        after = Compile(edited!);
        var reRead = Read(after);
        var editedLinear = reRead.Paints.OfType<ColrPaintLinearGradient>().Single();
        Assert.Equal((ushort)0xFFFF, editedLinear.ColorLine.Stops[0].PaletteIndex);
        Assert.Equal((ushort)0x1111, editedLinear.ColorLine.Stops[0].Alpha);

        // (3) transform tuple.
        var transform = model.Paints.OfType<ColrPaintTransform>().Single();
        Assert.Equal(ReadStatus.Done, model.TryWithTransform(transform.Id,
            new ColrAffine2x3(0x00020000, 0, 0, 0x00020000, 0, 0), out edited,
            out diagnostic));
        after = Compile(edited!);
        reRead = Read(after);
        Assert.Equal(0x00020000u, reRead.Paints.OfType<ColrPaintTransform>().Single()
            .Transform.Xx);

        // (4) scalar family: translate's two words; the arity face refuses.
        var translate = model.Paints.OfType<ColrPaintTranslate>().Single();
        Assert.Equal(ReadStatus.InvalidFormat, model.TryWithPaintScalar(translate.Id,
            [1, 2, 3], out _, out diagnostic));
        Assert.Equal(2uL, diagnostic.Expected);
        Assert.Equal(ReadStatus.Done, model.TryWithPaintScalar(translate.Id,
            [0x0010, 0x0020], out edited, out diagnostic));
        after = Compile(edited!);
        reRead = Read(after);
        Assert.Equal((short)0x0010, reRead.Paints.OfType<ColrPaintTranslate>().Single().Dx);

        // (5) composite mode: in-domain writes, above-domain refuses.
        var composite = model.Paints.OfType<ColrPaintComposite>().Single();
        Assert.Equal(ReadStatus.InvalidFormat, model.TryWithCompositeMode(composite.Id,
            28, out _, out diagnostic));
        Assert.Equal(27uL, diagnostic.Expected);
        Assert.Equal(ReadStatus.Done, model.TryWithCompositeMode(composite.Id, 12,
            out edited, out diagnostic));
        after = Compile(edited!);
        reRead = Read(after);
        Assert.Equal((byte)12, reRead.Paints.OfType<ColrPaintComposite>().Single()
            .CompositeMode);

        // (6) clip box: the range stays, the four FWORDs move.
        Assert.Equal(ReadStatus.InvalidIndex, model.TryWithClipBox(3,
            new ColrClipBox(0, 0, 1, 1), out _, out diagnostic));
        Assert.Equal(ReadStatus.Done, model.TryWithClipBox(0,
            new ColrClipBox(-5, -6, 7, 8), out edited, out diagnostic));
        after = Compile(edited!);
        reRead = Read(after);
        Assert.Equal((short)-5, reRead.Clips[0].Box.XMin);
        Assert.Equal((ushort)10, reRead.Clips[0].StartGlyphId);

        // The family-mismatch wing: a solid id refuses the transform family.
        Assert.Equal(ReadStatus.InvalidFormat, model.TryWithTransform(solid.Id,
            new ColrAffine2x3(0, 0, 0, 0, 0, 0), out _, out diagnostic));
    }

    private static int DiffCount(byte[] left, byte[] right)
    {
        Assert.Equal(left.Length, right.Length);
        int diff = 0;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) diff++;
        }
        return diff;
    }

    [Fact]
    public void FactoryReanswersTheInvariantsAndRefusesThePinnedFaces()
    {
        // A legal factory model compiles and re-reads to the same graph. The
        // paints arrive parents first (the glyph at ordinal 0, its child solid
        // at ordinal 1 - the L476 order the compile emits in).
        var solid = new ColrPaintSolid(1, 3, 0x4000);
        var glyph = new ColrPaintGlyph(0, 1, 9);
        ReadStatus status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(9, 0)], [], [], [glyph, solid], 256, 4,
            out var model, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(model);
        byte[] compiled = Compile(model!);
        ColrTable reRead = Read(compiled);
        Assert.Equal(2, reRead.PaintCount);
        Assert.IsType<ColrPaintGlyph>(reRead.Paints[0]);
        Assert.IsType<ColrPaintSolid>(reRead.Paints[1]);
        Assert.Equal(reRead.Paints[1].Id, ((ColrPaintGlyph)reRead.Paints[0]).PaintNodeId);

        // (a) the BGL ascent face.
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(9, 0), new ColrBaseGlyphPaint(9, 0)], [], [],
            [glyph, solid], 256, 4, out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);

        // (b) the glyph domain face.
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(256, 0)], [], [], [glyph, solid], 256, 4,
            out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidIndex, status);

        // (c) the cycle face (a self-parenting glyph; the forward face answers
        // the self-edge before the path set would).
        var cyclic = new ColrPaintGlyph(0, 0, 9);
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(9, 0)], [], [], [cyclic], 256, 4,
            out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);

        // (d) the slice overflow face.
        var layers = new ColrPaintLayers(0, 0, [0, 1]);
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(9, 0)], [0], [], [layers], 256, 4,
            out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);

        // (e) the PaintColrGlyph record-presence face.
        var orphan = new ColrPaintColrGlyph(0, 8, 0);
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(9, 0)], [], [], [orphan], 256, 4,
            out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidIndex, status);
        Assert.Equal(DiagnosticCode.GlyphNotMapped, diagnostic.Code);

        // (f) the cross-table palette face.
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(9, 0)], [], [], [new ColrPaintSolid(0, 4, 0x4000)],
            256, 4, out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidIndex, status);

        // (g) the clip overlap face.
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(9, 0)], [],
            [new ColrClip(10, 19, new ColrClipBox(0, 0, 1, 1)),
             new ColrClip(19, 29, new ColrClipBox(0, 0, 1, 1))],
            [new ColrPaintSolid(0, 1, 0x4000)], 256, 4, out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.OverlappingRange, diagnostic.Code);

        // (h) the ordinal-order contract: paints[i].Id must equal i.
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(9, 0)], [], [], [solid, glyph], 256, 4,
            out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);

        // (i) the forward face: matching ordinals, a child behind its parent -
        // the compile's forward offsets could not address it (L476).
        var backwardChild = new ColrPaintGlyph(1, 0, 9);
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(9, 1)], [], [],
            [new ColrPaintSolid(0, 3, 0x4000), backwardChild], 256, 4,
            out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);

        // (j) a shared node stays shared through the factory and the compile;
        // it sits behind both of its parents (the L476 re-use order).
        var shared = new ColrPaintSolid(2, 1, 0x4000);
        var glyphA = new ColrPaintGlyph(0, 2, 10);
        var glyphB = new ColrPaintGlyph(1, 2, 11);
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(10, 0), new ColrBaseGlyphPaint(11, 1)], [], [],
            [glyphA, glyphB, shared], 256, 4, out model, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        compiled = Compile(model!);
        reRead = Read(compiled);
        var a = (ColrPaintGlyph)reRead.Paints[0];
        var b = (ColrPaintGlyph)reRead.Paints[1];
        Assert.Equal(a.PaintNodeId, b.PaintNodeId);
        Assert.Equal(3, reRead.PaintCount);
    }

    [Fact]
    public void VarSlotsEmitZeroAndShortDestinationLeavesTheSpanUntouched()
    {
        var w = NewWire();
        w.Patch32(14, 34);
        w.Patch32(26, 34);                       // varIndexMap present-but-unread
        w.U32(0);
        byte[] wire = w.ToBytes();
        ColrTable model = Read(wire);
        byte[] compiled = Compile(model);
        // The receipt rides the model, but the compiler has no var content to
        // write - the slot emits 0 (T6-03 opens it).
        Assert.Equal(34u, model.VarIndexMapOffset);
        Assert.Equal(0u, ReadU32(compiled, 26));
        Assert.Equal(0u, ReadU32(compiled, 30));

        // The atomicity face: a short destination refuses and leaves the span.
        var destination = new byte[compiled.Length - 1];
        destination.AsSpan().Fill(0xAA);
        ReadStatus status = ColrTableCompiler.TryWrite(model, destination,
            out var diagnostic);
        Assert.Equal(ReadStatus.DestinationTooSmall, status);
        Assert.All(destination, b => Assert.Equal(0xAA, b));
    }

    private static uint ReadU32(byte[] wire, int at) =>
        ((uint)wire[at] << 24) | ((uint)wire[at + 1] << 16) |
        ((uint)wire[at + 2] << 8) | wire[at + 3];

    /// <summary>A growable big-endian wire builder (same shape as the read
    /// tests' builder, kept local so both suites stay independent).</summary>
    private sealed class Wire
    {
        private byte[] _data = new byte[1024];

        public int Position { get; private set; }

        private int Jump(int bytes)
        {
            int at = Position;
            Reserve(bytes);
            return at;
        }

        public void Reserve(int bytes)
        {
            while (_data.Length < Position + bytes) Array.Resize(ref _data, _data.Length * 2);
            Position += bytes;
        }

        public void U8(uint v) => Patch8(Jump(1), v);
        public void U16(uint v) => Patch16(Jump(2), v);

        public void U24(uint v)
        {
            int at = Jump(3);
            _data[at] = (byte)((v >> 16) & 0xFF);
            _data[at + 1] = (byte)((v >> 8) & 0xFF);
            _data[at + 2] = (byte)(v & 0xFF);
        }

        public void U32(uint v) => Patch32(Jump(4), v);
        public void Patch8(int at, uint v) => _data[at] = (byte)(v & 0xFF);

        public void Patch16(int at, uint v)
        {
            _data[at] = (byte)((v >> 8) & 0xFF);
            _data[at + 1] = (byte)(v & 0xFF);
        }

        public void Patch24(int at, uint v)
        {
            _data[at] = (byte)((v >> 16) & 0xFF);
            _data[at + 1] = (byte)((v >> 8) & 0xFF);
            _data[at + 2] = (byte)(v & 0xFF);
        }

        public void Patch32(int at, uint v)
        {
            Patch16(at, v >> 16);
            Patch16(at + 2, v & 0xFFFF);
        }

        public byte[] ToBytes() => _data[..Position];
    }

    private static Wire NewWire()
    {
        var w = new Wire();
        w.Reserve(34);
        w.Patch16(0, 1);
        return w;
    }
}
