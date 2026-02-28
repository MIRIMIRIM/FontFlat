using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class SbitTablesTests
{
    [TestMethod]
    public void SyntheticEblcEbdt_IndexFormat1_ParsesGlyphAndMatchesLegacy()
    {
        byte[] eblcBytes = BuildEblc_IndexFormat1(
            firstGlyphId: 0,
            lastGlyphId: 0,
            imageFormat: 1,
            imageDataOffset: 4,
            glyphDataLength: 6);

        byte[] ebdtBytes = BuildEbdt_WithSingleGlyphImage(
            versionRaw: 0x00020000u,
            imageDataOffset: 4,
            glyphImage: new byte[]
            {
                1,    // height
                2,    // width
                unchecked((byte)(sbyte)-3), // bearingX
                unchecked((byte)(sbyte)-4), // bearingY
                5,    // advance
                0xAA, // bitmap payload (1 byte)
            });

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.EBLC, eblcBytes);
        builder.SetTable(KnownTags.EBDT, ebdtBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetEblc(out var eblc));
        Assert.IsTrue(font.TryGetEbdt(out var ebdt));

        Assert.IsTrue(eblc.TryGetBitmapSizeTable(0, out var strike));
        Assert.IsTrue(strike.TryGetGlyphImageBounds(0, out ushort imageFormat, out int imageOffset, out int imageLength));
        Assert.AreEqual((ushort)1, imageFormat);
        Assert.AreEqual(4, imageOffset);
        Assert.AreEqual(6, imageLength);

        Assert.IsTrue(ebdt.TryGetGlyphSpan(imageOffset, imageLength, out var glyphData));
        Assert.IsTrue(EbdtTable.TryGetSmallMetricsAndBitmap(glyphData, out var metrics, out var bitmap));

        Assert.AreEqual((byte)1, metrics.Height);
        Assert.AreEqual((byte)2, metrics.Width);
        Assert.AreEqual(unchecked((sbyte)-3), metrics.BearingX);
        Assert.AreEqual(unchecked((sbyte)-4), metrics.BearingY);
        Assert.AreEqual((byte)5, metrics.Advance);
        Assert.AreEqual(1, bitmap.Length);
        Assert.AreEqual((byte)0xAA, bitmap[0]);

        string tempPath = Path.Combine(Path.GetTempPath(), $"synthetic-sbit-{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(tempPath, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tempPath));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyEblc = (Legacy.Table_EBLC)legacyFont.GetTable("EBLC")!;
            var legacyEbdt = (Legacy.Table_EBDT)legacyFont.GetTable("EBDT")!;

            var legacyStrike = legacyEblc.GetBitmapSizeTable(0)!;
            var legacyIsta = legacyStrike.FindIndexSubTableArray(0)!;
            var legacyIst = legacyStrike.GetIndexSubTable(legacyIsta)!;

            var legacyMetrics = legacyEbdt.GetSmallMetrics(legacyIst, 0, legacyIsta.firstGlyphIndex)!;
            Assert.AreEqual(metrics.Height, legacyMetrics.height);
            Assert.AreEqual(metrics.Width, legacyMetrics.width);
            Assert.AreEqual(metrics.BearingX, legacyMetrics.BearingX);
            Assert.AreEqual(metrics.BearingY, legacyMetrics.BearingY);
            Assert.AreEqual(metrics.Advance, legacyMetrics.Advance);

            byte[] legacyBitmap = legacyEbdt.GetImageData(legacyIst, 0, legacyIsta.firstGlyphIndex)!;
            Assert.AreEqual(bitmap.Length, legacyBitmap.Length);
            for (int i = 0; i < legacyBitmap.Length; i++)
                Assert.AreEqual(bitmap[i], legacyBitmap[i]);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void SyntheticEblc_IndexFormats_ComputeGlyphBounds()
    {
        // Format 2 (fixed size, contiguous range)
        {
            byte[] eblc = BuildEblc_IndexFormat2(
                firstGlyphId: 0,
                lastGlyphId: 1,
                imageFormat: 5,
                imageDataOffset: 4,
                imageSize: 3);

            byte[] ebdt = BuildEbdt_Raw(versionRaw: 0x00020000u, payload: new byte[6]);

            AssertSbitBounds(eblc, ebdt, glyphId: 0, expectedOffset: 4, expectedLength: 3);
            AssertSbitBounds(eblc, ebdt, glyphId: 1, expectedOffset: 7, expectedLength: 3);
        }

        // Format 3 (ushort offsets array)
        {
            byte[] eblc = BuildEblc_IndexFormat3(
                firstGlyphId: 0,
                lastGlyphId: 0,
                imageFormat: 1,
                imageDataOffset: 4,
                glyphDataLength: 6);

            byte[] ebdt = BuildEbdt_Raw(versionRaw: 0x00020000u, payload: new byte[6]);

            AssertSbitBounds(eblc, ebdt, glyphId: 0, expectedOffset: 4, expectedLength: 6);
        }

        // Format 4 (codeOffsetPairs)
        {
            byte[] eblc = BuildEblc_IndexFormat4(
                glyphId: 42,
                imageFormat: 1,
                imageDataOffset: 4,
                glyphDataLength: 6);

            byte[] ebdt = BuildEbdt_Raw(versionRaw: 0x00020000u, payload: new byte[6]);

            AssertSbitBounds(eblc, ebdt, glyphId: 42, expectedOffset: 4, expectedLength: 6);
        }

        // Format 5 (glyphCodeArray + fixed size)
        {
            byte[] eblc = BuildEblc_IndexFormat5(
                glyphId: 99,
                imageFormat: 5,
                imageDataOffset: 4,
                imageSize: 2);

            byte[] ebdt = BuildEbdt_Raw(versionRaw: 0x00020000u, payload: new byte[2]);

            AssertSbitBounds(eblc, ebdt, glyphId: 99, expectedOffset: 4, expectedLength: 2);
        }
    }

    private static void AssertSbitBounds(byte[] eblcBytes, byte[] ebdtBytes, ushort glyphId, int expectedOffset, int expectedLength)
    {
        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.EBLC, eblcBytes);
        builder.SetTable(KnownTags.EBDT, ebdtBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetEblc(out var eblc));
        Assert.IsTrue(font.TryGetEbdt(out var ebdt));
        Assert.IsTrue(eblc.TryGetBitmapSizeTable(0, out var strike));
        Assert.IsTrue(strike.TryGetGlyphImageBounds(glyphId, out _, out int offset, out int length));
        Assert.AreEqual(expectedOffset, offset);
        Assert.AreEqual(expectedLength, length);

        Assert.IsTrue(ebdt.TryGetGlyphSpan(offset, length, out var glyphData));
        Assert.AreEqual(expectedLength, glyphData.Length);
    }

    private static byte[] BuildEbdt_Raw(uint versionRaw, byte[] payload)
    {
        byte[] table = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(0, 4), versionRaw);
        payload.CopyTo(table.AsSpan(4));
        return table;
    }

    private static byte[] BuildEbdt_WithSingleGlyphImage(uint versionRaw, int imageDataOffset, byte[] glyphImage)
    {
        if (imageDataOffset < 4)
            throw new ArgumentOutOfRangeException(nameof(imageDataOffset));

        byte[] table = new byte[imageDataOffset + glyphImage.Length];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), versionRaw);
        glyphImage.CopyTo(span.Slice(imageDataOffset));
        return table;
    }

    private static byte[] BuildEblc_IndexFormat1(ushort firstGlyphId, ushort lastGlyphId, ushort imageFormat, uint imageDataOffset, uint glyphDataLength)
    {
        if (lastGlyphId < firstGlyphId)
            throw new ArgumentOutOfRangeException(nameof(lastGlyphId));

        const uint version = 0x00020000u;
        const uint numSizes = 1;

        const int headerLen = 8;
        const int bstLen = 48;
        const int arrayLen = 8;
        const int subTableOffset = headerLen + bstLen + arrayLen;

        int glyphCount = lastGlyphId - firstGlyphId + 1;
        int offsetsLen = (glyphCount + 1) * 4;
        int subTableLen = 8 + offsetsLen;

        int totalLen = subTableOffset + subTableLen;
        byte[] table = new byte[totalLen];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), version);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), numSizes);

        int bst = headerLen;
        int arrayOffset = headerLen + bstLen;
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 0, 4), (uint)arrayOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 4, 4), (uint)(arrayLen + subTableLen));
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 8, 4), 1u);

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(bst + 40, 2), firstGlyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(bst + 42, 2), lastGlyphId);

        // indexSubTableArray
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(arrayOffset + 0, 2), firstGlyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(arrayOffset + 2, 2), lastGlyphId);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(arrayOffset + 4, 4), arrayLen);

        // indexSubTable (format 1)
        int st = subTableOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 0, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 2, 2), imageFormat);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 4, 4), imageDataOffset);

        // offsets array: [0, glyphDataLength]
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 8, 4), 0u);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 12, 4), glyphDataLength);

        return table;
    }

    private static byte[] BuildEblc_IndexFormat2(ushort firstGlyphId, ushort lastGlyphId, ushort imageFormat, uint imageDataOffset, uint imageSize)
    {
        if (lastGlyphId < firstGlyphId)
            throw new ArgumentOutOfRangeException(nameof(lastGlyphId));

        const uint version = 0x00020000u;
        const uint numSizes = 1;

        const int headerLen = 8;
        const int bstLen = 48;
        const int arrayLen = 8;
        const int subTableOffset = headerLen + bstLen + arrayLen;

        const int subTableLen = 8 + 4 + 8;

        int totalLen = subTableOffset + subTableLen;
        byte[] table = new byte[totalLen];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), version);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), numSizes);

        int bst = headerLen;
        int arrayOffset = headerLen + bstLen;
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 0, 4), (uint)arrayOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 4, 4), (uint)(arrayLen + subTableLen));
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 8, 4), 1u);

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(bst + 40, 2), firstGlyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(bst + 42, 2), lastGlyphId);

        // indexSubTableArray
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(arrayOffset + 0, 2), firstGlyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(arrayOffset + 2, 2), lastGlyphId);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(arrayOffset + 4, 4), arrayLen);

        // indexSubTable (format 2)
        int st = subTableOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 0, 2), 2);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 2, 2), imageFormat);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 4, 4), imageDataOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 8, 4), imageSize);
        // bigGlyphMetrics left as zeros

        return table;
    }

    private static byte[] BuildEblc_IndexFormat3(ushort firstGlyphId, ushort lastGlyphId, ushort imageFormat, uint imageDataOffset, ushort glyphDataLength)
    {
        if (lastGlyphId < firstGlyphId)
            throw new ArgumentOutOfRangeException(nameof(lastGlyphId));

        const uint version = 0x00020000u;
        const uint numSizes = 1;

        const int headerLen = 8;
        const int bstLen = 48;
        const int arrayLen = 8;
        const int subTableOffset = headerLen + bstLen + arrayLen;

        int glyphCount = lastGlyphId - firstGlyphId + 1;
        int offsetsLen = (glyphCount + 1) * 2;
        int subTableLen = 8 + offsetsLen;

        int totalLen = subTableOffset + subTableLen;
        byte[] table = new byte[totalLen];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), version);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), numSizes);

        int bst = headerLen;
        int arrayOffset = headerLen + bstLen;
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 0, 4), (uint)arrayOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 4, 4), (uint)(arrayLen + subTableLen));
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 8, 4), 1u);

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(bst + 40, 2), firstGlyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(bst + 42, 2), lastGlyphId);

        // indexSubTableArray
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(arrayOffset + 0, 2), firstGlyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(arrayOffset + 2, 2), lastGlyphId);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(arrayOffset + 4, 4), arrayLen);

        // indexSubTable (format 3)
        int st = subTableOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 0, 2), 3);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 2, 2), imageFormat);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 4, 4), imageDataOffset);

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 8, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 10, 2), glyphDataLength);

        return table;
    }

    private static byte[] BuildEblc_IndexFormat4(ushort glyphId, ushort imageFormat, uint imageDataOffset, ushort glyphDataLength)
    {
        const uint version = 0x00020000u;
        const uint numSizes = 1;

        const int headerLen = 8;
        const int bstLen = 48;
        const int arrayLen = 8;
        const int subTableOffset = headerLen + bstLen + arrayLen;

        const int numGlyphs = 1;
        const int pairsLen = (numGlyphs + 1) * 4;
        const int subTableLen = 8 + 4 + pairsLen;

        int totalLen = subTableOffset + subTableLen;
        byte[] table = new byte[totalLen];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), version);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), numSizes);

        int bst = headerLen;
        int arrayOffset = headerLen + bstLen;
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 0, 4), (uint)arrayOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 4, 4), (uint)(arrayLen + subTableLen));
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 8, 4), 1u);

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(bst + 40, 2), glyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(bst + 42, 2), glyphId);

        // indexSubTableArray
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(arrayOffset + 0, 2), glyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(arrayOffset + 2, 2), glyphId);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(arrayOffset + 4, 4), arrayLen);

        // indexSubTable (format 4)
        int st = subTableOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 0, 2), 4);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 2, 2), imageFormat);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 4, 4), imageDataOffset);

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 8, 4), numGlyphs);

        // pair[0] = (glyphId, 0), pair[1] = (0, glyphDataLength)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 12, 2), glyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 14, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 16, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 18, 2), glyphDataLength);

        return table;
    }

    private static byte[] BuildEblc_IndexFormat5(ushort glyphId, ushort imageFormat, uint imageDataOffset, uint imageSize)
    {
        const uint version = 0x00020000u;
        const uint numSizes = 1;

        const int headerLen = 8;
        const int bstLen = 48;
        const int arrayLen = 8;
        const int subTableOffset = headerLen + bstLen + arrayLen;

        const int numGlyphs = 1;
        const int glyphCodeArrayLen = numGlyphs * 2;
        const int subTableLen = 8 + 4 + 8 + 4 + glyphCodeArrayLen;

        int totalLen = subTableOffset + subTableLen;
        byte[] table = new byte[totalLen];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), version);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), numSizes);

        int bst = headerLen;
        int arrayOffset = headerLen + bstLen;
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 0, 4), (uint)arrayOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 4, 4), (uint)(arrayLen + subTableLen));
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(bst + 8, 4), 1u);

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(bst + 40, 2), glyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(bst + 42, 2), glyphId);

        // indexSubTableArray
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(arrayOffset + 0, 2), glyphId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(arrayOffset + 2, 2), glyphId);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(arrayOffset + 4, 4), arrayLen);

        // indexSubTable (format 5)
        int st = subTableOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 0, 2), 5);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 2, 2), imageFormat);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 4, 4), imageDataOffset);

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 8, 4), imageSize);
        // bigGlyphMetrics left as zeros
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(st + 20, 4), numGlyphs);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(st + 24, 2), glyphId);

        return table;
    }
}

