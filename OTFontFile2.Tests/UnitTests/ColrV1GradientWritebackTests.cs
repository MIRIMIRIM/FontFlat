using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class ColrV1GradientWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteColrV1_LinearGradient()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<ColrTableBuilder>(out var colr));

        colr.ClearToVersion1();

        var stops = new[]
        {
            new ColrTableBuilder.ColorStopV1(new F2Dot14(unchecked((short)0xC000)), paletteIndex: 0, alpha: new F2Dot14(0x4000)), // -1.0
            new ColrTableBuilder.ColorStopV1(new F2Dot14(0x4000), paletteIndex: 1, alpha: new F2Dot14(0x4000)), //  1.0
        };

        var grad = ColrTableBuilder.LinearGradient(
            extend: 0,
            x0: 0, y0: 0,
            x1: 100, y1: 0,
            x2: 0, y2: 100,
            stops: stops);

        colr.SetBaseGlyphPaint(baseGlyphId: 5, ColrTableBuilder.Glyph(glyphId: 10, paint: grad));

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetColr(out var editedColr));
        Assert.AreEqual((ushort)1, editedColr.Version);

        Assert.IsTrue(editedColr.TryGetBaseGlyphPaint(5, out var basePaint));
        Assert.IsTrue(basePaint.TryGetPaintGlyph(out var pg));
        Assert.IsTrue(pg.TryGetPaint(out var p));

        Assert.AreEqual((byte)4, p.Format);
        Assert.IsTrue(p.TryGetPaintLinearGradient(out var lg));
        Assert.IsTrue(lg.TryGetColorLine(out var line));
        Assert.AreEqual((ushort)2, line.StopCount);
        Assert.IsTrue(line.TryGetStop(0, out var s0));
        Assert.AreEqual((ushort)0, s0.PaletteIndex);
        Assert.IsTrue(line.TryGetStop(1, out var s1));
        Assert.AreEqual((ushort)1, s1.PaletteIndex);
    }
}
