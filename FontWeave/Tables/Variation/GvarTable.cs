using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>Owned gvar 1.0 table: the two explicit authorities the caller
/// supplied (fvarAxisCount as the axis authority, numGlyphs from maxp), the
/// shared tuples as raw F2DOT14 vectors, the decoded GlyphVariationData offsets
/// (short-form values already doubled) and the flags-preserved offsets form. The
/// table bytes are kept so the per-glyph blocks can be materialized lazily - the
/// glyf-domain glyph-scoped shape (card 2, OQ-1): a whole-table read validates
/// every block without materializing the 70,612-tuple witnesses, and
/// <see cref="TryReadGlyphVariationData"/> materializes one glyph at a time.</summary>
public sealed class GvarTable
{
    private readonly byte[] _tableBytes;
    private readonly int[] _offsets;
    private readonly int _gvdArrayOffset;

    internal GvarTable(int axisCount, int numGlyphs, ushort flags,
        IReadOnlyList<short[]> sharedTuples, int[] offsets, int gvdArrayOffset,
        byte[] tableBytes, DataLocation location, int consumedLength)
    {
        AxisCount = axisCount;
        NumGlyphs = numGlyphs;
        Flags = flags;
        SharedTuples = sharedTuples;
        _offsets = offsets;
        _gvdArrayOffset = gvdArrayOffset;
        _tableBytes = tableBytes;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The validated fvar axis count (equal to the header's AxisCount).</summary>
    public int AxisCount { get; }

    /// <summary>The validated maxp numGlyphs (equal to the header's GlyphCount).</summary>
    public int NumGlyphs { get; }

    /// <summary>The raw header flags; bit0 is the offsets form (preserved on
    /// write, OQ-7), the upper bits carry no defined semantics.</summary>
    public ushort Flags { get; }

    public bool LongOffsets => (Flags & GvarLayout.LongOffsetsFlag) != 0;

    /// <summary>The shared peak tuples, each axisCount raw F2DOT14 values in axis
    /// order (gvar-only data; cvar has none).</summary>
    public IReadOnlyList<short[]> SharedTuples { get; }

    /// <summary>The decoded byte offsets relative to the GlyphVariationData array
    /// start, glyphCount+1 entries (short-form values doubled). Equal neighbors
    /// are the absent-glyph form (a legal positive, card 3).</summary>
    public IReadOnlyList<int> GlyphVariationDataOffsets => _offsets;

    public DataLocation Location { get; }

    /// <summary>The whole table is consumed (the offsets array plus the
    /// GlyphVariationData array span the tail of the table).</summary>
    public int ConsumedLength { get; }

    /// <summary>The exact wire bytes this model carries (source for read-built
    /// models, canonical emission for parts-built ones); the compiler re-emits
    /// the GlyphVariationData blocks from here (OQ-7 verbatim) and the edits
    /// splice into it.</summary>
    internal byte[] RawTableBytes => _tableBytes;

    internal int GvdArrayOffset => _gvdArrayOffset;

    /// <summary>Materializes one glyph's GlyphVariationData block.
    /// <paramref name="pointCount"/> is the glyph's outline item count from the
    /// glyf/loca typed face (simple glyphs: total points; composites: the direct
    /// component count - the four phantom points are added here, card OQ-1); a
    /// negative value means no topology context, which refuses all-points tuples
    /// with UnsupportedOperation while sparse tuples still decode. An absent
    /// glyph (zero-size block) is Done with present=false.</summary>
    public ReadStatus TryReadGlyphVariationData(int glyphId, int pointCount,
        out GlyphVariationDataTable? data, out bool present, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        data = null;
        present = false;
        var budget = new BuildBudget(limits);
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)NumGlyphs);
        if (!budget.Input(_tableBytes.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int start = _gvdArrayOffset + _offsets[glyphId];
        int size = _offsets[glyphId + 1] - _offsets[glyphId];
        if (size == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        int allPointsDeltaCount = pointCount < 0 ? -1 :
            (pointCount + TupleVariationCodec.PhantomPointCount) * 2;
        var tuples = new List<TupleVariation>();
        // Whole-table coordinates (headerStart = dataOffsetBase = the block
        // start) so diagnostics land on table-absolute offsets, matching the
        // validation pass.
        ReadStatus status = TupleVariationStoreReader.ReadStore(
            _tableBytes, start, start + size, start, isCvar: false, axisCount: AxisCount,
            sharedTupleCount: SharedTuples.Count, allPointsDeltaCount: allPointsDeltaCount,
            cvtValueCount: -1, materialize: true, tuplesOut: tuples, location: Location,
            budget: budget, tvcRaw: out ushort tvcRaw, sharedPointNumbers: out int[]? shared,
            serializedEnd: out int serializedEnd, diagnostic: out diagnostic);
        if (status != ReadStatus.Done) return status;
        data = new GlyphVariationDataTable(tvcRaw, shared, tuples,
            start + size - serializedEnd, Location);
        present = true;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Replaces one tuple's delta columns (gvar: X then Y). The tuple is
    /// addressed by its ordinal inside the glyph's header order; the private
    /// point number bytes are preserved byte-for-byte and only the delta runs are
    /// repacked (per-value minimum width, adjacent equal-width runs merged -
    /// OQ-7). Structural changes (tuple add/remove, point set changes,
    /// encoding-class rewrites, form upgrades) have no entry point (OQ-8 finite
    /// refusal face).</summary>
    public ReadStatus TryWithGlyphDeltas(int glyphId, int tupleOrdinal, short[] xDeltas,
        short[] yDeltas, out GvarTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = PrepareEdit(glyphId, tupleOrdinal, out int blockStart, out int blockSize,
            out int headerAt, out ushort tupleIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (xDeltas is null || yDeltas is null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 0, glyphId), out diagnostic, 0, 1);
        if (xDeltas.Length != yDeltas.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 0, glyphId), out diagnostic,
                (ulong)yDeltas.Length, (ulong)xDeltas.Length);
        if (!budget.Input(_tableBytes.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int runStart = TupleRunStart(blockStart, headerAt);
        int runSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(
            _tableBytes.AsSpan(headerAt, 2));
        bool hasPrivate = (tupleIndex & TupleVariationCodec.PrivatePointNumbersFlag) != 0;
        int pointBytes = 0;
        int[]? privatePoints = null;
        if (hasPrivate)
        {
            // The packed private point bytes are preserved verbatim (OQ-7); only
            // their length is needed here. Tuples without private data carry no
            // point block at all.
            status = TupleVariationCodec.TryDecodePointNumbers(_tableBytes, runStart,
                runStart + runSize, Location, blockStart, out privatePoints, out pointBytes,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        // The point-number set is unchanged, so the delta column length must
        // stay the tuple's logical one - checked whenever the existing set
        // determines it (explicit lists, no-data); a context-free all-points
        // tuple takes the caller's topology on faith and the read-back replays
        // it against the glyf counts (OQ-1).
        status = ExistingColumnCount(blockStart, hasPrivate, privatePoints, out int columns,
            out bool contextFree, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!contextFree && xDeltas.Length != columns)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 0, glyphId), out diagnostic,
                (ulong)xDeltas.Length, (ulong)columns);
        int deltaBytes = TupleVariationCodec.MeasureDeltaRuns(xDeltas) +
            TupleVariationCodec.MeasureDeltaRuns(yDeltas);
        long newSize = pointBytes + (long)deltaBytes;
        if (newSize > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, (ulong)headerAt, tupleOrdinal), out diagnostic,
                (ulong)newSize, (ulong)ushort.MaxValue);
        var run = new byte[(int)newSize];
        _tableBytes.AsSpan(runStart, pointBytes).CopyTo(run);
        var writer = new Span<byte>(run, pointBytes, run.Length - pointBytes);
        TupleVariationCodec.WriteDeltaRuns(xDeltas, writer);
        TupleVariationCodec.WriteDeltaRuns(yDeltas, writer.Slice(
            TupleVariationCodec.MeasureDeltaRuns(xDeltas)));
        status = Splice(glyphId, headerAt, runStart, runSize, run, tupleOrdinal, out var bytes,
            out var offsets, out diagnostic);
        if (status != ReadStatus.Done) return status;
        updated = new GvarTable(AxisCount, NumGlyphs, Flags, SharedTuples, offsets,
            _gvdArrayOffset, bytes, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Replaces one tuple's embedded peak coordinates. Tuples that
    /// dereference the shared tuples refuse (the shared-to-embedded rewrite is an
    /// encoding-class change, OQ-8); the wire tupleIndex is preserved.</summary>
    public ReadStatus TryWithGlyphPeakCoords(int glyphId, int tupleOrdinal, short[] coords,
        out GvarTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = PrepareEdit(glyphId, tupleOrdinal, out int blockStart, out _,
            out int headerAt, out ushort tupleIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((tupleIndex & TupleVariationCodec.EmbeddedPeakFlag) == 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Flags, (ulong)(headerAt + 2), tupleOrdinal),
                out diagnostic, tupleIndex, TupleVariationCodec.EmbeddedPeakFlag);
        status = CheckCoords(coords, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(_tableBytes.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int at = headerAt + 4;
        var replacement = new byte[coords.Length * 2];
        for (int i = 0; i < coords.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(
                replacement.AsSpan(i * 2, 2), coords[i]);
        var bytes = (byte[])_tableBytes.Clone();
        replacement.CopyTo(bytes, at);
        updated = new GvarTable(AxisCount, NumGlyphs, Flags, SharedTuples, _offsets,
            _gvdArrayOffset, bytes, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Replaces one tuple's intermediate region start/end coordinates;
    /// tuples without the INTERMEDIATE_REGION flag refuse (OQ-8).</summary>
    public ReadStatus TryWithGlyphIntermediateCoords(int glyphId, int tupleOrdinal,
        short[] startCoords, short[] endCoords, out GvarTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = PrepareEdit(glyphId, tupleOrdinal, out _, out _,
            out int headerAt, out ushort tupleIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((tupleIndex & TupleVariationCodec.IntermediateRegionFlag) == 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Flags, (ulong)(headerAt + 2), tupleOrdinal),
                out diagnostic, tupleIndex, TupleVariationCodec.IntermediateRegionFlag);
        status = CheckCoords(startCoords, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckCoords(endCoords, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(_tableBytes.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int embeddedLength = (tupleIndex & TupleVariationCodec.EmbeddedPeakFlag) != 0
            ? 2 * AxisCount : 0;
        int at = headerAt + 4 + embeddedLength;
        var replacement = new byte[AxisCount * 4];
        for (int i = 0; i < AxisCount; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(
                replacement.AsSpan(i * 4, 2), startCoords[i]);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(
                replacement.AsSpan(i * 4 + 2, 2), endCoords[i]);
        }
        var bytes = (byte[])_tableBytes.Clone();
        replacement.CopyTo(bytes, at);
        updated = new GvarTable(AxisCount, NumGlyphs, Flags, SharedTuples, _offsets,
            _gvdArrayOffset, bytes, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The existing delta column count of the target tuple: explicit point
    // numbers (the tuple's private data, else the block's shared data) pin twice
    // their count for the gvar columns; the count==0 marker is context-free; no
    // point data at all pins zero.
    private ReadStatus ExistingColumnCount(int blockStart, bool hasPrivate,
        int[]? privatePoints, out int columns, out bool contextFree,
        out FontDiagnostic diagnostic)
    {
        int[]? points;
        if (hasPrivate)
        {
            points = privatePoints;
        }
        else if ((ReadU16(blockStart) & TupleVariationCodec.SharedPointNumbersFlag) != 0)
        {
            int dataOffset = ReadU16(blockStart + 2);
            ReadStatus status = TupleVariationCodec.TryDecodePointNumbers(_tableBytes,
                blockStart + dataOffset, _tableBytes.Length, Location, blockStart,
                out points, out _, out diagnostic);
            if (status != ReadStatus.Done)
            {
                columns = 0;
                contextFree = false;
                return status;
            }
        }
        else
        {
            points = Array.Empty<int>(); // neither private nor shared data
        }
        contextFree = points is null;
        columns = (points?.Length ?? 0) * 2;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Edit shared prep: the glyph and tuple ordinal domains, then the byte
    // positions of the target tuple's header (the header walk is stride-only -
    // the bytes were validated at read).
    private ReadStatus PrepareEdit(int glyphId, int tupleOrdinal, out int blockStart,
        out int blockSize, out int headerAt, out ushort tupleIndex, out FontDiagnostic diagnostic)
    {
        blockStart = 0;
        blockSize = 0;
        headerAt = 0;
        tupleIndex = 0;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)NumGlyphs);
        blockStart = _gvdArrayOffset + _offsets[glyphId];
        blockSize = _offsets[glyphId + 1] - _offsets[glyphId];
        if (blockSize == 0)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), 0ul);
        var headers = TupleVariationCodec.LocateTupleVariationHeaders(_tableBytes, blockStart,
            blockStart + blockSize, AxisCount, ReadU16(blockStart) & 0x0FFF);
        if (headers is null || tupleOrdinal < 0 || tupleOrdinal >= headers.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, (ulong)blockStart, tupleOrdinal),
                out diagnostic, unchecked((ulong)(long)tupleOrdinal),
                (ulong)(headers?.Length ?? 0));
        headerAt = headers[tupleOrdinal];
        tupleIndex = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(
            _tableBytes.AsSpan(headerAt + 2, 2));
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The serialized run start of the tuple whose header sits at headerAt: the
    // block's dataOffset plus the declared sizes of the preceding runs.
    private int TupleRunStart(int blockStart, int headerAt)
    {
        int cursor = blockStart + ReadU16(blockStart + 2);
        int at = blockStart + 4;
        while (at < headerAt)
        {
            cursor += ReadU16(at);
            ushort tupleIndex = ReadU16(at + 2);
            at += TupleVariationCodec.TupleVariationHeaderSize(tupleIndex, AxisCount);
        }
        return cursor;
    }

    private ushort ReadU16(int at) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(_tableBytes.AsSpan(at, 2));

    // Replaces one tuple's serialized run and header size field, shifting the
    // following runs, and rebuilds the offsets array for the shifted blocks.
    private ReadStatus Splice(int glyphId, int headerAt, int runStart, int oldRunSize,
        byte[] newRun, int tupleOrdinal, out byte[] bytes, out int[] offsets,
        out FontDiagnostic diagnostic)
    {
        bytes = null!;
        offsets = null!;
        int delta = newRun.Length - oldRunSize;
        var buffer = new byte[_tableBytes.Length + delta];
        _tableBytes.AsSpan(0, headerAt).CopyTo(buffer);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
            buffer.AsSpan(headerAt, 2), (ushort)newRun.Length);
        _tableBytes.AsSpan(headerAt + 2, runStart - headerAt - 2).CopyTo(
            buffer.AsSpan(headerAt + 2));
        newRun.CopyTo(buffer, runStart);
        _tableBytes.AsSpan(runStart + oldRunSize).CopyTo(buffer.AsSpan(runStart + newRun.Length));
        offsets = (int[])_offsets.Clone();
        for (int i = glyphId + 1; i < offsets.Length; i++)
            offsets[i] += delta;
        bytes = buffer;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus CheckCoords(short[]? coords, out FontDiagnostic diagnostic)
    {
        if (coords is null || coords.Length != AxisCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 0), out diagnostic,
                (ulong)(coords?.Length ?? 0), (ulong)AxisCount);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
