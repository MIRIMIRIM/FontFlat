using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// The contextual view's semantic partial (T4-04 section 3). The typed read serves
// formats 1/2/3 with the direct-leaf conventions: one shared per-Lookup Coverage
// cache keyed on physical Lookup-local positions, Inspect for pure validation
// (nothing materialized) and ReadOwned for materialization, budgets charged as
// Work(structure visits) + Buffer(materialized arrays). The lookup-index graph
// takes the caller's lookup count as an explicit input (T5-01/T4-02b signature
// discipline): LookupListIndex >= lookupCount refuses InvalidIndex (OQ-4's
// structural-corruption hard refusal - the degradation channel stays reserved for
// unsupported types); a negative lookupCount means the caller did not supply the
// count and only the owner-local SequenceIndex domain is enforced, the same
// conditional-checking first-class treatment as a null fvar input (section 3.3).
public readonly ref partial struct ContextualSubstView
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

    public ReadStatus TryRead(int numGlyphs, int lookupCount, out ContextualSubstTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ReadOwned(numGlyphs, lookupCount, out model, out diagnostic, budget, null, 0, default);
    }

    internal ReadStatus TryRead(int numGlyphs, int lookupCount, out ContextualSubstTable? model,
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
                out diagnostic, budget, cache, subtableStart, lookupLocation);
            case 2: return InspectRuleSets(numGlyphs, lookupCount, classForm: true, out consumed,
                out diagnostic, budget, cache, subtableStart, lookupLocation);
            case 3: return InspectFormat3(numGlyphs, lookupCount, out consumed,
                out diagnostic, budget, cache, subtableStart, lookupLocation);
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    Location.AtLocal(DataField.Version, 0), out diagnostic, format);
        }
    }

    // Formats 1/2: coverage (class: plus ClassDef) and the rule-set chain. The
    // correspondence checks are the W10 direction (one set per coverage entry /
    // per class value; OQ-3's lean hard check) and NULL set offsets stay legal.
    private ReadStatus InspectRuleSets(int numGlyphs, int lookupCount, bool classForm,
        out int consumed, out FontDiagnostic diagnostic, BuildBudget budget,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation)
    {
        consumed = 0;
        ReadStatus status;
        int setCount, coverageCount, coverageLength;
        if (classForm)
        {
            status = TryGetClassSeqRuleSetCount(out ushort classSetCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            setCount = classSetCount;
            if (status != ReadStatus.Done) return status;
            status = TryOpenClassCoverage(out var classCoverage, out bool classPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!classPresent)
                return MissingReference(ContextualSubstLayout.GlyphCountField, out diagnostic);
            status = classCoverage!.Inspect(numGlyphs, out coverageCount, out coverageLength,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            status = TryOpenClassDef(out var classDef, out bool defPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!defPresent)
                return MissingReference(4, out diagnostic);
            // OQ-3's class-value direction: every class value the ClassDef assigns
            // must index an existing rule-set slot (InvalidIndex at the assigning row).
            status = CheckClassDomain(classDef!, classSetCount, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        else
        {
            status = TryGetSequenceRuleSetCount(out ushort ruleSetCount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            setCount = ruleSetCount;
            status = TryOpenCoverage(out var coverage, out bool present, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!present)
                return MissingReference(ContextualSubstLayout.GlyphCountField, out diagnostic);
            status = coverage!.Inspect(numGlyphs, out coverageCount, out coverageLength,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        consumed = (int)Math.Max((classForm ? ContextualSubstLayout.Format2ArrayAt
            : ContextualSubstLayout.Format1ArrayAt) + ContextualSubstLayout.OffsetStride * (long)setCount,
            (long)ContextualSubstLayout.GlyphCountField + coverageLength);
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // The rule-set bodies live on the shared rule-set view; its walk owns the
        // rule offsets, the declared underflow and the graph domains.
        for (int i = 0; i < setCount; i++)
        {
            status = OpenRuleSet(i, classForm, out var ruleSet, out bool setPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!setPresent) continue;
            status = ruleSet.ValidateRules(lookupCount, budget, out int ruleSetConsumed, out diagnostic);
            if (status != ReadStatus.Done) return status;
            long setStart = RuleSetStart(i, classForm);
            consumed = (int)Math.Max(consumed, setStart + ruleSetConsumed);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus MissingReference(ulong at, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            Location.AtLocal(DataField.Offset, at), out diagnostic, 0UL, 1UL);

    // Opens one rule set; a spec-NULL offset yields present=false (the view stays
    // default and callers must not touch it).
    private ReadStatus OpenRuleSet(int ordinal, bool classForm,
        out ContextualRuleSetView ruleSet, out bool present, out FontDiagnostic diagnostic)
    {
        present = false;
        ruleSet = default;
        ReadStatus status;
        if (classForm)
        {
            status = TryOpenClassSequenceRuleSet(ordinal, out var classSet, out present, out diagnostic);
            ruleSet = classSet;
        }
        else
        {
            status = TryOpenSequenceRuleSet(ordinal, out var set, out present, out diagnostic);
            ruleSet = set;
        }
        return status;
    }

    private long RuleSetStart(int ordinal, bool classForm)
    {
        long start = classForm ? ContextualSubstLayout.Format2ArrayAt : ContextualSubstLayout.Format1ArrayAt;
        if (classForm)
        {
            ReadStatus st = TryGetClassRuleSetOffsetRecord(ordinal, out var classRecord, out _);
            return st == ReadStatus.Done ? start + classRecord.RuleSetOffset : 0;
        }
        ReadStatus st2 = TryGetSeqRuleSetOffsetRecord(ordinal, out var record, out _);
        return st2 == ReadStatus.Done ? start + record.RuleSetOffset : 0;
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

    // Format 3 (count-first): the glyphCount coverage array is declared; the records
    // behind it are hand-walked from the computed start 6 + 2*glyphCount.
    private ReadStatus InspectFormat3(int numGlyphs, int lookupCount, out int consumed,
        out FontDiagnostic diagnostic, BuildBudget budget,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation)
    {
        consumed = 0;
        ReadStatus status = TryGetGlyphCount(out ushort glyphCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetSeqLookupCount(out ushort recordCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        consumed = (int)(ContextualSubstLayout.Format1ArrayAt +
            (long)ContextualSubstLayout.OffsetStride * glyphCount + 4L * recordCount);
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < glyphCount; i++)
        {
            status = OpenFormat3Coverage(i, numGlyphs, cache, subtableStart, lookupLocation,
                budget, validateOnly: true, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        for (int i = 0; i < recordCount; i++)
        {
            status = ReadRecordAt(ContextualSubstLayout.Format1ArrayAt +
                ContextualSubstLayout.OffsetStride * glyphCount + 4L * i, lookupCount,
                glyphCount, i, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus OpenFormat3Coverage(int ordinal, int numGlyphs,
        Dictionary<int, CoverageTable>? cache, int subtableStart, DataLocation lookupLocation,
        BuildBudget budget, bool validateOnly, out CoverageTable? model, out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = TryGetFormat3CoverageOffsetRecord(ordinal, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (record.CoverageOffset >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset,
                    ContextualSubstLayout.Format1ArrayAt + (ulong)ordinal * ContextualSubstLayout.OffsetStride,
                    ordinal), out diagnostic, record.CoverageOffset, (ulong)_data.Length);
        int target = record.CoverageOffset;
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

    // One 4-byte seqLookup record at a subtable-local offset, with the graph's two
    // domain checks: SequenceIndex against this subtable's glyphCount (owner-local,
    // always enforced) and LookupListIndex against the caller's lookup count
    // (the explicit-input check, OQ-4).
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

    private ReadStatus ReadOwned(int numGlyphs, int lookupCount, out ContextualSubstTable? model,
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
                    status = TryGetClassSeqRuleSetCount(out ushort classSetCount, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    setCount = classSetCount;
                }
                else
                {
                    status = TryGetSequenceRuleSetCount(out ushort setCountRead, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    setCount = setCountRead;
                }
                var ruleSets = new ContextualRuleSet?[setCount];
                long consumed = (classForm ? ContextualSubstLayout.Format2ArrayAt
                    : ContextualSubstLayout.Format1ArrayAt) + 2L * setCount;
                for (int i = 0; i < setCount; i++)
                {
                    status = OpenRuleSet(i, classForm, out var ruleSet, out bool setPresent, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!setPresent) continue;
                    status = ruleSet.ReadRules(lookupCount, budget, out var setModel, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    ruleSets[i] = setModel;
                    consumed = (long)Math.Max(consumed, RuleSetStart(i, classForm) + setModel!.ConsumedLength);
                }
                if (classForm)
                {
                    status = TryOpenClassCoverage(out var classCoverage, out bool classCoveragePresent,
                        out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!classCoveragePresent)
                        return MissingReference(ContextualSubstLayout.GlyphCountField, out diagnostic);
                    status = classCoverage!.TryRead(numGlyphs, out var coverageModel, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    status = TryOpenClassDef(out var classDef, out bool defPresent, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (!defPresent) return MissingReference(4, out diagnostic);
                    status = classDef!.TryRead(numGlyphs, out var classDefModel, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    model = new(format, coverageModel!, classDefModel, ruleSets, null, null,
                        numGlyphs, Location, (int)Math.Max(consumed,
                            coverageModel!.ConsumedLength + classDefModel!.ConsumedLength));
                    diagnostic = default;
                    return ReadStatus.Done;
                }
                status = TryOpenCoverage(out var singleCoverage, out bool singlePresent, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (!singlePresent) return MissingReference(ContextualSubstLayout.GlyphCountField, out diagnostic);
                status = singleCoverage!.TryRead(numGlyphs, out var singleModel, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                model = new(format, singleModel!, null, ruleSets, null, null, numGlyphs,
                    Location, (int)Math.Max(consumed, singleModel!.ConsumedLength));
                diagnostic = default;
                return ReadStatus.Done;
            }
            case 3:
            {
                status = TryGetGlyphCount(out ushort glyphCount, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = TryGetSeqLookupCount(out ushort recordCount, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(8L * glyphCount + 8L * recordCount, Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                var coverages = new CoverageTable[glyphCount];
                for (int i = 0; i < glyphCount; i++)
                {
                    status = OpenFormat3Coverage(i, numGlyphs, cache, subtableStart,
                        lookupLocation, budget, validateOnly: false, out var coverage, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    coverages[i] = coverage!;
                }
                SequenceLookupRecord[] records = recordCount == 0
                    ? [] : new SequenceLookupRecord[recordCount];
                for (int i = 0; i < recordCount; i++)
                {
                    status = ReadRecordAt(ContextualSubstLayout.Format1ArrayAt +
                        ContextualSubstLayout.OffsetStride * glyphCount + 4L * i,
                        lookupCount, glyphCount, i, out records[i], out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                long consumed = ContextualSubstLayout.Format1ArrayAt +
                    2L * glyphCount + 4L * recordCount;
                // Format 3 aggregates the first coverage into the single-coverage slot
                // (an empty chain stays a legal empty aggregate).
                CoverageTable aggregate = coverages.Length != 0 ? coverages[0]
                    : new CoverageTable([], numGlyphs, 1, Location, 4, 0);
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                model = new(format, aggregate, null, [], coverages, records, numGlyphs,
                    Location, (int)consumed);
                diagnostic = default;
                return ReadStatus.Done;
            }
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    Location.AtLocal(DataField.Version, 0), out diagnostic, format);
        }
    }
}
