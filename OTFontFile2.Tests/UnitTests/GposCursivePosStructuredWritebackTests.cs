using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposCursivePosStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithCursivePosLookupAndAnchorDevices()
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

        var entry10 = AnchorTableBuilder.Format3(x: 100, y: 0, xDevice: device, yDevice: null);
        var exit10 = AnchorTableBuilder.Format1(x: 200, y: 0);

        var exit11 = AnchorTableBuilder.Format1(x: 300, y: 0);

        var cursive = new GposCursivePosSubtableBuilder();
        cursive.AddOrReplace(glyphId: 10, entryAnchor: entry10, exitAnchor: exit10);
        cursive.AddOrReplace(glyphId: 11, entryAnchor: null, exitAnchor: exit11);

        var lookup = gposBuilder.Layout.Lookups.AddLookup(lookupType: 3, lookupFlag: 0);
        lookup.AddSubtable(cursive.ToMemory());

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
        Assert.AreEqual((ushort)3, lookupTable.LookupType);

        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel));
        int subtableOffset = lookupTable.Offset + rel;

        Assert.IsTrue(GposCursivePosSubtable.TryCreate(gpos.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)1, subtable.PosFormat);

        Assert.IsTrue(subtable.TryGetEntryExitRecordForGlyph(glyphId: 10, out bool covered10, out _, out var rec10));
        Assert.IsTrue(covered10);

        Assert.IsTrue(rec10.TryGetEntryAnchorTable(out bool hasEntry10, out var entryAnchor10));
        Assert.IsTrue(hasEntry10);
        Assert.AreEqual((ushort)3, entryAnchor10.AnchorFormat);
        Assert.AreEqual((short)100, entryAnchor10.XCoordinate);

        Assert.IsTrue(entryAnchor10.TryGetXDeviceTableAbsoluteOffset(out int deviceAbs));
        Assert.IsTrue(DeviceTable.TryCreate(gpos.Table, deviceAbs, out var deviceTable));
        Assert.AreEqual((ushort)1, deviceTable.DeltaFormat);
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 9, out sbyte d9));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 10, out sbyte d10));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 11, out sbyte d11));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 12, out sbyte d12));
        CollectionAssert.AreEqual(new sbyte[] { -1, 0, 1, -2 }, new sbyte[] { d9, d10, d11, d12 });

        Assert.IsTrue(rec10.TryGetExitAnchorTable(out bool hasExit10, out var exitAnchorTable10));
        Assert.IsTrue(hasExit10);
        Assert.AreEqual((ushort)1, exitAnchorTable10.AnchorFormat);
        Assert.AreEqual((short)200, exitAnchorTable10.XCoordinate);

        Assert.IsTrue(subtable.TryGetEntryExitRecordForGlyph(glyphId: 11, out bool covered11, out _, out var rec11));
        Assert.IsTrue(covered11);

        Assert.IsTrue(rec11.TryGetEntryAnchorTable(out bool hasEntry11, out _));
        Assert.IsFalse(hasEntry11);

        Assert.IsTrue(rec11.TryGetExitAnchorTable(out bool hasExit11, out var exitAnchorTable11));
        Assert.IsTrue(hasExit11);
        Assert.AreEqual((short)300, exitAnchorTable11.XCoordinate);
    }
}

