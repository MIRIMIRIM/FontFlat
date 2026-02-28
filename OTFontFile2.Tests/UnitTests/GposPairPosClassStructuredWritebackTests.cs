using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposPairPosClassStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithPairPosFormat2AndDevice()
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

        var device = new DeviceTableBuilder();
        device.SetDeltas(startSize: 9, endSize: 12, deltas: new sbyte[] { -1, 0, 1, -2 });

        var classDef1 = new ClassDefTableBuilder();
        classDef1.SetClass(glyphId: 10, classValue: 1);

        var classDef2 = new ClassDefTableBuilder();
        classDef2.SetClass(glyphId: 20, classValue: 1);

        var v1 = new GposValueRecordBuilder { XAdvance = -50, XAdvanceDevice = device };
        var v2 = new GposValueRecordBuilder { XPlacement = 30 };

        var pairPos = new GposPairPosClassSubtableBuilder();
        pairPos.Coverage.AddGlyph(10);
        pairPos.SetClassDef1(classDef1);
        pairPos.SetClassDef2(classDef2);
        pairPos.SetClassCounts(class1Count: 2, class2Count: 2);
        pairPos.SetPairValue(class1: 1, class2: 1, value1: v1, value2: v2);

        var lookup = gposBuilder.Layout.Lookups.AddLookup(lookupType: 2, lookupFlag: 0);
        lookup.AddSubtable(pairPos.ToMemory());

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
        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookupTable));
        Assert.AreEqual((ushort)2, lookupTable.LookupType);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel));

        int subtableOffset = lookupTable.Offset + rel;
        Assert.IsTrue(GposPairPosSubtable.TryCreate(gpos.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)2, subtable.PosFormat);

        Assert.IsTrue(subtable.TryGetPairAdjustment(
            firstGlyphId: 10,
            secondGlyphId: 20,
            out bool positioned,
            out var outValue1,
            out var outValue2));

        Assert.IsTrue(positioned);

        Assert.IsTrue(outValue1.TryGetXAdvance(out short xa));
        Assert.AreEqual((short)-50, xa);
        Assert.IsTrue(outValue2.TryGetXPlacement(out short xp));
        Assert.AreEqual((short)30, xp);

        Assert.IsTrue(outValue1.TryGetXAdvanceDeviceTableOffset(out int deviceAbs));
        Assert.IsTrue(DeviceTable.TryCreate(gpos.Table, deviceAbs, out var deviceTable));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 9, out sbyte d9));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 10, out sbyte d10));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 11, out sbyte d11));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 12, out sbyte d12));
        CollectionAssert.AreEqual(new sbyte[] { -1, 0, 1, -2 }, new sbyte[] { d9, d10, d11, d12 });
    }
}
