using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetSelectiveLayoutRewriteTests
{
    private const int NumGlyphs = 12;

    [Fact]
    public void SinglePositionFiltersCoverageAndParallelValues()
    {
        Ok(SinglePosTableCompiler.TryCreate([1, 4, 7],
            [Advance(11), Advance(44), Advance(77)], NumGlyphs, 2, 4,
            out var single, out _));
        Ok(LookupTableCompiler.TryCreateSinglePos(NumGlyphs, [single!], 0,
            null, null, out var lookup, out _));

        var (result, table) = Run(LayoutLookupKind.Gpos, [lookup!], [4, 7]);

        Assert.Equal(new ushort[] { 0, 4, 7 }, Inputs(result));
        var output = Assert.Single(table.Lookups.ToArray()).SinglePositions![0];
        Assert.Equal(new ushort[] { 1, 2 }, output.Coverage.Glyphs.ToArray());
        Assert.Equal(new short[] { 44, 77 }, output.Values.ToArray()
            .Select(value => value.XAdvance).ToArray());
    }

    [Fact]
    public void PairPositionFiltersFirstAndSecondGlyphsWithoutMovingValues()
    {
        Ok(PairPosTableCompiler.TryCreate([1, 4],
            [
                [new PairValueRecord(7, Advance(17), default)],
                [new PairValueRecord(2, Advance(42), default),
                    new PairValueRecord(7, Advance(47), default)]
            ], NumGlyphs, 4, 0, out var pair, out _));
        Ok(LookupTableCompiler.TryCreatePairPos(NumGlyphs, [pair!], 0,
            null, null, out var lookup, out _));

        var (result, table) = Run(LayoutLookupKind.Gpos, [lookup!], [4, 7]);

        Assert.Equal(new ushort[] { 0, 4, 7 }, Inputs(result));
        var output = Assert.Single(table.Lookups.ToArray()).PairPositions![0];
        Assert.Equal(new ushort[] { 1 }, output.Coverage.Glyphs.ToArray());
        var record = Assert.Single(Assert.Single(output.PairSets!).Records.ToArray());
        Assert.Equal(2, record.SecondGlyph);
        Assert.Equal(47, record.Value1.XAdvance);
    }

    [Fact]
    public void PairClassMatrixCompactsLiveClassesAndPreservesImplicitClassZero()
    {
        var values = new StaticValueRecord[16];
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                values[row * 4 + column] = Advance((short)(row * 100 + column));
        Ok(PairPosTableCompiler.TryCreate([2, 4, 8, 9],
            [new(2, 2, 1), new(4, 4, 2), new(9, 9, 3)],
            [new(3, 3, 1), new(5, 5, 2), new(10, 10, 3)],
            4, 4, values, new StaticValueRecord[16], NumGlyphs, 4, 0,
            out var pair, out _, classDefFormat: 2));
        Ok(LookupTableCompiler.TryCreatePairPos(NumGlyphs, [pair!], 0,
            null, null, out var lookup, out _));

        var (result, table) = Run(LayoutLookupKind.Gpos, [lookup!], [4, 5, 8]);

        Assert.Equal(new ushort[] { 0, 4, 5, 8 }, Inputs(result));
        var output = Assert.Single(table.Lookups.ToArray()).PairPositions![0];
        Assert.Equal(new ushort[] { 1, 3 }, output.Coverage.Glyphs.ToArray());
        Assert.Equal(2, output.Class1Count);
        Assert.Equal(2, output.Class2Count);
        Assert.Equal(1, output.ClassDef1!.ClassOf(1));
        Assert.Equal(0, output.ClassDef1.ClassOf(3));
        Assert.Equal(1, output.ClassDef2!.ClassOf(2));
        Assert.Equal(0, output.ClassDef2.ClassOf(3));
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 2; column++)
            {
                Ok(output.TryGetMatrixValue(row, column, out var value, out _, out _));
                Assert.Equal(row * 200 + column * 2, value.XAdvance);
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PairClassOutsideCoverageDoesNotIndexTheMatrix(bool keepOutsideGlyph)
    {
        var lookup = PairClassLookup([new(9, 9, 1)], []);
        ushort[] roots = keepOutsideGlyph ? [4, 5, 9] : [4, 5];

        var (result, table) = Run(LayoutLookupKind.Gpos, [lookup], roots);

        Assert.Equal(keepOutsideGlyph ? new ushort[] { 0, 4, 5, 9 } : [0, 4, 5],
            Inputs(result));
        var output = Assert.Single(table.Lookups.ToArray()).PairPositions![0];
        Assert.Equal(new ushort[] { 1 }, output.Coverage.Glyphs.ToArray());
        Assert.Equal(1, output.Class1Count);
        Assert.Equal(1, output.Class2Count);
        Ok(output.TryGetMatrixValue(output.ClassDef1!.ClassOf(1),
            output.ClassDef2!.ClassOf(2), out var value, out _, out _));
        Assert.Equal(47, value.XAdvance);
    }

    [Theory]
    [InlineData(SubsetMode.CompactRemap)]
    [InlineData(SubsetMode.RetainGids)]
    public void FullGlyphDomainPreservesOutOfCoverageClassValues(SubsetMode mode)
    {
        const uint keptFeature = 0x6B65726E, droppedFeature = 0x70616C74;
        var lookup = PairClassLookup([new(9, 9, 1)], []);
        Ok(LayoutListsTableCompiler.TryCreateLangSys(ushort.MaxValue, [0, 1], 2,
            out var language, out _));
        Ok(LayoutListsTableCompiler.TryCreateScript(0x44464C54, language, [], 2,
            out var script, out _));
        Ok(LayoutListsTableCompiler.TryCreateFeature(keptFeature, 0, [0], 2,
            out var feature, out _));
        Ok(LayoutListsTableCompiler.TryCreateFeature(droppedFeature, 0, [1], 2,
            out var excluded, out _));
        Ok(GposTableCompiler.TryCreate(LayoutListsLayout.Version10, 0, [script!],
            [feature!, excluded!], [lookup, lookup], NumGlyphs, out var model, out _));
        Ok(GposTableCompiler.TryCompile(model!, null, out var bytes, out _));
        var roots = Enumerable.Range(0, NumGlyphs).Select(glyph => (ushort)glyph).ToArray();
        var request = Request(SubsetFontFixture.GposTag, bytes!, roots, mode,
            new() { PreserveLayout = true,
                LayoutScope = new() { FeatureTags = [keptFeature] } });

        Ok(SubsetOperation.TryPrepare(request, out var result, out var report,
            out var diagnostic));

        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        var scope = Assert.Single(report!.LayoutScopeCensus);
        Assert.Equal(1, scope.SurvivingFeatureCount);
        Assert.Equal(1, scope.SurvivingLookupCount);
        byte[] outputBytes = OutputTable(result!, SubsetFontFixture.GposTag);
        Ok(LayoutListsView.TryOpen(outputBytes, out var view, out _));
        Ok(view.TryReadTable(NumGlyphs, LayoutLookupKind.Gpos, null, out var output, out _));
        var pair = Assert.Single(output!.Lookups.ToArray()).PairPositions![0];
        Assert.Equal(new ushort[] { 4 }, pair.Coverage.Glyphs.ToArray());
        Assert.Equal(1, pair.Class1Count);
        Assert.Equal(1, pair.ClassDef1!.ClassOf(9));
        Ok(pair.TryGetMatrixValue(0, 0, out var value, out _, out _));
        Assert.Equal(47, value.XAdvance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActivePairClassOutsideMatrixReturnsDiagnostic(bool firstSide)
    {
        var lookup = PairClassLookup(firstSide ? [new(4, 4, 1)] : [],
            firstSide ? [] : [new(5, 5, 1)]);
        Ok(GposTableCompiler.TryCreate(LayoutListsLayout.Version10, 0, [], [],
            [lookup], NumGlyphs, out var model, out _));
        Ok(GposTableCompiler.TryCompile(model!, null, out var bytes, out _));

        var status = SubsetOperation.TryPrepare(
            Request(SubsetFontFixture.GposTag, bytes!, [4, 5]),
            out var result, out _, out var diagnostic);

        Assert.Equal(ReadStatus.InvalidIndex, status);
        Assert.Null(result);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.GposTag, diagnostic.Location.TableTag);
        Assert.Equal(1UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);
    }

    [Fact]
    public void LigatureDropsOnlyRulesWithMissingComponents()
    {
        var rules = new LigatureTable[]
        {
            new(10, [9], default, 0),
            new(7, [6], default, 0)
        };
        Ok(LigatureSubstTableCompiler.TryCreate([4],
            [new LigatureSetTable(rules, default, 0)], NumGlyphs, out var ligature, out _));
        Ok(LookupTableCompiler.TryCreateLigatureSubst(NumGlyphs, [ligature!], 0,
            null, null, out var lookup, out _));

        var (result, table) = Run(LayoutLookupKind.Gsub, [lookup!], [4, 6]);

        Assert.Equal(new ushort[] { 0, 4, 6, 7 }, Inputs(result));
        var output = Assert.Single(table.Lookups.ToArray()).Ligatures![0];
        Assert.Equal(new ushort[] { 1 }, output.Coverage.Glyphs.ToArray());
        var rule = Assert.Single(output.GetSet(0).Ligatures.ToArray());
        Assert.Equal(3, rule.LigatureGlyph);
        Assert.Equal(new ushort[] { 2 }, rule.Components.ToArray());
    }

    [Fact]
    public void ContextualGlyphRulesDropMissingInputsWithoutShorteningTheRule()
    {
        Ok(ContextualSubstTableCompiler.TryCreateRule([9],
            [new(0, 1)], 2, out var missing, out _));
        Ok(ContextualSubstTableCompiler.TryCreateRule([6],
            [new(0, 1)], 2, out var live, out _));
        Ok(ContextualSubstTableCompiler.TryCreateRuleSet([missing!, live!], out var set, out _));
        Ok(ContextualSubstTableCompiler.TryCreate(Coverage(4), [set], NumGlyphs,
            out var contextual, out _));
        Ok(LookupTableCompiler.TryCreateContextualSubst(NumGlyphs, [contextual!], 0,
            null, null, out var lookup, out _));

        var (result, table) = Run(LayoutLookupKind.Gsub,
            [lookup!, SingleSubstitution(4, 7)], [4, 6]);

        Assert.Equal(new ushort[] { 0, 4, 6, 7 }, Inputs(result));
        var output = table.Lookups[0].Contextuals![0];
        Assert.Equal(new ushort[] { 1 }, output.Coverage.Glyphs.ToArray());
        var rule = Assert.Single(Assert.Single(output.RuleSets!)!.Rules.ToArray());
        Assert.Equal(new ushort[] { 2 }, rule.InputSequence.ToArray());
        Assert.Equal(new SequenceLookupRecord(0, 1), Assert.Single(rule.LookupRecords.ToArray()));
    }

    [Fact]
    public void ContextualCoverageRulesWithAnEmptyRequiredPositionArePruned()
    {
        Ok(ContextualSubstTableCompiler.TryCreateFormat3([Coverage(4), Coverage(9)],
            [new SequenceLookupRecord(0, 1)], NumGlyphs, out var contextual, out _));
        Ok(LookupTableCompiler.TryCreateContextualSubst(NumGlyphs, [contextual!], 0,
            null, null, out var lookup, out _));

        var (result, table) = Run(LayoutLookupKind.Gsub,
            [lookup!, SingleSubstitution(4, 7)], [4]);

        Assert.Equal(new ushort[] { 0, 4, 7 }, Inputs(result));
        var output = Assert.Single(table.Lookups.ToArray()).SingleSubstitutions![0];
        Assert.Equal(new ushort[] { 1 }, output.Coverage.Glyphs.ToArray());
        Assert.Equal(new ushort[] { 2 }, output.Substitutes.ToArray());
    }

    [Fact]
    public void MarkPositionFiltersBothAnchorSidesByCoverageOrdinal()
    {
        Ok(MarkPosTableCompiler.TryCreate([2, 5],
            [new(0, Anchor(20)), new(0, Anchor(50))], [4, 8],
            [Anchor(40), Anchor(80)], 1, NumGlyphs, out var mark, out _));
        Ok(LookupTableCompiler.TryCreateMarkBase(NumGlyphs, [mark!], 0,
            null, null, out var lookup, out _));

        var (result, table) = Run(LayoutLookupKind.Gpos, [lookup!], [5, 8]);

        Assert.Equal(new ushort[] { 0, 5, 8 }, Inputs(result));
        var output = Assert.Single(table.Lookups.ToArray()).MarkBases![0];
        Assert.Equal(new ushort[] { 1 }, output.MarkCoverage.Glyphs.ToArray());
        Assert.Equal(new ushort[] { 2 }, output.BaseCoverage.Glyphs.ToArray());
        Assert.Equal(50, Assert.Single(output.Marks.ToArray()).Anchor.XCoordinate);
        Assert.Equal(80, Assert.Single(output.BaseRow(0).ToArray())!.XCoordinate);
    }

    [Fact]
    public void MarkLigatureWithNoRetainedTargetsIsPruned()
    {
        var attachment = new LigatureAttachTable(1, [[Anchor(80)]], default, 0);
        Ok(MarkPosTableCompiler.TryCreateLig([5], [new(0, Anchor(50))],
            [8], [attachment], NumGlyphs, out var mark, out _));
        Ok(LookupTableCompiler.TryCreateMarkLig(NumGlyphs, [mark!], 0,
            null, null, out var lookup, out _));

        var (result, table) = Run(LayoutLookupKind.Gpos, [lookup!], [5]);

        Assert.Equal(new ushort[] { 0, 5 }, Inputs(result));
        Assert.Empty(table.Lookups.ToArray());
    }

    [Fact]
    public void GdefFiltersAttachmentPayloadsAndKeepsMarkSetIndices()
    {
        var coverage = Coverage(2, 5);
        var attachments = new AttachListTable(coverage, [[2], [5, 6]], NumGlyphs,
            default, 0, []);
        var carets = new LigCaretListTable(coverage,
            [new([CaretValueEntry.Create(20)], default, 0),
                new([CaretValueEntry.Create(50)], default, 0)], NumGlyphs, default, 0, []);
        Ok(MarkGlyphSetsTableCompiler.TryCreate([Coverage(2), Coverage(5)], NumGlyphs,
            out var markSets, out _));
        Ok(GdefTableCompiler.TryCreate(0x00010002, NumGlyphs, null, null,
            markSets, attachments, carets, null, out var gdef, out _));
        Ok(GdefTableCompiler.TryCompile(gdef!, out var bytes, out _));

        var result = Prepare(SubsetFontFixture.GdefTag, bytes!, [5]);
        Assert.Equal(new ushort[] { 0, 5 }, Inputs(result));
        var outputBytes = OutputTable(result, SubsetFontFixture.GdefTag);
        Ok(GdefView.TryOpen(outputBytes, out var view, out _));
        Ok(view.TryReadTable(2, 0, out var output, out _));

        Assert.Equal(new ushort[] { 1 }, output!.AttachList!.Coverage.Glyphs.ToArray());
        Assert.Equal(new ushort[] { 5, 6 }, output.AttachList.Points(0).ToArray());
        Assert.Equal((short?)50, Assert.Single(output.LigCaretList!.LigGlyphs)[0].Coordinate);
        Assert.Equal(2, output.MarkGlyphSets!.Sets.Count);
        Assert.Empty(output.MarkGlyphSets.Sets[0].Glyphs.ToArray());
        Assert.Equal(new ushort[] { 1 }, output.MarkGlyphSets.Sets[1].Glyphs.ToArray());
    }

    private static (SubsetResult Result, LayoutListsTable Table) Run(LayoutLookupKind kind,
        LookupTable[] lookups, ushort[] roots)
    {
        uint tag = kind == LayoutLookupKind.Gsub
            ? SubsetFontFixture.GsubTag : SubsetFontFixture.GposTag;
        Ok(LayoutListsTableCompiler.TryCreate(LayoutListsLayout.Version10, 0, [], [],
            lookups, NumGlyphs, kind, out var model, out _));
        byte[]? bytes;
        ReadStatus status = kind == LayoutLookupKind.Gsub
            ? GsubTableCompiler.TryCompile(model!, null, out bytes, out _)
            : GposTableCompiler.TryCompile(model!, null, out bytes, out _);
        Ok(status);
        var result = Prepare(tag, bytes!, roots);
        byte[] output = OutputTable(result, tag);
        Ok(LayoutListsView.TryOpen(output, out var view, out _));
        Ok(view.TryReadTable(result.Map!.OutputGlyphCount, kind, null, out var table, out _));
        return (result, table!);
    }

    private static SubsetResult Prepare(uint tag, byte[] layout, ushort[] roots)
    {
        var status = SubsetOperation.TryPrepare(Request(tag, layout, roots),
            out var result, out _, out var diagnostic);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(status);
        return result!;
    }

    private static SubsetRequest Request(uint tag, byte[] layout, ushort[] roots,
        SubsetMode mode = SubsetMode.CompactRemap, SubsetOptions? options = null)
    {
        var glyphs = new List<byte>();
        var loca = new List<uint> { 0 };
        for (int i = 0; i < NumGlyphs; i++)
        {
            glyphs.AddRange(GlyfImages.NoContourGlyphRecord());
            loca.Add((uint)glyphs.Count);
        }
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
            (tag, layout),
            (SubsetFontFixture.GlyfTag, [.. glyphs]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([.. loca])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 4))));
        return new SubsetRequest
        {
            Face = face,
            Root = new() { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = roots },
            Mode = mode,
            Options = options ?? new() { PreserveLayout = true }
        };
    }

    private static byte[] OutputTable(SubsetResult result, uint tag)
    {
        var bytes = new byte[result.Font.OutputLength];
        Ok(result.Font.TryCopyTo(bytes, out int written, out _));
        Assert.Equal(bytes.Length, written);
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            var record = bytes.AsSpan(12 + 16 * i, 16);
            if (BinaryPrimitives.ReadUInt32BigEndian(record) != tag) continue;
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]));
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]));
            return bytes.AsSpan(offset, length).ToArray();
        }
        throw new Xunit.Sdk.XunitException($"Missing output table {tag}");
    }

    private static LookupTable PairClassLookup(ClassDefRange[] firstClasses,
        ClassDefRange[] secondClasses)
    {
        Ok(PairPosTableCompiler.TryCreate([4], firstClasses, secondClasses,
            1, 1, [Advance(47)], [default], NumGlyphs, 4, 0, out var pair,
            out _, classDefFormat: 2));
        Ok(LookupTableCompiler.TryCreatePairPos(NumGlyphs, [pair!], 0, null, null,
            out var lookup, out _));
        return lookup!;
    }

    private static LookupTable SingleSubstitution(ushort input, ushort output)
    {
        Ok(SingleSubstTableCompiler.TryCreate([input], [output], NumGlyphs, 2,
            out var single, out _));
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [single!], 0,
            null, null, out var lookup, out _));
        return lookup!;
    }

    private static CoverageTable Coverage(params ushort[] glyphs)
    {
        Ok(CoverageTableCompiler.TryCreate(glyphs, NumGlyphs, 1, out var coverage, out _));
        return coverage!;
    }

    private static AnchorTable Anchor(short x) => new(1, x, 0, 0, null, null, default, 6);
    private static StaticValueRecord Advance(short value) => new(0, 0, value, 0);
    private static ushort[] Inputs(SubsetResult result) => result.Map!.Entries
        .Select(entry => entry.InputGid).ToArray();
    private static void Ok(ReadStatus status) => Assert.Equal(ReadStatus.Done, status);
}
