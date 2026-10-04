using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using FontWeave.Core;
using FontWeave.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FontWeave.Generators.Tests;

public sealed class SchemaContractsTests
{
    private static readonly CSharpParseOptions Parse = new(LanguageVersion.CSharp14);
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Append(typeof(FontSource).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => MetadataReference.CreateFromFile(path)).ToArray();

    private static CSharpCompilation Input(params SyntaxTree[] trees) => CSharpCompilation.Create(
        "SchemaContract_" + Guid.NewGuid().ToString("N"), trees, References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable, checkOverflow: true));

    private static GeneratorDriver Driver() => CSharpGeneratorDriver.Create(
        [new FontSchemaGenerator().AsSourceGenerator()], parseOptions: Parse,
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: true));

    private static SyntaxTree Tree(string source, string path = "Contract.cs") =>
        CSharpSyntaxTree.ParseText(source, Parse, path);

    private static (GeneratorDriver Driver, Compilation Output) Generate(
        GeneratorDriver driver, CSharpCompilation input)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out var output, out _);
        Assert.All(driver.GetRunResult().Results, result => Assert.Null(result.Exception));
        return (driver, output);
    }

    private static string Execute(string source)
    {
        var (driver, output) = Generate(Driver(), Input(Tree(source)));
        Assert.Empty(driver.GetRunResult().Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("schema-contract", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return (string)assembly.GetType("Probe.Entry")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void EmptyBindingIsDistinctFromDefaultAndRetainsItsOrigin()
    {
        Assert.Equal("Done|True|0|Uninitialized|17|12|1012", Execute("""
            using System;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 0)]
            [BinaryRecord("Word", 2)]
            [BinaryElement("Word", "Value", WireType.Int16, 0, DataField.Payload)]
            public readonly ref partial struct EmptyView {
                public ReadStatus Read(out FontDiagnostic d) => TryReadWordAt(0, 0, out _, out d);
            }
            public static class Entry {
                public static string Run() {
                    var location = DataLocation.Anonymous(0x74657374) with {SourceId=17, Offset=12, TableFileOffset=1000};
                    var status = EmptyView.TryOpen(ReadOnlySpan<byte>.Empty, out var view, out _, location);
                    EmptyView missing = default;
                    var rejected = missing.Read(out _);
                    view.Read(out var diagnostic);
                    return $"{status}|{view.IsBound}|{view.RawBytes.Length}|{rejected}|{diagnostic.Location.SourceId}|{diagnostic.Location.Offset}|{diagnostic.Location.AbsoluteOffset}";
                }
            }
            """));
    }

    [Fact]
    public void FixedScalarWritesUseDeclaredOffsetsTypesAndEndianness()
    {
        Assert.Equal("Done|A53412FFFE80A5|DestinationTooSmall|A5|13", Execute("""
            using System;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 7)]
            [BinaryScalar("Count", WireType.UInt16, 1, DataField.Count, Endian=WireEndian.Little)]
            [BinaryScalar("Delta", WireType.Int16, 3, DataField.Payload)]
            [BinaryScalar("Flag", WireType.Int8, 5, DataField.Flags)]
            public readonly ref partial struct ScalarView { }
            public static class Entry {
                public static string Run() {
                    byte[] bytes = new byte[7]; Array.Fill(bytes, (byte)0xA5);
                    ScalarView.TryWriteCount(bytes, 0x1234, out _);
                    ScalarView.TryWriteDelta(bytes, -2, out _);
                    var status = ScalarView.TryWriteFlag(bytes, -128, out _);
                    byte[] shortBytes = {0xA5};
                    var location = DataLocation.Anonymous(0x74657374) with {Offset=12};
                    var refused = ScalarView.TryWriteCount(shortBytes, 1, out var diagnostic, location);
                    return $"{status}|{Convert.ToHexString(bytes)}|{refused}|{Convert.ToHexString(shortBytes)}|{diagnostic.Location.Offset}";
                }
            }
            """));
    }

    [Fact]
    public void UInt24ScalarAtHighOffsetCompilesAndRefusesAnUnreachableField()
    {
        Assert.Equal("DestinationTooSmall|A5|2147483647|2147483650", Execute("""
            using System;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 0)]
            [BinaryScalar("Value", WireType.UInt24, int.MaxValue, DataField.Payload)]
            public readonly ref partial struct HighOffsetView { }
            public static class Entry {
                public static string Run() {
                    byte[] bytes = {0xA5};
                    var status = HighOffsetView.TryWriteValue(bytes, 0x123456, out var diagnostic);
                    return $"{status}|{Convert.ToHexString(bytes)}|{diagnostic.Location.Offset}|{diagnostic.Expected}";
                }
            }
            """));
    }

    [Fact]
    public void UInt24RecordsReadAndWriteBothEndiansWithoutPartialWrites()
    {
        Assert.Equal("Done|1193046|16702650|4660|A5A5123456BADCFE1234A5A5|InvalidFormat|True|DestinationTooSmall|True", Execute("""
            using System;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 1)]
            [BinaryRecord("Item", 8)]
            [BinaryElement("Item", "Big", WireType.UInt24, 0, DataField.CharCode)]
            [BinaryElement("Item", "Little", WireType.UInt24, 3, DataField.Offset, Endian=WireEndian.Little)]
            [BinaryElement("Item", "Glyph", WireType.UInt16, 6, DataField.GlyphId)]
            public readonly ref partial struct PackedView {
                public ReadStatus Read(out ItemData item, out FontDiagnostic d) => TryReadItemAt(2, 7, out item, out d);
            }
            public static class Entry {
                public static string Run() {
                    byte[] bytes = new byte[12]; Array.Fill(bytes, (byte)0xA5);
                    var row = new PackedView.ItemData(7, 0x123456, 0xFEDCBA, 0x1234);
                    var status = PackedView.TryWriteItemAt(bytes, 2, row, out _);
                    PackedView.TryOpen(bytes, out var view, out _); view.Read(out var read, out _);
                    string expected = Convert.ToHexString(bytes);
                    var invalid = row with {Little=0x1000000};
                    var refusal = PackedView.TryWriteItemAt(bytes, 2, invalid, out _);
                    bool unchanged = expected == Convert.ToHexString(bytes);
                    var shortStatus = PackedView.TryWriteItemAt(bytes.AsSpan(0, 9), 2, row, out _);
                    return $"{status}|{read.Big}|{read.Little}|{read.Glyph}|{expected}|{refusal}|{unchanged}|{shortStatus}|{expected == Convert.ToHexString(bytes)}";
                }
            }
            """));
    }

    [Fact]
    public void GeneratedRawFailuresTranslateTheWindowOriginWithoutOverflow()
    {
        Assert.Equal("Truncated|28|1028|17|Truncated|None||17", Execute("""
            using System;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 1)]
            [BinaryRecord("Item", 3)]
            [BinaryElement("Item", "Value", WireType.UInt24, 0, DataField.CharCode)]
            public readonly ref partial struct RawView {
                public ReadStatus Read(ulong at, out FontDiagnostic d) => TryReadItemAt(at, 7, out _, out d);
            }
            public static class Entry {
                public static string Run() {
                    var location = DataLocation.Anonymous(0x74657374) with {SourceId=17, Offset=12, TableFileOffset=1000};
                    RawView.TryOpen(new byte[]{0}, out var view, out _, location);
                    var status = view.Read(16, out var first);
                    var last = view.Read(ulong.MaxValue, out var overflow);
                    return $"{status}|{first.Location.Offset}|{first.Location.AbsoluteOffset}|{first.Location.SourceId}|{last}|{overflow.Location.Basis}|{overflow.Location.AbsoluteOffset}|{overflow.Location.SourceId}";
                }
            }
            """));
    }

    // The dynamic Driver's parse options, not the test project's LangVersion, parse the probe
    // source: `sink?.Field = bytes[1];` is null-conditional assignment, a C# 14 parser gate.
    // Roslyn reports CS9202 ("feature 'null-conditional assignment' is not available in
    // C# 12.0; please use language version 14.0 or greater") when the input tree is parsed
    // with LanguageVersion.CSharp12, so Emit fails before any generator code runs; only a
    // CSharp14 Driver parse lets the generated table and the probe compile and execute.
    // Gate basis verified against the SDK 10.0.401 compiler: -langversion:12 -> error CS9202,
    // -langversion:14 -> clean compile.
    [Fact]
    public void DriverParsesCSharp14GatedNullConditionalAssignment()
    {
        Assert.Equal("Done|1234|0034", Execute("""
            using System;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2)]
            [BinaryScalar("Tag", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct GatedView { }
            public sealed class Sink { public ushort Field; }
            public static class Entry {
                public static string Run() {
                    byte[] bytes = new byte[2];
                    var status = GatedView.TryWriteTag(bytes, 0x1234, out _);
                    Sink sink = new();
                    sink?.Field = bytes[1];
                    return $"{status}|{Convert.ToHexString(bytes)}|{sink.Field:X4}";
                }
            }
            """));
    }

    [Theory]
    [InlineData(-1, "FWG003")]
    [InlineData(0, "FWG003")]
    public void InvalidMinimumOrVersionDeclarationsAreDiagnosedWithoutGeneratingATable(int minimum, string code)
    {
        string version = minimum == 0 ? ", VersionField=\"Version\", SupportedVersions=new uint[]{1}" : "";
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, {{minimum}}{{version}})]
            [BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
            public readonly ref partial struct InvalidView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == code && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources),
            s => s.HintName == "Probe.InvalidView.g.cs");
    }

    [Fact]
    public void UInt24CannotCrossItsRecordStride()
    {
        var (driver, _) = Generate(Driver(), Input(Tree("""
            using FontWeave.Core; using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 1)]
            [BinaryRecord("Item", 2)]
            [BinaryElement("Item", "Value", WireType.UInt24, 0, DataField.Payload)]
            public readonly ref partial struct InvalidView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG003");
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources),
            s => s.HintName == "Probe.InvalidView.g.cs");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(-1, true)]
    public void UnsupportedContractVersionsAreRejectedWithoutGeneratingATable(int version, bool rejected)
    {
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2, ContractVersion = {{version}})]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Item", "Count", 2, 2)]
            [BinaryElement("Item", "Value", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct GateView { }
            """)));
        var diagnostics = driver.GetRunResult().Diagnostics;
        var sources = driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).ToList();
        if (rejected)
        {
            Assert.Contains(diagnostics, d => d.Id == "FWG001" && d.Location.IsInSource);
            Assert.DoesNotContain(sources, s => s.HintName == "Probe.GateView.g.cs");
        }
        else
        {
            Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            Assert.Contains(sources, s => s.HintName == "Probe.GateView.g.cs");
        }
    }

    // The V0 pin keeps the current cross-partial merge semantics legal so the V1-only
    // single-declaration rule stays an intentional contract difference (card 1.2-b/1.7-4).
    [Fact]
    public void ContractVersionOneRequiresAttributesOnTheSingleTableDeclaration()
    {
        const string split = """
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4{GATE})]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            public readonly ref partial struct SplitView { }

            [BinaryScalar("Extra", WireType.UInt16, 2, DataField.Flags)]
            public readonly ref partial struct SplitView { }
            """;
        var (v0Driver, _) = Generate(Driver(), Input(Tree(split.Replace("{GATE}", ""))));
        Assert.DoesNotContain(v0Driver.GetRunResult().Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(v0Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.SplitView.g.cs");
        var (v1Driver, _) = Generate(Driver(), Input(Tree(split.Replace("{GATE}", ", ContractVersion = 1"))));
        Assert.Contains(v1Driver.GetRunResult().Diagnostics, d => d.Id == "FWG001" && d.Location.IsInSource);
        Assert.DoesNotContain(v1Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.SplitView.g.cs");
    }

    private static SyntaxTree OrderedSchema(string attributes, string path = "Ordered.cs") => Tree($$"""
        using FontWeave.Core;
        using FontWeave.Schema;
        namespace Probe;
        [BinaryTable(0x74657374, 4)]
        {{attributes}}
        public readonly ref partial struct OrderedView { }
        """, path);

    private static SyntaxTree OtherSchema(string path = "Other.cs") => Tree("""
        using FontWeave.Core;
        using FontWeave.Schema;
        namespace Probe;
        [BinaryTable(0x12345678, 2)]
        [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
        public readonly ref partial struct OtherView { }
        """, path);

    [Fact]
    public void ReorderingAttributesReemitsWhileCommentChangesStayCached()
    {
        const string countFirst = """
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryScalar("Extra", WireType.UInt16, 2, DataField.Flags)]
            """;
        const string extraFirst = """
            [BinaryScalar("Extra", WireType.UInt16, 2, DataField.Flags)]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            """;
        const string commented = """
            [BinaryScalar("Extra", WireType.UInt16, 2, DataField.Flags)]
            // provenance comment only; the declared order is unchanged.
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            """;
        var one = OrderedSchema(countFirst);
        var other = OtherSchema();
        var initial = Generate(Driver(), Input(one, other));
        var swappedTree = OrderedSchema(extraFirst);
        var swappedInput = Input(one, other).ReplaceSyntaxTree(one, swappedTree);
        var swapped = Generate(initial.Driver, swappedInput);
        var outputs = swapped.Driver.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs).ToArray();
        Assert.Equal(1, outputs.Count(o => o.Reason == IncrementalStepRunReason.Modified));
        Assert.Contains(outputs, o => o.Reason == IncrementalStepRunReason.Cached);
        var commentedRun = Generate(swapped.Driver, swappedInput.ReplaceSyntaxTree(swappedTree, OrderedSchema(commented)));
        Assert.All(commentedRun.Driver.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs), s => Assert.Equal(IncrementalStepRunReason.Cached, s.Reason));
    }

    private static SyntaxTree TagListSchema(string member, string path = "TagList.cs") => Tree($$"""
        using FontWeave.Core;
        using FontWeave.Schema;
        namespace Probe;
        [BinaryTable(0x74657374, 2, ContractVersion = 1)]
        [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
        [BinaryArray("TagRecord", "Count", 2, 6)]
        [BinaryElement("TagRecord", "Tag", WireType.UInt32, 0, DataField.Tag)]
        [BinaryElement("TagRecord", "Offset", WireType.UInt16, 4, DataField.Offset)]
        public readonly ref partial struct TagListView
        {
            {{member}}
        }
        """, path);

    [Fact]
    public void VersionOneReservesGeneratedArrayAccessorNames()
    {
        const string collide = """
            public global::FontWeave.Core.ReadStatus TryGetTagRecord(int ordinal, out TagRecordData record, out global::FontWeave.Core.FontDiagnostic diagnostic)
            { record = default; diagnostic = default; return global::FontWeave.Core.ReadStatus.Done; }
            """;
        var (conflict, _) = Generate(Driver(), Input(TagListSchema(collide)));
        Assert.Contains(conflict.GetRunResult().Diagnostics, d => d.Id == "FWG002" && d.Location.IsInSource);
        Assert.DoesNotContain(conflict.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.TagListView.g.cs");
        var (clean, output) = Generate(Driver(), Input(TagListSchema("")));
        Assert.DoesNotContain(clean.GetRunResult().Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(clean.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.TagListView.g.cs");
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // V1 local array contracts per spec 5 and card 1.5/2.3: negative ordinal uses the
    // unchecked signed extension, truncation names the first incomplete field, Get
    // publishes the extent without proving it lies inside the source, Require does.
    [Fact]
    public void VersionOneArrayContractsFollowSpecifiedFailureOrder()
    {
        Assert.Equal("Done|65|7," +
            "InvalidIndex|IndexOutOfRange|Count|0|18446744073709551615|1," +
            "Truncated|TruncatedData|Tag|8|11|12," +
            "Truncated|TruncatedData|Offset|12|13|14," +
            "Truncated|TruncatedData|Tag|8|8|12," +
            "Truncated|TruncatedData|Tag|14|8|18," +
            "Done|2|3|20," +
            "Truncated|TruncatedData|Tag|8|8|12," +
            "Done|2|0|2," +
            "Done," +
            "InvalidIndex|IndexOutOfRange|Count|0|0|0," +
            "Truncated|TruncatedData|None|100|2|100," +
            "Truncated|TruncatedData|None|100|2|100," +
            "Truncated|TruncatedData|None|100|2|100", Execute("""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2, ContractVersion = 1)]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Item", "Count", 2, 6)]
            [BinaryElement("Item", "Tag", WireType.UInt32, 0, DataField.Tag)]
            [BinaryElement("Item", "Offset", WireType.UInt16, 4, DataField.Offset)]
            public readonly ref partial struct ArrayView { }
            [BinaryTable(0x74657374, 2, ContractVersion = 1)]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Far", "Count", 100, 2)]
            [BinaryElement("Far", "Value", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct FarView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] one = { 0, 1, 0, 0, 0, 65, 0, 7 };
                    ArrayView.TryOpen(one, out var bound, out _);
                    ReadStatus read = bound.TryGetItem(0, out var record, out _);
                    results.Add($"{read}|{record.Tag}|{record.Offset}");
                    ReadStatus negative = bound.TryGetItem(-1, out _, out var negativeDiagnostic);
                    results.Add(Describe(negative, negativeDiagnostic));
                    byte[] tagCut = { 0, 2, 0, 0, 0, 65, 0, 7, 1, 2, 3 };
                    ArrayView.TryOpen(tagCut, out var tagCutView, out _);
                    results.Add(Describe(tagCutView.TryGetItem(1, out _, out var tagDiagnostic), tagDiagnostic));
                    byte[] offsetCut = { 0, 2, 0, 0, 0, 65, 0, 7, 1, 2, 3, 4, 5 };
                    ArrayView.TryOpen(offsetCut, out var offsetCutView, out _);
                    results.Add(Describe(offsetCutView.TryGetItem(1, out _, out var offsetDiagnostic), offsetDiagnostic));
                    byte[] beyond = { 0, 3, 0, 0, 0, 65, 0, 7 };
                    ArrayView.TryOpen(beyond, out var beyondView, out _);
                    results.Add(Describe(beyondView.TryGetItem(1, out _, out var firstDiagnostic), firstDiagnostic));
                    results.Add(Describe(beyondView.TryGetItem(2, out _, out var secondDiagnostic), secondDiagnostic));
                    ReadStatus extent = beyondView.TryGetItemExtent(out ulong start, out ulong count, out ulong end, out _);
                    results.Add($"{extent}|{start}|{count}|{end}");
                    results.Add(Describe(beyondView.TryRequireItemExtent(out var requireDiagnostic), requireDiagnostic));
                    byte[] zero = { 0, 0 };
                    ArrayView.TryOpen(zero, out var zeroView, out _);
                    ReadStatus zeroExtent = zeroView.TryGetItemExtent(out ulong zeroStart, out ulong zeroCount, out ulong zeroEnd, out _);
                    results.Add($"{zeroExtent}|{zeroStart}|{zeroCount}|{zeroEnd}");
                    results.Add(zeroView.TryRequireItemExtent(out _).ToString());
                    results.Add(Describe(zeroView.TryGetItem(0, out _, out var zeroOrdinal), zeroOrdinal));
                    byte[] shortSource = { 0, 1 };
                    FarView.TryOpen(shortSource, out var farView, out _);
                    ReadStatus farExtent = farView.TryGetFarExtent(out _, out _, out _, out var farDiagnostic);
                    results.Add($"{farExtent}|{farDiagnostic.Code}|{farDiagnostic.Location.Field}|{farDiagnostic.Location.Offset}|{farDiagnostic.Detail}|{farDiagnostic.Expected}");
                    results.Add(Describe(farView.TryGetFar(0, out _, out var farOrdinal), farOrdinal));
                    results.Add(Describe(farView.TryRequireFarExtent(out var farRequire), farRequire));
                    return string.Join(",", results);
                }
            }
            """));
    }

    // Padding-only truncation reports None at the row start with the row end expected.
    [Fact]
    public void VersionOneArrayExtentReportsPaddingOnlyTruncationAtTheRowStart()
    {
        Assert.Equal("Truncated|TruncatedData|None|2|9|10,Truncated|TruncatedData|None|2|9|10", Execute("""
            using System;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2, ContractVersion = 1)]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Item", "Count", 2, 8)]
            [BinaryElement("Item", "Tag", WireType.UInt32, 0, DataField.Tag)]
            [BinaryElement("Item", "Offset", WireType.UInt16, 4, DataField.Offset)]
            public readonly ref partial struct PaddedView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    byte[] bytes = { 0, 1, 0, 0, 0, 65, 0, 7, 9 };
                    PaddedView.TryOpen(bytes, out var view, out _);
                    var getter = view.TryGetItem(0, out _, out var getterDiagnostic);
                    var require = view.TryRequireItemExtent(out var requireDiagnostic);
                    return $"{Describe(getter, getterDiagnostic)},{Describe(require, requireDiagnostic)}";
                }
            }
            """));
    }

    [Fact]
    public void VersionZeroArrayGetterKeepsRowLevelTruncationAndZeroOrdinalDetail()
    {
        Assert.Equal("Done|65|7," +
            "InvalidIndex|IndexOutOfRange|Count|0|0|1," +
            "Truncated|TruncatedData|None|8|11|14," +
            "Truncated|TruncatedData|None|8|13|14," +
            "InvalidIndex|IndexOutOfRange|Count|0|0|0", Execute("""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2)]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Item", "Count", 2, 6)]
            [BinaryElement("Item", "Tag", WireType.UInt32, 0, DataField.Tag)]
            [BinaryElement("Item", "Offset", WireType.UInt16, 4, DataField.Offset)]
            public readonly ref partial struct ArrayView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] one = { 0, 1, 0, 0, 0, 65, 0, 7 };
                    ArrayView.TryOpen(one, out var bound, out _);
                    ReadStatus read = bound.TryGetItem(0, out var record, out _);
                    results.Add($"{read}|{record.Tag}|{record.Offset}");
                    ReadStatus negative = bound.TryGetItem(-1, out _, out var negativeDiagnostic);
                    results.Add(Describe(negative, negativeDiagnostic));
                    byte[] tagCut = { 0, 2, 0, 0, 0, 65, 0, 7, 1, 2, 3 };
                    ArrayView.TryOpen(tagCut, out var tagCutView, out _);
                    results.Add(Describe(tagCutView.TryGetItem(1, out _, out var tagDiagnostic), tagDiagnostic));
                    byte[] offsetCut = { 0, 2, 0, 0, 0, 65, 0, 7, 1, 2, 3, 4, 5 };
                    ArrayView.TryOpen(offsetCut, out var offsetCutView, out _);
                    results.Add(Describe(offsetCutView.TryGetItem(1, out _, out var offsetDiagnostic), offsetDiagnostic));
                    byte[] zero = { 0, 0 };
                    ArrayView.TryOpen(zero, out var zeroView, out _);
                    results.Add(Describe(zeroView.TryGetItem(0, out _, out var zeroOrdinal), zeroOrdinal));
                    return string.Join(",", results);
                }
            }
            """));
    }

    // Fixed-start counted writer per spec 7.1 and card 1.2-g: destination row range,
    // then ordinal versus the plan-provided count, then every narrow value, and only
    // then the declared fields; the destination header count is neither read nor written.
    [Fact]
    public void VersionOneCountedWriterChecksDestinationOrdinalAndValuesInOrder()
    {
        Assert.Equal("A5A5123456A5456789A5A5A5A5A5A5A5A5A5A5A5A5A5|" +
            "InvalidIndex|IndexOutOfRange|Count|0|1|1|" +
            "DestinationTooSmall|A5A5A5A5A5|" +
            "DestinationTooSmall|" +
            "InvalidFormat|A5A5A5A5A5A5A5A5A5A5A5A5|" +
            "InvalidValue|Payload|6|16777216|16777215", Execute("""
            using System;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2, ContractVersion = 1)]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Item", "Count", 2, 10)]
            [BinaryElement("Item", "Tag", WireType.UInt24, 0, DataField.Tag)]
            [BinaryElement("Item", "High", WireType.UInt24, 4, DataField.Payload)]
            public readonly ref partial struct WriteView { }
            public static class Entry
            {
                public static string Run()
                {
                    var row = new WriteView.ItemData(0, 0x123456, 0x456789);
                    byte[] destination = new byte[22];
                    Array.Fill(destination, (byte)0xA5);
                    WriteView.TryWriteItem(destination, 1, row, out _);
                    string after = Convert.ToHexString(destination);
                    var outOfRange = row with { Ordinal = 1 };
                    var ordinalStatus = WriteView.TryWriteItem(destination, 1, outOfRange, out var ordinalDiagnostic);
                    byte[] shortDestination = { 0xA5, 0xA5, 0xA5, 0xA5, 0xA5 };
                    var shortStatus = WriteView.TryWriteItem(shortDestination, 1, row, out _);
                    var bothStatus = WriteView.TryWriteItem(shortDestination, 1, row with { Ordinal = 5 }, out _);
                    byte[] narrowDestination = new byte[12];
                    Array.Fill(narrowDestination, (byte)0xA5);
                    var narrowStatus = WriteView.TryWriteItem(narrowDestination, 1, row with { High = 0x1000000 }, out var narrowDiagnostic);
                    return $"{after}|{ordinalStatus}|{ordinalDiagnostic.Code}|{ordinalDiagnostic.Location.Field}|{ordinalDiagnostic.Location.Offset}|{ordinalDiagnostic.Detail}|{ordinalDiagnostic.Expected}|" +
                        $"{shortStatus}|{Convert.ToHexString(shortDestination)}|{bothStatus}|" +
                        $"{narrowStatus}|{Convert.ToHexString(narrowDestination)}|{narrowDiagnostic.Code}|{narrowDiagnostic.Location.Field}|{narrowDiagnostic.Location.Offset}|{narrowDiagnostic.Detail}|{narrowDiagnostic.Expected}";
                }
            }
            """));
    }

    [Fact]
    public void InvalidSchemasClearStaleOutputAndRecoverOnRoundTrip()
    {
        static SyntaxTree Gate(string version, string member) => Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2, ContractVersion = {{version}})]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("TagRecord", "Count", 2, 6)]
            [BinaryElement("TagRecord", "Tag", WireType.UInt32, 0, DataField.Tag)]
            [BinaryElement("TagRecord", "Offset", WireType.UInt16, 4, DataField.Offset)]
            public readonly ref partial struct GateView
            {
                {{member}}
            }
            """, "Gate.cs");
        const string collide = """
            public global::FontWeave.Core.ReadStatus TryGetTagRecord(int ordinal, out TagRecordData record, out global::FontWeave.Core.FontDiagnostic diagnostic)
            { record = default; diagnostic = default; return global::FontWeave.Core.ReadStatus.Done; }
            """;
        var invalid = Gate("2", "");
        var invalidInput = Input(invalid);
        var (firstDriver, _) = Generate(Driver(), invalidInput);
        Assert.Contains(firstDriver.GetRunResult().Diagnostics, d => d.Id == "FWG001");
        Assert.DoesNotContain(firstDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.GateView.g.cs");
        var valid = Gate("1", "");
        var validInput = invalidInput.ReplaceSyntaxTree(invalid, valid);
        var (secondDriver, secondOutput) = Generate(firstDriver, validInput);
        Assert.DoesNotContain(secondDriver.GetRunResult().Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(secondDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.GateView.g.cs");
        Assert.Empty(secondOutput.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error));
        var collided = Gate("1", collide);
        var collidedInput = validInput.ReplaceSyntaxTree(valid, collided);
        var (thirdDriver, _) = Generate(secondDriver, collidedInput);
        Assert.Contains(thirdDriver.GetRunResult().Diagnostics, d => d.Id == "FWG002");
        Assert.DoesNotContain(thirdDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.GateView.g.cs");
        var (fourthDriver, fourthOutput) = Generate(thirdDriver, collidedInput.ReplaceSyntaxTree(collided, valid));
        Assert.DoesNotContain(fourthDriver.GetRunResult().Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(fourthDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.GateView.g.cs");
        Assert.Empty(fourthOutput.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // The worst-case start/count product must be proven representable at build time so
    // the runtime overflow guards on the emitted array contract stay unreachable.
    [Fact]
    public void UnrepresentableStartCountProductsAreRejectedAtBuildTime()
    {
        var (driver, _) = Generate(Driver(), Input(Tree("""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 8)]
            [BinaryScalar("Count", WireType.UInt32, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryScalar("Anchor", WireType.UInt32, 4, DataField.Offset)]
            [BinaryDerived("Big", Count = "Anchor", Stride = 0xFFFFFFFFu)]
            [BinaryArray("Item", "Count", 0, 3, Start = "Big")]
            [BinaryElement("Item", "Value", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct OverflowView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG003" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.OverflowView.g.cs");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownFontWeaveSchemaAttributesAreRejectedForBothContractVersions(bool versionOne)
    {
        var schema = Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2{{(versionOne ? ", ContractVersion = 1" : "")}})]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Item", "Count", 2, 2)]
            [BinaryElement("Item", "Value", WireType.UInt16, 0, DataField.Payload)]
            [BinaryUnheardOf(7)]
            public readonly ref partial struct UnknownView { }
            """);
        // Declared in FontWeave.Schema by the probe itself, in a separate tree so the
        // attribute binds like a future generator-added declaration would.
        var unheardOf = Tree("""
            namespace FontWeave.Schema
            {
                internal sealed class BinaryUnheardOfAttribute : global::System.Attribute
                {
                    public BinaryUnheardOfAttribute(int value) { }
                }
            }
            """, "UnheardOf.cs");
        var (driver, _) = Generate(Driver(), Input(schema, unheardOf));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG001" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.UnknownView.g.cs");
    }

    [Fact]
    public void EditingOneSchemaDoesNotReemitAnUnrelatedTable()
    {
        static string Schema(string name, int offset) => $$"""
            using FontWeave.Core; using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4)]
            [BinaryScalar("Count", WireType.UInt16, {{offset}}, DataField.Count)]
            public readonly ref partial struct {{name}} { }
            """;
        var one = Tree(Schema("OneView", 0), "One.cs");
        var two = Tree(Schema("TwoView", 0), "Two.cs");
        var input = Input(one, two);
        var initial = Generate(Driver(), input);
        var changed = Generate(initial.Driver, input.ReplaceSyntaxTree(one, Tree(Schema("OneView", 2), "One.cs")));
        var steps = changed.Driver.GetRunResult().Results.Single().TrackedSteps["EmittedTables"];
        var reasons = steps.SelectMany(s => s.Outputs).Select(s => s.Reason).ToArray();
        Assert.Contains(IncrementalStepRunReason.Modified, reasons);
        Assert.Contains(IncrementalStepRunReason.Cached, reasons);
        // Run against an input without previously generated trees.
        var cleanChanged = input.ReplaceSyntaxTree(one, Tree(Schema("OneView", 2), "One.cs"));
        var unrelated = Generate(changed.Driver, cleanChanged.AddSyntaxTrees(Tree("namespace Probe; public class Unrelated { }", "Other.cs")));
        Assert.All(unrelated.Driver.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(s => s.Outputs), s => Assert.Equal(IncrementalStepRunReason.Cached, s.Reason));
    }

    // ---- A2 generator contracts (card 2.4, GC1-GC10) ----

    // GC1: computed basic contract (spec 2.3, card 1.2-a): inputs are evaluated in
    // declared order through their own getters, the hook receives the diagnostic
    // location built from DiagnosticSource+DiagnosticField, hook failures propagate
    // with value 0, and a successful value above Maximum is refused at the same place.
    [Fact]
    public void ComputedContractEvaluatesInputsHookAndMaximumInOrder()
    {
        Assert.Equal("Done|3," +
            "InvalidFormat|InvalidValue|Count|4|3|5," +
            "InvalidFormat|InvalidValue|Count|4|8|5", Execute("""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 6, ContractVersion = 1, VersionField = "Version", SupportedVersions = new uint[] { 7 })]
            [BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
            [BinaryScalar("StartSize", WireType.UInt16, 2, DataField.Count)]
            [BinaryScalar("EndSize", WireType.UInt16, 4, DataField.Count)]
            [BinaryComputed("Delta", "DeltaOf", Inputs = new[] { "StartSize", "EndSize" },
                Maximum = 5ul, Semantic = NumberSemantic.Count, DiagnosticField = DataField.Count, DiagnosticSource = "EndSize")]
            public readonly ref partial struct ComputedView
            {
                private static ReadStatus DeltaOf(ushort startSize, ushort endSize, DataLocation diagnosticLocation,
                    out ulong value, out FontDiagnostic diagnostic)
                {
                    value = 0;
                    if (endSize < startSize)
                    {
                        diagnostic = new FontDiagnostic(DiagnosticCode.InvalidValue, diagnosticLocation, endSize, startSize);
                        return ReadStatus.InvalidFormat;
                    }
                    value = (ulong)(endSize - startSize);
                    diagnostic = default;
                    return ReadStatus.Done;
                }
            }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] ok = { 0, 7, 0, 1, 0, 4 };
                    ComputedView.TryOpen(ok, out var view, out _);
                    ReadStatus done = view.TryGetDelta(out ulong value, out _);
                    results.Add($"{done}|{value}");
                    byte[] backwards = { 0, 7, 0, 5, 0, 3 };
                    ComputedView.TryOpen(backwards, out var falling, out _);
                    results.Add(Describe(falling.TryGetDelta(out _, out var hookDiagnostic), hookDiagnostic));
                    byte[] excessive = { 0, 7, 0, 1, 0, 9 };
                    ComputedView.TryOpen(excessive, out var capped, out _);
                    results.Add(Describe(capped.TryGetDelta(out _, out var maximumDiagnostic), maximumDiagnostic));
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GC2: computed hook binding (spec 2.3, card 1.2-a, FWG006): exactly one private
    // static non-generic method with the frozen parameter shape must bind, otherwise
    // the schema is rejected without emitting the table.
    [Theory]
    [InlineData("missing")]
    [InlineData("instance")]
    [InlineData("public")]
    [InlineData("parameterType")]
    [InlineData("parameterOrder")]
    [InlineData("outType")]
    [InlineData("generic")]
    [InlineData("returnType")]
    public void ComputedHooksMustBindAsPrivateStaticWithTheFrozenSignature(string defect)
    {
        const string good = """
            private static ReadStatus DeltaOf(uint startSize, ushort endSize, DataLocation diagnosticLocation,
                out ulong value, out FontDiagnostic diagnostic)
            { value = 0; diagnostic = default; return ReadStatus.Done; }
            """;
        string hook = defect switch
        {
            "missing" => "",
            "instance" => good.Replace("private static", "private"),
            "public" => good.Replace("private static", "public static"),
            "parameterType" => good.Replace("uint startSize", "ulong startSize"),
            "parameterOrder" => good.Replace("uint startSize, ushort endSize", "ushort endSize, uint startSize"),
            "outType" => good.Replace("out ulong value", "out uint value"),
            "generic" => "private static ReadStatus DeltaOf<T>(uint startSize, ushort endSize, DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic) { value = 0; diagnostic = default; return ReadStatus.Done; }",
            "returnType" => good.Replace("ReadStatus DeltaOf", "int DeltaOf").Replace("return ReadStatus.Done;", "return 0;"),
            _ => good
        };
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 8, ContractVersion = 1)]
            [BinaryScalar("StartSize", WireType.UInt32, 2, DataField.Count)]
            [BinaryScalar("EndSize", WireType.UInt16, 6, DataField.Count)]
            [BinaryComputed("Delta", "DeltaOf", Inputs = new[] { "StartSize", "EndSize" })]
            public readonly ref partial struct HookView
            {
                {{hook}}
            }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG006" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.HookView.g.cs");
    }

    // GC3: Maximum is enforced at the generated boundary (Detail=value, Expected=Maximum)
    // and parses as unsigned: declarations above long.MaxValue must not throw.
    [Fact]
    public void ComputedMaximumIsCheckedAtRuntimeAndParsesAsUnsigned()
    {
        Assert.Equal("InvalidFormat|InvalidValue|Payload|0|6|5,Done|18446744073709551615", Execute("""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2, ContractVersion = 1)]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            [BinaryComputed("Capped", "CappedOf", Maximum = 5ul, Semantic = NumberSemantic.Count, DiagnosticField = DataField.Payload)]
            public readonly ref partial struct CappedView
            {
                private static ReadStatus CappedOf(DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
                { value = 6; diagnostic = default; return ReadStatus.Done; }
            }
            [BinaryTable(0x12345678, 2, ContractVersion = 1)]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            [BinaryComputed("Wide", "WideOf", Maximum = 18446744073709551615ul, Semantic = NumberSemantic.Count, DiagnosticField = DataField.Count)]
            public readonly ref partial struct WideView
            {
                private static ReadStatus WideOf(DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
                { value = 18446744073709551615ul; diagnostic = default; return ReadStatus.Done; }
            }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] bytes = { 0, 1 };
                    CappedView.TryOpen(bytes, out var capped, out _);
                    results.Add(Describe(capped.TryGetCapped(out _, out var cappedDiagnostic), cappedDiagnostic));
                    WideView.TryOpen(bytes, out var wide, out _);
                    ReadStatus done = wide.TryGetWide(out ulong value, out _);
                    results.Add($"{done}|{value}");
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GC4: conditional domains (card 1.2-b, FWG005): WhenVersions must be a subset of
    // the supported versions, requires a version field, and excludes WhenVersion-conditioned
    // count sources.
    [Theory]
    [InlineData("notSubset")]
    [InlineData("withWhenVersion")]
    [InlineData("noVersionField")]
    public void ConditionalArrayDomainsMustMatchTheDeclaredVersionSupport(string defect)
    {
        string table = defect switch
        {
            "notSubset" => "[BinaryTable(0x74657374, 4, ContractVersion = 1, VersionField = \"Version\", SupportedVersions = new uint[] { 1 })]",
            "withWhenVersion" => "[BinaryTable(0x74657374, 4, ContractVersion = 1, VersionField = \"Version\", SupportedVersions = new uint[] { 1, 2 })]",
            _ => "[BinaryTable(0x74657374, 4, ContractVersion = 1)]"
        };
        string count = defect == "withWhenVersion"
            ? "[BinaryScalar(\"Count\", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count, WhenVersion = 1, Otherwise = 3)]"
            : "[BinaryScalar(\"Count\", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]";
        string versions = defect switch
        {
            "notSubset" => "WhenVersions = new uint[] { 2 }",
            "withWhenVersion" => "WhenVersions = new uint[] { 1, 2 }",
            _ => "WhenVersions = new uint[] { 1 }"
        };
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{table}}
            [BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
            {{count}}
            [BinaryArray("Item", "Count", 4, 2, {{versions}})]
            [BinaryElement("Item", "Value", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct ConditionalView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG005" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.ConditionalView.g.cs");
    }

    // GC4 legal form: 0x8000-scale versions are ordinary uint values on both
    // SupportedVersions and WhenVersions, for arrays and records alike.
    [Fact]
    public void ConditionalDomainsAcceptSupportedLargeVersions()
    {
        var (driver, output) = Generate(Driver(), Input(Tree("""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 6, ContractVersion = 1, VersionField = "Version", SupportedVersions = new uint[] { 0x8000u })]
            [BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
            [BinaryScalar("Count", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Item", "Count", 6, 2, WhenVersions = new uint[] { 0x8000u })]
            [BinaryElement("Item", "Value", WireType.UInt16, 0, DataField.Payload)]
            [BinaryRecord("Sidecar", 4, WhenVersions = new uint[] { 0x8000u })]
            [BinaryElement("Sidecar", "Marker", WireType.UInt16, 0, DataField.Flags)]
            public readonly ref partial struct ConditionalView { }
            """)));
        Assert.DoesNotContain(driver.GetRunResult().Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.ConditionalView.g.cs");
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // GC5: conditional activation order (card 1.5): an inactive condition refuses before
    // the ordinal, count and truncation checks with UnsupportedOperation at the version
    // field (Detail=version, Expected=0); unknown versions still report UnsupportedVersion.
    [Fact]
    public void ConditionalActivationRefusesBeforeOrdinalCountAndTruncationChecks()
    {
        Assert.Equal("UnsupportedFormat|UnsupportedOperation|Version|0|2|0," +
            "UnsupportedFormat|UnsupportedOperation|Version|0|2|0," +
            "UnsupportedFormat|UnsupportedVersion|Version|0|9|0," +
            "InvalidIndex|IndexOutOfRange|Count|2|18446744073709551615|1," +
            "Truncated|TruncatedData|Payload|4|5|6", Execute("""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4, ContractVersion = 1, VersionField = "Version", SupportedVersions = new uint[] { 1, 2 })]
            [BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
            [BinaryScalar("Count", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Item", "Count", 4, 2, WhenVersions = new uint[] { 1 })]
            [BinaryElement("Item", "Value", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct ConditionalView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] inactive = { 0, 2, 0, 3, 0, 1, 0, 2 };
                    ConditionalView.TryOpen(inactive, out var view, out _);
                    results.Add(Describe(view.TryGetItem(-1, out _, out var inactiveDiagnostic), inactiveDiagnostic));
                    results.Add(Describe(view.TryGetItemExtent(out _, out _, out _, out var extentDiagnostic), extentDiagnostic));
                    byte[] unknown = { 0, 9, 0, 3, 0, 1, 0, 2 };
                    ConditionalView.TryOpen(unknown, out var stranger, out _);
                    results.Add(Describe(stranger.TryGetItem(0, out _, out var unknownDiagnostic), unknownDiagnostic));
                    byte[] active = { 0, 1, 0, 1, 0, 2 };
                    ConditionalView.TryOpen(active, out var current, out _);
                    results.Add(Describe(current.TryGetItem(-1, out _, out var ordinalDiagnostic), ordinalDiagnostic));
                    byte[] cut = { 0, 1, 0, 1, 0 };
                    ConditionalView.TryOpen(cut, out var truncated, out _);
                    results.Add(Describe(truncated.TryGetItem(0, out _, out var truncationDiagnostic), truncationDiagnostic));
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GC6: computed counts (spec 2.2, card 1.2-e, OQ-5): count diagnostics locate at the
    // computed's DiagnosticSource position with its DiagnosticField; count 0 is a legal
    // empty extent and the ordinal refusal expects 0.
    [Fact]
    public void ComputedCountDiagnosticsLocateAtTheDeclaredDiagnosticSource()
    {
        Assert.Equal("Done|4|0|4," +
            "InvalidIndex|IndexOutOfRange|Count|2|0|0," +
            "InvalidIndex|IndexOutOfRange|Count|2|5|2," +
            "Truncated|TruncatedData|Payload|4|5|6", Execute("""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("Anchor", WireType.UInt16, 2, DataField.Count)]
            [BinaryComputed("RowCount", "RowCountOf", Inputs = new[] { "Anchor" }, Maximum = 4ul,
                Semantic = NumberSemantic.Count, DiagnosticField = DataField.Count, DiagnosticSource = "Anchor")]
            [BinaryArray("Item", "RowCount", 4, 2)]
            [BinaryElement("Item", "Value", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct CountedView
            {
                private static ReadStatus RowCountOf(ushort anchor, DataLocation diagnosticLocation,
                    out ulong value, out FontDiagnostic diagnostic)
                { value = anchor; diagnostic = default; return ReadStatus.Done; }
            }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] empty = { 0, 0, 0, 0 };
                    CountedView.TryOpen(empty, out var hollow, out _);
                    ReadStatus extent = hollow.TryGetItemExtent(out ulong start, out ulong count, out ulong end, out _);
                    results.Add($"{extent}|{start}|{count}|{end}");
                    results.Add(Describe(hollow.TryGetItem(0, out _, out var emptyOrdinal), emptyOrdinal));
                    byte[] two = { 0, 2, 0, 2, 0, 1, 0, 2 };
                    CountedView.TryOpen(two, out var pair, out _);
                    results.Add(Describe(pair.TryGetItem(5, out _, out var ordinalDiagnostic), ordinalDiagnostic));
                    byte[] cut = { 0, 2, 0, 2, 0 };
                    CountedView.TryOpen(cut, out var truncated, out _);
                    results.Add(Describe(truncated.TryRequireItemExtent(out var requireDiagnostic), requireDiagnostic));
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GC7: CountSubtract (spec 2.2, card 1.2-d): the declarative count form is exactly
    // rawCount-CountSubtract, runtime underflow refuses with InvalidFormat/InvalidValue at
    // the raw count, and general affine positions subtract before multiplying.
    [Fact]
    public void DerivedCountSubtractionFollowsTheDeclaredFormAndUnderflowRules()
    {
        Assert.Equal("Done|66|8," +
            "InvalidIndex|IndexOutOfRange|Count|0|4|4," +
            "InvalidFormat|InvalidValue|Count|0|0|1," +
            "Done|18", Execute("""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2, ContractVersion = 1)]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryDerived("Rows", Count = "Count", CountSubtract = 1, Semantic = NumberSemantic.Count)]
            [BinaryArray("Item", "Rows", 2, 6)]
            [BinaryElement("Item", "Tag", WireType.UInt32, 0, DataField.Tag)]
            [BinaryElement("Item", "Offset", WireType.UInt16, 4, DataField.Offset)]
            public readonly ref partial struct SubtractedView { }
            [BinaryTable(0x12345678, 4, ContractVersion = 1)]
            [BinaryScalar("Anchor", WireType.UInt16, 0, DataField.Offset)]
            [BinaryScalar("Count", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryDerived("Span", Base = "Anchor", Count = "Count", CountSubtract = 1, Stride = 4)]
            public readonly ref partial struct AffineView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] rows = { 0, 5, 0, 0, 0, 65, 0, 7, 0, 0, 0, 66, 0, 8, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
                    SubtractedView.TryOpen(rows, out var view, out _);
                    ReadStatus second = view.TryGetItem(1, out var record, out _);
                    results.Add($"{second}|{record.Tag}|{record.Offset}");
                    results.Add(Describe(view.TryGetItem(4, out _, out var ordinalDiagnostic), ordinalDiagnostic));
                    byte[] empty = { 0, 0 };
                    SubtractedView.TryOpen(empty, out var hollow, out _);
                    results.Add(Describe(hollow.TryGetItem(0, out _, out var underflowDiagnostic), underflowDiagnostic));
                    byte[] affine = { 0, 10, 0, 3 };
                    AffineView.TryOpen(affine, out var spanView, out _);
                    ReadStatus span = spanView.TryGetSpan(out ulong value, out _);
                    results.Add($"{span}|{value}");
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GC7 build-time rejections: CountSubtract without Count, a count-form derivation
    // with Base, and a provably constant underflow are all FWG003.
    [Theory]
    [InlineData("noCount")]
    [InlineData("shaped")]
    [InlineData("alwaysUnderflows")]
    public void DerivedCountSubtractionDeclarationsAreShapeChecked(string defect)
    {
        string derived = defect switch
        {
            "noCount" => "[BinaryDerived(\"Rows\", Constant = 2, CountSubtract = 1, Semantic = NumberSemantic.Count)]",
            "shaped" => "[BinaryDerived(\"Rows\", Base = \"Anchor\", Count = \"Count\", CountSubtract = 1, Semantic = NumberSemantic.Count)]",
            _ => "[BinaryDerived(\"Rows\", Count = \"Count\", CountSubtract = 70000, Semantic = NumberSemantic.Count)]"
        };
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("Anchor", WireType.UInt16, 0, DataField.Offset)]
            [BinaryScalar("Count", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
            {{derived}}
            [BinaryArray("Item", "Rows", 4, 2)]
            [BinaryElement("Item", "Value", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct InvalidView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG003" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.InvalidView.g.cs");
    }

    // GC8 chain: computed-to-computed inputs evaluate through their own getters in
    // declaration order with ulong parameters.
    [Fact]
    public void ComputedChainsEvaluateInDeclaredInputOrder()
    {
        Assert.Equal("Done|18,Done|36", Execute("""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2, ContractVersion = 1)]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            [BinaryComputed("Double", "DoubleOf", Inputs = new[] { "Seed" }, Maximum = 1000000ul)]
            [BinaryComputed("Quad", "QuadOf", Inputs = new[] { "Double" }, Maximum = 1000000ul)]
            public readonly ref partial struct ChainView
            {
                private static ReadStatus DoubleOf(ushort seed, DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
                { value = (ulong)seed * 2; diagnostic = default; return ReadStatus.Done; }
                private static ReadStatus QuadOf(ulong doubled, DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
                { value = doubled * 2; diagnostic = default; return ReadStatus.Done; }
            }
            public static class Entry
            {
                public static string Run()
                {
                    byte[] bytes = { 0, 9 };
                    ChainView.TryOpen(bytes, out var view, out _);
                    var results = new List<string>();
                    ReadStatus first = view.TryGetDouble(out ulong doubled, out _);
                    results.Add($"{first}|{doubled}");
                    ReadStatus second = view.TryGetQuad(out ulong quad, out _);
                    results.Add($"{second}|{quad}");
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GC8 graph contracts: computed inputs join the cycle check (FWG004), the
    // DiagnosticSource must be an unconditional fixed-position scalar (FWG005), and
    // unknown inputs or sources are name errors (FWG002).
    [Theory]
    [InlineData("cycle", "FWG004")]
    [InlineData("sourceConditional", "FWG005")]
    [InlineData("sourceComputed", "FWG005")]
    [InlineData("sourceUnknown", "FWG002")]
    [InlineData("inputUnknown", "FWG002")]
    public void ComputedDependencyGraphContractsAreEnforced(string defect, string code)
    {
        // The probes below omit their using directives and namespace on purpose in the
        // defect switch only when quoted separately; every schema must still bind to
        // FontWeave.Schema attributes inside namespace Probe so the generator runs and
        // the emitted hint name stays "Probe.GraphView.g.cs".
        const string Prefix = """
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;

            """;
        string schema = Prefix + (defect switch
        {
            "cycle" => """
                [BinaryTable(0x74657374, 2, ContractVersion = 1)]
                [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
                [BinaryComputed("Alpha", "AlphaOf", Inputs = new[] { "Beta" })]
                [BinaryComputed("Beta", "BetaOf", Inputs = new[] { "Alpha" })]
                public readonly ref partial struct GraphView { }
                """,
            "sourceConditional" => """
                [BinaryTable(0x74657374, 4, ContractVersion = 1, VersionField = "Version", SupportedVersions = new uint[] { 1, 2 })]
                [BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
                [BinaryScalar("Opt", WireType.UInt16, 2, DataField.Flags, WhenVersion = 1, Otherwise = 3)]
                [BinaryComputed("Delta", "DeltaOf", Inputs = new[] { "Version" }, DiagnosticSource = "Opt")]
                public readonly ref partial struct GraphView { }
                """,
            "sourceComputed" => """
                [BinaryTable(0x74657374, 2, ContractVersion = 1)]
                [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
                [BinaryComputed("Alpha", "AlphaOf", Inputs = new[] { "Seed" })]
                [BinaryComputed("Beta", "BetaOf", Inputs = new[] { "Alpha" }, DiagnosticSource = "Alpha")]
                public readonly ref partial struct GraphView { }
                """,
            "sourceUnknown" => """
                [BinaryTable(0x74657374, 2, ContractVersion = 1)]
                [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
                [BinaryComputed("Delta", "DeltaOf", DiagnosticSource = "Missing")]
                public readonly ref partial struct GraphView { }
                """,
            _ => """
                [BinaryTable(0x74657374, 2, ContractVersion = 1)]
                [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
                [BinaryComputed("Delta", "DeltaOf", Inputs = new[] { "Missing" })]
                public readonly ref partial struct GraphView { }
                """
        });
        var (driver, _) = Generate(Driver(), Input(Tree(schema)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == code && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.GraphView.g.cs");
    }

    private const string DeltaHook = """
        private static ReadStatus DeltaOf(ushort startSize, ushort endSize, DataLocation diagnosticLocation,
            out ulong value, out FontDiagnostic diagnostic)
        { value = (ulong)(endSize - startSize); diagnostic = default; return ReadStatus.Done; }
        """;

    private static SyntaxTree ComputedSchema(string attributes, string hook, string path = "Computed.cs") => Tree($$"""
        using FontWeave.Core;
        using FontWeave.Schema;
        namespace Probe;
        [BinaryTable(0x74657374, 6, ContractVersion = 1, VersionField = "Version", SupportedVersions = new uint[] { 1, 2 })]
        [BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
        [BinaryScalar("StartSize", WireType.UInt16, 2, DataField.Count)]
        [BinaryScalar("EndSize", WireType.UInt16, 4, DataField.Count)]
        [BinaryComputed("Delta", "DeltaOf", {{attributes}})]
        public readonly ref partial struct DeltaView
        {
            {{hook}}
        }
        """, path);

    // GC9: identity (card 1.2-h): WhenVersions compare as normalized sets, hook method
    // bodies stay with the C# compiler, and Inputs order, Maximum, DiagnosticField and
    // DiagnosticSource are part of the canonical identity.
    [Fact]
    public void ConditionalAndComputedIdentityNormalizesSetsAndIgnoresHookBodies()
    {
        const string swapped = "Inputs = new[] { \"StartSize\", \"EndSize\" }, WhenVersions = new uint[] { 2, 1 }";
        const string ordered = "Inputs = new[] { \"StartSize\", \"EndSize\" }, WhenVersions = new uint[] { 1, 2 }";
        const string commentedHook = """
            // provenance comment only; the frozen signature is unchanged.
            private static ReadStatus DeltaOf(ushort startSize, ushort endSize, DataLocation diagnosticLocation,
                out ulong value, out FontDiagnostic diagnostic)
            { value = (ulong)(endSize - startSize); diagnostic = default; return ReadStatus.Done; }
            """;
        var one = ComputedSchema(swapped, DeltaHook);
        var initial = Generate(Driver(), Input(one));
        var orderedTree = ComputedSchema(ordered, DeltaHook, "Computed2.cs");
        // Chain replacements on the input compilation (never on a driver output, which
        // already contains generated trees); the driver carries the incremental state.
        var orderedInput = Input(one).ReplaceSyntaxTree(one, orderedTree);
        var (reordered, _) = Generate(initial.Driver, orderedInput);
        Assert.All(reordered.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs), s => Assert.Equal(IncrementalStepRunReason.Cached, s.Reason));
        var commentedTree = ComputedSchema(ordered, commentedHook, "Computed3.cs");
        var (commented, _) = Generate(reordered, orderedInput.ReplaceSyntaxTree(orderedTree, commentedTree));
        Assert.All(commented.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs), s => Assert.Equal(IncrementalStepRunReason.Cached, s.Reason));
    }

    [Theory]
    [InlineData("inputsReordered")]
    [InlineData("maximum")]
    [InlineData("diagnosticField")]
    [InlineData("diagnosticSource")]
    public void ComputedIdentityChangesReemitTheTable(string change)
    {
        const string baseline = "Inputs = new[] { \"StartSize\", \"EndSize\" }";
        string changed = change switch
        {
            "inputsReordered" => "Inputs = new[] { \"EndSize\", \"StartSize\" }",
            "maximum" => baseline + ", Maximum = 9ul",
            "diagnosticField" => baseline + ", Semantic = NumberSemantic.Count, DiagnosticField = DataField.Payload",
            _ => baseline + ", DiagnosticSource = \"StartSize\""
        };
        var one = ComputedSchema(baseline, DeltaHook);
        var initial = Generate(Driver(), Input(one));
        var two = ComputedSchema(changed, DeltaHook, "Computed2.cs");
        var (driver, _) = Generate(initial.Driver, Input(one).ReplaceSyntaxTree(one, two));
        var outputs = driver.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs).ToArray();
        Assert.Equal(1, outputs.Count(o => o.Reason == IncrementalStepRunReason.Modified));
    }

    [Fact]
    public void DerivedCountSubtractChangesReemitTheTable()
    {
        static SyntaxTree Derived(string subtract, string path = "Derived.cs") => Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("Anchor", WireType.UInt16, 0, DataField.Offset)]
            [BinaryScalar("Count", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryDerived("Span", Base = "Anchor", Count = "Count", CountSubtract = {{subtract}}, Stride = 4)]
            public readonly ref partial struct SpanView { }
            """, path);
        var one = Derived("1");
        var initial = Generate(Driver(), Input(one));
        var two = Derived("2", "Derived2.cs");
        var (changed, _) = Generate(initial.Driver, Input(one).ReplaceSyntaxTree(one, two));
        var outputs = changed.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs).ToArray();
        Assert.Equal(1, outputs.Count(o => o.Reason == IncrementalStepRunReason.Modified));
    }

    // GC9: hook signature changes and deletions are rechecked (FWG006), stale output is
    // cleared, and the invalid-to-valid round trip recovers emission.
    [Fact]
    public void ComputedHookSignatureChangesAreRecheckedAndClearStaleOutput()
    {
        const string renamedHook = """
            private static ReadStatus DeltaOf2(ushort startSize, ushort endSize, DataLocation diagnosticLocation,
                out ulong value, out FontDiagnostic diagnostic)
            { value = 0; diagnostic = default; return ReadStatus.Done; }
            """;
        const string inputs = "Inputs = new[] { \"StartSize\", \"EndSize\" }";
        var one = ComputedSchema(inputs, DeltaHook);
        var initial = Generate(Driver(), Input(one));
        var renamed = ComputedSchema(inputs, renamedHook, "Computed2.cs");
        var renamedInput = Input(one).ReplaceSyntaxTree(one, renamed);
        var (renamedDriver, _) = Generate(initial.Driver, renamedInput);
        Assert.Contains(renamedDriver.GetRunResult().Diagnostics, d => d.Id == "FWG006" && d.Location.IsInSource);
        Assert.DoesNotContain(renamedDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.DeltaView.g.cs");
        var deleted = ComputedSchema(inputs, "", "Computed3.cs");
        var deletedInput = renamedInput.ReplaceSyntaxTree(renamed, deleted);
        var (deletedDriver, _) = Generate(renamedDriver, deletedInput);
        Assert.Contains(deletedDriver.GetRunResult().Diagnostics, d => d.Id == "FWG006" && d.Location.IsInSource);
        Assert.DoesNotContain(deletedDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.DeltaView.g.cs");
        var restored = ComputedSchema(inputs, DeltaHook, "Computed4.cs");
        var (restoredDriver, restoredOutput) = Generate(deletedDriver, deletedInput.ReplaceSyntaxTree(deleted, restored));
        Assert.DoesNotContain(restoredDriver.GetRunResult().Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(restoredDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.DeltaView.g.cs");
        Assert.Empty(restoredOutput.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // GC10: V0 stays frozen (card 1.7): new capability declarations on a V0 table are an
    // explicit ContractVersion=1 rejection, not silent hardening.
    [Theory]
    [InlineData("computed")]
    [InlineData("whenVersions")]
    [InlineData("countSubtract")]
    public void VersionZeroRejectsNewCapabilityDeclarationsWithContractVersionOne(string capability)
    {
        string declaration = capability switch
        {
            "computed" => "[BinaryComputed(\"Delta\", \"DeltaOf\", Inputs = new[] { \"StartSize\", \"EndSize\" })]",
            "whenVersions" => "[BinaryArray(\"Item\", \"Count\", 4, 2, WhenVersions = new uint[] { 1 })]",
            _ => "[BinaryDerived(\"Rows\", Count = \"Count\", CountSubtract = 1, Semantic = NumberSemantic.Count)]"
        };
        string element = capability == "whenVersions"
            ? "[BinaryElement(\"Item\", \"Value\", WireType.UInt16, 0, DataField.Payload)]"
            : "";
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 6)]
            [BinaryScalar("StartSize", WireType.UInt16, 0, DataField.Count)]
            [BinaryScalar("EndSize", WireType.UInt16, 2, DataField.Count)]
            [BinaryScalar("Count", WireType.UInt16, 4, DataField.Count, Semantic = NumberSemantic.Count)]
            {{declaration}}
            {{element}}
            public readonly ref partial struct LegacyView
            {
                private static ReadStatus DeltaOf(ushort startSize, ushort endSize, DataLocation diagnosticLocation,
                    out ulong value, out FontDiagnostic diagnostic)
                { value = 0; diagnostic = default; return ReadStatus.Done; }
            }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG001" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.LegacyView.g.cs");
    }

    // ---- A3 generator contracts (card 2.4, GA1-GA12) ----

    private const string LeafSchema = """
        [BinaryTable(0x11111111, 2)]
        [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
        public readonly ref partial struct LeafView { }
        """;

    // GA1 (card 1.2-b, spec 3.4): the scalar opener succeeds through the child, refuses an
    // unbound owner, applies the Required zero policy at the ref field, and keeps the
    // unknown-version gate ahead of the offset field read.
    [Fact]
    public void ReferenceOpenerFollowsTheScalarContractWithVersionAndNullGates()
    {
        Assert.Equal("Done|True|9," +
            "InvalidOffset|False|False|RangeOutsideSource|Offset|2|0|4," +
            "Uninitialized|False|UnboundView|None|0|0|0," +
            "UnsupportedFormat|False|UnsupportedVersion|Version|0|9|0", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            [BinaryTable(0x74657374, 4, ContractVersion = 1, VersionField = "Version", SupportedVersions = new uint[] { 1 })]
            [BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
            [BinaryScalar("LeafAt", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("Leaf", "LeafAt", typeof(LeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required)]
            public readonly ref partial struct ReferrerView { }
            public static class Entry
            {
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] ok = { 0, 1, 0, 4, 0, 9 };
                    ReferrerView.TryOpen(ok, out var view, out _);
                    ReadStatus done = view.TryOpenLeaf(out var child, out var present, out _);
                    results.Add($"{done}|{present}|{child.Seed}");
                    byte[] absent = { 0, 1, 0, 0 };
                    ReferrerView.TryOpen(absent, out var hollow, out _);
                    ReadStatus zero = hollow.TryOpenLeaf(out var none, out var zeroPresent, out var zeroDiagnostic);
                    results.Add($"{zero}|{zeroPresent}|{none.IsBound}|{zeroDiagnostic.Code}|{zeroDiagnostic.Location.Field}|{zeroDiagnostic.Location.Offset}|{zeroDiagnostic.Detail}|{zeroDiagnostic.Expected}");
                    ReferrerView missing = default;
                    ReadStatus unbound = missing.TryOpenLeaf(out _, out var unboundPresent, out var unboundDiagnostic);
                    results.Add($"{unbound}|{unboundPresent}|{unboundDiagnostic.Code}|{unboundDiagnostic.Location.Field}|{unboundDiagnostic.Location.Offset}|{unboundDiagnostic.Detail}|{unboundDiagnostic.Expected}");
                    byte[] stranger = { 0, 9, 0, 4, 0, 9 };
                    ReferrerView.TryOpen(stranger, out var outsider, out _);
                    ReadStatus versioned = outsider.TryOpenLeaf(out _, out var versionPresent, out var versionDiagnostic);
                    results.Add($"{versioned}|{versionPresent}|{versionDiagnostic.Code}|{versionDiagnostic.Location.Field}|{versionDiagnostic.Location.Offset}|{versionDiagnostic.Detail}|{versionDiagnostic.Expected}");
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GA2 (card 1.2-b, spec 3.4/5): the counted-array opener keeps A1 ordinal semantics at
    // the declared count source, and the Minimum=derived storage-before, zero policy,
    // source bounds and target-at-end refusals at the ref field with per-row positions.
    [Fact]
    public void ReferenceArrayOpenerEnforcesWindowOrdinalAndCountGates()
    {
        Assert.Equal("Done|True|9,Done|True|11," +
            "InvalidIndex|IndexOutOfRange|Count|0|18446744073709551615|2," +
            "InvalidIndex|IndexOutOfRange|Count|0|2|2," +
            "InvalidOffset|RangeOutsideSource|Offset|4|0|8," +
            "InvalidOffset|StorageBeforeRecords|Offset|4|4|12," +
            "InvalidOffset|RangeOutsideSource|Offset|4|8|8", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("SetCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryDerived("SetsEnd", Constant = 4, Count = "SetCount", Stride = 4)]
            [BinaryArray("Row", "SetCount", 4, 4)]
            [BinaryElement("Row", "SetAt", WireType.UInt32, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("Set", "SetAt", typeof(LeafView), Array = "Row", Minimum = "SetsEnd")]
            public readonly ref partial struct SetsView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] ok = { 0, 2, 0, 0, 0, 0, 0, 12, 0, 0, 0, 14, 0, 9, 0, 11 };
                    SetsView.TryOpen(ok, out var view, out _);
                    ReadStatus first = view.TryOpenSet(0, out var one, out var onePresent, out _);
                    results.Add($"{first}|{onePresent}|{one.Seed}");
                    ReadStatus second = view.TryOpenSet(1, out var two, out var twoPresent, out _);
                    results.Add($"{second}|{twoPresent}|{two.Seed}");
                    results.Add(Describe(view.TryOpenSet(-1, out _, out _, out var negative), negative));
                    results.Add(Describe(view.TryOpenSet(2, out _, out _, out var excessive), excessive));
                    byte[] zero = { 0, 1, 0, 0, 0, 0, 0, 0 };
                    SetsView.TryOpen(zero, out var hollow, out _);
                    results.Add(Describe(hollow.TryOpenSet(0, out _, out _, out var zeroDiagnostic), zeroDiagnostic));
                    byte[] backwards = { 0, 2, 0, 0, 0, 0, 0, 4, 0, 0, 0, 4 };
                    SetsView.TryOpen(backwards, out var early, out _);
                    results.Add(Describe(early.TryOpenSet(0, out _, out _, out var beforeDiagnostic), beforeDiagnostic));
                    byte[] ending = { 0, 1, 0, 0, 0, 0, 0, 8 };
                    SetsView.TryOpen(ending, out var endingView, out _);
                    results.Add(Describe(endingView.TryOpenSet(0, out _, out _, out var endDiagnostic), endDiagnostic));
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GA2 (spec 3.4): an explicit Length window is bounds-checked at the length field; a
    // length-omitted reference opens the whole suffix; only AllowTargetAtEnd=true with a
    // zero-minimum-header target may bind the empty end window.
    [Fact]
    public void ReferenceWindowHonorsExplicitLengthAndBoundEmpty()
    {
        Assert.Equal("Done|True|6," +
            "Done|True|2," +
            "InvalidOffset|False|False|RangeOutsideSource|Length|6|7|6," +
            "Done|True|0," +
            "InvalidOffset|False|False|RangeOutsideSource|Offset|4|8|8", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x33333333, 0, ContractVersion = 1)]
            public readonly ref partial struct EmptyView { }
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("RowCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Row", "RowCount", 4, 4)]
            [BinaryElement("Row", "At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("End", "At", typeof(EmptyView), Array = "Row")]
            public readonly ref partial struct EndView { }
            [BinaryTable(0x45678912, 4, ContractVersion = 1)]
            [BinaryScalar("RowCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Row", "RowCount", 4, 4)]
            [BinaryElement("Row", "At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryElement("Row", "Span", WireType.UInt16, 2, DataField.Length, Semantic = NumberSemantic.Count)]
            [BinaryReference("Piece", "At", typeof(EmptyView), Array = "Row", Length = "Span", AllowTargetAtEnd = true)]
            public readonly ref partial struct WindowView { }
            public static class Entry
            {
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] suffix = { 0, 1, 0, 0, 0, 2, 0, 0 };
                    EndView.TryOpen(suffix, out var endView, out _);
                    ReadStatus opened = endView.TryOpenEnd(0, out var suffixChild, out var suffixPresent, out _);
                    results.Add($"{opened}|{suffixPresent}|{suffixChild.RawBytes.Length}");
                    byte[] windowed = { 0, 1, 0, 0, 0, 2, 0, 2 };
                    WindowView.TryOpen(windowed, out var windowView, out _);
                    ReadStatus piece = windowView.TryOpenPiece(0, out var pieceChild, out var piecePresent, out _);
                    results.Add($"{piece}|{piecePresent}|{pieceChild.RawBytes.Length}");
                    byte[] overlong = { 0, 1, 0, 0, 0, 2, 0, 7 };
                    WindowView.TryOpen(overlong, out var overlongView, out _);
                    ReadStatus refused = overlongView.TryOpenPiece(0, out _, out var refusedPresent, out var refusedDiagnostic);
                    results.Add($"{refused}|{refusedPresent}|False|{refusedDiagnostic.Code}|{refusedDiagnostic.Location.Field}|{refusedDiagnostic.Location.Offset}|{refusedDiagnostic.Detail}|{refusedDiagnostic.Expected}");
                    byte[] atEnd = { 0, 1, 0, 0, 0, 8, 0, 0 };
                    WindowView.TryOpen(atEnd, out var endBound, out _);
                    ReadStatus bound = endBound.TryOpenPiece(0, out var boundChild, out var boundPresent, out _);
                    results.Add($"{bound}|{boundPresent}|{boundChild.RawBytes.Length}");
                    byte[] unallowed = { 0, 1, 0, 0, 0, 8, 0, 0 };
                    EndView.TryOpen(unallowed, out var strict, out _);
                    ReadStatus strictStatus = strict.TryOpenEnd(0, out _, out _, out var strictDiagnostic);
                    results.Add($"{strictStatus}|False|False|{strictDiagnostic.Code}|{strictDiagnostic.Location.Field}|{strictDiagnostic.Location.Offset}|{strictDiagnostic.Detail}|{strictDiagnostic.Expected}");
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GA3 (V1-02 via card 1.2-b): the three null policies stay separate — Optional absence
    // succeeds without evaluating Minimum, a malformed present child is never swallowed,
    // and ZeroIsAddress opens base+0 like any present target.
    [Fact]
    public void ReferenceNullPoliciesSeparateAbsentMalformedAndAddressStates()
    {
        Assert.Equal("Done|False|0," +
            "Done|True|7," +
            "Truncated|TruncatedData|Payload|2|1|2," +
            "Done|True|0,2", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            [BinaryTable(0x74657374, 2, ContractVersion = 1)]
            [BinaryScalar("Opt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryComputed("Gate", "GateOf", Maximum = 1024ul)]
            [BinaryReference("OptLeaf", "Opt", typeof(LeafView), NullPolicy = ReferenceNullPolicy.Optional, Minimum = "Gate")]
            public readonly ref partial struct OptionalView
            {
                public static int GateCalls;
                private static ReadStatus GateOf(DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
                { GateCalls++; value = 1; diagnostic = default; return ReadStatus.Done; }
            }
            [BinaryTable(0x55555555, 2, ContractVersion = 1)]
            [BinaryScalar("Zia", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("ZiaLeaf", "Zia", typeof(LeafView), NullPolicy = ReferenceNullPolicy.ZeroIsAddress)]
            public readonly ref partial struct ZiaView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] absent = { 0, 0 };
                    OptionalView.TryOpen(absent, out var optional, out _);
                    ReadStatus absentStatus = optional.TryOpenOptLeaf(out _, out var absentPresent, out _);
                    results.Add($"{absentStatus}|{absentPresent}|{OptionalView.GateCalls}");
                    byte[] good = { 0, 2, 0, 7 };
                    OptionalView.TryOpen(good, out var goodView, out _);
                    ReadStatus goodStatus = goodView.TryOpenOptLeaf(out var goodChild, out var goodPresent, out _);
                    results.Add($"{goodStatus}|{goodPresent}|{goodChild.Seed}");
                    byte[] malformed = { 0, 2, 0 };
                    OptionalView.TryOpen(malformed, out var broken, out _);
                    results.Add(Describe(broken.TryOpenOptLeaf(out _, out _, out var brokenDiagnostic), brokenDiagnostic));
                    byte[] zero = { 0, 0, 1, 2 };
                    ZiaView.TryOpen(zero, out var ziaView, out _);
                    ReadStatus ziaStatus = ziaView.TryOpenZiaLeaf(out var ziaChild, out var ziaPresent, out _);
                    results.Add($"{ziaStatus}|{ziaPresent}|{ziaChild.Seed}");
                    results.Add(OptionalView.GateCalls.ToString());
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GA4 (card 1.2-c, spec 3.2): every origin form computes base + encoded exactly once in
    // owner coordinates — the child diagnostic position is owner.AtLocal(target), the
    // record row is never mistaken for the wire parent, and the window is the owner suffix.
    [Fact]
    public void ReferenceOriginsTranslateThePhysicalBaseOnce()
    {
        Assert.Equal("Truncated|TruncatedData|Payload|6|2|4," +
            "Done|True|42," +
            "Truncated|TruncatedData|Payload|12|2|4," +
            "Truncated|TruncatedData|Payload|12|2|4," +
            "Done|True|42", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x11111111, 4, VersionField = "Format", SupportedVersions = new uint[] { 9 })]
            [BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
            [BinaryScalar("Seed", WireType.UInt16, 2, DataField.Payload)]
            public readonly ref partial struct LeafView { }
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("DirectAt", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("Direct", "DirectAt", typeof(LeafView), Origin = ReferenceOrigin.ViewStart)]
            public readonly ref partial struct DirectView { }
            [BinaryTable(0x23456789, 8, ContractVersion = 1)]
            [BinaryScalar("RowCount", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Row", "RowCount", 8, 4)]
            [BinaryElement("Row", "RowAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("RowLeaf", "RowAt", typeof(LeafView), Array = "Row", Origin = ReferenceOrigin.RecordStart)]
            public readonly ref partial struct RowView { }
            [BinaryTable(0x34567890, 12, ContractVersion = 1)]
            [BinaryScalar("Anchor", WireType.UInt16, 0, DataField.Offset)]
            [BinaryScalar("BaseAt", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.NameStorage, ZeroAllowed = false)]
            [BinaryReference("Anchored", "BaseAt", typeof(LeafView), Origin = ReferenceOrigin.NamedBase, Base = "Anchor")]
            public readonly ref partial struct BasedView { }
            [BinaryTable(0x45678901, 12, ContractVersion = 1)]
            [BinaryRecord("Ext", 4)]
            [BinaryElement("Ext", "ExtAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("ExtLeaf", "ExtAt", typeof(LeafView), Array = "Ext", Origin = ReferenceOrigin.RecordStart)]
            public readonly ref partial struct ExtView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] direct = { 0, 0, 0, 4, 0, 3 };
                    DirectView.TryOpen(direct, out var directView, out _);
                    results.Add(Describe(directView.TryOpenDirect(out _, out _, out var directDiagnostic), directDiagnostic));
                    byte[] rowWindow = { 0, 1, 0, 0, 0, 0, 0, 0, 0, 2, 0, 9, 0, 42, 0, 0 };
                    RowView.TryOpen(rowWindow, out var rowView, out _);
                    ReadStatus row = rowView.TryOpenRowLeaf(0, out var rowChild, out var rowPresent, out _);
                    results.Add($"{row}|{rowPresent}|{rowChild.Seed}");
                    byte[] rowPosition = { 0, 1, 0, 0, 0, 0, 0, 0, 0, 2, 0, 3 };
                    RowView.TryOpen(rowPosition, out var pinned, out _);
                    results.Add(Describe(pinned.TryOpenRowLeaf(0, out _, out _, out var pinnedDiagnostic), pinnedDiagnostic));
                    byte[] based = { 0, 6, 0, 4, 0, 0, 0, 0, 0, 0, 0, 3 };
                    BasedView.TryOpen(based, out var basedView, out _);
                    results.Add(Describe(basedView.TryOpenAnchored(out _, out _, out var basedDiagnostic), basedDiagnostic));
                    byte[] external = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 9, 0, 42, 0, 0 };
                    ExtView.TryOpen(external, out var extView, out _);
                    ReadStatus ext = extView.TryOpenExtLeafAt(8, 0, out var extChild, out var extPresent, out _);
                    results.Add($"{ext}|{extPresent}|{extChild.Seed}");
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GA5 (spec 3.4, card 1.2-e): base + encoded*Scale overflow is refused with wide
    // arithmetic (Detail=encoded, Expected=(max-base)/Scale) before any narrow math.
    [Fact]
    public void ReferenceWindowOverflowIsRefusedWithWideArithmetic()
    {
        Assert.Equal("InvalidOffset|RangeOutsideSource|Offset|2|4294967295|2147483648," +
            "InvalidOffset|RangeOutsideSource|Offset|2|9223372045444710398|8", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            [BinaryTable(0x74657374, 8, ContractVersion = 1)]
            [BinaryScalar("WideAt", WireType.UInt32, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.NameStorage, ZeroAllowed = false, Scale = 0xFFFFFFFFu)]
            [BinaryComputed("Big", "BigOf", Maximum = 18446744073709551615ul)]
            [BinaryReference("Wide", "WideAt", typeof(LeafView), Origin = ReferenceOrigin.NamedBase, Base = "Big")]
            public readonly ref partial struct WideView
            {
                private static ReadStatus BigOf(DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
                { value = 9223372036854775808ul; diagnostic = default; return ReadStatus.Done; }
            }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] overflow = { 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0, 9 };
                    WideView.TryOpen(overflow, out var view, out _);
                    results.Add(Describe(view.TryOpenWide(out _, out _, out var overflowDiagnostic), overflowDiagnostic));
                    byte[] outside = { 0, 0, 0, 0, 0, 2, 0, 9 };
                    WideView.TryOpen(outside, out var bounded, out _);
                    results.Add(Describe(bounded.TryOpenWide(out _, out _, out var boundsDiagnostic), boundsDiagnostic));
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GA5 (card 1.2-h): a Minimum/Length pair whose worst-case values overflow UInt64 is
    // refused at build time (FWG003) without emitting the table.
    [Fact]
    public void UnrepresentableReferenceBoundsAreRejectedAtBuildTime()
    {
        var (driver, _) = Generate(Driver(), Input(Tree("""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryComputed("MaxGate", "MaxGateOf", Maximum = 18446744073709551615ul)]
            [BinaryComputed("LenGate", "LenGateOf", Maximum = 18446744073709551615ul)]
            [BinaryReference("Leaf", "At", typeof(LeafView), Minimum = "MaxGate", Length = "LenGate")]
            public readonly ref partial struct WideView { }
            [BinaryTable(0x11111111, 2)]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct LeafView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG003");
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.WideView.g.cs");
    }

    // GA6 (card 1.2-d, spec 7.2): the plan-time fixup helper matrix — absent legality by
    // null policy, target<origin, scale divisibility, narrow-value overflow, the
    // present-delta=0 rule, zeroed outputs on failure, and a pass-through field location
    // that is never shifted a second time.
    [Fact]
    public void ReferenceFixupHelperFollowsThePlanMatrix()
    {
        Assert.Equal("Done|None|0|0|0," +
            "InvalidFormat|InvalidValue|0|1|0," +
            "UnsupportedFormat|UnsupportedOperation|4|10|0," +
            "UnsupportedFormat|UnsupportedOperation|5|2|0," +
            "UnsupportedFormat|UnsupportedOperation|4294967296|4294967295|0," +
            "UnsupportedFormat|UnsupportedOperation|0|1|0," +
            "Done|None|0|0|0," +
            "100|Offset|UnsupportedFormat|UnsupportedOperation|4|10|0", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 2, ContractVersion = 1)]
            [BinaryScalar("Opt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("OptLeaf", "Opt", typeof(LeafView), NullPolicy = ReferenceNullPolicy.Optional)]
            public readonly ref partial struct OptView { }
            [BinaryTable(0x23456789, 2, ContractVersion = 1)]
            [BinaryScalar("ReqAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("ReqLeaf", "ReqAt", typeof(LeafView), NullPolicy = ReferenceNullPolicy.Required)]
            public readonly ref partial struct ReqView { }
            [BinaryTable(0x34567890, 2, ContractVersion = 1)]
            [BinaryScalar("Zia", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("ZiaLeaf", "Zia", typeof(LeafView), NullPolicy = ReferenceNullPolicy.ZeroIsAddress)]
            public readonly ref partial struct ZiaView { }
            [BinaryTable(0x45678901, 4, ContractVersion = 1)]
            [BinaryScalar("SclAt", WireType.UInt32, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false, Scale = 2)]
            [BinaryReference("Scaled", "SclAt", typeof(LeafView), NullPolicy = ReferenceNullPolicy.Required)]
            public readonly ref partial struct SclView { }
            [BinaryTable(0x11111111, 2)]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct LeafView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, FontDiagnostic d, ulong encoded) =>
                    $"{status}|{d.Code}|{d.Detail}|{d.Expected}|{encoded}";
                public static string Run()
                {
                    var results = new List<string>();
                    ReadStatus absent = OptView.TryGetOptLeafOffsetValue(false, 0, 0, out ushort absentEncoded, out var absentDiagnostic);
                    results.Add(Describe(absent, absentDiagnostic, absentEncoded));
                    ReadStatus missing = ReqView.TryGetReqLeafOffsetValue(false, 0, 0, out ushort missingEncoded, out var missingDiagnostic);
                    results.Add(Describe(missing, missingDiagnostic, missingEncoded));
                    ReadStatus backwards = ReqView.TryGetReqLeafOffsetValue(true, 10, 4, out ushort backwardsEncoded, out var backwardsDiagnostic);
                    results.Add(Describe(backwards, backwardsDiagnostic, backwardsEncoded));
                    ReadStatus indivisible = SclView.TryGetScaledOffsetValue(true, 0, 5, out uint indivisibleEncoded, out var indivisibleDiagnostic);
                    results.Add(Describe(indivisible, indivisibleDiagnostic, indivisibleEncoded));
                    ReadStatus narrow = SclView.TryGetScaledOffsetValue(true, 0, 8589934592, out uint narrowEncoded, out var narrowDiagnostic);
                    results.Add(Describe(narrow, narrowDiagnostic, narrowEncoded));
                    ReadStatus stationary = ReqView.TryGetReqLeafOffsetValue(true, 7, 7, out ushort stationaryEncoded, out var stationaryDiagnostic);
                    results.Add(Describe(stationary, stationaryDiagnostic, stationaryEncoded));
                    ReadStatus addressed = ZiaView.TryGetZiaLeafOffsetValue(true, 7, 7, out ushort addressedEncoded, out var addressedDiagnostic);
                    results.Add(Describe(addressed, addressedDiagnostic, addressedEncoded));
                    var location = DataLocation.Anonymous(0x74657374) with { Field = DataField.Offset, Offset = 100 };
                    ReadStatus passed = ReqView.TryGetReqLeafOffsetValue(true, 10, 4, out ushort passedEncoded, out var passedDiagnostic, location);
                    results.Add($"{passedDiagnostic.Location.Offset}|{passedDiagnostic.Location.Field}|{Describe(passed, passedDiagnostic, passedEncoded)}");
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GA7 (card 1.2-f, spec 3.1): an explicit legacy Origin/ZeroAllowed pair consistent with
    // the reference normalizes into one relation — the raw getter stays a raw getter and the
    // opener keeps the zero policy; inconsistent explicit values are FWG005.
    [Fact]
    public void ConsistentLegacyMetadataNormalizesIntoOneRelation()
    {
        Assert.Equal("Done|0|None,InvalidOffset|RangeOutsideSource|Offset|2|0|4", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("Leaf", "At", typeof(LeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required)]
            public readonly ref partial struct ConsistentView { }
            public static class Entry
            {
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] zero = { 0, 0, 0, 0 };
                    ConsistentView.TryOpen(zero, out var view, out _);
                    ReadStatus raw = view.TryGetAt(out ushort encoded, out var rawDiagnostic);
                    results.Add($"{raw}|{encoded}|{rawDiagnostic.Code}");                    results.Add(Describe(view.TryOpenLeaf(out _, out _, out var openerDiagnostic), openerDiagnostic));
                    return string.Join(",", results);
                }
                private static string Describe(ReadStatus status, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}";
            }
            """));
    }

    [Theory]
    [InlineData("originMismatch")]
    [InlineData("zeroMismatch")]
    [InlineData("nameStorageWithoutBase")]
    public void InconsistentLegacyMetadataIsRejected(string defect)
    {
        string field = defect switch
        {
            "originMismatch" => "[BinaryScalar(\"At\", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]",
            "zeroMismatch" => "[BinaryScalar(\"At\", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = true)]",
            _ => "[BinaryScalar(\"At\", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.NameStorage, ZeroAllowed = false)]"
        };
        string reference = defect switch
        {
            "originMismatch" => "[BinaryReference(\"Leaf\", \"At\", typeof(LeafView), Origin = ReferenceOrigin.NamedBase, Base = \"Anchor\")]",
            // Required against an explicit ZeroAllowed=true is the zero-policy mismatch.
            "zeroMismatch" => "[BinaryReference(\"Leaf\", \"At\", typeof(LeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required)]",
            "nameStorageWithoutBase" => "[BinaryReference(\"Leaf\", \"At\", typeof(LeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required)]",
            _ => "[BinaryReference(\"Leaf\", \"At\", typeof(LeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Optional)]"
        };
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("Anchor", WireType.UInt16, 0, DataField.Offset)]
            {{field}}
            {{reference}}
            public readonly ref partial struct LegacyView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG005" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.LegacyView.g.cs");
    }

    // GA8 (card 1.2-a/1.2-g, spec 3.1): targets must be registered BinaryTable schemas of
    // this compilation, reference names are unique, Base/Minimum must name declared values,
    // and a slot cannot carry both a payload and a reference.
    [Theory]
    [InlineData("plainStructTarget", "FWG002")]
    [InlineData("duplicateName", "FWG002")]
    [InlineData("unknownBase", "FWG002")]
    [InlineData("unknownMinimum", "FWG002")]
    [InlineData("payloadConflict", "FWG005")]
    public void RegistryAndSlotContractsAreEnforced(string defect, string code)
    {
        string schema = defect switch
        {
            "plainStructTarget" => """
                [BinaryTable(0x74657374, 4, ContractVersion = 1)]
                [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryReference("Leaf", "At", typeof(NotASchema))]
                public readonly ref partial struct RefView { }
                public readonly struct NotASchema { }
                """,
            "duplicateName" => """
                [BinaryTable(0x74657374, 6, ContractVersion = 1)]
                [BinaryScalar("First", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryScalar("Second", WireType.UInt16, 4, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryReference("Leaf", "First", typeof(LeafView))]
                [BinaryReference("Leaf", "Second", typeof(LeafView))]
                public readonly ref partial struct RefView { }
                """,
            "unknownBase" => """
                [BinaryTable(0x74657374, 4, ContractVersion = 1)]
                [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryReference("Leaf", "At", typeof(LeafView), Origin = ReferenceOrigin.NamedBase, Base = "Missing")]
                public readonly ref partial struct RefView { }
                """,
            "unknownMinimum" => """
                [BinaryTable(0x74657374, 4, ContractVersion = 1)]
                [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryReference("Leaf", "At", typeof(LeafView), Minimum = "Missing")]
                public readonly ref partial struct RefView { }
                """,
            _ => """
                [BinaryTable(0x74657374, 8, ContractVersion = 1)]
                [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
                [BinaryDerived("DirectoryEnd", Constant = 4, Count = "Count", Stride = 4)]
                [BinaryArray("Rec", "Count", 4, 4)]
                [BinaryElement("Rec", "Off", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryElement("Rec", "Len", WireType.UInt16, 2, DataField.Length, Semantic = NumberSemantic.Count)]
                [BinaryPayload("Rec", "Off", "Len", "DirectoryEnd")]
                [BinaryReference("Leaf", "Off", typeof(LeafView), Array = "Rec")]
                public readonly ref partial struct RefView { }
                """
        };
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            {{schema}}
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == code);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.RefView.g.cs");
    }

    // GA8 (card 1.2-a): a V0 target is legal to local-open — the opener only calls the
    // target's TryOpen and never demands scan support.
    [Fact]
    public void VersionZeroTargetSupportsLocalOpen()
    {
        Assert.Equal("Done|True|9", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("Leaf", "At", typeof(LeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required)]
            public readonly ref partial struct RefView { }
            public static class Entry
            {
                public static string Run()
                {
                    byte[] bytes = { 0, 0, 0, 4, 0, 9 };
                    RefView.TryOpen(bytes, out var view, out _);
                    ReadStatus status = view.TryOpenLeaf(out var child, out var present, out _);
                    return $"{status}|{present}|{child.Seed}";
                }
            }
            """));
    }

    // GA9 (card 1.1/1.2-h, FWG003): the offset field shape, the Array/Length scoping rules,
    // scaled base references and the AllowTargetAtEnd x target-minimum-header mismatch are
    // each build errors; the legal counterpart compiles and emits.
    [Theory]
    [InlineData("signedOffset")]
    [InlineData("scaleZero")]
    [InlineData("plainSemanticOffsetField")]
    [InlineData("arrayNotDeclared")]
    [InlineData("recordScalarUsage")]
    [InlineData("lengthCrossScope")]
    [InlineData("baseScaled")]
    [InlineData("allowTargetAtEndMismatch")]
    public void ReferenceDeclarationShapesAreValueChecked(string defect)
    {
        string schema = defect switch
        {
            "signedOffset" => """
                [BinaryTable(0x74657374, 4, ContractVersion = 1)]
                [BinaryScalar("At", WireType.Int16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryReference("Leaf", "At", typeof(LeafView))]
                public readonly ref partial struct ShapeView { }
                """,
            "scaleZero" => """
                [BinaryTable(0x74657374, 4, ContractVersion = 1)]
                [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false, Scale = 0)]
                [BinaryReference("Leaf", "At", typeof(LeafView))]
                public readonly ref partial struct ShapeView { }
                """,
            "plainSemanticOffsetField" => """
                [BinaryTable(0x74657374, 4, ContractVersion = 1)]
                [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset)]
                [BinaryReference("Leaf", "At", typeof(LeafView))]
                public readonly ref partial struct ShapeView { }
                """,
            "arrayNotDeclared" => """
                [BinaryTable(0x74657374, 4, ContractVersion = 1)]
                [BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
                [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryReference("Leaf", "At", typeof(LeafView), Array = "Version")]
                public readonly ref partial struct ShapeView { }
                """,
            "recordScalarUsage" => """
                [BinaryTable(0x74657374, 4, ContractVersion = 1)]
                [BinaryRecord("Ext", 4)]
                [BinaryElement("Ext", "ExtAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryReference("Leaf", "ExtAt", typeof(LeafView))]
                public readonly ref partial struct ShapeView { }
                """,
            "lengthCrossScope" => """
                [BinaryTable(0x74657374, 6, ContractVersion = 1)]
                [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
                [BinaryArray("Row", "Count", 6, 4)]
                [BinaryElement("Row", "RowAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryArray("Other", "Count", 4, 4)]
                [BinaryElement("Other", "OtherLen", WireType.UInt16, 0, DataField.Length, Semantic = NumberSemantic.Count)]
                [BinaryReference("Leaf", "RowAt", typeof(LeafView), Array = "Row", Length = "OtherLen")]
                public readonly ref partial struct ShapeView { }
                """,
            "baseScaled" => """
                [BinaryTable(0x74657374, 6, ContractVersion = 1)]
                [BinaryScalar("Anchor", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false, Scale = 2)]
                [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryReference("Leaf", "At", typeof(LeafView), Origin = ReferenceOrigin.NamedBase, Base = "Anchor")]
                public readonly ref partial struct ShapeView { }
                """,
            _ => """
                [BinaryTable(0x74657374, 4, ContractVersion = 1)]
                [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryReference("Leaf", "At", typeof(LeafView), AllowTargetAtEnd = true)]
                public readonly ref partial struct ShapeView { }
                """
        };
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            {{schema}}
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG003");
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.ShapeView.g.cs");
    }

    // GA9 legal counterpart: Minimum=derived, Length=element and ZeroIsAddress combine into
    // one emitted, compiling contract.
    [Fact]
    public void ReferenceDeclarationShapesCompileInTheLegalForm()
    {
        Assert.Equal("Done|True|7", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryDerived("Floor", Constant = 2)]
            [BinaryArray("Row", "Count", 4, 4)]
            [BinaryElement("Row", "At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryElement("Row", "Span", WireType.UInt16, 2, DataField.Length, Semantic = NumberSemantic.Count)]
            [BinaryReference("Leaf", "At", typeof(LeafView), Array = "Row", Minimum = "Floor", Length = "Span", NullPolicy = ReferenceNullPolicy.ZeroIsAddress)]
            public readonly ref partial struct ShapeView { }
            public static class Entry
            {
                public static string Run()
                {
                    byte[] bytes = { 0, 1, 0, 0, 0, 8, 0, 2, 0, 7 };
                    ShapeView.TryOpen(bytes, out var view, out _);
                    ReadStatus status = view.TryOpenLeaf(0, out var child, out var present, out _);
                    return $"{status}|{present}|{child.Seed}";
                }
            }
            """));
    }

    private static SyntaxTree A3Leaf(string path = "Leaf.cs") => Tree("""
        using FontWeave.Core;
        using FontWeave.Schema;
        namespace Probe;
        [BinaryTable(0x11111111, 2)]
        [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
        public readonly ref partial struct LeafView { }
        """, path);

    private static SyntaxTree A3Referrer(string reference, string path = "Referrer.cs") => Tree($$"""
        using FontWeave.Core;
        using FontWeave.Schema;
        namespace Probe;
        [BinaryTable(0x74657374, 4, ContractVersion = 1)]
        [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
        {{reference}}
        public readonly ref partial struct RefView { }
        """, path);

    private static string[] HintNames(GeneratorDriverRunResult runResult) =>
        runResult.Results.SelectMany(static r => r.TrackedSteps["EmittedTables"])
            .SelectMany(static s => s.Outputs)
            .Select(static o => (Output: ((string, string, string?))o.Value, Reason: o.Reason))
            .Where(static o => o.Output.Item1 == "Probe.RefView.g.cs")
            .Select(static o => $"{o.Reason}").ToArray();

    // GA10 (card 1.2-g, spec 8.2): the target dimension of the incremental contract — a
    // target layout change re-emits the referrer while a comment change stays cached.
    [Fact]
    public void TargetContractChangesReemitTheReferrerWhileCommentsStayCached()
    {
        var leaf = A3Leaf();
        var referrer = A3Referrer("[BinaryReference(\"Leaf\", \"At\", typeof(LeafView))]");
        var firstInput = Input(leaf, referrer);
        var initial = Generate(Driver(), firstInput);
        Assert.Contains(initial.Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RefView.g.cs");
        // A comment inside the target declaration does not change the target contract.
        var commentedLeaf = Tree("""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x11111111, 2)]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct LeafView
            {
                // provenance comment only; the declared contract is unchanged.
            }
            """, "Leaf.cs");
        var commentedInput = firstInput.ReplaceSyntaxTree(leaf, commentedLeaf);
        var commented = Generate(initial.Driver, commentedInput);
        Assert.Equal(["Cached"], HintNames(commented.Driver.GetRunResult()));
        // A target layout change folds into the referrer identity and re-emits it.
        var widerLeaf = Tree("""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x11111111, 2)]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            [BinaryScalar("Extra", WireType.UInt16, 2, DataField.Flags)]
            public readonly ref partial struct LeafView { }
            """, "Leaf.cs");
        var wider = Generate(commented.Driver, commentedInput.ReplaceSyntaxTree(commentedLeaf, widerLeaf));
        Assert.Equal(["Modified"], HintNames(wider.Driver.GetRunResult()));
    }

    [Fact]
    public void RemovedTargetClearsStaleOutputAndRecoversOnRoundTrip()
    {
        var leaf = A3Leaf();
        var referrer = A3Referrer("[BinaryReference(\"Leaf\", \"At\", typeof(LeafView))]");
        var input = Input(leaf, referrer);
        var initial = Generate(Driver(), input);
        Assert.Contains(initial.Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RefView.g.cs");
        // The target stops being a registered schema: the referrer must report FWG002 and
        // drop its stale output.
        var removedLeaf = Tree("""
            namespace Probe;
            public readonly ref partial struct LeafView { }
            """, "Leaf.cs");
        var removedInput = input.ReplaceSyntaxTree(leaf, removedLeaf);
        var removed = Generate(initial.Driver, removedInput);
        Assert.Contains(removed.Driver.GetRunResult().Diagnostics, static d => d.Id == "FWG002");
        Assert.DoesNotContain(removed.Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RefView.g.cs");
        var restoredInput = removedInput.ReplaceSyntaxTree(removedLeaf, leaf);
        var restored = Generate(removed.Driver, restoredInput);
        Assert.DoesNotContain(restored.Driver.GetRunResult().Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(restored.Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RefView.g.cs");
    }

    [Fact]
    public void TargetVersionAndReferenceDeclarationChangesReemitTheReferrer()
    {
        static SyntaxTree ReferrerWithGates(string minimum, string path) => Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryComputed("GateA", "GateOf", Maximum = 1024ul)]
            [BinaryComputed("GateB", "GateOf", Maximum = 2048ul)]
            [BinaryReference("Leaf", "At", typeof(LeafView), Minimum = "{{minimum}}")]
            public readonly ref partial struct RefView
            {
                private static ReadStatus GateOf(DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
                { value = 2; diagnostic = default; return ReadStatus.Done; }
            }
            """, path);
        var leaf = A3Leaf();
        var referrer = ReferrerWithGates("GateA", "Referrer.cs");
        var input = Input(leaf, referrer);
        var initial = Generate(Driver(), input);
        // Flipping the target's ContractVersion changes the target contract: Modified.
        var flippedLeaf = Tree("""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x11111111, 2, ContractVersion = 1)]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct LeafView { }
            """, "Leaf.cs");
        var flipped = Generate(initial.Driver, input.ReplaceSyntaxTree(leaf, flippedLeaf));
        Assert.Equal(["Modified"], HintNames(flipped.Driver.GetRunResult()));
        // Changing a declared reference field (Minimum) re-emits the referrer; the target
        // tree is held at the flipped variant so only the referrer changes.
        var flippedInput = input.ReplaceSyntaxTree(leaf, flippedLeaf);
        var renamedTree = ReferrerWithGates("GateB", "Referrer.cs");
        var renamedInput = flippedInput.ReplaceSyntaxTree(referrer, renamedTree);
        var renamed = Generate(flipped.Driver, renamedInput);
        Assert.Equal(["Modified"], HintNames(renamed.Driver.GetRunResult()));
        // An invalid reference declaration drops the output; fixing it recovers emission.
        var broken = A3Referrer("[BinaryReference(\"Leaf\", \"At\", typeof(LeafView), Minimum = \"Missing\")]", "Referrer.cs");
        var brokenInput = renamedInput.ReplaceSyntaxTree(renamedTree, broken);
        var brokenRun = Generate(renamed.Driver, brokenInput);
        Assert.Contains(brokenRun.Driver.GetRunResult().Diagnostics, static d => d.Id == "FWG002");
        Assert.DoesNotContain(brokenRun.Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RefView.g.cs");
        var restoredRun = Generate(brokenRun.Driver, brokenInput.ReplaceSyntaxTree(broken, ReferrerWithGates("GateB", "Referrer.cs")));
        Assert.DoesNotContain(restoredRun.Driver.GetRunResult().Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(restoredRun.Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RefView.g.cs");
    }

    // GA11 (card 1.2-h/1.7, G11 pin): BinaryReference on a V0 table is an explicit
    // ContractVersion=1 rejection for every declaration shape.
    [Theory]
    [InlineData("scalar")]
    [InlineData("array")]
    public void VersionZeroRejectsBinaryReferenceDeclarations(string shape)
    {
        string reference = shape == "scalar"
            ? "[BinaryReference(\"Leaf\", \"At\", typeof(LeafView))]"
            : """
                [BinaryArray("Row", "Count", 6, 4)]
                [BinaryElement("Row", "RowAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
                [BinaryReference("Leaf", "RowAt", typeof(LeafView), Array = "Row")]
                """;
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            [BinaryTable(0x74657374, 6)]
            [BinaryScalar("Count", WireType.UInt16, 0, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            {{reference}}
            public readonly ref partial struct LegacyView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG001" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.LegacyView.g.cs");
    }

    // GA12 (card 1.2-g, OQ-5): a reference identity cycle A->B->A cannot terminate the
    // canonical identity fold and is refused at build time (FWG004) for both participants.
    [Fact]
    public void ReferenceIdentityCyclesAreRejectedAtBuildTime()
    {
        var (driver, _) = Generate(Driver(), Input(Tree("""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("Peer", "At", typeof(PeerView))]
            public readonly ref partial struct CycleView { }
            [BinaryTable(0x23456789, 4, ContractVersion = 1)]
            [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("Cycle", "At", typeof(CycleView))]
            public readonly ref partial struct PeerView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG004");
        var sources = driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).ToList();
        Assert.DoesNotContain(sources, s => s.HintName == "Probe.CycleView.g.cs");
        Assert.DoesNotContain(sources, s => s.HintName == "Probe.PeerView.g.cs");
    }

    // ---- A4 generator contracts (card 2.4, GA1-GA9) ----

    private const string WireKindEnum = "public enum WireKind : byte { Sub = 1, Pos = 2 }";
    private const string WideKindEnum = "public enum WideKind : long { Wide = 1 }";
    private const string FlagKindEnum = "[global::System.Flags] public enum FlagKind : byte { Flag = 1 }";
    private const string FullRoles = "(long)WireKind.Sub, (long)WireKind.Pos";

    // A role-bearing slot: selector Kind@0, count SetCount@2, counted array Row of two-byte
    // rows holding the offset field SetAt. WhenRoles restricts the branch to Sub.
    private const string RoleSlotFields = """
        [BinaryScalar("Kind", WireType.UInt16, 0, DataField.Tag)]
        [BinaryScalar("SetCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
        [BinaryArray("Row", "SetCount", 6, 2)]
        [BinaryElement("Row", "SetAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
        """;

    private const string SingleBranch = "[BinaryReference(\"Leaf\", \"SetAt\", typeof(LeafView), Array = \"Row\", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = \"Kind\", SelectorValues = new uint[] { 1 })]";

    // The enum stays in the referrer tree, so a role-bearing target and its referrer share
    // one declared WireKind per compilation.
    private static SyntaxTree RoleLeaf(string roles, string path = "RoleLeaf.cs") => Tree($$"""
        using FontWeave.Core;
        using FontWeave.Schema;
        namespace Probe;
        [BinaryTable(0x11111111, 2, ContractVersion = 1, RoleType = typeof(WireKind), SupportedRoles = new long[] { {{roles}} })]
        [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
        public readonly ref partial struct RoleLeafView { }
        """, path);

    private static SyntaxTree RoleReferrer(string tableTail, string references, string path = "RoleReferrer.cs") => Tree($$"""
        using FontWeave.Core;
        using FontWeave.Schema;
        namespace Probe;
        {{WireKindEnum}}
        {{LeafSchema}}
        [BinaryTable(0x74657374, 6, ContractVersion = 1, RoleType = typeof(WireKind){{tableTail}})]
        {{RoleSlotFields}}
        {{references}}
        public readonly ref partial struct RoleView { }
        """, path);

    // GA1 (card 1.2-a/1.2-g, spec 4): RoleType must be an actual non-Flags C# enum with a
    // backing type of at most 32 bits, and SupportedRoles must be a non-empty set of its
    // declared members (FWG003).
    [Theory]
    [InlineData("plainStruct")]
    [InlineData("flagsEnum")]
    [InlineData("longBacking")]
    [InlineData("emptySupported")]
    [InlineData("undeclaredValue")]
    public void RoleTypeDeclarationsAreShapeChecked(string defect)
    {
        string table = defect switch
        {
            "plainStruct" => "ContractVersion = 1, RoleType = typeof(PlainShape), SupportedRoles = new long[] { 1 }",
            "flagsEnum" => "ContractVersion = 1, RoleType = typeof(FlagKind), SupportedRoles = new long[] { 1 }",
            "longBacking" => "ContractVersion = 1, RoleType = typeof(WideKind), SupportedRoles = new long[] { 1 }",
            "emptySupported" => "ContractVersion = 1, RoleType = typeof(WireKind), SupportedRoles = new long[] { }",
            _ => "ContractVersion = 1, RoleType = typeof(WireKind), SupportedRoles = new long[] { 7 }"
        };
        string shapes = defect == "plainStruct" ? "public struct PlainShape { }" : "";
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{WireKindEnum}}
            {{WideKindEnum}}
            {{FlagKindEnum}}
            {{shapes}}
            {{LeafSchema}}
            [BinaryTable(0x74657374, 6, {{table}})]
            {{RoleSlotFields}}
            {{SingleBranch}}
            public readonly ref partial struct RoleView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG003" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    // GA1 (spec 4): a role set or WhenRoles without a declared RoleType has no member
    // domain and is a dispatch contract violation (FWG005).
    [Theory]
    [InlineData("supportedRoles")]
    [InlineData("whenRoles")]
    public void RoleSetsWithoutRoleTypeAreDispatchContractErrors(string shape)
    {
        string table = shape == "supportedRoles"
            ? "[BinaryTable(0x74657374, 6, ContractVersion = 1, SupportedRoles = new long[] { 1 })]"
            : "[BinaryTable(0x74657374, 6, ContractVersion = 1)]";
        string reference = shape == "supportedRoles" ? SingleBranch
            : "[BinaryReference(\"Leaf\", \"SetAt\", typeof(LeafView), Array = \"Row\", WhenRoles = new long[] { 1 })]";
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            {{table}}
            {{RoleSlotFields}}
            {{reference}}
            public readonly ref partial struct RoleView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG005" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    // GA1 legal counterpart: the role-bearing schema with two role-disjoint branches emits.
    [Fact]
    public void RoleDispatchSchemaGeneratesInTheLegalForm()
    {
        var (driver, _) = Generate(Driver(), Input(RoleReferrer(
            $", SupportedRoles = new long[] {{ {FullRoles} }}",
            $$"""
            {{SingleBranch}}
            [BinaryReference("PosLeaf", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Pos })]
            """)));
        Assert.Empty(driver.GetRunResult().Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    // GA2 (card 1.2-g, spec 4): branches of one source slot must not overlap on the
    // version x role x selector intersection (FWG005); empty WhenRoles means the full
    // supported role domain.
    [Theory]
    [InlineData("roleOverlap")]
    [InlineData("roleSelectorCross")]
    [InlineData("selectorOverlapFullRole")]
    public void BranchIntersectionsOfOneSlotAreDispatchErrors(string defect)
    {
        string branches = defect switch
        {
            "roleOverlap" => """
                [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub })]
                [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub })]
                """,
            "roleSelectorCross" => """
                [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = "Kind", SelectorValues = new uint[] { 1 })]
                [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub, (long)WireKind.Pos }, Selector = "Kind", SelectorValues = new uint[] { 1, 2 })]
                """,
            _ => """
                [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", Selector = "Kind", SelectorValues = new uint[] { 1 })]
                [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", Selector = "Kind", SelectorValues = new uint[] { 1, 2 })]
                """
        };
        var (driver, _) = Generate(Driver(), Input(RoleReferrer(
            $", SupportedRoles = new long[] {{ {FullRoles} }}", branches)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG005" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    // GA2 legal counterparts: role-disjoint, selector-disjoint and orthogonal branches.
    [Theory]
    [InlineData("roleDisjoint")]
    [InlineData("selectorDisjoint")]
    [InlineData("orthogonal")]
    public void BranchIntersectionsCompileInTheLegalForms(string form)
    {
        string branches = form switch
        {
            "selectorDisjoint" => """
                [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", Selector = "Kind", SelectorValues = new uint[] { 1 })]
                [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", Selector = "Kind", SelectorValues = new uint[] { 2 })]
                """,
            "orthogonal" => """
                [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = "Kind", SelectorValues = new uint[] { 1 })]
                [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Pos }, Selector = "Kind", SelectorValues = new uint[] { 2 })]
                """,
            _ => """
                [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub })]
                [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Pos })]
                """
        };
        var (driver, _) = Generate(Driver(), Input(RoleReferrer(
            $", SupportedRoles = new long[] {{ {FullRoles} }}", branches)));
        Assert.Empty(driver.GetRunResult().Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    // GA2 (card 1.2-f, spec 8.2/4): condition sets join the identity normalized, so a
    // reordering of SupportedRoles, WhenRoles or SelectorValues stays cached.
    [Fact]
    public void RoleConditionSetsAreNormalizedInIdentity()
    {
        var one = RoleReferrer($", SupportedRoles = new long[] {{ {FullRoles} }}", $$"""
            [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = "Kind", SelectorValues = new uint[] { 1, 2 })]
            [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Pos }, Selector = "Kind", SelectorValues = new uint[] { 3 })]
            """);
        var initial = Generate(Driver(), Input(one));
        var reordered = RoleReferrer($", SupportedRoles = new long[] {{ (long)WireKind.Pos, (long)WireKind.Sub }}", $$"""
            [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = "Kind", SelectorValues = new uint[] { 2, 1 })]
            [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Pos }, Selector = "Kind", SelectorValues = new uint[] { 3 })]
            """, "RoleReferrer2.cs");
        var (driver, _) = Generate(initial.Driver, Input(one).ReplaceSyntaxTree(one, reordered));
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs), s => Assert.Equal(IncrementalStepRunReason.Cached, s.Reason));
    }

    // GA3 (card 1.2-g, spec 4): a selector must resolve to one unconditional unsigned
    // fixed-position scalar of the view (FWG002 for unknown names, FWG005 for every other
    // shape), branches of one slot must share its name, and explicit branches need a
    // non-empty selector value set.
    [Theory]
    [InlineData("unknownName")]
    [InlineData("conditionalScalar")]
    [InlineData("signedScalar")]
    [InlineData("mixedNames")]
    [InlineData("emptyValues")]
    [InlineData("derivedSource")]
    [InlineData("computedSource")]
    public void SelectorDeclarationsAreShapeChecked(string defect)
    {
        string scalars = defect switch
        {
            "mixedNames" => """
                [BinaryScalar("Kind", WireType.UInt16, 0, DataField.Tag)]
                [BinaryScalar("Other", WireType.UInt16, 4, DataField.Tag)]
                [BinaryScalar("SetCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
                """,
            "conditionalScalar" => """
                [BinaryScalar("Kind", WireType.UInt16, 0, DataField.Tag, WhenVersion = 1, Otherwise = 0)]
                [BinaryScalar("SetCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
                [BinaryScalar("Version", WireType.UInt16, 4, DataField.Version)]
                """,
            "signedScalar" => """
                [BinaryScalar("Kind", WireType.Int16, 0, DataField.Tag)]
                [BinaryScalar("SetCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
                """,
            "derivedSource" or "computedSource" => """
                [BinaryScalar("Kind", WireType.UInt16, 0, DataField.Tag)]
                [BinaryScalar("SetCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
                """,
            _ => """
                [BinaryScalar("Kind", WireType.UInt16, 0, DataField.Tag)]
                [BinaryScalar("SetCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
                """
        };
        string derived = defect == "derivedSource" ? "[BinaryDerived(\"SelectorValue\", Constant = 1)]" : "";
        string computed = defect == "computedSource" ? "[BinaryComputed(\"SelectorValue\", \"SelectorOf\", Maximum = 16ul)]" : "";
        string hook = defect == "computedSource"
            ? """
            {
                private static ReadStatus SelectorOf(DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
                { value = 1; diagnostic = default; return ReadStatus.Done; }
            }
            """
            : " { }";
        string versionField = defect == "conditionalScalar"
            ? ", VersionField = \"Version\", SupportedVersions = new uint[] { 1 }" : "";
        (string selector, string values) branch = defect switch
        {
            "unknownName" => ("Missing", "1"),
            "emptyValues" => ("Kind", ""),
            _ => ("Kind", "1")
        };
        string values = branch.values.Length == 0 ? "" : $", SelectorValues = new uint[] {{ {branch.values} }}";
        // Two branches of one slot disagreeing on the selector name are the mixed-names
        // defect; every other shape uses the single branch above.
        string reference = defect == "mixedNames"
            ? """
            [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = "Kind", SelectorValues = new uint[] { 1 })]
            [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Pos }, Selector = "Other", SelectorValues = new uint[] { 2 })]
            """
            : defect == "derivedSource" || defect == "computedSource"
            ? """[BinaryReference("Leaf", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = "SelectorValue", SelectorValues = new uint[] { 1 })]"""
            : $$"""[BinaryReference("Leaf", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = "{{branch.selector}}"{{values}})]""";
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{WireKindEnum}}
            {{LeafSchema}}
            [BinaryTable(0x74657374, 6, ContractVersion = 1, RoleType = typeof(WireKind), SupportedRoles = new long[] { {{FullRoles}} }{{versionField}})]
            {{scalars}}
            [BinaryArray("Row", "SetCount", 6, 2)]
            [BinaryElement("Row", "SetAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            {{derived}}
            {{computed}}
            {{reference}}
            public readonly ref partial struct SelectorView{{hook}}
            """)));
        string expected = defect == "unknownName" ? "FWG002" : "FWG005";
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == expected && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.SelectorView.g.cs");
    }

    // GA4 (card 1.2-d, spec 4): a DefaultRaw branch declares no target, no selector values
    // of its own, Scan=Observe, at most one per slot, and only on a slot with a common
    // selector and at least one explicit branch; the legal form emits no typed opener or
    // fixup for the remainder-set branch.
    [Theory]
    [InlineData("withTarget")]
    [InlineData("nonEmptyValues")]
    [InlineData("missingSelector")]
    [InlineData("noExplicitBranch")]
    [InlineData("scanUnspecified")]
    [InlineData("twoDefaults")]
    public void DefaultRawDeclarationsAreShapeChecked(string defect)
    {
        string raw = defect switch
        {
            "withTarget" => "[BinaryReference(\"Raw\", \"SetAt\", typeof(LeafView), Array = \"Row\", Selector = \"Kind\", DefaultRaw = true, Scan = ReferenceScan.Observe)]",
            "nonEmptyValues" => "[BinaryReference(\"Raw\", \"SetAt\", Array = \"Row\", Selector = \"Kind\", SelectorValues = new uint[] { 1 }, DefaultRaw = true, Scan = ReferenceScan.Observe)]",
            "missingSelector" => "[BinaryReference(\"Raw\", \"SetAt\", Array = \"Row\", DefaultRaw = true, Scan = ReferenceScan.Observe)]",
            "noExplicitBranch" => "[BinaryReference(\"Raw\", \"SetAt\", Array = \"Row\", Selector = \"Kind\", DefaultRaw = true, Scan = ReferenceScan.Observe)]",
            "scanUnspecified" => "[BinaryReference(\"Raw\", \"SetAt\", Array = \"Row\", Selector = \"Kind\", DefaultRaw = true)]",
            _ => """
                [BinaryReference("Raw", "SetAt", Array = "Row", Selector = "Kind", DefaultRaw = true, Scan = ReferenceScan.Observe)]
                [BinaryReference("RawTwo", "SetAt", Array = "Row", Selector = "Kind", DefaultRaw = true, Scan = ReferenceScan.Observe)]
                """
        };
        string explicitBranch = defect == "noExplicitBranch" ? "" : SingleBranch + "\n";
        var (driver, _) = Generate(Driver(), Input(RoleReferrer(
            $", SupportedRoles = new long[] {{ {FullRoles} }}", explicitBranch + raw)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG005" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    // GA4 legal counterpart: the remainder-set registration emits no members of its own;
    // even an overlap with the explicit branch's selector values stays legal (remainder
    // semantics), and a WhenRoles restriction on the raw branch is accepted (OQ-4).
    [Fact]
    public void DefaultRawEmitsNoTypedMembersInTheLegalForm()
    {
        var (driver, _) = Generate(Driver(), Input(RoleReferrer(
            $", SupportedRoles = new long[] {{ {FullRoles} }}", $$"""
            {{SingleBranch}}
            [BinaryReference("Raw", "SetAt", Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = "Kind", DefaultRaw = true, Scan = ReferenceScan.Observe)]
            """)));
        Assert.Empty(driver.GetRunResult().Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        string text = driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources)
            .Single(static s => s.HintName == "Probe.RoleView.g.cs").SourceText!.ToString();
        Assert.Contains("TryOpenLeaf", text);
        Assert.DoesNotContain("TryOpenRaw", text);
        Assert.DoesNotContain("TryGetRawOffsetValue", text);
    }

    // GA5 (card 1.2-c, OQ-1): A4 accepts only Unspecified and Observe; the frozen
    // Follow/OpenOnly/Manual names are refused until the A5 structure-scan slice.
    [Theory]
    [InlineData("ReferenceScan.Follow")]
    [InlineData("ReferenceScan.OpenOnly")]
    [InlineData("ReferenceScan.Manual")]
    public void ScanValuesOutsideTheA4DomainAreRefused(string scan)
    {
        var (driver, _) = Generate(Driver(), Input(RoleReferrer(
            $", SupportedRoles = new long[] {{ {FullRoles} }}",
            $"""[BinaryReference("Leaf", "SetAt", typeof(LeafView), Array = "Row", Scan = {scan})]""")));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG005" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScanValuesInTheA4DomainCompile(bool declared)
    {
        string scan = declared ? ", Scan = ReferenceScan.Observe" : "";
        var (driver, _) = Generate(Driver(), Input(RoleReferrer(
            $", SupportedRoles = new long[] {{ {FullRoles} }}",
            $"""[BinaryReference("Leaf", "SetAt", typeof(LeafView), Array = "Row"{scan})]""")));
        Assert.Empty(driver.GetRunResult().Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    // GA6 (card 1.2-e, spec 4): a role-bearing target demands exactly one of TargetRole or
    // ForwardRole, the value must be a supported target role, ForwardRole demands the same
    // enum on both sides with the source covered values inside the target's support, and a
    // roleless target carries neither (all FWG005, registry combine step).
    [Theory]
    [InlineData("rolelessTargetRole")]
    [InlineData("rolelessTargetForward")]
    [InlineData("missingBoth")]
    [InlineData("bothSet")]
    [InlineData("targetRoleOutside")]
    [InlineData("forwardEnumMismatch")]
    [InlineData("forwardCoverageGap")]
    public void TargetRoleContractsAreDispatchErrors(string defect)
    {
        bool roleTarget = defect is not ("rolelessTargetRole" or "rolelessTargetForward");
        string target = roleTarget ? "typeof(RoleLeafView)" : "typeof(LeafView)";
        string contract = defect switch
        {
            "rolelessTargetRole" => ", TargetRole = 1",
            "rolelessTargetForward" => ", ForwardRole = true",
            "missingBoth" => "",
            "bothSet" => ", TargetRole = (long)WireKind.Sub, ForwardRole = true",
            "targetRoleOutside" => ", TargetRole = 7",
            _ => ", ForwardRole = true"
        };
        string referrerType = defect == "forwardEnumMismatch" ? "typeof(ReferrerKind)" : "typeof(WireKind)";
        string referrerEnum = defect == "forwardEnumMismatch"
            ? WireKindEnum + "\npublic enum ReferrerKind : byte { Sub = 1, Pos = 2 }" : WireKindEnum;
        // The coverage gap narrows the target's support below the referrer's full domain
        // (OQ-3: the source covered values are the whole source SupportedRoles set).
        string targetRoles = defect == "forwardCoverageGap" ? "(long)WireKind.Sub" : FullRoles;
        string reference = $"""[BinaryReference("Leaf", "SetAt", {target}, Array = "Row"{contract})]""";
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{LeafSchema}}
            {{referrerEnum}}
            [BinaryTable(0x11111111, 2, ContractVersion = 1, RoleType = typeof(WireKind), SupportedRoles = new long[] { {{targetRoles}} })]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct RoleLeafView { }
            [BinaryTable(0x74657374, 6, ContractVersion = 1, RoleType = {{referrerType}}, SupportedRoles = new long[] { {{FullRoles}} })]
            {{RoleSlotFields}}
            {{reference}}
            public readonly ref partial struct RoleView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG005");
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    // GA6 legal counterparts: a fixed role inside the target support and a forward whose
    // source support is a subset of the target's both compile and emit.
    [Theory]
    [InlineData("fixed")]
    [InlineData("forward")]
    public void TargetRoleContractsCompileInTheLegalForms(string form)
    {
        string contract = form == "fixed"
            ? ", TargetRole = (long)WireKind.Sub"
            : ", ForwardRole = true";
        string referrerRoles = form == "fixed" ? FullRoles : "(long)WireKind.Sub";
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{WireKindEnum}}
            {{LeafSchema}}
            [BinaryTable(0x11111111, 2, ContractVersion = 1, RoleType = typeof(WireKind), SupportedRoles = new long[] { {{FullRoles}} })]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct RoleLeafView { }
            [BinaryTable(0x74657374, 6, ContractVersion = 1, RoleType = typeof(WireKind), SupportedRoles = new long[] { {{referrerRoles}} })]
            {{RoleSlotFields}}
            [BinaryReference("Leaf", "SetAt", typeof(RoleLeafView), Array = "Row"{{contract}})]
            public readonly ref partial struct RoleView { }
            """)));
        Assert.Empty(driver.GetRunResult().Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    // GA7 (card 1.2-b/1.5, spec 3.4/4): the opener runtime matrix — unknown role,
    // inapplicable role, unknown selector, and the minimum gate all fail with restored out
    // values; applicability and the selector precede the ordinal and zero checks.
    [Fact]
    public void RoleOpenerRuntimeMatrixFollowsTheSpecifiedOrder()
    {
        Assert.Equal("Done|True|9," +
            "UnsupportedFormat|UnsupportedOperation|None|0|9|0|False|False," +
            "UnsupportedFormat|UnsupportedOperation|None|0|2|0|False|False," +
            "UnsupportedFormat|UnsupportedOperation|None|0|2|0|False|False," +
            "UnsupportedFormat|UnsupportedOperation|Tag|0|7|0|False|False," +
            "InvalidOffset|StorageBeforeRecords|Offset|6|4|6|False|False", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{WireKindEnum}}
            {{LeafSchema}}
            [BinaryTable(0x74657374, 6, ContractVersion = 1, RoleType = typeof(WireKind), SupportedRoles = new long[] { {{FullRoles}} })]
            [BinaryScalar("Kind", WireType.UInt16, 0, DataField.Tag)]
            [BinaryScalar("SetCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryScalar("Floor", WireType.UInt16, 4, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryArray("Row", "SetCount", 6, 2)]
            [BinaryElement("Row", "SetAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("Leaf", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = "Kind", SelectorValues = new uint[] { 1 }, Minimum = "Floor")]
            public readonly ref partial struct RoleView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, bool present, LeafView child, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}|{child.IsBound}|{present}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] ok = { 0, 1, 0, 1, 0, 6, 0, 8, 0, 9 };
                    RoleView.TryOpen(ok, out var view, out _);
                    ReadStatus done = view.TryOpenLeaf(WireKind.Sub, 0, out var child, out var present, out _);
                    results.Add($"{done}|{present}|{child.Seed}");
                    byte[] hollow = { 0, 1, 0, 1, 0, 6, 0, 0, 0, 0 };
                    RoleView.TryOpen(hollow, out var empty, out _);
                    results.Add(Describe(empty.TryOpenLeaf((WireKind)9, 0, out var unknown, out var unknownPresent, out var unknownDiagnostic),
                        unknownPresent, unknown, unknownDiagnostic));
                    results.Add(Describe(view.TryOpenLeaf(WireKind.Pos, 0, out var other, out var otherPresent, out var otherDiagnostic),
                        otherPresent, other, otherDiagnostic));
                    results.Add(Describe(view.TryOpenLeaf(WireKind.Pos, 5, out var late, out var latePresent, out var lateDiagnostic),
                        latePresent, late, lateDiagnostic));
                    byte[] stranger = { 0, 7, 0, 1, 0, 6, 0, 8, 0, 9 };
                    RoleView.TryOpen(stranger, out var outsider, out _);
                    results.Add(Describe(outsider.TryOpenLeaf(WireKind.Sub, 0, out var selector, out var selectorPresent, out var selectorDiagnostic),
                        selectorPresent, selector, selectorDiagnostic));
                    byte[] early = { 0, 1, 0, 1, 0, 6, 0, 4, 0, 9 };
                    RoleView.TryOpen(early, out var before, out _);
                    results.Add(Describe(before.TryOpenLeaf(WireKind.Sub, 0, out var minimum, out var minimumPresent, out var minimumDiagnostic),
                        minimumPresent, minimum, minimumDiagnostic));
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GA7 (card 1.2-b, spec 4): a negative role uses the unchecked sign-extended ulong in
    // the Detail, exactly like a negative ordinal; only reachable on a signed-backed enum.
    [Fact]
    public void NegativeRoleDetailIsSignExtended()
    {
        Assert.Equal("Done|True|9," +
            "UnsupportedFormat|UnsupportedOperation|None|0|18446744073709551614|0|False|False", Execute($$"""
            using System;
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            public enum SignedKind : int { Neg = -1 }
            {{LeafSchema}}
            [BinaryTable(0x23456789, 4, ContractVersion = 1, RoleType = typeof(SignedKind), SupportedRoles = new long[] { (long)SignedKind.Neg })]
            [BinaryScalar("LeafAt", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("Leaf", "LeafAt", typeof(LeafView))]
            public readonly ref partial struct SignedView { }
            public static class Entry
            {
                private static string Describe(ReadStatus status, bool present, LeafView child, FontDiagnostic d) =>
                    $"{status}|{d.Code}|{d.Location.Field}|{d.Location.Offset}|{d.Detail}|{d.Expected}|{child.IsBound}|{present}";
                public static string Run()
                {
                    var results = new List<string>();
                    byte[] ok = { 0, 0, 0, 4, 0, 9 };
                    SignedView.TryOpen(ok, out var view, out _);
                    ReadStatus done = view.TryOpenLeaf(SignedKind.Neg, out var child, out var present, out _);
                    results.Add($"{done}|{present}|{child.Seed}");
                    results.Add(Describe(view.TryOpenLeaf((SignedKind)(-2), out var refused, out var refusedPresent, out var diagnostic),
                        refusedPresent, refused, diagnostic));
                    return string.Join(",", results);
                }
            }
            """));
    }

    // GA8 (card 1.2-f, spec 8.2): every role-contract declaration field joins the identity,
    // so changing it re-emits the table. TargetRole/ForwardRole run against the
    // role-bearing target as the reference target.
    [Theory]
    [InlineData("supportedRoles")]
    [InlineData("whenRoles")]
    [InlineData("selectorValues")]
    [InlineData("scan")]
    [InlineData("defaultRaw")]
    [InlineData("targetRole")]
    [InlineData("forwardRole")]
    public void RoleContractChangesReemitTheTable(string change)
    {
        var leaf = RoleLeaf(FullRoles);
        var one = RoleReferrer($", SupportedRoles = new long[] {{ {FullRoles} }}", SingleBranch);
        var plainInput = Input(leaf, one);
        var plainInitial = Generate(Driver(), plainInput);
        Assert.Contains(plainInitial.Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
        var roleAwareOne = RoleReferrer($", SupportedRoles = new long[] {{ {FullRoles} }}", $$"""
            [BinaryReference("Leaf", "SetAt", typeof(RoleLeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = "Kind", SelectorValues = new uint[] { 1 }, TargetRole = (long)WireKind.Sub)]
            """);
        var roleAwareInput = Input(leaf, roleAwareOne);
        var roleAwareInitial = Generate(Driver(), roleAwareInput);
        (string tail, string references) changed = change switch
        {
            "supportedRoles" => (", SupportedRoles = new long[] { (long)WireKind.Sub }", SingleBranch),
            "whenRoles" => ($", SupportedRoles = new long[] {{ {FullRoles} }}",
                "[BinaryReference(\"Leaf\", \"SetAt\", typeof(LeafView), Array = \"Row\", WhenRoles = new long[] { (long)WireKind.Sub, (long)WireKind.Pos }, Selector = \"Kind\", SelectorValues = new uint[] { 1 })]"),
            "selectorValues" => ($", SupportedRoles = new long[] {{ {FullRoles} }}",
                "[BinaryReference(\"Leaf\", \"SetAt\", typeof(LeafView), Array = \"Row\", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = \"Kind\", SelectorValues = new uint[] { 1, 2 })]"),
            "scan" => ($", SupportedRoles = new long[] {{ {FullRoles} }}",
                "[BinaryReference(\"Leaf\", \"SetAt\", typeof(LeafView), Array = \"Row\", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = \"Kind\", SelectorValues = new uint[] { 1 }, Scan = ReferenceScan.Observe)]"),
            "defaultRaw" => ($", SupportedRoles = new long[] {{ {FullRoles} }}",
                SingleBranch + "\n[BinaryReference(\"Raw\", \"SetAt\", Array = \"Row\", Selector = \"Kind\", DefaultRaw = true, Scan = ReferenceScan.Observe)]"),
            "targetRole" => ($", SupportedRoles = new long[] {{ {FullRoles} }}",
                "[BinaryReference(\"Leaf\", \"SetAt\", typeof(RoleLeafView), Array = \"Row\", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = \"Kind\", SelectorValues = new uint[] { 1 }, TargetRole = (long)WireKind.Pos)]"),
            _ => ($", SupportedRoles = new long[] {{ (long)WireKind.Sub }}",
                "[BinaryReference(\"Leaf\", \"SetAt\", typeof(RoleLeafView), Array = \"Row\", WhenRoles = new long[] { (long)WireKind.Sub }, Selector = \"Kind\", SelectorValues = new uint[] { 1 }, ForwardRole = true)]")
        };
        var changedTree = RoleReferrer(changed.tail, changed.references, "RoleReferrer2.cs");
        var (driver, _) = change is "targetRole" or "forwardRole"
            ? Generate(roleAwareInitial.Driver, roleAwareInput.ReplaceSyntaxTree(roleAwareOne, changedTree))
            : Generate(plainInitial.Driver, plainInput.ReplaceSyntaxTree(one, changedTree));
        var outputs = driver.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs).ToArray();
        Assert.Equal(1, outputs.Count(o => o.Reason == IncrementalStepRunReason.Modified));
    }

    // GA8 (card 1.2-f, spec 8.2): the target's role contract rides the A3 registry
    // summary — a comment stays cached while SupportedRoles, the RoleType spelling and the
    // enum member set each re-emit the referrer.
    [Fact]
    public void TargetRoleContractChangesReemitTheReferrer()
    {
        var leaf = RoleLeaf(FullRoles);
        var referrer = RoleReferrer($", SupportedRoles = new long[] {{ {FullRoles} }}",
            "[BinaryReference(\"Leaf\", \"SetAt\", typeof(RoleLeafView), Array = \"Row\", TargetRole = (long)WireKind.Sub)]");
        var input = Input(leaf, referrer);
        var initial = Generate(Driver(), input);
        Assert.Contains(initial.Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
        var commentedLeaf = Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x11111111, 2, ContractVersion = 1, RoleType = typeof(WireKind), SupportedRoles = new long[] { {{FullRoles}} })]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct RoleLeafView
            {
                // provenance comment only; the declared contract is unchanged.
            }
            """, "RoleLeaf.cs");
        var commentedInput = input.ReplaceSyntaxTree(leaf, commentedLeaf);
        var commented = Generate(initial.Driver, commentedInput);
        Assert.All(commented.Driver.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs), s => Assert.Equal(IncrementalStepRunReason.Cached, s.Reason));
        var narrowerLeaf = Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x11111111, 2, ContractVersion = 1, RoleType = typeof(WireKind), SupportedRoles = new long[] { (long)WireKind.Sub })]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct RoleLeafView { }
            """, "RoleLeaf.cs");
        var narrowedInput = commentedInput.ReplaceSyntaxTree(commentedLeaf, narrowerLeaf);
        var narrowed = Generate(commented.Driver, narrowedInput);
        Assert.Equal(["Modified"], NarrowedReasons(narrowed.Driver.GetRunResult()));
        var renamedLeaf = Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            public enum RenamedKind : byte { Sub = 1, Pos = 2 }
            [BinaryTable(0x11111111, 2, ContractVersion = 1, RoleType = typeof(RenamedKind), SupportedRoles = new long[] { (long)RenamedKind.Sub })]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct RoleLeafView { }
            """, "RoleLeaf.cs");
        var renamedInput = narrowedInput.ReplaceSyntaxTree(narrowerLeaf, renamedLeaf);
        var renamed = Generate(narrowed.Driver, renamedInput);
        Assert.Equal(["Modified"], NarrowedReasons(renamed.Driver.GetRunResult()));
        var extendedLeaf = Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            public enum RenamedKind : byte { Sub = 1, Pos = 2, Extra = 3 }
            [BinaryTable(0x11111111, 2, ContractVersion = 1, RoleType = typeof(RenamedKind), SupportedRoles = new long[] { (long)RenamedKind.Sub })]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct RoleLeafView { }
            """, "RoleLeaf.cs");
        var extendedInput = renamedInput.ReplaceSyntaxTree(renamedLeaf, extendedLeaf);
        var extended = Generate(renamed.Driver, extendedInput);
        Assert.Equal(["Modified"], NarrowedReasons(extended.Driver.GetRunResult()));
    }

    private static string[] NarrowedReasons(GeneratorDriverRunResult runResult) =>
        runResult.Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs)
            .Select(static o => (Output: ((string, string, string?))o.Value, Reason: o.Reason))
            .Where(static o => o.Output.Item1 == "Probe.RoleView.g.cs")
            .Select(static o => $"{o.Reason}").ToArray();

    // GA8 (card 1.2-f): an invalid dispatch contract drops the output and the restored
    // declaration recovers emission on the round trip.
    [Fact]
    public void InvalidRoleContractRecoversOnRoundTrip()
    {
        var leaf = RoleLeaf(FullRoles);
        var brokenSchema = RoleReferrer($", SupportedRoles = new long[] {{ {FullRoles} }}", $$"""
            [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub })]
            [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub })]
            """);
        var input = Input(leaf, brokenSchema);
        var broken = Generate(Driver(), input);
        Assert.Contains(broken.Driver.GetRunResult().Diagnostics, static d => d.Id == "FWG005");
        Assert.DoesNotContain(broken.Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
        var restored = RoleReferrer($", SupportedRoles = new long[] {{ {FullRoles} }}", $$"""
            [BinaryReference("First", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Sub })]
            [BinaryReference("Second", "SetAt", typeof(LeafView), Array = "Row", WhenRoles = new long[] { (long)WireKind.Pos })]
            """, "RoleReferrer2.cs");
        var recovered = Generate(broken.Driver, input.ReplaceSyntaxTree(brokenSchema, restored));
        Assert.DoesNotContain(recovered.Driver.GetRunResult().Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(recovered.Driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.RoleView.g.cs");
    }

    // GA9 (card 1.7, G9 pin): every A4 capability attribute is V1-only; a V0 declaration
    // is the explicit ContractVersion=1 rejection, never silent reinterpretation.
    [Theory]
    [InlineData("roleType")]
    [InlineData("supportedRoles")]
    [InlineData("referenceScan")]
    public void VersionZeroRejectsRoleCapabilityDeclarations(string capability)
    {
        string table = capability switch
        {
            "roleType" => "[BinaryTable(0x74657374, 6, RoleType = typeof(WireKind))]",
            "supportedRoles" => "[BinaryTable(0x74657374, 6, SupportedRoles = new long[] { 1 })]",
            _ => "[BinaryTable(0x74657374, 6)]"
        };
        string reference = capability == "referenceScan"
            ? "[BinaryReference(\"Leaf\", \"SetAt\", typeof(LeafView), Array = \"Row\", Scan = ReferenceScan.Observe)]"
            : "";
        var (driver, _) = Generate(Driver(), Input(Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{WireKindEnum}}
            {{LeafSchema}}
            {{table}}
            [BinaryScalar("SetCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Row", "SetCount", 6, 2)]
            [BinaryElement("Row", "SetAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            {{reference}}
            public readonly ref partial struct LegacyView { }
            """)));
        Assert.Contains(driver.GetRunResult().Diagnostics, d => d.Id == "FWG001" && d.Location.IsInSource);
        Assert.DoesNotContain(driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.LegacyView.g.cs");
    }
}
