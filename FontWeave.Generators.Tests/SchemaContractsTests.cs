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
}
