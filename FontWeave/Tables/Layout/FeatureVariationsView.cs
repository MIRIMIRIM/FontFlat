using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// The FeatureVariations family's typed read face (T4-04 section 3.4): the records,
// condition sets and substitutions materialize with the direct-leaf conventions
// (Work on structure visits, Buffer on materialized arrays). The version gates are
// native (1.0 only for FV/FTS); the condition format word is deliberately not a
// gate - the semantic layer's Validate refuses unknown formats while the evaluator
// tolerates them (the OQ-7 layering).
public readonly ref partial struct FeatureVariationsView
{
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Inspect(out _, out diagnostic, budget);
    }

    internal ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget) =>
        Inspect(out _, out diagnostic, budget);

    public ReadStatus TryRead(out FeatureVariationsTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ReadOwned(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out FeatureVariationsTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        ReadOwned(out model, out diagnostic, budget);

    internal ReadStatus Inspect(out int consumed, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        consumed = 0;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetFeatureVariationRecordCount(out uint recordCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        consumed = (int)Math.Min(int.MaxValue, FeatureVariationsLayout.RecordsAt + 8L * recordCount);
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (uint r = 0; r < recordCount; r++)
        {
            status = TryOpenConditionSet((int)r, out var conditionSet, out bool setPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (setPresent)
            {
                status = conditionSet!.Validate(out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            status = TryOpenFeatureTableSubst((int)r, out var substitution, out bool substPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (substPresent)
            {
                status = substitution!.Validate(out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadOwned(out FeatureVariationsTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetFeatureVariationRecordCount(out uint recordCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(16L * recordCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = new FeatureVariationRecord[recordCount];
        long consumed = FeatureVariationsLayout.RecordsAt + 8L * recordCount;
        for (uint r = 0; r < recordCount; r++)
        {
            ConditionSetTable? conditionSet = null;
            status = TryOpenConditionSet((int)r, out var conditionSetView, out bool setPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (setPresent)
            {
                status = conditionSetView!.TryRead(out conditionSet, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            }
            FeatureTableSubstTable? substitution = null;
            status = TryOpenFeatureTableSubst((int)r, out var substitutionView, out bool substPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (substPresent)
            {
                status = substitutionView!.TryRead(out substitution, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                consumed = Math.Max(consumed, RecordTarget(r, fts: true) + substitution!.ConsumedLength);
            }
            if (setPresent)
                consumed = Math.Max(consumed, RecordTarget(r, fts: false) + conditionSet!.ConsumedLength);
            records[r] = new(conditionSet, substitution);
        }
        model = new(records, Location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The validate-mode FTS featureIndex domain walk (strictly increasing,
    // duplicate-free, inside the caller's FeatureList count) - the same checks the
    // owned ValidateTable carries, without materializing (T4 discipline).
    internal ReadStatus ValidateFeatureDomains(int featureCount, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryGetFeatureVariationRecordCount(out uint recordCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (uint r = 0; r < recordCount; r++)
        {
            status = TryOpenFeatureTableSubst((int)r, out var substitution, out bool substPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!substPresent) continue;
            status = substitution!.TryGetSubstitutionCount(out ushort substitutionCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!budget.Work(substitutionCount, substitution!.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            // The ordering is OQ-T4-04-8's flipped observation (real test-rvrn
            // witness); only the featureIndex domain is the hard check.
            for (int i = 0; i < substitutionCount; i++)
            {
                status = substitution.TryGetSubstitutionRecord(i, out var record, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (featureCount >= 0 && record.FeatureIndex >= featureCount)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        substitution.Location.AtLocal(DataField.Payload,
                            FeatureTableSubstLayout.RecordsAt + (ulong)i * FeatureTableSubstLayout.RecordStride,
                            i), out diagnostic, record.FeatureIndex, (ulong)featureCount);
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private long RecordTarget(uint ordinal, bool fts)
    {
        ReadStatus status = TryGetFeatureVariationRecord((int)ordinal, out var record, out _);
        if (status != ReadStatus.Done) return 0;
        return fts ? record.FeatureTableSubstOffset : record.ConditionSetOffset;
    }
}

public readonly ref partial struct ConditionSetView
{
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(out diagnostic, budget);
    }

    internal ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryGetConditionCount(out ushort conditionCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(conditionCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < conditionCount; i++)
        {
            status = TryOpenCondition(i, out var condition, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            _ = condition;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(out ConditionSetTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out ConditionSetTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetConditionCount(out ushort conditionCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(conditionCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (!budget.Buffer(16L * conditionCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var conditions = conditionCount == 0 ? [] : new ConditionTable[conditionCount];
        long end = ConditionSetLayout.OffsetsAt + ConditionSetLayout.OffsetStride * (long)conditionCount;
        for (int i = 0; i < conditionCount; i++)
        {
            status = TryOpenCondition(i, out var conditionView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = conditionView.TryRead(out var condition, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            conditions[i] = condition!;
            end = Math.Max(end, ConditionTarget(i) + ConditionLayout.HeaderLength);
        }
        if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(conditions, Location, (int)end);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private long ConditionTarget(int ordinal)
    {
        ReadStatus status = TryGetConditionOffsetRecord(ordinal, out var record, out _);
        if (status != ReadStatus.Done) return 0;
        return ConditionSetLayout.OffsetsAt + record.ConditionOffset;
    }
}

public readonly ref partial struct ConditionView
{
    public ReadStatus TryRead(out ConditionTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out ConditionTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetFormat(out ushort format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetAxisIndex(out ushort axisIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetFilterRangeMinValue(out short minValue, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetFilterRangeMaxValue(out short maxValue, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(format, axisIndex, minValue, maxValue, Location, ConditionLayout.HeaderLength);
        diagnostic = default;
        return ReadStatus.Done;
    }
}

// The FTS typed target: an alternate feature payload under the SAME LayoutFeature
// contract. The record's own tag lives on the FeatureList row this substitution
// replaces, not in the FTS block, so the materialized model keeps tag 0 and the
// consumer applies its known tag through AtRecord.
public readonly ref partial struct LayoutFeatureView
{
    internal ReadStatus TryReadFeature(uint tag, int lookupCount, out LayoutFeature? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetLookupCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int consumed = LayoutFeatureLayout.HeaderLength + LayoutFeatureLayout.IndexStride * count;
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (!budget.Buffer(2L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var indices = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            status = TryGetLookupIndex(i, out ushort index, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (lookupCount >= 0)
            {
                status = LayoutReadWire.Index(index, lookupCount, Location,
                    LayoutFeatureLayout.HeaderLength + (ulong)i * LayoutFeatureLayout.IndexStride, i,
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            indices[i] = index;
        }
        model = new(tag, ParamsOffsetRaw, indices, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly ref partial struct FeatureTableSubstView
{
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(out diagnostic, budget);
    }

    internal ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetSubstitutionCount(out ushort substitutionCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(substitutionCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < substitutionCount; i++)
        {
            status = TryOpenAlternateFeature(i, out var feature, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            _ = feature;
        }
        // OQ-T4-04-8 flipped at implementation, real witness: the lean draft made the
        // "ordered increasing featureIndex" MUST a hard InvalidValue refusal, and the
        // frozen test-rvrn pin carries real FTS records [3, 1, 4] - a shipped fixture
        // violating the ordering that HarfBuzz tolerates. The ordering is therefore an
        // observation on the read face (the records stay in wire order); the write
        // face keeps the spec-conforming requirement for caller-supplied parts.
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(out FeatureTableSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out FeatureTableSubstTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetSubstitutionCount(out ushort substitutionCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(16L * substitutionCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = substitutionCount == 0 ? [] : new FeatureSubstitutionRecord[substitutionCount];
        for (int i = 0; i < substitutionCount; i++)
        {
            status = TryOpenAlternateFeature(i, out var feature, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = feature!.TryReadFeature(0, -1, out var featureModel, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            status = TryGetSubstitutionRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            records[i] = new(record.FeatureIndex, featureModel!);
        }
        long end = FeatureTableSubstLayout.RecordsAt + FeatureTableSubstLayout.RecordStride * (long)substitutionCount;
        if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(records, Location, (int)end);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
