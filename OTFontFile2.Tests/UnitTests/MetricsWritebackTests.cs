using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class MetricsWritebackTests
{
    [TestMethod]
    public void HmtxTable_CanEditAndWriteBack_WithSfntEditor()
    {
        const ushort numGlyphs = 4;
        const ushort numberOfHMetrics = 3;

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { TableVersionNumber = new Fixed1616(0x00010000u), NumGlyphs = numGlyphs };
        var hhea = new HheaTableBuilder { NumberOfHMetrics = numberOfHMetrics };

        var hmtx = new HmtxTableBuilder(numGlyphs, numberOfHMetrics);
        hmtx.SetMetric(glyphId: 0, advanceWidth: 500, leftSideBearing: 10);
        hmtx.SetMetric(glyphId: 1, advanceWidth: 600, leftSideBearing: 20);
        hmtx.SetMetric(glyphId: 2, advanceWidth: 700, leftSideBearing: 30);
        hmtx.SetMetric(glyphId: 3, advanceWidth: 700, leftSideBearing: 40);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(hhea);
        sfnt.SetTable(hmtx);
        byte[] originalBytes = sfnt.ToArray();

        using var file = SfntFile.FromMemory(originalBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetMaxp(out var originalMaxp));
        Assert.IsTrue(font.TryGetHhea(out var originalHhea));
        Assert.IsTrue(font.TryGetHmtx(out var originalHmtx));

        Assert.IsTrue(originalHmtx.TryGetMetric(3, originalHhea.NumberOfHMetrics, originalMaxp.NumGlyphs, out var metric3));
        Assert.AreEqual((ushort)700, metric3.AdvanceWidth);
        Assert.AreEqual((short)40, metric3.LeftSideBearing);

        Assert.IsTrue(HmtxTableBuilder.TryFrom(originalHmtx, originalHhea.NumberOfHMetrics, originalMaxp.NumGlyphs, out var edit));
        edit.SetLeftSideBearing(glyphId: 3, leftSideBearing: 55);

        var editor = new SfntEditor(font);
        editor.SetTable(edit);
        byte[] editedBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.IsTrue(editedFont.TryGetHhea(out var editedHhea));
        Assert.IsTrue(editedFont.TryGetHmtx(out var editedHmtx));

        Assert.IsTrue(editedHmtx.TryGetMetric(3, editedHhea.NumberOfHMetrics, editedMaxp.NumGlyphs, out var editedMetric3));
        Assert.AreEqual((ushort)700, editedMetric3.AdvanceWidth);
        Assert.AreEqual((short)55, editedMetric3.LeftSideBearing);
    }

    [TestMethod]
    public void FontModel_CanExpandHmtxAndAutoFixHhea()
    {
        const ushort numGlyphs = 4;
        const ushort numberOfHMetrics = 3;

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { TableVersionNumber = new Fixed1616(0x00010000u), NumGlyphs = numGlyphs };
        var hhea = new HheaTableBuilder { NumberOfHMetrics = numberOfHMetrics };

        var hmtx = new HmtxTableBuilder(numGlyphs, numberOfHMetrics);
        hmtx.SetMetric(glyphId: 0, advanceWidth: 500, leftSideBearing: 10);
        hmtx.SetMetric(glyphId: 1, advanceWidth: 600, leftSideBearing: 20);
        hmtx.SetMetric(glyphId: 2, advanceWidth: 700, leftSideBearing: 30);
        hmtx.SetMetric(glyphId: 3, advanceWidth: 700, leftSideBearing: 40);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(hhea);
        sfnt.SetTable(hmtx);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<HmtxTableBuilder>(out var edit));

        // This requires expanding to full metrics (numberOfHMetrics == numGlyphs),
        // and the model should auto-fix hhea.numberOfHMetrics to match.
        edit.SetAdvanceWidth(glyphId: 3, advanceWidth: 900);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.IsTrue(editedFont.TryGetHhea(out var editedHhea));
        Assert.IsTrue(editedFont.TryGetHmtx(out var editedHmtx));

        Assert.AreEqual(numGlyphs, editedMaxp.NumGlyphs);
        Assert.AreEqual((ushort)4, editedHhea.NumberOfHMetrics);

        Assert.IsTrue(editedHmtx.TryGetMetric(3, editedHhea.NumberOfHMetrics, editedMaxp.NumGlyphs, out var metric3));
        Assert.AreEqual((ushort)900, metric3.AdvanceWidth);
        Assert.AreEqual((short)40, metric3.LeftSideBearing);
    }

    [TestMethod]
    public void FontModel_CanExpandVmtxAndAutoFixVhea()
    {
        const ushort numGlyphs = 4;
        const ushort numOfLongVerMetrics = 3;

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { TableVersionNumber = new Fixed1616(0x00010000u), NumGlyphs = numGlyphs };

        var vhea = new VheaTableBuilder
        {
            NumOfLongVerMetrics = numOfLongVerMetrics,
            AdvanceHeightMax = 1000
        };

        var vmtx = new VmtxTableBuilder(numGlyphs, numOfLongVerMetrics);
        vmtx.SetMetric(glyphId: 0, advanceHeight: 800, topSideBearing: 10);
        vmtx.SetMetric(glyphId: 1, advanceHeight: 900, topSideBearing: 20);
        vmtx.SetMetric(glyphId: 2, advanceHeight: 1000, topSideBearing: 30);
        vmtx.SetMetric(glyphId: 3, advanceHeight: 1000, topSideBearing: 40);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(vhea);
        sfnt.SetTable(vmtx);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<VmtxTableBuilder>(out var edit));

        edit.SetAdvanceHeight(glyphId: 3, advanceHeight: 1100);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.IsTrue(editedFont.TryGetVhea(out var editedVhea));
        Assert.IsTrue(editedFont.TryGetVmtx(out var editedVmtx));

        Assert.AreEqual(numGlyphs, editedMaxp.NumGlyphs);
        Assert.AreEqual((ushort)4, editedVhea.NumOfLongVerMetrics);

        Assert.IsTrue(editedVmtx.TryGetMetric(3, editedVhea.NumOfLongVerMetrics, editedMaxp.NumGlyphs, out var metric3));
        Assert.AreEqual((ushort)1100, metric3.AdvanceHeight);
        Assert.AreEqual((short)40, metric3.TopSideBearing);
    }
}

