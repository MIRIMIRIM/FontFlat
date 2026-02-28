using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposMarkMarkPosStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithMarkMarkPosLookup()
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

        var mark1Anchor = AnchorTableBuilder.Format3(x: 100, y: 200, xDevice: device, yDevice: null);
        var mark2Anchor = AnchorTableBuilder.Format1(x: 10, y: 20);

        var markMark = new GposMarkMarkPosSubtableBuilder();
        markMark.AddOrReplaceMark1(markGlyphId: 10, @class: 0, markAnchor: mark1Anchor);
        markMark.AddOrReplaceMark2Anchor(mark2GlyphId: 20, @class: 0, mark2Anchor: mark2Anchor);

        var lookup = gposBuilder.Layout.Lookups.AddLookup(lookupType: 6, lookupFlag: 0);
        lookup.AddSubtable(markMark.ToMemory());

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
        Assert.AreEqual((ushort)6, lookupTable.LookupType);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel));

        int subtableOffset = lookupTable.Offset + rel;
        Assert.IsTrue(GposMarkMarkPosSubtable.TryCreate(gpos.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)1, subtable.PosFormat);
        Assert.AreEqual((ushort)1, subtable.ClassCount);

        Assert.IsTrue(subtable.TryGetAnchorsForGlyphs(mark1GlyphId: 10, mark2GlyphId: 20, out bool positioned, out var outMark1Anchor, out var outMark2Anchor));
        Assert.IsTrue(positioned);

        Assert.AreEqual((ushort)3, outMark1Anchor.AnchorFormat);
        Assert.AreEqual((short)100, outMark1Anchor.XCoordinate);

        Assert.IsTrue(outMark1Anchor.TryGetXDeviceTableAbsoluteOffset(out int deviceAbs));
        Assert.IsTrue(DeviceTable.TryCreate(gpos.Table, deviceAbs, out var deviceTable));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 9, out sbyte d9));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 10, out sbyte d10));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 11, out sbyte d11));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 12, out sbyte d12));
        CollectionAssert.AreEqual(new sbyte[] { -1, 0, 1, -2 }, new sbyte[] { d9, d10, d11, d12 });

        Assert.AreEqual((ushort)1, outMark2Anchor.AnchorFormat);
        Assert.AreEqual((short)10, outMark2Anchor.XCoordinate);
        Assert.AreEqual((short)20, outMark2Anchor.YCoordinate);
    }
}

