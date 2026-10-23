using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetLayoutPruningTests
{
    private const int GlyphCount = 8;
    private const uint GsubTag = 0x47535542, GposTag = 0x47504F53, JstfTag = 0x4A535446;
    private const uint Dflt = 0x44464C54, Eng = 0x454E4720, Kern = 0x6B65726E;

    [Theory]
    [InlineData(false, false, false, 1)]
    [InlineData(false, false, false, 2)]
    [InlineData(false, false, false, 3)]
    [InlineData(false, false, true, 1)]
    [InlineData(false, false, true, 2)]
    [InlineData(false, false, true, 3)]
    [InlineData(false, true, false, 1)]
    [InlineData(false, true, false, 2)]
    [InlineData(false, true, false, 3)]
    [InlineData(false, true, true, 1)]
    [InlineData(false, true, true, 2)]
    [InlineData(false, true, true, 3)]
    [InlineData(true, false, false, 1)]
    [InlineData(true, false, false, 2)]
    [InlineData(true, false, false, 3)]
    [InlineData(true, false, true, 1)]
    [InlineData(true, false, true, 2)]
    [InlineData(true, false, true, 3)]
    [InlineData(true, true, false, 1)]
    [InlineData(true, true, false, 2)]
    [InlineData(true, true, false, 3)]
    [InlineData(true, true, true, 1)]
    [InlineData(true, true, true, 2)]
    [InlineData(true, true, true, 3)]
    public void CompactPruningRemapsNestedActionsAndKeepsMatchingBarriers(
        bool positioning, bool chaining, bool extension, int format)
    {
        var kind = positioning ? LayoutLookupKind.Gpos : LayoutLookupKind.Gsub;
        var at = DataLocation.Anonymous(Tag(kind));
        var lookups = new[]
        {
            Single(kind, 7, 7, at), Single(kind, 1, 1, at),
            ContextLookup(kind, chaining, extension, (ushort)format, at),
            Single(kind, 2, 3, at), Single(kind, 3, 2, at), SpecialBarrier(kind, at)
        };
        var result = Run(Face((Tag(kind), Compile(Root(kind, lookups, at)))), out var report);
        var output = ReadLayout(result, kind);

        Assert.Equal(5, output.Lookups.Length);
        Assert.Equal(new ushort[] { 0, 1, 2, 3, 4 }, output.Features[0].LookupIndices.ToArray());
        var context = output.Lookups[1];
        Assert.Equal(3, context.SubtableCount);
        Assert.Equal((ushort)(extension ? positioning ? 9 : 7 : chaining ? positioning ? 8 : 6 : positioning ? 7 : 5),
            context.LookupType);
        for (int i = 0; i < 3; i++)
        {
            var (coverage, records, trailing) = ContextShape(context, i, chaining, extension);
            Assert.Equal(new ushort[] { i == 1 ? (ushort)2 : (ushort)1 }, coverage);
            Assert.Equal(new ushort[] { format == 2 ? (ushort)2 : (ushort)3 }, trailing);
            Assert.Equal(i switch
            {
                0 => [],
                1 => new SequenceLookupRecord[] { new(1, 2) },
                _ => new SequenceLookupRecord[] { new(1, 3) }
            }, records);
        }
        if (positioning)
        {
            Assert.Equal(default, output.Lookups[0].SinglePositions![0].Values[0]);
            var pair = Assert.Single(output.Lookups[4].PairPositions!);
            Assert.Equal((ushort)2, pair.Format);
            Assert.Equal(1, pair.Class2Count);
            Assert.Equal((ushort)0, pair.ClassDef2!.ClassOf(2));
            Ok(pair.TryGetMatrixValue(0, 0, out var first, out var second, out _));
            Assert.Equal(default, first);
            Assert.Equal(default, second);
        }
        else
        {
            Assert.Equal(new ushort[] { 1 }, output.Lookups[0].SingleSubstitutions![0].Substitutes.ToArray());
            var deletion = Assert.Single(output.Lookups[4].Multiples!);
            Assert.Equal(new ushort[] { 1 }, deletion.Coverage.Glyphs.ToArray());
            Assert.Empty(deletion.GetSequence(0).ToArray());
        }
        var census = Assert.Single(report.LayoutRewriteCensus, row => row.Tag == Tag(kind));
        Assert.Equal(9, census.InputSubtableCount);
        Assert.Equal(7, census.OutputSubtableCount);
        Assert.False(census.CountsInvarianceHeld);
        Assert.Equal(Table(result, Tag(kind)).Length, census.OutputBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactPruningPreservesFeaturesLanguageShellsAndVariationBarriers(bool positioning)
    {
        var kind = positioning ? LayoutLookupKind.Gpos : LayoutLookupKind.Gsub;
        var at = DataLocation.Anonymous(Tag(kind));
        LayoutFeature[] features =
        [
            new(Kern, 0, [0, 1], at, 0), new(0x70726566, 0, [], at, 0),
            new(0x73697A65, 0, [], at, 0),
            new(0x73733031, 4, [0], at, 0, paramsPayload: [0, 0, 1, 44]),
            new(0x7A7A7A7A, 0, [], at, 0)
        ];
        LayoutScript[] scripts =
        [
            new(Dflt, new(1, [0, 1, 2, 3, 4], at, 0),
                [new(Eng, new(ushort.MaxValue, [], at, 0))], at, 0),
            new(0x6C61746E, null, [], at, 0)
        ];
        Ok(FeatureVariationsTableCompiler.TryCreateCondition(0, -100, 100, 1,
            out var axisCondition, out _, location: at));
        Ok(FeatureVariationsTableCompiler.TryCreateConditionSet([axisCondition!],
            out var condition, out _, location: at));
        var variations = Variations(at,
        [
            new(condition, Substitution(at, 5, [(0, Alternate(at, [0]))])),
            new(condition, Substitution(at, 5, [])),
            new(condition, Substitution(at, 5, [(4, Alternate(at, [1]))])),
            new(null, null)
        ]);
        var root = new LayoutListsTable(0x00010001, 1, scripts, features,
            [Single(kind, 7, 7, at), Single(kind, 1, 2, at)], GlyphCount, kind, at, variations);
        var result = Run(Face((Tag(kind), Compile(root)),
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar())), out _);
        var output = ReadLayout(result, kind);

        Assert.Single(output.Lookups.ToArray());
        Assert.Equal(features.Select(feature => feature.Tag), output.Features.ToArray().Select(feature => feature.Tag));
        Assert.Equal(new ushort[] { 0 }, output.Features[0].LookupIndices.ToArray());
        foreach (int index in new[] { 1, 2, 3, 4 }) Assert.Empty(output.Features[index].LookupIndices.ToArray());
        Assert.Equal(new byte[] { 0, 0, 1, 44 }, output.Features[3].ParamsPayload);
        Assert.Equal(2, output.Scripts.Length);
        Assert.Equal((ushort)1, output.Scripts[0].DefaultLanguage!.RequiredFeatureIndex);
        Assert.Equal(new ushort[] { 0, 1, 2, 3, 4 }, output.Scripts[0].DefaultLanguage!.FeatureIndices.ToArray());
        var language = Assert.Single(output.Scripts[0].Languages.ToArray());
        Assert.Equal(Eng, language.Tag);
        Assert.Equal(ushort.MaxValue, language.Language.RequiredFeatureIndex);
        Assert.Empty(language.Language.FeatureIndices.ToArray());
        Assert.Equal((uint)0x6C61746E, output.Scripts[1].Tag);
        Assert.Null(output.Scripts[1].DefaultLanguage);
        Assert.Empty(output.Scripts[1].Languages.ToArray());

        var records = output.FeatureVariations!.Records;
        Assert.Equal(4, records.Count);
        for (int i = 0; i < 3; i++)
        {
            var actual = Assert.Single(records[i].ConditionSet!.Conditions);
            Assert.Equal((ushort)0, actual.AxisIndex);
            Assert.Equal((short)-100, actual.MinValue);
            Assert.Equal((short)100, actual.MaxValue);
        }
        var disabled = Assert.Single(records[0].Substitution!.Records);
        Assert.Equal((ushort)0, disabled.FeatureIndex);
        Assert.Empty(disabled.Alternate.LookupIndices.ToArray());
        Assert.Empty(records[1].Substitution!.Records);
        var alternate = Assert.Single(records[2].Substitution!.Records);
        Assert.Equal((ushort)4, alternate.FeatureIndex);
        Assert.Equal(new ushort[] { 0 }, alternate.Alternate.LookupIndices.ToArray());
        Assert.Null(records[3].ConditionSet);
        Assert.Null(records[3].Substitution);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedVariationsKeepFirstMatchingRecordAfterItsFeatureIsCut(bool compact)
    {
        var at = DataLocation.Anonymous(GsubTag);
        LayoutFeature[] features = [new(0x61616161, 0, [0], at, 0), new(0x62626262, 0, [1], at, 0)];
        var variations = Variations(at,
        [
            new(null, Substitution(at, 2, [(0, Alternate(at, [0]))])),
            new(null, Substitution(at, 2, [(1, Alternate(at, [2]))]))
        ]);
        var source = new LayoutListsTable(0x00010001, 1,
            [new(Dflt, new(ushort.MaxValue, [0, 1], at, 0), [], at, 0)], features,
            [Single(LayoutLookupKind.Gsub, 1, 1, at), Single(LayoutLookupKind.Gsub, 1, 2, at),
                Single(LayoutLookupKind.Gsub, 1, 3, at)], GlyphCount, LayoutLookupKind.Gsub, at, variations);
        var result = Run(Face((GsubTag, Compile(source)),
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar())), out _,
            compact ? SubsetMode.CompactRemap : SubsetMode.RetainGids,
            new() { FeatureTags = [0x62626262] });
        var output = ReadLayout(result, LayoutLookupKind.Gsub, compact ? 4 : GlyphCount);

        Assert.Equal((uint)0x62626262, Assert.Single(output.Features.ToArray()).Tag);
        Assert.Equal(2, output.Lookups.Length);
        Assert.Equal(new ushort[] { 0 }, output.Features[0].LookupIndices.ToArray());
        Assert.Equal(2, output.FeatureVariations!.RecordCount);
        Assert.Null(output.FeatureVariations.Records[0].ConditionSet);
        Assert.Empty(output.FeatureVariations.Records[0].Substitution!.Records);
        var second = Assert.Single(output.FeatureVariations.Records[1].Substitution!.Records);
        Assert.Equal((ushort)0, second.FeatureIndex);
        Assert.Equal(new ushort[] { 1 }, second.Alternate.LookupIndices.ToArray());
    }

    [Fact]
    public void CompactPruningMapsAllJstfDomainsWithoutMappingInlineLookupOrdinals()
    {
        var subAt = DataLocation.Anonymous(GsubTag);
        var posAt = DataLocation.Anonymous(GposTag);
        var at = DataLocation.Anonymous(JstfTag);
        var gsub = Root(LayoutLookupKind.Gsub,
            [Single(LayoutLookupKind.Gsub, 7, 7, subAt), Single(LayoutLookupKind.Gsub, 1, 1, subAt),
                Single(LayoutLookupKind.Gsub, 2, 3, subAt)], subAt);
        var gpos = Root(LayoutLookupKind.Gpos,
            [Single(LayoutLookupKind.Gpos, 1, 1, posAt), Single(LayoutLookupKind.Gpos, 7, 7, posAt),
                Single(LayoutLookupKind.Gpos, 2, 3, posAt)], posAt);
        JstfModListTable Sub(params ushort[] indices) => new(JstfLookupDomain.Gsub, indices, at, 0);
        JstfModListTable Pos(params ushort[] indices) => new(JstfLookupDomain.Gpos, indices, at, 0);
        var priority = new JstfPriorityTable(Sub(0, 2), Sub(1), Pos(0, 1), Pos(2),
            new([EmptyPosition(at), Single(LayoutLookupKind.Gpos, 1, 1, at)], at, 0),
            Sub(0, 1), Sub(2), Pos(1, 2), Pos(0),
            new([EmptyPosition(at)], at, 0), at, 0);
        var lang = new JstfLangSysTable([priority], at, 0);
        var source = new JstfTable(0x00010000, GlyphCount, 3, 3, null,
            [new(Dflt, new(null, lang, [new(Eng, lang)], at, 0))], at, 0);
        Ok(JstfTableCompiler.TryCompile(source, out var bytes, out _));
        var result = Run(Face((GsubTag, Compile(gsub)), (GposTag, Compile(gpos)), (JstfTag, bytes!)), out _);
        Assert.Equal(2, ReadLayout(result, LayoutLookupKind.Gsub).Lookups.Length);
        Assert.Equal(2, ReadLayout(result, LayoutLookupKind.Gpos).Lookups.Length);
        var output = ReadJstf(result, 2, 2);
        var script = Assert.Single(output.Scripts).Script;
        foreach (var actual in new[] { script.DefaultLangSys!.Priorities[0], Assert.Single(script.LangSys).LangSys.Priorities[0] })
        {
            Assert.Equal(new ushort[] { 1 }, actual.GsubShrinkageEnable!.LookupIndices);
            Assert.Equal(new ushort[] { 0 }, actual.GsubShrinkageDisable!.LookupIndices);
            Assert.Equal(new ushort[] { 0 }, actual.GposShrinkageEnable!.LookupIndices);
            Assert.Equal(new ushort[] { 1 }, actual.GposShrinkageDisable!.LookupIndices);
            Assert.Equal(new ushort[] { 0 }, actual.GsubExtensionEnable!.LookupIndices);
            Assert.Equal(new ushort[] { 1 }, actual.GsubExtensionDisable!.LookupIndices);
            Assert.Equal(new ushort[] { 1 }, actual.GposExtensionEnable!.LookupIndices);
            Assert.Equal(new ushort[] { 0 }, actual.GposExtensionDisable!.LookupIndices);
            var inline = Assert.Single(actual.ShrinkageJstfMax!.Lookups);
            Assert.Equal(new ushort[] { 1 }, inline.SinglePositions![0].Coverage.Glyphs.ToArray());
            Assert.Equal(default, inline.SinglePositions[0].Values[0]);
            Assert.Empty(actual.ExtensionJstfMax!.Lookups);
        }
    }

    [Fact]
    public void CompactPruningProcessesJstfWithoutOtherLayoutRoots()
    {
        var at = DataLocation.Anonymous(JstfTag);
        var priority = new JstfPriorityTable(null, null, null, null,
            new([EmptyPosition(at)], at, 0),
            null, null, null, null, null, at, 0);
        var source = new JstfTable(0x00010000, GlyphCount, 0, 0, null,
            [new(Dflt, new(null, new([priority], at, 0), [], at, 0))], at, 0);
        Ok(JstfTableCompiler.TryCompile(source, out var bytes, out _));
        var result = Run(Face((JstfTag, bytes!)), out _);
        var output = ReadJstf(result, 0, 0);
        Assert.Empty(Assert.Single(output.Scripts).Script.DefaultLangSys!.Priorities[0].ShrinkageJstfMax!.Lookups);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompactPruningRemovesLeavesWithUnmatchableRequiredDomains(bool positioning, bool extension)
    {
        var kind = positioning ? LayoutLookupKind.Gpos : LayoutLookupKind.Gsub;
        var at = DataLocation.Anonymous(Tag(kind));
        var live = Coverage(at, 1);
        var dead = Coverage(at, 7);
        var context = new ContextualSubstTable(3, live, null, [], [live, dead], [], GlyphCount, at, 0);
        var chain = new ChainingSubstTable(3, null, null, null, null, [],
            [dead], [live], [live], [], GlyphCount, at, 0);
        var anchor = new AnchorTable(1, 100, 200, 0, null, null, at, 0);
        Ok(ContextualSubstTableCompiler.TryCreateRuleSet([], out var emptySet, out _, location: at));
        LookupTable[] lookups = positioning
            ?
            [
                Single(kind, 7, 7, at),
                new(kind, GlyphCount, 2, 0, null, [], null, at,
                    pairPositions: [new(live, 1, 4, 0, pairSets: [new([], at, 0)], location: at)]),
                new(kind, GlyphCount, 3, 0, null, [], null, at,
                    cursiveAttachments: [new(dead, [new(anchor, anchor)], GlyphCount, at, 0)]),
                new(kind, GlyphCount, 4, 0, null, [], null, at,
                    markBases: [new(live, [new(0, anchor)], dead, 1, [[anchor]], GlyphCount, at, 0)]),
                new(kind, GlyphCount, 5, 0, null, [], null, at,
                    markLigs: [new(dead, [new(0, anchor)], live, [new(1, [[anchor]], at, 0)], GlyphCount, at, 0)]),
                new(kind, GlyphCount, 6, 0, null, [], null, at,
                    markMarks: [new(live, [new(0, anchor)], dead, 1, [[anchor]], GlyphCount, at, 0)]),
                new(kind, GlyphCount, 7, 0, null, [], null, at, contextuales: [context]),
                new(kind, GlyphCount, 8, 0, null, [], null, at, chainings: [chain])
            ]
            :
            [
                Single(kind, 7, 7, at),
                new(kind, GlyphCount, 2, 0, null, [], null, at, multiples: [new(dead, [[7]], at, 0)]),
                new(kind, GlyphCount, 3, 0, null, [], null, at, alternates: [new(dead, [[7]], at, 0)]),
                new(kind, GlyphCount, 4, 0, null, [], null, at, ligatures: [new(live, [new([], at, 0)], at, 0)]),
                new(kind, GlyphCount, 5, 0, null, [], null, at, contextuales: [context,
                    new(1, live, null, [emptySet], null, null, GlyphCount, at, 0)]),
                new(kind, GlyphCount, 6, 0, null, [], null, at, chainings: [chain]),
                new(kind, GlyphCount, 8, 0, null, [], null, at,
                    reverseChains: [new(live, [], [dead], [2], GlyphCount, at, 0)])
            ];
        if (extension) lookups = lookups.Select(Wrap).ToArray();
        var result = Run(Face((Tag(kind), Compile(Root(kind, lookups, at)))), out var report);
        var output = ReadLayout(result, kind);
        Assert.Empty(output.Lookups.ToArray());
        Assert.Equal(Kern, Assert.Single(output.Features.ToArray()).Tag);
        Assert.Empty(output.Features[0].LookupIndices.ToArray());
        Assert.Equal(new ushort[] { 0 }, Assert.Single(output.Scripts.ToArray()).DefaultLanguage!.FeatureIndices.ToArray());
        var census = Assert.Single(report.LayoutRewriteCensus, row => row.Tag == Tag(kind));
        Assert.Equal(8, census.InputSubtableCount);
        Assert.Equal(0, census.OutputSubtableCount);
        Assert.False(census.CountsInvarianceHeld);
    }

    private static LookupTable Wrap(LookupTable lookup)
    {
        var at = lookup.Location;
        if (lookup.Kind == LayoutLookupKind.Gsub)
            return new(lookup.Kind, GlyphCount, 7, 0, null, [], null, at,
                extensions: Enumerable.Range(0, lookup.SubtableCount).Select(i => new ExtensionSubstTable(
                    lookup.LookupType, lookup.SingleSubstitutions?[i], lookup.Multiples?[i],
                    lookup.Alternates?[i], lookup.Ligatures?[i], at, 0, lookup.Contextuals?[i],
                    lookup.Chainings?[i], lookup.ReverseChains?[i])).ToArray());
        return new(lookup.Kind, GlyphCount, 9, 0, null, [], null, at,
            extensionPositions: Enumerable.Range(0, lookup.SubtableCount).Select(i => new ExtensionPosTable(
                lookup.LookupType, lookup.SinglePositions?[i], lookup.PairPositions?[i],
                lookup.CursiveAttachments?[i], at, 0, lookup.MarkBases?[i], lookup.MarkLigs?[i],
                lookup.MarkMarks?[i], lookup.Contextuals?[i], lookup.Chainings?[i])).ToArray());
    }

    private static LookupTable ContextLookup(LayoutLookupKind kind, bool chaining,
        bool extension, ushort format, DataLocation at)
    {
        SequenceLookupRecord[][] actions = [[new(0, 0)], [new(0, 0), new(1, 3)], [new(1, 4)], [new(0, 0)]];
        ushort[] first = [1, 2, 1, 7];
        var contexts = new ContextualSubstTable[4];
        var chains = new ChainingSubstTable[4];
        for (int i = 0; i < 4; i++)
        {
            var coverage = Coverage(at, first[i]);
            ClassDefTable? classes = null;
            if (format == 2)
            {
                ClassDefRange[] ranges = [new(first[i], first[i], 1), new(3, 3, 2)];
                Array.Sort(ranges, static (left, right) => left.StartGlyph.CompareTo(right.StartGlyph));
                Ok(ClassDefTableCompiler.TryCreate(ranges, GlyphCount, 2, out classes, out _, location: at));
            }
            ushort[] tail = [format == 2 ? (ushort)2 : (ushort)3];
            ContextualSubstTable? context;
            ChainingSubstTable? chain;
            if (format == 3)
            {
                Ok(ContextualSubstTableCompiler.TryCreateFormat3([coverage, Coverage(at, 3)],
                    actions[i], GlyphCount, out context, out _, location: at));
                Ok(ChainingSubstTableCompiler.TryCreateFormat3([], [coverage, Coverage(at, 3)], [],
                    actions[i], GlyphCount, out chain, out _, location: at));
            }
            else
            {
                Ok(ContextualSubstTableCompiler.TryCreateRule(tail, actions[i], 6, out var rule, out _, location: at));
                Ok(ContextualSubstTableCompiler.TryCreateRuleSet([rule!], out var set, out _, location: at));
                Ok(ChainingSubstTableCompiler.TryCreateRule([], tail, [], actions[i], 6, out var chainRule, out _, location: at));
                Ok(ChainingSubstTableCompiler.TryCreateRuleSet([chainRule!], out var chainSet, out _, location: at));
                if (format == 1)
                {
                    Ok(ContextualSubstTableCompiler.TryCreate(coverage, [set], GlyphCount, out context, out _, location: at));
                    Ok(ChainingSubstTableCompiler.TryCreate(coverage, [chainSet], GlyphCount, out chain, out _, location: at));
                }
                else
                {
                    Ok(ContextualSubstTableCompiler.TryCreate(coverage, classes!, [null, set, null],
                        GlyphCount, out context, out _, location: at));
                    Ok(ChainingSubstTableCompiler.TryCreate(coverage, classes!, classes!, classes!,
                        [null, chainSet, null], GlyphCount, out chain, out _, location: at));
                }
            }
            contexts[i] = context!;
            chains[i] = chain!;
        }
        ushort direct = (ushort)(chaining ? kind == LayoutLookupKind.Gpos ? 8 : 6 : kind == LayoutLookupKind.Gpos ? 7 : 5);
        if (!extension) return new(kind, GlyphCount, direct, 0, null, [], null, at,
            contextuales: chaining ? null : contexts, chainings: chaining ? chains : null);
        if (kind == LayoutLookupKind.Gsub)
            return new(kind, GlyphCount, 7, 0, null, [], null, at,
                extensions: Enumerable.Range(0, 4).Select(i => new ExtensionSubstTable(direct,
                    null, null, null, null, at, 0, chaining ? null : contexts[i], chaining ? chains[i] : null)).ToArray());
        return new(kind, GlyphCount, 9, 0, null, [], null, at,
            extensionPositions: Enumerable.Range(0, 4).Select(i => new ExtensionPosTable(direct,
                null, null, null, at, 0, contextual: chaining ? null : contexts[i], chaining: chaining ? chains[i] : null)).ToArray());
    }

    private static (ushort[] Coverage, SequenceLookupRecord[] Records, ushort[] Trailing)
        ContextShape(LookupTable lookup, int index, bool chaining, bool extension)
    {
        if (chaining)
        {
            var table = extension ? lookup.Kind == LayoutLookupKind.Gsub
                ? lookup.Extensions![index].Chaining! : lookup.ExtensionPositions![index].Chaining!
                : lookup.Chainings![index];
            if (table.Format == 3) return (table.InputCoverages![0].Glyphs.ToArray(),
                table.LookupRecords!.ToArray(), table.InputCoverages[1].Glyphs.ToArray());
            var rule = Assert.Single(Assert.Single(table.RuleSets!, set => set is not null)!.Rules.ToArray());
            return (table.Coverage!.Glyphs.ToArray(), rule.LookupRecords.ToArray(), rule.InputSequence.ToArray());
        }
        var context = extension ? lookup.Kind == LayoutLookupKind.Gsub
            ? lookup.Extensions![index].Contextual! : lookup.ExtensionPositions![index].Contextual!
            : lookup.Contextuals![index];
        if (context.Format == 3) return (context.Coverages![0].Glyphs.ToArray(),
            context.LookupRecords!.ToArray(), context.Coverages[1].Glyphs.ToArray());
        var contextRule = Assert.Single(Assert.Single(context.RuleSets!, set => set is not null)!.Rules.ToArray());
        return (context.Coverage.Glyphs.ToArray(), contextRule.LookupRecords.ToArray(), contextRule.InputSequence.ToArray());
    }

    private static LayoutFeature Alternate(DataLocation at, ushort[] lookups)
    {
        Ok(FeatureVariationsTableCompiler.TryCreateAlternateFeature(0, lookups, 3,
            out var feature, out _, location: at));
        return feature!;
    }

    private static FeatureTableSubstTable Substitution(DataLocation at, int featureCount,
        (ushort FeatureIndex, LayoutFeature Alternate)[] entries)
    {
        Ok(FeatureVariationsTableCompiler.TryCreateFeatureTableSubst(entries, featureCount,
            out var table, out _, location: at));
        return table!;
    }

    private static FeatureVariationsTable Variations(DataLocation at, FeatureVariationRecord[] records)
    {
        Ok(FeatureVariationsTableCompiler.TryCreate(records, out var table, out _, location: at));
        return table!;
    }

    private static LookupTable EmptyPosition(DataLocation at) =>
        new(LayoutLookupKind.Gpos, GlyphCount, 1, 0, null, [], null, at,
            singlePositions: [new(Coverage(at), [], 2, 4, at, 0)]);

    private static LookupTable Single(LayoutLookupKind kind, ushort glyph, ushort replacement, DataLocation at)
    {
        var coverage = Coverage(at, glyph);
        return kind == LayoutLookupKind.Gsub
            ? new(kind, GlyphCount, 1, 0, null, [], [new(coverage, [replacement], 2, at, 0)], at)
            : new(kind, GlyphCount, 1, 0, null, [], null, at,
                singlePositions: [new(coverage, [new(0, 0, (short)(replacement - glyph), 0)], 2, 4, at, 0)]);
    }

    private static LookupTable SpecialBarrier(LayoutLookupKind kind, DataLocation at)
    {
        if (kind == LayoutLookupKind.Gsub)
            return new(kind, GlyphCount, 2, 0, null, [], null, at,
                multiples: [new(Coverage(at, 1), [[]], at, 0)]);
        Ok(ClassDefTableCompiler.TryCreate([], GlyphCount, 2, out var classes, out _, location: at));
        return new(kind, GlyphCount, 2, 0, null, [], null, at,
            pairPositions: [new(Coverage(at, 1), 2, 4, 0, classDef1: classes,
                classDef2: classes, class1Count: 1, class2Count: 1,
                values1: [default], values2: [default], location: at)]);
    }

    private static LayoutListsTable Root(LayoutLookupKind kind, LookupTable[] lookups, DataLocation at) =>
        new(0x00010000, 0, [new(Dflt, new(ushort.MaxValue, [0], at, 0), [], at, 0)],
            [new(Kern, 0, Enumerable.Range(0, lookups.Length).Select(index => (ushort)index).ToArray(), at, 0)],
            lookups, GlyphCount, kind, at);

    private static byte[] Compile(LayoutListsTable table)
    {
        var status = table.Kind == LayoutLookupKind.Gsub
            ? GsubTableCompiler.TryCompile(table, null, out var bytes, out var diagnostic)
            : GposTableCompiler.TryCompile(table, null, out bytes, out diagnostic);
        Assert.True(status == ReadStatus.Done, $"Compile failed: {status}; {diagnostic}");
        return bytes!;
    }

    private static FontFaceRef Face(params (uint Tag, byte[] Bytes)[] extra)
    {
        var glyf = new List<byte>();
        var offsets = new List<uint> { 0 };
        for (int i = 0; i < GlyphCount; i++)
        {
            glyf.AddRange(GlyfImages.NoContourGlyphRecord());
            offsets.Add((uint)glyf.Count);
        }
        return SubsetFontFixture.Face(
        [
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(GlyphCount)),
            (SubsetFontFixture.GlyfTag, [.. glyf]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([.. offsets])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))), .. extra
        ]);
    }

    private static SubsetResult Run(FontFaceRef face, out SubsetReport report,
        SubsetMode mode = SubsetMode.CompactRemap, SubsetLayoutScope? scope = null)
    {
        var status = SubsetOperation.TryPrepare(new()
        {
            Face = face, Mode = mode,
            Root = new() { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = new ushort[] { 1, 2, 3 } },
            Options = new() { PreserveLayout = true, LayoutScope = scope }
        }, out var result, out var actualReport, out var diagnostic);
        Assert.True(status == ReadStatus.Done, $"Subset failed: {status}; {diagnostic}");
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        Assert.NotNull(actualReport);
        report = actualReport;
        if (mode == SubsetMode.CompactRemap)
            Assert.Equal(new ushort[] { 0, 1, 2, 3 }, result.Map!.Entries.Select(entry => entry.InputGid).ToArray());
        return result;
    }

    private static LayoutListsTable ReadLayout(SubsetResult result, LayoutLookupKind kind, int glyphCount = 4)
    {
        Ok(LayoutListsView.TryOpen(Table(result, Tag(kind)), out var view, out _, DataLocation.Anonymous(Tag(kind))));
        Ok(view.TryReadTable(glyphCount, kind, null, out var table, out _));
        return table!;
    }

    private static JstfTable ReadJstf(SubsetResult result, int gsubCount, int gposCount)
    {
        Ok(JstfView.TryOpen(Table(result, JstfTag), out var view, out _, DataLocation.Anonymous(JstfTag)));
        Ok(view.TryReadTable(4, gsubCount, gposCount, null, out var table, out _));
        return table!;
    }

    private static CoverageTable Coverage(DataLocation at, params ushort[] glyphs)
    {
        Ok(CoverageTableCompiler.TryCreate(glyphs, GlyphCount, 1, out var table, out _, location: at));
        return table!;
    }

    private static byte[] Table(SubsetResult result, uint tag)
    {
        var font = new byte[result.Font.OutputLength];
        Ok(result.Font.TryCopyTo(font, out int written, out _));
        Assert.Equal(font.Length, written);
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            var record = font.AsSpan(12 + 16 * i, 16);
            if (BinaryPrimitives.ReadUInt32BigEndian(record) != tag) continue;
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]));
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]));
            return font.AsSpan(offset, length).ToArray();
        }
        throw new Xunit.Sdk.XunitException($"Missing table {tag}");
    }

    private static uint Tag(LayoutLookupKind kind) => kind == LayoutLookupKind.Gsub ? GsubTag : GposTag;
    private static void Ok(ReadStatus status) => Assert.Equal(ReadStatus.Done, status);
}
