using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct CursivePosView
{
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, false), out _, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, false), out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out CursivePosTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out CursivePosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, true), out model, out diagnostic, budget);

    internal ReadStatus TryRead(int numGlyphs, out CursivePosTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, CursivePosReadState state, int subtableStart) =>
        Process(numGlyphs, subtableStart, state, out model, out diagnostic, budget);

    // One EntryExit record per covered glyph (W's correspondence); both anchor slots
    // may be NULL individually or together (spec gpos.md:374, OQ-T4-05-4), meaning no
    // positioning adjustment. Anchor bodies dedupe by physical position so shared
    // bodies stay one instance (W5) and recompilation reproduces the wire's sharing.
    private ReadStatus Process(int numGlyphs, int position, CursivePosReadState state,
        out CursivePosTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (state.NumGlyphs != numGlyphs || position < 0 || position != state.Length - _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace, Location, out diagnostic);
        int count = EntryExitCount;
        int recordsEnd = CursivePosLayout.MinimumHeader + CursivePosLayout.RecordStride * count;
        status = TryRequireEntryExitRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(recordsEnd + (long)count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryGetCoverageOffset(out ushort coverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadCoverage(numGlyphs, position + coverageOffset, state,
            out var coverage, out var coverageFact, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (count != coverageFact.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, CursivePosLayout.CountField), out diagnostic,
                (ulong)count, (ulong)coverageFact.Count);
        EntryExitTable[]? records = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(16L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            records = new EntryExitTable[count];
        }
        int consumed = Math.Max(recordsEnd, coverageOffset + coverageFact.Length);
        for (int i = 0; i < count; i++)
        {
            status = TryGetEntryExitRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ReadAnchorSlot(record.EntryAnchorOffset, CursivePosLayout.EntryOffsetAt, i,
                recordsEnd, position, state, ref consumed, out var entry, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            status = ReadAnchorSlot(record.ExitAnchorOffset, CursivePosLayout.ExitOffsetAt, i,
                recordsEnd, position, state, ref consumed, out var exit, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (records is not null) records[i] = new(entry, exit);
        }
        if (state.Materialize) model = new(coverage!, records ?? [], numGlyphs, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One anchor slot: offset 0 keeps the no-adjustment NULL semantics (the declared
    // opener's zero policy); a nonzero offset is floor/bounds checked by the same
    // opener (W10's RangeOutsideSource pin) before the deduped anchor read.
    private ReadStatus ReadAnchorSlot(ushort offset, int fieldAt, int ordinal,
        int recordsEnd, int position, CursivePosReadState state, ref int consumed,
        out AnchorTable? anchor, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        anchor = null;
        ReadStatus status = TryOpenAnchorAt(fieldAt, ordinal, out var anchorView,
            out bool present, out diagnostic);
        if (status != ReadStatus.Done || !present) return status;
        status = ReadAnchor(position + offset, anchorView, state, out anchor, out int length,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (offset + length > consumed) consumed = offset + length;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadAnchor(int target, AnchorView view, CursivePosReadState state,
        out AnchorTable? model, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null; length = 0;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.AnchorFacts.TryGetValue(target, out var fact))
        {
            length = fact.Length;
            if (state.Materialize) state.Anchors.TryGetValue(target, out model);
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus read = view.TryRead(out model, out int bodyLength, out diagnostic, budget, state, target);
        if (read != ReadStatus.Done) return read;
        length = bodyLength;
        if (!budget.Buffer(state.Materialize ? 32 : 16, state.At(target), out diagnostic)) return ReadStatus.ResourceLimit;
        state.AnchorFacts.Add(target, new(1, length));
        if (state.Materialize) state.Anchors.Add(target, model!);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The generated opener is role-less and counted-array scoped; the two anchor slots
    // share one declaration shape, so the slot's element offset picks the field.
    private ReadStatus TryOpenAnchorAt(int fieldAt, int ordinal,
        out AnchorView anchor, out bool present, out FontDiagnostic diagnostic)
    {
        anchor = default;
        present = false;
        return fieldAt == CursivePosLayout.EntryOffsetAt
            ? TryOpenEntryAnchor(ordinal, out anchor, out present, out diagnostic)
            : TryOpenExitAnchor(ordinal, out anchor, out present, out diagnostic);
    }

    private ReadStatus ReadCoverage(int numGlyphs, int target, CursivePosReadState state,
        out CoverageTable? model, out CursivePosReadState.Leaf fact, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null; fact = default;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.CoverageFacts.TryGetValue(target, out fact))
        { if (state.Materialize) state.Coverages.TryGetValue(target, out model); diagnostic = default; return ReadStatus.Done; }
        int local = target - (state.Length - _data.Length);
        // The coverage target must land inside this view's own slice: an offset
        // beyond it is the range refusal, never a slice past the view.
        if (local < 0 || local > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                state.At(target), out diagnostic, (ulong)target, (ulong)state.Length);
        ReadStatus status = CoverageView.TryOpen(_data.Slice(local), out var view, out diagnostic, state.At(target));
        if (status != ReadStatus.Done) return status;
        status = view.Inspect(numGlyphs, out int count, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (state.Materialize)
        {
            // Expansion consumes the facts Inspect just proved; no second inspection.
            status = view.Materialize(numGlyphs, count, length, out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (!budget.Buffer(state.Materialize ? 32 : 16, state.At(target), out diagnostic)) return ReadStatus.ResourceLimit;
        fact = new(count, length);
        state.CoverageFacts.Add(target, fact);
        if (state.Materialize) state.Coverages.Add(target, model!);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
