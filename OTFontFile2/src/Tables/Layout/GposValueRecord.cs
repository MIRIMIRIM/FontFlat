using System.Numerics;

namespace OTFontFile2.Tables;

[Flags]
public enum GposValueFormat : ushort
{
    XPlacement = 0x0001,
    YPlacement = 0x0002,
    XAdvance = 0x0004,
    YAdvance = 0x0008,
    XPlacementDevice = 0x0010,
    YPlacementDevice = 0x0020,
    XAdvanceDevice = 0x0040,
    YAdvanceDevice = 0x0080
}

public readonly struct GposValueRecord
{
    private readonly TableSlice _gpos;
    private readonly int _offset;
    private readonly int _posTableOffset;
    private readonly ushort _valueFormat;

    private GposValueRecord(TableSlice gpos, int offset, int posTableOffset, ushort valueFormat)
    {
        _gpos = gpos;
        _offset = offset;
        _posTableOffset = posTableOffset;
        _valueFormat = valueFormat;
    }

    public static int GetByteLength(ushort valueFormat)
        => 2 * BitOperations.PopCount((uint)(valueFormat & 0x00FF));

    public static bool TryCreate(TableSlice gpos, int offset, int posTableOffset, ushort valueFormat, out GposValueRecord record)
    {
        int length = GetByteLength(valueFormat);
        if ((uint)offset > (uint)gpos.Length - (uint)length)
        {
            record = default;
            return false;
        }

        record = new GposValueRecord(gpos, offset, posTableOffset, valueFormat);
        return true;
    }

    public TableSlice Table => _gpos;
    public int Offset => _offset;
    public int PositioningTableOffset => _posTableOffset;

    public ushort ValueFormat => _valueFormat;

    public bool HasXPlacement => (_valueFormat & (ushort)GposValueFormat.XPlacement) != 0;
    public bool HasYPlacement => (_valueFormat & (ushort)GposValueFormat.YPlacement) != 0;
    public bool HasXAdvance => (_valueFormat & (ushort)GposValueFormat.XAdvance) != 0;
    public bool HasYAdvance => (_valueFormat & (ushort)GposValueFormat.YAdvance) != 0;

    public bool HasXPlacementDevice => (_valueFormat & (ushort)GposValueFormat.XPlacementDevice) != 0;
    public bool HasYPlacementDevice => (_valueFormat & (ushort)GposValueFormat.YPlacementDevice) != 0;
    public bool HasXAdvanceDevice => (_valueFormat & (ushort)GposValueFormat.XAdvanceDevice) != 0;
    public bool HasYAdvanceDevice => (_valueFormat & (ushort)GposValueFormat.YAdvanceDevice) != 0;

    private static int GetFieldByteOffset(ushort valueFormat, int fieldBit)
    {
        uint mask = fieldBit == 0 ? 0u : ((1u << fieldBit) - 1u);
        return 2 * BitOperations.PopCount((uint)(valueFormat & (ushort)mask));
    }

    private bool TryGetInt16Field(int fieldBit, out short value)
    {
        value = 0;

        if (((_valueFormat >> fieldBit) & 1) == 0)
            return false;

        int o = _offset + GetFieldByteOffset(_valueFormat, fieldBit);
        if ((uint)o > (uint)_gpos.Length - 2)
            return false;

        value = BigEndian.ReadInt16(_gpos.Span, o);
        return true;
    }

    private bool TryGetOffset16Field(int fieldBit, out ushort offset)
    {
        offset = 0;

        if (((_valueFormat >> fieldBit) & 1) == 0)
            return false;

        int o = _offset + GetFieldByteOffset(_valueFormat, fieldBit);
        if ((uint)o > (uint)_gpos.Length - 2)
            return false;

        offset = BigEndian.ReadUInt16(_gpos.Span, o);
        return true;
    }

    public bool TryGetXPlacement(out short xPlacement) => TryGetInt16Field(fieldBit: 0, out xPlacement);
    public bool TryGetYPlacement(out short yPlacement) => TryGetInt16Field(fieldBit: 1, out yPlacement);
    public bool TryGetXAdvance(out short xAdvance) => TryGetInt16Field(fieldBit: 2, out xAdvance);
    public bool TryGetYAdvance(out short yAdvance) => TryGetInt16Field(fieldBit: 3, out yAdvance);

    public bool TryGetXPlacementDeviceOffset(out ushort deviceOffset) => TryGetOffset16Field(fieldBit: 4, out deviceOffset);
    public bool TryGetYPlacementDeviceOffset(out ushort deviceOffset) => TryGetOffset16Field(fieldBit: 5, out deviceOffset);
    public bool TryGetXAdvanceDeviceOffset(out ushort deviceOffset) => TryGetOffset16Field(fieldBit: 6, out deviceOffset);
    public bool TryGetYAdvanceDeviceOffset(out ushort deviceOffset) => TryGetOffset16Field(fieldBit: 7, out deviceOffset);

    public bool TryGetXPlacementDeviceTableOffset(out int tableOffset)
    {
        tableOffset = 0;
        if (!TryGetXPlacementDeviceOffset(out ushort rel) || rel == 0)
            return false;
        int offset = _posTableOffset + rel;
        if ((uint)offset >= (uint)_gpos.Length)
            return false;
        tableOffset = offset;
        return true;
    }

    public bool TryGetYPlacementDeviceTableOffset(out int tableOffset)
    {
        tableOffset = 0;
        if (!TryGetYPlacementDeviceOffset(out ushort rel) || rel == 0)
            return false;
        int offset = _posTableOffset + rel;
        if ((uint)offset >= (uint)_gpos.Length)
            return false;
        tableOffset = offset;
        return true;
    }

    public bool TryGetXAdvanceDeviceTableOffset(out int tableOffset)
    {
        tableOffset = 0;
        if (!TryGetXAdvanceDeviceOffset(out ushort rel) || rel == 0)
            return false;
        int offset = _posTableOffset + rel;
        if ((uint)offset >= (uint)_gpos.Length)
            return false;
        tableOffset = offset;
        return true;
    }

    public bool TryGetYAdvanceDeviceTableOffset(out int tableOffset)
    {
        tableOffset = 0;
        if (!TryGetYAdvanceDeviceOffset(out ushort rel) || rel == 0)
            return false;
        int offset = _posTableOffset + rel;
        if ((uint)offset >= (uint)_gpos.Length)
            return false;
        tableOffset = offset;
        return true;
    }
}
