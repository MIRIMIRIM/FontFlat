using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables.Variation;

/// <summary>The MVAR root writer: one planned, atomically published whole-table
/// emission. The canonical layout is header(12) - records (inline, stepped by the
/// model's preserved valueRecordSize) - ItemVariationStore, the shape every corpus
/// host already carries, which makes the unedited round-trips byte-identity
/// candidates (OQ-T5-04-8). Unlike the Offset32 variation roots the store slot is
/// an Offset16, so a records block whose end would pass 65535 refuses at plan time
/// with the declared Detail/Expected and no second layout candidate - no
/// reordering can dissolve the 64K bound, and the refusal is the only honest shape
/// (OQ-T5-04-7; the native slot helper carries the same bound at write time).
/// Record slots beyond the 8-byte tag/outer/inner payload stay zero; sub-8 strides
/// overlay slots in record order (deterministic, and the reader's stride walk
/// round-trips the same records either way).</summary>
public static class MvarTableCompiler
{
    /// <summary>Explicit root-model factory: the store and the records are
    /// caller-supplied and never inferred. The L60 count/offset coupling becomes the
    /// owned-model invariant (records without a store, or a store without records,
    /// refuse); axisCount must equal the store's declared count; the records must
    /// arrive in strict binary tag order (OQ-T5-04-3). The declared valueRecordSize
    /// is the caller's and stays verbatim - zero refuses, no ==8 constant is
    /// invented (OQ-T5-04-1 lean).</summary>
    public static ReadStatus TryCreate(VariationStoreTable? store,
        IReadOnlyList<MvarValueRecord> records, ushort valueRecordSize, int axisCount,
        out MvarTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(records);
        var budget = new BuildBudget(limits);
        if (valueRecordSize == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Length, MvarLayout.ValueRecordSizeAt),
                out diagnostic, valueRecordSize, 1);
        if (records.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MvarLayout.ValueRecordCountAt),
                out diagnostic, (ulong)records.Count, (ulong)ushort.MaxValue);
        // The L60 conditional, both directions, at the owned-model boundary.
        if (records.Count > 0 && store is null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Offset, MvarLayout.VariationStoreAt),
                out diagnostic, 0, 1);
        if (records.Count == 0 && store is not null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Offset, MvarLayout.VariationStoreAt),
                out diagnostic, 1, 0);
        if (store is not null)
        {
            if (store.AxisCount != axisCount)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, VariationRegionLayout.AxisCountAt),
                    out diagnostic, (ulong)store.AxisCount, (ulong)axisCount);
            if (!SameSource(location, store))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    location.AtLocal(DataField.None, 0), out diagnostic);
        }
        for (int ordinal = 1; ordinal < records.Count; ordinal++)
        {
            if (records[ordinal].ValueTag <= records[ordinal - 1].ValueTag)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Tag, (ulong)(MvarLayout.RecordsAt +
                        ordinal * valueRecordSize), ordinal),
                    out diagnostic, records[ordinal].ValueTag,
                    records[ordinal - 1].ValueTag + 1UL);
        }
        if (!budget.Work(records.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(store, records.ToArray(), valueRecordSize, location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation location, object? model)
    {
        DataLocation child = model switch
        {
            VariationStoreTable inner => inner.Location,
            _ => location,
        };
        return location.SourceId == child.SourceId && location.FaceIndex == child.FaceIndex &&
            (location.SourceId == 0 || location.TableTag == child.TableTag);
    }

    public static ReadStatus TryCompile(MvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(MvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MvarTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        // Every plan admission, including the emission work, completes before the
        // destination capacity check (spec 7.3; the destination is untouched on refusal).
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Wire length of the canonical emission; budget-free arithmetic for
    /// callers that only measure.</summary>
    internal static int Measure(MvarTable model)
    {
        int length = MvarLayout.RecordsAt + checked(model.RecordCount * model.ValueRecordSize);
        if (model.Store is not null)
            length += VariationStoreTableCompiler.Measure(model.Store);
        return length;
    }

    // The single canonical candidate: every child size, every fixup value, the 64K
    // store-slot bound and the whole budget admission happen here, before any byte
    // exists.
    private static ReadStatus TryPlan(MvarTable model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = 0;
        int measured = Measure(model);
        long emission = measured + (model.Store?.DataCount ?? 0);
        if (!budget.Work(emission, model.Location, out diagnostic) ||
            !budget.Output(measured, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // The Offset16 store slot: a records block ending past 65535 cannot address
        // the canonical store - refused here so a refused plan never reaches the
        // destination (OQ-T5-04-7).
        int recordsEnd = MvarLayout.RecordsAt + model.RecordCount * model.ValueRecordSize;
        if (model.Store is not null && recordsEnd > 0xFFFF)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, MvarLayout.VariationStoreAt),
                out diagnostic, (ulong)recordsEnd, 0xFFFF);
        length = measured;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only a fully preflighted plan and destination reach this path.
    private static void WritePlanned(MvarTable model, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(MvarView.TryWriteVersion(destination, 0x00010000,
            out _, location));
        CoverageTableCompiler.RequireWritten(MvarView.TryWriteReserved(destination, 0,
            out _, location));
        CoverageTableCompiler.RequireWritten(MvarView.TryWriteValueRecordSize(destination,
            model.ValueRecordSize, out _, location));
        CoverageTableCompiler.RequireWritten(MvarView.TryWriteValueRecordCount(destination,
            (ushort)model.RecordCount, out _, location));
        int recordsEnd = MvarLayout.RecordsAt + model.RecordCount * model.ValueRecordSize;
        WriteStoreSlot(destination, location, model.Store is not null, recordsEnd);
        // The records are inline with no offset field of their own; each 8-byte
        // payload lands at its declared stride slot (the zeroed buffer carries any
        // gap, and sub-8 strides overlay in record order).
        for (int ordinal = 0; ordinal < model.RecordCount; ordinal++)
        {
            var record = model.Records[ordinal];
            int at = MvarLayout.RecordsAt + ordinal * model.ValueRecordSize;
            global::System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                destination.Slice(at), record.ValueTag);
            global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                destination.Slice(at + 4), record.Outer);
            global::System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                destination.Slice(at + 6), record.Inner);
        }
        if (model.Store is not null)
            VariationStoreTableCompiler.WritePlanned(model.Store,
                destination.Slice(recordsEnd));
    }

    // The store slot: present tracks the L60 coupling; the 64K bound was preflighted
    // in TryPlan, so a refusal here is an invariant breach (GDEF root precedent).
    private static void WriteStoreSlot(Span<byte> destination, DataLocation location,
        bool present, int target)
    {
        ReadStatus status = MvarView.TryGetItemVariationStoreOffsetValue(present, 0UL,
            (ulong)target, out ushort encoded, out _, location);
        if (status == ReadStatus.Done)
            status = MvarView.TryWriteItemVariationStoreOffset(destination, encoded,
                out _, location);
        CoverageTableCompiler.RequireWritten(status);
    }
}
