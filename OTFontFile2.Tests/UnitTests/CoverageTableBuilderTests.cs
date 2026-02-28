using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CoverageTableBuilderTests
{
    [TestMethod]
    public void CoverageTableBuilder_BuildsFormat1_WhenNotWorseThanFormat2()
    {
        var b = new CoverageTableBuilder();
        b.AddGlyphs(new ushort[] { 7, 6, 5, 6 }); // includes duplicates and unsorted

        Assert.IsTrue(Tag.TryParse("TEST", out var testTag));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(testTag, b.ToArray());

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetTableSlice(testTag, out var slice));
        Assert.IsTrue(CoverageTable.TryCreate(slice, 0, out var coverage));
        Assert.AreEqual((ushort)1, coverage.CoverageFormat);

        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 4, out bool covered4, out ushort index4));
        Assert.IsFalse(covered4);
        Assert.AreEqual((ushort)0, index4);

        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 5, out bool covered5, out ushort index5));
        Assert.IsTrue(covered5);
        Assert.AreEqual((ushort)0, index5);

        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 6, out bool covered6, out ushort index6));
        Assert.IsTrue(covered6);
        Assert.AreEqual((ushort)1, index6);

        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 7, out bool covered7, out ushort index7));
        Assert.IsTrue(covered7);
        Assert.AreEqual((ushort)2, index7);
    }

    [TestMethod]
    public void CoverageTableBuilder_BuildsFormat2_ForContiguousRanges()
    {
        var b = new CoverageTableBuilder();
        b.AddGlyphs(new ushort[] { 5, 6, 7, 8 });

        Assert.IsTrue(Tag.TryParse("TEST", out var testTag));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(testTag, b.ToArray());

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetTableSlice(testTag, out var slice));
        Assert.IsTrue(CoverageTable.TryCreate(slice, 0, out var coverage));
        Assert.AreEqual((ushort)2, coverage.CoverageFormat);

        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 5, out bool covered5, out ushort index5));
        Assert.IsTrue(covered5);
        Assert.AreEqual((ushort)0, index5);

        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 8, out bool covered8, out ushort index8));
        Assert.IsTrue(covered8);
        Assert.AreEqual((ushort)3, index8);
    }

    [TestMethod]
    public void CoverageTable_Format2_ReturnsFalse_WhenCoverageIndexOverflowsUInt16()
    {
        byte[] tableData =
        {
            0x00, 0x02, // CoverageFormat = 2
            0x00, 0x01, // RangeCount = 1
            0x00, 0x0A, // StartGlyphId = 10
            0x00, 0x14, // EndGlyphId = 20
            0xFF, 0xFF  // StartCoverageIndex = 65535
        };

        Assert.IsTrue(Tag.TryParse("TEST", out var tag));
        TableSlice slice = TableSlice.CreateStandalone(tag, tableData);
        Assert.IsTrue(CoverageTable.TryCreate(slice, 0, out var coverage));

        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 10, out bool coveredStart, out ushort startIndex));
        Assert.IsTrue(coveredStart);
        Assert.AreEqual(ushort.MaxValue, startIndex);

        Assert.IsFalse(coverage.TryGetCoverage(glyphId: 20, out _, out _));
    }
}
