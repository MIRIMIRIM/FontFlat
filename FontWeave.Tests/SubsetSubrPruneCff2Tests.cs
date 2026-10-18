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

    // ---- the Private-vsindex blend faces (TN5171 §5's inherited selector) ------------

    /// <summary>A four-result blend block: 4 defaults, 4·k deltas, the count 4 and
    /// blend. Over the FD's Private vsindex 1 (RegionIndexCount 2) the walker
    /// keeps 4 values; the vstemhm adds 2 hints to the 6 declared hstems, so the
    /// mask width is one byte. A walker that reads IVD 0 instead (RegionIndexCount
    /// 1) keeps 8 values, adds 4 hints, and skips two mask bytes — one past the
    /// program that ends at the mask.</summary>
    private static byte[] BlendBlock() 
    {
        var program = new List<byte>();
        for (int i = 0; i < 12; i++) program.Add(0x8b);    // six hstem pairs
        program.Add(1);                                    // hstem
        for (int i = 0; i < 4; i++) program.Add(0x8b);    // four defaults
        for (int i = 0; i < 8; i++) program.Add(0x8b);    // 4 · k(=2) deltas
        program.Add(143);                                  // the result count 4
        program.Add(16);                                   // blend
        program.Add(3);                                    // vstemhm
        return [.. program];
    }

    // TN5171 §5: a blend with no explicit vsindex operator reads the
    // ItemVariationData the glyph's Private DICT vsindex names — the walk must
    // seed its selector from there, not from slot 0. The glyph's mask width is
    // the observer: the correct k=2 keeps the skip at one byte; the k=1 mis-model
    // reads IVD 0 and derails at the mask (the reach walk refuses a legal font).
    [Fact]
    public void PrivateVsindexDefaultsTheGlyphBlendStackModel()
    {
        var builder = new Cff2PruneBuilder(fdCount: 1, numGlyphs: 2);
        builder.SetStore(regions: 2, regionIndexCounts: [1, 2]);
        builder.SetPrivateVsindex(0, 1);                   // the blend's k is IVD 1's 2
        builder.SetLocals(0, [0x8b], [0x8b]);              // local 1 unreferenced: prunes
        builder.SetGlyphProgram(0, [], fd: 0);
        List<byte> glyph = [.. BlendBlock()];
        glyph.AddRange(Num(-107));                         // callsubr 0
        glyph.Add(10);
        glyph.Add(19);                                     // hintmask: the mask byte is last
        glyph.Add(0xFF);
        builder.SetGlyphProgram(1, [.. glyph], fd: 0);
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [1]),
            out var result, out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected}");
        Assert.NotNull(result);
        Assert.Equal([1], report!.SubrPrunedLocalByFd);
        var cff2 = OutputCff2(result!, 2, fvarAxisCount: 1);
        // The prune kept every byte: the blend-driven arithmetic rode the
        // inherited selector on both sides (reach and rewrite), and the store
        // with its two RegionIndexCount columns carries over verbatim.
        Assert.Equal([.. glyph], cff2.CharStrings[1].ToArray());
        Assert.Equal([0x8b], cff2.FontDicts[0].Private!.LocalSubrs[0]);
        Assert.Equal(1, cff2.FontDicts[0].Private!.VsindexValue);
        Assert.NotNull(cff2.Store);
        Assert.Equal(2, cff2.Store!.DataCount);
        Assert.Equal(1, cff2.Store.Data[0]!.RegionIndexCount);
        Assert.Equal(2, cff2.Store.Data[1]!.RegionIndexCount);
    }

    // The same inheritance with the blend inside a surviving local body: the
    // rewrite walk over that body is a fresh frame, so it must seed the Private
    // vsindex too — the mask-width derail there refuses the rewrite side alone.
    [Fact]
    public void PrivateVsindexDefaultsTheLocalBodyBlendStackModel()
    {
        var builder = new Cff2PruneBuilder(fdCount: 1, numGlyphs: 2);
        builder.SetStore(regions: 2, regionIndexCounts: [1, 2]);
        builder.SetPrivateVsindex(0, 1);
        byte[] blendBody = [.. BlendBlock(), 19, 0xFF];    // hintmask: one mask byte
        builder.SetLocals(0, blendBody, [0x8b]);           // local 1 unreferenced: prunes
        builder.SetGlyphProgram(0, [], fd: 0);
        builder.SetGlyphProgram(1, [.. Num(-107), 10], fd: 0); // callsubr 0
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [1]),
            out var result, out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected}");
        Assert.NotNull(result);
        Assert.Equal([1], report!.SubrPrunedLocalByFd);
        var cff2 = OutputCff2(result!, 2, fvarAxisCount: 1);
        Assert.Equal(blendBody, cff2.FontDicts[0].Private!.LocalSubrs[0]);
        Assert.Equal([.. Num(-107), 10], cff2.CharStrings[1].ToArray());
        Assert.Equal(1, cff2.FontDicts[0].Private!.VsindexValue);
    }

    // ---- the caller-switched selector (TN5171 §5's path-dependent context) ----------

    // A glyph arms vsindex 1 (IVD 1, RegionIndexCount 2) BEFORE calling a local
    // body whose blend carries no vsindex of its own: the body's runtime
    // selector is the caller's armed value, so the FD-default seed mis-sized
    // the rewrite frame (k=1 kept 8 values, the vstemhm added 4 hints, and the
    // mask skip ran past the body — the A3 mis-refusal of a legal face). The
    // per-context entry memo seeds the rewrite with the recorded entry
    // selector instead, and every non-call byte survives verbatim.
    [Fact]
    public void CallerSwitchedVsindexSeedsTheCalledBodyRewrite()
    {
        var builder = new Cff2PruneBuilder(fdCount: 1, numGlyphs: 2);
        builder.SetStore(regions: 2, regionIndexCounts: [1, 2]);
        byte[] blendBody = [.. BlendBlock(), 19, 0xFF];    // hintmask: one mask byte
        builder.SetLocals(0, blendBody, [0x8b]);           // local 1 unreferenced: prunes
        builder.SetGlyphProgram(0, [], fd: 0);
        builder.SetGlyphProgram(1, [0x8c, 15, .. Num(-107), 10], fd: 0); // vsindex 1; callsubr 0
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [1]),
            out var result, out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected}");
        Assert.NotNull(result);
        Assert.Equal([1], report!.SubrPrunedLocalByFd);
        var cff2 = OutputCff2(result!, 2, fvarAxisCount: 1);
        // The body's bytes ride the rewrite untouched: the blend/vstemhm/hintmask
        // arithmetic over the caller-armed k=2 matches the reach walk's, so no
        // token moved.
        Assert.Equal(blendBody, cff2.FontDicts[0].Private!.LocalSubrs[0]);
        Assert.Equal([0x8c, 15, .. Num(-107), 10], cff2.CharStrings[1].ToArray());
        Assert.NotNull(cff2.Store);
        Assert.Equal(2, cff2.Store!.DataCount);
        Assert.Equal(2, cff2.Store.Data[1]!.RegionIndexCount);
    }

    // Two glyphs call the same blend body under two different entry selectors —
    // glyph 1 through the FD default (selector 0), glyph 2 after an explicit
    // vsindex 1. The body's runtime blend interpretation is path-dependent
    // (k=1 keeps 4 values, k=2 keeps 2), so no single rewritten product is
    // semantically faithful to both entries and the plan refuses (A3): Detail
    // names the body, Expected the distinct entry count.
    [Fact]
    public void TwoEntrySelectorsIntoOneBlendBodyRefuse()
    {
        var builder = new Cff2PruneBuilder(fdCount: 1, numGlyphs: 3);
        builder.SetStore(regions: 2, regionIndexCounts: [1, 2]);
        // Six values, count 2, blend, vstemhm: the blend parses under both
        // selectors and no mask rides the count, so only the entry-memo guard
        // can refuse the face.
        byte[] body = [0x8b, 0x8b, 0x8b, 0x8b, 0x8b, 0x8b, 0x8d, 16, 3];
        builder.SetLocals(0, body, [0x8b]);                // local 1 unreferenced: prunes
        builder.SetGlyphProgram(0, [], fd: 0);
        builder.SetGlyphProgram(1, [.. Num(-107), 10], fd: 0);           // the FD default's selector
        builder.SetGlyphProgram(2, [0x8c, 15, .. Num(-107), 10], fd: 0); // vsindex 1, then call
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [1, 2]),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(0UL, diagnostic.Detail);    // local body 0
        Assert.Equal(2UL, diagnostic.Expected);  // the two distinct entry selectors
        Assert.Null(result);
        // The refusal's report retains the same diagnostic (the failure shape).
        Assert.NotNull(report);
        FontDiagnostic retained = Assert.Single(report!.Diagnostics);
        Assert.Equal(diagnostic, retained);
    }

    // ---- the ambiguous-entry guard narrowed to selector-sensitive bodies ----------

    // Two glyphs call the same blend-FREE body under two different entry
    // selectors (the FD default, and an explicit vsindex 1 before the call).
    // The body's parse never consults the entry — no blend, nothing else reads
    // k — so the multi-valued entry set is innocent and both entries' bytes
    // re-emit verbatim; a body-keyed refusal here refused a legal face.
    [Fact]
    public void TwoEntrySelectorsIntoABlendFreeBodyRewrite()
    {
        var builder = new Cff2PruneBuilder(fdCount: 1, numGlyphs: 3);
        builder.SetStore(regions: 2, regionIndexCounts: [1, 2]);
        builder.SetLocals(0, [0x8b], [0x8b]);              // local 1 unreferenced: prunes
        builder.SetGlyphProgram(0, [], fd: 0);
        builder.SetGlyphProgram(1, [.. Num(-107), 10], fd: 0);           // the FD default's selector
        builder.SetGlyphProgram(2, [0x8c, 15, .. Num(-107), 10], fd: 0); // vsindex 1, then call
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [1, 2]),
            out var result, out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected}");
        Assert.NotNull(result);
        Assert.Equal([1], report!.SubrPrunedLocalByFd);
        var cff2 = OutputCff2(result!, 3, fvarAxisCount: 1);
        Assert.Equal([0x8b], cff2.FontDicts[0].Private!.LocalSubrs[0].ToArray());
        Assert.Equal([.. Num(-107), 10], cff2.CharStrings[1].ToArray());
        Assert.Equal([0x8c, 15, .. Num(-107), 10], cff2.CharStrings[2].ToArray());
    }

    // The same two-entry shape into a body whose own vsindex operator runs
    // BEFORE its blend: the blend's k is the body's armed selector from there
    // on (TN5171 §5), the entry never reaches it, and the face rewrites — the
    // guard's refusal stays with the selector-sensitive bodies, the shape the
    // TwoEntrySelectorsIntoOneBlendBodyRefuse pin keeps refusing.
    [Fact]
    public void OwnVsindexBeforeTheBlendMakesTheEntryIrrelevant()
    {
        var builder = new Cff2PruneBuilder(fdCount: 1, numGlyphs: 3);
        builder.SetStore(regions: 2, regionIndexCounts: [1, 2]);
        byte[] blendBody = [0x8c, 15, .. BlendBlock(), 19, 0xFF]; // vsindex 1 first
        builder.SetLocals(0, blendBody, [0x8b]);                  // local 1 unreferenced: prunes
        builder.SetGlyphProgram(0, [], fd: 0);
        builder.SetGlyphProgram(1, [.. Num(-107), 10], fd: 0);           // the FD default's selector
        builder.SetGlyphProgram(2, [0x8c, 15, .. Num(-107), 10], fd: 0); // vsindex 1, then call
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [1, 2]),
            out var result, out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected}");
        Assert.NotNull(result);
        Assert.Equal([1], report!.SubrPrunedLocalByFd);
        var cff2 = OutputCff2(result!, 3, fvarAxisCount: 1);
        // The body's blend arithmetic rode its own armed selector on both sides,
        // so no token moved even though the two entries disagree.
        Assert.Equal(blendBody, cff2.FontDicts[0].Private!.LocalSubrs[0].ToArray());
        Assert.Equal([.. Num(-107), 10], cff2.CharStrings[1].ToArray());
        Assert.Equal([0x8c, 15, .. Num(-107), 10], cff2.CharStrings[2].ToArray());
    }

    // The global-body leg of the narrowed guard: two entries into a blend-free
    // global body parse identically and rewrite byte-faithfully.
    [Fact]
    public void TwoEntrySelectorsIntoABlendFreeGlobalBodyRewrite()
    {
        var builder = new Cff2PruneBuilder(fdCount: 1, numGlyphs: 3);
        builder.SetStore(regions: 2, regionIndexCounts: [1, 2]);
        builder.SetGlobals([0x8b], [0x8b]);                // global 1 unreferenced: prunes
        builder.SetGlyphProgram(0, [], fd: 0);
        builder.SetGlyphProgram(1, [.. Num(-107), 29], fd: 0);           // the FD default's selector
        builder.SetGlyphProgram(2, [0x8c, 15, .. Num(-107), 29], fd: 0); // vsindex 1, then call
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [1, 2]),
            out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(1, report!.SubrPrunedGlobal);
        var cff2 = OutputCff2(result!, 3, fvarAxisCount: 1);
        _ = Assert.Single(cff2.GlobalSubrs);
        Assert.Equal([0x8b], cff2.GlobalSubrs[0].ToArray());
        Assert.Equal([0x8c, 15, .. Num(-107), 29], cff2.CharStrings[2].ToArray());
    }

    // The global leg across FD contexts: a global blend-free body reached under
    // two entries from two DIFFERENT FDs — its sensitivity fact is FD-free (its
    // tokens and the face-wide store fix it), so the multi-valued set stays
    // innocent whatever context scanned it first.
    [Fact]
    public void TwoEntrySelectorsFromTwoFdsIntoABlendFreeGlobalBodyRewrite()
    {
        var builder = new Cff2PruneBuilder(fdCount: 2, numGlyphs: 3);
        builder.SetStore(regions: 2, regionIndexCounts: [1, 2]);
        builder.SetGlobals([0x8b]);
        builder.SetLocals(0);
        builder.SetLocals(1);
        builder.SetGlyphProgram(0, [], fd: 0);
        builder.SetGlyphProgram(1, [.. Num(-107), 29], fd: 0);           // entry 0 via FD0
        builder.SetGlyphProgram(2, [0x8c, 15, .. Num(-107), 29], fd: 1); // entry 1 via FD1
        var status = SubsetOperation.TryPrepare(Request(builder.Build(), [1, 2]),
            out var result, out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected}");
        Assert.NotNull(result);
        var cff2 = OutputCff2(result!, 3, fvarAxisCount: 1);
        Assert.Equal([0x8b], cff2.GlobalSubrs[0].ToArray());
        Assert.Equal([.. Num(-107), 29], cff2.CharStrings[1].ToArray());
        Assert.Equal([0x8c, 15, .. Num(-107), 29], cff2.CharStrings[2].ToArray());
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
