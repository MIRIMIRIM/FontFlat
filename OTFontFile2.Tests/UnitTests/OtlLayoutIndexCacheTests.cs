using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class OtlLayoutIndexCacheTests
{
    [TestMethod]
    public void SyntheticOtlIndexCache_LookupEnumerator_MatchesView()
    {
        Assert.IsTrue(Tag.TryParse("latn", out var latn));
        Assert.IsTrue(Tag.TryParse("ENG ", out var eng));
        Assert.IsTrue(Tag.TryParse("ccmp", out var ccmp));
        Assert.IsTrue(Tag.TryParse("liga", out var liga));
        Assert.IsTrue(Tag.TryParse("rlig", out var rlig));

        var gsubBuilder = new GsubTableBuilder();
        var layout = gsubBuilder.Layout;

        var l0 = layout.Lookups.AddLookup(lookupType: 1);
        var l1 = layout.Lookups.AddLookup(lookupType: 2);
        var l2 = layout.Lookups.AddLookup(lookupType: 3);

        var fCcmp = layout.Features.GetOrAddFeature(ccmp);
        var fLiga = layout.Features.GetOrAddFeature(liga);
        var fRlig = layout.Features.GetOrAddFeature(rlig);

        fCcmp.AddLookup(l0);
        fLiga.AddLookup(l1);
        fRlig.AddLookup(l2);

        var script = layout.Scripts.GetOrAddScript(latn);
        var dflt = script.GetOrCreateDefaultLangSys();
        dflt.RequiredFeature = fCcmp;
        dflt.AddFeature(fLiga);
        dflt.AddFeature(fRlig);

        var sfnt = new SfntBuilder();
        sfnt.SetTable(KnownTags.GSUB, gsubBuilder.DataBytes.ToArray());
        byte[] fontBytes = sfnt.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetTableSlice(KnownTags.GSUB, out var gsubSlice));
        Assert.IsTrue(OtlLayoutTable.TryCreate(gsubSlice, out var view));

        Assert.IsTrue(font.TryGetGsub(out var gsub));
        Assert.IsTrue(OtlLayoutIndexCache.TryCreate(gsub, out var cache));

        ReadOnlySpan<Tag> enabled = stackalloc Tag[] { liga, rlig };
        Assert.IsTrue(view.TryGetLookupIndexEnumerator(latn, eng, enabled, out var eView));
        Assert.IsTrue(cache.TryGetLookupIndexEnumerator(latn, eng, enabled, out var eCache));
        Drain(eView, out ushort[] viewLookups);
        Drain(eCache, out ushort[] cacheLookups);
        CollectionAssert.AreEqual(viewLookups, cacheLookups);

        ReadOnlySpan<Tag> enabledLiga = stackalloc Tag[] { liga };
        Assert.IsTrue(view.TryGetLookupIndexEnumerator(latn, eng, enabledLiga, out var eViewLiga));
        Assert.IsTrue(cache.TryGetLookupIndexEnumerator(latn, eng, enabledLiga, out var eCacheLiga));
        Drain(eViewLiga, out ushort[] viewLookupsLiga);
        Drain(eCacheLiga, out ushort[] cacheLookupsLiga);
        CollectionAssert.AreEqual(viewLookupsLiga, cacheLookupsLiga);

        Assert.IsTrue(view.TryGetLookupIndexEnumerator(latn, eng, ReadOnlySpan<Tag>.Empty, out var eViewAll));
        Assert.IsTrue(cache.TryGetLookupIndexEnumerator(latn, eng, ReadOnlySpan<Tag>.Empty, out var eCacheAll));
        Drain(eViewAll, out ushort[] viewLookupsAll);
        Drain(eCacheAll, out ushort[] cacheLookupsAll);
        CollectionAssert.AreEqual(viewLookupsAll, cacheLookupsAll);
    }

    private static void Drain(OtlLayoutTable.LookupIndexEnumerator e, out ushort[] lookups)
    {
        var list = new List<ushort>();
        while (e.MoveNext())
            list.Add(e.Current);
        lookups = list.ToArray();
    }

    private static void Drain(OtlLayoutIndexCache.LookupIndexEnumerator e, out ushort[] lookups)
    {
        var list = new List<ushort>();
        while (e.MoveNext())
            list.Add(e.Current);
        lookups = list.ToArray();
    }
}
