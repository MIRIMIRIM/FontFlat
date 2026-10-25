using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct MetaView
{
    /// <summary>Strict version 1, zero flags, unique tags and disjoint payloads.
    /// Payload contents are opaque: this does not validate UTF-8 or language-tag semantics.
    /// The byte-8 field is historical: the ecosystem's shipped fonts (the Windows
    /// core set included) carry non-zero values there and every consumer ignores
    /// it, so both faces tolerate it and the model records the source value.</summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        Validate(out diagnostic, new BuildBudget(limits));

    private ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (Flags != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Flags, 4UL), out diagnostic, Flags);
        status = TryGetDirectoryEnd(out ulong directoryEnd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (directoryEnd > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, 12), out diagnostic, (ulong)_data.Length, directoryEnd);
        // The on-wire uint32 count is narrowed only after the complete directory fits.
        int count = (int)DataMapsCount;
        if (!budget.Work(64L * count + MetaLayout.HeaderLength, Location, out diagnostic) ||
            !budget.Buffer(16L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var ranges = new MetadataPayloadRange[count];
        var tags = new uint[count];
        for (int i = 0; i < count; i++)
        {
            _ = TryGetDataMap(i, out var record, out _);
            status = TryGetDataMapPayload(i, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            tags[i] = record.Tag;
            ranges[i] = new(record.DataOffset, record.DataLength, i);
        }
        Array.Sort(tags);
        for (int i = 1; i < tags.Length; i++)
            if (tags[i] == tags[i - 1])
            {
                // Locate the second original occurrence for a source-relative diagnostic.
                bool seen = false;
                for (int ordinal = 0; ordinal < count; ordinal++)
                {
                    _ = TryGetDataMap(ordinal, out var record, out _);
                    if (record.Tag != tags[i]) continue;
                    if (seen)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                            Location.At(DataField.Tag, MetaLayout.HeaderLength + (ulong)ordinal * MetaLayout.RecordStride, ordinal),
                            out diagnostic, record.Tag);
                    seen = true;
                }
            }
        return MetadataPayloadRanges.Validate(ranges, Location, out diagnostic);
    }

    /// <summary>Reads owned metadata. DataMap offsets and all structural checks apply;
    /// the historical byte-8 field is tolerated on both faces. SourceReserved records
    /// the original field value, while the compiler always writes Reserved as zero.</summary>
    public ReadStatus TryRead(out MetaTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = Validate(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        long payloadBytes = 0;
        for (int i = 0; i < (int)DataMapsCount; i++)
        {
            _ = TryGetDataMap(i, out var record, out _);
            payloadBytes += record.DataLength;
        }
        if (!budget.Work(payloadBytes, Location, out diagnostic) ||
            !budget.Buffer(payloadBytes + (IntPtr.Size + 8L) * DataMapsCount,
                Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = new MetaRecord[(int)DataMapsCount];
        for (int i = 0; i < records.Length; i++)
        {
            _ = TryGetDataMap(i, out var record, out _);
            _ = TryGetDataMapPayload(i, out var payload, out _);
            records[i] = new(record.Tag, payload.ToArray());
        }
        model = new(records, Location, Reserved);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
