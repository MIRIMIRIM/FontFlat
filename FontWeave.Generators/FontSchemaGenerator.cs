using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FontWeave.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class FontSchemaGenerator : IIncrementalGenerator
{
    internal static readonly DiagnosticDescriptor Shape = Rule("FWG001", "Invalid schema declaration");
    internal static readonly DiagnosticDescriptor Reference = Rule("FWG002", "Invalid schema name or reference");
    internal static readonly DiagnosticDescriptor Layout = Rule("FWG003", "Invalid wire layout or value");
    internal static readonly DiagnosticDescriptor Cycle = Rule("FWG004", "Cyclic schema expression");
    internal static readonly DiagnosticDescriptor Condition = Rule("FWG005", "Invalid schema condition or dependency path");
    internal static readonly DiagnosticDescriptor Hook = Rule("FWG006", "Invalid computed hook binding");
    private static DiagnosticDescriptor Rule(string id, string title) =>
        new(id, title, "{0}", "FontWeave.Schema", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource("FontWeave.Schema.Attributes.g.cs", SourceText.From(SchemaAttributes.Source, Encoding.UTF8)));
        var parsed = context.SyntaxProvider.ForAttributeWithMetadataName(
            "FontWeave.Schema.BinaryTableAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (input, cancellation) => SchemaParser.Parse(input, cancellation));
        context.RegisterSourceOutput(parsed, static (output, result) =>
        {
            foreach (var diagnostic in result.Diagnostics) output.ReportDiagnostic(diagnostic);
        });
        var schemas = parsed.Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithComparer(TableEquality.Instance).WithTrackingName("ValidatedSchemas");
        var emitted = schemas.Select(static (schema, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            return new EmittedTable(schema.HintName, ViewEmitter.Emit(schema));
        }).WithTrackingName("EmittedTables");
        context.RegisterSourceOutput(emitted, static (output, source) =>
            output.AddSource(source.HintName, SourceText.From(source.Text, Encoding.UTF8)));
    }

    private sealed class TableEquality : IEqualityComparer<TableModel>
    {
        internal static readonly TableEquality Instance = new();
        public bool Equals(TableModel? x, TableModel? y) => x?.Identity == y?.Identity;
        public int GetHashCode(TableModel value) => value.Identity.GetHashCode();
    }
    private sealed class EmittedTable
    {
        internal EmittedTable(string hintName, string text) { HintName = hintName; Text = text; }
        internal string HintName { get; }
        internal string Text { get; }
    }
}
