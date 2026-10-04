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
