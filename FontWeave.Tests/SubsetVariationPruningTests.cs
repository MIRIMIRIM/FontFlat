using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Variation;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetVariationPruningTests
{
    [Fact]
    public void EmptyVariationReachabilityStillAccountsForRegionScan()
    {
        var budget = new BuildBudget(new BuildLimits { MaxWorkUnits = 0 });
        Assert.Equal(ReadStatus.ResourceLimit, SubsetOperation.PruneVariationStore(
            Store(), Array.Empty<DeltaSetIndex>(), budget, out var pruned,
            out var remap, out var diagnostic));
        Assert.Null(pruned);
        Assert.Null(remap);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(DataField.WorkUnits, diagnostic.Location.Field);
        Assert.Equal(3UL, diagnostic.Detail);
        Assert.Equal(0UL, diagnostic.Expected);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MetricStorePrunesDeadRowsAndRegionsWithoutChangingAnyMetric(
        bool vertical, bool implicitAdvance)
    {
        var store = Store();
        var advance = implicitAdvance ? null : Map(new(0, 0), new(0, 1), new(0, 2), new(0, 3), new(0, 4));
        var side = Map(new(2, 1), new(2, 0));
        byte[]? input;
        if (vertical)
        {
            Assert.Equal(ReadStatus.Done, VvarTableCompiler.TryCreate(store, advance,
                side, null, side, 1, 6, out var model, out _));
            Assert.Equal(ReadStatus.Done, VvarTableCompiler.TryCompile(model!, out input, out _));
        }
        else
        {
            Assert.Equal(ReadStatus.Done, HvarTableCompiler.TryCreate(store, advance,
                side, null, 1, 6, out var model, out _));
            Assert.Equal(ReadStatus.Done, HvarTableCompiler.TryCompile(model!, out input, out _));
        }
        uint tag = vertical ? SubsetFontFixture.VvarTag : SubsetFontFixture.HvarTag;
        byte[] output = Subset(tag, input!);
        VariationStoreTable outputStore;
        DeltaSetIndexMapTable? outputAdvance, outputSide;
        if (vertical)
        {
            Assert.Equal(ReadStatus.Done, VvarView.TryOpen(output, out var view, out _));
            Assert.Equal(ReadStatus.Done, view.TryReadTable(1, 3, out var model, out _));
            outputStore = model!.Store;
            outputAdvance = model.AdvanceHeightMapping;
            outputSide = model.TsbMapping;
            Assert.Null(model.BsbMapping);
            for (int gid = 0; gid < 3; gid++)
            {
                Assert.True(model.VOrgMapping!.TryGetMapping(gid, out var origin));
                Assert.True(outputSide!.TryGetMapping(gid, out var bearing));
                Assert.Equal(bearing, origin);
            }
        }
        else
        {
            Assert.Equal(ReadStatus.Done, HvarView.TryOpen(output, out var view, out _));
            Assert.Equal(ReadStatus.Done, view.TryReadTable(1, 3, out var model, out _));
            outputStore = model!.Store;
            outputAdvance = model.AdvanceWidthMapping;
            outputSide = model.LsbMapping;
            Assert.Null(model.RsbMapping);
        }
        Assert.Equal(2, outputStore.DataCount);
        Assert.Equal(2, outputStore.RegionCount);
        Assert.Equal(implicitAdvance ? 3 : 2, outputStore.Data[0]!.ItemCount);
        Assert.Equal(2, outputStore.Data[1]!.ItemCount);
        Assert.Equal(1, outputStore.Data[0]!.WordDeltaCount);
        Assert.Equal(2, outputStore.Data[0]!.RegionIndexCount);
        Assert.Equal(new ushort[] { 1, 0 }, outputStore.Data[0]!.RegionIndexes);
        Assert.True(output.Length < input!.Length);
        int[] sourceGlyphs = [0, 4, 5];
        for (int gid = 0; gid < sourceGlyphs.Length; gid++)
        {
            int original = sourceGlyphs[gid];
            DeltaSetIndex expected = new(0, (ushort)original);
            if (advance is not null) Assert.True(advance.TryGetMapping(original, out expected));
            var actual = new DeltaSetIndex(0, (ushort)gid);
            if (outputAdvance is not null) Assert.True(outputAdvance.TryGetMapping(gid, out actual));
            Assert.Equal(SparseDeltas(store, expected), SparseDeltas(outputStore, actual));
            Assert.True(side.TryGetMapping(original, out expected));
            Assert.True(outputSide!.TryGetMapping(gid, out actual));
            Assert.Equal(SparseDeltas(store, expected), SparseDeltas(outputStore, actual));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MetricStoreRejectsLiveOutOfRangeVariationIndex(bool outer)
    {
        var map = Map(outer ? new DeltaSetIndex(8, 0) : new DeltaSetIndex(0, 9));
        Assert.Equal(ReadStatus.Done, HvarTableCompiler.TryCreate(Store(), map, null,
            null, 1, 6, out var model, out _));
        Assert.Equal(ReadStatus.Done, HvarTableCompiler.TryCompile(model!, out var input, out _));
        Assert.Equal(ReadStatus.InvalidIndex, SubsetOperation.TryPrepare(
            Request(SubsetFontFixture.HvarTag, input!), out var result, out _, out var diagnostic));
        Assert.Null(result);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.HvarTag, diagnostic.Location.TableTag);
        Assert.Equal(outer ? 8UL : 9UL, diagnostic.Detail);
        Assert.Equal(outer ? 3UL : 6UL, diagnostic.Expected);
    }

    [Fact]
    public void MetricStorePreservesSentinelAndNullOuterNoVariation()
    {
        var store = Store();
        Assert.Equal(ReadStatus.Done, VariationStoreTableCompiler.TryCreate(1,
            store.Regions, [store.Data[0], null], out var source, out _));
        var map = Map(new(ushort.MaxValue, ushort.MaxValue), new(1, 234));
        Assert.Equal(ReadStatus.Done, HvarTableCompiler.TryCreate(source!, map, null,
            null, 1, 6, out var model, out _));
        Assert.Equal(ReadStatus.Done, HvarTableCompiler.TryCompile(model!, out var input, out _));
        byte[] output = Subset(SubsetFontFixture.HvarTag, input!);
        Assert.Equal(ReadStatus.Done, HvarView.TryOpen(output, out var view, out _));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(1, 3, out var rebuilt, out _));
        Assert.Equal(0, rebuilt!.Store.DataCount);
        Assert.Equal(0, rebuilt.Store.RegionCount);
        for (int gid = 0; gid < 3; gid++)
        {
            Assert.Equal(ReadStatus.Done, rebuilt.TryGetDeltaSets(HvarMetric.Advance,
                gid, out bool hasVariation, out _, out _));
            Assert.False(hasVariation);
        }
    }

    private static VariationStoreTable Store()
    {
        VariationRegionAxis[][] regions = [[new(0, 16384, 16384)],
            [new(-16384, -16384, 0)], [new(0, 8192, 16384)]];
        Assert.Equal(ReadStatus.Done, ItemVariationDataTableCompiler.TryCreate(3,
            new ushort[] { 0, 1, 2 }, new int[][] { [10, 0, 128], [200, 300, 30],
                [201, 301, 31], [202, 302, 32], [-128, 0, -129], [127, 0, 127] }, out var first, out _));
        Assert.Equal(ReadStatus.Done, ItemVariationDataTableCompiler.TryCreate(0,
            new ushort[] { 1 }, new int[][] { [50], [60] }, out var dead, out _));
        Assert.Equal(ReadStatus.Done, ItemVariationDataTableCompiler.TryCreate(1,
            new ushort[] { 2 }, new int[][] { [-128], [128] }, out var side, out _));
        Assert.Equal(ReadStatus.Done, VariationStoreTableCompiler.TryCreate(1,
            regions, [first, dead, side], out var store, out _));
        return store!;
    }

    private static DeltaSetIndexMapTable Map(params DeltaSetIndex[] entries)
    {
        Assert.Equal(ReadStatus.Done, DeltaSetIndexMapTableCompiler.TryCreate(0x3F,
            entries, out var map, out _));
        return map!;
    }

    private static (VariationRegionAxis, int)[] SparseDeltas(VariationStoreTable store, DeltaSetIndex index)
    {
        var data = store.Data[index.Outer]!;
        return data.Rows[index.Inner].Select((delta, column) =>
            (store.Regions[data.RegionIndexes[column]][0], delta))
            .Where(static pair => pair.delta != 0).OrderBy(static pair => pair.Item1.PeakCoord).ToArray();
    }

    private static SubsetRequest Request(uint tag, byte[] table)
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(6)),
            (SubsetFontFixture.GlyfTag, Enumerable.Range(0, 6).SelectMany(_ => GlyfImages.NoContourGlyphRecord()).ToArray()),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 12, 24, 36, 48, 60, 72])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapWithMacRoman((0x41, 4))),
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar(1)), (tag, table));
        return new SubsetRequest { Face = face, Mode = SubsetMode.CompactRemap,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = [0, 4, 5] },
            Options = new SubsetOptions { NoLayout = true } };
    }

    private static byte[] Subset(uint tag, byte[] table)
    {
        Assert.Equal(ReadStatus.Done, SubsetOperation.TryPrepare(Request(tag, table), out var result, out _, out _));
        byte[] font = new byte[result!.Font.OutputLength];
        Assert.Equal(ReadStatus.Done, result.Font.TryCopyTo(font, out _, out _));
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int at = 12 + i * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(at)) != tag) continue;
            int start = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(at + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(at + 12));
            return font.AsSpan(start, length).ToArray();
        }
        throw new InvalidOperationException("Required metric variation table was removed.");
    }
}
