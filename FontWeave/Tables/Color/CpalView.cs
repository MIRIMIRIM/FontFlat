using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Strict whole-table validation and materialization for CPAL v0/v1
/// (card T6-01 §3). ValidateTable takes no external input: CPAL has no glyph
/// domain and no cross-table structural dependency (the COLR⟹CPAL container
/// requirement is the joint validator's business, OQ-T6-01-4). The three-zero
/// minimum shape, the L45 upper bound and the reserved-flag hard refusal are the
/// card's hard faces; shared/overlapping palettes and index ordering are
/// observations projected verbatim. Error priority follows the T4-01 line:
/// version, presence, bounds, relations, value domains.</summary>
public readonly ref partial struct CpalView
{
    public ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumPaletteEntries(out ushort numEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumPalettes(out ushort numPalettes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumColorRecords(out ushort numRecords, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // "Palettes must have a minimum of one color record" / "An empty CPAL
        // table ... is not permitted" (spec L43): all three counts refuse at zero
        // (OQ-T6-01-2's entries wing - entries==0 degenerates the L45 bound into
        // an any-index zero-width window).
        if (numEntries == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 2), out diagnostic, numEntries, 1UL);
        if (numPalettes == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 4), out diagnostic, numPalettes, 1UL);
        if (numRecords == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 6), out diagnostic, numRecords, 1UL);
        if (!budget.Work((long)_data.Length + 4L * (long)(numPalettes + numRecords),
                Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // The index array window: the generated extent require owns the truncation
        // provenance (first incomplete row, the T4-10 family line).
        status = TryRequireColorRecordIndicesExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetColorRecordsArrayOffset(out uint recordsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The BGRA records window: an offset outside the source refuses as a
        // range face, a short window as a truncation with the available/required
        // accounting (spec L92's contiguous array).
        if (recordsOffset > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, 8), out diagnostic,
                recordsOffset, (ulong)_data.Length);
        if ((ulong)recordsOffset + 4L * (ulong)numRecords > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Offset, 8), out diagnostic,
                (ulong)_data.Length - recordsOffset, 4UL * (ulong)numRecords);
        // The three v1 trailing arrays: zero is "no array provided" (spec L80-82)
        // on v1 and the accessors read zero on v0, so one uniform walk carries
        // both versions; a non-zero offset must hold its full body in the window.
        status = TryGetPaletteTypesArrayOffset(out uint typesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryEvaluateScalarPaletteTypesArrayOffsetPosition(out ulong typesFieldAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RequireArrayWindow(typesOffset, typesFieldAt, 4L * (ulong)numPalettes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetPaletteLabelsArrayOffset(out uint labelsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryEvaluateScalarPaletteLabelsArrayOffsetPosition(out ulong labelsFieldAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RequireArrayWindow(labelsOffset, labelsFieldAt, 2L * (ulong)numPalettes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetPaletteEntryLabelsArrayOffset(out uint entryLabelsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryEvaluateScalarPaletteEntryLabelsArrayOffsetPosition(out ulong entryLabelsFieldAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RequireArrayWindow(entryLabelsOffset, entryLabelsFieldAt, 2L * (ulong)numEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // L45 (spec L45 "must"): numColorRecords >= max(colorRecordIndices) +
        // numPaletteEntries, in wide arithmetic (max+entries <= 0x1FFFE, the
        // 65535-1 index bound plus the u16 entry count).
        uint maxIndex = 0;
        for (int i = 0; i < numPalettes; i++)
        {
            status = TryGetColorRecordIndices(i, out var index, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (index.Value > maxIndex) maxIndex = index.Value;
        }
        if ((uint)maxIndex + (uint)numEntries > (uint)numRecords)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, CpalLayout.IndicesAt), out diagnostic,
                (uint)maxIndex + (uint)numEntries, (ulong)numRecords);
        // The paletteTypes reserved mask is a hard face (OQ-T6-01-5, the
        // MVAR/MERG reserved-word line): "Reserved; set to 0" (spec L125) refuses
        // with the flag word it arrived in.
        if (typesOffset != 0)
        {
            for (int i = 0; i < numPalettes; i++)
            {
                status = TryGetPaletteType(i, out uint flags, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if ((flags & CpalLayout.PaletteReservedMask) != 0)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Flags, typesOffset + 4UL * (ulong)i, i),
                        out diagnostic, flags, 0UL);
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One trailing array's window: an offset outside the source is a
    /// range face at the offset field's own wire position; a non-zero offset with
    /// a short body is a truncation carrying the available/required accounting.</summary>
    private ReadStatus RequireArrayWindow(uint offset, ulong fieldPosition, ulong requiredBytes,
        out FontDiagnostic diagnostic)
    {
        if (offset > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, fieldPosition), out diagnostic,
                offset, (ulong)_data.Length);
        if (offset != 0 && (ulong)offset + (ulong)requiredBytes > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Offset, fieldPosition), out diagnostic,
                (ulong)_data.Length - offset, (ulong)requiredBytes);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads the complete declared slice into an owned
    /// <see cref="CpalTable"/>; the BGRA rows travel as verbatim byte quads.</summary>
    public ReadStatus TryReadTable(out CpalTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(out CpalTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetVersion(out ushort version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumPaletteEntries(out ushort numEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumPalettes(out ushort numPalettes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumColorRecords(out ushort numRecords, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetColorRecordsArrayOffset(out uint recordsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetPaletteTypesArrayOffset(out uint typesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetPaletteLabelsArrayOffset(out uint labelsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetPaletteEntryLabelsArrayOffset(out uint entryLabelsOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long buffer = 2L * (long)numPalettes + 4L * (long)numRecords;
        if (numPalettes > 0)
        {
            if (typesOffset != 0) buffer += 4L * (long)numPalettes;
            if (labelsOffset != 0) buffer += 2L * (long)numPalettes;
        }
        if (entryLabelsOffset != 0) buffer += 2L * (long)numEntries;
        if (!budget.Buffer(buffer, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var indices = new ushort[numPalettes];
        for (int i = 0; i < indices.Length; i++)
        {
            status = TryGetColorRecordIndices(i, out var index, out diagnostic);
            if (status != ReadStatus.Done) return status;
            indices[i] = index.Value;
        }
        var records = new byte[numRecords][];
        for (int i = 0; i < records.Length; i++)
        {
            status = TryGetColorRecord(i, out byte blue, out byte green, out byte red, out byte alpha,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            records[i] = [blue, green, red, alpha];
        }
        uint[]? paletteTypes = null;
        ushort[]? paletteLabels = null;
        ushort[]? paletteEntryLabels = null;
        if (typesOffset != 0)
        {
            paletteTypes = new uint[numPalettes];
            for (int i = 0; i < paletteTypes.Length; i++)
            {
                status = TryGetPaletteType(i, out uint flags, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                paletteTypes[i] = flags;
            }
        }
        if (labelsOffset != 0)
        {
            paletteLabels = new ushort[numPalettes];
            for (int i = 0; i < paletteLabels.Length; i++)
            {
                status = TryGetPaletteLabel(i, out ushort nameId, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                paletteLabels[i] = nameId;
            }
        }
        if (entryLabelsOffset != 0)
        {
            paletteEntryLabels = new ushort[numEntries];
            for (int i = 0; i < paletteEntryLabels.Length; i++)
            {
                status = TryGetPaletteEntryLabel(i, out ushort nameId, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                paletteEntryLabels[i] = nameId;
            }
        }
        long consumed = CpalLayout.IndicesAt + 2L * (long)numPalettes;
        if (version == CpalLayout.Version1) consumed += CpalLayout.V1TailLength;
        consumed = Math.Max(consumed, (long)recordsOffset + 4L * (long)numRecords);
        if (typesOffset != 0) consumed = Math.Max(consumed, (long)typesOffset + 4L * (long)numPalettes);
        if (labelsOffset != 0) consumed = Math.Max(consumed, (long)labelsOffset + 2L * (long)numPalettes);
        if (entryLabelsOffset != 0)
            consumed = Math.Max(consumed, (long)entryLabelsOffset + 2L * (long)numEntries);
        model = new(version, numEntries, indices, records, paletteTypes, paletteLabels,
            paletteEntryLabels, recordsOffset, typesOffset, labelsOffset, entryLabelsOffset,
            Location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One BGRA color record's four bytes in wire order: blue, green,
    /// red, alpha (spec L100-103). The values are non-premultiplied sRGB bytes
    /// preserved verbatim - never interpreted, never reordered to RGBA.</summary>
    public ReadStatus TryGetColorRecord(int recordIndex, out byte blue, out byte green, out byte red,
        out byte alpha, out FontDiagnostic diagnostic)
    {
        blue = green = red = alpha = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumColorRecords(out ushort numRecords, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((uint)recordIndex >= (uint)numRecords)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 6, recordIndex), out diagnostic,
                unchecked((ulong)(long)recordIndex), (ulong)numRecords);
        status = TryGetColorRecordsArrayOffset(out uint offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ulong at = offset + 4UL * (ulong)recordIndex;
        if (!TrySlice(_data, at, 4, out var row))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, at, recordIndex), out diagnostic,
                (ulong)_data.Length, at + 4UL);
        blue = row[0];
        green = row[1];
        red = row[2];
        alpha = row[3];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Projects one palette's color-record slice: the first record
    /// ordinal and the consecutive count (numPaletteEntries). Palettes sharing or
    /// overlapping records project verbatim with no normalization (spec L41, the
    /// six-host corpus observation).</summary>
    public ReadStatus TryGetPaletteRecords(int paletteIndex, out CpalPaletteRecords records,
        out FontDiagnostic diagnostic)
    {
        records = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumPalettes(out ushort numPalettes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((uint)paletteIndex >= (uint)numPalettes)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 4, paletteIndex), out diagnostic,
                unchecked((ulong)(long)paletteIndex), (ulong)numPalettes);
        status = TryGetNumPaletteEntries(out ushort numEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetColorRecordIndices(paletteIndex, out var index, out diagnostic);
        if (status != ReadStatus.Done) return status;
        records = new CpalPaletteRecords(index.Value, numEntries);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One v1 paletteTypes word; absent (present=false) when the array
    /// offset is zero on v1 or on a v0 table (the accessors read zero there).</summary>
    public ReadStatus TryGetPaletteType(int paletteIndex, out uint flags, out bool present,
        out FontDiagnostic diagnostic)
    {
        flags = 0;
        present = false;
        ReadStatus status = RequireTrailingArray(TrailingArray.Types, paletteIndex,
            out uint arrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (arrayOffset == 0) return Done(out diagnostic);
        ulong at = arrayOffset + 4UL * (ulong)paletteIndex;
        if (!TrySlice(_data, at, 4, out var row))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Flags, at, paletteIndex), out diagnostic,
                (ulong)_data.Length, at + 4UL);
        flags = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(row);
        present = true;
        return Done(out diagnostic);
    }

    /// <summary>One v1 paletteLabels nameID; 0xFFFF is "no string" (spec L135),
    /// never dereferenced here. Absent follows the same zero-offset line.</summary>
    public ReadStatus TryGetPaletteLabel(int paletteIndex, out ushort nameId, out bool present,
        out FontDiagnostic diagnostic)
    {
        nameId = 0;
        present = false;
        ReadStatus status = RequireTrailingArray(TrailingArray.Labels, paletteIndex,
            out uint arrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (arrayOffset == 0) return Done(out diagnostic);
        return ReadTrailingU16(arrayOffset + 2UL * (ulong)paletteIndex, paletteIndex,
            out nameId, out present, out diagnostic);
    }

    /// <summary>One v1 paletteEntryLabels nameID over the entry domain.</summary>
    public ReadStatus TryGetPaletteEntryLabel(int entryIndex, out ushort nameId, out bool present,
        out FontDiagnostic diagnostic)
    {
        nameId = 0;
        present = false;
        ReadStatus status = RequireTrailingArray(TrailingArray.EntryLabels, entryIndex,
            out uint arrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (arrayOffset == 0) return Done(out diagnostic);
        return ReadTrailingU16(arrayOffset + 2UL * (ulong)entryIndex, entryIndex,
            out nameId, out present, out diagnostic);
    }

    private enum TrailingArray : byte { Types, Labels, EntryLabels }

    private ReadStatus RequireTrailingArray(TrailingArray array, int ordinal,
        out uint arrayOffset, out FontDiagnostic diagnostic)
    {
        arrayOffset = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ushort count;
        DataField countField;
        byte countAt;
        switch (array)
        {
            case TrailingArray.Types:
                status = TryGetNumPalettes(out count, out diagnostic);
                countField = DataField.Count;
                countAt = 4;
                break;
            case TrailingArray.Labels:
                status = TryGetNumPalettes(out count, out diagnostic);
                countField = DataField.Count;
                countAt = 4;
                break;
            default:
                status = TryGetNumPaletteEntries(out count, out diagnostic);
                countField = DataField.Count;
                countAt = 2;
                break;
        }
        if (status != ReadStatus.Done) return status;
        if ((uint)ordinal >= (uint)count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(countField, countAt, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), (ulong)count);
        status = array switch
        {
            TrailingArray.Types => TryGetPaletteTypesArrayOffset(out arrayOffset, out diagnostic),
            TrailingArray.Labels => TryGetPaletteLabelsArrayOffset(out arrayOffset, out diagnostic),
            _ => TryGetPaletteEntryLabelsArrayOffset(out arrayOffset, out diagnostic),
        };
        return status;
    }

    private ReadStatus ReadTrailingU16(ulong at, int ordinal, out ushort value, out bool present,
        out FontDiagnostic diagnostic)
    {
        value = 0;
        present = false;
        if (!TrySlice(_data, at, 2, out var row))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, at, ordinal), out diagnostic,
                (ulong)_data.Length, at + 2UL);
        value = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(row);
        present = true;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>One palette's projected color-record slice: the first record ordinal
/// and the consecutive count (spec L86's index arithmetic, never normalized).</summary>
public readonly record struct CpalPaletteRecords(int FirstRecordIndex, int RecordCount);
