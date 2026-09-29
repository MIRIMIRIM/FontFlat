using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FontWeave.Generators;

internal sealed class ParseResult
{
    internal ParseResult(TableModel? model, ImmutableArray<Diagnostic> diagnostics)
    { Model = model; Diagnostics = diagnostics; }
    internal TableModel? Model { get; }
    internal ImmutableArray<Diagnostic> Diagnostics { get; }
}

internal sealed class SchemaParser
{
    private readonly List<Diagnostic> _errors = new();
    private readonly HashSet<int> _fieldIds;
    private readonly CancellationToken _cancellation;
    private Location _location;
    private readonly TableModel _model = new();
    private readonly Dictionary<string, ScalarModel> _scalars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DerivedModel> _derived = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _bounds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);

    private SchemaParser(GeneratorAttributeSyntaxContext context, CancellationToken cancellation)
    {
        _location = context.TargetNode.GetLocation();
        _cancellation = cancellation;
        var fieldType = context.SemanticModel.Compilation.GetTypeByMetadataName("FontWeave.Core.DataField");
        _fieldIds = new HashSet<int>(fieldType?.GetMembers().OfType<IFieldSymbol>()
            .Where(static field => field.HasConstantValue)
            .Select(static field => Convert.ToInt32(field.ConstantValue, CultureInfo.InvariantCulture)) ?? Enumerable.Empty<int>());
    }

    internal static ParseResult Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellation) =>
        new SchemaParser(context, cancellation).Read(context);

    private ParseResult Read(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var declaration = (TypeDeclarationSyntax)context.TargetNode;
        if (symbol.TypeKind != TypeKind.Struct || !symbol.IsRefLikeType || !symbol.IsReadOnly ||
            symbol.DeclaredAccessibility != Accessibility.Public || symbol.ContainingType is not null ||
            symbol.TypeParameters.Length != 0 || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) ||
            context.Attributes.Length != 1)
            Error(FontSchemaGenerator.Shape, "A schema must be one attributed, public readonly ref partial, top-level, non-generic struct.");

        _model.Name = symbol.Name;
        _model.Namespace = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString();
        Name(_model.Name);
        var table = context.Attributes[0];
        _model.Tag = (uint)Number(table, 0);
        _model.MinimumHeader = (int)Number(table, 1);
        _model.VersionField = Text(table, "VersionField");
        _model.SupportedVersions = Versions(table);
        if (_model.MinimumHeader <= 0) Error(FontSchemaGenerator.Layout, "minimumHeader must be positive.");
        if (_fieldIds.Count == 0) Error(FontSchemaGenerator.Reference, "FontWeave.Core.DataField is required; no second diagnostic contract is generated.");

        var scalars = new List<ScalarModel>();
        var derived = new List<DerivedModel>();
        var arrays = new List<ArrayModel>();
        var elements = new List<(string Array, ScalarModel Field)>();
        var payloads = new List<PayloadModel>();
        var attributes = symbol.GetAttributes().Where(static a => a.AttributeClass?.ContainingNamespace.ToDisplayString() == "FontWeave.Schema").ToArray();
        if (attributes.Length > 256)
        {
            Error(FontSchemaGenerator.Layout, "The R2 schema limit is 256 declarations per table.");
            return Finish();
        }
        foreach (var attribute in attributes)
        {
            _cancellation.ThrowIfCancellationRequested();
            _location = attribute.ApplicationSyntaxReference?.GetSyntax(_cancellation).GetLocation() ?? declaration.GetLocation();
            switch (attribute.AttributeClass!.Name)
            {
                case "BinaryScalarAttribute":
                    scalars.Add(Scalar(attribute, 0));
                    break;
                case "BinaryDerivedAttribute":
                    derived.Add(new DerivedModel { Name = Text(attribute, 0), Expression = new PositionModel {
                        Base = Text(attribute, "Base"), Constant = (ulong)Number(attribute, "Constant", 0),
                        Count = Text(attribute, "Count"), Stride = (ulong)Number(attribute, "Stride", 1) },
                        WhenVersion = Number(attribute, "WhenVersion", -1), Otherwise = Text(attribute, "Otherwise") });
                    break;
                case "BinaryArrayAttribute":
                    arrays.Add(new ArrayModel { Name = Text(attribute, 0), Count = Text(attribute, 1),
                        Start = new PositionModel { Constant = Unsigned(Number(attribute, 2)), Base = Text(attribute, "Start") },
                        Stride = (int)Number(attribute, 3) });
                    break;
                case "BinaryElementAttribute":
                    elements.Add((Text(attribute, 0), Scalar(attribute, 1)));
                    break;
                case "BinaryPayloadAttribute":
                    payloads.Add(new PayloadModel { Array = Text(attribute, 0), Offset = Text(attribute, 1),
                        Length = Text(attribute, 2), Base = Text(attribute, 3), Minimum = Text(attribute, 4) });
                    break;
            }
        }
        _location = declaration.GetLocation();
        _model.Scalars = scalars.ToImmutableArray();
        _model.Derived = derived.ToImmutableArray();
        _model.Arrays = arrays.ToImmutableArray();
        _model.Payloads = payloads.ToImmutableArray();
        foreach (var array in arrays)
            array.Fields = elements.Where(e => e.Array == array.Name).Select(static e => e.Field).ToImmutableArray();
        foreach (var element in elements)
            if (!arrays.Any(a => a.Name == element.Array)) Error(FontSchemaGenerator.Reference, "Unknown element array: " + element.Array);
        Validate(symbol);
        if (_errors.Count == 0) _model.Identity = Identity();
        return Finish();
    }

    private ParseResult Finish() => new(_errors.Count == 0 ? _model : null, _errors.ToImmutableArray());

    private ScalarModel Scalar(AttributeData attribute, int first)
    {
        var value = new ScalarModel {
            Name = Text(attribute, first), Type = (WireType)Number(attribute, first + 1),
            Position = new PositionModel { Constant = Unsigned(Number(attribute, first + 2)), Base = Text(attribute, "Position") },
            FieldId = (int)Number(attribute, first + 3), Endian = (WireEndian)Number(attribute, "Endian", 0),
            Semantic = (NumberSemantic)Number(attribute, "Semantic", 0), Origin = (WireOrigin)Number(attribute, "Origin", 0),
            Scale = (ulong)Number(attribute, "Scale", 1), ZeroAllowed = Boolean(attribute, "ZeroAllowed", true),
            WhenVersion = Number(attribute, "WhenVersion", -1), Otherwise = (ulong)Number(attribute, "Otherwise", 0)
        };
        return value;
    }

    private void Validate(INamedTypeSymbol symbol)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scalar in _model.Scalars)
        {
            Unique(names, scalar.Name);
            if (!_scalars.ContainsKey(scalar.Name)) _scalars.Add(scalar.Name, scalar);
            ValidateScalar(scalar);
        }
        foreach (var value in _model.Derived)
        {
            Unique(names, value.Name);
            if (!_derived.ContainsKey(value.Name)) _derived.Add(value.Name, value);
            VersionBranch(value.WhenVersion);
            if (value.WhenVersion >= 0 && value.Otherwise is null)
                Error(FontSchemaGenerator.Reference, "A conditional derived field requires an Otherwise value reference.");
            if (value.WhenVersion < 0 && value.Otherwise is not null)
                Error(FontSchemaGenerator.Layout, "Otherwise requires WhenVersion.");
        }
        if (_model.VersionField is null)
        {
            if (!_model.SupportedVersions.IsEmpty) Error(FontSchemaGenerator.Reference, "SupportedVersions requires VersionField.");
        }
        else if (!_scalars.TryGetValue(_model.VersionField, out var version) || !version.IsUnsigned ||
            version.WhenVersion >= 0 || version.Position.Base is not null ||
            version.Position.Constant + (uint)version.Width > (ulong)Math.Max(0, _model.MinimumHeader) ||
            _model.SupportedVersions.IsEmpty)
            Error(FontSchemaGenerator.Layout, "VersionField must be an unconditional unsigned field within the bound header and have supported versions.");
        else if (_model.SupportedVersions.Any(v => v > RawBound(version)))
            Error(FontSchemaGenerator.Layout, "A supported version is not representable by VersionField.");

        var arrayNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var array in _model.Arrays)
        {
            Unique(arrayNames, array.Name);
            if (array.Stride <= 0) Error(FontSchemaGenerator.Layout, "Array stride must be positive.");
            if (!_scalars.TryGetValue(array.Count, out var count) || !count.IsUnsigned || count.Semantic != NumberSemantic.Count)
                Error(FontSchemaGenerator.Reference, "Array count must reference an unsigned Count scalar: " + array.Count);
            var fieldNames = new HashSet<string>(StringComparer.Ordinal)
                { "Ordinal", "Equals", "ToString", "GetHashCode", "PrintMembers", "Deconstruct", array.Name + "Data" };
            foreach (var field in array.Fields)
            {
                Unique(fieldNames, field.Name);
                ValidateScalar(field);
                if (field.Position.Constant + (uint)field.Width > (ulong)Math.Max(0, array.Stride))
                    Error(FontSchemaGenerator.Layout, "Element field exceeds the array stride: " + field.Name);
            }
            if (array.Fields.IsEmpty) Error(FontSchemaGenerator.Layout, "An array needs element fields: " + array.Name);
            Overlaps(array.Fields);
        }
        Overlaps(_model.Scalars.Where(static x => x.Position.Base is null).ToImmutableArray());

        // Check dependencies even for scalar values: a positional dependency can itself cycle.
        foreach (var name in names) Bound(name);
        foreach (var array in _model.Arrays)
            Add(PositionBound(array.Start), Multiply(Bound(array.Count), (ulong)Math.Max(0, array.Stride)));

        var payloadNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var payload in _model.Payloads)
        {
            Unique(payloadNames, payload.Array);
            var array = _model.Arrays.FirstOrDefault(a => a.Name == payload.Array);
            var offset = array?.Fields.FirstOrDefault(f => f.Name == payload.Offset);
            var length = array?.Fields.FirstOrDefault(f => f.Name == payload.Length);
            if (array is null || offset is null || length is null || !_scalars.TryGetValue(payload.Base, out var origin))
            {
                Error(FontSchemaGenerator.Reference, "Payload requires an existing array, offset, length and origin scalar: " + payload.Array);
                continue;
            }
            if (!offset.IsUnsigned || offset.Semantic != NumberSemantic.Offset || offset.Origin != WireOrigin.NameStorage ||
                !length.IsUnsigned || length.Semantic != NumberSemantic.Count ||
                !origin.IsUnsigned || origin.Semantic != NumberSemantic.Offset || origin.Origin != WireOrigin.Table || origin.Scale != 1)
                Error(FontSchemaGenerator.Layout, "Payload offsets are NameStorage-relative and require a table-relative origin and unsigned length.");
            Add(Add(Bound(payload.Base), Multiply(RawBound(offset), offset.Scale)), RawBound(length));
            UnsignedReference(payload.Minimum);
        }

        var generated = new HashSet<string>(StringComparer.Ordinal)
            { "IsBound", "RawBytes", "TryOpen", "_data", "_location", "Location", "NormalizeLocation", "TrySlice", "Fail", "TryRequireKnownVersion" };
        GeneratedName(generated, _model.Name);
        foreach (var scalar in _model.Scalars)
        {
            GeneratedName(generated, "TryGet" + scalar.Name);
            GeneratedName(generated, "TryEvaluateScalar" + scalar.Name + "Position");
            if (scalar.Semantic == NumberSemantic.FixedPoint) GeneratedName(generated, "TryGet" + scalar.Name + "Value");
            if (scalar.WhenVersion < 0 && scalar.Position.Base is null && scalar.Position.Constant + (uint)scalar.Width <= (ulong)Math.Max(0, _model.MinimumHeader))
                GeneratedName(generated, scalar.Name);
        }
        foreach (var value in _model.Derived) GeneratedName(generated, "TryGet" + value.Name);
        foreach (var array in _model.Arrays)
        { GeneratedName(generated, "TryGet" + array.Name); GeneratedName(generated, array.Name + "Data"); GeneratedName(generated, "TryEvaluateArray" + array.Name + "Start"); }
        foreach (var payload in _model.Payloads) GeneratedName(generated, "TryGet" + payload.Array + "Payload");
        foreach (var member in symbol.GetMembers())
            if (!member.IsImplicitlyDeclared && generated.Contains(member.Name))
                Error(FontSchemaGenerator.Reference, "A handwritten member collides with generated API: " + member.Name);
    }

    private void ValidateScalar(ScalarModel value)
    {
        if ((int)value.Type is < 0 or > 4 || (int)value.Endian is < 0 or > 1 ||
            (int)value.Semantic is < 0 or > 3 || (int)value.Origin is < 0 or > 2 || !_fieldIds.Contains(value.FieldId))
            Error(FontSchemaGenerator.Layout, "Invalid wire type, endianness, numeric semantic, origin or FieldId: " + value.Name);
        if (value.Scale == 0 || (value.Semantic is NumberSemantic.Count or NumberSemantic.Offset && !value.IsUnsigned) ||
            (value.Semantic is NumberSemantic.Plain or NumberSemantic.Count && value.Scale != 1) ||
            (value.Semantic != NumberSemantic.Offset && (value.Origin != WireOrigin.None || !value.ZeroAllowed)) ||
            (value.Semantic == NumberSemantic.Offset && value.Origin == WireOrigin.None))
            Error(FontSchemaGenerator.Layout, "Inconsistent scale, zero policy or numeric/offset semantics: " + value.Name);
        VersionBranch(value.WhenVersion);
        if (value.WhenVersion >= 0 && (!value.IsUnsigned || value.Otherwise > RawBound(value)))
            Error(FontSchemaGenerator.Layout, "A conditional scalar requires a representable unsigned fallback.");
        if (value.WhenVersion < 0 && value.Otherwise != 0)
            Error(FontSchemaGenerator.Layout, "Otherwise requires WhenVersion.");
    }

    private void VersionBranch(long version)
    {
        if (version < -1 || version > uint.MaxValue || (version >= 0 &&
            (_model.VersionField is null || !_model.SupportedVersions.Contains((uint)version))))
            Error(FontSchemaGenerator.Layout, "WhenVersion must be a declared supported version, or -1 for unconditional.");
    }

    private ulong Bound(string name)
    {
        _cancellation.ThrowIfCancellationRequested();
        if (_bounds.TryGetValue(name, out ulong bound)) return bound;
        if (_visiting.Contains(name))
        {
            Error(FontSchemaGenerator.Cycle, "Cyclic position/value reference: " + name);
            return 0;
        }
        if (_visiting.Count >= 32)
        {
            Error(FontSchemaGenerator.Layout, "The R2 expression depth limit is 32.");
            return 0;
        }
        _visiting.Add(name);
        if (_scalars.TryGetValue(name, out var scalar))
        {
            Add(PositionBound(scalar.Position), (uint)scalar.Width);
            if (scalar.WhenVersion >= 0 && _model.VersionField is not null) Bound(_model.VersionField);
            bound = RawBound(scalar);
        }
        else if (_derived.TryGetValue(name, out var derived))
        {
            bound = PositionBound(derived.Expression);
            if (derived.WhenVersion >= 0)
            {
                if (_model.VersionField is not null) Bound(_model.VersionField);
                if (derived.Otherwise is not null) bound = Math.Max(bound, UnsignedReference(derived.Otherwise));
            }
        }
        else
        {
            Error(FontSchemaGenerator.Reference, "Unknown value reference: " + name);
            bound = 0;
        }
        _visiting.Remove(name);
        _bounds[name] = bound;
        return bound;
    }

    private ulong PositionBound(PositionModel value) => Add(Add(value.Constant,
        value.Base is null ? 0 : UnsignedReference(value.Base)),
        value.Count is null ? 0 : Multiply(UnsignedReference(value.Count), value.Stride));

    private ulong UnsignedReference(string name)
    {
        if (_scalars.TryGetValue(name, out var field) && (!field.IsUnsigned || field.Scale != 1))
            Error(FontSchemaGenerator.Layout, "Position/value expressions require unsigned unscaled references: " + name);
        return Bound(name);
    }
    private ulong Add(ulong left, ulong right)
    {
        if (left > ulong.MaxValue - right) { Error(FontSchemaGenerator.Layout, "Expression worst-case addition exceeds UInt64."); return 0; }
        return left + right;
    }
    private ulong Multiply(ulong left, ulong right)
    {
        if (right != 0 && left > ulong.MaxValue / right) { Error(FontSchemaGenerator.Layout, "Expression worst-case multiplication exceeds UInt64."); return 0; }
        return left * right;
    }
    private static ulong RawBound(ScalarModel value) => value.Type switch
    { WireType.UInt16 => ushort.MaxValue, WireType.UInt32 => uint.MaxValue, WireType.Int16 => (ulong)short.MaxValue, WireType.Int64 => long.MaxValue, _ => int.MaxValue };

    private void Overlaps(ImmutableArray<ScalarModel> fields)
    {
        for (int i = 0; i < fields.Length; i++)
            for (int j = 0; j < i; j++)
            {
                var a = fields[i]; var b = fields[j];
                if (a.WhenVersion >= 0 && b.WhenVersion >= 0 && a.WhenVersion != b.WhenVersion) continue;
                if (a.Position.Constant < b.Position.Constant + (uint)b.Width && b.Position.Constant < a.Position.Constant + (uint)a.Width)
                    Error(FontSchemaGenerator.Layout, "Overlapping fixed fields: " + a.Name + " and " + b.Name);
            }
    }
    private void Unique(HashSet<string> names, string name)
    {
        Name(name);
        if (!names.Add(name)) Error(FontSchemaGenerator.Reference, "Duplicate schema name: " + name);
    }
    private void GeneratedName(HashSet<string> names, string name)
    {
        if (!names.Add(name)) Error(FontSchemaGenerator.Reference, "Generated member collision: " + name);
    }
    private void Name(string name)
    {
        if (!SyntaxFacts.IsValidIdentifier(name) || name.StartsWith("@", StringComparison.Ordinal) || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None)
            Error(FontSchemaGenerator.Reference, "Expected a plain C# identifier, not an expression: " + name);
    }
    private void Error(DiagnosticDescriptor rule, string message) => _errors.Add(Diagnostic.Create(rule, _location, message));
    private ulong Unsigned(long value)
    {
        if (value < 0) { Error(FontSchemaGenerator.Layout, "Wire positions cannot be negative."); return 0; }
        return (ulong)value;
    }

    private long Number(AttributeData attribute, int index)
    {
        if (index >= attribute.ConstructorArguments.Length || attribute.ConstructorArguments[index].Value is null)
        { Error(FontSchemaGenerator.Layout, "Missing numeric declaration argument."); return 0; }
        return Convert.ToInt64(attribute.ConstructorArguments[index].Value, CultureInfo.InvariantCulture);
    }
    private static long Number(AttributeData attribute, string name, long fallback) =>
        attribute.NamedArguments.FirstOrDefault(p => p.Key == name).Value.Value is object value
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : fallback;
    private string Text(AttributeData attribute, int index)
    {
        if (index < attribute.ConstructorArguments.Length && attribute.ConstructorArguments[index].Value is string text) return text;
        Error(FontSchemaGenerator.Reference, "Missing identifier declaration argument."); return "";
    }
    private static string? Text(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(p => p.Key == name).Value.Value as string;
    private static bool Boolean(AttributeData attribute, string name, bool fallback) =>
        attribute.NamedArguments.FirstOrDefault(p => p.Key == name).Value.Value is bool value ? value : fallback;
    private ImmutableArray<uint> Versions(AttributeData attribute)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(static p => p.Key == "SupportedVersions").Value;
        if (argument.Kind != TypedConstantKind.Array || argument.IsNull) return ImmutableArray<uint>.Empty;
        return argument.Values.Where(static v => v.Value is uint).Select(static v => (uint)v.Value!).Distinct().ToImmutableArray();
    }

    private string Identity()
    {
        var key = new StringBuilder();
        void Value(object? value) => key.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('|');
        void Position(PositionModel value) { Value(value.Base); Value(value.Constant); Value(value.Count); Value(value.Stride); }
        void Field(ScalarModel field)
        {
            Value(field.Name); Value(field.Type); Value(field.Endian); Value(field.Semantic); Value(field.Origin);
            Value(field.FieldId); Value(field.Scale); Value(field.ZeroAllowed); Position(field.Position);
            Value(field.WhenVersion); Value(field.Otherwise);
        }
        Value(_model.Namespace); Value(_model.Name); Value(_model.Tag); Value(_model.MinimumHeader); Value(_model.VersionField);
        Value(_model.SupportedVersions.Length); foreach (uint version in _model.SupportedVersions) Value(version);
        Value(_model.Scalars.Length); foreach (var field in _model.Scalars) Field(field);
        Value(_model.Derived.Length); foreach (var value in _model.Derived)
        { Value(value.Name); Position(value.Expression); Value(value.WhenVersion); Value(value.Otherwise); }
        Value(_model.Arrays.Length); foreach (var array in _model.Arrays)
        { Value(array.Name); Value(array.Count); Position(array.Start); Value(array.Stride); Value(array.Fields.Length); foreach (var field in array.Fields) Field(field); }
        Value(_model.Payloads.Length); foreach (var payload in _model.Payloads)
        { Value(payload.Array); Value(payload.Offset); Value(payload.Length); Value(payload.Base); Value(payload.Minimum); }
        return key.ToString();
    }
}
