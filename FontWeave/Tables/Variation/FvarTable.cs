using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>One VariationAxisRecord: the tag, the raw Fixed min/default/max user
/// coordinates, the flag bits and the axis name ID. Values are the wire's raw Fixed
/// representations (HeadSchema FontRevision philosophy) - normalized conversions
/// belong to the avar evaluator's oracle face, not to the axis authority.</summary>
public sealed class FvarAxis
{
    /// <summary>Creates one axis record; the write contracts (range order, flag mask,
    /// name-ID domain) are enforced by <see cref="FvarTableCompiler.TryCreate"/>.</summary>
    public FvarAxis(uint tag, int minValue, int defaultValue, int maxValue,
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
    /// <summary>Creates one instance record; the coordinates vector must carry one
    /// raw Fixed per axis and the postScriptNameID presence must be uniform over the
    /// whole instance set (the factory enforces both).</summary>
    public FvarInstance(ushort subfamilyNameID, ushort flags, int[] coordinates,
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

    /// <summary>Replaces one axis's min/default/max triple; the write contract
    /// (min<=default<=max) is enforced by the factory rebuild (card 4). The axis set,
    /// order and instance block never move (OQ-T5-02-6 finite refusal for anything
    /// beyond value replacement).</summary>
    public ReadStatus TryWithAxisRange(int axisIndex, int minValue, int defaultValue,
        int maxValue, out FvarTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        if (IsOutside(axisIndex, Axes.Count))
            return AxisIndexFail(axisIndex, out diagnostic);
        var axes = Replace(Axes, axisIndex, new FvarAxis(Axes[axisIndex].Tag, minValue,
            defaultValue, maxValue, Axes[axisIndex].Flags, Axes[axisIndex].AxisNameID));
        return Create(axes, Instances, out updated, out diagnostic, limits);
    }

    /// <summary>Replaces one axis's flag bits; the reserved mask stays a refusal
    /// domain (HIDDEN_AXIS = 0x0001 is the only legal bit).</summary>
    public ReadStatus TryWithAxisFlags(int axisIndex, ushort flags, out FvarTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (IsOutside(axisIndex, Axes.Count))
            return AxisIndexFail(axisIndex, out diagnostic);
        var axes = Replace(Axes, axisIndex, new FvarAxis(Axes[axisIndex].Tag, Axes[axisIndex].MinValue,
            Axes[axisIndex].DefaultValue, Axes[axisIndex].MaxValue, flags, Axes[axisIndex].AxisNameID));
        return Create(axes, Instances, out updated, out diagnostic, limits);
    }

    /// <summary>Replaces one axis's name ID; the wire domain ([256,32768)) is the
    /// factory's refusal domain - no name-table existence is consulted (card 1.2-II).</summary>
    public ReadStatus TryWithAxisNameID(int axisIndex, ushort axisNameID, out FvarTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (IsOutside(axisIndex, Axes.Count))
            return AxisIndexFail(axisIndex, out diagnostic);
        var axes = Replace(Axes, axisIndex, new FvarAxis(Axes[axisIndex].Tag, Axes[axisIndex].MinValue,
            Axes[axisIndex].DefaultValue, Axes[axisIndex].MaxValue, Axes[axisIndex].Flags,
            axisNameID));
        return Create(axes, Instances, out updated, out diagnostic, limits);
    }

    /// <summary>Replaces one instance's coordinate vector; the count must equal the
    /// axis count (the axis-count authority is preserved).</summary>
    public ReadStatus TryWithInstanceCoordinates(int instanceIndex,
        IReadOnlyList<int> coordinates, out FvarTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        ArgumentNullException.ThrowIfNull(coordinates);
        if (IsOutside(instanceIndex, Instances.Count))
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, FvarLayout.InstanceCountAt, instanceIndex),
                out diagnostic, unchecked((ulong)(long)instanceIndex), (ulong)Instances.Count);
        FvarInstance previous = Instances[instanceIndex];
        var instance = new FvarInstance(previous.SubfamilyNameID, previous.Flags,
            coordinates.ToArray(), previous.PostScriptNameID);
        return Create(Replace(Axes, -1, null), Replace(Instances, instanceIndex, instance),
            out updated, out diagnostic, limits);
    }

    /// <summary>Replaces one instance's name IDs; flipping the postScriptNameID
    /// presence on a single instance mixes the two instance shapes and refuses
    /// (OQ-T5-02-6: the factory's one-shape rule).</summary>
    public ReadStatus TryWithInstanceNameIDs(int instanceIndex, ushort subfamilyNameID,
        ushort? postScriptNameID, out FvarTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        if (IsOutside(instanceIndex, Instances.Count))
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, FvarLayout.InstanceCountAt, instanceIndex),
                out diagnostic, unchecked((ulong)(long)instanceIndex), (ulong)Instances.Count);
        FvarInstance previous = Instances[instanceIndex];
        var instance = new FvarInstance(subfamilyNameID, previous.Flags,
            previous.Coordinates.ToArray(), postScriptNameID);
        return Create(Replace(Axes, -1, null), Replace(Instances, instanceIndex, instance),
            out updated, out diagnostic, limits);
    }

    private ReadStatus Create(IReadOnlyList<FvarAxis> axes, IReadOnlyList<FvarInstance> instances,
        out FvarTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits) =>
        FvarTableCompiler.TryCreate(axes, instances, out updated, out diagnostic, limits, Location);

    private ReadStatus AxisIndexFail(int axisIndex, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            Location.AtLocal(DataField.Count, FvarLayout.AxisCountAt, axisIndex),
            out diagnostic, unchecked((ulong)(long)axisIndex), (ulong)Axes.Count);

    private static bool IsOutside(int index, int count) => index < 0 || index >= count;

    private static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> source, int index, T? value)
        where T : class
    {
        var copy = new T[source.Count];
        for (int i = 0; i < source.Count; i++)
            copy[i] = i == index ? value! : source[i];
        return copy;
    }
}
