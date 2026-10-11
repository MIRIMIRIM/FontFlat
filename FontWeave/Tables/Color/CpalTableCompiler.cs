using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Canonical CPAL compiler (card T6-01 §4, OQ-T6-01-8's majority
/// order). One planned layout candidate: header(12) → colorRecordIndices →
/// colorRecords; on v1 the three Offset32 slots physically follow the indices
/// (spec L68-82) and the three optional arrays trail in spec field order
/// (paletteTypes → paletteLabels → paletteEntryLabels), each emitted exactly
/// when supplied - the corpus's 11/16 tail-placed majority, so most v0 wires
/// and 11 v1 hosts recompile byte-identical and the mid-placed forms land in
/// the wire-equivalent tier. Every offset is a single forward emission (no
/// nesting, no fixpoint). BGRA rows travel byte-for-byte and record rows are
/// never re-encoded, so an unedited round trip preserves every payload byte.
/// Structural changes - palette, entry or record count changes, version
/// conversion - have no edit surface (OQ-T6-01-9); the four value edits return
/// a new owned model and never mutate the source.</summary>
public static class CpalTableCompiler
{
    /// <summary>Explicit root-model factory from owned parts (card §4.1): the
    /// root re-answers the structural invariants the read side enforces - the
    /// L43 three-zero minimum shape, the L45 upper bound in wide arithmetic, the
    /// v1-only arrays' coupling with the declared version and the reserved
    /// 0xFFFC mask. Each color-record row must be a 4-byte BGRA quad; rows are
    /// stored verbatim. There are no child models, so the SameSource guard
    /// surface is empty by construction.</summary>
    public static ReadStatus TryCreate(ushort version, ushort numPaletteEntries,
        IReadOnlyList<ushort> colorRecordIndices, IReadOnlyList<byte[]> colorRecords,
        IReadOnlyList<uint>? paletteTypes, IReadOnlyList<ushort>? paletteLabels,
        IReadOnlyList<ushort>? paletteEntryLabels, out CpalTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(colorRecordIndices);
        ArgumentNullException.ThrowIfNull(colorRecords);
        location = location == default ? DataLocation.Anonymous(CpalLayout.Tag) : location;
        var budget = new BuildBudget(limits);
        if (version is not (CpalLayout.Version0 or CpalLayout.Version1))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, version);
        // The v1-only arrays are a version coupling, both directions: supplying
        // one on a v0 model is the refused upgrade path (OQ-T6-01-9's version
        // conversion face).
        if (version == CpalLayout.Version0 &&
            (paletteTypes is not null || paletteLabels is not null || paletteEntryLabels is not null))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Version, 0), out diagnostic, version, CpalLayout.Version1);
        if (numPaletteEntries == 0 || colorRecordIndices.Count == 0 || colorRecords.Count == 0)
        {
            // the three-zero minimum shape, in the read side's wire order
            ushort at = numPaletteEntries == 0 ? (ushort)2
                : colorRecordIndices.Count == 0 ? (ushort)4 : (ushort)6;
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, at), out diagnostic, 0UL, 1UL);
        }
        // The u16 count fields bound the plan: an array longer than its count
        // word's domain cannot be declared on the wire (the NameContent line).
        if (colorRecordIndices.Count > ushort.MaxValue || colorRecords.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count,
                    colorRecordIndices.Count > ushort.MaxValue ? 4UL : 6UL), out diagnostic,
                (ulong)(colorRecordIndices.Count > ushort.MaxValue
                    ? colorRecordIndices.Count : colorRecords.Count), (ulong)ushort.MaxValue);
        if (!budget.Work(colorRecordIndices.Count + colorRecords.Count +
                2L * (paletteEntryLabels?.Count ?? 0), location, out diagnostic))
            return ReadStatus.ResourceLimit;
        foreach (var row in colorRecords)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (row.Length != 4)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, 0), out diagnostic,
                    (uint)row.Length, 4UL);
        }
        // The L45 bound re-answer (wide arithmetic; the read side's face).
        uint maxIndex = 0;
        foreach (ushort index in colorRecordIndices)
            if (index > maxIndex) maxIndex = index;
        if ((uint)maxIndex + (uint)numPaletteEntries > (uint)colorRecords.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, CpalLayout.IndicesAt), out diagnostic,
                (uint)maxIndex + (uint)numPaletteEntries, (uint)colorRecords.Count);
        // The trailing arrays' shape coupling: each supplied array is exactly its
        // declared domain, and the reserved mask refuses with the read-side face.
        if (paletteTypes is not null && paletteTypes.Count != colorRecordIndices.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 4), out diagnostic,
                (uint)paletteTypes.Count, (uint)colorRecordIndices.Count);
        if (paletteTypes is not null)
        {
            for (int i = 0; i < paletteTypes.Count; i++)
            {
                if ((paletteTypes[i] & CpalLayout.PaletteReservedMask) != 0)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Flags, 0, i), out diagnostic,
                        paletteTypes[i], 0UL);
            }
        }
        if (paletteLabels is not null && paletteLabels.Count != colorRecordIndices.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 4), out diagnostic,
                (uint)paletteLabels.Count, (uint)colorRecordIndices.Count);
        if (paletteEntryLabels is not null && paletteEntryLabels.Count != numPaletteEntries)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 2), out diagnostic,
                (uint)paletteEntryLabels.Count, (uint)numPaletteEntries);
        // The factory model carries the canonical plan's own coordinates, so edit
        // diagnostics stay addressable before the first compile re-roots them
        // (the COLR factory line).
        PlanCanonicalLayout(version, colorRecordIndices.Count, numPaletteEntries,
            colorRecords.Count, paletteTypes is not null, paletteLabels is not null,
            paletteEntryLabels is not null, out uint recordsOffset, out uint typesOffset,
            out uint labelsOffset, out uint entryLabelsOffset, out long length);
        model = new(version, numPaletteEntries, [.. colorRecordIndices], [.. colorRecords],
            paletteTypes is null ? null : [.. paletteTypes],
            paletteLabels is null ? null : [.. paletteLabels],
            paletteEntryLabels is null ? null : [.. paletteEntryLabels],
            recordsOffset, typesOffset, labelsOffset, entryLabelsOffset, location, (int)length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Deterministic write through the same planned emission; every
    /// budget admission completes before any byte lands, and a short destination
    /// leaves the span untouched (the T5-05 atomicity discipline).</summary>
    public static ReadStatus TryWrite(CpalTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = TryCompile(model, out byte[]? encoded, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < encoded!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (uint)encoded.Length);
        encoded.AsSpan().CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(CpalTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        // Unreachable for TryReadTable/TryCreate models: a hand-built model
        // carrying v1 arrays on v0 refuses instead of emitting them.
        if (!model.IsVersion1 && (model.PaletteTypes is not null || model.PaletteLabels is not null
                || model.PaletteEntryLabels is not null))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Version, 0), out diagnostic,
                model.Version, CpalLayout.Version1);
        if (model.PaletteCount > ushort.MaxValue || model.ColorRecordCount > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Count,
                    model.PaletteCount > ushort.MaxValue ? 4UL : 6UL), out diagnostic,
                (ulong)(model.PaletteCount > ushort.MaxValue
                    ? model.PaletteCount : model.ColorRecordCount), (ulong)ushort.MaxValue);
        // The plan: the canonical layout's sizes are exact functions of the model
        // (no nesting, no fixpoint), so one admission covers the whole emission.
        PlanCanonicalLayout(model.Version, model.PaletteCount, model.NumPaletteEntries,
            model.ColorRecordCount, model.PaletteTypes is not null,
            model.PaletteLabels is not null, model.PaletteEntryLabels is not null,
            out uint recordsOffset, out uint typesOffset, out uint labelsOffset,
            out uint entryLabelsOffset, out long length);
        if (length > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.AtLocal(DataField.Length, 0), out diagnostic,
                (ulong)length, (ulong)int.MaxValue);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        foreach (var row in model.ColorRecords)
            if (row is null || row.Length != 4)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.Payload, recordsOffset), out diagnostic,
                    (ulong)(row?.Length ?? 0), 4UL);
        var output = new byte[(int)length];
        // After the capacity preflight these generated writers cannot fail; a
        // failure is a writer contract bug and surfaces loudly.
        RequireWritten(CpalView.TryWriteVersion(output, model.Version, out _, model.Location));
        RequireWritten(CpalView.TryWriteNumPaletteEntries(output, model.NumPaletteEntries, out _, model.Location));
        RequireWritten(CpalView.TryWriteNumPalettes(output, (ushort)model.PaletteCount, out _, model.Location));
        RequireWritten(CpalView.TryWriteNumColorRecords(output, (ushort)model.ColorRecordCount, out _, model.Location));
        RequireWritten(CpalView.TryWriteColorRecordsArrayOffset(output, recordsOffset, out _, model.Location));
        for (int i = 0; i < model.PaletteCount; i++)
        {
            RequireWritten(CpalView.TryWriteColorRecordIndices(output, (ulong)model.PaletteCount,
                new CpalView.ColorRecordIndicesData(i, model.ColorRecordIndices[i]),
                out _, model.Location));
        }
        if (model.IsVersion1)
        {
            // The three v1 Offset32 slots physically follow the indices array
            // (spec L68-82) - runtime-positioned fields with no generated writer,
            // written at the plan's declared slot base.
            uint slotsAt = (uint)(CpalLayout.IndicesAt + 2L * model.PaletteCount);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                output.AsSpan((int)slotsAt), typesOffset);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                output.AsSpan((int)slotsAt + 4), labelsOffset);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                output.AsSpan((int)slotsAt + 8), entryLabelsOffset);
        }
        for (int i = 0; i < model.ColorRecordCount; i++)
            model.ColorRecords[i].AsSpan().CopyTo(output.AsSpan((int)recordsOffset + 4 * i));
        if (model.PaletteTypes is not null)
        {
            for (int i = 0; i < model.PaletteTypes.Count; i++)
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                    output.AsSpan((int)typesOffset + 4 * i), model.PaletteTypes[i]);
        }
        if (model.PaletteLabels is not null)
        {
            for (int i = 0; i < model.PaletteLabels.Count; i++)
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                    output.AsSpan((int)labelsOffset + 2 * i), model.PaletteLabels[i]);
        }
        if (model.PaletteEntryLabels is not null)
        {
            for (int i = 0; i < model.PaletteEntryLabels.Count; i++)
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                    output.AsSpan((int)entryLabelsOffset + 2 * i), model.PaletteEntryLabels[i]);
        }
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The canonical layout's single forward plan (card T6-01 §4, the
    /// OQ-8 majority order): header(12) → colorRecordIndices → [v1: the three
    /// Offset32 slots, physically after the indices per spec L68-82] →
    /// colorRecords → the three v1 arrays tail-placed in spec field order, each
    /// emitted exactly when supplied. Every offset is a body position and zero
    /// stays "no array provided" (spec L80-82); on v0 the arrays are structurally
    /// absent, so their offsets stay zero.</summary>
    private static void PlanCanonicalLayout(ushort version, int palettes, int entries, int records,
        bool types, bool labels, bool entryLabels, out uint recordsOffset, out uint typesOffset,
        out uint labelsOffset, out uint entryLabelsOffset, out long length)
    {
        length = CpalLayout.V0HeaderLength + 2L * palettes + 4L * records;
        recordsOffset = (uint)(CpalLayout.IndicesAt + 2L * palettes);
        typesOffset = labelsOffset = entryLabelsOffset = 0;
        if (version == CpalLayout.Version1)
        {
            length += CpalLayout.V1TailLength;
            recordsOffset += CpalLayout.V1TailLength;
            uint cursor = recordsOffset + (uint)(4L * records);
            if (types)
            {
                typesOffset = cursor;
                length += 4L * palettes;
                cursor += (uint)(4L * palettes);
            }
            if (labels)
            {
                labelsOffset = cursor;
                length += 2L * palettes;
                cursor += (uint)(2L * palettes);
            }
            if (entryLabels)
            {
                entryLabelsOffset = cursor;
                length += 2L * entries;
            }
        }
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted cpal write failed.");
    }
}
