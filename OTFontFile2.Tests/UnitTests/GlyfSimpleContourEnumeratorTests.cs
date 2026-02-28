using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GlyfSimpleContourEnumeratorTests
{
    [TestMethod]
    public void Glyf_SimpleGlyphContourEnumerator_DecodesContourEndpoints()
    {
        byte[] glyph = new byte[]
        {
            0x00, 0x01, // numberOfContours = 1
            0x00, 0x00, // xMin
            0x00, 0x00, // yMin
            0x00, 0x00, // xMax
            0x00, 0x00, // yMax

            0x00, 0x02, // endPtsOfContours[0] = 2
            0x00, 0x00, // instructionLength = 0

            0x31, // flag0
            0x31, // flag1
            0x31  // flag2
        };

        Assert.IsTrue(GlyfTable.TryCreateSimpleGlyphContourEnumerator(glyph, out var e));
        Assert.AreEqual((ushort)1, e.ContourCount);
        Assert.AreEqual((ushort)3, e.PointCount);

        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((ushort)0, e.Current.StartPointIndex);
        Assert.AreEqual((ushort)2, e.Current.EndPointIndex);

        Assert.IsFalse(e.MoveNext());
    }

    [TestMethod]
    public void Glyf_SimpleGlyphPointEnumerator_RejectsNonMonotonicEndPts()
    {
        byte[] glyph = new byte[]
        {
            0x00, 0x02, // numberOfContours = 2
            0x00, 0x00, // xMin
            0x00, 0x00, // yMin
            0x00, 0x00, // xMax
            0x00, 0x00, // yMax

            0x00, 0x02, // endPtsOfContours[0] = 2
            0x00, 0x01, // endPtsOfContours[1] = 1  (invalid: decreasing)
            0x00, 0x00  // instructionLength = 0
        };

        Assert.IsFalse(GlyfTable.TryCreateSimpleGlyphPointEnumerator(glyph, out _));
        Assert.IsFalse(GlyfTable.TryCreateSimpleGlyphContourEnumerator(glyph, out _));
    }

    [TestMethod]
    public void Glyf_SimpleGlyphPointEnumerator_RejectsEndPtOverflow()
    {
        byte[] glyph = new byte[]
        {
            0x00, 0x01, // numberOfContours = 1
            0x00, 0x00, // xMin
            0x00, 0x00, // yMin
            0x00, 0x00, // xMax
            0x00, 0x00, // yMax

            0xFF, 0xFF, // endPtsOfContours[0] = 65535 (invalid)
            0x00, 0x00  // instructionLength = 0
        };

        Assert.IsFalse(GlyfTable.TryCreateSimpleGlyphPointEnumerator(glyph, out _));
        Assert.IsFalse(GlyfTable.TryCreateSimpleGlyphContourEnumerator(glyph, out _));
    }
}

