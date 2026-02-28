using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class SvgTableValidationTests
{
    [TestMethod]
    public void SvgTableBuilder_OverlappingRanges_Throws()
    {
        var svg = new SvgTableBuilder();
        svg.AddDocument(startGlyphId: 5, endGlyphId: 10, documentBytes: new byte[] { 1 });
        svg.AddDocument(startGlyphId: 10, endGlyphId: 12, documentBytes: new byte[] { 2 });

        Assert.ThrowsException<InvalidOperationException>(() => svg.ToArray());
    }

    [TestMethod]
    public void SvgTableBuilder_XmlWithoutSvgTag_Throws_WhenValidationEnabled()
    {
        var svg = new SvgTableBuilder { ValidateSvgPayload = true };
        svg.AddDocument(startGlyphId: 5, endGlyphId: 5, documentBytes: System.Text.Encoding.ASCII.GetBytes("<html></html>"));

        Assert.ThrowsException<InvalidOperationException>(() => svg.ToArray());
    }
}

