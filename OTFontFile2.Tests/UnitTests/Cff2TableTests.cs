using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class Cff2TableTests
{
    [TestMethod]
    public void SyntheticCff2Table_ParsesTopDictAndIndexes()
    {
        byte[] cff2Bytes = BuildCff2Table(
            out int expectedFdSelectOffset,
            out int expectedFdArrayOffset,
            out int expectedCharStringsOffset,
            out int expectedVarStoreOffset,
            out int expectedPrivateOffset,
            out int expectedPrivateSize);

        var builder = new SfntBuilder { SfntVersion = 0x4F54544F }; // 'OTTO'
        builder.SetTable(KnownTags.CFF2, cff2Bytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCff2(out var cff2));
        Assert.AreEqual((byte)2, cff2.Major);
        Assert.AreEqual((byte)0, cff2.Minor);
        Assert.AreEqual((byte)5, cff2.HeaderSize);
        Assert.AreEqual((ushort)22, cff2.TopDictLength);

        Assert.IsTrue(cff2.TryGetTopDict(out var topDict));
        Assert.AreEqual(expectedCharStringsOffset, topDict.CharStringsOffset);
        Assert.AreEqual(expectedFdArrayOffset, topDict.FdArrayOffset);
        Assert.AreEqual(expectedFdSelectOffset, topDict.FdSelectOffset);
        Assert.IsTrue(topDict.HasVarStore);
        Assert.AreEqual(expectedVarStoreOffset, topDict.VarStoreOffset);
        Assert.IsTrue(topDict.HasMaxStack);
        Assert.AreEqual(513, topDict.MaxStack);

        Assert.IsTrue(cff2.TryGetGlobalSubrIndex(out var globalSubrs));
        Assert.AreEqual(0u, globalSubrs.Count);

        Assert.IsTrue(cff2.TryGetCharStringsIndex(out var charStrings));
        Assert.AreEqual(1u, charStrings.Count);
        Assert.IsTrue(charStrings.TryGetObjectSpan(0, out var charString0));
        Assert.AreEqual(1, charString0.Length);
        Assert.AreEqual((byte)0x0E, charString0[0]);

        Assert.IsTrue(cff2.TryGetFdSelect(out var fdSelect));
        Assert.IsTrue(fdSelect.TryGetFontDictIndex(0, out ushort fdIndex));
        Assert.AreEqual((ushort)0, fdIndex);

        Assert.IsTrue(cff2.TryGetFontDict(0, out var fontDict));
        Assert.AreEqual(expectedPrivateOffset, fontDict.PrivateOffset);
        Assert.AreEqual(expectedPrivateSize, fontDict.PrivateSize);

        Assert.IsTrue(fontDict.TryGetPrivateDictCff2(out var privateDict));
        Assert.AreEqual(4, privateDict.SubrsOffset);
        Assert.IsTrue(privateDict.TryGetSubrsIndex(out var subrs));
        Assert.IsTrue(subrs.IsEmpty);
    }

    private static byte[] BuildCff2Table(
        out int fdSelectOffset,
        out int fdArrayOffset,
        out int charStringsOffset,
        out int varStoreOffset,
        out int privateOffset,
        out int privateSize)
    {
        // Layout:
        // header(5) + TopDict(22) + GlobalSubrs INDEX(empty, 4)
        // + FDSelect(format0, 2) + FDArray INDEX(14) + CharStrings INDEX(8)
        // + Private DICT(4) + Subrs INDEX(empty, 4)
        //
        // Offsets are absolute from the start of the CFF2 table.

        const int headerSize = 5;
        const int topDictLength = 22;

        int globalSubrsOffset = headerSize + topDictLength;
        const int globalSubrsLength = 4; // count(4) == 0

        fdSelectOffset = globalSubrsOffset + globalSubrsLength;
        const int fdSelectLength = 2; // format(1) + fdIndex[1]

        fdArrayOffset = fdSelectOffset + fdSelectLength;
        const int fdArrayLength = 14;

        charStringsOffset = fdArrayOffset + fdArrayLength;
        const int charStringsLength = 8;

        privateOffset = charStringsOffset + charStringsLength;
        privateSize = 4;

        int subrsOffset = privateOffset + privateSize;
        const int subrsLength = 4;

        varStoreOffset = privateOffset;

        int totalLength = subrsOffset + subrsLength;
        byte[] table = new byte[totalLength];
        var span = table.AsSpan();

        // Header
        span[0] = 2; // major
        span[1] = 0; // minor
        span[2] = headerSize;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(3, 2), topDictLength);

        // Top DICT (starts at offset 5)
        int td = headerSize;

        // maxstack 513: 28 0x02 0x01 25
        span[td + 0] = 28;
        span[td + 1] = 0x02;
        span[td + 2] = 0x01;
        span[td + 3] = 25;

        // FDSelect offset: 28 hi lo 12 37
        span[td + 4] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(td + 5, 2), checked((ushort)fdSelectOffset));
        span[td + 7] = 12;
        span[td + 8] = 37;

        // FDArray offset: 28 hi lo 12 36
        span[td + 9] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(td + 10, 2), checked((ushort)fdArrayOffset));
        span[td + 12] = 12;
        span[td + 13] = 36;

        // CharStrings offset: 28 hi lo 17
        span[td + 14] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(td + 15, 2), checked((ushort)charStringsOffset));
        span[td + 17] = 17;

        // VarStore offset: 28 hi lo 24
        span[td + 18] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(td + 19, 2), checked((ushort)varStoreOffset));
        span[td + 21] = 24;

        // GlobalSubrs INDEX (empty): count(4)=0
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(globalSubrsOffset, 4), 0u);

        // FDSelect (format 0, glyphCount 1): [format=0][fdIndex=0]
        span[fdSelectOffset + 0] = 0;
        span[fdSelectOffset + 1] = 0;

        // FDArray INDEX (count 1, offSize 1, offsets [1,8], data len 7)
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(fdArrayOffset, 4), 1u);
        span[fdArrayOffset + 4] = 1; // offSize
        span[fdArrayOffset + 5] = 1; // first offset
        span[fdArrayOffset + 6] = 8; // last offset (1 + 7 bytes)
        int fontDictOffset = fdArrayOffset + 7;

        // Font DICT: Private(size, offset) operator (18)
        span[fontDictOffset + 0] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(fontDictOffset + 1, 2), checked((ushort)privateSize));
        span[fontDictOffset + 3] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(fontDictOffset + 4, 2), checked((ushort)privateOffset));
        span[fontDictOffset + 6] = 18;

        // CharStrings INDEX (count 1, offSize 1, offsets [1,2], data [0x0E])
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(charStringsOffset, 4), 1u);
        span[charStringsOffset + 4] = 1;
        span[charStringsOffset + 5] = 1;
        span[charStringsOffset + 6] = 2;
        span[charStringsOffset + 7] = 0x0E;

        // Private DICT: Subrs offset (4) operator (19)
        span[privateOffset + 0] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(privateOffset + 1, 2), 4);
        span[privateOffset + 3] = 19;

        // Local Subrs INDEX (empty)
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(subrsOffset, 4), 0u);

        return table;
    }
}

