using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class VariationStoreTablesTests
{
    [TestMethod]
    public void SyntheticHvarMvarTables_ParseVariationStoreAndMappings()
    {
        byte[] hvarBytes = BuildHvarTable();
        byte[] mvarBytes = BuildMvarTable();

        var builder = new SfntBuilder { SfntVersion = 0x00010000 };
        builder.SetTable(KnownTags.HVAR, hvarBytes);
        builder.SetTable(KnownTags.MVAR, mvarBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetHvar(out var hvar));
        Assert.AreEqual((ushort)1, hvar.MajorVersion);
        Assert.AreEqual((ushort)0, hvar.MinorVersion);

        Assert.IsTrue(hvar.TryGetItemVariationStore(out var store));
        Assert.AreEqual((ushort)1, store.Format);
        Assert.AreEqual((ushort)1, store.ItemVariationDataCount);

        Assert.IsTrue(store.TryGetVariationRegionList(out var regionList));
        Assert.AreEqual((ushort)1, regionList.AxisCount);
        Assert.AreEqual((ushort)1, regionList.RegionCount);
        Assert.IsTrue(regionList.TryGetRegion(0, out var region0));
        Assert.IsTrue(region0.TryGetAxisCoordinates(0, out var coords));
        Assert.AreEqual(unchecked((short)0xC000), coords.StartCoord.RawValue);
        Assert.AreEqual((short)0, coords.PeakCoord.RawValue);
        Assert.AreEqual(0x4000, coords.EndCoord.RawValue);

        Assert.IsTrue(store.TryGetItemVariationData(0, out var ivd));
        Assert.AreEqual((ushort)2, ivd.ItemCount);
        Assert.AreEqual((ushort)1, ivd.ShortDeltaCount);
        Assert.AreEqual((ushort)1, ivd.RegionIndexCount);
        Assert.IsTrue(ivd.TryGetRegionIndex(0, out ushort regionIndex));
        Assert.AreEqual((ushort)0, regionIndex);
        Assert.IsTrue(ivd.TryGetDelta(0, 0, out int delta0));
        Assert.IsTrue(ivd.TryGetDelta(1, 0, out int delta1));
        Assert.AreEqual(10, delta0);
        Assert.AreEqual(-5, delta1);

        Assert.IsTrue(hvar.TryGetAdvanceWidthMapping(out var map));
        Assert.AreEqual((ushort)3, map.MapCount);
        Assert.AreEqual(2, map.EntrySize);
        Assert.AreEqual(8, map.InnerIndexBitCount);
        Assert.IsTrue(map.TryGetVarIdx(1, out var varIdx1));
        Assert.AreEqual((ushort)0, varIdx1.OuterIndex);
        Assert.AreEqual((ushort)1, varIdx1.InnerIndex);

        Assert.IsTrue(font.TryGetMvar(out var mvar));
        Assert.AreEqual((ushort)1, mvar.MajorVersion);
        Assert.AreEqual((ushort)0, mvar.MinorVersion);
        Assert.AreEqual((ushort)1, mvar.ValueRecordCount);

        Assert.IsTrue(mvar.TryGetValueRecord(0, out var valueRecord));
        Assert.AreEqual("TEST", valueRecord.ValueTag.ToString());
        Assert.AreEqual((ushort)0, valueRecord.DeltaSetIndex.OuterIndex);
        Assert.AreEqual((ushort)1, valueRecord.DeltaSetIndex.InnerIndex);

        Assert.IsTrue(mvar.TryGetItemVariationStore(out var mvarStore));
        Assert.IsTrue(mvarStore.TryGetItemVariationData(0, out var mvarIvd));
        Assert.IsTrue(mvarIvd.TryGetDelta(1, 0, out int mvarDelta1));
        Assert.AreEqual(-5, mvarDelta1);
    }

    private static byte[] BuildHvarTable()
    {
        byte[] store = BuildItemVariationStore();
        byte[] map = BuildDeltaSetIndexMap();

        const int headerSize = 20;
        int storeOffset = headerSize;
        int mapOffset = storeOffset + store.Length;

        byte[] table = new byte[mapOffset + map.Length];
        var span = table.AsSpan();

        // Header
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1); // major
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 0); // minor
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), (uint)storeOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), (uint)mapOffset); // advanceWidthMappingOffset
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(12, 4), 0u); // lsbMappingOffset
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(16, 4), 0u); // rsbMappingOffset

        store.CopyTo(span.Slice(storeOffset, store.Length));
        map.CopyTo(span.Slice(mapOffset, map.Length));
        return table;
    }

    private static byte[] BuildMvarTable()
    {
        byte[] store = BuildItemVariationStore();

        const int headerSize = 12;
        const int valueRecordSize = 8;
        const int valueRecordCount = 1;
        const int storeOffset = headerSize + (valueRecordSize * valueRecordCount);

        byte[] table = new byte[storeOffset + store.Length];
        var span = table.AsSpan();

        // Header
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1); // major
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 0); // minor
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), storeOffset);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), valueRecordSize);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), valueRecordCount);

        // ValueRecord[0]: tag + outer + inner
        WriteTag(span.Slice(12, 4), "TEST");
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(16, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(18, 2), 1);

        store.CopyTo(span.Slice(storeOffset, store.Length));
        return table;
    }

    private static byte[] BuildItemVariationStore()
    {
        // Store layout:
        // header(12) + regionList(10) + itemVariationData(12) = 34
        byte[] store = new byte[34];
        var span = store.AsSpan();

        // ItemVariationStore header
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1); // format
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(2, 4), 12u); // variationRegionListOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 1); // itemVariationDataCount
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), 22u); // itemVariationDataOffsets[0]

        // VariationRegionList (offset 12)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 1); // axisCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), 1); // regionCount
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(16, 2), unchecked((short)0xC000)); // start -1.0
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(18, 2), 0); // peak 0.0
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(20, 2), 0x4000); // end 1.0

        // ItemVariationData (offset 22)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(22, 2), 2); // itemCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(24, 2), 1); // shortDeltaCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), 1); // regionIndexCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(28, 2), 0); // regionIndices[0]
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(30, 2), 10); // delta item0
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(32, 2), -5); // delta item1

        return store;
    }

    private static byte[] BuildDeltaSetIndexMap()
    {
        // format(1) + entryFormat(1) + mapCount(2) + mapData(3 entries, 2 bytes each) = 10
        byte[] map = new byte[10];
        var span = map.AsSpan();

        span[0] = 0; // format 0
        span[1] = 0x17; // entrySize=2 (hi nibble=1), innerIndexBitCount=8 (lo nibble=7)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 3); // mapCount

        // VarIdx packed as (outer << innerBits) | inner, big-endian.
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 0x0000); // outer=0, inner=0
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 0x0001); // outer=0, inner=1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), 0x0001); // outer=0, inner=1

        return map;
    }

    private static void WriteTag(Span<byte> dst, string tag)
    {
        dst[0] = (byte)tag[0];
        dst[1] = (byte)tag[1];
        dst[2] = (byte)tag[2];
        dst[3] = (byte)tag[3];
    }
}

