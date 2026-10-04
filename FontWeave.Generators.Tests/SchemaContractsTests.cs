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
}
