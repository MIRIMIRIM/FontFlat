// CFF2 subr prune tests (S4 card §2.1's CFF2 addendum, OQ-S4-1): the prune/
// renumber/re-encode contract the CFF1 faces pin, over the CFF2 wire (TN5171) —
// u32-count INDEXes, no endchar (charstrings end by fall-off), the FDArray/
// FDSelect binding, the Private DICT's Subrs/vsindex keys. The fixtures are
// assembled from the wire format directly, never by asking the product to write
// them; expectations parse the emitted output bytes back through the typed read
// chain (the strict validator re-checks every tiling) and compare re-encoded
// operands against the spec operand convention (TN5177 §4.7/§3.1), never against
// the producer.
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetSubrPruneCff2Tests
{
    // ---- wire-level helpers -------------------------------------------------------

    private static byte[] Num(int v) => v is >= -107 and <= 107
        ? [(byte)(v + 139)]
        : [(byte)28, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)];

    /// <summary>The CFF2 INDEX (TN5171 spec L277-293): u32 count, empty = exactly
    /// 4 bytes, otherwise offSize + (count+1) offsets + data.</summary>
    private static byte[] Cff2Index(params byte[][] items)
    {
        var bytes = new List<byte> { (byte)(items.Length >> 24), (byte)((items.Length >> 16) & 0xFF),
            (byte)((items.Length >> 8) & 0xFF), (byte)(items.Length & 0xFF) };
        if (items.Length == 0) return [.. bytes];
        var offsets = new List<int> { 1 };
        foreach (var item in items) offsets.Add(offsets[^1] + item.Length);
        int width = 1;
        while (offsets[^1] >= (1 << (8 * width))) width++;
        bytes.Add((byte)width);
        foreach (int offset in offsets)
            for (int i = width - 1; i >= 0; i--)
                bytes.Add((byte)((offset >> (8 * i)) & 0xFF));
        foreach (var item in items) bytes.AddRange(item);
        return [.. bytes];
    }

    private static byte[] Maxp05(ushort numGlyphs)
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), numGlyphs);
        return bytes;
    }

    private static FontFaceRef Face(params (uint Tag, byte[] Bytes)[] tables) =>
        SubsetFontFixture.Face(tables);

    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        SubsetMode mode = SubsetMode.CompactRemap) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = mode,
        Options = new SubsetOptions { NoLayout = true }
    };

    private static byte[] Output(SubsetResult result)
    {
        byte[] output = new byte[result.Font.OutputLength];
        ReadStatus status = result.Font.TryCopyTo(output, out int written, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(output.Length, written);
        return output;
    }

    /// <summary>Parses the emitted sfnt's CFF2 table back through the typed read
    /// chain (the output oracle: the strict validator re-checks every tiling).
    /// numGlyphs is the caller's declared context (the T5-04 dual-input
    /// discipline) — the output's glyph count, and the fvar axis count only on a
    /// store face (the store<->fvar signature coupling).</summary>
    private static Cff2Table OutputCff2(SubsetResult result, int numGlyphs,
        int? fvarAxisCount = null)
    {
        byte[] sfnt = Output(result);
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        byte[] cff2 = [];
        bool present = false;
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            if (BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record)) != 0x43464632)
                continue;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 12));
            cff2 = sfnt[offset..(offset + length)];
            present = true;
            break;
        }
        Assert.True(present);
        Assert.Equal(ReadStatus.Done, Cff2View.TryOpen(cff2, out var view, out _, default));
        Assert.Equal(ReadStatus.Done,
            view.TryReadTable(numGlyphs, fvarAxisCount, out var model, out _,
                new BuildBudget()));
        return model!;
    }

    // ---- the cross-INDEX face (the C1 surface) --------------------------------------

    // The CFF2 mirror of the CFF1 cross-INDEX pin (SubsetSubrPruneCrossIndexTests'
    // LocalSurvivorRewritesItsStaleGlobalCall): the global INDEX prunes its
    // unreachable body 0 while the local INDEX keeps every body (null slot), and
    // the surviving local's callgsubr still renumbers — the stale operand -106
    // would resolve past the one-body output INDEX and fault at run time.
    [Fact]
    public void LocalSurvivorRewritesItsStaleGlobalCall()
    {
        var builder = new Cff2PruneBuilder(fdCount: 1, numGlyphs: 2);
        builder.SetGlobals([0x8b], [0x8b]);
        builder.SetLocals(0, [0x21, 0x1d]);              // callgsubr 1, fall-off
        builder.SetGlyphProgram(0, [], fd: 0);
        builder.SetGlyphProgram(1, [.. Num(-107), 10], fd: 0); // callsubr 0, fall-off
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [1]),
            out var result, out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected}");
        Assert.NotNull(result);
        Assert.Equal(1, report!.SubrPrunedGlobal);
        Assert.Empty(report.SubrPrunedLocalByFd);
        // The output keeps .notdef: two glyphs ride the compact map.
        var cff2 = OutputCff2(result!, 2);
        Assert.False(cff2.HasFdSelect);
        _ = Assert.Single(cff2.GlobalSubrs);
        // The rewritten cross-INDEX site: global 1 renumbers to 0, operand -107 —
        // the stale byte 0x21 is gone.
        Assert.Equal([0x20, 0x1d], cff2.FontDicts[0].Private!.LocalSubrs[0]);
        // The glyph's same-INDEX site keeps its bytes (the value rule).
        Assert.Equal([.. Num(-107), 10], cff2.CharStrings[1].ToArray());
        Assert.Equal([0x8b], cff2.GlobalSubrs[0].ToArray());
    }

    // ---- the per-FD face (the C2 surface) -------------------------------------------

    // The CFF2 mirror of the CID per-FD context pin (SubsetSubrPruneTests'
    // CidLocalSurvivorsRewriteUnderTheirOwnFdContext): FD0's local INDEX prunes to
    // zero — its Private DICT loses the Subrs key with it — while FD1 prunes one
    // unreferenced body and its survivors re-emit byte-identical under FD1's own
    // table, map and bias (a zero-filled context column read FD0's pruned map and
    // refused FD1's first legal site).
    [Fact]
    public void LocalSurvivorsRewriteUnderTheirOwnFdContext()
    {
        var builder = new Cff2PruneBuilder(fdCount: 2, numGlyphs: 2);
        builder.SetLocals(0, [0x8b]);                    // no retained glyph binds FD0
        builder.SetLocals(1, [0x8b], [0x20, 0x0a, 0x8b], [0x8b]); // local 1 calls local 0
        builder.SetGlyphProgram(0, [], fd: 0);
        builder.SetGlyphProgram(1, [.. Num(-106), 10], fd: 1); // callsubr 1, fall-off
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [1]),
            out var result, out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected}");
        Assert.NotNull(result);
        Assert.Equal([1, 1], report!.SubrPrunedLocalByFd);
        // The output keeps .notdef: two glyphs ride the compact map.
        var cff2 = OutputCff2(result!, 2);
        Assert.Equal(2, cff2.FontDictCount);
        // FD0: the INDEX pruned to zero survivors, the Subrs key with it.
        Assert.False(cff2.FontDicts[0].Private!.HasLocalSubrs);
        Assert.DoesNotContain(cff2.FontDicts[0].Private!.Entries.ToArray(),
            entry => entry.Operator == 0x13);
        // FD1: both survivors kept under FD1's own map — the call sites' operand
        // values are unchanged (old and new bias both the 107 tier), so the bodies
        // re-emit byte-identical through the rewrite.
        Assert.Equal(2, cff2.FontDicts[1].Private!.LocalSubrs.Length);
        Assert.Equal([0x8b], cff2.FontDicts[1].Private!.LocalSubrs[0]);
        Assert.Equal([0x20, 0x0a, 0x8b], cff2.FontDicts[1].Private!.LocalSubrs[1]);
        Assert.Equal([.. Num(-106), 10], cff2.CharStrings[1].ToArray());
        // The FD bindings re-slot with their glyphs: .notdef keeps FD0, the
        // selected glyph keeps FD1.
        Assert.Equal([0, 1], cff2.FdIndex.ToArray());
    }

    // ---- the identity anchor (§3.5) ---------------------------------------------------

    // The CFF2 form of the §3.5 anchor over a face with both subr INDEXes live
    // (the store-less blend-stem pin SubsetCff2SubsetTests.
    // BlendStemFullSelectPublishesAndKeepsCharStringsByteIdentical covers the
    // CharStrings-only side): every body referenced, full select — the plan is
    // null and the output keeps the input's charstring and subr bytes byte for
    // byte through the typed chain (the S1c verbatim path; the compiler's
    // canonical re-tiling moves bodies, never rewrites them).
    [Fact]
    public void FullReachabilityIsByteIdentityWithTheVerbatimPath()
    {
        var builder = new Cff2PruneBuilder(fdCount: 1, numGlyphs: 3);
        builder.SetGlobals([0x8b]);
        builder.SetLocals(0, [0x8b], [0x20, 0x0a]);      // local 1 calls local 0
        builder.SetGlyphProgram(0, [], fd: 0);
        builder.SetGlyphProgram(1, [.. Num(-106), 10], fd: 0);  // callsubr 1 -> local 0
        builder.SetGlyphProgram(2, [.. Num(-107), 29], fd: 0);  // callgsubr 0
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [0, 1, 2]),
            out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(0, report!.SubrPrunedGlobal);
        Assert.Empty(report.SubrPrunedLocalByFd);
        var cff2 = OutputCff2(result!, 3);
        Assert.Equal([0x8b], cff2.GlobalSubrs[0].ToArray());
        Assert.Equal(2, cff2.FontDicts[0].Private!.LocalSubrs.Length);
        Assert.Equal([0x8b], cff2.FontDicts[0].Private!.LocalSubrs[0]);
        Assert.Equal([0x20, 0x0a], cff2.FontDicts[0].Private!.LocalSubrs[1]);
        Assert.Empty(cff2.CharStrings[0].ToArray());
        Assert.Equal([.. Num(-106), 10], cff2.CharStrings[1].ToArray());
        Assert.Equal([.. Num(-107), 29], cff2.CharStrings[2].ToArray());
    }

    /// <summary>A CFF2 wire builder (TN5171) with local-subr injection, the strict
    /// canonical tiling header(5)|TopDICT|GlobalSubrs|CharStrings|FDArray|FDSelect|
    /// private₀+local₀|…|VarStore: every DICT offset operand rides the fixed 29
    /// 5-byte form so one measurement closes the layout, each Subrs operand is
    /// pinned to its own Private DICT size (the canonical emission puts the INDEX
    /// right behind the body), and the per-FD vsindex rides the 0x16 key. Bodies
    /// end by fall-off — CFF2 has no endchar (TN5171).</summary>
    private sealed class Cff2PruneBuilder
    {
        private readonly int _fdCount;
        private readonly int _numGlyphs;
        private readonly int[] _fds;
        private readonly byte[][] _programs;
        private readonly byte[][][] _locals;
        private readonly int?[] _vsindexByFd;
        private byte[][] _globals = [];
        private (int Regions, int[] RegionIndexCounts)? _store;

        internal Cff2PruneBuilder(int fdCount, int numGlyphs)
        {
            _fdCount = fdCount;
            _numGlyphs = numGlyphs;
            _fds = new int[numGlyphs];
            _programs = new byte[numGlyphs][];
            _locals = new byte[fdCount][][];
            _vsindexByFd = new int?[fdCount];
            for (int fd = 0; fd < fdCount; fd++) _locals[fd] = [];
        }

        internal void SetGlobals(params byte[][] bodies) => _globals = bodies;

        /// <summary>Replaces an FD's whole local INDEX with explicit bodies (the
        /// INDEX exists exactly when the body set is non-empty).</summary>
        internal void SetLocals(int fd, params byte[][] bodies) => _locals[fd] = bodies;

        /// <summary>The Private DICT's vsindex (key 0x16): the default
        /// ItemVariationData selector every blend of the FD's charstrings
        /// inherits when no explicit vsindex operator runs (TN5171 §5).</summary>
        internal void SetPrivateVsindex(int fd, int value) => _vsindexByFd[fd] = value;

        internal void SetGlyphProgram(int gid, byte[] program, int fd)
        {
            _programs[gid] = program;
            _fds[gid] = fd;
        }

        /// <summary>The variation store over one axis: `regions` declared regions,
        /// one ItemVariationData per entry of `regionIndexCounts` (itemCount and
        /// wordDeltaCount zero — the CFF2 degenerate gate, spec L1444), so slot v's
        /// RegionIndexCount is what the blend arithmetic reads for vsindex v.</summary>
        internal void SetStore(int regions, params int[] regionIndexCounts) =>
            _store = (regions, regionIndexCounts);

        internal FontFaceRef Build()
        {
            // Private DICTs first: the Subrs operand is the DICT's own size, fixed
            // by the 29-form operand's constant width (2-byte StdHW 0 + the
            // optional 2-byte vsindex + the 6-byte Subrs entry).
            var privateDicts = new byte[_fdCount][];
            for (int fd = 0; fd < _fdCount; fd++)
            {
                bool hasLocals = _locals[fd].Length > 0;
                bool hasVsindex = _vsindexByFd[fd] is not null;
                int size = 2 + (hasVsindex ? 2 : 0) + (hasLocals ? 6 : 0);
                var dict = new List<byte> { 0x8b, 0x0a };          // StdHW 0
                if (hasVsindex)
                {
                    // The fixture domain keeps the selector within the one-byte
                    // push form [-107, 107].
                    dict.Add((byte)(139 + _vsindexByFd[fd]!.Value));
                    dict.Add(0x16);
                }
                if (hasLocals)
                    dict.AddRange([29, 0, 0, 0, (byte)size, 0x13]); // Subrs = DICT size
                privateDicts[fd] = [.. dict];
            }

            byte[] globalSubrs = Cff2Index(_globals);
            byte[] charStrings = Cff2Index(_programs);

            // FDArray: per-FD FontDICT, the `size offset` pair over the fixed
            // 29-form (11-byte items), the offsets pinned after the FDSelect.
            var fontDictItems = new List<byte[]>();
            for (int fd = 0; fd < _fdCount; fd++)
                fontDictItems.Add(new byte[11]); // placeholder, rewritten below
            byte[] fdArrayPlaceholder = Cff2Index([.. fontDictItems]);

            // FDSelect: format 0, one byte per glyph (the more-than-one-FontDICT
            // coupling — a single-FD face never carries the key).
            byte[] fdSelect = _fdCount > 1 ? [(byte)0, .. _fds.Select(static f => (byte)f)] : [];

            // The variation store: u16 length wrapper + format 1 + offsets array +
            // region list + the degenerate VarData blocks.
            byte[] storeBlock = _store is { } storeSpec
                ? StoreBlock(storeSpec.Regions, storeSpec.RegionIndexCounts) : [];

            // The layout closes in one measurement: the fixed-width TopDICT makes
            // every block position operand-width-independent.
            int topDictLength = (5 + 1) + (5 + 2) + (_fdCount > 1 ? 5 + 2 : 0)
                + (_store is not null ? 5 + 1 : 0);
            int globalSubrsAt = 5 + topDictLength;
            int charStringsAt = globalSubrsAt + globalSubrs.Length;
            int fdArrayAt = charStringsAt + charStrings.Length;
            int fdSelectAt = fdArrayAt + fdArrayPlaceholder.Length;
            int cursor = fdSelectAt + fdSelect.Length;
            var privateOffsets = new int[_fdCount];
            for (int fd = 0; fd < _fdCount; fd++)
            {
                privateOffsets[fd] = cursor;
                cursor += privateDicts[fd].Length + Cff2Index(_locals[fd]).Length;
            }
            int storeAt = cursor;

            for (int fd = 0; fd < _fdCount; fd++)
            {
                int size = privateDicts[fd].Length;
                int offset = privateOffsets[fd];
                fontDictItems[fd] =
                [
                    29, 0, 0, 0, (byte)size,
                    29, (byte)((offset >> 24) & 0xFF), (byte)((offset >> 16) & 0xFF),
                        (byte)((offset >> 8) & 0xFF), (byte)(offset & 0xFF), 0x12,
                ];
            }
            byte[] fdArray = Cff2Index([.. fontDictItems]);
            Assert.Equal(fdArrayPlaceholder.Length, fdArray.Length);

            var top = new List<byte>();
            void Entry(int op, int value)
            {
                top.AddRange([29, (byte)((value >> 24) & 0xFF), (byte)((value >> 16) & 0xFF),
                    (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF), (byte)op]);
            }
            void Escape(int b1, int value)
            {
                top.AddRange([29, (byte)((value >> 24) & 0xFF), (byte)((value >> 16) & 0xFF),
                    (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF), 12, (byte)b1]);
            }
            Entry(0x11, charStringsAt);                      // CharStrings
            Escape(0x24, fdArrayAt);                         // FDArray
            if (_fdCount > 1) Escape(0x25, fdSelectAt);      // FDSelect
            if (_store is not null) Entry(24, storeAt);      // VariationStore
            Assert.Equal(topDictLength, top.Count);

            var bytes = new List<byte> { 2, 0, 5 };          // major 2, minor 0, hdrSize 5
            bytes.Add((byte)(topDictLength >> 8));
            bytes.Add((byte)(topDictLength & 0xFF));
            bytes.AddRange(top);
            bytes.AddRange(globalSubrs);
            bytes.AddRange(charStrings);
            bytes.AddRange(fdArray);
            bytes.AddRange(fdSelect);
            for (int fd = 0; fd < _fdCount; fd++)
            {
                bytes.AddRange(privateDicts[fd]);
                bytes.AddRange(Cff2Index(_locals[fd]));
            }
            bytes.AddRange(storeBlock);

            var tables = new List<(uint, byte[])>
            {
                (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
                (SubsetFontFixture.MaxpTag, Maxp05((ushort)_numGlyphs)),
                (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapWithMacRoman((0x41, 1))),
            };
            if (_store is not null)
                tables.Add((SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar(1)));
            tables.Add((SubsetFontFixture.Cff2Tag, [.. bytes]));
            var vorg = new (ushort, ushort)[_numGlyphs];
            for (int gid = 0; gid < _numGlyphs; gid++) vorg[gid] = ((ushort)gid, 700);
            tables.Add((SubsetFontFixture.VorgTag, Vorg(vorg)));
            return Face([.. tables]);
        }

        /// <summary>The +2 length-prefixed ItemVariationStore (spec L1420-1426):
        /// format 1, `regions` regions over one axis, one ItemVariationData per
        /// regionIndexCounts entry with zero itemCount/wordDeltaCount (the CFF2
        /// degenerate gate) and regionIndexes 0..k-1.</summary>
        private static byte[] StoreBlock(int regions, int[] regionIndexCounts)
        {
            var blocks = new List<byte[]>();
            foreach (int k in regionIndexCounts)
            {
                var block = new List<byte> { 0, 0, 0, 0, (byte)(k >> 8), (byte)(k & 0xFF) };
                for (int i = 0; i < k; i++)
                    block.AddRange([(byte)((i >> 8) & 0xFF), (byte)(i & 0xFF)]);
                blocks.Add([.. block]);
            }
            var regionList = new byte[4 + 6 * regions];
            BinaryPrimitives.WriteUInt16BigEndian(regionList.AsSpan(0), 1);  // axisCount
            BinaryPrimitives.WriteUInt16BigEndian(regionList.AsSpan(2), (ushort)regions);
            int regionListOffset = 8 + 4 * blocks.Count;
            int storeLength = regionListOffset + regionList.Length
                + blocks.Sum(static block => block.Length);
            var store = new byte[storeLength];
            BinaryPrimitives.WriteUInt16BigEndian(store.AsSpan(0), 1);       // format
            BinaryPrimitives.WriteUInt32BigEndian(store.AsSpan(2), (uint)regionListOffset);
            BinaryPrimitives.WriteUInt16BigEndian(store.AsSpan(6), (ushort)blocks.Count);
            regionList.CopyTo(store.AsSpan(regionListOffset));
            int at = regionListOffset + regionList.Length;
            for (int i = 0; i < blocks.Count; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(store.AsSpan(8 + 4 * i), (uint)at);
                blocks[i].CopyTo(store.AsSpan(at));
                at += blocks[i].Length;
            }
            var wrapped = new byte[2 + store.Length];
            BinaryPrimitives.WriteUInt16BigEndian(wrapped.AsSpan(0), (ushort)store.Length);
            store.CopyTo(wrapped.AsSpan(2));
            return wrapped;
        }
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
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8 + 4 * i), records[i].Gid);
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(10 + 4 * i),
                (short)records[i].OriginY);
        }
        return bytes;
    }
}
