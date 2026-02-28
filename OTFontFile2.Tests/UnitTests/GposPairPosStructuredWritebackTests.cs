using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposPairPosStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithPairPosLookupAndDevice()
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

        var v1 = new GposValueRecordBuilder { XAdvance = -50, XAdvanceDevice = device };
        var v2 = new GposValueRecordBuilder { XPlacement = 30 };

        var pairPos = new GposPairPosSubtableBuilder();
        pairPos.AddOrReplace(firstGlyphId: 10, secondGlyphId: 20, value1: v1, value2: v2);
        pairPos.AddOrReplace(firstGlyphId: 10, secondGlyphId: 21, value1: new GposValueRecordBuilder { XAdvance = -10 }, value2: null);

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
        Assert.AreEqual((ushort)1, lookupTable.SubtableCount);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel));

        int subtableOffset = lookupTable.Offset + rel;
        Assert.IsTrue(GposPairPosSubtable.TryCreate(gpos.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)1, subtable.PosFormat);

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

        Assert.IsTrue(subtable.TryGetPairAdjustment(
            firstGlyphId: 10,
            secondGlyphId: 21,
            out bool positioned21,
            out var outValue1_21,
            out var outValue2_21));

        Assert.IsTrue(positioned21);
        Assert.IsTrue(outValue1_21.TryGetXAdvance(out short xa21));
        Assert.AreEqual((short)-10, xa21);
        Assert.IsFalse(outValue1_21.TryGetXAdvanceDeviceTableOffset(out _));
        Assert.IsTrue(outValue2_21.TryGetXPlacement(out short xp21)); // valueFormat2 present, but this record is zeroed
        Assert.AreEqual((short)0, xp21);
    }
}
