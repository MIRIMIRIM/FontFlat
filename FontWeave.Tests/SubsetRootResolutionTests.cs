// SR root-resolution unit pins (SR card §4 commit ② acceptance).
//
// Three faces drive the slice: the canonical-cmap TrueType fixture (the
// SubsetRebuildTests shape) answers the resolution and seeding pins; a Mac-only
// cmap proves the non-Unicode record's consumed-not-consulted stance, and the
// (0,5) fmt14 synthetic face (OQ-F14) extends that stance to the
// variation-sequence record; the S2c GSUB synthetic face carries the derived-scope
// pins (the three code-23 contradictions, the census match counts, the derived
// no-op byte identity).
// Every refusal pins outcome, diagnostic code and the null-result/no-partial-set
// shape (W1 §4.5); the identity tier compares the resolved root's bytes with the
// equal explicit root's. The R7 belt (a resolved GID outside numGlyphs) has no
// pipeline-level pin: both typed views already validate resolvable GID domains
// at read (CmapFormat4View.ValidateDeltaArc, the format 12 group bound), so an
// out-of-domain resolution cannot survive step 3.25's model read — the belt
// stays for defense, unreachable by construction.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetRootResolutionTests
{
    private const uint TagA = 0x41424344; // 'ABCD'
    private const uint TagB = 0x42434445; // 'BCDE'
    private const uint TagDflt = 0x44464C54; // 'DFLT'
    private const int NumGlyphs = 16;

    private static SubsetOptions NoLayout() => new() { NoLayout = true };

    private static SubsetRequest CodepointRequest(FontFaceRef face, uint[] codepoints,
        SubsetOptions? options = null) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot
            { Kind = SubsetRootKind.UnicodeCodepoints, Codepoints = codepoints },
        Mode = SubsetMode.RetainGids,
        Options = options ?? NoLayout()
    };

    private static SubsetRequest TextRequest(FontFaceRef face, string text,
        SubsetOptions? options = null, IReadOnlyList<uint>? scripts = null,
        IReadOnlyList<uint>? languages = null, IReadOnlyList<uint>? features = null) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot
        {
            Kind = SubsetRootKind.TextLayout,
            Text = text,
            ScriptTags = scripts,
            LanguageTags = languages,
            FeatureTags = features
        },
        Mode = SubsetMode.RetainGids,
        Options = options ?? NoLayout()
    };

    private static SubsetRequest ExplicitRequest(FontFaceRef face, ushort[] gids,
        SubsetOptions? options = null) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = SubsetMode.RetainGids,
        Options = options ?? NoLayout()
    };

    /// <summary>The four-glyph TrueType fixture over one canonical cmap (the
    /// SubsetRebuildTests shape): instructive simple glyph 0, empty glyphs 1..3,
    /// layout family dropped under NoLayout.</summary>
    private static FontFaceRef RebuildFace(
        params (uint Code, ushort Gid)[] mappings)
    {
        byte[] glyfSource =
        [
            .. SubsetFontFixture.InstructedSimpleGlyph(),
            .. GlyfImages.NoContourGlyphRecord(),
            .. GlyfImages.CompositeRecord(0),
            .. GlyfImages.NoContourGlyphRecord()
        ];
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, glyfSource),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 18, 30, 46, 58])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical(mappings)),
            (SubsetFontFixture.GsubTag, SubsetFontFixture.Dummy()));
    }

    /// <summary>One format 4 subtable with a single real segment whose affine delta
    /// arc resolves <paramref name="code"/> to <paramref name="gid"/>, plus the
    /// structural sentinel (the reservedPad glue included — the real 32-byte wire
    /// form). Test-side independent encoding.</summary>
    private static byte[] Format4Subtable(ushort code, ushort gid)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 4);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 32);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 4);  // segsX2 = 2
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), 4);  // searchRange
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), 1); // entrySelector
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), code);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16), 0xFFFF);
        // bytes 18..19: the reservedPad glue stays zero.
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(20), code);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(22), 0xFFFF);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(24),
            unchecked((ushort)(gid - code)));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(26), 1); // sentinel delta
        return bytes;
    }

    /// <summary>A cmap over raw subtables, one encoding record each, payloads
    /// contiguous in record order (both records and offsets ascend together).</summary>
    private static byte[] CmapOver(
        params ((ushort Platform, ushort Encoding), byte[] Subtable)[] records)
    {
        int header = 4 + 8 * records.Length;
        int total = header;
        foreach (var (_, subtable) in records) total += subtable.Length;
        var bytes = new byte[total];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)records.Length);
        int at = header;
        for (int i = 0; i < records.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4 + 8 * i),
                records[i].Item1.Platform);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6 + 8 * i),
                records[i].Item1.Encoding);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8 + 8 * i), (uint)at);
            records[i].Subtable.CopyTo(bytes.AsSpan(at));
            at += records[i].Subtable.Length;
        }
        return bytes;
    }

    private static FontFaceRef FaceOver(ushort numGlyphs, byte[] cmap)
    {
        byte[] glyfSource =
        [
            .. SubsetFontFixture.InstructedSimpleGlyph(),
            .. GlyfImages.NoContourGlyphRecord(),
            .. GlyfImages.CompositeRecord(0),
            .. GlyfImages.NoContourGlyphRecord()
        ];
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(numGlyphs)),
            (SubsetFontFixture.GlyfTag, glyfSource),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 18, 30, 46, 58])),
            (SubsetFontFixture.CmapTag, cmap));
    }

    private static byte[] OutputBytes(SubsetResult result)
    {
        byte[] output = new byte[result.Font.OutputLength];
        Assert.Equal(ReadStatus.Done,
            result.Font.TryCopyTo(output, out int written, out _));
        Assert.Equal(output.Length, written);
        return output;
    }

    // ---- resolution table pins (SR card §2.1) ----------------------------------

    [Fact]
    public void SingleMappingResolvesIntoTheSeed()
    {
        var status = SubsetOperation.TryPrepare(
            CodepointRequest(RebuildFace((0x41, 1), (0x42, 2)), [0x41, 0x42]),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        // {0,1,2} of 4 retained — the resolved set plus the implicit .notdef.
        Assert.Equal(1, report!.UnselectedSlotCount);
        Assert.Empty(report.ClosureAddedGids);
        // The root form rides the report (OQ-SR-6).
        Assert.Contains(report.OptionRecords, r => r.Id == SubsetOptionId.Root &&
            r.Value == nameof(SubsetRootKind.UnicodeCodepoints));
    }

    [Fact]
    public void MacOnlyCodepointIsUnmappedNotAmbiguous()
    {
        // A code point carried only by the (1,0) Mac record is not consulted: the
        // root resolution reads the record as evidence but never consumes it — the
        // same predicate as the rebuild (SR card §2.1 posture 1, OQ-S1a-2's stance).
        byte[] macOnly = CmapOver(((1, 0), Format4Subtable(0x41, 1)));
        var status = SubsetOperation.TryPrepare(
            CodepointRequest(FaceOver(4, macOnly), [0x41]),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Null(result);
        FontDiagnostic retained = Assert.Single(report!.Diagnostics);
        Assert.Equal(DiagnosticCode.InvalidValue, retained.Code);
    }

    // OQ-F14: the (0,5) record takes the same consumed-not-consulted stance — a normal
    // code point resolves through the Unicode-semantic records only. Done proves the
    // fmt14 subtable's unsupported TryMapCharacter path never ran.
    [Fact]
    public void Format14RecordIsNeverConsultedForScalarResolution()
    {
        var face = FaceOver(4, SubsetFontFixture.CmapWithUvs((0x41, 1), (0x42, 2)));
        var status = SubsetOperation.TryPrepare(CodepointRequest(face, [0x41]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
    }

    // Posture 6 with format 14 present in the face: a VS value refuses before any
    // query — the face carrying UVS data opens no third state (OQ-F14 keeps posture 6).
    [Theory]
    [InlineData(0xFE00u)]
    [InlineData(0xE0100u)]
    public void VariationSelectorRefusesWithFormat14Present(uint selector)
    {
        var face = FaceOver(4, SubsetFontFixture.CmapWithUvs((0x41, 1), (0x42, 2)));
        var status = SubsetOperation.TryPrepare(CodepointRequest(face, [selector]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal((ulong)selector, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void GidZeroResolutionIsTheUnmappedRefusal()
    {
        // The typed read reports a GID 0 resolution as mapped=false (CmapTable.cs
        // contract), so the root's disposition is the unmapped refusal — .notdef's
        // membership stays the seeder's alone (SR card §2.1 posture 4).
        byte[] toZero = CmapOver(((3, 1), Format4Subtable(0x41, 0)));
        var status = SubsetOperation.TryPrepare(
            CodepointRequest(FaceOver(4, toZero), [0x41]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(0x41UL, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void CrossRecordAmbiguityRefusesWithoutAPartialSet()
    {
        // R2: two Unicode-semantic records map one code point to two distinct GIDs —
        // the caller's input set is not well-defined and nothing seeds (W1 §1.2;
        // the rebuild's first-wins rule never reaches a committed code point).
        byte[] ambiguous = CmapOver(
            ((0, 3), Format4Subtable(0x41, 1)),
            ((3, 1), Format4Subtable(0x41, 2)));
        var status = SubsetOperation.TryPrepare(
            CodepointRequest(FaceOver(4, ambiguous), [0x41]),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(0x41UL, diagnostic.Detail);
        Assert.Null(result);
        Assert.NotEmpty(report!.Diagnostics);
    }

    [Fact]
    public void SameGidAcrossRecordsIsNotAmbiguous()
    {
        // Distinct-GID is the ambiguity test: two records agreeing on the GID are
        // one mapping (SR card §2.1 posture 3's "≥2 distinct GID").
        byte[] agreeing = CmapOver(
            ((0, 3), Format4Subtable(0x41, 1)),
            ((3, 1), Format4Subtable(0x41, 1)));
        var status = SubsetOperation.TryPrepare(
            CodepointRequest(FaceOver(4, agreeing), [0x41]),
            out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
    }

    [Fact]
    public void VariationSelectorValueIsUnmappedOnAFmt14FreeFace()
    {
        // Posture 6: no third state. The admission gate refuses a format 14 before
        // the root resolution ever runs, so a VS value on a fmt14-free face is the
        // plain unmapped refusal — the OQ-S0-9 rejected-semantic parity.
        var status = SubsetOperation.TryPrepare(
            CodepointRequest(RebuildFace((0x41, 1)), [0xFE00]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(0xFE00UL, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void MappedVariationSelectorStillRefuses()
    {
        // The explicit posture-6 arm: a legacy Unicode record that happens to map a
        // VS code point is never consulted — the mapped face refuses exactly like
        // the unmapped twin above, because the third state does not exist.
        var status = SubsetOperation.TryPrepare(
            CodepointRequest(RebuildFace((0x41, 1), (0xFE00, 2)), [0xFE00]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(0xFE00UL, diagnostic.Detail);
        Assert.Equal(DataField.CharCode, diagnostic.Location.Field);
        Assert.Null(result);
    }

    [Fact]
    public void IdeographicVariationSelectorRefusesEvenWhenMapped()
    {
        // The E0100..E01EF arm over the format 12 record: the same no-third-state
        // refusal at the code point's own CharCode position.
        var status = SubsetOperation.TryPrepare(
            CodepointRequest(RebuildFace((0x41, 1), (0xE0100, 2)), [0xE0100]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(0xE0100UL, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void VariationSelectorInTextRefuses()
    {
        // The text form shares the posture-6 arm: the VS character decodes to one
        // scalar and refuses before any GID is seeded, on a face that maps it.
        var status = SubsetOperation.TryPrepare(
            TextRequest(RebuildFace((0x41, 1), (0xFE00, 2)), "A\uFE00"),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(0xFE00UL, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void CodepointSequenceViolationsRefuseAtStep1()
    {
        // R3: the explicit codepoint list is a precise input contract — strictly
        // ascending scalar values; disorder, duplicate, surrogate and above-plane
        // values all refuse as InvalidValue before any table work.
        var face = RebuildFace((0x41, 1));
        foreach (var codepoints in new[]
                 {
                     new uint[] { 0x42, 0x41 },   // disordered
                     new uint[] { 0x41, 0x41 },   // duplicate
                     new uint[] { 0xD800 },       // surrogate
                     new uint[] { 0x110000 }      // above the scalar domain
                 })
        {
            var status = SubsetOperation.TryPrepare(
                CodepointRequest(face, codepoints), out var result, out _,
                out var diagnostic);
            Assert.Equal(ReadStatus.UnsupportedFormat, status);
            Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
            Assert.Null(result);
        }
    }

    [Fact]
    public void ForeignMembersOnFormsAAndBRefuseAtStep1()
    {
        // The one-form contract (SubsetGlyphRoot): Text and the three form-(c) scope
        // dimensions on forms (a)/(b) are pollution — the request refuses instead of
        // silently ignoring the foreign member and publishing the rest (W1 §1.2).
        var face = RebuildFace((0x41, 1));
        foreach (var root in new[]
                 {
                     new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet,
                         Gids = [1], Text = "B" },
                     new SubsetGlyphRoot { Kind = SubsetRootKind.UnicodeCodepoints,
                         Codepoints = [0x41], Text = "B" },
                     new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet,
                         Gids = [1], ScriptTags = [TagDflt] },
                     new SubsetGlyphRoot { Kind = SubsetRootKind.UnicodeCodepoints,
                         Codepoints = [0x41], FeatureTags = [TagA] }
                 })
        {
            var status = SubsetOperation.TryPrepare(
                new SubsetRequest { Face = face, Root = root,
                    Mode = SubsetMode.RetainGids, Options = NoLayout() },
                out var result, out _, out var diagnostic);
            Assert.Equal(ReadStatus.UnsupportedFormat, status);
            Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
            Assert.Equal(face.Location, diagnostic.Location);
            Assert.Null(result);
        }
    }

    // ---- text decode pins (SR card §2.2) ----------------------------------------

    [Fact]
    public void TextDecodesIntoASortedDistinctSet()
    {
        // Text is a set-semantics entry: "BA AB" and [0x20,0x41,0x42] carry the same
        // request — order and duplicates are the form's definition, not a silent
        // normalization of a precise input contract.
        var face = RebuildFace((0x20, 3), (0x41, 1), (0x42, 2));
        var status = SubsetOperation.TryPrepare(TextRequest(face, "BA AB"),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(0, result!.Report.UnselectedSlotCount); // all 4 glyphs retained
        // The identical set as an explicit codepoint list is the identical request.
        var twin = SubsetOperation.TryPrepare(
            CodepointRequest(face, [0x20, 0x41, 0x42]), out var twinResult,
            out _, out _);
        Assert.Equal(ReadStatus.Done, twin);
        Assert.NotNull(twinResult);
        Assert.Equal(OutputBytes(result!), OutputBytes(twinResult!));
    }

    [Fact]
    public void EmptyTextIsTheEmptySet()
    {
        var status = SubsetOperation.TryPrepare(TextRequest(RebuildFace((0x41, 1)), ""),
            out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(3, report!.UnselectedSlotCount);
    }

    [Theory]
    [InlineData(0)] // lone high surrogate at the end
    [InlineData(1)] // lone low surrogate after a letter
    public void UnpairedSurrogateRefuses(int form)
    {
        // R4: the request-shape walk refuses before any table work — the decode's
        // own defensive branch never sees the request. The strings are built in the
        // body: theory serialization cannot carry lone surrogates verbatim.
        string text = form == 0
            ? new string((char)0xD800, 1)
            : new string(new[] { 'a', (char)0xDC00 });
        var status = SubsetOperation.TryPrepare(
            TextRequest(RebuildFace((0x41, 1)), text),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.InvalidUtf16, diagnostic.Code);
        Assert.Null(result);
    }

    [Fact]
    public void CombiningSequenceIsNeverNormalized()
    {
        // Zero normalization: "e" + combining acute does not fold to U+00E9 — the
        // e + acute code points refuse as unmapped on a face that maps only é,
        // while the precomposed character itself resolves (the registered honest
        // boundary: the request is interpreted over its characters as given).
        var face = RebuildFace((0xE9, 1));
        var folded = SubsetOperation.TryPrepare(
            TextRequest(face, "e\u0301"), out var foldedResult, out _,
            out var foldedDiagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, folded);
        Assert.Equal(DiagnosticCode.InvalidValue, foldedDiagnostic.Code);
        Assert.Null(foldedResult);
        var precomposed = SubsetOperation.TryPrepare(
            TextRequest(face, "\u00E9"), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, precomposed);
        Assert.NotNull(result);
    }

    [Fact]
    public void SurrogatePairTextResolvesThroughTheAstralMapping()
    {
        // A legal pair is one scalar: the step-1 walk consumes both code units, so
        // the text root resolves like the equal codepoint list — the astral mapping
        // rides the canonical cmap's format 12 record (format 4 cannot carry it).
        var face = RebuildFace((0x1F600, 1));
        var status = SubsetOperation.TryPrepare(TextRequest(face, "\U0001F600"),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Report.UnselectedSlotCount); // {0,1} of 4 retained
        // The pair decodes to the single scalar the codepoint form names.
        var twin = SubsetOperation.TryPrepare(
            CodepointRequest(face, [0x1F600]), out var twinResult, out _, out _);
        Assert.Equal(ReadStatus.Done, twin);
        Assert.NotNull(twinResult);
        Assert.Equal(OutputBytes(result), OutputBytes(twinResult!));
    }

    [Fact]
    public void BmpTextAfterAPairResumesTheWalk()
    {
        // The pair's two consumed units do not desync the walk: the BMP letter after
        // the pair is still walked (and would refuse a following lone surrogate).
        var face = RebuildFace((0x41, 1), (0x1F600, 2));
        var status = SubsetOperation.TryPrepare(TextRequest(face, "\U0001F600A"),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        Assert.Equal(1, report!.UnselectedSlotCount); // {0,1,2} of 4 retained
    }

    [Theory]
    [InlineData(0)] // lone high before a BMP letter, not at the end
    [InlineData(1)] // unpaired low ahead of a legal pair
    [InlineData(2)] // lone low trailing a legal pair
    public void UnpairedSurrogatesRefuseAtTheirOwnOffset(int form)
    {
        // Pairing never moves a refusal onto a pair's low half, and the consumption
        // leaves any further lone unit exposed at its own offset. The strings are
        // built in the body: theory serialization cannot carry lone surrogates.
        string text = form switch
        {
            0 => new string(new[] { (char)0xD83D, 'a' }),
            1 => new string(new[] { (char)0xDC00, '\uD83D', '\uDE00' }),
            _ => "\U0001F600" + new string((char)0xDC00, 1)
        };
        var status = SubsetOperation.TryPrepare(
            TextRequest(RebuildFace((0x41, 1)), text),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.InvalidUtf16, diagnostic.Code);
        Assert.Equal(DataField.Payload, diagnostic.Location.Field);
        Assert.Equal(form == 2 ? 2UL : 0UL, diagnostic.Location.Offset);
        Assert.Null(result);
    }

    // ---- derived-scope pins (SR card §2.3, the S2c face) ------------------------

    /// <summary>The S2c synthetic GSUB face (the SubsetLayoutScopeTests shape): one
    /// DFLT script, features TagA/TagB, three lookups; the cmap maps 0x41→1.</summary>
    private static FontFaceRef GsubFace()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 4 }, NumGlyphs, 1,
            out var contextualCoverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        var record = new SequenceLookupRecord(0, 1);
        Ok(ContextualSubstTableCompiler.TryCreateRule(stackalloc ushort[] { 5 },
            [record], 3, out var rule, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreateRuleSet([rule!], out var ruleSet,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreate(contextualCoverage!,
            [ruleSet!], NumGlyphs, out var contextual, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateContextualSubst(NumGlyphs, [contextual!],
            0, null, null, out var contextualLookup, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 6 },
            stackalloc ushort[] { 7 }, NumGlyphs, 1, out var orphanSingle, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [orphanSingle!],
            0, null, null, out var orphanLookup, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            stackalloc ushort[] { 2 }, NumGlyphs, 1, out var plainSingle, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [plainSingle!],
            0, null, null, out var plainLookup, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateLangSys(ushort.MaxValue,
            stackalloc ushort[] { 0, 1 }, 2, out var langSys, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateScript(TagDflt, langSys!, [], 2,
            out var script, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateFeature(TagA, 0, stackalloc ushort[] { 0 },
            3, out var featureA, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateFeature(TagB, 0, stackalloc ushort[] { 2 },
            3, out var featureB, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreate(LayoutListsLayout.Version10, 0,
            [script!], [featureA!, featureB!],
            [contextualLookup!, orphanLookup!, plainLookup!], NumGlyphs,
            LayoutLookupKind.Gsub, out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(GsubTableCompiler.TryCompile(model!, null, out var gsub, out diagnostic));
        NoDiagnostic(diagnostic);

        var glyphs = new List<byte>();
        var loca = new List<uint> { 0 };
        for (int i = 0; i < NumGlyphs; i++)
        {
            glyphs.AddRange(GlyfImages.NoContourGlyphRecord());
            loca.Add((uint)(12 * (i + 1)));
        }
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
            (SubsetFontFixture.GsubTag, gsub!),
            (SubsetFontFixture.GlyfTag, [.. glyphs]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca(loca)),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))));
    }

    private static void Ok(ReadStatus status) => Assert.Equal(ReadStatus.Done, status);

    private static void NoDiagnostic(FontDiagnostic diagnostic) =>
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);

    [Fact]
    public void DerivedScopeCensusCarriesTheMatchCounts()
    {
        // The derived declaration rides the S2c machine: the census row carries the
        // per-dimension match counts (one script match, one dangling script tag, two
        // feature matches), and the TextRootScope records register the declaration.
        var options = new SubsetOptions { PreserveLayout = true };
        var status = SubsetOperation.TryPrepare(
            TextRequest(GsubFace(), "A", options,
                scripts: [TagDflt, 0x5A5A5A5A], features: [TagA, TagB]),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var censusRow = Assert.Single(report!.LayoutScopeCensus);
        Assert.Equal(1, censusRow.ScriptTagMatches);
        Assert.Equal(2, censusRow.FeatureTagMatches);
        Assert.Equal(0, censusRow.LanguageTagMatches);
        Assert.Contains(report.OptionRecords, r => r.Id == SubsetOptionId.TextRootScope &&
            r.Tag == TagDflt && r.Value == "Script");
        Assert.Contains(report.OptionRecords, r => r.Id == SubsetOptionId.TextRootScope &&
            r.Tag == 0x5A5A5A5A && r.Value == "Script");
        Assert.Contains(report.OptionRecords, r => r.Id == SubsetOptionId.Root &&
            r.Value == nameof(SubsetRootKind.TextLayout));
    }

    [Fact]
    public void DualSourceScopeDeclarationRefuses()
    {
        // OQ-SR-4, detail 2: two scope authorities have no merge semantics.
        var options = new SubsetOptions
        {
            PreserveLayout = true,
            LayoutScope = new SubsetLayoutScope { FeatureTags = [TagB] }
        };
        var status = SubsetOperation.TryPrepare(
            TextRequest(GsubFace(), "A", options, scripts: [TagDflt]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Equal(2UL, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void DerivedScopeContradictsNoLayout()
    {
        var options = new SubsetOptions { NoLayout = true };
        var status = SubsetOperation.TryPrepare(
            TextRequest(GsubFace(), "A", options, scripts: [TagDflt]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Equal(1UL, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void DerivedScopeWithoutPreserveLayoutRefuses()
    {
        // The foot-gun form (detail 0): a scope with no retention path — NoLayout
        // stays false, so this is the pure PreserveLayout contradiction.
        var status = SubsetOperation.TryPrepare(
            TextRequest(GsubFace(), "A", new SubsetOptions(), scripts: [TagDflt]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Equal(0UL, diagnostic.Detail);
        Assert.Null(result);
    }

    [Fact]
    public void TextRootWithoutListsKeepsTheExplicitPathSemantics()
    {
        // A TextLayout root with no declared list is pure Unicode-root semantics: no
        // derived scope, no TextRootScope records, and the layout options behave
        // exactly as on an explicit request (NoLayout drops the family).
        var status = SubsetOperation.TryPrepare(
            TextRequest(GsubFace(), "A", NoLayout()),
            out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Contains<uint>(SubsetFontFixture.GsubTag, report!.DroppedTables);
        Assert.DoesNotContain(report.OptionRecords, r => r.Id == SubsetOptionId.TextRootScope);
    }

    [Fact]
    public void DerivedNoOpScopeIsByteIdenticalToNoDeclaration()
    {
        // §4.2's consequence-equivalence verdict, derived form: the full declaration
        // (plus a dangling tag) short-circuits to the undeclared decision — fonts
        // and reports agree byte for byte.
        var undeclared = new SubsetOptions { PreserveLayout = true };
        var declared = new SubsetOptions { PreserveLayout = true };
        var first = SubsetOperation.TryPrepare(
            TextRequest(GsubFace(), "A", undeclared), out var plain, out var plainReport,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, first);
        var second = SubsetOperation.TryPrepare(
            TextRequest(GsubFace(), "A", declared,
                scripts: [TagDflt, 0x5A5A5A5A], features: [TagA, TagB]),
            out var scoped, out var scopedReport, out diagnostic);
        Assert.Equal(ReadStatus.Done, second);
        Assert.NotNull(plain);
        Assert.NotNull(scoped);
        Assert.Equal(OutputBytes(plain!), OutputBytes(scoped!));
        Assert.Equal(plainReport!.Decisions, scopedReport!.Decisions);
    }

    // ---- seeding equivalence and the identity tier (SR card §5.4) ---------------

    [Fact]
    public void ResolvedSetSeedsLikeTheEqualExplicitRoot()
    {
        // Identity tier (a): one pipeline, one determinism — the resolved set G and
        // the explicit GID root G publish identical fonts.
        var face = RebuildFace((0x20, 3), (0x41, 1), (0x42, 2));
        var resolved = SubsetOperation.TryPrepare(
            CodepointRequest(face, [0x20, 0x41, 0x42]), out var fromCodepoints,
            out _, out _);
        Assert.Equal(ReadStatus.Done, resolved);
        var explicitStatus = SubsetOperation.TryPrepare(
            ExplicitRequest(face, [1, 2, 3]), out var fromGids, out _, out _);
        Assert.Equal(ReadStatus.Done, explicitStatus);
        Assert.NotNull(fromCodepoints);
        Assert.NotNull(fromGids);
        Assert.Equal(OutputBytes(fromCodepoints!), OutputBytes(fromGids!));
    }

    [Fact]
    public void FullCoverageIdentityHoldsOnTheSyntheticFace()
    {
        // Identity tier (b)'s shape on a face this test controls end to end: the cmap
        // covers every non-.notdef glyph, so the all-codepoints root and the all-GID
        // explicit root are the same seed {0,1,2,3} and the same bytes. The tier is
        // conditional on full coverage by construction — a coverage-partial face
        // (the roboto 2770/3387 shape) keeps unmapped slots empty under the
        // codepoints root and is deliberately not pinned here (SR card §5.4(b)'s
        // registered precondition; the fontations anchor is the E2E gate's).
        var face = RebuildFace((0x41, 1), (0x42, 2), (0x43, 3));
        var allCodepoints = SubsetOperation.TryPrepare(
            CodepointRequest(face, [0x41, 0x42, 0x43]), out var fromCodepoints,
            out _, out _);
        Assert.Equal(ReadStatus.Done, allCodepoints);
        Assert.NotNull(fromCodepoints);
        Assert.Equal(0, fromCodepoints!.Report.UnselectedSlotCount);
        var allGids = SubsetOperation.TryPrepare(
            ExplicitRequest(face, [0, 1, 2, 3]), out var fromGids, out _, out _);
        Assert.Equal(ReadStatus.Done, allGids);
        Assert.NotNull(fromGids);
        Assert.Equal(OutputBytes(fromCodepoints), OutputBytes(fromGids));
    }

    // ---- the budget pin (R9) -----------------------------------------------------

    [Fact]
    public void ResolutionWorkBudgetExhaustionRefuses()
    {
        // The S0-BF-001 shape over the resolved root: the decode and the point
        // queries charge Work like every other stage, so one unit below admission
        // turns the outcome into ResourceLimit/BudgetExceeded on the WorkUnits field
        // with no partial artifact and a retained report.
        var request = CodepointRequest(RebuildFace((0x41, 1), (0x42, 2)), [0x41, 0x42]);
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
        var status = SubsetOperation.TryPrepare(request, out var result, out var report,
            out var diagnostic, new BuildLimits { MaxWorkUnits = high - 1 });
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(DataField.WorkUnits, diagnostic.Location.Field);
        Assert.Null(result);
        Assert.NotNull(report);
        Assert.NotEmpty(report!.Diagnostics);
    }

    private static ReadStatus PrepareStatus(SubsetRequest request, BuildLimits limits) =>
        SubsetOperation.TryPrepare(request, out _, out _, out _, limits);

    [Fact]
    public void RootResolutionArraysAreChargedToTheTemporaryBudget()
    {
        // R9 over the stage's own payloads: the decode's final array and the resolved
        // GID array are charged before allocation (the codepoint list is consumed in
        // place — no copy), so the resolved roots cost measurably more than the
        // byte-identical explicit root and a cap one byte under the need refuses
        // finitely with no partial state.
        var face = RebuildFace((0x41, 1), (0x42, 2));
        Assert.Equal(ReadStatus.Done, SubsetOperation.TryPrepare(
            ExplicitRequest(face, [1, 2]), out var explicitResult, out _, out _,
            new BuildLimits()));
        long explicitBytes = explicitResult!.Font.Usage.TemporaryBytes;

        var codepoints = CodepointRequest(face, [0x41, 0x42]);
        Assert.Equal(ReadStatus.Done, SubsetOperation.TryPrepare(codepoints,
            out var codepointResult, out _, out _, new BuildLimits()));
        long codepointBytes = codepointResult!.Font.Usage.TemporaryBytes;
        // The resolved {1,2} array is charged at two bytes per GID over the explicit
        // twin; everything else the pipeline charges is identical.
        Assert.Equal(explicitBytes + 2L * 2, codepointBytes);

        var text = TextRequest(face, "AB");
        Assert.Equal(ReadStatus.Done, SubsetOperation.TryPrepare(text,
            out var textResult, out _, out _, new BuildLimits()));
        long textBytes = textResult!.Font.Usage.TemporaryBytes;
        // The decode's array is charged at its worst case, one uint per code unit:
        // four bytes per character over the codepoint twin.
        Assert.Equal(codepointBytes + 4L * 2, textBytes);

        // A cap one byte under the resolved root's need refuses at TemporaryBytes —
        // the increment over the admitted explicit twin is exactly the new array —
        // with no result and the budget diagnostic retained on the report.
        var refused = SubsetOperation.TryPrepare(codepoints, out var refusedResult,
            out var refusedReport, out var diagnostic,
            new BuildLimits { MaxTemporaryBytes = codepointBytes - 1 });
        Assert.Equal(ReadStatus.ResourceLimit, refused);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(DataField.TemporaryBytes, diagnostic.Location.Field);
        Assert.Null(refusedResult);
        FontDiagnostic retained = Assert.Single(refusedReport!.Diagnostics);
        Assert.Equal(DiagnosticCode.BudgetExceeded, retained.Code);
        Assert.Equal(ReadStatus.Done,
            PrepareStatus(codepoints, new BuildLimits { MaxTemporaryBytes = codepointBytes }));

        // The text form's decode charge makes it the first to hit a cap sized
        // between the two twins, and the exact need is admitted.
        Assert.Equal(ReadStatus.ResourceLimit,
            PrepareStatus(text, new BuildLimits { MaxTemporaryBytes = textBytes - 1 }));
        Assert.Equal(ReadStatus.Done,
            PrepareStatus(text, new BuildLimits { MaxTemporaryBytes = textBytes }));
    }
}
