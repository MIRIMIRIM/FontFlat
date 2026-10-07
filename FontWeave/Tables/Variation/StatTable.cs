using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>One STAT AxisRecord: the axis tag, its name ID and the application-facing
/// ordering value (UI composition semantics stay outside this table - card 3.3).</summary>
public sealed class StatDesignAxis
{
    /// <summary>Creates one design-axis record; the DuplicateTag and name-ID
    /// domains are enforced by <see cref="StatTableCompiler.TryCreate"/>.</summary>
    public StatDesignAxis(uint axisTag, ushort axisNameID, ushort axisOrdering)
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
    /// <summary>Creates one axis value in the given format; only the fields of that
    /// format are consumed (the others keep their defaults), and the format 4
    /// combination axis set and row count are part of the write contract.</summary>
    public StatAxisValue(ushort format, ushort flags, ushort valueNameID, ushort axisIndex,
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

    /// <summary>Replaces one design axis's ordering value; tags, name IDs and the
    /// axis set never move (card 4, OQ-T5-02-6).</summary>
    public ReadStatus TryWithAxisOrdering(int axisIndex, ushort axisOrdering,
        out StatTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (axisIndex < 0 || axisIndex >= DesignAxes.Count)
            return AxisIndexFail(axisIndex, DesignAxes.Count, out diagnostic);
        var axes = new StatDesignAxis[DesignAxes.Count];
        for (int i = 0; i < DesignAxes.Count; i++)
        {
            StatDesignAxis axis = DesignAxes[i];
            axes[i] = i == axisIndex ? new(axis.AxisTag, axis.AxisNameID, axisOrdering) : axis;
        }
        return Create(axes, AxisValues, out updated, out diagnostic, limits);
    }

    /// <summary>Replaces one axis value's flag bits.</summary>
    public ReadStatus TryWithAxisValueFlags(int valueIndex, ushort flags,
        out StatTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (IsOutside(valueIndex))
            return ValueIndexFail(valueIndex, out diagnostic);
        return Create(DesignAxes.ToArray(), Replace(valueIndex, WithFlags(AxisValues[valueIndex], flags)),
            out updated, out diagnostic, limits);
    }

    /// <summary>Replaces one axis value's name ID; the wire domain is the factory's
    /// refusal domain - no name-table existence is consulted (card 1.2-II).</summary>
    public ReadStatus TryWithAxisValueNameID(int valueIndex, ushort valueNameID,
        out StatTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (IsOutside(valueIndex))
            return ValueIndexFail(valueIndex, out diagnostic);
        return Create(DesignAxes.ToArray(), Replace(valueIndex, WithNameID(AxisValues[valueIndex], valueNameID)),
            out updated, out diagnostic, limits);
    }

    /// <summary>Replaces one axis value's value face: the value for formats 1 and 3,
    /// the nominal value for format 2 (its range stays), and - with the entry index -
    /// one combination entry's value for format 4, keeping the combination axis set
    /// and row count (card 4).</summary>
    public ReadStatus TryWithAxisValueValue(int valueIndex, int value,
        out StatTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryWithAxisValueValue(valueIndex, null, value, out updated, out diagnostic, limits);

    public ReadStatus TryWithAxisValueValue(int valueIndex, int? entryIndex, int value,
        out StatTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (IsOutside(valueIndex))
            return ValueIndexFail(valueIndex, out diagnostic);
        StatAxisValue previous = AxisValues[valueIndex];
        StatAxisValue replacement;
        switch (previous.Format)
        {
            case 1 or 3:
                replacement = WithValue(previous, value);
                break;
            case 2:
                replacement = WithNominal(previous, value);
                break;
            default:
                if (entryIndex is null || entryIndex.Value < 0 ||
                    entryIndex.Value >= previous.Combination.Count)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.Count, 0, valueIndex), out diagnostic,
                        unchecked((ulong)(long)(entryIndex ?? -1)), (ulong)previous.Combination.Count);
                var combination = new StatCombinationEntry[previous.Combination.Count];
                for (int i = 0; i < combination.Length; i++)
                    combination[i] = i == entryIndex.Value
                        ? new(previous.Combination[i].AxisIndex, value)
                        : previous.Combination[i];
                replacement = WithCombination(previous, combination);
                break;
        }
        return Create(DesignAxes.ToArray(), Replace(valueIndex, replacement),
            out updated, out diagnostic, limits);
    }

    // Edits rebuild through the factory under the source's own version: 1.1/1.2
    // round-trip, while a 1.0 source's edit refuses as the deprecated new-write
    // domain (the compile round-trip of a 1.0 source stays unrestricted, card 4).
    private ReadStatus Create(IReadOnlyList<StatDesignAxis> axes,
        IReadOnlyList<StatAxisValue> values, out StatTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits) =>
        StatTableCompiler.TryCreate(Version, axes, values, ElidedFallbackNameID,
            out updated, out diagnostic, limits, Location);

    private static StatAxisValue WithFlags(StatAxisValue value, ushort flags) =>
        new(value.Format, flags, value.ValueNameID, value.AxisIndex, value.Value,
            value.NominalValue, value.RangeMinValue, value.RangeMaxValue, value.LinkedValue,
            value.Combination.ToArray());

    private static StatAxisValue WithNameID(StatAxisValue value, ushort valueNameID) =>
        new(value.Format, value.Flags, valueNameID, value.AxisIndex, value.Value,
            value.NominalValue, value.RangeMinValue, value.RangeMaxValue, value.LinkedValue,
            value.Combination.ToArray());

    private static StatAxisValue WithValue(StatAxisValue value, int newValue) =>
        new(value.Format, value.Flags, value.ValueNameID, value.AxisIndex, newValue,
            value.NominalValue, value.RangeMinValue, value.RangeMaxValue, value.LinkedValue,
            value.Combination.ToArray());

    private static StatAxisValue WithNominal(StatAxisValue value, int nominal) =>
        new(value.Format, value.Flags, value.ValueNameID, value.AxisIndex, value.Value,
            nominal, value.RangeMinValue, value.RangeMaxValue, value.LinkedValue,
            value.Combination.ToArray());

    private static StatAxisValue WithCombination(StatAxisValue value, StatCombinationEntry[] combination) =>
        new(value.Format, value.Flags, value.ValueNameID, value.AxisIndex, value.Value,
            value.NominalValue, value.RangeMinValue, value.RangeMaxValue, value.LinkedValue,
            combination);

    private bool IsOutside(int valueIndex) => valueIndex < 0 || valueIndex >= AxisValues.Count;

    private IReadOnlyList<StatAxisValue> Replace(int index, StatAxisValue value)
    {
        var copy = new StatAxisValue[AxisValues.Count];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = i == index ? value : AxisValues[i];
        return copy;
    }

    private ReadStatus AxisIndexFail(int index, int count, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            Location.AtLocal(DataField.Count, StatLayout.DesignAxisCountAt, index),
            out diagnostic, unchecked((ulong)(long)index), (ulong)count);

    private ReadStatus ValueIndexFail(int index, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            Location.AtLocal(DataField.Count, StatLayout.AxisValueCountAt, index),
            out diagnostic, unchecked((ulong)(long)index), (ulong)AxisValues.Count);
}
