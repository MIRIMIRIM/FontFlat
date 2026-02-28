using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GlyfSimpleGlyphOutlineTests
{
    [TestMethod]
    public void Glyf_SimpleGlyphPointEnumerator_DecodesTriangle()
    {
        // Simple glyph:
        // - 1 contour
        // - 3 points (endPt = 2)
        // - no instructions
        // - points: (0,0), (50,0), (50,50) all on-curve

        byte[] glyph = new byte[]
        {
            0x00, 0x01, // numberOfContours = 1
            0x00, 0x00, // xMin
            0x00, 0x00, // yMin
            0x00, 0x32, // xMax = 50
            0x00, 0x32, // yMax = 50

            0x00, 0x02, // endPtsOfContours[0] = 2
            0x00, 0x00, // instructionLength = 0

            0x31,       // flag0: on-curve + x same (0) + y same (0)
            0x33,       // flag1: on-curve + x short + x pos + y same (0)
            0x35,       // flag2: on-curve + y short + y pos + x same (0)

            0x32,       // x delta for point1 = +50
            0x32        // y delta for point2 = +50
        };

        Assert.IsTrue(GlyfTable.TryReadGlyphHeader(glyph, out var header));
        Assert.AreEqual((short)1, header.NumberOfContours);
        Assert.IsFalse(header.IsComposite);

        Assert.IsTrue(GlyfTable.TryCreateSimpleGlyphPointEnumerator(glyph, out var e));
        Assert.AreEqual((ushort)1, e.ContourCount);
        Assert.AreEqual((ushort)3, e.PointCount);

        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((short)0, e.Current.X);
        Assert.AreEqual((short)0, e.Current.Y);
        Assert.IsTrue(e.Current.OnCurve);
        Assert.IsFalse(e.Current.IsContourEnd);

        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((short)50, e.Current.X);
        Assert.AreEqual((short)0, e.Current.Y);
        Assert.IsTrue(e.Current.OnCurve);
        Assert.IsFalse(e.Current.IsContourEnd);

        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((short)50, e.Current.X);
        Assert.AreEqual((short)50, e.Current.Y);
        Assert.IsTrue(e.Current.OnCurve);
        Assert.IsTrue(e.Current.IsContourEnd);

        Assert.IsFalse(e.MoveNext());
    }
}

