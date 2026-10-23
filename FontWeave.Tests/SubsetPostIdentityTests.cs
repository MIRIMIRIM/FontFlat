using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetPostIdentityTests
{
    [Fact]
    public void CompactPost10PreservesStandardNamesAcrossNonidentityGlyphMap()
    {
        byte[] post = Post10();
        ReadStatus status = SubsetOperation.TryPrepare(
            Request(Face(post), SubsetMode.CompactRemap), out var result,
            out var report, out _);

        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        byte[] output = Table(FontBytes(result), SubsetFontFixture.PostTag);
        Assert.Equal(0x00020000u, BinaryPrimitives.ReadUInt32BigEndian(output));
        Assert.Equal(post[4..], output[4..32]);
        Assert.Equal(4, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(32)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(34)));
        Assert.Equal(36, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(36)));
        Assert.Equal(69, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(38)));
        Assert.Equal(257, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(40)));
        Assert.Equal(42, output.Length);
        Assert.Equal(TableAction.Rebuild,
            Assert.Single(report!.Decisions, d => d.Tag == SubsetFontFixture.PostTag).Action);
    }

    [Fact]
    public void RetainPost10PreservesRawNamesAtOriginalGlyphIds()
    {
        byte[] post = Post10();
        ReadStatus status = SubsetOperation.TryPrepare(
            Request(Face(post), SubsetMode.RetainGids), out var result,
            out var report, out _);

        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(post, Table(FontBytes(result), SubsetFontFixture.PostTag));
        Assert.Equal(TableAction.Preserve,
            Assert.Single(report!.Decisions, d => d.Tag == SubsetFontFixture.PostTag).Action);
    }

    [Fact]
    public void CompactPost10PreservesNamesWhenGlyphDomainUsesOnlyStandardPrefix()
    {
        byte[] post = Post10();
        ReadStatus status = SubsetOperation.TryPrepare(
            Request(Face(post, 37), SubsetMode.CompactRemap, [0, 36]), out var result,
            out _, out _);

        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        byte[] output = Table(FontBytes(result), SubsetFontFixture.PostTag);
        Assert.Equal(0x00020000u, BinaryPrimitives.ReadUInt32BigEndian(output));
        Assert.Equal(post[4..], output[4..32]);
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(32)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(34)));
        Assert.Equal(36, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(36)));
        Assert.Equal(38, output.Length);
    }

    [Fact]
    public void CompactPost10RefusesGlyphIdsOutsideStandardNameDomain()
    {
        ReadStatus status = SubsetOperation.TryPrepare(
            Request(Face(Post10(), 259), SubsetMode.CompactRemap, [0, 258]), out var result,
            out _, out var diagnostic);

        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.MaxpTag, diagnostic.Location.TableTag);
        Assert.Equal(DataField.Count, diagnostic.Location.Field);
        Assert.Equal(259UL, diagnostic.Detail);
        Assert.Equal(258UL, diagnostic.Expected);
        Assert.Null(result);
    }

    private static SubsetRequest Request(FontFaceRef face, SubsetMode mode,
        IReadOnlyList<ushort>? gids = null) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot
        {
            Kind = SubsetRootKind.ExplicitGlyphSet,
            Gids = gids ?? [0, 36, 69, 257]
        },
        Mode = mode,
        Options = new SubsetOptions { NoLayout = true }
    };

    private static byte[] Post10()
    {
        var post = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(post, 0x00010000);
        BinaryPrimitives.WriteInt32BigEndian(post.AsSpan(4), -786432);
        BinaryPrimitives.WriteInt16BigEndian(post.AsSpan(8), -120);
        BinaryPrimitives.WriteInt16BigEndian(post.AsSpan(10), 40);
        BinaryPrimitives.WriteUInt32BigEndian(post.AsSpan(12), 1);
        return post;
    }

    private static FontFaceRef Face(byte[] post, ushort glyphCount = 258)
    {
        var records = new List<byte[]>();
        for (int i = 0; i < glyphCount; i++) records.Add(GlyfImages.NoContourGlyphRecord());
        var (loca, glyf, _) = GlyfImages.Assemble(records);
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(glyphCount)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 36))),
            (SubsetCompactPolicyTests.HheaTag, SubsetCompactPolicyTests.Hhea(glyphCount)),
            (SubsetCompactPolicyTests.HmtxTag, SubsetCompactPolicyTests.Hmtx(glyphCount, glyphCount)),
            (SubsetFontFixture.PostTag, post));
    }

    private static byte[] FontBytes(SubsetResult result)
    {
        var bytes = new byte[result.Font.OutputLength];
        Assert.Equal(ReadStatus.Done, result.Font.TryCopyTo(bytes, out int written, out _));
        Assert.Equal(bytes.Length, written);
        return bytes;
    }

    private static byte[] Table(byte[] font, uint tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            if (BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record)) != tag) continue;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));
            return font[offset..(offset + length)];
        }
        throw new Xunit.Sdk.XunitException($"Missing table 0x{tag:X8}");
    }
}
