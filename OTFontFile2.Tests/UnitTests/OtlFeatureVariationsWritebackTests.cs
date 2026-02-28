using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class OtlFeatureVariationsWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGsub_WithFeatureVariations()
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

        var lookup0 = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        lookup0.AddSubtable(new GsubSingleSubstSubtableBuilder().ToMemory());

        var lookup1 = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        lookup1.AddSubtable(new GsubSingleSubstSubtableBuilder().ToMemory());

        var feature = gsubBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(lookup0);

        var script = gsubBuilder.Layout.Scripts.GetOrAddScript(dflt);
        script.GetOrCreateDefaultLangSys().AddFeature(feature);

        var fv = gsubBuilder.Layout.FeatureVariations.AddRecord();
        fv.Conditions.AddConditionFormat1(
            axisIndex: 0,
            filterRangeMinValue: new F2Dot14(unchecked((short)-16384)), // -1.0
            filterRangeMaxValue: new F2Dot14(0));                        // 0.0

        var replacement = fv.Substitution.CreateReplacementFeatureTable();
        replacement.AddLookup(lookup1);
        fv.Substitution.AddOrReplaceSubstitution(feature, replacement);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGsub(out var gsub));
        Assert.AreEqual(0x00010001u, gsub.Version.RawValue);
        Assert.IsTrue(gsub.HasFeatureVariations);
        Assert.AreNotEqual(0u, gsub.FeatureVariationsOffset);

        Assert.IsTrue(gsub.FeatureVariationsOffset <= int.MaxValue);
        Assert.IsTrue(FeatureVariationsTable.TryCreate(gsub.Table, (int)gsub.FeatureVariationsOffset, out var featureVariations));
        Assert.AreEqual((ushort)1, featureVariations.MajorVersion);
        Assert.AreEqual((ushort)0, featureVariations.MinorVersion);
        Assert.AreEqual(1u, featureVariations.FeatureVariationRecordCount);

        Assert.IsTrue(featureVariations.TryGetFeatureVariationRecord(0, out var record));

        Assert.IsTrue(featureVariations.TryGetConditionSet(record, out var conditionSet));
        Assert.AreEqual((ushort)1, conditionSet.ConditionCount);
        Assert.IsTrue(conditionSet.TryGetCondition(0, out var condition));
        Assert.AreEqual((ushort)1, condition.ConditionFormat);
        Assert.IsTrue(condition.TryGetFormat1(out var c1));
        Assert.AreEqual((ushort)0, c1.AxisIndex);
        Assert.AreEqual(unchecked((short)-16384), c1.FilterRangeMinValue.RawValue);
        Assert.AreEqual((short)0, c1.FilterRangeMaxValue.RawValue);

        Assert.IsTrue(featureVariations.TryGetFeatureTableSubstitution(record, out var subst));
        Assert.AreEqual((ushort)1, subst.Version);
        Assert.AreEqual((ushort)1, subst.SubstitutionCount);

        Assert.IsTrue(subst.TryGetSubstitutionRecord(0, out var substRec));
        Assert.AreEqual((ushort)0, substRec.FeatureIndex);

        Assert.IsTrue(subst.TryGetFeature(substRec, out var substitutedFeature));
        Assert.AreEqual((ushort)1, substitutedFeature.LookupIndexCount);
        Assert.IsTrue(substitutedFeature.TryGetLookupListIndex(0, out ushort lookupIndex));
        Assert.AreEqual((ushort)1, lookupIndex);
    }
}

