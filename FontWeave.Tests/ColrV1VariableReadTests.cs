using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-03 commit 2's var read chain: the fourteen formats' shared
/// faces through the smallest carriers (the var solid, the var gradient's
/// VarColorLine, the var scalar family's slots), the map/IVS open chain with
/// its presence matrix, the var index resolution (map soft-landing, sentinel,
/// implicit identity, the overflow narrow window) and the ClipBox format-2
/// faces. Each refusal is keyed on status/code/field/localOffset/Detail/Expected;
/// the real-corpus witness reconciliation belongs to the frozen validation gate.</summary>
public sealed class ColrV1VariableReadTests
{
    private const uint ColrTag = 0x434F4C52;
    private static readonly DataLocation Source = new(11, 0, ColrTag, -1,
        DataField.None, OffsetBasis.Table, 0);

    /// <summary>A growable big-endian wire builder with a 34-byte v1 header
    /// (the same shape as the static suites' builders, kept local).</summary>
    private sealed class Wire
    {
        private readonly List<byte> _data = new(new byte[34]);

        public Wire()
        {
            _data[1] = 1;                    // version 1 (big-endian u16)
        }

        public int Position => _data.Count;

        public void U8(uint v) => _data.Add((byte)(v & 0xFF));

        public void U16(uint v)
        {
            _data.Add((byte)((v >> 8) & 0xFF));
            _data.Add((byte)(v & 0xFF));
        }

        public void U24(uint v)
        {
            _data.Add((byte)((v >> 16) & 0xFF));
            _data.Add((byte)((v >> 8) & 0xFF));
            _data.Add((byte)(v & 0xFF));
        }

        public void U32(uint v)
        {
            U16(v >> 16);
            U16(v & 0xFFFF);
        }

        public void S16(short v) => U16(unchecked((ushort)v));

        public void Patch32(int at, uint v)
        {
            _data[at] = (byte)((v >> 24) & 0xFF);
            _data[at + 1] = (byte)((v >> 16) & 0xFF);
            _data[at + 2] = (byte)((v >> 8) & 0xFF);
            _data[at + 3] = (byte)(v & 0xFF);
        }

        public byte[] ToBytes() => [.. _data];
    }

    /// <summary>The var-IVS block: format 1, one one-axis region (start 0,
    /// peak +1.0, end +1.0), one VarData with one row - or a LONG_WORDS row
    /// when asked. Returns the block's absolute start.</summary>
    private static int AppendIvs(Wire w, bool longWords)
    {
        int at = w.Position;
        w.U16(1);                        // store format
        w.U32(12);                       // regionList, IVS-relative
        w.U16(1);                        // dataCount
        w.U32(22);                       // dataOffset[0], IVS-relative
        // Region list at IVS+12: axisCount 1, regionCount 1, one axis triple.
        w.U16(1); w.U16(1); w.U16(0); w.U16(0x4000); w.U16(0x4000);
        // VarData at IVS+22: itemCount 1, wordDeltaCount, one region index, row.
        w.U16(1);
        w.U16(longWords ? 0x8001u : 0u);
        w.U16(1); w.U16(0);
        if (longWords)
        {
            w.U32(100000);               // the int32 row delta
        }
        else
        {
            w.U8(10);                    // the int8 row delta
        }
        return at;
    }

    /// <summary>A format-0 DeltaSetIndexMap block: entryFormat 0 (entrySize 1,
    /// innerBits 1), so each packed byte is (outer << 1) | inner.</summary>
    private static int AppendMap(Wire w, params byte[] packedEntries)
    {
        int at = w.Position;
        w.U8(0);
        w.U8(0);
        w.U16((uint)packedEntries.Length);
        foreach (byte entry in packedEntries)
        {
            w.U8(entry);
        }
        return at;
    }

    /// <summary>The base var-solid fixture: header | BGL(root at 44) | the
    /// 9-byte PaintVarSolid | [map] | [IVS].</summary>
    private static byte[] VarSolidWire(uint varIndexBase, bool withMap = true,
        bool withIvs = true, bool longWords = false, byte mapEntry = 0x00)
    {
        var w = new Wire();
        w.Patch32(14, 34);               // baseGlyphListOffset
        w.U32(1);
        w.U16(7); w.U32(10);             // root -> 44
        w.U8(3); w.U16(1); w.U16(0x4000); w.U32(varIndexBase);
        int map = withMap ? AppendMap(w, mapEntry) : 0;
        int store = withIvs ? AppendIvs(w, longWords) : 0;
        w.Patch32(26, (uint)map);
        w.Patch32(30, (uint)store);
        return w.ToBytes();
    }

    [Fact]
    public void VarSolidReadsThroughTheMapStoreChain()
    {
        byte[] wire = VarSolidWire(0);
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done,
            view.ValidateTable(256, 4, 1, out var diagnostic));
        Assert.Equal(ReadStatus.Done,
            view.TryReadTable(256, 4, 1, out var model, out diagnostic));
        Assert.NotNull(model);
        // The two variation slots' receipts: the source offsets and the
        // materialized models ride the read model together.
        Assert.Equal((uint)(wire.Length - 36), model!.VarIndexMapOffset);
        Assert.Equal((uint)(wire.Length - 31), model.ItemVariationStoreOffset);
        Assert.NotNull(model.VarIndexMap);
        Assert.Equal(1, model.VarIndexMap!.MapCount);
        Assert.Equal(0, model.VarIndexMap.Format);
        Assert.NotNull(model.ItemVariationStore);
        Assert.Equal(1, model.ItemVariationStore!.DataCount);
        var solid = Assert.IsType<ColrPaintVarSolid>(model.Paints[0]);
        Assert.Equal((ushort)1, solid.PaletteIndex);
        Assert.Equal((ushort)0x4000, solid.Alpha);
        Assert.Equal(0u, solid.VarIndexBase);
    }

    [Fact]
    public void ValidateOnlyEnumeratesTheVarSlotsToo()
    {
        // The pure gate answers the same resolution chain - the models are its
        // scratch state (OQ-T6-03-7's validate-time full enumeration).
        byte[] wire = VarSolidWire(0);
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, 1, out var diagnostic));

        // An out-of-store inner index refuses in the pure gate exactly like in
        // the read: the map entry (0, 1) misses the one-row VarData.
        byte[] wire2 = VarSolidWire(0, mapEntry: 0x01);
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        int storeAt = wire2.Length - 31;
        Assert.Equal(ReadStatus.InvalidIndex,
            view2.ValidateTable(256, 4, 1, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        // The inner-index miss names the VarData row count (the subtable's own
        // face), not the store head's data count.
        Assert.Equal((uint)(storeAt + 22), diagnostic.Location.Offset);
        Assert.Equal(1uL, diagnostic.Detail);
        Assert.Equal(1uL, diagnostic.Expected);
    }

    [Fact]
    public void SentinelVarIndexBaseSkipsEveryDomain()
    {
        byte[] wire = VarSolidWire(0xFFFFFFFFu);
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, 1, out var diagnostic));
        Assert.Equal(ReadStatus.Done,
            view.TryReadTable(256, 4, 1, out var model, out diagnostic));
        var solid = Assert.IsType<ColrPaintVarSolid>(model!.Paints[0]);
        Assert.Equal(0xFFFFFFFFu, solid.VarIndexBase);
    }

    [Fact]
    public void ImplicitIdentitySplitsTheU32IntoOuterInner()
    {
        // The store without a map (L1556): the u32 index IS the delta-set
        // index, outer = the high half, inner = the low half.
        byte[] wire = VarSolidWire(0x00000000u, withMap: false);
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, 1, out var diagnostic));
        Assert.Equal(ReadStatus.Done,
            view.TryReadTable(256, 4, 1, out var model, out diagnostic));
        Assert.Null(model!.VarIndexMap);
        Assert.NotNull(model.ItemVariationStore);
        Assert.Equal(0u, model.VarIndexMapOffset);

        // outer 5 misses the one-entry store -> the store's InvalidIndex face.
        byte[] wire2 = VarSolidWire(0x00050000u, withMap: false);
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        int storeAt = wire2.Length - 31;
        Assert.Equal(ReadStatus.InvalidIndex,
            view2.ValidateTable(256, 4, 1, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal((uint)(storeAt + 6), diagnostic.Location.Offset);
        Assert.Equal(5uL, diagnostic.Detail);
        Assert.Equal(1uL, diagnostic.Expected);
    }

    [Fact]
    public void VarIndexBaseOverflowRefusesInItsNarrowWindow()
    {
        // The 4-slot family (format 19) overflows when base+3 passes u32: the
        // violation window is [0xFFFFFFFD, 0xFFFFFFFE], the sentinel exempt.
        byte[] offender = VarScalarFamilyWire(19, 0xFFFFFFFDu);
        ReadStatus status = ColrView.TryOpen(offender, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat,
            view.ValidateTable(256, 4, 1, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(0xFFFFFFFDuL, diagnostic.Detail);
        Assert.Equal(0xFFFFFFFCuL, diagnostic.Expected);

        // base 0xFFFFFFFC derives exactly to 0xFFFFFFFF - the last index may
        // reach the word's own ceiling (OQ-T6-03-6's lean).
        byte[] edge = VarScalarFamilyWire(19, 0xFFFFFFFCu);
        status = ColrView.TryOpen(edge, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view2.ValidateTable(256, 4, 1, out diagnostic));

        // A 2-slot format (format 15) can never overflow: 0xFFFFFFFE + 1 is
        // the ceiling, and 0xFFFFFFFF is the sentinel.
        byte[] twoSlot = VarScalarFamilyWire(15, 0xFFFFFFFEu);
        status = ColrView.TryOpen(twoSlot, out var view3, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view3.ValidateTable(256, 4, 1, out diagnostic));
    }

    /// <summary>The var scalar-family fixture: the node at 44 with its child
    /// solid right behind it (the per-format node size decides the child's
    /// offset), then the map and the IVS. The scalar words follow each
    /// format's own field order.</summary>
    private static byte[] VarScalarFamilyWire(byte format, uint varIndexBase)
    {
        var w = new Wire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(7); w.U32(10);             // root -> 44
        w.U8(format);
        int childDelta = format == 19 ? 16 : 12;
        w.U24((uint)childDelta);         // the child right behind the node
        if (format == 19)
        {
            w.U16(0x4000); w.U16(0x4000); w.S16(8); w.S16(9);
        }
        else // format 15: dx, dy
        {
            w.S16(8); w.S16(9);
        }
        w.U32(varIndexBase);
        w.U8(2); w.U16(1); w.U16(0x4000);   // the child
        int map = AppendMap(w, 0x00);
        int store = AppendIvs(w, longWords: false);
        w.Patch32(26, (uint)map);
        w.Patch32(30, (uint)store);
        return w.ToBytes();
    }

    [Fact]
    public void CouplingArmRefusesBothWays()
    {
        // IVS present without the axis authority.
        byte[] wire = VarSolidWire(0);
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat,
            view.ValidateTable(256, 4, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Signature, diagnostic.Location.Field);
        Assert.Equal(30uL, diagnostic.Location.Offset);
        Assert.Equal(0uL, diagnostic.Detail);
        Assert.Equal(1uL, diagnostic.Expected);

        // The axis authority without an IVS (the static host's slot left at 0).
        var w2 = new Wire();
        w2.Patch32(14, 34);
        w2.U32(0);
        byte[] wire2 = w2.ToBytes();
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat,
            view2.ValidateTable(256, 4, 1, out diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Signature, diagnostic.Location.Field);
        Assert.Equal(0uL, diagnostic.Location.Offset);
        Assert.Equal(1uL, diagnostic.Detail);
        Assert.Equal(0uL, diagnostic.Expected);
    }

    [Fact]
    public void PresenceMatrixAnswersPerTheAdjudication()
    {
        // mapCount == 0 on a non-NULL map: the T5-01 OQ-3 ruling re-answered
        // on the COLR path (the T6-02 empty-map window alone used to pass).
        var w = new Wire();
        w.Patch32(14, 34);
        w.U32(0);
        int map = AppendMap(w);          // zero entries
        int store = AppendIvs(w, longWords: false);
        w.Patch32(26, (uint)map);
        w.Patch32(30, (uint)store);
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidFormat, view.ValidateTable(256, 4, 1,
            out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Count, diagnostic.Location.Field);
        Assert.Equal((uint)(map + 2), diagnostic.Location.Offset);

        // IVS with zero var formats and no format-2 box: the legal unused-store
        // observation (OQ-T6-03-11's in-window face).
        var w2 = new Wire();
        w2.Patch32(14, 34);
        w2.U32(1);
        w2.U16(7); w2.U32(10);
        w2.U8(2); w2.U16(1); w2.U16(0x4000);
        int store2 = AppendIvs(w2, longWords: false);
        w2.Patch32(26, 0);
        w2.Patch32(30, (uint)store2);
        byte[] wire2 = w2.ToBytes();
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view2.ValidateTable(256, 4, 1, out diagnostic));
    }

    [Fact]
    public void VarColorLineReadsTenByteStops()
    {
        // PaintVarLinearGradient at 44 (20 bytes), its VarColorLine at 64 with
        // two 10-byte stops - the second carrying the per-stop sentinel.
        var w = new Wire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(7); w.U32(10);             // root -> 44
        w.U8(5); w.U24(20);              // colorLineOffset -> 64
        for (int i = 0; i < 6; i++) w.S16((short)(i * 2));
        w.U32(0);                        // the node's six slots ride base 0
        w.U8(0); w.U16(2);               // extend PAD, 2 stops
        w.S16(0x4000); w.U16(1); w.U16(0x8000); w.U32(0);
        w.S16(0x0000); w.U16(0xFFFF); w.U16(0x0000); w.U32(0xFFFFFFFFu);
        int map = AppendMap(w, 0x00);
        int store = AppendIvs(w, longWords: false);
        w.Patch32(26, (uint)map);
        w.Patch32(30, (uint)store);
        byte[] wire = w.ToBytes();
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, 1, out var diagnostic));
        Assert.Equal(ReadStatus.Done,
            view.TryReadTable(256, 4, 1, out var model, out diagnostic));
        var linear = Assert.IsType<ColrPaintVarLinearGradient>(model!.Paints[0]);
        Assert.Equal(2, linear.ColorLine.Stops.Count);
        Assert.Equal((short)0x4000, linear.ColorLine.Stops[0].StopOffset);
        Assert.Equal((ushort)1, linear.ColorLine.Stops[0].PaletteIndex);
        Assert.Equal(0u, linear.ColorLine.Stops[0].VarIndexBase);
        Assert.Equal((ushort)0xFFFF, linear.ColorLine.Stops[1].PaletteIndex);
        Assert.Equal(0xFFFFFFFFu, linear.ColorLine.Stops[1].VarIndexBase);
        Assert.Equal(0u, linear.VarIndexBase);

        // The var stop's palette domain answers the cross-table authority like
        // the static line's: entry 4 with cpal=4 refuses at the stop's word.
        var w2 = new Wire();
        w2.Patch32(14, 34);
        w2.U32(1);
        w2.U16(7); w2.U32(10);
        w2.U8(5); w2.U24(20);
        for (int i = 0; i < 6; i++) w2.S16(0);
        w2.U32(0xFFFFFFFFu);             // the node's own slots ride the sentinel
        w2.U8(0); w2.U16(1);
        w2.S16(0); w2.U16(4); w2.U16(0); w2.U32(0xFFFFFFFFu);
        int map2 = AppendMap(w2, 0x00);
        int store2 = AppendIvs(w2, longWords: false);
        w2.Patch32(26, (uint)map2);
        w2.Patch32(30, (uint)store2);
        byte[] wire2 = w2.ToBytes();
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidIndex,
            view2.ValidateTable(256, 4, 1, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(4uL, diagnostic.Detail);
        Assert.Equal(4uL, diagnostic.Expected);
    }

    /// <summary>The format-2 clip fixture: header | BGL | var solid (44..53) |
    /// ClipList at 53 (one row) | the 13-byte box at 65 | [map] | [IVS].
    /// Returns the wire and the IVS's absolute start.</summary>
    private static byte[] Clip2Wire(uint boxVarIndexBase, bool withMap,
        out int storeAt)
    {
        var w = new Wire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(7); w.U32(10);             // root -> 44
        w.U8(3); w.U16(1); w.U16(0x4000); w.U32(0xFFFFFFFFu);   // 44..53
        int clipList = w.Position;       // 53
        w.U8(1); w.U32(1);               // ClipList format 1, one clip
        w.U16(10); w.U16(19); w.U24(12); // the box at clipList+12 = 65
        w.U8(2);                         // the format-2 box
        w.S16(-10); w.S16(0); w.S16(100); w.S16(100);
        w.U32(boxVarIndexBase);
        int map = withMap ? AppendMap(w, 0x00) : 0;
        storeAt = AppendIvs(w, longWords: false);
        w.Patch32(22, (uint)clipList);
        w.Patch32(26, (uint)map);
        w.Patch32(30, (uint)storeAt);
        return w.ToBytes();
    }

    [Fact]
    public void FormatTwoClipBoxResolvesThroughTheStore()
    {
        // The sentinel box base skips the chain; the box still rides verbatim.
        byte[] wire = Clip2Wire(0xFFFFFFFFu, withMap: true, out _);
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, 1, out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryGetClipBox(15, out var box, out diagnostic));
        Assert.Equal(2, box.Format);
        Assert.Equal((short)-10, box.XMin);
        Assert.Equal(0xFFFFFFFFu, box.VarIndexBase);
        status = view.TryReadTable(256, 4, 1, out var model, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(model);
        Assert.Equal(2, model!.Clips[0].Box.Format);
        Assert.Equal(0xFFFFFFFFu, model.Clips[0].Box.VarIndexBase);

        // A resolvable box base rides the same chain: base 0 soft-lands onto
        // the one-entry map, (0, 0) hits the one-row store.
        byte[] wire2 = Clip2Wire(0, withMap: true, out _);
        status = ColrView.TryOpen(wire2, out var view2, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view2.ValidateTable(256, 4, 1, out diagnostic));

        // The implicit-identity miss refuses at the box's resolution too.
        byte[] wire3 = Clip2Wire(0x00050000u, withMap: false, out int store3);
        status = ColrView.TryOpen(wire3, out var view3, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.InvalidIndex,
            view3.ValidateTable(256, 4, 1, out diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal((uint)(store3 + 6), diagnostic.Location.Offset);
        Assert.Equal(5uL, diagnostic.Detail);
        Assert.Equal(1uL, diagnostic.Expected);
    }

    [Fact]
    public void LongWordsStoreReadsThroughTheColrConsumption()
    {
        // The typed COLR consumption passes the LONG_WORDS gate (OQ-T6-03-5):
        // the int32 row travels verbatim.
        byte[] wire = VarSolidWire(0, longWords: true);
        ReadStatus status = ColrView.TryOpen(wire, out var view, out _, Source);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, view.ValidateTable(256, 4, 1, out var diagnostic));
        Assert.Equal(ReadStatus.Done,
            view.TryReadTable(256, 4, 1, out var model, out diagnostic));
        Assert.NotNull(model!.ItemVariationStore);
        var data = model.ItemVariationStore!.Data[0];
        Assert.NotNull(data);
        Assert.True(data!.LongWords);
        Assert.Equal(100000, data.Rows[0][0]);
    }
}
