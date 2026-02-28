using System.Buffers;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>cvar</c> table.
/// </summary>
/// <remarks>
/// Supports a raw-bytes mode and a structured mode (TupleVariationStore import/build) when axis count and cvt count are available.
/// </remarks>
[OtTableBuilder("cvar", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class CvarTableBuilder : ISfntTableSource
{
    private enum StorageKind : byte
    {
        RawBytes = 0,
        Structured = 1,
    }

    private StorageKind _kind;

    // Raw-bytes mode.
    private ReadOnlyMemory<byte> _data;

    // Structured mode.
    private ushort _axisCount;
    private int _cvtCount;
    private bool _hasSharedPointNumbers;
    private ushort[] _sharedPointNumbers = Array.Empty<ushort>(); // empty == "all points"
    private readonly List<CvarTupleVariation> _variations = new();

    // Built bytes (structured only).
    private byte[]? _built;

    public CvarTableBuilder()
    {
        _kind = StorageKind.RawBytes;
        _data = BuildMinimalTable();
    }

    public bool IsStructured => _kind == StorageKind.Structured;

    public ushort AxisCount
    {
        get
        {
            EnsureStructured();
            return _axisCount;
        }
    }

    public int CvtCount
    {
        get
        {
            EnsureStructured();
            return _cvtCount;
        }
    }

    public int TupleVariationCount
    {
        get
        {
            EnsureStructured();
            return _variations.Count;
        }
    }

    public ReadOnlyMemory<byte> DataBytes
    {
        get
        {
            if (_kind == StorageKind.RawBytes)
                return _data;

            return EnsureBuiltStructured();
        }
    }

    private int ComputeLength()
    {
        if (_kind == StorageKind.RawBytes)
            return _data.Length;

        return EnsureBuiltStructured().Length;
    }

    private uint ComputeDirectoryChecksum()
    {
        if (_kind == StorageKind.RawBytes)
            return OpenTypeChecksum.Compute(_data.Span);

        var built = EnsureBuiltStructured();
        return OpenTypeChecksum.Compute(built.Span);
    }

    private void WriteTable(Stream destination, uint headCheckSumAdjustment)
        => destination.Write(DataBytes.Span);

    partial void OnMarkDirty()
    {
        _built = null;
    }

    public void Clear()
    {
        _kind = StorageKind.RawBytes;
        _data = BuildMinimalTable();
        _variations.Clear();
        _hasSharedPointNumbers = false;
        _sharedPointNumbers = Array.Empty<ushort>();
        MarkDirty();
    }

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 8)
            throw new ArgumentException("cvar table must be at least 8 bytes.", nameof(data));

        _kind = StorageKind.RawBytes;
        _data = data;
        _variations.Clear();
        _hasSharedPointNumbers = false;
        _sharedPointNumbers = Array.Empty<ushort>();
        MarkDirty();
    }

    public void ClearStructured(ushort axisCount, int cvtCount)
    {
        if (axisCount == 0)
            throw new ArgumentOutOfRangeException(nameof(axisCount));
        if (cvtCount < 0)
            throw new ArgumentOutOfRangeException(nameof(cvtCount));

        _kind = StorageKind.Structured;
        _axisCount = axisCount;
        _cvtCount = cvtCount;
        _hasSharedPointNumbers = false;
        _sharedPointNumbers = Array.Empty<ushort>();
        _variations.Clear();
        MarkDirty();
    }

    public void SetSharedPointNumbers(ReadOnlySpan<ushort> points)
    {
        EnsureStructured();

        _hasSharedPointNumbers = true;
        _sharedPointNumbers = points.ToArray();
        MarkDirty();
    }

    public void ClearSharedPointNumbers()
    {
        EnsureStructured();

        if (!_hasSharedPointNumbers && _sharedPointNumbers.Length == 0)
            return;

        _hasSharedPointNumbers = false;
        _sharedPointNumbers = Array.Empty<ushort>();
        MarkDirty();
    }

    public void AddTupleVariation(CvarTupleVariation variation)
    {
        if (variation is null) throw new ArgumentNullException(nameof(variation));
        EnsureStructured();

        ValidateVariation(variation, _axisCount, _cvtCount, _hasSharedPointNumbers, _sharedPointNumbers);
        _variations.Add(variation);
        MarkDirty();
    }

    public static bool TryFrom(CvarTable cvar, out CvarTableBuilder builder)
    {
        var b = new CvarTableBuilder();
        b.SetTableData(cvar.Table.Span.ToArray());
        builder = b;
        return true;
    }

    public static bool TryFrom(SfntFont font, out CvarTableBuilder builder)
    {
        builder = null!;

        if (!font.TryGetCvar(out var cvar))
        {
            builder = new CvarTableBuilder();
            return true;
        }

        if (!font.TryGetFvar(out var fvar))
        {
            var raw = new CvarTableBuilder();
            raw.SetTableData(cvar.Table.Span.ToArray());
            builder = raw;
            return true;
        }

        if (!font.TryGetCvt(out var cvt))
        {
            var raw = new CvarTableBuilder();
            raw.SetTableData(cvar.Table.Span.ToArray());
            builder = raw;
            return true;
        }

        if (!TryFrom(cvar, axisCount: fvar.AxisCount, cvtCount: cvt.ValueCount, out var structured))
        {
            var raw = new CvarTableBuilder();
            raw.SetTableData(cvar.Table.Span.ToArray());
            builder = raw;
            return true;
        }

        builder = structured;
        return true;
    }

    public static bool TryFrom(CvarTable cvar, ushort axisCount, int cvtCount, out CvarTableBuilder builder)
    {
        builder = null!;

        if (axisCount == 0)
            return false;
        if (cvtCount < 0)
            return false;

        if (!cvar.TryGetTupleVariationStore(axisCount, out var store))
            return false;

        if (store.TupleVariationCount > 0x0FFF)
            return false;

        var b = new CvarTableBuilder
        {
            _kind = StorageKind.Structured,
            _axisCount = axisCount,
            _cvtCount = cvtCount,
        };

        if (store.HasSharedPointNumbers)
        {
            int recordEnd = store.RecordOffset + store.RecordLength;
            int sharedPointsOffset = store.OriginOffset + store.OffsetToData;

            if (!PackedPointNumbers.TryDecode(store.Table.Span, sharedPointsOffset, recordEnd, out var pts, out _))
                return false;

            b._hasSharedPointNumbers = true;
            b._sharedPointNumbers = pts; // empty == all points
        }

        int count = store.TupleVariationCount;
        for (int i = 0; i < count; i++)
        {
            if (!store.TryGetTupleVariation(i, out var tv))
                return false;

            if (!tv.HasEmbeddedPeakTuple)
                return false; // structured import requires explicit coordinates for now

            short[] peak = new short[axisCount];
            for (int a = 0; a < axisCount; a++)
            {
                if (!tv.TryGetPeakTupleCoordinate(a, out var coord))
                    return false;
                peak[a] = coord.RawValue;
            }

            short[]? start = null;
            short[]? end = null;
            if (tv.HasIntermediateRegion)
            {
                start = new short[axisCount];
                end = new short[axisCount];
                for (int a = 0; a < axisCount; a++)
                {
                    if (!tv.TryGetIntermediateStartCoordinate(a, out var c0))
                        return false;
                    if (!tv.TryGetIntermediateEndCoordinate(a, out var c1))
                        return false;
                    start[a] = c0.RawValue;
                    end[a] = c1.RawValue;
                }
            }

            if (!tv.TryGetVariationDataSpan(out var varData))
                return false;

            int pos = 0;

            PointSelectionKind selection;
            ushort[] privatePoints = Array.Empty<ushort>();

            if (tv.HasPrivatePointNumbers)
            {
                if (!PackedPointNumbers.TryDecode(varData, 0, varData.Length, out var pts, out int pointsBytes))
                    return false;

                pos = pointsBytes;
                if (pts.Length == 0)
                {
                    selection = PointSelectionKind.AllPoints;
                    privatePoints = Array.Empty<ushort>();
                }
                else
                {
                    selection = PointSelectionKind.Private;
                    privatePoints = pts;
                }
            }
            else
            {
                selection = store.HasSharedPointNumbers ? PointSelectionKind.Shared : PointSelectionKind.AllPoints;
            }

            int deltaCount = selection switch
            {
                PointSelectionKind.Private => privatePoints.Length,
                PointSelectionKind.Shared => b._sharedPointNumbers.Length == 0 ? cvtCount : b._sharedPointNumbers.Length,
                _ => cvtCount
            };

            var deltas = new short[deltaCount];
            if (!PackedDeltas.TryDecode(varData, pos, varData.Length, deltaCount, deltas, out int deltaBytes))
                return false;

            if (pos + deltaBytes != varData.Length)
                return false;

            var variation = new CvarTupleVariation(
                peakTupleRaw: peak,
                intermediateStartRaw: start,
                intermediateEndRaw: end,
                selectionKind: selection,
                privatePointNumbers: privatePoints,
                deltas: deltas);

            ValidateVariation(variation, axisCount, cvtCount, b._hasSharedPointNumbers, b._sharedPointNumbers);
            b._variations.Add(variation);
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private void EnsureStructured()
    {
        if (_kind != StorageKind.Structured)
            throw new InvalidOperationException("This operation requires a structured cvar builder.");
    }

    private ReadOnlyMemory<byte> EnsureBuiltStructured()
    {
        if (_built is not null)
            return _built;

        _built = BuildStructuredTable();
        return _built;
    }

    public bool TryGetTupleVariation(int index, out CvarTupleVariation variation)
    {
        variation = null!;
        EnsureStructured();
        if ((uint)index >= (uint)_variations.Count)
            return false;
        variation = _variations[index];
        return true;
    }

    public void ReplaceTupleVariation(int index, CvarTupleVariation variation)
    {
        if (variation is null) throw new ArgumentNullException(nameof(variation));
        EnsureStructured();
        if ((uint)index >= (uint)_variations.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        ValidateVariation(variation, _axisCount, _cvtCount, _hasSharedPointNumbers, _sharedPointNumbers);
        _variations[index] = variation;
        MarkDirty();
    }

    private byte[] BuildStructuredTable()
    {
        ushort axisCount = _axisCount;
        int cvtCount = _cvtCount;

        if (axisCount == 0)
            throw new InvalidOperationException("Structured cvar builder requires axisCount.");

        // Header:
        // version(4) + tupleVariationCountRaw(2) + offsetToData(2)
        const int cvarHeaderLen = 8;
        const int storeRecordOffset = 4;

        int tupleCount = _variations.Count;
        if (tupleCount > 0x0FFF)
            throw new InvalidOperationException("cvar tupleVariationCount must fit in 12 bits.");

        ushort tupleVariationCountRaw = (ushort)tupleCount;
        if (_hasSharedPointNumbers)
            tupleVariationCountRaw |= 0x8000;

        // Compute tuple headers.
        var payloads = new byte[tupleCount][];
        var headerLengths = new int[tupleCount];

        for (int i = 0; i < tupleCount; i++)
        {
            var v = _variations[i];
            ValidateVariation(v, axisCount, cvtCount, _hasSharedPointNumbers, _sharedPointNumbers);

            int headerLen = 4 + (axisCount * 2);
            if (v.HasIntermediateRegion)
                headerLen = checked(headerLen + (axisCount * 4));
            headerLengths[i] = headerLen;

            payloads[i] = BuildVariationPayload(v, cvtCount, _hasSharedPointNumbers, _sharedPointNumbers);
        }

        int headersTotal = 4;
        for (int i = 0; i < headerLengths.Length; i++)
            headersTotal = checked(headersTotal + headerLengths[i]);

        int dataStartAbs = storeRecordOffset + Align2(headersTotal);
        ushort offsetToData = checked((ushort)dataStartAbs);

        int prefixLen = cvarHeaderLen + Align2(headersTotal);

        int dataLen = 0;
        if (_hasSharedPointNumbers)
        {
            var temp = new ArrayBufferWriter<byte>(Math.Max(8, _sharedPointNumbers.Length * 2));
            PackedPointNumbers.Encode(ref temp, _sharedPointNumbers);
            dataLen = checked(dataLen + temp.WrittenCount);
        }

        for (int i = 0; i < payloads.Length; i++)
            dataLen = checked(dataLen + payloads[i].Length);

        byte[] bytes = new byte[checked(prefixLen + dataLen)];
        var span = bytes.AsSpan();

        BigEndian.WriteUInt32(span, 0, 0x00010000u);
        BigEndian.WriteUInt16(span, 4, tupleVariationCountRaw);
        BigEndian.WriteUInt16(span, 6, offsetToData);

        int headerPos = storeRecordOffset + 4;
        for (int i = 0; i < tupleCount; i++)
        {
            var v = _variations[i];
            byte[] payload = payloads[i];

            BigEndian.WriteUInt16(span, headerPos + 0, checked((ushort)payload.Length));

            ushort tupleIndex = 0x8000; // embedded peak tuple
            if (v.HasIntermediateRegion)
                tupleIndex |= 0x4000;

            bool needsPrivatePointNumbers = v.SelectionKind == PointSelectionKind.Private
                || (v.SelectionKind == PointSelectionKind.AllPoints && _hasSharedPointNumbers && _sharedPointNumbers.Length != 0);

            if (needsPrivatePointNumbers)
                tupleIndex |= 0x2000;

            BigEndian.WriteUInt16(span, headerPos + 2, tupleIndex);
            headerPos += 4;

            for (int a = 0; a < axisCount; a++)
            {
                BigEndian.WriteInt16(span, headerPos, v.PeakTupleRaw[a]);
                headerPos += 2;
            }

            if (v.HasIntermediateRegion)
            {
                for (int a = 0; a < axisCount; a++)
                {
                    BigEndian.WriteInt16(span, headerPos, v.IntermediateStartRaw![a]);
                    headerPos += 2;
                }

                for (int a = 0; a < axisCount; a++)
                {
                    BigEndian.WriteInt16(span, headerPos, v.IntermediateEndRaw![a]);
                    headerPos += 2;
                }
            }
        }

        // Data section.
        int dataPos = offsetToData;
        if (_hasSharedPointNumbers)
        {
            var temp = new ArrayBufferWriter<byte>(Math.Max(8, _sharedPointNumbers.Length * 2));
            PackedPointNumbers.Encode(ref temp, _sharedPointNumbers);
            temp.WrittenSpan.CopyTo(span.Slice(dataPos));
            dataPos = checked(dataPos + temp.WrittenCount);
        }

        for (int i = 0; i < payloads.Length; i++)
        {
            payloads[i].CopyTo(span.Slice(dataPos));
            dataPos = checked(dataPos + payloads[i].Length);
        }

        return bytes;
    }

    private static byte[] BuildVariationPayload(CvarTupleVariation v, int cvtCount, bool hasSharedPointNumbers, ushort[] sharedPoints)
    {
        int deltaCount = v.SelectionKind switch
        {
            PointSelectionKind.Private => v.PrivatePointNumbers.Length,
            PointSelectionKind.Shared => sharedPoints.Length == 0 ? cvtCount : sharedPoints.Length,
            _ => cvtCount
        };

        if (v.Deltas.Length != deltaCount)
            throw new InvalidOperationException("cvar deltas length does not match point selection.");

        var w = new ArrayBufferWriter<byte>(deltaCount + 8);

        bool needsPrivatePointNumbers = v.SelectionKind == PointSelectionKind.Private
            || (v.SelectionKind == PointSelectionKind.AllPoints && hasSharedPointNumbers && sharedPoints.Length != 0);

        if (needsPrivatePointNumbers)
        {
            if (v.SelectionKind == PointSelectionKind.Private)
                PackedPointNumbers.Encode(ref w, v.PrivatePointNumbers);
            else
                PackedPointNumbers.Encode(ref w, ReadOnlySpan<ushort>.Empty); // pointCount=0 => all points
        }

        PackedDeltas.Encode(ref w, v.Deltas);
        return w.WrittenSpan.ToArray();
    }

    private static void ValidateVariation(CvarTupleVariation v, ushort axisCount, int cvtCount, bool hasSharedPointNumbers, ushort[] sharedPoints)
    {
        if (v.PeakTupleRaw.Length != axisCount)
            throw new InvalidOperationException("cvar peak tuple axis count mismatch.");

        if (v.HasIntermediateRegion)
        {
            if (v.IntermediateStartRaw is null || v.IntermediateEndRaw is null)
                throw new InvalidOperationException("cvar intermediate region requires start/end.");
            if (v.IntermediateStartRaw.Length != axisCount || v.IntermediateEndRaw.Length != axisCount)
                throw new InvalidOperationException("cvar intermediate region axis count mismatch.");
        }
        else
        {
            if (v.IntermediateStartRaw is not null || v.IntermediateEndRaw is not null)
                throw new InvalidOperationException("cvar intermediate region must be null when not enabled.");
        }

        if (v.SelectionKind == PointSelectionKind.Shared && !hasSharedPointNumbers)
            throw new InvalidOperationException("cvar variation uses shared points but store has no shared point numbers.");

        if (v.SelectionKind == PointSelectionKind.Private && v.PrivatePointNumbers.Length == 0)
            throw new InvalidOperationException("cvar private point numbers must be non-empty; use AllPoints for pointCount=0 semantics.");

        int deltaCount = v.SelectionKind switch
        {
            PointSelectionKind.Private => v.PrivatePointNumbers.Length,
            PointSelectionKind.Shared => sharedPoints.Length == 0 ? cvtCount : sharedPoints.Length,
            _ => cvtCount
        };

        if (deltaCount < 0)
            throw new InvalidOperationException("Invalid delta count.");

        if (v.Deltas.Length != deltaCount)
            throw new InvalidOperationException("cvar deltas length mismatch.");
    }

    private static int Align2(int offset) => (offset + 1) & ~1;

    private static byte[] BuildMinimalTable()
    {
        // Minimal cvar:
        // version=1.0, tupleVariationCount=0, offsetToData=8 (end of table)
        byte[] bytes = new byte[8];
        var span = bytes.AsSpan();
        BigEndian.WriteUInt32(span, 0, 0x00010000u);
        BigEndian.WriteUInt16(span, 4, 0);
        BigEndian.WriteUInt16(span, 6, 8);
        return bytes;
    }

    public enum PointSelectionKind : byte
    {
        AllPoints = 0,
        Shared = 1,
        Private = 2,
    }

    public sealed class CvarTupleVariation
    {
        public short[] PeakTupleRaw { get; }
        public short[]? IntermediateStartRaw { get; }
        public short[]? IntermediateEndRaw { get; }

        public PointSelectionKind SelectionKind { get; }
        public ushort[] PrivatePointNumbers { get; }

        public short[] Deltas { get; }

        public bool HasIntermediateRegion => IntermediateStartRaw is not null && IntermediateEndRaw is not null;

        public CvarTupleVariation(
            short[] peakTupleRaw,
            short[]? intermediateStartRaw,
            short[]? intermediateEndRaw,
            PointSelectionKind selectionKind,
            ushort[] privatePointNumbers,
            short[] deltas)
        {
            PeakTupleRaw = peakTupleRaw ?? throw new ArgumentNullException(nameof(peakTupleRaw));
            IntermediateStartRaw = intermediateStartRaw;
            IntermediateEndRaw = intermediateEndRaw;
            SelectionKind = selectionKind;
            PrivatePointNumbers = privatePointNumbers ?? Array.Empty<ushort>();
            Deltas = deltas ?? throw new ArgumentNullException(nameof(deltas));
        }
    }
}
