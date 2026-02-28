namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for OpenType Device tables (and VariationIndex tables when <c>deltaFormat</c> is 0x8000).
/// </summary>
public sealed class DeviceTableBuilder
{
    private enum DeviceKind : byte
    {
        Unset = 0,
        Delta = 1,
        VariationIndex = 2
    }

    private DeviceKind _kind;

    private ushort _startSize;
    private ushort _endSize;
    private ushort _deltaFormat;
    private sbyte[]? _deltas;
    private VarIdx _varIdx;

    private bool _dirty = true;
    private byte[]? _built;

    public bool IsSet => _kind != DeviceKind.Unset;

    public bool IsVariationIndex => _kind == DeviceKind.VariationIndex;

    /// <summary>Meaning depends on <see cref="IsVariationIndex"/>.</summary>
    public ushort StartSize => _kind == DeviceKind.VariationIndex ? _varIdx.OuterIndex : _startSize;

    /// <summary>Meaning depends on <see cref="IsVariationIndex"/>.</summary>
    public ushort EndSize => _kind == DeviceKind.VariationIndex ? _varIdx.InnerIndex : _endSize;

    public ushort DeltaFormat => _kind switch
    {
        DeviceKind.Unset => 0,
        DeviceKind.VariationIndex => 0x8000,
        _ => _deltaFormat
    };

    public void Clear()
    {
        _kind = DeviceKind.Unset;
        _startSize = 0;
        _endSize = 0;
        _deltaFormat = 0;
        _deltas = null;
        _varIdx = default;
        MarkDirty();
    }

    public void SetVariationIndex(VarIdx varIdx)
    {
        _kind = DeviceKind.VariationIndex;
        _varIdx = varIdx;
        _deltas = null;
        MarkDirty();
    }

    public void SetVariationIndex(ushort outerIndex, ushort innerIndex)
        => SetVariationIndex(new VarIdx(outerIndex, innerIndex));

    public void SetDeltas(ushort startSize, ReadOnlySpan<sbyte> deltas)
    {
        if (deltas.Length == 0)
            throw new ArgumentException("Device deltas must be non-empty.", nameof(deltas));

        ushort endSize = checked((ushort)(startSize + (deltas.Length - 1)));
        SetDeltas(startSize, endSize, deltas);
    }

    public void SetDeltas(ushort startSize, ushort endSize, ReadOnlySpan<sbyte> deltas, ushort deltaFormat = 0)
    {
        if (endSize < startSize)
            throw new ArgumentOutOfRangeException(nameof(endSize), "Device endSize must be >= startSize.");

        int count = (endSize - startSize) + 1;
        if (deltas.Length != count)
            throw new ArgumentException("Device deltas length must match endSize-startSize+1.", nameof(deltas));

        if (deltaFormat == 0)
            deltaFormat = ChooseSmallestDeltaFormat(deltas);

        ValidateDeltaFormat(deltaFormat);
        ValidateDeltaRange(deltaFormat, deltas);

        _kind = DeviceKind.Delta;
        _startSize = startSize;
        _endSize = endSize;
        _deltaFormat = deltaFormat;
        _deltas = deltas.ToArray();
        MarkDirty();
    }

    public byte[] ToArray()
    {
        EnsureBuilt();
        return _built!;
    }

    public ReadOnlyMemory<byte> ToMemory() => EnsureBuilt();

    public static bool TryFrom(DeviceTable device, out DeviceTableBuilder builder)
    {
        builder = null!;

        ushort deltaFormat = device.DeltaFormat;
        if (deltaFormat == 0x8000)
        {
            if (!device.TryGetVarIdx(out var varIdx))
                return false;

            var b = new DeviceTableBuilder();
            b.SetVariationIndex(varIdx);
            builder = b;
            return true;
        }

        if (deltaFormat is not (1 or 2 or 3))
            return false;

        ushort start = device.StartSize;
        ushort end = device.EndSize;
        if (end < start)
            return false;

        int count = (end - start) + 1;
        var deltas = new sbyte[count];

        for (int i = 0; i < count; i++)
        {
            ushort ppem = checked((ushort)(start + i));
            if (!device.TryGetDelta(ppem, out sbyte delta))
                return false;
            deltas[i] = delta;
        }

        var builderInstance = new DeviceTableBuilder();
        builderInstance.SetDeltas(start, end, deltas, deltaFormat: deltaFormat);
        builder = builderInstance;
        return true;
    }

    private void MarkDirty()
    {
        _dirty = true;
        _built = null;
    }

    private ReadOnlyMemory<byte> EnsureBuilt()
    {
        if (!_dirty && _built is not null)
            return _built;

        _built = BuildBytes();
        _dirty = false;
        return _built;
    }

    private byte[] BuildBytes()
    {
        if (_kind == DeviceKind.Unset)
            throw new InvalidOperationException("DeviceTableBuilder is not configured. Call SetDeltas(...) or SetVariationIndex(...).");

        if (_kind == DeviceKind.VariationIndex)
        {
            byte[] bytes = new byte[6];
            var bytesSpan = bytes.AsSpan();
            BigEndian.WriteUInt16(bytesSpan, 0, _varIdx.OuterIndex);
            BigEndian.WriteUInt16(bytesSpan, 2, _varIdx.InnerIndex);
            BigEndian.WriteUInt16(bytesSpan, 4, 0x8000);
            return bytes;
        }

        if (_deltas is null)
            throw new InvalidOperationException("DeviceTableBuilder delta mode requires deltas.");

        ushort start = _startSize;
        ushort end = _endSize;
        if (end < start)
            throw new InvalidOperationException("DeviceTableBuilder startSize must be <= endSize.");

        int count = (end - start) + 1;
        if (_deltas.Length != count)
            throw new InvalidOperationException("DeviceTableBuilder deltas length does not match startSize/endSize.");

        ushort deltaFormat = _deltaFormat;
        ValidateDeltaFormat(deltaFormat);
        ValidateDeltaRange(deltaFormat, _deltas);

        int bitsPerValue = deltaFormat switch
        {
            1 => 2,
            2 => 4,
            3 => 8,
            _ => throw new InvalidOperationException("Invalid device deltaFormat.")
        };

        int valuesPerWord = 16 / bitsPerValue;
        int wordCount = (count + valuesPerWord - 1) / valuesPerWord;

        byte[] table = new byte[checked(6 + (wordCount * 2))];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, start);
        BigEndian.WriteUInt16(span, 2, end);
        BigEndian.WriteUInt16(span, 4, deltaFormat);

        int mask = (1 << bitsPerValue) - 1;
        int minDelta = -(1 << (bitsPerValue - 1));
        int maxDelta = (1 << (bitsPerValue - 1)) - 1;

        int valueIndex = 0;
        for (int w = 0; w < wordCount; w++)
        {
            int word = 0;
            for (int i = 0; i < valuesPerWord; i++)
            {
                int raw = 0;
                if (valueIndex < count)
                {
                    int d = _deltas[valueIndex];
                    if ((uint)(d - minDelta) > (uint)(maxDelta - minDelta))
                        throw new InvalidOperationException("Device delta is out of range for the chosen deltaFormat.");

                    raw = d & mask;
                }

                int shift = (valuesPerWord - 1 - i) * bitsPerValue;
                word |= raw << shift;
                valueIndex++;
            }

            BigEndian.WriteUInt16(span, 6 + (w * 2), (ushort)word);
        }

        return table;
    }

    private static ushort ChooseSmallestDeltaFormat(ReadOnlySpan<sbyte> deltas)
    {
        sbyte min = 0;
        sbyte max = 0;

        for (int i = 0; i < deltas.Length; i++)
        {
            sbyte d = deltas[i];
            if (d < min) min = d;
            if (d > max) max = d;
        }

        if (min >= -2 && max <= 1)
            return 1;
        if (min >= -8 && max <= 7)
            return 2;
        return 3;
    }

    private static void ValidateDeltaFormat(ushort deltaFormat)
    {
        if (deltaFormat is not (1 or 2 or 3))
            throw new ArgumentOutOfRangeException(nameof(deltaFormat), "Device deltaFormat must be 1, 2, or 3.");
    }

    private static void ValidateDeltaRange(ushort deltaFormat, ReadOnlySpan<sbyte> deltas)
    {
        int bitsPerValue = deltaFormat switch
        {
            1 => 2,
            2 => 4,
            3 => 8,
            _ => 0
        };

        if (bitsPerValue == 0)
            throw new ArgumentOutOfRangeException(nameof(deltaFormat), "Device deltaFormat must be 1, 2, or 3.");

        int minDelta = -(1 << (bitsPerValue - 1));
        int maxDelta = (1 << (bitsPerValue - 1)) - 1;

        for (int i = 0; i < deltas.Length; i++)
        {
            int d = deltas[i];
            if ((uint)(d - minDelta) > (uint)(maxDelta - minDelta))
                throw new ArgumentException("Device deltas contain values not representable by the chosen deltaFormat.", nameof(deltas));
        }
    }
}
