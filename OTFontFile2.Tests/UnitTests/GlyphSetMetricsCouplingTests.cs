using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GlyphSetMetricsCouplingTests
{
    [TestMethod]
    public void FontModel_WhenMaxpNumGlyphsIncreases_RebuildsHmtxVmtxAndUpdatesOs2XAvgCharWidth()
    {
        using var file = BuildBaseMetricsFont(numGlyphs: 3);
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<MaxpTableBuilder>(out var maxp));
        maxp.NumGlyphs = 5;

        byte[] editedBytes = model.ToArray();

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.AreEqual((ushort)5, editedMaxp.NumGlyphs);

        Assert.IsTrue(editedFont.TryGetHhea(out var hhea));
        Assert.AreEqual((ushort)3, hhea.NumberOfHMetrics);

        Assert.IsTrue(editedFont.TryGetHmtx(out var hmtx));
        Assert.IsTrue(hmtx.TryGetMetric(4, hhea.NumberOfHMetrics, editedMaxp.NumGlyphs, out var m4));
        Assert.AreEqual((ushort)700, m4.AdvanceWidth);
        Assert.AreEqual((short)0, m4.LeftSideBearing);

        Assert.IsTrue(editedFont.TryGetVhea(out var vhea));
        Assert.AreEqual((ushort)3, vhea.NumOfLongVerMetrics);

        Assert.IsTrue(editedFont.TryGetVmtx(out var vmtx));
        Assert.IsTrue(vmtx.TryGetMetric(4, vhea.NumOfLongVerMetrics, editedMaxp.NumGlyphs, out var v4));
        Assert.AreEqual((ushort)1000, v4.AdvanceHeight);
        Assert.AreEqual((short)0, v4.TopSideBearing);

        Assert.IsTrue(editedFont.TryGetOs2(out var os2));
        // (500 + 600 + 700 + 700 + 700) / 5 = 640
        Assert.AreEqual((short)640, os2.XAvgCharWidth);
    }

    [TestMethod]
    public void FontModel_WhenMaxpNumGlyphsDecreases_ClampsHheaVheaCountsAndUpdatesOs2XAvgCharWidth()
    {
        using var file = BuildBaseMetricsFont(numGlyphs: 3);
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<MaxpTableBuilder>(out var maxp));
        maxp.NumGlyphs = 2;

        byte[] editedBytes = model.ToArray();

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.AreEqual((ushort)2, editedMaxp.NumGlyphs);

        Assert.IsTrue(editedFont.TryGetHhea(out var hhea));
        Assert.AreEqual((ushort)2, hhea.NumberOfHMetrics);

        Assert.IsTrue(editedFont.TryGetHmtx(out var hmtx));
        Assert.IsTrue(hmtx.TryGetMetric(1, hhea.NumberOfHMetrics, editedMaxp.NumGlyphs, out var m1));
        Assert.AreEqual((ushort)600, m1.AdvanceWidth);
        Assert.AreEqual((short)2, m1.LeftSideBearing);

        Assert.IsTrue(editedFont.TryGetVhea(out var vhea));
        Assert.AreEqual((ushort)2, vhea.NumOfLongVerMetrics);

        Assert.IsTrue(editedFont.TryGetVmtx(out var vmtx));
        Assert.IsTrue(vmtx.TryGetMetric(1, vhea.NumOfLongVerMetrics, editedMaxp.NumGlyphs, out var v1));
        Assert.AreEqual((ushort)900, v1.AdvanceHeight);
        Assert.AreEqual((short)20, v1.TopSideBearing);

        Assert.IsTrue(editedFont.TryGetOs2(out var os2));
        // (500 + 600) / 2 = 550
        Assert.AreEqual((short)550, os2.XAvgCharWidth);
    }

    private static SfntFile BuildBaseMetricsFont(ushort numGlyphs)
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder
        {
            TableVersionNumber = new Fixed1616(0x00010000u),
            NumGlyphs = numGlyphs
        };

        var hhea = new HheaTableBuilder
        {
            NumberOfHMetrics = numGlyphs
        };

        var hmtx = new HmtxTableBuilder(numGlyphs, numberOfHMetrics: numGlyphs);
        // (aw, lsb): (500,1), (600,2), (700,3)
        for (ushort i = 0; i < numGlyphs; i++)
            hmtx.SetMetric(i, (ushort)(500 + (i * 100)), (short)(1 + i));

        var vhea = new VheaTableBuilder
        {
            NumOfLongVerMetrics = numGlyphs
        };

        var vmtx = new VmtxTableBuilder(numGlyphs, numOfLongVerMetrics: numGlyphs);
        // (ah, tsb): (800,10), (900,20), (1000,30)
        for (ushort i = 0; i < numGlyphs; i++)
            vmtx.SetMetric(i, (ushort)(800 + (i * 100)), (short)(10 + (i * 10)));

        var os2 = new Os2TableBuilder
        {
            Version = 4,
            XAvgCharWidth = 0
        };

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(hhea);
        sfnt.SetTable(hmtx);
        sfnt.SetTable(vhea);
        sfnt.SetTable(vmtx);
        sfnt.SetTable(os2);

        return SfntFile.FromMemory(sfnt.ToArray());
    }
}

