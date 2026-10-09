using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The whole-table read/validate face of MVAR 1.0. The declared face carries
/// the header scalars and the store reference; this partial owns the header value
/// domains (reserved==0, OQ-T5-04-2; valueRecordSize&gt;0), the bidirectional count/
/// offset coupling (spec mvar.md L60), the valueRecordSize-strided records walk with
/// its strict binary tag order (OQ-T5-04-1/OQ-T5-04-3 lean forms) and the store
/// chain. axisCount is the only explicit input - MVAR has no glyph domain, so this
/// is deliberately the variation line's single-input consumer signature (no
/// numGlyphs parameter exists to mis-borrow; card 3.2). valueTag is an opaque u32
/// key: the 39-tag registry, its case rules and private-tag conventions are never
/// interpreted here (card 11), and a target tag missing from the records is the
/// spec's constant-across-the-variation-space answer, not an error (L79).</summary>
public readonly ref partial struct MvarView
{
    public ReadStatus ValidateTable(int axisCount, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(axisCount, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int axisCount, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (axisCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.None, 0), out diagnostic,
                unchecked((ulong)(long)axisCount), 0);
        // "Not used; set to 0" (spec L57) - the reserved word is a hard face
        // (OQ-T5-04-2, the MERG/gvar reserved-bit precedent).
        status = TryGetReserved(out ushort reserved, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (reserved != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, MvarLayout.ReservedAt),
                out diagnostic, reserved, 0);
        // "must be greater than zero" (spec L58): the stride is legal at any
        // positive value, so only zero refuses and no ==8 constant is invented
        // (OQ-T5-04-1 lean).
        status = TryGetValueRecordSize(out ushort recordSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (recordSize == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, MvarLayout.ValueRecordSizeAt),
                out diagnostic, recordSize, 1);
        status = TryGetValueRecordCount(out ushort recordCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetItemVariationStoreOffset(out ushort storeOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The L60 conditional, both directions: a non-empty record list requires a
        // store target and an empty one requires the zero offset (the corpus is
        // 139/139 coupled; the zero-record host is synthetic-pinned).
        if (recordCount > 0 && storeOffset == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, MvarLayout.VariationStoreAt),
                out diagnostic, storeOffset, 1);
        if (recordCount == 0 && storeOffset != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, MvarLayout.VariationStoreAt),
                out diagnostic, storeOffset, 0);
        if (recordCount > 0)
        {
            // The records are inline from the header end with no offset field of
            // their own; valueRecordSize steps each record start (spec L63).
            long declaredEnd = (long)MvarLayout.RecordsAt + (long)recordCount * recordSize;
            if (!budget.Work(recordCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            if (declaredEnd > _data.Length)
            {
                // The first incomplete record pins the truncation at its own tag
                // slot (the T4-10 row-granular convention, the line's adjudicated
                // face - not the declaring stride field); the need is the first
                // 8 record bytes at any positive stride (OQ-T5-04-1 lean).
                int incomplete = (int)((_data.Length - MvarLayout.RecordsAt) / recordSize);
                if (incomplete >= recordCount) incomplete = recordCount - 1;
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Tag,
                        (ulong)(MvarLayout.RecordsAt + incomplete * recordSize), incomplete),
                    out diagnostic, (ulong)_data.Length,
                    (ulong)(MvarLayout.RecordsAt + incomplete * recordSize) + 8UL);
            }
            // "records must be given in binary order of the valueTag values" (spec
            // L61/L75): strict increase, duplicates included (OQ-T5-04-3 - the L79
            // scan-to-find semantics are undefined under duplicates and no
            // disambiguation rule is invented). The refusal names the offending
            // tag against the PREVIOUS tag (the T4-01/T4-11 order-face convention).
            uint previous = ReadWire.U32(_data, MvarLayout.RecordsAt);
            for (int ordinal = 1; ordinal < recordCount; ordinal++)
            {
                uint tag = ReadWire.U32(_data, MvarLayout.RecordsAt + ordinal * recordSize);
                if (tag <= previous)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Tag,
                            (ulong)(MvarLayout.RecordsAt + ordinal * recordSize), ordinal),
                        out diagnostic, tag, previous);
                previous = tag;
            }
        }
        status = TryOpenItemVariationStore(out var storeView, out bool storePresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (recordCount > 0 && !storePresent)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, MvarLayout.VariationStoreAt),
                out diagnostic, 0ul, (ulong)_data.Length);
        if (storePresent)
        {
            status = storeView.Validate(axisCount, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads the complete table into an owned <see cref="MvarTable"/>: the
    /// records in wire order (each stepped by the table's own valueRecordSize) and
    /// the store materialized when present. The declared stride value travels with
    /// the model (card 3.2).</summary>
    public ReadStatus TryReadTable(int axisCount, out MvarTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(axisCount, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int axisCount, out MvarTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(axisCount, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetValueRecordSize(out ushort recordSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetValueRecordCount(out ushort recordCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        VariationStoreTable? store = null;
        if (recordCount > 0)
        {
            status = TryOpenItemVariationStore(out var storeView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = storeView.TryRead(axisCount, out store, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        var records = new MvarValueRecord[recordCount];
        if (recordCount > 0)
        {
            if (!budget.Buffer(8L * recordCount, Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            for (int ordinal = 0; ordinal < recordCount; ordinal++)
            {
                int at = MvarLayout.RecordsAt + ordinal * recordSize;
                records[ordinal] = new(ReadWire.U32(_data, at), ReadWire.U16(_data, at + 4),
                    ReadWire.U16(_data, at + 6));
            }
        }
        int consumed = MvarLayout.HeaderLength;
        if (recordCount > 0)
        {
            consumed = Math.Max(consumed, checked(MvarLayout.RecordsAt + recordCount * recordSize));
            consumed = Math.Max(consumed,
                checked((int)ItemVariationStoreOffset + store!.ConsumedLength));
        }
        model = new(store, records, recordSize, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
