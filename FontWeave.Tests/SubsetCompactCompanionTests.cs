// Compact companion-table rebuild tests (SC card §4 commit ④ acceptance): post 2.0,
// hdmx, LTSH and VDMX rebuild over the frozen map; post 1.0/3.0 stay raw in both
// modes; the drop-hinting branch deletes LTSH and strips glyf instructions.
//
// Output expectations are parsed from the emitted wire bytes, independent of the
// production models.
using System.Buffers.Binary;
using System.Text;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCompactCompanionTests
{
    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        bool dropHinting = false) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = SubsetMode.CompactRemap,
        Options = new SubsetOptions { NoLayout = true, DropHinting = dropHinting },
    };

    /// <summary>A post 2.0 wire image: header, numGlyphs, glyphNameIndex values and a
    /// Pascal name pool of [A-Za-z0-9._] names.</summary>
    internal static byte[] Post20(ushort numGlyphs, ushort[] indices, string[] names)
    {
        var bytes = new List<byte>();
        bytes.AddRange([0x00, 0x02, 0x00, 0x00]);                 // version 2.0
        bytes.AddRange([0xFF, 0xFF, 0xFF, 0x88]);                 // italic angle
        bytes.AddRange([0x00, 0x80]);                             // underline position
        bytes.AddRange([0x00, 0x28]);                             // underline thickness
        bytes.AddRange([0x00, 0x00, 0x00, 0x00]);                 // isFixedPitch
        for (int i = 0; i < 16; i++) bytes.Add(0);                // memory words
        var num = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(num, numGlyphs);
        bytes.AddRange(num);
        foreach (ushort index in indices)
        {
            BinaryPrimitives.WriteUInt16BigEndian(num, index);
            bytes.AddRange(num);
        }
        foreach (string name in names)
        {
            bytes.Add((byte)name.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(name));
        }
        return [.. bytes];
    }

    /// <summary>A minimal VDMX: one ratio group over three pixel heights.</summary>
    internal static byte[] Vdmx()
    {
        // header 6: version 0, numRecs 1, numRatios 1; ratio 4 + offset 2 -> directory
        // end 12; group header 4 + one 6-byte entry.
        var bytes = new List<byte>
        {
            0x00, 0x00,             // version
            0x00, 0x01,             // numRecs
            0x00, 0x01,             // numRatios
            0x00, 0x01, 0x01, 0x02, // ratio {charSet 0, x 1, yStart 1, yEnd 2}
            0x00, 0x0C,             // group offset = 12
            0x00, 0x01,             // numEntries
            0x08,                   // startSize
            0x0A,                   // endSize
            0x00, 0x0A, 0x00, 0x0C, 0x00, 0x08, // entry {10, +12, +8}
        };
        return [.. bytes];
    }

    private static FontFaceRef CompanionFace(byte[] post, bool withHintingTables = true)
    {
        var (loca, glyf, _) = GlyfImages.Assemble(
        [
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.CompositeRecord(2),
        ]);
        var tables = new List<(uint, byte[])>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 2))),
            (SubsetCompactPolicyTests.HheaTag, SubsetCompactPolicyTests.Hhea(4)),
            (SubsetCompactPolicyTests.HmtxTag, SubsetCompactPolicyTests.Hmtx(4, 4)),
            (SubsetFontFixture.PostTag, post),
        };
        if (withHintingTables)
        {
            tables.Add((SubsetCompactPolicyTests.LtshTag, SubsetCompactPolicyTests.Ltsh(4)));
            tables.Add((SubsetCompactPolicyTests.HdmxTag, SubsetCompactPolicyTests.Hdmx(4)));
            tables.Add((0x56444D58, Vdmx()));
        }
        return SubsetFontFixture.Face([.. tables]);
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

    private static byte[] FontBytes(SubsetResult result)
    {
        byte[] bytes = new byte[result.Font.OutputLength];
        var status = result.Font.TryCopyTo(bytes, out int written, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(bytes.Length, written);
        return bytes;
    }

    // post 2.0 rebuilds: glyphNameIndex remaps through the map, and the custom names
    // prune to the referenced set renumbered 258+i in original relative order (OQ-SC-2).
    // Request {0,2,3}: output indices come from inputs {0,2,3}.
    [Fact]
    public void CompactRebuildsPost20WithPrunedRenumberedNames()
    {
        var post = Post20(4, [0, 258, 259, 260], ["nA", "nB", "nC"]);
        var face = CompanionFace(post);
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2, 3]), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var font = FontBytes(result!);
        var spans = TableSpans(font);
        byte[] outPost = Table(font, spans, SubsetFontFixture.PostTag);
        // header nine fields verbatim, numGlyphs 3, then indices
        Assert.Equal(0x00020000u, BinaryPrimitives.ReadUInt32BigEndian(outPost));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(outPost.AsSpan(32)));
        // old indices [0, 259, 260] -> referenced customs {nB, nC} renumbered {258, 259}
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(outPost.AsSpan(34)));
        Assert.Equal(258, BinaryPrimitives.ReadUInt16BigEndian(outPost.AsSpan(36)));
        Assert.Equal(259, BinaryPrimitives.ReadUInt16BigEndian(outPost.AsSpan(38)));
        // names segment: length-prefixed nB then nC, nothing else
        Assert.Equal(2, outPost[40]);
        Assert.Equal("nB", Encoding.ASCII.GetString(outPost, 41, 2));
        Assert.Equal(2, outPost[43]);
        Assert.Equal("nC", Encoding.ASCII.GetString(outPost, 44, 2));
        Assert.Equal(46, outPost.Length);
    }

    // Renumber cascade: referenced customs whose renumbered values land on other
    // referenced indices ({259, 260} -> {258, 259}) must rename exactly once per slot,
    // independent of any dictionary enumeration order (OQ-SC-2 single-rewrite rule).
    [Fact]
    public void CompactPostRenameIsOrderIndependentUnderCascade()
    {
        var post = Post20(4, [0, 259, 260, 261], ["p258", "p259", "p260", "p261"]);
        var face = CompanionFace(post, withHintingTables: false);
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2]), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var font = FontBytes(result!);
        var spans = TableSpans(font);
        byte[] outPost = Table(font, spans, SubsetFontFixture.PostTag);
        // output slots come from inputs {0, 1, 2}: indices [0, 259, 260] renumber to
        // [0, 258, 259]; 261 (input 3) leaves the domain and 258 is unreferenced
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(outPost.AsSpan(32)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(outPost.AsSpan(34)));
        Assert.Equal(258, BinaryPrimitives.ReadUInt16BigEndian(outPost.AsSpan(36)));
        Assert.Equal(259, BinaryPrimitives.ReadUInt16BigEndian(outPost.AsSpan(38)));
        // the pool keeps the referenced names in their original relative order
        Assert.Equal(4, outPost[40]);
        Assert.Equal("p259", Encoding.ASCII.GetString(outPost, 41, 4));
        Assert.Equal(4, outPost[45]);
        Assert.Equal("p260", Encoding.ASCII.GetString(outPost, 46, 4));
        Assert.Equal(50, outPost.Length);
    }

    // A post 2.0 whose glyphNameIndex column does not cover the maxp domain has no
    // well-defined per-output-slot source: the rebuild refuses gracefully instead of
    // indexing past the column (the maxp-bound read the metrics contexts perform).
    [Fact]
    public void CompactPostShortOfMaxpDomainRefuses()
    {
        var post = Post20(3, [0, 1, 2], []);
        var face = CompanionFace(post);
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2, 3]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Null(result);
    }

    // A composite whose component output GID exceeds a byte must still patch: the
    // glyphIndex write is byte splitting, never a checked narrowing (identity map over
    // 261 glyphs, component 260 stays 260).
    [Fact]
    public void CompactCompositePatchWritesHighComponentGids()
    {
        const int glyphs = 261;
        var records = new List<byte[]>();
        for (int i = 0; i < glyphs; i++)
            records.Add(i == 0 ? GlyfImages.CompositeRecord(glyphs - 1)
                : GlyfImages.NoContourGlyphRecord());
        var (loca, glyf, _) = GlyfImages.Assemble([.. records]);
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(glyphs)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 1))),
            (SubsetCompactPolicyTests.HheaTag, SubsetCompactPolicyTests.Hhea(glyphs)),
            (SubsetCompactPolicyTests.HmtxTag, SubsetCompactPolicyTests.Hmtx(glyphs,
                glyphs)),
            (SubsetFontFixture.PostTag, new byte[32]));
        var gids = new List<ushort>();
        for (ushort i = 0; i < glyphs; i++) gids.Add(i);
        var status = SubsetOperation.TryPrepare(Request(face, gids), out var result,
            out _, out var diagnostic);
        if (status != ReadStatus.Done)
            throw new Xunit.Sdk.XunitException(
                $"status={status} code={diagnostic.Code} detail={diagnostic.Detail}");
        Assert.NotNull(result);
        var font = FontBytes(result!);
        var spans = TableSpans(font);
        byte[] glyfOut = Table(font, spans, SubsetFontFixture.GlyfTag);
        byte[] locaOut = Table(font, spans, SubsetFontFixture.LocaTag);
        uint start = BinaryPrimitives.ReadUInt32BigEndian(locaOut.AsSpan(0));
        uint end = BinaryPrimitives.ReadUInt32BigEndian(locaOut.AsSpan(4));
        // the composite (slot 0) keeps its component pointing at output 260
        Assert.Equal(glyphs - 1,
            BinaryPrimitives.ReadUInt16BigEndian(glyfOut.AsSpan((int)start + 12)));
        Assert.True(end > start, "composite slot empty");
    }

    // post 1.0 and 3.0 carry no per-glyph data: both stay raw under compact.
    [Theory]
    [InlineData(0x00010000u)]
    [InlineData(0x00030000u)]
    public void CompactKeepsNamelessPostVersionsRaw(uint version)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), version);
        var face = CompanionFace(bytes, withHintingTables: false);
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2, 3]), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var font = FontBytes(result!);
        var spans = TableSpans(font);
        Assert.True(Table(font, spans, SubsetFontFixture.PostTag).AsSpan()
            .SequenceEqual(bytes), "a nameless post version changed bytes");
    }

    // hdmx and LTSH reorder their per-GID arrays through the map; VDMX re-emits with
    // semantically identical ratio/group/entry content on canonical offsets.
    [Fact]
    public void CompactRebuildsDeviceAndThresholdTables()
    {
        var face = CompanionFace(Post20(4, [0, 1, 2, 3], []));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2, 3]), out var result,
            out _, out var diag);
        if (status != ReadStatus.Done)
            throw new Xunit.Sdk.XunitException(
                $"status={status} code={diag.Code} tag=0x{diag.Location.TableTag:X8} " +
                $"field={diag.Location.Field} detail={diag.Detail} expected={diag.Expected}");
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var font = FontBytes(result!);
        var spans = TableSpans(font);

        // hdmx: version 0, one record, canonical stride (3+2+3)&~3 = 8; the widths are
        // the input widths reordered through [0, 2, 3]
        byte[] hdmx = Table(font, spans, SubsetCompactPolicyTests.HdmxTag);
        Assert.Equal(16, hdmx.Length); // header 8 + 1 record of stride 8
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(hdmx));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(hdmx.AsSpan(2)));
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32BigEndian(hdmx.AsSpan(4)));
        Assert.Equal(12, hdmx[8]);   // pixel size kept
        Assert.Equal(0, hdmx[9]);    // max width recomputed over reordered widths
        Assert.Equal([0, 0, 0], hdmx[10..13].ToArray());
        Assert.Equal([0, 0], hdmx[14..16].ToArray()); // zero padding to the stride

        // LTSH: thresholds reorder through [0, 2, 3]; the fixture thresholds were 0s
        byte[] ltsh = Table(font, spans, SubsetCompactPolicyTests.LtshTag);
        Assert.Equal(4 + 3, ltsh.Length);
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(ltsh.AsSpan(2)));

        // VDMX: same ratio, same group start/end and entry, offset re-pinned to 12
        byte[] vdmx = Table(font, spans, 0x56444D58);
        Assert.Equal(Vdmx(), vdmx);
    }

    // DropHinting under compact deletes LTSH (mode-independent branch) while hdmx and
    // VDMX still rebuild, and the glyf instructions strip through the same encode path
    // the retain splice uses.
    [Fact]
    public void CompactDropHintingDeletesLtshOnly()
    {
        var glyf = new List<byte>();
        var offsets = new List<uint> { 0 };
        glyf.AddRange(GlyfImages.NoContourGlyphRecord());
        offsets.Add((uint)glyf.Count);
        glyf.AddRange(SubsetFontFixture.InstructedSimpleGlyph());
        offsets.Add((uint)glyf.Count);
        glyf.AddRange(GlyfImages.CompositeRecord(1));
        offsets.Add((uint)glyf.Count);
        var loca = new byte[4 * offsets.Count];
        for (int i = 0; i < offsets.Count; i++)
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(4 * i), offsets[i]);
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(3)),
            (SubsetFontFixture.GlyfTag, [.. glyf]),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 0))),
            (SubsetCompactPolicyTests.HheaTag, SubsetCompactPolicyTests.Hhea(3)),
            (SubsetCompactPolicyTests.HmtxTag, SubsetCompactPolicyTests.Hmtx(3, 3)),
            (SubsetCompactPolicyTests.LtshTag, SubsetCompactPolicyTests.Ltsh(3)),
            (SubsetCompactPolicyTests.HdmxTag, SubsetCompactPolicyTests.Hdmx(3)));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2], dropHinting: true),
            out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var font = FontBytes(result!);
        var spans = TableSpans(font);
        Assert.DoesNotContain(SubsetCompactPolicyTests.LtshTag, spans.Keys);
        Assert.Contains(SubsetCompactPolicyTests.HdmxTag, spans.Keys);
        var dropped = Assert.Single(report!.DroppedTables);
        Assert.Equal(SubsetCompactPolicyTests.LtshTag, dropped);
        // the instructed simple glyph (output slot 1) lost its instruction payload but
        // keeps its outline: 17 bytes (header 10 + endPt 2 + zero instructionLength 2
        // + flag 1 + two 1-byte deltas)
        byte[] glyfOut = Table(font, spans, SubsetFontFixture.GlyfTag);
        byte[] locaOut = Table(font, spans, SubsetFontFixture.LocaTag);
        uint start = BinaryPrimitives.ReadUInt32BigEndian(locaOut.AsSpan(4));
        uint end = BinaryPrimitives.ReadUInt32BigEndian(locaOut.AsSpan(8));
        Assert.Equal(17u, end - start);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(glyfOut.AsSpan((int)start + 10)));
    }
}
