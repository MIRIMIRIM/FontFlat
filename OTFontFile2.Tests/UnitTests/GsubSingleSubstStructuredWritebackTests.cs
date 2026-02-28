using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubSingleSubstStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGsub_WithSingleSubstLookup()
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

        var single = new GsubSingleSubstSubtableBuilder();
        single.AddOrReplace(fromGlyphId: 10, toGlyphId: 12);
        single.AddOrReplace(fromGlyphId: 11, toGlyphId: 13);

        var lookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        lookup.AddSubtable(single.ToMemory());

        var feature = gsubBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(lookup);

        var script = gsubBuilder.Layout.Scripts.GetOrAddScript(dflt);
        script.GetOrCreateDefaultLangSys().AddFeature(feature);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGsub(out var gsub));
        Assert.AreEqual(0x00010000u, gsub.Version.RawValue);

        Assert.IsTrue(gsub.TryGetScriptList(out var scriptList));
        Assert.AreEqual((ushort)1, scriptList.ScriptCount);
        Assert.IsTrue(scriptList.TryGetScriptRecord(0, out var scriptRecord));
        Assert.AreEqual(dflt, scriptRecord.ScriptTag);
        Assert.IsTrue(scriptList.TryGetScript(scriptRecord, out var scriptTable));
        Assert.IsTrue(scriptTable.TryGetDefaultLangSys(out var langSys));
        Assert.AreEqual((ushort)1, langSys.FeatureIndexCount);
        Assert.IsTrue(langSys.TryGetFeatureIndex(0, out ushort featureIndex));
        Assert.AreEqual((ushort)0, featureIndex);

        Assert.IsTrue(gsub.TryGetFeatureList(out var featureList));
        Assert.AreEqual((ushort)1, featureList.FeatureCount);
        Assert.IsTrue(featureList.TryGetFeatureRecord(0, out var featureRecord));
        Assert.AreEqual(testFeature, featureRecord.FeatureTag);
        Assert.IsTrue(featureList.TryGetFeature(featureRecord, out var featureTable));
        Assert.AreEqual((ushort)1, featureTable.LookupIndexCount);
        Assert.IsTrue(featureTable.TryGetLookupListIndex(0, out ushort lookupIndex));
        Assert.AreEqual((ushort)0, lookupIndex);

        Assert.IsTrue(gsub.TryGetLookupList(out var lookupList));
        Assert.AreEqual((ushort)1, lookupList.LookupCount);
        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookupTable));
        Assert.AreEqual((ushort)1, lookupTable.LookupType);
        Assert.AreEqual((ushort)1, lookupTable.SubtableCount);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort subtableRel));

        int subtableOffset = lookupTable.Offset + subtableRel;
        Assert.IsTrue(GsubSingleSubstSubtable.TryCreate(gsub.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)1, subtable.SubstFormat);

        Assert.IsTrue(subtable.TrySubstituteGlyph(glyphId: 10, out bool substituted10, out ushort out10));
        Assert.IsTrue(substituted10);
        Assert.AreEqual((ushort)12, out10);

        Assert.IsTrue(subtable.TrySubstituteGlyph(glyphId: 11, out bool substituted11, out ushort out11));
        Assert.IsTrue(substituted11);
        Assert.AreEqual((ushort)13, out11);

        Assert.IsTrue(subtable.TrySubstituteGlyph(glyphId: 9, out bool substituted9, out ushort out9));
        Assert.IsFalse(substituted9);
        Assert.AreEqual((ushort)9, out9);
    }
}

