using FontWeave.Core;

namespace FontWeave.Tables;

public readonly struct DsigRecord
{
    private readonly ReadOnlyMemory<byte> _signature;
    internal DsigRecord(uint format, uint length, uint offset, ReadOnlyMemory<byte> signature)
    { Format = format; Length = length; Offset = offset; _signature = signature; }
    public uint Format { get; }
    /// <summary>Entire block length, including its eight-byte header.</summary>
    public uint Length { get; }
    public uint Offset { get; }
    /// <summary>Opaque packet only; no PKCS#7 parsing or signature verification is performed.</summary>
    public ReadOnlySpan<byte> Signature => _signature.Span;
}

/// <summary>Owned DSIG structure. Original block placement and unreferenced bytes are retained.
/// This is not a certificate, trust assertion, or evidence that the font is unchanged.</summary>
public sealed class DsigTable
{
    private readonly DsigRecord[] _records;
    private readonly byte[] _originalBytes;
    internal DsigTable(ushort flags, DsigRecord[] records, byte[] originalBytes, DataLocation location)
    { Flags = flags; _records = records; _originalBytes = originalBytes; Location = location; }
    public uint Version => 1;
    public ushort Flags { get; }
    public ushort NumSignatures => (ushort)_records.Length;
    public ReadOnlySpan<DsigRecord> Records => _records;
    public DataLocation Location { get; }
    internal ReadOnlySpan<byte> OriginalBytes => _originalBytes;

    /// <summary>Copy into complete editable structural content under explicit resource limits.
    /// Packets and source-order gaps/tail are independently owned. This does not validate,
    /// generate or preserve the cryptographic validity of any signature.</summary>
    public ReadStatus TryToContent(out DsigTableContent? content, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        content = null;
        var budget = new BuildBudget(limits);
        int count = _records.Length;
        int directoryEnd = DsigLayout.HeaderLength + count * DsigLayout.RecordStride;
        long copiedBytes = _originalBytes.Length - directoryEnd - count * (long)DsigLayout.BlockHeaderLength;
        if (!budget.Input(_originalBytes.Length, Location, out diagnostic) ||
            !budget.Work(32L * count + 2L * _originalBytes.Length, Location, out diagnostic) ||
            !budget.Buffer(copiedBytes + (IntPtr.Size + 12L) * count, Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        var packets = new ReadOnlyMemory<byte>[count];
        var order = new int[count];
        int opaqueLength = _originalBytes.Length - directoryEnd;
        for (int i = 0; i < count; i++)
        {
            packets[i] = _records[i].Signature.ToArray();
            order[i] = i;
            opaqueLength -= (int)_records[i].Length;
        }
        Array.Sort(order, (left, right) => _records[left].Offset.CompareTo(_records[right].Offset));
        var opaque = new byte[opaqueLength];
        int cursor = directoryEnd, written = 0;
        foreach (int i in order)
        {
            int start = (int)_records[i].Offset;
            int length = start - cursor;
            _originalBytes.AsSpan(cursor, length).CopyTo(opaque.AsSpan(written));
            written += length;
            cursor = start + (int)_records[i].Length;
        }
        _originalBytes.AsSpan(cursor).CopyTo(opaque.AsSpan(written));
        content = new() { Flags = Flags, Signatures = packets, UnreferencedBytes = opaque };
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>Complete version 1 / format 1 structural content, not a signing operation.
/// Caller-supplied packets are opaque; replacing them or flags makes no validity claim.</summary>
public sealed record DsigTableContent
{
    /// <summary>Only bit 0 (cannot be resigned) is defined.</summary>
    public ushort Flags { get; init; }
    /// <summary>Packets in desired directory order, without their eight-byte block headers.
    /// Insert, remove, reorder or replace entries explicitly. Empty packets are structurally
    /// representable, not evidence of a valid signature.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Signatures { get; init; } = Array.Empty<ReadOnlyMemory<byte>>();
    /// <summary>Opaque gaps and tail retained in source order; emitted after all blocks.
    /// Clear this explicitly to discard unreferenced source bytes.</summary>
    public ReadOnlyMemory<byte> UnreferencedBytes { get; init; }
}
