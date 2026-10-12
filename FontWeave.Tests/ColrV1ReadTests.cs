using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-02 commit 2's v1 read chain: the paint graph walk, the
/// ClipList face, the compat-record grace, the v1-first lookup and every hard
/// refusal the card §3 named - each keyed on status/code/field/Detail/Expected.
/// The synthetic wires are minimal node fixtures (the card's no-giant-font line);
/// the real-corpus witness reconciliation belongs to the frozen validation
/// gate.</summary>
public sealed class ColrV1ReadTests
{
    private const uint ColrTag = 0x434F4C52;
    private static readonly DataLocation Source = new(11, 0, ColrTag, -1,
        DataField.None, OffsetBasis.Table, 0);

    /// <summary>A growable big-endian wire builder with a 34-byte v1 header
    /// whose five offsets get patched once the bodies know their positions.</summary>
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

    // header layout: version@0, numBase@2, baseOff@4, layerOff@8, numLayers@12,
    // bgl@14, ll@18, clipList@22, varIndexMap@26, ivs@30.
    private static Wire NewWire()
    {
        var w = new Wire();
        w.Reserve(34);
        w.Patch16(0, 1);                     // version 1
        return w;
    }

    [Fact]
    public void MinimalSolidGraphReadsAndMaterializes()
    {
        var w = NewWire();
        w.Patch32(14, 34);                   // baseGlyphListOffset
        w.U32(1);                            // BGL count
        w.U16(9);                            // glyph
        w.U32(10);                           // paintOffset from BGL -> 44
        w.U8(2); w.U16(3); w.U16(0x4000);    // PaintSolid palette=3 alpha=0.25
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(256, 4, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(1, model!.Version);
        Assert.Equal(1, model.BaseGlyphPaintCount);
        Assert.Equal((ushort)9, model.BaseGlyphPaints[0].GlyphId);
        Assert.Equal(0, model.BaseGlyphPaints[0].PaintNodeId);
        Assert.Equal(1, model.PaintCount);
        var solid0 = Assert.IsType<ColrPaintSolid>(model.Paints[0]);
        Assert.Equal(0, solid0.Id);
        Assert.Equal((ushort)3, solid0.PaletteIndex);
        Assert.Equal((ushort)0x4000, solid0.Alpha);
        Assert.Equal(ReadStatus.Done, view.TryFindBaseGlyphV1(9, out int ordinal, out diagnostic));
        Assert.Equal(0, ordinal);
        Assert.Equal(ReadStatus.Done, view.TryFindColorGlyph(9, out var hit, out diagnostic));
        Assert.True(hit.FromPaintGraph);
        Assert.Equal(0, hit.Ordinal);
    }

    [Fact]
    public void SharedSubgraphsConvergeAndChildrenSortBeforeParents()
    {
        // Wire: BGL@34 (2 records) | root0 glyph@50 | root1 colrGlyph@56 |
        // shared solid@59. root1's PaintColrGlyph targets root0 - the legal
        // indirect back-reference (L547).
        var w = NewWire();
        w.Patch32(14, 34);
        w.U32(2);
        w.U16(10); w.U32(16);                // record 0 -> 50
        w.U16(11); w.U32(22);                // record 1 -> 56
        w.U8(10); w.U24(9); w.U16(50);       // PaintGlyph -> shared X at 59
        w.U8(11); w.U16(10);                 // PaintColrGlyph -> root0 (backwards, indirect)
        w.U8(2); w.U16(1); w.U16(0x4000);
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(256, 4, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(3, model!.PaintCount);
        // The canonical order is parents first (L476): the PaintColrGlyph's
        // indirect edge makes glyph0 its child, so glyph1 precedes glyph0 and
        // both precede the shared leaf.
        var glyph1 = Assert.IsType<ColrPaintColrGlyph>(model.Paints[0]);
        var glyph0 = Assert.IsType<ColrPaintGlyph>(model.Paints[1]);
        var x = Assert.IsType<ColrPaintSolid>(model.Paints[2]);
        Assert.Equal(x.Id, glyph0.PaintNodeId);
        Assert.Equal(glyph0.Id, glyph1.TargetNodeId);
        Assert.True(glyph1.Id < glyph0.Id && glyph0.Id < x.Id);
        Assert.Equal(0, glyph0.Depth);
        Assert.Equal(0, glyph1.Depth);
        Assert.Equal(1, x.Depth);
        Assert.Equal(ReadStatus.Done, view.TryFindColorGlyph(11, out var hit, out diagnostic));
        Assert.True(hit.FromPaintGraph);
    }

    [Fact]
    public void PaintColrLayersSliceReadsWithDeadEntriesUntouched()
    {
        // Wire: BGL@34 (root -> the PaintColrLayers node) | LL@44 (3 entries:
        // leafA, X, dead leafB) | layers node@60 | leafA glyph@66 | X@72 |
        // leafB@77. The dead entry is bounds-checked only and materializes -1.
        var w = NewWire();
        w.Patch32(14, 34);
        w.Patch32(18, 44);
        w.U32(1);
        w.U16(7); w.U32(26);                 // root -> 60
        w.U32(3);                            // LL count
        w.U32(22);                           // entry 0 -> 66 (leafA)
        w.U32(28);                           // entry 1 -> 72 (X)
        w.U32(33);                           // entry 2 -> 77 (leafB, dead)
        w.U8(1); w.U8(2); w.U32(0);          // PaintColrLayers: slice entries 0..1
        w.U8(10); w.U24(6); w.U16(51);       // leafA -> X (forward)
        w.U8(2); w.U16(1); w.U16(0x4000);    // X
        w.U8(2); w.U16(2); w.U16(0x8000);    // leafB (only bounds-checked)
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(256, 4, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(3, model!.LayerPaintNodeIds.Count);
        Assert.True(model.LayerPaintNodeIds[0] >= 0);
        Assert.True(model.LayerPaintNodeIds[1] >= 0);
        Assert.Equal(-1, model.LayerPaintNodeIds[2]);
        Assert.Equal(3, model.PaintCount);   // layers, leafA, X - the dead leaf is no graph member
        var layers = Assert.IsType<ColrPaintLayers>(model.Paints[model.BaseGlyphPaints[0].PaintNodeId]);
        Assert.Equal(2, layers.LayerNodeIds.Count);
        Assert.Equal(model.Paints[layers.LayerNodeIds[1]].Id, layers.LayerNodeIds[1]);
    }

    [Fact]
    public void GradientColorLineReadsWithTheCrossTablePaletteDomain()
    {
        var w = NewWire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(5); w.U32(10);                 // root -> 44
        w.U8(4); w.U24(16);                  // PaintLinearGradient, colorLine at +16
        w.U16(1); w.U16(2); w.U16(3); w.U16(4); w.U16(5); w.U16(6);
        w.U8(0); w.U16(2);                   // extend PAD, 2 stops (wire-unsorted on purpose)
        w.U16(0x4000); w.U16(0xFFFF); w.U16(0xC000);   // 0.5, foreground sentinel, observed alpha
        w.U16(0x0000); w.U16(1); w.U16(0x0000);        // 0.0 - out of order, legal (L833)
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(256, 4, out var model, out diagnostic));
        var linear = Assert.IsType<ColrPaintLinearGradient>(model!.Paints[0]);
        Assert.Equal(0, linear.ColorLine.Extend);
        Assert.Equal(2, linear.ColorLine.Stops.Count);
        Assert.Equal((short)0x4000, linear.ColorLine.Stops[0].StopOffset);
        Assert.Equal((ushort)0xFFFF, linear.ColorLine.Stops[0].PaletteIndex);
        Assert.Equal((short)0, linear.ColorLine.Stops[1].StopOffset);
        Assert.Equal(1, model.PaintCount);

        // The palette domain: entry 4 with cpal=4 refuses at the stop's index word.
        var w2 = NewWire();
        w2.Patch32(14, 34);
        w2.U32(1);
        w2.U16(5); w2.U32(10);
        w2.U8(4); w2.U24(16);
        for (int i = 0; i < 6; i++) w2.U16(0);
        w2.U8(0); w2.U16(1);
        w2.U16(0); w2.U16(4); w2.U16(0);
        byte[] wire2 = w2.ToBytes();
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidIndex, view2.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(4uL, diagnostic.Detail);
        Assert.Equal(4uL, diagnostic.Expected);
    }

    [Fact]
    public void ClipListReadsProjectsAndRefusesThePinnedFaces()
    {
        byte[] wire = ClipFixture(out Wire w, out _);
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(256, 4, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(2, model!.ClipCount);
        Assert.Equal((short)-10, model.Clips[0].Box.XMin);
        Assert.Equal(ReadStatus.Done, view.TryGetClipBox(15, out var box, out diagnostic));
        Assert.Equal((short)-10, box.XMin);
        Assert.Equal(ReadStatus.Done, view.TryGetClipBox(29, out box, out diagnostic));
        Assert.Equal((short)50, box.XMax);
        Assert.Equal(ReadStatus.NotFound, view.TryGetClipBox(5, out _, out diagnostic));
        Assert.Equal(ReadStatus.NotFound, view.TryGetClipBox(35, out _, out diagnostic));
        Assert.Equal(DiagnosticCode.GlyphNotMapped, diagnostic.Code);

        // (a) ClipList format 2 - the T6-03 boundary witness.
        Assert.Equal(ReadStatus.UnsupportedFormat, ClipCase(256, m => m.Patch8(60, 2),
            out var diag));
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diag.Code);
        Assert.Equal(2uL, diag.Detail);
        Assert.Equal(1uL, diag.Expected);
        Assert.Equal(DataField.Version, diag.Location.Field);

        // (b) unsorted starts (the L737 ascent face: equal start refuses first).
        Assert.Equal(ReadStatus.InvalidFormat, ClipCase(256, m => m.Patch16(72, 10),
            out diag));
        Assert.Equal(DiagnosticCode.InvalidValue, diag.Code);

        // (c) overlap with distinct starts (the L747 face; equal starts too).
        Assert.Equal(ReadStatus.InvalidFormat, ClipCase(256, m => m.Patch16(72, 19),
            out diag));
        Assert.Equal(DiagnosticCode.OverlappingRange, diag.Code);
        Assert.Equal(19uL, diag.Detail);
        Assert.Equal(20uL, diag.Expected);

        // (d) reversed range (the OQ-T6-02-9 face).
        Assert.Equal(ReadStatus.InvalidFormat, ClipCase(256, m => m.Patch16(67, 9),
            out diag));
        Assert.Equal(DiagnosticCode.InvalidValue, diag.Code);
        Assert.Equal(10uL, diag.Detail);
        Assert.Equal(9uL, diag.Expected);

        // (e) glyph domain (the OQ-T6-02-10 face).
        Assert.Equal(ReadStatus.InvalidIndex, ClipCase(300, m => m.Patch16(67, 300),
            out diag));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diag.Code);

        // (f) ClipBox format 2 without a store (OQ-T6-03-3's degraded face, the
        // L1554 "must be ignored / should be set to zero" wording): the box
        // reads, the varIndexBase rides verbatim as an observation.
        byte[] wireF = ClipFixture(out Wire wF, out _);
        wF.Patch8(60 + 5 + 14, 2);           // the first clip's box goes format 2
        wireF = wF.ToBytes();
        status = ColrView.TryOpen(wireF, out var viewF, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, viewF.ValidateTable(256, 4, out diag));
        Assert.Equal(ReadStatus.Done, viewF.TryGetClipBox(15, out var box2, out diag));
        Assert.Equal(2, box2.Format);
        Assert.Equal(0x01000000u, box2.VarIndexBase);   // box 2's head rides beneath
        Assert.Equal((short)-10, box2.XMin);
        Assert.Equal((short)100, box2.XMax);
        status = viewF.TryReadTable(256, 4, out var modelF, out diag);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(modelF);
        Assert.Equal(2, modelF!.Clips[0].Box.Format);
        Assert.Equal(1, modelF.Clips[1].Box.Format);
    }

    private static byte[] ClipFixture(out Wire w, out int clipList)
    {
        w = NewWire();
        clipList = 60;
        w.Patch32(14, 34);
        w.Patch32(22, (uint)clipList);
        w.U32(1);
        w.U16(7); w.U32(10);                 // root -> 44
        w.U8(2); w.U16(1); w.U16(0x4000);
        while (w.Position < clipList) w.U8(0);
        w.U8(1); w.U32(2);                   // ClipList format 1, 2 clips
        w.U16(10); w.U16(19); w.U24(5 + 14);
        w.U16(20); w.U16(29); w.U24(5 + 14 + 9);
        w.U8(1); w.U16(0xFFF6); w.U16(0xFFF6); w.U16(0x0064); w.U16(0x0064);
        w.U8(1); w.U16(0x0000); w.U16(0x0000); w.U16(0x0032); w.U16(0x0032);
        return w.ToBytes();
    }

    private static ReadStatus ClipCase(int numGlyphs, Action<Wire> mutate,
        out FontDiagnostic diagnostic)
    {
        byte[] wire = ClipFixture(out Wire w, out _);
        mutate(w);
        wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        if (status != ReadStatus.Done)
        {
            diagnostic = default;
            return ReadStatus.InvalidFormat;
        }
        return view.ValidateTable(numGlyphs, 4, out diagnostic);
    }

    [Fact]
    public void CompatRecordsAndZeroFormsAnswerUnderTheV1Grace()
    {
        // All-zero compat with an empty BGL: the OQ-T6-02-10 zero-form matrix.
        var w = NewWire();
        w.Patch32(14, 34);
        w.U32(0);                            // empty BGL
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(256, 4, out var model, out diagnostic));
        Assert.NotNull(model);
        Assert.Equal(0, model!.BaseGlyphPaintCount);
        Assert.Equal(0, model.PaintCount);
        Assert.Equal(0, model.BaseGlyphCount);

        // Compat records re-answer the T6-01 rules; the graph walk still runs.
        var w2 = NewWire();
        w2.Patch32(14, 34);
        w2.Patch16(2, 1);                    // numBaseGlyphRecords
        w2.Patch32(4, 60);                   // baseGlyphRecordsOffset
        w2.Patch32(8, 54);                   // layerRecordsOffset
        w2.Patch16(12, 1);                   // numLayerRecords
        w2.U32(1);                           // BGL count
        w2.U16(20); w2.U32(10);              // root solid at 44 (gid 20, not the compat's 7)
        w2.U8(2); w2.U16(0xFFFF); w2.U16(0x4000);
        while (w2.Position < 54) w2.U8(0);
        w2.U16(7); w2.U16(2);                // layer record at 54
        while (w2.Position < 60) w2.U8(0);
        w2.U16(7); w2.U16(0); w2.U16(1);     // base record at 60
        byte[] wire2 = w2.ToBytes();
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view2.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(ReadStatus.Done, view2.TryReadTable(256, 4, out var model2, out diagnostic));
        Assert.NotNull(model2);
        Assert.Equal(1, model2!.BaseGlyphCount);
        Assert.Equal(1, model2.LayerCount);
        // v1-first: glyph 20 is a BGL root; glyph 7 lives only in the compat
        // records and the lookup falls through (L653).
        Assert.Equal(ReadStatus.Done, view2.TryFindColorGlyph(20, out var hit20, out diagnostic));
        Assert.True(hit20.FromPaintGraph);
        Assert.Equal(ReadStatus.Done, view2.TryFindColorGlyph(7, out var hit, out diagnostic));
        Assert.False(hit.FromPaintGraph);
        Assert.Equal(0, hit.Ordinal);

        // A compat count with a null offset refuses (count>0, offset==0).
        var w3 = NewWire();
        w3.Patch32(14, 34);
        w3.Patch16(2, 1);
        w3.U32(0);
        byte[] wire3 = w3.ToBytes();
        status = ColrView.TryOpen(wire3, out var view3, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view3.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(4uL, diagnostic.Location.Offset);
    }

    [Fact]
    public void HeaderFacesRefuseExactlyAsTheCardPins()
    {
        // OQ-T6-02-1: baseGlyphListOffset == 0 on v1.
        var w = NewWire();
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(14uL, diagnostic.Location.Offset);
        Assert.Equal(0uL, diagnostic.Detail);
        Assert.Equal(1uL, diagnostic.Expected);

        // OQ-T6-03-8's matrix over the old receipt window: the map slot's
        // out-of-window face stays mechanical, but a map with no
        // ItemVariationStore is now the dangling form and refuses (L643's
        // "in conjunction with" - a map alone has no semantics).
        var w2 = NewWire();
        w2.Patch32(14, 34);
        w2.Patch32(26, 34);                  // varIndexMap window at 34
        w2.U32(0);                           // empty BGL (the count word)
        byte[] wire2 = w2.ToBytes();
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view2.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(26uL, diagnostic.Location.Offset);
        Assert.Equal(34uL, diagnostic.Detail);
        Assert.Equal(0uL, diagnostic.Expected);
        Assert.Equal(ReadStatus.InvalidFormat,
            view2.TryReadTable(256, 4, out var model2, out diagnostic));
        Assert.Null(model2);

        // The same slot outside the window refuses mechanically.
        var w3 = NewWire();
        w3.Patch32(14, 34);
        w3.Patch32(30, 999);                 // IVS beyond the table
        w3.U32(0);
        byte[] wire3 = w3.ToBytes();
        status = ColrView.TryOpen(wire3, out var view3, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidOffset, view3.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
        Assert.Equal(30uL, diagnostic.Location.Offset);
    }

    [Fact]
    public void BaseGlyphListFacesRefuseKeyByKey()
    {
        // (a) unsorted (duplicated glyph).
        var w = NewWire();
        w.Patch32(14, 34);
        w.U32(2);
        w.U16(9); w.U32(16);
        w.U16(9); w.U32(16);
        w.U8(2); w.U16(1); w.U16(0x4000);
        w.U8(2); w.U16(1); w.U16(0x4000);
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.GlyphId, diagnostic.Location.Field);
        Assert.Equal(9uL, diagnostic.Detail);
        Assert.Equal(9uL, diagnostic.Expected);

        // (b) glyph domain.
        var w2 = NewWire();
        w2.Patch32(14, 34);
        w2.U32(1);
        w2.U16(256); w2.U32(10);
        w2.U8(2); w2.U16(1); w2.U16(0x4000);
        byte[] wire2 = w2.ToBytes();
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidIndex, view2.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);

        // (c) backward direct link (the L476 face): the target lands on the
        // record itself.
        var w3 = NewWire();
        w3.Patch32(14, 34);
        w3.U32(1);
        w3.U16(9); w3.U32(4);                // root = 38 == the record row
        byte[] wire3 = w3.ToBytes();
        status = ColrView.TryOpen(wire3, out var view3, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view3.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);

        // (d) paint target beyond the window (the L564(1) face).
        var w4 = NewWire();
        w4.Patch32(14, 34);
        w4.U32(1);
        w4.U16(9); w4.U32(0xFFFFFF);
        byte[] wire4 = w4.ToBytes();
        status = ColrView.TryOpen(wire4, out var view4, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidOffset, view4.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
    }

    [Fact]
    public void PaintWalkFacesRefuseKeyByKey()
    {
        // (a) unknown paint format.
        var w = NewWire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(9); w.U32(10);
        w.U8(33);
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.UnsupportedFormat, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(DataField.Version, diagnostic.Location.Field);
        Assert.Equal(33uL, diagnostic.Detail);
        Assert.Equal(0uL, diagnostic.Expected);

        // (b) a var paint with no ItemVariationStore - the L871 must-not is the
        // structural refusal at the format word (OQ-T6-03-2), a value face and
        // no longer the T6-02 UnsupportedFormat boundary.
        var w2 = NewWire();
        w2.Patch32(14, 34);
        w2.U32(1);
        w2.U16(9); w2.U32(10);
        w2.U8(3); w2.U16(1); w2.U16(0x4000); w2.U32(0);
        byte[] wire2 = w2.ToBytes();
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view2.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Version, diagnostic.Location.Field);
        Assert.Equal(44uL, diagnostic.Location.Offset);
        Assert.Equal(3uL, diagnostic.Detail);
        Assert.Equal(0uL, diagnostic.Expected);

        // (c) the two-node PaintColrGlyph cycle (the corpus witness shape).
        var w3 = NewWire();
        w3.Patch32(14, 34);
        w3.U32(2);
        w3.U16(10); w3.U32(16);              // A at 50
        w3.U16(11); w3.U32(19);              // B at 53
        w3.U8(11); w3.U16(11);               // A -> B
        w3.U8(11); w3.U16(10);               // B -> A: the cycle edge
        byte[] wire3 = w3.ToBytes();
        status = ColrView.TryOpen(wire3, out var view3, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view3.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(50uL, diagnostic.Detail);   // the on-path node the edge closes onto

        // (d) PaintColrLayers without a LayerList (the L565 face).
        var w4 = NewWire();
        w4.Patch32(14, 34);
        w4.U32(1);
        w4.U16(9); w4.U32(10);
        w4.U8(1); w4.U8(1); w4.U32(0);
        byte[] wire4 = w4.ToBytes();
        status = ColrView.TryOpen(wire4, out var view4, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view4.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(18uL, diagnostic.Location.Offset);

        // (e) slice overflow: the slice asks for entries 0..1, the LL has one.
        var w5 = NewWire();
        w5.Patch32(14, 34);
        w5.Patch32(18, 44);
        w5.U32(1);
        w5.U16(9); w5.U32(18);               // root -> the layers node at 52
        w5.U32(1);                           // LL count 1
        w5.U32(8);                           // entry 0 -> 52 (the node itself)
        w5.U8(1); w5.U8(2); w5.U32(0);
        byte[] wire5 = w5.ToBytes();
        status = ColrView.TryOpen(wire5, out var view5, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view5.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(2uL, diagnostic.Detail);
        Assert.Equal(1uL, diagnostic.Expected);

        // (f) transform window beyond the table (the frozen malformed-witness face).
        var w6 = NewWire();
        w6.Patch32(14, 34);
        w6.U32(1);
        w6.U16(9); w6.U32(10);
        w6.U8(12); w6.U24(7); w6.U24(40);    // transformOffset runs past the end
        w6.U8(2); w6.U16(1); w6.U16(0x4000);
        byte[] wire6 = w6.ToBytes();
        status = ColrView.TryOpen(wire6, out var view6, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidOffset, view6.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);

        // (g) PaintColrGlyph without a BGL record (the L566(3) face).
        var w7 = NewWire();
        w7.Patch32(14, 34);
        w7.U32(1);
        w7.U16(9); w7.U32(10);
        w7.U8(11); w7.U16(8);
        byte[] wire7 = w7.ToBytes();
        status = ColrView.TryOpen(wire7, out var view7, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidIndex, view7.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.GlyphNotMapped, diagnostic.Code);

        // (h) direct-link self edge (PaintGlyph pointing at itself).
        var w8 = NewWire();
        w8.Patch32(14, 34);
        w8.U32(1);
        w8.U16(9); w8.U32(10);
        w8.U8(10); w8.U24(0); w8.U16(50);    // child target == the node itself
        byte[] wire8 = w8.ToBytes();
        status = ColrView.TryOpen(wire8, out var view8, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view8.ValidateTable(256, 4, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
    }

    [Fact]
    public void DepthCapIsAResourceLimitFace()
    {
        // A 130-node PaintGlyph chain: node d sits at depth d; the cap is 128.
        var w = NewWire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(9); w.U32(10);
        for (int i = 0; i < 130; i++)
        {
            w.U8(10); w.U24(6); w.U16(50);   // child at +6
        }
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        ReadStatus readStatus = view.ValidateTable(140, 4, out var diagnostic);
        Assert.True(readStatus == ReadStatus.ResourceLimit,
            $"got {readStatus} {diagnostic.Code} @{diagnostic.Location.Offset} expected {diagnostic.Expected}");
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
    }

    [Fact]
    public void CompositeAndObservationFacesStayLegal()
    {
        // compositeMode 28 (above the enumeration) and alpha out of [0,1] are
        // the renderer's fallback domain - the wire validates and the values
        // travel verbatim (OQ-T6-02-8).
        var w = NewWire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(9); w.U32(10);                 // root composite at 44
        w.U8(32); w.U24(8); w.U8(28); w.U24(13);
        w.U8(2); w.U16(1); w.U16(0xC000);    // source solid at 52, alpha 1.5 (reserved wording)
        w.U8(2); w.U16(2); w.U16(0x0000);    // backdrop solid at 57
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(256, 4, out var model, out diagnostic));
        var compositeNode = Assert.IsType<ColrPaintComposite>(model!.Paints[0]);
        Assert.Equal((byte)28, compositeNode.CompositeMode);
        Assert.Equal(3, model.PaintCount);
        var source = Assert.IsType<ColrPaintSolid>(model.Paints[compositeNode.SourceNodeId]);
        var backdrop = Assert.IsType<ColrPaintSolid>(model.Paints[compositeNode.BackdropNodeId]);
        Assert.Equal((ushort)0xC000, source.Alpha);
        Assert.Equal((ushort)2, backdrop.PaletteIndex);
    }

    [Fact]
    public void AffineAndScaleFamiliesReadByteFaithfully()
    {
        // Wire: transform@44 | skew@51 | solid@59 | affine@64.
        var w = NewWire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(9); w.U32(10);
        w.U8(12); w.U24(7); w.U24(20);       // transform: child at 51, affine at 64
        w.U8(28); w.U24(8); w.U16(0x2000); w.U16(0x4000);  // skew: child at 59
        w.U8(2); w.U16(3); w.U16(0x4000);    // the shared leaf solid
        w.U32(0x00010000); w.U32(0); w.U32(0);
        w.U32(0x00010000); w.U32(0xFFF00000); w.U32(0x00040000);
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(256, 4, out var model, out diagnostic));
        var transform = Assert.IsType<ColrPaintTransform>(model!.Paints[0]);
        Assert.Equal(0x00010000u, transform.Transform.Xx);
        Assert.Equal(0xFFF00000u, transform.Transform.Dx);
        var skew = Assert.IsType<ColrPaintSkew>(model.Paints[1]);
        Assert.Equal((ushort)0x2000, skew.XSkewAngle);
        Assert.Equal(skew.Id, transform.PaintNodeId);
        Assert.Equal(2, model.Paints[skew.PaintNodeId].Id);
    }
}
