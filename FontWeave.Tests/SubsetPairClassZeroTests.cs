using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using FontWeave.Tables.Variation;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetPairClassZeroTests
{
    private const int GlyphCount = 10;
    private const uint GdefTag = 0x47444546, GposTag = 0x47504F53, JstfTag = 0x4A535446;
    private static readonly ushort[] Kept = [0, 1, 2, 4, 5, 6, 8];

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void CompactPairClassZeroFollowsFirstCoverageAndPreservesAllConsumers(bool extension, int zeroDomain)
    {
        ushort[] coverage = zeroDomain switch
        {
            0 => [1, 2, 7],
            1 or 2 => [1, 2, 6, 7],
            _ => Enumerable.Range(0, GlyphCount).Select(glyph => (ushort)glyph).ToArray()
        };
        var result = Run(Face(coverage, zeroDomain == 2, extension, true), Kept);
        Assert.Equal(Kept, result.Map!.Entries.Select(entry => entry.InputGid).ToArray());
        var (gdef, context) = ReadGdef(result, Kept.Length);
        var store = Assert.IsType<VariationStoreTable>(gdef.ItemVariationStore);
        ushort[] rows = zeroDomain switch { 0 => [2, 4], 1 or 2 => [0, 2, 4], _ => [0, 2, 3, 4] };
        ushort[] columns = [0, 2, 3, 4];
        var output = ReadGpos(result, Kept.Length, context);
        Assert.Equal(3, output.Lookups.Length);
        var pair = PairLeaf(output.Lookups[0], extension);
        AssertMatrix(pair, rows, columns, store);
        Assert.Equal(zeroDomain switch
        {
            0 => new ushort[] { 1, 2 },
            1 or 2 => [1, 2, 5],
            _ => [0, 1, 2, 3, 4, 5, 6]
        }, pair.Coverage.Glyphs.ToArray());
        Assert.Equal(zeroDomain switch
        {
            0 => new ushort[] { 0, 0, 1, 0, 0, 0, 0 },
            1 or 2 => [0, 1, 2, 0, 0, 0, 0],
            _ => [0, 1, 3, 0, 0, 0, 2]
        }, Enumerable.Range(0, Kept.Length).Select(pair.ClassDef1!.ClassOf).ToArray());
        Assert.Equal(new ushort[] { 0, 0, 0, 1, 3, 0, 2 }, Enumerable.Range(0, Kept.Length)
            .Select(pair.ClassDef2!.ClassOf).ToArray());

        var contextual = Assert.Single(output.Lookups[1].Contextuals!);
        Assert.Equal(new ushort[] { 1 }, contextual.Coverages![0].Glyphs.ToArray());
        Assert.Equal(new ushort[] { 3 }, contextual.Coverages[1].Glyphs.ToArray());
        Assert.Equal(new SequenceLookupRecord(0, 0), Assert.Single(contextual.LookupRecords!));
        var later = Assert.Single(output.Lookups[2].SinglePositions!);
        Assert.Equal(new ushort[] { 1 }, later.Coverage.Glyphs.ToArray());
        Assert.Equal((short)99, later.Values[0].XAdvance);
        Assert.Equal(new ushort[] { 0, 2 }, output.Features[0].LookupIndices.ToArray());
        var variation = Assert.Single(output.FeatureVariations!.Records);
        var alternate = Assert.Single(variation.Substitution!.Records);
        Assert.Equal((ushort)0, alternate.FeatureIndex);
        Assert.Equal(new ushort[] { 1, 2 }, alternate.Alternate.LookupIndices.ToArray());
        Assert.Equal((short)-16384, Assert.Single(variation.ConditionSet!.Conditions).MinValue);
        Assert.Equal((short)16384, variation.ConditionSet.Conditions[0].MaxValue);

        Ok(JstfView.TryOpen(Table(result, JstfTag), out var jstfView, out _, DataLocation.Anonymous(JstfTag)));
        Ok(jstfView.TryReadTable(Kept.Length, 0, 3, context, out var jstf, out _));
        var priority = Assert.Single(Assert.Single(jstf!.Scripts).Script.DefaultLangSys!.Priorities);
        Assert.Equal(new ushort[] { 0, 1, 2 }, priority.GposShrinkageEnable!.LookupIndices);
        var inline = Assert.Single(Assert.Single(priority.ShrinkageJstfMax!.Lookups).SinglePositions!);
        Assert.Equal(new ushort[] { 1 }, inline.Coverage.Glyphs.ToArray());
        var inlineValue = Assert.Single(inline.Values.ToArray());
        Assert.Equal((short)405, inlineValue.XPlacement);
        Assert.Equal((short)-405, inlineValue.YPlacement);
        Assert.Equal((short)810, inlineValue.XAdvance);
        Assert.Equal((short)-810, inlineValue.YAdvance);
        Assert.NotNull(inlineValue.XPlacementDevice);
        Assert.Equal((ushort)12, inlineValue.XPlacementDevice.StartSize);
        Assert.Equal((ushort)13, inlineValue.XPlacementDevice.EndSize);
        Assert.Equal((ushort)1, inlineValue.XPlacementDevice.DeltaFormat);
        Assert.Equal(new sbyte[] { 1, -1 }, inlineValue.XPlacementDevice.Deltas.ToArray());
        AssertVariation(store, inlineValue.XAdvanceDevice, 1405);
        Assert.Null(inlineValue.YPlacementDevice);
        Assert.Null(inlineValue.YAdvanceDevice);
        foreach (ushort firstGlyph in pair.Coverage.Glyphs)
            for (int secondGlyph = 0; secondGlyph < Kept.Length; secondGlyph++)
            {
                Ok(pair.TryGetMatrixValue(pair.ClassDef1!.ClassOf(firstGlyph), pair.ClassDef2!.ClassOf(secondGlyph),
                    out var globalFirst, out var globalSecond, out _));
                int oldRow = Kept[firstGlyph] switch { 1 => 2, 2 => 4, 8 => 3, _ => 0 };
                int oldColumn = Kept[secondGlyph] switch { 4 => 2, 5 => 4, 8 => 3, _ => 0 };
                short marker = Marker(oldRow, oldColumn);
                Assert.Equal(marker * 2, globalFirst.XAdvance);
                Assert.Equal(marker + 40, globalSecond.YAdvance);
            }
        Assert.Equal(rows.Length * columns.Length, store.Data[0]!.ItemCount);
    }

    [Theory]
    [InlineData(SubsetMode.CompactRemap)]
    [InlineData(SubsetMode.RetainGids)]
    public void FullGlyphDomainKeepsPairClassesAndOutsideCoverageDefinitions(SubsetMode mode)
    {
        var roots = Enumerable.Range(0, GlyphCount).Select(glyph => (ushort)glyph).ToArray();
        var result = Run(Face([1, 2], false, false, true), roots, mode);
        var (gdef, context) = ReadGdef(result, GlyphCount);
        var output = ReadGpos(result, GlyphCount, context);
        var pair = PairLeaf(output.Lookups[0], false);
        AssertMatrix(pair, [0, 1, 2, 3, 4], [0, 1, 2, 3, 4], gdef.ItemVariationStore!);
        Assert.Equal(new ushort[] { 1, 2 }, pair.Coverage.Glyphs.ToArray());
        Assert.Equal((ushort)2, pair.ClassDef1!.ClassOf(1));
        Assert.Equal((ushort)4, pair.ClassDef1.ClassOf(2));
        Assert.Equal((ushort)3, pair.ClassDef1.ClassOf(8));
        Assert.Equal((ushort)0, pair.ClassDef2!.ClassOf(0));
        Assert.Equal((ushort)3, pair.ClassDef2.ClassOf(8));
    }

    [Fact]
    public void EmptyFirstCoverageStaysLegalUntilThePairLookupIsPruned()
    {
        var result = Run(Face([7], false, false, false), Kept);
        Assert.Equal(Kept, result.Map!.Entries.Select(entry => entry.InputGid).ToArray());
        var (_, context) = ReadGdef(result, Kept.Length);
        var output = ReadGpos(result, Kept.Length, context);
        Assert.Empty(output.Lookups.ToArray());
        Assert.Empty(Assert.Single(output.Features.ToArray()).LookupIndices.ToArray());
        Assert.Equal((uint)0x6B65726E, output.Features[0].Tag);
        Assert.Equal(new ushort[] { 0 }, Assert.Single(output.Scripts.ToArray()).DefaultLanguage!.FeatureIndices.ToArray());
    }

    private static FontFaceRef Face(ushort[] coverage, bool explicitZero, bool extension, bool composition)
    {
        var gdAt = DataLocation.Anonymous(GdefTag);
        var gpAt = DataLocation.Anonymous(GposTag);
        var jsAt = DataLocation.Anonymous(JstfTag);
        var deltas = Enumerable.Range(0, 25).Select(cell => new[] { 1000 + Marker(cell / 5, cell % 5) }).ToArray();
        Ok(ItemVariationDataTableCompiler.TryCreate(1, [0], deltas, out var data, out _, location: gdAt));
        Ok(VariationStoreTableCompiler.TryCreate(1, [new[] { new VariationRegionAxis(0, 16384, 16384) }],
            [data], out var store, out _, location: gdAt));
        Ok(GdefTableCompiler.TryCreate(0x00010003, GlyphCount, null, null, null, null, null,
            store, out var gdef, out _, location: gdAt));
        Ok(GdefTableCompiler.TryCompile(gdef!, out var gdBytes, out _));
        var context = Context(gdef!);
        var pair = Pair(coverage, explicitZero, gpAt);
        var pairLookup = Lookup(pair, extension, gpAt);
        LookupTable[] lookups = [pairLookup];
        FeatureVariationsTable? variations = null;
        if (composition)
        {
            Ok(ContextualSubstTableCompiler.TryCreateFormat3([Coverage(gpAt, 1), Coverage(gpAt, 4)],
                [new(0, 0)], GlyphCount, out var contextual, out _, location: gpAt));
            lookups = [pairLookup,
                new(LayoutLookupKind.Gpos, GlyphCount, 7, 0, null, [], null, gpAt, contextuales: [contextual!]),
                new(LayoutLookupKind.Gpos, GlyphCount, 1, 0, null, [], null, gpAt,
                    singlePositions: [new(Coverage(gpAt, 1), [new(0, 0, 99)], 2, 4, gpAt, 0)])];
            Ok(FeatureVariationsTableCompiler.TryCreateAlternateFeature(0, [1, 2], 3,
                out var alternate, out _, location: gpAt));
            Ok(FeatureVariationsTableCompiler.TryCreateFeatureTableSubst([(0, alternate!)], 1,
                out var substitution, out _, location: gpAt));
            Ok(FeatureVariationsTableCompiler.TryCreateCondition(0, -16384, 16384, 1,
                out var condition, out _, location: gpAt));
            Ok(FeatureVariationsTableCompiler.TryCreateConditionSet([condition!],
                out var conditions, out _, location: gpAt));
            Ok(FeatureVariationsTableCompiler.TryCreate([new(conditions, substitution)],
                out variations, out _, location: gpAt));
        }
        var gpos = new LayoutListsTable(composition ? 0x00010001u : 0x00010000u,
            composition ? 1u : 0u, [new(0x44464C54, new(ushort.MaxValue, [0], gpAt, 0), [], gpAt, 0)],
            [new(0x6B65726E, 0, composition ? [0, 2] : [0], gpAt, 0)], lookups,
            GlyphCount, LayoutLookupKind.Gpos, gpAt, variations);
        Ok(GposTableCompiler.TryCompile(gpos, context, out var gpBytes, out _));
        var tables = new List<(uint, byte[])> { (GdefTag, gdBytes!), (GposTag, gpBytes!) };
        if (composition)
        {
            var inline = new LookupTable(LayoutLookupKind.Gpos, GlyphCount, 1, 0, null, [], null, jsAt,
                singlePositions: [new(Coverage(jsAt, 1), [pair.GetMatrixValue1(4, 4)], 2, 0x005F, jsAt, 0)]);
            var priority = new JstfPriorityTable(null, null,
                new(JstfLookupDomain.Gpos, [0, 1, 2], jsAt, 0), null, new([inline], jsAt, 0),
                null, null, null, null, null, jsAt, 0);
            var jstf = new JstfTable(0x00010000, GlyphCount, 0, 3, context,
                [new(0x44464C54, new(null, new([priority], jsAt, 0), [], jsAt, 0))], jsAt, 0);
            Ok(JstfTableCompiler.TryCompile(jstf, out var jsBytes, out _));
            tables.Add((JstfTag, jsBytes!));
        }
        var glyphs = new List<byte>();
        var offsets = new List<uint> { 0 };
        for (int i = 0; i < GlyphCount; i++)
        {
            glyphs.AddRange(GlyfImages.NoContourGlyphRecord());
            offsets.Add((uint)glyphs.Count);
        }
        tables.AddRange([
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(GlyphCount)),
            (SubsetFontFixture.GlyfTag, glyphs.ToArray()),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca(offsets.ToArray())),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))),
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar())]);
        return SubsetFontFixture.Face(tables.ToArray());
    }

    private static PairPosTable Pair(ushort[] coverage, bool explicitZero, DataLocation at)
    {
        var ranges = new List<ClassDefRange> { new(1, 1, 2), new(2, 2, 4) };
        if (explicitZero) ranges.Add(new(6, 6, 0));
        ranges.Add(new(8, 8, 3));
        var values1 = new StaticValueRecord[25];
        var values2 = new StaticValueRecord[25];
        Ok(StaticDeviceTableCompiler.TryCreate(12, 13, 1, new sbyte[] { 1, -1 },
            out var pixels, out _, location: at));
        for (int row = 0; row < 5; row++)
            for (int column = 0; column < 5; column++)
            {
                int index = row * 5 + column;
                short marker = Marker(row, column);
                var variation = new StaticDeviceTable(0, (ushort)index, 0x8000, [], at, 6);
                values1[index] = new(marker, (short)-marker, (short)(marker * 2), (short)(marker * -2),
                    pixels, null, variation, null);
                values2[index] = new((short)(marker + 10), (short)(marker + 20),
                    (short)(marker + 30), (short)(marker + 40), null, pixels, null, variation);
            }
        Ok(PairPosTableCompiler.TryCreate(coverage, ranges.ToArray(), [new(4, 4, 2), new(5, 5, 4), new(8, 8, 3)],
            5, 5, values1, values2, GlyphCount, 0x005F, 0x00AF, out var pair, out _,
            location: at, classDefFormat: 2));
        return pair!;
    }

    private static LookupTable Lookup(PairPosTable pair, bool extension, DataLocation at) => extension
        ? new(LayoutLookupKind.Gpos, GlyphCount, 9, 0, null, [], null, at,
            extensionPositions: [new(2, null, pair, null, at, 0)])
        : new(LayoutLookupKind.Gpos, GlyphCount, 2, 0, null, [], null, at, pairPositions: [pair]);

    private static PairPosTable PairLeaf(LookupTable lookup, bool extension) => extension
        ? Assert.Single(lookup.ExtensionPositions!).Pair! : Assert.Single(lookup.PairPositions!);

    private static void AssertMatrix(PairPosTable pair, ushort[] rows, ushort[] columns, VariationStoreTable store)
    {
        Assert.Equal(rows.Length, pair.Class1Count);
        Assert.Equal(columns.Length, pair.Class2Count);
        Assert.Equal(new VariationRegionAxis(0, 16384, 16384), store.Regions[0][0]);
        for (int row = 0; row < rows.Length; row++)
            for (int column = 0; column < columns.Length; column++)
            {
                Ok(pair.TryGetMatrixValue(row, column, out var first, out var second, out _));
                short marker = Marker(rows[row], columns[column]);
                Assert.Equal(marker, first.XPlacement);
                Assert.Equal(-marker, first.YPlacement);
                Assert.Equal(marker * 2, first.XAdvance);
                Assert.Equal(marker * -2, first.YAdvance);
                Assert.Equal(marker + 10, second.XPlacement);
                Assert.Equal(marker + 20, second.YPlacement);
                Assert.Equal(marker + 30, second.XAdvance);
                Assert.Equal(marker + 40, second.YAdvance);
                foreach (var pixels in new[] { first.XPlacementDevice, second.YPlacementDevice })
                {
                    Assert.NotNull(pixels);
                    Assert.Equal((ushort)12, pixels.StartSize);
                    Assert.Equal((ushort)13, pixels.EndSize);
                    Assert.Equal((ushort)1, pixels.DeltaFormat);
                    Assert.Equal(new sbyte[] { 1, -1 }, pixels.Deltas.ToArray());
                }
                foreach (var device in new[] { first.XAdvanceDevice, second.YAdvanceDevice })
                {
                    AssertVariation(store, device, 1000 + marker);
                }
                Assert.Null(first.YPlacementDevice);
                Assert.Null(first.YAdvanceDevice);
                Assert.Null(second.XPlacementDevice);
                Assert.Null(second.XAdvanceDevice);
            }
    }

    private static void AssertVariation(VariationStoreTable store, StaticDeviceTable? device, int expected)
    {
        Assert.NotNull(device);
        Assert.True(device.IsVariationDevice);
        Ok(store.TryGetDeltaSet(device.Outer, device.Inner, out bool present, out var deltas, out _));
        Assert.True(present);
        Assert.Equal(new[] { expected }, deltas.ToArray());
    }

    private static SubsetResult Run(FontFaceRef face, ushort[] roots, SubsetMode mode = SubsetMode.CompactRemap)
    {
        var status = SubsetOperation.TryPrepare(new()
        {
            Face = face, Mode = mode,
            Root = new() { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = roots },
            Options = new() { PreserveLayout = true }
        }, out var result, out _, out var diagnostic);
        Assert.True(status == ReadStatus.Done, $"Subset failed: {status}; {diagnostic}");
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        return Assert.IsType<SubsetResult>(result);
    }

    private static (GdefTable Table, GdefLookupContext Context) ReadGdef(SubsetResult result, int count)
    {
        Ok(GdefView.TryOpen(Table(result, GdefTag), out var view, out _, DataLocation.Anonymous(GdefTag)));
        Ok(view.TryReadTable(count, 1, out var table, out _));
        return (table!, Context(table!));
    }

    private static LayoutListsTable ReadGpos(SubsetResult result, int count, GdefLookupContext context)
    {
        Ok(LayoutListsView.TryOpen(Table(result, GposTag), out var view, out _, DataLocation.Anonymous(GposTag)));
        Ok(view.TryReadTable(count, LayoutLookupKind.Gpos, context, out var table, out _));
        return table!;
    }

    private static GdefLookupContext Context(GdefTable gdef)
    {
        var classes = new GdefClassDefinitions(gdef.Version, gdef.NumGlyphs, null, null, 0, 0, 0, 0, gdef.Location);
        Ok(GdefLookupContext.TryCreate(classes, null, gdef.ItemVariationStore, out var context, out _));
        return context!;
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

    private static short Marker(int row, int column) => (short)(row * 100 + column + 1);
    private static void Ok(ReadStatus status) => Assert.Equal(ReadStatus.Done, status);
}
