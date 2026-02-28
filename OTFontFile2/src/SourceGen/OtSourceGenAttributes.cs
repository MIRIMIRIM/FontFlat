// NOTE: These attributes/enums are used by OTFontFile2's Roslyn source generators.
// They are internal and not intended as part of the public API surface.
#nullable enable

using System;

namespace OTFontFile2.SourceGen;

internal enum OtFieldKind
{
    UInt16,
    Int16,
    UInt32,
    Int32,
    UInt64,
    Int64,
    Fixed1616,
    Byte,
    SByte,
    Tag,
    Bytes,
    UInt24
}

internal enum OtTableBuilderMode
{
    ByteArray,
    Streaming
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
internal sealed class OtTableAttribute : Attribute
{
    public OtTableAttribute(string tag, int minLength)
    {
        Tag = tag;
        MinLength = minLength;
    }

    public string Tag { get; }
    public int MinLength { get; }

    public bool GenerateTryCreate { get; set; } = true;
    public bool GenerateStorage { get; set; } = true;
    public bool GenerateBuilder { get; set; }
    public string? BuilderName { get; set; }
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
internal sealed class OtSubTableAttribute : Attribute
{
    public OtSubTableAttribute(int minLength) => MinLength = minLength;

    public int MinLength { get; }

    public bool GenerateTryCreate { get; set; } = true;
    public bool GenerateStorage { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
internal sealed class OtFieldAttribute : Attribute
{
    public OtFieldAttribute(string name, OtFieldKind kind, int offset)
    {
        Name = name;
        Kind = kind;
        Offset = offset;
    }

    public string Name { get; }
    public OtFieldKind Kind { get; }
    public int Offset { get; }

    public int Length { get; set; }
    public bool InView { get; set; } = true;
    public bool InBuilder { get; set; } = true;

    public bool HasDefaultValue { get; set; }
    public long DefaultValue { get; set; }

    public byte PadByte { get; set; }
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
internal sealed class OtTagOffsetRecordArrayAttribute : Attribute
{
    public OtTagOffsetRecordArrayAttribute(string name, int recordsOffset)
    {
        Name = name;
        RecordsOffset = recordsOffset;
    }

    public string Name { get; }
    public int RecordsOffset { get; }

    /// <summary>The name of the count property (defaults to &lt;Name&gt;Count).</summary>
    public string? CountPropertyName { get; set; }

    /// <summary>If true, reads an Offset32 (Tag+UInt32) record; otherwise Offset16.</summary>
    public bool Offset32 { get; set; }

    /// <summary>
    /// Optional subtable type to generate resolvers:
    /// <c>TryGet&lt;Name&gt;(&lt;Name&gt;Record record, out T value)</c> and
    /// <c>TryGet&lt;Name&gt;(int index, out T value)</c>.
    /// </summary>
    public Type? SubTableType { get; set; }

    /// <summary>
    /// Optional override for the generated <c>out</c> parameter name when generating resolvers.
    /// Defaults to a lower-camel form of <see cref="Name"/>.
    /// </summary>
    public string? OutParameterName { get; set; }
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
internal sealed class OtUInt16ArrayAttribute : Attribute
{
    public OtUInt16ArrayAttribute(string name, int valuesOffset)
    {
        Name = name;
        ValuesOffset = valuesOffset;
    }

    public string Name { get; }
    public int ValuesOffset { get; }

    /// <summary>
    /// Optional C# expression for the values offset (relative to the start of this struct).
    /// When specified, the generator uses this instead of <see cref="ValuesOffset"/>.
    /// </summary>
    public string? ValuesOffsetExpression { get; set; }

    /// <summary>
    /// Optional C# expression for the bounds length to validate against instead of <c>_table.Length</c>.
    /// The expression is interpreted as a length relative to the start of this struct (i.e. end = base + length).
    /// </summary>
    public string? BoundsLengthExpression { get; set; }

    /// <summary>The name of the count property (defaults to &lt;Name&gt;Count).</summary>
    public string? CountPropertyName { get; set; }

    /// <summary>
     /// Optional override for the generated <c>out</c> parameter name. Defaults to a lower-camel form of <see cref="Name"/>.
    /// </summary>
    public string? OutParameterName { get; set; }

    /// <summary>
    /// Adjustment applied to the raw count value before bounds checks (e.g. -1 for arrays with length = Count-1).
    /// </summary>
    public int CountAdjustment { get; set; }
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
internal sealed class OtUInt32ArrayAttribute : Attribute
{
    public OtUInt32ArrayAttribute(string name, int valuesOffset)
    {
        Name = name;
        ValuesOffset = valuesOffset;
    }

    public string Name { get; }
    public int ValuesOffset { get; }

    /// <summary>
    /// Optional C# expression for the values offset (relative to the start of this struct).
    /// When specified, the generator uses this instead of <see cref="ValuesOffset"/>.
    /// </summary>
    public string? ValuesOffsetExpression { get; set; }

    /// <summary>
    /// Optional C# expression for the bounds length to validate against instead of <c>_table.Length</c>.
    /// The expression is interpreted as a length relative to the start of this struct (i.e. end = base + length).
    /// </summary>
    public string? BoundsLengthExpression { get; set; }

    /// <summary>The name of the count property (defaults to &lt;Name&gt;Count).</summary>
    public string? CountPropertyName { get; set; }

    /// <summary>
     /// Optional override for the generated <c>out</c> parameter name. Defaults to a lower-camel form of <see cref="Name"/>.
    /// </summary>
    public string? OutParameterName { get; set; }

    /// <summary>
    /// Adjustment applied to the raw count value before bounds checks (e.g. -1 for arrays with length = Count-1).
    /// </summary>
    public int CountAdjustment { get; set; }
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
internal sealed class OtSequentialRecordArrayAttribute : Attribute
{
    public OtSequentialRecordArrayAttribute(string name, int recordsOffset, int recordSize)
    {
        Name = name;
        RecordsOffset = recordsOffset;
        RecordSize = recordSize;
    }

    public string Name { get; }
    public int RecordsOffset { get; }
    public int RecordSize { get; }

    /// <summary>
    /// Optional C# expression for the records offset (relative to the start of this struct).
    /// When specified, the generator uses this instead of <see cref="RecordsOffset"/>.
    /// </summary>
    public string? RecordsOffsetExpression { get; set; }

    /// <summary>
     /// Optional C# expression for the bounds length to validate against instead of <c>_table.Length</c>.
     /// The expression is interpreted as a length relative to the start of this struct (i.e. end = base + length).
     /// </summary>
    public string? BoundsLengthExpression { get; set; }

    /// <summary>
    /// Optional C# expression for the record stride used to advance between records.
    /// When specified, the generator uses this instead of <see cref="RecordSize"/> for indexing (<c>index * stride</c>).
    /// </summary>
    public string? RecordStrideExpression { get; set; }

    /// <summary>The name of the count property (defaults to &lt;Name&gt;Count).</summary>
    public string? CountPropertyName { get; set; }

    /// <summary>The record type name (defaults to &lt;Name&gt;).</summary>
    public string? RecordTypeName { get; set; }

    /// <summary>
    /// Optional override for the generated <c>out</c> parameter name. Defaults to a lower-camel form of <see cref="Name"/>.
    /// </summary>
    public string? OutParameterName { get; set; }
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
internal sealed class OtRecordContextAttribute : Attribute
{
    public OtRecordContextAttribute(string expression) => Expression = expression;

    public string Expression { get; }
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
internal sealed class OtRecordFieldAttribute : Attribute
{
    public OtRecordFieldAttribute(OtFieldKind kind) => Kind = kind;

    public OtFieldKind Kind { get; }
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
internal sealed class OtSubTableOffsetAttribute : Attribute
{
    public OtSubTableOffsetAttribute(string name, string offsetPropertyName, Type subTableType)
    {
        Name = name;
        OffsetPropertyName = offsetPropertyName;
        SubTableType = subTableType;
    }

    /// <summary>Generated method name is <c>TryGet&lt;Name&gt;</c>.</summary>
    public string Name { get; }

    /// <summary>The name of the offset property (a generated <c>[OtField]</c> accessor).</summary>
    public string OffsetPropertyName { get; }

    /// <summary>The subtable type that exposes <c>TryCreate(TableSlice,int,out T)</c>.</summary>
    public Type SubTableType { get; }

    /// <summary>
    /// Optional override for the generated <c>out</c> parameter name. Defaults to a lower-camel form of <see cref="Name"/>.
    /// </summary>
    public string? OutParameterName { get; set; }

    /// <summary>
    /// If true, the offset is relative to the start of the table (not the start of this sub-structure).
    /// </summary>
    public bool RelativeToTableStart { get; set; }
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
internal sealed class OtSubTableOffsetArrayAttribute : Attribute
{
    public OtSubTableOffsetArrayAttribute(string name, string offsetArrayName, Type subTableType)
    {
        Name = name;
        OffsetArrayName = offsetArrayName;
        SubTableType = subTableType;
    }

    /// <summary>Generated method name is <c>TryGet&lt;Name&gt;</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// The name of the offset array accessor (a generated <c>[OtUInt16Array]</c> or <c>[OtUInt32Array]</c> accessor).
    /// The generator will call <c>TryGet&lt;OffsetArrayName&gt;(index, out ushort|uint rel)</c>.
    /// </summary>
    public string OffsetArrayName { get; }

    /// <summary>The subtable type that exposes <c>TryCreate(TableSlice,int,out T)</c>.</summary>
    public Type SubTableType { get; }

    /// <summary>
    /// Optional override for the generated <c>out</c> parameter name. Defaults to a lower-camel form of <see cref="Name"/>.
    /// </summary>
    public string? OutParameterName { get; set; }

    /// <summary>
    /// If true, the offset is relative to the start of the table (not the start of this sub-structure).
    /// </summary>
    public bool RelativeToTableStart { get; set; }
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
internal sealed class OtDiscriminantAttribute : Attribute
{
    public OtDiscriminantAttribute(string fieldName) => FieldName = fieldName;

    /// <summary>
    /// The name of an <c>[OtField]</c> accessor on the same struct used as the discriminant (e.g. <c>Format</c>).
    /// </summary>
    public string FieldName { get; }
}

[AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
internal sealed class OtCaseAttribute : Attribute
{
    public OtCaseAttribute(int value, Type variantType)
    {
        Value = value;
        VariantType = variantType;
    }

    public int Value { get; }
    public Type VariantType { get; }

    /// <summary>
    /// Optional suffix for the generated method name <c>TryGet&lt;Name&gt;</c>.
    /// Defaults to the <see cref="VariantType"/> name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional override for the generated <c>out</c> parameter name.
    /// Defaults to a lower-camel form of <see cref="Name"/>.
    /// </summary>
    public string? OutParameterName { get; set; }
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
internal sealed class OtTableBuilderAttribute : Attribute
{
    public OtTableBuilderAttribute(string tag) => Tag = tag;

    public string Tag { get; }

    public OtTableBuilderMode Mode { get; set; } = OtTableBuilderMode.ByteArray;

    public string BuildMethodName { get; set; } = "BuildTable";
}
