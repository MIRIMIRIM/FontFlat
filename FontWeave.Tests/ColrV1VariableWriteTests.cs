using FontWeave.Core;
using FontWeave.Tables;
using FontWeave.Tables.Variation;
using Xunit;

namespace FontWeave.Tests;

/// <summary>Card T6-03 commit 3's write side: the var slots' real emission with
/// the map/IVS bodies tail-placed (OQ-T6-03-9), the var paint nodes' byte order
/// with their auxiliaries right behind, the format-2 clip box, the factory's
/// var admission, and the value-level var edits with their refusal wings. Every
/// compiled wire re-reads through the View; unedited var hosts are byte-for-byte
/// identities, and an edit moves only its own words.</summary>
public sealed class ColrV1VariableWriteTests
{
    private const uint ColrTag = 0x434F4C52;
    private static readonly DataLocation Source = new(13, 0, ColrTag, -1,
        DataField.None, OffsetBasis.Table, 0);

    private static ColrTable Read(byte[] wire, int? fvarAxisCount = 1)
    {
        ReadStatus status = ColrView.TryOpen(wire, out var view, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        status = view.TryReadTable(256, 4, fvarAxisCount, out var model, out diagnostic);
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

    /// <summary>A growable big-endian wire builder (the same shape as the read
    /// suites' builders, kept local so the suites stay independent).</summary>
    private sealed class Wire
    {
        private readonly List<byte> _data = new(new byte[34]);

        public Wire() => _data[1] = 1;   // version 1 (big-endian u16)

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

    /// <summary>The shared tail blocks behind the paint region: the one-entry
    /// format-0 map and the one-region/one-row IVS - exactly the canonical
    /// compiler's own shapes.</summary>
    private static (int MapAt, int StoreAt) AppendMapAndStore(Wire w, byte packedEntry)
    {
        int mapAt = w.Position;
        w.U8(0);
        w.U8(0);
        w.U16(1);
        w.U8(packedEntry);
        int storeAt = w.Position;
        w.U16(1);                        // store format
        w.U32(12);                       // regionList, IVS-relative
        w.U16(1);                        // dataCount
        w.U32(22);                       // dataOffset[0], IVS-relative
        w.U16(1); w.U16(1); w.U16(0); w.U16(0x4000); w.U16(0x4000);
        w.U16(1); w.U16(0); w.U16(1); w.U16(0); w.U8(10);
        return (mapAt, storeAt);
    }

    /// <summary>The var-solid host: header | BGL(root at 44) | the node at 44 |
    /// map | IVS - already the canonical layout.</summary>
    private static byte[] VarSolidWire(uint varIndexBase)
    {
        var w = new Wire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(7); w.U32(10);             // root -> 44
        w.U8(3); w.U16(1); w.U16(0x4000); w.U32(varIndexBase);
        var (mapAt, storeAt) = AppendMapAndStore(w, 0x00);
        w.Patch32(26, (uint)mapAt);
        w.Patch32(30, (uint)storeAt);
        return w.ToBytes();
    }

    [Fact]
    public void VarSolidHostRecompilesByteIdentical()
    {
        byte[] wire = VarSolidWire(0);
        ColrTable model = Read(wire);
        byte[] compiled = Compile(model);
        Assert.Equal(wire, compiled);
        // The fixed point: re-read the compiled wire, compile again - identical.
        Assert.Equal(compiled, Compile(Read(compiled)));

        // The sentinel host rides the same identity.
        byte[] sentinelWire = VarSolidWire(0xFFFFFFFFu);
        Assert.Equal(sentinelWire, Compile(Read(sentinelWire)));
    }

    [Fact]
    public void VarGradientAndTransformRecompileByteIdentical()
    {
        // Two roots: PaintVarLinearGradient at 50 (its 10-byte-stop VarColorLine
        // right behind at 70) and PaintVarTransform at 93 (its 28-byte
        // VarAffine2x3 right behind at 104, the child at 132 after the affine).
        var w = new Wire();
        w.Patch32(14, 34);
        w.U32(2);
        w.U16(7); w.U32(16);             // root A -> 50 (the gradient)
        w.U16(8); w.U32(59);             // root B -> 93 (the transform)
        w.U8(5); w.U24(20);              // colorLine at 70
        for (int i = 0; i < 6; i++) w.S16((short)(i + 1));
        w.U32(0);                        // the node's six slots ride base 0
        w.U8(0); w.U16(2);               // extend PAD, two 10-byte stops
        w.S16(0x4000); w.U16(1); w.U16(0x8000); w.U32(0);
        w.S16(0); w.U16(0xFFFF); w.U16(0); w.U32(0xFFFFFFFFu);
        // The transform at 93: the affine right behind the 7-byte node
        // (offset 7), the child at 93+35=128 after the affine.
        w.U8(13); w.U24(35); w.U24(7);
        w.U32(0x3F800000); w.U32(0); w.U32(0); w.U32(0x3F800000);
        w.U32(0x00010000); w.U32(0); w.U32(0x00000002);
        w.U8(2); w.U16(1); w.U16(0x4000);   // the child solid at 128
        var (mapAt, storeAt) = AppendMapAndStore(w, 0x00);
        w.Patch32(26, (uint)mapAt);
        w.Patch32(30, (uint)storeAt);
        byte[] wire = w.ToBytes();
        ColrTable model = Read(wire);
        byte[] compiled = Compile(model);
        Assert.Equal(wire, compiled);
        Assert.Equal(compiled, Compile(Read(compiled)));
        Assert.Equal(3, model.PaintCount);   // gradient, transform, child
    }

    [Fact]
    public void FormatTwoClipHostRecompilesByteIdentical()
    {
        // The canonical order: the ClipList sits between the BaseGlyphList and
        // the paint region, its 13-byte format-2 box tail-placed after the row.
        var w = new Wire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(7); w.U32(35);             // root -> 69
        int clipListAt = w.Position;     // 44
        w.U8(1); w.U32(1);               // ClipList format 1, one clip
        w.U16(10); w.U16(19); w.U24(12); // the 13-byte format-2 box at 56
        w.U8(2);
        w.S16(-10); w.S16(0); w.S16(100); w.S16(100);
        w.U32(0x00000002u);              // the box's own base
        w.U8(3); w.U16(1); w.U16(0x4000); w.U32(0xFFFFFFFFu);   // the node at 69
        var (mapAt, storeAt) = AppendMapAndStore(w, 0x00);
        w.Patch32(22, (uint)clipListAt);
        w.Patch32(26, (uint)mapAt);
        w.Patch32(30, (uint)storeAt);
        byte[] wire = w.ToBytes();
        ColrTable model = Read(wire);
        byte[] compiled = Compile(model);
        Assert.Equal(wire, compiled);
        Assert.Equal(2, model.Clips[0].Box.Format);
        Assert.Equal(2u, model.Clips[0].Box.VarIndexBase);
    }

    [Fact]
    public void FactoryCompilesTheVarSlotsAndTail()
    {
        // The owned parts: one var solid, a one-entry format-0 map, a
        // one-region/one-row store - the same shapes the read model carries.
        ReadStatus status = DeltaSetIndexMapTableCompiler.TryCreate(0,
            [new DeltaSetIndex(0, 0)], out var map, out _);
        Assert.Equal(ReadStatus.Done, status);
        status = ItemVariationDataTableCompiler.TryCreate(0, [0], [[10]],
            out var varData, out _);
        Assert.Equal(ReadStatus.Done, status);
        status = VariationStoreTableCompiler.TryCreate(1,
            [[new VariationRegionAxis(0, 0x4000, 0x4000)]], [varData],
            out var store, out _);
        Assert.Equal(ReadStatus.Done, status);
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(7, 0)], [], [],
            [new ColrPaintVarSolid(0, 1, 0x4000, 0)], 256, 4,
            out var model, out _, varIndexMap: map, itemVariationStore: store);
        Assert.Equal(ReadStatus.Done, status);
        byte[] compiled = Compile(model!);
        Assert.Equal(VarSolidWire(0), compiled);
        // The read back answers every slot.
        ColrTable reRead = Read(compiled);
        Assert.NotNull(reRead.VarIndexMap);
        Assert.NotNull(reRead.ItemVariationStore);
        Assert.Equal(10, reRead.ItemVariationStore!.Data[0]!.Rows[0][0]);
    }

    [Fact]
    public void FactoryRefusesVarPaintAndMapWithoutAStore()
    {
        ReadStatus status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(7, 0)], [], [],
            [new ColrPaintVarSolid(0, 1, 0x4000, 0)], 256, 4,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(30uL, diagnostic.Location.Offset);

        status = DeltaSetIndexMapTableCompiler.TryCreate(0,
            [new DeltaSetIndex(0, 0)], out var map, out _);
        Assert.Equal(ReadStatus.Done, status);
        status = ColrTableCompiler.TryCreate(
            [new ColrBaseGlyphPaint(7, 0)], [], [],
            [new ColrPaintSolid(0, 1, 0x4000)], 256, 4,
            out _, out diagnostic, varIndexMap: map);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
    }

    [Fact]
    public void EditedVarBaseMovesOnlyItsOwnWord()
    {
        byte[] wire = VarSolidWire(0);
        ColrTable model = Read(wire);
        ReadStatus status = model.TryWithVarIndexBase(0, 0xFFFFFFFFu,
            out var updated, out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] compiled = Compile(updated!);
        Assert.Equal(VarSolidWire(0xFFFFFFFFu), compiled);
        // The unedited nodes' bytes stay identical by construction (one node
        // here) - the compiled whole re-validates and re-compiles identically.
        Assert.Equal(compiled, Compile(Read(compiled)));

        // The value-level face on a var scalar twin: the translate's base.
        var w2 = new Wire();
        w2.Patch32(14, 34);
        w2.U32(1);
        w2.U16(7); w2.U32(10);
        w2.U8(15); w2.U24(12);           // the child solid right behind (56)
        w2.S16(8); w2.S16(9);
        w2.U32(0);
        w2.U8(2); w2.U16(1); w2.U16(0x4000);
        var (mapAt, storeAt) = AppendMapAndStore(w2, 0x00);
        w2.Patch32(26, (uint)mapAt);
        w2.Patch32(30, (uint)storeAt);
        byte[] translateWire = w2.ToBytes();
        ColrTable translateModel = Read(translateWire);
        status = translateModel.TryWithVarIndexBase(0, 0xFFFFFFFFu,
            out var translated, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(ReadStatus.Done, ColrView.TryOpen(Compile(translated!),
            out var view, out _, Source));
        Assert.Equal(ReadStatus.Done,
            view.TryReadTable(256, 4, 1, out var reRead, out _));
        var node = Assert.IsType<ColrPaintVarTranslate>(reRead!.Paints[0]);
        Assert.Equal(0xFFFFFFFFu, node.VarIndexBase);
        Assert.Equal((short)8, node.Dx);
        Assert.Equal((short)9, node.Dy);
    }

    [Fact]
    public void VarValueEditsAnswerTheirFamilies()
    {
        // The var solid's alpha: the variable slot moves, the base rides.
        ColrTable model = Read(VarSolidWire(0));
        ReadStatus status = model.TryWithPaintSolidAlpha(0, 0x2000,
            out var updated, out _);
        Assert.Equal(ReadStatus.Done, status);
        var solid = Assert.IsType<ColrPaintVarSolid>(updated!.Paints[0]);
        Assert.Equal((ushort)0x2000, solid.Alpha);
        Assert.Equal(0u, solid.VarIndexBase);

        // The static/var conversion refusal: the static stop overload on a var
        // gradient refuses; the var stop overload answers.
        var w2 = new Wire();
        w2.Patch32(14, 34);
        w2.U32(1);
        w2.U16(7); w2.U32(10);
        w2.U8(5); w2.U24(20);
        for (int i = 0; i < 6; i++) w2.S16(0);
        w2.U32(0);
        w2.U8(0); w2.U16(1);
        w2.S16(0x4000); w2.U16(1); w2.U16(0x8000); w2.U32(0);
        var (mapAt, storeAt) = AppendMapAndStore(w2, 0x00);
        w2.Patch32(26, (uint)mapAt);
        w2.Patch32(30, (uint)storeAt);
        ColrTable gradientModel = Read(w2.ToBytes());
        status = gradientModel.TryWithColorStop(0, 0,
            new ColrColorStop(0x4000, 1, 0x8000), 4, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(5uL, diagnostic.Detail);
        status = gradientModel.TryWithColorStop(0, 0,
            new ColrVarColorStop(0x4000, 1, 0x8000, 0xFFFFFFFFu), 4,
            out updated, out _);
        Assert.Equal(ReadStatus.Done, status);
        var varLinear = Assert.IsType<ColrPaintVarLinearGradient>(updated!.Paints[0]);
        Assert.Equal(0xFFFFFFFFu, varLinear.ColorLine.Stops[0].VarIndexBase);

        // The var base family refuses a static node (the family mismatch names
        // the static format and the VarBase family, ordinal 5).
        status = updated.TryWithVarIndexBase(0, 0, out _, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);   // the node is still var here
        ColrTable staticModel = Read(NewStaticWire(), fvarAxisCount: null);
        status = staticModel.TryWithVarIndexBase(0, 0, out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(2uL, diagnostic.Detail);    // the static solid's format
        Assert.Equal(5uL, diagnostic.Expected);  // the VarBase family ordinal

        // The map/IVS presence flips refuse; the same-presence swap answers.
        status = DeltaSetIndexMapTableCompiler.TryCreate(0,
            [new DeltaSetIndex(0, 0)], out var map2, out _);
        Assert.Equal(ReadStatus.Done, status);
        status = staticModel.TryWithVarIndexMap(map2, out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);   // addition refused
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        status = staticModel.TryWithItemVariationStore(null, out _, out _);
        Assert.Equal(ReadStatus.Done, status);            // the trivial no-op swap
        ColrTable varModel = Read(VarSolidWire(0));
        status = varModel.TryWithVarIndexMap(map2, out var swapped, out _);
        Assert.Equal(ReadStatus.Done, status);            // the same-presence swap
        Assert.Equal(VarSolidWire(0), Compile(swapped!));
        status = varModel.TryWithVarIndexMap(null, out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);   // removal refused
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        status = varModel.TryWithItemVariationStore(null, out _, out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);   // removal refused
    }

    private static byte[] NewStaticWire()
    {
        var w = new Wire();
        w.Patch32(14, 34);
        w.U32(1);
        w.U16(7); w.U32(10);
        w.U8(2); w.U16(1); w.U16(0x4000);
        return w.ToBytes();
    }
}
