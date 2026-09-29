using System;
using System.Collections.Immutable;

namespace FontWeave.Generators;

// Build-time only: no syntax/symbol/Compilation references survive into this model.
internal enum WireType { UInt16 = 0, UInt32 = 1, Int16 = 2, Int32 = 3, Int64 = 4 }
internal enum WireEndian { Big = 0, Little = 1 }
internal enum NumberSemantic { Plain = 0, Count = 1, Offset = 2, FixedPoint = 3 }
internal enum WireOrigin { None = 0, Table = 1, NameStorage = 2 }

// A finite affine node: (Base ?? 0) + Constant + (Count ?? 0) * Stride.
// A version branch selects this node or Otherwise. References are resolved and
// checked for cycles and worst-case ulong overflow before emission.
internal sealed class PositionModel
{
    public string? Base { get; set; }
    public ulong Constant { get; set; }
    public string? Count { get; set; }
    public ulong Stride { get; set; } = 1;
}

internal sealed class ScalarModel
{
    public string Name { get; set; } = "";
    public WireType Type { get; set; }
    public WireEndian Endian { get; set; }
    public NumberSemantic Semantic { get; set; }
    public WireOrigin Origin { get; set; }
    public int FieldId { get; set; }
    public ulong Scale { get; set; } = 1;
    public bool ZeroAllowed { get; set; } = true;
    public PositionModel Position { get; set; } = new();
    public long WhenVersion { get; set; } = -1;
    public ulong Otherwise { get; set; }
    public int Width => Type is WireType.UInt16 or WireType.Int16 ? 2 : Type == WireType.Int64 ? 8 : 4;
    public bool IsUnsigned => Type is WireType.UInt16 or WireType.UInt32;
    public string CsType => Type switch { WireType.UInt16 => "ushort", WireType.UInt32 => "uint", WireType.Int16 => "short", WireType.Int64 => "long", _ => "int" };
}

internal sealed class DerivedModel
{
    public string Name { get; set; } = "";
    public PositionModel Expression { get; set; } = new();
    public long WhenVersion { get; set; } = -1;
    public string? Otherwise { get; set; }
}

internal sealed class ArrayModel
{
    // Emits a nested <Name>Data value record and TryGet<Name>(ordinal,...).
    public string Name { get; set; } = "";
    public string Count { get; set; } = "";
    public PositionModel Start { get; set; } = new();
    public int Stride { get; set; }
    public ImmutableArray<ScalarModel> Fields { get; set; } = ImmutableArray<ScalarModel>.Empty;
}

internal sealed class PayloadModel
{
    // Method: TryGet<Array>Payload. Base/Minimum refer to table values;
    // Offset/Length refer to fields of the selected array record.
    public string Array { get; set; } = "";
    public string Offset { get; set; } = "";
    public string Length { get; set; } = "";
    public string Base { get; set; } = "";
    public string Minimum { get; set; } = "";
}

internal sealed class TableModel
{
    public string Namespace { get; set; } = "";
    public string Name { get; set; } = "";
    public uint Tag { get; set; }
    public int MinimumHeader { get; set; }
    public string? VersionField { get; set; }
    public ImmutableArray<uint> SupportedVersions { get; set; } = ImmutableArray<uint>.Empty;
    public ImmutableArray<ScalarModel> Scalars { get; set; } = ImmutableArray<ScalarModel>.Empty;
    public ImmutableArray<DerivedModel> Derived { get; set; } = ImmutableArray<DerivedModel>.Empty;
    public ImmutableArray<ArrayModel> Arrays { get; set; } = ImmutableArray<ArrayModel>.Empty;
    public ImmutableArray<PayloadModel> Payloads { get; set; } = ImmutableArray<PayloadModel>.Empty;
    // Canonical typed-model encoding; used only for incremental equality.
    public string Identity { get; set; } = "";
    public string HintName => (Namespace.Length == 0 ? "" : Namespace + ".") + Name + ".g.cs";
}
