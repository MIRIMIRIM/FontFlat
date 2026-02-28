using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GdefV13WritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteGdefV13_WithItemVariationStore()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GdefTableBuilder>(out var gdefBuilder));

        byte[] storeBytes = BuildMinimalItemVariationStore();
        gdefBuilder.Clear();
        gdefBuilder.SetItemVariationStoreData(storeBytes);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGdef(out var gdef));
        Assert.AreEqual(0x00010003u, gdef.Version.RawValue);
        Assert.IsTrue(gdef.ItemVarStoreOffset > 0);

        Assert.IsTrue(gdef.TryGetItemVariationStore(out var store));
        Assert.AreEqual((ushort)1, store.Format);
        Assert.AreEqual((ushort)1, store.ItemVariationDataCount);

        Assert.IsTrue(store.TryGetVariationRegionList(out var regions));
        Assert.AreEqual((ushort)1, regions.AxisCount);
        Assert.AreEqual((ushort)1, regions.RegionCount);

        Assert.IsTrue(store.TryGetItemVariationData(0, out var data0));
        Assert.AreEqual((ushort)1, data0.ItemCount);
        Assert.AreEqual((ushort)0, data0.ShortDeltaCount);
        Assert.AreEqual((ushort)1, data0.RegionIndexCount);
        Assert.IsTrue(data0.TryGetRegionIndex(0, out ushort regionIndex));
        Assert.AreEqual((ushort)0, regionIndex);
        Assert.IsTrue(data0.TryGetDelta(itemIndex: 0, regionDeltaIndex: 0, out int delta));
        Assert.AreEqual(0, delta);
    }

    [TestMethod]
    public void FontModel_CanWriteGdefLigCaretList_WithCaretValueFormat3VariationIndex()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GdefTableBuilder>(out var gdefBuilder));

        var device = new DeviceTableBuilder();
        device.SetVariationIndex(outerIndex: 7, innerIndex: 9);

        var lig = new GdefLigCaretListBuilder();
        lig.AddOrReplace(
            ligGlyphId: 10,
            carets: new[]
            {
                GdefLigCaretListBuilder.CaretValue.DeviceValue(coordinate: 123, device: device),
            });

        gdefBuilder.Clear();
        gdefBuilder.SetLigCaretList(lig);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGdef(out var gdef));
        Assert.IsTrue(gdef.TryGetLigCaretList(out var ligCaretList));
        Assert.IsTrue(ligCaretList.TryGetLigGlyphTableForGlyph(glyphId: 10, out bool covered10, out var ligGlyph));
        Assert.IsTrue(covered10);
        Assert.AreEqual((ushort)1, ligGlyph.CaretCount);

        Assert.IsTrue(ligGlyph.TryGetCaretValueTable(0, out var caret));
        Assert.AreEqual((ushort)3, caret.CaretValueFormat);
        Assert.IsTrue(caret.TryGetCoordinate(out short coord));
        Assert.AreEqual((short)123, coord);

        Assert.IsTrue(caret.TryGetDeviceTableAbsoluteOffset(out int deviceOffset));
        Assert.IsTrue(DeviceTable.TryCreate(gdef.Table, deviceOffset, out var deviceTable));
        Assert.AreEqual((ushort)0x8000, deviceTable.DeltaFormat);
        Assert.IsTrue(deviceTable.IsVariationIndex);
        Assert.IsTrue(deviceTable.TryGetVarIdx(out var varIdx));
        Assert.AreEqual((ushort)7, varIdx.OuterIndex);
        Assert.AreEqual((ushort)9, varIdx.InnerIndex);
    }

    private static byte[] BuildMinimalItemVariationStore()
    {
        // ItemVariationStore:
        // format(2)=1
        // variationRegionListOffset(4)=12
        // itemVariationDataCount(2)=1
        // itemVariationDataOffsets[0](4)=22
        //
        // VariationRegionList @ 12:
        // axisCount(2)=1, regionCount(2)=1, region[0].axis[0] coords: (0,0,0)
        //
        // ItemVariationData @ 22:
        // itemCount(2)=1, shortDeltaCount(2)=0, regionIndexCount(2)=1
        // regionIndexes[0](2)=0
        // deltaSet[0]: 1 byte delta = 0

        byte[] bytes = new byte[31];
        var span = bytes.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(2, 4), 12u);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), 22u);

        // Region list @ 12
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), 1);
        // coords @ 16: start/peak/end all 0
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(16, 2), 0);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(18, 2), 0);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(20, 2), 0);

        // ItemVariationData @ 22
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(22, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(24, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(28, 2), 0);
        span[30] = 0;

        return bytes;
    }
}

