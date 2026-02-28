using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class OtlTagFindTests
{
    [TestMethod]
    public void SyntheticGsub_TryFindScriptFeatureLangSys_Works()
    {
        Assert.IsTrue(Tag.TryParse("DFLT", out var dflt));
        Assert.IsTrue(Tag.TryParse("latn", out var latn));
        Assert.IsTrue(Tag.TryParse("ENG ", out var eng));
        Assert.IsTrue(Tag.TryParse("liga", out var liga));
        Assert.IsTrue(Tag.TryParse("rlig", out var rlig));

        var gsubBuilder = new GsubTableBuilder();
        var layout = gsubBuilder.Layout;

        var ligaFeature = layout.Features.GetOrAddFeature(liga);
        var rligFeature = layout.Features.GetOrAddFeature(rlig);

        var dfltScript = layout.Scripts.GetOrAddScript(dflt);
        dfltScript.GetOrCreateDefaultLangSys().AddFeature(ligaFeature);

        var latnScript = layout.Scripts.GetOrAddScript(latn);
        latnScript.GetOrCreateDefaultLangSys().AddFeature(ligaFeature);
        latnScript.GetOrAddLangSys(eng).AddFeature(rligFeature);

        byte[] gsubBytes = gsubBuilder.DataBytes.ToArray();

        var sfnt = new SfntBuilder();
        sfnt.SetTable(KnownTags.GSUB, gsubBytes);
        byte[] fontBytes = sfnt.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGsub(out var gsub));
        Assert.IsTrue(font.TryGetTableSlice(KnownTags.GSUB, out var gsubSlice));
        Assert.IsTrue(OtlLayoutTable.TryCreate(gsubSlice, out var layoutView));
        Assert.IsTrue(gsub.TryGetScriptList(out var scriptList));
        Assert.IsTrue(gsub.TryGetFeatureList(out var featureList));

        Assert.IsTrue(scriptList.TryFindScript(latn, out var latnScriptTable));
        Assert.IsTrue(layoutView.TryFindScriptOrDefault(latn, out var latnScriptViaLayout));
        Assert.IsTrue(latnScriptViaLayout.TryFindLangSysOrDefault(eng, out var engLangSys));
        Assert.AreEqual((ushort)1, engLangSys.FeatureIndexCount);
        Assert.IsTrue(engLangSys.TryGetFeatureIndex(0, out ushort engFeatureIndex));
        Assert.AreEqual((ushort)1, engFeatureIndex); // 'rlig' should sort after 'liga'

        Assert.IsTrue(latnScriptTable.TryGetDefaultLangSys(out var defaultLangSys));
        Assert.AreEqual((ushort)1, defaultLangSys.FeatureIndexCount);
        Assert.IsTrue(defaultLangSys.TryGetFeatureIndex(0, out ushort defaultFeatureIndex));
        Assert.AreEqual((ushort)0, defaultFeatureIndex);

        Assert.IsTrue(featureList.TryFindFeature(liga, out var ligaFeatureTable));
        Assert.AreEqual((ushort)0, ligaFeatureTable.LookupIndexCount);

        Assert.IsTrue(featureList.TryFindFeature(rlig, out var rligFeatureTable));
        Assert.AreEqual((ushort)0, rligFeatureTable.LookupIndexCount);

        Assert.IsTrue(layoutView.TryFindFeature(liga, out var ligaFeatureViaLayout));
        Assert.AreEqual(ligaFeatureTable.LookupIndexCount, ligaFeatureViaLayout.LookupIndexCount);
    }
}
