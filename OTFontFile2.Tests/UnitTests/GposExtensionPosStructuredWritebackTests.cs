using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposExtensionPosStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithExtensionPosWrappingSinglePos()
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

        var single = new GposSinglePosSubtableBuilder();
        single.AddOrReplace(glyphId: 10, value: new GposValueRecordBuilder { XAdvance = 111 });

        var ext = new GposExtensionPosSubtableBuilder();
        ext.SetSubtableData(extensionLookupType: 1, subtableData: single.ToMemory());

        var lookup = gposBuilder.Layout.Lookups.AddLookup(lookupType: 9, lookupFlag: 0);
        lookup.AddSubtable(ext.ToMemory());

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
        Assert.AreEqual((ushort)1, lookupTable.SubtableCount);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel));

        int extOffset = lookupTable.Offset + rel;
        Assert.IsTrue(GposExtensionPosSubtable.TryCreate(gpos.Table, extOffset, out var extTable));
        Assert.AreEqual((ushort)1, extTable.PosFormat);

        Assert.IsTrue(extTable.TryResolve(out ushort resolvedLookupType, out int subtableOffset));
        Assert.AreEqual((ushort)1, resolvedLookupType);

        Assert.IsTrue(GposSinglePosSubtable.TryCreate(gpos.Table, subtableOffset, out var singleTable));
        Assert.IsTrue(singleTable.TryGetValueRecordForGlyph(glyphId: 10, out bool positioned, out var value));
        Assert.IsTrue(positioned);
        Assert.IsTrue(value.TryGetXAdvance(out short xa));
        Assert.AreEqual((short)111, xa);
    }
}

