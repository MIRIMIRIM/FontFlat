using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The packed-point/packed-delta serialization core of the tuple variation
/// stores (otvarcommonformats): pure span-in/position-out helpers with no view
/// state. One implementation serves the gvar GlyphVariationData partials and the
/// cvar store partial - the正面 single-reader form (AGENTS; card 2). The decode
/// walks the normative "read until the expected logical count of deltas is
/// obtained" contract (OQ-9 lean); the encode is the deterministic minimum-width
/// repack of the write side (OQ-7), never an optimizing compiler. All positions
/// are block-local; the caller supplies the view location and the block's
/// table-absolute base so diagnostics land on the failing byte.</summary>
internal static class TupleVariationCodec
{
    // tupleVariationCount field bits: the packed form is flags in the high nibble
    // and the tuple count in the low 12 bits.
    internal const ushort SharedPointNumbersFlag = 0x8000;
    internal const ushort TvcReservedMask = 0x7000;
    internal const ushort TvcCountMask = 0x0FFF;
    // TupleVariationHeader tupleIndex bits.
    internal const ushort EmbeddedPeakFlag = 0x8000;
    internal const ushort IntermediateRegionFlag = 0x4000;
    internal const ushort PrivatePointNumbersFlag = 0x2000;
    internal const ushort TupleIndexReservedMask = 0x1000;
    internal const ushort TupleIndexMask = 0x0FFF;
    // Packed point number run control bits.
    internal const byte PointsAreWords = 0x80;
    internal const byte PointRunCountMask = 0x7F;
    // Packed delta run control bits.
    internal const byte DeltasAreZero = 0x80;
    internal const byte DeltasAreWords = 0x40;
    internal const byte DeltaRunCountMask = 0x3F;

    /// <summary>Phantom points the rasterizer adds to every glyph: the all-points
    /// logical delta count is the outline item count (simple-glyph points or the
    /// direct components of a composite) plus this constant (spec gvar.md, phantom
    /// points; the constant reconciles over the 123,059-tuple frozen corpus,
    /// card 0/fix 6).</summary>
    internal const int PhantomPointCount = 4;

    /// <summary>Next TupleVariationHeader stride: 4 fixed bytes, the peak coordinate
    /// pair under EMBEDDED_PEAK_TUPLE and the two intermediate coordinates under
    /// INTERMEDIATE_REGION - the dynamic walk the declarations cannot express.</summary>
    internal static int TupleVariationHeaderSize(ushort tupleIndex, int axisCount)
    {
        int size = 4;
        if ((tupleIndex & EmbeddedPeakFlag) != 0) size += 2 * axisCount;
        if ((tupleIndex & IntermediateRegionFlag) != 0) size += 4 * axisCount;
        return size;
    }

    /// <summary>Decodes one packed point number block from [start, end). A null
    /// result is the count==0 all-points marker (the logical count comes from the
    /// glyph/CVT context, never from expansion); a non-null result is the explicit
    /// increasing point number list. Truncated when the count bytes or a run
    /// payload crosses the window end; InvalidValue when a run would deliver more
    /// point numbers than the declared count (the runs must reach it exactly,
    /// OQ-9).</summary>
    internal static ReadStatus TryDecodePointNumbers(ReadOnlySpan<byte> data, int start, int end,
        DataLocation location, int baseOffset, out int[]? points, out int consumed,
        out FontDiagnostic diagnostic)
    {
        points = null;
        consumed = 0;
        int at = start;
        if (at >= end)
            return Truncated(location, baseOffset + at, end - start, end - start + 1,
                DataField.Count, out diagnostic);
        int first = data[at++];
        if (first == 0)
        {
            consumed = at - start;
            diagnostic = default;
            return ReadStatus.Done;
        }
        int count;
        if (first < 0x80)
        {
            count = first;
        }
        else
        {
            if (at >= end)
                return Truncated(location, baseOffset + at, end - start, end - start + 1,
                    DataField.Count, out diagnostic);
            count = ((first & PointRunCountMask) << 8) | data[at++];
        }
        var numbers = new int[count];
        uint last = 0;
        int produced = 0;
        while (produced < count)
        {
            if (at >= end)
                return Truncated(location, baseOffset + at, end - start, end - start + 1,
                    DataField.Count, out diagnostic);
            int controlAt = at;
            int control = data[at++];
            int run = (control & PointRunCountMask) + 1;
            if (produced + run > count)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, (ulong)(baseOffset + controlAt)),
                    out diagnostic, (ulong)(produced + run), (ulong)count);
            bool words = (control & PointsAreWords) != 0;
            int payload = run * (words ? 2 : 1);
            if (at + payload > end)
                return Truncated(location, baseOffset + at, end - start, at + payload - start,
                    DataField.Payload, out diagnostic);
            if (words)
            {
                for (int i = 0; i < run; i++)
                {
                    last += BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 2 * i, 2));
                    numbers[produced++] = unchecked((int)last);
                }
            }
            else
            {
                for (int i = 0; i < run; i++)
                {
                    last += data[at + i];
                    numbers[produced++] = unchecked((int)last);
                }
            }
            at += payload;
        }
        points = numbers;
        consumed = at - start;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Decodes packed deltas from [start, end) until exactly
    /// <paramref name="logicalCount"/> values are obtained (gvar passes twice the
    /// point number count, X column first; cvar passes one count). Truncated when
    /// the runs end or a run payload crosses the window end before the logical
    /// count is reached; InvalidValue when a run would overshoot the logical count
    /// (the runs must reach it exactly - OQ-9 lean).</summary>
    internal static ReadStatus TryDecodeDeltas(ReadOnlySpan<byte> data, int start, int end,
        int logicalCount, DataLocation location, int baseOffset, out short[] deltas,
        out int consumed, out FontDiagnostic diagnostic)
    {
        deltas = new short[logicalCount];
        consumed = 0;
        int at = start;
        int produced = 0;
        while (produced < logicalCount)
        {
            if (at >= end)
                return Truncated(location, baseOffset + at, end - start, end - start + 1,
                    DataField.Payload, out diagnostic);
            int controlAt = at;
            int control = data[at++];
            int run = (control & DeltaRunCountMask) + 1;
            if (produced + run > logicalCount)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Length, (ulong)(baseOffset + controlAt)),
                    out diagnostic, (ulong)(produced + run), (ulong)logicalCount);
            if ((control & DeltasAreZero) != 0)
            {
                produced += run;
                continue;
            }
            bool words = (control & DeltasAreWords) != 0;
            int payload = run * (words ? 2 : 1);
            if (at + payload > end)
                return Truncated(location, baseOffset + at, end - start, at + payload - start,
                    DataField.Payload, out diagnostic);
            if (words)
            {
                for (int i = 0; i < run; i++)
                    deltas[produced++] = BinaryPrimitives.ReadInt16BigEndian(
                        data.Slice(at + 2 * i, 2));
            }
            else
            {
                for (int i = 0; i < run; i++)
                    deltas[produced++] = unchecked((sbyte)data[at + i]);
            }
            at += payload;
        }
        consumed = at - start;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Deterministic minimum-width packed emission (OQ-7): point numbers as one
    // count header plus uint8/uint16 difference runs; deltas as zero/int8/int16
    // runs of at most 64 elements, each run one width class. These serve the
    // edited-delta repack path only - unedited tuples emit their source bytes
    // verbatim (OQ-7), so no repack optimizer exists on this path.

    internal static int PointNumberHeaderSize(int count) => count <= 0x7F ? 1 : 2;

    internal static void WritePointNumberHeader(Span<byte> destination, int count)
    {
        if (count <= 0x7F)
        {
            destination[0] = (byte)count;
        }
        else
        {
            destination[0] = (byte)(0x80 | (count >> 8));
            destination[1] = (byte)(count & 0xFF);
        }
    }

    /// <summary>Exact encoded size of a full packed point number block for an
    /// explicit increasing list: the count header plus the per-difference
    /// minimum-width runs (the write path's measure - it must agree byte for
    /// byte with <see cref="WritePointNumberValues"/>).</summary>
    internal static int MeasurePointNumberValues(int[] points)
    {
        int bytes = PointNumberHeaderSize(points.Length);
        uint last = 0;
        var diffs = new uint[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            diffs[i] = (uint)points[i] - last;
            last = (uint)points[i];
        }
        int at = 0;
        while (at < points.Length)
        {
            bool words = diffs[at] > byte.MaxValue;
            // The run bound is the elements left in this run class, capped at the
            // 128 the control byte encodes - a bound mixed with the absolute
            // position indexes past the list on every run after the first
            // (>128 point numbers).
            int limit = Math.Min(PointRunCountMask + 1, points.Length - at);
            int run = 1;
            while (run < limit && (diffs[at + run] > byte.MaxValue) == words) run++;
            bytes += 1 + run * (words ? 2 : 1);
            at += run;
        }
        return bytes;
    }

    /// <summary>Writes the packed point number block for an explicit increasing
    /// list: count header, then per-point differences in minimum-width runs of at
    /// most 128 elements, each run one width class (OQ-7 repack rule).</summary>
    internal static void WritePointNumberValues(Span<byte> destination, int[] points)
    {
        WritePointNumberHeader(destination, points.Length);
        int outAt = PointNumberHeaderSize(points.Length);
        uint last = 0;
        var diffs = new uint[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            diffs[i] = (uint)points[i] - last;
            last = (uint)points[i];
        }
        int at = 0;
        while (at < points.Length)
        {
            bool words = diffs[at] > byte.MaxValue;
            int limit = Math.Min(PointRunCountMask + 1, points.Length - at);
            int run = 1;
            while (run < limit && (diffs[at + run] > byte.MaxValue) == words) run++;
            destination[outAt++] = (byte)((words ? PointsAreWords : 0) | (run - 1));
            for (int i = 0; i < run; i++)
            {
                if (words)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(outAt),
                        (ushort)diffs[at + i]);
                    outAt += 2;
                }
                else
                {
                    destination[outAt++] = (byte)diffs[at + i];
                }
            }
            at += run;
        }
    }

    /// <summary>Positions of every TupleVariationHeader inside the store window
    /// (the dynamic stride walk, no semantic checks - the bytes were validated at
    /// read; the byte-level edit path uses this to locate splicing targets).
    /// Returns null when the headers do not fit the window.</summary>
    internal static int[]? LocateTupleVariationHeaders(ReadOnlySpan<byte> data, int headerStart,
        int windowEnd, int axisCount, int tupleCount)
    {
        if (tupleCount == 0) return Array.Empty<int>();
        var positions = new int[tupleCount];
        int at = headerStart + 4;
        for (int t = 0; t < tupleCount; t++)
        {
            if (at + 4 > windowEnd) return null;
            ushort tupleIndex = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 2, 2));
            positions[t] = at;
            at += TupleVariationHeaderSize(tupleIndex, axisCount);
        }
        return positions;
    }

    /// <summary>The encoded size of the delta runs: zero runs carry no payload, an
    /// int8 run one byte per value, an int16 run two.</summary>
    internal static int MeasureDeltaRuns(short[] deltas)
    {
        int bytes = 0;
        int at = 0;
        while (at < deltas.Length)
        {
            int run = DeltaRunLength(deltas, at);
            bytes += 1 + run * DeltaWidth(deltas[at]);
            at += run;
        }
        return bytes;
    }

    internal static void WriteDeltaRuns(short[] deltas, Span<byte> destination)
    {
        int at = 0;
        int outAt = 0;
        while (at < deltas.Length)
        {
            int run = DeltaRunLength(deltas, at);
            short first = deltas[at];
            bool zero = first == 0;
            bool words = DeltaWidth(first) == 2;
            destination[outAt++] = (byte)((zero ? DeltasAreZero : 0) |
                (words ? DeltasAreWords : 0) | (run - 1));
            for (int i = 0; i < run; i++)
            {
                if (zero) continue;
                if (words)
                {
                    BinaryPrimitives.WriteInt16BigEndian(destination.Slice(outAt), deltas[at + i]);
                    outAt += 2;
                }
                else
                {
                    destination[outAt++] = unchecked((byte)deltas[at + i]);
                }
            }
            at += run;
        }
    }

    /// <summary>The run at <paramref name="at"/> extends while every element shares
    /// the minimal payload width (zero / int8 / int16) and the 64-element control
    /// bound holds.</summary>
    private static int DeltaRunLength(short[] deltas, int at)
    {
        int width = DeltaWidth(deltas[at]);
        int limit = Math.Min(at + (DeltaRunCountMask + 1), deltas.Length);
        int end = at + 1;
        while (end < limit && DeltaWidth(deltas[end]) == width) end++;
        return end - at;
    }

    // The packed delta width class: zero, int8 or int16. The signed bounds are
    // compared directly - short.MinValue has no positive short magnitude, so the
    // Math.Abs form would throw instead of classifying the legal int16 value.
    private static int DeltaWidth(short delta) => delta == 0 ? 0 :
        delta is < sbyte.MinValue or > sbyte.MaxValue ? 2 : 1;

    /// <summary>TruncatedData with the convention detail=available bytes,
    /// expected=needed bytes, located at the first byte past the window.</summary>
    private static ReadStatus Truncated(DataLocation location, int failAt, int available,
        int needed, DataField field, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
            location.AtLocal(field, (ulong)failAt), out diagnostic, (ulong)available,
            (ulong)needed);
}

/// <summary>The shared tuple-variation-store walk: the TupleVariationHeader array
/// (dynamic stride) plus the serialized data (shared point numbers first, then
/// one contiguous run per header) - the one implementation behind both the gvar
/// GlyphVariationData blocks and the whole cvar store (card 2; the AGENTS
/// single-reader form). Reserved bits are hard refusals (OQ-3), cvar's embedded
/// peak is mandatory (OQ-4), shared tuple indices and CVT indices are domain
/// checked, and every tuple run decodes inside its declared variationDataSize
/// window with the runs reaching the logical delta count exactly (OQ-9).
/// Positions inside <paramref name="data"/> are block-local; diagnostics report
/// table-absolute positions directly.</summary>
internal static class TupleVariationStoreReader
{
    /// <summary>Walks one store. <paramref name="data"/> spans the store window
    /// (a GlyphVariationData block slice, or the whole cvar table with the
    /// [tvc][dataOffset] header at offset 4). <paramref name="dataOffsetBase"/>
    /// is the origin the dataOffset value is relative to, in
    /// <paramref name="data"/> coordinates: the block start for a
    /// GlyphVariationData (equal to headerStart) and the table start for cvar
    /// (0). When <paramref name="materialize"/> is false the walk
    /// is the structural pass (OQ-1: an all-points tuple gets the window checks
    /// only, its delta runs need the count context); when true every tuple is
    /// materialized and an all-points tuple without context refuses with
    /// UnsupportedOperation. <paramref name="allPointsDeltaCount"/> is the final
    /// logical delta count of count==0 tuples - gvar passes twice the glyph's
    /// outline item count plus the phantom constant (the view owns both), or a
    /// negative value when no topology context exists; cvar passes the explicit
    /// cvtValueCount. <paramref name="cvtValueCount"/> below zero disables the CVT
    /// index domain (gvar); cvar passes the explicit CvtView.ValueCount input
    /// (T0). The shared point number result follows the decode convention:
    /// null = flag clear, empty = all-points marker, non-empty = explicit.</summary>
    internal static ReadStatus ReadStore(ReadOnlySpan<byte> data, int headerStart, int windowEnd,
        int dataOffsetBase, bool isCvar, int axisCount, int sharedTupleCount,
        int allPointsDeltaCount, int cvtValueCount, bool materialize,
        List<TupleVariation>? tuplesOut, DataLocation location, BuildBudget budget,
        out ushort tvcRaw, out int[]? sharedPointNumbers, out int serializedEnd,
        out FontDiagnostic diagnostic)
    {
        tvcRaw = 0;
        sharedPointNumbers = null;
        serializedEnd = 0;
        int at = headerStart;
        if (at + 4 > windowEnd)
            return Truncated(location, at, windowEnd - headerStart,
                (4 + at - headerStart), DataField.Count, out diagnostic);
        tvcRaw = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));
        if ((tvcRaw & TupleVariationCodec.TvcReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, (ulong)(at)), out diagnostic,
                (ulong)(tvcRaw & TupleVariationCodec.TvcReservedMask), 0);
        bool sharedPointsFlag = (tvcRaw & TupleVariationCodec.SharedPointNumbersFlag) != 0;
        int tupleCount = tvcRaw & TupleVariationCodec.TvcCountMask;
        at += 4;
        // The TupleVariationHeader array: the dynamic stride walk (common formats
        // L168-182). Bit classification is validated here; the coordinates are
        // re-read on materialization.
        var indexes = new ushort[tupleCount];
        var sizes = new ushort[tupleCount];
        var headerPositions = new int[tupleCount];
        for (int t = 0; t < tupleCount; t++)
        {
            if (at + 4 > windowEnd)
                return Truncated(location, at, windowEnd - headerStart,
                    (4 + at - headerStart), DataField.Length, out diagnostic);
            ushort variationDataSize = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));
            ushort tupleIndex = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 2, 2));
            if ((tupleIndex & TupleVariationCodec.TupleIndexReservedMask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Flags, (ulong)(at + 2)), out diagnostic,
                    (ulong)(tupleIndex & TupleVariationCodec.TupleIndexReservedMask), 0);
            if (isCvar && (tupleIndex & TupleVariationCodec.EmbeddedPeakFlag) == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Flags, (ulong)(at + 2)), out diagnostic,
                    tupleIndex, TupleVariationCodec.EmbeddedPeakFlag);
            if ((tupleIndex & TupleVariationCodec.EmbeddedPeakFlag) == 0 &&
                (tupleIndex & TupleVariationCodec.TupleIndexMask) >= sharedTupleCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Flags, (ulong)(at + 2)), out diagnostic,
                    (ulong)(tupleIndex & TupleVariationCodec.TupleIndexMask),
                    (ulong)sharedTupleCount);
            int headerSize = TupleVariationCodec.TupleVariationHeaderSize(tupleIndex, axisCount);
            if (at + headerSize > windowEnd)
                return Truncated(location, at, windowEnd - headerStart,
                    (headerSize + at - headerStart), DataField.Length, out diagnostic);
            if (materialize && !budget.Buffer(headerSize, location, out diagnostic))
                return ReadStatus.ResourceLimit;
            if (!budget.Work(1, location, out diagnostic)) return ReadStatus.ResourceLimit;
            headerPositions[t] = at;
            indexes[t] = tupleIndex;
            sizes[t] = variationDataSize;
            at += headerSize;
        }
        // The serialized data starts at dataOffset (relative to the store window
        // start); a value before the header array end would overlap it (card 3:
        // GVD dataOffset < 4+TVH end / cvar dataOffset < 8+TVH end).
        int dataOffset = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(headerStart + 2, 2));
        if (dataOffset < at - headerStart)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                location.AtLocal(DataField.Offset, (ulong)(headerStart + 2)),
                out diagnostic, (ulong)dataOffset, (ulong)(at - headerStart));
        if (dataOffset > windowEnd - dataOffsetBase)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                location.AtLocal(DataField.Offset, (ulong)(headerStart + 2)),
                out diagnostic, (ulong)dataOffset, (ulong)(windowEnd - dataOffsetBase));
        int cursor = dataOffsetBase + dataOffset;
        // Shared point number data precedes the per-tuple runs (legal in gvar and
        // cvar alike - only shared peak TUPLES are gvar-only, card 0/fix 7).
        int[]? sharedRaw = null;
        if (sharedPointsFlag)
        {
            ReadStatus status = TupleVariationCodec.TryDecodePointNumbers(data, cursor, windowEnd,
                location, 0, out sharedRaw, out int consumed, out diagnostic);
            if (status != ReadStatus.Done) return status;
            cursor += consumed;
            if (!budget.Work(sharedRaw?.Length ?? 0, location, out diagnostic))
                return ReadStatus.ResourceLimit;
            if (cvtValueCount >= 0 && sharedRaw is not null)
                foreach (int point in sharedRaw)
                    if (point >= cvtValueCount)
                        return IndexFail(location, cursor - consumed, point,
                            cvtValueCount, out diagnostic);
        }
        // The model convention: null = flag clear, empty = all-points marker,
        // non-empty = explicit (an explicit decode is never empty).
        sharedPointNumbers = sharedPointsFlag ? sharedRaw ?? [] : null;
        // One serialized run per header, contiguous in header order; each run is
        // consumed exactly inside its declared variationDataSize window (the
        // frozen corpus carries zero internal pad - card 0).
        for (int t = 0; t < tupleCount; t++)
        {
            ushort tupleIndex = indexes[t];
            ushort variationDataSize = sizes[t];
            // The header position comes from the dynamic stride walk above - a
            // fixed 4-byte stride would misplace every tuple after one with an
            // embedded peak or intermediate region.
            int headerAt = headerPositions[t];
            int runStart = cursor;
            int runEnd = runStart + variationDataSize;
            if (runEnd > windowEnd)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Length, (ulong)(headerAt)), out diagnostic,
                    (ulong)runEnd, (ulong)windowEnd);
            if (!budget.Work(variationDataSize, location, out diagnostic))
                return ReadStatus.ResourceLimit;
            bool hasPrivate = (tupleIndex & TupleVariationCodec.PrivatePointNumbersFlag) != 0;
            int[]? points;
            int pointBytes = 0;
            if (hasPrivate)
            {
                ReadStatus status = TupleVariationCodec.TryDecodePointNumbers(data, runStart,
                    runEnd, location, 0, out points, out pointBytes, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (materialize && !budget.Buffer(pointBytes, location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                if (cvtValueCount >= 0 && points is not null)
                    foreach (int point in points)
                        if (point >= cvtValueCount)
                            return IndexFail(location, runStart, point,
                                cvtValueCount, out diagnostic);
                if (!budget.Work(points?.Length ?? 0, location, out diagnostic))
                    return ReadStatus.ResourceLimit;
            }
            else if (sharedPointsFlag)
            {
                points = sharedRaw; // null = the shared data is an all-points marker
            }
            else
            {
                points = Array.Empty<int>(); // no point data at all: zero logical deltas
            }
            // The logical delta count: explicit point numbers -> the list length
            // (gvar doubles it for the X/Y columns); count==0 -> the caller's
            // all-points/all-CVTs count; no point data at all -> zero.
            bool allPoints = points is null;
            int logicalCount;
            if (allPoints)
            {
                if (allPointsDeltaCount < 0)
                {
                    if (materialize)
                        return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                            DiagnosticCode.UnsupportedOperation,
                            location.AtLocal(DataField.Flags, (ulong)(headerAt + 2)),
                            out diagnostic, tupleIndex, 0);
                    // The context-free structural pass cannot size the delta runs
                    // of an all-points tuple (OQ-1): the header walk and the run
                    // window above are the checks that remain.
                    cursor = runEnd;
                    continue;
                }
                logicalCount = allPointsDeltaCount;
            }
            else
            {
                logicalCount = points!.Length * (isCvar ? 1 : 2);
            }
            if (materialize && !budget.Buffer((long)logicalCount * 2, location, out diagnostic))
                return ReadStatus.ResourceLimit;
            ReadStatus deltasStatus = TupleVariationCodec.TryDecodeDeltas(data,
                runStart + pointBytes, runEnd, logicalCount, location, 0,
                out short[] deltas, out int deltasConsumed, out diagnostic);
            if (deltasStatus != ReadStatus.Done) return deltasStatus;
            if (materialize)
            {
                int columnCount = logicalCount / (isCvar ? 1 : 2);
                short[] x = new short[columnCount];
                short[] y = new short[isCvar ? 0 : columnCount];
                Array.Copy(deltas, 0, x, 0, columnCount);
                if (!isCvar)
                    Array.Copy(deltas, columnCount, y, 0, columnCount);
                bool embedded = (tupleIndex & TupleVariationCodec.EmbeddedPeakFlag) != 0;
                bool intermediate = (tupleIndex & TupleVariationCodec.IntermediateRegionFlag) != 0;
                var peak = embedded ? ReadCoords(data, headerAt + 4, axisCount) : null;
                int intermediateAt = headerAt + 4 + (embedded ? 2 * axisCount : 0);
                var intermediateStart = intermediate ? ReadCoords(data, intermediateAt, axisCount) : null;
                var intermediateEnd = intermediate
                    ? ReadCoords(data, intermediateAt + 2 * axisCount, axisCount) : null;
                var run = data.Slice(runStart, variationDataSize).ToArray();
                tuplesOut!.Add(new TupleVariation(variationDataSize, tupleIndex, peak,
                    intermediateStart, intermediateEnd, hasPrivate, points, x, y, run,
                    hasPrivate ? pointBytes : 0,
                    variationDataSize - pointBytes - deltasConsumed));
            }
            cursor = runEnd;
        }
        serializedEnd = cursor;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus IndexFail(DataLocation location, int at, int point,
        int cvtValueCount, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            location.AtLocal(DataField.Count, (ulong)at), out diagnostic,
            (ulong)point, (ulong)cvtValueCount);

    private static short[] ReadCoords(ReadOnlySpan<byte> data, int at, int axisCount)
    {
        var coords = new short[axisCount];
        for (int i = 0; i < axisCount; i++)
            coords[i] = BinaryPrimitives.ReadInt16BigEndian(data.Slice(at + 2 * i, 2));
        return coords;
    }

    /// <summary>TruncatedData with the convention detail=available bytes,
    /// expected=needed bytes, located at the first byte past the window.</summary>
    private static ReadStatus Truncated(DataLocation location, int failAt, int available,
        int needed, DataField field, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
            location.AtLocal(field, (ulong)failAt), out diagnostic, (ulong)available,
            (ulong)needed);
}
