using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CpalTableWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditCpalAndWriteBack()
    {
        var cpalBuilder = new CpalTableBuilder(version: 1);
        cpalBuilder.Resize(paletteEntryCount: 2, paletteCount: 1);
        cpalBuilder.SetPaletteColor(0, 0, new CpalTable.ColorRecord(blue: 0, green: 0, red: 255, alpha: 255));
        cpalBuilder.SetPaletteColor(0, 1, new CpalTable.ColorRecord(blue: 0, green: 255, red: 0, alpha: 255));
        cpalBuilder.SetPaletteType(0, 0xAABBCCDDu);
        cpalBuilder.SetPaletteLabelNameId(0, 256);
        cpalBuilder.SetPaletteEntryLabelNameId(0, 300);
        cpalBuilder.SetPaletteEntryLabelNameId(1, 301);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(cpalBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCpal(out var originalCpal));
        Assert.AreEqual((ushort)1, originalCpal.Version);
        Assert.AreEqual((ushort)2, originalCpal.PaletteEntryCount);
        Assert.AreEqual((ushort)1, originalCpal.PaletteCount);
        Assert.AreEqual((ushort)2, originalCpal.ColorRecordCount);
        Assert.IsTrue(originalCpal.TryGetPaletteColor(0, 0, out var c0));
        Assert.AreEqual((byte)255, c0.Red);
        Assert.IsTrue(originalCpal.TryGetPaletteType(0, out uint paletteType));
        Assert.AreEqual(0xAABBCCDDu, paletteType);
        Assert.IsTrue(originalCpal.TryGetPaletteLabelNameId(0, out ushort labelNameId));
        Assert.AreEqual((ushort)256, labelNameId);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<CpalTableBuilder>(out var edit));
        edit.SetPaletteColor(0, 1, new CpalTable.ColorRecord(blue: 1, green: 2, red: 3, alpha: 4));
        edit.SetPaletteType(0, 0x11223344u);
        edit.SetPaletteLabelNameId(0, 999);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetCpal(out var editedCpal));
        Assert.AreEqual((ushort)1, editedCpal.Version);
        Assert.IsTrue(editedCpal.TryGetPaletteColor(0, 1, out var editedColor));
        Assert.AreEqual((byte)1, editedColor.Blue);
        Assert.AreEqual((byte)2, editedColor.Green);
        Assert.AreEqual((byte)3, editedColor.Red);
        Assert.AreEqual((byte)4, editedColor.Alpha);
        Assert.IsTrue(editedCpal.TryGetPaletteType(0, out uint editedPaletteType));
        Assert.AreEqual(0x11223344u, editedPaletteType);
        Assert.IsTrue(editedCpal.TryGetPaletteLabelNameId(0, out ushort editedLabelNameId));
        Assert.AreEqual((ushort)999, editedLabelNameId);
    }
}

