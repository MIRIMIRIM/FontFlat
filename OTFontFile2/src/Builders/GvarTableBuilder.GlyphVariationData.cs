using System.Buffers;

namespace OTFontFile2.Tables;

public sealed partial class GvarTableBuilder
{
    public sealed class GlyphVariationData
    {
        private readonly ushort _axisCount;
        private readonly int _pointCountWithPhantoms;
        private bool _hasSharedPointNumbers;
        private ushort[] _sharedPointNumbers = Array.Empty<ushort>(); // empty == all points
        private readonly List<TupleVariation> _variations = new();

        public ushort AxisCount => _axisCount;
        public int PointCountWithPhantoms => _pointCountWithPhantoms;

        public bool HasSharedPointNumbers => _hasSharedPointNumbers;
        public ushort[] SharedPointNumbers => _sharedPointNumbers;

        public int TupleVariationCount => _variations.Count;
        public IReadOnlyList<TupleVariation> Variations => _variations;

        public GlyphVariationData(ushort axisCount, int pointCountWithPhantoms)
        {
            if (axisCount == 0) throw new ArgumentOutOfRangeException(nameof(axisCount));
            if (pointCountWithPhantoms < 0) throw new ArgumentOutOfRangeException(nameof(pointCountWithPhantoms));

            _axisCount = axisCount;
            _pointCountWithPhantoms = pointCountWithPhantoms;
        }

        public void SetSharedPointNumbers(ReadOnlySpan<ushort> sharedPointNumbers)
        {
            _hasSharedPointNumbers = true;
            _sharedPointNumbers = sharedPointNumbers.ToArray();
        }

        public void ClearSharedPointNumbers()
        {
            _hasSharedPointNumbers = false;
            _sharedPointNumbers = Array.Empty<ushort>();
        }

        public void AddTupleVariation(TupleVariation variation)
        {
            if (variation is null) throw new ArgumentNullException(nameof(variation));
            ValidateTupleVariation(variation, _axisCount, _pointCountWithPhantoms, _hasSharedPointNumbers, _sharedPointNumbers);
            _variations.Add(variation);
        }

        public bool TryGetTupleVariation(int index, out TupleVariation variation)
        {
            variation = null!;
            if ((uint)index >= (uint)_variations.Count)
                return false;
            variation = _variations[index];
            return true;
        }

        public void ReplaceTupleVariation(int index, TupleVariation variation)
        {
            if (variation is null) throw new ArgumentNullException(nameof(variation));
            if ((uint)index >= (uint)_variations.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            ValidateTupleVariation(variation, _axisCount, _pointCountWithPhantoms, _hasSharedPointNumbers, _sharedPointNumbers);
            _variations[index] = variation;
        }

        public byte[] BuildGlyphVariationDataRecord()
        {
            int tupleCount = _variations.Count;
            if (tupleCount > 0x0FFF)
                throw new InvalidOperationException("TupleVariationCount must fit in 12 bits.");

            ushort tupleVariationCountRaw = (ushort)tupleCount;
            if (_hasSharedPointNumbers)
                tupleVariationCountRaw |= 0x8000;

            var payloads = new byte[tupleCount][];
            var headerLengths = new int[tupleCount];

            for (int i = 0; i < tupleCount; i++)
            {
                var tv = _variations[i];
                ValidateTupleVariation(tv, _axisCount, _pointCountWithPhantoms, _hasSharedPointNumbers, _sharedPointNumbers);

                int headerLen = 4 + (_axisCount * 2);
                if (tv.HasIntermediateRegion)
                    headerLen = checked(headerLen + (_axisCount * 4));
                headerLengths[i] = headerLen;

                payloads[i] = BuildTupleVariationPayload(tv, _pointCountWithPhantoms, _hasSharedPointNumbers, _sharedPointNumbers);
            }

            int headersTotal = 4;
            for (int i = 0; i < headerLengths.Length; i++)
                headersTotal = checked(headersTotal + headerLengths[i]);

            int offsetToData = Align2(headersTotal);
            if (offsetToData > ushort.MaxValue)
                throw new InvalidOperationException("gvar offsetToData must fit in uint16.");

            int dataLen = 0;
            byte[] sharedPointsBytes = Array.Empty<byte>();
            if (_hasSharedPointNumbers)
            {
                var tmp = new ArrayBufferWriter<byte>(Math.Max(8, _sharedPointNumbers.Length * 2));
                PackedPointNumbers.Encode(ref tmp, _sharedPointNumbers);
                sharedPointsBytes = tmp.WrittenSpan.ToArray();
                dataLen = checked(dataLen + sharedPointsBytes.Length);
            }

            for (int i = 0; i < payloads.Length; i++)
                dataLen = checked(dataLen + payloads[i].Length);

            byte[] record = new byte[checked(offsetToData + dataLen)];
            var span = record.AsSpan();

            BigEndian.WriteUInt16(span, 0, tupleVariationCountRaw);
            BigEndian.WriteUInt16(span, 2, checked((ushort)offsetToData));

            int headerPos = 4;
            for (int i = 0; i < tupleCount; i++)
            {
                var tv = _variations[i];
                byte[] payload = payloads[i];

                BigEndian.WriteUInt16(span, headerPos + 0, checked((ushort)payload.Length));

                ushort tupleIndex = 0x8000; // embedded peak tuple required here
                if (tv.HasIntermediateRegion)
                    tupleIndex |= 0x4000;

                bool needsPrivatePointsFlag = tv.SelectionKind == PointSelectionKind.Private
                    || (tv.SelectionKind == PointSelectionKind.AllPoints && _hasSharedPointNumbers && _sharedPointNumbers.Length != 0);

                if (needsPrivatePointsFlag)
                    tupleIndex |= 0x2000;

                BigEndian.WriteUInt16(span, headerPos + 2, tupleIndex);
                headerPos += 4;

                for (int a = 0; a < _axisCount; a++)
                {
                    BigEndian.WriteInt16(span, headerPos, tv.PeakTupleRaw[a]);
                    headerPos += 2;
                }

                if (tv.HasIntermediateRegion)
                {
                    for (int a = 0; a < _axisCount; a++)
                    {
                        BigEndian.WriteInt16(span, headerPos, tv.IntermediateStartRaw![a]);
                        headerPos += 2;
                    }

                    for (int a = 0; a < _axisCount; a++)
                    {
                        BigEndian.WriteInt16(span, headerPos, tv.IntermediateEndRaw![a]);
                        headerPos += 2;
                    }
                }
            }

            int dataPos = offsetToData;
            if (_hasSharedPointNumbers && sharedPointsBytes.Length != 0)
            {
                sharedPointsBytes.CopyTo(span.Slice(dataPos, sharedPointsBytes.Length));
                dataPos = checked(dataPos + sharedPointsBytes.Length);
            }

            for (int i = 0; i < payloads.Length; i++)
            {
                payloads[i].CopyTo(span.Slice(dataPos, payloads[i].Length));
                dataPos = checked(dataPos + payloads[i].Length);
            }

            return record;
        }

        public static bool TryParse(ushort axisCount, int pointCountWithPhantoms, ReadOnlySpan<byte> glyphVariationDataRecord, out GlyphVariationData data)
            => TryParse(axisCount, pointCountWithPhantoms, glyphVariationDataRecord, sharedTupleCount: 0, sharedTuplesBytes: ReadOnlySpan<byte>.Empty, out data);

        public static bool TryParse(
            ushort axisCount,
            int pointCountWithPhantoms,
            ReadOnlySpan<byte> glyphVariationDataRecord,
            ushort sharedTupleCount,
            ReadOnlySpan<byte> sharedTuplesBytes,
            out GlyphVariationData data)
        {
            data = null!;

            if (axisCount == 0)
                return false;
            if (pointCountWithPhantoms < 0)
                return false;

            if (glyphVariationDataRecord.Length < 4)
                return false;

            ushort tupleVariationCountRaw = BigEndian.ReadUInt16(glyphVariationDataRecord, 0);
            int tupleCount = tupleVariationCountRaw & 0x0FFF;
            bool hasSharedPoints = (tupleVariationCountRaw & 0x8000) != 0;
            ushort offsetToData = BigEndian.ReadUInt16(glyphVariationDataRecord, 2);

            if (offsetToData < 4 || offsetToData > glyphVariationDataRecord.Length)
                return false;

            int headerPos = 4;
            int dataPos = offsetToData;

            ushort[] sharedPoints = Array.Empty<ushort>();
            if (hasSharedPoints)
            {
                if (!PackedPointNumbers.TryDecode(glyphVariationDataRecord, dataPos, glyphVariationDataRecord.Length, out sharedPoints, out int bytesRead))
                    return false;
                dataPos = checked(dataPos + bytesRead);
            }

            var result = new GlyphVariationData(axisCount, pointCountWithPhantoms);
            if (hasSharedPoints)
                result.SetSharedPointNumbers(sharedPoints);

            for (int i = 0; i < tupleCount; i++)
            {
                if ((uint)headerPos > (uint)glyphVariationDataRecord.Length - 4)
                    return false;

                ushort variationDataSize = BigEndian.ReadUInt16(glyphVariationDataRecord, headerPos + 0);
                ushort tupleIndexRaw = BigEndian.ReadUInt16(glyphVariationDataRecord, headerPos + 2);
                headerPos += 4;

                bool embeddedPeak = (tupleIndexRaw & 0x8000) != 0;
                bool hasIntermediate = (tupleIndexRaw & 0x4000) != 0;
                bool hasPrivatePoints = (tupleIndexRaw & 0x2000) != 0;
                ushort sharedTupleIndex = (ushort)(tupleIndexRaw & 0x0FFF);

                var peak = new short[axisCount];
                if (embeddedPeak)
                {
                    for (int a = 0; a < axisCount; a++)
                    {
                        if ((uint)headerPos > (uint)glyphVariationDataRecord.Length - 2)
                            return false;
                        peak[a] = BigEndian.ReadInt16(glyphVariationDataRecord, headerPos);
                        headerPos += 2;
                    }
                }
                else
                {
                    if (sharedTupleCount == 0)
                        return false;
                    if (sharedTupleIndex >= sharedTupleCount)
                        return false;

                    int tupleBytes = checked(axisCount * 2);
                    int start = checked(sharedTupleIndex * tupleBytes);
                    if ((uint)start > (uint)sharedTuplesBytes.Length - (uint)tupleBytes)
                        return false;

                    for (int a = 0; a < axisCount; a++)
                        peak[a] = BigEndian.ReadInt16(sharedTuplesBytes, start + (a * 2));
                }

                short[]? intermediateStart = null;
                short[]? intermediateEnd = null;
                if (hasIntermediate)
                {
                    intermediateStart = new short[axisCount];
                    intermediateEnd = new short[axisCount];

                    for (int a = 0; a < axisCount; a++)
                    {
                        if ((uint)headerPos > (uint)glyphVariationDataRecord.Length - 2)
                            return false;
                        intermediateStart[a] = BigEndian.ReadInt16(glyphVariationDataRecord, headerPos);
                        headerPos += 2;
                    }

                    for (int a = 0; a < axisCount; a++)
                    {
                        if ((uint)headerPos > (uint)glyphVariationDataRecord.Length - 2)
                            return false;
                        intermediateEnd[a] = BigEndian.ReadInt16(glyphVariationDataRecord, headerPos);
                        headerPos += 2;
                    }
                }

                if ((uint)dataPos > (uint)glyphVariationDataRecord.Length - variationDataSize)
                    return false;

                ReadOnlySpan<byte> varData = glyphVariationDataRecord.Slice(dataPos, variationDataSize);
                dataPos = checked(dataPos + variationDataSize);

                int p = 0;
                PointSelectionKind selection;
                ushort[] privatePoints = Array.Empty<ushort>();

                if (hasPrivatePoints)
                {
                    if (!PackedPointNumbers.TryDecode(varData, 0, varData.Length, out var pts, out int pointsBytes))
                        return false;

                    p = pointsBytes;
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
                    selection = hasSharedPoints ? PointSelectionKind.Shared : PointSelectionKind.AllPoints;
                }

                int selectedCount = selection switch
                {
                    PointSelectionKind.Private => privatePoints.Length,
                    PointSelectionKind.Shared => sharedPoints.Length == 0 ? pointCountWithPhantoms : sharedPoints.Length,
                    _ => pointCountWithPhantoms
                };

                if (selectedCount < 0)
                    return false;

                var xDeltas = new short[selectedCount];
                if (!PackedDeltas.TryDecode(varData, p, varData.Length, selectedCount, xDeltas, out int xBytes))
                    return false;
                p = checked(p + xBytes);

                var yDeltas = new short[selectedCount];
                if (!PackedDeltas.TryDecode(varData, p, varData.Length, selectedCount, yDeltas, out int yBytes))
                    return false;
                p = checked(p + yBytes);

                if (p != varData.Length)
                    return false;

                var tv = new TupleVariation(
                    peakTupleRaw: peak,
                    intermediateStartRaw: intermediateStart,
                    intermediateEndRaw: intermediateEnd,
                    selectionKind: selection,
                    privatePointNumbers: privatePoints,
                    xDeltas: xDeltas,
                    yDeltas: yDeltas);

                ValidateTupleVariation(tv, axisCount, pointCountWithPhantoms, hasSharedPoints, sharedPoints);
                result._variations.Add(tv);
            }

            data = result;
            return true;
        }

        private static byte[] BuildTupleVariationPayload(TupleVariation tv, int pointCountWithPhantoms, bool hasSharedPoints, ushort[] sharedPoints)
        {
            int selectedCount = tv.SelectionKind switch
            {
                PointSelectionKind.Private => tv.PrivatePointNumbers.Length,
                PointSelectionKind.Shared => sharedPoints.Length == 0 ? pointCountWithPhantoms : sharedPoints.Length,
                _ => pointCountWithPhantoms
            };

            if (tv.XDeltas.Length != selectedCount || tv.YDeltas.Length != selectedCount)
                throw new InvalidOperationException("TupleVariation delta length does not match point selection.");

            bool needsPrivatePointsFlag = tv.SelectionKind == PointSelectionKind.Private
                || (tv.SelectionKind == PointSelectionKind.AllPoints && hasSharedPoints && sharedPoints.Length != 0);

            var w = new ArrayBufferWriter<byte>(selectedCount * 3 + 16);

            if (needsPrivatePointsFlag)
            {
                if (tv.SelectionKind == PointSelectionKind.Private)
                    PackedPointNumbers.Encode(ref w, tv.PrivatePointNumbers);
                else
                    PackedPointNumbers.Encode(ref w, ReadOnlySpan<ushort>.Empty); // pointCount=0 => all points
            }

            PackedDeltas.Encode(ref w, tv.XDeltas);
            PackedDeltas.Encode(ref w, tv.YDeltas);

            return w.WrittenSpan.ToArray();
        }

        private static void ValidateTupleVariation(TupleVariation tv, ushort axisCount, int pointCountWithPhantoms, bool hasSharedPoints, ushort[] sharedPoints)
        {
            if (tv.PeakTupleRaw.Length != axisCount)
                throw new InvalidOperationException("TupleVariation peak tuple axis count mismatch.");

            if (tv.HasIntermediateRegion)
            {
                if (tv.IntermediateStartRaw is null || tv.IntermediateEndRaw is null)
                    throw new InvalidOperationException("Intermediate region requires start/end arrays.");
                if (tv.IntermediateStartRaw.Length != axisCount || tv.IntermediateEndRaw.Length != axisCount)
                    throw new InvalidOperationException("Intermediate region axis count mismatch.");
            }
            else
            {
                if (tv.IntermediateStartRaw is not null || tv.IntermediateEndRaw is not null)
                    throw new InvalidOperationException("Intermediate start/end must be null when intermediate region is not enabled.");
            }

            if (tv.SelectionKind == PointSelectionKind.Shared && !hasSharedPoints)
                throw new InvalidOperationException("TupleVariation uses shared points but the record has no shared point numbers.");

            if (tv.SelectionKind == PointSelectionKind.Private)
            {
                if (tv.PrivatePointNumbers.Length == 0)
                    throw new InvalidOperationException("Private point numbers must be non-empty; use AllPoints for pointCount=0 semantics.");

                ushort prev = 0;
                for (int i = 0; i < tv.PrivatePointNumbers.Length; i++)
                {
                    ushort p = tv.PrivatePointNumbers[i];
                    if (p >= pointCountWithPhantoms)
                        throw new InvalidOperationException("Private point number is out of range.");
                    if (i != 0 && p <= prev)
                        throw new InvalidOperationException("Private point numbers must be strictly increasing.");
                    prev = p;
                }
            }

            int selectedCount = tv.SelectionKind switch
            {
                PointSelectionKind.Private => tv.PrivatePointNumbers.Length,
                PointSelectionKind.Shared => sharedPoints.Length == 0 ? pointCountWithPhantoms : sharedPoints.Length,
                _ => pointCountWithPhantoms
            };

            if (tv.XDeltas.Length != selectedCount || tv.YDeltas.Length != selectedCount)
                throw new InvalidOperationException("TupleVariation delta length mismatch.");
        }

        private static int Align2(int offset) => (offset + 1) & ~1;
    }

    public enum PointSelectionKind : byte
    {
        AllPoints = 0,
        Shared = 1,
        Private = 2,
    }

    public sealed class TupleVariation
    {
        public short[] PeakTupleRaw { get; }
        public short[]? IntermediateStartRaw { get; }
        public short[]? IntermediateEndRaw { get; }

        public PointSelectionKind SelectionKind { get; }
        public ushort[] PrivatePointNumbers { get; }

        public short[] XDeltas { get; }
        public short[] YDeltas { get; }

        public bool HasIntermediateRegion => IntermediateStartRaw is not null && IntermediateEndRaw is not null;

        public TupleVariation(
            short[] peakTupleRaw,
            short[]? intermediateStartRaw,
            short[]? intermediateEndRaw,
            PointSelectionKind selectionKind,
            ushort[] privatePointNumbers,
            short[] xDeltas,
            short[] yDeltas)
        {
            PeakTupleRaw = peakTupleRaw ?? throw new ArgumentNullException(nameof(peakTupleRaw));
            IntermediateStartRaw = intermediateStartRaw;
            IntermediateEndRaw = intermediateEndRaw;
            SelectionKind = selectionKind;
            PrivatePointNumbers = privatePointNumbers ?? Array.Empty<ushort>();
            XDeltas = xDeltas ?? throw new ArgumentNullException(nameof(xDeltas));
            YDeltas = yDeltas ?? throw new ArgumentNullException(nameof(yDeltas));
        }
    }
}
