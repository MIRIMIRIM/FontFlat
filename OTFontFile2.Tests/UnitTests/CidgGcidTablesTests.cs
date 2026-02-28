using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CidgGcidTablesTests
{
    [TestMethod]
    public void CidgTable_CanBuildAndParse()
    {
        var builder = new CidgTableBuilder
        {
            Registry = 1,
            Order = 2,
            SupplementVersion = 3
        };

        builder.SetRegistryNameString("Registry");
        builder.SetOrderNameString("Order");
        builder.SetGlyphId(cid: 0, glyphId: 10);
        builder.SetGlyphId(cid: 2, glyphId: 20);

        byte[] tableBytes = BuildTableBytes(builder);

        Assert.IsTrue(Tag.TryParse("cidg", out var tag));
        Assert.IsTrue(TableSlice.TryCreateStandalone(tag, tableBytes, out var slice));
        Assert.IsTrue(CidgTable.TryCreate(slice, out var cidg));

        Assert.AreEqual((ushort)0, cidg.Format);
        Assert.AreEqual((ushort)1, cidg.Registry);
        Assert.AreEqual("Registry", cidg.GetRegistryNameString());
        Assert.AreEqual((ushort)2, cidg.Order);
        Assert.AreEqual("Order", cidg.GetOrderNameString());
        Assert.AreEqual((ushort)3, cidg.SupplementVersion);

        Assert.IsTrue(cidg.TryGetCidCount(out ushort count));
        Assert.AreEqual((ushort)3, count);

        Assert.IsTrue(cidg.TryGetMappedGlyphIdForCid(0, out ushort g0));
        Assert.AreEqual((ushort)10, g0);

        Assert.IsTrue(cidg.TryGetGlyphIdForCid(1, out ushort g1));
        Assert.AreEqual((ushort)0xFFFF, g1);
        Assert.IsFalse(cidg.TryGetMappedGlyphIdForCid(1, out _));
    }

    [TestMethod]
    public void GcidTable_CanBuildAndParse()
    {
        var builder = new GcidTableBuilder
        {
            Registry = 1,
            Order = 2,
            SupplementVersion = 3
        };

        builder.SetRegistryNameString("Registry");
        builder.SetOrderNameString("Order");
        builder.SetCid(glyphId: 0, cid: 100);
        builder.SetCid(glyphId: 2, cid: 200);

        byte[] tableBytes = BuildTableBytes(builder);

        Assert.IsTrue(Tag.TryParse("gcid", out var tag));
        Assert.IsTrue(TableSlice.TryCreateStandalone(tag, tableBytes, out var slice));
        Assert.IsTrue(GcidTable.TryCreate(slice, out var gcid));

        Assert.AreEqual((ushort)0, gcid.Format);
        Assert.AreEqual((ushort)1, gcid.Registry);
        Assert.AreEqual("Registry", gcid.GetRegistryNameString());
        Assert.AreEqual((ushort)2, gcid.Order);
        Assert.AreEqual("Order", gcid.GetOrderNameString());
        Assert.AreEqual((ushort)3, gcid.SupplementVersion);

        Assert.IsTrue(gcid.TryGetGlyphCount(out ushort count));
        Assert.AreEqual((ushort)3, count);

        Assert.IsTrue(gcid.TryGetMappedCidForGlyphId(0, out ushort c0));
        Assert.AreEqual((ushort)100, c0);

        Assert.IsTrue(gcid.TryGetCidForGlyphId(1, out ushort c1));
        Assert.AreEqual((ushort)0xFFFF, c1);
        Assert.IsFalse(gcid.TryGetMappedCidForGlyphId(1, out _));
    }

    private static byte[] BuildTableBytes(ISfntTableSource source)
    {
        using var ms = new MemoryStream(source.Length);
        source.WriteTo(ms, headCheckSumAdjustment: 0);
        return ms.ToArray();
    }
}

