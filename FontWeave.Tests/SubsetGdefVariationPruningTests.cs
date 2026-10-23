using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using FontWeave.Tables.Variation;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetGdefVariationPruningTests
{
    private const int GlyphCount = 8;
    private const uint GdefTag = 0x47444546, GposTag = 0x47504F53,
        JstfTag = 0x4A535446, MathTag = 0x4D415448;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(12)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(9)]
    public void CompactSubsetPrunesSharedStoreAndPreservesEveryConsumer(int lookupKind)
    {
        var result = Run(Face(lookupKind));
        var (gdef, context) = ReadGdef(result);
        var store = Assert.IsType<VariationStoreTable>(gdef.ItemVariationStore);
        Assert.Equal(2, store.DataCount);
        Assert.Equal(1, store.RegionCount);
        Assert.Equal(1, store.Data[0]!.ItemCount);
        Assert.Equal(3, store.Data[1]!.ItemCount);
        Assert.Equal(new VariationRegionAxis(0, 16384, 16384), store.Regions[0][0]);

        var gpos = ReadGpos(result, context);
        if (lookupKind == 12)
        {
            var pair = Assert.Single(gpos.Lookups[0].PairPositions!);
            Assert.Equal(1, pair.Class1Count);
            Assert.Equal(2, pair.Class2Count);
            Assert.Equal((ushort)0, pair.ClassDef1!.ClassOf(1));
            Assert.Equal((ushort)1, pair.ClassDef2!.ClassOf(2));
        }
        foreach (var device in GposDevices(gpos.Lookups[0]))
            AssertDelta(store, device, 0, 0, 12);
        Assert.Equal(new ushort[] { 3 }, gdef.LigCaretList!.Coverage.Glyphs.ToArray());
        AssertDelta(store, gdef.LigCaretList.LigGlyphs[0][0].Device, 1, 2, 33);

        Ok(JstfView.TryOpen(Table(result, JstfTag), out var view, out _,
            DataLocation.Anonymous(JstfTag)));
        Ok(view.TryReadTable(4, 0, 1, context, out var jstf, out _));
        var script = Assert.Single(jstf!.Scripts).Script;
        var shrink = script.DefaultLangSys!.Priorities[0].ShrinkageJstfMax!.Lookups[0];
        var extend = Assert.Single(script.LangSys).LangSys.Priorities[0]
            .ExtensionJstfMax!.Lookups[0];
        foreach (var device in GposDevices(shrink)) AssertDelta(store, device, 1, 0, 31);
        foreach (var device in GposDevices(extend)) AssertDelta(store, device, 1, 1, 32);
    }

    [Fact]
    public void MathVariationDeviceKeepsSharedStoreAndIndicesUnchanged()
    {
        var result = Run(Face(1, withMath: true));
        var (gdef, context) = ReadGdef(result);
        var store = Assert.IsType<VariationStoreTable>(gdef.ItemVariationStore);
        Assert.Equal(4, store.DataCount);
        Assert.Equal(3, store.RegionCount);
        Assert.Equal(new[] { 1, 3, 1, 4 }, store.Data.Select(data => (int)data!.ItemCount).ToArray());
        foreach (var device in GposDevices(ReadGpos(result, context).Lookups[0]))
            AssertDelta(store, device, 1, 2, 12);
        Ok(MathView.TryOpen(Table(result, MathTag), out var view, out _,
            DataLocation.Anonymous(MathTag)));
        Ok(view.TryReadTable(4, out var math, out _));
        AssertDelta(store, math!.Constants!.Devices[0], 0, 0, 99);
    }

    private static FontFaceRef Face(int lookupKind, bool withMath = false)
    {
        var gdefAt = DataLocation.Anonymous(GdefTag);
        var gposAt = DataLocation.Anonymous(GposTag);
        var jstfAt = DataLocation.Anonymous(JstfTag);
        var data = new ItemVariationDataTable?[4];
        int[][][] rows = [[[99]], [[10], [11], [12]], [[88]], [[30], [31], [32], [33]]];
        for (int i = 0; i < data.Length; i++)
            Ok(ItemVariationDataTableCompiler.TryCreate(1, [0], rows[i], out data[i],
                out _, location: gdefAt));
        Ok(VariationStoreTableCompiler.TryCreate(1,
            [new[] { new VariationRegionAxis(0, 16384, 16384) },
                [new(-16384, -16384, 0)], [new(0, 8192, 16384)]], data,
            out var store, out _, location: gdefAt));
        var carets = new LigCaretListTable(Coverage(gdefAt, 3, 7),
            [new([CaretValueEntry.ForDevice(300, Device(3, 3, gdefAt), gdefAt)], gdefAt, 0),
                new([CaretValueEntry.ForDevice(700, Device(0, 0, gdefAt), gdefAt)], gdefAt, 0)],
            GlyphCount, gdefAt, 0, []);
        Ok(GdefTableCompiler.TryCreate(0x00010003, GlyphCount, null, null, null,
            null, carets, store, out var gdef, out _, location: gdefAt));
        Ok(GdefTableCompiler.TryCompile(gdef!, out var gdefBytes, out _));
        var context = Context(gdef!);

        var lookup = Lookup(lookupKind, Device(1, 2, gposAt), gposAt);
        var script = new LayoutScript(0x44464C54,
            new(ushort.MaxValue, [0], gposAt, 0), [], gposAt, 0);
        var feature = new LayoutFeature(0x6B65726E, 0, [0], gposAt, 0);
        Ok(GposTableCompiler.TryCreate(0x00010000, 0, [script], [feature], [lookup],
            GlyphCount, out var gpos, out _, location: gposAt));
        Ok(GposTableCompiler.TryCompile(gpos!, context, out var gposBytes, out _));

        var shrink = new JstfMaxTable([Lookup(1, Device(3, 1, jstfAt), jstfAt)], jstfAt, 0);
        var extend = new JstfMaxTable([Lookup(1, Device(3, 2, jstfAt), jstfAt)], jstfAt, 0);
        var defaultLang = new JstfLangSysTable(
            [new(null, null, null, null, shrink, null, null, null, null, null, jstfAt, 0)], jstfAt, 0);
        var taggedLang = new JstfLangSysTable(
            [new(null, null, null, null, null, null, null, null, null, extend, jstfAt, 0)], jstfAt, 0);
        var jstfScript = new JstfScriptTable(null, defaultLang,
            [new(0x454E4720, taggedLang)], jstfAt, 0);
        Ok(JstfTableCompiler.TryCreate(0x00010000, GlyphCount, 0, 1, context,
            [new JstfScriptRecord(0x44464C54, jstfScript)], out var jstf, out _, location: jstfAt));
        Ok(JstfTableCompiler.TryCompile(jstf!, out var jstfBytes, out _));

        var glyphs = new List<byte>();
        var offsets = new List<uint> { 0 };
        for (int i = 0; i < GlyphCount; i++)
        {
            glyphs.AddRange(GlyfImages.NoContourGlyphRecord());
            offsets.Add((uint)glyphs.Count);
        }
        var tables = new List<(uint, byte[])>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(GlyphCount)),
            (SubsetFontFixture.GlyfTag, [.. glyphs]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([.. offsets])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))),
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar()),
            (GdefTag, gdefBytes!), (GposTag, gposBytes!), (JstfTag, jstfBytes!)
        };
        if (withMath)
        {
            var at = DataLocation.Anonymous(MathTag);
            var devices = new StaticDeviceTable?[51];
            devices[0] = Device(0, 0, at);
            var constants = new MathConstantsTable(80, 60, 100, 200,
                new MathValueRecord[51], devices, 60, at, 0);
            Ok(MathTableCompiler.TryCreate(0x00010000, GlyphCount, constants, null,
                null, out var math, out _, location: at));
            Ok(MathTableCompiler.TryCompile(math!, out var bytes, out _));
            tables.Add((MathTag, bytes!));
        }
        return SubsetFontFixture.Face([.. tables]);
    }

    private static LookupTable Lookup(int kind, StaticDeviceTable device, DataLocation at)
    {
        var value = new StaticValueRecord(10, 20, 30, 40, device, device, device, device);
        var first = Coverage(at, 1);
        var second = Coverage(at, 2);
        var anchor = new AnchorTable(3, 100, 200, 0, device, device, at, 0);
        var single = new SinglePosTable(first, [value], 2, 0x00FF, at, 0);
        PairPosTable Pair(bool matrix)
        {
            if (!matrix) return new(first, 1, 0x00FF, 0x00FF,
                pairSets: [new([new PairValueRecord(2, value, value)], at, 0)], location: at);
            Ok(ClassDefTableCompiler.TryCreate([new(1, 1, 1)], GlyphCount, 1, out var c1, out _, location: at));
            Ok(ClassDefTableCompiler.TryCreate([new(2, 2, 1)], GlyphCount, 1, out var c2, out _, location: at));
            return new(first, 2, 0x00FF, 0x00FF, classDef1: c1, classDef2: c2,
                class1Count: 2, class2Count: 2, values1: [default, default, default, value],
                values2: [default, default, default, value], location: at);
        }
        return kind switch
        {
            1 => new(LayoutLookupKind.Gpos, GlyphCount, 1, 0, null, [], null, at,
                singlePositions: [single]),
            2 or 12 => new(LayoutLookupKind.Gpos, GlyphCount, 2, 0, null, [], null, at,
                pairPositions: [Pair(kind == 12)]),
            3 => new(LayoutLookupKind.Gpos, GlyphCount, 3, 0, null, [], null, at,
                cursiveAttachments: [new(first, [new(anchor, anchor)], GlyphCount, at, 0)]),
            4 => new(LayoutLookupKind.Gpos, GlyphCount, 4, 0, null, [], null, at,
                markBases: [new(first, [new(0, anchor)], second, 1, [[anchor]], GlyphCount, at, 0)]),
            5 => new(LayoutLookupKind.Gpos, GlyphCount, 5, 0, null, [], null, at,
                markLigs: [new(first, [new(0, anchor)], second,
                    [new(1, [[anchor]], at, 0)], GlyphCount, at, 0)]),
            6 => new(LayoutLookupKind.Gpos, GlyphCount, 6, 0, null, [], null, at,
                markMarks: [new(first, [new(0, anchor)], second, 1, [[anchor]], GlyphCount, at, 0)]),
            9 => new(LayoutLookupKind.Gpos, GlyphCount, 9, 0, null, [], null, at,
                extensionPositions: [new(1, single, null, null, at, 0)]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static StaticDeviceTable?[] GposDevices(LookupTable lookup)
    {
        static StaticDeviceTable?[] Values(StaticValueRecord value) =>
            [value.XPlacementDevice, value.YPlacementDevice, value.XAdvanceDevice, value.YAdvanceDevice];
        static StaticDeviceTable?[] Anchors(AnchorTable? first, AnchorTable? second) =>
            [first!.XDevice, first.YDevice, second!.XDevice, second.YDevice];
        if (lookup.LookupType == 1) return Values(lookup.SinglePositions![0].Values[0]);
        if (lookup.LookupType == 9) return Values(lookup.ExtensionPositions![0].Single!.Values[0]);
        if (lookup.LookupType == 2)
        {
            var pair = lookup.PairPositions![0];
            StaticValueRecord first, second;
            if (pair.Format == 1)
            {
                var row = pair.PairSets![0].Records[0];
                first = row.Value1; second = row.Value2;
            }
            else Ok(pair.TryGetMatrixValue(pair.ClassDef1!.ClassOf(1), pair.ClassDef2!.ClassOf(2),
                out first, out second, out _));
            return [.. Values(first), .. Values(second)];
        }
        return lookup.LookupType switch
        {
            3 => Anchors(lookup.CursiveAttachments![0].Records[0].Entry,
                lookup.CursiveAttachments[0].Records[0].Exit),
            4 => Anchors(lookup.MarkBases![0].Marks[0].Anchor, lookup.MarkBases[0].BaseRow(0)[0]),
            5 => Anchors(lookup.MarkLigs![0].Marks[0].Anchor,
                lookup.MarkLigs[0].Ligatures[0].ComponentRow(0)[0]),
            6 => Anchors(lookup.MarkMarks![0].Marks[0].Anchor, lookup.MarkMarks[0].Mark2Row(0)[0]),
            _ => throw new ArgumentOutOfRangeException(nameof(lookup))
        };
    }

    private static SubsetResult Run(FontFaceRef face)
    {
        var status = SubsetOperation.TryPrepare(new SubsetRequest
        {
            Face = face, Mode = SubsetMode.CompactRemap,
            Root = new() { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = new ushort[] { 1, 2, 3 } },
            Options = new() { PreserveLayout = true }
        }, out var result, out _, out var diagnostic);
        Assert.True(status == ReadStatus.Done, $"Subset failed: {status}; {diagnostic}.");
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(status);
        Assert.Equal(new ushort[] { 0, 1, 2, 3 }, result!.Map!.Entries.Select(e => e.InputGid).ToArray());
        return result;
    }

    private static (GdefTable, GdefLookupContext) ReadGdef(SubsetResult result)
    {
        Ok(GdefView.TryOpen(Table(result, GdefTag), out var view, out _, DataLocation.Anonymous(GdefTag)));
        Ok(view.TryReadTable(4, 1, out var model, out _));
        return (model!, Context(model!));
    }

    private static LayoutListsTable ReadGpos(SubsetResult result, GdefLookupContext context)
    {
        Ok(LayoutListsView.TryOpen(Table(result, GposTag), out var view, out _, DataLocation.Anonymous(GposTag)));
        Ok(view.TryReadTable(4, LayoutLookupKind.Gpos, context, out var model, out _));
        return model!;
    }

    private static GdefLookupContext Context(GdefTable gdef)
    {
        var classes = new GdefClassDefinitions(gdef.Version, gdef.NumGlyphs,
            gdef.GlyphClasses, gdef.MarkAttachmentClasses, 0, 0, 0, 0, gdef.Location);
        Ok(GdefLookupContext.TryCreate(classes, gdef.MarkGlyphSets, gdef.ItemVariationStore,
            out var context, out _));
        return context!;
    }

    private static StaticDeviceTable Device(ushort outer, ushort inner, DataLocation location) =>
        new(outer, inner, 0x8000, [], location, 6);

    private static CoverageTable Coverage(DataLocation at, params ushort[] glyphs)
    {
        Ok(CoverageTableCompiler.TryCreate(glyphs, GlyphCount, 1, out var model, out _, location: at));
        return model!;
    }

    private static void AssertDelta(VariationStoreTable store, StaticDeviceTable? device,
        ushort outer, ushort inner, int delta)
    {
        var actual = Assert.IsType<StaticDeviceTable>(device);
        Assert.Equal((ushort)0x8000, actual.DeltaFormat);
        Assert.Equal(outer, actual.Outer);
        Assert.Equal(inner, actual.Inner);
        Ok(store.TryGetDeltaSet(actual.Outer, actual.Inner, out bool present, out var deltas, out _));
        Assert.True(present);
        Assert.Equal(new[] { delta }, deltas.ToArray());
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

    private static void Ok(ReadStatus status) => Assert.Equal(ReadStatus.Done, status);
}
