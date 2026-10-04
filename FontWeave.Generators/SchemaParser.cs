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
    private INamedTypeSymbol _symbol = null!;
    private readonly Dictionary<string, ScalarModel> _scalars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DerivedModel> _derived = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComputedModel> _computeds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _bounds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);
    private readonly List<ReferenceModel> _references = new();
    private readonly HashSet<string> _referenceNames = new(StringComparer.Ordinal);
    // Slot keys "name" (scalars) / "array:element" (elements) mapped to the legacy
    // Origin/ZeroAllowed values the declaration stated explicitly (card 1.2-f: only
    // explicit legacy values participate in the consistency check).
    private readonly Dictionary<string, (WireOrigin? Origin, bool? ZeroAllowed)> _explicitMetadata = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScalarModel> _elements = new(StringComparer.Ordinal);
    private readonly HashSet<long> _supportedRoleValues = new();

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
        ParseTableRoles(table);

        var scalars = new List<ScalarModel>();
        var derived = new List<DerivedModel>();
        var computeds = new List<ComputedModel>();
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
                    var parsedScalar = Scalar(attribute, 0);
                    scalars.Add(parsedScalar);
                    RecordExplicitMetadata(parsedScalar.Name, attribute);
                    break;
                case "BinaryDerivedAttribute":
                    var derivedModel = new DerivedModel {
                        Name = Text(attribute, 0), Expression = new PositionModel {
                            Base = Text(attribute, "Base"), Constant = (ulong)Number(attribute, "Constant", 0),
                            Count = Text(attribute, "Count"), Stride = (ulong)Number(attribute, "Stride", 1) },
                        WhenVersion = Number(attribute, "WhenVersion", -1), Otherwise = Text(attribute, "Otherwise"),
                        CountSubtract = (uint)Number(attribute, "CountSubtract", 0),
                        Semantic = (NumberSemantic)Number(attribute, "Semantic", 0) };
                    if ((derivedModel.CountSubtract != 0 || derivedModel.Semantic != NumberSemantic.Plain) &&
                        _model.ContractVersion != 1)
                    {
                        // Spec 1.7: a V0 declaration of a V1 capability is an explicit
                        // ContractVersion=1 rejection, not silent reinterpretation.
                        Error(FontSchemaGenerator.Shape, "Derived CountSubtract/Semantic requires ContractVersion=1.");
                        derivedModel.CountSubtract = 0;
                        derivedModel.Semantic = NumberSemantic.Plain;
                    }
                    derived.Add(derivedModel);
                    break;
                case "BinaryArrayAttribute":
                    arrays.Add(new ArrayModel { Name = Text(attribute, 0), Count = Text(attribute, 1),
                        Start = new PositionModel { Constant = Unsigned(Number(attribute, 2)), Base = Text(attribute, "Start") },
                        Stride = (int)Number(attribute, 3), WhenVersions = Gate(WhenVersions(attribute)) });
                    break;
                case "BinaryRecordAttribute":
                    arrays.Add(new ArrayModel { Name = Text(attribute, 0), Stride = (int)Number(attribute, 1),
                        ExternallyPositioned = true, WhenVersions = Gate(WhenVersions(attribute)) });
                    break;
                case "BinaryComputedAttribute":
                    var computed = new ComputedModel {
                        Name = Text(attribute, 0), Method = Text(attribute, 1), Inputs = Inputs(attribute),
                        Maximum = Maximum(attribute),
                        Semantic = (NumberSemantic)Number(attribute, "Semantic", 0),
                        DiagnosticFieldId = (int)Number(attribute, "DiagnosticField", 0),
                        HasExplicitDiagnosticField = attribute.NamedArguments.Any(static p => p.Key == "DiagnosticField"),
                        DiagnosticSource = Text(attribute, "DiagnosticSource"),
                        WhenVersions = Gate(WhenVersions(attribute)) };
                    if (_model.ContractVersion != 1)
                        Error(FontSchemaGenerator.Shape, "BinaryComputed requires ContractVersion=1.");
                    else
                        computeds.Add(computed);
                    break;
                case "BinaryElementAttribute":
                    var parsedElement = Scalar(attribute, 1);
                    elements.Add((Text(attribute, 0), parsedElement));
                    RecordExplicitMetadata(Text(attribute, 0) + ":" + parsedElement.Name, attribute);
                    break;
                case "BinaryReferenceAttribute":
                    var reference = new ReferenceModel {
                        Name = Text(attribute, 0), OffsetField = Text(attribute, 1),
                        ArrayName = Text(attribute, "Array"), Origin = (ReferenceOrigin)Number(attribute, "Origin", 0),
                        Base = Text(attribute, "Base"), Minimum = Text(attribute, "Minimum"), Length = Text(attribute, "Length"),
                        NullPolicy = (ReferenceNullPolicy)Number(attribute, "NullPolicy", 0),
                        ZeroFailure = (ReferenceZeroFailure)Number(attribute, "ZeroFailure", 0),
                        AllowTargetAtEnd = Boolean(attribute, "AllowTargetAtEnd", false),
                        WhenRoles = Longs(attribute, "WhenRoles"), Selector = Text(attribute, "Selector"),
                        SelectorValues = SelectorValues(attribute), DefaultRaw = Boolean(attribute, "DefaultRaw", false),
                        TargetRole = Number(attribute, "TargetRole", long.MinValue),
                        ForwardRole = Boolean(attribute, "ForwardRole", false),
                        Scan = (ReferenceScan)Number(attribute, "Scan", 0) };
                    if (attribute.ConstructorArguments.Length > 2 &&
                        attribute.ConstructorArguments[2].Value is ITypeSymbol target && target.TypeKind != TypeKind.Error)
                        reference.TargetFullName = target.ToDisplayString();
                    if (_model.ContractVersion != 1)
                    {
                        // Spec 1.7: a V0 declaration of a V1 capability is an explicit
                        // ContractVersion=1 rejection, not silent reinterpretation.
                        Error(FontSchemaGenerator.Shape, "BinaryReference requires ContractVersion=1.");
                        break;
                    }
                    _references.Add(reference);
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
        _model.Computed = computeds.ToImmutableArray();
        _model.Arrays = arrays.ToImmutableArray();
        _model.Payloads = payloads.ToImmutableArray();
        _model.References = _references.ToImmutableArray();
        foreach (var array in arrays)
            array.Fields = elements.Where(e => e.Array == array.Name).Select(static e => e.Field).ToImmutableArray();
        foreach (var array in arrays)
            foreach (var field in array.Fields)
                _elements[array.Name + ":" + field.Name] = field;
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
        _symbol = symbol;
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
            if ((int)value.Semantic is < 0 or > 1)
                Error(FontSchemaGenerator.Layout, "A derived semantic must be Plain or Count: " + value.Name);
            if (value.CountSubtract != 0)
            {
                // Spec 2.2: CountSubtract needs a count and must not provably underflow it.
                if (value.Expression.Count is null)
                    Error(FontSchemaGenerator.Layout, "CountSubtract requires a Count reference: " + value.Name);
                else if (Bound(value.Expression.Count) < value.CountSubtract)
                    Error(FontSchemaGenerator.Layout, "CountSubtract provably underflows the raw count: " + value.Name);
            }
        }
        foreach (var computed in _model.Computed)
        {
            Unique(names, computed.Name);
            if (!_computeds.ContainsKey(computed.Name)) _computeds.Add(computed.Name, computed);
            ValidateComputed(computed);
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
            if (array.WhenVersions.Length != 0)
                ValidateConditionDomain(array.WhenVersions, "Array " + array.Name);
            if (!array.ExternallyPositioned)
                ValidateArrayCount(array);
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

        ValidateReferences();

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
        foreach (var computed in _model.Computed) GeneratedName(generated, "TryGet" + computed.Name);
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
        foreach (var reference in _model.References)
        {
            GeneratedName(generated, "TryOpen" + reference.Name);
            GeneratedName(generated, "TryGet" + reference.Name + "OffsetValue");
            if (reference.ArrayName is { } openArray &&
                _model.Arrays.FirstOrDefault(a => a.Name == openArray) is { ExternallyPositioned: true })
                GeneratedName(generated, "TryOpen" + reference.Name + "At");
        }
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

    // Card 1.2-f: only explicitly stated legacy Origin/ZeroAllowed values join the
    // reference consistency check; defaults and None are never asserted.
    private void RecordExplicitMetadata(string slotKey, AttributeData attribute)
    {
        _explicitMetadata[slotKey] = (
            attribute.NamedArguments.Any(static p => p.Key == "Origin")
                ? (WireOrigin?)(WireOrigin)Number(attribute, "Origin", 0) : null,
            attribute.NamedArguments.Any(static p => p.Key == "ZeroAllowed")
                ? (bool?)Boolean(attribute, "ZeroAllowed", true) : null);
    }

    // Spec 3 typed references (card 1.1): slot identity, offset-field shape, name
    // resolution, record scope and legacy-metadata consistency. Target registration and
    // the AllowTargetAtEnd x minimum-header match are the registry combine step's job.
    private void ValidateReferences()
    {
        var payloadSlots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var payload in _model.Payloads)
            payloadSlots.Add(payload.Array + ":" + payload.Offset);
        foreach (var reference in _model.References)
        {
            Name(reference.Name);
            if (!_referenceNames.Add(reference.Name))
                Error(FontSchemaGenerator.Reference, "Duplicate reference name: " + reference.Name);
            if ((int)reference.Origin is < 0 or > 2 || (int)reference.NullPolicy is < 0 or > 2 ||
                (int)reference.ZeroFailure is < 0 or > 1)
                Error(FontSchemaGenerator.Layout, "Invalid reference origin, null policy or zero failure: " + reference.Name);
            if (reference.Origin == ReferenceOrigin.NamedBase && reference.Base is null)
                Error(FontSchemaGenerator.Layout, "A NamedBase reference requires an explicit Base: " + reference.Name);
            if (reference.Origin != ReferenceOrigin.NamedBase && reference.Base is not null)
                Error(FontSchemaGenerator.Layout, "Only a NamedBase reference takes a Base: " + reference.Name);

            ScalarModel? offset;
            ArrayModel? array = null;
            if (reference.ArrayName is { } arrayName)
            {
                array = _model.Arrays.FirstOrDefault(a => a.Name == arrayName);
                if (array is null)
                {
                    if (_scalars.ContainsKey(arrayName) || _derived.ContainsKey(arrayName) || _computeds.ContainsKey(arrayName))
                        Error(FontSchemaGenerator.Layout, "A reference Array must name a declared array or record: " + arrayName);
                    else
                        Error(FontSchemaGenerator.Reference, "Unknown reference array: " + arrayName);
                    continue;
                }
                offset = array.Fields.FirstOrDefault(f => f.Name == reference.OffsetField);
                if (offset is null)
                {
                    if (_elements.ContainsKey(arrayName + ":" + reference.OffsetField))
                        Error(FontSchemaGenerator.Layout, "The reference offset field belongs to another array: " + reference.OffsetField);
                    else if (!_scalars.ContainsKey(reference.OffsetField))
                        Error(FontSchemaGenerator.Reference, "Unknown reference offset field: " + reference.OffsetField);
                    else
                        Error(FontSchemaGenerator.Layout, "The reference offset field must be an element of the array: " + reference.OffsetField);
                    continue;
                }
            }
            else
            {
                if (_scalars.TryGetValue(reference.OffsetField, out var scalarOffset))
                {
                    offset = scalarOffset;
                }
                else
                {
                    if (_elements.Values.Any(e => e.Name == reference.OffsetField))
                        Error(FontSchemaGenerator.Layout, "An array-element offset requires the reference Array: " + reference.OffsetField);
                    else
                        Error(FontSchemaGenerator.Reference, "Unknown reference offset field: " + reference.OffsetField);
                    continue;
                }
            }
            if (!offset.IsUnsigned || offset.Semantic != NumberSemantic.Offset || offset.Scale == 0)
                Error(FontSchemaGenerator.Layout, "A reference offset field must be an unsigned Offset value with a positive scale: " + reference.OffsetField);

            string slotKey = reference.ArrayName is null ? reference.OffsetField : reference.ArrayName + ":" + reference.OffsetField;
            if (payloadSlots.Contains(slotKey))
                Error(FontSchemaGenerator.Condition, "A source slot cannot declare both a payload and a reference: " + slotKey);
            if (_explicitMetadata.TryGetValue(slotKey, out var legacy))
            {
                if (legacy.Origin is { } declaredOrigin)
                {
                    // Card 1.2-f (spec 3.1): explicit legacy Origin and the reference Origin
                    // normalize into one relation. Table anchors the table window, which
                    // covers both the ViewStart and RecordStart bases; NameStorage only
                    // pairs with an explicit NamedBase Base.
                    bool originConsistent = declaredOrigin == WireOrigin.NameStorage
                        ? reference.Origin == ReferenceOrigin.NamedBase
                        : reference.Origin != ReferenceOrigin.NamedBase;
                    if (!originConsistent)
                        Error(FontSchemaGenerator.Condition, "The declared field origin conflicts with the reference origin: " + reference.OffsetField);
                }
                if (legacy.ZeroAllowed is { } declaredZero &&
                    reference.NullPolicy == ReferenceNullPolicy.Required == declaredZero)
                    Error(FontSchemaGenerator.Condition, "The declared zero policy conflicts with the reference null policy: " + reference.OffsetField);
            }

            foreach (var (valueName, role) in new[] { (reference.Base, "Base"), (reference.Minimum, "Minimum") })
            {
                if (valueName is null) continue;
                if (_scalars.TryGetValue(valueName, out var referenced))
                {
                    if (referenced.Scale != 1)
                        Error(FontSchemaGenerator.Layout, "Reference " + role + " must reference an unscaled scalar: " + valueName);
                }
                else if (!_derived.ContainsKey(valueName) && !_computeds.ContainsKey(valueName))
                    Error(FontSchemaGenerator.Reference, "Unknown reference " + role + ": " + valueName);
            }
            ulong worstLength = 0;
            if (reference.Length is { } lengthName)
            {
                if (array is { } scope)
                {
                    var lengthElement = scope.Fields.FirstOrDefault(f => f.Name == lengthName);
                    if (lengthElement is null)
                    {
                        if (_elements.Values.Any(e => e.Name == lengthName) || !_scalars.ContainsKey(lengthName))
                            Error(FontSchemaGenerator.Layout, "A reference Length must share the offset field's record scope: " + lengthName);
                        else
                            Error(FontSchemaGenerator.Layout, "A record-scoped reference Length must be an element of the array: " + lengthName);
                    }
                    else worstLength = RawBound(lengthElement);
                }
                else if (_scalars.TryGetValue(lengthName, out var lengthScalar))
                {
                    if (lengthScalar.Scale != 1)
                        Error(FontSchemaGenerator.Layout, "Reference Length must reference an unscaled scalar: " + lengthName);
                    worstLength = Bound(lengthName);
                }
                else if (_derived.ContainsKey(lengthName) || _computeds.ContainsKey(lengthName))
                {
                    worstLength = Bound(lengthName);
                }
                else
                    Error(FontSchemaGenerator.Reference, "Unknown reference Length: " + lengthName);
            }
            // Card 1.2-h: the worst-case Minimum+Length sum must stay representable (FWG003).
            // Unknown names stay FWG002 and are excluded from the sum check.
            if (reference.Minimum is { } minimumName &&
                (_scalars.ContainsKey(minimumName) || _derived.ContainsKey(minimumName) || _computeds.ContainsKey(minimumName)))
                Add(Bound(minimumName), worstLength);
        }
        ValidateRoleContracts();
    }

    // Card 1.2-a (spec 4): RoleType resolves through typeof like an A3 registry target and
    // must be an actual non-Flags C# enum whose underlying type is at most 32 bits, with a
    // non-empty SupportedRoles set of declared member values (FWG003 shapes). The declared
    // member snapshot joins the identity (card 1.2-f); a V0 declaration of the capability
    // is the explicit ContractVersion=1 rejection (card 1.7).
    private void ParseTableRoles(AttributeData table)
    {
        bool hasRoleType = table.NamedArguments.Any(static p => p.Key == "RoleType");
        bool hasSupported = table.NamedArguments.Any(static p => p.Key == "SupportedRoles");
        if (!hasRoleType && !hasSupported) return;
        if (_model.ContractVersion != 1)
        {
            Error(FontSchemaGenerator.Shape, "BinaryTable RoleType/SupportedRoles requires ContractVersion=1.");
            return;
        }
        if (!hasRoleType)
        {
            Error(FontSchemaGenerator.Condition, "SupportedRoles requires a declared RoleType: no role domain exists.");
            return;
        }
        if (table.NamedArguments.First(static p => p.Key == "RoleType").Value.Value is not INamedTypeSymbol roleType ||
            roleType.TypeKind == TypeKind.Error)
        {
            Error(FontSchemaGenerator.Layout, "RoleType must name a resolvable enum type.");
            return;
        }
        if (roleType.TypeKind != TypeKind.Enum)
        {
            Error(FontSchemaGenerator.Layout, "RoleType must be an actual C# enum: " + roleType.ToDisplayString());
            return;
        }
        if (roleType.GetAttributes().Any(static attribute => attribute.AttributeClass is { } flags &&
            (flags.Name == "FlagsAttribute" || flags.Name == "Flags") &&
            (flags.ContainingNamespace?.ToDisplayString() ?? "") == "System"))
        {
            Error(FontSchemaGenerator.Layout, "RoleType must not be a Flags enum: " + roleType.ToDisplayString());
            return;
        }
        if (roleType.EnumUnderlyingType is not { } underlying ||
            underlying.SpecialType is not (SpecialType.System_Byte or SpecialType.System_SByte or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32))
        {
            Error(FontSchemaGenerator.Layout, "The RoleType underlying type must not exceed 32 bits: " + roleType.ToDisplayString());
            return;
        }
        var members = roleType.GetMembers().OfType<IFieldSymbol>()
            .Where(static field => field.HasConstantValue)
            .Select(static field => new RoleMemberModel(field.Name,
                Convert.ToInt64(field.ConstantValue, CultureInfo.InvariantCulture)))
            .ToArray();
        if (members.Length == 0)
        {
            Error(FontSchemaGenerator.Layout, "RoleType declares no members: " + roleType.ToDisplayString());
            return;
        }
        var argument = table.NamedArguments.First(static p => p.Key == "SupportedRoles").Value;
        var declared = new HashSet<long>(members.Select(static member => member.Value));
        if (argument.Kind != TypedConstantKind.Array || argument.IsNull || argument.Values.IsDefaultOrEmpty ||
            argument.Values.Any(static value => value.Value is not long))
        {
            Error(FontSchemaGenerator.Layout, "SupportedRoles must be a non-empty array of declared enum member values.");
            return;
        }
        var supported = new List<long>();
        foreach (var value in argument.Values)
        {
            long role = (long)value.Value!;
            if (!declared.Contains(role))
            {
                Error(FontSchemaGenerator.Layout, "SupportedRoles must be a non-empty array of declared enum member values.");
                return;
            }
            supported.Add(role);
        }
        _model.RoleType = roleType.ToDisplayString();
        _model.RoleMembers = members.ToImmutableArray();
        _model.SupportedRoles = supported.Distinct().OrderBy(static value => value).ToImmutableArray();
        foreach (long role in _model.SupportedRoles) _supportedRoleValues.Add(role);
    }

    // Card 1.2-g (spec 4): the whole A4 dispatch contract is FWG005 with no new diagnostic
    // IDs — the Scan value domain, WhenRoles validity, the selector shape, per-slot branch
    // consistency and intersection, and the DefaultRaw remainder-set shape.
    private void ValidateRoleContracts()
    {
        foreach (var reference in _model.References)
        {
            if ((int)reference.Scan is < 0 or > 4 ||
                reference.Scan is ReferenceScan.Follow or ReferenceScan.OpenOnly or ReferenceScan.Manual)
                Error(FontSchemaGenerator.Condition, "ReferenceScan " + reference.Scan + " is outside the A4 value domain {Unspecified, Observe}; the rest follows the A5 structure-scan slice: " + reference.Name);
            if (reference.WhenRoles.Length != 0)
            {
                if (_model.RoleType is null)
                    Error(FontSchemaGenerator.Condition, "WhenRoles requires a declared table RoleType: " + reference.Name);
                else if (reference.WhenRoles.Any(role => !_supportedRoleValues.Contains(role)))
                    Error(FontSchemaGenerator.Condition, "A WhenRoles value is not a supported role of the table: " + reference.Name);
            }
            if (reference.DefaultRaw && reference.TargetFullName is not null)
                Error(FontSchemaGenerator.Condition, "A DefaultRaw reference declares no target: " + reference.Name);
            if (reference.DefaultRaw && reference.Scan != ReferenceScan.Observe)
                Error(FontSchemaGenerator.Condition, "A DefaultRaw reference must declare ReferenceScan.Observe: " + reference.Name);
            if (reference.DefaultRaw && reference.SelectorValues.Length != 0)
                Error(FontSchemaGenerator.Condition, "A DefaultRaw reference declares no selector values of its own: " + reference.Name);
            ValidateSelector(reference);
        }
        var slots = new Dictionary<string, List<ReferenceModel>>(StringComparer.Ordinal);
        foreach (var reference in _model.References)
        {
            string key = reference.ArrayName is null ? reference.OffsetField : reference.ArrayName + ":" + reference.OffsetField;
            if (!slots.TryGetValue(key, out var group)) slots[key] = group = new List<ReferenceModel>();
            group.Add(reference);
        }
        foreach (var slot in slots)
        {
            string key = slot.Key;
            var group = slot.Value;
            // Only branches that use a selector declare one; those must agree on the name
            // (spec 4), while a selector-less branch carries no selector axis at all.
            var declaredSelectors = group.Where(static reference => reference.Selector is not null)
                .Select(static reference => reference.Selector!).Distinct().ToList();
            if (declaredSelectors.Count > 1)
                Error(FontSchemaGenerator.Condition, "Branches of a source slot must share one selector name: " + key);
            string? selector = declaredSelectors.Count == 0 ? null : declaredSelectors[0];
            var defaults = group.Where(static reference => reference.DefaultRaw).ToList();
            var branches = group.Where(static reference => !reference.DefaultRaw).ToList();
            if (defaults.Count > 1)
                Error(FontSchemaGenerator.Condition, "A source slot declares at most one DefaultRaw branch: " + key);
            if (defaults.Count != 0)
            {
                // Spec 4: the remainder branch writes the slot's common selector; a slot
                // without any selector cannot host a DefaultRaw branch at all.
                if (selector is null)
                    Error(FontSchemaGenerator.Condition, "A slot without a common selector cannot declare a DefaultRaw branch: " + key);
                else if (defaults.Any(reference => reference.Selector is null))
                    Error(FontSchemaGenerator.Condition, "A DefaultRaw branch must declare the common selector of its slot: " + key);
                if (branches.Count == 0)
                    Error(FontSchemaGenerator.Condition, "A DefaultRaw branch requires at least one non-default explicit branch in its slot: " + key);
            }
            if (branches.Any(reference => reference.Selector is not null && reference.SelectorValues.Length == 0))
                Error(FontSchemaGenerator.Condition, "An explicit branch declares a non-empty selector value set: " + key);
            for (int i = 0; i < branches.Count; i++)
                for (int j = 0; j < i; j++)
                    if (BranchesIntersect(branches[i], branches[j]))
                        Error(FontSchemaGenerator.Condition, "Branches of a source slot overlap on the version, role and selector intersection: " + key);
        }
    }

    // Spec 4: a selector is exactly one unconditional unsigned fixed-position scalar of
    // this view; unknown names stay FWG002, every other shape is FWG005 (card 1.2-g).
    private void ValidateSelector(ReferenceModel reference)
    {
        if (reference.Selector is not { } name) return;
        if (_scalars.TryGetValue(name, out var selector))
        {
            if (!selector.IsUnsigned || selector.WhenVersion >= 0 ||
                selector.Position.Base is not null || selector.Position.Count is not null)
                Error(FontSchemaGenerator.Condition, "A selector must be one unconditional unsigned fixed-position scalar: " + name);
        }
        else if (_derived.ContainsKey(name) || _computeds.ContainsKey(name))
            Error(FontSchemaGenerator.Condition, "A selector must be one unconditional unsigned fixed-position scalar: " + name);
        else
            Error(FontSchemaGenerator.Reference, "Unknown selector reference: " + name);
    }

    // The A4 intersection rule (spec 4): A4 references declare no version conditions
    // (OQ-6), so two branches overlap when their effective role sets (empty = every
    // supported role) and selector value sets both intersect. DefaultRaw branches never
    // participate (card 1.2-d). Without a table RoleType the role axis is empty and two
    // condition-free branches stay non-overlapping, preserving existing schemas.
    private bool BranchesIntersect(ReferenceModel left, ReferenceModel right)
    {
        var leftRoles = left.WhenRoles.Length == 0 ? _model.SupportedRoles : left.WhenRoles;
        var rightRoles = right.WhenRoles.Length == 0 ? _model.SupportedRoles : right.WhenRoles;
        if (!leftRoles.Any(rightRoles.Contains)) return false;
        if (left.Selector is null || right.Selector is null) return true;
        return left.SelectorValues.Any(right.SelectorValues.Contains);
    }

    // WhenVersions domains (spec 2.1/4, card 1.2-b, FWG005): a finite activation domain
    // needs a declared version field and stays inside its supported versions.
    private void ValidateConditionDomain(ImmutableArray<uint> whenVersions, string owner)
    {
        if (_model.VersionField is null || _model.SupportedVersions.IsEmpty)
            Error(FontSchemaGenerator.Condition, owner + " declares WhenVersions but the table has no supported version field.");
        else if (whenVersions.Any(v => !_model.SupportedVersions.Contains(v)))
            Error(FontSchemaGenerator.Condition, owner + " declares a WhenVersions value outside the supported versions.");
    }

    // Spec 2.2: an array count is an unsigned Count scalar, a Semantic=Count derived in
    // the exact rawCount-CountSubtract form, or a Semantic=Count computed. A conditional
    // count source cannot coexist with the array's own WhenVersions domain.
    private void ValidateArrayCount(ArrayModel array)
    {
        if (_scalars.TryGetValue(array.Count, out var scalar))
        {
            if (!scalar.IsUnsigned || scalar.Semantic != NumberSemantic.Count)
                Error(FontSchemaGenerator.Reference, "Array count must reference an unsigned Count scalar: " + array.Count);
            if (array.WhenVersions.Length != 0 && scalar.WhenVersion >= 0)
                Error(FontSchemaGenerator.Condition, "Array " + array.Name + " cannot combine WhenVersions with a WhenVersion-conditioned count.");
            return;
        }
        if (_derived.TryGetValue(array.Count, out var derived))
        {
            if (derived.Semantic != NumberSemantic.Count)
                Error(FontSchemaGenerator.Reference, "Array count must reference an unsigned Count scalar: " + array.Count);
            else if (derived.Expression.Base is not null || derived.Expression.Constant != 0 ||
                derived.Expression.Stride != 1 || derived.Expression.Count is null)
                Error(FontSchemaGenerator.Layout, "A declarative derived count must be the rawCount-CountSubtract form: " + array.Count);
            if (array.WhenVersions.Length != 0 && derived.WhenVersion >= 0)
                Error(FontSchemaGenerator.Condition, "Array " + array.Name + " cannot combine WhenVersions with a WhenVersion-conditioned count.");
            return;
        }
        if (_computeds.TryGetValue(array.Count, out var computed))
        {
            if (computed.Semantic != NumberSemantic.Count)
                Error(FontSchemaGenerator.Reference, "Array count must reference an unsigned Count scalar: " + array.Count);
            return;
        }
        Error(FontSchemaGenerator.Reference, "Array count must reference an unsigned Count scalar: " + array.Count);
    }

    // Spec 2.3/2.2 and card 1.2-c: input existence (FWG002), dependency paths (FWG005),
    // value-shape rules (FWG003) and hook binding (FWG006).
    private void ValidateComputed(ComputedModel computed)
    {
        if ((int)computed.Semantic is < 0 or > 3 || !_fieldIds.Contains(computed.DiagnosticFieldId))
            Error(FontSchemaGenerator.Layout, "Invalid computed semantic or FieldId: " + computed.Name);
        if (computed.Semantic == NumberSemantic.FixedPoint)
            Error(FontSchemaGenerator.Layout, "A computed semantic must be Plain, Count or Offset: " + computed.Name);
        if (computed.Semantic == NumberSemantic.Count && (!computed.HasExplicitDiagnosticField || computed.DiagnosticFieldId == 0))
            Error(FontSchemaGenerator.Layout, "A count computed requires an explicit non-None diagnostic field: " + computed.Name);
        if (computed.WhenVersions.Length != 0)
            ValidateConditionDomain(computed.WhenVersions, "Computed " + computed.Name);
        foreach (var input in computed.Inputs)
        {
            if (input == "@length") continue;
            if (!_scalars.ContainsKey(input) && !_derived.ContainsKey(input) && !_computeds.ContainsKey(input))
            {
                Error(FontSchemaGenerator.Reference, "Unknown computed input: " + input);
                continue;
            }
            // A conditional input must carry its real value on every version where the
            // referring computed is active (card 1.2-c: dependencies on the use path).
            if (computed.WhenVersions.Length != 0 && ConditionalDomain(input) is { Length: > 0 } domain &&
                !computed.WhenVersions.All(domain.Contains))
                Error(FontSchemaGenerator.Condition, "Computed " + computed.Name + " uses an input outside its activation path: " + input);
        }
        if (computed.DiagnosticSource is { } source)
        {
            if (_scalars.TryGetValue(source, out var origin))
            {
                if (origin.WhenVersion >= 0 || origin.Position.Base is not null || origin.Position.Count is not null)
                    Error(FontSchemaGenerator.Condition, "DiagnosticSource must be an unconditional fixed-position scalar: " + source);
            }
            else if (_derived.ContainsKey(source) || _computeds.ContainsKey(source))
                // Known but not a scalar: derived/computed sources are refused as
                // FWG005 (card 1.2-c), while unknown names stay FWG002.
                Error(FontSchemaGenerator.Condition, "DiagnosticSource must be an unconditional fixed-position scalar: " + source);
            else
                Error(FontSchemaGenerator.Reference, "Unknown DiagnosticSource reference: " + source);
        }
        ValidateHook(computed);
    }

    // The versions where the named value carries its declared (non-fallback) value,
    // or null when it is unconditional. Empty conditional sets cannot occur.
    private uint[]? ConditionalDomain(string name)
    {
        if (_scalars.TryGetValue(name, out var scalar))
            return scalar.WhenVersion >= 0 ? new[] { (uint)scalar.WhenVersion } : null;
        if (_derived.TryGetValue(name, out var derived))
            return derived.WhenVersion >= 0 ? new[] { (uint)derived.WhenVersion } : null;
        if (_computeds.TryGetValue(name, out var computed))
            return computed.WhenVersions.IsEmpty ? null : computed.WhenVersions.ToArray();
        return null;
    }

    // Spec 2.3: the hook is one private static non-generic method in the same partial
    // with the frozen signature (inputs in declared order, DataLocation, out ulong,
    // out FontDiagnostic, ReadStatus return).
    private void ValidateHook(ComputedModel computed)
    {
        var methods = _symbol.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.Name == computed.Method).ToArray();
        if (methods.Length != 1)
        {
            Error(FontSchemaGenerator.Hook, "A computed hook must bind to exactly one method: " + computed.Method);
            return;
        }
        var method = methods[0];
        if (method.IsGenericMethod || method.TypeParameters.Length != 0 ||
            method.DeclaredAccessibility != Accessibility.Private || !method.IsStatic ||
            !IsCoreType(method.ReturnType, "ReadStatus"))
        {
            Error(FontSchemaGenerator.Hook, "A computed hook must be a private static ReadStatus method: " + computed.Method);
            return;
        }
        if (method.Parameters.Length != computed.Inputs.Length + 3)
        {
            Error(FontSchemaGenerator.Hook, "A computed hook must take its inputs, a DataLocation and two out values: " + computed.Method);
            return;
        }
        for (int i = 0; i < computed.Inputs.Length; i++)
        {
            var parameter = method.Parameters[i];
            if (parameter.RefKind != RefKind.None || !IsInputType(parameter.Type, computed.Inputs[i]))
            {
                Error(FontSchemaGenerator.Hook, "A computed hook input must match the declared input type: " + computed.Method);
                return;
            }
        }
        var location = method.Parameters[computed.Inputs.Length];
        var value = method.Parameters[computed.Inputs.Length + 1];
        var diagnostic = method.Parameters[computed.Inputs.Length + 2];
        if (location.RefKind != RefKind.None || !IsCoreType(location.Type, "DataLocation") ||
            value.RefKind != RefKind.Out || value.Type.SpecialType != SpecialType.System_UInt64 ||
            diagnostic.RefKind != RefKind.Out || !IsCoreType(diagnostic.Type, "FontDiagnostic"))
            Error(FontSchemaGenerator.Hook, "A computed hook must take a DataLocation and out ulong/out FontDiagnostic: " + computed.Method);
    }

    private bool IsInputType(ITypeSymbol type, string input)
    {
        if (input == "@length") return type.SpecialType == SpecialType.System_Int32;
        if (_scalars.TryGetValue(input, out var scalar))
            return scalar.CsType switch
            {
                "byte" => type.SpecialType == SpecialType.System_Byte,
                "sbyte" => type.SpecialType == SpecialType.System_SByte,
                "ushort" => type.SpecialType == SpecialType.System_UInt16,
                "uint" => type.SpecialType == SpecialType.System_UInt32,
                "short" => type.SpecialType == SpecialType.System_Int16,
                "int" => type.SpecialType == SpecialType.System_Int32,
                "long" => type.SpecialType == SpecialType.System_Int64,
                _ => false
            };
        return type.SpecialType == SpecialType.System_UInt64;
    }

    private static bool IsCoreType(ITypeSymbol type, string name) =>
        type is INamedTypeSymbol named && named.Name == name &&
        (named.ContainingNamespace?.ToDisplayString() ?? "") == "FontWeave.Core";

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
        else if (_computeds.TryGetValue(name, out var computed))
        {
            // Spec 2.3: Maximum is the static upper bound of a computed (the emitted
            // boundary enforces it at runtime); inputs join the dependency graph so
            // cycles through computed inputs are rejected like positional cycles.
            bound = computed.Maximum;
            foreach (var input in computed.Inputs)
                if (input != "@length") Bound(input);
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

    // Maximum is declared ulong; converting through Int64 would overflow above
    // long.MaxValue (assessment §4), so the boxed value is read in its own width.
    private ulong Maximum(AttributeData attribute)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(p => p.Key == "Maximum").Value;
        switch (argument.Value)
        {
            case null: return uint.MaxValue;
            case ulong value: return value;
            case long value when value >= 0: return (ulong)value;
            case uint value: return value;
            case int value when value >= 0: return (ulong)value;
            default:
                Error(FontSchemaGenerator.Layout, "Maximum must be an unsigned value.");
                return uint.MaxValue;
        }
    }

    private static ImmutableArray<string> Inputs(AttributeData attribute)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(p => p.Key == "Inputs").Value;
        if (argument.Kind != TypedConstantKind.Array || argument.IsNull) return ImmutableArray<string>.Empty;
        return argument.Values.Select(static v => v.Value as string ?? "").ToImmutableArray();
    }

    // WhenVersions is stored normalized (distinct, ordered) so activation semantics,
    // identity and emission share one canonical set (card 1.2-b).
    private ImmutableArray<uint> WhenVersions(AttributeData attribute)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(p => p.Key == "WhenVersions").Value;
        if (argument.Kind != TypedConstantKind.Array || argument.IsNull) return ImmutableArray<uint>.Empty;
        var values = argument.Values.Where(static v => v.Value is uint)
            .Select(static v => (uint)v.Value!).Distinct().OrderBy(static v => v).ToImmutableArray();
        if (values.Length != 0 && attribute.NamedArguments.Any(static p => p.Key == "WhenVersion"))
            Error(FontSchemaGenerator.Condition, "WhenVersion and WhenVersions are mutually exclusive.");
        return values;
    }

    // Role/value condition arrays are stored normalized the same way (card 1.2-f): a
    // reordering of WhenRoles or SelectorValues is one identity, not a contract change.
    private static ImmutableArray<long> Longs(AttributeData attribute, string name)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(p => p.Key == name).Value;
        if (argument.Kind != TypedConstantKind.Array || argument.IsNull) return ImmutableArray<long>.Empty;
        return argument.Values.Where(static v => v.Value is long)
            .Select(static v => (long)v.Value!).Distinct().OrderBy(static v => v).ToImmutableArray();
    }

    private static ImmutableArray<uint> SelectorValues(AttributeData attribute)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(p => p.Key == "SelectorValues").Value;
        if (argument.Kind != TypedConstantKind.Array || argument.IsNull) return ImmutableArray<uint>.Empty;
        return argument.Values.Where(static v => v.Value is uint)
            .Select(static v => (uint)v.Value!).Distinct().OrderBy(static v => v).ToImmutableArray();
    }

    // Spec 1.7: new capability arguments on a V0 declaration are an explicit
    // ContractVersion=1 rejection; the capability is not reinterpreted under V0.
    private ImmutableArray<uint> Gate(ImmutableArray<uint> whenVersions)
    {
        if (whenVersions.Length != 0 && _model.ContractVersion != 1)
        {
            Error(FontSchemaGenerator.Shape, "WhenVersions requires ContractVersion=1.");
            return ImmutableArray<uint>.Empty;
        }
        return whenVersions;
    }

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
        // The role contract is identity: enum full name, declared member snapshot and the
        // normalized supported set (card 1.2-f, spec 8.2).
        Value(_model.RoleType);
        Value(_model.RoleMembers.Length);
        for (int i = 0; i < _model.RoleMembers.Length; i++)
        { Value(_model.RoleMembers[i].Name); Value(_model.RoleMembers[i].Value); }
        Value(_model.SupportedRoles.Length); foreach (long role in _model.SupportedRoles) Value(role);
        Value(_model.Scalars.Length);
        for (int i = 0; i < _model.Scalars.Length; i++) { Value(i); Field(_model.Scalars[i]); }
        Value(_model.Derived.Length);
        for (int i = 0; i < _model.Derived.Length; i++)
        { Value(i); Value(_model.Derived[i].Name); Position(_model.Derived[i].Expression); Value(_model.Derived[i].WhenVersion); Value(_model.Derived[i].Otherwise); Value(_model.Derived[i].CountSubtract); Value(_model.Derived[i].Semantic); }
        Value(_model.Computed.Length);
        for (int i = 0; i < _model.Computed.Length; i++)
        {
            var computed = _model.Computed[i];
            Value(i); Value(computed.Name); Value(computed.Method);
            // Inputs order is semantic (hook parameter order) and part of identity.
            Value(computed.Inputs.Length); foreach (string input in computed.Inputs) Value(input);
            Value(computed.Maximum); Value(computed.Semantic); Value(computed.DiagnosticFieldId);
            Value(computed.DiagnosticSource);
            Value(computed.WhenVersions.Length); foreach (uint version in computed.WhenVersions) Value(version);
        }
        Value(_model.Arrays.Length);
        for (int i = 0; i < _model.Arrays.Length; i++)
        {
            var array = _model.Arrays[i];
            Value(i); Value(array.Name); Value(array.Count); Position(array.Start); Value(array.Stride); Value(array.ExternallyPositioned);
            Value(array.WhenVersions.Length); foreach (uint version in array.WhenVersions) Value(version);
            Value(array.Fields.Length);
            for (int j = 0; j < array.Fields.Length; j++) { Value(j); Field(array.Fields[j]); }
        }
        Value(_model.Payloads.Length);
        for (int i = 0; i < _model.Payloads.Length; i++)
        { Value(i); Value(_model.Payloads[i].Array); Value(_model.Payloads[i].Offset); Value(_model.Payloads[i].Length); Value(_model.Payloads[i].Base); Value(_model.Payloads[i].Minimum); }
        Value(_model.References.Length);
        for (int i = 0; i < _model.References.Length; i++)
        {
            var reference = _model.References[i];
            Value(i); Value(reference.Name); Value(reference.OffsetField); Value(reference.ArrayName);
            Value(reference.TargetFullName); Value(reference.Origin); Value(reference.Base); Value(reference.Minimum);
            Value(reference.Length); Value(reference.NullPolicy); Value(reference.ZeroFailure); Value(reference.AllowTargetAtEnd);
            Value(reference.WhenRoles.Length); foreach (long role in reference.WhenRoles) Value(role);
            Value(reference.Selector);
            Value(reference.SelectorValues.Length); foreach (uint value in reference.SelectorValues) Value(value);
            Value(reference.DefaultRaw); Value(reference.TargetRole); Value(reference.ForwardRole); Value(reference.Scan);
        }
        return key.ToString();
    }
}
