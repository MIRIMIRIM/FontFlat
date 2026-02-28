namespace OTFontFile2.Writing;

internal struct OpenTypeChecksumAccumulator
{
    private uint _sum;
    private uint _tail;
    private int _tailLength;

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        if (_tailLength != 0)
        {
            int need = 4 - _tailLength;
            if (data.Length < need)
            {
                for (int i = 0; i < data.Length; i++)
                    AppendByte(data[i]);
                return;
            }

            for (int i = 0; i < need; i++)
                AppendByte(data[i]);

            data = data.Slice(need);
        }

        int end = data.Length & ~3;
        for (int i = 0; i < end; i += 4)
            _sum += BigEndian.ReadUInt32(data, i);

        int remaining = data.Length - end;
        if (remaining != 0)
        {
            uint tail = 0;
            for (int i = 0; i < remaining; i++)
                tail = (tail << 8) | data[end + i];

            _tail = tail;
            _tailLength = remaining;
        }
    }

    public void AppendByte(byte b)
    {
        _tail = (_tail << 8) | b;
        _tailLength++;
        if (_tailLength == 4)
        {
            _sum += _tail;
            _tail = 0;
            _tailLength = 0;
        }
    }

    public uint FinalizeChecksum()
    {
        if (_tailLength != 0)
        {
            _sum += _tail << (8 * (4 - _tailLength));
            _tail = 0;
            _tailLength = 0;
        }

        return _sum;
    }
}

