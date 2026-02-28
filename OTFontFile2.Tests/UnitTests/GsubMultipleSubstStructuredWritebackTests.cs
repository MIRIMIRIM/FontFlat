using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubMultipleSubstStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGsub_WithMultipleSubstLookup()
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

        var multiple = new GsubMultipleSubstSubtableBuilder();
        multiple.AddOrReplace(fromGlyphId: 10, substitutes: new ushort[] { 11, 12 });
        multiple.AddOrReplace(fromGlyphId: 20, substitutes: new ushort[] { 21 });

        var lookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 2, lookupFlag: 0);
        lookup.AddSubtable(multiple.ToMemory());

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
        Assert.AreEqual((ushort)1, lookupList.LookupCount);
        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookupTable));
        Assert.AreEqual((ushort)2, lookupTable.LookupType);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel));

        int subtableOffset = lookupTable.Offset + rel;
        Assert.IsTrue(GsubMultipleSubstSubtable.TryCreate(gsub.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)1, subtable.SubstFormat);

        Assert.IsTrue(subtable.TryGetSequenceForGlyph(glyphId: 10, out bool substituted10, out var seq10));
        Assert.IsTrue(substituted10);
        Assert.AreEqual((ushort)2, seq10.GlyphCount);
        Assert.IsTrue(seq10.TryGetSubstituteGlyphId(0, out ushort s0));
        Assert.IsTrue(seq10.TryGetSubstituteGlyphId(1, out ushort s1));
        CollectionAssert.AreEqual(new ushort[] { 11, 12 }, new[] { s0, s1 });

        Assert.IsTrue(subtable.TryGetSequenceForGlyph(glyphId: 20, out bool substituted20, out var seq20));
        Assert.IsTrue(substituted20);
        Assert.AreEqual((ushort)1, seq20.GlyphCount);
        Assert.IsTrue(seq20.TryGetSubstituteGlyphId(0, out ushort s20));
        Assert.AreEqual((ushort)21, s20);
    }
}

