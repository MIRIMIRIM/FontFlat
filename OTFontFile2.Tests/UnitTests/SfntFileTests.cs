using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class SfntFileTests
{
    [TestMethod]
    public void FromMemory_SingleSfnt_CanReadDirectoryAndTable()
    {
        // Build a minimal sfnt with one table: 'TEST' (4 bytes).
        // Offset table (12) + 1 directory entry (16) = 28, table at offset 28.
        byte[] fontBytes = new byte[32];

        // sfntVersion = 0x00010000 (TrueType)
        fontBytes[0] = 0x00;
        fontBytes[1] = 0x01;
        fontBytes[2] = 0x00;
        fontBytes[3] = 0x00;

        // numTables = 1
        fontBytes[4] = 0x00;
        fontBytes[5] = 0x01;

        // searchRange = 16, entrySelector = 0, rangeShift = 0
        fontBytes[6] = 0x00;
        fontBytes[7] = 0x10;

        // Directory entry @ 12:
        // tag = 'TEST'
        fontBytes[12] = (byte)'T';
        fontBytes[13] = (byte)'E';
        fontBytes[14] = (byte)'S';
        fontBytes[15] = (byte)'T';

        // checksum = 0 (16..19)

        // offset = 28 (20..23)
        fontBytes[23] = 0x1C;

        // length = 4 (24..27)
        fontBytes[27] = 0x04;

        // table data @ 28
        fontBytes[28] = 1;
        fontBytes[29] = 2;
        fontBytes[30] = 3;
        fontBytes[31] = 4;

        Assert.IsTrue(SfntFile.TryFromMemory(fontBytes, out var file, out var error), error.ToString());
        using (file)
        {
            Assert.IsFalse(file.IsTtc);
            Assert.AreEqual(1, file.FontCount);

            var font = file.GetFont(0);
            Assert.AreEqual(1u, (uint)font.TableCount);

            Assert.IsTrue(Tag.TryParse("TEST", out var testTag));
            Assert.IsTrue(font.TryGetTableData(testTag, out var tableData, out var record));

            Assert.AreEqual("TEST", record.Tag.ToString());
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, tableData.ToArray());
        }
    }

    [TestMethod]
    public void Writer_EmitsValidHeadCheckSumAdjustment()
    {
        Assert.IsTrue(Tag.TryParse("head", out var headTag));
        Assert.IsTrue(Tag.TryParse("TEST", out var testTag));

        // Minimal head (12 bytes) with a non-zero checkSumAdjustment placeholder (offset 8).
        // Writer must replace it so that whole-file checksum equals 0xB1B0AFBA.
        byte[] head = new byte[12];
        head[0] = 0xDE;
        head[1] = 0xAD;
        head[2] = 0xBE;
        head[3] = 0xEF;
        head[4] = 0x01;
        head[5] = 0x02;
        head[6] = 0x03;
        head[7] = 0x04;
        head[8] = 0x11;
        head[9] = 0x22;
        head[10] = 0x33;
        head[11] = 0x44;

        byte[] test = new byte[] { 1, 2, 3, 4 };

        var builder = new SfntBuilder();
        builder.SetTable(headTag, head);
        builder.SetTable(testTag, test);

        byte[] written = builder.ToArray();

        // Verify file checksum magic.
        uint fileChecksum = ComputeChecksum(written);
        Assert.AreEqual(0xB1B0AFBAu, fileChecksum);

        // Verify head directory checksum uses checkSumAdjustment=0.
        Assert.IsTrue(SfntFile.TryFromMemory(written, out var file, out var error), error.ToString());
        using (file)
        {
            var font = file.GetFont(0);
            Assert.IsTrue(font.TryGetTableData(headTag, out var headData, out var headRecord));

            byte[] headCopy = headData.ToArray();
            headCopy[8] = 0;
            headCopy[9] = 0;
            headCopy[10] = 0;
            headCopy[11] = 0;

            uint headDirChecksum = ComputeChecksum(headCopy);
            Assert.AreEqual(headDirChecksum, headRecord.Checksum);
        }
    }

    private static uint ComputeChecksum(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            uint sum = 0;

            int end = data.Length & ~3;
            for (int i = 0; i < end; i += 4)
            {
                sum += (uint)(data[i] << 24 | data[i + 1] << 16 | data[i + 2] << 8 | data[i + 3]);
            }

            int rem = data.Length - end;
            if (rem != 0)
            {
                Span<byte> tail = stackalloc byte[4];
                tail.Clear();
                data.Slice(end, rem).CopyTo(tail);
                sum += (uint)(tail[0] << 24 | tail[1] << 16 | tail[2] << 8 | tail[3]);
            }

            return sum;
        }
    }
}
