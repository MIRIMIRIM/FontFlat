using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class VorgTableWritebackTests
{
    [TestMethod]
    public void VorgTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var vorgBuilder = new VorgTableBuilder
        {
            MajorVersion = 1,
            MinorVersion = 0,
            DefaultVertOriginY = 100
        };
        vorgBuilder.AddOrReplaceMetric(glyphIndex: 5, vertOriginY: 200);
        vorgBuilder.AddOrReplaceMetric(glyphIndex: 10, vertOriginY: 300);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x4F54544Fu }; // 'OTTO'
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(vorgBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetVorg(out var originalVorg));
        Assert.AreEqual((ushort)1, originalVorg.MajorVersion);
        Assert.AreEqual((ushort)0, originalVorg.MinorVersion);
        Assert.AreEqual((short)100, originalVorg.DefaultVertOriginY);
        Assert.AreEqual((ushort)2, originalVorg.MetricCount);
        Assert.IsTrue(originalVorg.TryGetVertOriginY(glyphIndex: 5, out short y5));
        Assert.AreEqual((short)200, y5);

        Assert.IsTrue(VorgTableBuilder.TryFrom(originalVorg, out var edit));
        edit.DefaultVertOriginY = 150;
        edit.AddOrReplaceMetric(glyphIndex: 5, vertOriginY: 250);
        Assert.IsTrue(edit.RemoveMetric(glyphIndex: 10));
        edit.AddOrReplaceMetric(glyphIndex: 20, vertOriginY: 400);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetVorg(out var editedVorg));
        Assert.AreEqual((short)150, editedVorg.DefaultVertOriginY);
        Assert.AreEqual((ushort)2, editedVorg.MetricCount);
        Assert.IsTrue(editedVorg.TryGetVertOriginY(glyphIndex: 5, out short editedY5));
        Assert.AreEqual((short)250, editedY5);
        Assert.IsTrue(editedVorg.TryGetVertOriginY(glyphIndex: 20, out short editedY20));
        Assert.AreEqual((short)400, editedY20);
    }
}

