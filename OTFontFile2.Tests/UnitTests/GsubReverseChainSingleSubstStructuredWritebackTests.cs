using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubReverseChainSingleSubstStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGsub_WithReverseChainSingleSubstLookup()
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

        var reverse = new GsubReverseChainSingleSubstSubtableBuilder();

        var back = new CoverageTableBuilder();
        back.AddGlyph(2);
        reverse.AddBacktrackCoverage(back);

        var look = new CoverageTableBuilder();
        look.AddGlyph(3);
        reverse.AddLookaheadCoverage(look);

        reverse.AddOrReplace(coveredGlyphId: 5, substituteGlyphId: 10);
        reverse.AddOrReplace(coveredGlyphId: 6, substituteGlyphId: 11);

        var lookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 8, lookupFlag: 0);
        lookup.AddSubtable(reverse.ToMemory());

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
        Assert.AreEqual((ushort)8, lookupTable.LookupType);
        Assert.AreEqual((ushort)1, lookupTable.SubtableCount);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort subtableRel));

        int subtableOffset = lookupTable.Offset + subtableRel;
        Assert.IsTrue(GsubReverseChainSingleSubstSubtable.TryCreate(gsub.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)1, subtable.SubstFormat);
        Assert.AreEqual((ushort)1, subtable.BacktrackGlyphCount);

        Assert.IsTrue(subtable.TryGetBacktrackCoverage(0, out var backCov));
        Assert.IsTrue(backCov.TryGetCoverage(glyphId: 2, out bool backCovered, out ushort backIndex));
        Assert.IsTrue(backCovered);
        Assert.AreEqual((ushort)0, backIndex);

        Assert.IsTrue(subtable.TryGetLookaheadGlyphCount(out ushort lookCount));
        Assert.AreEqual((ushort)1, lookCount);

        Assert.IsTrue(subtable.TryGetLookaheadCoverage(0, out var lookCov));
        Assert.IsTrue(lookCov.TryGetCoverage(glyphId: 3, out bool lookCovered, out ushort lookIndex));
        Assert.IsTrue(lookCovered);
        Assert.AreEqual((ushort)0, lookIndex);

        Assert.IsTrue(subtable.TryGetSubstituteGlyphCount(out ushort substCount));
        Assert.AreEqual((ushort)2, substCount);

        Assert.IsTrue(subtable.TrySubstituteGlyph(glyphId: 5, out bool substituted5, out ushort out5));
        Assert.IsTrue(substituted5);
        Assert.AreEqual((ushort)10, out5);

        Assert.IsTrue(subtable.TrySubstituteGlyph(glyphId: 6, out bool substituted6, out ushort out6));
        Assert.IsTrue(substituted6);
        Assert.AreEqual((ushort)11, out6);

        Assert.IsTrue(subtable.TrySubstituteGlyph(glyphId: 7, out bool substituted7, out ushort out7));
        Assert.IsFalse(substituted7);
        Assert.AreEqual((ushort)7, out7);
    }
}

