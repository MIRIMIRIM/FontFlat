namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS ValueRecords.
/// </summary>
public sealed class GposValueRecordBuilder
{
    private bool _hasXPlacement;
    private bool _hasYPlacement;
    private bool _hasXAdvance;
    private bool _hasYAdvance;

    private short _xPlacement;
    private short _yPlacement;
    private short _xAdvance;
    private short _yAdvance;

    private DeviceTableBuilder? _xPlacementDevice;
    private DeviceTableBuilder? _yPlacementDevice;
    private DeviceTableBuilder? _xAdvanceDevice;
    private DeviceTableBuilder? _yAdvanceDevice;

    public bool HasXPlacement => _hasXPlacement;
    public bool HasYPlacement => _hasYPlacement;
    public bool HasXAdvance => _hasXAdvance;
    public bool HasYAdvance => _hasYAdvance;

    public short XPlacement
    {
        get => _xPlacement;
        set
        {
            _xPlacement = value;
            _hasXPlacement = true;
        }
    }

    public short YPlacement
    {
        get => _yPlacement;
        set
        {
            _yPlacement = value;
            _hasYPlacement = true;
        }
    }

    public short XAdvance
    {
        get => _xAdvance;
        set
        {
            _xAdvance = value;
            _hasXAdvance = true;
        }
    }

    public short YAdvance
    {
        get => _yAdvance;
        set
        {
            _yAdvance = value;
            _hasYAdvance = true;
        }
    }

    public DeviceTableBuilder? XPlacementDevice
    {
        get => _xPlacementDevice;
        set => _xPlacementDevice = value;
    }

    public DeviceTableBuilder? YPlacementDevice
    {
        get => _yPlacementDevice;
        set => _yPlacementDevice = value;
    }

    public DeviceTableBuilder? XAdvanceDevice
    {
        get => _xAdvanceDevice;
        set => _xAdvanceDevice = value;
    }

    public DeviceTableBuilder? YAdvanceDevice
    {
        get => _yAdvanceDevice;
        set => _yAdvanceDevice = value;
    }

    public void Clear()
    {
        _hasXPlacement = false;
        _hasYPlacement = false;
        _hasXAdvance = false;
        _hasYAdvance = false;

        _xPlacement = 0;
        _yPlacement = 0;
        _xAdvance = 0;
        _yAdvance = 0;

        _xPlacementDevice = null;
        _yPlacementDevice = null;
        _xAdvanceDevice = null;
        _yAdvanceDevice = null;
    }

    public ushort GetValueFormat()
    {
        ushort fmt = 0;

        if (_hasXPlacement) fmt |= (ushort)GposValueFormat.XPlacement;
        if (_hasYPlacement) fmt |= (ushort)GposValueFormat.YPlacement;
        if (_hasXAdvance) fmt |= (ushort)GposValueFormat.XAdvance;
        if (_hasYAdvance) fmt |= (ushort)GposValueFormat.YAdvance;

        if (_xPlacementDevice is not null) fmt |= (ushort)GposValueFormat.XPlacementDevice;
        if (_yPlacementDevice is not null) fmt |= (ushort)GposValueFormat.YPlacementDevice;
        if (_xAdvanceDevice is not null) fmt |= (ushort)GposValueFormat.XAdvanceDevice;
        if (_yAdvanceDevice is not null) fmt |= (ushort)GposValueFormat.YAdvanceDevice;

        return fmt;
    }

    internal void WriteTo(OTFontFile2.OffsetWriter writer, ushort valueFormat, int posTableBaseOffset, DeviceTablePool deviceTables)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        if (deviceTables is null) throw new ArgumentNullException(nameof(deviceTables));

        if ((valueFormat & (ushort)GposValueFormat.XPlacement) != 0)
            writer.WriteInt16(_hasXPlacement ? _xPlacement : (short)0);

        if ((valueFormat & (ushort)GposValueFormat.YPlacement) != 0)
            writer.WriteInt16(_hasYPlacement ? _yPlacement : (short)0);

        if ((valueFormat & (ushort)GposValueFormat.XAdvance) != 0)
            writer.WriteInt16(_hasXAdvance ? _xAdvance : (short)0);

        if ((valueFormat & (ushort)GposValueFormat.YAdvance) != 0)
            writer.WriteInt16(_hasYAdvance ? _yAdvance : (short)0);

        if ((valueFormat & (ushort)GposValueFormat.XPlacementDevice) != 0)
        {
            if (_xPlacementDevice is null)
                writer.WriteUInt16(0);
            else
                deviceTables.WriteOffset16(writer, _xPlacementDevice, posTableBaseOffset);
        }

        if ((valueFormat & (ushort)GposValueFormat.YPlacementDevice) != 0)
        {
            if (_yPlacementDevice is null)
                writer.WriteUInt16(0);
            else
                deviceTables.WriteOffset16(writer, _yPlacementDevice, posTableBaseOffset);
        }

        if ((valueFormat & (ushort)GposValueFormat.XAdvanceDevice) != 0)
        {
            if (_xAdvanceDevice is null)
                writer.WriteUInt16(0);
            else
                deviceTables.WriteOffset16(writer, _xAdvanceDevice, posTableBaseOffset);
        }

        if ((valueFormat & (ushort)GposValueFormat.YAdvanceDevice) != 0)
        {
            if (_yAdvanceDevice is null)
                writer.WriteUInt16(0);
            else
                deviceTables.WriteOffset16(writer, _yAdvanceDevice, posTableBaseOffset);
        }
    }
}
