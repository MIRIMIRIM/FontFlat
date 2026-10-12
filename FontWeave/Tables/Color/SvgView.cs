using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Strict whole-table validation and materialization for SVG v0
/// (card T6-04 §3). ValidateTable takes no external input: the spec has no
/// maxp/glyph-count coupling text (OQ-T6-04-6's lean - the V L50 outline
/// correspondence is the joint validator's container fact), so the glyph ranges
/// answer no numGlyphs domain. The four must-non-zero gates, the three range
/// clauses, the list-relative document windows and the reserved word are the
/// hard faces; payload overlap, gzip vs plain encodings (the 3-byte magic
/// observation, OQ-T6-04-7 - never decompression), mixed encodings, docList
/// placement and inter-region padding are observations. Error priority follows
/// the T4-01 line and the frozen walk order: version, reserved, presence,
/// bounds, per-record presence, range legality, overlap, document windows.</summary>
public readonly ref partial struct SvgView
{
    public ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        // The generated version gate: SupportedVersions={0}, the OQ-T6-04-1
        // face ("starting at 0. Set to 0") - detail carries the wire version.
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // "reserved: Set to 0" (V L62): the OQ-T6-04-2 hard face, the
        // MVAR/MERG reserved-word family, in the word it arrived in.
        status = TryGetReserved(out uint reserved, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (reserved != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, SvgLayout.HeaderReservedAt), out diagnostic,
                reserved, 0UL);
        status = TryGetDocumentListOffset(out uint listOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // "svgDocumentListOffset: Must be non-zero" (V L61, OQ-T6-04-3).
        if (listOffset == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, SvgLayout.HeaderListOffsetAt), out diagnostic,
                0UL, 1UL);
        // The list anchor's own bounds: at/after the window end is a range face
        // at the offset field; a count word short is a truncation with the
        // available/required accounting.
        if (listOffset >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, SvgLayout.HeaderListOffsetAt), out diagnostic,
                listOffset, (ulong)_data.Length);
        if (listOffset + 2UL > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, listOffset), out diagnostic,
                (ulong)_data.Length - listOffset, 2UL);
        status = TryGetNumEntries(out ushort numEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // "numEntries: Must be non-zero" (V L70, OQ-T6-04-3).
        if (numEntries == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, listOffset), out diagnostic, 0UL, 1UL);
        if (!budget.Work((long)_data.Length + 16L * (long)numEntries, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        // The records array window: the first incomplete record field refuses at
        // its own slot with the window length and the field end (the T4-10
        // family convention, the generated extent's provenance shape).
        status = RequireRecordsWindow(listOffset, numEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Per-record presence gates in record order (V L81/L82, OQ-T6-04-3):
        // every record's offset gate runs before the length pass and both run
        // before the range clauses - the frozen walk's phase order.
        for (int i = 0; i < numEntries; i++)
        {
            ulong at = RecordAt(listOffset, i);
            status = TryGetDocumentRecord(i, out ushort start, out ushort end,
                out uint documentOffset, out uint documentLength, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (documentOffset == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, at + 4, i), out diagnostic, 0UL, 1UL);
            if (documentLength == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Length, at + 8, i), out diagnostic, 0UL, 1UL);
        }
        // "startGlyphID must be less than or equal to the endGlyphID of that
        // record" (V L84 middle clause).
        for (int i = 0; i < numEntries; i++)
        {
            status = TryGetDocumentRecord(i, out ushort start, out ushort end, out _, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (start > end)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.GlyphId, RecordAt(listOffset, i), i), out diagnostic,
                    start, end);
        }
        // "startGlyphID must be greater than the endGlyphID of any previous
        // record" (V L84 last clause): the overlap hard refusal; strict ascent
        // is implied (an equal start is already an overlap), one check with no
        // separate sortedness gate (OQ-T6-04-4).
        ushort previousEnd = 0;
        for (int i = 0; i < numEntries; i++)
        {
            status = TryGetDocumentRecord(i, out ushort start, out ushort end, out _, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (i > 0 && start <= previousEnd)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.GlyphId, RecordAt(listOffset, i), i), out diagnostic,
                    start, previousEnd);
            previousEnd = end;
        }
        // The document windows: base = the SVGDocumentList start, NOT the table
        // start (OQ-T6-04-5's pinned read face); u64 intermediates so the u32
        // offset+length sum cannot wrap. A document starting at/after the window
        // end is a range face pinned at the record's offset field; a short body
        // is a truncation with the available/required accounting (T0's payload
        // truncation slice).
        for (int i = 0; i < numEntries; i++)
        {
            status = TryGetDocumentRecord(i, out _, out _, out uint documentOffset,
                out uint documentLength, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong absoluteStart = (ulong)listOffset + documentOffset;
            ulong at = RecordAt(listOffset, i);
            if (absoluteStart >= (ulong)_data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.AtLocal(DataField.Offset, at + 4, i), out diagnostic,
                    absoluteStart, (ulong)_data.Length);
            if (absoluteStart + documentLength > (ulong)_data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Offset, at + 4, i), out diagnostic,
                    (ulong)_data.Length - absoluteStart, documentLength);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The document records array window: every record's four words
    /// must sit inside the source; the first incomplete field names its own
    /// slot with the window length and the field end.</summary>
    private ReadStatus RequireRecordsWindow(uint listOffset, ushort numEntries,
        out FontDiagnostic diagnostic)
    {
        for (int i = 0; i < numEntries; i++)
        {
            ulong at = RecordAt(listOffset, i);
            for (int word = 0; word < 4; word++)
            {
                ulong width = word < 2 ? 2UL : 4UL;
                ulong wordAt = at + (ulong)(word < 2 ? word * 2 : 4 + (word - 2) * 4);
                DataField field = word switch
                {
                    0 or 1 => DataField.GlyphId,
                    2 => DataField.Offset,
                    _ => DataField.Length,
                };
                if (wordAt + width > (ulong)_data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.AtLocal(field, wordAt, i), out diagnostic,
                        (ulong)_data.Length, wordAt + width);
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ulong RecordAt(uint listOffset, int recordIndex) =>
        (ulong)listOffset + 2UL + (ulong)recordIndex * SvgLayout.RecordStride;

    /// <summary>The document list's entry count, read behind the header offset
    /// (the runtime-positioned face the declaration leaves to the partial).</summary>
    public ReadStatus TryGetNumEntries(out ushort numEntries, out FontDiagnostic diagnostic)
    {
        numEntries = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = TryGetDocumentListOffset(out uint listOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!TrySlice(_data, listOffset, 2, out var row))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, listOffset), out diagnostic,
                (ulong)_data.Length, (ulong)listOffset + 2UL);
        numEntries = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(row);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One document record's wire quad: startGlyphID, endGlyphID and
    /// the document window as written - svgDocOffset stays list-relative
    /// (OQ-T6-04-5), svgDocLength stays the encoded length (OQ-T6-04-7).</summary>
    public ReadStatus TryGetDocumentRecord(int recordIndex, out ushort startGlyphID,
        out ushort endGlyphID, out uint documentOffset, out uint documentLength,
        out FontDiagnostic diagnostic)
    {
        startGlyphID = endGlyphID = 0;
        documentOffset = documentLength = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDocumentListOffset(out uint listOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumEntries(out ushort numEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((uint)recordIndex >= (uint)numEntries)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, listOffset, recordIndex), out diagnostic,
                unchecked((ulong)(long)recordIndex), numEntries);
        ulong at = RecordAt(listOffset, recordIndex);
        if (!TrySlice(_data, at, SvgLayout.RecordStride, out var row))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.GlyphId, at, recordIndex), out diagnostic,
                (ulong)_data.Length, at + SvgLayout.RecordStride);
        startGlyphID = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(row);
        endGlyphID = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(row.Slice(2));
        documentOffset = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(row.Slice(4));
        documentLength = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(row.Slice(8));
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One record's document window translated to absolute table
    /// coordinates (the list-relative offset rebased onto the SVGDocumentList
    /// start, OQ-T6-04-5) with the window check the read chain enforces.</summary>
    public ReadStatus TryGetDocumentSpan(int recordIndex, out ulong start, out ulong length,
        out FontDiagnostic diagnostic)
    {
        start = length = 0;
        ReadStatus status = RequireUsableList(out uint listOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDocumentRecord(recordIndex, out _, out _, out uint documentOffset,
            out uint documentLength, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (documentOffset == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, RecordAt(listOffset, recordIndex) + 4,
                    recordIndex), out diagnostic, 0UL, 1UL);
        if (documentLength == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, RecordAt(listOffset, recordIndex) + 8,
                    recordIndex), out diagnostic, 0UL, 1UL);
        start = (ulong)listOffset + documentOffset;
        length = documentLength;
        if (start >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, RecordAt(listOffset, recordIndex) + 4,
                    recordIndex), out diagnostic, start, (ulong)_data.Length);
        if (start + length > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Offset, RecordAt(listOffset, recordIndex) + 4,
                    recordIndex), out diagnostic, (ulong)_data.Length - start, length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One record's document payload as opaque bytes: gzip members
    /// travel verbatim (the structural layer never decompresses, OQ-T6-04-7).</summary>
    public ReadStatus TryGetDocumentPayload(int recordIndex, out ReadOnlySpan<byte> payload,
        out FontDiagnostic diagnostic)
    {
        payload = default;
        ReadStatus status = TryGetDocumentSpan(recordIndex, out ulong start, out ulong length,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        payload = _data.Slice((int)start, (int)length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The 3-byte gzip magic observation (0x1F 0x8B 0x08, V L100) -
    /// a census key only: the structural layer performs zero decompression
    /// (OQ-T6-04-7). Unreadable or short payloads answer false.</summary>
    public ReadStatus IsGzipPayload(int recordIndex, out bool isGzip, out FontDiagnostic diagnostic)
    {
        isGzip = false;
        ReadStatus status = TryGetDocumentPayload(recordIndex, out var payload, out diagnostic);
        if (status != ReadStatus.Done) return status;
        isGzip = payload.Length >= 3 && payload[0] == 0x1F && payload[1] == 0x8B && payload[2] == 0x08;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The shared-document group's size for one record: the records
    /// pointing at the same (svgDocOffset, svgDocLength) wire identity - two or
    /// more records can share one document over discontinuous glyph ranges
    /// (V L86). Identical content at distinct offsets stays distinct documents
    /// (the wire's own identity, the frozen shared-group semantics).</summary>
    public ReadStatus TryGetDocumentGroupSize(int recordIndex, out int groupSize,
        out FontDiagnostic diagnostic)
    {
        groupSize = 0;
        ReadStatus status = RequireUsableList(out uint listOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDocumentRecord(recordIndex, out _, out _, out uint documentOffset,
            out uint documentLength, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumEntries(out ushort numEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        groupSize = 0;
        for (int i = 0; i < numEntries; i++)
        {
            status = TryGetDocumentRecord(i, out _, out _, out uint otherOffset,
                out uint otherLength, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (otherOffset == documentOffset && otherLength == documentLength) groupSize++;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One member of a record's shared-document group (ordinal within
    /// the group, in record order); ordinal 0 is the record itself.</summary>
    public ReadStatus TryGetDocumentGroupMember(int recordIndex, int memberOrdinal,
        out int memberRecordIndex, out FontDiagnostic diagnostic)
    {
        memberRecordIndex = -1;
        ReadStatus status = RequireUsableList(out uint listOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDocumentRecord(recordIndex, out _, out _, out uint documentOffset,
            out uint documentLength, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumEntries(out ushort numEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int seen = 0;
        for (int i = 0; i < numEntries; i++)
        {
            status = TryGetDocumentRecord(i, out _, out _, out uint otherOffset,
                out uint otherLength, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (otherOffset == documentOffset && otherLength == documentLength)
            {
                if (seen == memberOrdinal)
                {
                    memberRecordIndex = i;
                    diagnostic = default;
                    return ReadStatus.Done;
                }
                seen++;
            }
        }
        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            Location.AtLocal(DataField.Count, listOffset, memberOrdinal), out diagnostic,
            unchecked((ulong)(long)memberOrdinal), (ulong)seen);
    }

    /// <summary>The bound view's list anchor in usable shape: known version and
    /// a non-zero in-window list offset (the shared-group projections run on
    /// wires the caller has not necessarily validated).</summary>
    private ReadStatus RequireUsableList(out uint listOffset, out FontDiagnostic diagnostic)
    {
        listOffset = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDocumentListOffset(out listOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (listOffset == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, SvgLayout.HeaderListOffsetAt), out diagnostic,
                0UL, 1UL);
        if (listOffset >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, SvgLayout.HeaderListOffsetAt), out diagnostic,
                listOffset, (ulong)_data.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads the validated table into an owned <see cref="SvgTable"/>:
    /// the records carry (start, end, payloadIndex) and the payloads hold each
    /// distinct document's bytes exactly once, in first-occurrence record order
    /// - the byte-faithful sharing the canonical emission re-fires.</summary>
    public ReadStatus TryReadTable(out SvgTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(out SvgTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetVersion(out ushort version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetReserved(out uint reserved, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDocumentListOffset(out uint listOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumEntries(out ushort numEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Payload dedup keys on the (offset, length) wire identity; the first
        // occurrence owns the stored copy and later sharers reference it.
        var records = new SvgDocumentRecord[numEntries];
        var payloads = new List<byte[]>(numEntries);
        var firstIndex = new Dictionary<(uint Offset, uint Length), int>(numEntries);
        long buffer = 8L * numEntries;
        ulong consumed = (ulong)listOffset + 2UL + (ulong)numEntries * SvgLayout.RecordStride;
        for (int i = 0; i < numEntries; i++)
        {
            status = TryGetDocumentRecord(i, out ushort start, out ushort end,
                out uint documentOffset, out uint documentLength, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var key = (documentOffset, documentLength);
            if (!firstIndex.TryGetValue(key, out int payloadIndex))
            {
                payloadIndex = payloads.Count;
                firstIndex[key] = payloadIndex;
                ulong at = (ulong)listOffset + documentOffset;
                payloads.Add(_data.Slice((int)at, (int)documentLength).ToArray());
                buffer += documentLength;
                consumed = Math.Max(consumed, at + documentLength);
            }
            records[i] = new SvgDocumentRecord(start, end, payloadIndex);
        }
        if (!budget.Buffer(buffer, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(version, reserved, listOffset, records, payloads.ToArray(),
            Location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
