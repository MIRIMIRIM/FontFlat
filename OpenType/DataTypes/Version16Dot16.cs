using CommunityToolkit.Diagnostics;

namespace FontFlat.OpenType.DataTypes;

public struct Version16Dot16
{
    private uint _value;
    private const int majorBits = 16;
    private const int minorBits = 4;
    private const int minorShift = majorBits - minorBits;

    public ushort MajorVersion;
    public ushort MinorVersion;

    public Version16Dot16(uint version) => _value = version;
    public Version16Dot16(ushort major, ushort minor)
    {
        if (minor > 9) { ThrowHelper.ThrowArgumentOutOfRangeException("major version must between 0 and 9"); }
        MajorVersion = major;
        MinorVersion = minor;
        _value = ((uint)major << majorBits) | ((uint)minor << minorShift);
    }
}
