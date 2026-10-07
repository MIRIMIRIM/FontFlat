using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>One STAT AxisRecord: the axis tag, its name ID and the application-facing
/// ordering value (UI composition semantics stay outside this table - card 3.3).</summary>
public sealed class StatDesignAxis
{
    internal StatDesignAxis(uint axisTag, ushort axisNameID, ushort axisOrdering)
    {
        AxisTag = axisTag;
        AxisNameID = axisNameID;
        AxisOrdering = axisOrdering;
    }

    public uint AxisTag { get; }
    public ushort AxisNameID { get; }
    public ushort AxisOrdering { get; }
}

/// <summary>One entry of a format 4 combination row.</summary>
public readonly record struct StatCombinationEntry(ushort AxisIndex, int Value);

/// <summary>One STAT axis value table, format-discriminated (card 3.2): formats 1-3
/// carry the single-axis face (a value; a nominal plus a range; a value plus its
/// linked value) and format 4 carries the combination rows. Fields outside the
/// record's own format keep their type defaults. All Fixed values are raw.</summary>
public sealed class StatAxisValue
{
    internal StatAxisValue(ushort format, ushort flags, ushort valueNameID, ushort axisIndex,
        int value, int nominalValue, int rangeMinValue, int rangeMaxValue, int linkedValue,
        StatCombinationEntry[] combination)
    {
        Format = format;
        Flags = flags;
        ValueNameID = valueNameID;
        AxisIndex = axisIndex;
        Value = value;
        NominalValue = nominalValue;
        RangeMinValue = rangeMinValue;
        RangeMaxValue = rangeMaxValue;
        LinkedValue = linkedValue;
        Combination = combination;
    }

    public ushort Format { get; }
    public ushort Flags { get; }
    public ushort ValueNameID { get; }

    /// <summary>The design-axis index for formats 1-3 (0 for format 4, which carries
    /// per-entry indices).</summary>
    public ushort AxisIndex { get; }

    /// <summary>Formats 1 and 3.</summary>
    public int Value { get; }

    /// <summary>Format 2.</summary>
    public int NominalValue { get; }

    /// <summary>Format 2.</summary>
    public int RangeMinValue { get; }

    /// <summary>Format 2.</summary>
    public int RangeMaxValue { get; }

    /// <summary>Format 3.</summary>
    public int LinkedValue { get; }

    /// <summary>Format 4's combination rows; empty for the single-axis formats.</summary>
    public IReadOnlyList<StatCombinationEntry> Combination { get; }

    /// <summary>Wire length of the value's format; the compiler's canonical measure.</summary>
    internal int WireLength => Format switch
    {
        1 => 2 + StatAxisValueLayout.Format1BodyStride,
        2 => 2 + StatAxisValueLayout.Format2BodyStride,
        3 => 2 + StatAxisValueLayout.Format3BodyStride,
        _ => 2 + StatAxisValueLayout.Format4HeaderStride +
            Combination.Count * StatAxisValueLayout.Format4EntryStride,
    };
}

/// <summary>One observed STAT/fvar name-ID divergence on a shared axis (tag): the
/// name ID the STAT record carries versus the one the fvar axis carries. Observation
/// only - the corpus's positive witnesses refute the hard reading (OQ-T5-02-8,
/// StatView header).</summary>
public readonly record struct StatNameIdMismatch(uint Tag, ushort StatNameID, ushort FvarNameID);

/// <summary>Owned STAT table (T5-02): the version, the design axes, every axis value
/// in offsets-array order, the elided fallback name ID under versions 1.1/1.2 and
/// the observed fvar name-ID divergences. Static fonts are first-class - this model
/// exists with or without an fvar context, and the hard fvar relations (axis-count
/// floor, per-axis tag presence) were already enforced at validation (card 3.3).</summary>
public sealed class StatTable
{
    internal StatTable(uint version, IReadOnlyList<StatDesignAxis> designAxes,
        IReadOnlyList<StatAxisValue> axisValues, ushort? elidedFallbackNameID,
        StatNameIdMismatch[] nameIdMismatches, DataLocation location, int consumedLength)
    {
        Version = version;
        DesignAxes = designAxes;
        AxisValues = axisValues;
        ElidedFallbackNameID = elidedFallbackNameID;
        NameIdMismatches = nameIdMismatches;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public uint Version { get; }

    public IReadOnlyList<StatDesignAxis> DesignAxes { get; }

    /// <summary>The axis value tables in offsets-array order (not wire physical
    /// order - the canonical emission re-places out-of-order sources, the
    /// AnekBangla wire-equivalent face, card 4).</summary>
    public IReadOnlyList<StatAxisValue> AxisValues { get; }

    /// <summary>Versions 1.1/1.2 only; null under the deprecated 1.0 header.</summary>
    public ushort? ElidedFallbackNameID { get; }

    /// <summary>The observed STAT/fvar name-ID divergences on shared axes (fvar axis
    /// order); empty for static fonts and agreeing variable fonts (OQ-T5-02-8).</summary>
    public StatNameIdMismatch[] NameIdMismatches { get; }

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}
