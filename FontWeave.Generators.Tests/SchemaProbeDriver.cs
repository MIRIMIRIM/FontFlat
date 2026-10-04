using System.Collections.Immutable;
using System.Runtime.Loader;
using FontWeave.Core;
using FontWeave.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FontWeave.Generators.Tests;

// Real Core source-closure probe driver (core-driver-mode-design §5, option A): the
// production ReadContracts/BuildLimits/StructureScanScope sources compile into the probe
// assembly, so the generated scan family can touch the internal BuildBudget and
// StructureScanScope under same-assembly semantics. Cross-boundary assertions exchange
// strings and numbers only (design R4): probe Core objects are never cast to host types.
internal static class SchemaProbeDriver
{
    // Option A fixed closure: real production sources, fixed per file; a new Core internal
    // must pass the pure-primitive boundary discipline before joining this list (design 3.3).
    private static readonly string[] CoreClosureFiles =
    [
        "FontWeave/Core/ReadContracts.cs", // ReadStatus/DiagnosticCode/DataField/OffsetBasis/DataLocation/FontDiagnostic/ReadWire(internal)
        "FontWeave/Core/BuildLimits.cs",   // BuildLimits/BuildUsage/BuildBudget(internal)
        "FontWeave/Core/StructureScanScope.cs", // StructureScanScope(internal; the A5 facts-slot cache primitive)
    ];

    private static readonly CSharpParseOptions Parse = new(LanguageVersion.CSharp14);

    // Mirrors the FontWeave.csproj ImplicitUsings=enable SDK default set (closure files rely on System).
    private static readonly SyntaxTree GlobalUsings = CSharpSyntaxTree.ParseText("""
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Net.Http;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """, Parse, "GlobalUsings.g.cs");

    // SyntaxTrees are immutable and shared across compilations; one instance also reads as
    // unchanged to the incremental engine.
    private static readonly Lazy<ImmutableArray<SyntaxTree>> CoreClosure = new(() =>
    {
        var root = FindRepoRoot();
        return [.. CoreClosureFiles.Select(f => CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(root, f)), Parse, f))];
    });

    // TPA minus FontWeave.dll, exactly ("FontWeave.Generators" is unaffected and harmless).
    private static MetadataReference[] ReferencesWithoutFontWeave() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Where(p => !string.Equals(Path.GetFileNameWithoutExtension(p), "FontWeave",
            StringComparison.OrdinalIgnoreCase))
        .Select(path => MetadataReference.CreateFromFile(path)).ToArray();

    private static CSharpCompilation InputInternal(params SyntaxTree[] trees) => CSharpCompilation.Create(
        "SchemaContractInternal_" + Guid.NewGuid().ToString("N"),
        [GlobalUsings, .. CoreClosure.Value, .. trees], ReferencesWithoutFontWeave(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable, checkOverflow: true));

    // Repo root: the host already references FontWeave.dll, so its location anchors an
    // upward search and no CWD assumption is made.
    private static string FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(typeof(FontSource).Assembly.Location)!;
        for (var current = dir; current is not null; current = Path.GetDirectoryName(current))
            if (File.Exists(Path.Combine(current, "FontWeave/Core/ReadContracts.cs"))) return current;
        throw new InvalidOperationException(
            $"Core closure not found: searched upward from {dir} for FontWeave/Core/ReadContracts.cs");
    }

    // ---- orchestration glue: the internal mode shares the public Driver/Generate skeleton ----

    internal static SyntaxTree Tree(string source, string path = "ScanContract.cs") =>
        CSharpSyntaxTree.ParseText(source, Parse, path);

    internal static GeneratorDriver Driver() => CSharpGeneratorDriver.Create(
        [new FontSchemaGenerator().AsSourceGenerator()], parseOptions: Parse,
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: true));

    internal static (GeneratorDriver Driver, CSharpCompilation Output) GenerateInternal(
        GeneratorDriver driver, CSharpCompilation input)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out var output, out _);
        Assert.All(driver.GetRunResult().Results, result => Assert.Null(result.Exception));
        return (driver, (CSharpCompilation)output);
    }

    internal static (GeneratorDriver Driver, CSharpCompilation Output) Generate(params SyntaxTree[] trees) =>
        GenerateInternal(Driver(), InputInternal(trees));

    // A fresh internal-mode input compilation the caller can keep across incremental steps.
    internal static CSharpCompilation NewInput(params SyntaxTree[] trees) => InputInternal(trees);

    // Compiles, emits, loads into a collectible context and invokes Probe.Entry.Run.
    internal static string Execute(string source)
    {
        var (driver, output) = Generate(Tree(source));
        Assert.Empty(driver.GetRunResult().Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("schema-contract-internal", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return (string)assembly.GetType("Probe.Entry")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally { context.Unload(); }
    }
}
