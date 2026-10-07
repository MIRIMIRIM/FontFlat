using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// Immutable fragment plan (the MultipleSubstPartPlan shape): one contextual subtable
// is one part; the wire layout facts and the plan-time fixup values. The layout is
// canonical: header, rule-set offset records, rule-set bodies in order (class: then
// the ClassDef), coverage last; format 3 lays out header, coverage offset records,
// coverage bodies, then the record list.
internal sealed record ContextualSubstPartPlan(int Length, ushort CoverageFormat,
    int CoverageRangeCount, ushort CoverageOffset, ushort? ClassDefOffset, ushort RecordsAt);

public static class ContextualSubstTableCompiler
{
    // One rule body: the input sequence carries glyphCount-1 entries (the first
    // input comes from the coverage/class), so the declarative underflow shape
    // (glyphCount=0) is unrepresentable by construction. The graph domains are the
    // factory's hard checks: SequenceIndex < glyphCount (owner-local) and
    // LookupListIndex < the caller's lookup count (the explicit input; negative
    // disables, the read face's conditional form).
    public static ReadStatus TryCreateRule(ReadOnlySpan<ushort> inputSequence,
        ReadOnlySpan<SequenceLookupRecord> records, int lookupCount, out ContextualRule? rule,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateRule(inputSequence, records, lookupCount, out rule, out diagnostic,
            new BuildBudget(limits), location);

    internal static ReadStatus TryCreateRule(ReadOnlySpan<ushort> inputSequence,
        ReadOnlySpan<SequenceLookupRecord> records, int lookupCount, out ContextualRule? rule,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default)
    {
        rule = null;
        int glyphCount = inputSequence.Length + 1;
        if (glyphCount > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic,
                (ulong)glyphCount, (ulong)ushort.MaxValue);
        if (records.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 2), out diagnostic,
                (ulong)records.Length, (ulong)ushort.MaxValue);
        if (!budget.Work(glyphCount + records.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.SequenceIndex >= glyphCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload, 4 + (ulong)i * 4, i), out diagnostic,
                    record.SequenceIndex, (ulong)glyphCount);
            if (lookupCount >= 0 && record.LookupListIndex >= lookupCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload, 6 + (ulong)i * 4, i), out diagnostic,
                    record.LookupListIndex, (ulong)lookupCount);
        }
        if (!budget.Buffer(2L * inputSequence.Length + 8L * records.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int consumed = 4 + 2 * inputSequence.Length + 4 * records.Length;
        rule = new(inputSequence.ToArray(), records.ToArray(), location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCreateRuleSet(IReadOnlyList<ContextualRule> rules,
        out ContextualRuleSet? ruleSet, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateRuleSet(rules, out ruleSet, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreateRuleSet(IReadOnlyList<ContextualRule> rules,
        out ContextualRuleSet? ruleSet, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        ruleSet = null;
        if (rules.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic,
                (ulong)rules.Count, (ulong)ushort.MaxValue);
        if (!budget.Work(rules.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        // Canonical extent: header, offset records, then every rule body in order.
        long consumed = ContextualRuleSetLayout.HeaderLength +
            ContextualRuleSetLayout.OffsetStride * (long)rules.Count;
        for (int i = 0; i < rules.Count; i++)
        {
            if (rules[i] is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, ContextualRuleSetLayout.HeaderLength +
                        (ulong)i * ContextualRuleSetLayout.OffsetStride, i), out diagnostic);
            consumed += rules[i]!.ConsumedLength;
        }
        if (!budget.Buffer(8L * rules.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = new ContextualRule[rules.Count];
        for (int i = 0; i < rules.Count; i++) copy[i] = rules[i];
        ruleSet = new(copy, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Format 1: one rule set per coverage entry (the read face's correspondence).
    public static ReadStatus TryCreate(CoverageTable coverage,
        IReadOnlyList<ContextualRuleSet?> ruleSets, int numGlyphs,
        out ContextualSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(1, coverage, null, ruleSets, numGlyphs, out model, out diagnostic,
            new BuildBudget(limits), location);

    // Format 2: the ClassDef's assigned class values own the rule-set index domain.
    public static ReadStatus TryCreate(CoverageTable coverage, ClassDefTable classDef,
        IReadOnlyList<ContextualRuleSet?> ruleSets, int numGlyphs,
        out ContextualSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(2, coverage, classDef, ruleSets, numGlyphs, out model, out diagnostic,
            new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(ushort format, CoverageTable coverage,
        ClassDefTable? classDef, IReadOnlyList<ContextualRuleSet?> ruleSets, int numGlyphs,
        out ContextualSubstTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format is not (1 or 2))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Version, 0), out diagnostic, format);
        if (coverage.NumGlyphs != numGlyphs || coverage.Location.SourceId != location.SourceId ||
            coverage.Location.FaceIndex != location.FaceIndex)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                coverage.Location, out diagnostic);
        if (ruleSets.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 4), out diagnostic,
                (ulong)ruleSets.Count, (ulong)ushort.MaxValue);
        if (ruleSets.Count != coverage.Glyphs.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 4), out diagnostic,
                (ulong)ruleSets.Count, (ulong)coverage.Glyphs.Length);
        if (!budget.Work(ruleSets.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (!budget.Buffer(8L * ruleSets.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = new ContextualRuleSet?[ruleSets.Count];
        for (int i = 0; i < ruleSets.Count; i++) copy[i] = ruleSets[i];
        long consumed = (format == 2 ? ContextualSubstLayout.Format2ArrayAt
            : ContextualSubstLayout.Format1ArrayAt) + 2L * ruleSets.Count;
        consumed += coverage.ConsumedLength + (classDef?.ConsumedLength ?? 0);
        if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(format, coverage, classDef, copy, null, null, numGlyphs, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Format 3 (count-first): the coverage chain plus the flat record list; each
    // record's SequenceIndex addresses this chain's slots.
    public static ReadStatus TryCreateFormat3(IReadOnlyList<CoverageTable> coverages,
        IReadOnlyList<SequenceLookupRecord> records, int numGlyphs,
        out ContextualSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateFormat3(coverages, records, numGlyphs, out model, out diagnostic,
            new BuildBudget(limits), location);

    internal static ReadStatus TryCreateFormat3(IReadOnlyList<CoverageTable> coverages,
        IReadOnlyList<SequenceLookupRecord> records, int numGlyphs,
        out ContextualSubstTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (coverages.Count > ushort.MaxValue || records.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 2), out diagnostic,
                (ulong)Math.Max(coverages.Count, records.Count), (ulong)ushort.MaxValue);
        if (!budget.Work(coverages.Count + records.Count, location, out diagnostic) ||
            !budget.Buffer(8L * coverages.Count + 8L * records.Count, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].SequenceIndex >= coverages.Count)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload, 4 + (ulong)i * 4, i), out diagnostic,
                    records[i].SequenceIndex, (ulong)coverages.Count);
        }
        var coverageCopy = new CoverageTable[coverages.Count];
        long consumed = ContextualSubstLayout.Format1ArrayAt + 2L * coverages.Count + 4L * records.Count;
        for (int i = 0; i < coverages.Count; i++)
        {
            var coverage = coverages[i];
            if (coverage is null || coverage.NumGlyphs != numGlyphs ||
                coverage.Location.SourceId != location.SourceId || coverage.Location.FaceIndex != location.FaceIndex)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    coverage?.Location ?? location, out diagnostic);
            coverageCopy[i] = coverage;
        }
        var recordCopy = new SequenceLookupRecord[records.Count];
        for (int i = 0; i < records.Count; i++) recordCopy[i] = records[i];
        foreach (var coverage in coverageCopy) consumed += CoverageTableCompiler.Measure(coverage);
        if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var aggregate = coverageCopy.Length != 0 ? coverageCopy[0]
            : new CoverageTable([], numGlyphs, 1, location, 4, 0);
        model = new(3, aggregate, null, [], coverageCopy, recordCopy, numGlyphs, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(ContextualSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(ContextualSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(plans![0].Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plans[0].Length];
        WritePlanned(model, plans[0], bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(ContextualSubstTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < plans![0].Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plans[0].Length);
        WritePlanned(model, plans[0], destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus TryPlanSubtables(ContextualSubstTable model,
        out ContextualSubstPartPlan[]? plans, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        ReadStatus status;
        if (model.Format is 1 or 2)
        {
            int setCount = model.RuleSets!.Count;
            long bodies = 0;
            for (int i = 0; i < setCount; i++)
                bodies += model.RuleSets[i]?.ConsumedLength ?? 0;
            int arrayAt = model.Format == 2 ? ContextualSubstLayout.Format2ArrayAt
                : ContextualSubstLayout.Format1ArrayAt;
            long cursor = arrayAt + 2L * setCount + bodies;
            int classDefAt = 0;
            if (model.Format == 2)
            {
                classDefAt = (int)cursor;
                cursor += model.ClassDef!.ConsumedLength;
            }
            int coverageAt = (int)cursor;
            status = model.Format == 2
                ? ContextualSubstView.TryGetClassCoverageOffsetValue(true, 0UL, (ulong)coverageAt,
                    out _, out diagnostic, model.Location.AtLocal(DataField.Offset, 2))
                : ContextualSubstView.TryGetCoverageOffsetValue(true, 0UL, (ulong)coverageAt,
                    out _, out diagnostic, model.Location.AtLocal(DataField.Offset, 2));
            if (status != ReadStatus.Done) return status;
            if (model.Format == 2)
            {
                status = ContextualSubstView.TryGetClassDefOffsetValue(true, 0UL, (ulong)classDefAt,
                    out _, out diagnostic, model.Location.AtLocal(DataField.Offset, 4));
                if (status != ReadStatus.Done) return status;
            }
            long check = arrayAt;
            for (int i = 0; i < setCount; i++)
            {
                var set = model.RuleSets[i];
                // The exact per-set target: the offset records walk in body order.
                long before = 0;
                for (int j = 0; j < i; j++) before += model.RuleSets[j]?.ConsumedLength ?? 0;
                ushort target = set is null ? (ushort)0 : (ushort)(arrayAt + 2L * setCount + before);
                status = model.Format == 2
                    ? ContextualSubstView.TryGetClassSequenceRuleSetOffsetValue(set is not null, 0UL,
                        (ulong)target, out _, out diagnostic, model.Location.AtLocal(DataField.Offset,
                            (ulong)arrayAt + (ulong)i * (ulong)ContextualSubstLayout.OffsetStride, i))
                    : ContextualSubstView.TryGetSequenceRuleSetOffsetValue(set is not null, 0UL,
                        (ulong)target, out _, out diagnostic, model.Location.AtLocal(DataField.Offset,
                            (ulong)arrayAt + (ulong)i * (ulong)ContextualSubstLayout.OffsetStride, i));
                if (status != ReadStatus.Done) return status;
                check += 2;
            }
            if (!budget.Work(EmissionWork(model), model.Location, out diagnostic) ||
                !budget.Output(coverageAt + CoverageTableCompiler.Measure(model.Coverage),
                    model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            plans =
            [
                new(coverageAt + CoverageTableCompiler.Measure(model.Coverage),
                    model.Coverage.Format, model.Coverage.RangeCount, (ushort)coverageAt,
                    model.Format == 2 ? (ushort)classDefAt : null, (ushort)arrayAt),
            ];
            diagnostic = default;
            return ReadStatus.Done;
        }
        // Format 3: header(6) + coverage offset records(2g) + the record list at its
        // spec position right behind the offset row, then the coverage bodies (the
        // canonical order the reader walks: 6+2g is the records' declared start).
        int coverageCount = model.Coverages!.Count, recordCount = model.LookupRecords!.Count;
        long coverageStart = ContextualSubstLayout.Format1ArrayAt + 2L * coverageCount + 4L * recordCount;
        long coverageBytes = 0;
        foreach (var coverage in model.Coverages) coverageBytes += CoverageTableCompiler.Measure(coverage);
        long length = coverageStart + coverageBytes;
        // The 16-bit coverage offsets bound the body region; the too-narrow targets
        // refuse in the writer's generated fixups (the registered finite overflow).
        if (coverageStart > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, ContextualSubstLayout.Format1ArrayAt),
                out diagnostic, (ulong)coverageStart, (ulong)ushort.MaxValue);
        if (!budget.Work(EmissionWork(model), model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plans =
        [
            new((int)length, model.Coverages.Count != 0 ? model.Coverages[0].Format : (ushort)1,
                model.Coverages.Count != 0 ? model.Coverages[0].RangeCount : 0,
                0, null, (ushort)ContextualSubstLayout.Format1ArrayAt),
        ];
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static long EmissionWork(ContextualSubstTable model) => model.Format == 3
        ? model.Coverages!.Count
        : model.RuleSets!.Count * 2L + 1;

    // Only a fully preflighted plan and destination reach this path. The version-
    // branched scalars have no generated writers (OQ-5's hand-written boundary), so
    // the header words go through this checked raw writer; every offset/row still
    // goes through the generated writers with RequireWritten.
    private static void WriteWord(Span<byte> destination, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(at, 2), value);

    internal static void WritePlanned(ContextualSubstTable model, ContextualSubstPartPlan plan,
        Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(ContextualSubstView.TryWriteSubstFormat(destination,
            model.Format, out _, location));
        if (model.Format is 1 or 2)
        {
            int setCount = model.RuleSets!.Count;
            int arrayAt = model.Format == 2 ? ContextualSubstLayout.Format2ArrayAt
                : ContextualSubstLayout.Format1ArrayAt;
            WriteWord(destination, 2, plan.CoverageOffset);
            if (model.Format == 2) WriteWord(destination, 4, plan.ClassDefOffset!.Value);
            WriteWord(destination, model.Format == 2 ? 6 : 4, (ushort)setCount);
            long bodyAt = arrayAt + 2L * setCount;
            for (int i = 0; i < setCount; i++)
            {
                var set = model.RuleSets[i];
                ushort target = set is null ? (ushort)0 : (ushort)bodyAt;
                CoverageTableCompiler.RequireWritten(model.Format == 2
                    ? ContextualSubstView.TryWriteClassRuleSetOffsetRecordAt(destination,
                        (ulong)arrayAt + (ulong)i * (ulong)ContextualSubstLayout.OffsetStride,
                        new ContextualSubstView.ClassRuleSetOffsetRecordData(i, target), out _, location)
                    : ContextualSubstView.TryWriteSeqRuleSetOffsetRecordAt(destination,
                        (ulong)arrayAt + (ulong)i * ContextualSubstLayout.OffsetStride,
                        new ContextualSubstView.SeqRuleSetOffsetRecordData(i, target), out _, location));
                if (set is not null)
                {
                    WriteRuleSet(set, destination.Slice((int)bodyAt), location);
                    bodyAt += set.ConsumedLength;
                }
            }
            if (model.Format == 2)
                ClassDefTableCompiler.WritePlanned(model.ClassDef!,
                    destination.Slice(plan.ClassDefOffset!.Value, model.ClassDef!.ConsumedLength));
            CoverageTableCompiler.WritePlanned(model.Coverage.Glyphs, plan.CoverageFormat,
                plan.CoverageRangeCount, destination.Slice(plan.CoverageOffset), location);
            return;
        }
        // Format 3: header, the offset row, the records at their spec position
        // (6+2g), then the coverage bodies the row targets.
        int coverageCount = model.Coverages!.Count, recordCount = model.LookupRecords!.Count;
        WriteWord(destination, 2, (ushort)coverageCount);
        WriteWord(destination, 4, (ushort)recordCount);
        long bodyCursor = ContextualSubstLayout.Format1ArrayAt + 2L * coverageCount + 4L * recordCount;
        long recordAt = ContextualSubstLayout.Format1ArrayAt + 2L * coverageCount;
        for (int i = 0; i < coverageCount; i++)
        {
            var coverage = model.Coverages[i];
            ushort target = (ushort)bodyCursor;
            CoverageTableCompiler.RequireWritten(ContextualSubstView.TryWriteFormat3CoverageOffsetRecordAt(
                destination, (ulong)ContextualSubstLayout.Format1ArrayAt +
                    (ulong)i * (ulong)ContextualSubstLayout.OffsetStride,
                new ContextualSubstView.Format3CoverageOffsetRecordData(i, target), out _, location));
            CoverageTableCompiler.WritePlanned(coverage.Glyphs, coverage.Format, coverage.RangeCount,
                destination.Slice((int)bodyCursor, CoverageTableCompiler.Measure(coverage)), location);
            bodyCursor += CoverageTableCompiler.Measure(coverage);
        }
        for (int i = 0; i < recordCount; i++)
        {
            var record = model.LookupRecords[i];
            WriteWord(destination, (int)recordAt + 4 * i, record.SequenceIndex);
            WriteWord(destination, (int)recordAt + 4 * i + 2, record.LookupListIndex);
        }
    }

    // One rule-set body (the Sequence/ClassSequence wire form shared by both roles):
    // rule count, rule offset records, then the variable rule bodies in order.
    private static void WriteRuleSet(ContextualRuleSet set, Span<byte> destination, DataLocation ownerLocation)
    {
        var location = set.Location;
        int ruleCount = set.Rules.Length;
        WriteWord(destination, 0, (ushort)ruleCount);
        long cursor = ContextualRuleSetLayout.HeaderLength +
            ContextualRuleSetLayout.OffsetStride * ruleCount;
        for (int i = 0; i < ruleCount; i++)
        {
            CoverageTableCompiler.RequireWritten(ContextualRuleSetView.TryWriteRuleOffsetRecordAt(
                destination, (ulong)ContextualRuleSetLayout.HeaderLength +
                    (ulong)i * (ulong)ContextualRuleSetLayout.OffsetStride,
                new ContextualRuleSetView.RuleOffsetRecordData(i, (ushort)cursor), out _, location));
            WriteRule(set.Rules[i], destination.Slice((int)cursor), location);
            cursor += set.Rules[i].ConsumedLength;
        }
    }

    private static void WriteRule(ContextualRule rule, Span<byte> destination, DataLocation ownerLocation)
    {
        WriteWord(destination, 0, (ushort)rule.InputCount);
        WriteWord(destination, 2, (ushort)rule.LookupRecords.Length);
        var input = rule.InputSequence;
        for (int i = 0; i < input.Length; i++)
            WriteWord(destination, 4 + 2 * i, input[i]);
        var records = rule.LookupRecords;
        for (int i = 0; i < records.Length; i++)
        {
            WriteWord(destination, 4 + 2 * input.Length + 4 * i, records[i].SequenceIndex);
            WriteWord(destination, 6 + 2 * input.Length + 4 * i, records[i].LookupListIndex);
        }
    }
}
