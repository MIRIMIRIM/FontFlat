using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class ColrTableWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditColrV0AndWriteBack()
    {
        var colrBuilder = new ColrTableBuilder();
        colrBuilder.AddOrReplaceBaseGlyph(
            baseGlyphId: 5,
            layers: new[]
            {
                new ColrTableBuilder.LayerEntry(layerGlyphId: 10, paletteIndex: 0),
                new ColrTableBuilder.LayerEntry(layerGlyphId: 11, paletteIndex: 1),
            });

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(colrBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetColr(out var originalColr));
        Assert.AreEqual((ushort)0, originalColr.Version);
        Assert.IsTrue(originalColr.TryFindBaseGlyphRecord(5, out var baseRec));
        Assert.AreEqual((ushort)2, baseRec.NumLayers);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<ColrTableBuilder>(out var edit));
        edit.AddOrReplaceBaseGlyph(
            baseGlyphId: 5,
            layers: new[]
            {
                new ColrTableBuilder.LayerEntry(layerGlyphId: 12, paletteIndex: 0xFFFF),
            });

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetColr(out var editedColr));
        Assert.AreEqual((ushort)0, editedColr.Version);
        Assert.IsTrue(editedColr.TryFindBaseGlyphRecord(5, out var editedBaseRec));
        Assert.AreEqual((ushort)1, editedBaseRec.NumLayers);

        var e = editedColr.EnumerateLayers(editedBaseRec);
        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((ushort)12, e.Current.LayerGlyphId);
        Assert.AreEqual((ushort)0xFFFF, e.Current.PaletteIndex);
        Assert.IsFalse(e.MoveNext());
    }
}

