using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct DsigView
{
    /// <summary>Checks version 1 / block format 1 structure only, never cryptographic validity.
    /// Signature payload access returns the entire block, including its eight-byte header.</summary>
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
        if ((Flags & ~1) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Flags, 6), out diagnostic, Flags, 1);
        status = TryGetDirectoryEnd(out ulong directoryEnd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (directoryEnd > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, 4), out diagnostic, (ulong)_data.Length, directoryEnd);
        int count = NumSignatures;
        if (!budget.Work(32L * count + DsigLayout.HeaderLength, Location, out diagnostic) ||
            !budget.Buffer(12L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var ranges = new MetadataPayloadRange[count];
        for (int i = 0; i < count; i++)
        {
            _ = TryGetSignature(i, out var record, out _);
            ulong row = DsigLayout.HeaderLength + (ulong)i * DsigLayout.RecordStride;
            if (record.Format != 1)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    Location.At(DataField.Version, row, i), out diagnostic, record.Format, 1);
            status = TryGetSignaturePayload(i, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (record.Length < DsigLayout.BlockHeaderLength)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Length, row + 4, i), out diagnostic,
                    record.Length, DsigLayout.BlockHeaderLength);
            status = TryReadSignatureBlockHeaderAt(record.Offset, i, out var block, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (block.Reserved1 != 0 || block.Reserved2 != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.None, (ulong)record.Offset + (block.Reserved1 != 0 ? 0UL : 2UL), i),
                    out diagnostic, block.Reserved1 != 0 ? block.Reserved1 : block.Reserved2);
            if (block.SignatureLength != record.Length - DsigLayout.BlockHeaderLength)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Length, (ulong)record.Offset + 4, i), out diagnostic,
                    block.SignatureLength, record.Length - DsigLayout.BlockHeaderLength);
            ranges[i] = new(record.Offset, record.Length, i);
        }
        return MetadataPayloadRanges.Validate(ranges, Location, out diagnostic);
    }

    public ReadStatus TryRead(out DsigTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = Validate(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(_data.Length, Location, out diagnostic) ||
            !budget.Buffer(_data.Length + (IntPtr.Size + 24L) * NumSignatures,
                Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = _data.ToArray();
        var records = new DsigRecord[NumSignatures];
        for (int i = 0; i < records.Length; i++)
        {
            _ = TryGetSignature(i, out var record, out _);
            records[i] = new(record.Format, record.Length, record.Offset,
                bytes.AsMemory((int)record.Offset + DsigLayout.BlockHeaderLength,
                    (int)record.Length - DsigLayout.BlockHeaderLength));
        }
        model = new(Flags, records, bytes, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
