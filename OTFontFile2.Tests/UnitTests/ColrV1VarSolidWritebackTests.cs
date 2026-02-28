using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class ColrV1VarSolidWritebackTests
{
    [TestMethod]
    public void ColrV1_CanWriteVarSolid_WithVarIndexMapAndItemVariationStore()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var colrBuilder = new ColrTableBuilder();
        colrBuilder.ClearToVersion1();

        colrBuilder.SetBaseGlyphPaint(
            baseGlyphId: 1,
            paint: ColrTableBuilder.VarSolid(
                paletteIndex: 2,
                alpha: new F2Dot14(0x4000),
                varIndexBase: 0));

        colrBuilder.SetVarIndexMapData(BuildDeltaSetIndexMapFormat0_EntrySize4_Inner16(new VarIdx(0, 0)));
        colrBuilder.SetMinimalItemVariationStore(axisCount: 0);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(colrBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetColr(out var colr));
        Assert.IsTrue(colr.IsVersion1);
        Assert.AreNotEqual(0, colr.VarIndexMapOffset);
        Assert.AreNotEqual(0, colr.ItemVariationStoreOffset);

        Assert.IsTrue(colr.TryGetVarIndexMap(out var map));
        Assert.AreEqual((byte)0, map.Format);
        Assert.AreEqual((byte)0x3F, map.EntryFormat);
        Assert.AreEqual(4, map.EntrySize);
        Assert.AreEqual(16, map.InnerIndexBitCount);
        Assert.AreEqual((ushort)1, map.MapCount);
        Assert.IsTrue(map.TryGetVarIdx(0, out var varIdx));
        Assert.AreEqual(new VarIdx(0, 0), varIdx);

        Assert.IsTrue(colr.TryGetItemVariationStore(out var store));
        Assert.AreEqual((ushort)1, store.Format);
        Assert.AreEqual((ushort)0, store.ItemVariationDataCount);
        Assert.IsTrue(store.TryGetVariationRegionList(out var regionList));
        Assert.AreEqual((ushort)0, regionList.AxisCount);

        Assert.IsTrue(colr.TryGetBaseGlyphPaint(baseGlyphId: 1, out var paint));
        Assert.AreEqual((byte)3, paint.Format);
        Assert.IsTrue(paint.TryGetPaintVarSolid(out var solid));
        Assert.AreEqual((ushort)2, solid.PaletteIndex);
        Assert.AreEqual((short)0x4000, solid.Alpha.RawValue);
        Assert.AreEqual(0u, solid.VarIndexBase);
    }

    private static byte[] BuildDeltaSetIndexMapFormat0_EntrySize4_Inner16(params VarIdx[] entries)
    {
        if ((uint)entries.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(entries));

        // format=0, entryFormat=0x3F => entrySize=4, innerIndexBitCount=16
        byte[] bytes = new byte[4 + (entries.Length * 4)];
        var span = bytes.AsSpan();
        span[0] = 0;
        span[1] = 0x3F;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), (ushort)entries.Length);

        int pos = 4;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            uint packed = ((uint)e.OuterIndex << 16) | e.InnerIndex;
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(pos, 4), packed);
            pos += 4;
        }

        return bytes;
    }
}
