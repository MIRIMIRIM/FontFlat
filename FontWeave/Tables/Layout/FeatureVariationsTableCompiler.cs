using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// Immutable fragment plan (the leaf-compiler shape): one FeatureVariations block is
// one part. The canonical layout is header(8) → record array (8 bytes each) →
// condition sets → condition bodies → FeatureTableSubst bodies → alternate feature
// bodies (contiguous; shared condition sets, substitutions and alternate features
// emit one body and alias their Offset32 targets by instance identity). Every
// record offset is relative to the FeatureVariations start and every internal
// target is Offset32 - the family has no 16-bit internal bound (the FV>64K
// positive's enabler); the budget stays the only width authority.
internal sealed record FeatureVariationsPartPlan(int Length, int ConditionSetsAt,
    int ConditionsAt, int SubstitutionsAt, int AlternatesAt);

public static class FeatureVariationsTableCompiler
{
    /// <summary>One Condition format 1 (the only writable format - condition format
    /// mixing is the registered finite refusal, OQ-T4-04-11): the raw F2DOT14 pair
    /// with min≤max hard. axisCount is the explicit fvar axis authority (T5-02's
    /// form); a negative value is the axis-free conditional first-class input.</summary>
    public static ReadStatus TryCreateCondition(ushort axisIndex, short minValue, short maxValue,
        int axisCount, out ConditionTable? condition, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateCondition(axisIndex, minValue, maxValue, axisCount, out condition, out diagnostic,
            new BuildBudget(limits), location);

    internal static ReadStatus TryCreateCondition(ushort axisIndex, short minValue, short maxValue,
        int axisCount, out ConditionTable? condition, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location = default)
    {
        condition = null;
        if (!budget.Work(1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (axisCount >= 0 && axisIndex >= axisCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.AtLocal(DataField.Payload, ConditionLayout.AxisIndexAt), out diagnostic,
                axisIndex, (ulong)axisCount);
        if (minValue > maxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, ConditionLayout.MaxValueAt), out diagnostic,
                unchecked((uint)(ushort)minValue), unchecked((uint)(ushort)maxValue));
        if (!budget.Buffer(8, location, out diagnostic)) return ReadStatus.ResourceLimit;
        condition = new(1, axisIndex, minValue, maxValue, location, ConditionLayout.HeaderLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One ConditionSet over caller-supplied conditions (same source/face
    /// identity; an empty set is the legal universal condition).</summary>
    public static ReadStatus TryCreateConditionSet(IReadOnlyList<ConditionTable> conditions,
        out ConditionSetTable? conditionSet, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateConditionSet(conditions, out conditionSet, out diagnostic,
            new BuildBudget(limits), location);

    internal static ReadStatus TryCreateConditionSet(IReadOnlyList<ConditionTable> conditions,
        out ConditionSetTable? conditionSet, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        conditionSet = null;
        if (conditions.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic,
                (ulong)conditions.Count, (ulong)ushort.MaxValue);
        if (!budget.Work(conditions.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = new ConditionTable[conditions.Count];
        long consumed = ConditionSetLayout.HeaderLength +
            ConditionSetLayout.OffsetStride * (long)conditions.Count;
        for (int i = 0; i < conditions.Count; i++)
        {
            ConditionTable? condition = conditions[i];
            if (condition is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, ConditionSetLayout.OffsetsAt +
                        (ulong)i * ConditionSetLayout.OffsetStride, i), out diagnostic);
            if (condition.Location.SourceId != location.SourceId ||
                condition.Location.FaceIndex != location.FaceIndex)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    condition.Location, out diagnostic);
            consumed += condition.ConsumedLength;
            copy[i] = condition;
        }
        if (!budget.Buffer(4L * conditions.Count + 8L * conditions.Count, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        conditionSet = new(copy, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One FTS alternate feature payload: the same LayoutFeature contract
    /// with tag fixed to 0 (the record's own tag lives on the FeatureList row this
    /// substitution replaces, not in the FTS block), so the tag-encoding check does
    /// not apply. The lookup-index domain is the explicit lookup count input.</summary>
    public static ReadStatus TryCreateAlternateFeature(ushort paramsOffsetRaw,
        ReadOnlySpan<ushort> lookupIndices, int lookupCount, out LayoutFeature? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateAlternateFeature(paramsOffsetRaw, lookupIndices, lookupCount, out model,
            out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreateAlternateFeature(ushort paramsOffsetRaw,
        ReadOnlySpan<ushort> lookupIndices, int lookupCount, out LayoutFeature? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default)
    {
        model = null;
        ReadStatus status = LayoutListsTableCompiler.CheckCount(lookupIndices.Length, location,
            LayoutFeatureLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(lookupIndices.Length + 1, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; i < lookupIndices.Length; i++)
        {
            if (lookupCount >= 0 && lookupIndices[i] >= lookupCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload, LayoutFeatureLayout.HeaderLength +
                        (ulong)i * LayoutFeatureLayout.IndexStride, i), out diagnostic,
                    lookupIndices[i], (ulong)lookupCount);
        }
        if (!budget.Buffer(2L * lookupIndices.Length + 16, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        model = new(0, paramsOffsetRaw, lookupIndices.ToArray(), location,
            LayoutFeatureLayout.HeaderLength + LayoutFeatureLayout.IndexStride * lookupIndices.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One FeatureTableSubst over caller-supplied records (same source/face;
    /// the alternate features carry tag 0 - the replaced row owns the tag). The
    /// featureIndex domain is the explicit FeatureList count input (negative
    /// disables, the conditional form).</summary>
    public static ReadStatus TryCreateFeatureTableSubst(
        IReadOnlyList<(ushort FeatureIndex, LayoutFeature Alternate)> substitutions, int featureCount,
        out FeatureTableSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateFeatureTableSubst(substitutions, featureCount, out model, out diagnostic,
            new BuildBudget(limits), location);

    internal static ReadStatus TryCreateFeatureTableSubst(
        IReadOnlyList<(ushort FeatureIndex, LayoutFeature Alternate)> substitutions, int featureCount,
        out FeatureTableSubstTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        if (substitutions.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, FeatureTableSubstLayout.SubstitutionCountAt),
                out diagnostic, (ulong)substitutions.Count, (ulong)ushort.MaxValue);
        if (!budget.Work(substitutions.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = new FeatureSubstitutionRecord[substitutions.Count];
        long consumed = FeatureTableSubstLayout.HeaderLength +
            FeatureTableSubstLayout.RecordStride * (long)substitutions.Count;
        for (int i = 0; i < substitutions.Count; i++)
        {
            var entry = substitutions[i];
            if (entry.Alternate is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, FeatureTableSubstLayout.RecordsAt +
                        (ulong)i * FeatureTableSubstLayout.RecordStride, i), out diagnostic);
            if (featureCount >= 0 && entry.FeatureIndex >= featureCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload, FeatureTableSubstLayout.RecordsAt +
                        (ulong)i * FeatureTableSubstLayout.RecordStride, i), out diagnostic,
                    entry.FeatureIndex, (ulong)featureCount);
            if (entry.Alternate.Location.SourceId != location.SourceId ||
                entry.Alternate.Location.FaceIndex != location.FaceIndex)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    entry.Alternate.Location, out diagnostic);
            consumed += entry.Alternate.ConsumedLength;
            copy[i] = new(entry.FeatureIndex, entry.Alternate);
        }
        if (!budget.Buffer(6L * substitutions.Count + 4L * substitutions.Count, location,
            out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(copy, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The explicit FeatureVariations factory: caller-supplied records over
    /// the same source/face. NULL condition set (universal) and NULL substitution
    /// (no-op) are the spec's legal per-record semantics, not corruption.</summary>
    public static ReadStatus TryCreate(IReadOnlyList<FeatureVariationRecord> records,
        out FeatureVariationsTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(records, out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(IReadOnlyList<FeatureVariationRecord> records,
        out FeatureVariationsTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        // The record count is the family's UInt32 word; the plan-level width authority
        // stays the budget (no 16-bit internal bound exists in this family).
        if (!budget.Work(records.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = new FeatureVariationRecord[records.Count];
        long consumed = FeatureVariationsLayout.HeaderLength +
            FeatureVariationsLayout.RecordStride * (long)records.Count;
        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (record.ConditionSet is not null &&
                (record.ConditionSet.Location.SourceId != location.SourceId ||
                    record.ConditionSet.Location.FaceIndex != location.FaceIndex))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    record.ConditionSet.Location, out diagnostic);
            if (record.Substitution is not null &&
                (record.Substitution.Location.SourceId != location.SourceId ||
                    record.Substitution.Location.FaceIndex != location.FaceIndex))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    record.Substitution.Location, out diagnostic);
            copy[i] = record;
        }
        if (!budget.Buffer(8L * records.Count + 16, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        // The owned extent is the plan's canonical width (TryPlanSubtables recomputes
        // it exactly); the model-level value charges the header + records floor plus
        // one pass over the shared bodies the factory was handed.
        foreach (var record in copy)
        {
            if (record.ConditionSet is not null) consumed += record.ConditionSet.ConsumedLength;
            if (record.Substitution is not null)
            {
                consumed += record.Substitution.ConsumedLength;
                foreach (var entry in record.Substitution.Records)
                    consumed += entry.Alternate.ConsumedLength;
            }
        }
        model = new(copy, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The T4-04 edit APIs (the write invariant: an edited model must be
    // spec-conforming). Single-slot replacements only - record/rule/count changes
    // are the registered finite refusal (OQ-T4-04-11).

    /// <summary>One condition's F2DOT14 range replacement: min≤max hard, the axis
    /// domain re-checked against the caller's axisCount (negative = axis-free).</summary>
    public static ReadStatus TryWithConditionRange(this FeatureVariationsTable model,
        int recordIndex, int conditionIndex, short minValue, short maxValue, int axisCount,
        out FeatureVariationsTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(model.ConsumedLength, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (recordIndex < 0 || recordIndex >= model.Records.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                model.Location.AtLocal(DataField.Count, FeatureVariationsLayout.RecordsAt),
                out diagnostic, unchecked((ulong)(long)recordIndex), (ulong)model.Records.Count);
        var set = model.Records[recordIndex].ConditionSet;
        if (set is null)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                model.Location.AtLocal(DataField.Offset, FeatureVariationsLayout.RecordsAt +
                    (ulong)recordIndex * FeatureVariationsLayout.RecordStride, recordIndex),
                out diagnostic, 0UL, 1UL);
        if (conditionIndex < 0 || conditionIndex >= set.Conditions.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                set.Location.AtLocal(DataField.Count, ConditionSetLayout.OffsetsAt), out diagnostic,
                unchecked((ulong)(long)conditionIndex), (ulong)set.Conditions.Count);
        var original = set.Conditions[conditionIndex];
        ReadStatus status = TryCreateCondition(original.AxisIndex, minValue, maxValue, axisCount,
            out var condition, out diagnostic, budget, original.Location);
        if (status != ReadStatus.Done) return status;
        var conditions = new ConditionTable[set.Conditions.Count];
        for (int i = 0; i < conditions.Length; i++)
            conditions[i] = i == conditionIndex ? condition! : set.Conditions[i];
        var updatedSet = new ConditionSetTable(conditions, set.Location, set.ConsumedLength);
        return model.WithConditionSet(recordIndex, updatedSet, out updated, out diagnostic, budget);
    }

    /// <summary>One substitution's target replacement: the alternate payload and the
    /// featureIndex swap in place; the record count cannot change. The strictly
    /// increasing featureIndex order is hard here (the edit must stay
    /// spec-conforming against the neighbouring rows; OQ-T4-04-8's edit-face rule),
    /// with the domain against the caller's explicit featureCount (negative
    /// disables).</summary>
    public static ReadStatus TryWithFeatureTableSubstTarget(this FeatureVariationsTable model,
        int recordIndex, int substitutionIndex, ushort featureIndex, LayoutFeature alternate,
        int featureCount, out FeatureVariationsTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(model.ConsumedLength, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (recordIndex < 0 || recordIndex >= model.Records.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                model.Location.AtLocal(DataField.Count, FeatureVariationsLayout.RecordsAt),
                out diagnostic, unchecked((ulong)(long)recordIndex), (ulong)model.Records.Count);
        var subst = model.Records[recordIndex].Substitution;
        if (subst is null)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                model.Location.AtLocal(DataField.Offset, FeatureVariationsLayout.RecordsAt +
                    FeatureVariationsLayout.RecordStride * (ulong)recordIndex +
                    FeatureVariationsLayout.RecordStride - 4, recordIndex),
                out diagnostic, 0UL, 1UL);
        if (substitutionIndex < 0 || substitutionIndex >= subst.Records.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                subst.Location.AtLocal(DataField.Count, FeatureTableSubstLayout.SubstitutionCountAt),
                out diagnostic, unchecked((ulong)(long)substitutionIndex), (ulong)subst.Records.Count);
        if (alternate is null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                subst.Location.AtLocal(DataField.Payload, FeatureTableSubstLayout.RecordsAt +
                    (ulong)substitutionIndex * FeatureTableSubstLayout.RecordStride,
                    substitutionIndex), out diagnostic);
        if (alternate.Location.SourceId != model.Location.SourceId ||
            alternate.Location.FaceIndex != model.Location.FaceIndex)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                alternate.Location, out diagnostic);
        if (featureCount >= 0 && featureIndex >= featureCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                subst.Location.AtLocal(DataField.Payload, FeatureTableSubstLayout.RecordsAt +
                    (ulong)substitutionIndex * FeatureTableSubstLayout.RecordStride,
                    substitutionIndex), out diagnostic, featureIndex, (ulong)featureCount);
        // The kept-order rule: the replacement index must sit strictly between the
        // neighbouring rows' indices (a duplicate or descent refuses InvalidValue).
        if (substitutionIndex > 0 && featureIndex <= subst.Records[substitutionIndex - 1].FeatureIndex)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                subst.Location.AtLocal(DataField.Payload, FeatureTableSubstLayout.RecordsAt +
                    (ulong)substitutionIndex * FeatureTableSubstLayout.RecordStride,
                    substitutionIndex), out diagnostic, featureIndex,
                    (ulong)subst.Records[substitutionIndex - 1].FeatureIndex);
        if (substitutionIndex + 1 < subst.Records.Count &&
            featureIndex >= subst.Records[substitutionIndex + 1].FeatureIndex)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                subst.Location.AtLocal(DataField.Payload, FeatureTableSubstLayout.RecordsAt +
                    (ulong)substitutionIndex * FeatureTableSubstLayout.RecordStride,
                    substitutionIndex), out diagnostic, featureIndex,
                    (ulong)subst.Records[substitutionIndex + 1].FeatureIndex);
        if (!budget.Work(1, alternate.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = new FeatureSubstitutionRecord[subst.Records.Count];
        for (int i = 0; i < records.Length; i++)
            records[i] = i == substitutionIndex
                ? new(featureIndex, alternate)
                : subst.Records[i];
        var updatedSubst = new FeatureTableSubstTable(records, subst.Location, subst.ConsumedLength);
        return model.WithSubstitution(recordIndex, updatedSubst, out updated, out diagnostic, budget);
    }

    public static ReadStatus TryCompile(FeatureVariationsTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(FeatureVariationsTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(plan!.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(FeatureVariationsTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlanSubtables(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < plan!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus TryPlanSubtables(FeatureVariationsTable model,
        out FeatureVariationsPartPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        if (!budget.Work(model.RecordCount, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        long cursor = FeatureVariationsLayout.RecordsAt +
            FeatureVariationsLayout.RecordStride * (long)model.RecordCount;
        long conditionSetsAt = cursor;
        var setTargets = new Dictionary<ConditionSetTable, long>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < model.RecordCount; i++)
        {
            var set = model.Records[i].ConditionSet;
            if (set is null || setTargets.ContainsKey(set)) continue;
            setTargets.Add(set, cursor);
            cursor += ConditionSetLayout.HeaderLength +
                ConditionSetLayout.OffsetStride * set.Conditions.Count;
            if (!budget.Work(1, set.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        }
        long conditionsAt = cursor;
        var conditionTargets = new Dictionary<ConditionTable, long>(ReferenceEqualityComparer.Instance);
        foreach (var record in model.Records)
        {
            if (record.ConditionSet is null) continue;
            foreach (var condition in record.ConditionSet.Conditions)
            {
                if (conditionTargets.ContainsKey(condition)) continue;
                conditionTargets.Add(condition, cursor);
                cursor += condition.ConsumedLength;
                if (!budget.Work(1, condition.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            }
        }
        long substitutionsAt = cursor;
        var substTargets = new Dictionary<FeatureTableSubstTable, long>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < model.RecordCount; i++)
        {
            var subst = model.Records[i].Substitution;
            if (subst is null || substTargets.ContainsKey(subst)) continue;
            substTargets.Add(subst, cursor);
            cursor += FeatureTableSubstLayout.HeaderLength +
                FeatureTableSubstLayout.RecordStride * subst.Records.Count;
            if (!budget.Work(1, subst.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        }
        long alternatesAt = cursor;
        var alternateTargets = new Dictionary<LayoutFeature, long>(ReferenceEqualityComparer.Instance);
        foreach (var record in model.Records)
        {
            if (record.Substitution is null) continue;
            foreach (var entry in record.Substitution.Records)
            {
                if (alternateTargets.ContainsKey(entry.Alternate.PayloadOwner)) continue;
                alternateTargets.Add(entry.Alternate.PayloadOwner, cursor);
                cursor += entry.Alternate.ConsumedLength;
                if (!budget.Work(1, entry.Alternate.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            }
        }
        if (!budget.Output(cursor, model.Location, out diagnostic) ||
            !budget.Work(cursor, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plan = new((int)cursor, (int)conditionSetsAt, (int)conditionsAt,
            (int)substitutionsAt, (int)alternatesAt);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only a fully preflighted plan and destination reach this path. The header
    // version/count words and the hand-shared body words go through the generated
    // writers with RequireWritten; every Offset32 fixup value is plan arithmetic
    // with no width bound to refuse.
    internal static void WritePlanned(FeatureVariationsTable model, FeatureVariationsPartPlan plan,
        Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(FeatureVariationsView.TryWriteVersion(destination,
            GsubLayout.Version10, out _, location));
        CoverageTableCompiler.RequireWritten(FeatureVariationsView.TryWriteFeatureVariationRecordCount(
            destination, unchecked((uint)model.RecordCount), out _, location));
        var setTargets = new Dictionary<ConditionSetTable, long>(ReferenceEqualityComparer.Instance);
        var substTargets = new Dictionary<FeatureTableSubstTable, long>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < model.RecordCount; i++)
        {
            var record = model.Records[i];
            if (record.ConditionSet is not null && !setTargets.ContainsKey(record.ConditionSet))
                setTargets.Add(record.ConditionSet, 0);
            if (record.Substitution is not null && !substTargets.ContainsKey(record.Substitution))
                substTargets.Add(record.Substitution, 0);
        }
        // The body regions in plan order; the per-instance targets fill in as each
        // body's first record claims it.
        long setCursor = plan.ConditionSetsAt;
        foreach (var set in setTargets.Keys)
        {
            setTargets[set] = setCursor;
            setCursor += ConditionSetLayout.HeaderLength +
                ConditionSetLayout.OffsetStride * set.Conditions.Count;
        }
        long conditionCursor = plan.ConditionsAt;
        var conditionTargets = new Dictionary<ConditionTable, long>(ReferenceEqualityComparer.Instance);
        foreach (var record in model.Records)
        {
            if (record.ConditionSet is null) continue;
            foreach (var condition in record.ConditionSet.Conditions)
            {
                if (conditionTargets.ContainsKey(condition)) continue;
                conditionTargets.Add(condition, conditionCursor);
                conditionCursor += condition.ConsumedLength;
            }
        }
        long substCursor = plan.SubstitutionsAt;
        foreach (var subst in substTargets.Keys)
        {
            substTargets[subst] = substCursor;
            substCursor += FeatureTableSubstLayout.HeaderLength +
                FeatureTableSubstLayout.RecordStride * subst.Records.Count;
        }
        long alternateCursor = plan.AlternatesAt;
        var alternateTargets = new Dictionary<LayoutFeature, long>(ReferenceEqualityComparer.Instance);
        foreach (var record in model.Records)
        {
            if (record.Substitution is null) continue;
            foreach (var entry in record.Substitution.Records)
            {
                var owner = entry.Alternate.PayloadOwner;
                if (alternateTargets.ContainsKey(owner)) continue;
                alternateTargets.Add(owner, alternateCursor);
                alternateCursor += entry.Alternate.ConsumedLength;
            }
        }
        for (int i = 0; i < model.RecordCount; i++)
        {
            var record = model.Records[i];
            uint setTarget = record.ConditionSet is null
                ? 0u
                : (uint)setTargets[record.ConditionSet];
            uint substTarget = record.Substitution is null
                ? 0u
                : (uint)substTargets[record.Substitution];
            CoverageTableCompiler.RequireWritten(FeatureVariationsView.TryWriteFeatureVariationRecordAt(
                destination, FeatureVariationsLayout.RecordsAt +
                    (ulong)i * FeatureVariationsLayout.RecordStride,
                new FeatureVariationsView.FeatureVariationRecordData(i, setTarget, substTarget),
                out _, location));
        }
        foreach (var (set, setAt) in setTargets)
        {
            var setLocation = set.Location;
            var bytes = destination.Slice((int)setAt);
            CoverageTableCompiler.RequireWritten(ConditionSetView.TryWriteConditionCount(bytes,
                (ushort)set.Conditions.Count, out _, setLocation));
            for (int c = 0; c < set.Conditions.Count; c++)
            {
                var condition = set.Conditions[c];
                CoverageTableCompiler.RequireWritten(ConditionSetView.TryWriteConditionOffsetRecordAt(
                    bytes, ConditionSetLayout.OffsetsAt + (ulong)c * ConditionSetLayout.OffsetStride,
                    new ConditionSetView.ConditionOffsetRecordData(c,
                        (uint)(conditionTargets[condition] - setAt)), out _, setLocation));
            }
        }
        foreach (var (condition, conditionAt) in conditionTargets)
        {
            var bytes = destination.Slice((int)conditionAt);
            var conditionLocation = condition.Location;
            CoverageTableCompiler.RequireWritten(ConditionView.TryWriteFormat(bytes, condition.Format,
                out _, conditionLocation));
            CoverageTableCompiler.RequireWritten(ConditionView.TryWriteAxisIndex(bytes, condition.AxisIndex,
                out _, conditionLocation));
            CoverageTableCompiler.RequireWritten(ConditionView.TryWriteFilterRangeMinValue(bytes,
                condition.MinValue, out _, conditionLocation));
            CoverageTableCompiler.RequireWritten(ConditionView.TryWriteFilterRangeMaxValue(bytes,
                condition.MaxValue, out _, conditionLocation));
        }
        foreach (var (subst, substAt) in substTargets)
        {
            var substLocation = subst.Location;
            var bytes = destination.Slice((int)substAt);
            CoverageTableCompiler.RequireWritten(FeatureTableSubstView.TryWriteVersion(bytes,
                GsubLayout.Version10, out _, substLocation));
            CoverageTableCompiler.RequireWritten(FeatureTableSubstView.TryWriteSubstitutionCount(bytes,
                (ushort)subst.Records.Count, out _, substLocation));
            for (int s = 0; s < subst.Records.Count; s++)
            {
                var entry = subst.Records[s];
                CoverageTableCompiler.RequireWritten(FeatureTableSubstView.TryWriteSubstitutionRecordAt(
                    bytes, FeatureTableSubstLayout.RecordsAt +
                        (ulong)s * FeatureTableSubstLayout.RecordStride,
                    new FeatureTableSubstView.SubstitutionRecordData(s, entry.FeatureIndex,
                        (uint)(alternateTargets[entry.Alternate.PayloadOwner] - substAt)), out _, substLocation));
            }
        }
        foreach (var (alternate, alternateAt) in alternateTargets)
        {
            var bytes = destination.Slice((int)alternateAt);
            var alternateLocation = alternate.Location;
            CoverageTableCompiler.RequireWritten(LayoutFeatureView.TryWriteParamsOffsetRaw(bytes,
                alternate.ParamsOffsetRaw, out _, alternateLocation));
            CoverageTableCompiler.RequireWritten(LayoutFeatureView.TryWriteLookupCount(bytes,
                (ushort)alternate.LookupIndices.Length, out _, alternateLocation));
            for (int l = 0; l < alternate.LookupIndices.Length; l++)
                CoverageTableCompiler.RequireWritten(LayoutFeatureView.TryWriteLookupIndexAt(bytes,
                    LayoutFeatureLayout.HeaderLength + (ulong)l * LayoutFeatureLayout.IndexStride,
                    new LayoutFeatureView.LookupIndexData(l, alternate.LookupIndices[l]), out _,
                    alternateLocation));
        }
    }
}
