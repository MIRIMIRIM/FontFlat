using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables.Variation;

/// <summary>The cvar root writer (HvarTableCompiler shape): one planned, atomically
/// published whole-table emission. The canonical layout is the store's own
/// narrative order - header(8), the TupleVariationHeader array, then the
/// serialized data (shared CVT point numbers first, then one run per header).
/// Unedited tables re-emit their bytes verbatim behind the re-derived header
/// (the frozen witnesses are canonical, so the round trip is byte-identical);
/// edited tuples splice in place, and parts-built stores encode at TryCreate.
/// The Offset16 dataOffset is the single finite width refusal (the 64K window,
/// card 1); every check completes before any byte is written.</summary>
public static class CvarTableCompiler
{
    /// <summary>Explicit parts factory for the synthetic compile path (T4-02
    /// precedent): the authorities are re-answered here - every tuple carries an
    /// embedded peak (mandatory, OQ-4) with exactly <paramref name="axisCount"/>
    /// F2DOT14 values, every CVT index stays below
    /// <paramref name="cvtValueCount"/> (T0), the packed count stays inside the
    /// 12-bit domain, and the delta column agrees with the point set (once per
    /// CVT index - cvar's single column, card 0). All-CVTs tuples (no point
    /// data) must carry exactly cvtValueCount deltas so the read-back under the
    /// same authority replays them.</summary>
    public static ReadStatus TryCreate(int axisCount, int cvtValueCount,
        int[]? sharedPointNumbers, IReadOnlyList<TupleVariation> tuples,
        out CvarTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(tuples);
        var budget = new BuildBudget(limits);
        location = location == default ? DataLocation.Anonymous(CvarLayout.Tag) : location;
        if (axisCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic, (ulong)axisCount, 0);
        if (cvtValueCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic, (ulong)cvtValueCount, 0);
        if (tuples.Count > TupleVariationCodec.TvcCountMask)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, CvarLayout.TupleVariationCountAt),
                out diagnostic, (ulong)tuples.Count,
                (ulong)TupleVariationCodec.TvcCountMask);
        foreach (var tuple in tuples)
        {
            if ((tuple.TupleIndex & TupleVariationCodec.EmbeddedPeakFlag) == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Flags, 0), out diagnostic, tuple.TupleIndex,
                    (ulong)TupleVariationCodec.EmbeddedPeakFlag);
            if (tuple.PeakCoords is null || tuple.PeakCoords.Length != axisCount)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, 0), out diagnostic,
                    (ulong)(tuple.PeakCoords?.Length ?? 0), (ulong)axisCount);
            if (tuple.HasIntermediate && (tuple.IntermediateStartCoords is null ||
                    tuple.IntermediateEndCoords is null ||
                    tuple.IntermediateStartCoords.Length != axisCount ||
                    tuple.IntermediateEndCoords.Length != axisCount))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, 0), out diagnostic, 0, (ulong)axisCount);
            if ((tuple.TupleIndex & TupleVariationCodec.TupleIndexReservedMask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Flags, 0), out diagnostic,
                    (ulong)(tuple.TupleIndex & TupleVariationCodec.TupleIndexReservedMask), 0);
            if (tuple.YDeltas.Count != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, 0), out diagnostic,
                    (ulong)tuple.YDeltas.Count, 0); // cvar: the single X column
            if (tuple.PointNumbers is { Count: > 0 })
            {
                if (tuple.XDeltas.Count != tuple.PointNumbers.Count)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Count, 0), out diagnostic,
                        (ulong)tuple.XDeltas.Count, (ulong)tuple.PointNumbers.Count);
                foreach (int index in tuple.PointNumbers)
                    if (index >= cvtValueCount)
                        return ReadWire.Fail(ReadStatus.InvalidIndex,
                            DiagnosticCode.IndexOutOfRange, location.AtLocal(DataField.Count, 0),
                            out diagnostic, (ulong)index, (ulong)cvtValueCount);
            }
            else if (tuple.PointNumbers is null && tuple.XDeltas.Count != cvtValueCount)
            {
                // All-CVTs: the logical count is the authority the caller
                // supplied - a different encoded count would not replay.
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, 0), out diagnostic,
                    (ulong)tuple.XDeltas.Count, (ulong)cvtValueCount);
            }
        }
        if (sharedPointNumbers is { Length: > 0 })
            foreach (int index in sharedPointNumbers)
                if (index >= cvtValueCount)
                    return ReadWire.Fail(ReadStatus.InvalidIndex,
                        DiagnosticCode.IndexOutOfRange, location.AtLocal(DataField.Count, 0),
                        out diagnostic, (ulong)index, (ulong)cvtValueCount);
        if (!budget.Work(tuples.Count + (sharedPointNumbers?.Length ?? 0), location,
                out diagnostic)) return ReadStatus.ResourceLimit;
        // Encode the canonical store: header + TVH array + serialized data.
        int sharedLength = sharedPointNumbers is null ? 0 :
            sharedPointNumbers.Length == 0 ? 1 :
            TupleVariationCodec.MeasurePointNumberValues(sharedPointNumbers);
        int headersLength = 4;
        foreach (var tuple in tuples)
            headersLength += TupleVariationCodec.TupleVariationHeaderSize(tuple.TupleIndex,
                axisCount);
        long runLength = 0;
        foreach (var tuple in tuples)
            runLength += MeasureRun(tuple);
        // headersLength counts the [tvc][dataOffset] pair from the store start, so the
        // serialized data begins at headerStart + the four-byte pair + the TVH strides;
        // the shared point block sits at that offset (it is not skipped) and extends the
        // table length.
        int dataOffset = CvarLayout.HeaderLength - 4 + headersLength;
        if (dataOffset > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Offset, CvarLayout.DataOffsetAt), out diagnostic,
                (ulong)dataOffset, (ulong)ushort.MaxValue);
        int length = dataOffset + sharedLength + (int)runLength;
        if (!budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), CvarLayout.Version10);
        ushort tvc = (ushort)((sharedPointNumbers is null ? 0 :
            TupleVariationCodec.SharedPointNumbersFlag) | tuples.Count);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4, 2), tvc);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6, 2), (ushort)dataOffset);
        int at = CvarLayout.HeaderLength;
        foreach (var tuple in tuples)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at, 2), (ushort)MeasureRun(tuple));
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 2, 2), tuple.TupleIndex);
            int payload = at + 4;
            WriteCoords(bytes.AsSpan(payload), tuple.PeakCoords!);
            payload += 2 * axisCount;
            if (tuple.IntermediateStartCoords is not null)
            {
                WriteCoords(bytes.AsSpan(payload), tuple.IntermediateStartCoords);
                WriteCoords(bytes.AsSpan(payload + 2 * axisCount), tuple.IntermediateEndCoords!);
            }
            at += TupleVariationCodec.TupleVariationHeaderSize(tuple.TupleIndex, axisCount);
        }
        if (sharedPointNumbers is not null)
        {
            if (sharedPointNumbers.Length == 0)
            {
                bytes[at++] = 0;
            }
            else
            {
                TupleVariationCodec.WritePointNumberValues(bytes.AsSpan(at), sharedPointNumbers);
                at += TupleVariationCodec.MeasurePointNumberValues(sharedPointNumbers);
            }
        }
        foreach (var tuple in tuples)
        {
            WriteRun(tuple, bytes.AsSpan(at));
            at += MeasureRun(tuple);
        }
        var reread = CvarView.TryOpen(bytes, out var view, out diagnostic, location);
        if (reread != ReadStatus.Done) return reread;
        return view.TryReadTable(axisCount, cvtValueCount, out model, out diagnostic, budget);
    }

    private static int MeasureRun(TupleVariation tuple)
    {
        int pointBytes = 0;
        if (tuple.HasPrivatePoints)
            pointBytes = tuple.PointNumbers is null ? 1 :
                TupleVariationCodec.MeasurePointNumberValues((int[])tuple.PointNumbers);
        return pointBytes + TupleVariationCodec.MeasureDeltaRuns((short[])tuple.XDeltas);
    }

    private static void WriteRun(TupleVariation tuple, Span<byte> destination)
    {
        int at = 0;
        if (tuple.HasPrivatePoints)
        {
            if (tuple.PointNumbers is null)
            {
                destination[at++] = 0;
            }
            else
            {
                var points = (int[])tuple.PointNumbers;
                TupleVariationCodec.WritePointNumberValues(destination, points);
                at += TupleVariationCodec.MeasurePointNumberValues(points);
            }
        }
        var x = (short[])tuple.XDeltas;
        TupleVariationCodec.WriteDeltaRuns(x, destination.Slice(at));
    }

    private static void WriteCoords(Span<byte> destination, short[] coords)
    {
        for (int i = 0; i < coords.Length; i++)
            BinaryPrimitives.WriteInt16BigEndian(destination.Slice(i * 2, 2), coords[i]);
    }

    /// <summary>Compiles the table into a fresh canonical byte array; the whole
    /// plan (lengths, fixups, budget admission) completes before allocation.</summary>
    public static ReadStatus TryCompile(CvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(CvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Writes the canonical emission into the destination; every plan
    /// admission completes before the capacity check (the destination is
    /// untouched on refusal).</summary>
    public static ReadStatus TryWrite(CvarTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall,
                DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Wire length of the canonical emission; budget-free arithmetic for
    /// callers that only measure.</summary>
    internal static int Measure(CvarTable model) => model.RawTableBytes.Length;

    // The single canonical candidate: the header re-derives from the model's
    // counts (the tail is the model's own bytes - edited tuples are already
    // spliced), so the only admission is the Offset16 window plus the budget.
    private static ReadStatus TryPlan(CvarTable model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = 0;
        int dataOffset = BinaryPrimitives.ReadUInt16BigEndian(
            model.RawTableBytes.AsSpan(6, 2));
        if (dataOffset > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, CvarLayout.DataOffsetAt),
                out diagnostic, (ulong)dataOffset, (ulong)ushort.MaxValue);
        int measured = Measure(model);
        if (!budget.Work(measured + model.Tuples.Count, model.Location, out diagnostic) ||
            !budget.Output(measured, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        length = measured;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void WritePlanned(CvarTable model, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(CvarView.TryWriteVersion(destination,
            CvarLayout.Version10, out _, location));
        CoverageTableCompiler.RequireWritten(CvarView.TryWriteTupleVariationCount(destination,
            model.TupleVariationCountRaw, out _, location));
        CoverageTableCompiler.RequireWritten(CvarView.TryWriteDataOffset(destination,
            BinaryPrimitives.ReadUInt16BigEndian(model.RawTableBytes.AsSpan(6, 2)),
            out _, location));
        model.RawTableBytes.AsSpan(CvarLayout.HeaderLength).CopyTo(
            destination.Slice(CvarLayout.HeaderLength));
    }
}
