using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubAlternateSubstStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGsub_WithAlternateSubstLookup()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GsubTableBuilder>(out var gsubBuilder));

        Assert.IsTrue(Tag.TryParse("DFLT", out var dflt));
        Assert.IsTrue(Tag.TryParse("TEST", out var testFeature));

        var alternate = new GsubAlternateSubstSubtableBuilder();
        alternate.AddOrReplace(fromGlyphId: 10, alternates: new ushort[] { 11, 12, 13 });

        var lookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 3, lookupFlag: 0);
        lookup.AddSubtable(alternate.ToMemory());

        var feature = gsubBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(lookup);

        var script = gsubBuilder.Layout.Scripts.GetOrAddScript(dflt);
        script.GetOrCreateDefaultLangSys().AddFeature(feature);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGsub(out var gsub));
        Assert.IsTrue(gsub.TryGetLookupList(out var lookupList));
        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookupTable));
        Assert.AreEqual((ushort)3, lookupTable.LookupType);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel));

        int subtableOffset = lookupTable.Offset + rel;
        Assert.IsTrue(GsubAlternateSubstSubtable.TryCreate(gsub.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)1, subtable.SubstFormat);

        Assert.IsTrue(subtable.TryGetAlternateSetForGlyph(glyphId: 10, out bool substituted, out var set));
        Assert.IsTrue(substituted);
        Assert.AreEqual((ushort)3, set.GlyphCount);
        Assert.IsTrue(set.TryGetAlternateGlyphId(0, out ushort a0));
        Assert.IsTrue(set.TryGetAlternateGlyphId(1, out ushort a1));
        Assert.IsTrue(set.TryGetAlternateGlyphId(2, out ushort a2));
        CollectionAssert.AreEqual(new ushort[] { 11, 12, 13 }, new[] { a0, a1, a2 });
    }
}

