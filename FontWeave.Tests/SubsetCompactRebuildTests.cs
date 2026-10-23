// Compact rebuild stage tests (SC card §4 commit ③ acceptance): the identity spine and
// the metrics families rebuild over the frozen map on a metrics-bearing face with no
// post-2.0/hdmx/LTSH rows, so the pipeline completes in this slice.
//
// Expectations are derived at the wire level here (raw byte parsing), independent of the
// production models — the same discipline the validation gate applies with its oracle.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCompactRebuildTests
{
    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = SubsetMode.CompactRemap,
        Options = new SubsetOptions { NoLayout = true },
    };

    /// <summary>The commit-③ spine face: no post 2.0, LTSH or hdmx rows, so every
    /// compact rebuild decision has a producer and the prepare completes.</summary>
    internal static FontFaceRef SpineFace(ushort[] advances)
    {
        var (loca, glyf, _) = GlyfImages.Assemble(
        [
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.CompositeRecord(2),
        ]);
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 2))),
            (SubsetCompactPolicyTests.HheaTag,
                SubsetCompactPolicyTests.Hhea(4, (ushort)advances.Max())),
            (SubsetCompactPolicyTests.HmtxTag, Hmtx(advances)),
            (SubsetFontFixture.NameTag, SubsetFontFixture.Dummy(16)));
    }

    /// <summary>Per-glyph long metrics with distinct or shared advances as given.</summary>
    internal static byte[] Hmtx(ushort[] advances)
    {
        var bytes = new byte[4 * advances.Length];
        for (int i = 0; i < advances.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4 * i), advances[i]);
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(4 * i + 2), (short)(i + 7));
        }
        return bytes;
    }

    private static Dictionary<uint, (int Offset, int Length)> TableSpans(byte[] font)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        var spans = new Dictionary<uint, (int, int)>();
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record));
            spans[tag] = ((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12)));
        }
        return spans;
    }

    private static byte[] Table(byte[] font, Dictionary<uint, (int Offset, int Length)> spans,
        uint tag)
    {
        var (offset, length) = spans[tag];
        return font[offset..(offset + length)];
    }

    [Fact]
    public void CompactPreparesSpineFaceWithDerivedFields()
    {
        var face = SpineFace([908, 0, 539, 496]);
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 3]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.NotNull(report!.MapArtifactSha256);
        Assert.NotNull(result!.Map);
        var spans = TableSpans(FontBytes(result));
        // maxp: numGlyphs = 4, version 1.0 intact
        byte[] maxp = Table(FontBytes(result), spans, SubsetFontFixture.MaxpTag);
        Assert.Equal(0x00010000u, BinaryPrimitives.ReadUInt32BigEndian(maxp));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16BigEndian(maxp.AsSpan(4)));
        byte[] loca = Table(FontBytes(result), spans, SubsetFontFixture.LocaTag);
        Assert.Equal(10, loca.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt16BigEndian(
            Table(FontBytes(result), spans, SubsetFontFixture.HeadTag).AsSpan(50)));
        byte[] glyf = Table(FontBytes(result), spans, SubsetFontFixture.GlyfTag);
        Assert.Equal(glyf.Length, (int)SubsetFontFixture.LocaOffset(
            Table(FontBytes(result), spans, SubsetFontFixture.HeadTag), loca, 4));
        // hhea/hmtx: advances of {0,1,2,3} are pairwise distinct -> boundary 4, 16 bytes
        byte[] hhea = Table(FontBytes(result), spans, SubsetCompactPolicyTests.HheaTag);
        Assert.Equal(4, BinaryPrimitives.ReadUInt16BigEndian(hhea.AsSpan(34)));
        byte[] hmtx = Table(FontBytes(result), spans, SubsetCompactPolicyTests.HmtxTag);
        Assert.Equal(16, hmtx.Length);
        Assert.Equal(908, BinaryPrimitives.ReadUInt16BigEndian(hmtx));
        Assert.Equal(496, BinaryPrimitives.ReadUInt16BigEndian(hmtx.AsSpan(12)));
    }

    // A non-identity map: requesting {0,2,3} drops glyph 1, so output 1 = input 2 and
    // output 2 = input 3. The composite's component reference and the cmap value both
    // remap to the output ordinals, and the metrics follow the same order.
    [Fact]
    public void CompactRemapsCompositeReferencesAndCmapValues()
    {
        var face = SpineFace([908, 0, 539, 496]);
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2, 3]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([], report!.ClosureAddedGids); // 2 is explicit; nothing to add
        var font = FontBytes(result!);
        var spans = TableSpans(font);
        // the composite output slot 2's single component now references output 1
        byte[] glyf = Table(font, spans, SubsetFontFixture.GlyfTag);
        byte[] loca = Table(font, spans, SubsetFontFixture.LocaTag);
        byte[] head = Table(font, spans, SubsetFontFixture.HeadTag);
        uint start = SubsetFontFixture.LocaOffset(head, loca, 2);
        uint end = SubsetFontFixture.LocaOffset(head, loca, 3);
        Assert.Equal(-1, (short)BinaryPrimitives.ReadInt16BigEndian(glyf.AsSpan((int)start)));
        // input 2 (the component) sits at output 1 in the frozen order [0, 2, 3]
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(glyf.AsSpan((int)start + 12)));
        // cmap: 0x41 mapped to input 2, whose output ordinal is 1
        byte[] cmap = Table(font, spans, SubsetFontFixture.CmapTag);
        Assert.Equal(1, ReadCanonicalCmapGid(cmap, 0x41));
        // maxp: numGlyphs moves to the map output domain (3), not the input's 4
        byte[] maxp = Table(font, spans, SubsetFontFixture.MaxpTag);
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(maxp.AsSpan(4)));
        // metrics in output order: advances of inputs {0,2,3} = 908, 539, 496
        byte[] hmtx = Table(font, spans, SubsetCompactPolicyTests.HmtxTag);
        Assert.Equal(908, BinaryPrimitives.ReadUInt16BigEndian(hmtx));
        Assert.Equal(539, BinaryPrimitives.ReadUInt16BigEndian(hmtx.AsSpan(4)));
        Assert.Equal(496, BinaryPrimitives.ReadUInt16BigEndian(hmtx.AsSpan(8)));
        // the map artifact carries the exact frozen order 0 -> 0, 1 -> 2, 2 -> 3
        var artifact = result!.Map!;
        Assert.Equal(3, artifact.OutputGlyphCount);
        Assert.Equal([0, 2, 3], artifact.Entries.Select(e => (int)e.InputGid));
    }

    // OQ-SC-4: shared trailing advances collapse. Output advances {908, 200, 200} give
    // the minimal boundary 2, so hmtx is 2 long records + 1 tail bearing = 10 bytes and
    // hhea.numberOfHMetrics = 2, with the advance maximum carried into hhea.
    [Fact]
    public void CompactCollapsesSharedTailAdvances()
    {
        var face = SpineFace([908, 200, 200, 539]);
        // request {0,1,2}: output advances 908, 200, 200 -> boundary 2
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2]), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var font = FontBytes(result!);
        var spans = TableSpans(font);
        byte[] hhea = Table(font, spans, SubsetCompactPolicyTests.HheaTag);
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(hhea.AsSpan(34)));
        // the advance maximum carries the rebuilt long metrics' maximum (908)
        Assert.Equal(908, BinaryPrimitives.ReadUInt16BigEndian(hhea.AsSpan(10)));
        byte[] hmtx = Table(font, spans, SubsetCompactPolicyTests.HmtxTag);
        Assert.Equal(10, hmtx.Length); // 2 long metrics + 1 tail bearing
        Assert.Equal(200, BinaryPrimitives.ReadUInt16BigEndian(hmtx.AsSpan(4)));
        Assert.Equal(9, BinaryPrimitives.ReadInt16BigEndian(hmtx.AsSpan(8))); // bearing of input 2
    }

    [Fact]
    public void FullSelectionCompactMatchesRetainOnSpliceTables()
    {
        var face = SpineFace([908, 0, 539, 496]);
        var gids = new ushort[] { 0, 1, 2, 3 };
        _ = SubsetOperation.TryPrepare(new SubsetRequest
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
            Mode = SubsetMode.RetainGids,
            Options = new SubsetOptions { NoLayout = true },
        }, out var retain, out _, out _);
        _ = SubsetOperation.TryPrepare(Request(face, gids), out var compact, out _,
            out _);
        Assert.NotNull(retain);
        Assert.NotNull(compact);
        byte[] retainFont = FontBytes(retain!);
        byte[] compactFont = FontBytes(compact!);
        var retainSpans = TableSpans(retainFont);
        var compactSpans = TableSpans(compactFont);
        Assert.True(Table(retainFont, retainSpans, SubsetFontFixture.GlyfTag)
            .AsSpan().SequenceEqual(Table(compactFont, compactSpans, SubsetFontFixture.GlyfTag)),
            "glyf bytes diverged between retain and full-selection compact");
        byte[] retainLoca = Table(retainFont, retainSpans, SubsetFontFixture.LocaTag);
        byte[] compactLoca = Table(compactFont, compactSpans, SubsetFontFixture.LocaTag);
        byte[] retainHead = Table(retainFont, retainSpans, SubsetFontFixture.HeadTag);
        byte[] compactHead = Table(compactFont, compactSpans, SubsetFontFixture.HeadTag);
        Assert.Equal(20, retainLoca.Length);
        Assert.Equal(10, compactLoca.Length);
        Assert.Equal(1, BinaryPrimitives.ReadInt16BigEndian(retainHead.AsSpan(50)));
        Assert.Equal(0, BinaryPrimitives.ReadInt16BigEndian(compactHead.AsSpan(50)));
        uint[] expectedOffsets = [0, 12, 24, 36, 52];
        for (int i = 0; i < expectedOffsets.Length; i++)
        {
            Assert.Equal(expectedOffsets[i], SubsetFontFixture.LocaOffset(retainHead, retainLoca, i));
            Assert.Equal(expectedOffsets[i], SubsetFontFixture.LocaOffset(compactHead, compactLoca, i));
        }
        Assert.True(Table(retainFont, retainSpans, SubsetFontFixture.CmapTag)
            .AsSpan().SequenceEqual(Table(compactFont, compactSpans, SubsetFontFixture.CmapTag)),
            "cmap bytes diverged between retain and full-selection compact");
    }

    // The empty member glyph (.null analogue at slot 1) stays a legal zero-length member
    // under compact: membership is preserved, never refilled or shifted.
    [Fact]
    public void CompactKeepsEmptyMemberSlotsEmpty()
    {
        var records = new List<byte[]>
        {
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.CompositeRecord(1),
        };
        var (loca, glyf, _) = GlyfImages.Assemble(records);
        // make slot 1 a zero-length (empty) member: entry[1] = entry[2] collapses it
        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(4),
            BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(8)));
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(3)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 0))),
            (SubsetCompactPolicyTests.HheaTag, SubsetCompactPolicyTests.Hhea(3)),
            (SubsetCompactPolicyTests.HmtxTag, Hmtx([100, 100, 100])));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2]), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var font = FontBytes(result!);
        var spans = TableSpans(font);
        byte[] outLoca = Table(font, spans, SubsetFontFixture.LocaTag);
        byte[] outHead = Table(font, spans, SubsetFontFixture.HeadTag);
        uint slot1Start = SubsetFontFixture.LocaOffset(outHead, outLoca, 1);
        uint slot1End = SubsetFontFixture.LocaOffset(outHead, outLoca, 2);
        Assert.Equal(slot1Start, slot1End); // the empty member stays empty
    }

    private static byte[] FontBytes(SubsetResult result)
    {
        byte[] bytes = new byte[result.Font.OutputLength];
        var status = result.Font.TryCopyTo(bytes, out int written, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(bytes.Length, written);
        return bytes;
    }

    /// <summary>Reads one code point out of a canonical {((3,1),4),((3,10),12)} table's
    /// format 4 subtable through the delta path (idRangeOffset 0).</summary>
    private static int ReadCanonicalCmapGid(byte[] cmap, uint code)
    {
        int subtables = BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(2));
        for (int i = 0; i < subtables; i++)
        {
            int record = 4 + 8 * i;
            if (BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(record)) != 3 ||
                BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(record + 2)) != 1) continue;
            int subtable = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap.AsSpan(record + 4));
            int segCountX2 = BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(subtable + 6));
            int segCount = segCountX2 / 2;
            for (int s = 0; s < segCount; s++)
            {
                ushort endCode = BinaryPrimitives.ReadUInt16BigEndian(
                    cmap.AsSpan(subtable + 14 + 2 * s));
                ushort startCode = BinaryPrimitives.ReadUInt16BigEndian(
                    cmap.AsSpan(subtable + 16 + segCountX2 + 2 * s));
                if (code < startCode || code > endCode) continue;
                // the wire word is the two's-complement delta; the narrowing is the
                // sign-bit reinterpretation the spec defines, hence unchecked
                short delta = unchecked((short)BinaryPrimitives.ReadUInt16BigEndian(
                    cmap.AsSpan(subtable + 16 + 2 * segCountX2 + 2 * s)));
                int resolved = (int)code + delta;
                return (ushort)resolved & 0xFFFF;
            }
        }
        return -1;
    }
}
