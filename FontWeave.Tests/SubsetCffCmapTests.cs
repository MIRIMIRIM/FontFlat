// CFF-face cmap rebuild wiring and identity smoke tests (S1b card §4/§7.2, commit ②
// acceptance). The rebuild face on a static CFF1 profile is exactly {cmap}: the S1a
// prune-and-canonicalize pipeline is consumed unchanged (its input domain is
// maxp.numGlyphs, orthogonal to the outline format), CFF/VORG and every other present
// table stay byte-identical, and the full-GID retain identity is asserted in the two
// pre-registered tiers (strict whole-file identity on a canonical-cmap fixture;
// wire-equivalence on a real-shaped fixture, FP-001 form).
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCffCmapTests
{
    private const uint VorgTag = 0x564F5247;

    private static FontFaceRef CffFace(byte[] cmap, byte[]? vorg = null,
        ushort numGlyphs = 3)
    {
        var tables = new List<(uint Tag, byte[] Bytes)>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(numGlyphs)),
            (SubsetFontFixture.CffTag, NameKeyedCff(numGlyphs)),
            (SubsetFontFixture.CmapTag, cmap),
        };
        if (vorg is not null) tables.Add((VorgTag, vorg));
        return SubsetFontFixture.Face(true, [.. tables]);
    }

    private static byte[] Maxp05(ushort numGlyphs)
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), numGlyphs);
        return bytes;
    }

    /// <summary>The same minimal valid name-keyed CFF1 wire fixture as the profile
    /// tests: predefined charset/encoding, one Private DICT, strict tiling.</summary>
    internal static byte[] NameKeyedCff(int numGlyphs)
    {
        static IEnumerable<byte> Number(int value)
        {
            var bytes = new byte[5];
            bytes[0] = 29;
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(1), value);
            return bytes;
        }
        static byte[] Index(params byte[][] items)
        {
            if (items.Length == 0) return [0x00, 0x00];
            var offsets = new List<int> { 1 };
            foreach (var item in items) offsets.Add(offsets[^1] + item.Length);
            int width = 1;
            while (offsets[^1] >= (1 << (8 * width))) width++;
            var bytes = new List<byte>
            {
                (byte)(items.Length >> 8), (byte)items.Length, (byte)width
            };
            foreach (int offset in offsets)
                for (int i = width - 1; i >= 0; i--) bytes.Add((byte)(offset >> (8 * i)));
            foreach (var item in items) bytes.AddRange(item);
            return [.. bytes];
        }
        byte[] name = Index("S1bSynthetic"u8.ToArray());
        byte[] charstrings = Index(Enumerable.Range(0, numGlyphs)
            .Select(_ => new byte[] { 0x8b, 0x0e }).ToArray()); // "0 endchar"
        int charStringsAt = 4 + name.Length + 22 + 2 + 2;
        int privateAt = charStringsAt + charstrings.Length;
        var top = new List<byte>();
        top.AddRange(Number(charStringsAt)); top.Add(17);
        top.AddRange(Number(2)); top.AddRange(Number(privateAt)); top.Add(18);
        var bytes = new List<byte> { 0x01, 0x00, 0x04, 0x04 };
        bytes.AddRange(name);
        bytes.AddRange(Index([.. top]));
        bytes.AddRange(Index());
        bytes.AddRange(Index());
        bytes.AddRange(charstrings);
        bytes.AddRange([0x8b, 0x14]);
        return [.. bytes];
    }

    private static byte[] TableOf(byte[] sfnt, uint tag)
    {
        Assert.True(TryFindTable(sfnt, tag, out int offset, out int length));
        return sfnt[offset..(offset + length)];
    }

    private static bool TryFindTable(byte[] sfnt, uint tag, out int offset, out int length)
    {
        offset = 0;
        length = 0;
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            if (BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record)) != tag) continue;
            offset = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 8));
            length = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 12));
            return true;
        }
        return false;
    }

    private static byte[] Prepare(FontFaceRef face, IReadOnlyList<ushort> gids,
        out SubsetReport report)
    {
        var status = SubsetOperation.TryPrepare(new SubsetRequest
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
            Mode = SubsetMode.RetainGids,
        }, out var result, out var observed, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        Assert.NotNull(observed);
        report = observed!;
        byte[] output = new byte[result!.Font.OutputLength];
        Assert.Equal(ReadStatus.Done,
            result.Font.TryCopyTo(output, out int written, out _));
        Assert.Equal(output.Length, written);
        return output;
    }

    // ---- strict identity tier: canonical-cmap fixture, full GID retain ----

    [Fact]
    public void FullRetainOverCanonicalCmapIsByteIdentical()
    {
        byte[] cmap = SubsetFontFixture.CmapCanonical((0x41, 1), (0x42, 2));
        byte[] input = SubsetFontFixture.Sfnt(true,
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff(3)),
            (SubsetFontFixture.CmapTag, cmap));
        _ = FontSource.TryCopy(input, out FontSource? source, out _);
        _ = source!.TryGetFace(0, out FontFaceRef face, out _);
        byte[] output = Prepare(face, [0, 1, 2], out var report);
        // strict tier: the output IS the input, byte for byte, including the
        // deterministically recomputed head.checkSumAdjustment.
        Assert.True(output.SequenceEqual(input),
            "full-retain identity smoke drifted on the canonical-cmap fixture");
        Assert.Equal(0, report.UnselectedSlotCount);
        Assert.Empty(report.CmapConsumedNotEmitted);
        Assert.NotNull(report.Cff);
    }

    // ---- wire-equivalent tier: real-shaped cmap, subset retain ----

    [Fact]
    public void SubsetPrunesCmapAndKeepsCffVorgBytes()
    {
        byte[] cmap = SubsetFontFixture.CmapWithMacRoman((0x41, 1), (0x42, 2));
        byte[] vorg = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(vorg.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(vorg.AsSpan(4), 880);
        FontFaceRef face = CffFace(cmap, vorg);
        byte[] input = SubsetFontFixture.Sfnt(true,
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff(3)),
            (SubsetFontFixture.CmapTag, cmap),
            (VorgTag, vorg));
        byte[] output = Prepare(face, [0, 1], out var report);

        // same table set, canonical ascending order
        Assert.Equal(5, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(4)));
        // every table is byte-identical except cmap (canonical rebuild) and head
        // (checkSumAdjustment only)
        byte[] cmapOut = TableOf(output, SubsetFontFixture.CmapTag);
        Assert.Equal(SubsetFontFixture.CmapCanonical((0x41, 1)), cmapOut);
        byte[] cffIn = TableOf(input, SubsetFontFixture.CffTag);
        Assert.Equal(cffIn, TableOf(output, SubsetFontFixture.CffTag));
        Assert.Equal(vorg, TableOf(output, VorgTag));
        Assert.Equal(TableOf(input, SubsetFontFixture.MaxpTag),
            TableOf(output, SubsetFontFixture.MaxpTag));
        byte[] headIn = TableOf(input, SubsetFontFixture.HeadTag);
        byte[] headOut = TableOf(output, SubsetFontFixture.HeadTag);
        Assert.True(headOut.AsSpan(0, 8).ToArray().SequenceEqual(headIn.AsSpan(0, 8)));
        Assert.True(headOut[12..].SequenceEqual(headIn[12..]));

        // report: the Mac Roman record was consumed as evidence, not re-emitted
        var consumed = report.CmapConsumedNotEmitted.Select(c => (c.PlatformId, c.EncodingId))
            .ToList();
        Assert.Equal([(1, 0)], consumed);
        // decisions: cmap rebuilds, everything else preserves (CFF/VORG stay raw)
        var byTag = report.Decisions.ToDictionary(d => d.Tag);
        Assert.Equal(TableAction.Rebuild, byTag[SubsetFontFixture.CmapTag].Action);
        Assert.Equal(SubsetPolicy.RebuildTyped, byTag[SubsetFontFixture.CmapTag].Policy);
        Assert.Equal(TableAction.Preserve, byTag[SubsetFontFixture.CffTag].Action);
        Assert.Equal(SubsetPolicy.RetainRaw, byTag[SubsetFontFixture.CffTag].Policy);
        Assert.Equal(TableAction.Preserve, byTag[VorgTag].Action);
        Assert.NotNull(report.Cff);
        Assert.NotNull(report.Vorg);
        Assert.Equal(1, report.UnselectedSlotCount);
        Assert.Empty(report.ClosureAddedGids);
    }

    // ---- the (0,5) fmt14 record drops on CFF faces (the former S0 freeze C5
    //      real-corpus witness; revised by OQ-F14) ----

    [Fact]
    public void CffFaceWithUvsOnlyCmapDropsTheRecord()
    {
        var tables = new List<(uint Tag, byte[] Bytes)>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff(3)),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapUvsOnly(1)),
        };
        FontFaceRef face = SubsetFontFixture.Face(true, [.. tables]);
        var status = SubsetOperation.TryPrepare(new SubsetRequest
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = [0] },
            Mode = SubsetMode.RetainGids,
        }, out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        // The (0,5) record was consumed as evidence and never merged; the rebuild still
        // emits the canonical pair (OQ-F14: subsetting serves no UVS semantics).
        var consumed = Assert.Single(report!.CmapConsumedNotEmitted);
        Assert.Equal((0, 5), (consumed.PlatformId, consumed.EncodingId));
        byte[] output = new byte[result!.Font.OutputLength];
        Assert.Equal(ReadStatus.Done, result.Font.TryCopyTo(output, out _, out _));
        byte[] cmapOut = TableOf(output, SubsetFontFixture.CmapTag);
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(2)));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(4)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(6)));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(12)));
        Assert.Equal(10, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(14)));
    }
}
