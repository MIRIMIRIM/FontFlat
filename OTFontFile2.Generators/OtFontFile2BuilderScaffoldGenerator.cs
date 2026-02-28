using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace OTFontFile2.Generators;

[Generator]
public sealed class OtFontFile2BuilderScaffoldGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "OTFontFile2.SourceGen.OtTableBuilderAttribute";
    private const string StreamingLengthMethodName = "ComputeLength";
    private const string StreamingChecksumMethodName = "ComputeDirectoryChecksum";
    private const string StreamingWriteMethodName = "WriteTable";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var builders = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetBuilderInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(builders, static (spc, info) => Execute(spc, info));
    }

    private static void Execute(SourceProductionContext context, BuilderInfo info)
    {
        if (!info.IsPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: "OTFF2SG100",
                    title: "Builder type must be partial",
                    messageFormat: "Type '{0}' must be partial to use [OtTableBuilder].",
                    category: "OTFontFile2.Generators",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                Location.None,
                info.HintName));
            return;
        }

        for (int i = 0; i < info.ContainingTypes.Length; i++)
        {
            if (!info.ContainingTypes[i].IsPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "OTFF2SG101",
                        title: "Containing types must be partial",
                        messageFormat: "Type '{0}' has a non-partial containing type '{1}'.",
                        category: "OTFontFile2.Generators",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    info.HintName,
                    info.ContainingTypes[i].Name));
                return;
            }
        }

        string tag = info.Tag ?? string.Empty;
        if (tag.Length != 4)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: "OTFF2SG102",
                    title: "Invalid tag",
                    messageFormat: "[OtTableBuilder] tag must be 4 characters. Type '{0}' tag was '{1}'.",
                    category: "OTFontFile2.Generators",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                Location.None,
                info.HintName,
                tag));
            return;
        }

        bool isHead = string.Equals(tag, "head", StringComparison.Ordinal);
        if (info.Mode == BuilderMode.Streaming && isHead)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: "OTFF2SG104",
                    title: "Streaming mode is not supported for head",
                    messageFormat: "Type '{0}' uses [OtTableBuilder] streaming mode, but tag 'head' requires special handling. Use byte-array mode instead.",
                    category: "OTFontFile2.Generators",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                Location.None,
                info.HintName));
            return;
        }

        if (info.Mode == BuilderMode.ByteArray)
        {
            if (!HasBuildMethod(info.Symbol, info.BuildMethodName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "OTFF2SG103",
                        title: "Missing BuildTable method",
                        messageFormat: "Type '{0}' must declare a parameterless method '{1}' that returns byte[].",
                        category: "OTFontFile2.Generators",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    info.HintName,
                    info.BuildMethodName));
                return;
            }
        }
        else
        {
            if (!HasComputeLengthMethod(info.Symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "OTFF2SG105",
                        title: "Missing ComputeLength method",
                        messageFormat: "Type '{0}' must declare a parameterless method '{1}' that returns int for [OtTableBuilder] streaming mode.",
                        category: "OTFontFile2.Generators",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    info.HintName,
                    StreamingLengthMethodName));
                return;
            }

            if (!HasComputeDirectoryChecksumMethod(info.Symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "OTFF2SG106",
                        title: "Missing ComputeDirectoryChecksum method",
                        messageFormat: "Type '{0}' must declare a parameterless method '{1}' that returns uint for [OtTableBuilder] streaming mode.",
                        category: "OTFontFile2.Generators",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    info.HintName,
                    StreamingChecksumMethodName));
                return;
            }

            if (!HasWriteTableMethod(info.Symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "OTFF2SG107",
                        title: "Missing WriteTable method",
                        messageFormat: "Type '{0}' must declare a method '{1}' with signature void {1}(Stream destination, uint headCheckSumAdjustment) for [OtTableBuilder] streaming mode.",
                        category: "OTFontFile2.Generators",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    info.HintName,
                    StreamingWriteMethodName));
                return;
            }
        }

        context.AddSource(
            $"{info.HintName}.OtTableBuilder.g.cs",
            SourceText.From(GenerateBuilderScaffold(info), Encoding.UTF8));
    }

    private static bool HasBuildMethod(INamedTypeSymbol type, string buildMethodName)
    {
        foreach (var member in type.GetMembers(buildMethodName))
        {
            if (member is not IMethodSymbol m)
                continue;

            if (m.MethodKind != MethodKind.Ordinary)
                continue;

            if (m.Parameters.Length != 0)
                continue;

            if (m.Arity != 0)
                continue;

            if (m.ReturnType is IArrayTypeSymbol arr &&
                arr.ElementType.SpecialType == SpecialType.System_Byte &&
                arr.Rank == 1)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasComputeLengthMethod(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers(StreamingLengthMethodName))
        {
            if (member is not IMethodSymbol m)
                continue;

            if (m.MethodKind != MethodKind.Ordinary)
                continue;

            if (m.Parameters.Length != 0)
                continue;

            if (m.Arity != 0)
                continue;

            if (m.ReturnType.SpecialType == SpecialType.System_Int32)
                return true;
        }

        return false;
    }

    private static bool HasComputeDirectoryChecksumMethod(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers(StreamingChecksumMethodName))
        {
            if (member is not IMethodSymbol m)
                continue;

            if (m.MethodKind != MethodKind.Ordinary)
                continue;

            if (m.Parameters.Length != 0)
                continue;

            if (m.Arity != 0)
                continue;

            if (m.ReturnType.SpecialType == SpecialType.System_UInt32)
                return true;
        }

        return false;
    }

    private static bool HasWriteTableMethod(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers(StreamingWriteMethodName))
        {
            if (member is not IMethodSymbol m)
                continue;

            if (m.MethodKind != MethodKind.Ordinary)
                continue;

            if (m.Arity != 0)
                continue;

            if (!m.ReturnsVoid)
                continue;

            if (m.Parameters.Length != 2)
                continue;

            if (m.Parameters[0].Type is not INamedTypeSymbol streamType ||
                streamType.Name != "Stream" ||
                streamType.ContainingNamespace.ToDisplayString() != "System.IO")
            {
                continue;
            }

            if (m.Parameters[1].Type.SpecialType != SpecialType.System_UInt32)
                continue;

            return true;
        }

        return false;
    }

    private static string GenerateBuilderScaffold(BuilderInfo info)
    {
        uint tagValue = TagToUInt32(info.Tag!);
        bool isHead = string.Equals(info.Tag, "head", StringComparison.Ordinal);

        static void AppendIndent(StringBuilder sb, int level)
        {
            for (int i = 0; i < level; i++)
                sb.Append("    ");
        }

        static string GetAccessibilityKeyword(Accessibility accessibility) => accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => "internal"
        };

        static void AppendTypeDeclaration(StringBuilder sb, TypeInfo type, int indentLevel)
        {
            AppendIndent(sb, indentLevel);
            sb.Append(GetAccessibilityKeyword(type.Accessibility));
            sb.Append(' ');

            switch (type.TypeKind)
            {
                case TypeKind.Struct:
                    if (type.IsReadOnly)
                        sb.Append("readonly ");
                    if (type.IsRefLike)
                        sb.Append("ref ");
                    sb.Append("partial struct ");
                    sb.Append(type.Name);
                    break;

                case TypeKind.Class:
                    if (type.IsStatic)
                        sb.Append("static ");
                    sb.Append("partial class ");
                    sb.Append(type.Name);
                    break;

                default:
                    sb.Append("partial class ");
                    sb.Append(type.Name);
                    break;
            }

            sb.AppendLine();
        }

        static void AppendLine(StringBuilder sb, int indentLevel, string line)
        {
            AppendIndent(sb, indentLevel);
            sb.AppendLine(line);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {info.Namespace};");
        sb.AppendLine();

        int indent = 0;
        foreach (var container in info.ContainingTypes)
        {
            AppendTypeDeclaration(sb, container, indent);
            AppendLine(sb, indent, "{");
            indent++;
        }

        AppendTypeDeclaration(sb, info.Type, indent);
        AppendLine(sb, indent, "{");
        indent++;

        AppendLine(sb, indent, "private uint _checksum;");
        AppendLine(sb, indent, "private bool _dirty = true;");

        if (info.Mode == BuilderMode.ByteArray)
        {
            AppendLine(sb, indent, "private byte[]? _built;");
        }
        else
        {
            AppendLine(sb, indent, "private int _length;");
        }
        sb.AppendLine();

        AppendLine(sb, indent, "partial void OnMarkDirty();");
        sb.AppendLine();

        AppendLine(sb, indent, $"public Tag Tag => new(0x{tagValue:X8}u);");
        sb.AppendLine();

        if (info.Mode == BuilderMode.ByteArray)
        {
            AppendLine(sb, indent, "public int Length => EnsureBuilt().Length;");
        }
        else
        {
            AppendLine(sb, indent, "public int Length");
            AppendLine(sb, indent, "{");
            AppendLine(sb, indent + 1, "get");
            AppendLine(sb, indent + 1, "{");
            AppendLine(sb, indent + 2, "EnsureComputed();");
            AppendLine(sb, indent + 2, "return _length;");
            AppendLine(sb, indent + 1, "}");
            AppendLine(sb, indent, "}");
        }
        sb.AppendLine();

        AppendLine(sb, indent, "public uint GetDirectoryChecksum()");
        AppendLine(sb, indent, "{");
        AppendLine(sb, indent + 1, info.Mode == BuilderMode.ByteArray ? "EnsureBuilt();" : "EnsureComputed();");
        AppendLine(sb, indent + 1, "return _checksum;");
        AppendLine(sb, indent, "}");
        sb.AppendLine();

        AppendLine(sb, indent, "public void WriteTo(Stream destination, uint headCheckSumAdjustment)");
        AppendLine(sb, indent, "{");
        AppendLine(sb, indent + 1, "if (destination is null) throw new ArgumentNullException(nameof(destination));");
        sb.AppendLine();

        if (info.Mode == BuilderMode.Streaming)
        {
            AppendLine(sb, indent + 1, "EnsureComputed();");
            AppendLine(sb, indent + 1, $"{StreamingWriteMethodName}(destination, headCheckSumAdjustment);");
        }
        else if (isHead)
        {
            AppendLine(sb, indent + 1, "var data = EnsureBuilt();");
            AppendLine(sb, indent + 1, "if (data.Length < 12)");
            AppendLine(sb, indent + 1, "{");
            AppendLine(sb, indent + 2, "destination.Write(data);");
            AppendLine(sb, indent + 2, "return;");
            AppendLine(sb, indent + 1, "}");
            sb.AppendLine();
            AppendLine(sb, indent + 1, "destination.Write(data.AsSpan(0, 8));");
            AppendLine(sb, indent + 1, "Span<byte> adj = stackalloc byte[4];");
            AppendLine(sb, indent + 1, "BigEndian.WriteUInt32(adj, 0, headCheckSumAdjustment);");
            AppendLine(sb, indent + 1, "destination.Write(adj);");
            AppendLine(sb, indent + 1, "destination.Write(data.AsSpan(12));");
        }
        else
        {
            AppendLine(sb, indent + 1, "destination.Write(EnsureBuilt());");
        }

        AppendLine(sb, indent, "}");
        sb.AppendLine();

        if (info.Mode == BuilderMode.ByteArray)
        {
            AppendLine(sb, indent, "public byte[] ToArray() => EnsureBuilt();");
        }
        else
        {
            AppendLine(sb, indent, "public byte[] ToArray()");
            AppendLine(sb, indent, "{");
            AppendLine(sb, indent + 1, "byte[] bytes = new byte[Length];");
            AppendLine(sb, indent + 1, "using var ms = new MemoryStream(bytes, writable: true);");
            AppendLine(sb, indent + 1, "WriteTo(ms, headCheckSumAdjustment: 0);");
            AppendLine(sb, indent + 1, "return bytes;");
            AppendLine(sb, indent, "}");
        }
        sb.AppendLine();

        AppendLine(sb, indent, "private void MarkDirty()");
        AppendLine(sb, indent, "{");
        AppendLine(sb, indent + 1, "_dirty = true;");
        if (info.Mode == BuilderMode.ByteArray)
            AppendLine(sb, indent + 1, "_built = null;");
        AppendLine(sb, indent + 1, "OnMarkDirty();");
        AppendLine(sb, indent, "}");
        sb.AppendLine();

        if (info.Mode == BuilderMode.Streaming)
        {
            AppendLine(sb, indent, "private void EnsureComputed()");
            AppendLine(sb, indent, "{");
            AppendLine(sb, indent + 1, "if (!_dirty)");
            AppendLine(sb, indent + 2, "return;");
            sb.AppendLine();
            AppendLine(sb, indent + 1, "_length = ComputeLength();");
            AppendLine(sb, indent + 1, "_checksum = ComputeDirectoryChecksum();");
            AppendLine(sb, indent + 1, "_dirty = false;");
            AppendLine(sb, indent, "}");
        }
        else
        {
            AppendLine(sb, indent, "private byte[] EnsureBuilt()");
            AppendLine(sb, indent, "{");
            AppendLine(sb, indent + 1, "if (!_dirty && _built is not null)");
            AppendLine(sb, indent + 2, "return _built;");
            sb.AppendLine();
            AppendLine(sb, indent + 1, $"byte[] table = {info.BuildMethodName}();");
            AppendLine(sb, indent + 1, isHead
                ? "_checksum = OpenTypeChecksum.ComputeHeadDirectoryChecksum(table);"
                : "_checksum = OpenTypeChecksum.Compute(table);");
            AppendLine(sb, indent + 1, "_built = table;");
            AppendLine(sb, indent + 1, "_dirty = false;");
            AppendLine(sb, indent + 1, "return table;");
            AppendLine(sb, indent, "}");
        }

        indent--;
        AppendLine(sb, indent, "}");

        for (int i = info.ContainingTypes.Length - 1; i >= 0; i--)
        {
            indent--;
            AppendLine(sb, indent, "}");
        }

        return sb.ToString();
    }

    private static BuilderInfo? GetBuilderInfo(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type || type.TypeKind != TypeKind.Class)
            return null;

        var attribute = ctx.Attributes[0];

        string? tag = attribute.ConstructorArguments.Length == 1
            ? attribute.ConstructorArguments[0].Value as string
            : null;

        BuilderMode mode = BuilderMode.ByteArray;
        string buildMethodName = "BuildTable";
        foreach (var kv in attribute.NamedArguments)
        {
            if (kv.Key == "BuildMethodName" && kv.Value.Value is string s && !string.IsNullOrWhiteSpace(s))
                buildMethodName = s;
            else if (kv.Key == "Mode" && kv.Value.Value is int modeValue && Enum.IsDefined(typeof(BuilderMode), modeValue))
                mode = (BuilderMode)modeValue;
        }

        return new BuilderInfo(
            Symbol: type,
            HintName: GetHintBaseName(type),
            Namespace: type.ContainingNamespace.ToDisplayString(),
            Type: new TypeInfo(
                Name: type.Name,
                TypeKind: type.TypeKind,
                Accessibility: type.DeclaredAccessibility,
                IsReadOnly: type.IsReadOnly,
                IsRefLike: type.IsRefLikeType,
                IsStatic: type.IsStatic,
                IsRecord: type.IsRecord,
                IsPartial: IsPartial(type),
                Arity: type.TypeParameters.Length),
            ContainingTypes: GetContainingTypes(type),
            Tag: tag,
            Mode: mode,
            BuildMethodName: buildMethodName,
            IsPartial: IsPartial(type));
    }

    private static string GetHintBaseName(INamedTypeSymbol symbol)
    {
        string s = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (s.StartsWith("global::", StringComparison.Ordinal))
            s = s.Substring("global::".Length);

        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');

        return sb.ToString();
    }

    private static ImmutableArray<TypeInfo> GetContainingTypes(INamedTypeSymbol symbol)
    {
        var list = new List<TypeInfo>();
        for (INamedTypeSymbol? cur = symbol.ContainingType; cur is not null; cur = cur.ContainingType)
        {
            list.Add(new TypeInfo(
                Name: cur.Name,
                TypeKind: cur.TypeKind,
                Accessibility: cur.DeclaredAccessibility,
                IsReadOnly: cur.IsReadOnly,
                IsRefLike: cur.IsRefLikeType,
                IsStatic: cur.IsStatic,
                IsRecord: cur.IsRecord,
                IsPartial: IsPartial(cur),
                Arity: cur.TypeParameters.Length));
        }

        list.Reverse();
        return list.ToImmutableArray();
    }

    private static bool IsPartial(INamedTypeSymbol symbol)
    {
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(SyntaxKind.PartialKeyword))
                return true;
        }

        return false;
    }

    private static uint TagToUInt32(string tag)
    {
        if (tag.Length != 4)
            return 0;

        return (uint)tag[0] << 24 |
               (uint)tag[1] << 16 |
               (uint)tag[2] << 8 |
               tag[3];
    }

    private sealed record BuilderInfo(
        INamedTypeSymbol Symbol,
        string HintName,
        string Namespace,
        TypeInfo Type,
        ImmutableArray<TypeInfo> ContainingTypes,
        string? Tag,
        BuilderMode Mode,
        string BuildMethodName,
        bool IsPartial);

    private enum BuilderMode
    {
        ByteArray = 0,
        Streaming = 1
    }

    private sealed record TypeInfo(
        string Name,
        TypeKind TypeKind,
        Accessibility Accessibility,
        bool IsReadOnly,
        bool IsRefLike,
        bool IsStatic,
        bool IsRecord,
        bool IsPartial,
        int Arity);
}
