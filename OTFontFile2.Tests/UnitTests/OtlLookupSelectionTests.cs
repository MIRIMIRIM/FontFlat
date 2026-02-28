using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class OtlLookupSelectionTests
{
    [TestMethod]
    public void SyntheticOtl_TryGetLookupIndexEnumerator_RespectsFilterAndOrder()
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

        ReadOnlySpan<Tag> enabled = stackalloc Tag[] { liga, rlig };
        Assert.IsTrue(view.TryGetLookupIndexEnumerator(latn, eng, enabled, out var e));
        Drain(e, out ushort[] lookups);
        CollectionAssert.AreEqual(new ushort[] { 0, 1, 2 }, lookups);

        ReadOnlySpan<Tag> enabledLiga = stackalloc Tag[] { liga };
        Assert.IsTrue(view.TryGetLookupIndexEnumerator(latn, eng, enabledLiga, out var eLiga));
        Drain(eLiga, out ushort[] lookupsLiga);
        CollectionAssert.AreEqual(new ushort[] { 0, 1 }, lookupsLiga);

        Assert.IsTrue(view.TryGetLookupIndexEnumerator(latn, eng, ReadOnlySpan<Tag>.Empty, out var eAll));
        Drain(eAll, out ushort[] lookupsAll);
        CollectionAssert.AreEqual(new ushort[] { 0, 1, 2 }, lookupsAll);
    }

    private static void Drain(OtlLayoutTable.LookupIndexEnumerator e, out ushort[] lookups)
    {
        var list = new List<ushort>();
        while (e.MoveNext())
            list.Add(e.Current);

        lookups = list.ToArray();
    }
}
