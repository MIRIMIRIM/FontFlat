using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Common "tuple variation store" record format used by tables like <c>gvar</c> (glyph variation data)
/// and <c>cvar</c> (cvt variations).
/// </summary>
[OtSubTable(4, GenerateTryCreate = false, GenerateStorage = false)]
[OtField("TupleVariationCountRaw", OtFieldKind.UInt16, 0)]
[OtField("OffsetToData", OtFieldKind.UInt16, 2)]
public readonly partial struct TupleVariationStore
{
    private readonly TableSlice _table;
    private readonly int _recordLength;
    private readonly int _originOffset;
    private readonly int _offset;
    private readonly ushort _axisCount;

    private TupleVariationStore(TableSlice table, int recordOffset, int recordLength, int originOffset, ushort axisCount)
    {
        _table = table;
        _recordLength = recordLength;
        _originOffset = originOffset;
        _offset = recordOffset;
        _axisCount = axisCount;
    }

    public static bool TryCreate(TableSlice table, int recordOffset, int recordLength, int originOffset, ushort axisCount, out TupleVariationStore store)
    {
        store = default;

        if (recordLength < 4)
            return false;

        if ((uint)recordOffset > (uint)table.Length - (uint)recordLength)
            return false;

        if ((uint)originOffset > (uint)table.Length)
            return false;

        var data = table.Span;
        ushort tupleVariationCountRaw = BigEndian.ReadUInt16(data, recordOffset);
        ushort offsetToData = BigEndian.ReadUInt16(data, recordOffset + 2);

        if (axisCount == 0 && (tupleVariationCountRaw & 0x0FFF) != 0)
            return false;

        int recordEnd = recordOffset + recordLength;

        int dataStartAbs = originOffset + offsetToData;
        if (dataStartAbs < recordOffset || dataStartAbs > recordEnd)
            return false;

        int headersStart = recordOffset + 4;
        if (headersStart > recordEnd)
            return false;

        store = new TupleVariationStore(table, recordOffset, recordLength, originOffset, axisCount);
        return true;
    }

    public int RecordLength => _recordLength;
    public int OriginOffset => _originOffset;
    public int RecordOffset => _offset;

    public ushort AxisCount => _axisCount;

    public ushort TupleVariationCount => (ushort)(TupleVariationCountRaw & 0x0FFF);
    public bool HasSharedPointNumbers => (TupleVariationCountRaw & 0x8000) != 0;

    public bool TryGetSharedPointNumbersByteLength(out int byteLength)
    {
        byteLength = 0;

        if (!HasSharedPointNumbers)
            return true;

        int recordEnd = _offset + _recordLength;
        int dataStartAbs = _originOffset + OffsetToData;
        return TryGetPackedPointNumbersByteLength(_table.Span, dataStartAbs, recordEnd, out byteLength);
    }

    public bool TryGetTupleVariation(int index, out TupleVariation variation)
    {
        variation = default;

        ushort count = TupleVariationCount;
        if ((uint)index >= count)
            return false;

        int recordEnd = _offset + _recordLength;
        int headerPos = _offset + 4;

        int dataPos = _originOffset + OffsetToData;
        if (HasSharedPointNumbers)
        {
            if (!TryGetPackedPointNumbersByteLength(_table.Span, dataPos, recordEnd, out int sharedLen))
                return false;

            dataPos = checked(dataPos + sharedLen);
            if (dataPos > recordEnd)
                return false;
        }

        var data = _table.Span;
        for (int i = 0; i < count; i++)
        {
            if ((uint)headerPos > (uint)recordEnd - 4)
                return false;

            ushort variationDataSizeU16 = BigEndian.ReadUInt16(data, headerPos + 0);
            ushort tupleIndexRaw = BigEndian.ReadUInt16(data, headerPos + 2);
            int headerStart = headerPos;
            headerPos += 4;

            bool hasEmbeddedPeakTuple = (tupleIndexRaw & 0x8000) != 0;
            bool hasIntermediateRegion = (tupleIndexRaw & 0x4000) != 0;
            bool hasPrivatePointNumbers = (tupleIndexRaw & 0x2000) != 0;
            ushort sharedTupleIndex = (ushort)(tupleIndexRaw & 0x0FFF);

            int peakTupleOffset = -1;
            int intermediateStartOffset = -1;
            int intermediateEndOffset = -1;

            int coordPos = headerPos;

            if (hasEmbeddedPeakTuple)
            {
                int bytes = checked(_axisCount * 2);
                if ((uint)coordPos > (uint)recordEnd - (uint)bytes)
                    return false;

                peakTupleOffset = coordPos;
                coordPos += bytes;
            }

            if (hasIntermediateRegion)
            {
                int tupleBytes = checked(_axisCount * 2);
                int bytes = checked(tupleBytes * 2);
                if ((uint)coordPos > (uint)recordEnd - (uint)bytes)
                    return false;

                intermediateStartOffset = coordPos;
                intermediateEndOffset = coordPos + tupleBytes;
                coordPos += bytes;
            }

            headerPos = coordPos;
            int headerLength = headerPos - headerStart;

            int variationDataSize = variationDataSizeU16;
            if ((uint)dataPos > (uint)recordEnd)
                return false;
            if ((uint)variationDataSize > (uint)(recordEnd - dataPos))
                return false;

            int tupleDataOffset = dataPos;
            dataPos = checked(dataPos + variationDataSize);

            if (i == index)
            {
                variation = new TupleVariation(
                    _table,
                    _offset,
                    recordEnd,
                    _axisCount,
                    headerStart,
                    headerLength,
                    tupleDataOffset,
                    variationDataSize,
                    tupleIndexRaw,
                    sharedTupleIndex,
                    hasEmbeddedPeakTuple,
                    hasIntermediateRegion,
                    hasPrivatePointNumbers,
                    peakTupleOffset,
                    intermediateStartOffset,
                    intermediateEndOffset);
                return true;
            }
        }

        return false;
    }

    internal static bool TryGetPackedPointNumbersByteLength(ReadOnlySpan<byte> data, int offset, int limit, out int byteLength)
    {
        byteLength = 0;

        if (offset < 0 || limit < 0)
            return false;
        if (offset > limit)
            return false;
        if ((uint)limit > (uint)data.Length)
            return false;

        if (offset == limit)
            return false;

        int pos = offset;
        byte first = data[pos++];

        int pointCount;
        if ((first & 0x80) != 0)
        {
            if (pos >= limit)
                return false;

            pointCount = ((first & 0x7F) << 8) | data[pos++];
        }
        else
        {
            pointCount = first;
        }

        if (pointCount == 0)
        {
            byteLength = pos - offset;
            return true;
        }

        int remaining = pointCount;
        while (remaining > 0)
        {
            if (pos >= limit)
                return false;

            byte runHeader = data[pos++];
            bool isWord = (runHeader & 0x80) != 0;
            int runLength = (runHeader & 0x7F) + 1;

            int valueSize = isWord ? 2 : 1;
            int runBytes = checked(runLength * valueSize);
            if (pos > limit - runBytes)
                return false;

            pos += runBytes;
            remaining -= runLength;
        }

        if (remaining != 0)
            return false;

        byteLength = pos - offset;
        return true;
    }

    public readonly struct TupleVariation
    {
        private readonly TableSlice _table;
        private readonly int _recordOffset;
        private readonly int _recordEnd;
        private readonly ushort _axisCount;
        private readonly int _headerOffset;
        private readonly int _headerLength;
        private readonly int _dataOffset;
        private readonly int _dataLength;
        private readonly ushort _tupleIndexRaw;
        private readonly ushort _sharedTupleIndex;
        private readonly bool _hasEmbeddedPeakTuple;
        private readonly bool _hasIntermediateRegion;
        private readonly bool _hasPrivatePointNumbers;
        private readonly int _peakTupleOffset;
        private readonly int _intermediateStartOffset;
        private readonly int _intermediateEndOffset;

        internal TupleVariation(
            TableSlice table,
            int recordOffset,
            int recordEnd,
            ushort axisCount,
            int headerOffset,
            int headerLength,
            int dataOffset,
            int dataLength,
            ushort tupleIndexRaw,
            ushort sharedTupleIndex,
            bool hasEmbeddedPeakTuple,
            bool hasIntermediateRegion,
            bool hasPrivatePointNumbers,
            int peakTupleOffset,
            int intermediateStartOffset,
            int intermediateEndOffset)
        {
            _table = table;
            _recordOffset = recordOffset;
            _recordEnd = recordEnd;
            _axisCount = axisCount;
            _headerOffset = headerOffset;
            _headerLength = headerLength;
            _dataOffset = dataOffset;
            _dataLength = dataLength;
            _tupleIndexRaw = tupleIndexRaw;
            _sharedTupleIndex = sharedTupleIndex;
            _hasEmbeddedPeakTuple = hasEmbeddedPeakTuple;
            _hasIntermediateRegion = hasIntermediateRegion;
            _hasPrivatePointNumbers = hasPrivatePointNumbers;
            _peakTupleOffset = peakTupleOffset;
            _intermediateStartOffset = intermediateStartOffset;
            _intermediateEndOffset = intermediateEndOffset;
        }

        public ushort AxisCount => _axisCount;

        public ushort TupleIndexRaw => _tupleIndexRaw;
        public ushort SharedTupleIndex => _sharedTupleIndex;

        public bool HasEmbeddedPeakTuple => _hasEmbeddedPeakTuple;
        public bool HasIntermediateRegion => _hasIntermediateRegion;
        public bool HasPrivatePointNumbers => _hasPrivatePointNumbers;

        public int HeaderOffset => _headerOffset;
        public int HeaderLength => _headerLength;

        public int DataOffset => _dataOffset;
        public int DataLength => _dataLength;

        public bool TryGetVariationDataSpan(out ReadOnlySpan<byte> data)
        {
            data = default;

            if ((uint)_dataOffset > (uint)_table.Length - (uint)_dataLength)
                return false;

            data = _table.Span.Slice(_dataOffset, _dataLength);
            return true;
        }

        public bool TryGetPeakTupleCoordinate(int axisIndex, out F2Dot14 coordinate)
        {
            coordinate = default;

            if (!_hasEmbeddedPeakTuple)
                return false;

            if ((uint)axisIndex >= _axisCount)
                return false;

            int offset = checked(_peakTupleOffset + (axisIndex * 2));
            if ((uint)offset > (uint)_table.Length - 2)
                return false;
            if (offset < _recordOffset || offset + 2 > _recordEnd)
                return false;

            coordinate = new F2Dot14(BigEndian.ReadInt16(_table.Span, offset));
            return true;
        }

        public bool TryGetIntermediateStartCoordinate(int axisIndex, out F2Dot14 coordinate)
        {
            coordinate = default;

            if (!_hasIntermediateRegion)
                return false;

            if ((uint)axisIndex >= _axisCount)
                return false;

            int offset = checked(_intermediateStartOffset + (axisIndex * 2));
            if ((uint)offset > (uint)_table.Length - 2)
                return false;
            if (offset < _recordOffset || offset + 2 > _recordEnd)
                return false;

            coordinate = new F2Dot14(BigEndian.ReadInt16(_table.Span, offset));
            return true;
        }

        public bool TryGetIntermediateEndCoordinate(int axisIndex, out F2Dot14 coordinate)
        {
            coordinate = default;

            if (!_hasIntermediateRegion)
                return false;

            if ((uint)axisIndex >= _axisCount)
                return false;

            int offset = checked(_intermediateEndOffset + (axisIndex * 2));
            if ((uint)offset > (uint)_table.Length - 2)
                return false;
            if (offset < _recordOffset || offset + 2 > _recordEnd)
                return false;

            coordinate = new F2Dot14(BigEndian.ReadInt16(_table.Span, offset));
            return true;
        }
    }
}
