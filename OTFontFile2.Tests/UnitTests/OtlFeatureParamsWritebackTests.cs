using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class OtlFeatureParamsWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteFeatureParams_ForGsubFeature()
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
        single.AddOrReplace(fromGlyphId: 10, toGlyphId: 11);

        var lookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        lookup.AddSubtable(single.ToMemory());

        var feature = gsubBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(lookup);
        feature.SetFeatureParams(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        var script = gsubBuilder.Layout.Scripts.GetOrAddScript(dflt);
        script.GetOrCreateDefaultLangSys().AddFeature(feature);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGsub(out var gsub));
        Assert.IsTrue(gsub.TryGetFeatureList(out var featureList));
        Assert.AreEqual((ushort)1, featureList.FeatureCount);
        Assert.IsTrue(featureList.TryGetFeatureRecord(0, out var featureRecord));
        Assert.AreEqual(testFeature, featureRecord.FeatureTag);
        Assert.IsTrue(featureList.TryGetFeature(featureRecord, out var featureTable));

        Assert.AreNotEqual((ushort)0, featureTable.FeatureParamsOffset);
        int featureAbs = gsub.FeatureListOffset + featureRecord.FeatureOffset;
        int paramsAbs = featureAbs + featureTable.FeatureParamsOffset;

        CollectionAssert.AreEqual(
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
            gsub.Table.Span.Slice(paramsAbs, 4).ToArray());
    }
}

