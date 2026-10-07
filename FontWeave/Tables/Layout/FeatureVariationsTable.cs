using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables.Layout;

/// <summary>One Condition format 1: the raw F2DOT14 filter range against the axis
/// index. Values stay raw (T5-02's raw Fixed philosophy); the normalized comparison
/// is the caller's duty - the condition evaluator consumes caller-supplied
/// normalized values.</summary>
public sealed class ConditionTable
{
    internal ConditionTable(ushort format, ushort axisIndex, short minValue, short maxValue,
        DataLocation location, int consumedLength)
    {
        Format = format; AxisIndex = axisIndex;
        MinValue = minValue; MaxValue = maxValue;
        Location = location; ConsumedLength = consumedLength;
    }

    /// <summary>The condition format; 1 is the only format the frozen corpus carries
    /// and the only one the typed validation accepts (unknown formats are the spec's
    /// tolerance domain - the evaluator reports them as unmatched, OQ-7).</summary>
    public ushort Format { get; }

    public ushort AxisIndex { get; }

    /// <summary>Raw F2DOT14 minimum.</summary>
    public short MinValue { get; }

    /// <summary>Raw F2DOT14 maximum.</summary>
    public short MaxValue { get; }

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}

/// <summary>One ConditionSet: the conditions in wire order, all of which must match
/// (the AND semantics belong to the evaluator). An empty set matches universally
/// (spec text).</summary>
public sealed class ConditionSetTable
{
    private readonly ConditionTable[] _conditions;

    internal ConditionSetTable(ConditionTable[] conditions, DataLocation location, int consumedLength)
    {
        _conditions = conditions;
        Location = location; ConsumedLength = consumedLength;
    }

    public IReadOnlyList<ConditionTable> Conditions => _conditions;

    public int ConditionCount => _conditions.Length;

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}

/// <summary>One feature-substitution record: the feature index replaced and the
/// alternate LayoutFeature payload (a typed target from outside the GSUB leaf
/// family). Records are strictly increasing in featureIndex with no duplicates -
/// the semantic hard check (the STAT format-4 "must be different" precedent).</summary>
public readonly record struct FeatureSubstitutionRecord(ushort FeatureIndex, LayoutFeature Alternate);

/// <summary>Owned FeatureTableSubst: the records in wire order.</summary>
public sealed class FeatureTableSubstTable
{
    private readonly FeatureSubstitutionRecord[] _records;

    internal FeatureTableSubstTable(FeatureSubstitutionRecord[] records,
        DataLocation location, int consumedLength)
    {
        _records = records;
        Location = location; ConsumedLength = consumedLength;
    }

    public IReadOnlyList<FeatureSubstitutionRecord> Records => _records;

    public int SubstitutionCount => _records.Length;

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}

/// <summary>One FeatureVariations record: the condition set (null = the universal
/// condition, matches everything) and the feature-table substitution (null = the
/// no-op record). Both nulls are spec-defined semantics, not corruption.</summary>
public readonly record struct FeatureVariationRecord(ConditionSetTable? ConditionSet,
    FeatureTableSubstTable? Substitution);

/// <summary>Owned FeatureVariations 1.0 (GSUB 1.1's variations block): the records
/// in wire order. The axis authority chain is explicit-input only (T5-02's form):
/// <see cref="ValidateTable(FvarTable?)"/> takes the fvar axis authority from the
/// caller and never infers a face; a null fvar (a static font carrying a v1.1 GSUB)
/// keeps the axis-free checks first-class and makes the axisIndex domain conditional.
/// The first-match-wins record traversal belongs to the runtime domain and is not
/// implemented - this type delivers the table-local single-set evaluation.</summary>
public sealed class FeatureVariationsTable
{
    private readonly FeatureVariationRecord[] _records;

    internal FeatureVariationsTable(FeatureVariationRecord[] records,
        DataLocation location, int consumedLength)
    {
        _records = records;
        Location = location; ConsumedLength = consumedLength;
    }

    public IReadOnlyList<FeatureVariationRecord> Records => _records;

    public int RecordCount => _records.Length;

    public DataLocation Location { get; }

    public int ConsumedLength { get; }

    /// <summary>Single-slot replacement for the condition-range edit (the record
    /// count is the wire layout and cannot change).</summary>
    internal ReadStatus WithConditionSet(int recordIndex, ConditionSetTable conditionSet,
        out FeatureVariationsTable? updated, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        var copy = (FeatureVariationRecord[])_records.Clone();
        copy[recordIndex] = new(conditionSet, _records[recordIndex].Substitution);
        updated = new(copy, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Single-slot replacement for the substitution-target edit.</summary>
    internal ReadStatus WithSubstitution(int recordIndex, FeatureTableSubstTable substitution,
        out FeatureVariationsTable? updated, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        var copy = (FeatureVariationRecord[])_records.Clone();
        copy[recordIndex] = new(_records[recordIndex].ConditionSet, substitution);
        updated = new(copy, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Typed validation against the caller-supplied axis authority: an
    /// axisIndex outside the fvar axis count refuses InvalidIndex; a null fvar
    /// keeps the format/order/feature-domain checks and skips the axis domain
    /// (the conditional-check first-class form).</summary>
    public ReadStatus ValidateTable(FvarTable? fvar, out FontDiagnostic diagnostic,
        BuildLimits? limits = null) =>
        ValidateTable(fvar, null, out diagnostic, limits);

    /// <summary>The featureCount-taking form: the FTS featureIndex domain check
    /// against the LookupList's FeatureList count (the explicit input, the same
    /// discipline as the lookup count).</summary>
    public ReadStatus ValidateTable(FvarTable? fvar, int? featureCount,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(ConsumedLength, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(fvar, featureCount, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(FvarTable? fvar, int? featureCount,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        Validate(fvar, featureCount, out diagnostic, budget);

    internal ReadStatus Validate(FvarTable? fvar, int? featureCount,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        for (int r = 0; r < _records.Length; r++)
        {
            var record = _records[r];
            if (record.ConditionSet is not null)
            {
                var conditions = record.ConditionSet.Conditions;
                if (!budget.Work(conditions.Count, record.ConditionSet.Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                for (int c = 0; c < conditions.Count; c++)
                {
                    ConditionTable condition = conditions[c];
                    if (condition.Format != 1)
                        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                            condition.Location.AtLocal(DataField.Version, 0), out diagnostic,
                            condition.Format);
                    if (fvar is not null && condition.AxisIndex >= fvar.AxisCount)
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            condition.Location.AtLocal(DataField.Payload, ConditionLayout.AxisIndexAt),
                            out diagnostic, condition.AxisIndex, (ulong)fvar.AxisCount);
                    if (condition.MinValue > condition.MaxValue)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            condition.Location.AtLocal(DataField.Payload, ConditionLayout.MaxValueAt),
                            out diagnostic, unchecked((uint)(short)condition.MinValue),
                            unchecked((uint)(short)condition.MaxValue));
                }
            }
            if (record.Substitution is not null)
            {
                var substitutions = record.Substitution.Records;
                if (!budget.Work(substitutions.Count, record.Substitution.Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                // OQ-T4-04-8 flipped (see the view partial): the ordering is a read-face
                // observation; only the featureIndex domain stays a hard check.
                for (int s = 0; s < substitutions.Count; s++)
                {
                    var entry = substitutions[s];
                    if (featureCount is int count && entry.FeatureIndex >= count)
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            record.Substitution.Location.AtLocal(DataField.Payload,
                                FeatureTableSubstLayout.RecordsAt + (ulong)s * FeatureTableSubstLayout.RecordStride,
                                s), out diagnostic, entry.FeatureIndex, (ulong)count);
                }
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The table-local single-set evaluation (OQ-9's lean product face): the
    /// caller supplies the normalized F2DOT14 axis values in fvar axis order; a
    /// NULL condition set or an empty set matches universally (spec text); an
    /// unknown condition format reports matched=false as a tolerated observation,
    /// never an error (OQ-7). An axisIndex outside the caller's vector refuses
    /// InvalidIndex.</summary>
    public ReadStatus TryMatchesConditionSet(int recordIndex, ReadOnlySpan<int> normalizedAxisValues,
        out bool matched, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        matched = false;
        var budget = new BuildBudget(limits);
        if (!budget.Input(ConsumedLength, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (recordIndex < 0 || recordIndex >= _records.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, FeatureVariationsLayout.RecordsAt), out diagnostic,
                unchecked((ulong)(long)recordIndex), (ulong)_records.Length);
        FeatureVariationRecord record = _records[recordIndex];
        if (record.ConditionSet is null)
        {
            matched = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        var conditions = record.ConditionSet.Conditions;
        if (!budget.Work(conditions.Count, record.ConditionSet.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        foreach (ConditionTable condition in conditions)
        {
            if (condition.Format != 1)
            {
                matched = false;
                diagnostic = default;
                return ReadStatus.Done;
            }
            if (condition.AxisIndex >= normalizedAxisValues.Length)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    condition.Location.AtLocal(DataField.Payload, ConditionLayout.AxisIndexAt),
                    out diagnostic, condition.AxisIndex, (ulong)normalizedAxisValues.Length);
            int value = normalizedAxisValues[condition.AxisIndex];
            if (value < condition.MinValue || value > condition.MaxValue)
            {
                matched = false;
                diagnostic = default;
                return ReadStatus.Done;
            }
        }
        matched = true;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
