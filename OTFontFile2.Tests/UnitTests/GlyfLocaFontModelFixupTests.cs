using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GlyfLocaFontModelFixupTests
{
    [TestMethod]
    public void FontModel_RebuildsLocaAndUpgradesHeadIndexToLocFormat_WhenGlyfGrowsPastFormat0Limit()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder
        {
            TableVersionNumber = new Fixed1616(0x00010000u),
            NumGlyphs = 2
        };

        byte[] baseGlyph1 = BuildTriangleGlyphWithTrailingPadByte();
        byte[] glyf = baseGlyph1; // glyph0 empty at offset 0; glyph1 starts at 0.
        byte[] loca =
        {
            0x00, 0x00, // glyph0 offset/2 = 0
            0x00, 0x00, // glyph1 offset/2 = 0
            0x00, 0x08  // end offset/2 = 8 (16 bytes)
        };

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(KnownTags.glyf, glyf);
        sfnt.SetTable(KnownTags.loca, loca);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GlyfTableBuilder>(out var glyfEdit));

        // Grow glyph1 so the total glyf length exceeds the max representable by loca format 0 (131070).
        glyfEdit.SetGlyphData(glyphId: 1, new byte[131_072]);

        byte[] editedBytes = model.ToArray();

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetHead(out var editedHead));
        Assert.AreEqual((short)1, editedHead.IndexToLocFormat);

        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.AreEqual((ushort)2, editedMaxp.NumGlyphs);

        Assert.IsTrue(editedFont.TryGetLoca(out var editedLoca));
        Assert.IsTrue(editedFont.TryGetGlyf(out var editedGlyf));

        Assert.IsTrue(editedGlyf.TryGetGlyphData(
            glyphId: 1,
            loca: editedLoca,
            indexToLocFormat: editedHead.IndexToLocFormat,
            numGlyphs: editedMaxp.NumGlyphs,
            out var glyph1Data));

        Assert.AreEqual(131_072, glyph1Data.Length);
    }

    [TestMethod]
    public void FontModel_RebuildsLocaButKeepsFormat0_WhenGlyfStillFits()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder
        {
            TableVersionNumber = new Fixed1616(0x00010000u),
            NumGlyphs = 2
        };

        byte[] baseGlyph1 = BuildTriangleGlyphWithTrailingPadByte();
        byte[] glyf = baseGlyph1;
        byte[] loca =
        {
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x08
        };

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(KnownTags.glyf, glyf);
        sfnt.SetTable(KnownTags.loca, loca);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GlyfTableBuilder>(out var glyfEdit));
        glyfEdit.SetGlyphData(glyphId: 1, new byte[100]);

        byte[] editedBytes = model.ToArray();

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetHead(out var editedHead));
        Assert.AreEqual((short)0, editedHead.IndexToLocFormat);

        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.AreEqual((ushort)2, editedMaxp.NumGlyphs);

        Assert.IsTrue(editedFont.TryGetLoca(out var editedLoca));
        Assert.IsTrue(editedFont.TryGetGlyf(out var editedGlyf));

        Assert.IsTrue(editedGlyf.TryGetGlyphData(
            glyphId: 1,
            loca: editedLoca,
            indexToLocFormat: editedHead.IndexToLocFormat,
            numGlyphs: editedMaxp.NumGlyphs,
            out var glyph1Data));

        Assert.AreEqual(100, glyph1Data.Length);
    }

    [TestMethod]
    public void FontModel_RebuildsDerivedLoca_WhenGlyfIsEditedMultipleTimes()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder
        {
            TableVersionNumber = new Fixed1616(0x00010000u),
            NumGlyphs = 2
        };

        byte[] baseGlyph1 = BuildTriangleGlyphWithTrailingPadByte();
        byte[] glyf = baseGlyph1;
        byte[] loca =
        {
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x08
        };

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(KnownTags.glyf, glyf);
        sfnt.SetTable(KnownTags.loca, loca);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GlyfTableBuilder>(out var glyfEdit));

        // First edit: still fits loca format 0.
        glyfEdit.SetGlyphData(glyphId: 1, new byte[100]);
        byte[] firstBytes = model.ToArray();

        using var firstFile = SfntFile.FromMemory(firstBytes);
        var firstFont = firstFile.GetFont(0);

        Assert.IsTrue(firstFont.TryGetHead(out var firstHead));
        Assert.AreEqual((short)0, firstHead.IndexToLocFormat);
        Assert.IsTrue(firstFont.TryGetMaxp(out var firstMaxp));
        Assert.IsTrue(firstFont.TryGetLoca(out var firstLoca));
        Assert.IsTrue(firstFont.TryGetGlyf(out var firstGlyf));

        Assert.IsTrue(firstGlyf.TryGetGlyphData(
            glyphId: 1,
            loca: firstLoca,
            indexToLocFormat: firstHead.IndexToLocFormat,
            numGlyphs: firstMaxp.NumGlyphs,
            out var firstGlyph1Data));
        Assert.AreEqual(100, firstGlyph1Data.Length);

        // Second edit: force upgrade to loca format 1.
        glyfEdit.SetGlyphData(glyphId: 1, new byte[131_072]);
        byte[] secondBytes = model.ToArray();

        using var secondFile = SfntFile.FromMemory(secondBytes);
        var secondFont = secondFile.GetFont(0);

        Assert.IsTrue(secondFont.TryGetHead(out var secondHead));
        Assert.AreEqual((short)1, secondHead.IndexToLocFormat);
        Assert.IsTrue(secondFont.TryGetMaxp(out var secondMaxp));
        Assert.IsTrue(secondFont.TryGetLoca(out var secondLoca));
        Assert.IsTrue(secondFont.TryGetGlyf(out var secondGlyf));

        Assert.IsTrue(secondGlyf.TryGetGlyphData(
            glyphId: 1,
            loca: secondLoca,
            indexToLocFormat: secondHead.IndexToLocFormat,
            numGlyphs: secondMaxp.NumGlyphs,
            out var secondGlyph1Data));
        Assert.AreEqual(131_072, secondGlyph1Data.Length);
    }

    private static byte[] BuildTriangleGlyphWithTrailingPadByte()
    {
        // Same as GlyfSimpleGlyphOutlineTests triangle glyph, plus one trailing zero pad byte to make it even-length.
        return new byte[]
        {
            0x00, 0x01, // numberOfContours = 1
            0x00, 0x00, // xMin
            0x00, 0x00, // yMin
            0x00, 0x32, // xMax = 50
            0x00, 0x32, // yMax = 50

            0x00, 0x02, // endPtsOfContours[0] = 2
            0x00, 0x00, // instructionLength = 0

            0x31,       // flag0
            0x33,       // flag1
            0x35,       // flag2

            0x32,       // x delta for point1 = +50
            0x32,       // y delta for point2 = +50

            0x00        // pad
        };
    }
}
