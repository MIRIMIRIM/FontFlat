using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class ColrV1ClipListWritebackTests
{
    [TestMethod]
    public void ColrV1_CanWriteClipListFormat1()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var colrBuilder = new ColrTableBuilder();
        colrBuilder.ClearToVersion1();

        colrBuilder.SetBaseGlyphPaint(baseGlyphId: 1, paint: ColrTableBuilder.Solid(paletteIndex: 0, alpha: new F2Dot14(0x4000)));
        colrBuilder.SetClipBoxRange(startGlyphId: 1, endGlyphId: 1, clipBox: new ColrTableBuilder.ClipBoxV1(xMin: -10, yMin: -20, xMax: 30, yMax: 40));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(colrBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetColr(out var colr));
        Assert.IsTrue(colr.TryGetClipList(out var clipList));
        Assert.AreEqual((byte)1, clipList.Format);

        Assert.IsTrue(clipList.TryFindClipBoxForGlyph(glyphId: 1, out var clipBox));
        Assert.AreEqual((byte)1, clipBox.Format);
        Assert.AreEqual((short)-10, clipBox.XMin);
        Assert.AreEqual((short)-20, clipBox.YMin);
        Assert.AreEqual((short)30, clipBox.XMax);
        Assert.AreEqual((short)40, clipBox.YMax);

        Assert.IsFalse(clipList.TryFindClipBoxForGlyph(glyphId: 2, out _));
    }
}

