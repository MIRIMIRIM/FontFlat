using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class MaxpFromGlyfFixupTests
{
    [TestMethod]
    public void FontModel_RecomputesMaxpDerivedFields_FromEditedGlyf()
    {
        // Base font:
        // - glyph0: simple (3 points, 1 contour)
        // - glyph1: composite (1 component referencing glyph0)
        // - maxp v1.0 derived fields are initially 0
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder
        {
            TableVersionNumber = new Fixed1616(0x00010000u),
            NumGlyphs = 2,

            MaxPoints = 0,
            MaxContours = 0,
            MaxCompositePoints = 0,
            MaxCompositeContours = 0,
            MaxSizeOfInstructions = 0,
            MaxComponentElements = 0,
            MaxComponentDepth = 0,
        };

        byte[] glyph0 = BuildTriangleGlyph(padToEven: true);
        byte[] glyph1 = BuildCompositeGlyph(componentGlyphIndex: 0);

        byte[] glyf = new byte[glyph0.Length + glyph1.Length];
        glyph0.CopyTo(glyf, 0);
        glyph1.CopyTo(glyf, glyph0.Length);

        // loca format 0, offsets are word-aligned.
        // glyph0 @ 0, glyph1 @ glyph0.Length, end @ glyf.Length
        ushort o0 = 0;
        ushort o1 = (ushort)(glyph0.Length / 2);
        ushort o2 = (ushort)(glyf.Length / 2);
        byte[] loca =
        {
            (byte)(o0 >> 8), (byte)o0,
            (byte)(o1 >> 8), (byte)o1,
            (byte)(o2 >> 8), (byte)o2
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

        // Edit glyph0: add 5 bytes of instructions (keep point/contour counts unchanged).
        glyfEdit.SetGlyphData(glyphId: 0, BuildTriangleGlyph(padToEven: true, instructionBytes: new byte[] { 1, 2, 3, 4, 5 }));

        byte[] editedBytes = model.ToArray();

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.IsTrue(editedMaxp.IsTrueTypeMaxp);

        Assert.IsTrue(editedMaxp.TryGetTrueTypeFields(out var tt));
        Assert.AreEqual((ushort)3, tt.MaxPoints);
        Assert.AreEqual((ushort)1, tt.MaxContours);
        Assert.AreEqual((ushort)3, tt.MaxCompositePoints);
        Assert.AreEqual((ushort)1, tt.MaxCompositeContours);
        Assert.AreEqual((ushort)1, tt.MaxComponentElements);
        Assert.AreEqual((ushort)1, tt.MaxComponentDepth);
        Assert.AreEqual((ushort)5, tt.MaxSizeOfInstructions);
    }

    private static byte[] BuildTriangleGlyph(bool padToEven, byte[]? instructionBytes = null)
    {
        instructionBytes ??= Array.Empty<byte>();

        // Simple glyph:
        // - 1 contour
        // - 3 points (endPt = 2)
        // - points: (0,0), (50,0), (50,50) all on-curve
        int pad = padToEven ? 1 : 0;
        if ((instructionBytes.Length & 1) != 0)
            pad ^= 1; // keep total glyph length even

        int length = 10 + 2 + 2 + instructionBytes.Length + 3 + 2 + pad;
        byte[] glyph = new byte[length];
        int p = 0;

        glyph[p++] = 0x00; glyph[p++] = 0x01; // numberOfContours = 1
        glyph[p++] = 0x00; glyph[p++] = 0x00; // xMin
        glyph[p++] = 0x00; glyph[p++] = 0x00; // yMin
        glyph[p++] = 0x00; glyph[p++] = 0x32; // xMax
        glyph[p++] = 0x00; glyph[p++] = 0x32; // yMax

        glyph[p++] = 0x00; glyph[p++] = 0x02; // endPts[0]=2

        glyph[p++] = (byte)(instructionBytes.Length >> 8);
        glyph[p++] = (byte)instructionBytes.Length;
        instructionBytes.CopyTo(glyph, p);
        p += instructionBytes.Length;

        glyph[p++] = 0x31; // flag0
        glyph[p++] = 0x33; // flag1
        glyph[p++] = 0x35; // flag2

        glyph[p++] = 0x32; // x delta for point1 = +50
        glyph[p++] = 0x32; // y delta for point2 = +50

        if (pad != 0)
            glyph[p++] = 0x00;

        return glyph;
    }

    private static byte[] BuildCompositeGlyph(ushort componentGlyphIndex)
    {
        // Composite glyph:
        // - 1 component, args are XY bytes (0,0), no transform, no instructions.
        return new byte[]
        {
            0xFF, 0xFF, // numberOfContours = -1
            0x00, 0x00, // xMin
            0x00, 0x00, // yMin
            0x00, 0x32, // xMax
            0x00, 0x32, // yMax

            0x00, 0x02, // flags: ARGS_ARE_XY_VALUES
            (byte)(componentGlyphIndex >> 8), (byte)componentGlyphIndex,
            0x00, 0x00, // dx,dy (byte args)
        };
    }
}
