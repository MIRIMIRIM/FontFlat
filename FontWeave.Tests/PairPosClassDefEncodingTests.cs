using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class PairPosClassDefEncodingTests
{
    private static readonly DataLocation Location = DataLocation.Anonymous(0x47504F53);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedSourceFormatsShareCanonicalClassesWithoutChangingGlyphClassification(bool extension)
    {
        ClassDefRange[] denseRanges = [new(0, 1, 0), new(2, 2, 1), new(3, 3, 1),
            new(4, 4, 0), new(5, 6, 2), new(7, 9, 0)];
        ClassDefRange[] sparseRanges = [new(2, 3, 1), new(5, 6, 2)];
        var dense = Matrix(Classes([new(1, 1, 1)], 1), Classes(denseRanges, 1), -11);
        var sparse = Matrix(Classes([new(1, 1, 1)], 2), Classes(sparseRanges, 2), -31);
        Ok(PairPosTableCompiler.TryCreate([10], [[new(11, new(XAdvance: -91), new(YAdvance: 44))]],
            32, 4, 8, out var format1, out _, location: Location));
        var lookup = Lookup([dense, format1!, sparse, dense], extension);

        byte[] bytes = Compile(lookup);

        Assert.Equal(4, Word(bytes, 4));
        int first = Subtable(bytes, 0), third = Subtable(bytes, 2), repeated = Subtable(bytes, 3);
        int class2 = first + Word(bytes, first + 10);
        Assert.NotEqual(first, third);
        Assert.Equal(class2, third + Word(bytes, third + 10));
        Assert.Equal(class2, repeated + Word(bytes, repeated + 10));
        Assert.Equal(first + Word(bytes, first + 8), third + Word(bytes, third + 8));
        byte[] expected = [0, 1, 0, 2, 0, 5, 0, 1, 0, 1, 0, 0, 0, 2, 0, 2];
        Assert.Equal(expected, bytes.AsSpan(class2, expected.Length).ToArray());
        var output = ReadPairs(bytes, 32);
        Assert.Equal(new ushort[] { 2, 1, 2, 2 }, output.Select(pair => pair.Format).ToArray());
        foreach (int index in new[] { 0, 2, 3 })
        {
            for (int glyph = 0; glyph < 32; glyph++)
            {
                Assert.Equal((ushort)(glyph == 1 ? 1 : 0), output[index].ClassDef1!.ClassOf(glyph));
                Assert.Equal((ushort)(glyph is 2 or 3 ? 1 : glyph is 5 or 6 ? 2 : 0),
                    output[index].ClassDef2!.ClassOf(glyph));
            }
            AssertMatrix(output[index], index == 2 ? (short)-31 : (short)-11);
        }
        Assert.Equal((short)-91, output[1].PairSets![0].Records[0].Value1.XAdvance);
        Assert.Equal((short)44, output[1].PairSets![0].Records[0].Value2.YAdvance);
        Assert.Equal((ushort)1, dense.ClassDef2!.Format);
        Assert.Equal(denseRanges, dense.ClassDef2.Ranges.ToArray());
        Assert.Equal((ushort)2, sparse.ClassDef2!.Format);
        Assert.Equal(sparseRanges, sparse.ClassDef2.Ranges.ToArray());
        Assert.Equal(bytes, Compile(lookup));
    }

    [Theory]
    [InlineData("dense")]
    [InlineData("empty")]
    [InlineData("zero")]
    [InlineData("merged")]
    [InlineData("tie")]
    [InlineData("full_domain")]
    public void SingletonPairLookupSelectsShortestClassEncodingAcrossWholeGlyphDomain(string scenario)
    {
        int glyphCount = scenario == "full_domain" ? ushort.MaxValue : 32;
        ClassDefRange[] ranges = scenario switch
        {
            "dense" => [new(2, 2, 1), new(3, 3, 2), new(4, 4, 1), new(5, 5, 2)],
            "empty" => [],
            "zero" => [new(3, 4, 0), new(10, 20, 0)],
            "merged" => [new(10, 11, 4), new(12, 13, 4)],
            "tie" => [new(7, 8, 3)],
            _ => [new(0, 59999, 0), new(60000, 60000, 7), new(60001, 65533, 0), new(65534, 65534, 3)],
        };
        ushort sourceFormat = scenario is "empty" or "full_domain" ? (ushort)1 : (ushort)2;
        var source = Matrix(Classes([new(1, 1, 1)], 2, glyphCount), Classes(ranges, sourceFormat, glyphCount), -7);

        byte[] bytes = Compile(Lookup([source], false));

        byte[] expectedWire = scenario switch
        {
            "dense" => [0, 1, 0, 2, 0, 4, 0, 1, 0, 2, 0, 1, 0, 2],
            "empty" or "zero" => [0, 2, 0, 0],
            "merged" => [0, 2, 0, 1, 0, 10, 0, 13, 0, 4],
            "tie" => [0, 1, 0, 7, 0, 2, 0, 3, 0, 3],
            _ => [0, 2, 0, 2, 234, 96, 234, 96, 0, 7, 255, 254, 255, 254, 0, 3],
        };
        int part = Subtable(bytes, 0), class2 = part + Word(bytes, part + 10);
        Assert.Equal(expectedWire, bytes.AsSpan(class2, expectedWire.Length).ToArray());
        Assert.Equal(class2 + expectedWire.Length, bytes.Length);
        var output = Assert.Single(ReadPairs(bytes, glyphCount));
        for (int glyph = 0; glyph < glyphCount; glyph++)
        {
            ushort expectedClass = scenario switch
            {
                "dense" => (ushort)(glyph is 2 or 4 ? 1 : glyph is 3 or 5 ? 2 : 0),
                "merged" => (ushort)(glyph is >= 10 and <= 13 ? 4 : 0),
                "tie" => (ushort)(glyph is 7 or 8 ? 3 : 0),
                "full_domain" => (ushort)(glyph == 60000 ? 7 : glyph == 65534 ? 3 : 0),
                _ => 0,
            };
            Assert.Equal(expectedClass, output.ClassDef2!.ClassOf(glyph));
            Assert.Equal((ushort)(glyph == 1 ? 1 : 0), output.ClassDef1!.ClassOf(glyph));
        }
        AssertMatrix(output, -7);
        Assert.Equal(sourceFormat, source.ClassDef2!.Format);
        Assert.Equal(ranges, source.ClassDef2.Ranges.ToArray());
    }

    [Fact]
    public void GenericClassCompilerPreservesExplicitZeroMembershipAndRequestedFormat()
    {
        ClassDefRange[] sparse = [new(1, 3, 0), new(5, 5, 7)];
        ClassDefRange[] dense = [new(1, 3, 0), new(4, 5, 7)];
        foreach (var (ranges, format, expected) in new[]
        {
            (sparse, (ushort)2, new byte[] { 0, 2, 0, 2, 0, 1, 0, 3, 0, 0, 0, 5, 0, 5, 0, 7 }),
            (dense, (ushort)1, new byte[] { 0, 1, 0, 1, 0, 5, 0, 0, 0, 0, 0, 0, 0, 7, 0, 7 }),
        })
        {
            var model = Classes(ranges, format);
            Ok(ClassDefTableCompiler.TryCompile(model, out var bytes, out _));
            Assert.Equal(expected, bytes);
            Ok(ClassDefView.TryOpen(bytes!, out var view, out _, Location));
            Ok(view.TryRead(32, out var decoded, out _));
            var output = Assert.IsType<ClassDefTable>(decoded);
            Assert.Equal(format, output.Format);
            Assert.Equal(ranges, output.Ranges.ToArray());
        }
        Assert.Equal(ReadStatus.UnsupportedFormat, ClassDefTableCompiler.TryCreate(sparse, 32, 1,
            out var rejected, out var diagnostic, location: Location));
        Assert.Null(rejected);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
    }

    [Fact]
    public void AlreadyShortestSingletonKeepsPrivateLayoutWithoutChangingSourceModels()
    {
        var pair = Matrix(Classes([new(1, 1, 1)], 1), Classes([], 2), -3);
        Ok(PairPosTableCompiler.TryCompile(pair, out var privateBytes, out _));

        byte[] bytes = Compile(Lookup([pair], false));

        Assert.Equal(8, Subtable(bytes, 0));
        Assert.Equal(privateBytes, bytes.AsSpan(8).ToArray());
        Assert.Equal((ushort)1, pair.ClassDef1!.Format);
        Assert.Equal(new ClassDefRange[] { new(1, 1, 1) }, pair.ClassDef1.Ranges.ToArray());
        Assert.Equal((ushort)2, pair.ClassDef2!.Format);
        Assert.Empty(pair.ClassDef2.Ranges.ToArray());
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void CanonicalSharedTailOutsideOffset16FallsBackToOriginalPrivateClasses(int count, bool extension)
    {
        var pairs = new PairPosTable[count];
        for (int i = 0; i < count; i++)
        {
            ushort format = i % 2 == 0 ? (ushort)1 : (ushort)2;
            pairs[i] = Matrix(Classes([new(1, 2, 0)], format),
                Classes([new(3, 4, 1)], format), (short)(-20 - i), 1, 16368);
        }

        byte[] bytes = Compile(Lookup(pairs, extension));

        Assert.Equal(extension || count == 3 ? 9 : 2, Word(bytes, 0));
        var output = ReadPairs(bytes, 32);
        var targets = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int part = Subtable(bytes, i), class1 = part + Word(bytes, part + 8);
            Assert.True(targets.Add(class1));
            Assert.InRange((int)Word(bytes, part + 8), 65488, ushort.MaxValue);
            Assert.Equal(pairs[i].ClassDef1!.Format, Word(bytes, class1));
            Assert.Equal(new ClassDefRange[] { new(1, 2, 0) }, output[i].ClassDef1!.Ranges.ToArray());
            Assert.Equal(new ClassDefRange[] { new(3, 4, 1) }, output[i].ClassDef2!.Ranges.ToArray());
            AssertMatrix(output[i], (short)(-20 - i), 1, 16368);
        }
    }

    [Theory]
    [InlineData("work")]
    [InlineData("temporary")]
    [InlineData("output")]
    [InlineData("destination")]
    public void CanonicalPlanningRefusalLeavesDestinationAndSourceMembershipUntouched(string limit)
    {
        ClassDefRange[] ranges = [new(0, 1, 0), new(2, 2, 1), new(3, 3, 2), new(4, 9, 0)];
        var pair = Matrix(Classes([new(1, 1, 1)], 2), Classes(ranges, 1), -1);
        var lookup = Lookup([pair], false);
        var budget = new BuildBudget();
        Ok(LookupTableCompiler.TryPlan(lookup, null, out var plan, out _, budget));
        var limits = limit switch
        {
            "work" => new BuildLimits { MaxWorkUnits = budget.Usage.WorkUnits - 1 },
            "temporary" => new BuildLimits { MaxTemporaryBytes = budget.Usage.TemporaryBytes - 1 },
            "output" => new BuildLimits { MaxOutputBytes = plan!.Length - 1 },
            _ => new BuildLimits(),
        };
        var destination = Enumerable.Repeat((byte)0xA5, plan!.Length - (limit == "destination" ? 1 : 0)).ToArray();

        var status = LookupTableCompiler.TryWrite(lookup, null, destination, out var diagnostic, limits);

        Assert.Equal(limit == "destination" ? ReadStatus.DestinationTooSmall : ReadStatus.ResourceLimit, status);
        Assert.Equal(limit == "destination" ? DiagnosticCode.DestinationTooSmall : DiagnosticCode.BudgetExceeded,
            diagnostic.Code);
        Assert.All(destination, value => Assert.Equal((byte)0xA5, value));
        Assert.Equal((ushort)1, pair.ClassDef2!.Format);
        Assert.Equal(ranges, pair.ClassDef2.Ranges.ToArray());
    }

    private static ClassDefTable Classes(ClassDefRange[] ranges, ushort format, int glyphCount = 32)
    {
        Ok(ClassDefTableCompiler.TryCreate(ranges, glyphCount, format, out var classes, out _, location: Location));
        return Assert.IsType<ClassDefTable>(classes);
    }

    private static PairPosTable Matrix(ClassDefTable first, ClassDefTable second, short marker,
        ushort class1Count = 2, ushort class2Count = 8)
    {
        Ok(CoverageTableCompiler.TryCreate([1], first.NumGlyphs, 1, out var coverage, out _, location: Location));
        var values1 = new StaticValueRecord[class1Count * class2Count];
        var values2 = new StaticValueRecord[values1.Length];
        for (int i = 0; i < values1.Length; i++)
        {
            values1[i] = new(XAdvance: (short)(marker - i));
            values2[i] = new(YAdvance: (short)(i + 10));
        }
        return new(coverage!, 2, 4, 8, classDef1: first, classDef2: second,
            class1Count: class1Count, class2Count: class2Count, values1: values1, values2: values2, location: Location);
    }

    private static void AssertMatrix(PairPosTable model, short marker,
        int expectedClass1Count = 2, int expectedClass2Count = 8)
    {
        Assert.Equal(expectedClass1Count, model.Class1Count);
        Assert.Equal(expectedClass2Count, model.Class2Count);
        for (int first = 0; first < model.Class1Count; first++)
            for (int second = 0; second < model.Class2Count; second++)
            {
                int ordinal = first * model.Class2Count + second;
                Assert.Equal((short)(marker - ordinal), model.GetMatrixValue1(first, second).XAdvance);
                Assert.Equal((short)(ordinal + 10), model.GetMatrixValue2(first, second).YAdvance);
            }
    }

    private static LookupTable Lookup(PairPosTable[] pairs, bool extension)
    {
        int glyphCount = pairs[0].NumGlyphs;
        if (!extension)
        {
            Ok(LookupTableCompiler.TryCreatePairPos(glyphCount, pairs, 0, null, null,
                out var lookup, out _, location: Location));
            return Assert.IsType<LookupTable>(lookup);
        }
        var wrapped = new ExtensionPosTable[pairs.Length];
        for (int i = 0; i < pairs.Length; i++)
        {
            Ok(ExtensionPosTable.TryCreate(2, null, pairs[i], null, out var part, out _, Location));
            wrapped[i] = Assert.IsType<ExtensionPosTable>(part);
        }
        return new(LayoutLookupKind.Gpos, glyphCount, 9, 0, null, [], null, Location,
            extensionPositions: wrapped);
    }

    private static byte[] Compile(LookupTable model)
    {
        Ok(LookupTableCompiler.TryCompile(model, null, out var bytes, out _));
        return Assert.IsType<byte[]>(bytes);
    }

    private static PairPosTable[] ReadPairs(byte[] bytes, int glyphCount)
    {
        Ok(LookupView.TryOpen(bytes, out var view, out _, Location));
        Ok(view.TryRead(glyphCount, LayoutLookupKind.Gpos, null, out var lookup, out _));
        var model = Assert.IsType<LookupTable>(lookup);
        return model.LookupType == 2 ? model.PairPositions!.ToArray()
            : model.ExtensionPositions!.Select(part => part.Pair!).ToArray();
    }

    private static int Subtable(byte[] bytes, int ordinal)
    {
        int target = Word(bytes, 6 + 2 * ordinal);
        return Word(bytes, 0) == 9 ? target + (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(target + 4)) : target;
    }

    private static ushort Word(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at, 2));
    private static void Ok(ReadStatus status) => Assert.Equal(ReadStatus.Done, status);
}
