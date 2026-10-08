using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One MergeEntry flag byte (card §2): the six defined bits and the two
/// reserved bits 0x08|0x80. The boolean projections are the spec's LTR/RTL visual-order
/// vocabulary; the reserved ridge is the OQ-1 hard face at the read chain (the spec's
/// imperative "set to 0" plus the T0 rejection list), so a constructed or read entry
/// with an occupied reserved bit refuses there - these projections only expose the
/// raw word's partitions. The orphaned-subordinate projections are the observation
/// face (the spec's "then it is ignored" - recorded, never refused and never treated
/// as active merging).</summary>
public readonly record struct MergeEntry(byte Flags)
{
    /// <summary>MERGE_LTR 0x01: merge in left-to-right visual order.</summary>
    public bool IsMergeLtr => (Flags & MergLayout.MergeLtrFlag) != 0;
    /// <summary>GROUP_LTR 0x02: group in left-to-right visual order.</summary>
    public bool IsGroupLtr => (Flags & MergLayout.GroupLtrFlag) != 0;
    /// <summary>SECOND_IS_SUBORDINATE_LTR 0x04.</summary>
    public bool IsSecondIsSubordinateLtr => (Flags & MergLayout.SecondIsSubordinateLtrFlag) != 0;
    /// <summary>MERGE_RTL 0x10: merge in right-to-left visual order.</summary>
    public bool IsMergeRtl => (Flags & MergLayout.MergeRtlFlag) != 0;
    /// <summary>GROUP_RTL 0x20: group in right-to-left visual order.</summary>
    public bool IsGroupRtl => (Flags & MergLayout.GroupRtlFlag) != 0;
    /// <summary>SECOND_IS_SUBORDINATE_RTL 0x40.</summary>
    public bool IsSecondIsSubordinateRtl => (Flags & MergLayout.SecondIsSubordinateRtlFlag) != 0;
    /// <summary>The reserved ridge 0x08|0x80 - zero on every legal wire (OQ-1 hard face).</summary>
    public byte ReservedBits => (byte)(Flags & MergLayout.ReservedFlagsMask);
    /// <summary>SecondIsSubordinate set with neither Merge nor Group in the same
    /// visual order: the spec's ignored form (the observation witness shape).</summary>
    public bool IsSubordinateLtrOrphan => IsSecondIsSubordinateLtr && !IsMergeLtr && !IsGroupLtr;
    /// <summary>The right-to-left orphan form of the same ignored shape.</summary>
    public bool IsSubordinateRtlOrphan => IsSecondIsSubordinateRtl && !IsMergeRtl && !IsGroupRtl;
}

/// <summary>The MERG read-only door (card §2): the generated accessors own the layout
/// (version gate, the two Required offsets, the classDefOffsets array over the raw
/// scalar start, the flat row-major merge matrix under the computed square count) and
/// the ClassDef reference openers; this partial adds the semantics the wire's
/// two-level "rows × columns" organization needs - the two-dimensional accessor over
/// the flat array (row = first element's class, column = second's, flat position
/// row·mergeClassCount + column) with the OQ-3 lean shape - and the whole-table
/// R/S/V face (card §3). No child view exists for the matrix (the count lives at the
/// root and the matrix owns no count bytes) and no MergClassDefView exists (the T4-01
/// frozen ClassDefView is the one formal ClassDef implementation; AGENTS'
/// single-View rule).</summary>
public readonly ref partial struct MergView
{
    /// <summary>Whole-table validate face (card §3): one strict pass whose checks are
    /// exactly the card's hard list - the native version gate, the two Required offset
    /// floors and windows, the array/matrix extent truncations, every distinct
    /// classDef body through the T4-01 frozen <see cref="ClassDefView.Validate"/>
    /// contract with the caller's numGlyphs authority, the OQ-2 cross-table joint
    /// strict increase over the single-materialized bodies in array order and the OQ-1
    /// reserved-bit refusal, byte-localized at the payload. The observations (orphaned
    /// SecondIsSubordinate, shared-offset identity, consumed slack) are never reported
    /// here - no diagnostic is ever emitted for them.</summary>
    public ReadStatus ValidateTable(int numGlyphs, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ReadTable(numGlyphs, false, out _, out diagnostic, budget);
    }

    /// <summary>Reads the complete table into an owned <see cref="MergTable"/> with
    /// every distinct classDef body materialized per budget. The materialize flag
    /// decides owned models, never which checks run: ValidateTable and TryReadTable
    /// answer with identical diagnostics at identical locations.</summary>
    public ReadStatus TryReadTable(int numGlyphs, out MergTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = ReadTable(numGlyphs, true, out var read, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = read;
        return ReadStatus.Done;
    }

    // The one strict pass, in the card's error priority (version -> existence domain
    // -> bounds -> relations -> value domain): the computed count gate precedes any
    // scanning (the OQ-5 declared maximum), then the two offset floors/windows in wire
    // order, the array/matrix extents, the per-slot classDef consumption (single
    // materialization per distinct wire body), the OQ-2 joint order over that
    // materialized sequence and the OQ-1 reserved-ridge scan over the matrix bytes.
    private ReadStatus ReadTable(int numGlyphs, bool materialize, out MergTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(MergLayout.MinimumHeader, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryGetMergeClassCount(out ushort mergeClassCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMergeEntryCount(out ulong entryCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMergeDataOffset(out ushort mergeDataOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RequireOffsetField(MergLayout.MergeDataOffsetAt, mergeDataOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetOffsetToClassDefOffsets(out ushort offsetsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RequireOffsetField(MergLayout.OffsetToClassDefOffsetsAt, offsetsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetClassDefCount(out ushort classDefCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The two extent faces (whole-extent form: the source length against the
        // declared end, at the array's own start byte).
        ulong arrayEnd = (ulong)offsetsOffset + 2UL * classDefCount;
        if (arrayEnd > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, offsetsOffset), out diagnostic,
                (ulong)_data.Length, arrayEnd);
        ulong matrixEnd = (ulong)mergeDataOffset + entryCount;
        if (matrixEnd > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, mergeDataOffset), out diagnostic,
                (ulong)_data.Length, matrixEnd);
        if (!budget.Work(2L * classDefCount + (long)entryCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        // Per-slot classDef consumption: the declared opener owns the per-item zero
        // policy, the HeaderEnd floor and the source bounds; a repeated offset is
        // legal sharing and materializes (and proves) exactly once - the GDEF
        // dual-offset identity, keyed by the body's own wire position.
        var slots = new ClassDefTable?[classDefCount];
        var offsets = new ushort[classDefCount];
        var bodies = new Dictionary<ulong, ClassDefTable>();
        var sequence = new List<(int FirstSlot, int FirstGlyph, int LastGlyph)>();
        for (int i = 0; i < classDefCount; i++)
        {
            status = TryGetClassDefOffset(i, out var slot, out diagnostic);
            if (status != ReadStatus.Done) return status;
            offsets[i] = slot.Offset;
            // The membership test decides the skip (validate mode stores the null
            // model for an already-proven body; read mode stores the shared
            // instance - either way the body's checks run exactly once).
            if (bodies.TryGetValue(slot.Offset, out var shared))
            {
                slots[i] = shared;
                continue;
            }
            status = TryOpenClassDef(i, out var classView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ClassDefTable? body = null;
            if (materialize)
            {
                status = classView.TryRead(numGlyphs, out body, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            else
            {
                status = classView.Validate(numGlyphs, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            status = BodyExtent(classView, body, out int firstGlyph, out int lastGlyph,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            bodies[slot.Offset] = body!;
            slots[i] = body;
            if (firstGlyph >= 0)
                sequence.Add((i, firstGlyph, lastGlyph));
        }

        // The OQ-2 cross-table joint strict increase: over the single-materialized
        // non-empty bodies in array order, max(k) < min(k+1) - the spec's "as the
        // class definition tables are read in order" with "at most one class"; a
        // violation makes the whole MERG table invalid.
        for (int i = 1; i < sequence.Count; i++)
        {
            var left = sequence[i - 1];
            var right = sequence[i];
            if (left.LastGlyph >= right.FirstGlyph)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset,
                        (ulong)(offsetsOffset + 2 * right.FirstSlot), right.FirstSlot),
                    out diagnostic, (ulong)right.FirstGlyph, (ulong)(left.LastGlyph + 1));
        }

        // The OQ-1 reserved-ridge scan: the first offending byte in row-major order
        // refuses, byte-localized at its payload position (detail = the raw byte,
        // expected = the byte's defined-bit residue).
        var matrix = RawBytes.Slice(mergeDataOffset, (int)entryCount);
        for (int i = 0; i < matrix.Length; i++)
        {
            byte flags = matrix[i];
            if ((flags & MergLayout.ReservedFlagsMask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload, (ulong)(mergeDataOffset + i), i),
                    out diagnostic, flags, (ulong)(flags & MergLayout.DefinedFlagsMask));
        }

        int consumed = MergLayout.MinimumHeader;
        consumed = Math.Max(consumed, offsetsOffset + 2 * classDefCount);
        consumed = Math.Max(consumed, mergeDataOffset + (int)entryCount);
        if (materialize)
        {
            var ownedSlots = new ClassDefTable[classDefCount];
            for (int i = 0; i < classDefCount; i++)
            {
                ownedSlots[i] = slots[i]!;
                if (slots[i] is { } body)
                    consumed = Math.Max(consumed, offsets[i] + body.ConsumedLength);
            }
            if (!budget.Buffer((long)entryCount + 16, Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            model = new((ushort)0, numGlyphs, mergeClassCount, mergeDataOffset,
                classDefCount, offsetsOffset, offsets, ownedSlots, matrix.ToArray(),
                Location, consumed);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The declared Required offset's two semantic faces (the raw scalars carry no
    // declared reference): below the MinimumHeader floor is the "points back at the
    // header" form (0 included - the OQ-6 Required non-zero ruling), beyond the table
    // window is the Offset16 escape. Both localize at the header slot itself.
    private ReadStatus RequireOffsetField(int fieldAt, ushort raw, out FontDiagnostic diagnostic)
    {
        if (raw < MergLayout.MinimumHeader)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, (ulong)fieldAt), out diagnostic,
                raw, (ulong)MergLayout.MinimumHeader);
        if (raw > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, (ulong)fieldAt), out diagnostic,
                raw, (ulong)_data.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One body's [first, last] glyph span for the OQ-2 joint walk: the materialized
    // model's ranges when reading, the view's own headers otherwise; an empty table
    // has no span (first < 0) and is skipped by the joint walk. The span faces live
    // entirely inside the T4-01 frozen contract's accessors - this composes them, it
    // does not reparse the body.
    private ReadStatus BodyExtent(ClassDefView classView, ClassDefTable? body,
        out int firstGlyph, out int lastGlyph, out FontDiagnostic diagnostic)
    {
        firstGlyph = lastGlyph = -1;
        if (body is { } materialized)
        {
            if (materialized.Ranges.Length > 0)
            {
                firstGlyph = materialized.Ranges[0].StartGlyph;
                lastGlyph = materialized.Ranges[^1].EndGlyph;
            }
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status;
        if (classView.Format == 1)
        {
            status = classView.TryGetDenseHeader(out var header, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (header.GlyphCount > 0)
            {
                firstGlyph = header.StartGlyph;
                lastGlyph = header.StartGlyph + header.GlyphCount - 1;
            }
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = classView.TryGetSparseHeader(out var sparse, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (sparse.RangeCount > 0)
        {
            status = classView.TryGetRange(0, out var first, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = classView.TryGetRange(sparse.RangeCount - 1, out var last, out diagnostic);
            if (status != ReadStatus.Done) return status;
            firstGlyph = first.StartGlyph;
            lastGlyph = last.EndGlyph;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads one merge entry by its (row, column) merge-class pair. The row
    /// is the first element's class and the column the second's (the spec's visual
    /// order note); both answer the mergeClassCount domain (InvalidOrdinal), the flat
    /// position is row·mergeClassCount + column (the row-major wire order), and the
    /// generated per-ordinal accessor owns the extent and truncation faces.</summary>
    public ReadStatus TryGetMergeEntry(int row, int column, out MergeEntry entry,
        out FontDiagnostic diagnostic)
    {
        entry = default;
        ReadStatus status = TryGetMergeClassCount(out ushort mergeClassCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((uint)row >= mergeClassCount)
            return InvalidOrdinal(row, mergeClassCount, out diagnostic);
        if ((uint)column >= mergeClassCount)
            return InvalidOrdinal(column, mergeClassCount, out diagnostic);
        status = TryGetMergeEntry(row * mergeClassCount + column,
            out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        entry = new(record.Flags);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus InvalidOrdinal(int ordinal, int mergeClassCount,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            Location.AtLocal(DataField.Count, MergLayout.MergeClassCountAt, ordinal),
            out diagnostic, unchecked((ulong)(long)ordinal), (ulong)mergeClassCount);
}
