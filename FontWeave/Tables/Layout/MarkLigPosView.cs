using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// GPOS type 5's semantic partial (T4-06 section 3.1/2.2). Same conventions as the
// type-4 face: one shared per-Lookup read state, validate-without-materialize and
// ReadOwned. The declared hops (two coverages, MarkArray, LigatureArray and each
// attach entry) refuse through their generated openers - zero/floor/bounds keep the
// declared shapes; the attach bodies' ComponentRecord rows stay the OQ-T4-06-1
// hand-written boundary (componentCount declared at the attach target, rows
// classCount-driven, row anchors Optional via the shared MarkAnchorMatrix walker).
// componentCount 0 is the lean legal empty attach (OQ-T4-06-5); attach count ==
// ligatureCoverage count is the same hard correspondence as the type-4 base side.
// The only real witness is Open Sans VF (facts R2: three subtables, component
// counts 2..3).
public readonly ref partial struct MarkLigPosView
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

    public ReadStatus TryRead(int numGlyphs, out MarkLigPosTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out MarkLigPosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, true), out model,
            out diagnostic, budget);

    // The Lookup walk's entry: subtableStart keys the shared state's absolute
    // Lookup-local positions (the cursive chain's contract, byte-identical shape).
    internal ReadStatus TryRead(int numGlyphs, out MarkLigPosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, CursivePosReadState state,
        int subtableStart) =>
        Process(numGlyphs, subtableStart, state, out model, out diagnostic, budget);

    private ReadStatus Process(int numGlyphs, int position, CursivePosReadState state,
        out MarkLigPosTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
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
        status = TryGetLigatureArrayOffset(out ushort ligArrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMarkCoverageOffset(out ushort markCoverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetLigatureCoverageOffset(out ushort ligCoverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int consumed = MarkLigPosLayout.MinimumHeader;
        status = TryOpenMarkCoverage(out _, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MarkCoverageReader.Read(numGlyphs, position + markCoverageOffset,
            _data, state, out var markCoverage, out var markCoverageFact, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (markCoverageOffset + markCoverageFact.Length > consumed)
            consumed = markCoverageOffset + markCoverageFact.Length;
        status = MarkCoverageReader.Read(numGlyphs, position + ligCoverageOffset, _data, state,
            out var ligCoverage, out var ligCoverageFact, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (ligCoverageOffset + ligCoverageFact.Length > consumed)
            consumed = ligCoverageOffset + ligCoverageFact.Length;
        status = TryOpenMarkArray(out var markArray, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MarkSideReader.Read(markArray, classCount, markCoverageFact.Count, position,
            markArrayOffset, _data, Location, state, budget, ref consumed, out var marks,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The declared LigatureArray hop proves the offset-record extent; the attach
        // count correspondence mirrors the type-4 base side at the array's own count.
        status = TryOpenLigatureArray(out var ligArray, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ligArray.TryRequireLigatureAttachOffsetRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        int ligCount = ligArray.LigatureCount;
        if (!budget.Work(LigatureArrayLayout.MinimumHeader +
                (long)ligCount * LigatureArrayLayout.OffsetStride, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (ligCount != ligCoverageFact.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, ligArrayOffset), out diagnostic,
                (ulong)ligCount, (ulong)ligCoverageFact.Count);
        var attaches = state.Materialize ? new LigatureAttachTable[ligCount] : null;
        if (state.Materialize && !budget.Buffer(16L * ligCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; i < ligCount; i++)
        {
            status = ligArray.TryGetLigatureAttachOffsetRecord(i, out var attachRecord,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            // The declared attach entry proves zero/floor(Minimum=RecordsEnd)/bounds;
            // the subtable-local body position keys the shared state's facts.
            status = ligArray.TryOpenLigatureAttach(i, out var attachView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int attachLocal = ligArrayOffset + attachRecord.LigatureAttachOffset;
            status = ReadAttach(attachView, attachLocal, classCount, position, state, budget,
                ref consumed, out var attach, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (attaches is not null) attaches[i] = attach!;
        }
        if (state.Materialize) model = new(markCoverage!, marks!, ligCoverage!, attaches!,
            numGlyphs, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One attach body at the subtable-local offset: the declared component count
    // plus the classCount-wide anchor rows (the shared matrix walker).
    private ReadStatus ReadAttach(LigatureAttachView attachView, int attachLocal,
        ushort classCount, int position, CursivePosReadState state, BuildBudget budget,
        ref int consumed, out LigatureAttachTable? attach, out FontDiagnostic diagnostic)
    {
        attach = null;
        ReadStatus status;
        if (!budget.Work(1, state.At(position + attachLocal), out diagnostic)) return ReadStatus.ResourceLimit;
        ushort componentCount = attachView.ComponentCount;
        if (!budget.Work((long)componentCount * 2 * classCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var componentRows = state.Materialize ? new AnchorTable?[componentCount][] : null;
        if (state.Materialize && !budget.Buffer(16L * componentCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int attachConsumed = MarkAnchorMatrix.ArrayHeaderLength;
        for (int c = 0; c < componentCount; c++)
        {
            status = MarkAnchorMatrix.ReadRow(_data, attachLocal, componentCount, c, classCount,
                position, state, budget, ref attachConsumed, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (componentRows is not null) componentRows[c] = row;
        }
        if (attachLocal + attachConsumed > consumed) consumed = attachLocal + attachConsumed;
        if (state.Materialize) attach = new(componentCount, componentRows!, Location, attachConsumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
