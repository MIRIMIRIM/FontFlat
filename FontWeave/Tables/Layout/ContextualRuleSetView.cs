using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// The shared rule-set view's semantic partial: the rule-body walks for the two wire
// forms. Sequence/ClassSequence rules (types 5 formats 1/2) and Chained rules
// (types 6 formats 1/2) differ in layout, so each walk is explicit and the caller
// picks by the role its reference opened with. Both walks enforce the lookup-index
// graph's domains (SequenceIndex against the rule's own input count - the owner
// relation; LookupListIndex against the caller's lookup count, the explicit input;
// OQ-4) and the declared underflow rule (glyphCount/inputGlyphCount = 0 is the
// negative inputSequence[-1] array, the GC7 rule); an empty record list is the
// legal no-op action (synthetic-pinned, spec carries no prohibition).
public readonly ref partial struct ContextualRuleSetView
{
    /// <summary>Validates every rule body of this set (Sequence/ClassSequence form).
    /// consumed is the set's own extent, for the caller's consumed arithmetic.</summary>
    internal ReadStatus ValidateRules(int lookupCount, BuildBudget budget,
        out int consumed, out FontDiagnostic diagnostic)
    {
        consumed = 0;
        ReadStatus status = TryGetRuleCount(out ushort ruleCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long end = ContextualRuleSetLayout.HeaderLength +
            ContextualRuleSetLayout.OffsetStride * ruleCount;
        for (int r = 0; r < ruleCount; r++)
        {
            status = TryGetRuleOffsetRecord(r, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = CheckRuleOffset(record.RuleOffset, r, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = InspectRule(record.RuleOffset, lookupCount, r, budget,
                out int ruleConsumed, out diagnostic);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, (long)record.RuleOffset + (long)ruleConsumed);
        }
        consumed = (int)end;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Materializes every rule body into the owned set (Sequence/ClassSequence
    /// form).</summary>
    internal ReadStatus ReadRules(int lookupCount, BuildBudget budget,
        out ContextualRuleSet? model, out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = TryGetRuleCount(out ushort ruleCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(ruleCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var rules = ruleCount == 0 ? [] : new ContextualRule[ruleCount];
        long end = ContextualRuleSetLayout.HeaderLength +
            ContextualRuleSetLayout.OffsetStride * ruleCount;
        for (int r = 0; r < ruleCount; r++)
        {
            status = TryGetRuleOffsetRecord(r, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = CheckRuleOffset(record.RuleOffset, r, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadRule(record.RuleOffset, lookupCount, r, budget, out var rule, out diagnostic);
            if (status != ReadStatus.Done) return status;
            rules[r] = rule!;
            end = Math.Max(end, (long)record.RuleOffset + (long)rule!.ConsumedLength);
        }
        if (!budget.Buffer(8L * ruleCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(rules, Location, (int)end);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Validates every rule body of this set (Chained form).</summary>
    internal ReadStatus ValidateChainedRules(int lookupCount, BuildBudget budget,
        out int consumed, out FontDiagnostic diagnostic)
    {
        consumed = 0;
        ReadStatus status = TryGetRuleCount(out ushort ruleCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long end = ContextualRuleSetLayout.HeaderLength +
            ContextualRuleSetLayout.OffsetStride * ruleCount;
        for (int r = 0; r < ruleCount; r++)
        {
            status = TryGetRuleOffsetRecord(r, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = CheckRuleOffset(record.RuleOffset, r, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = InspectChainedRule(record.RuleOffset, lookupCount, r, budget,
                out int ruleConsumed, out diagnostic);
            if (status != ReadStatus.Done) return status;
            end = Math.Max(end, (long)record.RuleOffset + (long)ruleConsumed);
        }
        consumed = (int)end;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Materializes every rule body into the owned set (Chained form).</summary>
    internal ReadStatus ReadChainedRules(int lookupCount, BuildBudget budget,
        out ChainingRuleSet? model, out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = TryGetRuleCount(out ushort ruleCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(ruleCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var rules = ruleCount == 0 ? [] : new ChainingRule[ruleCount];
        long end = ContextualRuleSetLayout.HeaderLength +
            ContextualRuleSetLayout.OffsetStride * ruleCount;
        for (int r = 0; r < ruleCount; r++)
        {
            status = TryGetRuleOffsetRecord(r, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = CheckRuleOffset(record.RuleOffset, r, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadChainedRule(record.RuleOffset, lookupCount, r, budget,
                out var rule, out diagnostic);
            if (status != ReadStatus.Done) return status;
            rules[r] = rule!;
            end = Math.Max(end, (long)record.RuleOffset + (long)rule!.ConsumedLength);
        }
        if (!budget.Buffer(8L * ruleCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(rules, Location, (int)end);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus CheckRuleOffset(ulong offset, int ordinal, out FontDiagnostic diagnostic)
    {
        if (offset < ContextualRuleSetLayout.HeaderLength || offset >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, ContextualRuleSetLayout.HeaderLength +
                    (ulong)ordinal * ContextualRuleSetLayout.OffsetStride, ordinal),
                out diagnostic, offset, (ulong)_data.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One SequenceRule/ClassSequenceRule body: glyphCount(2) + seqLookupCount(2) +
    // input[glyphCount-1] + records[seqLookupCount].
    private ReadStatus InspectRule(ulong at, int lookupCount, int ordinal, BuildBudget budget,
        out int consumed, out FontDiagnostic diagnostic)
    {
        consumed = 0;
        ReadStatus status = MeasureRule(at, ordinal, budget, materialize: false,
            out int glyphCount, out int recordCount, out ulong recordsAt, out ulong end,
            out var _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckRecords(recordsAt, recordCount, glyphCount, lookupCount, ordinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        consumed = (int)(end - at);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadRule(ulong at, int lookupCount, int ordinal, BuildBudget budget,
        out ContextualRule? rule, out FontDiagnostic diagnostic)
    {
        rule = null;
        ReadStatus status = MeasureRule(at, ordinal, budget, materialize: true,
            out int glyphCount, out int recordCount, out ulong recordsAt, out ulong end,
            out var input, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckRecords(recordsAt, recordCount, glyphCount, lookupCount, ordinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var records = recordCount == 0 ? [] : new SequenceLookupRecord[recordCount];
        for (int i = 0; i < recordCount; i++)
        {
            ulong recordAt = recordsAt + 4UL * (ulong)i;
            records[i] = new(ReadWire.U16(_data, (int)recordAt), ReadWire.U16(_data, (int)recordAt + 2));
        }
        rule = new(input!, records, Location.AtLocal(DataField.None, at, ordinal), (int)(end - at));
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus MeasureRule(ulong at, int ordinal, BuildBudget budget, bool materialize,
        out int glyphCount, out int recordCount, out ulong recordsAt, out ulong end,
        out ushort[]? input, out FontDiagnostic diagnostic)
    {
        glyphCount = recordCount = 0;
        recordsAt = end = 0;
        input = null;
        if (at + 4 > (ulong)_data.Length)
            return ShortBody(at, ordinal, out diagnostic);
        glyphCount = ReadWire.U16(_data, (int)at);
        recordCount = ReadWire.U16(_data, (int)at + 2);
        // The declared underflow: inputSequence[glyphCount-1] with glyphCount=0.
        if (glyphCount == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, at, ordinal), out diagnostic, 0UL, 1UL);
        recordsAt = at + 4 + 2UL * (ulong)(glyphCount - 1);
        end = recordsAt + 4UL * (ulong)recordCount;
        if (end > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, recordsAt, ordinal), out diagnostic,
                end, (ulong)_data.Length);
        if (!budget.Work(glyphCount + recordCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (materialize)
        {
            if (!budget.Buffer(2L * (glyphCount - 1) + 8L * recordCount, Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            input = new ushort[glyphCount - 1];
            for (int i = 0; i < input.Length; i++)
                input[i] = ReadWire.U16(_data, (int)at + 4 + 2 * i);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One chained rule body: backtrackGlyphCount(2) + backtrack[bt] +
    // inputGlyphCount(2) + input[inputGlyphCount-1] + lookaheadGlyphCount(2) +
    // lookahead[la] + seqLookupCount(2) + records[seqLookupCount]. The underflow
    // guard lives on the input group only; empty backtrack/lookahead chains are
    // legal (the gsub8 pin carries real empty chains on its type-8 sibling).
    private ReadStatus InspectChainedRule(ulong at, int lookupCount, int ordinal, BuildBudget budget,
        out int consumed, out FontDiagnostic diagnostic)
    {
        consumed = 0;
        ReadStatus status = MeasureChainedRule(at, ordinal, budget, materialize: false,
            out int inputGlyphCount, out int recordCount, out ulong recordsAt, out ulong end,
            out var _, out var _, out var _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckRecords(recordsAt, recordCount, inputGlyphCount, lookupCount, ordinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        consumed = (int)(end - at);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadChainedRule(ulong at, int lookupCount, int ordinal, BuildBudget budget,
        out ChainingRule? rule, out FontDiagnostic diagnostic)
    {
        rule = null;
        ReadStatus status = MeasureChainedRule(at, ordinal, budget, materialize: true,
            out int inputGlyphCount, out int recordCount, out ulong recordsAt, out ulong end,
            out var backtrack, out var input, out var lookahead, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckRecords(recordsAt, recordCount, inputGlyphCount, lookupCount, ordinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var records = recordCount == 0 ? [] : new SequenceLookupRecord[recordCount];
        for (int i = 0; i < recordCount; i++)
        {
            ulong recordAt = recordsAt + 4UL * (ulong)i;
            records[i] = new(ReadWire.U16(_data, (int)recordAt), ReadWire.U16(_data, (int)recordAt + 2));
        }
        rule = new(backtrack!, input!, lookahead!, records,
            Location.AtLocal(DataField.None, at, ordinal), (int)(end - at));
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus MeasureChainedRule(ulong at, int ordinal, BuildBudget budget, bool materialize,
        out int inputGlyphCount, out int recordCount, out ulong recordsAt, out ulong end,
        out ushort[]? backtrack, out ushort[]? input, out ushort[]? lookahead,
        out FontDiagnostic diagnostic)
    {
        backtrack = input = lookahead = null;
        inputGlyphCount = recordCount = 0;
        recordsAt = end = 0;
        int cursor = (int)at;
        // Backtrack group: count + complete sequence (empty is legal).
        if (cursor + 2 > _data.Length) return ShortBody(at, ordinal, out diagnostic);
        int backtrackCount = ReadWire.U16(_data, cursor);
        cursor += 2;
        if (cursor + 2 * backtrackCount > _data.Length) return ShortBody(at, ordinal, out diagnostic);
        if (materialize && backtrackCount != 0)
        {
            backtrack = new ushort[backtrackCount];
            for (int i = 0; i < backtrackCount; i++)
                backtrack[i] = ReadWire.U16(_data, cursor + 2 * i);
        }
        cursor += 2 * backtrackCount;
        // Input group: the count IS inputGlyphCount and the sequence carries
        // inputGlyphCount-1 entries - zero is the declared underflow.
        if (cursor + 2 > _data.Length) return ShortBody(at, ordinal, out diagnostic);
        inputGlyphCount = ReadWire.U16(_data, cursor);
        cursor += 2;
        if (inputGlyphCount == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, (ulong)(cursor - 2), ordinal), out diagnostic,
                0UL, 1UL);
        int inputLength = inputGlyphCount - 1;
        if (cursor + 2 * inputLength > _data.Length) return ShortBody(at, ordinal, out diagnostic);
        if (materialize && inputLength != 0)
        {
            input = new ushort[inputLength];
            for (int i = 0; i < inputLength; i++)
                input[i] = ReadWire.U16(_data, cursor + 2 * i);
        }
        cursor += 2 * inputLength;
        // Lookahead group: count + complete sequence (empty is legal).
        if (cursor + 2 > _data.Length) return ShortBody(at, ordinal, out diagnostic);
        int lookaheadCount = ReadWire.U16(_data, cursor);
        cursor += 2;
        if (cursor + 2 * lookaheadCount > _data.Length) return ShortBody(at, ordinal, out diagnostic);
        if (materialize && lookaheadCount != 0)
        {
            lookahead = new ushort[lookaheadCount];
            for (int i = 0; i < lookaheadCount; i++)
                lookahead[i] = ReadWire.U16(_data, cursor + 2 * i);
        }
        cursor += 2 * lookaheadCount;
        // Record list: the no-op empty list is legal.
        if (cursor + 2 > _data.Length) return ShortBody(at, ordinal, out diagnostic);
        recordCount = ReadWire.U16(_data, cursor);
        cursor += 2;
        recordsAt = (ulong)cursor;
        end = recordsAt + 4UL * (ulong)recordCount;
        if (end > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, recordsAt, ordinal), out diagnostic,
                end, (ulong)_data.Length);
        if (!budget.Work(inputGlyphCount + backtrackCount + lookaheadCount + recordCount,
            Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ShortBody(ulong at, int ordinal, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
            Location.AtLocal(DataField.Count, at, ordinal), out diagnostic,
            (ulong)at + 4UL, (ulong)_data.Length);

    private ReadStatus CheckRecords(ulong recordsAt, int recordCount, int glyphCount,
        int lookupCount, int ordinal, out FontDiagnostic diagnostic)
    {
        for (int i = 0; i < recordCount; i++)
        {
            ulong at = recordsAt + 4UL * (ulong)i;
            var candidate = new SequenceLookupRecord(ReadWire.U16(_data, (int)at),
                ReadWire.U16(_data, (int)at + 2));
            if (candidate.SequenceIndex >= glyphCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.Payload, at, ordinal), out diagnostic,
                    candidate.SequenceIndex, (ulong)glyphCount);
            if (lookupCount >= 0 && candidate.LookupListIndex >= lookupCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.Payload, at + 2, ordinal), out diagnostic,
                    candidate.LookupListIndex, (ulong)lookupCount);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
