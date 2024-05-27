using CommunityToolkit.Diagnostics;

namespace FontFlat.OpenType.DataTypes;

public readonly struct F16DOT16
{
    private readonly uint _value;
    private const int majorBits = 16;
    private const int minorBits = 4;
    private const int minorShift = majorBits - minorBits;

    public readonly ushort MajorVersion;
    public readonly ushort MinorVersion;

    public F16DOT16(uint version)
    {
        _value = version;
        MajorVersion = (ushort)(version >> majorBits);
        MinorVersion = (ushort)((version & 0b_1111_0000) >> minorShift);
    }
    public F16DOT16(ushort major, ushort minor)
    {
        if (minor > 9) { ThrowHelper.ThrowArgumentOutOfRangeException("major version must between 0 and 9"); }
        MajorVersion = major;
        MinorVersion = minor;
        _value = ((uint)major << majorBits) | ((uint)minor << minorShift);
    }

    public override readonly string ToString() => $"{MajorVersion}.{MinorVersion}";
    public override readonly bool Equals(object? obj) => obj switch
    {
        F16DOT16 dot => dot._value == _value,
        uint u32 => u32.Equals(_value),
        int i32 => i32.Equals(_value),
        _ => false,
    };
    public static bool operator ==(F16DOT16 left, F16DOT16 right) => left.Equals(right);
    public static bool operator !=(F16DOT16 left, F16DOT16 right) => !(left == right);
    public override readonly int GetHashCode() => HashCode.Combine(_value);
}
