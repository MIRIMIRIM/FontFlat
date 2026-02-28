using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposSinglePosStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithSinglePosLookupAndDevice()
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

        var v0 = new GposValueRecordBuilder { XAdvance = 120, XAdvanceDevice = device };
        var v1 = new GposValueRecordBuilder { XAdvance = 200 };

        var singlePos = new GposSinglePosSubtableBuilder();
        singlePos.AddOrReplace(glyphId: 10, value: v0);
        singlePos.AddOrReplace(glyphId: 11, value: v1);

        var lookup = gposBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        lookup.AddSubtable(singlePos.ToMemory());

        var feature = gposBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(lookup);

        var script = gposBuilder.Layout.Scripts.GetOrAddScript(dflt);
        script.GetOrCreateDefaultLangSys().AddFeature(feature);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGpos(out var gpos));
        Assert.AreEqual(0x00010000u, gpos.Version.RawValue);

        Assert.IsTrue(gpos.TryGetLookupList(out var lookupList));
        Assert.AreEqual((ushort)1, lookupList.LookupCount);
        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookupTable));
        Assert.AreEqual((ushort)1, lookupTable.LookupType);
        Assert.AreEqual((ushort)1, lookupTable.SubtableCount);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort subtableRel));

        int subtableOffset = lookupTable.Offset + subtableRel;
        Assert.IsTrue(GposSinglePosSubtable.TryCreate(gpos.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)2, subtable.PosFormat);

        Assert.IsTrue(subtable.TryGetValueRecordForGlyph(glyphId: 10, out bool positioned10, out var vr10));
        Assert.IsTrue(positioned10);
        Assert.IsTrue(vr10.TryGetXAdvance(out short xa10));
        Assert.AreEqual((short)120, xa10);

        Assert.IsTrue(vr10.TryGetXAdvanceDeviceTableOffset(out int devAbs10));
        Assert.IsTrue(DeviceTable.TryCreate(gpos.Table, devAbs10, out var dev10));
        Assert.AreEqual((ushort)1, dev10.DeltaFormat);
        Assert.IsTrue(dev10.TryGetDelta(ppemSize: 9, out sbyte d9));
        Assert.IsTrue(dev10.TryGetDelta(ppemSize: 10, out sbyte d10));
        Assert.IsTrue(dev10.TryGetDelta(ppemSize: 11, out sbyte d11));
        Assert.IsTrue(dev10.TryGetDelta(ppemSize: 12, out sbyte d12));
        CollectionAssert.AreEqual(new sbyte[] { -1, 0, 1, -2 }, new sbyte[] { d9, d10, d11, d12 });

        Assert.IsTrue(subtable.TryGetValueRecordForGlyph(glyphId: 11, out bool positioned11, out var vr11));
        Assert.IsTrue(positioned11);
        Assert.IsTrue(vr11.TryGetXAdvance(out short xa11));
        Assert.AreEqual((short)200, xa11);
        Assert.IsFalse(vr11.TryGetXAdvanceDeviceTableOffset(out _));
    }
}

