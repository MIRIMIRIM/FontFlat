using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GlyfInstructionsAndCompositeTests
{
    [TestMethod]
    public void Glyf_SimpleGlyphInstructions_AreExtracted()
    {
        byte[] glyph = new byte[]
        {
            0x00, 0x01, // numberOfContours = 1
            0x00, 0x00, // xMin
            0x00, 0x00, // yMin
            0x00, 0x00, // xMax
            0x00, 0x00, // yMax

            0x00, 0x00, // endPtsOfContours[0] = 0
            0x00, 0x02, // instructionLength = 2
            0xAA, 0xBB, // instructions[2]

            0x31        // flag0: on-curve + x same + y same => point (0,0)
        };

        Assert.IsTrue(GlyfTable.TryGetSimpleGlyphInstructions(glyph, out var instructions));
        Assert.AreEqual(2, instructions.Length);
        Assert.AreEqual((byte)0xAA, instructions[0]);
        Assert.AreEqual((byte)0xBB, instructions[1]);
    }

    [TestMethod]
    public void Glyf_CompositeGlyphInstructions_AreExtracted()
    {
        // Composite glyph with 2 components and trailing instructions.
        byte[] glyph = new byte[]
        {
            0xFF, 0xFF, // numberOfContours = -1 (composite)
            0x00, 0x00, // xMin
            0x00, 0x00, // yMin
            0x00, 0x00, // xMax
            0x00, 0x00, // yMax

            // Component 1: ARGS_ARE_XY_VALUES | MORE_COMPONENTS | WE_HAVE_A_SCALE
            0x00, 0x2A, // flags
            0x00, 0x05, // glyphIndex
            0xFD, 0x07, // dx=-3, dy=+7 (byte args)
            0x20, 0x00, // scale=0.5 (F2Dot14)

            // Component 2: ARG_1_AND_2_ARE_WORDS | WE_HAVE_A_TWO_BY_TWO | WE_HAVE_INSTRUCTIONS
            0x01, 0x81, // flags
            0x00, 0x06, // glyphIndex
            0x00, 0x0A, // parentPoint=10
            0x00, 0x0B, // childPoint=11
            0x40, 0x00, // a=1.0
            0x20, 0x00, // b=0.5
            0xE0, 0x00, // c=-0.5
            0x40, 0x00, // d=1.0

            0x00, 0x03, // instructionLength=3
            0xAA, 0xBB, 0xCC
        };

        Assert.IsTrue(GlyfTable.TryGetCompositeGlyphInstructions(glyph, out var instructions));
        Assert.AreEqual(3, instructions.Length);
        Assert.AreEqual((byte)0xAA, instructions[0]);
        Assert.AreEqual((byte)0xBB, instructions[1]);
        Assert.AreEqual((byte)0xCC, instructions[2]);
    }

    [TestMethod]
    public void Glyf_CompositeGlyphComponentEnumerator_DecodesArgsAndTransform()
    {
        byte[] glyph = new byte[]
        {
            0xFF, 0xFF, // numberOfContours = -1 (composite)
            0x00, 0x00, // xMin
            0x00, 0x00, // yMin
            0x00, 0x00, // xMax
            0x00, 0x00, // yMax

            // Component 1: ARGS_ARE_XY_VALUES | MORE_COMPONENTS | WE_HAVE_A_SCALE
            0x00, 0x2A, // flags
            0x00, 0x05, // glyphIndex
            0xFD, 0x07, // dx=-3, dy=+7 (byte args)
            0x20, 0x00, // scale=0.5 (F2Dot14)

            // Component 2: ARG_1_AND_2_ARE_WORDS | WE_HAVE_A_TWO_BY_TWO
            0x00, 0x81, // flags
            0x00, 0x06, // glyphIndex
            0x00, 0x0A, // parentPoint=10
            0x00, 0x0B, // childPoint=11
            0x40, 0x00, // a=1.0
            0x20, 0x00, // b=0.5
            0xE0, 0x00, // c=-0.5
            0x40, 0x00  // d=1.0
        };

        Assert.IsTrue(GlyfTable.TryReadGlyphHeader(glyph, out var header));
        Assert.IsTrue(header.IsComposite);

        Assert.IsTrue(GlyfTable.TryCreateCompositeGlyphComponentEnumerator(glyph, out var e));
        Assert.IsTrue(e.IsValid);

        Assert.IsTrue(e.MoveNext());
        var c1 = e.Current;
        Assert.AreEqual((ushort)0x002A, c1.Flags);
        Assert.AreEqual((ushort)5, c1.GlyphIndex);
        Assert.IsTrue(c1.TryGetTranslation(out short dx1, out short dy1));
        Assert.AreEqual((short)-3, dx1);
        Assert.AreEqual((short)7, dy1);
        Assert.AreEqual((short)0x2000, c1.A.RawValue);
        Assert.AreEqual((short)0x0000, c1.B.RawValue);
        Assert.AreEqual((short)0x0000, c1.C.RawValue);
        Assert.AreEqual((short)0x2000, c1.D.RawValue);

        Assert.IsTrue(e.MoveNext());
        var c2 = e.Current;
        Assert.AreEqual((ushort)0x0081, c2.Flags);
        Assert.AreEqual((ushort)6, c2.GlyphIndex);
        Assert.IsTrue(c2.TryGetMatchingPoints(out ushort parentPoint, out ushort childPoint));
        Assert.AreEqual((ushort)10, parentPoint);
        Assert.AreEqual((ushort)11, childPoint);
        Assert.AreEqual((short)0x4000, c2.A.RawValue);
        Assert.AreEqual((short)0x2000, c2.B.RawValue);
        Assert.AreEqual(unchecked((short)0xE000), c2.C.RawValue);
        Assert.AreEqual((short)0x4000, c2.D.RawValue);

        Assert.IsFalse(e.MoveNext());
        Assert.IsTrue(e.IsValid);
    }
}
