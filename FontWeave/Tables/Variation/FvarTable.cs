using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>One VariationAxisRecord: the tag, the raw Fixed min/default/max user
/// coordinates, the flag bits and the axis name ID. Values are the wire's raw Fixed
/// representations (HeadSchema FontRevision philosophy) - normalized conversions
/// belong to the avar evaluator's oracle face, not to the axis authority.</summary>
public sealed class FvarAxis
{
    internal FvarAxis(uint tag, int minValue, int defaultValue, int maxValue,
        ushort flags, ushort axisNameID)
    {
        Tag = tag;
        MinValue = minValue;
        DefaultValue = defaultValue;
        MaxValue = maxValue;
        Flags = flags;
        AxisNameID = axisNameID;
    }

    public uint Tag { get; }
    public int MinValue { get; }
    public int DefaultValue { get; }
    public int MaxValue { get; }
    public ushort Flags { get; }
    public ushort AxisNameID { get; }
}

/// <summary>One InstanceRecord: the subfamily name ID, the (zero) instance flags, the
/// raw Fixed coordinate per axis in axis order and the optional postScriptNameID,
/// whose presence is the instanceSize=4*axisCount+6 form. Coordinates stay raw Fixed
/// user values (card 3.2); instance coordinate ranges and duplicate names are the
/// spec's "should" domain and are observed, never enforced.</summary>
public sealed class FvarInstance
{
    internal FvarInstance(ushort subfamilyNameID, ushort flags, int[] coordinates,
        ushort? postScriptNameID)
    {
        SubfamilyNameID = subfamilyNameID;
        Flags = flags;
        Coordinates = coordinates;
        PostScriptNameID = postScriptNameID;
    }

    public ushort SubfamilyNameID { get; }
    public ushort Flags { get; }
    public IReadOnlyList<int> Coordinates { get; }
    public ushort? PostScriptNameID { get; }
}

/// <summary>Owned fvar 1.0 table: the axis list in wire order and the instance list.
/// This type is the whole variable line's axis-count and axis-order authority
/// (card 1.2-III): avar/STAT validation and the HVAR/GDEF callers take their
/// axisCount from <see cref="AxisCount"/> explicitly, and no face-level assembly
/// exists on top of it. Instances are fully materialized - the witness bound is 64
/// instances over 44 axes (frozen facts R8) - with per-axis laziness left to a real
/// need (the T5-01 OQ-6 registration).</summary>
public sealed class FvarTable
{
    internal FvarTable(IReadOnlyList<FvarAxis> axes, IReadOnlyList<FvarInstance> instances,
        DataLocation location, int consumedLength)
    {
        Axes = axes;
        Instances = instances;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The authoritative axis count: the value every cross-table consumer
    /// feeds to its explicit Validate/TryRead input contract.</summary>
    public int AxisCount => Axes.Count;

    /// <summary>The axes in wire order; the variable line's axis order.</summary>
    public IReadOnlyList<FvarAxis> Axes { get; }

    public IReadOnlyList<FvarInstance> Instances { get; }

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}
