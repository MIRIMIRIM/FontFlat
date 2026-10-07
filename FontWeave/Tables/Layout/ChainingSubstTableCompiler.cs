using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// Immutable fragment plan: one chaining subtable is one part. Canonical layout:
// header, rule-set offset records, rule-set bodies in order (class: then the three
// ClassDefs), coverage last; format 3 lays out the declared backtrack coverage
// bodies at its computed chain positions and the input/lookahead chains behind them
// (the OQ-5 hand-written fixup boundary).
internal sealed record ChainingSubstPartPlan(int Length, ushort? CoverageFormat,
    int CoverageRangeCount, ushort? CoverageOffset, int BacktrackChainAt);

public static class ChainingSubstTableCompiler
{
    // One chained rule body: backtrack (complete), input (glyphCount-1 after the
    // coverage/class first input), lookahead (complete) and the replacement records.
    // The declared underflow (inputGlyphCount=0) is unrepresentable - the stored
    // sequence plus the coverage/class first input is always >= 1.
    public static ReadStatus TryCreateRule(ReadOnlySpan<ushort> backtrackSequence,
        ReadOnlySpan<ushort> inputSequence, ReadOnlySpan<ushort> lookaheadSequence,
        ReadOnlySpan<SequenceLookupRecord> records, int lookupCount, out ChainingRule? rule,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateRule(backtrackSequence, inputSequence, lookaheadSequence, records, lookupCount,
            out rule, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreateRule(ReadOnlySpan<ushort> backtrackSequence,
        ReadOnlySpan<ushort> inputSequence, ReadOnlySpan<ushort> lookaheadSequence,
        ReadOnlySpan<SequenceLookupRecord> records, int lookupCount, out ChainingRule? rule,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default)
    {
        rule = null;
        int inputGlyphCount = inputSequence.Length + 1;
        if (inputGlyphCount > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic,
                (ulong)inputGlyphCount, (ulong)ushort.MaxValue);
        if (backtrackSequence.Length > ushort.MaxValue || lookaheadSequence.Length > ushort.MaxValue ||
            records.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic,
                (ulong)Math.Max(backtrackSequence.Length,
                    Math.Max(lookaheadSequence.Length, records.Length)), (ulong)ushort.MaxValue);
        if (!budget.Work(inputGlyphCount + backtrackSequence.Length + lookaheadSequence.Length +
            records.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.SequenceIndex >= inputGlyphCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload, 4 + (ulong)i * 4, i), out diagnostic,
                    record.SequenceIndex, (ulong)inputGlyphCount);
            if (lookupCount >= 0 && record.LookupListIndex >= lookupCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload, 6 + (ulong)i * 4, i), out diagnostic,
                    record.LookupListIndex, (ulong)lookupCount);
        }
        if (!budget.Buffer(2L * (backtrackSequence.Length + inputSequence.Length +
            lookaheadSequence.Length) + 8L * records.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int consumed = 8 + 2 * (backtrackSequence.Length + inputSequence.Length +
            lookaheadSequence.Length) + 4 * records.Length;
        rule = new(backtrackSequence.ToArray(), inputSequence.ToArray(), lookaheadSequence.ToArray(),
            records.ToArray(), location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCreateRuleSet(IReadOnlyList<ChainingRule> rules,
        out ChainingRuleSet? ruleSet, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateRuleSet(rules, out ruleSet, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreateRuleSet(IReadOnlyList<ChainingRule> rules,
        out ChainingRuleSet? ruleSet, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        ruleSet = null;
        if (rules.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic,
                (ulong)rules.Count, (ulong)ushort.MaxValue);
        if (!budget.Work(rules.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
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
        var copy = new ChainingRule[rules.Count];
        for (int i = 0; i < rules.Count; i++) copy[i] = rules[i];
        ruleSet = new(copy, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Format 1: one chained rule set per coverage entry.
    public static ReadStatus TryCreate(CoverageTable coverage,
        IReadOnlyList<ChainingRuleSet?> ruleSets, int numGlyphs,
        out ChainingSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(1, coverage, null, null, null, ruleSets, numGlyphs, out model, out diagnostic,
            new BuildBudget(limits), location);

    // Format 2: the three class definitions (which may legally share one instance).
    public static ReadStatus TryCreate(CoverageTable coverage, ClassDefTable backtrackClassDef,
        ClassDefTable inputClassDef, ClassDefTable lookaheadClassDef,
        IReadOnlyList<ChainingRuleSet?> ruleSets, int numGlyphs,
        out ChainingSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(2, coverage, backtrackClassDef, inputClassDef, lookaheadClassDef, ruleSets,
            numGlyphs, out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(ushort format, CoverageTable? coverage,
        ClassDefTable? backtrackClassDef, ClassDefTable? inputClassDef, ClassDefTable? lookaheadClassDef,
        IReadOnlyList<ChainingRuleSet?> ruleSets, int numGlyphs,
        out ChainingSubstTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format is not (1 or 2))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Version, 0), out diagnostic, format);
        if (coverage is null || coverage.NumGlyphs != numGlyphs ||
            coverage.Location.SourceId != location.SourceId || coverage.Location.FaceIndex != location.FaceIndex)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                coverage?.Location ?? location, out diagnostic);
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
        var copy = new ChainingRuleSet?[ruleSets.Count];
        for (int i = 0; i < ruleSets.Count; i++) copy[i] = ruleSets[i];
        long consumed = (format == 2 ? ChainingSubstLayout.Format2ArrayAt
            : ChainingSubstLayout.Format1ArrayAt) + 2L * ruleSets.Count;
        consumed += coverage.ConsumedLength + (backtrackClassDef?.ConsumedLength ?? 0) +
            (inputClassDef?.ConsumedLength ?? 0) + (lookaheadClassDef?.ConsumedLength ?? 0);
        if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(format, coverage, backtrackClassDef, inputClassDef, lookaheadClassDef, copy,
            null, null, null, null, numGlyphs, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Format 3: the declared backtrack coverage chain plus the hand-walked
    // input/lookahead chains (OQ-5's boundary) and the flat record list; each
    // record's SequenceIndex addresses the input chain's slots.
    public static ReadStatus TryCreateFormat3(IReadOnlyList<CoverageTable> backtrackCoverages,
        IReadOnlyList<CoverageTable> inputCoverages, IReadOnlyList<CoverageTable> lookaheadCoverages,
        IReadOnlyList<SequenceLookupRecord> records, int numGlyphs,
        out ChainingSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateFormat3(backtrackCoverages, inputCoverages, lookaheadCoverages, records, numGlyphs,
            out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreateFormat3(IReadOnlyList<CoverageTable> backtrackCoverages,
        IReadOnlyList<CoverageTable> inputCoverages, IReadOnlyList<CoverageTable> lookaheadCoverages,
        IReadOnlyList<SequenceLookupRecord> records, int numGlyphs,
        out ChainingSubstTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (backtrackCoverages.Count > ushort.MaxValue || inputCoverages.Count > ushort.MaxValue ||
            lookaheadCoverages.Count > ushort.MaxValue || records.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 2), out diagnostic,
                (ulong)records.Count, (ulong)ushort.MaxValue);
        if (!budget.Work(backtrackCoverages.Count + inputCoverages.Count + lookaheadCoverages.Count +
            records.Count, location, out diagnostic) ||
            !budget.Buffer(8L * (backtrackCoverages.Count + inputCoverages.Count +
                lookaheadCoverages.Count + records.Count), location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].SequenceIndex >= inputCoverages.Count)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload, 4 + (ulong)i * 4, i), out diagnostic,
                    records[i].SequenceIndex, (ulong)inputCoverages.Count);
        }
        var backtrack = CopyCoverages(backtrackCoverages, numGlyphs, location, out status, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var input = CopyCoverages(inputCoverages, numGlyphs, location, out status, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var lookahead = CopyCoverages(lookaheadCoverages, numGlyphs, location, out status, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var recordCopy = new SequenceLookupRecord[records.Count];
        for (int i = 0; i < records.Count; i++) recordCopy[i] = records[i];
        long consumed = ChainingSubstLayout.Format3ArrayAt +
            2L * (backtrackCoverages.Count + inputCoverages.Count + lookaheadCoverages.Count) +
            6L + 4L * records.Count;
        foreach (var coverage in backtrack.Concat(input).Concat(lookahead))
            consumed += CoverageTableCompiler.Measure(coverage);
        if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(3, null, null, null, null, [], backtrack, input, lookahead, recordCopy,
            numGlyphs, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static CoverageTable[] CopyCoverages(IReadOnlyList<CoverageTable> coverages,
        int numGlyphs, DataLocation location, out ReadStatus status, out FontDiagnostic diagnostic)
    {
        var copy = new CoverageTable[coverages.Count];
        for (int i = 0; i < coverages.Count; i++)
        {
            var coverage = coverages[i];
            if (coverage is null || coverage.NumGlyphs != numGlyphs ||
                coverage.Location.SourceId != location.SourceId || coverage.Location.FaceIndex != location.FaceIndex)
            {
                status = ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    coverage?.Location ?? location, out diagnostic);
                return copy;
            }
            copy[i] = coverage;
        }
        status = ReadStatus.Done;
        diagnostic = default;
        return copy;
    }

    public static ReadStatus TryCompile(ChainingSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(ChainingSubstTable model, out byte[]? output,
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

    public static ReadStatus TryWrite(ChainingSubstTable model, Span<byte> destination,
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

    internal static ReadStatus TryPlanSubtables(ChainingSubstTable model,
        out ChainingSubstPartPlan[]? plans, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        ReadStatus status;
        if (model.Format is 1 or 2)
        {
            int setCount = model.RuleSets!.Count;
            int arrayAt = model.Format == 2 ? ChainingSubstLayout.Format2ArrayAt
                : ChainingSubstLayout.Format1ArrayAt;
            long bodies = 0;
            for (int i = 0; i < setCount; i++)
                bodies += model.RuleSets[i]?.ConsumedLength ?? 0;
            long cursor = arrayAt + 2L * setCount + bodies;
            int coverageAt = (int)cursor;
            cursor += CoverageTableCompiler.Measure(model.Coverage!);
            long backtrackClassDefAt = 0L, inputClassDefAt = 0, lookaheadClassDefAt = 0;
            if (model.Format == 2)
            {
                backtrackClassDefAt = (int)cursor;
                cursor += model.BacktrackClassDef!.ConsumedLength;
                inputClassDefAt = (int)cursor;
                cursor += model.InputClassDef!.ConsumedLength;
                lookaheadClassDefAt = (int)cursor;
                cursor += model.LookaheadClassDef!.ConsumedLength;
                status = ChainingSubstView.TryGetBacktrackClassDefOffsetValue(true, 0UL, (ulong)backtrackClassDefAt,
                    out _, out diagnostic, model.Location.AtLocal(DataField.Offset, 4));
                if (status != ReadStatus.Done) return status;
                status = ChainingSubstView.TryGetInputClassDefOffsetValue(true, 0UL, (ulong)inputClassDefAt,
                    out _, out diagnostic, model.Location.AtLocal(DataField.Offset, 6));
                if (status != ReadStatus.Done) return status;
                status = ChainingSubstView.TryGetLookaheadClassDefOffsetValue(true, 0UL, (ulong)lookaheadClassDefAt,
                    out _, out diagnostic, model.Location.AtLocal(DataField.Offset, 8));
                if (status != ReadStatus.Done) return status;
            }
            status = ChainingSubstView.TryGetCoverageOffsetValue(true, 0UL, (ulong)coverageAt,
                out _, out diagnostic, model.Location.AtLocal(DataField.Offset, 2));
            if (status != ReadStatus.Done) return status;
            if (!budget.Work(EmissionWork(model), model.Location, out diagnostic) ||
                !budget.Output(cursor, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            plans =
            [
                new((int)cursor, model.Coverage!.Format, model.Coverage.RangeCount,
                    (ushort)coverageAt, 0),
            ];
            diagnostic = default;
            return ReadStatus.Done;
        }
        // Format 3: header(4) + backtrack offsets(2b) + backtrack bodies + input
        // count + input offsets(2i) + input bodies + lookahead count + offsets(2l) +
        // lookahead bodies + seqLookupCount + records. Every offset is subtable-
        // relative; the plan only admits widths, the writer fixes exact targets.
        int backtrackCount = model.BacktrackCoverages!.Count;
        int inputCount = model.InputCoverages!.Count;
        int lookaheadCount = model.LookaheadCoverages!.Count;
        int recordCount = model.LookupRecords!.Count;
        long bytes = 4L + 2L * backtrackCount + 2L + 2L * inputCount + 2L + 2L * lookaheadCount +
            2L + 4L * recordCount;
        foreach (var coverage in model.BacktrackCoverages)
            bytes += CoverageTableCompiler.Measure(coverage);
        foreach (var coverage in model.InputCoverages)
            bytes += CoverageTableCompiler.Measure(coverage);
        foreach (var coverage in model.LookaheadCoverages)
            bytes += CoverageTableCompiler.Measure(coverage);
        if (bytes > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, ChainingSubstLayout.Format3ArrayAt),
                out diagnostic, (ulong)bytes, (ulong)ushort.MaxValue);
        if (!budget.Work(EmissionWork(model), model.Location, out diagnostic) ||
            !budget.Output(bytes, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plans = [new((int)bytes, null, 0, null, ChainingSubstLayout.Format3ArrayAt)];
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static long EmissionWork(ChainingSubstTable model) => model.Format == 3
        ? model.BacktrackCoverages!.Count + model.InputCoverages!.Count + model.LookaheadCoverages!.Count
        : model.RuleSets!.Count * 2L + 1;

    // The version-branched scalars have no generated writers (OQ-5's hand-written
    // boundary); the header words go through this checked raw writer and every
    // row/fixup still goes through the generated writers with RequireWritten.
    private static void WriteWord(Span<byte> destination, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(at, 2), value);

    internal static void WritePlanned(ChainingSubstTable model, ChainingSubstPartPlan plan,
        Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(ChainingSubstView.TryWriteSubstFormat(destination,
            model.Format, out _, location));
        if (model.Format is 1 or 2)
        {
            int setCount = model.RuleSets!.Count;
            int arrayAt = model.Format == 2 ? ChainingSubstLayout.Format2ArrayAt
                : ChainingSubstLayout.Format1ArrayAt;
            long bodies = 0;
            for (int i = 0; i < setCount; i++)
                bodies += model.RuleSets[i]?.ConsumedLength ?? 0;
            int coverageAt = arrayAt + 2 * setCount + (int)bodies;
            WriteWord(destination, 2, (ushort)coverageAt);
            if (model.Format == 2)
            {
                // The three class definitions follow the coverage body in the plan's
                // canonical order; their offset fields sit at 4/6/8.
                int classDefAt = coverageAt + CoverageTableCompiler.Measure(model.Coverage!);
                WriteWord(destination, 4, (ushort)classDefAt);
                ClassDefTableCompiler.WritePlanned(model.BacktrackClassDef!,
                    destination.Slice(classDefAt, model.BacktrackClassDef!.ConsumedLength));
                classDefAt += model.BacktrackClassDef.ConsumedLength;
                WriteWord(destination, 6, (ushort)classDefAt);
                ClassDefTableCompiler.WritePlanned(model.InputClassDef!,
                    destination.Slice(classDefAt, model.InputClassDef!.ConsumedLength));
                classDefAt += model.InputClassDef.ConsumedLength;
                WriteWord(destination, 8, (ushort)classDefAt);
                ClassDefTableCompiler.WritePlanned(model.LookaheadClassDef!,
                    destination.Slice(classDefAt, model.LookaheadClassDef!.ConsumedLength));
            }
            WriteWord(destination, model.Format == 2 ? 10 : 4, (ushort)setCount);
            long bodyAt = arrayAt + 2L * setCount;
            for (int i = 0; i < setCount; i++)
            {
                var set = model.RuleSets[i];
                ushort target = set is null ? (ushort)0 : (ushort)bodyAt;
                CoverageTableCompiler.RequireWritten(model.Format == 2
                    ? ChainingSubstView.TryWriteChainedClassRuleSetOffsetRecordAt(destination,
                        (ulong)arrayAt + (ulong)i * (ulong)ChainingSubstLayout.OffsetStride,
                        new ChainingSubstView.ChainedClassRuleSetOffsetRecordData(i, target), out _, location)
                    : ChainingSubstView.TryWriteChainedSeqRuleSetOffsetRecordAt(destination,
                        (ulong)arrayAt + (ulong)i * ChainingSubstLayout.OffsetStride,
                        new ChainingSubstView.ChainedSeqRuleSetOffsetRecordData(i, target), out _, location));
                if (set is not null)
                {
                    WriteRuleSet(set, destination.Slice((int)bodyAt), location);
                    bodyAt += set.ConsumedLength;
                }
            }
            CoverageTableCompiler.WritePlanned(model.Coverage!.Glyphs, plan.CoverageFormat!.Value,
                plan.CoverageRangeCount, destination.Slice(coverageAt,
                    CoverageTableCompiler.Measure(model.Coverage)), location);
            return;
        }
        // Format 3: the whole header block is contiguous (format, backtrack count and
        // offset row, input count and row, lookahead count and row, seqLookupCount,
        // records) and the coverage bodies follow it in chain order - the offsets
        // target those body positions, never the header region itself.
        int backtrackCount = model.BacktrackCoverages!.Count;
        int inputCount = model.InputCoverages!.Count;
        int lookaheadCount = model.LookaheadCoverages!.Count;
        int recordCount = model.LookupRecords!.Count;
        WriteWord(destination, 0, 3);
        WriteWord(destination, 2, (ushort)backtrackCount);
        long backtrackBase = 4L + 2L * backtrackCount + 2L + 2L * inputCount + 2L +
            2L * lookaheadCount + 2L + 4L * recordCount;
        long inputBase = backtrackBase;
        foreach (var coverage in model.BacktrackCoverages)
            inputBase += CoverageTableCompiler.Measure(coverage);
        long lookaheadBase = inputBase;
        foreach (var coverage in model.InputCoverages)
            lookaheadBase += CoverageTableCompiler.Measure(coverage);
        int cursor = ChainingSubstLayout.Format3ArrayAt;
        for (int i = 0; i < backtrackCount; i++)
        {
            long target = backtrackBase;
            for (int j = 0; j < i; j++) target += CoverageTableCompiler.Measure(model.BacktrackCoverages[j]);
            WriteWord(destination, cursor + 2 * i, (ushort)target);
        }
        cursor += 2 * backtrackCount;
        WriteWord(destination, cursor, (ushort)inputCount);
        cursor += 2;
        for (int i = 0; i < inputCount; i++)
        {
            long target = inputBase;
            for (int j = 0; j < i; j++) target += CoverageTableCompiler.Measure(model.InputCoverages[j]);
            WriteWord(destination, cursor + 2 * i, (ushort)target);
        }
        cursor += 2 * inputCount;
        WriteWord(destination, cursor, (ushort)lookaheadCount);
        cursor += 2;
        for (int i = 0; i < lookaheadCount; i++)
        {
            long target = lookaheadBase;
            for (int j = 0; j < i; j++) target += CoverageTableCompiler.Measure(model.LookaheadCoverages[j]);
            WriteWord(destination, cursor + 2 * i, (ushort)target);
        }
        cursor += 2 * lookaheadCount;
        WriteWord(destination, cursor, (ushort)recordCount);
        cursor += 2;
        for (int i = 0; i < recordCount; i++)
        {
            var record = model.LookupRecords[i];
            WriteWord(destination, cursor + 4 * i, record.SequenceIndex);
            WriteWord(destination, cursor + 4 * i + 2, record.LookupListIndex);
        }
        cursor += 4 * recordCount;
        foreach (var coverage in model.BacktrackCoverages)
        {
            int size = CoverageTableCompiler.Measure(coverage);
            CoverageTableCompiler.WritePlanned(coverage.Glyphs, coverage.Format, coverage.RangeCount,
                destination.Slice(cursor, size), location);
            cursor += size;
        }
        foreach (var coverage in model.InputCoverages)
        {
            int size = CoverageTableCompiler.Measure(coverage);
            CoverageTableCompiler.WritePlanned(coverage.Glyphs, coverage.Format, coverage.RangeCount,
                destination.Slice(cursor, size), location);
            cursor += size;
        }
        foreach (var coverage in model.LookaheadCoverages)
        {
            int size = CoverageTableCompiler.Measure(coverage);
            CoverageTableCompiler.WritePlanned(coverage.Glyphs, coverage.Format, coverage.RangeCount,
                destination.Slice(cursor, size), location);
            cursor += size;
        }
    }

    private static void WriteRuleSet(ChainingRuleSet set, Span<byte> destination, DataLocation ownerLocation)
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

    private static void WriteRule(ChainingRule rule, Span<byte> destination, DataLocation ownerLocation)
    {
        var backtrack = rule.BacktrackSequence;
        var input = rule.InputSequence;
        var lookahead = rule.LookaheadSequence;
        var records = rule.LookupRecords;
        WriteWord(destination, 0, (ushort)backtrack.Length);
        for (int i = 0; i < backtrack.Length; i++) WriteWord(destination, 2 + 2 * i, backtrack[i]);
        int at = 2 + 2 * backtrack.Length;
        WriteWord(destination, at, (ushort)rule.InputCount);
        at += 2;
        for (int i = 0; i < input.Length; i++) WriteWord(destination, at + 2 * i, input[i]);
        at += 2 * input.Length;
        WriteWord(destination, at, (ushort)lookahead.Length);
        at += 2;
        for (int i = 0; i < lookahead.Length; i++) WriteWord(destination, at + 2 * i, lookahead[i]);
        at += 2 * lookahead.Length;
        WriteWord(destination, at, (ushort)records.Length);
        at += 2;
        for (int i = 0; i < records.Length; i++)
        {
            WriteWord(destination, at + 4 * i, records[i].SequenceIndex);
            WriteWord(destination, at + 4 * i + 2, records[i].LookupListIndex);
        }
    }
}
