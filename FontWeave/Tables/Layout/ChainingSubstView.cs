using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// The chaining view's semantic partial (T4-04 section 3): formats 1/2 walk their
// rule sets through the shared rule-set view's chained-body walks; format 3's
// input/lookahead coverage chains and record list sit behind computed starts
// (OQ-5's hand-walked boundary) and are walked here with the same graph-domain
// checks and coverage-cache convention as the declared paths.
public readonly ref partial struct ChainingSubstView
{
    /// <summary>Typed whole-subtable validation. lookupCount is the LookupList count
    /// in wire order, or a negative value when the caller has no list context.</summary>
    public ReadStatus Validate(int numGlyphs, int lookupCount,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Inspect(numGlyphs, lookupCount, out _, out diagnostic, budget, cache: null, 0, default);
    }

    public ReadStatus TryRead(int numGlyphs, int lookupCount, out ChainingSubstTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ReadOwned(numGlyphs, lookupCount, out model, out diagnostic, budget, null, 0, default);
    }

    internal ReadStatus TryRead(int numGlyphs, int lookupCount, out ChainingSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, Dictionary<int, CoverageTable>? coverageCache,
        int subtableStart, DataLocation lookupLocation) =>
        ReadOwned(numGlyphs, lookupCount, out model, out diagnostic, budget, coverageCache,
            subtableStart, lookupLocation);

    internal ReadStatus Validate(int numGlyphs, int lookupCount, out FontDiagnostic diagnostic,
        BuildBudget budget) =>
        Inspect(numGlyphs, lookupCount, out _, out diagnostic, budget, cache: null, 0, default);

    internal ReadStatus Validate(int numGlyphs, int lookupCount, out FontDiagnostic diagnostic,
        BuildBudget budget, Dictionary<int, CoverageTable>? coverageCache,
        int subtableStart, DataLocation lookupLocation) =>
        Inspect(numGlyphs, lookupCount, out _, out diagnostic, budget, coverageCache,
            subtableStart, lookupLocation);

    private ReadStatus Inspect(int numGlyphs, int lookupCount, out int consumed,
        out FontDiagnostic diagnostic, BuildBudget budget,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation)
    {
        consumed = 0;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetSubstFormat(out ushort format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        switch (format)
        {
            case 1: return InspectRuleSets(numGlyphs, lookupCount, classForm: false, out consumed,
                out diagnostic, budget);
            case 2: return InspectRuleSets(numGlyphs, lookupCount, classForm: true, out consumed,
                out diagnostic, budget);
            case 3: return InspectFormat3(numGlyphs, lookupCount, out consumed,
                out diagnostic, budget, cache, subtableStart, lookupLocation);
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    Location.AtLocal(DataField.Version, 0), out diagnostic, format);
        }
    }

    private ReadStatus InspectRuleSets(int numGlyphs, int lookupCount, bool classForm,
        out int consumed, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        consumed = 0;
        ReadStatus status;
        int setCount, coverageCount = 0, coverageLength = 0;
        if (classForm)
        {
            status = TryGetChainedClassSeqRuleSetCount(out ushort classSetCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            setCount = classSetCount;
            if (status != ReadStatus.Done) return status;
            status = TryOpenClassCoverage(out var classCoverage, out bool classPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!classPresent)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, ChainingSubstLayout.BacktrackGlyphCountField),
                    out diagnostic, 0UL, 1UL);
            status = classCoverage!.Inspect(numGlyphs, out coverageCount, out coverageLength,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            // The three class definitions may legally share one body (W12); every
            // provided definition validates against the glyph domain, and the input
            // definition's class values own the rule-set index domain (OQ-3's
            // chained direction: InvalidIndex at the assigning row).
            status = OpenAndCheckClassDefs(numGlyphs, setCount, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        else
        {
            status = TryGetChainedSeqRuleSetCount(out ushort chainSetCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            setCount = chainSetCount;
            status = TryOpenCoverage(out var coverage, out bool present, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!present)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, ChainingSubstLayout.BacktrackGlyphCountField),
                    out diagnostic, 0UL, 1UL);
            status = coverage!.Inspect(numGlyphs, out coverageCount, out coverageLength,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        consumed = (int)Math.Max((classForm ? ChainingSubstLayout.Format2ArrayAt
            : ChainingSubstLayout.Format1ArrayAt) + ChainingSubstLayout.OffsetStride * (long)setCount,
            (long)ChainingSubstLayout.BacktrackGlyphCountField + coverageLength);
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < setCount; i++)
        {
            status = OpenRuleSet(i, classForm, out var ruleSet, out bool setPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!setPresent) continue;
            status = ruleSet.ValidateChainedRules(lookupCount, budget, out int ruleSetConsumed, out diagnostic);
            if (status != ReadStatus.Done) return status;
            long setStart = RuleSetStart(i, classForm);
            consumed = (int)Math.Max(consumed, setStart + ruleSetConsumed);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus OpenAndCheckClassDefs(int numGlyphs, int classSetCount,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryOpenInputClassDef(out var inputDef, out bool inputPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!inputPresent)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, ChainingSubstLayout.ClassDefOffsetField),
                out diagnostic, 0UL, 1UL);
        status = inputDef!.Validate(numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = CheckClassDomain(inputDef!, classSetCount, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenBacktrackClassDef(out var backtrackDef, out bool backtrackPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (backtrackPresent)
        {
            status = backtrackDef!.Validate(numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        status = TryOpenLookaheadClassDef(out var lookaheadDef, out bool lookaheadPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (lookaheadPresent)
        {
            status = lookaheadDef!.Validate(numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus CheckClassDomain(ClassDefView classDef, int classSetCount,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = classDef.TryGetDenseHeader(out var dense, out diagnostic);
        if (status == ReadStatus.Done)
        {
            if (!budget.Work(dense.GlyphCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            for (int i = 0; i < dense.GlyphCount; i++)
            {
                status = classDef.TryGetClassValue(i, out ushort value, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (value >= classSetCount)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.Payload, 0, i), out diagnostic,
                        value, (ulong)classSetCount);
            }
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = classDef.TryGetSparseHeader(out var sparse, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(sparse.RangeCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < sparse.RangeCount; i++)
        {
            status = classDef.TryGetRange(i, out var range, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (range.ClassValue >= classSetCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.Payload, 0, i), out diagnostic,
                    range.ClassValue, (ulong)classSetCount);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus OpenRuleSet(int ordinal, bool classForm,
        out ContextualRuleSetView ruleSet, out bool present, out FontDiagnostic diagnostic)
    {
        present = false;
        ruleSet = default;
        ReadStatus status;
        if (classForm)
        {
            status = TryOpenChainedClassSequenceRuleSet(ordinal, out var classSet, out present, out diagnostic);
            ruleSet = classSet;
        }
        else
        {
            status = TryOpenChainedSequenceRuleSet(ordinal, out var set, out present, out diagnostic);
            ruleSet = set;
        }
        return status;
    }

    private long RuleSetStart(int ordinal, bool classForm)
    {
        long start = classForm ? ChainingSubstLayout.Format2ArrayAt : ChainingSubstLayout.Format1ArrayAt;
        if (classForm)
        {
            ReadStatus st = TryGetChainedClassRuleSetOffsetRecord(ordinal, out var classRecord, out _);
            return st == ReadStatus.Done ? start + classRecord.RuleSetOffset : 0;
        }
        ReadStatus st2 = TryGetChainedSeqRuleSetOffsetRecord(ordinal, out var record, out _);
        return st2 == ReadStatus.Done ? start + record.RuleSetOffset : 0;
    }

    // Format 3: the declared backtrack coverage array plus the hand-walked computed
    // starts - inputGlyphCount at 4+2*bt, its coverage offsets after it, then the
    // lookahead chain and the record list (OQ-5's hand-walked boundary).
    private ReadStatus InspectFormat3(int numGlyphs, int lookupCount, out int consumed,
        out FontDiagnostic diagnostic, BuildBudget budget,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation)
    {
        consumed = 0;
        ReadStatus status = TryGetBacktrackGlyphCount(out ushort backtrackCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int cursor = ChainingSubstLayout.Format3ArrayAt + 2 * backtrackCount;
        status = ReadCountAt(cursor, "inputGlyphCount", out int inputCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2;
        status = WalkCoverageChain(cursor, inputCount, numGlyphs, cache, subtableStart,
            lookupLocation, budget, validateOnly: true, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2 * inputCount;
        status = ReadCountAt(cursor, "lookaheadGlyphCount", out int lookaheadCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2;
        status = WalkCoverageChain(cursor, lookaheadCount, numGlyphs, cache, subtableStart,
            lookupLocation, budget, validateOnly: true, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2 * lookaheadCount;
        status = ReadCountAt(cursor, "seqLookupCount", out int recordCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        cursor += 2;
        consumed = (int)(cursor + 4L * recordCount);
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < backtrackCount; i++)
        {
            status = OpenDeclaredBacktrackCoverage(i, numGlyphs, cache, subtableStart,
                lookupLocation, budget, validateOnly: true, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        long recordsAt = cursor;
        for (int i = 0; i < recordCount; i++)
        {
            status = ReadRecordAt(recordsAt + 4L * i, lookupCount, inputCount, i, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadCountAt(int at, string role, out int count, out FontDiagnostic diagnostic)
    {
        count = 0;
        if (at < 0 || at + 2 > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Count, (ulong)at), out diagnostic,
                (ulong)at, (ulong)_data.Length);
        count = ReadWire.U16(_data, at);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus WalkCoverageChain(int offsetsAt, int count, int numGlyphs,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation,
        BuildBudget budget, bool validateOnly, out CoverageTable[]? models, out FontDiagnostic diagnostic)
    {
        models = null;
        if (offsetsAt + 2L * count > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, (ulong)offsetsAt), out diagnostic,
                (ulong)(offsetsAt + 2L * count), (ulong)_data.Length);
        if (!budget.Work(count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        CoverageTable[]? result = validateOnly ? null : new CoverageTable[count];
        for (int i = 0; i < count; i++)
        {
            // Coverage offsets in the fmt3 chains address from the subtable start
            // (the spec's "from the beginning of" wording), not from the offsets row.
            int offset = ReadWire.U16(_data, offsetsAt + 2 * i);
            int target = offset;
            if (offset == 0 || target >= _data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.AtLocal(DataField.Offset, (ulong)(offsetsAt + 2 * i), i), out diagnostic,
                    (ulong)offset, (ulong)_data.Length);
            if (cache is not null && cache.TryGetValue(subtableStart + target, out var cached))
            {
                if (result is not null) result[i] = cached;
                continue;
            }
            ReadStatus status = CoverageView.TryOpen(_data.Slice(offset), out var coverage,
                out diagnostic, Location);
            if (status != ReadStatus.Done) return status;
            if (validateOnly)
            {
                status = coverage!.Validate(numGlyphs, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                continue;
            }
            status = coverage!.TryRead(numGlyphs, out var model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (cache is not null && subtableStart >= 0)
            {
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                cache.TryAdd(subtableStart + target, model!);
            }
            if (result is not null) result[i] = model!;
        }
        models = result;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus OpenDeclaredBacktrackCoverage(int ordinal, int numGlyphs,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation,
        BuildBudget budget, bool validateOnly, out CoverageTable? model, out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = TryGetBacktrackCoverageOffsetRecord(ordinal, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (record.CoverageOffset >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset,
                    ChainingSubstLayout.Format3ArrayAt + (ulong)ordinal * ChainingSubstLayout.OffsetStride,
                    ordinal), out diagnostic, record.CoverageOffset, (ulong)_data.Length);
        int target = ChainingSubstLayout.Format3ArrayAt + record.CoverageOffset;
        if (cache is not null && cache.TryGetValue(subtableStart + target, out var cached))
        {
            model = cached;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = CoverageView.TryOpen(_data.Slice(record.CoverageOffset), out var coverage,
            out diagnostic, Location);
        if (status != ReadStatus.Done) return status;
        if (validateOnly)
        {
            status = coverage!.Validate(numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = coverage!.TryRead(numGlyphs, out model, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (cache is not null && subtableStart >= 0)
        {
            if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            cache.TryAdd(subtableStart + target, model!);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadRecordAt(long at, int lookupCount, int glyphCount, int ordinal,
        out SequenceLookupRecord record, out FontDiagnostic diagnostic)
    {
        record = default;
        if (at < 0 || at + 4 > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, (ulong)at, ordinal), out diagnostic,
                (ulong)at, (ulong)_data.Length);
        record = new(ReadWire.U16(_data, (int)at), ReadWire.U16(_data, (int)at + 2));
        if (record.SequenceIndex >= glyphCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload, (ulong)at, ordinal), out diagnostic,
                record.SequenceIndex, (ulong)glyphCount);
        if (lookupCount >= 0 && record.LookupListIndex >= lookupCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload, (ulong)at + 2, ordinal), out diagnostic,
                record.LookupListIndex, (ulong)lookupCount);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadOwned(int numGlyphs, int lookupCount, out ChainingSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, Dictionary<int, CoverageTable>? cache,
        int subtableStart, DataLocation lookupLocation)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetSubstFormat(out ushort format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        switch (format)
        {
            case 1:
            case 2:
            {
                bool classForm = format == 2;
                int setCount;
                if (classForm)
                {
                    status = TryGetChainedClassSeqRuleSetCount(out ushort classSetCount, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    setCount = classSetCount;
                }
                else
                {
                    status = TryGetChainedSeqRuleSetCount(out ushort setCountRead, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    setCount = setCountRead;
                }
                var ruleSets = new ChainingRuleSet?[setCount];
                long consumed = (classForm ? ChainingSubstLayout.Format2ArrayAt
                    : ChainingSubstLayout.Format1ArrayAt) + 2L * setCount;
                for (int i = 0; i < setCount; i++)
                {
                    status = OpenRuleSet(i, classForm, out var ruleSet, out bool setPresent, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!setPresent) continue;
                    status = ruleSet.ReadChainedRules(lookupCount, budget, out var setModel, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    ruleSets[i] = setModel;
                    consumed = (int)Math.Max(consumed, RuleSetStart(i, classForm) + setModel!.ConsumedLength);
                }
                if (classForm)
                {
                    status = OpenClassDefModels(numGlyphs, budget, out var backtrackDef,
                        out var inputDef, out var lookaheadDef, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = TryOpenClassCoverage(out var classCoverage, out bool coveragePresent, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!coveragePresent)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.Offset, ChainingSubstLayout.BacktrackGlyphCountField),
                            out diagnostic, 0UL, 1UL);
                    status = classCoverage!.TryRead(numGlyphs, out var coverageModel, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(48, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    model = new(format, coverageModel, backtrackDef, inputDef, lookaheadDef,
                        ruleSets, null, null, null, null, numGlyphs, Location,
                        (int)Math.Max(consumed, coverageModel!.ConsumedLength + ClassDefExtent(
                            backtrackDef, inputDef, lookaheadDef)));
                    diagnostic = default;
                    return ReadStatus.Done;
                }
                status = TryOpenCoverage(out var singleCoverage, out bool singlePresent, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (!singlePresent)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Offset, ChainingSubstLayout.BacktrackGlyphCountField),
                        out diagnostic, 0UL, 1UL);
                status = singleCoverage!.TryRead(numGlyphs, out var singleModel, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                model = new(format, singleModel, null, null, null, ruleSets, null, null, null,
                    null, numGlyphs, Location, (int)Math.Max(consumed, singleModel!.ConsumedLength));
                diagnostic = default;
                return ReadStatus.Done;
            }
            case 3:
            {
                status = TryGetBacktrackGlyphCount(out ushort backtrackCount, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(8L * backtrackCount, Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                var backtrackCoverages = new CoverageTable[backtrackCount];
                for (int i = 0; i < backtrackCount; i++)
                {
                    status = OpenDeclaredBacktrackCoverage(i, numGlyphs, cache, subtableStart,
                        lookupLocation, budget, validateOnly: false, out var bt, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    backtrackCoverages[i] = bt!;
                }
                int cursor = ChainingSubstLayout.Format3ArrayAt + 2 * backtrackCount;
                status = ReadCountAt(cursor, "inputGlyphCount", out int inputCount, out diagnostic);
                if (status != ReadStatus.Done) return status;
                cursor += 2;
                status = WalkCoverageChain(cursor, inputCount, numGlyphs, cache, subtableStart,
                    lookupLocation, budget, validateOnly: false, out var inputCoverages, out diagnostic);
                if (status != ReadStatus.Done) return status;
                cursor += 2 * inputCount;
                status = ReadCountAt(cursor, "lookaheadGlyphCount", out int lookaheadCount, out diagnostic);
                if (status != ReadStatus.Done) return status;
                cursor += 2;
                status = WalkCoverageChain(cursor, lookaheadCount, numGlyphs, cache, subtableStart,
                    lookupLocation, budget, validateOnly: false, out var lookaheadCoverages, out diagnostic);
                if (status != ReadStatus.Done) return status;
                cursor += 2 * lookaheadCount;
                status = ReadCountAt(cursor, "seqLookupCount", out int recordCount, out diagnostic);
                if (status != ReadStatus.Done) return status;
                cursor += 2;
                var records = recordCount == 0 ? [] : new SequenceLookupRecord[recordCount];
                for (int i = 0; i < recordCount; i++)
                {
                    status = ReadRecordAt(cursor + 4L * i, lookupCount, inputCount, i,
                        out records[i], out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                long consumed = cursor + 4L * recordCount;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                model = new(format, null, null, null, null, [], backtrackCoverages,
                    inputCoverages, lookaheadCoverages, records, numGlyphs, Location, (int)consumed);
                diagnostic = default;
                return ReadStatus.Done;
            }
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    Location.AtLocal(DataField.Version, 0), out diagnostic, format);
        }
    }

    private ReadStatus OpenClassDefModels(int numGlyphs, BuildBudget budget,
        out ClassDefTable? backtrackDef, out ClassDefTable? inputDef, out ClassDefTable? lookaheadDef,
        out FontDiagnostic diagnostic)
    {
        backtrackDef = inputDef = lookaheadDef = null;
        ReadStatus status = TryOpenBacktrackClassDef(out var bt, out bool btPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenInputClassDef(out var input, out bool inputPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenLookaheadClassDef(out var la, out bool laPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!inputPresent)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, ChainingSubstLayout.ClassDefOffsetField),
                out diagnostic, 0UL, 1UL);
        status = input!.TryRead(numGlyphs, out inputDef, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (btPresent)
        {
            status = bt!.TryRead(numGlyphs, out backtrackDef, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (laPresent)
        {
            status = la!.TryRead(numGlyphs, out lookaheadDef, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static long ClassDefExtent(ClassDefTable? backtrack, ClassDefTable? input,
        ClassDefTable? lookahead) =>
        (backtrack?.ConsumedLength ?? 0) + (input?.ConsumedLength ?? 0) + (lookahead?.ConsumedLength ?? 0);
}
