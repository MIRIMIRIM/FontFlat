// seac closure scanner and pipeline tests (S3 card §3, commit ② acceptance).
//
// The CFF fixtures are assembled from the wire format directly (TN5176), never by asking
// the product to write them, so the scan runs against an independent expectation. The
// scanner is pinned through the pipeline's retain mode (the report's addition list is the
// observable), the resolution triples through the internal accessor, and every refusal key
// pins outcome, code and detail — failure atomic: result null, report retained.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetSeacClosureTests
{
    // Standard-encoding codes used across the fixtures (TN5176 appendix B; the
    // drafting-round triples: 65→SID 34 "A", 194→SID 125 "acute", 193→SID 124 "grave",
    // 66→SID 35 "B"; 204 carries no Standard entry — the card's "200" probe actually
    // resolves to SID 131 (dieresis), so the negative uses 204).
    private const int CodeA = 65, CodeB = 66, CodeGrave = 193, CodeAcute = 194,
        CodeUnmapped = 204;
    private const int SidA = 34, SidB = 35, SidGrave = 124, SidAcute = 125,
        SidAacute = 171;

    // ---- wire-level helpers ------------------------------------------------------

    private static byte[] Num(int v) => v is >= -107 and <= 107
        ? [(byte)(v + 139)]
        : [(byte)28, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)];

    private static byte[] Index(params byte[][] items)
    {
        if (items.Length == 0) return [0x00, 0x00];
        var offsets = new List<int> { 1 };
        foreach (var item in items) offsets.Add(offsets[^1] + item.Length);
        int width = 1;
        while (offsets[^1] >= (1 << (8 * width))) width++;
        var bytes = new List<byte> { (byte)(items.Length >> 8), (byte)items.Length,
            (byte)width };
        foreach (int offset in offsets)
            for (int i = width - 1; i >= 0; i--) bytes.Add((byte)(offset >> (8 * i)));
        foreach (var item in items) bytes.AddRange(item);
        return [.. bytes];
    }

    /// <summary>A name-keyed CFF1 table over the given charset SID column (entry 0 is
    /// .notdef, one SID per glyph) and glyph programs, with optional local/global subr
    /// INDEXes. Layout: Header|Name|Top|String|GlobalSubrs|charset|CharStrings|Private
    /// |LocalSubrs — the strict tiling with a format-0 custom charset, no encoding key
    /// (predefined Standard), and a Private DICT whose Subrs offset points at the
    /// trailing local INDEX.</summary>
    private static byte[] NameKeyedCff(int[] sids, byte[][] programs,
        byte[][]? locals = null, byte[][]? globals = null)
    {
        byte[] header = [0x01, 0x00, 0x04, 0x01];
        byte[] name = Index("S3Synthetic"u8.ToArray());
        byte[] strings = Index();
        byte[] globalIndex = Index(globals ?? []);
        byte[] charset = [(byte)0, .. sids.Skip(1).SelectMany(s =>
            new[] { (byte)((s >> 8) & 0xFF), (byte)(s & 0xFF) })];
        byte[] charstrings = Index(programs);
        byte[] localIndex = Index(locals ?? []);
        // Private DICT: defaultWidthX 0, then (with locals) Subrs → the INDEX behind.
        // The strict tiling pins the Subrs offset to exactly the Private DICT size and
        // the INDEX to the bytes right behind it (Cff1ViewValidate), so the declared
        // Private size is the DICT bytes only.
        byte[] privateDict = locals is null
            ? [0x8b, 0x14]
            : [0x8b, 0x14, 29, 0, 0, 0, 0x08, 19];
        int privateSize = privateDict.Length;

        // Top DICT: CharStrings, charset, Private — every operand in the fixed 5-byte
        // form so the Top DICT length is constant and one measurement pass suffices.
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
        Entry(17, charStringsAt);                    // CharStrings
        Entry(15, charsetAt);                        // charset (custom, format 0)
        // Private carries two operands (size, offset) before its operator byte.
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
        SubsetMode mode) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = mode,
    };

    /// <summary>The composite roster: .notdef, then three named glyphs where the last
    /// one's charstring carries the 4-operand seac form referencing A (base) and acute
    /// (accent). The default charset [0, A, acute, Aacute] makes it gid 3 = Aacute;
    /// a caller may supply a different column (same length) to build the hanging-name
    /// refusals.</summary>
    private static FontFaceRef CompositeFace(byte[]? seacProgram = null,
        int[]? sids = null, byte[]? baseProgram = null)
    {
        int[] charset = sids ?? [0, SidA, SidAcute, SidAacute];
        var programs = new byte[4][];
        programs[0] = [14];
        programs[1] = baseProgram ?? [14];
        programs[2] = [14];
        programs[3] = seacProgram ??
            [.. Num(0), .. Num(0), .. Num(CodeA), .. Num(CodeAcute), 14];
        return Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(4)),
            (SubsetFontFixture.CffTag, NameKeyedCff(charset, programs)));
    }

    // ---- detection: the seac forms the scanner must find (retain mode) -----------

    [Fact]
    public void FourOperandFormAddsBaseAndAccent()
    {
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(), [3],
            SubsetMode.RetainGids), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([], report!.ClosureAddedGids);
        Assert.Equal([1, 2], report.SeacClosureAddedGids);
        Assert.Equal(0, report.UnselectedSlotCount);
    }

    [Fact]
    public void FiveOperandWidthFormAddsBaseAndAccent()
    {
        // wx adx ady bchar achar endchar: the width operand rides the front.
        byte[] program = [.. Num(100), .. Num(0), .. Num(0), .. Num(CodeA),
            .. Num(CodeAcute), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void FixedPointWidthFormAddsBaseAndAccent()
    {
        // 255-prefixed 16.16 width: a real value the scan keeps as unknown; the seac
        // operands behind it stay known and the form is still detected.
        byte[] program = [0xFF, 0x00, 0x00, 0x00, 0x00, .. Num(0), .. Num(0),
            .. Num(CodeA), .. Num(CodeAcute), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void TwoBytePushFormsAreDecoded()
    {
        // adx through the negative 251-form (-108), bchar through the positive
        // 247-form (194 = (247-247)*256 + 86 + 108), achar a shortint (28-form).
        byte[] program = [(byte)251, 0x00, .. Num(0),
            (byte)247, (byte)(CodeAcute - 108), 28, 0x00, (byte)CodeA, 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void EndcharInsideSubrBodyIsDetected()
    {
        // The census's per-program scan misses this shape (S1c gate finding): the glyph
        // frame pushes adx ady bchar, calls the subr, and the subr body pushes achar
        // and ends the glyph — the stack persists across the call boundary. The call
        // operand is the spec form: index 0 − bias 107 over the 1-subr INDEX (TN5177
        // §4.7 corrected, S4 card §8 finding 1).
        byte[] glyph = [.. Num(0), .. Num(0), .. Num(CodeA), .. Num(-107), 10];
        byte[] subr = [.. Num(CodeAcute), 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(4)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, SidA, SidAcute, SidAacute],
                [new byte[] { 14 }, new byte[] { 14 }, new byte[] { 14 }, glyph],
                locals: [subr])));
        var status = SubsetOperation.TryPrepare(Request(face, [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void WorklistFixedPointScansSecondOrderReferences()
    {
        // Aacute → {A, acute}, and A itself is a seac composite → {B, grave}: the newly
        // added glyph's program joins the worklist and its own references land too.
        int[] charset = [0, SidA, SidAcute, SidAacute, SidB, SidGrave];
        byte[] a = [.. Num(0), .. Num(0), .. Num(CodeB), .. Num(CodeGrave), 14];
        byte[] aacute = [.. Num(0), .. Num(0), .. Num(CodeA), .. Num(CodeAcute), 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(6)),
            (SubsetFontFixture.CffTag, NameKeyedCff(charset,
            [
                new byte[] { 14 }, a, new byte[] { 14 }, aacute, new byte[] { 14 },
                new byte[] { 14 }
            ])));
        var status = SubsetOperation.TryPrepare(Request(face, [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2, 4, 5], report!.SeacClosureAddedGids);
        Assert.Equal(0, report.UnselectedSlotCount);
    }

    [Fact]
    public void ResolvedReferencesAlreadyRetainedAddNothing()
    {
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(), [1, 2, 3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Empty(report!.SeacClosureAddedGids);
        Assert.Equal(0, report.UnselectedSlotCount);
    }

    [Fact]
    public void PlainEndcharWidthFormsAddNothing()
    {
        // One- and three-argument endchar are the legal width forms, never seac.
        byte[] one = [.. Num(100), 14];
        byte[] three = [.. Num(100), .. Num(0), .. Num(0), 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(4)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, SidA, SidAcute, SidAacute],
                [new byte[] { 14 }, one, three, new byte[] { 14 }])));
        var status = SubsetOperation.TryPrepare(Request(face, [1, 2],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Empty(report!.SeacClosureAddedGids);
    }

    // ---- the stack-effect table pieces -------------------------------------------

    [Fact]
    public void HintmaskBytesAreSkippedByStemCount()
    {
        // One hstem over two pushes = one hint, so the following hintmask skips exactly
        // one mask byte; a mis-sized skip swallows a seac push and the form is lost.
        byte[] program = [.. Num(1), .. Num(1), 1, 19, 0x14,
            .. Num(0), .. Num(0), .. Num(CodeA), .. Num(CodeAcute), 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(4)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, SidA, SidAcute, SidAacute],
                [new byte[] { 14 }, new byte[] { 14 }, new byte[] { 14 }, program])));
        var status = SubsetOperation.TryPrepare(Request(face, [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void SubrBiasDecodesLiteralIndicesAndStackPersists()
    {
        // Two callsubr calls build the seac stack across frames: subr 0 pushes
        // adx ady, subr 1 pushes bchar achar — one stack, three bodies, one form.
        // Operands are the spec form −107/−106 (indices 0/1 minus bias 107).
        byte[] glyph = [.. Num(-107), 10, .. Num(-106), 10, 14];
        byte[] subr0 = [.. Num(0), .. Num(0), 11];
        byte[] subr1 = [.. Num(CodeA), .. Num(CodeAcute), 11];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(4)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, SidA, SidAcute, SidAacute],
                [new byte[] { 14 }, new byte[] { 14 }, new byte[] { 14 }, glyph],
                locals: [subr0, subr1])));
        var status = SubsetOperation.TryPrepare(Request(face, [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void CallPathCycleGuardTerminates()
    {
        // A self-calling subr must end its branch at the guard, not loop forever; the
        // glyph then ends plainly and the operation succeeds with nothing added. The
        // operands are the spec form (index 0 − bias 107).
        byte[] glyph = [.. Num(-107), 10, 14];
        byte[] subr0 = [.. Num(-107), 10];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(4)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, SidA, SidAcute, SidAacute],
                [new byte[] { 14 }, new byte[] { 14 }, new byte[] { 14 }, glyph],
                locals: [subr0])));
        var status = SubsetOperation.TryPrepare(Request(face, [3],
            SubsetMode.RetainGids), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Empty(report!.SeacClosureAddedGids);
    }

    [Fact]
    public void UnknownIndexDescendsEveryBodyWithUnknownStack()
    {
        // A div-computed callsubr index is not statically decidable: every body of the
        // INDEX is descended with an all-unknown stack, and the endchar inside sees an
        // unknown operand — the category-3 refusal, not a silent skip.
        byte[] glyph = [.. Num(0), .. Num(0), .. Num(CodeA), .. Num(1), .. Num(2),
            12, 12, 10];
        byte[] subr0 = [.. Num(CodeAcute), 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(4)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, SidA, SidAcute, SidAacute],
                [new byte[] { 14 }, new byte[] { 14 }, new byte[] { 14 }, glyph],
                locals: [subr0])));
        var status = SubsetOperation.TryPrepare(Request(face, [3],
            SubsetMode.RetainGids), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(4UL, diagnostic.Expected);
        Assert.Null(result);
    }

    [Fact]
    public void DivResultIsUnknown()
    {
        // 3 1 div leaves an unknown value on the stack; the base position it occupies
        // cannot be decided statically (card §2.3 category 3).
        byte[] program = [.. Num(0), .. Num(CodeAcute), .. Num(3), .. Num(1),
            12, 12, .. Num(CodeA), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(4UL, diagnostic.Detail);
        Assert.Equal(4UL, diagnostic.Expected);
        Assert.Null(result);
    }

    [Fact]
    public void RandomResultIsUnknown()
    {
        byte[] program = [.. Num(0), .. Num(0), .. Num(CodeA), .. Num(CodeAcute),
            12, 23, 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Null(result);
    }

    // ---- the stack-tool operators the seac form rides through (TN5177 §4.4-§4.6) ----

    [Fact]
    public void RollBringsTheCodesToTheTopBeforeEndchar()
    {
        // 65 194 10 20 4 2 roll: the control pair rotates the top four so the
        // codes land in the endchar's last two slots (TN5177 §4.4; the operator
        // is the escape 12 30, appendix A — an earlier draft filed it at the
        // reserved 12 31 and let 12 30 eat a value).
        byte[] program = [.. Num(65), .. Num(194), .. Num(10), .. Num(20),
            .. Num(4), .. Num(2), 0x0c, 0x1e, 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void IndexCopiesADeepSlotIntoTheSeacForm()
    {
        // 10 20 65 194 1 index: the copy of the element one deep rides behind the
        // original code — endchar reads (194, 65), base acute and accent A, and
        // both additions land.
        byte[] program = [.. Num(10), .. Num(20), .. Num(65), .. Num(194),
            .. Num(1), 0x0c, 0x1d, 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void ExchSwapsTheCodesIntoPlace()
    {
        byte[] program = [.. Num(0), .. Num(0), .. Num(194), .. Num(65),
            0x0c, 0x1c, 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void DupFeedsBothSeacSlotsFromOnePush()
    {
        // 0 0 65 dup 194 endchar: the duplicate carries the original's known
        // state; the base slot reads the copy, the accent the literal.
        byte[] program = [.. Num(0), .. Num(0), .. Num(65), 0x0c, 0x1b,
            .. Num(194), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void PutGetRoundTripBelowTheCodesKeepsTheForm()
    {
        // 98 0 put 0 get: the transient-array round trip (TN5177 §4.5) leaves an
        // unknown slot — harmless below the codes, the conservative slot model
        // keeps the top two known and the form resolves.
        byte[] program = [.. Num(98), .. Num(0), 0x0c, 0x14, .. Num(0), 0x0c, 0x15,
            .. Num(0), .. Num(0), .. Num(65), .. Num(194), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void GetUnknownSlotInTheCodesRefuses()
    {
        // The get's never-stored slot lands in the top two: the value is not
        // statically decidable, so the seac contract refuses rather than guess
        // (the category-3 shape the div/random pins share).
        byte[] program = [.. Num(98), .. Num(0), 0x0c, 0x14, .. Num(10), .. Num(20),
            .. Num(0), 0x0c, 0x15, .. Num(65), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(4UL, diagnostic.Expected);
        Assert.Null(result);
    }

    [Fact]
    public void NotResultBelowTheCodesKeepsTheForm()
    {
        // The logic family's unknown result is harmless below the codes: the
        // form's top two stay literals.
        byte[] program = [.. Num(3), 0x0c, 0x05, .. Num(0), .. Num(0),
            .. Num(65), .. Num(194), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void NotResultInTheCodesRefuses()
    {
        byte[] program = [.. Num(0), .. Num(0), .. Num(65), .. Num(1), 0x0c, 0x05, 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Null(result);
    }

    [Fact]
    public void IfelseResultBelowTheCodesKeepsTheForm()
    {
        // 3 4 1 2 ifelse leaves its unknown branch value below the codes.
        byte[] program = [.. Num(3), .. Num(4), .. Num(1), .. Num(2), 0x0c, 0x16,
            .. Num(0), .. Num(0), .. Num(65), .. Num(194), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void RandomBelowTheCodesKeepsTheForm()
    {
        byte[] program = [0x0c, 0x17, .. Num(0), .. Num(0), .. Num(65),
            .. Num(194), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    [Fact]
    public void FlexOperandsDoNotPoseAsSeacOperands()
    {
        // The flex family is path construction: hflex's seven operands are
        // consumed, so endchar sees the two trailing values only — short of the
        // four-operand form, nothing is added. A walker that let them persist
        // would read depth nine and detect a seac form that never existed.
        byte[] program = [.. Num(1), .. Num(2), .. Num(3), .. Num(4), .. Num(5),
            .. Num(6), .. Num(7), 0x0c, 0x22, .. Num(65), .. Num(194), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Empty(report!.SeacClosureAddedGids);
    }

    [Fact]
    public void HvcurvetoOperandsDoNotPoseAsSeacOperands()
    {
        // The stix-two-math refusal shape: path operands left standing by a
        // missing hvcurveto entry (31, TN5177 appendix A) filled the endchar's
        // four-slot window with contour data and the closure read a seac form
        // whose "codes" were coordinates — one of them unmapped, refusing the
        // face. The operator consumes its operands; nothing is detected.
        byte[] program = [.. Num(1), .. Num(2), .. Num(3), .. Num(4), 31, 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Empty(report!.SeacClosureAddedGids);
    }

    [Fact]
    public void ReservedEscapeLeavesTheStackUntouched()
    {
        // 12 31 is reserved (TN5177 appendix A) — the unmodeled default assumes
        // no stack effect, values persist; the form behind it still resolves.
        byte[] program = [.. Num(0), .. Num(0), .. Num(65), .. Num(194),
            0x0c, 0x1f, 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out _, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1, 2], report!.SeacClosureAddedGids);
    }

    // ---- resolution triples (the internal Standard-code accessor) ----------------

    [Fact]
    public void StandardCodeTriplesResolve()
    {
        byte[] cff = NameKeyedCff([0, SidA, SidAcute, SidAacute, SidB],
            [new byte[] { 14 }, new byte[] { 14 }, new byte[] { 14 },
                new byte[] { 14 }, new byte[] { 14 }]);
        Assert.Equal(ReadStatus.Done,
            Cff1View.TryOpen(cff, out var view, out _, default));
        Assert.Equal(ReadStatus.Done,
            view.TryRead(out var model, out _, out _, out _, new BuildBudget()));
        Assert.Equal(ReadStatus.Done, model!.TryMapStandardCode(CodeA,
            out int sidA, out bool mappedA, out int gidA, out _));
        Assert.Equal(SidA, sidA);
        Assert.True(mappedA);
        Assert.Equal(1, gidA);
        Assert.Equal(ReadStatus.Done, model.TryMapStandardCode(CodeAcute,
            out int sidAcute, out bool mappedAcute, out int gidAcute, out _));
        Assert.Equal(SidAcute, sidAcute);
        Assert.True(mappedAcute);
        Assert.Equal(2, gidAcute);
        // grave resolves to its standard SID but the charset has no such name: the
        // hanging-reference tier the caller refuses on.
        Assert.Equal(ReadStatus.Done, model.TryMapStandardCode(CodeGrave,
            out int sidGrave, out bool mappedGrave, out _, out _));
        Assert.Equal(SidGrave, sidGrave);
        Assert.False(mappedGrave);
        // A code without a Standard entry resolves sid=0, mapped=false.
        Assert.Equal(ReadStatus.Done, model.TryMapStandardCode(CodeUnmapped,
            out int sidNone, out bool mappedNone, out _, out _));
        Assert.Equal(0, sidNone);
        Assert.False(mappedNone);
    }

    // ---- refusal keys (card §2.3; OQ-S3-3 lean) ----------------------------------

    [Fact]
    public void UnmappedAccentCodeRefuses()
    {
        byte[] program = [.. Num(0), .. Num(0), .. Num(CodeA), .. Num(CodeUnmapped), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal((ulong)CodeUnmapped, diagnostic.Detail);
        Assert.Equal(0UL, diagnostic.Expected);
        Assert.Null(result);
        Assert.NotEmpty(report!.Diagnostics);
    }

    [Fact]
    public void UnmappedBaseCodeRefuses()
    {
        byte[] program = [.. Num(0), .. Num(0), .. Num(CodeUnmapped), .. Num(CodeA), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal((ulong)CodeUnmapped, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void AccentNameOutsideCharsetRefuses()
    {
        // The charset carries no "acute": the face's own hanging reference refuses at
        // the standard SID that found no glyph (Detail = 125). The composite rides a
        // placeholder SID so the column stays unique.
        var face = CompositeFace(sids: [0, SidA, SidB, SidAacute]);
        var status = SubsetOperation.TryPrepare(Request(face, [3],
            SubsetMode.RetainGids), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal((ulong)SidAcute, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void BaseNameOutsideCharsetRefuses()
    {
        // The charset carries no "A": the base reference refuses before the accent.
        var face = CompositeFace(sids: [0, SidAcute, SidB, SidAacute]);
        var status = SubsetOperation.TryPrepare(Request(face, [3],
            SubsetMode.RetainGids), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal((ulong)SidA, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void UnknownBaseCodeRefuses()
    {
        // The base slot holds a div-computed unknown; the accent stays a literal.
        byte[] program = [.. Num(0), .. Num(0), .. Num(3), .. Num(1), 12, 12,
            .. Num(CodeA), 14];
        var status = SubsetOperation.TryPrepare(Request(CompositeFace(program), [3],
            SubsetMode.RetainGids), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Null(result);
    }

    [Fact]
    public void CidKeyedSeacFormRefuses()
    {
        // CID-keyed charsets carry CIDs and no name domain: a detected seac form is
        // structurally unresolvable and refuses with the glyph's GID in Detail.
        var image = CidFontBuilder.Build(new CidFont { FdCount = 1, NumGlyphs = 3 });
        byte[] charstrings = Index(
        [
            new byte[] { 14 },
            [.. Num(0), .. Num(0), .. Num(CodeA), .. Num(CodeAcute), 14],
            new byte[] { 14 }
        ]);
        // CharStrings is the trailing block: the splice moves no earlier offset.
        var patched = new byte[image.CharStringsOffset + charstrings.Length];
        Array.Copy(image.Bytes, patched, image.CharStringsOffset);
        Array.Copy(charstrings, 0, patched, image.CharStringsOffset, charstrings.Length);
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, patched));
        var status = SubsetOperation.TryPrepare(Request(face, [1],
            SubsetMode.RetainGids), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(1UL, diagnostic.Detail);
        Assert.Equal(0UL, diagnostic.Expected);
        Assert.Null(result);
    }

    // ---- compact mode: the additions enter the frozen order ----------------------

    [Fact]
    public void CompactClosureAddsBeforeFreezeAndOrdersByIdentity()
    {
        // Charset [0, Aacute, A, acute]: the composite sits at GID 1 with a HIGHER SID
        // than its references, so the frozen order must place the additions ahead of
        // their referrer — the additions join the sort, they are not appended.
        byte[] aacute = [.. Num(0), .. Num(0), .. Num(CodeA), .. Num(CodeAcute), 14];
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(4)),
            (SubsetFontFixture.CffTag, NameKeyedCff([0, SidAacute, SidA, SidAcute],
                [new byte[] { 14 }, aacute, new byte[] { 14 }, new byte[] { 14 }])));
        var status = SubsetOperation.TryPrepare(Request(face, [1],
            SubsetMode.CompactRemap), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([2, 3], report!.SeacClosureAddedGids);
        var artifact = result!.Map;
        Assert.NotNull(artifact);
        Assert.Equal(4, artifact!.OutputGlyphCount);
        Assert.Equal([0, 2, 3, 1],
            artifact.Entries.Select(e => (int)e.InputGid).ToArray());
        Assert.Equal("cff1-sid:0", artifact.Entries[0].StableIdentity);
        Assert.Equal("cff1-sid:34", artifact.Entries[1].StableIdentity);
        Assert.Equal("cff1-sid:125", artifact.Entries[2].StableIdentity);
        Assert.Equal("cff1-sid:171", artifact.Entries[3].StableIdentity);
        Assert.NotNull(report.MapArtifactSha256);
    }

    // ---- budget: the closure charges its injection points -------------------------

    [Fact]
    public void WorkBudgetCoversTheSeacScan()
    {
        // Bisect the smallest admitting work budget, then pin that one unit below
        // exhausts as ResourceLimit/BudgetExceeded on the work-units field (the
        // S0-BF-001 shape, now with the closure's charges in the pipeline).
        var request = Request(CompositeFace(), [3], SubsetMode.RetainGids);
        long low = 1, high = 1L << 40;
        Assert.Equal(ReadStatus.Done,
            PrepareStatus(request, new BuildLimits { MaxWorkUnits = high }));
        while (high - low > 1)
        {
            long mid = low + (high - low) / 2;
            if (PrepareStatus(request, new BuildLimits { MaxWorkUnits = mid }) ==
                ReadStatus.Done) high = mid;
            else low = mid;
        }
        var status = SubsetOperation.TryPrepare(request, out var result,
            out _, out var diagnostic, new BuildLimits { MaxWorkUnits = high - 1 });
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(DataField.WorkUnits, diagnostic.Location.Field);
        Assert.Null(result);
    }

    private static ReadStatus PrepareStatus(SubsetRequest request, BuildLimits limits) =>
        SubsetOperation.TryPrepare(request, out _, out _, out _, limits);
}
