using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetLocaEncodingTests
{
    [Fact]
    public void CompactChoosesShortLocaWithoutChangingGlyphBytesOrUnrelatedHeadFields()
    {
        byte[][] records = [Glyph(12), Glyph(14), Glyph(16), [], []];
        byte[] input = Font(records);
        byte[] output = Subset(input, SubsetMode.CompactRemap, [0, 2, 4], out var result);

        Assert.Equal(new uint[] { 0, 12, 28, 28 }, Offsets(output));
        Assert.Equal(8, Table(output, SubsetFontFixture.LocaTag).Length);
        Assert.Equal(records[0].Concat(records[2]).ToArray(), Table(output, SubsetFontFixture.GlyfTag));
        Assert.Equal(new ushort[] { 0, 2, 4 }, result.Map!.Entries.Select(e => e.InputGid));
        AssertHead(input, output, 0);
        AssertHeadDecision(result, TableAction.Rebuild, SubsetPolicy.RebuildTyped);
        AssertChecksums(output);
        Assert.Equal(Table(output, SubsetFontFixture.GlyfTag),
            Table(Subset(output, SubsetMode.CompactRemap, [0, 1, 2], out var second), SubsetFontFixture.GlyfTag));
        AssertHeadDecision(second, TableAction.Preserve, SubsetPolicy.RetainRaw);
    }

    [Theory]
    [InlineData(131070, 0)]
    [InlineData(131072, 1)]
    public void CompactLocaUsesTheExactShortOffsetLimit(int glyphBytes, short expectedFormat)
    {
        byte[] input = Font([Glyph(65534), Glyph(glyphBytes - 65534)]);
        byte[] output = Subset(input, SubsetMode.CompactRemap, [0, 1], out var result);

        Assert.Equal(new uint[] { 0, 65534, (uint)glyphBytes }, Offsets(output));
        Assert.Equal(expectedFormat == 0 ? 6 : 12, Table(output, SubsetFontFixture.LocaTag).Length);
        Assert.Equal(Table(input, SubsetFontFixture.GlyfTag), Table(output, SubsetFontFixture.GlyfTag));
        AssertHead(input, output, expectedFormat);
        AssertHeadDecision(result, expectedFormat == 0 ? TableAction.Rebuild : TableAction.Preserve,
            expectedFormat == 0 ? SubsetPolicy.RebuildTyped : SubsetPolicy.RetainRaw);
        AssertChecksums(output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactKeepsLongLocaForOddInteriorOrTerminalOffsets(bool oddTerminal)
    {
        byte[] input = Font(oddTerminal ? [Glyph(12), Glyph(13), []] : [Glyph(13), Glyph(13), []]);
        byte[] output = Subset(input, SubsetMode.CompactRemap, [0, 1, 2], out var result);

        Assert.Equal(oddTerminal ? new uint[] { 0, 12, 25, 25 } : [0, 13, 26, 26], Offsets(output));
        Assert.Equal(Table(input, SubsetFontFixture.LocaTag), Table(output, SubsetFontFixture.LocaTag));
        Assert.Equal(Table(input, SubsetFontFixture.GlyfTag), Table(output, SubsetFontFixture.GlyfTag));
        AssertHead(input, output, 1);
        AssertHeadDecision(result, TableAction.Preserve, SubsetPolicy.RetainRaw);
        AssertChecksums(output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactShortLocaPreservesZeroAndTrailingEmptyGlyphSlots(bool allEmpty)
    {
        byte[] input = Font([[], allEmpty ? [] : Glyph(12), []]);
        byte[] output = Subset(input, SubsetMode.CompactRemap, [0, 1, 2], out var result);

        uint end = allEmpty ? 0u : 12u;
        Assert.Equal(new uint[] { 0, 0, end, end }, Offsets(output));
        Assert.Equal(8, Table(output, SubsetFontFixture.LocaTag).Length);
        Assert.Equal(Table(input, SubsetFontFixture.GlyfTag), Table(output, SubsetFontFixture.GlyfTag));
        AssertHead(input, output, 0);
        AssertHeadDecision(result, TableAction.Rebuild, SubsetPolicy.RebuildTyped);
        AssertChecksums(output);
    }

    [Fact]
    public void CompactKeepsShortLocaAndHeadWhenSourceIsAlreadyShort()
    {
        byte[] input = Font([Glyph(12), Glyph(14)], shortFormat: true);
        byte[] output = Subset(input, SubsetMode.CompactRemap, [0, 1], out var result);

        Assert.Equal(new uint[] { 0, 12, 26 }, Offsets(output));
        Assert.Equal(Table(input, SubsetFontFixture.LocaTag), Table(output, SubsetFontFixture.LocaTag));
        Assert.Equal(Table(input, SubsetFontFixture.GlyfTag), Table(output, SubsetFontFixture.GlyfTag));
        AssertHead(input, output, 0);
        AssertHeadDecision(result, TableAction.Preserve, SubsetPolicy.RetainRaw);
        AssertChecksums(output);
    }

    [Fact]
    public void RetainFullSelectionPreservesLongLocaAndHead()
    {
        byte[] input = Font([Glyph(12), Glyph(14)]);
        byte[] output = Subset(input, SubsetMode.RetainGids, [0, 1], out var result);

        Assert.Equal(Table(input, SubsetFontFixture.LocaTag), Table(output, SubsetFontFixture.LocaTag));
        Assert.Equal(Table(input, SubsetFontFixture.GlyfTag), Table(output, SubsetFontFixture.GlyfTag));
        AssertHead(input, output, 1);
        AssertHeadDecision(result, TableAction.Preserve, SubsetPolicy.RetainRaw);
        AssertChecksums(output);
    }

    [Fact]
    public void CompactLocaHeadBudgetFailurePublishesNoPartialFont()
    {
        var request = Request(Font([Glyph(12), Glyph(14)]), SubsetMode.CompactRemap, [0, 1]);
        Assert.Equal(ReadStatus.ResourceLimit, SubsetOperation.TryPrepare(request,
            out var result, out var report, out var diagnostic,
            new BuildLimits { MaxOutputBytes = 53 }));

        Assert.Null(result);
        Assert.NotNull(report);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.HeadTag, diagnostic.Location.TableTag);
        Assert.Equal(DataField.OutputBytes, diagnostic.Location.Field);
        Assert.Equal(54UL, diagnostic.Detail);
        Assert.Equal(53UL, diagnostic.Expected);
        var decision = Assert.Single(report.Decisions, d => d.Tag == SubsetFontFixture.HeadTag);
        Assert.Equal(TableAction.Preserve, decision.Action);
        Assert.Equal(SubsetPolicy.RetainRaw, decision.Policy);
    }

    [Fact]
    public void CompactRejectsDescendingSourceLocaBeforeEncoding()
    {
        byte[] input = Font([Glyph(12), Glyph(12)], offsets: [0, 24, 12]);
        Assert.Equal(ReadStatus.InvalidOffset, SubsetOperation.TryPrepare(
            Request(input, SubsetMode.CompactRemap, [0, 1]), out var result,
            out _, out var diagnostic));

        Assert.Null(result);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.LocaTag, diagnostic.Location.TableTag);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
        Assert.Equal(12UL, diagnostic.Detail);
        Assert.Equal(24UL, diagnostic.Expected);
    }

    private static byte[] Glyph(int length)
    {
        byte[] bytes = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), checked((ushort)(length - 12)));
        bytes.AsSpan(12).Fill(0x4D);
        return bytes;
    }

    private static byte[] Font(byte[][] records, bool shortFormat = false, uint[]? offsets = null)
    {
        var (loca, glyf, _) = GlyfImages.Assemble(records.ToList());
        if (offsets is not null) loca = GlyfImages.LongLoca(offsets);
        if (shortFormat)
        {
            byte[] shortLoca = new byte[2 * (records.Length + 1)];
            for (int i = 0; i <= records.Length; i++)
                BinaryPrimitives.WriteUInt16BigEndian(shortLoca.AsSpan(2 * i),
                    checked((ushort)(BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(4 * i)) / 2)));
            loca = shortLoca;
        }
        byte[] head = SubsetFontFixture.Head((short)(shortFormat ? 0 : 1));
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(4), 0x00017532);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(16), 0x0013);
        BinaryPrimitives.WriteInt64BigEndian(head.AsSpan(20), 1234567890);
        BinaryPrimitives.WriteInt64BigEndian(head.AsSpan(28), 1234567891);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(36), -50);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(40), 800);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(44), 3);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(46), 9);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(48), -1);
        ushort count = checked((ushort)records.Length);
        return SubsetFontFixture.Sfnt(true,
            (SubsetFontFixture.HeadTag, head),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(count)),
            (SubsetFontFixture.GlyfTag, glyf), (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 1))),
            (SubsetCompactPolicyTests.HheaTag, SubsetCompactPolicyTests.Hhea(count)),
            (SubsetCompactPolicyTests.HmtxTag, SubsetCompactPolicyTests.Hmtx(count, count)));
    }

    private static SubsetRequest Request(byte[] font, SubsetMode mode, ushort[] glyphs)
    {
        Assert.Equal(ReadStatus.Done, FontSource.TryCopy(font, out var source, out _));
        Assert.Equal(ReadStatus.Done, source!.TryGetFace(0, out var face, out _));
        return new SubsetRequest
        {
            Face = face, Mode = mode,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = glyphs },
            Options = new SubsetOptions { NoLayout = true }
        };
    }

    private static byte[] Subset(byte[] font, SubsetMode mode, ushort[] glyphs, out SubsetResult result)
    {
        Assert.Equal(ReadStatus.Done, SubsetOperation.TryPrepare(Request(font, mode, glyphs),
            out var prepared, out _, out var diagnostic));
        Assert.Equal(default, diagnostic);
        Assert.NotNull(prepared);
        result = prepared;
        var bytes = new byte[result.Font.OutputLength];
        Assert.Equal(ReadStatus.Done, result.Font.TryCopyTo(bytes, out int written, out _));
        Assert.Equal(bytes.Length, written);
        return bytes;
    }

    private static void AssertHead(byte[] input, byte[] output, short format)
    {
        byte[] original = Table(input, SubsetFontFixture.HeadTag);
        byte[] rebuilt = Table(output, SubsetFontFixture.HeadTag);
        Assert.Equal(format, BinaryPrimitives.ReadInt16BigEndian(rebuilt.AsSpan(50)));
        original.AsSpan(8, 4).Clear();
        rebuilt.AsSpan(8, 4).Clear();
        BinaryPrimitives.WriteInt16BigEndian(original.AsSpan(50), format);
        Assert.Equal(original, rebuilt);
    }

    private static void AssertHeadDecision(SubsetResult result, TableAction action, SubsetPolicy policy)
    {
        var decision = Assert.Single(result.Report.Decisions, d => d.Tag == SubsetFontFixture.HeadTag);
        Assert.Equal(action, decision.Action);
        Assert.Equal(policy, decision.Policy);
        Assert.Equal(action, Assert.Single(result.Font.Decisions.ToArray(),
            d => d.Tag == SubsetFontFixture.HeadTag).Action);
    }

    private static uint[] Offsets(byte[] font)
    {
        byte[] loca = Table(font, SubsetFontFixture.LocaTag);
        bool longFormat = BinaryPrimitives.ReadInt16BigEndian(Table(font, SubsetFontFixture.HeadTag).AsSpan(50)) != 0;
        var offsets = new uint[loca.Length / (longFormat ? 4 : 2)];
        for (int i = 0; i < offsets.Length; i++)
            offsets[i] = longFormat ? BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(i * 4))
                : 2u * BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan(i * 2));
        return offsets;
    }

    private static byte[] Table(byte[] font, uint tag)
    {
        for (int i = 0; i < BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4)); i++)
        {
            int row = 12 + i * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(row)) != tag) continue;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(row + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(row + 12));
            return font[offset..(offset + length)];
        }
        throw new Xunit.Sdk.XunitException($"Missing table {tag:X8}");
    }

    private static void AssertChecksums(byte[] font)
    {
        Assert.Equal(0xB1B0AFBAu, Checksum(font));
        for (int i = 0; i < BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4)); i++)
        {
            int row = 12 + i * 16;
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(row));
            byte[] bytes = Table(font, tag);
            if (tag == SubsetFontFixture.HeadTag) bytes.AsSpan(8, 4).Clear();
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(row + 4)), Checksum(bytes));
        }
    }

    private static uint Checksum(byte[] bytes)
    {
        uint sum = 0;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            uint word = 0;
            for (int j = 0; j < 4 && i + j < bytes.Length; j++)
                word |= (uint)bytes[i + j] << (24 - j * 8);
            sum = unchecked(sum + word);
        }
        return sum;
    }
}
