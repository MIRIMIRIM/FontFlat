using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class OpenTypeChecksumTests
{
    [TestMethod]
    public void Compute_MatchesReference_ForVariousLengths()
    {
        var rng = new Random(1234567);

        for (int length = 0; length <= 2048; length++)
        {
            byte[] data = new byte[length];
            rng.NextBytes(data);

            uint expected = ComputeReference(data);
            uint actual = OpenTypeChecksum.Compute(data);
            Assert.AreEqual(expected, actual, $"Checksum mismatch at length={length}");
        }
    }

    [TestMethod]
    public void ComputeHeadDirectoryChecksum_ZeroesCheckSumAdjustmentField()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);
        head[8] = 0x12;
        head[9] = 0x34;
        head[10] = 0x56;
        head[11] = 0x78;

        uint expected = ComputeReferenceWithHeadAdjustmentZero(head);
        uint actual = OpenTypeChecksum.ComputeHeadDirectoryChecksum(head);
        Assert.AreEqual(expected, actual);
    }

    private static uint ComputeReference(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            uint sum = 0;
            int i = 0;
            int end = data.Length & ~3;
            while (i < end)
            {
                uint word = ((uint)data[i] << 24) |
                            ((uint)data[i + 1] << 16) |
                            ((uint)data[i + 2] << 8) |
                            data[i + 3];
                sum += word;
                i += 4;
            }

            if (i < data.Length)
            {
                uint tail = 0;
                int remaining = data.Length - i;
                for (int j = 0; j < remaining; j++)
                {
                    tail |= (uint)data[i + j] << (24 - (j * 8));
                }

                sum += tail;
            }

            return sum;
        }
    }

    private static uint ComputeReferenceWithHeadAdjustmentZero(ReadOnlySpan<byte> head)
    {
        byte[] copy = head.ToArray();
        if (copy.Length >= 12)
        {
            copy[8] = 0;
            copy[9] = 0;
            copy[10] = 0;
            copy[11] = 0;
        }

        return ComputeReference(copy);
    }
}
