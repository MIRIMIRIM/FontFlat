// Cross-INDEX subr prune tests (S4 card §3.3's per-body rewrite obligation): a
// parked plan rewrites every surviving body — the plan slots with no prunings of
// their own included, under the identity map whose unchanged count keeps their
// same-INDEX operands — plus the shared walker behaviors the per-body walk
// depends on. The CFF fixtures are assembled from the wire format directly
// (TN5176), never by asking the product to write them; expectations parse the
// emitted output bytes back through the typed read chain (the strict validator
// re-checks every tiling) and compare re-encoded operands against an independent
// minimal-form table (TN5177 §3.1), never against the producer.
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetSubrPruneCrossIndexTests
{
    // ---- wire-level helpers (the S4 test's builders) ------------------------------

    private static byte[] Num(int v) => v is >= -107 and <= 107
        ? [(byte)(v + 139)]
        : [(byte)28, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)];

    /// <summary>The two-byte positive push form (TN5177 §3.1), lead 247-250.</summary>
    private static byte[] Pos2(int v)
    {
        int remainder = v - 108;
        return [(byte)(247 + (remainder >> 8)), (byte)(remainder & 0xFF)];
    }

    /// <summary>The minimal push encoding (TN5177 §3.1): the test-side independent
    /// form table the re-encoded sites compare against.</summary>
    private static byte[] Minimal(int v) => v switch
    {
        >= -107 and <= 107 => [(byte)(v + 139)],
        >= 108 and <= 1131 => Pos2(v),
        >= -1131 and <= -108 => Neg2(v),
        >= -32768 and <= 32767 => [0x1C, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)],
        _ => [0xFF, (byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF),
            (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)],
    };

    /// <summary>The two-byte negative push form (TN5177 §3.1), lead 251-254.</summary>
    private static byte[] Neg2(int v)
    {
        int remainder = -v - 108;
        return [(byte)(251 + (remainder >> 8)), (byte)(remainder & 0xFF)];
    }

    private static byte[] Index(params byte[][] items)
    {
        if (items.Length == 0) return [0x00, 0x00];
        var offsets = new List<int> { 1 };
        foreach (var item in items) offsets.Add(offsets[^1] + item.Length);
        int width = 1;
        while (offsets[^1] >= (1 << (8 * width))) width++;
        // The count byte rides the mask, never a narrowing cast: the checked
        // builds throw on (byte)1300.
        var bytes = new List<byte> { (byte)(items.Length >> 8), (byte)(items.Length & 0xFF),
            (byte)width };
        foreach (int offset in offsets)
            for (int i = width - 1; i >= 0; i--)
                bytes.Add((byte)((offset >> (8 * i)) & 0xFF));
        foreach (var item in items) bytes.AddRange(item);
        return [.. bytes];
    }

    /// <summary>A name-keyed CFF1 table over the given charset SID column with the
    /// given glyph programs and subroutine INDEXes (layout: Header|Name|Top|String
    /// |GlobalSubrs|charset|CharStrings|Private|LocalSubrs, the strict tiling with
    /// the Subrs offset pinned to the Private DICT size).</summary>
    private static byte[] NameKeyedCff(int[] sids, byte[][] programs,
        byte[][]? locals = null, byte[][]? globals = null)
    {
        byte[] header = [0x01, 0x00, 0x04, 0x01];
        byte[] name = Index("S4XSynthetic"u8.ToArray());
        byte[] strings = Index();
        byte[] globalIndex = Index(globals ?? []);
        byte[] charset = [(byte)0, .. sids.Skip(1).SelectMany(s =>
            new[] { (byte)((s >> 8) & 0xFF), (byte)(s & 0xFF) })];
        byte[] charstrings = Index(programs);
        byte[] localIndex = Index(locals ?? []);
        byte[] privateDict = locals is null
            ? [0x8b, 0x14]
            : [0x8b, 0x14, 29, 0, 0, 0, 0x08, 19];
        int privateSize = privateDict.Length;

        int at = header.Length + name.Length + 28 + strings.Length + globalIndex.Length;
        int charsetAt = at;
        int charStringsAt = charsetAt + charset.Length;
        int privateAt = charStringsAt + charstrings.Length;
        var top = new List<byte>();
        void Entry(int op, int value)
        {
            top.AddRange([29, (byte)((value >> 24) & 0xFF), (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF), (byte)op]);
        }
        Entry(17, charStringsAt);
        Entry(15, charsetAt);
        top.AddRange([29, (byte)((privateSize >> 24) & 0xFF),
            (byte)((privateSize >> 16) & 0xFF), (byte)((privateSize >> 8) & 0xFF),
            (byte)privateSize,
            29, (byte)((privateAt >> 24) & 0xFF), (byte)((privateAt >> 16) & 0xFF),
            (byte)((privateAt >> 8) & 0xFF), (byte)(privateAt & 0xFF), 18]);
        byte[] topIndex = Index([.. top]);
        Assert.Equal(28, topIndex.Length);

        var bytes = new List<byte>();
        bytes.AddRange(header);
        bytes.AddRange(name);
        bytes.AddRange(topIndex);
        bytes.AddRange(strings);
        bytes.AddRange(globalIndex);
        bytes.AddRange(charset);
        bytes.AddRange(charstrings);
        bytes.AddRange(privateDict);
        if (locals is not null) bytes.AddRange(localIndex);
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
    };

    private static byte[] Output(SubsetResult result)
    {
        byte[] output = new byte[result.Font.OutputLength];
        ReadStatus status = result.Font.TryCopyTo(output, out int written, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(output.Length, written);
        return output;
    }

    /// <summary>Parses the emitted sfnt's `CFF ` table back through the typed read
    /// chain (the output oracle: the strict validator re-checks every tiling).</summary>
    private static Cff1Table OutputCff(SubsetResult result)
    {
        Assert.True(TryOutputCffBytes(result, out byte[] cff));
        Assert.Equal(ReadStatus.Done, Cff1View.TryOpen(cff, out var view, out _, default));
        Assert.Equal(ReadStatus.Done,
            view.TryRead(out var model, out _, out _, out _, new BuildBudget()));
        return model!;
    }

    private static bool TryOutputCffBytes(SubsetResult result, out byte[] cff)
    {
        byte[] sfnt = Output(result);
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        cff = [];
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            if (BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record)) != 0x43464620)
                continue;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 12));
            cff = sfnt[offset..(offset + length)];
            return true;
        }
        return false;
    }

    // ---- the null-slot survivors rewrite their cross-INDEX calls (§3.3) -----------

    [Fact]
    public void LocalSurvivorRewritesItsStaleGlobalCall()
    {
        // The local INDEX keeps every body (null slot) while the global INDEX
        // prunes body 0: the surviving local's callgsubr still renumbers — the
        // stale operand -106 would resolve past the one-body output INDEX and
        // fault at run time. The identity slot rewrites the body under the
        // unchanged local count, so only the cross-INDEX site moves -106 -> -107.
        byte[][] locals = [[0x21, 0x1d, 0x0b]];           // callgsubr 1, return
        byte[][] globals = [[0x0b], [0x0b]];
        byte[] glyph = [.. Num(-107), 10, 14];            // callsubr 0
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [[14], glyph], locals: locals, globals: globals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected}");
        Assert.NotNull(result);
        Assert.Equal(1, report!.SubrPrunedGlobal);
        Assert.Empty(report.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(1, cff.GlobalSubrCount);
        Assert.Equal(1, cff.LocalSubrCount);
        // The rewritten cross-INDEX site: global 1 renumbers to 0, operand -107.
        Assert.Equal([0x20, 0x1d, 0x0b], cff.LocalSubrs[0].ToArray());
        // The glyph's same-INDEX site keeps its bytes (the value rule).
        Assert.Equal(glyph, cff.CharStrings[1].ToArray());
        Assert.Equal([0x0b], cff.GlobalSubrs[0].ToArray());
    }

    [Fact]
    public void GlobalSurvivorRewritesItsRenumberedLocalCall()
    {
        // The reverse face: the global INDEX keeps every body (null slot) while
        // the local INDEX prunes two of three bodies. The surviving global's
        // callsubr renumbers — local 1 moves to 0, operand -106 -> -107 — which
        // the verbatim ride used to strand pointing past the one-body output.
        byte[][] globals = [[0x21, 0x0a, 0x0b]];          // callsubr 1, return
        byte[][] locals = [[0x0b], [0x0b], [0x0b]];
        byte[] glyph = [.. Num(-107), 29, 14];            // callgsubr 0
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [[14], glyph], locals: locals, globals: globals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected}");
        Assert.NotNull(result);
        Assert.Equal(0, report!.SubrPrunedGlobal);
        Assert.Equal([2], report.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(1, cff.GlobalSubrCount);
        Assert.Equal(1, cff.LocalSubrCount);
        // The rewritten cross-INDEX site inside the global body.
        Assert.Equal([0x20, 0x0a, 0x0b], cff.GlobalSubrs[0].ToArray());
        Assert.Equal([0x0b], cff.LocalSubrs[0].ToArray());
        // The glyph's same-INDEX site keeps its bytes (the value rule).
        Assert.Equal(glyph, cff.CharStrings[1].ToArray());
    }

    // ---- the shortint emitter's full byte domain (the re-encode overflow) ---------

    [Fact]
    public void RecodedSitePastTheTwoByteCeilingSurvivesTheShortintLowByte()
    {
        // 2265 one-byte global bodies (the 1131 tier); the glyph calls 1..2264
        // through Num's 28 shortint form (operand = index − 1131). Global 0
        // prunes and the survivor count 2264 stays on the 1131 tier, so the
        // last site re-encodes 1133 -> 1132 — one past the two-byte positive
        // ceiling, the first site the emitter serves from the shortint branch,
        // whose low byte the checked builds narrowed with a bare cast.
        byte[][] globals = Enumerable.Range(0, 2265).Select(_ => (byte[])[0x0b])
            .ToArray();
        var glyph = new List<byte>();
        for (int index = 1; index <= 2264; index++)
        {
            glyph.AddRange(Num(index - 1131));
            glyph.Add(29);
        }
        glyph.Add(14);
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [[14], [.. glyph]], globals: globals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out var diagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={diagnostic.Code} detail={diagnostic.Detail} expected={diagnostic.Expected} field={diagnostic.Location.Field} off={diagnostic.Location.Offset} ord={diagnostic.Location.RecordOrdinal}");
        Assert.NotNull(result);
        Assert.Equal(1, report!.SubrPrunedGlobal);
        var cff = OutputCff(result!);
        Assert.Equal(2264, cff.GlobalSubrCount);
        // Every site re-encodes over the 1131 tier onto the independent form
        // table; the whole-program comparison sweeps the four push forms and
        // pins the 1132 tail — the overflow site itself.
        var expected = new List<byte>();
        for (int index = 1; index <= 2264; index++)
        {
            expected.AddRange(Minimal(index - 1132));
            expected.Add(29);
        }
        expected.Add(14);
        Assert.Equal(expected, cff.CharStrings[1].ToArray());
        // The boundary sites at their own output offsets: the two-byte form's
        // low-byte seam (255/256), its positive ceiling (1131) and the first
        // shortint value (1132).
        byte[] program = cff.CharStrings[1].ToArray();
        int OperandAt(int index)
        {
            int at = 0;
            for (int k = 1; k < index; k++) at += Minimal(k - 1132).Length + 1;
            return at;
        }
        foreach (int index in new[] { 1387, 1388, 2263, 2264 })
        {
            int at = OperandAt(index);
            byte[] form = Minimal(index - 1132);
            Assert.Equal(form, program[at..(at + form.Length)]);
        }
        Assert.Equal([0xF7, 0x93], Minimal(255));
        Assert.Equal([0xF7, 0x94], Minimal(256));
        Assert.Equal([0xFA, 0xFF], Minimal(1131));
        Assert.Equal([0x1C, 0x04, 0x6C], Minimal(1132));
    }

    [Fact]
    public void RecodedSitesCrossThe255LowByteSeamInTwoByteForm()
    {
        // 400 local bodies (the 107 tier); the glyph calls 1..399, body 0
        // prunes, and the survivor count 399 stays on the 107 tier: sites 363
        // and 364 re-encode to new operands 255 and 256 — the two-byte positive
        // form's low byte carrying across its seam.
        byte[][] locals = Enumerable.Range(0, 400).Select(_ => (byte[])[0x0b])
            .ToArray();
        var glyph = new List<byte>();
        for (int index = 1; index <= 399; index++)
        {
            glyph.AddRange(Num(index - 107));
            glyph.Add(10);
        }
        glyph.Add(14);
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [[14], [.. glyph]], locals: locals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([1], report!.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(399, cff.LocalSubrCount);
        var expected = new List<byte>();
        for (int index = 1; index <= 399; index++)
        {
            expected.AddRange(Minimal(index - 108));
            expected.Add(10);
        }
        expected.Add(14);
        Assert.Equal(expected, cff.CharStrings[1].ToArray());
    }

    [Fact]
    public void RecodedSiteLandsOnTheTwoBytePositiveCeiling()
    {
        // 1240 local bodies (the 1131 tier); the glyph calls 1..1239, body 0
        // prunes, and the survivor count 1239 drops to the 107 tier: the last
        // site re-encodes to new operand 1131 — the two-byte positive ceiling.
        byte[][] locals = Enumerable.Range(0, 1240).Select(_ => (byte[])[0x0b])
            .ToArray();
        var glyph = new List<byte>();
        for (int index = 1; index <= 1239; index++)
        {
            glyph.AddRange(Num(index - 1131));
            glyph.Add(10);
        }
        glyph.Add(14);
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [[14], [.. glyph]], locals: locals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([1], report!.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(1239, cff.LocalSubrCount);
        var expected = new List<byte>();
        for (int index = 1; index <= 1239; index++)
        {
            expected.AddRange(Minimal(index - 108));
            expected.Add(10);
        }
        expected.Add(14);
        Assert.Equal(expected, cff.CharStrings[1].ToArray());
    }

    // ---- the walker's roll (TN5177's stack tool) -----------------------------------

    [Fact]
    public void RollRotatesTheTopNByJBeforeTheSeacFormReads()
    {
        // TN5177's "4 2 roll" turns [65, 194, 66, 193] into [66, 193, 65, 194].
        // The walker only popped the control pair before, so the endchar seac
        // form read bchar/achar off the unrotated top — B + grave, SIDs 35/124 —
        // and the closure published the wrong components. The rotated top is
        // 65/194: A + acute, SIDs 34/125, the additions the frozen order takes.
        int[] sids = [0, 34, 125, 35, 124, 171];
        byte[] rollSeac = [.. Num(65), .. Num(194), .. Num(66), .. Num(193),
            .. Num(4), .. Num(2), 0x0c, 0x1f, 0x0e];
        var programs = new byte[6][];
        for (int gid = 0; gid < 5; gid++) programs[gid] = [14];
        programs[5] = rollSeac;
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(6)),
            (SubsetFontFixture.CffTag, NameKeyedCff(sids, programs)));
        var status = SubsetOperation.TryPrepare(Request(face, [5],
            SubsetMode.CompactRemap), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
        var cff = OutputCff(result!);
        Assert.Equal(4, cff.NumGlyphs);
        // The additions' SIDs ride the output charset; Aacute's program copies
        // verbatim (no subrs on the face, the plan is null).
        Assert.Equal([0, 34, 125, 171], cff.CharsetSids.ToArray());
        Assert.Equal(rollSeac, cff.CharStrings[3].ToArray());
    }

    // ---- the implicit vstems the mask widths must count (TN5177 §4.3) --------------

    [Fact]
    public void HintmaskCountsTheImplicitVstemsLeftOnTheStack()
    {
        // Eight declared hstem pairs, then sixteen stem arguments left standing
        // on the stack when hintmask runs — the implicit-vstem form the spec
        // allows after hstemhm. The mask covers h and v stems together: two
        // bytes. The walk that wiped the stack uncounted read width 1 and
        // parsed the second mask byte (0x0A) as callsubr over an empty stack —
        // refusing a legal glyph.
        var stems = new List<byte>();
        for (int i = 0; i < 16; i++) stems.AddRange(Num(50));
        byte[] glyph = [.. stems, (byte)18, .. stems, (byte)19, 0xFF, 0x0A,
            .. Num(0), .. Num(0), (byte)21, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34], [[14], glyph])));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        // Nothing prunes (no subrs on the face): the program rides verbatim.
        var cff = OutputCff(result!);
        Assert.Equal(glyph, cff.CharStrings[1].ToArray());
    }
}
