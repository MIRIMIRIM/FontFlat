// S2b-1 compact GSUB/GPOS rewrite unit tests (S2b card §2.1, commit ③ acceptance).
//
// Each test builds one lookup's wire through the public compilers, runs a
// compact+PreserveLayout request over a synthetic face, and re-reads the output
// table through the typed view — asserting remapped semantics, never bytes (the
// canonical re-emission is wire-equivalent by pre-registration, OQ-S2b-4). The
// fixture face covers the forms with public explicit factories: GSUB 1 (with the
// fmt1→fmt2 re-selection witness), 2, 3, 5 (format 2's class-domain shrink), 6
// (format 3), 7, 8; GPOS 1, 2 (formats 1 and 2). The forms whose input models have
// no public factory (GSUB 4's ligature sets, GPOS 3/4/5/6's anchors, GPOS 9's
// type-9 lookup) exercise on the real corpus in the FontWeave.S2b1 gate (roboto
// carries GSUB T4, GPOS T4/ext and GPOS 9 chains; opensans carries GPOS T5).
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetLayoutRewriteFormsTests
{
    private const int NumGlyphs = 10;

    private static void Ok(ReadStatus status)
    {
        Assert.Equal(ReadStatus.Done, status);
    }

    private static void NoDiagnostic(FontDiagnostic diagnostic)
    {
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
    }

    private static LookupTable One(ReadOnlySpan<LookupTable> lookups)
    {
        Assert.Equal(1, lookups.Length);
        return lookups[0];
    }

    private static byte[] Compiled(LayoutLookupKind kind, LookupTable model)
    {
        ReadStatus status;
        FontDiagnostic diagnostic;
        byte[]? bytes;
        if (kind == LayoutLookupKind.Gsub)
        {
            status = GsubTableCompiler.TryCreate(LayoutListsLayout.Version10, 0, [],
                [], [model], NumGlyphs, out var gsubRoot, out diagnostic);
            NoDiagnostic(diagnostic);
            Assert.Equal(ReadStatus.Done, status);
            status = GsubTableCompiler.TryCompile(gsubRoot!, null, out bytes,
                out diagnostic);
        }
        else
        {
            status = GposTableCompiler.TryCreate(LayoutListsLayout.Version10, 0, [],
                [], [model], NumGlyphs, out var gposRoot, out diagnostic);
            NoDiagnostic(diagnostic);
            Assert.Equal(ReadStatus.Done, status);
            status = GposTableCompiler.TryCompile(gposRoot!, null, out bytes,
                out diagnostic);
        }
        NoDiagnostic(diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(bytes);
        return bytes;
    }

    private static FontFaceRef Face(LayoutLookupKind kind, byte[] tableBytes)
    {
        uint tag = kind == LayoutLookupKind.Gsub
            ? SubsetFontFixture.GsubTag
            : SubsetFontFixture.GposTag;
        var glyf = new List<byte>();
        var loca = new List<uint> { 0 };
        for (int i = 0; i < NumGlyphs; i++)
        {
            var record = GlyfImages.NoContourGlyphRecord();
            glyf.AddRange(record);
            loca.Add(loca[^1] + (uint)record.Length);
        }
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
            (tag, tableBytes),
            (SubsetFontFixture.GlyfTag, [.. glyf]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([.. loca])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))));
    }

    private static SubsetResult Run(FontFaceRef face, IReadOnlyList<ushort> gids)
    {
        var request = new SubsetRequest
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
            Mode = SubsetMode.CompactRemap,
            Options = new SubsetOptions { PreserveLayout = true }
        };
        var status = SubsetOperation.TryPrepare(request, out var result, out _,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        return result!;
    }

    private static LayoutListsTable OutputLists(SubsetResult result,
        LayoutLookupKind kind, int outputNumGlyphs)
    {
        byte[] output = new byte[result.Font.OutputLength];
        Assert.Equal(ReadStatus.Done,
            result.Font.TryCopyTo(output, out int written, out _));
        Assert.Equal(output.Length, written);
        byte[] table = TableBytes(output, kind == LayoutLookupKind.Gsub
            ? SubsetFontFixture.GsubTag
            : SubsetFontFixture.GposTag);
        Ok(LayoutListsView.TryOpen(table, out var view, out var diagnostic,
            DataLocation.Anonymous(kind == LayoutLookupKind.Gsub
                ? SubsetFontFixture.GsubTag
                : SubsetFontFixture.GposTag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryReadTable(outputNumGlyphs, kind, null, out var model,
            out diagnostic));
        NoDiagnostic(diagnostic);
        return model!;
    }

    // ---- GSUB 1, identity form: the uniform format survives a monotone map.
    [Fact]
    public void GsubSingleIdentityRemapKeepsUniformFormat()
    {
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1, 2 },
            stackalloc ushort[] { 2, 3 }, NumGlyphs, 1, out var single,
            out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [single!], 0, null,
            null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        byte[] gsub = Compiled(LayoutLookupKind.Gsub, lookup!);
        var result = Run(Face(LayoutLookupKind.Gsub, gsub), [1]);
        // The closure retains {0,1,2,3}: the identity order.
        var output = OutputLists(result, LayoutLookupKind.Gsub, 4);
        var outLookup = Assert.Single(output.Lookups.ToArray());
        var outSingle = Assert.Single(outLookup.SingleSubstitutions!);
        Assert.Equal(1, outSingle.Format);
        Assert.Equal(new ushort[] { 1, 2 }, outSingle.Coverage.Glyphs.ToArray());
        Assert.Equal(new ushort[] { 2, 3 }, outSingle.Substitutes.ToArray());
    }

    // ---- GSUB 1, the OQ-S2b-4 re-selection witness: the map breaks the uniform
    // delta, so the rewrite re-answers the subtable's format as 2.
    [Fact]
    public void GsubSingleReselectsFormatTwoWhenTheMapBreaksTheUniformDelta()
    {
        // Input fmt1: coverage {1,4} + uniform delta 2 -> substitutes {3,6}; the
        // seed's GID 2 fills the first rank window but not the second, so the
        // output ranks shift the two rows by different deltas — format 1 cannot
        // answer, format 2 is the definitional re-selection.
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1, 4 },
            stackalloc ushort[] { 3, 6 }, NumGlyphs, 1, out var single,
            out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [single!], 0, null,
            null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        byte[] gsub = Compiled(LayoutLookupKind.Gsub, lookup!);
        var result = Run(Face(LayoutLookupKind.Gsub, gsub), [1, 2]);
        // Retained {0,1,2} u {1,4} u {3,6} = {0..4,6}; ranks 1->1, 4->4, 3->3,
        // 6->5: the first row keeps delta 2, the second shifts to 1.
        var output = OutputLists(result, LayoutLookupKind.Gsub, 6);
        var outLookup = One(output.Lookups);
        var outSingle = Assert.Single(outLookup.SingleSubstitutions!);
        Assert.Equal(2, outSingle.Format);
        Assert.Equal(new ushort[] { 1, 4 }, outSingle.Coverage.Glyphs.ToArray());
        Assert.Equal(new ushort[] { 3, 5 }, outSingle.Substitutes.ToArray());
    }

    // ---- GSUB 2/3: the sequences ride their coverage ordinals.
    [Fact]
    public void GsubMultipleAndAlternateRemapSequences()
    {
        Ok(MultipleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            [[2, 3]], NumGlyphs, out var multiple, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateMultipleSubst(NumGlyphs, [multiple!], 0, null,
            null, out var multipleLookup, out diagnostic));
        NoDiagnostic(diagnostic);
        byte[] gsub = Compiled(LayoutLookupKind.Gsub, multipleLookup!);
        var result = Run(Face(LayoutLookupKind.Gsub, gsub), [1]);
        var output = OutputLists(result, LayoutLookupKind.Gsub, 4);
        var outMultiple = Assert.Single(One(output.Lookups).Multiples!);
        Assert.Equal(new ushort[] { 2, 3 }, outMultiple.GetSequence(0).ToArray());

        Ok(AlternateSubstTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            [[2, 4]], NumGlyphs, out var alternate, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateAlternateSubst(NumGlyphs, [alternate!], 0,
            null, null, out var alternateLookup, out diagnostic));
        NoDiagnostic(diagnostic);
        gsub = Compiled(LayoutLookupKind.Gsub, alternateLookup!);
        result = Run(Face(LayoutLookupKind.Gsub, gsub), [1]);
        output = OutputLists(result, LayoutLookupKind.Gsub, 5);
        var outAlternate = Assert.Single(One(output.Lookups).Alternates!);
        // Retained {0,1,2,4}: rank(4)=3 — the set remaps {2,4}->{2,3}.
        Assert.Equal(new ushort[] { 2, 3 }, outAlternate.GetAlternateSet(0).ToArray());
    }

    // ---- GSUB 5 format 2: the class-valued rules and rule sets stay untouched;
    // the class domain shrinks per entry with identical values (OQ-S2b-3's GSUB face).
    [Fact]
    public void GsubContextualFormat2KeepsRulesAndShrinksTheClassDomain()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1 }, NumGlyphs, 1,
            out var coverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate(
            [new ClassDefRange(1, 1, 1), new ClassDefRange(3, 3, 2)], NumGlyphs, 2,
            out var classDef, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreateRule(stackalloc ushort[] { 1 },
            [new SequenceLookupRecord(0, 0)], 1, out var rule, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ContextualSubstTableCompiler.TryCreateRuleSet([rule!], out var ruleSet,
            out diagnostic));
        NoDiagnostic(diagnostic);
        // One coverage key (glyph 1, class 1): the rule-set array carries one entry.
        Ok(ContextualSubstTableCompiler.TryCreate(coverage!, classDef!,
            new ContextualRuleSet?[] { ruleSet }, NumGlyphs, out var contextual,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateContextualSubst(NumGlyphs, [contextual!], 0,
            null, null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        byte[] gsub = Compiled(LayoutLookupKind.Gsub, lookup!);
        var result = Run(Face(LayoutLookupKind.Gsub, gsub), [1]);
        var output = OutputLists(result, LayoutLookupKind.Gsub, 4);
        var outContextual = Assert.Single(One(output.Lookups).Contextuals!);
        Assert.Equal(2, outContextual.Format);
        // Glyph 3 was never referenced: its class-2 entry drops; class values do
        // not renumber (the surviving entry keeps class 1).
        var ranges = outContextual.ClassDef!.Ranges.ToArray();
        Assert.Single(ranges);
        Assert.Equal(new ClassDefRange(1, 1, 1), ranges[0]);
        // The rule-set array (class-value keyed) is structurally identical, and the
        // rule's seqLookup record kept its indices byte-stable.
        var sets = outContextual.RuleSets!;
        Assert.Single(sets);
        Assert.Equal(1, sets[0]!.RuleCount);
        var outRule = sets[0]!.Rules[0];
        Assert.Equal(new SequenceLookupRecord(0, 0), outRule.LookupRecords[0]);
    }

    // ---- GSUB 6 format 3: the three coverage chains remap in declared order.
    [Fact]
    public void GsubChainingFormat3RemapsTheCoverageChains()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1 }, NumGlyphs, 1,
            out var back, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 2 }, NumGlyphs, 1,
            out var input, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 3 }, NumGlyphs, 1,
            out var ahead, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ChainingSubstTableCompiler.TryCreateFormat3([back!], [input!], [ahead!],
            [new SequenceLookupRecord(0, 0)], NumGlyphs, out var chaining,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateChainingSubst(NumGlyphs, [chaining!], 0, null,
            null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        byte[] gsub = Compiled(LayoutLookupKind.Gsub, lookup!);
        var result = Run(Face(LayoutLookupKind.Gsub, gsub), [1, 2, 3]);
        var output = OutputLists(result, LayoutLookupKind.Gsub, 4);
        var outChaining = Assert.Single(One(output.Lookups).Chainings!);
        Assert.Equal(3, outChaining.Format);
        Assert.Equal(new ushort[] { 1 },
            outChaining.BacktrackCoverages![0].Glyphs.ToArray());
        Assert.Equal(new ushort[] { 2 },
            outChaining.InputCoverages![0].Glyphs.ToArray());
        Assert.Equal(new ushort[] { 3 },
            outChaining.LookaheadCoverages![0].Glyphs.ToArray());
        Assert.Equal(new SequenceLookupRecord(0, 0), outChaining.LookupRecords![0]);
    }

    // ---- GSUB 7: the extension wrapper keeps its type and rewrites the leaf.
    [Fact]
    public void GsubExtensionRewritesTheWrappedLeaf()
    {
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            stackalloc ushort[] { 2 }, NumGlyphs, 2, out var single,
            out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ExtensionSubstTable.TryCreate(1, single, null, null, null, out var ext,
            out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateExtensionSubst(NumGlyphs, [ext!], 0, null,
            null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        byte[] gsub = Compiled(LayoutLookupKind.Gsub, lookup!);
        var result = Run(Face(LayoutLookupKind.Gsub, gsub), [1]);
        var output = OutputLists(result, LayoutLookupKind.Gsub, 3);
        var outLookup = One(output.Lookups);
        Assert.Equal(7, outLookup.LookupType);
        var outExt = Assert.Single(outLookup.Extensions!);
        Assert.Equal(1, outExt.ExtensionLookupType);
        var outSingle = outExt.Single!;
        Assert.Equal(new ushort[] { 1 }, outSingle.Coverage.Glyphs.ToArray());
        Assert.Equal(new ushort[] { 2 }, outSingle.Substitutes.ToArray());
    }

    // ---- GSUB 8: the substitute list rides the coverage; the chains remap.
    [Fact]
    public void GsubReverseChainRemapsCoverageAndSubstitutes()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1 }, NumGlyphs, 1,
            out var coverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 2 }, NumGlyphs, 1,
            out var backtrack, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ReverseChainSubstTableCompiler.TryCreate(coverage!, [backtrack!], [],
            stackalloc ushort[] { 3 }, NumGlyphs, out var reverse, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateReverseChainSubst(NumGlyphs, [reverse!], 0,
            null, null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        byte[] gsub = Compiled(LayoutLookupKind.Gsub, lookup!);
        var result = Run(Face(LayoutLookupKind.Gsub, gsub), [1, 2, 3]);
        var output = OutputLists(result, LayoutLookupKind.Gsub, 4);
        var outReverse = Assert.Single(One(output.Lookups).ReverseChains!);
        Assert.Equal(new ushort[] { 1 }, outReverse.Coverage.Glyphs.ToArray());
        Assert.Equal(new ushort[] { 3 }, outReverse.Substitutes.ToArray());
        Assert.Equal(new ushort[] { 2 },
            outReverse.BacktrackCoverages[0].Glyphs.ToArray());
    }

    // ---- GPOS 1: the value rows ride their coverage ordinals.
    [Fact]
    public void GposSingleRemapsCoverageAndKeepsValues()
    {
        Ok(SinglePosTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            [new StaticValueRecord(0, 0, 5, 0)], NumGlyphs, 2, 0x0004,
            out var single, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSinglePos(NumGlyphs, [single!], 0, null,
            null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        byte[] gpos = Compiled(LayoutLookupKind.Gpos, lookup!);
        var result = Run(Face(LayoutLookupKind.Gpos, gpos), [1]);
        var output = OutputLists(result, LayoutLookupKind.Gpos, 2);
        var outSingle = Assert.Single(One(output.Lookups).SinglePositions!);
        Assert.Equal(new ushort[] { 1 }, outSingle.Coverage.Glyphs.ToArray());
        Assert.Equal(5, outSingle.Values[0].XAdvance);
    }

    // ---- GPOS 2 format 1: the pair sets permute with the coverage and each set's
    // second-glyph strict order re-answers over the output keys.
    [Fact]
    public void GposPairFormat1RemapsSetsAndSecondGlyphs()
    {
        var first = new[]
        {
            new PairValueRecord(2, new StaticValueRecord(0, 0, 10, 0), default),
            new PairValueRecord(6, new StaticValueRecord(0, 0, 20, 0), default),
        };
        var second = new[] { new PairValueRecord(3, default, default) };
        Ok(PairPosTableCompiler.TryCreate(stackalloc ushort[] { 1, 4 },
            [first, second], NumGlyphs, 0x0004, 0, out var pair, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreatePairPos(NumGlyphs, [pair!], 0, null, null,
            out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        byte[] gpos = Compiled(LayoutLookupKind.Gpos, lookup!);
        var result = Run(Face(LayoutLookupKind.Gpos, gpos), [1]);
        // Retained {0,1,2,3,4,6}: ranks 1->1, 4->4, 6->5, so the first set's
        // second glyphs remap {2,6}->{2,5} and the coverage {1,4}->{1,4}; the
        // values ride their coverage ordinals.
        var output = OutputLists(result, LayoutLookupKind.Gpos, 7);
        var outPair = Assert.Single(One(output.Lookups).PairPositions!);
        Assert.Equal(1, outPair.Format);
        Assert.Equal(new ushort[] { 1, 4 }, outPair.Coverage.Glyphs.ToArray());
        var firstSet = outPair.PairSets![0];
        Assert.Equal(2, firstSet.PairValueCount);
        Assert.Equal(2, firstSet.Records[0].SecondGlyph);
        Assert.Equal(10, firstSet.Records[0].Value1.XAdvance);
        Assert.Equal(5, firstSet.Records[1].SecondGlyph);
        Assert.Equal(20, firstSet.Records[1].Value1.XAdvance);
        var secondSet = outPair.PairSets[1];
        Assert.Equal(3, secondSet.Records[0].SecondGlyph);
    }

    // ---- GPOS 2 format 2: the class-keyed matrix values stay per (class1, class2);
    // the class domains' entries keep their values (the fmt2 closure bound keeps
    // the whole domain, so nothing drops here — the drop form is GDEF/GSUB 5's).
    [Fact]
    public void GposPairFormat2KeepsTheMatrixValues()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1 }, NumGlyphs, 1,
            out var coverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate([new ClassDefRange(1, 2, 1)], NumGlyphs, 2,
            out var classDef1, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate([new ClassDefRange(3, 4, 1)], NumGlyphs, 2,
            out var classDef2, out diagnostic));
        NoDiagnostic(diagnostic);
        var values1 = new StaticValueRecord[4];
        var values2 = new StaticValueRecord[4];
        values1[0] = new StaticValueRecord(0, 0, 7, 0);
        Ok(PairPosTableCompiler.TryCreate(coverage!.Glyphs, classDef1!.Ranges,
            classDef2!.Ranges, 2, 2, values1, values2, NumGlyphs, 0x0004, 0,
            out var pair, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreatePairPos(NumGlyphs, [pair!], 0, null, null,
            out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        byte[] gpos = Compiled(LayoutLookupKind.Gpos, lookup!);
        var result = Run(Face(LayoutLookupKind.Gpos, gpos), [1]);
        // The pair format-2 class domains merge the full glyph domain (the closure's
        // OQ-S2a-6 bound), so the output domain stays all 10 glyphs and every class
        // entry survives with its value.
        var output = OutputLists(result, LayoutLookupKind.Gpos, NumGlyphs);
        var outPair = Assert.Single(One(output.Lookups).PairPositions!);
        Assert.Equal(2, outPair.Format);
        Assert.Equal(2, outPair.Class1Count);
        Assert.Equal(2, outPair.Class2Count);
        outPair.TryGetMatrixValue(0, 0, out var cell1, out _, out diagnostic);
        NoDiagnostic(diagnostic);
        Assert.Equal(7, cell1.XAdvance);
    }

    private static byte[] TableBytes(byte[] sfnt, uint tag)
    {
        int numTables = (sfnt[4] << 8) | sfnt[5];
        for (int i = 0; i < numTables; i++)
        {
            int at = 12 + 16 * i;
            uint found = (uint)(sfnt[at] << 24 | sfnt[at + 1] << 16 |
                sfnt[at + 2] << 8 | sfnt[at + 3]);
            if (found != tag) continue;
            int offset = (sfnt[at + 8] << 24) | (sfnt[at + 9] << 16) |
                (sfnt[at + 10] << 8) | sfnt[at + 11];
            int length = (sfnt[at + 12] << 24) | (sfnt[at + 13] << 16) |
                (sfnt[at + 14] << 8) | sfnt[at + 15];
            return sfnt[offset..(offset + length)];
        }
        throw new Xunit.Sdk.XunitException($"tag {tag} missing from output");
    }
}
