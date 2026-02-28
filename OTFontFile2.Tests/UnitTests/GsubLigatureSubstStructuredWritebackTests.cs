using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubLigatureSubstStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGsub_WithLigatureSubstLookup()
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

        var lig = new GsubLigatureSubstSubtableBuilder();
        lig.AddOrReplace(firstGlyphId: 10, remainingComponents: new ushort[] { 20 }, ligatureGlyphId: 30);
        lig.AddOrReplace(firstGlyphId: 10, remainingComponents: new ushort[] { 21 }, ligatureGlyphId: 31);

        var lookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 4, lookupFlag: 0);
        lookup.AddSubtable(lig.ToMemory());

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
        Assert.AreEqual((ushort)4, lookupTable.LookupType);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel));

        int subtableOffset = lookupTable.Offset + rel;
        Assert.IsTrue(GsubLigatureSubstSubtable.TryCreate(gsub.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)1, subtable.SubstFormat);

        Assert.IsTrue(subtable.TryGetLigatureSetForGlyph(glyphId: 10, out bool substituted, out var set));
        Assert.IsTrue(substituted);
        Assert.AreEqual((ushort)2, set.LigatureCount);

        Span<ushort> ligGlyphs = stackalloc ushort[2];
        Span<ushort> secondComponents = stackalloc ushort[2];

        for (int i = 0; i < 2; i++)
        {
            Assert.IsTrue(set.TryGetLigature(i, out var ligTable));
            ligGlyphs[i] = ligTable.LigGlyph;
            Assert.AreEqual((ushort)2, ligTable.ComponentCount);
            Assert.IsTrue(ligTable.TryGetComponentGlyphId(0, out ushort c1));
            secondComponents[i] = c1;
        }

        // We don't require a particular ordering, but the set must contain both ligatures.
        CollectionAssert.AreEquivalent(
            new ushort[] { 30, 31 },
            ligGlyphs.ToArray());
        CollectionAssert.AreEquivalent(
            new ushort[] { 20, 21 },
            secondComponents.ToArray());
    }
}

