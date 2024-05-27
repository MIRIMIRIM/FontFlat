using CommunityToolkit.Diagnostics;

namespace FontFlat.OpenType.DataTypes;

public struct Version16Dot16
{
    private readonly uint _value;
    private const int majorBits = 16;
    private const int minorBits = 4;
    private const int minorShift = majorBits - minorBits;

    public ushort MajorVersion;
    public ushort MinorVersion;

    public Version16Dot16(uint version)
    {
        _value = version;
        MajorVersion = (ushort)(version >> majorBits);
        MinorVersion = (ushort)((version & 0b_1111_0000) >> minorShift);
    }
    public Version16Dot16(ushort major, ushort minor)
    {
        if (minor > 9) { ThrowHelper.ThrowArgumentOutOfRangeException("major version must between 0 and 9"); }
        MajorVersion = major;
        MinorVersion = minor;
        _value = ((uint)major << majorBits) | ((uint)minor << minorShift);
    }

    public override readonly string ToString() => $"{MajorVersion}.{MinorVersion}";
    public override readonly bool Equals(object? obj) => obj switch
    {
        Version16Dot16 dot => dot._value == _value,
        uint u32 => u32.Equals(_value),
        int i32 => i32.Equals(_value),
        _ => false,
    };
    public static bool operator ==(Version16Dot16 left, Version16Dot16 right) => left.Equals(right);
    public static bool operator !=(Version16Dot16 left, Version16Dot16 right) => !(left == right);
    public override readonly int GetHashCode() => HashCode.Combine(_value);
}
