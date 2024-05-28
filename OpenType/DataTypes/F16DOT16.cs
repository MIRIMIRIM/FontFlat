using CommunityToolkit.Diagnostics;

namespace FontFlat.OpenType.DataTypes;

public readonly struct F16DOT16
{
    private readonly uint _value;
    private const int highBits = 16;
    private const int lowBits = 4;
    private const int lowShift = highBits - lowBits;

    public readonly ushort High;
    public readonly ushort Low;

    public F16DOT16(uint value)
    {
        _value = value;
        High = (ushort)(value >> highBits);
        Low = (ushort)((value & 0b_1111_0000) >> lowShift);
    }
    public F16DOT16(ushort high, ushort low)
    {
        if (low > 9) { ThrowHelper.ThrowArgumentOutOfRangeException("major version must between 0 and 9"); }
        High = high;
        Low = low;
        _value = ((uint)high << highBits) | ((uint)low << lowShift);
    }

    public override readonly string ToString() => $"{High}.{Low}";
    public override readonly bool Equals(object? obj) => obj switch
    {
        F16DOT16 dot => dot._value == _value,
        uint u32 => u32.Equals(_value),
        int i32 => i32.Equals(_value),
        _ => false,
    };
    public static bool operator ==(F16DOT16 left, F16DOT16 right) => left.Equals(right);
    public static bool operator !=(F16DOT16 left, F16DOT16 right) => !(left == right);
    public static bool operator ==(F16DOT16 left, uint right) => left.Equals(right);
    public static bool operator !=(F16DOT16 left, uint right) => !(left == right);
    public override readonly int GetHashCode() => HashCode.Combine(_value);
}
