// gvar compact-rebuild tests (SV card §2.1, commit ② acceptance): the per-glyph
// tuple blocks re-slot over the frozen map byte-for-byte — the identity tier on
// a canonical full select, per-slot block fidelity under a pruning select, and
// the budget refusal face. The gvar wire here is test-side independent
// encoding: the block interiors (tuple headers, peak/shared references, delta
// runs) are block-internal data that must move verbatim.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetVfGvarTests
{
    private const ushort AxisCount = 1, NumGlyphs = 3;

    private static byte[] GvarBlock(byte firstDelta, byte secondDelta) =>
        [0x00, 0x01,              // tupleVariationCount: 1 tuple, no shared points
         0x00, 0x08,              // dataOffset: the serialized run follows the header
         0x00, 0x02,              // tuple header: dataSize 2
         0x00, 0x00,              // tuple header: shared tuple index 0
         firstDelta, secondDelta];

    /// <summary>A canonical gvar 1.0: header, short-form offsets (glyph 0's block
    /// absent), one shared tuple, then two 10-byte blocks in glyph order.</summary>
    private static byte[] Gvar(byte firstDelta, byte secondDelta) =>
        GvarFromBlocks(Array.Empty<byte>(), GvarBlock(firstDelta, secondDelta),
            GvarBlock((byte)(firstDelta + 1), (byte)(secondDelta + 1)));

    private static byte[] GvarFromBlocks(params byte[][] blocks)
    {
        int offsetsAt = 20;
        int sharedTuplesAt = offsetsAt + (NumGlyphs + 1) * 2;
        int gvdArrayAt = sharedTuplesAt + 2 * AxisCount; // one shared tuple
        int total = gvdArrayAt + blocks.Sum(b => b.Length);
        var bytes = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), AxisCount);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 1); // sharedTupleCount
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)sharedTuplesAt);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), NumGlyphs);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), 0); // short form
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)gvdArrayAt);
        int offset = 0;
        for (int i = 0; i < blocks.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                bytes.AsSpan(offsetsAt + 2 * i), (ushort)(offset / 2));
            blocks[i].CopyTo(bytes.AsSpan(gvdArrayAt + offset));
            offset += blocks[i].Length;
        }
        BinaryPrimitives.WriteUInt16BigEndian(
            bytes.AsSpan(offsetsAt + 2 * blocks.Length), (ushort)(offset / 2));
        // the shared tuple: one axis, peak coordinate 0.0
        return bytes;
    }

    private static FontFaceRef Face(byte[] gvar) => SubsetFontFixture.Face(
        (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
        (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
        (SubsetFontFixture.GlyfTag, [.. GlyfImages.NoContourGlyphRecord(),
            .. GlyfImages.NoContourGlyphRecord(), .. GlyfImages.NoContourGlyphRecord()]),
        (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 12, 24, 36])),
        (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapWithMacRoman((0x41, 1))),
        (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar(AxisCount)),
        (SubsetFontFixture.GvarTag, gvar));

    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids) =>
        new()
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
            Mode = SubsetMode.CompactRemap,
            Options = new SubsetOptions { NoLayout = true }
        };

    private static byte[] Output(SubsetResult result)
    {
        byte[] output = new byte[result.Font.OutputLength];
        _ = result.Font.TryCopyTo(output, out _, out _);
        return output;
    }

    private static byte[] TableOf(byte[] sfnt, uint tag, out bool present)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            if (BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record)) != tag) continue;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 12));
            present = true;
            return sfnt[offset..(offset + length)];
        }
        present = false;
        return [];
    }

    // Full select: the frozen map is the identity and the input layout is already
    // canonical, so the re-spliced gvar is the byte-identity tier.
    [Fact]
    public void FullSelectReSpliceIsByteIdentical()
    {
        byte[] gvarIn = Gvar(0x10, 0x20);
        var status = SubsetOperation.TryPrepare(Request(Face(gvarIn),
            [(ushort)0, (ushort)1, (ushort)2]), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] gvarOut = TableOf(Output(result!), SubsetFontFixture.GvarTag,
            out bool present);
        Assert.True(present);
        Assert.Equal(gvarIn, gvarOut);
    }

    // Pruning select {0, 2}: output slot 1 carries input glyph 2's block
    // byte-for-byte, glyph 0's absent block stays absent, and the header keeps
    // the source's axisCount/sharedTupleCount/flags with the output glyph count.
    [Fact]
    public void PruningSelectSplicesBlocksByteForByte()
    {
        byte[] gvarIn = Gvar(0x10, 0x20);
        var status = SubsetOperation.TryPrepare(Request(Face(gvarIn),
            [(ushort)0, (ushort)2]), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] gvarOut = TableOf(Output(result!), SubsetFontFixture.GvarTag,
            out bool present);
        Assert.True(present);

        Assert.Equal(0x00010000u, BinaryPrimitives.ReadUInt32BigEndian(gvarOut.AsSpan(0)));
        Assert.Equal(AxisCount, BinaryPrimitives.ReadUInt16BigEndian(gvarOut.AsSpan(4)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(gvarOut.AsSpan(6))); // shared tuples
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(gvarOut.AsSpan(12))); // glyphCount
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(gvarOut.AsSpan(14))); // flags kept
        // offsets: slot 0 absent, slot 1 carries the 10-byte block
        int offsetsAt = 20;
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(gvarOut.AsSpan(offsetsAt)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(gvarOut.AsSpan(offsetsAt + 2)));
        Assert.Equal(5, BinaryPrimitives.ReadUInt16BigEndian(gvarOut.AsSpan(offsetsAt + 4)));
        int gvdArrayAt = offsetsAt + 3 * 2 + 2 * AxisCount;
        byte[] inputGlyph2Block = GvarBlock(0x11, 0x21);
        Assert.Equal(inputGlyph2Block, gvarOut[gvdArrayAt..(gvdArrayAt + 10)]);
        Assert.Equal(gvdArrayAt + 10, gvarOut.Length);
    }

    // The budget face: an output budget below the gvar emission refuses with no
    // partial product (the compile plan's Work/Output admission).
    [Fact]
    public void OutputBudgetBelowGvarEmissionRefuses()
    {
        byte[] gvarIn = Gvar(0x10, 0x20);
        var limits = new BuildLimits { MaxOutputBytes = 120 };
        var status = SubsetOperation.TryPrepare(Request(Face(gvarIn),
            [(ushort)0, (ushort)1, (ushort)2]), out var result, out var report,
            out var diagnostic, limits);
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Null(result);
        Assert.NotNull(report);
    }

    // The retain path keeps the raw copy semantics: the same face under
    // RetainGids moves gvar byte-identical (the relaxed column's contract).
    [Fact]
    public void RetainPathKeepsGvarRaw()
    {
        byte[] gvarIn = Gvar(0x10, 0x20);
        var face = Face(gvarIn);
        var request = new SubsetRequest
        {
            Face = face,
            Root = new SubsetGlyphRoot
            {
                Kind = SubsetRootKind.ExplicitGlyphSet,
                Gids = [(ushort)0, (ushort)2]
            },
            Mode = SubsetMode.RetainGids,
            Options = new SubsetOptions { NoLayout = true }
        };
        var status = SubsetOperation.TryPrepare(request, out var result, out _,
            out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] gvarOut = TableOf(Output(result!), SubsetFontFixture.GvarTag,
            out bool present);
        Assert.True(present);
        Assert.Equal(gvarIn, gvarOut);
    }
}
