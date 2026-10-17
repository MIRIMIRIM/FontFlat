// CFF2 compact-rebuild and cff2-idx identity tests (SV card §2.5/§3, commit ④
// acceptance): the CharStrings INDEX items and the per-glyph FD index re-slot
// over the frozen map as byte-faithful moves, the TopDICT offset keys re-answer
// over the new layout, the map artifact carries the cff2-idx kind, and the
// retain path keeps the raw copy. The CFF2/VORG wire here is test-side
// independent encoding (single-FD face, no store — the source-serif witness's
// full complexity stays with the SV gate).
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCff2SubsetTests
{
    private const ushort NumGlyphs = 3;

    /// <summary>A minimal but legal CFF2 2.0 table: header(5), TopDICT with the
    /// two required keys (CharStrings 0x11, FDArray 12 24), an empty GlobalSubr
    /// INDEX, a three-item CharString INDEX of endchar-only charstrings, and a
    /// one-FontDICT FDArray carrying the `0 0` empty-private form (no FDSelect,
    /// no store). Layout arithmetic is test-side, byte-level.</summary>
    private static byte[] Cff2()
    {
        // CharStrings INDEX: 3 items of the single byte 0x0e (endchar).
        var charStrings = new byte[4 + 1 + 4 + 3];
        BinaryPrimitives.WriteUInt32BigEndian(charStrings.AsSpan(0), 3);
        charStrings[4] = 1; // offSize
        for (int i = 0; i <= 3; i++) charStrings[5 + i] = (byte)(1 + i);
        charStrings.AsSpan(9, 3).Fill(0x0e);

        // FDArray INDEX: one FontDICT `0 0` (139 encodes 0), key 0x12 escape.
        var fdArray = new byte[] { 0, 0, 0, 1, 1, 1, 4, 139, 139, 18 };

        // Layout: header 5 | TopDICT 5 | GlobalSubrs 4 | CharStrings | FDArray.
        int topDictAt = 5, globalSubrsAt = 10, charStringsAt = 14,
            fdArrayAt = charStringsAt + charStrings.Length;
        var bytes = new byte[fdArrayAt + fdArray.Length];
        bytes[0] = 2;             // majorVersion
        bytes[1] = 0;             // minorVersion
        bytes[2] = 5;             // headerSize
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(3), 5); // topDictLength
        int at = topDictAt;
        bytes[at++] = unchecked((byte)(139 + charStringsAt)); // operand: CharStrings offset
        bytes[at++] = 0x11;                                    // CharStrings key
        bytes[at++] = unchecked((byte)(139 + fdArrayAt));      // operand: FDArray offset
        bytes[at++] = 12;                                      // escape
        bytes[at++] = 0x24;                                    // FDArray key
        bytes.AsSpan(globalSubrsAt, 4).Clear();               // empty GlobalSubr INDEX: u32 count 0
        charStrings.CopyTo(bytes.AsSpan(charStringsAt));
        fdArray.CopyTo(bytes.AsSpan(fdArrayAt));
        return bytes;
    }

    private static byte[] Vorg(params (ushort Gid, ushort OriginY)[] records)
    {
        // version 1.0 (u32), defaultVertOriginY (i16), then the counted records.
        var bytes = new byte[8 + 4 * records.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(4), 880);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)records.Length);
        for (int i = 0; i < records.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8 + 4 * i),
                records[i].Gid);
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(10 + 4 * i),
                (short)records[i].OriginY);
        }
        return bytes;
    }

    private static FontFaceRef Face() => SubsetFontFixture.Face(
        (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
        (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp05(NumGlyphs)),
        (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapWithMacRoman((0x41, 1))),
        (SubsetFontFixture.Cff2Tag, Cff2()),
        (SubsetFontFixture.VorgTag, Vorg((0, 700), (1, 750), (2, 800))));

    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        SubsetMode mode) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = mode,
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

    // Pruning select {0, 2} on the single-FD CFF2 face: the CharStrings INDEX
    // carries items [cs0, cs2] (both endchar bytes here), the FDArray stays
    // byte-identical, the TopDICT re-answers its offset keys over the new
    // layout, and the map artifact carries the cff2-idx kind with the frozen
    // contract (0 → 0, injective, ascending input GID).
    [Fact]
    public void PruningSelectReSlotsCharStringsAndKeepsFdArray()
    {
        var status = SubsetOperation.TryPrepare(Request(Face(),
            [(ushort)0, (ushort)2], SubsetMode.CompactRemap), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] cff2Out = TableOf(Output(result!), SubsetFontFixture.Cff2Tag,
            out bool present);
        Assert.True(present);

        // header: major 2, minor 0, headerSize 5; walk TopDICT's offset keys.
        Assert.Equal(2, cff2Out[0]);
        Assert.Equal(0, cff2Out[1]);
        Assert.Equal(5, cff2Out[2]);
        int charStringsOffset = cff2Out[5] - 139;
        int at = 5;
        bool charStringsKeySeen = false, fdArrayKeySeen = false;
        int fdArrayOffset = -1;
        while (at < 5 + BinaryPrimitives.ReadUInt16BigEndian(cff2Out.AsSpan(3)))
        {
            int operand = cff2Out[at] - 139;
            at++;
            if (cff2Out[at] == 0x11)
            {
                charStringsOffset = operand;
                charStringsKeySeen = true;
            }
            else if (cff2Out[at] == 12)
            {
                at++;
                Assert.Equal(0x24, cff2Out[at]);
                fdArrayOffset = operand;
                fdArrayKeySeen = true;
            }
            else
            {
                Assert.Fail("unexpected TopDICT key");
            }
            at++;
        }
        Assert.True(charStringsKeySeen && fdArrayKeySeen);
        // the CharStrings INDEX at its key: two endchar items
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(
            cff2Out.AsSpan(charStringsOffset)));
        Assert.Equal(1, cff2Out[charStringsOffset + 4]); // offSize
        // the FDArray at its key: byte-identical to the input's INDEX
        byte[] fdArrayIn = Cff2()[^10..];
        Assert.Equal(fdArrayIn, cff2Out[fdArrayOffset..(fdArrayOffset + 10)]);

        // the frozen map: cff2-idx kind, ascending input GID, 0 → 0, injective
        var artifact = result!.Map;
        Assert.NotNull(artifact);
        Assert.Equal(2, artifact!.OutputGlyphCount);
        Assert.Equal("cff2-idx:0", artifact.Entries[0].StableIdentity);
        Assert.Equal("cff2-idx:2", artifact.Entries[1].StableIdentity);
        Assert.Equal(0u, artifact.Entries[0].InputGid);
        Assert.Equal(2u, artifact.Entries[1].InputGid);
    }

    // VORG rides the same remap on a CFF2 face (OQ-SV-7): glyph 2's record
    // re-slots to output gid 1, glyph 1's record goes, and the records stay
    // ascending by output gid.
    [Fact]
    public void VorgRidesTheRemapOnCff2Face()
    {
        var status = SubsetOperation.TryPrepare(Request(Face(),
            [(ushort)0, (ushort)2], SubsetMode.CompactRemap), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] vorgOut = TableOf(Output(result!), SubsetFontFixture.VorgTag,
            out bool present);
        Assert.True(present);
        Assert.Equal(880, BinaryPrimitives.ReadInt16BigEndian(vorgOut.AsSpan(4)));
        // glyph 0's record survives (0 -> 0) with glyph 2's re-slot to output gid
        // 1; glyph 1's record goes with the dropped glyph.
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(vorgOut.AsSpan(6)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(vorgOut.AsSpan(8)));
        Assert.Equal(700, BinaryPrimitives.ReadInt16BigEndian(vorgOut.AsSpan(10)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(vorgOut.AsSpan(12)));
        Assert.Equal(800, BinaryPrimitives.ReadInt16BigEndian(vorgOut.AsSpan(14)));
    }

    // The retain path keeps CFF2 and VORG raw (the relaxed column's contract).
    [Fact]
    public void RetainKeepsCff2AndVorgRaw()
    {
        byte[] face = SubsetFontFixture.Sfnt(false,
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp05(NumGlyphs)),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapWithMacRoman((0x41, 1))),
            (SubsetFontFixture.Cff2Tag, Cff2()),
            (SubsetFontFixture.VorgTag, Vorg((0, 700), (1, 750), (2, 800))));
        var faceRef = SubsetFontFixture.Face(false,
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp05(NumGlyphs)),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapWithMacRoman((0x41, 1))),
            (SubsetFontFixture.Cff2Tag, Cff2()),
            (SubsetFontFixture.VorgTag, Vorg((0, 700), (1, 750), (2, 800))));
        var status = SubsetOperation.TryPrepare(Request(faceRef,
            [(ushort)0, (ushort)2], SubsetMode.RetainGids), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        byte[] output = Output(result!);
        foreach (uint tag in new[] { SubsetFontFixture.Cff2Tag,
            SubsetFontFixture.VorgTag })
        {
            byte[] input = TableOf(face, tag, out bool inPresent);
            byte[] outBytes = TableOf(output, tag, out bool outPresent);
            Assert.True(inPresent && outPresent);
            Assert.Equal(input, outBytes);
        }
    }

    // ---- the blend-stem face (the S4 walker's CFF2 blend pin) ------------------

    /// <summary>One DICT integer in the minimal form (single byte over [-107, 107],
    /// the two-byte positive window, else the 28 shortint).</summary>
    private static void DictInt(List<byte> dict, int value)
    {
        if (value is >= -107 and <= 107)
        {
            dict.Add((byte)(value + 139));
        }
        else if (value is >= 108 and <= 1131)
        {
            int remainder = value - 108;
            dict.Add((byte)(247 + (remainder >> 8)));
            dict.Add((byte)(remainder & 0xFF));
        }
        else
        {
            dict.Add(28);
            dict.Add((byte)((value >> 8) & 0xFF));
            dict.Add((byte)(value & 0xFF));
        }
    }

    /// <summary>The blend-stem charstring (TN5171 §5's shape every real CFF2
    /// variable face writes): two hstem/vstem declarations whose operands arrive
    /// as blend results — <c>n defaults, n deltas, n</c> then blend — followed by
    /// the hintmask. Two blends keep 2+2 stem values, so the mask width is one
    /// byte, and the mask byte is 0xFF so a walker that wiped the stack at blend
    /// (hints 0 → width 0) reads the mask byte as the 5-byte fixed push and
    /// derails mid-token, while a walker with a wrong region count (width 2)
    /// skips past the program: both broken models refuse where the correct one
    /// completes by fall-off (TN5171 charstrings end without a terminator).</summary>
    private static byte[] BlendStemCharstring()
    {
        var program = new List<byte>();
        for (int round = 0; round < 2; round++)
        {
            for (int i = 0; i < 5; i++) program.Add(140); // five defaults
            for (int i = 0; i < 5; i++) program.Add(140); // five deltas
            program.Add(144);                             // the result count 5
            program.Add(16);                              // blend
            program.Add(round == 0 ? (byte)1 : (byte)3);  // hstem / vstem
        }
        program.Add(19);  // hintmask: hints 4 → skip 1
        program.Add(0xFF); // the one mask byte
        return [.. program];
    }

    /// <summary>A single-FD CFF2 table with a one-region variation store (the
    /// store<->fvar signature coupling satisfied by the fvar table in the face)
    /// whose three charstrings are the blend-stem program: the shape the S4
    /// walker's first blend model died of. Wire layout is test-side, byte-level.</summary>
    private static byte[] Cff2WithBlendStems(out byte[] charStringsIndex)
    {
        byte[] charstring = BlendStemCharstring();

        // CharStrings INDEX: 3 items of the 28-byte blend-stem program.
        int itemTotal = charstring.Length * 3;
        var charStrings = new byte[4 + 1 + 4 + itemTotal];
        BinaryPrimitives.WriteUInt32BigEndian(charStrings.AsSpan(0), 3);
        charStrings[4] = 1; // offSize
        for (int i = 0; i <= 3; i++)
            charStrings[5 + i] = (byte)(1 + i * charstring.Length);
        for (int i = 0; i < 3; i++)
            charstring.CopyTo(charStrings.AsSpan(9 + i * charstring.Length));
        charStringsIndex = charStrings;

        // FDArray INDEX: one FontDICT `0 0` (139 encodes 0), key 0x12 escape —
        // the empty-private form, no local subrs, so nothing is prunable.
        var fdArray = new byte[] { 0, 0, 0, 1, 1, 1, 4, 139, 139, 18 };

        // VariationStore: length(u16) + format 1 + one-region region list +
        // one ItemVariationData of itemCount 0 over region 0 (the real-corpus
        // shape). k = RegionIndexCount = 1 is what the blend arithmetic reads.
        var regionList = new byte[4 + 6];
        BinaryPrimitives.WriteUInt16BigEndian(regionList.AsSpan(0), 1); // axisCount
        BinaryPrimitives.WriteUInt16BigEndian(regionList.AsSpan(2), 1); // regionCount
        regionList.AsSpan(4, 6).Clear(); // the (0,0,0) region axis triple
        var varData = new byte[6 + 2];
        BinaryPrimitives.WriteUInt16BigEndian(varData.AsSpan(0), 0); // itemCount
        BinaryPrimitives.WriteUInt16BigEndian(varData.AsSpan(2), 0); // wordDeltaCount
        BinaryPrimitives.WriteUInt16BigEndian(varData.AsSpan(4), 1); // regionIndexCount
        BinaryPrimitives.WriteUInt16BigEndian(varData.AsSpan(6), 0); // regionIndexes[0]
        int regionListOffset = 12;                    // right after the offsets array
        int varDataOffset = regionListOffset + regionList.Length;
        var store = new byte[8 + 4 + regionList.Length + varData.Length];
        BinaryPrimitives.WriteUInt16BigEndian(store.AsSpan(0), 1);   // format
        BinaryPrimitives.WriteUInt32BigEndian(store.AsSpan(2),
            (uint)regionListOffset);
        BinaryPrimitives.WriteUInt16BigEndian(store.AsSpan(6), 1);   // dataCount
        BinaryPrimitives.WriteUInt32BigEndian(store.AsSpan(8), (uint)varDataOffset);
        regionList.CopyTo(store.AsSpan(regionListOffset));
        varData.CopyTo(store.AsSpan(varDataOffset));
        var storeBlock = new byte[2 + store.Length];
        BinaryPrimitives.WriteUInt16BigEndian(storeBlock.AsSpan(0),
            (ushort)store.Length); // the length field the vstore slice reads
        store.CopyTo(storeBlock.AsSpan(2));

        // Layout: header 5 | TopDICT L | GlobalSubrs 4 | CharStrings | FDArray
        // | VarStore. TopDICT keys: CharStrings(0x11), FDArray(12 24), vstore(24).
        // The offsets depend on the TopDICT length the operand encoding yields,
        // so the layout settles by fixed point (the minimal DICT integer form).
        var topDict = new List<byte>();
        int charStringsAt = 0, fdArrayAt = 0, storeAt = 0;
        for (int topDictLength = 13; ;)
        {
            int globalSubrsAt = 5 + topDictLength, charStringsCandidate = globalSubrsAt + 4;
            int fdArrayCandidate = charStringsCandidate + charStrings.Length,
                storeCandidate = fdArrayCandidate + fdArray.Length;
            topDict.Clear();
            DictInt(topDict, charStringsCandidate);
            topDict.Add(0x11);                                   // CharStrings key
            DictInt(topDict, fdArrayCandidate);
            topDict.Add(12);
            topDict.Add(0x24);                                   // FDArray key
            DictInt(topDict, storeCandidate);
            topDict.Add(24);                                     // vstore key
            if (topDict.Count == topDictLength)
            {
                charStringsAt = charStringsCandidate;
                fdArrayAt = fdArrayCandidate;
                storeAt = storeCandidate;
                break;
            }
            topDictLength = topDict.Count;
        }
        var bytes = new byte[storeAt + storeBlock.Length];
        bytes[0] = 2;             // majorVersion
        bytes[1] = 0;             // minorVersion
        bytes[2] = 5;             // headerSize
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(3), (ushort)topDict.Count);
        topDict.CopyTo(bytes.AsSpan(5));
        bytes.AsSpan(5 + topDict.Count, 4).Clear(); // empty GlobalSubr INDEX: u32 count 0
        charStrings.CopyTo(bytes.AsSpan(charStringsAt));
        fdArray.CopyTo(bytes.AsSpan(fdArrayAt));
        storeBlock.CopyTo(bytes.AsSpan(storeAt));
        return bytes;
    }

    /// <summary>The blend-stem face: the CFF2-with-store table over the standard
    /// fixture core plus the fvar axis authority the store's signature requires.</summary>
    private static FontFaceRef BlendFace(out byte[] cff2In, out byte[] charStringsIndex)
    {
        cff2In = Cff2WithBlendStems(out charStringsIndex);
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp05(NumGlyphs)),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapWithMacRoman((0x41, 1))),
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar(1)),
            (SubsetFontFixture.Cff2Tag, cff2In),
            (SubsetFontFixture.VorgTag, Vorg((0, 700), (1, 750), (2, 800))));
    }

    // The S4 walker's CFF2 blend pin: full select over the blend-stem face —
    // every stem declared over blend results — must publish (the first model's
    // stack wipe desynced the mask width and refused this exact shape), and the
    // plan-null identity anchor holds: the CharStrings INDEX re-slots byte for
    // byte because nothing was prunable.
    [Fact]
    public void BlendStemFullSelectPublishesAndKeepsCharStringsByteIdentical()
    {
        var face = BlendFace(out _, out byte[] charStringsIndex);
        var status = SubsetOperation.TryPrepare(Request(face,
            [(ushort)0, (ushort)1, (ushort)2], SubsetMode.CompactRemap),
            out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        byte[] cff2Out = TableOf(Output(result!), SubsetFontFixture.Cff2Tag,
            out bool present);
        Assert.True(present);

        // the output's CharStrings INDEX at its TopDICT key: byte-identical to
        // the input's INDEX (the identity anchor — the items move, never rewrite)
        int at = 5;
        int charStringsOffset = -1;
        while (at < 5 + BinaryPrimitives.ReadUInt16BigEndian(cff2Out.AsSpan(3)))
        {
            int operand = cff2Out[at] - 139;
            at++;
            if (cff2Out[at] == 0x11)
            {
                charStringsOffset = operand;
                break;
            }
            if (cff2Out[at] == 12) at++;
            at++;
        }
        Assert.True(charStringsOffset > 0);
        Assert.Equal(charStringsIndex,
            cff2Out[charStringsOffset..(charStringsOffset + charStringsIndex.Length)]);
        // the pin's self-check: the INDEX the walker walked is the 3-item
        // blend-stem INDEX the builder above produced
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(charStringsIndex.AsSpan(0)));
        Assert.Equal(3 * 28, charStringsIndex.Length - 9);
    }
}
