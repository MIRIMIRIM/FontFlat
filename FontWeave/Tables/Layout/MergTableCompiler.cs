using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The MERG writer (card §4): one planned, atomically published version-0
/// emission whose canonical layout is the flat kind order - the 10-byte header, the
/// classDefOffsets array, every distinct classDef body in first-arrival order and
/// finally the row-major matrix. Every edge is strictly forward by construction. The
/// classDef bodies are emitted through the existing
/// <see cref="ClassDefTableCompiler"/> chain (zero new ClassDef emission logic), and
/// content-identical bodies dedupe to one emission with every slot aimed at the
/// representative - the write-side dual of the read chain's single materialization
/// (the T4-10 shared-identity precedent; the OQ-2 joint order runs over the deduped
/// sequence, which is what makes shared bodies legal). The whole emission lives
/// inside the table's own Offset16 window (the §0 format-design reading): the
/// matrix start is the last Offset16 target, and a plan that puts it past the
/// word refuses up front as UnsupportedOperation with the plan's Detail/Expected
/// - the read chain's 65025-entry declared maximum's write-side dual.
///
/// TryCreate admission mirrors the MATH root: version is constant 0 (not an input),
/// the numGlyphs authority re-answers against THIS root's explicit input (a subtree
/// read under another authority does not carry its proof over - every classDef's
/// numGlyphs and its GID domains re-answer here), the SameSource guard covers every
/// body, the matrix answers the exact mergeClassCount square and its reserved ridge
/// stays clear (the OQ-1 imperative at the construction face). The empty form is the
/// legal emission: zero classDefs and a zero-side matrix emit the ten-byte header
/// with both offsets at the floor (the spec's "no declarations" positive). The edit
/// witness re-roots here: <see cref="MergTable.TryWithMergeEntry"/> produces an
/// updated matrix and TryCompile re-emits byte-identically for unedited isomorphic
/// sources (the write-read-write fixed point); matrix resizes, classDef structural
/// changes and version-upgrade writes have no surface at all (the OQ-7 finite
/// refusal: the single edit API is TryWithMergeEntry, ClassDef content edits go
/// through the existing ClassDef chain and re-root as new owned inputs).</summary>
public static class MergTableCompiler
{
    /// <summary>Explicit root-model factory: the numGlyphs authority, the classDef
    /// slot sequence (shared bodies arrive as the same instance or as content-equal
    /// slots - both dedupe to one representative) and the row-major matrix bytes are
    /// caller-supplied and never inferred. The consumed length of a factory model is
    /// zero (no source extent) and the raw offset words are zeros until emission.</summary>
    public static ReadStatus TryCreate(IReadOnlyList<ClassDefTable> classDefs,
        int mergeClassCount, ReadOnlySpan<byte> matrix, int numGlyphs,
        out MergTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(classDefs);
        var budgeted = new BuildBudget(limits);
        if (!budgeted.Input(classDefs.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The computed-count dual: the square answers the declared 65025 maximum
        // (mergeClassCount 256+ refuses on the count alone, before any body look).
        if ((uint)mergeClassCount > 255 ||
            (ulong)mergeClassCount * (ulong)mergeClassCount > MergLayout.MergeEntryCountMaximum)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MergLayout.MergeClassCountAt),
                out diagnostic,
                (ulong)mergeClassCount * (ulong)mergeClassCount, MergLayout.MergeEntryCountMaximum);
        if (matrix.Length != mergeClassCount * mergeClassCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MergLayout.MergeClassCountAt),
                out diagnostic, (ulong)matrix.Length,
                (ulong)mergeClassCount * (ulong)mergeClassCount);
        if (classDefs.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MergLayout.ClassDefCountAt),
                out diagnostic, (ulong)classDefs.Count, (ulong)ushort.MaxValue);
        // The reserved ridge is the spec's imperative "set to 0" (OQ-1): a matrix
        // byte that would compile into an unreadable table refuses at construction,
        // keeping the write-read-write fixed point total.
        for (int i = 0; i < matrix.Length; i++)
        {
            byte flags = matrix[i];
            if ((flags & MergLayout.ReservedFlagsMask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, (ulong)i, i),
                    out diagnostic, flags, (ulong)(flags & MergLayout.DefinedFlagsMask));
        }
        // The authority re-answer and the SameSource guard: every distinct body's
        // numGlyphs and GID domains answer this root's input (the read-side model
        // carries its proof as a value, never as a transfer), and every body shares
        // this root's source.
        var representatives = new List<ClassDefTable>();
        var repOf = new Dictionary<ClassDefTable, ClassDefTable>(ReferenceEqualityComparer.Instance);
        foreach (var slot in classDefs)
        {
            if (slot is null)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    location.AtLocal(DataField.Offset, 0), out diagnostic);
            if (repOf.ContainsKey(slot)) continue;
            var representative = Find(representatives, slot);
            if (representative is { } found)
            {
                repOf[slot] = found;
                continue;
            }
            if (!SameSource(location, slot.Location))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    slot.Location, out diagnostic);
            if (slot.NumGlyphs != numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    slot.Location, out diagnostic, (ulong)slot.NumGlyphs, (ulong)numGlyphs);
            for (int r = 0; r < slot.Ranges.Length; r++)
            {
                var range = slot.Ranges[r];
                if (range.EndGlyph >= (ushort)numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        slot.Location.AtLocal(DataField.GlyphId,
                            (ulong)(ClassDefLayout.SparseRangesOffset +
                                (ulong)r * ClassDefLayout.RangeStride +
                                ClassDefLayout.RangeEndOffset), r),
                        out diagnostic, range.EndGlyph, (ulong)numGlyphs);
            }
            representatives.Add(slot);
            repOf[slot] = slot;
        }
        if (!budgeted.Work(1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var slots = new ClassDefTable[classDefs.Count];
        for (int i = 0; i < classDefs.Count; i++) slots[i] = repOf[classDefs[i]];
        if (!budgeted.Buffer((long)matrix.Length + 16, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        model = new((ushort)0, numGlyphs, (ushort)mergeClassCount, 0,
            (ushort)classDefs.Count, 0, new ushort[classDefs.Count], slots,
            matrix.ToArray(), location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation location, DataLocation child) =>
        location.SourceId == child.SourceId && location.FaceIndex == child.FaceIndex &&
        (location.SourceId == 0 || location.TableTag == child.TableTag);

    // Content identity (the write-side dedupe): same format and the same range
    // sequence - the emission bytes are a pure function of exactly these.
    private static bool SameClassDef(ClassDefTable a, ClassDefTable b)
    {
        if (a.Format != b.Format || a.Ranges.Length != b.Ranges.Length) return false;
        for (int i = 0; i < a.Ranges.Length; i++)
            if (a.Ranges[i] != b.Ranges[i]) return false;
        return true;
    }

    private static ClassDefTable? Find(List<ClassDefTable> bodies, ClassDefTable candidate)
    {
        foreach (var body in bodies)
            if (SameClassDef(body, candidate)) return body;
        return null;
    }

    // ------------------------------------------------------------------ plan

    /// <summary>The dedupe-aware placement: the representatives in first-arrival order
    /// with their planned positions, one measure per block, budget charged before any
    /// byte exists.</summary>
    private sealed class Placement
    {
        internal int Length;
        internal List<ClassDefTable> Bodies = new();
        internal Dictionary<ClassDefTable, ClassDefTable> BodyRep = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<ClassDefTable, int> BodyAt = new(ReferenceEqualityComparer.Instance);
        internal int OffsetsAt, MatrixAt;
        internal int MergeClassCount, ClassDefCount;
    }

    public static ReadStatus TryCompile(MergTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(MergTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Output(plan!.Length, model.Location, out diagnostic) ||
            !budget.Work(plan.Length, model.Location, out diagnostic) ||
            !budget.Buffer(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MergTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Every plan admission completes before the destination capacity check (spec
        // 7.3; the A6 1.5 O4 order); the destination is untouched on refusal.
        if (!budget.Output(plan!.Length, model.Location, out diagnostic) ||
            !budget.Work(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (destination.Length < plan.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The one deterministic plan: header, offsets array, distinct bodies in
    // first-arrival order, matrix. The Offset16 window is the single finite refusal:
    // every position and every edge target lives inside 65536 when the whole plan
    // does, and the canonical forward order keeps each edge pointing at its body.
    private static ReadStatus TryPlan(MergTable model, out Placement? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        var placement = new Placement
        {
            MergeClassCount = model.MergeClassCount,
            ClassDefCount = model.ClassDefSlots.Count,
        };
        foreach (var slot in model.ClassDefSlots)
        {
            if (placement.BodyRep.TryGetValue(slot, out var known)) continue;
            var representative = Find(placement.Bodies, slot) ?? slot;
            placement.BodyRep[slot] = representative;
            if (ReferenceEquals(representative, slot)) placement.Bodies.Add(slot);
        }
        int cursor = MergLayout.MinimumHeader;
        if (!budget.Work(cursor, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        placement.OffsetsAt = cursor;
        cursor += 2 * placement.ClassDefCount;
        foreach (var body in placement.Bodies)
        {
            int length = ClassDefTableCompiler.Measure(body);
            if (!budget.Work(length, body.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            placement.BodyAt[body] = cursor;
            cursor += length;
        }
        placement.MatrixAt = cursor;
        cursor += model.MatrixLength;
        placement.Length = cursor;
        // The Offset16 words state positions, not the exclusive end: bodies start
        // strictly before the matrix, so the matrix start is the only target that
        // can sit at the word's edge (an empty matrix under a window-filling body
        // set) and must refuse there instead of throwing at the writer's narrowing.
        if (placement.MatrixAt > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)placement.MatrixAt, (ulong)ushort.MaxValue);
        plan = placement;
        return ReadStatus.Done;
    }

    // Only a fully planned model and destination reach this path; each body writes
    // exactly once at its position (content duplicates share the position, and equal
    // content makes either instance's bytes identical).
    private static void WritePlanned(MergTable model, Placement plan, Span<byte> destination)
    {
        var location = model.Location;
        int mergeDataOffset = plan.MatrixAt;
        CoverageTableCompiler.RequireWritten(MergView.TryWriteVersion(destination, 0,
            out _, location));
        CoverageTableCompiler.RequireWritten(MergView.TryWriteMergeClassCount(destination,
            (ushort)plan.MergeClassCount, out _, location));
        CoverageTableCompiler.RequireWritten(MergView.TryWriteMergeDataOffset(destination,
            (ushort)mergeDataOffset, out _, location));
        CoverageTableCompiler.RequireWritten(MergView.TryWriteClassDefCount(destination,
            (ushort)plan.ClassDefCount, out _, location));
        CoverageTableCompiler.RequireWritten(MergView.TryWriteOffsetToClassDefOffsets(destination,
            (ushort)plan.OffsetsAt, out _, location));
        for (int i = 0; i < plan.ClassDefCount; i++)
        {
            ushort target = (ushort)plan.BodyAt[plan.BodyRep[model.ClassDefSlots[i]]];
            CoverageTableCompiler.RequireWritten(MergView.TryWriteClassDefOffsetAt(destination,
                (ulong)(plan.OffsetsAt + 2 * i), new MergView.ClassDefOffsetData(i, target),
                out _, location));
        }
        foreach (var body in plan.Bodies)
        {
            int at = plan.BodyAt[body];
            CoverageTableCompiler.RequireWritten(ClassDefTableCompiler.TryWrite(body,
                destination.Slice(at, ClassDefTableCompiler.Measure(body)), out _));
        }
        model.Matrix.CopyTo(destination.Slice(plan.MatrixAt, model.MatrixLength));
    }
}
