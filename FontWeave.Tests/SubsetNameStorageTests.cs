using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetNameStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactSharesNameStorageWithoutChangingRecordsTagsOrOpaqueBytes(bool format1)
    {
        byte[] input = Name(format1);
        byte[] output = Subset(input, SubsetMode.CompactRemap, out var report);
        int metadata = 6 + 5 * 12 + (format1 ? 10 : 0);

        Assert.Equal(metadata + 11, output.Length);
        Assert.Equal((ushort)(format1 ? 1 : 0), U16(output, 0));
        Assert.Equal(5, U16(output, 2));
        Assert.Equal(metadata, U16(output, 4));
        for (int i = 0; i < 5; i++)
        {
            int row = 6 + i * 12;
            Assert.Equal(input[row..(row + 10)], output[row..(row + 10)]);
            Assert.Equal(Payload(input, row + 8), Payload(output, row + 8));
        }
        Assert.Equal(U16(output, 16), U16(output, 28));
        Assert.Equal(U16(output, 16) + 2, U16(output, 40));
        if (format1)
        {
            Assert.Equal(2, U16(output, 66));
            for (int i = 0; i < 2; i++)
                Assert.Equal(Payload(input, 68 + i * 4), Payload(output, 68 + i * 4));
            Assert.Equal(U16(output, 16), U16(output, 70));
            Assert.Equal(U16(output, 70), U16(output, 74));
        }
        Assert.Equal(new byte[] { 0xE1, 0xE2, 0xF1, 0xF2, 0xF3 }, output[^5..]);
        Assert.Equal(TableAction.Rebuild,
            Assert.Single(report.Decisions, d => d.Tag == SubsetFontFixture.NameTag).Action);
        Assert.Equal(output, Subset(output, SubsetMode.CompactRemap, out var secondReport));
        Assert.Equal(TableAction.Preserve,
            Assert.Single(secondReport.Decisions, d => d.Tag == SubsetFontFixture.NameTag).Action);
    }

    [Fact]
    public void RetainPreservesTheOriginalNameStorage()
    {
        byte[] input = Name(true);
        Assert.Equal(input, Subset(input, SubsetMode.RetainGids, out var report));
        Assert.Equal(TableAction.Preserve,
            Assert.Single(report.Decisions, d => d.Tag == SubsetFontFixture.NameTag).Action);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CompactKeepsNameRawWhenItsStorageCannotBeSafelyInterpreted(int damage)
    {
        byte[] input = Name(true);
        if (damage == 0) U16(input, 0, 2);
        else if (damage == 1) U16(input, 16, ushort.MaxValue);
        else U16(input, 4, 6);

        Assert.Equal(input, Subset(input, SubsetMode.CompactRemap, out var report));
        Assert.Equal(TableAction.Preserve,
            Assert.Single(report.Decisions, d => d.Tag == SubsetFontFixture.NameTag).Action);
    }

    private static byte[] Name(bool format1)
    {
        byte[][] payloads = [[0, 65, 0, 66], [0, 65, 0, 66], [0, 66], [0xD8, 0], []];
        int metadata = 66 + (format1 ? 10 : 0);
        byte[] bytes = new byte[metadata + 2 + 12 + (format1 ? 8 : 0) + 3];
        U16(bytes, 0, (ushort)(format1 ? 1 : 0));
        U16(bytes, 2, 5);
        U16(bytes, 4, (ushort)(metadata + 2));
        bytes[metadata] = 0xE1;
        bytes[metadata + 1] = 0xE2;
        ushort[] platforms = [3, 255, 0, 3, 3];
        ushort[] encodings = [1, 65535, 4, 1, 1];
        ushort[] languages = [0x409, 123, 0, 0x409, 0x409];
        ushort[] ids = [4, 999, 6, 1, 2];
        int cursor = metadata + 2;
        for (int i = 0; i < payloads.Length; i++)
        {
            int row = 6 + i * 12;
            U16(bytes, row, platforms[i]);
            U16(bytes, row + 2, encodings[i]);
            U16(bytes, row + 4, languages[i]);
            U16(bytes, row + 6, ids[i]);
            U16(bytes, row + 8, (ushort)payloads[i].Length);
            U16(bytes, row + 10, (ushort)(cursor - metadata - 2));
            payloads[i].CopyTo(bytes, cursor);
            cursor += payloads[i].Length;
        }
        if (format1)
        {
            U16(bytes, 66, 2);
            for (int i = 0; i < 2; i++)
            {
                U16(bytes, 68 + i * 4, 4);
                U16(bytes, 70 + i * 4, (ushort)(cursor - metadata - 2));
                payloads[0].CopyTo(bytes, cursor);
                cursor += 4;
            }
        }
        bytes[cursor] = 0xF1;
        bytes[cursor + 1] = 0xF2;
        bytes[cursor + 2] = 0xF3;
        return bytes;
    }

    private static byte[] Payload(byte[] bytes, int lengthAt)
    {
        int start = U16(bytes, 4) + U16(bytes, lengthAt + 2);
        return bytes[start..(start + U16(bytes, lengthAt))];
    }

    private static byte[] Subset(byte[] name, SubsetMode mode, out SubsetReport report)
    {
        var (loca, glyf, _) = GlyfImages.Assemble(
            [GlyfImages.NoContourGlyphRecord(), GlyfImages.NoContourGlyphRecord()]);
        FontFaceRef face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(2)),
            (SubsetFontFixture.GlyfTag, glyf), (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 1))),
            (SubsetCompactPolicyTests.HheaTag, SubsetCompactPolicyTests.Hhea(2)),
            (SubsetCompactPolicyTests.HmtxTag, SubsetCompactPolicyTests.Hmtx(2, 2)),
            (SubsetFontFixture.NameTag, name));
        var request = new SubsetRequest
        {
            Face = face, Mode = mode,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = [0, 1] },
            Options = new SubsetOptions { NoLayout = true }
        };
        Assert.Equal(ReadStatus.Done, SubsetOperation.TryPrepare(request,
            out var result, out var actualReport, out _));
        Assert.NotNull(result);
        Assert.NotNull(actualReport);
        report = actualReport;
        byte[] bytes = new byte[result.Font.OutputLength];
        Assert.Equal(ReadStatus.Done, result.Font.TryCopyTo(bytes, out int written, out _));
        Assert.Equal(bytes.Length, written);
        for (int i = 0; i < U16(bytes, 4); i++)
        {
            int row = 12 + i * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(row)) != SubsetFontFixture.NameTag)
                continue;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(row + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(row + 12));
            return bytes[offset..(offset + length)];
        }
        throw new Xunit.Sdk.XunitException("Missing name table");
    }

    private static ushort U16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));

    private static void U16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
}
