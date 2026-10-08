using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>Owned cvar 1.0 table: the whole-table materialization of the one
/// versioned tuple variation store (the frozen witnesses are bounded at 283
/// bytes, card 2). The two explicit authorities the caller supplied are kept as
/// context: the fvar axis count (the TVH stride authority) and the CVT value
/// count that bounds every CVT index (T0). Tuples always carry embedded peaks
/// (mandatory, OQ-4) and a single delta column. The exact table bytes are kept
/// so the write side re-emits unedited tuples verbatim (OQ-7) and the value
/// edits splice bytes in place.</summary>
public sealed class CvarTable
{
    private readonly byte[] _tableBytes;

    internal CvarTable(int axisCount, int cvtValueCount, ushort tupleVariationCountRaw,
        int[]? sharedPointNumbers, List<TupleVariation> tuples, DataLocation location,
        int consumedLength, byte[] tableBytes)
    {
        AxisCount = axisCount;
        CvtValueCount = cvtValueCount;
        TupleVariationCountRaw = tupleVariationCountRaw;
        SharedPointNumbers = sharedPointNumbers;
        Tuples = tuples;
        Location = location;
        ConsumedLength = consumedLength;
        _tableBytes = tableBytes;
    }

    public int AxisCount { get; }

    /// <summary>The explicit CVT value count the CVT indices are bounded by
    /// (floor(cvtLength/2) from CvtView.ValueCount).</summary>
    public int CvtValueCount { get; }

    /// <summary>The packed field: SHARED_POINT_NUMBERS in the high bit, reserved
    /// 0x7000 (refused when set), count in the low 12 bits.</summary>
    public ushort TupleVariationCountRaw { get; }

    public int TupleCount => TupleVariationCountRaw & TupleVariationCodec.TvcCountMask;

    public bool SharesPointNumbers =>
        (TupleVariationCountRaw & TupleVariationCodec.SharedPointNumbersFlag) != 0;

    /// <summary>null = flag clear; empty = the shared data is the all-CVTs
    /// marker; non-empty = the explicit shared CVT indices.</summary>
    public IReadOnlyList<int>? SharedPointNumbers { get; }

    public IReadOnlyList<TupleVariation> Tuples { get; }

    public DataLocation Location { get; }

    public int ConsumedLength { get; }

    /// <summary>The exact wire bytes this model carries (source for read-built
    /// models, canonical emission for parts-built ones); the compiler re-emits
    /// the store tail from here (OQ-7 verbatim) and the edits splice into it.</summary>
    internal byte[] RawTableBytes => _tableBytes;

    /// <summary>Replaces one tuple's delta column (cvar: the single column). The
    /// private point number bytes are preserved byte-for-byte; only the delta run
    /// repacks (OQ-7). All-CVTs tuples (no point data of their own) replace the
    /// whole delta column - the new length defines the store's logical count and
    /// must match the CVT authority at read-back.</summary>
    public ReadStatus TryWithTupleDeltas(int tupleOrdinal, short[] deltas,
        out CvarTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = PrepareEdit(tupleOrdinal, out int headerAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (deltas is null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 0), out diagnostic, 0, 1);
        if (!budget.Input(_tableBytes.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int runStart = RunStart(headerAt);
        int runSize = BinaryPrimitives.ReadUInt16BigEndian(_tableBytes.AsSpan(headerAt, 2));
        bool hasPrivate = (TupleIndexAt(headerAt) & TupleVariationCodec.PrivatePointNumbersFlag) != 0;
        int pointBytes = 0;
        if (hasPrivate)
        {
            status = TupleVariationCodec.TryDecodePointNumbers(_tableBytes, runStart,
                runStart + runSize, Location, 0, out _, out pointBytes, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        int newSize = pointBytes + TupleVariationCodec.MeasureDeltaRuns(deltas);
        if (newSize > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, (ulong)headerAt, tupleOrdinal), out diagnostic,
                (ulong)newSize, (ulong)ushort.MaxValue);
        var run = new byte[newSize];
        _tableBytes.AsSpan(runStart, pointBytes).CopyTo(run);
        TupleVariationCodec.WriteDeltaRuns(deltas, run.AsSpan(pointBytes));
        status = Splice(headerAt, runStart, runSize, run, tupleOrdinal, out var bytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        return Reread(bytes, out updated, out diagnostic);
    }

    /// <summary>Replaces one tuple's embedded peak coordinates (every cvar tuple
    /// carries one, so there is no shared-index refusal wing here - the mandatory
    /// flag is a read invariant, OQ-4).</summary>
    public ReadStatus TryWithTuplePeakCoords(int tupleOrdinal, short[] coords,
        out CvarTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = PrepareEdit(tupleOrdinal, out int headerAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (coords is null || coords.Length != AxisCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 0), out diagnostic,
                (ulong)(coords?.Length ?? 0), (ulong)AxisCount);
        if (!budget.Input(_tableBytes.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var bytes = (byte[])_tableBytes.Clone();
        for (int i = 0; i < AxisCount; i++)
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(headerAt + 4 + i * 2, 2),
                coords[i]);
        return Reread(bytes, out updated, out diagnostic);
    }

    /// <summary>Replaces one tuple's intermediate region start/end coordinates;
    /// tuples without the INTERMEDIATE_REGION flag refuse (OQ-8).</summary>
    public ReadStatus TryWithTupleIntermediateCoords(int tupleOrdinal, short[] startCoords,
        short[] endCoords, out CvarTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = PrepareEdit(tupleOrdinal, out int headerAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ushort tupleIndex = TupleIndexAt(headerAt);
        if ((tupleIndex & TupleVariationCodec.IntermediateRegionFlag) == 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Flags, (ulong)(headerAt + 2), tupleOrdinal),
                out diagnostic, tupleIndex, TupleVariationCodec.IntermediateRegionFlag);
        if (startCoords is null || endCoords is null || startCoords.Length != AxisCount ||
            endCoords.Length != AxisCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 0), out diagnostic,
                (ulong)(startCoords?.Length ?? 0), (ulong)AxisCount);
        if (!budget.Input(_tableBytes.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var bytes = (byte[])_tableBytes.Clone();
        for (int i = 0; i < AxisCount; i++)
        {
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(headerAt + 4 + i * 2, 2),
                startCoords[i]);
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(headerAt + 4 + AxisCount * 2 + i * 2, 2),
                endCoords[i]);
        }
        return Reread(bytes, out updated, out diagnostic);
    }

    // The splice re-validates through the typed read: the materialized tuple
    // list stays consistent with the bytes (fail-closed on any inconsistency).
    private ReadStatus Reread(byte[] bytes, out CvarTable? model, out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = CvarView.TryOpen(bytes, out var view, out diagnostic, Location);
        if (status != ReadStatus.Done) return status;
        return view.TryReadTable(AxisCount, CvtValueCount, out model, out diagnostic);
    }

    private ReadStatus PrepareEdit(int tupleOrdinal, out int headerAt, out FontDiagnostic diagnostic)
    {
        // The locator wants the [tvc][dataOffset] pair position (4), the same
        // coordinate the store walk uses - the TVH array follows it.
        var headers = TupleVariationCodec.LocateTupleVariationHeaders(_tableBytes,
            4, _tableBytes.Length, AxisCount,
            TupleVariationCountRaw & TupleVariationCodec.TvcCountMask);
        if (headers is null || tupleOrdinal < 0 || tupleOrdinal >= headers.Length)
        {
            headerAt = 0;
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, CvarLayout.HeaderLength, tupleOrdinal),
                out diagnostic, unchecked((ulong)(long)tupleOrdinal),
                (ulong)(headers?.Length ?? 0));
        }
        headerAt = headers[tupleOrdinal];
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ushort TupleIndexAt(int headerAt) =>
        BinaryPrimitives.ReadUInt16BigEndian(_tableBytes.AsSpan(headerAt + 2, 2));

    private int RunStart(int headerAt)
    {
        int dataOffset = BinaryPrimitives.ReadUInt16BigEndian(_tableBytes.AsSpan(6, 2));
        int cursor = dataOffset;
        int at = CvarLayout.HeaderLength;
        while (at < headerAt)
        {
            cursor += BinaryPrimitives.ReadUInt16BigEndian(_tableBytes.AsSpan(at, 2));
            at += TupleVariationCodec.TupleVariationHeaderSize(TupleIndexAt(at), AxisCount);
        }
        return cursor;
    }

    private ReadStatus Splice(int headerAt, int runStart, int oldRunSize, byte[] newRun,
        int tupleOrdinal, out byte[] bytes, out FontDiagnostic diagnostic)
    {
        int delta = newRun.Length - oldRunSize;
        var buffer = new byte[_tableBytes.Length + delta];
        _tableBytes.AsSpan(0, headerAt).CopyTo(buffer);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(headerAt, 2),
            (ushort)newRun.Length);
        _tableBytes.AsSpan(headerAt + 2, runStart - headerAt - 2).CopyTo(
            buffer.AsSpan(headerAt + 2));
        newRun.CopyTo(buffer, runStart);
        _tableBytes.AsSpan(runStart + oldRunSize).CopyTo(buffer.AsSpan(runStart + newRun.Length));
        bytes = buffer;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
