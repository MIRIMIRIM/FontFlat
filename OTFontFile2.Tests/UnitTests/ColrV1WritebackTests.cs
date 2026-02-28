using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class ColrV1WritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteColrV1_SolidGlyphAndLayers()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<ColrTableBuilder>(out var colr));

        colr.ClearToVersion1();

        var solid = ColrTableBuilder.Solid(paletteIndex: 1, alpha: new F2Dot14(0x4000));
        var glyphPaint = ColrTableBuilder.Glyph(glyphId: 10, paint: solid);
        colr.SetBaseGlyphPaint(baseGlyphId: 5, glyphPaint);

        var layer0 = ColrTableBuilder.Glyph(glyphId: 11, paint: ColrTableBuilder.Solid(2, new F2Dot14(0x4000)));
        var layer1 = ColrTableBuilder.Glyph(glyphId: 12, paint: ColrTableBuilder.Solid(3, new F2Dot14(0x4000)));
        var layers = ColrTableBuilder.ColrLayers(new ColrTableBuilder.PaintV1[] { layer0, layer1 });
        colr.SetBaseGlyphPaint(baseGlyphId: 6, layers);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetColr(out var editedColr));
        Assert.AreEqual((ushort)1, editedColr.Version);

        Assert.IsTrue(editedColr.TryGetBaseGlyphPaint(5, out var p5));
        Assert.AreEqual((byte)10, p5.Format);
        Assert.IsTrue(p5.TryGetPaintGlyph(out var pg));
        Assert.AreEqual((ushort)10, pg.GlyphId);
        Assert.IsTrue(pg.TryGetPaint(out var child));
        Assert.AreEqual((byte)2, child.Format);
        Assert.IsTrue(child.TryGetPaintSolid(out var solidPaint));
        Assert.AreEqual((ushort)1, solidPaint.PaletteIndex);

        Assert.IsTrue(editedColr.TryGetBaseGlyphPaint(6, out var p6));
        Assert.AreEqual((byte)1, p6.Format);
        Assert.IsTrue(p6.TryGetPaintColrLayers(out var layersPaint));
        Assert.AreEqual((byte)2, layersPaint.NumLayers);
        Assert.IsTrue(layersPaint.TryGetLayerPaint(editedColr.LayerListOffset, layerIndex: 0, out var l0));
        Assert.IsTrue(l0.TryGetPaintGlyph(out var l0g));
        Assert.AreEqual((ushort)11, l0g.GlyphId);
        Assert.IsTrue(layersPaint.TryGetLayerPaint(editedColr.LayerListOffset, layerIndex: 1, out var l1));
        Assert.IsTrue(l1.TryGetPaintGlyph(out var l1g));
        Assert.AreEqual((ushort)12, l1g.GlyphId);
    }
}

