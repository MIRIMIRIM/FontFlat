using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CmapReverseMapTests
{
    [TestMethod]
    public void SyntheticCmapReverseMap_Format12_PicksLowestUnicodeAndCapturesUvs()
    {
        var cmapBuilder = new CmapTableBuilder();
        cmapBuilder.AddOrReplaceMapping(0x0041, glyphId: 3);  // 'A'
        cmapBuilder.AddOrReplaceMapping(0x0042, glyphId: 3);  // 'B' -> same glyph
        cmapBuilder.AddOrReplaceMapping(0x1F600, glyphId: 5); // 😀
        cmapBuilder.AddOrReplaceNonDefaultUvsMapping(0xFE0F, 0x0041, glyphId: 7); // VS16 for 'A'

        var maxpBuilder = new MaxpTableBuilder { NumGlyphs = 20 };

        var sfnt = new SfntBuilder();
        sfnt.SetTable(cmapBuilder);
        sfnt.SetTable(maxpBuilder);
        byte[] fontBytes = sfnt.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(CmapUnicodeMap.TryCreate(font, out var map));
        Assert.AreEqual((ushort)12, map.Format);

        Assert.IsTrue(map.TryCreateReverseMap(numGlyphs: 20, out var reverse));
        Assert.AreEqual((ushort)12, reverse.Format);

        Assert.IsTrue(reverse.TryGetCodePoint(glyphId: 3, out uint cp3));
        Assert.AreEqual(0x0041u, cp3);

        Assert.IsTrue(reverse.TryGetCodePoint(glyphId: 5, out uint cp5));
        Assert.AreEqual(0x1F600u, cp5);

        Assert.IsTrue(reverse.HasNonDefaultUvs);
        Assert.IsTrue(reverse.TryGetNonDefaultVariationSequence(glyphId: 7, out uint uvsUnicode, out uint uvsSelector));
        Assert.AreEqual(0x0041u, uvsUnicode);
        Assert.AreEqual(0xFE0Fu, uvsSelector);

        Assert.IsTrue(CmapReverseMap.TryCreate(font, out var reverse2));
        Assert.IsTrue(reverse2.TryGetCodePoint(glyphId: 3, out uint cp3b));
        Assert.AreEqual(0x0041u, cp3b);
        Assert.IsTrue(reverse2.TryGetNonDefaultVariationSequence(glyphId: 7, out uint uvsUnicode2, out uint uvsSelector2));
        Assert.AreEqual(0x0041u, uvsUnicode2);
        Assert.AreEqual(0xFE0Fu, uvsSelector2);
    }

    [TestMethod]
    public void SyntheticCmapReverseMap_Format4_MapsBmpGlyphs()
    {
        var cmapBuilder = new CmapTableBuilder();
        cmapBuilder.AddOrReplaceMapping(0x0041, glyphId: 10); // 'A' (forces glyphIdArray segment)
        cmapBuilder.AddOrReplaceMapping(0x0042, glyphId: 3);  // 'B'
        cmapBuilder.AddOrReplaceMapping(0x0044, glyphId: 20); // 'D' (delta segment)
        cmapBuilder.AddOrReplaceMapping(0x0045, glyphId: 21); // 'E'

        var maxpBuilder = new MaxpTableBuilder { NumGlyphs = 30 };

        var sfnt = new SfntBuilder();
        sfnt.SetTable(cmapBuilder);
        sfnt.SetTable(maxpBuilder);
        byte[] fontBytes = sfnt.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(CmapUnicodeMap.TryCreate(font, out var map));
        Assert.AreEqual((ushort)4, map.Format);
        Assert.IsFalse(map.HasVariationSequences);

        Assert.IsTrue(map.TryCreateReverseMap(numGlyphs: 30, out var reverse));
        Assert.AreEqual((ushort)4, reverse.Format);

        Assert.IsTrue(reverse.TryGetCodePoint(glyphId: 10, out uint cp10));
        Assert.AreEqual(0x0041u, cp10);

        Assert.IsTrue(reverse.TryGetCodePoint(glyphId: 3, out uint cp3));
        Assert.AreEqual(0x0042u, cp3);

        Assert.IsTrue(reverse.TryGetCodePoint(glyphId: 20, out uint cp20));
        Assert.AreEqual(0x0044u, cp20);

        Assert.IsTrue(reverse.TryGetCodePoint(glyphId: 21, out uint cp21));
        Assert.AreEqual(0x0045u, cp21);

        Assert.IsFalse(reverse.HasNonDefaultUvs);
        Assert.IsFalse(reverse.TryGetNonDefaultVariationSequence(glyphId: 10, out _, out _));

        Assert.IsTrue(CmapReverseMap.TryCreate(font, out var reverse2));
        Assert.IsTrue(reverse2.TryGetCodePoint(glyphId: 10, out uint cp10b));
        Assert.AreEqual(0x0041u, cp10b);
    }
}

