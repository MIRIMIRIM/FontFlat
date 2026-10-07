using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// GPOS type 6's semantic partial (T4-06 section 3.1/2.1): the type-4 face with the
// Mark2Array as the matrix side - same conventions, same shared walkers, same
// correspondence checks (mark count and mark2 count against their coverages, mark
// classes inside the classCount array-index domain). The Mark2Array count and rows
// are the OQ-T4-06-1 hand-written boundary (the shared MarkAnchorMatrix walker).
public readonly ref partial struct MarkMarkPosView
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

    public ReadStatus TryRead(int numGlyphs, out MarkMarkPosTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out MarkMarkPosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, true), out model,
            out diagnostic, budget);

    internal ReadStatus TryRead(int numGlyphs, out MarkMarkPosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, CursivePosReadState state,
        int subtableStart) =>
        Process(numGlyphs, subtableStart, state, out model, out diagnostic, budget);

    private ReadStatus Process(int numGlyphs, int position, CursivePosReadState state,
        out MarkMarkPosTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
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
        status = TryGetMark1ArrayOffset(out ushort mark1ArrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMark2ArrayOffset(out ushort mark2ArrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMark1CoverageOffset(out ushort mark1CoverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMark2CoverageOffset(out ushort mark2CoverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The hand-opened Mark2 array mirrors the generated Required-reference
        // refusals (the type-4 face's base-array trio, Mark2ArrayField provenance).
        if (mark2ArrayOffset == 0)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, MarkMarkPosLayout.Mark2ArrayField),
                out diagnostic, 0UL, (ulong)_data.Length);
        if (mark2ArrayOffset < MarkMarkPosLayout.MinimumHeader)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, MarkMarkPosLayout.Mark2ArrayField),
                out diagnostic, (ulong)mark2ArrayOffset, (ulong)MarkMarkPosLayout.MinimumHeader);
        if (mark2ArrayOffset >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, MarkMarkPosLayout.Mark2ArrayField),
                out diagnostic, (ulong)mark2ArrayOffset, (ulong)_data.Length);
        int consumed = MarkMarkPosLayout.MinimumHeader;
        status = TryOpenMark1Coverage(out _, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MarkCoverageReader.Read(numGlyphs, position + mark1CoverageOffset,
            _data, state, out var markCoverage, out var markCoverageFact, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (mark1CoverageOffset + markCoverageFact.Length > consumed)
            consumed = mark1CoverageOffset + markCoverageFact.Length;
        status = MarkCoverageReader.Read(numGlyphs, position + mark2CoverageOffset,
            _data, state, out var mark2Coverage, out var mark2CoverageFact, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (mark2CoverageOffset + mark2CoverageFact.Length > consumed)
            consumed = mark2CoverageOffset + mark2CoverageFact.Length;
        status = TryOpenMark1Array(out var markArray, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MarkSideReader.Read(markArray, classCount, markCoverageFact.Count, position,
            mark1ArrayOffset, _data, Location, state, budget, ref consumed, out var marks,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The Mark2 array count and its rows: the hand-written boundary.
        status = MarkAnchorMatrix.ReadCount(_data, mark2ArrayOffset, position, state,
            out ushort mark2Count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(mark2ArrayOffset + MarkAnchorMatrix.ArrayHeaderLength +
                (long)mark2Count * 2 * classCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (mark2Count != mark2CoverageFact.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, mark2ArrayOffset), out diagnostic,
                (ulong)mark2Count, (ulong)mark2CoverageFact.Count);
        AnchorTable?[][]? mark2Rows = state.Materialize ? new AnchorTable?[mark2Count][] : null;
        if (state.Materialize && !budget.Buffer(16L * mark2Count, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; i < mark2Count; i++)
        {
            status = MarkAnchorMatrix.ReadRow(_data, mark2ArrayOffset, mark2Count, i, classCount,
                position, state, budget, ref consumed, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (mark2Rows is not null) mark2Rows[i] = row;
        }
        if (state.Materialize) model = new(markCoverage!, marks!, mark2Coverage!, classCount,
            mark2Rows!, numGlyphs, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
