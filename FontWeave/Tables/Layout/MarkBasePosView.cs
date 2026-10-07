using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// GPOS type 4's semantic partial (T4-06 section 3.1). The direct-leaf conventions:
// one shared per-Lookup read state (the T4-05 anchor/coverage facts caches, reused
// verbatim), Inspect-shaped validation without materialization and ReadOwned for
// the model. Correspondence: markCount == markCoverage count and baseCount ==
// baseCoverage count (spec orders both arrays by their coverage); every mark class
// stays inside the header's classCount array-index domain (InvalidIndex,
// OQ-T4-06-2's hard check; the classCount-to-peak relation stays an observation).
// The BaseArray count and rows are the OQ-T4-06-1 hand-written boundary - the
// shared MarkAnchorMatrix walker serves them.
public readonly ref partial struct MarkBasePosView
{
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, false), out _,
            out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, false), out _,
            out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out MarkBasePosTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out MarkBasePosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, true), out model,
            out diagnostic, budget);

    // The Lookup walk's entry: subtableStart keys the shared state's absolute
    // Lookup-local positions (the cursive chain's contract, byte-identical shape).
    internal ReadStatus TryRead(int numGlyphs, out MarkBasePosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, CursivePosReadState state,
        int subtableStart) =>
        Process(numGlyphs, subtableStart, state, out model, out diagnostic, budget);

    private ReadStatus Process(int numGlyphs, int position, CursivePosReadState state,
        out MarkBasePosTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (state.NumGlyphs != numGlyphs || position < 0 || position != state.Length - _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace, Location, out diagnostic);
        status = TryGetClassCount(out ushort classCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMarkArrayOffset(out ushort markArrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetBaseArrayOffset(out ushort baseArrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMarkCoverageOffset(out ushort markCoverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetBaseCoverageOffset(out ushort baseCoverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The hand-opened matrix array mirrors the generated Required-reference
        // refusals (zero: InvalidOffset/RangeOutsideSource at the offset field with
        // the window length; floor/bounds: StorageBeforeRecords against the 12-byte
        // header end); the declared MarkArray hop refuses through its own opener.
        if (baseArrayOffset == 0)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, MarkBasePosLayout.BaseArrayField),
                out diagnostic, 0UL, (ulong)_data.Length);
        if (baseArrayOffset < MarkBasePosLayout.MinimumHeader)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, MarkBasePosLayout.BaseArrayField),
                out diagnostic, (ulong)baseArrayOffset, (ulong)MarkBasePosLayout.MinimumHeader);
        if (baseArrayOffset >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, MarkBasePosLayout.BaseArrayField),
                out diagnostic, (ulong)baseArrayOffset, (ulong)_data.Length);
        int consumed = MarkBasePosLayout.MinimumHeader;
        status = TryOpenMarkCoverage(out _, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MarkCoverageReader.Read(numGlyphs, position + markCoverageOffset,
            _data, state, out var markCoverage, out var markCoverageFact,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (markCoverageOffset + markCoverageFact.Length > consumed)
            consumed = markCoverageOffset + markCoverageFact.Length;
        status = MarkCoverageReader.Read(numGlyphs, position + baseCoverageOffset,
            _data, state, out var baseCoverage, out var baseCoverageFact,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (baseCoverageOffset + baseCoverageFact.Length > consumed)
            consumed = baseCoverageOffset + baseCoverageFact.Length;
        status = TryOpenMarkArray(out var markArray, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MarkSideReader.Read(markArray, classCount, markCoverageFact.Count, position,
            markArrayOffset, _data, Location, state, budget, ref consumed,
            out var marks, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The base array count and its rows: the hand-written boundary.
        status = MarkAnchorMatrix.ReadCount(_data, baseArrayOffset, position, state,
            out ushort baseCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(baseArrayOffset + MarkAnchorMatrix.ArrayHeaderLength +
                (long)baseCount * 2 * classCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (baseCount != baseCoverageFact.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, baseArrayOffset), out diagnostic,
                (ulong)baseCount, (ulong)baseCoverageFact.Count);
        AnchorTable?[][]? baseRows = state.Materialize ? new AnchorTable?[baseCount][] : null;
        if (state.Materialize && !budget.Buffer(16L * baseCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        AnchorTable?[] row = [];
        for (int i = 0; i < baseCount; i++)
        {
            status = MarkAnchorMatrix.ReadRow(_data, baseArrayOffset, baseCount, i, classCount,
                position, state, budget, ref consumed, out row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (baseRows is not null) baseRows[i] = row;
        }
        if (state.Materialize) model = new(markCoverage!, marks!, baseCoverage!, classCount,
            baseRows!, numGlyphs, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
