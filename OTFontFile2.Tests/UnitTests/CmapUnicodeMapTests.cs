using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CmapUnicodeMapTests
{
    [TestMethod]
    public void SyntheticCmapUnicodeMap_SelectsFullUnicodeAndMapsUvs()
    {
        var cmapBuilder = new CmapTableBuilder();
        cmapBuilder.AddOrReplaceMapping(0x0041, glyphId: 3);      // 'A'
        cmapBuilder.AddOrReplaceMapping(0x1F600, glyphId: 5);     // 😀
        cmapBuilder.AddOrReplaceNonDefaultUvsMapping(0xFE0F, 0x0041, glyphId: 7); // VS16 for 'A'

        var sfnt = new SfntBuilder();
        sfnt.SetTable(cmapBuilder);
        byte[] fontBytes = sfnt.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(CmapUnicodeMap.TryCreate(font, out var map));
        Assert.AreEqual((ushort)0, map.PlatformId);
        Assert.AreEqual((ushort)4, map.EncodingId);
        Assert.AreEqual((ushort)12, map.Format);
        Assert.IsTrue(map.HasVariationSequences);
        Assert.IsTrue(map.TryCreateFastMap(out var fastMap));
        Assert.AreEqual((ushort)12, fastMap.Format);

        Assert.IsTrue(map.TryMapCodePoint(0x0041, out uint glyphA));
        Assert.AreEqual(3u, glyphA);
        Assert.IsTrue(fastMap.TryMapCodePoint(0x0041, out uint fastGlyphA));
        Assert.AreEqual(3u, fastGlyphA);

        Assert.IsTrue(map.TryMapCodePoint(0x1F600, out uint glyphSmile));
        Assert.AreEqual(5u, glyphSmile);
        Assert.IsTrue(fastMap.TryMapCodePoint(0x1F600, out uint fastGlyphSmile));
        Assert.AreEqual(5u, fastGlyphSmile);

        Assert.IsTrue(map.TryMapVariationSequence(0x0041, 0xFE0F, out uint glyphAFe0f));
        Assert.AreEqual(7u, glyphAFe0f);

        // No explicit UVS mapping for 😀; should fall back to base cmap.
        Assert.IsTrue(map.TryMapVariationSequence(0x1F600, 0xFE0F, out uint glyphSmileFe0f));
        Assert.AreEqual(5u, glyphSmileFe0f);
    }

    [TestMethod]
    public void SyntheticCmapUnicodeMap_Format4FastMap_MatchesMapping()
    {
        var cmapBuilder = new CmapTableBuilder();
        cmapBuilder.AddOrReplaceMapping(0x0041, glyphId: 10); // 'A' (forces glyphIdArray segment)
        cmapBuilder.AddOrReplaceMapping(0x0042, glyphId: 3);  // 'B'
        cmapBuilder.AddOrReplaceMapping(0x0044, glyphId: 20); // 'D' (delta segment)
        cmapBuilder.AddOrReplaceMapping(0x0045, glyphId: 21); // 'E'

        var sfnt = new SfntBuilder();
        sfnt.SetTable(cmapBuilder);
        byte[] fontBytes = sfnt.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(CmapUnicodeMap.TryCreate(font, out var map));
        Assert.AreEqual((ushort)0, map.PlatformId);
        Assert.AreEqual((ushort)3, map.EncodingId);
        Assert.AreEqual((ushort)4, map.Format);
        Assert.IsFalse(map.HasVariationSequences);
        Assert.IsTrue(map.TryCreateFastMap(out var fastMap));
        Assert.AreEqual((ushort)4, fastMap.Format);

        Assert.IsTrue(map.TryMapCodePoint(0x0041, out uint glyphA));
        Assert.AreEqual(10u, glyphA);
        Assert.IsTrue(fastMap.TryMapCodePoint(0x0041, out uint fastGlyphA));
        Assert.AreEqual(10u, fastGlyphA);

        Assert.IsTrue(map.TryMapCodePoint(0x0042, out uint glyphB));
        Assert.AreEqual(3u, glyphB);
        Assert.IsTrue(fastMap.TryMapCodePoint(0x0042, out uint fastGlyphB));
        Assert.AreEqual(3u, fastGlyphB);

        Assert.IsTrue(map.TryMapCodePoint(0x0043, out uint glyphC));
        Assert.AreEqual(0u, glyphC); // not mapped
        Assert.IsTrue(fastMap.TryMapCodePoint(0x0043, out uint fastGlyphC));
        Assert.AreEqual(0u, fastGlyphC);

        Assert.IsTrue(map.TryMapCodePoint(0x0044, out uint glyphD));
        Assert.AreEqual(20u, glyphD);
        Assert.IsTrue(fastMap.TryMapCodePoint(0x0044, out uint fastGlyphD));
        Assert.AreEqual(20u, fastGlyphD);

        Assert.IsTrue(map.TryMapCodePoint(0x0045, out uint glyphE));
        Assert.AreEqual(21u, glyphE);
        Assert.IsTrue(fastMap.TryMapCodePoint(0x0045, out uint fastGlyphE));
        Assert.AreEqual(21u, fastGlyphE);

        Assert.IsFalse(map.TryMapCodePoint(0x1F600, out _));
        Assert.IsFalse(fastMap.TryMapCodePoint(0x1F600, out _));
    }
}
