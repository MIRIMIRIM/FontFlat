using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class VarStoreLinkingTests
{
    [TestMethod]
    public void SyntheticGdefTable_ItemVarStoreOffset_ParsesItemVariationStore()
    {
        byte[] storeBytes = BuildItemVariationStore();

        // GDEF v1.3 header is 18 bytes.
        byte[] gdefBytes = new byte[18 + storeBytes.Length];
        var span = gdefBytes.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010003u); // version 1.3
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 0); // GlyphClassDefOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 0); // AttachListOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), 0); // LigCaretListOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), 0); // MarkAttachClassDefOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 0); // MarkGlyphSetsDefOffset
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(14, 4), 18u); // ItemVarStoreOffset

        storeBytes.CopyTo(span.Slice(18, storeBytes.Length));

        var builder = new SfntBuilder { SfntVersion = 0x00010000 };
        builder.SetTable(KnownTags.GDEF, gdefBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGdef(out var gdef));
        Assert.AreEqual(0x00010003u, gdef.Version.RawValue);
        Assert.IsTrue(gdef.ItemVarStoreOffset != 0);

        Assert.IsTrue(gdef.TryGetItemVariationStore(out var store));
        Assert.AreEqual((ushort)1, store.Format);
        Assert.AreEqual((ushort)1, store.ItemVariationDataCount);
    }

    [TestMethod]
    public void SyntheticCff2Table_VarStoreOffset_ParsesItemVariationStore()
    {
        byte[] storeBytes = BuildItemVariationStore();

        // Layout: header(5) + topDict(4) + GlobalSubrs INDEX(empty, 4) + VarStore(storeBytes)
        const int headerSize = 5;
        const int topDictLength = 4;
        const int globalSubrsLength = 4;
        int varStoreOffset = headerSize + topDictLength + globalSubrsLength;

        byte[] cff2Bytes = new byte[varStoreOffset + storeBytes.Length];
        var span = cff2Bytes.AsSpan();

        // Header
        span[0] = 2; // major
        span[1] = 0; // minor
        span[2] = headerSize;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(3, 2), topDictLength);

        // Top DICT: VarStore offset (operator 24)
        // 28 hi lo 24
        int td = headerSize;
        span[td + 0] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(td + 1, 2), checked((ushort)varStoreOffset));
        span[td + 3] = 24;

        // GlobalSubrs INDEX (empty): count(4)=0
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(headerSize + topDictLength, 4), 0u);

        // VarStore
        storeBytes.CopyTo(span.Slice(varStoreOffset, storeBytes.Length));

        var builder = new SfntBuilder { SfntVersion = 0x4F54544F }; // 'OTTO'
        builder.SetTable(KnownTags.CFF2, cff2Bytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCff2(out var cff2));
        Assert.IsTrue(cff2.TryGetVarStore(out var store));
        Assert.AreEqual((ushort)1, store.Format);
        Assert.AreEqual((ushort)1, store.ItemVariationDataCount);
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
}

