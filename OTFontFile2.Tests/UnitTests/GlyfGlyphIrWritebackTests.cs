using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using OTFontFile2.Tables.Glyf;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GlyfGlyphIrWritebackTests
{
    [TestMethod]
    public void Glyf_TableBuilder_CanWriteSimpleGlyph_FromIR()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder
        {
            TableVersionNumber = new Fixed1616(0x00010000u),
            NumGlyphs = 1
        };

        // Base font has an empty glyf/loca.
        byte[] glyf = new byte[] { 0 };
        byte[] loca = new byte[] { 0, 0, 0, 0 }; // 2 entries (numGlyphs+1=2) in format0

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(KnownTags.glyf, glyf);
        sfnt.SetTable(KnownTags.loca, loca);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GlyfTableBuilder>(out var glyfEdit));

        var g = new GlyfSimpleGlyphBuilder();
        g.SetContours(
            endPointsOfContours: new ushort[] { 2 },
            points: new GlyfGlyphPoint[]
            {
                new(0, 0, onCurve: true),
                new(50, 0, onCurve: true),
                new(50, 50, onCurve: true),
            });
        g.SetInstructions(new byte[] { 0xAA, 0xBB });

        glyfEdit.SetGlyph(glyphId: 0, g);

        byte[] editedBytes = model.ToArray();

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetHead(out var editedHead));
        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.IsTrue(editedFont.TryGetLoca(out var editedLoca));
        Assert.IsTrue(editedFont.TryGetGlyf(out var editedGlyf));

        Assert.IsTrue(editedGlyf.TryGetGlyphData(0, editedLoca, editedHead.IndexToLocFormat, editedMaxp.NumGlyphs, out var glyphData));
        Assert.IsTrue(GlyfTable.TryGetSimpleGlyphInstructions(glyphData, out var instr));
        Assert.AreEqual(2, instr.Length);
        Assert.AreEqual((byte)0xAA, instr[0]);
        Assert.AreEqual((byte)0xBB, instr[1]);

        Assert.IsTrue(GlyfTable.TryCreateSimpleGlyphPointEnumerator(glyphData, out var e));
        Assert.AreEqual((ushort)3, e.PointCount);

        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((short)0, e.Current.X);
        Assert.AreEqual((short)0, e.Current.Y);

        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((short)50, e.Current.X);
        Assert.AreEqual((short)0, e.Current.Y);

        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((short)50, e.Current.X);
        Assert.AreEqual((short)50, e.Current.Y);
        Assert.IsFalse(e.MoveNext());
    }

    [TestMethod]
    public void Glyf_TableBuilder_CanWriteCompositeGlyph_FromIR()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder
        {
            TableVersionNumber = new Fixed1616(0x00010000u),
            NumGlyphs = 2
        };

        byte[] baseGlyph0 = BuildTriangleGlyphWithTrailingPadByte();
        byte[] glyf = baseGlyph0; // glyph1 empty in base
        byte[] loca = new byte[]
        {
            0x00, 0x00, // glyph0 off/2=0
            0x00, 0x08, // glyph1 off/2=8 (16 bytes)
            0x00, 0x08  // end off/2=8
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

        var c = new GlyfCompositeGlyphBuilder();
        c.AddComponent(glyphIndex: 0, dx: -3, dy: 7, a: new F2Dot14(0x4000), b: default, c: default, d: new F2Dot14(0x4000));
        c.SetInstructions(new byte[] { 1, 2, 3 });
        glyfEdit.SetGlyph(glyphId: 1, c);

        byte[] editedBytes = model.ToArray();

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetHead(out var editedHead));
        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.IsTrue(editedFont.TryGetLoca(out var editedLoca));
        Assert.IsTrue(editedFont.TryGetGlyf(out var editedGlyf));

        Assert.IsTrue(editedGlyf.TryGetGlyphData(1, editedLoca, editedHead.IndexToLocFormat, editedMaxp.NumGlyphs, out var glyphData));
        Assert.IsTrue(GlyfTable.TryReadGlyphHeader(glyphData, out var h));
        Assert.IsTrue(h.IsComposite);

        Assert.IsTrue(GlyfTable.TryGetCompositeGlyphInstructions(glyphData, out var instr));
        Assert.AreEqual(3, instr.Length);
        Assert.AreEqual((byte)1, instr[0]);
        Assert.AreEqual((byte)2, instr[1]);
        Assert.AreEqual((byte)3, instr[2]);

        Assert.IsTrue(GlyfTable.TryCreateCompositeGlyphComponentEnumerator(glyphData, out var e));
        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((ushort)0, e.Current.GlyphIndex);
        Assert.IsTrue(e.Current.TryGetTranslation(out short dx, out short dy));
        Assert.AreEqual((short)-3, dx);
        Assert.AreEqual((short)7, dy);
        Assert.IsFalse(e.MoveNext());
        Assert.IsTrue(e.IsValid);
    }

    private static byte[] BuildTriangleGlyphWithTrailingPadByte()
    {
        return new byte[]
        {
            0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x32,
            0x00, 0x32,

            0x00, 0x02,
            0x00, 0x00,

            0x31,
            0x33,
            0x35,

            0x32,
            0x32,
            0x00
        };
    }
}

