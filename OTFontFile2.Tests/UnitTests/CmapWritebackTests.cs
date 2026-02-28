using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CmapWritebackTests
{
    [TestMethod]
    public void CmapTable_CanBuildFormat4AndMapBmp()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { TableVersionNumber = new Fixed1616(0x00010000u), NumGlyphs = 20 };

        var cmapBuilder = new CmapTableBuilder();
        cmapBuilder.AddOrReplaceMapping(0x0041, 3); // 'A'
        cmapBuilder.AddOrReplaceMapping(0x0042, 4); // 'B'
        cmapBuilder.AddOrReplaceMapping(0x0061, 10); // 'a'
        cmapBuilder.AddOrReplaceMapping(0x0062, 12); // 'b' (forces non-delta segment)

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(cmapBuilder);

        byte[] bytes = sfnt.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(bytes));

        using var file = SfntFile.FromMemory(bytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetCmap(out var cmap));

        Assert.IsTrue(cmap.TryGetSubtable(platformId: 3, encodingId: 1, out var st));
        Assert.AreEqual((ushort)4, st.Format);

        Assert.IsTrue(st.TryMapCodePoint(0x0041, out uint gidA));
        Assert.AreEqual(3u, gidA);
        Assert.IsTrue(st.TryMapCodePoint(0x0061, out uint gida));
        Assert.AreEqual(10u, gida);
        Assert.IsTrue(st.TryMapCodePoint(0x0062, out uint gidb));
        Assert.AreEqual(12u, gidb);
    }

    [TestMethod]
    public void CmapTable_CanBuildFormat12AndMapNonBmp()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { TableVersionNumber = new Fixed1616(0x00010000u), NumGlyphs = 100 };

        var cmapBuilder = new CmapTableBuilder();
        cmapBuilder.AddOrReplaceMapping(0x0041, 3); // BMP
        cmapBuilder.AddOrReplaceMapping(0x1F600, 20); // 😀

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(cmapBuilder);

        byte[] bytes = sfnt.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(bytes));

        using var file = SfntFile.FromMemory(bytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetCmap(out var cmap));

        Assert.IsTrue(cmap.TryGetSubtable(platformId: 3, encodingId: 10, out var st));
        Assert.AreEqual((ushort)12, st.Format);

        Assert.IsTrue(st.TryMapCodePoint(0x1F600, out uint gid));
        Assert.AreEqual(20u, gid);
    }

    [TestMethod]
    public void FontModel_CanEditCmapAndWriteBack()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { TableVersionNumber = new Fixed1616(0x00010000u), NumGlyphs = 50 };

        var cmapBuilder = new CmapTableBuilder();
        cmapBuilder.AddOrReplaceMapping(0x0041, 3);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(cmapBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<CmapTableBuilder>(out var edit));
        edit.AddOrReplaceMapping(0x0042, 4);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetCmap(out var editedCmap));

        Assert.IsTrue(editedCmap.TryGetSubtable(platformId: 3, encodingId: 1, out var st));
        Assert.IsTrue(st.TryMapCodePoint(0x0042, out uint gidB));
        Assert.AreEqual(4u, gidB);
    }

    [TestMethod]
    public void FontModel_RejectsCmapGlyphIdsOutsideMaxp()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { TableVersionNumber = new Fixed1616(0x00010000u), NumGlyphs = 5 };

        var cmapBuilder = new CmapTableBuilder();
        cmapBuilder.AddOrReplaceMapping(0x0041, 10); // out of range

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(cmapBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<CmapTableBuilder>(out var edit));
        edit.AddOrReplaceMapping(0x0042, 4);

        _ = Assert.ThrowsException<InvalidOperationException>(() => model.ToArray());

        edit.RemoveMapping(0x0041);
        edit.AddOrReplaceMapping(0x0041, 3);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));
    }

    [TestMethod]
    public void CmapTable_CanBuildFormat14AndQueryUvs()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { TableVersionNumber = new Fixed1616(0x00010000u), NumGlyphs = 30 };

        var cmapBuilder = new CmapTableBuilder();
        cmapBuilder.AddOrReplaceMapping(0x0041, 3);
        cmapBuilder.AddOrReplaceNonDefaultUvsMapping(variationSelector: 0xFE0Fu, unicodeValue: 0x0041u, glyphId: 10);
        cmapBuilder.AddOrReplaceDefaultUvsRange(variationSelector: 0xFE0Eu, startUnicodeValue: 0x0041u, additionalCount: 0);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(cmapBuilder);

        byte[] bytes = sfnt.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(bytes));

        using var file = SfntFile.FromMemory(bytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetCmap(out var cmap));

        Assert.IsTrue(cmap.TryGetSubtable(platformId: 0, encodingId: 5, out var uvsSt));
        Assert.AreEqual((ushort)14, uvsSt.Format);
        Assert.IsTrue(uvsSt.TryGetFormat14(out var format14));

        Assert.IsTrue(format14.TryGetNonDefaultGlyphId(unicodeValue: 0x0041u, variationSelector: 0xFE0Fu, out ushort gid));
        Assert.AreEqual((ushort)10, gid);
        Assert.IsTrue(format14.IsDefaultVariationSequence(unicodeValue: 0x0041u, variationSelector: 0xFE0Eu));
    }

    [TestMethod]
    public void FontModel_RejectsUvsGlyphIdsOutsideMaxp()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { TableVersionNumber = new Fixed1616(0x00010000u), NumGlyphs = 5 };

        var cmapBuilder = new CmapTableBuilder();
        cmapBuilder.AddOrReplaceMapping(0x0041, 3);
        cmapBuilder.AddOrReplaceNonDefaultUvsMapping(variationSelector: 0xFE0Fu, unicodeValue: 0x0041u, glyphId: 10); // out of range

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(cmapBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<CmapTableBuilder>(out var edit));

        _ = Assert.ThrowsException<InvalidOperationException>(() => model.ToArray());

        edit.RemoveNonDefaultUvsMapping(variationSelector: 0xFE0Fu, unicodeValue: 0x0041u);
        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));
    }
}
