// Subr prune pipeline tests (S4 card §5 ③ acceptance; §6's fixture families at unit
// scale): bias tiers, the spec operand convention, the four push forms on both sides
// of the re-encoding (decode by value, emit minimal), prune+renumber+re-encode over
// name-keyed and multi-FD CID faces, the key-delete face, the nested/ends-at-call
// forms, the cycle guard, the stem memo's mask widths, the refusal keys A1/A2/A3 and
// the §3.5 identity anchor. The CFF fixtures are assembled from the wire format
// directly (TN5176), never by asking the product to write them; expectations parse
// the emitted output bytes back through the typed read chain (the strict validator
// re-checks every tiling), and the re-encoded operands compare against an
// independent minimal-form table (TN5177 §3.1), never against the producer.
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetSubrPruneTests
{
    // ---- wire-level helpers (the S3 test's builders, subr-count parameterized) ----

    private static byte[] Num(int v) => v is >= -107 and <= 107
        ? [(byte)(v + 139)]
        : [(byte)28, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)];

    /// <summary>The two-byte positive push form (TN5177 §3.1), lead 247-250.</summary>
    private static byte[] Pos2(int v)
    {
        int remainder = v - 108;
        return [(byte)(247 + (remainder >> 8)), (byte)(remainder & 0xFF)];
    }

    /// <summary>The two-byte negative push form (TN5177 §3.1), lead 251-254.</summary>
    private static byte[] Neg2(int v)
    {
        int remainder = -v - 108;
        return [(byte)(251 + (remainder >> 8)), (byte)(remainder & 0xFF)];
    }

    /// <summary>The minimal push encoding (TN5177 §3.1): the test-side independent
    /// form table the re-encoded sites compare against — one byte over [-107, 107],
    /// the two-byte positive/negative ranges, the 28 shortint beyond, the 255 fixed
    /// form as the unreachable total. Num rides this for [-107, 107] and delegates
    /// the rest to the 28 form; the re-encoder's choices are pinned per tier below.
    /// </summary>
    private static byte[] Minimal(int v) => v switch
    {
        >= -107 and <= 107 => [(byte)(v + 139)],
        >= 108 and <= 1131 => Pos2(v),
        >= -1131 and <= -108 => Neg2(v),
        >= -32768 and <= 32767 => [0x1C, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)],
        _ => [0xFF, (byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF),
            (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)],
    };

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
        byte[] name = Index("S4Synthetic"u8.ToArray());
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

    /// <summary>The CID flavor of the output probe: the emitted `CFF ` read back as
    /// the CID-keyed model (the strict validator re-checks every tiling).</summary>
    private static CidCff1Table OutputCidCff(SubsetResult result)
    {
        Assert.True(TryOutputCffBytes(result, out byte[] cff));
        Assert.Equal(ReadStatus.Done, Cff1View.TryOpen(cff, out var view, out _, default));
        Assert.Equal(ReadStatus.Done,
            view.TryRead(out _, out var cidKeyed, out _, out _, new BuildBudget()));
        Assert.NotNull(cidKeyed);
        return cidKeyed!;
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

    /// <summary>The endchar-only program and the charset column over it: the minimal
    /// faces ride [0, 34("A"), 35("B"), ...] so selections stay identity-ordered.</summary>
    private static readonly byte[] Endchar = [14];

    // ---- bias tiers and the spec operand convention (§6 case 1) -------------------

    [Fact]
    public void SubrBiasTiersAreTheSpecTrip()
    {
        Assert.Equal(107, SubsetOperation.SubrBias(0));
        Assert.Equal(107, SubsetOperation.SubrBias(1));
        Assert.Equal(107, SubsetOperation.SubrBias(1239));
        Assert.Equal(1131, SubsetOperation.SubrBias(1240));
        Assert.Equal(1131, SubsetOperation.SubrBias(33899));
        Assert.Equal(32768, SubsetOperation.SubrBias(33900));
        Assert.Equal(32768, SubsetOperation.SubrBias(65536));
    }

    [Fact]
    public void PruneKeepsSurvivorValueUnchangedOperands()
    {
        // Case (a): count=1200 (107 tier), the glyph calls index 0 through the spec
        // operand -107. The survivor set is {0}, the new bias over count 1 is 107
        // again, so the site's operand value — and its byte — is unchanged: the
        // re-encoding judgment is by value, not width (card §6 case 1a).
        byte[][] locals = Enumerable.Range(0, 1200).Select(_ => (byte[])[0x8b, 0x0b])
            .ToArray();
        byte[][] globals = Enumerable.Range(0, 1200).Select(_ => (byte[])[0x8b, 0x0b])
            .ToArray();
        byte[] glyph = [.. Num(100), .. Num(100), 21, .. Num(-107), 10,
            .. Num(-107), 29, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [Endchar, glyph], locals: locals, globals: globals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(1199, report!.SubrPrunedGlobal);
        Assert.Equal([1199], report.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(1, cff.GlobalSubrCount);
        Assert.Equal(1, cff.LocalSubrCount);
        // The glyph program: every byte identical, the two call operands kept.
        Assert.Equal(glyph, cff.CharStrings[1].ToArray());
        // The survivors are old bodies 0, byte-identical (no calls inside).
        Assert.Equal(locals[0], cff.LocalSubrs[0].ToArray());
        Assert.Equal(globals[0], cff.GlobalSubrs[0].ToArray());
    }

    [Fact]
    public void PruneReEncodesAcrossBiasTierFlip()
    {
        // Case (b): count=1300 (1131 tier), the glyph calls index 0 through operand
        // -1131 (the two-byte negative form). The survivor set {0} renumbers to 0,
        // the new bias over count 1 drops to 107, so the site re-encodes -1131 ->
        // -107: value and encoding form both change (card §6 case 1b).
        byte[][] locals = Enumerable.Range(0, 1300).Select(_ => (byte[])[0x8b, 0x0b])
            .ToArray();
        byte[] glyph = [.. Num(100), .. Num(100), 21, .. Num(-1131), 10, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [Endchar, glyph], locals: locals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        // The face carries no Global Subrs INDEX: the global count stays 0 and the
        // local INDEX prunes 1299 of its 1300 bodies.
        Assert.Equal(0, report!.SubrPrunedGlobal);
        Assert.Equal([1299], report.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(1, cff.LocalSubrCount);
        byte[] expectedGlyph = [.. Num(100), .. Num(100), 21, .. Num(-107), 10, 14];
        Assert.Equal(expectedGlyph, cff.CharStrings[1].ToArray());
    }

    [Fact]
    public void PruneFlipsDownFromTheTopBiasTier()
    {
        // Case (c): count=34000 (the 32768 tier — the tier source-han lacks), a
        // glyph chain calling 1240 distinct bodies keeps exactly those survivors,
        // so the new count 1240 re-biases to 1131 and every site re-encodes
        // (old operand = index - 32768 over the shortint 28 form; new operand =
        // new index - 1131, which sweeps three of the four push forms in one
        // program). The bodies are bare returns so the shared stack stays empty at
        // the endchar — values left behind by the called bodies would read as the
        // deprecated seac form (the S3 closure's frozen semantics, scanned ahead of
        // the prune) and refuse on the unmapped code pair. The expectations ride
        // the test-side Minimal table.
        byte[][] globals = Enumerable.Range(0, 34000).Select(_ => (byte[])[0x0b])
            .ToArray();
        List<byte> glyph = [.. Num(100), .. Num(100), 21];
        for (int index = 0; index < 1240; index++)
        {
            glyph.AddRange(Num(index - 32768));
            glyph.Add(29);
        }
        glyph.Add(14);
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [Endchar, [.. glyph]], globals: globals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(34000 - 1240, report!.SubrPrunedGlobal);
        var cff = OutputCff(result!);
        Assert.Equal(1240, cff.GlobalSubrCount);
        List<byte> expected = [.. Num(100), .. Num(100), 21];
        for (int index = 0; index < 1240; index++)
        {
            expected.AddRange(Minimal(index - 1131));
            expected.Add(29);
        }
        expected.Add(14);
        Assert.Equal(expected, cff.CharStrings[1].ToArray());
        // The form boundaries the sweep crosses, spelled out at their own output
        // offsets (each site's width rides Minimal, so the stride is not uniform):
        // new index 0 lands on the two-byte negative floor, 1023/1024 straddle its
        // seam with the one-byte form, and 1239 is the two-byte positive floor.
        byte[] program = cff.CharStrings[1].ToArray();
        int SiteAt(int index)
        {
            int at = 3;
            for (int k = 0; k < index; k++) at += Minimal(k - 1131).Length + 1;
            return at;
        }
        int at0 = SiteAt(0), at1023 = SiteAt(1023), at1024 = SiteAt(1024),
            at1239 = SiteAt(1239);
        Assert.Equal(Neg2(-1131), program[at0..(at0 + 2)]);
        Assert.Equal(Neg2(-108), program[at1023..(at1023 + 2)]);
        Assert.Equal(Num(-107), program[at1024..(at1024 + 1)]);
        Assert.Equal(Pos2(108), program[at1239..(at1239 + 2)]);
    }

    // ---- nested bodies and the ends-at-call form (§6 case 1d) ---------------------

    [Fact]
    public void NestedSubrsEndAtCallRewriteEveryFrame()
    {
        // Case (d): depth-3 nesting over a 1300-body local INDEX (1131 tier) with
        // the charstring terminating at a call — the called body's endchar ends the
        // program (TN5177 §4.2 Note 6). The chain 0 -> 1 -> 2 survives, the 1297
        // unreferenced bodies prune, the tier flips 1131 -> 107 and every frame —
        // glyph and both intermediate bodies — re-encodes across it.
        byte[][] locals = Enumerable.Range(0, 1300).Select(_ => (byte[])[0x8b, 0x0b])
            .ToArray();
        locals[0] = [.. Num(-1131 + 1), 10];            // calls index 1
        locals[1] = [.. Num(-1131 + 2), 10];            // calls index 2
        byte[] ender = [.. Num(100), .. Num(100), 21, 14];
        locals[2] = ender;                              // the depth-3 body ends it
        byte[] glyph = [.. Num(-1131 + 0), 10];         // index 0, ends the glyph
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [Endchar, glyph], locals: locals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([1297], report!.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(3, cff.LocalSubrCount);
        // The survivors keep their relative order and renumber under the 107 tier:
        // every call site's value and encoding form change together.
        Assert.Equal([.. Num(-106), 10], cff.LocalSubrs[0].ToArray());
        Assert.Equal([.. Num(-105), 10], cff.LocalSubrs[1].ToArray());
        Assert.Equal(ender, cff.LocalSubrs[2].ToArray());
        Assert.Equal([.. Num(-107), 10], cff.CharStrings[1].ToArray());
    }

    [Fact]
    public void CycleBranchTerminatesAndPrunesOnlyTheUnreachedBody()
    {
        // The cycle guard (§2.1): two locals reference each other — the second visit
        // of a body already on the call path ends that branch instead of looping —
        // and the glyph's descent still collects both. The third body no one calls
        // prunes; the survivors renumber identity so every operand's value (and
        // byte) stays.
        byte[][] locals =
        [
            [.. Num(-106), 10, 11],  // 0: calls 1, returns
            [.. Num(-107), 10, 11],  // 1: calls 0, returns
            [0x8b, 0x0b],            // 2: unreferenced
        ];
        byte[] glyph = [.. Num(-107), 10, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [Endchar, glyph], locals: locals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([1], report!.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(2, cff.LocalSubrCount);
        Assert.Equal(locals[0], cff.LocalSubrs[0].ToArray());
        Assert.Equal(locals[1], cff.LocalSubrs[1].ToArray());
        Assert.Equal(glyph, cff.CharStrings[1].ToArray());
    }

    [Fact]
    public void MaskWidthReadsTheCalledBodyStemsAcrossTheRewrite()
    {
        // The called body's stems (§2.1): the body declares one hint, and the
        // glyph's hintmask width depends on that contribution — the rewrite walk
        // executes the called body's tokens on the shared stack at the call site,
        // so the mask byte is sized by the runtime count and the program re-emits
        // byte-identical (the operand's value is unchanged too). A missed or
        // miscounted contribution derails the mask skip — either way the
        // assertions fail.
        byte[] stemBody = [.. Num(1), .. Num(1), 1, 11];
        byte[] glyph = [.. Num(-107), 10, 19, 0x80, .. Num(100), .. Num(100), 21, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [Endchar, glyph], locals: [stemBody, [0x8b, 0x0b]])));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([1], report!.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(1, cff.LocalSubrCount);
        Assert.Equal(stemBody, cff.LocalSubrs[0].ToArray());
        Assert.Equal(glyph, cff.CharStrings[1].ToArray());
    }

    [Fact]
    public void HintHelperSubrCountsStemsPerCaller()
    {
        // A hint-helper subr (the real-font shape: a body whose hstemhm consumes
        // the caller's passed arguments — stix-two-math's local 1039) has an
        // entry-DEPENDENT stem contribution: the first caller's twelve arguments
        // count six hints, the second caller's two count one. A body-keyed summary
        // replayed the first caller's count everywhere, so the second glyph's mask
        // skip spanned a byte that was never mask and the walk derailed on its own
        // program; the call-site execution counts each caller's own pairs and both
        // programs re-emit byte-identical.
        byte[] helper = [18, .. Num(0), .. Num(0), 11]; // hstemhm; 0 0; return
        byte[] twelveArgs = [.. Num(1), .. Num(2), .. Num(3), .. Num(4), .. Num(5),
            .. Num(6), .. Num(7), .. Num(8), .. Num(9), .. Num(10), .. Num(11),
            .. Num(12)];
        // Glyph 1: six pairs ride the helper's hstemhm, the helper's own 0 0 and
        // the glyph's 100 100 make two more pairs — hints 8, mask one byte.
        byte[] glyph1 = [.. twelveArgs, .. Num(-107), 10, .. Num(100), .. Num(100),
            19, 0x80, .. Num(0), .. Num(0), 21, 14];
        // Glyph 2: one pair rides the helper, two more pairs follow — hints 3,
        // mask one byte; the replayed six would have skipped two.
        byte[] glyph2 = [.. Num(1), .. Num(2), .. Num(-107), 10, .. Num(20),
            .. Num(30), 19, 0x80, .. Num(0), .. Num(0), 21, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34, 35],
                [Endchar, glyph1, glyph2], locals: [helper])));
        var status = SubsetOperation.TryPrepare(Request(face, [1, 2]), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var cff = OutputCff(result!);
        Assert.Equal(glyph1, cff.CharStrings[1].ToArray());
        Assert.Equal(glyph2, cff.CharStrings[2].ToArray());
        Assert.Equal(helper, cff.LocalSubrs[0].ToArray());
    }

    [Fact]
    public void DeadTailAfterACalledEndcharCopiesVerbatim()
    {
        // The called body's endchar ends the whole program (TN5177 §4.2 Note 6):
        // the glyph's trailing bytes are dead. The scan's frames stop with the
        // callee — no fall-off refusal on the glyph — and the rewrite preserves
        // the dead tail verbatim without executing its tokens as live ones.
        byte[] ender = [.. Num(1), .. Num(1), 1, 14];
        byte[] glyph = [.. Num(-107), 10, .. Num(9), .. Num(9), 21, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [Endchar, glyph], locals: [ender])));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var cff = OutputCff(result!);
        Assert.Equal(ender, cff.LocalSubrs[0].ToArray());
        Assert.Equal(glyph, cff.CharStrings[1].ToArray());
    }

    // ---- the four push forms across the decode/re-encode seam (§5 ③'s table) ------

    [Fact]
    public void CallOperandsDecodeByValueAcrossTheInputForms()
    {
        // One glyph, four input encodings over a 1300-body local INDEX (the 1131
        // tier, so operand = index - 1131): the one-byte form (-107 -> index 1024),
        // the two-byte negative form (-1129 -> index 2), the two-byte positive form
        // (109 -> index 1240) and the 28 shortint (-1131 -> index 0) — the last
        // body also called through its two-byte negative encoding (-1131 rides both
        // forms' domain), which is the decode-by-value pin: both sites reach the
        // same survivor. The re-encode emits the minimal form per new index: the
        // 1024/0 sites renumber to 2/0 and the 2/1240 sites to 1/3, so every site's
        // value changes and re-encodes to the one-byte form — the 28-form site
        // shrinking 3 -> 1 bytes. The bodies are bare returns so the shared stack
        // never carries four literals into the endchar (the S3 seac form's frozen
        // read).
        byte[][] locals = Enumerable.Range(0, 1300).Select(_ => (byte[])[0x0b])
            .ToArray();
        byte[] glyph = [.. Num(-107), 10, .. Neg2(-1129), 10, .. Pos2(109), 10,
            .. Neg2(-1131), 10, .. Num(-1131), 10, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [Endchar, glyph], locals: locals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([1296], report!.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(4, cff.LocalSubrCount);
        byte[] expectedGlyph = [.. Num(-105), 10, .. Num(-106), 10, .. Num(-104), 10,
            .. Num(-107), 10, .. Num(-107), 10, 14];
        Assert.Equal(expectedGlyph, cff.CharStrings[1].ToArray());
    }

    // ---- the null-slot face: an unpruned INDEX keeps its own bias -----------------

    [Fact]
    public void NullLocalSlotKeepsValueAndThePerFdReportStaysEmpty()
    {
        // The local INDEX (one body, fully reachable) parks a null plan slot while
        // the globals prune: the local site's operand keeps its value and byte (the
        // walker reads the unchanged bias off the called table) and the global site
        // re-encodes across the tier flip - and the report's per-FD array stays
        // empty, the member contract's "no local INDEX pruned anywhere" face.
        byte[] glyph = [.. Num(100), .. Num(100), 21, .. Num(-107), 10,
            .. Num(-1131), 29, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [Endchar, glyph], locals: [[0x0b]],
                globals: Enumerable.Range(0, 1300).Select(_ => (byte[])[0x0b])
                    .ToArray())));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(1299, report!.SubrPrunedGlobal);
        Assert.Empty(report.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(1, cff.LocalSubrCount);
        byte[] expectedGlyph = [.. Num(100), .. Num(100), 21, .. Num(-107), 10,
            .. Num(-107), 29, 14];
        Assert.Equal(expectedGlyph, cff.CharStrings[1].ToArray());
    }

    // ---- the key-delete face (§6 case 1e) ------------------------------------------

    [Fact]
    public void FullPruneDeletesBothIndexesAndTheSubrsKey()
    {
        // Case (e): the retained glyph calls nothing, so both INDEXes prune to zero
        // survivors and the Private DICT loses its Subrs key; the compiler emits
        // the zero-count global INDEX and drops the local INDEX entirely.
        byte[][] locals = [[0x8b, 0x0b]];
        byte[][] globals = [[0x8b, 0x0b]];
        byte[] caller = [.. Num(100), .. Num(100), 21, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34],
                [Endchar, caller], locals: locals, globals: globals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(1, report!.SubrPrunedGlobal);
        Assert.Equal([1], report.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(0, cff.GlobalSubrCount);
        Assert.Equal(0, cff.LocalSubrCount);
        Assert.False(cff.HasLocalSubrs);
        // The Private DICT carries no Subrs key any more (op 19 gone).
        Assert.DoesNotContain(cff.PrivateDict.ToArray(), entry => entry.Operator == 19);
        // The caller's bytes stay verbatim.
        Assert.Equal(caller, cff.CharStrings[1].ToArray());
    }

    // ---- the identity anchor (§3.5, §6 case 3) --------------------------------------

    [Fact]
    public void FullReachabilityIsByteIdentityWithTheVerbatimPath()
    {
        // Every body referenced, full select: the plan is null and the output keeps
        // the input's charstring bytes and both INDEXes byte for byte (the S1c
        // verbatim path, the §3.5 regression anchor). The subr bodies end with
        // return, so every call site stays live — a body ending with endchar would
        // terminate the caller's graph there (TN5177 §4.2 Note 6) and strand its
        // tail's dead sites behind a pruned body.
        byte[] local0 = [.. Num(100), 0x0b];
        byte[] local1 = [.. Num(200), 0x0b];
        byte[] global0 = [.. Num(50), 0x0b];
        byte[] glyph0 = [.. Num(-107), 10, 14];
        byte[] glyph1 = [.. Num(-106), 10, .. Num(-107), 29, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34, 35],
                [Endchar, glyph0, glyph1],
                locals: [local0, local1], globals: [global0])));
        var status = SubsetOperation.TryPrepare(Request(face, [1, 2]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(0, report!.SubrPrunedGlobal);
        Assert.Empty(report.SubrPrunedLocalByFd);
        var cff = OutputCff(result!);
        Assert.Equal(2, cff.LocalSubrCount);
        Assert.Equal(local0, cff.LocalSubrs[0].ToArray());
        Assert.Equal(local1, cff.LocalSubrs[1].ToArray());
        Assert.Equal(global0, cff.GlobalSubrs[0].ToArray());
        Assert.Equal(glyph0, cff.CharStrings[1].ToArray());
        Assert.Equal(glyph1, cff.CharStrings[2].ToArray());
        // §3.5's byte identity is with the S1c verbatim path's OUTPUT: the plan is
        // null, so the rebuild keeps the model's CharStrings and both subr INDEXes
        // byte for byte (pinned above) and the compiler re-emits the DICT operands
        // in its canonical minimal form — the whole-table comparison against the
        // prepared expectation is the S4 gate's §6.3 fixture case.
    }

    // ---- the members the CLI receipt projects ---------------------------------------

    [Fact]
    public void SeacClosureAndPruneCountsRideOneCompactReport()
    {
        // The receipt projects the seac additions and both prune counts off one
        // report, so a single compact request must fill all three together: the
        // retained seac glyph pulls A and acute into the closure while the two
        // uncalled local bodies prune (the S3/S4 members the CLI left unprojected).
        int[] sids = [0, 34, 125, 171];
        byte[] seac = [.. Num(0), .. Num(0), .. Num(65), .. Num(194), 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(4)),
            (SubsetFontFixture.CffTag, NameKeyedCff(sids,
                [Endchar, Endchar, Endchar, seac],
                locals: [[0x8b, 0x0b], [0x8b, 0x0b]])));
        var status = SubsetOperation.TryPrepare(Request(face, [3]), out _,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
        Assert.Equal(0, report.SubrPrunedGlobal);
        Assert.Equal([2], report.SubrPrunedLocalByFd);
    }

    // ---- the refusal keys (§4, §6 case 4) -------------------------------------------

    [Fact]
    public void DivComputedCallOperandRefuses()
    {
        // A1: 3 1 div leaves an unknown value; the callsubr operand is not
        // statically decidable and the operation refuses (17), failure atomic.
        byte[] glyph = [.. Num(3), .. Num(1), 12, 12, 10, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34], [Endchar, glyph],
                locals: [[(byte)0x8b, (byte)0x0b]])));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Null(result);
    }

    [Fact]
    public void WildCallOperandRefuses()
    {
        // A2: operand 0 over a 1-body INDEX resolves to index 107 — outside
        // [0, 1); the face's own hanging call refuses with the operand in Detail.
        byte[] glyph = [.. Num(0), 10, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34], [Endchar, glyph],
                locals: [[(byte)0x8b, (byte)0x0b]])));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(0UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);
        Assert.Null(result);
    }

    [Fact]
    public void TruncatedCalledBodyRefuses()
    {
        // A3: the called body ends mid-token (a bare 28 shortint prefix); the scan
        // refuses instead of guessing the token's width.
        byte[] glyph = [.. Num(-107), 10, 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34], [Endchar, glyph],
                locals: [[28]])));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Null(result);
    }

    [Fact]
    public void ProgramWithoutTerminatorRefuses()
    {
        // A3: a CFF1 program that neither returns nor ends falls off its end —
        // TN5177 §4.2 Note 6's terminator obligation — and refuses.
        byte[] glyph = [.. Num(100), .. Num(100), 21];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34], [Endchar, glyph],
                locals: [[(byte)0x8b, (byte)0x0b]])));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Null(result);
    }

    [Fact]
    public void NestingPastTenFramesRefuses()
    {
        // A3: TN5177 appendix B bounds subr nesting at ten frames; an eleven-deep
        // chain (body i calls body i+1) refuses at the eleventh subr body instead
        // of modeling the unbounded runtime. Detail counts the frames the walk
        // held when the guard fired (the glyph frame included), Expected the bound.
        var locals = new byte[11][];
        for (int i = 0; i < 10; i++) locals[i] = [.. Num(i - 106), 10];
        locals[10] = [.. Num(100), .. Num(100), 21, 14];
        byte[] glyph = [.. Num(-107), 10];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34], [Endchar, glyph],
                locals: locals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(12UL, diagnostic.Detail);
        Assert.Equal(10UL, diagnostic.Expected);
        Assert.Null(result);
    }

    [Fact]
    public void TenFrameNestingSucceeds()
    {
        // The boundary itself is legal: ten chained subr frames, the tenth body
        // ending the program (§4.2 Note 6 again). Every body is reachable, so the
        // plan is null and the INDEX rides verbatim.
        var locals = new byte[10][];
        for (int i = 0; i < 9; i++) locals[i] = [.. Num(i - 106), 10];
        locals[9] = [.. Num(100), .. Num(100), 21, 14];
        byte[] glyph = [.. Num(-107), 10];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(2)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, 34], [Endchar, glyph],
                locals: locals)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var cff = OutputCff(result!);
        Assert.Equal(10, cff.LocalSubrCount);
    }

    // ---- multi-FD CID: per-FD bias, tier differences and the unreferenced FD ----
    // (§6 case 1f)

    [Fact]
    public void CidFacesPruneEachFdUnderItsOwnBias()
    {
        // FD0's local INDEX carries 1 body (107 tier), FD1's carries 1300 (1131
        // tier) and FD2's one body no retained glyph ever references: each FD's
        // glyph calls its own index 0, so FD0 and FD1 prune to one survivor under
        // their own old/new bias pair — FD1's site re-encodes across the tier flip
        // while FD0's bytes stay (the value rule again) — and FD2's INDEX prunes
        // to zero with its Private DICT losing the Subrs key (FDArray keeps all
        // three FDs, OQ-S4-4 lean).
        var builder = new CidPruneBuilder(fdCount: 3, numGlyphs: 3);
        builder.AddLocals(fd: 0, count: 1);
        builder.AddLocals(fd: 1, count: 1300);
        builder.AddLocals(fd: 2, count: 1);
        builder.SetGlyphProgram(0, [14], fd: 0);
        builder.SetGlyphProgram(1, [.. Num(100), .. Num(100), 21, .. Num(-107), 10, 14],
            fd: 0);
        builder.SetGlyphProgram(2, [.. Num(100), .. Num(100), 21, .. Num(-1131), 10, 14],
            fd: 1);
        var face = builder.Build();
        var status = SubsetOperation.TryPrepare(Request(face, [1, 2]), out var result,
            out var report, out var cidDiagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={cidDiagnostic.Code} detail={cidDiagnostic.Detail} expected={cidDiagnostic.Expected} field={cidDiagnostic.Location.Field} off={cidDiagnostic.Location.Offset} ord={cidDiagnostic.Location.RecordOrdinal}");
        Assert.NotNull(result);
        Assert.Equal([0, 1299, 1], report!.SubrPrunedLocalByFd);
        var cid = OutputCidCff(result!);
        // FD0 keeps its 1 body (glyph 1's bytes unchanged), FD1 keeps 1 body with
        // glyph 2's site re-encoded to -107, FD2's INDEX is gone with its key.
        Assert.Equal(1, cid[0].LocalSubrCount);
        Assert.Equal(1, cid[1].LocalSubrCount);
        Assert.False(cid[2].HasLocalSubrs);
        Assert.DoesNotContain(cid[2].PrivateEntries.ToArray(),
            entry => entry.Operator == 19);
        Assert.Equal(3, cid.FdCount);
        Assert.Equal(
            [.. Num(100), .. Num(100), 21, .. Num(-107), 10, 14],
            cid.CharStrings[1].ToArray());
            Assert.Equal(
                [.. Num(100), .. Num(100), 21, .. Num(-107), 10, 14],
                cid.CharStrings[2].ToArray());
    }

    [Fact]
    public void CidLocalSurvivorsRewriteUnderTheirOwnFdContext()
    {
        // The multi-FD context face: FD1's local 1 calls FD1's local 0 while
        // FD1's INDEX prunes one unreferenced body, and FD0's INDEX prunes all
        // (no retained glyph binds it). Rewriting FD1's survivors over the
        // zero-filled context column read FD0's pruned map and refused glyph 1's
        // first legal site with its operand in Detail; each body's own FD is the
        // context, and FD0's Subrs key goes with its emptied INDEX.
        var builder = new CidPruneBuilder(fdCount: 2, numGlyphs: 2);
        builder.SetLocals(fd: 0, [0x0b]);
        builder.SetLocals(fd: 1, [0x0b], [0x20, 0x0a, 0x0b], [0x0b]);
        builder.SetGlyphProgram(0, [14], fd: 0);
        builder.SetGlyphProgram(1, [0x21, 0x0a, 0x0e], fd: 1);
        var face = builder.Build();
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out var cidDiagnostic);
        Assert.True(status == ReadStatus.Done,
            $"status={status} code={cidDiagnostic.Code} detail={cidDiagnostic.Detail} expected={cidDiagnostic.Expected}");
        Assert.NotNull(result);
        Assert.Equal([1, 1], report!.SubrPrunedLocalByFd);
        var cid = OutputCidCff(result!);
        Assert.Equal(2, cid.FdCount);
        // FD0: the INDEX pruned to zero survivors, the Subrs key with it.
        Assert.False(cid[0].HasLocalSubrs);
        Assert.DoesNotContain(cid[0].PrivateEntries.ToArray(),
            entry => entry.Operator == 19);
        // FD1: both survivors kept under FD1's own map — the callsites' operand
        // values are unchanged (old and new bias both the 107 tier), so the
        // bodies re-emit byte-identical through the rewrite.
        Assert.Equal(2, cid[1].LocalSubrCount);
        Assert.Equal([0x0b], cid[1].LocalSubrs[0].ToArray());
        Assert.Equal([0x20, 0x0a, 0x0b], cid[1].LocalSubrs[1].ToArray());
        Assert.Equal([0x21, 0x0a, 0x0e], cid.CharStrings[1].ToArray());
    }

    /// <summary>A CID-keyed CFF1 wire builder (TN5176), the strict CID tiling:
    /// Header|Name|Top|String|GlobalSubrs|FDSelect(fmt0)|charset(fmt0)|private₀+
    /// local₀|…|FDArray|CharStrings, every Top DICT and FD DICT operand in the
    /// fixed 5-byte form so one measurement closes the layout, and per-FD Private
    /// sizes with each Subrs offset pinned to its own DICT's size.</summary>
    private sealed class CidPruneBuilder
    {
        private readonly int _fdCount;
        private readonly int _numGlyphs;
        private readonly int[] _fds;
        private readonly byte[][] _programs;
        private readonly byte[][][] _locals;

        internal CidPruneBuilder(int fdCount, int numGlyphs)
        {
            _fdCount = fdCount;
            _numGlyphs = numGlyphs;
            _fds = new int[numGlyphs];
            _programs = new byte[numGlyphs][];
            _locals = new byte[fdCount][][];
            for (int fd = 0; fd < fdCount; fd++) _locals[fd] = [];
        }

        internal void AddLocals(int fd, int count) =>
            _locals[fd] = Enumerable.Range(0, count).Select(_ => (byte[])[0x8b, 0x0b])
                .ToArray();

        /// <summary>Replaces an FD's whole local INDEX with explicit bodies (the
        /// count-based AddLocals covers the uniform-body fixtures).</summary>
        internal void SetLocals(int fd, params byte[][] bodies) => _locals[fd] = bodies;

        internal void SetGlyphProgram(int gid, byte[] program, int fd)
        {
            _programs[gid] = program;
            _fds[gid] = fd;
        }

        internal FontFaceRef Build()
        {
            byte[] header = [0x01, 0x00, 0x04, 0x01];
            byte[] name = Index("S4Cid"u8.ToArray());
            byte[] strings = Index(["S4Registry"u8.ToArray(), "S4Ordering"u8.ToArray()]);
            byte[] globalSubrs = Index();
            byte[] fdselect = [(byte)0, .. _fds.Select(static f => (byte)f)];
            // A CID charset maps glyphs to CIDs: format 0 with one entry per glyph
            // past .notdef (the predefined charset markers are name-keyed shapes a
            // CID font refuses), the CID column riding the glyph's own index.
            byte[] charset = [(byte)0, .. Enumerable.Range(1, _numGlyphs - 1)
                .SelectMany(cid => new[] { (byte)(cid >> 8), (byte)(cid & 0xFF) })];
            var privateDicts = new List<byte[]>();
            var localIndexes = new List<byte[]>();
            for (int fd = 0; fd < _fdCount; fd++)
            {
                localIndexes.Add(Index(_locals[fd]));
                privateDicts.Add(_locals[fd].Length == 0
                    ? [0x8b, 0x14]
                    : [0x8b, 0x14, 29, 0, 0, 0, 0x08, 19]);
            }
            byte[] charstrings = Index(_programs);

            byte[] FdArray(int[] privateOffsets, int[] privateSizes)
            {
                var items = new List<byte[]>();
                for (int fd = 0; fd < _fdCount; fd++)
                {
                    int at = privateOffsets[fd];
                    items.Add(
                    [
                        29, 0, 0, 0, (byte)privateSizes[fd],
                        29, (byte)((at >> 24) & 0xFF), (byte)((at >> 16) & 0xFF),
                            (byte)((at >> 8) & 0xFF), (byte)(at & 0xFF), 18,
                    ]);
                }
                return Index([.. items]);
            }

            // Fixed-width Top DICT: every operand rides the 5-byte form, so the
            // DICT length is a constant; only the offset values move.
            byte[] MeasureTop(int fdarrayLength, int fdselectAt)
            {
                int charsetAt = fdselectAt + fdselect.Length;
                int cursor = charsetAt + charset.Length;
                var offsets = new int[_fdCount];
                for (int fd = 0; fd < _fdCount; fd++)
                {
                    offsets[fd] = cursor;
                    cursor += privateDicts[fd].Length + localIndexes[fd].Length;
                }
                int fdarrayAt = cursor;
                int charstringsAt = fdarrayAt + fdarrayLength;
                var top = new List<byte>();
                void Operand(int value)
                {
                    top.AddRange([29, (byte)((value >> 24) & 0xFF),
                        (byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF),
                        (byte)(value & 0xFF)]);
                }
                void Entry(int op, int value)
                {
                    Operand(value);
                    top.Add((byte)op);
                }
                void Escape(int b1, int value)
                {
                    Operand(value);
                    top.AddRange([12, (byte)b1]);
                }
                Operand(391); Operand(392); Operand(0);
                top.AddRange([12, 0x1E]);            // ROS registry/ordering/supplement
                Escape(0x22, _numGlyphs);            // CIDCount (12 34)
                Entry(15, charsetAt);                // charset — single-byte op 15
                Entry(17, charstringsAt);            // CharStrings
                Escape(0x24, fdarrayAt);             // FDArray
                Escape(0x25, fdselectAt);            // FDSelect
                return [.. top];
            }

            // The DICT's length is operand-value-independent (fixed 5-byte forms),
            // so a probe INDEX built over a placeholder offset is final — its byte
            // length is what the real FDSelect offset rides.
            byte[] topDictProbe = MeasureTop(2, 0);
            Assert.Equal(TopDictLength, topDictProbe.Length);
            byte[] topIndexProbe = Index([topDictProbe]);
            int fdselectAt = 4 + name.Length + topIndexProbe.Length
                + strings.Length + globalSubrs.Length;
            int charsetAt = fdselectAt + fdselect.Length;
            int cursor = charsetAt + charset.Length;
            var privateOffsets = new int[_fdCount];
            var privateSizes = new int[_fdCount];
            for (int fd = 0; fd < _fdCount; fd++)
            {
                privateOffsets[fd] = cursor;
                privateSizes[fd] = privateDicts[fd].Length;
                cursor += privateSizes[fd] + localIndexes[fd].Length;
            }
            int fdarrayAt = cursor;
            byte[] fdarray = FdArray(privateOffsets, privateSizes);
            byte[] topDict = MeasureTop(fdarray.Length, fdselectAt);
            Assert.Equal(topDictProbe.Length, topDict.Length);
            byte[] topIndex = Index([topDict]);
            Assert.Equal(topIndexProbe.Length, topIndex.Length);

            List<byte> bytes =
            [
                .. header, .. name, .. topIndex, .. strings, .. globalSubrs,
                .. fdselect, .. charset,
            ];
            for (int fd = 0; fd < _fdCount; fd++)
            {
                bytes.AddRange(privateDicts[fd]);
                bytes.AddRange(localIndexes[fd]);
            }
            bytes.AddRange(fdarray);
            bytes.AddRange(charstrings);
            return Face(
                (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
                (SubsetFontFixture.MaxpTag, Maxp05((ushort)_numGlyphs)),
                (SubsetFontFixture.CffTag, [.. bytes]));
        }

        /// <summary>The fixed Top DICT length: the ROS triple's operands and its
        /// escape operator, the CIDCount and FDArray/FDSelect escapes (5+2 each),
        /// and the charset/CharStrings single-byte keys (5+1 each).</summary>
        private const int TopDictLength = 3 * 5 + 2 + 3 * (5 + 2) + 2 * (5 + 1);
    }
}
