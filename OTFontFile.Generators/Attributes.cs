using System;

namespace OTFontFile.Generators;

/// <summary>
/// OpenType field data types
/// </summary>
public enum OTFieldType
{
    /// <summary>16-bit unsigned integer</summary>
    UShort,
    /// <summary>16-bit signed integer</summary>
    Short,
    /// <summary>32-bit unsigned integer</summary>
    UInt,
    /// <summary>32-bit signed integer</summary>
    Int,
    /// <summary>64-bit signed integer</summary>
    Long,
    /// <summary>32-bit fixed-point (16.16)</summary>
    Fixed,
    /// <summary>16-bit offset</summary>
    Offset16,
    /// <summary>32-bit offset</summary>
    Offset32,
    /// <summary>8-bit signed integer</summary>
    SByte,
    /// <summary>8-bit unsigned integer</summary>
    Byte,
    /// <summary>4-byte tag</summary>
    Tag
}

/// <summary>
/// Marks a class as an OpenType table for source generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class OTTableAttribute : Attribute
{
    public string Tag { get; }
    
    public OTTableAttribute(string tag)
    {
        Tag = tag;
    }
}

/// <summary>
/// Marks a field in an OT table for source generation.
/// The field will get a property accessor generated.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class OTFieldAttribute : Attribute
{
    public int Offset { get; }
    public OTFieldType Type { get; }
    
    public OTFieldAttribute(int offset, OTFieldType type)
    {
        Offset = offset;
        Type = type;
    }
}

/// <summary>
/// Marks the table as having a cache class for modification/generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class OTCacheableAttribute : Attribute
{
    /// <summary>
    /// Fixed size of the table in bytes. 0 if variable length.
    /// </summary>
    public int FixedSize { get; set; }
}
