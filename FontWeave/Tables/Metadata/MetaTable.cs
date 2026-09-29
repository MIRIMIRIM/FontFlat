using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>An owned opaque metadata payload; interpretation is tag-specific.</summary>
public readonly struct MetaRecord
{
    private readonly byte[] _payload;
    internal MetaRecord(uint tag, byte[] payload) { Tag = tag; _payload = payload; }
    public uint Tag { get; }
    public ReadOnlySpan<byte> Payload => _payload;
}

/// <summary>Validated version 1 metadata with unique tags in original directory order.
/// The writable header is canonical; SourceReserved retains any compatibility-read value.</summary>
public sealed class MetaTable
{
    private readonly MetaRecord[] _records;
    internal MetaTable(MetaRecord[] records, DataLocation location, uint sourceReserved)
    { _records = records; Location = location; SourceReserved = sourceReserved; }
    public uint Version => 1;
    public uint Flags => 0;
    public uint Reserved => 0;
    /// <summary>Original input header value at byte 8, retained across payload edits for provenance.
    /// It is not a payload offset and is never emitted by the compiler.</summary>
    public uint SourceReserved { get; }
    public uint DataMapsCount => (uint)_records.Length;
    public ReadOnlySpan<MetaRecord> Records => _records;
    public DataLocation Location { get; }
}

/// <summary>Replace one existing tag's opaque bytes; no tag-specific semantic claim.</summary>
public readonly record struct MetaPayloadEdit(uint Tag, ReadOnlyMemory<byte> Payload);

public sealed record MetaEditPlan
{
    public IReadOnlyList<MetaPayloadEdit> Entries { get; init; } = [];
}
