using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class PairPosLookupSharingTests
{
    private const int GlyphCount = 64;
    private static readonly DataLocation Location = DataLocation.Anonymous(0x47504F53);
    private static readonly byte[] StaticWire = [0, 9, 0, 12, 0, 2, 208, 71];
    private static readonly byte[] VariationWire = [0, 1, 0, 2, 128, 0];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PairLookupSharesContentWithoutMergingSubtableEntries(bool extension)
    {
        var first = Matrix(-11, Device(StaticWire), Device(VariationWire));
        var second = Matrix(-22, Device(StaticWire), Device(VariationWire));
        var third = Matrix(-33, Device(StaticWire), Device(VariationWire), 5);
        var lookup = Lookup([first, second, first, third], extension);

        byte[] bytes = Compile(lookup);

        Assert.Equal(extension ? 9 : 2, Word(bytes, 0));
        Assert.Equal(1, Word(bytes, 2));
        Assert.Equal(4, Word(bytes, 4));
        int a = Subtable(bytes, 0), b = Subtable(bytes, 1), c = Subtable(bytes, 3);
        if (!extension) Assert.Equal(a, Subtable(bytes, 2));
        Assert.NotEqual(a, b);
        Assert.Equal(a + Word(bytes, a + 8), b + Word(bytes, b + 8));
        Assert.Equal(a + Word(bytes, a + 10), b + Word(bytes, b + 10));
        Assert.NotEqual(a + Word(bytes, a + 10), c + Word(bytes, c + 10));
        Assert.Equal(a + Word(bytes, a + 18), b + Word(bytes, b + 18));
        Assert.Equal(a + Word(bytes, a + 20), c + Word(bytes, c + 20));
        int repeat = Subtable(bytes, 2);
        Assert.Equal(a + Word(bytes, a + 8), repeat + Word(bytes, repeat + 8));
        Assert.Equal(a + Word(bytes, a + 18), repeat + Word(bytes, repeat + 18));
        Assert.Equal(StaticWire, bytes.AsSpan(a + Word(bytes, a + 18), StaticWire.Length).ToArray());
        Assert.Equal(VariationWire, bytes.AsSpan(a + Word(bytes, a + 20), VariationWire.Length).ToArray());
        var pairs = ReadPairs(bytes);
        Assert.Equal(new short[] { -11, -22, -11, -33 },
            pairs.Select(pair => pair.GetMatrixValue1(0, 0).XAdvance).ToArray());
        Assert.Equal(new ClassDefRange[] { new(1, 3, 1) }, pairs[1].ClassDef1!.Ranges.ToArray());
        Assert.Equal(new ClassDefRange[] { new(5, 7, 1) }, pairs[3].ClassDef2!.Ranges.ToArray());
        Assert.Equal(bytes, Compile(lookup));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedPairFormatsShareDevicesUsingEachActualRelativeOrigin(bool extension)
    {
        Ok(PairPosTableCompiler.TryCreate([8, 9],
            [
                [new(10, new(XAdvance: -7, XAdvanceDevice: Device(StaticWire),
                    YAdvanceDevice: Device(VariationWire)))],
                [new(11, new(XAdvance: -8, XAdvanceDevice: Device(StaticWire)))],
            ], GlyphCount, 0x00C4, 0, out var pairSet, out _, location: Location));
        var matrix = Matrix(-9, Device(StaticWire), Device(VariationWire));

        byte[] bytes = Compile(Lookup([pairSet!, matrix], extension));

        int a = Subtable(bytes, 0), b = Subtable(bytes, 1);
        int set0 = a + Word(bytes, a + 10), set1 = a + Word(bytes, a + 12);
        Assert.NotEqual(set0, set1);
        int staticAt = set0 + Word(bytes, set0 + 6);
        Assert.Equal(staticAt, set1 + Word(bytes, set1 + 6));
        Assert.Equal(staticAt, b + Word(bytes, b + 18));
        Assert.Equal(set0 + Word(bytes, set0 + 8), b + Word(bytes, b + 20));
        Assert.Equal(0, Word(bytes, set1 + 8));
        Assert.Equal(StaticWire, bytes.AsSpan(staticAt, StaticWire.Length).ToArray());
        var pairs = ReadPairs(bytes);
        Assert.Equal(new ushort[] { 1, 2 }, pairs.Select(pair => pair.Format).ToArray());
        Assert.Equal((short?)-7, pairs[0].PairSets![0].Records[0].Value1.XAdvance);
        Assert.Equal((short?)-8, pairs[0].PairSets![1].Records[0].Value1.XAdvance);
        Assert.Null(pairs[0].PairSets![1].Records[0].Value1.YAdvanceDevice);
        Assert.Equal((short?)-9, pairs[1].GetMatrixValue1(0, 0).XAdvance);
    }

    [Fact]
    public void DeviceContentIncludesSizesFormatDeltasVariationIndicesAndNullPresence()
    {
        byte[][] wires =
        [
            StaticWire,
            [0, 9, 0, 12, 0, 2, 208, 70],
            [0, 10, 0, 13, 0, 2, 208, 71],
            [0, 9, 0, 10, 0, 2, 208, 0],
            [0, 9, 0, 12, 0, 3, 253, 0, 4, 7],
            VariationWire,
            [0, 2, 0, 2, 128, 0],
            [0, 1, 0, 3, 128, 0],
        ];
        var pairs = wires.Select(wire => Matrix(-1, Device(wire), null)).ToList();
        pairs.Add(Matrix(-1, null, null));
        pairs.Add(Matrix(-1, Device(StaticWire), null));

        byte[] bytes = Compile(Lookup([.. pairs], false));

        var targets = new HashSet<int>();
        for (int i = 0; i < wires.Length; i++)
        {
            int part = Subtable(bytes, i);
            int target = part + Word(bytes, part + 18);
            Assert.True(targets.Add(target), $"Distinct device {i} reused a different device's target.");
            Assert.Equal(wires[i], bytes.AsSpan(target, wires[i].Length).ToArray());
            Assert.Equal(0, Word(bytes, part + 20));
        }
        int absent = Subtable(bytes, wires.Length);
        Assert.Equal(0, Word(bytes, absent + 18));
        int repeated = Subtable(bytes, wires.Length + 1), first = Subtable(bytes, 0);
        Assert.Equal(first + Word(bytes, first + 18), repeated + Word(bytes, repeated + 18));
        Assert.Null(ReadPairs(bytes)[wires.Length].GetMatrixValue1(0, 0).XAdvanceDevice);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 9)]
    public void UnreachableSharedTailFallsBackToLegalPrivateTails(int subtableCount, int wireType)
    {
        const ushort class2Count = 16368;
        var pairs = new PairPosTable[subtableCount];
        for (int i = 0; i < pairs.Length; i++)
        {
            var values = new StaticValueRecord[class2Count];
            values[0] = new(XAdvance: (short)(-20 - i), XAdvanceDevice: Device(VariationWire));
            Ok(PairPosTableCompiler.TryCreate([1], [new(1, 2, 0)], [new(3, 4, 1)],
                1, class2Count, values, new StaticValueRecord[class2Count], GlyphCount, 0x0044, 0,
                out var pair, out _, location: Location, classDefFormat: 2));
            pairs[i] = Assert.IsType<PairPosTable>(pair);
        }

        byte[] bytes = Compile(Lookup(pairs, false));

        Assert.Equal(wireType, Word(bytes, 0));
        Assert.Equal(subtableCount, Word(bytes, 4));
        var classTargets = new HashSet<int>();
        for (int i = 0; i < pairs.Length; i++)
        {
            int part = Subtable(bytes, i);
            Assert.Equal((short)(-20 - i), BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(part + 16)));
            foreach (int field in new[] { 2, 8, 10, 18 })
                Assert.InRange((int)Word(bytes, part + field), 65488, ushort.MaxValue);
            int device = part + Word(bytes, part + 18);
            Assert.Equal(VariationWire, bytes.AsSpan(device, VariationWire.Length).ToArray());
            Assert.True(classTargets.Add(part + Word(bytes, part + 8)));
        }
        Assert.Equal(Enumerable.Range(0, subtableCount).Select(i => (short)(-20 - i)).ToArray(),
            ReadPairs(bytes).Select(pair => pair.GetMatrixValue1(0, 0).XAdvance).ToArray());
        if (wireType == 9)
        {
            int lastWrapper = Word(bytes, 6 + 2 * (subtableCount - 1));
            Assert.True(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(lastWrapper + 4)) > ushort.MaxValue);
        }
    }

    [Theory]
    [InlineData("work")]
    [InlineData("temporary")]
    [InlineData("output")]
    [InlineData("destination")]
    public void SharedPlanningRefusalDoesNotModifyDestination(string limit)
    {
        var lookup = Lookup([Matrix(-1, Device(StaticWire), Device(VariationWire)),
            Matrix(-2, Device(StaticWire), Device(VariationWire))], false);
        var budget = new BuildBudget();
        Ok(LookupTableCompiler.TryPlan(lookup, null, out var plan, out _, budget));
        var limits = limit switch
        {
            "work" => new BuildLimits { MaxWorkUnits = budget.Usage.WorkUnits - 1 },
            "temporary" => new BuildLimits { MaxTemporaryBytes = budget.Usage.TemporaryBytes - 1 },
            "output" => new BuildLimits { MaxOutputBytes = plan!.Length - 1 },
            _ => new BuildLimits(),
        };
        int capacity = plan!.Length - (limit == "destination" ? 1 : 0);
        var destination = Enumerable.Repeat((byte)0xA5, capacity).ToArray();

        var status = LookupTableCompiler.TryWrite(lookup, null, destination, out var diagnostic, limits);

        Assert.Equal(limit == "destination" ? ReadStatus.DestinationTooSmall : ReadStatus.ResourceLimit, status);
        Assert.Equal(limit == "destination" ? DiagnosticCode.DestinationTooSmall : DiagnosticCode.BudgetExceeded,
            diagnostic.Code);
        Assert.Equal(limit switch
        {
            "work" => DataField.WorkUnits,
            "temporary" => DataField.TemporaryBytes,
            _ => DataField.OutputBytes,
        }, diagnostic.Location.Field);
        Assert.All(destination, value => Assert.Equal((byte)0xA5, value));
    }

    private static PairPosTable Matrix(short value, StaticDeviceTable? x, StaticDeviceTable? y,
        ushort secondClassStart = 4)
    {
        Ok(PairPosTableCompiler.TryCreate([1, 2], [new(1, 3, 1)],
            [new(secondClassStart, (ushort)(secondClassStart + 2), 1)], 2, 2,
            [new(XAdvance: value, XAdvanceDevice: x, YAdvanceDevice: y), default, default, default],
            new StaticValueRecord[4], GlyphCount, 0x00C4, 0, out var pair, out _,
            location: Location, classDefFormat: 2));
        return Assert.IsType<PairPosTable>(pair);
    }

    private static StaticDeviceTable Device(byte[] wire)
    {
        Ok(StaticDeviceView.TryOpen(wire, out var view, out _, Location));
        Ok(view.TryRead(out var device, out _));
        return Assert.IsType<StaticDeviceTable>(device);
    }

    private static LookupTable Lookup(PairPosTable[] pairs, bool extension)
    {
        if (!extension)
        {
            Ok(LookupTableCompiler.TryCreatePairPos(GlyphCount, pairs, 1, null, null,
                out var lookup, out _, location: Location));
            return Assert.IsType<LookupTable>(lookup);
        }
        var wrapped = new ExtensionPosTable[pairs.Length];
        for (int i = 0; i < pairs.Length; i++)
        {
            int prior = Array.IndexOf(pairs, pairs[i], 0, i);
            if (prior >= 0)
            {
                wrapped[i] = wrapped[prior];
                continue;
            }
            Ok(ExtensionPosTable.TryCreate(2, null, pairs[i], null, out var child, out _, Location));
            wrapped[i] = Assert.IsType<ExtensionPosTable>(child);
        }
        return new(LayoutLookupKind.Gpos, GlyphCount, 9, 1, null, [], null, Location,
            extensionPositions: wrapped);
    }

    private static byte[] Compile(LookupTable lookup)
    {
        Ok(LookupTableCompiler.TryCompile(lookup, null, out var bytes, out _));
        return Assert.IsType<byte[]>(bytes);
    }

    private static PairPosTable[] ReadPairs(byte[] bytes)
    {
        Ok(LookupView.TryOpen(bytes, out var view, out _, Location));
        Ok(view.TryRead(GlyphCount, LayoutLookupKind.Gpos, null, out var lookup, out _));
        var parsed = Assert.IsType<LookupTable>(lookup);
        return parsed.LookupType == 2 ? parsed.PairPositions!.ToArray()
            : parsed.ExtensionPositions!.Select(child => child.Pair!).ToArray();
    }

    private static int Subtable(byte[] bytes, int ordinal)
    {
        int at = Word(bytes, 6 + 2 * ordinal);
        return Word(bytes, 0) == 9 ? at + (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 4)) : at;
    }

    private static ushort Word(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at, 2));
    private static void Ok(ReadStatus status) => Assert.Equal(ReadStatus.Done, status);
}
