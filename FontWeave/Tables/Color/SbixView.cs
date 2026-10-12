using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables;

/// <summary>Strict whole-table validation and materialization for sbix v1
/// (card T6-04 §3). numGlyphs is the explicit required input - the
/// glyphDataOffsets arrays are numGlyphs+1 long and no other authority exists
/// (B L72/L107; T1-01's MaxpTable.NumGlyphs; the T5-04/T5-05 dual-input line,
/// no default value). The flags three-segment contract (bit0 required-1,
/// bit2-15 reserved-zero, bit1 an application draw instruction observed but
/// never checked, OQ-T6-04-8), the strikeOffset non-zero gate, the descending
/// offsets refusal and the graphicType closed set are the hard faces; the dupe
/// graph carries the three-layer structural semantics (truncated target,
/// out-of-domain or empty target, cycles - chains terminate legally,
/// OQ-T6-04-13). numStrikes==0 is a legal bare-header shell; strikeOffsets
/// ordering, cross-strike overlap, strike padding, same-ppem strikes, coverage
/// differences and ppem/ppi values are observations. Error priority follows
/// the T4-01 line and the frozen walk order: input contract, version, flags,
/// array windows, per-strike head, per-strike array, ascent, per-glyph slots.</summary>
public readonly ref partial struct SbixView
{
    public ReadStatus ValidateTable(int numGlyphs, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int numGlyphs, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        // The explicit input contract first (the frozen walk order): the
        // glyphDataOffsets array length has no other authority than this input.
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The version gate: SupportedVersions={1} ("set to 1", B L49); the
        // refusal names the one supported version as its expectation.
        status = TryGetVersion(out ushort version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (version != SbixLayout.Version1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, 0), out diagnostic, version,
                SbixLayout.Version1);
        // The flags three-segment contract in the frozen order: bit0 required
        // (OQ-T6-04-8's lean - the T0 slice check item), then the reserved
        // 0xFFFC mask (the set-to-0 family); bit1 is observed, never checked.
        status = TryGetFlags(out ushort flags, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((flags & SbixLayout.FlagsBit0) == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, 2), out diagnostic, flags, 1UL);
        if ((flags & SbixLayout.FlagsReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, 2), out diagnostic,
                (ulong)(flags & SbixLayout.FlagsReservedMask), 0UL);
        status = TryGetNumStrikes(out uint numStrikes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work((long)_data.Length + 8L * (long)numStrikes +
                8L * (long)numStrikes * (numGlyphs + 1L), Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        // The strikeOffsets array window: the generated extent's first
        // incomplete-slot provenance (window length, slot end).
        status = TryRequireStrikeOffsetsExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int i = 0; i < numStrikes; i++)
        {
            status = TryGetStrikeOffsets(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            uint strikeOffset = row.Value;
            // "strikeOffsets[i]: Must be non-zero" is OQ-T6-04-9's lean face
            // (a self-referential header offset carries no structural meaning;
            // numStrikes==0 never reaches this loop - the legal shell).
            if (strikeOffset == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, SbixLayout.StrikeOffsetsAt +
                        (ulong)i * SbixLayout.StrikeOffsetStride, i), out diagnostic, 0UL, 1UL);
            status = ValidateStrike(i, strikeOffset, numGlyphs, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One strike's walk in the frozen order: the strike head's two
    /// words, the glyphDataOffsets array window, the ascending contract, then
    /// the per-glyph slots (empty dual form, header window, table window,
    /// closed graphicType, dupe layers).</summary>
    private ReadStatus ValidateStrike(int strikeOrdinal, uint strikeOffset,
        int numGlyphs, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (strikeOffset >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, SbixLayout.StrikeOffsetsAt +
                    (ulong)strikeOrdinal * SbixLayout.StrikeOffsetStride, strikeOrdinal),
                out diagnostic, strikeOffset, (ulong)_data.Length);
        // The strike head: ppem then ppi, each naming its own slot (the T4-10
        // first-incomplete-field line).
        if (strikeOffset + 2UL > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, strikeOffset, strikeOrdinal), out diagnostic,
                (ulong)_data.Length, strikeOffset + 2UL);
        if (strikeOffset + SbixLayout.StrikeHeaderLength > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, strikeOffset + 2UL, strikeOrdinal),
                out diagnostic, (ulong)_data.Length, strikeOffset + SbixLayout.StrikeHeaderLength);
        // The glyphDataOffsets array window: numGlyphs+1 u32 slots from the
        // strike start + 4 (the explicit input's authority).
        ulong offsetsAt = strikeOffset + SbixLayout.StrikeHeaderLength;
        ulong arrayBytes = 4UL * ((ulong)numGlyphs + 1UL);
        if (offsetsAt + arrayBytes > (ulong)_data.Length)
        {
            for (int k = 0; k <= numGlyphs; k++)
            {
                ulong at = offsetsAt + 4UL * (ulong)k;
                if (at + 4UL > (ulong)_data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.AtLocal(DataField.Offset, at, k), out diagnostic,
                        (ulong)_data.Length, at + 4UL);
            }
        }
        Span<uint> offsets = numGlyphs + 1 <= 512 ? stackalloc uint[numGlyphs + 1]
            : new uint[numGlyphs + 1];
        for (int k = 0; k <= numGlyphs; k++)
        {
            if (!TrySlice(_data, offsetsAt + 4UL * (ulong)k, 4, out var slot))
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Offset, offsetsAt + 4UL * (ulong)k, k),
                    out diagnostic, (ulong)_data.Length, offsetsAt + 4UL * (ulong)k + 4UL);
            offsets[k] = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(slot);
        }
        // The ascending contract (OQ-T6-04-10's lean): pairs with both anchors
        // non-zero and decreasing refuse at the later anchor's own slot; a
        // negative glyphData length has no derivation (B L72's definitional
        // premise plus Apple's increasing line). 0-anchored pairs are the
        // OQ-T6-04-11 dual-form contract and skip the check.
        for (int gid = 0; gid < numGlyphs; gid++)
        {
            uint a = offsets[gid], b = offsets[gid + 1];
            if (a != 0 && b != 0 && b < a)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset,
                        offsetsAt + 4UL * (ulong)(gid + 1), gid + 1), out diagnostic, b, a);
        }
        // The per-glyph slots: an empty slot is a NULL anchor (a == 0) or a
        // zero span (b == 0 or b == a - the dual form, OQ-T6-04-11), normalized
        // everywhere below.
        for (int gid = 0; gid < numGlyphs; gid++)
        {
            uint a = offsets[gid], b = offsets[gid + 1];
            if (a == 0 || b == 0 || b == a) continue;
            uint span = b - a;
            ulong absoluteAt = strikeOffset + a;
            if (span < SbixLayout.GlyphDataHeaderLength)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Payload, absoluteAt, gid), out diagnostic,
                    span, SbixLayout.GlyphDataHeaderLength);
            if (absoluteAt >= (ulong)_data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.AtLocal(DataField.Offset, offsetsAt + 4UL * (ulong)gid, gid),
                    out diagnostic, a, (ulong)_data.Length - strikeOffset);
            if (absoluteAt + span > (ulong)_data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Payload, absoluteAt, gid), out diagnostic,
                    (ulong)_data.Length - absoluteAt, span);
            if (!TrySlice(_data, absoluteAt + 4UL, 4, out var tagBytes))
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Tag, absoluteAt + 4UL, gid), out diagnostic,
                    (ulong)_data.Length, absoluteAt + 8UL);
            uint graphicType = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(tagBytes);
            if (!SbixLayout.IsGraphicTypeDefined(graphicType))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    Location.AtLocal(DataField.Tag, absoluteAt + 4UL, gid), out diagnostic,
                    graphicType, 0UL);
            if (graphicType == SbixLayout.GraphicTypeDupe)
            {
                uint dataLength = span - SbixLayout.GlyphDataHeaderLength;
                ReadStatus dupeStatus = ValidateDupe(strikeOffset, offsets, gid, dataLength,
                    numGlyphs, out diagnostic);
                if (dupeStatus != ReadStatus.Done) return dupeStatus;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The dupe three-layer structural semantics (OQ-T6-04-13): a
    /// payload shorter than the u16 BE target refuses as a truncation; an
    /// out-of-domain or empty target refuses as an index face; then the chain
    /// walk re-checks every hop (truncation, domain, populated) and refuses a
    /// revisit as a cycle - a non-dupe terminator is legal, chains terminate.</summary>
    private ReadStatus ValidateDupe(uint strikeOffset, ReadOnlySpan<uint> offsets, int gid,
        uint dataLength, int numGlyphs, out FontDiagnostic diagnostic)
    {
        ulong targetAt = strikeOffset + offsets[gid] + SbixLayout.GlyphDataHeaderLength;
        if (dataLength < 2)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, targetAt, gid), out diagnostic,
                dataLength, 2UL);
        if (!TrySlice(_data, targetAt, 2, out var targetBytes))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, targetAt, gid), out diagnostic,
                (ulong)_data.Length, targetAt + 2UL);
        int target = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(targetBytes);
        if (target >= numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, targetAt, gid), out diagnostic,
                (uint)target, (uint)numGlyphs);
        uint targetAnchor = offsets[target];
        if (targetAnchor == 0 || offsets[target + 1] == targetAnchor)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, targetAt, gid), out diagnostic,
                (uint)target, targetAnchor);
        // The chain walk: every hop re-checks structure; a revisit is a cycle
        // (the COLR path-set family); a non-dupe terminator is legal.
        var visited = new HashSet<int> { gid };
        int current = target;
        while (true)
        {
            uint anchor = offsets[current];
            uint span = offsets[current + 1] - anchor;
            ulong at = strikeOffset + anchor;
            if (span < SbixLayout.GlyphDataHeaderLength)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Payload, at, current), out diagnostic,
                    span, SbixLayout.GlyphDataHeaderLength);
            if (!TrySlice(_data, at + 4UL, 4, out var tagBytes))
                break; // an unreadable hop cannot name 'dupe': the chain terminates
            uint hopType = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(tagBytes);
            if (hopType != SbixLayout.GraphicTypeDupe) break;
            if (span - SbixLayout.GlyphDataHeaderLength < 2)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Payload,
                        at + SbixLayout.GlyphDataHeaderLength, current), out diagnostic,
                    span - SbixLayout.GlyphDataHeaderLength, 2UL);
            if (!TrySlice(_data, at + SbixLayout.GlyphDataHeaderLength, 2, out var hopTargetBytes))
                break;
            int next = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(hopTargetBytes);
            if (next >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId,
                        at + SbixLayout.GlyphDataHeaderLength, current), out diagnostic,
                    (uint)next, (uint)numGlyphs);
            uint nextAnchor = offsets[next];
            if (nextAnchor == 0 || offsets[next + 1] == nextAnchor)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId,
                        at + SbixLayout.GlyphDataHeaderLength, current), out diagnostic,
                    (uint)next, nextAnchor);
            if (visited.Contains(next))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.GlyphId,
                        at + SbixLayout.GlyphDataHeaderLength, current), out diagnostic,
                    (uint)next, 0UL);
            visited.Add(next);
            current = next;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One strike's head: ppem, ppi and the glyphDataOffsets array's
    /// base (the strike start + 4; array offsets are strike-relative).</summary>
    public ReadStatus TryGetStrike(int strikeIndex, out ushort ppem, out ushort ppi,
        out ulong glyphDataOffsetsAt, out FontDiagnostic diagnostic)
    {
        ppem = ppi = 0;
        glyphDataOffsetsAt = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = TryGetVersion(out ushort version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (version != SbixLayout.Version1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, 0), out diagnostic, version,
                SbixLayout.Version1);
        status = TryGetNumStrikes(out uint numStrikes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((uint)strikeIndex >= numStrikes)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 4, strikeIndex), out diagnostic,
                unchecked((ulong)(long)strikeIndex), numStrikes);
        status = TryGetStrikeOffsets(strikeIndex, out var row, out diagnostic);
        if (status != ReadStatus.Done) return status;
        uint strikeOffset = row.Value;
        if (strikeOffset == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, SbixLayout.StrikeOffsetsAt +
                    (ulong)strikeIndex * SbixLayout.StrikeOffsetStride, strikeIndex),
                out diagnostic, 0UL, 1UL);
        if (strikeOffset >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, SbixLayout.StrikeOffsetsAt +
                    (ulong)strikeIndex * SbixLayout.StrikeOffsetStride, strikeIndex),
                out diagnostic, strikeOffset, (ulong)_data.Length);
        if (strikeOffset + 2UL > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, strikeOffset, strikeIndex), out diagnostic,
                (ulong)_data.Length, strikeOffset + 2UL);
        if (strikeOffset + SbixLayout.StrikeHeaderLength > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, strikeOffset + 2UL, strikeIndex),
                out diagnostic, (ulong)_data.Length, strikeOffset + SbixLayout.StrikeHeaderLength);
        if (!TrySlice(_data, strikeOffset, 2, out var ppemBytes) ||
            !TrySlice(_data, strikeOffset + 2UL, 2, out var ppiBytes))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, strikeOffset, strikeIndex), out diagnostic,
                (ulong)_data.Length, strikeOffset + SbixLayout.StrikeHeaderLength);
        ppem = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(ppemBytes);
        ppi = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(ppiBytes);
        glyphDataOffsetsAt = strikeOffset + SbixLayout.StrikeHeaderLength;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One glyph's slot bytes (the 8-byte GlyphData head plus the
    /// opaque payload) in absolute view coordinates. The empty dual form
    /// normalizes to a zero-length span (OQ-T6-04-11): a NULL anchor and a
    /// zero span both answer Done with an empty slice.</summary>
    public ReadStatus TryGetGlyphSpan(int strikeIndex, int glyphId,
        out ReadOnlySpan<byte> glyphData, out FontDiagnostic diagnostic)
    {
        glyphData = default;
        ReadStatus status = TryGetGlyphWindow(strikeIndex, glyphId, out ulong absoluteAt,
            out uint span, out bool empty, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (empty)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        glyphData = _data.Slice((int)absoluteAt, (int)span);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One glyph's GlyphData head: the raw origin placement pair and
    /// the graphicType tag; present=false for the normalized empty slots
    /// (OQ-T6-04-11's dual form).</summary>
    public ReadStatus TryGetGlyphHeader(int strikeIndex, int glyphId, out short originOffsetX,
        out short originOffsetY, out uint graphicType, out bool present,
        out FontDiagnostic diagnostic)
    {
        originOffsetX = originOffsetY = 0;
        graphicType = 0;
        present = false;
        ReadStatus status = TryGetGlyphWindow(strikeIndex, glyphId, out ulong absoluteAt,
            out uint span, out bool empty, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (empty)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (!TrySlice(_data, absoluteAt, SbixLayout.GlyphDataHeaderLength, out var header))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, absoluteAt, glyphId), out diagnostic,
                (ulong)_data.Length, absoluteAt + SbixLayout.GlyphDataHeaderLength);
        originOffsetX = global::System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(header);
        originOffsetY = global::System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(header.Slice(2));
        graphicType = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4));
        present = true;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One glyph's opaque payload bytes (past the 8-byte head);
    /// present=false for the normalized empty slots.</summary>
    public ReadStatus TryGetGlyphPayload(int strikeIndex, int glyphId,
        out ReadOnlySpan<byte> payload, out bool present, out FontDiagnostic diagnostic)
    {
        payload = default;
        present = false;
        ReadStatus status = TryGetGlyphWindow(strikeIndex, glyphId, out ulong absoluteAt,
            out uint span, out bool empty, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (empty)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        payload = _data.Slice((int)absoluteAt + SbixLayout.GlyphDataHeaderLength,
            (int)(span - SbixLayout.GlyphDataHeaderLength));
        present = true;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>A dupe slot's u16 big-endian target glyph ID (B L101); the
    /// slot must carry the 'dupe' graphicType and a readable target.</summary>
    public ReadStatus TryGetDupeTarget(int strikeIndex, int glyphId, out ushort targetGlyphId,
        out FontDiagnostic diagnostic)
    {
        targetGlyphId = 0;
        ReadStatus status = TryGetGlyphHeader(strikeIndex, glyphId, out _, out _,
            out uint graphicType, out bool present, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!present || graphicType != SbixLayout.GraphicTypeDupe)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Tag, 0, glyphId), out diagnostic, graphicType, 0UL);
        status = TryGetGlyphPayload(strikeIndex, glyphId, out var payload, out present,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (payload.Length < 2)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, 0, glyphId), out diagnostic,
                (ulong)payload.Length, 2UL);
        targetGlyphId = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(payload);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One glyph slot's resolved absolute window: the strike head and
    /// the offsets pair, the normalized empty form, and the window faces the
    /// read chain enforces (slot shorter than the head, outside the source,
    /// short body). The glyph id answers the 0..65535 input contract; the
    /// numGlyphs authority stays the caller's ValidateTable/TryReadTable input
    /// - a raw accessor reads window-bounded or refuses.</summary>
    private ReadStatus TryGetGlyphWindow(int strikeIndex, int glyphId, out ulong absoluteAt,
        out uint span, out bool empty, out FontDiagnostic diagnostic)
    {
        absoluteAt = 0;
        span = 0;
        empty = true;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(glyphId, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetStrike(strikeIndex, out _, out _, out ulong offsetsAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        uint strikeOffset = checked((uint)(offsetsAt - SbixLayout.StrikeHeaderLength));
        uint a = 0, b = 0;
        for (int k = glyphId; k <= glyphId + 1; k++)
        {
            if (!TrySlice(_data, offsetsAt + 4UL * (ulong)k, 4, out var slot))
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Offset, offsetsAt + 4UL * (ulong)k, k),
                    out diagnostic, (ulong)_data.Length, offsetsAt + 4UL * (ulong)k + 4UL);
            uint value = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(slot);
            if (k == glyphId) a = value;
            else b = value;
        }
        empty = SbixLayout.IsEmptySlot(a, b);
        if (empty)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        span = b - a;
        absoluteAt = strikeOffset + a;
        if (span < SbixLayout.GlyphDataHeaderLength)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, absoluteAt, glyphId), out diagnostic,
                span, SbixLayout.GlyphDataHeaderLength);
        if (absoluteAt >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, offsetsAt + 4UL * (ulong)glyphId, glyphId),
                out diagnostic, a, (ulong)_data.Length - strikeOffset);
        if (absoluteAt + span > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, absoluteAt, glyphId), out diagnostic,
                (ulong)_data.Length - absoluteAt, span);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads the validated table into an owned <see cref="SbixTable"/>:
    /// every glyph slot materializes as its raw origin pair, graphicType and
    /// verbatim payload bytes, or the normalized empty (OQ-T6-04-11's dual
    /// form collapses to a single owned shape; the wire tier is decided at
    /// compile time).</summary>
    public ReadStatus TryReadTable(int numGlyphs, out SbixTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int numGlyphs, out SbixTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetVersion(out ushort version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetFlags(out ushort flags, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumStrikes(out uint numStrikes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var strikes = new SbixStrike[numStrikes];
        long buffer = 8L * numStrikes * (numGlyphs + 1L);
        ulong consumed = SbixLayout.StrikeOffsetsAt + 4UL * numStrikes;
        for (int i = 0; i < numStrikes; i++)
        {
            status = TryGetStrike(i, out ushort ppem, out ushort ppi, out ulong offsetsAt,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = TryGetStrikeOffsets(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            uint strikeOffset = row.Value;
            var glyphs = new SbixGlyph?[numGlyphs];
            // The offsets array's own end is a floor: a strike of all-empty
            // slots still consumes its array.
            consumed = Math.Max(consumed, strikeOffset + SbixLayout.StrikeHeaderLength +
                4UL * ((ulong)numGlyphs + 1UL));
            for (int gid = 0; gid < numGlyphs; gid++)
            {
                status = TryGetGlyphWindow(i, gid, out ulong absoluteAt, out uint span,
                    out bool empty, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (empty) continue;
                status = TryGetGlyphHeader(i, gid, out short originX, out short originY,
                    out uint graphicType, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                var payload = new byte[span - SbixLayout.GlyphDataHeaderLength];
                _data.Slice((int)absoluteAt + SbixLayout.GlyphDataHeaderLength,
                    payload.Length).CopyTo(payload);
                glyphs[gid] = new SbixGlyph(originX, originY, graphicType, payload);
                buffer += payload.Length;
                consumed = Math.Max(consumed, absoluteAt + span);
            }
            strikes[i] = new SbixStrike(ppem, ppi, glyphs);
        }
        if (!budget.Buffer(buffer, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(version, flags, numGlyphs, strikes, Location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
