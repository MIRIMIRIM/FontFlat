using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>One owned SVG document record: the glyph range as written plus the
/// payload index of the document serving it. Records sharing a document (the
/// same (svgDocOffset, svgDocLength) wire identity, V L86) carry the same
/// index, so a payload replacement follows the whole shared group by the index
/// arithmetic with no special case.</summary>
public readonly record struct SvgDocumentRecord(ushort StartGlyphID, ushort EndGlyphID,
    int PayloadIndex);

/// <summary>Owned SVG table (card T6-04 §2): the version/reserved receipts, the
/// document records in wire order, and each distinct document's encoded bytes
/// exactly once (gzip members verbatim - never decompressed, never recompressed,
/// OQ-T6-04-7). No XML parsing, no CSS var() evaluation, no rendering - those
/// are consumer domains (card T6-04 §11). Produced by SvgView.TryReadTable and
/// edited through the value-level API; structural changes (record/payload
/// count changes, reordering, version conversion) have no edit surface
/// (OQ-T6-04-14); consumers re-root through SvgTableCompiler.TryCreate.</summary>
public sealed class SvgTable
{
    private readonly SvgDocumentRecord[] _records;
    private readonly byte[][] _payloads;

    internal SvgTable(ushort version, uint reserved, uint documentListOffset,
        SvgDocumentRecord[] records, byte[][] payloads, DataLocation location,
        int consumedLength)
    {
        Version = version;
        Reserved = reserved;
        DocumentListOffset = documentListOffset;
        _records = records;
        _payloads = payloads;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The wire version: 0, the declared supported domain.</summary>
    public ushort Version { get; }

    /// <summary>The reserved word's receipt (read models carry the wire value,
    /// which validated to zero; factory models carry the canonical zero).</summary>
    public uint Reserved { get; }

    /// <summary>The document list's start in the coordinates the model was read
    /// from (the wire offset, or the canonical plan's 10 for factory models) -
    /// the record-row diagnostics' coordinate.</summary>
    public uint DocumentListOffset { get; }

    public int RecordCount => _records.Length;

    /// <summary>The distinct documents' encoded bytes in first-occurrence
    /// record order; unedited documents round-trip byte-for-byte (the identity
    /// hard gate's basis).</summary>
    public IReadOnlyList<byte[]> Documents => Array.AsReadOnly(_payloads);

    public int DocumentCount => _payloads.Length;

    /// <summary>The document records in wire order.</summary>
    public IReadOnlyList<SvgDocumentRecord> Records => Array.AsReadOnly(_records);

    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>One record's owned projection.</summary>
    public ReadStatus TryGetRecord(int recordIndex, out SvgDocumentRecord record,
        out FontDiagnostic diagnostic)
    {
        record = default;
        if ((uint)recordIndex >= (uint)_records.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, DocumentListOffset, recordIndex),
                out diagnostic, unchecked((ulong)(long)recordIndex), (ulong)_records.Length);
        record = _records[recordIndex];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One distinct document's encoded bytes.</summary>
    public ReadStatus TryGetDocumentPayload(int payloadIndex, out ReadOnlySpan<byte> payload,
        out FontDiagnostic diagnostic)
    {
        payload = default;
        if ((uint)payloadIndex >= (uint)_payloads.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Length, DocumentListOffset, payloadIndex),
                out diagnostic, unchecked((ulong)(long)payloadIndex), (ulong)_payloads.Length);
        payload = _payloads[payloadIndex];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one distinct document's encoded bytes (the
    /// card's edit witness). Every record sharing the payload index follows by
    /// the spec's sharing semantics - the index arithmetic is the sharing, no
    /// special case exists (V L86). The encoded length must stay non-zero
    /// (V L82's must-non-zero, re-answered on the edit); gzip members travel
    /// verbatim. Structural changes stay outside the surface (OQ-T6-04-14).</summary>
    public ReadStatus TryWithDocumentPayload(int payloadIndex, ReadOnlySpan<byte> payload,
        out SvgTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if ((uint)payloadIndex >= (uint)_payloads.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Length, DocumentListOffset, payloadIndex),
                out diagnostic, unchecked((ulong)(long)payloadIndex), (ulong)_payloads.Length);
        if (payload.Length == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, DocumentListOffset, payloadIndex),
                out diagnostic, 0UL, 1UL);
        var payloads = (byte[][])_payloads.Clone();
        payloads[payloadIndex] = payload.ToArray();
        updated = new(Version, Reserved, DocumentListOffset, _records, payloads,
            Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically rewrites one record's glyph range; the ordering and
    /// range clauses re-answer in wide record order (V L84): start &lt;= end, the
    /// range strictly after the previous record's end and strictly before the
    /// next record's start. Refusals carry the read-side faces (OQ-T6-04-4).</summary>
    public ReadStatus TryWithRange(int recordIndex, ushort startGlyphID, ushort endGlyphID,
        out SvgTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if ((uint)recordIndex >= (uint)_records.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, DocumentListOffset, recordIndex),
                out diagnostic, unchecked((ulong)(long)recordIndex), (ulong)_records.Length);
        ulong at = DocumentListOffset + 2UL + (ulong)recordIndex * SvgLayout.RecordStride;
        if (startGlyphID > endGlyphID)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.GlyphId, at, recordIndex), out diagnostic,
                startGlyphID, endGlyphID);
        if (recordIndex > 0)
        {
            ushort previousEnd = _records[recordIndex - 1].EndGlyphID;
            if (startGlyphID <= previousEnd)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.GlyphId, at, recordIndex), out diagnostic,
                    startGlyphID, previousEnd);
        }
        if (recordIndex + 1 < _records.Length)
        {
            ushort nextStart = _records[recordIndex + 1].StartGlyphID;
            if (nextStart <= endGlyphID)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.GlyphId,
                        DocumentListOffset + 2UL + (ulong)(recordIndex + 1) * SvgLayout.RecordStride,
                        recordIndex + 1), out diagnostic, nextStart, endGlyphID);
        }
        var records = (SvgDocumentRecord[])_records.Clone();
        records[recordIndex] = records[recordIndex] with
        {
            StartGlyphID = startGlyphID,
            EndGlyphID = endGlyphID,
        };
        updated = new(Version, Reserved, DocumentListOffset, records, _payloads,
            Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
