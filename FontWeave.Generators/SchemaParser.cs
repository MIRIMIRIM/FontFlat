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
        _model.ContractVersion = (int)Number(table, "ContractVersion", 0);
        if (_model.ContractVersion is < 0 or > 1)
        {
            _location = table.ApplicationSyntaxReference?.GetSyntax(_cancellation).GetLocation() ?? declaration.GetLocation();
            Error(FontSchemaGenerator.Shape, "ContractVersion " + _model.ContractVersion +
                " is not supported; only 0 (the current contract) and 1 are defined.");
            return Finish();
        }
        if (_model.MinimumHeader < 0) Error(FontSchemaGenerator.Layout, "minimumHeader cannot be negative.");
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
        // Attribute document order is the declared semantic order of the structural
        // model (spec 2.1): sort by the attribute syntax position, keeping the symbol
        // order for anything without a syntax reference. The owning declaration of
        // each attribute also feeds the V1 single-declaration rule.
        var ordered = new List<(AttributeData Attribute, int Start, TypeDeclarationSyntax? Owner)>(attributes.Length);
        foreach (var attribute in attributes)
        {
            int start = int.MaxValue;
            TypeDeclarationSyntax? owner = null;
            if (attribute.ApplicationSyntaxReference?.GetSyntax(_cancellation) is AttributeSyntax syntax)
            {
                start = syntax.SpanStart;
                for (SyntaxNode? node = syntax; node is not null; node = node.Parent)
                    if (node is TypeDeclarationSyntax typeDeclaration) { owner = typeDeclaration; break; }
            }
            ordered.Add((attribute, start, owner));
        }
        if (_model.ContractVersion == 1)
            foreach (var entry in ordered)
                if (entry.Owner is not null && !ReferenceEquals(entry.Owner, declaration))
                {
                    _location = entry.Attribute.ApplicationSyntaxReference?.GetSyntax(_cancellation).GetLocation() ?? declaration.GetLocation();
                    Error(FontSchemaGenerator.Shape, "Contract version 1 requires all FontWeave.Schema attributes on the single attributed partial declaration.");
                }
        attributes = ordered.OrderBy(static entry => entry.Start).Select(static entry => entry.Attribute).ToArray();
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
                case "BinaryRecordAttribute":
                    arrays.Add(new ArrayModel { Name = Text(attribute, 0), Stride = (int)Number(attribute, 1),
                        ExternallyPositioned = true });
                    break;
                case "BinaryElementAttribute":
                    elements.Add((Text(attribute, 0), Scalar(attribute, 1)));
                    break;
                case "BinaryPayloadAttribute":
                    payloads.Add(new PayloadModel { Array = Text(attribute, 0), Offset = Text(attribute, 1),
                        Length = Text(attribute, 2),
                        Base = attribute.ConstructorArguments.Length == 5 ? Text(attribute, 3) : null,
                        Minimum = Text(attribute, attribute.ConstructorArguments.Length - 1) });
                    break;
                case "BinaryTableAttribute":
                    // Parsed from context.Attributes above; listed so the unknown
                    // declaration check below cannot reject the table attribute itself.
                    break;
                default:
                    // Unknown FontWeave.Schema declarations fail loudly instead of being
                    // silently ignored, so future capabilities cannot degrade unnoticed.
                    Error(FontSchemaGenerator.Shape, "Unknown FontWeave.Schema attribute: " + attribute.AttributeClass!.Name);
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
            if (!array.ExternallyPositioned &&
                (!_scalars.TryGetValue(array.Count, out var count) || !count.IsUnsigned || count.Semantic != NumberSemantic.Count))
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
            if (!array.ExternallyPositioned)
                Add(PositionBound(array.Start), Multiply(Bound(array.Count), (ulong)Math.Max(0, array.Stride)));

        var payloadNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var payload in _model.Payloads)
        {
            Unique(payloadNames, payload.Array);
            var array = _model.Arrays.FirstOrDefault(a => a.Name == payload.Array);
            var offset = array?.Fields.FirstOrDefault(f => f.Name == payload.Offset);
            var length = array?.Fields.FirstOrDefault(f => f.Name == payload.Length);
            if (array is null || array.ExternallyPositioned || offset is null || length is null ||
                (payload.Base is not null && !_scalars.ContainsKey(payload.Base)))
            {
                Error(FontSchemaGenerator.Reference, "Payload requires an existing array, offset, length and declared origin scalar: " + payload.Array);
                continue;
            }
            if (!offset.IsUnsigned || offset.Semantic != NumberSemantic.Offset ||
                offset.Origin != (payload.Base is null ? WireOrigin.Table : WireOrigin.NameStorage) ||
                !length.IsUnsigned || length.Semantic != NumberSemantic.Count ||
                (payload.Base is not null && (!_scalars[payload.Base].IsUnsigned ||
                    _scalars[payload.Base].Semantic != NumberSemantic.Offset ||
                    _scalars[payload.Base].Origin != WireOrigin.Table || _scalars[payload.Base].Scale != 1)))
                Error(FontSchemaGenerator.Layout, "Payload requires a matching offset origin and unsigned length.");
            ulong offsetBound = Multiply(RawBound(offset), offset.Scale);
            Add(payload.Base is null ? offsetBound : Add(Bound(payload.Base), offsetBound), RawBound(length));
            UnsignedReference(payload.Minimum);
        }

        var generated = new HashSet<string>(StringComparer.Ordinal)
            { "IsBound", "RawBytes", "TryOpen", "_data", "_location", "_bound", "Location", "NormalizeLocation", "TrySlice", "Fail", "TryRequireKnownVersion" };
        GeneratedName(generated, _model.Name);
        foreach (var scalar in _model.Scalars)
        {
            GeneratedName(generated, "TryGet" + scalar.Name);
            if (scalar.WhenVersion < 0 && scalar.Position.Base is null && scalar.Position.Count is null)
                GeneratedName(generated, "TryWrite" + scalar.Name);
            GeneratedName(generated, "TryEvaluateScalar" + scalar.Name + "Position");
            if (scalar.Semantic == NumberSemantic.FixedPoint) GeneratedName(generated, "TryGet" + scalar.Name + "Value");
            if (scalar.WhenVersion < 0 && scalar.Position.Base is null && scalar.Position.Constant + (uint)scalar.Width <= (ulong)Math.Max(0, _model.MinimumHeader))
                GeneratedName(generated, scalar.Name);
        }
        foreach (var value in _model.Derived) GeneratedName(generated, "TryGet" + value.Name);
        foreach (var array in _model.Arrays)
        {
            GeneratedName(generated, array.Name + "Data");
            GeneratedName(generated, "TryWrite" + array.Name + "At");
            if (array.ExternallyPositioned) GeneratedName(generated, "TryRead" + array.Name + "At");
            else { GeneratedName(generated, "TryGet" + array.Name); GeneratedName(generated, "TryEvaluateArray" + array.Name + "Start"); }
        }
        if (_model.ContractVersion == 1)
            foreach (var array in _model.Arrays)
            {
                if (array.ExternallyPositioned) continue;
                GeneratedName(generated, "TryGet" + array.Name + "Extent");
                GeneratedName(generated, "TryRequire" + array.Name + "Extent");
                // The static counted writer is only emitted for fixed constant starts
                // driven by an unconditional, fixed-position count scalar.
                if (array.Start.Base is null && _scalars.TryGetValue(array.Count, out var count) &&
                    count.WhenVersion < 0 && count.Position.Base is null)
                    GeneratedName(generated, "TryWrite" + array.Name);
            }
        foreach (var payload in _model.Payloads) GeneratedName(generated, "TryGet" + payload.Array + "Payload");
        foreach (var member in symbol.GetMembers())
            if (!member.IsImplicitlyDeclared && generated.Contains(member.Name))
                Error(FontSchemaGenerator.Reference, "A handwritten member collides with generated API: " + member.Name);
    }

    private void ValidateScalar(ScalarModel value)
    {
        if ((int)value.Type is < 0 or > 7 || (int)value.Endian is < 0 or > 1 ||
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
    { WireType.UInt8 => byte.MaxValue, WireType.Int8 => (ulong)sbyte.MaxValue, WireType.UInt16 => ushort.MaxValue, WireType.UInt24 => 0xFFFFFF, WireType.UInt32 => uint.MaxValue, WireType.Int16 => (ulong)short.MaxValue, WireType.Int64 => long.MaxValue, _ => int.MaxValue };

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
        Value(_model.Namespace); Value(_model.Name); Value(_model.Tag); Value(_model.MinimumHeader);
        Value(_model.ContractVersion); Value(_model.VersionField);
        Value(_model.SupportedVersions.Length); foreach (uint version in _model.SupportedVersions) Value(version);
        Value(_model.Scalars.Length);
        for (int i = 0; i < _model.Scalars.Length; i++) { Value(i); Field(_model.Scalars[i]); }
        Value(_model.Derived.Length);
        for (int i = 0; i < _model.Derived.Length; i++)
        { Value(i); Value(_model.Derived[i].Name); Position(_model.Derived[i].Expression); Value(_model.Derived[i].WhenVersion); Value(_model.Derived[i].Otherwise); }
        Value(_model.Arrays.Length);
        for (int i = 0; i < _model.Arrays.Length; i++)
        {
            var array = _model.Arrays[i];
            Value(i); Value(array.Name); Value(array.Count); Position(array.Start); Value(array.Stride); Value(array.ExternallyPositioned);
            Value(array.Fields.Length);
            for (int j = 0; j < array.Fields.Length; j++) { Value(j); Field(array.Fields[j]); }
        }
        Value(_model.Payloads.Length);
        for (int i = 0; i < _model.Payloads.Length; i++)
        { Value(i); Value(_model.Payloads[i].Array); Value(_model.Payloads[i].Offset); Value(_model.Payloads[i].Length); Value(_model.Payloads[i].Base); Value(_model.Payloads[i].Minimum); }
        return key.ToString();
    }
}
