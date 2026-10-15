// HVAR/VVAR compact-rebuild tests (SV card §2.3, commit ③ acceptance): explicit
// DeltaSetIndexMap entries re-resolve per output gid in the input domain (the
// last-entry fallback included), the NULL advance map's implicit reading
// materializes (OQ-SV-6), the store re-emits byte-identical, and the VVAR twin
// keeps its tsb/bsb/vOrg NULL slots (OQ-T5-04-4). The HVAR/VVAR wire here is
// test-side independent encoding.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetVfHvarTests
{
    private const ushort NumGlyphs = 3;

    private static byte[] AdvanceMap(params ushort[] inners)
    {
        // format 0, entryFormat 0x00 (1-byte entries, innerBits 1), inners in order.
        var bytes = new byte[4 + inners.Length];
        bytes[0] = 0; // format
        bytes[1] = 0; // entryFormat
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)inners.Length);
        for (int i = 0; i < inners.Length; i++) bytes[4 + i] = (byte)inners[i];
        return bytes;
    }

    /// <summary>HVAR 1.0 over one region axis and one VarData whose item count
    /// covers the glyph domain; an optional explicit advance map follows the
    /// store (the canonical order). NULL lsb/rsb throughout.</summary>
    private static byte[] Hvar(byte[]? advanceMap, ushort numGlyphs = NumGlyphs)
    {
        int ivs = 20, rl = ivs + 12, vd = rl + 4 + 6;
        int ivsLength = (vd + 6 + 2 + numGlyphs) - ivs;
        int mapAt = advanceMap is null ? 0 : ivs + ivsLength;
        var bytes = new byte[(advanceMap is null ? ivs + ivsLength : mapAt + advanceMap.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)ivs);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)mapAt); // adv map
        // lsb/rsb stay NULL (offsets 12/16 zero)
        WriteStore(bytes, ivs, rl, vd, numGlyphs);
        advanceMap?.CopyTo(bytes.AsSpan(mapAt));
        return bytes;
    }

    /// <summary>VVAR 1.0: the HVAR twin's 24-byte header; an optional explicit
    /// advanceHeight map; tsb/bsb/vOrg always NULL.</summary>
    private static byte[] Vvar(byte[]? advanceHeightMap, ushort numGlyphs = NumGlyphs)
    {
        int ivs = 24, rl = ivs + 12, vd = rl + 4 + 6;
        int ivsLength = (vd + 6 + 2 + numGlyphs) - ivs;
        int mapAt = advanceHeightMap is null ? 0 : ivs + ivsLength;
        var bytes = new byte[(advanceHeightMap is null ? ivs + ivsLength : mapAt + advanceHeightMap.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)ivs);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)mapAt);
        // tsb/bsb/vOrg stay NULL (offsets 12/16/20 zero)
        WriteStore(bytes, ivs, rl, vd, numGlyphs);
        advanceHeightMap?.CopyTo(bytes.AsSpan(mapAt));
        return bytes;
    }

    private static void WriteStore(byte[] bytes, int ivs, int rl, int vd,
        ushort numGlyphs)
    {
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(ivs), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(ivs + 2), (uint)(rl - ivs));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(ivs + 6), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(ivs + 8), (uint)(vd - ivs));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(rl), 1);       // axisCount
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(rl + 2), 1);   // regionCount
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(vd), numGlyphs);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(vd + 2), 0);   // wordDeltaCount
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(vd + 4), 1);   // regionIndexCount
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(vd + 6), 0);   // regionIndexes[0]
    }

    private static FontFaceRef Face(uint hvarOrVvarTag, byte[] body) => SubsetFontFixture.Face(
        (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
        (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
        (SubsetFontFixture.GlyfTag, [.. GlyfImages.NoContourGlyphRecord(),
            .. GlyfImages.NoContourGlyphRecord(), .. GlyfImages.NoContourGlyphRecord()]),
        (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 12, 24, 36])),
        (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapWithMacRoman((0x41, 1))),
        (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar(1)),
        (hvarOrVvarTag, body));

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

    private static (ushort InnerBits, int EntrySize, int MapCount, ushort[] Inners,
        byte[] StoreBytes) ReadMapAndStore(byte[] hvar)
    {
        int ivsAt = (int)BinaryPrimitives.ReadUInt32BigEndian(hvar.AsSpan(4));
        int mapAt = (int)BinaryPrimitives.ReadUInt32BigEndian(hvar.AsSpan(8));
        Assert.True(mapAt > 0, "advance map must be present");
        byte entryFormat = hvar[mapAt + 1];
        int mapCount = BinaryPrimitives.ReadUInt16BigEndian(hvar.AsSpan(mapAt + 2));
        int innerBits = (entryFormat & 0x0F) + 1;
        int entrySize = ((entryFormat & 0x30) >> 4) + 1;
        var inners = new ushort[mapCount];
        for (int i = 0; i < mapCount; i++)
        {
            int at = mapAt + 4 + i * entrySize;
            uint packed = 0;
            for (int b = 0; b < entrySize; b++) packed = (packed << 8) | hvar[at + b];
            inners[i] = (ushort)(packed & ((1u << innerBits) - 1));
        }
        // the store's bytes: from its offset to the map (canonical order)
        int storeLength = (mapAt - ivsAt);
        var store = hvar[ivsAt..(ivsAt + storeLength)];
        return ((ushort)innerBits, entrySize, mapCount, inners, store);
    }

    // Explicit advance map with a fallback domain (mapCount 2 < numGlyphs 3):
    // output gid 1 resolves input gid 2 through the last-entry fallback in the
    // INPUT domain first, so the output map's entries are [resolve(0), resolve(2)]
    // = [(0,0), (0,1)] with the source entryFormat preserved, and the store's
    // bytes re-emit identically.
    [Fact]
    public void PruningSelectRemapsExplicitAdvanceMapWithFallback()
    {
        byte[] hvarIn = Hvar(AdvanceMap(0, 1));
        var status = SubsetOperation.TryPrepare(Request(
            Face(SubsetFontFixture.HvarTag, hvarIn), [(ushort)0, (ushort)2]),
            out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] hvarOut = TableOf(Output(result!), SubsetFontFixture.HvarTag,
            out bool present);
        Assert.True(present);
        var (innerBits, entrySize, mapCount, inners, store) = ReadMapAndStore(hvarOut);
        Assert.Equal(1, innerBits);
        Assert.Equal(1, entrySize);
        Assert.Equal(2, mapCount);
        Assert.Equal(new ushort[] { 0, 1 }, inners);
        // the input store region re-emits byte-identical
        int ivsIn = (int)BinaryPrimitives.ReadUInt32BigEndian(hvarIn.AsSpan(4));
        Assert.Equal(hvarIn.AsSpan(ivsIn, hvarIn.Length - 8 - ivsIn).ToArray(),
            store.AsSpan(0, hvarIn.Length - 8 - ivsIn).ToArray());
    }

    // The NULL advance map materializes (OQ-SV-6): output gid o's implicit
    // (0, o) reading would dereference the wrong input row, so the output map
    // pins entries[o] = (0, outputToInput[o]) with the minimal representable
    // entryFormat derived mechanically.
    [Fact]
    public void NullAdvanceMapMaterializesImplicitReading()
    {
        byte[] hvarIn = Hvar(null);
        var status = SubsetOperation.TryPrepare(Request(
            Face(SubsetFontFixture.HvarTag, hvarIn), [(ushort)0, (ushort)2]),
            out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] hvarOut = TableOf(Output(result!), SubsetFontFixture.HvarTag,
            out bool present);
        Assert.True(present);
        var (innerBits, entrySize, mapCount, inners, _) = ReadMapAndStore(hvarOut);
        Assert.Equal(2, innerBits);   // maxInner = 2 → innerBits 2
        Assert.Equal(1, entrySize);   // packed ≤ 2 → 1-byte entries
        Assert.Equal(2, mapCount);
        Assert.Equal(new ushort[] { 0, 2 }, inners); // (0, outputToInput[o])
    }

    // Full select over an explicit canonical map: the re-rooted model compiles
    // to the byte-identity tier.
    [Fact]
    public void FullSelectOnExplicitCanonicalMapIsByteIdentical()
    {
        byte[] hvarIn = Hvar(AdvanceMap(0, 1, 2));
        var status = SubsetOperation.TryPrepare(Request(
            Face(SubsetFontFixture.HvarTag, hvarIn),
            [(ushort)0, (ushort)1, (ushort)2]), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] hvarOut = TableOf(Output(result!), SubsetFontFixture.HvarTag,
            out bool present);
        Assert.True(present);
        Assert.Equal(hvarIn, hvarOut);
    }

    // The VVAR twin: the explicit advanceHeight map re-roots like HVAR's, and
    // the tsb/bsb/vOrg slots stay NULL (no-variation semantics — materializing
    // them would invent data).
    [Fact]
    public void VvarRemapsAdvanceHeightAndKeepsNullSides()
    {
        byte[] vvarIn = Vvar(AdvanceMap(0, 1));
        var status = SubsetOperation.TryPrepare(Request(
            Face(SubsetFontFixture.VvarTag, vvarIn), [(ushort)0, (ushort)2]),
            out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] vvarOut = TableOf(Output(result!), SubsetFontFixture.VvarTag,
            out bool present);
        Assert.True(present);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(vvarOut.AsSpan(12))); // tsb NULL
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(vvarOut.AsSpan(16))); // bsb NULL
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(vvarOut.AsSpan(20))); // vOrg NULL
        var (_, _, mapCount, inners, _) = ReadMapAndStore(vvarOut);
        Assert.Equal(2, mapCount);
        Assert.Equal(new ushort[] { 0, 1 }, inners);
    }
}
