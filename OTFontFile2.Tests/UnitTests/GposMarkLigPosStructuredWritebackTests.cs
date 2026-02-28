using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposMarkLigPosStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithMarkLigPosLookup()
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

        var markAnchor = AnchorTableBuilder.Format3(x: 100, y: 200, xDevice: device, yDevice: null);
        var ligAnchor = AnchorTableBuilder.Format1(x: 10, y: 20);

        var markLig = new GposMarkLigPosSubtableBuilder();
        markLig.AddOrReplaceMark(markGlyphId: 10, @class: 0, markAnchor: markAnchor);
        markLig.AddOrReplaceLigatureAnchor(ligatureGlyphId: 30, componentIndex: 0, @class: 0, ligatureAnchor: ligAnchor);

        var lookup = gposBuilder.Layout.Lookups.AddLookup(lookupType: 5, lookupFlag: 0);
        lookup.AddSubtable(markLig.ToMemory());

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
        Assert.AreEqual((ushort)5, lookupTable.LookupType);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort rel));

        int subtableOffset = lookupTable.Offset + rel;
        Assert.IsTrue(GposMarkLigPosSubtable.TryCreate(gpos.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)1, subtable.PosFormat);
        Assert.AreEqual((ushort)1, subtable.ClassCount);

        Assert.IsTrue(subtable.TryGetAnchorsForGlyphs(markGlyphId: 10, ligatureGlyphId: 30, componentIndex: 0, out bool positioned, out var outMarkAnchor, out var outLigAnchor));
        Assert.IsTrue(positioned);

        Assert.AreEqual((ushort)3, outMarkAnchor.AnchorFormat);
        Assert.AreEqual((short)100, outMarkAnchor.XCoordinate);

        Assert.IsTrue(outMarkAnchor.TryGetXDeviceTableAbsoluteOffset(out int deviceAbs));
        Assert.IsTrue(DeviceTable.TryCreate(gpos.Table, deviceAbs, out var deviceTable));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 9, out sbyte d9));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 10, out sbyte d10));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 11, out sbyte d11));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 12, out sbyte d12));
        CollectionAssert.AreEqual(new sbyte[] { -1, 0, 1, -2 }, new sbyte[] { d9, d10, d11, d12 });

        Assert.AreEqual((ushort)1, outLigAnchor.AnchorFormat);
        Assert.AreEqual((short)10, outLigAnchor.XCoordinate);
        Assert.AreEqual((short)20, outLigAnchor.YCoordinate);
    }
}

