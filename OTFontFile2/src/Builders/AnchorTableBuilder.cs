namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS Anchor tables (AnchorFormat 1/2/3).
/// </summary>
public sealed class AnchorTableBuilder
{
    private ushort _format = 1;

    private short _x;
    private short _y;

    private ushort _anchorPoint;

    private DeviceTableBuilder? _xDevice;
    private DeviceTableBuilder? _yDevice;

    public ushort AnchorFormat => _format;

    public short XCoordinate
    {
        get => _x;
        set => _x = value;
    }

    public short YCoordinate
    {
        get => _y;
        set => _y = value;
    }

    public ushort AnchorPoint
    {
        get => _anchorPoint;
        set
        {
            _anchorPoint = value;
            _format = 2;
            _xDevice = null;
            _yDevice = null;
        }
    }

    public DeviceTableBuilder? XDevice
    {
        get => _xDevice;
        set
        {
            _xDevice = value;
            _format = 3;
            _anchorPoint = 0;
        }
    }

    public DeviceTableBuilder? YDevice
    {
        get => _yDevice;
        set
        {
            _yDevice = value;
            _format = 3;
            _anchorPoint = 0;
        }
    }

    public AnchorTableBuilder()
    {
    }

    public AnchorTableBuilder(short x, short y)
        => SetFormat1(x, y);

    public static AnchorTableBuilder Format1(short x, short y) => new(x, y);

    public static AnchorTableBuilder Format2(short x, short y, ushort anchorPoint)
    {
        var b = new AnchorTableBuilder(x, y);
        b.SetFormat2(anchorPoint);
        return b;
    }

    public static AnchorTableBuilder Format3(short x, short y, DeviceTableBuilder? xDevice, DeviceTableBuilder? yDevice)
    {
        var b = new AnchorTableBuilder(x, y);
        b.SetFormat3(xDevice, yDevice);
        return b;
    }

    public void SetFormat1(short x, short y)
    {
        _format = 1;
        _x = x;
        _y = y;
        _anchorPoint = 0;
        _xDevice = null;
        _yDevice = null;
    }

    public void SetFormat2(ushort anchorPoint)
    {
        _format = 2;
        _anchorPoint = anchorPoint;
        _xDevice = null;
        _yDevice = null;
    }

    public void SetFormat3(DeviceTableBuilder? xDevice, DeviceTableBuilder? yDevice)
    {
        _format = 3;
        _anchorPoint = 0;
        _xDevice = xDevice;
        _yDevice = yDevice;
    }

    internal void WriteTo(OTFontFile2.OffsetWriter writer, int anchorStartOffset, DeviceTablePool devices)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        if (devices is null) throw new ArgumentNullException(nameof(devices));
        if (anchorStartOffset < 0) throw new ArgumentOutOfRangeException(nameof(anchorStartOffset));

        ushort format = _format;
        if (format is not (1 or 2 or 3))
            throw new InvalidOperationException("Invalid AnchorFormat.");

        writer.WriteUInt16(format);
        writer.WriteInt16(_x);
        writer.WriteInt16(_y);

        if (format == 2)
        {
            writer.WriteUInt16(_anchorPoint);
            return;
        }

        if (format == 3)
        {
            if (_xDevice is null) writer.WriteUInt16(0);
            else devices.WriteOffset16(writer, _xDevice, baseOffset: anchorStartOffset);

            if (_yDevice is null) writer.WriteUInt16(0);
            else devices.WriteOffset16(writer, _yDevice, baseOffset: anchorStartOffset);
        }
    }
}

