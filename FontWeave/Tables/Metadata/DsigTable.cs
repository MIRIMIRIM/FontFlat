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
}
