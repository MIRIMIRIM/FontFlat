using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class OtlLookupAutoExtensionTests
{
    [TestMethod]
    public void GSUB_AutoWrapsLookup_WhenSubtableOffset16Overflows()
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

        byte[] st0 = new byte[40_000];
        byte[] st1 = new byte[40_000];
        byte[] st2 = new byte[40_000];
        st0[0] = 1;
        st1[0] = 2;
        st2[0] = 3;

        var lookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        lookup.AddSubtable(st0);
        lookup.AddSubtable(st1);
        lookup.AddSubtable(st2);

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
        Assert.AreEqual((ushort)7, lookupTable.LookupType);
        Assert.AreEqual((ushort)3, lookupTable.SubtableCount);

        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel0));
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(1, out ushort rel1));
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(2, out ushort rel2));
        Assert.IsTrue(rel2 < 128);

        int extOffset0 = lookupTable.Offset + rel0;
        int extOffset1 = lookupTable.Offset + rel1;
        int extOffset2 = lookupTable.Offset + rel2;

        Assert.IsTrue(GsubExtensionSubstSubtable.TryCreate(gsub.Table, extOffset0, out var ext0));
        Assert.AreEqual((ushort)1, ext0.SubstFormat);
        Assert.AreEqual((ushort)1, ext0.ExtensionLookupType);
        Assert.IsTrue(ext0.ExtensionOffset > 8);
        Assert.IsTrue(ext0.TryResolve(out ushort resolvedType0, out int payload0));
        Assert.AreEqual((ushort)1, resolvedType0);
        Assert.IsTrue(gsub.Table.Span.Slice(payload0, st0.Length).SequenceEqual(st0));

        Assert.IsTrue(GsubExtensionSubstSubtable.TryCreate(gsub.Table, extOffset1, out var ext1));
        Assert.AreEqual((ushort)1, ext1.SubstFormat);
        Assert.AreEqual((ushort)1, ext1.ExtensionLookupType);
        Assert.IsTrue(ext1.TryResolve(out _, out int payload1));
        Assert.IsTrue(gsub.Table.Span.Slice(payload1, st1.Length).SequenceEqual(st1));

        Assert.IsTrue(GsubExtensionSubstSubtable.TryCreate(gsub.Table, extOffset2, out var ext2));
        Assert.AreEqual((ushort)1, ext2.SubstFormat);
        Assert.AreEqual((ushort)1, ext2.ExtensionLookupType);
        Assert.IsTrue(ext2.TryResolve(out _, out int payload2));
        Assert.IsTrue(payload2 > ushort.MaxValue);
        Assert.IsTrue(gsub.Table.Span.Slice(payload2, st2.Length).SequenceEqual(st2));
    }

    [TestMethod]
    public void GPOS_AutoWrapsLookup_WhenSubtableOffset16Overflows()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GposTableBuilder>(out var gposBuilder));

        Assert.IsTrue(Tag.TryParse("DFLT", out var dflt));
        Assert.IsTrue(Tag.TryParse("TEST", out var testFeature));

        byte[] st0 = new byte[40_000];
        byte[] st1 = new byte[40_000];
        byte[] st2 = new byte[40_000];
        st0[0] = 1;
        st1[0] = 2;
        st2[0] = 3;

        var lookup = gposBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        lookup.AddSubtable(st0);
        lookup.AddSubtable(st1);
        lookup.AddSubtable(st2);

        var feature = gposBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(lookup);

        var script = gposBuilder.Layout.Scripts.GetOrAddScript(dflt);
        script.GetOrCreateDefaultLangSys().AddFeature(feature);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGpos(out var gpos));
        Assert.IsTrue(gpos.TryGetLookupList(out var lookupList));
        Assert.AreEqual((ushort)1, lookupList.LookupCount);

        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookupTable));
        Assert.AreEqual((ushort)9, lookupTable.LookupType);
        Assert.AreEqual((ushort)3, lookupTable.SubtableCount);

        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel0));
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(1, out ushort rel1));
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(2, out ushort rel2));
        Assert.IsTrue(rel2 < 128);

        int extOffset0 = lookupTable.Offset + rel0;
        int extOffset1 = lookupTable.Offset + rel1;
        int extOffset2 = lookupTable.Offset + rel2;

        Assert.IsTrue(GposExtensionPosSubtable.TryCreate(gpos.Table, extOffset0, out var ext0));
        Assert.AreEqual((ushort)1, ext0.PosFormat);
        Assert.AreEqual((ushort)1, ext0.ExtensionLookupType);
        Assert.IsTrue(ext0.ExtensionOffset > 8);
        Assert.IsTrue(ext0.TryResolve(out ushort resolvedType0, out int payload0));
        Assert.AreEqual((ushort)1, resolvedType0);
        Assert.IsTrue(gpos.Table.Span.Slice(payload0, st0.Length).SequenceEqual(st0));

        Assert.IsTrue(GposExtensionPosSubtable.TryCreate(gpos.Table, extOffset1, out var ext1));
        Assert.AreEqual((ushort)1, ext1.PosFormat);
        Assert.AreEqual((ushort)1, ext1.ExtensionLookupType);
        Assert.IsTrue(ext1.TryResolve(out _, out int payload1));
        Assert.IsTrue(gpos.Table.Span.Slice(payload1, st1.Length).SequenceEqual(st1));

        Assert.IsTrue(GposExtensionPosSubtable.TryCreate(gpos.Table, extOffset2, out var ext2));
        Assert.AreEqual((ushort)1, ext2.PosFormat);
        Assert.AreEqual((ushort)1, ext2.ExtensionLookupType);
        Assert.IsTrue(ext2.TryResolve(out _, out int payload2));
        Assert.IsTrue(payload2 > ushort.MaxValue);
        Assert.IsTrue(gpos.Table.Span.Slice(payload2, st2.Length).SequenceEqual(st2));
    }
}

